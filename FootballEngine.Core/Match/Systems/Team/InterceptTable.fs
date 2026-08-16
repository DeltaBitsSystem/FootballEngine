namespace FootballEngine.Core.Match

/// RoboCup InterceptTable, adapted. Closed-form geometric-series ball model:
/// how many ticks until each player can control the ball, plus the team
/// aggregates (self/teammate/opponent/ball step) HELIOS uses for pressing,
/// situation, passing and positioning. Pure and deterministic.
module InterceptTable =

    type InterceptParams =
        { BallDecayPerTick: float // effective ball speed decay per tick (air drag * ground friction per dt)
          PlayerSpeedPerSecond: float<meterPerSecond>
          ControlRadius: float<meter> // kickable area
          MaxSteps: int } // horizon (50)

    let private dt = 1.0<second> / 40.0

    /// Closed-form ball position after n ticks: p(n) = p + v·dt·(1-d^n)/(1-d)
    /// (v is m/s; dt is the sim step in seconds). Returns a Spatial with the new
    /// X/Y and Vx/Vy decayed by d^n.
    let ballInertiaPoint (p: InterceptParams) (ball: Spatial) (n: int) : Spatial =
        let d = p.BallDecayPerTick

        let geom =
            if abs (d - 1.0) < 1e-12 then
                float n
            else
                (1.0 - pown d n) / (1.0 - d)

        { X = ball.X + ball.Vx * dt * geom
          Y = ball.Y + ball.Vy * dt * geom
          Z = 0.0<meter>
          Vx = ball.Vx * pown d n
          Vy = ball.Vy * pown d n
          Vz = 0.0<meterPerSecond> }

    /// Smallest n in [0..MaxSteps] such that the player, running straight at
    /// the ball at PlayerSpeed, is within ControlRadius of the ball's inertia
    /// point at tick n. Equivalently: dist(player, ballAt(n)) - ControlRadius
    /// <= speed·dt·n. Returns MaxSteps when unreachable. Monotonic and
    /// deterministic (iterate n upward, first hit wins).
    let stepsToReach (p: InterceptParams) (ball: Spatial) (px: float<meter>) (py: float<meter>) : int =
        let control = float p.ControlRadius
        let stepPerTick = float p.PlayerSpeedPerSecond * float dt
        let mutable n = 0
        let mutable reached = false

        while not reached && n <= p.MaxSteps do
            let bp = ballInertiaPoint p ball n

            if MatchMath.dist px py bp.X bp.Y - control <= stepPerTick * float n then
                reached <- true
            else
                n <- n + 1

        if reached then n else p.MaxSteps

    /// Per-team aggregate: min steps over the side's active outfielders
    /// (excludes GK slot 0 and sidelined), min over the opponent's active
    /// outfielders, and BallStep = min(self, teammate, opponent). SlotSteps is
    /// filled for every slot: active non-GK get their stepsToReach, everything
    /// else (GK, sidelined, inactive) gets MaxSteps.
    let compute (p: InterceptParams) (world: World) (side: ClubSide) : InterceptState =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps

        let sideSteps (side: ClubSide) : int[] * (int * int)[] =
            let frame = (MatchWorld.team world side).Frame
            let steps = Array.create frame.SlotCount maxSteps

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    steps.[slot] <- stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

            let activeOutfield =
                MatchWorld.activeSlots frame
                |> Array.filter (fun slot -> slot <> 0)
                |> Array.map (fun slot -> slot, steps.[slot])

            steps, activeOutfield

        let ourSteps, our = sideSteps side
        let _, opp = sideSteps (MatchWorld.oppOf side)

        let selfStep =
            if our.Length = 0 then
                maxSteps
            else
                our |> Array.map snd |> Array.min

        let bestSlot = if our.Length = 0 then -1 else our |> Array.minBy snd |> fst

        let teammateStep =
            if our.Length <= 1 then
                maxSteps
            else
                our
                |> Array.filter (fun (slot, _) -> slot <> bestSlot)
                |> Array.map snd
                |> Array.min

        let opponentStep =
            if opp.Length = 0 then
                maxSteps
            else
                opp |> Array.map snd |> Array.min

        let ballStep = min selfStep (min teammateStep opponentStep)

        { ComputedAt = world.State.Tick
          SelfStep = selfStep
          TeammateStep = teammateStep
          OpponentStep = opponentStep
          BallStep = ballStep
          SlotSteps = ourSteps }
