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
        { impulse with RestartId = Some restartId }

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
        let angular = impulse.AngularVelocity

        MatchWorld.updateBall world (fun b ->
            { b with
                Position =
                    { X = origin.X
                      Y = origin.Y
                      Z = origin.Z
                      Vx = velocity.X
                      Vy = velocity.Y
                      Vz = velocity.Z }
                Spin =
                    { Top = angular.Top
                      Side = angular.Side }
                Control = BallControl.Airborne
                Trajectory = Some trajectory
                LastTouch = Some impulse.ActorId
                StationarySince = None
                GKHoldSince = None
                ArrivalGate = arrivalGate
                PreviousPosition = None
                Impulse = Some impulse })

    let goalCrossing (previous: Spatial) (current: Spatial) : GoalCrossing option =
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
            crossingFor ClubSide.Home Pitch.awayGoalX (previous.X < Pitch.awayGoalX && current.X >= Pitch.awayGoalX),
            crossingFor ClubSide.Away Pitch.homeGoalX (previous.X > Pitch.homeGoalX && current.X <= Pitch.homeGoalX)
        with
        | Some crossing, _ -> Some crossing
        | _, Some crossing -> Some crossing
        | _ -> None

    let withinGoalFrame (position: Vec3) : bool =
        abs (float position.Y) <= float Pitch.GoalHalfWidth
        && float position.Z >= 0.0
        && float position.Z <= 2.44

    let private integrate (ctx: SystemContext) (tick: int<subtick>) (ball: BallState) (cfg: BallConfig) : BallState =
        let dt = float (SimulationClock.dt ctx.Clock)
        let p = ball.Position
        let dragDt = cfg.AirDrag ** dt

        let vx0 = float p.Vx * dragDt
        let vy0 = float p.Vy * dragDt
        let vz0 = float p.Vz - float cfg.Gravity * dt

        let speed = sqrt (vx0 * vx0 + vy0 * vy0 + vz0 * vz0)

        // Magnus: topspin bends down, sidespin bends laterally.
        let vz1 = vz0 - float cfg.MagnusFactor * speed * float ball.Spin.Top * dt
        let vy1 = vy0 + float cfg.MagnusFactor * speed * float ball.Spin.Side * dt

        let mutable vx = vx0 * 1.0<meterPerSecond>
        let mutable vy = vy1 * 1.0<meterPerSecond>
        let mutable vz = vz1 * 1.0<meterPerSecond>

        // On the ground the ball has two regimes: a bounce when it still has vertical
        // speed, or a roll otherwise — each with its own friction.
        let grounded = float p.Z <= 0.0 && vz0 <= 0.0

        if grounded && -vz1 > float cfg.StopSpeedCutoff then
            vz <- -vz * cfg.BounceRestitution
            vx <- vx * (1.0 - cfg.BounceFriction)
            vy <- vy * (1.0 - cfg.BounceFriction)
        elif grounded then
            vz <- 0.0<meterPerSecond>

        if float p.Z <= 0.0 then
            let gd = cfg.GroundFriction ** dt
            vx <- vx * gd
            vy <- vy * gd

        let totalSpeed = MatchMath.speedOf vx vy

        if
            totalSpeed < float cfg.StopSpeedCutoff
            && abs (float vz) < float cfg.StopSpeedCutoff
        then
            vx <- 0.0<meterPerSecond>
            vy <- 0.0<meterPerSecond>
            vz <- 0.0<meterPerSecond>

        let nx =
            MatchMath.clamp (-(float Pitch.HalfLength + 5.0)) (float Pitch.HalfLength + 5.0) (float p.X + float vx * dt)

        let ny =
            MatchMath.clamp (-(float Pitch.HalfWidth + 5.0)) (float Pitch.HalfWidth + 5.0) (float p.Y + float vy * dt)

        let nz = max 0.0 (float p.Z + float vz * dt)

        let wasMoving = totalSpeed > float cfg.StopSpeedCutoff

        let stationarySince =
            if wasMoving then None
            elif ball.StationarySince.IsSome then ball.StationarySince
            else Some tick

        let spinTop = float ball.Spin.Top * (cfg.SpinDecay ** dt) * 1.0<radianPerSecond>
        let spinSide = float ball.Spin.Side * (cfg.SpinDecay ** dt) * 1.0<radianPerSecond>

        { ball with
            Position =
                { X = nx * 1.0<meter>
                  Y = ny * 1.0<meter>
                  Z = nz * 1.0<meter>
                  Vx = vx
                  Vy = vy
                  Vz = vz }
            Spin = { Top = spinTop; Side = spinSide }
            StationarySince = stationarySince
            PreviousPosition = Some p }

    let step: SystemStep =
        fun ctx rng world _ ->
            let ball = world.State.Ball

            match ball.Control with
            | BallControl.Controlled _
            | BallControl.Receiving _ -> rng
            | _ ->
                let updated = integrate ctx world.State.Tick ball ctx.Ctx.Config.Ball
                MatchWorld.updateBall world (fun _ -> updated)
                rng
