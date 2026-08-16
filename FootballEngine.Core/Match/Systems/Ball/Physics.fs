namespace FootballEngine.Core.Match

/// The ball must behave believably with no agent acting on it: once struck,
/// only physics owns it. Gravity, air drag, ground friction, bounce, stop
/// cutoff, Magnus and spin decay are the whole model — no entity is consulted,
/// so the motion is fully deterministic and can be replayed.
module BallPhysics =

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
            StationarySince = stationarySince }

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
