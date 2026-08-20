namespace FootballEngine.Core.Match

/// Cross execution: picks the best attacker in the box and aims the delivery
/// at him. A cross is a pass in disguise — resolution reuses the ordinary
/// BallContact path so one receiving physics serves every aerial ball. The
/// CrossAttempt event is emitted here because this is the moment the action
/// starts.
module Cross =

    let launch (ctx: SystemContext) (rng: Rng) (world: World) (buffer: EventBuffer) (side: ClubSide) (slot: int) : Rng =
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let pid = p.Id
        let frame = (MatchWorld.team world side).Frame

        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let dir = MatchMath.goalDirection side

        let goalX = MatchMath.goalX side
        let targetX = goalX - 6.0<meter> * dir

        let mutable bestSlot = -1
        let mutable bestScore = 0.0

        for s2 in MatchWorld.activeSlots frame do
            if s2 <> slot && s2 <> 0 then
                let rx = MatchWorld.posX frame s2
                let ry = MatchWorld.posY frame s2
                let dToSpot = MatchMath.dist targetX 0.0<meter> rx ry
                let dToGoal = MatchMath.distanceToGoal side rx ry

                if dToSpot < float cfg.Actions.CrossTargetRadius + 5.0 && dToGoal < 22.0 then
                    let score = (22.0 - dToGoal) * 0.5 - dToSpot * 0.2
                    let attrs = float (MatchWorld.profileOf ctx.Ctx side s2).Finishing / 50.0

                    if score + attrs > bestScore then
                        bestScore <- score + attrs
                        bestSlot <- s2

        buffer.Events.Add
            { Tick = tick
              Data = EventData.CrossAttempt(pid, x, y) }

        if bestSlot >= 0 then
            let d =
                MatchMath.dist x y (MatchWorld.posX frame bestSlot) (MatchWorld.posY frame bestSlot)

            let quality =
                MatchMath.clamp01 (
                    cfg.Actions.PassBaseQuality + (float p.Crossing - 60.0) * 0.008
                    - d * cfg.Actions.PassDistancePenaltyPerMeter
                    + 0.15
                )

            let speed = 20.0

            MatchWorld.updateBall world (fun b ->
                { b with
                    Control = BallControl.Airborne
                    Trajectory =
                        Some(
                            TrajectoryIntent.Aimed(
                                pid,
                                (MatchWorld.lineup ctx.Ctx side).Profiles.[bestSlot].Id,
                                quality
                            )
                        )
                    LastTouch = Some pid
                    ArrivalGate = Some(tick + int (d / speed * 40.0) * 1<subtick> + 10<subtick>)
                    StationarySince = None })

            rng
        else
            rng
