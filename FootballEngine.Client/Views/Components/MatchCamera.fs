namespace FootballEngine.Client.Views.Components

open System

type VisualQuality =
    | Low
    | Balanced
    | Ultra
    | Capture

[<Struct>]
type Camera2D =
    { CenterX: float32
      CenterY: float32
      Zoom: float32 }

module Camera2D =
    let fullPitch =
        { CenterX = 525.0f
          CenterY = 340.0f
          Zoom = 1.0f }

    let private clamp low high value = MathF.Max(low, MathF.Min(high, value))

    let followBall (ball: RenderBall) =
        let zoom = 1.045f
        let halfVisibleWidth = 1050.0f / zoom / 2.0f
        let halfVisibleHeight = 680.0f / zoom / 2.0f
        let ballX = float32 ((ball.Position.X + 52.5) * 10.0)
        let ballY = float32 ((ball.Position.Y + 34.0) * 10.0)

        { CenterX = clamp halfVisibleWidth (1050.0f - halfVisibleWidth) ballX
          CenterY = clamp halfVisibleHeight (680.0f - halfVisibleHeight) ballY
          Zoom = zoom }

    let translation camera =
        525.0f - camera.CenterX * camera.Zoom, 340.0f - camera.CenterY * camera.Zoom
