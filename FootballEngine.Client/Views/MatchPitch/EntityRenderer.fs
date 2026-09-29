namespace FootballEngine.Client.Views.MatchPitch

open System
open SkiaSharp
open FootballEngine.Client.Views.Components

module EntityRenderer =
    let private person (canvas: SKCanvas) (r: RenderResources) x y stride (shirt: SKPaint) (sleeve: SKPaint) (shorts: SKPaint) =
        canvas.DrawOval(SKRect(x - 13.0f, y + 8.0f, x + 13.0f, y + 17.0f), r.Shadow)
        canvas.Save() |> ignore
        canvas.Translate(x, y)
        let arm = -stride * 0.72f
        canvas.DrawLine(-3.0f, 8.0f, -3.7f + stride, 14.2f, shorts)
        canvas.DrawLine(-3.7f + stride, 14.2f, -3.8f + stride * 1.25f, 20.2f, r.Socks)
        canvas.DrawLine(-3.8f + stride * 1.25f, 20.2f, -3.8f + stride * 1.35f, 22.2f, r.Boot)
        canvas.DrawLine(-5.4f, -4.8f, -8.7f, 1.5f + arm, sleeve)
        canvas.DrawLine(-8.7f, 1.5f + arm, -8.4f, 6.6f + arm, r.SkinLimb)
        canvas.DrawLine(3.0f, 8.0f, 3.7f - stride, 14.2f, shorts)
        canvas.DrawLine(3.7f - stride, 14.2f, 3.8f - stride * 1.25f, 20.2f, r.Socks)
        canvas.DrawLine(3.8f - stride * 1.25f, 20.2f, 3.8f - stride * 1.35f, 22.2f, r.Boot)
        canvas.DrawLine(5.4f, -4.8f, 8.7f, 1.5f - arm, sleeve)
        canvas.DrawLine(8.7f, 1.5f - arm, 8.4f, 6.6f - arm, r.SkinLimb)
        canvas.DrawPath(r.Torso, shirt)
        canvas.DrawPath(r.Shorts, r.ShortsFill)
        canvas.DrawRoundRect(SKRect(-2.2f, -10.0f, 2.2f, -6.7f), 1.2f, 1.2f, r.Skin)
        canvas.DrawOval(SKRect(-4.5f, -16.8f, 4.5f, -8.2f), r.Skin)
        canvas.Restore()

    let player (canvas: SKCanvas) (r: RenderResources) (player: RenderPlayer) position isKeeper time =
        let x, y = PitchRenderer.toCanvas position.X position.Y
        let speed = Vector2.magnitude player.Velocity
        let gait = float32 (Math.Sin(time * (2.2 + min 3.8 speed) + float player.VisualSlot * 0.73))
        let stride = gait * min 4.5f (float32 speed * 0.75f)
        let shirt = if isKeeper then r.Keeper elif player.IsHome then r.Home else r.Away
        let sleeve = if isKeeper then r.KeeperSleeve elif player.IsHome then r.HomeSleeve else r.AwaySleeve
        let shorts = if player.IsHome then r.HomeShorts else r.AwayShorts
        let sprite = if isKeeper then r.KeeperSprite elif player.IsHome then r.HomeSprite else r.AwaySprite
        if player.HasBall then
            canvas.DrawOval(SKRect(x - 16.0f, y + 15.0f, x + 16.0f, y + 25.0f), r.Possession)
        match sprite with
        | Some bitmap ->
            canvas.DrawOval(SKRect(x - 13.0f, y + 15.0f, x + 13.0f, y + 22.0f), r.Shadow)
            canvas.DrawBitmap(bitmap, SKRect(x - 17.0f, y - 28.0f, x + 17.0f, y + 24.0f))
        | None -> person canvas r x y stride shirt sleeve shorts
        let condition = if player.Condition >= 70 then r.Condition elif player.Condition >= 45 then r.Tired else r.Exhausted
        canvas.DrawLine(x - 8.0f, y + 25.0f, x - 8.0f + 16.0f * float32 player.Condition / 100.0f, y + 25.0f, condition)
        let nameWidth = r.Name.MeasureText player.ShortName
        canvas.DrawRoundRect(SKRect(x - nameWidth / 2.0f - 4.0f, y + 29.0f, x + nameWidth / 2.0f + 4.0f, y + 42.0f), 3.0f, 3.0f, r.NameBackground)
        canvas.DrawText(player.ShortName, x, y + 39.0f, r.Name)

    let official (canvas: SKCanvas) (r: RenderResources) (official: RenderOfficial) =
        let x, y = PitchRenderer.toCanvas official.Position.X official.Position.Y
        match r.OfficialSprite with
        | Some bitmap ->
            canvas.DrawOval(SKRect(x - 13.0f, y + 15.0f, x + 13.0f, y + 22.0f), r.Shadow)
            canvas.DrawBitmap(bitmap, SKRect(x - 17.0f, y - 28.0f, x + 17.0f, y + 24.0f))
        | None -> person canvas r x y 0.0f r.Official r.OfficialSleeve r.Boot
        if official.IsAssistant then
            canvas.DrawLine(x + 8.0f, y - 2.0f, x + 8.0f, y + 10.0f, r.Boot)
            canvas.DrawRect(SKRect(x + 8.0f, y - 2.0f, x + 17.0f, y + 4.0f), r.Flag)
        elif official.Signal <> OfficialSignal.NoSignal then
            canvas.DrawLine(x - 6.0f, y - 4.0f, x - 11.0f, y - 14.0f, r.SkinLimb)

    let ball (canvas: SKCanvas) (r: RenderResources) (ball: RenderBall) position height time =
        let x, y = PitchRenderer.toCanvas position.X position.Y
        let h = float32 (max 0.0 height)
        canvas.DrawOval(SKRect(x - 7.0f, y - 3.0f, x + 7.0f, y + 4.0f), r.Shadow)
        canvas.Save() |> ignore
        // Elevation belongs to presentation only; the ground position stays authoritative.
        canvas.Translate(x, y - h * 8.0f)
        let radius = 6.5f + min 2.0f (h * 0.25f)
        let speed = Vector2.magnitude ball.Velocity
        if speed > 5.0 then
            let trail = float32 (min 24.0 (speed * 2.0))
            canvas.DrawLine(-float32 (ball.Velocity.X / speed) * trail, -float32 (ball.Velocity.Y / speed) * trail, 0.0f, 0.0f, r.BallTrail)
        canvas.DrawCircle(0.0f, 0.0f, radius, r.Ball)
        canvas.RotateRadians(float32 (ball.Spin.Y * time))
        canvas.DrawCircle(0.0f, 0.0f, radius * 0.3f, r.BallPanel)
        for index = 0 to 4 do
            let angle = float32 index * 2.0f * MathF.PI / 5.0f
            let px = MathF.Cos angle * radius * 0.64f
            let py = MathF.Sin angle * radius * 0.64f
            canvas.DrawLine(0.0f, 0.0f, px, py, r.BallSeam)
            canvas.DrawCircle(px, py, radius * 0.1f, r.BallPanel)
        canvas.DrawCircle(0.0f, 0.0f, radius, r.BallOutline)
        canvas.DrawOval(SKRect(-3.2f, -4.2f, -0.8f, -2.2f), r.Highlight)
        canvas.Restore()
