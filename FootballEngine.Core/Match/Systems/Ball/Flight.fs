namespace FootballEngine.Core.Match

open System

/// Aimed deliveries are solved against the same air model the ball then flies, so the point a player aims at is
/// where the physics actually takes the ball, curl included. Iteration counts are fixed, which keeps the solver
/// deterministic and its cost bounded.
module BallFlight =

    [<Struct>]
    type Arrival =
        { /// Underside height when the ball reaches the target distance, or minus the shortfall if it landed first.
          Height: float
          /// Signed offset to the left of the aim line at the target distance.
          Lateral: float
          Seconds: float }

    [<Struct>]
    type Launch =
        { Velocity: LinearVelocity
          Seconds: float }

    let private maxFlightSeconds = 6.0
    let private headingIterations = 4
    let private elevationIterations = 22
    // Driven shots stay well below 45°; a lob above it is a different delivery and is never what is being solved.
    let private minimumElevation = -0.2
    let private maximumElevation = 0.8

    /// Mirrors the airborne branch of BallDynamics.integrate step for step, so prediction and simulation agree.
    let fly
        (cfg: BallConfig)
        dt
        (origin: Vec3)
        (velocity: LinearVelocity)
        (spin: Spin)
        (aimX: float)
        (aimY: float)
        (distance: float)
        : Arrival =
        let x0, y0 = float origin.X, float origin.Y
        let mutable x, y, z = x0, y0, float origin.Z
        let mutable vx, vy, vz = float velocity.X, float velocity.Y, float velocity.Z
        let mutable wx, wy, wz = float spin.X, float spin.Y, float spin.Z
        let decay = cfg.SpinDecay ** dt
        let mutable elapsed = 0.0
        let mutable result = ValueNone

        while result.IsNone do
            let struct (ax, ay, az) = BallAero.flightAcceleration cfg dt vx vy vz wx wy wz
            let nx = x + vx * dt + 0.5 * ax * dt * dt
            let ny = y + vy * dt + 0.5 * ay * dt * dt
            let nz = z + vz * dt + 0.5 * az * dt * dt
            let before = (x - x0) * aimX + (y - y0) * aimY
            let after = (nx - x0) * aimX + (ny - y0) * aimY

            if after >= distance && after > before then
                let f = (distance - before) / (after - before)
                let hx, hy, hz = x + (nx - x) * f, y + (ny - y) * f, z + (nz - z) * f

                result <-
                    ValueSome
                        { Height = hz
                          Lateral = (hx - x0) * -aimY + (hy - y0) * aimX
                          Seconds = elapsed + f * dt }
            elif nz < 0.0 || elapsed >= maxFlightSeconds then
                result <-
                    ValueSome
                        { Height = -(distance - after)
                          Lateral = (nx - x0) * -aimY + (ny - y0) * aimX
                          Seconds = elapsed + dt }
            else
                x <- nx
                y <- ny
                z <- nz
                vx <- vx + ax * dt
                vy <- vy + ay * dt
                vz <- vz + az * dt
                wx <- wx * decay
                wy <- wy * decay
                wz <- wz * decay
                elapsed <- elapsed + dt

        result.Value

    let private velocityFor speed heading elevation : LinearVelocity =
        { X = speed * cos elevation * cos heading * 1.0<meterPerSecond>
          Y = speed * cos elevation * sin heading * 1.0<meterPerSecond>
          Z = speed * sin elevation * 1.0<meterPerSecond> }

    /// Launch velocity of the given speed that carries the ball to `target` (underside height) with the given
    /// boot-relative spin. Elevation is bisected because arrival height rises monotonically with it on the low
    /// branch; heading is corrected by the lateral miss, which converges in a few steps since curl is smooth.
    /// An unreachable target returns the closest launch, so the ball falls short or flies high as it really would.
    let solve
        (cfg: BallConfig)
        (dt: float)
        (origin: Vec3)
        (target: Vec3)
        (speed: float<meterPerSecond>)
        (spin: AngularVelocity)
        : Launch =
        let dx, dy = float target.X - float origin.X, float target.Y - float origin.Y
        let distance = max 0.01 (sqrt (dx * dx + dy * dy))
        let aimX, aimY = dx / distance, dy / distance
        let aimHeading = atan2 dy dx
        let speed = float speed
        let mutable heading = aimHeading
        let mutable elevation = 0.0
        let mutable arrival = { Height = 0.0; Lateral = 0.0; Seconds = 0.0 }

        let flyAt heading elevation =
            let velocity = velocityFor speed heading elevation
            fly cfg dt origin velocity (BallPhysics.worldSpin velocity spin) aimX aimY distance

        for _ in 1..headingIterations do
            let mutable low = minimumElevation
            let mutable high = maximumElevation

            for _ in 1..elevationIterations do
                let middle = 0.5 * (low + high)

                if (flyAt heading middle).Height < float target.Z then
                    low <- middle
                else
                    high <- middle

            elevation <- 0.5 * (low + high)
            arrival <- flyAt heading elevation
            heading <- heading - arrival.Lateral / distance

        { Velocity = velocityFor speed heading elevation
          Seconds = arrival.Seconds }
