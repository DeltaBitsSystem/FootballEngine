namespace FootballEngine.Core.Match

/// F4 — pass feasibility with the RoboCup interceptor model. The carrier
/// rejects any pass the fastest opponent can reach before the receiver, plus a
/// configurable risk margin. Pure, deterministic, reuses InterceptTable.
module PassIntercept =

    /// Airborne pass params: only air drag per tick (dt = 1/40).
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

    /// Steps for the fastest non-GK opponent to intercept the launched ball.
    let interceptorStep (p: InterceptTable.InterceptParams) (world: World) (side: ClubSide) (ball: Spatial) : int =
        let frame = (MatchWorld.team world (MatchWorld.oppOf side)).Frame

        let steps =
            MatchWorld.activeSlots frame
            |> Array.filter (fun slot -> slot <> 0)
            |> Array.map (fun slot ->
                InterceptTable.stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot))

        if steps.Length = 0 then p.MaxSteps else Array.min steps

    /// Steps for the receiver slot to control the launched ball.
    let passArrivalStep
        (p: InterceptTable.InterceptParams)
        (world: World)
        (side: ClubSide)
        (receiverSlot: int)
        (ball: Spatial)
        : int =
        let frame = (MatchWorld.team world side).Frame
        InterceptTable.stepsToReach p ball (MatchWorld.posX frame receiverSlot) (MatchWorld.posY frame receiverSlot)

    /// Risk for this pass to a receiver slot given the launched ball: base risk
    /// plus OffsideCrossRisk when the receiver is beyond the offside line and
    /// the pass arrives diagonally.
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

    /// True when feasible: interceptorStep + risk > passArrivalStep.
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
