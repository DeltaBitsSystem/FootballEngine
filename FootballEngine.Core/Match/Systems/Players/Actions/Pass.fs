namespace FootballEngine.Core.Match

/// Pass execution: aim the ball at a teammate, offside check at launch,
/// quality from attributes + distance + jitter. Resolution happens in
/// BallContact (which emits the Pass event).
module Pass =

    let private flagOffside
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (tick: int<subtick>)
        : unit =
        let team = MatchWorld.team world side

        MatchWorld.updateBall world (fun b ->
            { b with
                Control = BallControl.Contesting side
                Trajectory = None
                ArrivalGate = None })

        buffer.Events.Add
            { Tick = tick
              Data = EventData.SetPiece(SetPieceKind.FreeKick, MatchWorld.oppOf side) }

        team.Stats.Offsides <- team.Stats.Offsides + 1

    /// Launch a pass to a teammate slot. `qualityBonus` lets the GK system
    /// cheapen short distribution; `maxDist` guards sanity.
    let launch
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (slot: int)
        (targetSlot: int)
        (qualityBonus: float)
        : Rng =
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let frame = (MatchWorld.team world side).Frame
        let pid = p.Id

        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let rx = MatchWorld.posX frame targetSlot
        let ry = MatchWorld.posY frame targetSlot

        let d = MatchMath.dist x y rx ry

        // R5 — a cognitively tired receiver is harder to feed.
        let receiverCognition = float frame.Cognitive.[targetSlot]
        let cognitiveQuality = 0.6 + 0.4 * receiverCognition

        if d > 65.0 then
            rng
        else
            let line = MatchWorld.offsideLine ctx.Ctx world side
            let dir = MatchMath.goalDirection side
            let forward = (float rx - float x) * dir > 0.0

            let beyond =
                if dir > 0.0 then
                    float rx > float line
                else
                    float rx < float line

            if forward && beyond then
                let flag, rng' = Rng.bernoulli 0.55 rng

                if flag then
                    flagOffside ctx world buffer side tick
                    rng'
                else
                    let jitter, rng'' = Rng.uniform (-1.0) 1.0 rng'

                    let quality =
                        MatchMath.clamp01 (
                            cfg.Actions.PassBaseQuality * cognitiveQuality
                            + (float p.Passing - 60.0) * cfg.Actions.PassTechnicalWeight
                            + (float p.Vision - 60.0) * cfg.Actions.PassVisionWeight
                            - d * cfg.Actions.PassDistancePenaltyPerMeter
                            + jitter * cfg.Actions.PassJitterBase
                            + qualityBonus
                        )

                    let speed = 13.0 + d * 0.12

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Control = BallControl.Airborne
                            Trajectory =
                                Some(
                                    TrajectoryIntent.Aimed(
                                        pid,
                                        (MatchWorld.lineup ctx.Ctx side).Profiles.[targetSlot].Id,
                                        quality
                                    )
                                )
                            LastTouch = Some pid
                            ArrivalGate = Some(tick + int (d / speed * 40.0) * 1<subtick> + 6<subtick>)
                            StationarySince = None })

                    rng''
            else
                let jitter, rng' = Rng.uniform (-1.0) 1.0 rng

                let quality =
                    MatchMath.clamp01 (
                        cfg.Actions.PassBaseQuality * cognitiveQuality
                        + (float p.Passing - 60.0) * cfg.Actions.PassTechnicalWeight
                        + (float p.Vision - 60.0) * cfg.Actions.PassVisionWeight
                        - d * cfg.Actions.PassDistancePenaltyPerMeter
                        + jitter * cfg.Actions.PassJitterBase
                        + qualityBonus
                    )

                let speed = 13.0 + d * 0.12

                MatchWorld.updateBall world (fun b ->
                    { b with
                        Control = BallControl.Airborne
                        Trajectory =
                            Some(
                                TrajectoryIntent.Aimed(
                                    pid,
                                    (MatchWorld.lineup ctx.Ctx side).Profiles.[targetSlot].Id,
                                    quality
                                )
                            )
                        LastTouch = Some pid
                        ArrivalGate = Some(tick + int (d / speed * 40.0) * 1<subtick> + 6<subtick>)
                        StationarySince = None })

                rng'
