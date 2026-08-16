namespace FootballEngine.Core.Match

/// F4 — a pass is only a real option if it beats the fastest interceptor: the
/// carrier rejects any delivery an opponent can reach before the receiver,
/// plus a configurable risk margin. Pure and deterministic so the decision
/// layer can rely on the verdict, reusing InterceptTable's flight math.
module PassIntercept =

    /// Flight params for an airborne pass: the ball is in the air, so the only
    /// decay is air drag per tick (dt = 1/40) — no ground friction to model.
    let passParams
        (intercept: InterceptConfig)
        (movement: MovementConfig)
        (ball: BallConfig)
        : InterceptTable.InterceptParams =
        let dt = 1.0 / 40.0

        { BallDecayPerTick = float ball.AirDrag ** dt
          PlayerSpeedPerSecond = movement.SprintSpeed
          ControlRadius = intercept.ControlRadius
          MaxSteps = intercept.MaxSteps }

    /// The binding constraint: steps for the fastest non-GK opponent to reach
    /// the ball. The GK is excluded — a keeper never leaves his line for a
    /// pass interception.
    let interceptorStep (p: InterceptTable.InterceptParams) (world: World) (side: ClubSide) (ball: Spatial) : int =
        let frame = (MatchWorld.team world (MatchWorld.oppOf side)).Frame

        let steps =
            MatchWorld.activeSlots frame
            |> Array.filter (fun slot -> slot <> 0)
            |> Array.map (fun slot ->
                InterceptTable.stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot))

        if steps.Length = 0 then p.MaxSteps else Array.min steps

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
    let riskFor
        (cfg: PassInterceptConfig)
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (receiverSlot: int)
        (ball: Spatial)
        : int =
        let frame = (MatchWorld.team world side).Frame
        let rx = MatchWorld.posX frame receiverSlot
        let line = MatchWorld.offsideLine ctx world side
        let dir = MatchMath.goalDirection side

        let offside =
            if dir > 0.0 then
                float rx > float line
            else
                float rx < float line

        let diagonal = abs (float ball.Vy) > abs (float ball.Vx) * 0.5
        cfg.BaseRisk + (if offside && diagonal then cfg.OffsideCrossRisk else 0)

    /// Feasibility = the receiver wins the race with margin:
    /// `interceptorStep + risk > passArrivalStep`. The margin is what turns a
    /// coin-flip pass into a safe one.
    let feasible
        (cfg: PassInterceptConfig)
        (ctx: MatchContext)
        (p: InterceptTable.InterceptParams)
        (world: World)
        (side: ClubSide)
        (receiverSlot: int)
        (ball: Spatial)
        : bool =
        interceptorStep p world side ball + riskFor cfg ctx world side receiverSlot ball > passArrivalStep
            p
            world
            side
            receiverSlot
            ball
