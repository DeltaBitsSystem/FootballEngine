namespace FootballEngine.Core.Match

/// The ball must behave believably with no agent acting on it: once struck,
/// only physics owns it. Gravity, air drag, ground friction, bounce, stop
/// cutoff, Magnus and spin decay are the whole model — no entity is consulted,
/// so the motion is fully deterministic and can be replayed.
module BallPhysics =

    let private strikeTag (kind: StrikeKind) : uint64 =
        match kind with
        | StrikeKind.Pass -> 1UL
        | StrikeKind.Cross -> 2UL
        | StrikeKind.Clearance -> 3UL
        | StrikeKind.Shot -> 4UL
        | StrikeKind.Restart SetPieceKind.KickOff -> 5UL
        | StrikeKind.Restart SetPieceKind.ThrowIn -> 6UL
        | StrikeKind.Restart SetPieceKind.Corner -> 7UL
        | StrikeKind.Restart SetPieceKind.GoalKick -> 8UL
        | StrikeKind.Restart SetPieceKind.FreeKick -> 9UL
        | StrikeKind.Restart SetPieceKind.Penalty -> 10UL

    let private actionId (tick: SimTick) (actor: PlayerId) (kind: StrikeKind) : ActionId =
        let (PlayerId player) = actor
        let tickValue = uint64 (max 0 (int (SimTick.value tick)))
        let playerValue = uint64 (max 0 player)
        ActionId((tickValue <<< 32) ^^^ ((playerValue &&& 0xFFFFFFUL) <<< 8) ^^^ strikeTag kind)

    let private vec3OfSpatial (p: Spatial) : Vec3 = { X = p.X; Y = p.Y; Z = p.Z }

    let createImpulse
        (actor: PlayerId)
        (origin: Vec3)
        (linearVelocity: LinearVelocity)
        (angularVelocity: AngularVelocity)
        (intendedTarget: Vec2 option)
        (strikeKind: StrikeKind)
        (contactTick: SimTick)
        (executionContext: ExecutionContext)
        : BallImpulse =
        { ActionId = actionId contactTick actor strikeKind
          RestartId = None
          ActorId = actor
          Origin = origin
          LinearVelocity = linearVelocity
          AngularVelocity = angularVelocity
          IntendedTarget = intendedTarget
          StrikeKind = strikeKind
          ContactTick = contactTick
          ExecutionContext = executionContext }

    let withRestartId (restartId: RestartId) (impulse: BallImpulse) : BallImpulse =
        { impulse with
            RestartId = Some restartId }

    let worldSpin (velocity: LinearVelocity) (angular: AngularVelocity) : Spin =
        let horizontal =
            max 1e-10 (sqrt (float velocity.X ** 2.0 + float velocity.Y ** 2.0))

        let forwardX, forwardY =
            float velocity.X / horizontal, float velocity.Y / horizontal

        { X =
            (-forwardY * float angular.Top + forwardX * float angular.Roll)
            * 1.0<radianPerSecond>
          Y =
            (forwardX * float angular.Top + forwardY * float angular.Roll)
            * 1.0<radianPerSecond>
          Z = angular.Side }

    let strikeContact (cfg: BallConfig) (ball: BallState) (impulse: BallImpulse) : Vec3 * AngularVelocity =
        let desired = worldSpin impulse.LinearVelocity impulse.AngularVelocity

        let dx, dy, dz =
            float (impulse.LinearVelocity.X - ball.Position.Vx),
            float (impulse.LinearVelocity.Y - ball.Position.Vy),
            float (impulse.LinearVelocity.Z - ball.Position.Vz)

        let sx, sy, sz =
            float (desired.X - ball.Spin.X), float (desired.Y - ball.Spin.Y), float (desired.Z - ball.Spin.Z)

        let squared = dx * dx + dy * dy + dz * dz
        let radius = float cfg.Radius
        let inertia = cfg.InertiaRatio * radius * radius

        let tx, ty, tz =
            if squared < 1e-10 then
                0.0, 0.0, 0.0
            else
                inertia * (dy * sz - dz * sy) / squared,
                inertia * (dz * sx - dx * sz) / squared,
                inertia * (dx * sy - dy * sx) / squared

        let tangentLength = sqrt (tx * tx + ty * ty + tz * tz)

        let scale =
            if tangentLength > radius then
                radius / tangentLength
            else
                1.0

        let tx, ty, tz = tx * scale, ty * scale, tz * scale

        let normal =
            if squared < 1e-10 then
                0.0
            else
                -sqrt(max 0.0 (radius * radius - tx * tx - ty * ty - tz * tz)) / sqrt squared

        let rx, ry, rz = tx + dx * normal, ty + dy * normal, tz + dz * normal
        // r cross delta-v divided by shell inertia ties spin to the actual off-centre impulse.
        let wx = float ball.Spin.X + (ry * dz - rz * dy) / inertia
        let wy = float ball.Spin.Y + (rz * dx - rx * dz) / inertia
        let wz = float ball.Spin.Z + (rx * dy - ry * dx) / inertia

        let speed =
            max 1e-10 (sqrt (float impulse.LinearVelocity.X ** 2.0 + float impulse.LinearVelocity.Y ** 2.0))

        let fx, fy =
            float impulse.LinearVelocity.X / speed, float impulse.LinearVelocity.Y / speed

        { X = ball.Position.X + rx * 1.0<meter>
          Y = ball.Position.Y + ry * 1.0<meter>
          Z = ball.Position.Z + cfg.Radius + rz * 1.0<meter> },
        { Top = (-fy * wx + fx * wy) * 1.0<radianPerSecond>
          Side = wz * 1.0<radianPerSecond>
          Roll = (fx * wx + fy * wy) * 1.0<radianPerSecond> }

    /// Every kick enters the ball state through this function. ArrivalGate is
    /// only a candidate-search hint; the contact system still observes the
    /// integrated path on every live tick.
    let applyImpulse
        (world: World)
        (impulse: BallImpulse)
        (trajectory: TrajectoryIntent)
        (arrivalGate: int<subtick> option)
        : unit =
        let origin = impulse.Origin
        let velocity = impulse.LinearVelocity

        world.Observation.Impulse(world.State.Tick, impulse)

        MatchWorld.updateBall world (fun b ->
            { b with
                Position =
                    { X = origin.X
                      Y = origin.Y
                      Z = origin.Z
                      Vx = velocity.X
                      Vy = velocity.Y
                      Vz = velocity.Z }
                Spin = worldSpin velocity impulse.AngularVelocity
                Control = BallControl.Airborne
                Trajectory = Some trajectory
                LastTouch = Some impulse.ActorId
                StationarySince = None
                GKHoldSince = None
                ArrivalGate = arrivalGate
                PreviousPosition = ValueNone
                Impulse = Some impulse })

    /// The Laws count a goal only once the whole ball is over the line, so the test plane sits one radius behind it.
    let goalCrossing (radius: float<meter>) (previous: Spatial) (current: Spatial) : GoalCrossing option =
        let awayPlane = Pitch.awayGoalX + radius
        let homePlane = Pitch.homeGoalX - radius

        let crossingFor side goalX crossed =
            if crossed then
                let dx = float current.X - float previous.X

                if abs dx < 0.000001 then
                    None
                else
                    let fraction = MatchMath.clamp01 ((float goalX - float previous.X) / dx)

                    Some
                        { Side = side
                          Position =
                            { X = goalX
                              Y =
                                (float previous.Y + (float current.Y - float previous.Y) * fraction)
                                * 1.0<meter>
                              Z =
                                (float previous.Z + (float current.Z - float previous.Z) * fraction)
                                * 1.0<meter> }
                          Fraction = fraction }
            else
                None

        match
            crossingFor ClubSide.Home awayPlane (previous.X < awayPlane && current.X >= awayPlane),
            crossingFor ClubSide.Away homePlane (previous.X > homePlane && current.X <= homePlane)
        with
        | Some crossing, _ -> Some crossing
        | _, Some crossing -> Some crossing
        | _ -> None

    let withinGoalFrame (position: Vec3) : bool =
        abs (float position.Y) <= float Pitch.GoalHalfWidth
        && float position.Z >= 0.0
        && position.Z <= Pitch.GoalHeight

    let step: SystemStep =
        fun ctx rng world _ ->
            let ball = world.State.Ball

            if ball.GKHoldSince.IsSome then
                rng
            else
                let updated =
                    BallDynamics.integrate
                        ctx.Ctx.Config.Ball
                        (float (SimulationClock.dt ctx.Clock))
                        world.State.Tick
                        ball

                MatchWorld.updateBall world (fun _ -> updated)
                rng
