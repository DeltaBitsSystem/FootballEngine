namespace FootballEngine

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module BallPhysics =

    let private applyAerodynamics (config: PhysicsConfig) (pos: Spatial) (spin: Spin) (dt: float<second>) : Spatial =
        let speed = sqrt (pos.Vx * pos.Vx + pos.Vy * pos.Vy + pos.Vz * pos.Vz)

        let magnusX = float spin.Side * config.MagnusCoeff * pos.Vy / 1.0<second>

        let magnusY = float spin.Side * config.MagnusCoeff * (-pos.Vx) / 1.0<second>

        let magnusZ = float spin.Top * config.MagnusCoeff * speed / 1.0<second>

        let vx = pos.Vx * config.AirDrag + magnusX * dt
        let vy = pos.Vy * config.AirDrag + magnusY * dt
        let vz = pos.Vz + config.Gravity * dt + magnusZ * dt

        { pos with
            X = pos.X + vx * dt
            Y = pos.Y + vy * dt
            Z = clamp (pos.Z + vz * dt) 0.0<meter> 100.0<meter>
            Vx = vx
            Vy = vy
            Vz = vz }

    let private applyGroundCollision (config: PhysicsConfig) (pos: Spatial) : Spatial =
        if pos.Z > 0.0<meter> then
            pos
        else
            { pos with
                Z = 0.0<meter>
                Vx = pos.Vx * config.GroundFriction
                Vy = pos.Vy * config.GroundFriction
                Vz = abs pos.Vz * config.GroundRestitution }

    let private clampToPitch (pos: Spatial) : Spatial =
        { pos with
            X = clamp pos.X 0.0<meter> PitchLength
            Y = clamp pos.Y 0.0<meter> PitchWidth }

    let private applyGoalPostHome (config: PhysicsConfig) (pos: Spatial) : Spatial =
        let inGoalY = pos.Y >= PostNearY && pos.Y <= PostFarY

        let inGoalZ = pos.Z >= 0.0<meter> && pos.Z <= CrossbarHeight

        let nearPost = pos.X >= GoalLineHome - 0.2<meter> && pos.X <= GoalLineHome

        if inGoalY && inGoalZ && nearPost then
            { pos with
                X = clamp pos.X (GoalLineHome - 0.2<meter>) GoalLineHome
                Vx = -pos.Vx * config.PostRestitution }
        else
            pos

    let private applyGoalPostAway (config: PhysicsConfig) (pos: Spatial) : Spatial =
        let inGoalY = pos.Y >= PostNearY && pos.Y <= PostFarY

        let inGoalZ = pos.Z >= 0.0<meter> && pos.Z <= CrossbarHeight

        let nearPost = pos.X <= GoalLineAway + 0.2<meter> && pos.X >= GoalLineAway

        if inGoalY && inGoalZ && nearPost then
            { pos with
                X = clamp pos.X GoalLineAway (GoalLineAway + 0.2<meter>)
                Vx = -pos.Vx * config.PostRestitution }
        else
            pos

    let private applyStopThreshold (config: PhysicsConfig) (pos: Spatial) : Spatial =
        if pos.Z > config.AirborneCheckThreshold then
            pos
        else
            let speed = sqrt (pos.Vx * pos.Vx + pos.Vy * pos.Vy)

            if speed < config.BallStopThreshold * 1.0<meter / second> then
                { pos with
                    Vx = 0.0<meter / second>
                    Vy = 0.0<meter / second>
                    Vz = 0.0<meter / second> }
            else
                { pos with
                    Vx =
                        if abs pos.Vx < config.AxisStopThreshold * 1.0<meter / second> then
                            0.0<meter / second>
                        else
                            pos.Vx
                    Vy =
                        if abs pos.Vy < config.AxisStopThreshold * 1.0<meter / second> then
                            0.0<meter / second>
                        else
                            pos.Vy }

    let update (config: PhysicsConfig) (dt: float<second>) (ball: BallPhysicsState) : BallPhysicsState =
        let pos = ball.Position
        let spin = ball.Spin

        if
            pos.Z = 0.0<meter>
            && pos.Vz = 0.0<meter / second>
            && spin.Top = 0.0<radianPerSecond>
            && spin.Side = 0.0<radianPerSecond>
        then
            let newPos =
                { pos with
                    X = pos.X + pos.Vx * dt
                    Y = pos.Y + pos.Vy * dt
                    Vx = pos.Vx * config.GroundFriction
                    Vy = pos.Vy * config.GroundFriction }
                |> clampToPitch
                |> applyStopThreshold config

            { ball with Position = newPos }
        else
            let newPos =
                applyAerodynamics config pos spin dt
                |> applyGroundCollision config
                |> clampToPitch
                |> applyGoalPostHome config
                |> applyGoalPostAway config
                |> applyStopThreshold config

            let newSpin =
                { Top = spin.Top * config.SpinDecay
                  Side = spin.Side * config.SpinDecay }

            { ball with
                Position = newPos
                Spin = newSpin }
