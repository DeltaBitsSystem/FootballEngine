namespace FootballEngine.Core.Match

/// F4 — a pass is only a real option if it beats the fastest interceptor: the
/// carrier rejects any delivery an opponent can reach before the receiver,
/// plus a configurable risk margin. Pure and deterministic so the decision
/// layer can rely on the verdict, reusing InterceptTable's flight math.
///
/// No function here takes a MatchContext: the only ctx-derived input was the
/// offside line, and it is constant for a whole pass scan, so the decision
/// layer precomputes it once and passes it in — per-candidate recomputation
/// would multiply a linear pass by the team size.
module PassIntercept =

    /// Candidate passes are ground passes, so they are raced with the same rolling-ball model as the team table.
    let passParams
        (intercept: InterceptConfig)
        (movement: MovementConfig)
        (ball: BallConfig)
        : InterceptTable.InterceptParams =
        { Ball = ball
          PlayerSpeedPerSecond = movement.SprintSpeed
          PlayerAcceleration = InterceptTable.bodyAcceleration movement
          ControlRadius = intercept.ControlRadius
          MaxSteps = intercept.MaxSteps
          RollTableResolution = intercept.RollTableResolution
          RollTableMaxSpeed = intercept.RollTableMaxSpeed }

    /// The binding constraint: steps for the fastest non-GK opponent to reach
    /// the ball. The GK is excluded — a keeper never leaves his line for a
    /// pass interception.
    let interceptorStep (p: InterceptTable.InterceptParams) (world: World) (side: ClubSide) (ball: Spatial) : int =
        let frame = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
        let mutable best = p.MaxSteps
        let mutable found = false

        for slot in MatchWorld.activeSlots frame do
            if slot <> 0 then
                let s =
                    InterceptTable.stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                if s < best then
                    best <- s

                found <- true

        if found then best else p.MaxSteps

    /// Steps for the receiver to get the delivery under control — the number
    /// the fastest interceptor must beat for the pass to be worth playing.
    let passArrivalStep
        (p: InterceptTable.InterceptParams)
        (world: World)
        (side: ClubSide)
        (receiverSlot: int)
        (ball: Spatial)
        : int =
        let frame = (MatchWorld.team world side).Frame
        InterceptTable.stepsToReach p ball (MatchWorld.posX frame receiverSlot) (MatchWorld.posY frame receiverSlot)

    /// The risk premium on a pass: a delivery beyond the offside line is
    /// dangerous on its own, and a diagonal one is the case the judge can get
    /// wrong — so the risk rises exactly at that intersection, nowhere else.
    /// `offsideLine` is precomputed by the caller because it is constant for
    /// the whole pass scan.
    let riskFor
        (cfg: PassInterceptConfig)
        (world: World)
        (side: ClubSide)
        (receiverSlot: int)
        (ball: Spatial)
        (offsideLine: float<meter>)
        : int =
        let frame = (MatchWorld.team world side).Frame
        let rx = MatchWorld.posX frame receiverSlot
        let dir = MatchMath.goalDirection side

        let offside =
            if dir > 0.0 then
                float rx > float offsideLine
            else
                float rx < float offsideLine

        let diagonal = abs (float ball.Vy) > abs (float ball.Vx) * 0.5
        cfg.BaseRisk + (if offside && diagonal then cfg.OffsideCrossRisk else 0)

    /// Steps by which the receiver wins the race, `interceptorStep + risk - passArrivalStep`. The margin is what turns a
    /// coin-flip pass into a safe one. `offsideLine` is precomputed by the
    /// caller (constant within the pass scan).
    let margin
        (cfg: PassInterceptConfig)
        (p: InterceptTable.InterceptParams)
        (world: World)
        (side: ClubSide)
        (receiverSlot: int)
        (ball: Spatial)
        (offsideLine: float<meter>)
        : int =
        // One roll raced by receiver and interceptors alike; the same verdict as interceptorStep/passArrivalStep.
        let rolled = InterceptTable.RolledBalls.Rent(p, ball)
        let opponents = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
        let struct (bodyX, bodyY) = rolled.Bodies opponents.Occupancy.Length
        let mutable count = 0

        for slot in MatchWorld.activeSlots opponents do
            if slot <> 0 then
                bodyX[count] <- float (MatchWorld.posX opponents slot)
                bodyY[count] <- float (MatchWorld.posY opponents slot)
                count <- count + 1

        let interceptor = rolled.FirstArrival(bodyX, bodyY, count, p.MaxSteps)

        let own = (MatchWorld.team world side).Frame
        let arrival = rolled.StepsFor(MatchWorld.posX own receiverSlot, MatchWorld.posY own receiverSlot)
        InterceptTable.RolledBalls.Return rolled
        interceptor + riskFor cfg world side receiverSlot ball offsideLine - arrival

    /// The receiver wins the race: a positive margin.
    let feasible cfg p world side receiverSlot ball offsideLine = margin cfg p world side receiverSlot ball offsideLine > 0
