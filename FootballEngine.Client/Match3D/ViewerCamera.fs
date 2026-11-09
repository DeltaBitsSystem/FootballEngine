namespace FootballEngine.Client.Match3D

open System
open System.Numerics

type ViewerCamera =
    | Broadcast
    | Tactical
    | BehindGoal
    | Touchline
    | CloseUp

type ViewerCameraFrame =
    { Position: Vector3
      Target: Vector3
      FieldOfView: float32
      NearPlane: float32
      FarPlane: float32 }

module ViewerCamera =
    let all = [| Broadcast; Tactical; BehindGoal; Touchline; CloseUp |]

    let private radians degrees = MathF.PI * degrees / 180.0f

    let frame mode aspect (focus: Vector3) =
        let aspect = max 0.1f aspect

        let position, target, fov =
            match mode with
            | Broadcast ->
                let distance = 48.0f * max 1.0f (1.5f / aspect)
                // Rising rather than retreating keeps the camera out of the near-side stands.
                let edge = Math.Clamp((focus.Z - 16.0f) / 14.0f, 0.0f, 1.0f)
                let pitch, yaw = radians (31.0f + edge * 14.0f), radians 6.0f

                let forward =
                    Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch))

                let target = Vector3(focus.X, focus.Y, Math.Clamp(focus.Z - 2.5f, -14.0f, 22.0f))
                target - forward * distance, target, radians 30.0f
            | Tactical ->
                // A fixed full-pitch overview preserves team shape instead of chasing the carrier.
                let distance = 78.0f * max 1.0f (1.55f / aspect)
                Vector3(0.0f, distance, distance * 0.28f), Vector3.Zero, radians 60.0f
            | BehindGoal ->
                // Generated grounds put the end stand at least 8 m behind the goal line; staying inside that runoff
                // keeps the lens out of every end stand and below every roof.
                Vector3(58.0f, 11.0f, 0.0f),
                Vector3(Math.Clamp(focus.X * 0.45f, -20.0f, 20.0f), 0.7f, focus.Z * 0.35f),
                radians 48.0f
            | Touchline ->
                // The near touchline runoff is at least 6.5 m deep, so z = 39 is always in front of the main stand.
                Vector3(focus.X * 0.65f, 9.0f, 39.0f),
                Vector3(focus.X, 0.9f, Math.Clamp(focus.Z, -22.0f, 14.0f)),
                radians 40.0f
            | CloseUp ->
                let target = Vector3(focus.X, 0.95f, focus.Z)
                target + Vector3(4.2f, 1.9f, 5.8f), target, radians 32.0f

        { Position = position
          Target = target
          FieldOfView = fov
          NearPlane = 0.1f
          FarPlane = 500.0f }

    let viewProjection aspect frame =
        Matrix4x4.CreateLookAt(frame.Position, frame.Target, Vector3.UnitY)
        * Matrix4x4.CreatePerspectiveFieldOfView(frame.FieldOfView, aspect, frame.NearPlane, frame.FarPlane)
