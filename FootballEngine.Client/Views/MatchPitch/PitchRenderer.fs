namespace FootballEngine.Client.Views.MatchPitch

open SkiaSharp

module PitchRenderer =
    let width = 1050.0f
    let height = 680.0f
    let toCanvas (x: float) (y: float) = float32 (x + 52.5) * 10.0f, float32 (y + 34.0) * 10.0f

    let record (resources: RenderResources) =
        use recorder = new SKPictureRecorder()
        let canvas = recorder.BeginRecording(SKRect(-20.0f, -2.0f, width + 20.0f, height + 2.0f))
        for stripe = 0 to 9 do
            canvas.DrawRect(SKRect(0.0f, float32 stripe * 68.0f, width, float32 (stripe + 1) * 68.0f),
                            if stripe % 2 = 0 then resources.GrassDark else resources.GrassLight)
        canvas.DrawRect(SKRect(0.0f, 0.0f, width, height), resources.Line)
        canvas.DrawLine(525.0f, 0.0f, 525.0f, height, resources.Line)
        canvas.DrawCircle(525.0f, 340.0f, 91.5f, resources.Line)
        canvas.DrawCircle(525.0f, 340.0f, 3.5f, resources.Spot)
        for right in [ false; true ] do
            canvas.Save() |> ignore
            if right then
                canvas.Translate(width, 0.0f)
                canvas.Scale(-1.0f, 1.0f)
            canvas.DrawRect(SKRect(0.0f, 138.4f, 165.0f, 541.6f), resources.Line)
            canvas.DrawRect(SKRect(0.0f, 248.4f, 55.0f, 431.6f), resources.Line)
            canvas.DrawCircle(110.0f, 340.0f, 3.0f, resources.Spot)
            canvas.DrawArc(SKRect(18.5f, 248.5f, 201.5f, 431.5f), -53.0f, 106.0f, false, resources.Line)
            canvas.DrawRect(SKRect(-16.0f, 303.4f, 0.0f, 376.6f), resources.Goal)
            canvas.DrawRect(SKRect(-16.0f, 303.4f, 0.0f, 376.6f), resources.Line)
            canvas.DrawArc(SKRect(-10.0f, -10.0f, 10.0f, 10.0f), 0.0f, 90.0f, false, resources.Line)
            canvas.DrawArc(SKRect(-10.0f, 670.0f, 10.0f, 690.0f), 270.0f, 90.0f, false, resources.Line)
            canvas.Restore()
        recorder.EndRecording()
