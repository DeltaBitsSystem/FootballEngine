namespace FootballEngine.Core.Match

/// Shot execution: strike the ball and leave behind everything a goal may be
/// judged on — a VAR offside snapshot for close-range attempts (the ones that
/// get disputed) and a memory record so goal-less runs cool shooting (R6).
/// The Shot event is emitted by BallOutcome at resolution, one per attempt,
/// so the viewer sees the shot when it resolves.
module Shot =

    let launch (ctx: SystemContext) (rng: Rng) (world: World) (buffer: EventBuffer) (side: ClubSide) (slot: int) : Rng =
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let frame = (MatchWorld.team world side).Frame
        let pid = p.Id

        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot

        let d = MatchMath.distanceToGoal side x y
        let near = MatchWorld.opponentsNear ctx.Ctx world side x y 6.0
        let openness = 1.0 - 0.3 * MatchMath.clamp01 (float near / 4.0)
        let xg = MatchMath.xGFrom d (float p.Finishing) openness

        let radius = float cfg.Ball.Radius
        let goalLineX = float (MatchMath.goalX side)
        let goalDistanceX = abs (goalLineX - float x)
        let finishing = float p.Finishing / 100.0

        // The finisher picks the corner with more room beside the keeper; where he lands is aim plus execution
        // error, so on-target rate, woodwork and misses all come from the same strike rather than a dice roll.
        let keeperY =
            let opponent = MatchWorld.team world (MatchWorld.oppOf side)

            match opponent.GoalkeeperSlot with
            | Some gk when MatchWorld.isActive opponent.Frame gk -> float (MatchWorld.posY opponent.Frame gk)
            | _ -> 0.0

        let corner = if keeperY > 0.0 then -1.0 else 1.0
        let innerEdge = float Pitch.GoalHalfWidth - radius - float cfg.Actions.ShotPostMargin * (1.0 - finishing)
        let placement, rng1 = Rng.uniform 0.25 1.0 rng
        let height, rng2 = Rng.uniform 0.0 1.0 rng1
        // Underside heights that keep the whole ball inside the frame.
        let frameTop = float Pitch.GoalHeight - 2.0 * radius - 0.05
        let intendedY = corner * placement * innerEdge
        // Low finishes are the percentage choice, so intended height is skewed toward the ground.
        let intendedZ = height * height * frameTop

        let error =
            (cfg.Actions.ShotErrorBase + cfg.Actions.ShotErrorSkill * (1.0 - finishing)) * (2.0 - openness)

        let reach = sqrt (goalDistanceX * goalDistanceX + (intendedY - float y) ** 2.0)
        let lateralError, rng3 = Rng.normal 0.0 (error * reach) rng2
        let verticalError, rng4 = Rng.normal 0.0 (error * cfg.Actions.ShotVerticalErrorRatio * reach) rng3
        let aimY = intendedY + lateralError
        // A strike aimed below the ground is a skidding shot along the turf.
        let aimZ = max 0.0 (intendedZ + verticalError)

        // Tucking a finish inside the post is done with the side of the foot, so placement costs power: the keeper
        // gets more time on the shots aimed furthest from him.
        let power = float cfg.Actions.ShotSpeedBase + float cfg.Actions.ShotSpeedStrength * float p.Strength / 100.0
        let speed = min (float cfg.Ball.MaxShotSpeed) (power * (1.0 - cfg.Actions.ShotPlacementSpeedCost * placement))
        let angle = atan2 (aimY - float y) goalDistanceX

        // Placed finishes curl the ball back toward the aim point; the solver keeps the target, only the path bends.
        let curl, rng' = Rng.uniform 0.0 1.0 rng4
        let curlSpin = float cfg.Actions.ShotCurlSpin * curl * float p.Finishing / 100.0 * float (sign (aimY - float y))

        let angularVelocity =
            { Top = 0.0<radianPerSecond>
              Side = curlSpin * 1.0<radianPerSecond>
              Roll = 0.0<radianPerSecond> }

        let ballPosition = world.State.Ball.Position

        let launch =
            BallFlight.solve
                cfg.Ball
                (float (SimulationClock.dt ctx.Clock))
                { X = ballPosition.X; Y = ballPosition.Y; Z = ballPosition.Z }
                { X = goalLineX * 1.0<meter>; Y = aimY * 1.0<meter>; Z = aimZ * 1.0<meter> }
                (speed * 1.0<meterPerSecond>)
                angularVelocity

        let linearVelocity = launch.Velocity
        let goalY = aimY

        let target: Vec2 option =
            Some
                { X = MatchMath.goalX side
                  Y = goalY * 1.0<meter> }

        let impulse =
            BallPhysics.createImpulse
                pid
                { X = world.State.Ball.Position.X
                  Y = world.State.Ball.Position.Y
                  Z = world.State.Ball.Position.Z }
                linearVelocity
                angularVelocity
                target
                StrikeKind.Shot
                (SimTick.ofSubTicks tick)
                ExecutionContext.OpenPlay

        let arrival =
            tick
            + int (round (launch.Seconds * float ctx.Clock.SubTicksPerSecond)) * 1<subtick>
            + 4<subtick>

        BallFootContact.request
            ctx
            world
            buffer
            impulse
            (TrajectoryIntent.Struck(pid, speed * 1.0<meterPerSecond>, angle, xg))
            (Some arrival)

        rng'
