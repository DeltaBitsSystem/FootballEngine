namespace FootballEngine.Core.Match

open System
open System.Numerics

/// Air forces shared by integration, launch solving and intercept prediction, so every prediction flies the same
/// ball the simulation flies.
module BallAero =
    /// ½ρA/m: the factor turning a dimensionless coefficient and v² into an acceleration.
    let reference (cfg: BallConfig) =
        let radius = float cfg.Radius
        cfg.AirDensity * Math.PI * radius * radius / (2.0 * cfg.Mass)

    let dragCoefficient (cfg: BallConfig) speed =
        // A logistic step keeps the drag crisis continuous, so solvers never see a kink to oscillate on.
        cfg.DragSupercritical
        + (cfg.DragSubcritical - cfg.DragSupercritical)
          / (1.0 + exp ((speed - float cfg.DragCrisisSpeed) / float cfg.DragCrisisWidth))

    let dragDeceleration (cfg: BallConfig) speed =
        reference cfg * dragCoefficient cfg speed * speed * speed

    [<Struct>]
    type GroundPass = { LaunchSpeed: float; Seconds: float }

    /// Launch speed and travel time for a rolling ball to cover `distance` and still be moving at `arrival`.
    /// v' = -(a + k·v²) has a closed form for a constant drag coefficient. The roll is solved backwards from the
    /// receiver in segments, each with the coefficient at its own mean speed, so the drag crisis a long pass rolls
    /// through is followed closely enough that planners, passer and arrival gate agree with what the ball does.
    let groundPass (cfg: BallConfig) (arrival: float) (distance: float) : GroundPass =
        // A frictionless pitch makes the closed form singular; a vanishing a is the pure-drag limit.
        let a = max 1e-6 (cfg.RollingResistance * float cfg.Gravity)
        let segments = 4
        let segment = max 0.0 distance / float segments
        let mutable speed = max 0.0 arrival
        let mutable seconds = 0.0

        for _ in 1..segments do
            let finish = speed
            let mutable start = finish
            let mutable k = 0.0

            for _ in 1..3 do
                k <- reference cfg * dragCoefficient cfg (0.5 * (start + finish))
                start <- sqrt (max 0.0 ((a + k * finish * finish) * exp (2.0 * k * segment) / k - a / k))

            let root = sqrt (k / a)
            seconds <- seconds + (atan (start * root) - atan (finish * root)) / sqrt (a * k)
            speed <- start

        { LaunchSpeed = speed
          Seconds = seconds }

    /// The pass every planner and passer means by "a pass of this length": one arrival-speed law, one roll model.
    let pass (ball: BallConfig) (arrivalSpeed: float<meterPerSecond>) (arrivalSpeedGain: float) (distance: float) =
        groundPass ball (float arrivalSpeed + arrivalSpeedGain * distance) distance

    let liftCoefficient (cfg: BallConfig) speed spin =
        if speed < 1e-9 then
            0.0
        else
            min cfg.MaxLift (cfg.LiftSlope * float cfg.Radius * spin / speed)

    /// Drag plus Magnus lift, without gravity.
    let acceleration (cfg: BallConfig) vx vy vz wx wy wz : struct (float * float * float) =
        let speed = sqrt (vx * vx + vy * vy + vz * vz)

        if speed < 1e-9 then
            struct (0.0, 0.0, 0.0)
        else
            let k = reference cfg
            let drag = k * dragCoefficient cfg speed * speed
            let spin = sqrt (wx * wx + wy * wy + wz * wz)
            let mutable ax = -drag * vx
            let mutable ay = -drag * vy
            let mutable az = -drag * vz

            if spin > 1e-9 then
                // |ω×v|/|ω| = v·sinθ, so k·C_L·v/|ω| · (ω×v) has magnitude k·C_L·v² along the lift direction.
                let scale = k * liftCoefficient cfg speed spin * speed / spin
                ax <- ax + scale * (wy * vz - wz * vy)
                ay <- ay + scale * (wz * vx - wx * vz)
                az <- az + scale * (wx * vy - wy * vx)

            struct (ax, ay, az)

    /// Acceleration held constant over one flight step: the midpoint (RK2) estimate keeps drag accurate at tick
    /// rate while the ground impact time stays a closed-form quadratic.
    let flightAcceleration (cfg: BallConfig) dt vx vy vz wx wy wz : struct (float * float * float) =
        let gravity = float cfg.Gravity
        let struct (ax0, ay0, az0) = acceleration cfg vx vy vz wx wy wz
        let half = 0.5 * dt

        let struct (ax, ay, az) =
            acceleration cfg (vx + ax0 * half) (vy + ay0 * half) (vz + (az0 - gravity) * half) wx wy wz

        struct (ax, ay, az - gravity)

/// Fixed-size sphere dynamics avoid a general collision solver and heap-allocated mutable closures.
module BallDynamics =
    /// The ball's kinematic state in bare floats: hot paths (keeper prediction, carry solving) step it in place
    /// instead of building a BallState per step.
    [<Struct>]
    type Motion =
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
            // A rolling ball still pushes through air; at pass speeds drag outweighs turf resistance.
            let loss =
                (cfg.RollingResistance * float cfg.Gravity + BallAero.dragDeceleration cfg speed)
                * duration

            let retained = max 0.0 (speed - loss) / speed

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

    /// Earliest time in [0, 1] at which a centre moving p → p + d comes within `reach` of a 2D axis point.
    let private sweptCircle px py dx dy reach =
        let a = dx * dx + dy * dy
        let b = 2.0 * (px * dx + py * dy)
        let c = px * px + py * py - reach * reach

        if c <= 0.0 then
            // Already overlapping: only a centre still moving inward needs resolving.
            if b < 0.0 then ValueSome 0.0 else ValueNone
        elif a < 1e-14 then
            ValueNone
        else
            let discriminant = b * b - 4.0 * a * c

            if discriminant < 0.0 then
                ValueNone
            else
                let t = (-b - sqrt discriminant) / (2.0 * a)
                if t >= 0.0 && t <= 1.0 then ValueSome t else ValueNone

    let private bounce (m: Motion) nx ny nz restitution friction =
        let mutable r = m
        let normal = r.Vx * nx + r.Vy * ny + r.Vz * nz

        if normal < 0.0 then
            r.Vx <- r.Vx - (1.0 + restitution) * normal * nx
            r.Vy <- r.Vy - (1.0 + restitution) * normal * ny
            r.Vz <- r.Vz - (1.0 + restitution) * normal * nz
            let after = r.Vx * nx + r.Vy * ny + r.Vz * nz
            let tx, ty, tz = r.Vx - after * nx, r.Vy - after * ny, r.Vz - after * nz
            r.Vx <- r.Vx - friction * tx
            r.Vy <- r.Vy - friction * ty
            r.Vz <- r.Vz - friction * tz
            r.Wx <- r.Wx * (1.0 - friction)
            r.Wy <- r.Wy * (1.0 - friction)
            r.Wz <- r.Wz * (1.0 - friction)

        r

    /// Posts, crossbar and net as analytic shapes. The path is swept so a 30 m/s shot cannot tunnel through a 12 cm
    /// post between ticks; one contact per step is enough at tick rate.
    let private goalFrame
        (cfg: BallConfig)
        dt
        tick
        x0
        y0
        z0
        (motion: Motion)
        : struct (Motion * GoalFrameContact voption) =
        let radius = float cfg.Radius
        let postRadius = float Pitch.PostRadius
        let halfWidth = float Pitch.GoalHalfWidth
        let height = float Pitch.GoalHeight
        let depth = float Pitch.GoalDepth
        let mutable m = motion
        let mutable contact = ValueNone
        let c0 = z0 + radius
        let dx, dy, dz = m.X - x0, m.Y - y0, (m.Z + radius) - c0

        for goalIndex = 0 to 1 do
            let goalX =
                if goalIndex = 0 then
                    float Pitch.homeGoalX
                else
                    float Pitch.awayGoalX

            let defender = if goalIndex = 0 then ClubSide.Home else ClubSide.Away
            let near = abs (x0 - goalX) < depth + 2.0 || abs (m.X - goalX) < depth + 2.0

            if contact.IsNone && near then
                let reach = radius + postRadius
                let mutable best = 2.0
                let mutable nx, ny, nz = 0.0, 0.0, 0.0
                let mutable part = Post

                for postIndex = 0 to 1 do
                    let postY = (if postIndex = 0 then -1.0 else 1.0) * (halfWidth + postRadius)

                    match sweptCircle (x0 - goalX) (y0 - postY) dx dy reach with
                    | ValueSome t when t < best ->
                        let cz = c0 + dz * t

                        if cz >= 0.0 && cz <= height + 2.0 * postRadius then
                            let px, py = x0 + dx * t - goalX, y0 + dy * t - postY
                            let length = max 1e-9 (sqrt (px * px + py * py))
                            best <- t
                            nx <- px / length
                            ny <- py / length
                            nz <- 0.0
                            part <- Post
                    | _ -> ()

                let barZ = height + postRadius

                match sweptCircle (x0 - goalX) (c0 - barZ) dx dz reach with
                | ValueSome t when t < best ->
                    let cy = y0 + dy * t

                    if abs cy <= halfWidth + 2.0 * postRadius then
                        let px, pz = x0 + dx * t - goalX, c0 + dz * t - barZ
                        let length = max 1e-9 (sqrt (px * px + pz * pz))
                        best <- t
                        nx <- px / length
                        ny <- 0.0
                        nz <- pz / length
                        part <- Crossbar
                | _ -> ()

                if best <= 1.0 then
                    let hitX, hitY, hitZ = x0 + dx * best, y0 + dy * best, c0 + dz * best
                    m <- bounce m nx ny nz cfg.WoodworkRestitution cfg.WoodworkFriction
                    let remaining = (1.0 - best) * dt
                    m.X <- hitX + m.Vx * remaining
                    m.Y <- hitY + m.Vy * remaining
                    m.Z <- max 0.0 (hitZ - radius + m.Vz * remaining)

                    contact <-
                        ValueSome
                            { Tick = tick
                              Part = part
                              Goal = defender
                              Position =
                                { X = hitX * 1.0<meter>
                                  Y = hitY * 1.0<meter>
                                  Z = (hitZ - radius) * 1.0<meter> } }
                else
                    // The net is a soft box behind the line: back, sides and roof stop the ball from either side.
                    let direction = if goalX > 0.0 then 1.0 else -1.0
                    let c1 = m.Z + radius
                    let depth0, depth1 = (x0 - goalX) * direction, (m.X - goalX) * direction

                    let crossed s0 s1 =
                        (s0 >= radius && s1 < radius) || (s0 <= -radius && s1 > -radius)

                    let mutable index = 0

                    while contact.IsNone && index < 4 do
                        // The four faces are computed in place: an array of them was built on every step near a goal.
                        let struct (s0, s1, fx, fy, fz) =
                            match index with
                            | 0 -> struct (depth - depth0, depth - depth1, -direction, 0.0, 0.0)
                            | 1 -> struct (halfWidth - y0, halfWidth - m.Y, 0.0, -1.0, 0.0)
                            | 2 -> struct (halfWidth + y0, halfWidth + m.Y, 0.0, 1.0, 0.0)
                            | _ -> struct (height - c0, height - c1, 0.0, 0.0, -1.0)

                        if crossed s0 s1 then
                            // Contact happens when the surface, not the centre, reaches the net: one radius short of it.
                            let touch = if s0 > 0.0 then radius else -radius
                            let t = Math.Clamp((s0 - touch) / (s0 - s1), 0.0, 1.0)
                            let hx, hy, hz = x0 + dx * t, y0 + dy * t, c0 + dz * t
                            let hd = (hx - goalX) * direction
                            let slack = radius + 0.01

                            if
                                hd > -slack
                                && hd < depth + slack
                                && abs hy < halfWidth + slack
                                && hz < height + slack
                            then
                                // Inward normal from inside the goal, outward from outside it.
                                let sign = if s0 > 0.0 then 1.0 else -1.0
                                m <- bounce m (fx * sign) (fy * sign) (fz * sign) cfg.NetRestitution cfg.NetFriction
                                let rest = radius * sign
                                m.X <- if fx <> 0.0 then goalX + direction * (depth - rest) else hx
                                m.Y <- if fy <> 0.0 then -fy * (halfWidth - rest) else hy
                                m.Z <- max 0.0 ((if fz <> 0.0 then height - rest else hz) - radius)

                                contact <-
                                    ValueSome
                                        { Tick = tick
                                          Part = Net
                                          Goal = defender
                                          Position =
                                            { X = m.X * 1.0<meter>
                                              Y = m.Y * 1.0<meter>
                                              Z = m.Z * 1.0<meter> } }

                        index <- index + 1

        struct (m, contact)

    let motionOf (ball: BallState) : Motion =
        let p = ball.Position

        { X = float p.X
          Y = float p.Y
          Z = float p.Z
          Vx = float p.Vx
          Vy = float p.Vy
          Vz = float p.Vz
          Wx = float ball.Spin.X
          Wy = float ball.Spin.Y
          Wz = float ball.Spin.Z }

    /// One dynamics step in place: the arithmetic `integrate` performs, without building a ball. Returns the
    /// goal-frame contact of the step, if any.
    let advance (cfg: BallConfig) dt tick (motion: byref<Motion>) : GoalFrameContact voption =
        let mutable m = motion
        let x0, y0, z0 = m.X, m.Y, m.Z

        if m.Z <= 0.0 && abs m.Vz <= float cfg.StopSpeedCutoff then
            // Support cancels gravity; applying it here would manufacture perpetual tiny bounces.
            m <- roll cfg dt m
        else
            let struct (ax, ay, az) =
                BallAero.flightAcceleration cfg dt m.Vx m.Vy m.Vz m.Wx m.Wy m.Wz

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

        let struct (framed, frameContact) = goalFrame cfg dt tick x0 y0 z0 m

        m <- framed
        let decay = cfg.SpinDecay ** dt
        // Air torque decays all spin in flight. On the turf, grip keeps horizontal spin locked to rolling (friction
        // already couples it), while pivoting on the grass still bleeds spin about the vertical axis.
        let horizontalDecay = if m.Z > 0.0 then decay else 1.0

        m.Wx <- m.Wx * horizontalDecay
        m.Wy <- m.Wy * horizontalDecay
        m.Wz <- m.Wz * decay
        motion <- m
        frameContact


    let integrate (cfg: BallConfig) dt tick (ball: BallState) =
        let p = ball.Position
        let mutable m = motionOf ball
        let frameContact = advance cfg dt tick &m

        let spin: Spin =
            { X = m.Wx * 1.0<radianPerSecond>
              Y = m.Wy * 1.0<radianPerSecond>
              Z = m.Wz * 1.0<radianPerSecond> }

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
            FrameContact =
                if frameContact.IsSome then
                    frameContact
                else
                    ball.FrameContact
            PreviousPosition = ValueSome p }
