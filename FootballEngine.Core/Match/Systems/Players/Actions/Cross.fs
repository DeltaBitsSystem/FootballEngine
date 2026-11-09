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

        if bestSlot >= 0 then
            let d =
                MatchMath.dist x y (MatchWorld.posX frame bestSlot) (MatchWorld.posY frame bestSlot)

            let quality =
                MatchMath.clamp01 (
                    cfg.Actions.PassBaseQuality + (float p.Crossing - 60.0) * 0.008
                    - d * cfg.Actions.PassDistancePenaltyPerMeter
                    + 0.15
                )

            let rx = MatchWorld.posX frame bestSlot
            let ry = MatchWorld.posY frame bestSlot
            // Longer deliveries are hit harder so they still arrive before the defence can close.
            let speed = MatchMath.clamp 16.0 27.0 (12.0 + 0.35 * d)

            let angularVelocity =
                { Top = -0.4<radianPerSecond>
                  Side = float (ry - y) * 0.02<radianPerSecond>
                  Roll = 0.0<radianPerSecond> }

            let ballPosition = world.State.Ball.Position

            let launch =
                BallFlight.solve
                    cfg.Ball
                    (float (SimulationClock.dt ctx.Clock))
                    { X = ballPosition.X; Y = ballPosition.Y; Z = ballPosition.Z }
                    { X = rx; Y = ry; Z = cfg.Actions.CrossArrivalHeight }
                    (speed * 1.0<meterPerSecond>)
                    angularVelocity

            let linearVelocity = launch.Velocity

            let target: Vec2 option = Some { X = rx; Y = ry }

            let impulse =
                BallPhysics.createImpulse
                    pid
                    { X = world.State.Ball.Position.X
                      Y = world.State.Ball.Position.Y
                      Z = world.State.Ball.Position.Z }
                    linearVelocity
                    angularVelocity
                    target
                    StrikeKind.Cross
                    (SimTick.ofSubTicks tick)
                    ExecutionContext.OpenPlay

            let arrival =
                tick
                + int (round (launch.Seconds * float ctx.Clock.SubTicksPerSecond)) * 1<subtick>
                + 10<subtick>

            BallFootContact.request
                ctx
                world
                buffer
                impulse
                (TrajectoryIntent.Aimed(pid, (MatchWorld.lineup ctx.Ctx side).Profiles.[bestSlot].Id, quality))
                (Some arrival)

            rng
        else
            let attempt =
                { Tick = tick
                  Data = EventData.CrossAttempt(pid, x, y) }

            buffer.Events.Add attempt

            match world.Observation.RejectAction(tick, pid, NoCrossTarget) with
            | ValueSome id -> world.Observation.Associate(attempt, ResolutionCause id)
            | ValueNone -> ()

            rng
