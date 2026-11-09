namespace FootballEngine.Client.Match3D

open System.Diagnostics

/// Presented-frame intervals include pacing and CPU work, without blocking for GPU timestamps.
[<Sealed>]
type FramePacing(startup: Stopwatch) =
    let clock = Stopwatch.StartNew()
    let mutable lastPresentation: float option = None
    let mutable sampleMilliseconds = 0.0
    let mutable sampleFrames = 0
    let mutable performance: (float * float) option = None
    let mutable frameCount = 0

    member _.Performance = performance

    member _.Presented(width: uint32, height: uint32) =
        let now = clock.Elapsed.TotalMilliseconds

        match lastPresentation with
        | Some previous when now - previous > 0.0 && now - previous < 2000.0 ->
            sampleMilliseconds <- sampleMilliseconds + now - previous
            sampleFrames <- sampleFrames + 1

            // A half-second window keeps the counter readable instead of flickering every frame.
            if sampleMilliseconds >= 500.0 then
                let milliseconds = sampleMilliseconds / float sampleFrames
                performance <- Some(1000.0 / milliseconds, milliseconds)
                sampleMilliseconds <- 0.0
                sampleFrames <- 0
        | _ ->
            sampleMilliseconds <- 0.0
            sampleFrames <- 0
            performance <- None

        match lastPresentation with
        | Some previous ->
            let interval = clock.Elapsed.TotalMilliseconds - previous

            ViewerDiagnostics.record
                { FrameIndex = int64 frameCount
                  Width = int width
                  Height = int height
                  Quality = "original"
                  FrameIntervalMs = interval
                  CpuFrameMs = interval
                  GpuTimingAvailable = false
                  GpuTotalMs = None
                  Passes = [||] }
        | None -> ()

        lastPresentation <- Some now
        frameCount <- frameCount + 1

        if frameCount = 1 then
            Trace.WriteLine(
                $"Presented the first live FootballEngine stadium frame after {startup.Elapsed.TotalMilliseconds:F1} ms."
            )
