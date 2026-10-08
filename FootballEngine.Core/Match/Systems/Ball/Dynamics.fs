namespace FootballEngine.Core.Match

open System
open System.Numerics

/// Fixed-size sphere dynamics avoid a general collision solver and heap-allocated mutable closures.
module BallDynamics =
    [<Struct>]
    type private Motion =
        { mutable X: float
          mutable Y: float
          mutable Z: float
          mutable Vx: float
          mutable Vy: float
          mutable Vz: float
          mutable Wx: float
          mutable Wy: float
          mutable Wz: float }

    let orientationStep (orientation: Quaternion) (spin: Spin) dt =
        let axis = Vector3(float32 spin.X, float32 spin.Y, float32 spin.Z)
        let speed = axis.Length()

        if speed <= 1e-8f then
            orientation
        else
            Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis / speed, speed * float32 dt) * orientation)

    let private tangentImpulse (cfg: BallConfig) maximum (motion: Motion) =
        let mutable m = motion
        let radius = float cfg.Radius
        let slipX, slipY = m.Vx - radius * m.Wy, m.Vy + radius * m.Wx
        let slip = sqrt (slipX * slipX + slipY * slipY)

        if slip > 1e-10 then
            let impulse = min maximum (slip / (1.0 + 1.0 / cfg.InertiaRatio))
            let ix, iy = slipX / slip * impulse, slipY / slip * impulse
            m.Vx <- m.Vx - ix
            m.Vy <- m.Vy - iy
            m.Wx <- m.Wx - iy / (cfg.InertiaRatio * radius)
            m.Wy <- m.Wy + ix / (cfg.InertiaRatio * radius)

        m

    let private roll (cfg: BallConfig) duration motion =
        let mutable m =
            tangentImpulse cfg (cfg.SlipFriction * float cfg.Gravity * duration) motion

        let speed = sqrt (m.Vx * m.Vx + m.Vy * m.Vy)

        if speed > 1e-10 then
            let retained =
                max 0.0 (speed - cfg.RollingResistance * float cfg.Gravity * duration) / speed

            m.Vx <- m.Vx * retained
            m.Vy <- m.Vy * retained
            m.Wx <- m.Wx * retained
            m.Wy <- m.Wy * retained

        if
            sqrt (m.Vx * m.Vx + m.Vy * m.Vy) < float cfg.StopSpeedCutoff
            && float cfg.Radius * sqrt (m.Wx * m.Wx + m.Wy * m.Wy) < float cfg.StopSpeedCutoff
        then
            m.Vx <- 0.0
            m.Vy <- 0.0
            m.Wx <- 0.0
            m.Wy <- 0.0

        m.X <- m.X + m.Vx * duration
        m.Y <- m.Y + m.Vy * duration
        m.Z <- 0.0
        m.Vz <- 0.0
        m

    let integrate (cfg: BallConfig) dt tick (ball: BallState) =
        let p = ball.Position

        let mutable m =
            { X = float p.X
              Y = float p.Y
              Z = float p.Z
              Vx = float p.Vx
              Vy = float p.Vy
              Vz = float p.Vz
              Wx = float ball.Spin.X
              Wy = float ball.Spin.Y
              Wz = float ball.Spin.Z }

        if m.Z <= 0.0 && abs m.Vz <= float cfg.StopSpeedCutoff then
            // Support cancels gravity; applying it here would manufacture perpetual tiny bounces.
            m <- roll cfg dt m
        else
            let drag = cfg.AirDrag ** dt
            m.Vx <- m.Vx * drag
            m.Vy <- m.Vy * drag
            m.Vz <- m.Vz * drag
            let ax = cfg.MagnusFactor * (m.Wy * m.Vz - m.Wz * m.Vy)
            let ay = cfg.MagnusFactor * (m.Wz * m.Vx - m.Wx * m.Vz)
            let az = cfg.MagnusFactor * (m.Wx * m.Vy - m.Wy * m.Vx) - float cfg.Gravity
            let nextZ = m.Z + m.Vz * dt + 0.5 * az * dt * dt

            if nextZ >= 0.0 then
                m.X <- m.X + m.Vx * dt + 0.5 * ax * dt * dt
                m.Y <- m.Y + m.Vy * dt + 0.5 * ay * dt * dt
                m.Z <- nextZ
                m.Vx <- m.Vx + ax * dt
                m.Vy <- m.Vy + ay * dt
                m.Vz <- m.Vz + az * dt
            else
                let impactTime =
                    if abs az < 1e-10 then
                        Math.Clamp(-m.Z / min -1e-10 m.Vz, 0.0, dt)
                    else
                        let discriminant = max 0.0 (m.Vz * m.Vz - 2.0 * az * m.Z)
                        let first = (-m.Vz - sqrt discriminant) / az
                        let second = (-m.Vz + sqrt discriminant) / az
                        Math.Clamp((if first >= 0.0 && first <= dt then first else second), 0.0, dt)

                m.X <- m.X + m.Vx * impactTime + 0.5 * ax * impactTime * impactTime
                m.Y <- m.Y + m.Vy * impactTime + 0.5 * ay * impactTime * impactTime
                m.Vx <- m.Vx + ax * impactTime
                m.Vy <- m.Vy + ay * impactTime
                let incoming = min 0.0 (m.Vz + az * impactTime)
                m.Vz <- -incoming * cfg.BounceRestitution
                m <- tangentImpulse cfg (cfg.BounceFriction * (1.0 + cfg.BounceRestitution) * -incoming) m
                let remaining = dt - impactTime

                if
                    m.Vz <= float cfg.StopSpeedCutoff
                    || m.Vz * remaining + 0.5 * az * remaining * remaining <= 0.0
                then
                    // A rebound whose entire next hop fits in the remainder settles instead of running unbounded micro-collisions.
                    m <- roll cfg remaining m
                else
                    m.X <- m.X + m.Vx * remaining
                    m.Y <- m.Y + m.Vy * remaining
                    m.Z <- m.Vz * remaining + 0.5 * az * remaining * remaining
                    m.Vz <- m.Vz + az * remaining

        let decay = cfg.SpinDecay ** dt

        let spin: Spin =
            { X = m.Wx * decay * 1.0<radianPerSecond>
              Y = m.Wy * decay * 1.0<radianPerSecond>
              Z = m.Wz * decay * 1.0<radianPerSecond> }

        let moving = m.Vx <> 0.0 || m.Vy <> 0.0 || m.Vz <> 0.0 || m.Z > 0.0

        { ball with
            Radius = cfg.Radius
            Position =
                { X = m.X * 1.0<meter>
                  Y = m.Y * 1.0<meter>
                  Z = m.Z * 1.0<meter>
                  Vx = m.Vx * 1.0<meterPerSecond>
                  Vy = m.Vy * 1.0<meterPerSecond>
                  Vz = m.Vz * 1.0<meterPerSecond> }
            Spin = spin
            Orientation = orientationStep ball.Orientation spin dt
            StationarySince =
                if moving then
                    None
                else
                    match ball.StationarySince with
                    | Some _ -> ball.StationarySince
                    | None -> Some tick
            PreviousPosition = ValueSome p }
