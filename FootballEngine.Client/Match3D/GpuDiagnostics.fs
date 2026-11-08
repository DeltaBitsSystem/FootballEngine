namespace FootballEngine.Client.Match3D

open System
open System.Threading

/// Timing of one named renderer pass; a pass that records several GPU passes reports their sum.
type PassTiming =
    {
        Name: string
        /// None when the adapter has no timestamp queries or the readback has not arrived yet.
        GpuMs: float option
        CpuMs: float
    }

type ViewerFrameStats =
    {
        FrameIndex: int64
        Width: int
        Height: int
        Quality: string
        /// Interval between presented frames, including pacing; not a GPU measurement.
        FrameIntervalMs: float
        CpuFrameMs: float
        GpuTimingAvailable: bool
        GpuTotalMs: float option
        Passes: PassTiming array
    }

type AveragedPass =
    { Name: string
      GpuMs: float option
      CpuMs: float }

type AveragedStats =
    { Frames: int
      Fps: float
      FrameIntervalMs: float
      CpuFrameMs: float
      GpuTimingAvailable: bool
      GpuTotalMs: float option
      Passes: AveragedPass array }

/// What the adapter exposed at startup; recorded so a missing optional feature is visible instead of silently ignored.
type AdapterReport =
    {
        AdapterName: string
        Backend: string
        TimestampQuery: bool
        TextureCompressionBC: bool
        IndirectFirstInstance: bool
        Float32Filterable: bool
        /// Nanoseconds per timestamp tick, when it could be determined for this adapter.
        TimestampPeriodNs: float option
    }

/// Thread-safe accumulator used by harnesses to average presented frames over an explicit window.
[<Sealed>]
type FrameStatsAccumulator() =
    let gate = obj ()
    let samples = ResizeArray<ViewerFrameStats>()

    member _.Add(stats: ViewerFrameStats) = lock gate (fun () -> samples.Add stats)

    member _.Reset() = lock gate (fun () -> samples.Clear())

    member _.Count = lock gate (fun () -> samples.Count)

    member _.Average() : AveragedStats option =
        let copy = lock gate (fun () -> samples.ToArray())

        if copy.Length = 0 then
            None
        else
            let mean (selector: ViewerFrameStats -> float) = copy |> Array.averageBy selector
            let intervalMs = mean _.FrameIntervalMs
            let timed = copy |> Array.filter _.GpuTimingAvailable

            let names =
                copy
                |> Array.collect (fun frame -> frame.Passes |> Array.map _.Name)
                |> Array.distinct

            let passes =
                names
                |> Array.map (fun name ->
                    let pass (frame: ViewerFrameStats) =
                        frame.Passes |> Array.tryFind (fun candidate -> candidate.Name = name)

                    let cpu =
                        copy
                        |> Array.choose pass
                        |> Array.map _.CpuMs
                        |> fun values -> if values.Length = 0 then 0.0 else Array.average values

                    let gpu =
                        timed
                        |> Array.choose (fun frame -> pass frame |> Option.bind _.GpuMs)
                        |> fun values ->
                            if values.Length = 0 then
                                None
                            else
                                Some(Array.average values)

                    { Name = name
                      GpuMs = gpu
                      CpuMs = cpu })

            Some
                { Frames = copy.Length
                  Fps = if intervalMs > 0.0 then 1000.0 / intervalMs else 0.0
                  FrameIntervalMs = intervalMs
                  CpuFrameMs = mean _.CpuFrameMs
                  GpuTimingAvailable = timed.Length > 0
                  GpuTotalMs =
                    timed
                    |> Array.choose _.GpuTotalMs
                    |> fun values ->
                        if values.Length = 0 then
                            None
                        else
                            Some(Array.average values)
                  Passes = passes }

/// Process-wide window onto the renderer for harnesses and the HUD; the renderer only writes, readers never block it.
module ViewerDiagnostics =
    let private accumulator = FrameStatsAccumulator()
    let mutable private latest: ViewerFrameStats option = None
    let mutable private adapter: AdapterReport option = None
    let mutable private showPassTimings = false
    let mutable private nativeWindow = 0UL

    /// Pass breakdown in the HUD is a profiling aid and must not appear in ordinary play.
    let enableProfilingHud () = Volatile.Write(&showPassTimings, true)

    let profilingHud () =
        Volatile.Read(&showPassTimings)
        || not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable "FE_VIEWER_PROFILE"))

    let record (stats: ViewerFrameStats) =
        Volatile.Write(&latest, Some stats)
        accumulator.Add stats

    let latestStats () = Volatile.Read(&latest)

    let publishAdapter (report: AdapterReport) = Volatile.Write(&adapter, Some report)

    let adapterReport () = Volatile.Read(&adapter)

    /// X11 id of the renderer's child window, so a harness can capture the 3D frame without the surrounding UI.
    let publishNativeWindow (window: uint64) = Volatile.Write(&nativeWindow, window)

    let nativeWindowId () = Volatile.Read(&nativeWindow)

    let beginWindow () = accumulator.Reset()

    let windowFrames () = accumulator.Count

    let windowAverage () = accumulator.Average()
