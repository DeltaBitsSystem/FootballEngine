namespace FootballEngine.Core.Match

/// RoboCup InterceptTable, adapted. Who reaches the ball first decides
/// whether the side attacks or defends, so every player is reduced to one
/// number: how many ticks until they can control the ball (stepped rolling-ball
/// model sharing BallAero's drag), plus the team aggregates (self/teammate/
/// opponent/ball step) HELIOS uses for pressing, situation, passing and
/// positioning. Pure and deterministic.
module InterceptTable =

    [<Struct>]
    type InterceptParams =
        { Ball: BallConfig
          PlayerSpeedPerSecond: float<meterPerSecond>
          /// The body's acceleration limit: a race from a standstill is not run at top speed from the first step.
          PlayerAcceleration: float<meterPerSecondSquared>
          ControlRadius: float<meter> // kickable area
          MaxSteps: int } // horizon cap: beyond this many ticks the ball is treated as unreachable

    let private dt = 1.0<second> / 40.0

    /// The same limit ContactDynamics applies when it integrates a player toward his target velocity.
    let bodyAcceleration (movement: MovementConfig) =
        movement.Acceleration * max 0.1 (min 1.0 (float movement.Smoothing))

    /// Distance covered from a standstill after the given time, accelerating to top speed and then holding it.
    let reachableDistance (p: InterceptParams) (seconds: float) =
        let top = float p.PlayerSpeedPerSecond
        let acceleration = float p.PlayerAcceleration

        // Validation keeps real configs positive; a hand-built zero means the instant-top-speed idealisation.
        if acceleration <= 0.0 then
            top * seconds
        else
            let rampTime = top / acceleration

            if seconds <= rampTime then
                0.5 * acceleration * seconds * seconds
            else
                top * seconds - top * rampTime * 0.5

    let parameters (intercept: InterceptConfig) (movement: MovementConfig) (ball: BallConfig) : InterceptParams =
        // This closed-form race estimate is conservative; authoritative motion remains BallDynamics.
        { Ball = ball
          PlayerSpeedPerSecond = movement.SprintSpeed
          PlayerAcceleration = bodyAcceleration movement
          ControlRadius = intercept.ControlRadius
          MaxSteps = intercept.MaxSteps }

    /// Ground speed after one prediction step: turf resistance plus air drag, as BallDynamics rolls the ball.
    let private nextGroundSpeed (p: InterceptParams) speed =
        let loss =
            (p.Ball.RollingResistance * float p.Ball.Gravity + BallAero.dragDeceleration p.Ball speed) * float dt

        max 0.0 (speed - loss)

    /// Predicted rolling-ball state after n ticks; the per-step loop is bounded by MaxSteps.
    let ballInertiaPoint (p: InterceptParams) (ball: Spatial) (n: int) : Spatial =
        let mutable x, y = float ball.X, float ball.Y
        let mutable vx, vy = float ball.Vx, float ball.Vy

        // Decelerate, then move: the same order BallDynamics uses when it rolls the ball.
        for _ in 1..n do
            let speed = sqrt (vx * vx + vy * vy)

            if speed > 1e-9 then
                let scale = nextGroundSpeed p speed / speed
                vx <- vx * scale
                vy <- vy * scale

            x <- x + vx * float dt
            y <- y + vy * float dt

        { X = x * 1.0<meter>
          Y = y * 1.0<meter>
          Z = 0.0<meter>
          Vx = vx * 1.0<meterPerSecond>
          Vy = vy * 1.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

    /// Smallest n in [0..MaxSteps] such that the player, running straight at
    /// the ball at PlayerSpeed, is within ControlRadius of the ball's inertia
    /// point at tick n. Equivalently: dist(player, ballAt(n)) - ControlRadius
    /// <= speed·dt·n. The reachable predicate is monotonic in n, so scanning
    /// upward and taking the first hit is exact; unreachable balls return
    /// MaxSteps, keeping the result deterministic.
    let stepsToReach (p: InterceptParams) (ball: Spatial) (px: float<meter>) (py: float<meter>) : int =
        let control = float p.ControlRadius
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
            let reach = control + reachableDistance p (float n * stepSeconds)

            if dx * dx + dy * dy <= reach * reach then
                reached <- true
            else
                let speed = sqrt (velocityX * velocityX + velocityY * velocityY)

                if speed > 1e-9 then
                    let scale = nextGroundSpeed p speed / speed
                    velocityX <- velocityX * scale
                    velocityY <- velocityY * scale

                ballX <- ballX + velocityX * stepSeconds
                ballY <- ballY + velocityY * stepSeconds

                n <- n + 1

        if reached then n else p.MaxSteps

    /// Per-team aggregate: the minimum steps define who is first to the ball,
    /// which is what the pressing and situation logic compares. SelfStep and
    /// TeammateStep are the min over the side's active outfielders (GK
    /// excluded — a keeper's reach is not comparable to an outfielder's),
    /// OpponentStep the min over the opponent's, and BallStep = min(self,
    /// teammate, opponent). SlotSteps holds every active slot's own
    /// stepsToReach, the keeper's included so he can be sent to a loose ball in
    /// his area; sidelined or inactive slots get MaxSteps.
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
                let s = stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)
                steps.[slot] <- s

                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper then
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
                let step = stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)
                steps.[slot] <- step

                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper then
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
                let step = stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)
                steps.[slot] <- step

                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper then
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
