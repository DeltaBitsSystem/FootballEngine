namespace FootballEngine.Benchmarks

open System
open System.Diagnostics
open System.IO
open FootballEngine.Core.Match

module Program =

    type private Sample =
        { Milliseconds: float
          AllocatedBytes: int64
          Gen0: int
          Gen1: int
          Gen2: int
          Events: int
          Ticks: int
          ReplayBytes: int
          EventsHash: uint64 }

    type private Scenario =
        { Name: string
          Mode: Simulator.RunMode }

    let private percentile (p: float) (values: float list) =
        let ordered = values |> List.sort
        let index = int (Math.Ceiling(p * float ordered.Length)) - 1
        ordered.[max 0 (min (ordered.Length - 1) index)]

    let private gitRevision () =
        try
            use proc = new Process()

            proc.StartInfo <-
                ProcessStartInfo(
                    FileName = "git",
                    Arguments = "rev-parse --short HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                )

            proc.Start() |> ignore
            let output = proc.StandardOutput.ReadToEnd().Trim()
            proc.WaitForExit()

            if String.IsNullOrWhiteSpace output then
                "unknown"
            else
                output
        with _ ->
            "unknown"

    let private cpuModel () =
        try
            File.ReadLines("/proc/cpuinfo")
            |> Seq.tryFind (fun line -> line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun line -> line.Substring(line.IndexOf(':') + 1).Trim())
            |> Option.defaultValue "unknown"
        with _ ->
            "unknown"

    let private runSample (systems: SystemSpec list) (ctx: MatchContext) (seed: uint64) mode =
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()

        let allocatedBefore = GC.GetAllocatedBytesForCurrentThread()
        let gen0Before = GC.CollectionCount(0)
        let gen1Before = GC.CollectionCount(1)
        let gen2Before = GC.CollectionCount(2)
        let stopwatch = Stopwatch.StartNew()
        let outcome = Simulator.runMatch systems ctx seed mode None
        stopwatch.Stop()

        { Milliseconds = stopwatch.Elapsed.TotalMilliseconds
          AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore
          Gen0 = GC.CollectionCount(0) - gen0Before
          Gen1 = GC.CollectionCount(1) - gen1Before
          Gen2 = GC.CollectionCount(2) - gen2Before
          Events = outcome.Events.Length
          Ticks = int outcome.Final.State.Tick
          ReplayBytes =
            outcome.Replay
            |> Option.map (fun replay -> replay.Snapshots.Length)
            |> Option.defaultValue 0
          EventsHash = outcome.EventsHash }

    let private reportScenario scenario samples =
        let times = samples |> List.map _.Milliseconds

        let allocations =
            samples |> List.map (fun s -> float s.AllocatedBytes / 1_048_576.0)

        let first = samples.Head
        let hashes = samples |> List.map _.EventsHash |> List.distinct
        let average value = samples |> List.averageBy value

        printfn
            "scenario=%s reps=%d medianMs=%.3f p95Ms=%.3f medianAllocMb=%.3f p95AllocMb=%.3f avgGen0=%.2f avgGen1=%.2f avgGen2=%.2f avgEvents=%.0f avgTicks=%.0f replayBytes=%d deterministicHashes=%d"
            scenario.Name
            samples.Length
            (percentile 0.50 times)
            (percentile 0.95 times)
            (percentile 0.50 allocations)
            (percentile 0.95 allocations)
            (average (fun s -> float s.Gen0))
            (average (fun s -> float s.Gen1))
            (average (fun s -> float s.Gen2))
            (average (fun s -> float s.Events))
            (average (fun s -> float s.Ticks))
            first.ReplayBytes
            hashes.Length

    [<EntryPoint>]
    let main argv =
        let repetitions =
            match argv |> Array.tryFindIndex ((=) "--repetitions") with
            | Some index when index + 1 < argv.Length ->
                match Int32.TryParse argv.[index + 1] with
                | true, value when value > 0 -> value
                | _ -> 7
            | _ -> 7

        let ctx = BenchContext.create ()
        let systems = Scheduler.systems

        let scenarios =
            [ { Name = "match-fast"
                Mode = Simulator.RunMode.Fast }
              { Name = "match-replay-20-ticks"
                Mode = Simulator.RunMode.Replay 20<tickDelta> } ]

        for scenario in scenarios do
            for seed in [| 0xFEEDUL; 0xBEEFUL |] do
                Simulator.runMatch systems ctx seed scenario.Mode None |> ignore

            let samples =
                [ for i in 0 .. repetitions - 1 -> runSample systems ctx (0x1000UL + uint64 i) scenario.Mode ]

            reportScenario scenario samples

        printfn
            "metadata=cpu:%s logicalCpus:%d os:%s runtime:%s commit:%s config:BalanceConfig.defaultConfig warmup:2 seeds:0x1000..0x%X"
            (cpuModel ())
            Environment.ProcessorCount
            (Environment.OSVersion.VersionString)
            (Environment.Version.ToString())
            (gitRevision ())
            (0x1000 + repetitions - 1)

        0
