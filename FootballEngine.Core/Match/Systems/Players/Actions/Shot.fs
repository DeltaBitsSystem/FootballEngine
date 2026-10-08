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
        let goalY = 0.0<meter>

        let near = MatchWorld.opponentsNear ctx.Ctx world side x y 6.0
        let openness = 1.0 - 0.3 * MatchMath.clamp01 (float near / 4.0)
        let xg = MatchMath.xGFrom d (float p.Finishing) openness

        let pOnTarget =
            MatchMath.clamp01 (0.5 + (float p.Finishing - 60.0) / 100.0 + 0.2 * openness)

        let angle, rng' =
            let onTarget, rng' = Rng.bernoulli pOnTarget rng

            if onTarget then
                let spread = cfg.Actions.ShotAngleSpread * (1.0 + d / 40.0)
                Rng.uniform (-spread) spread rng'
            else
                let wide, rng'' = Rng.uniform 1.0 4.0 rng'
                let flip, rng''' = Rng.bernoulli 0.5 rng''
                let targetY = (float Pitch.GoalHalfWidth + wide) * (if flip then 1.0 else -1.0)
                atan ((targetY - float y) / float d), rng'''

        let speed = min (float cfg.Ball.MaxShotSpeed) (22.0 + float p.Pace * 0.06)
        let direction = MatchMath.goalDirection side
        let goalDistanceX = abs (float (MatchMath.goalX side) - float x)
        let vx = direction * cos angle * speed
        let vy = sin angle * speed
        let goalY = float y + tan angle * goalDistanceX

        let linearVelocity: LinearVelocity =
            { X = vx * 1.0<meterPerSecond>
              Y = vy * 1.0<meterPerSecond>
              Z = float cfg.Actions.ShotVzBase * 1.0<meterPerSecond> }

        let angularVelocity =
            { Top = -float cfg.Actions.ShotVzBase * 1.0<radianPerSecond>
              Side = angle * 1.0<radianPerSecond>
              Roll = 0.0<radianPerSecond> }

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
            + int (round (goalDistanceX / max 0.1 (abs vx) * float ctx.Clock.SubTicksPerSecond))
              * 1<subtick>
            + 4<subtick>

        BallFootContact.request
            ctx
            world
            buffer
            impulse
            (TrajectoryIntent.Struck(pid, speed * 1.0<meterPerSecond>, angle, xg))
            (Some arrival)

        rng'
