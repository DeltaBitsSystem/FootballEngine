namespace FootballEngine.Core.Match

/// RoboCup InterceptTable, adapted. Who reaches the ball first decides
/// whether the side attacks or defends, so every player is reduced to one
/// number: how many ticks until they can control the ball (closed-form
/// geometric-series ball model), plus the team aggregates (self/teammate/
/// opponent/ball step) HELIOS uses for pressing, situation, passing and
/// positioning. Pure and deterministic.
module InterceptTable =

    [<Struct>]
    type InterceptParams =
        { BallDecayPerTick: float // conservative decay estimate; contact integration is authoritative
          PlayerSpeedPerSecond: float<meterPerSecond>
          ControlRadius: float<meter> // kickable area
          MaxSteps: int } // horizon cap: beyond this many ticks the ball is treated as unreachable

    let private dt = 1.0<second> / 40.0

    let parameters (intercept: InterceptConfig) (movement: MovementConfig) (ball: BallConfig) : InterceptParams =
        // This closed-form race estimate is conservative; authoritative motion remains BallDynamics.
        { BallDecayPerTick =
            float ball.AirDrag ** float dt
            * exp (-ball.RollingResistance * float ball.Gravity * float dt)
          PlayerSpeedPerSecond = movement.SprintSpeed
          ControlRadius = intercept.ControlRadius
          MaxSteps = intercept.MaxSteps }

    /// Closed-form ball position after n ticks: p(n) = p + v·dt·(1-d^n)/(1-d)
    /// (v is m/s; dt is the sim step in seconds). Deriving the geometric
    /// series in closed form keeps the per-player cost O(1) instead of
    /// simulating n ticks for every slot. Returns a Spatial with the new X/Y
    /// and Vx/Vy decayed by d^n.
    let ballInertiaPoint (p: InterceptParams) (ball: Spatial) (n: int) : Spatial =
        let d = p.BallDecayPerTick
        let decayPower = pown d n

        let geom =
            if abs (d - 1.0) < 1e-12 then
                float n
            else
                (1.0 - decayPower) / (1.0 - d)

        { X = ball.X + ball.Vx * dt * geom
          Y = ball.Y + ball.Vy * dt * geom
          Z = 0.0<meter>
          Vx = ball.Vx * decayPower
          Vy = ball.Vy * decayPower
          Vz = 0.0<meterPerSecond> }

    /// Smallest n in [0..MaxSteps] such that the player, running straight at
    /// the ball at PlayerSpeed, is within ControlRadius of the ball's inertia
    /// point at tick n. Equivalently: dist(player, ballAt(n)) - ControlRadius
    /// <= speed·dt·n. The reachable predicate is monotonic in n, so scanning
    /// upward and taking the first hit is exact; unreachable balls return
    /// MaxSteps, keeping the result deterministic.
    let stepsToReach (p: InterceptParams) (ball: Spatial) (px: float<meter>) (py: float<meter>) : int =
        let control = float p.ControlRadius
        let stepPerTick = float p.PlayerSpeedPerSecond * float dt
        let stepSeconds = float dt
        let mutable ballX = float ball.X
        let mutable ballY = float ball.Y
        let mutable velocityX = float ball.Vx
        let mutable velocityY = float ball.Vy
        let playerX = float px
        let playerY = float py
        let mutable n = 0
        let mutable reached = false

        while not reached && n <= p.MaxSteps do
            let dx = playerX - ballX
            let dy = playerY - ballY
            let reach = control + stepPerTick * float n

            if dx * dx + dy * dy <= reach * reach then
                reached <- true
            else
                ballX <- ballX + velocityX * stepSeconds
                ballY <- ballY + velocityY * stepSeconds
                velocityX <- velocityX * p.BallDecayPerTick
                velocityY <- velocityY * p.BallDecayPerTick
                n <- n + 1

        if reached then n else p.MaxSteps

    /// Per-team aggregate: the minimum steps define who is first to the ball,
    /// which is what the pressing and situation logic compares. SelfStep and
    /// TeammateStep are the min over the side's active outfielders (GK
    /// excluded — a keeper's reach is not comparable to an outfielder's),
    /// OpponentStep the min over the opponent's, and BallStep = min(self,
    /// teammate, opponent). SlotSteps is filled for every slot: active
    /// non-GK get their stepsToReach, everything else (GK, sidelined,
    /// inactive) gets MaxSteps.
    let compute (p: InterceptParams) (world: World) (side: ClubSide) : InterceptState =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps

        let sideInfo (side: ClubSide) : int[] * int * int * int =
            // Single pass: fill SlotSteps and track the team aggregate minima
            // (self, best slot, next-best) without allocating filter/map arrays.
            let frame = (MatchWorld.team world side).Frame
            let steps = Array.create frame.SlotCount maxSteps
            let mutable bestSlot = -1
            let mutable bestStep = maxSteps
            let mutable secondStep = maxSteps
            let mutable found = false

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let s =
                        stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                    steps.[slot] <- s
                    found <- true

                    if s < bestStep then
                        secondStep <- bestStep
                        bestStep <- s
                        bestSlot <- slot
                    elif s < secondStep then
                        secondStep <- s

            steps, (if found then bestStep else maxSteps), bestSlot, (if found then secondStep else maxSteps)

        let ourSteps, selfStep, bestSlot, teammateStep = sideInfo side
        let _, opponentStep, _, _ = sideInfo (MatchWorld.oppOf side)

        let ballStep = min selfStep (min teammateStep opponentStep)

        { ComputedAt = world.State.Tick
          SelfStep = selfStep
          TeammateStep = teammateStep
          OpponentStep = opponentStep
          BallStep = ballStep
          SlotSteps = ourSteps }

    let computeBoth (p: InterceptParams) (world: World) : InterceptState * InterceptState =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps

        let sideInfo side =
            let frame = (MatchWorld.team world side).Frame
            let steps = Array.create frame.SlotCount maxSteps
            let mutable bestSlot = -1
            let mutable bestStep = maxSteps
            let mutable secondStep = maxSteps

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let step =
                        stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                    steps.[slot] <- step

                    if step < bestStep then
                        secondStep <- bestStep
                        bestStep <- step
                        bestSlot <- slot
                    elif step < secondStep then
                        secondStep <- step

            steps, bestStep, bestSlot, secondStep

        let homeSteps, homeBest, homeSlot, homeSecond = sideInfo ClubSide.Home
        let awaySteps, awayBest, awaySlot, awaySecond = sideInfo ClubSide.Away

        let state steps ownBest ownSlot ownSecond opponentBest =
            { ComputedAt = world.State.Tick
              SelfStep = ownBest
              TeammateStep = ownSecond
              OpponentStep = opponentBest
              BallStep = min ownBest (min ownSecond opponentBest)
              SlotSteps = steps }

        state homeSteps homeBest homeSlot homeSecond awayBest, state awaySteps awayBest awaySlot awaySecond homeBest

    let computeBothInto (p: InterceptParams) (world: World) : unit =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps

        let sideInfo side (steps: int[]) =
            let frame = (MatchWorld.team world side).Frame
            Array.fill steps 0 steps.Length maxSteps
            let mutable bestStep = maxSteps
            let mutable secondStep = maxSteps

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let step =
                        stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                    steps.[slot] <- step

                    if step < bestStep then
                        secondStep <- bestStep
                        bestStep <- step
                    elif step < secondStep then
                        secondStep <- step

            bestStep, secondStep

        let homeSteps = world.Home.Intercept.SlotSteps
        let awaySteps = world.Away.Intercept.SlotSteps
        let homeBest, homeSecond = sideInfo ClubSide.Home homeSteps
        let awayBest, awaySecond = sideInfo ClubSide.Away awaySteps

        world.Home.Intercept <-
            { ComputedAt = world.State.Tick
              SelfStep = homeBest
              TeammateStep = homeSecond
              OpponentStep = awayBest
              BallStep = min homeBest (min homeSecond awayBest)
              SlotSteps = homeSteps }

        world.Away.Intercept <-
            { ComputedAt = world.State.Tick
              SelfStep = awayBest
              TeammateStep = awaySecond
              OpponentStep = homeBest
              BallStep = min awayBest (min awaySecond homeBest)
              SlotSteps = awaySteps }
