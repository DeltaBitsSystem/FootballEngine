namespace FootballEngine.Benchmarks

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Reflection
open System.Text
open System.Text.Json
open FootballEngine.Core.Match

module Program =

    [<Literal>]
    let private reportSchemaVersion = 1

    type private Options =
        { Repetitions: int
          Warmups: int
          Seeds: uint64[] option
          OutputDirectory: string
          Scenario: string option
          Profile: bool
          ProfileWorker: bool
          Verify: bool
          TraceTool: string }

    type private Scenario =
        { Name: string
          Mode: Simulator.RunMode }

    type Sample =
        { Seed: uint64
          Milliseconds: float
          AllocatedBytes: int64
          Gen0: int
          Gen1: int
          Gen2: int
          Events: int
          Ticks: int
          ReplayBytes: int64
          EventsHash: uint64 }

    type ScenarioReport =
        { Name: string
          Mode: string
          Repetitions: int
          Samples: Sample[]
          MedianMilliseconds: float
          P95Milliseconds: float
          MedianAllocatedMegabytes: float
          P95AllocatedMegabytes: float
          AverageGen0: float
          AverageGen1: float
          AverageGen2: float
          AverageEvents: float
          AverageTicks: float
          ReplayBytes: int64
          Deterministic: bool
          DistinctEventHashes: int }

    type TraceReport =
        { Status: string
          NettracePath: string
          SpeedscopePath: string
          SystemProfilePath: string
          Error: string }

    type BenchmarkReport =
        { SchemaVersion: int
          GeneratedUtc: string
          GitRevision: string
          Cpu: string
          LogicalProcessors: int
          OperatingSystem: string
          Runtime: string
          Configuration: string
          Warmups: int
          Repetitions: int
          OutputDirectory: string
          Scenarios: ScenarioReport[]
          Trace: TraceReport }

    type private SystemTiming =
        { Name: string
          mutable Calls: int64
          mutable ElapsedTicks: int64
          mutable AllocatedBytes: int64 }

    type SystemProfileRow =
        { Name: string
          Calls: int64
          TotalMilliseconds: float
          AverageMicroseconds: float
          ShareOfSystemTimePercent: float
          AllocatedBytesCurrentThread: int64 }

    type SystemProfileReport =
        { SchemaVersion: int
          GeneratedUtc: string
          Scenario: string
          Seed: uint64
          SimulatorMilliseconds: float
          AllocatedBytesCurrentThread: int64
          AllocatedBytesAllThreads: int64
          AllocatedBytesInSystems: int64
          AllocatedBytesOutsideSystems: int64
          ManagedBytesBefore: int64
          ManagedBytesAfter: int64
          ManagedBytesAfterFullCollection: int64
          ProcessPeakWorkingSetBytes: int64
          FinalTick: int
          EventsHash: uint64
          ExportProbeMilliseconds: float
          ExportProbeAllocatedBytes: int64
          ExportProbeCharacters: int
          ExportProbeIsEmptyObject: bool
          Gen0Collections: int
          Gen1Collections: int
          Gen2Collections: int
          GcPauseMilliseconds: float
          Systems: SystemProfileRow[] }

    type private OutcomeFingerprint =
        { EventsHash: uint64
          Events: MatchEvent list
          State: MatchState
          HomePosX: float32[]
          HomePosY: float32[]
          AwayPosX: float32[]
          AwayPosY: float32[]
          HomeStats: TeamStats
          AwayStats: TeamStats }

    type private SystemProfiler() =
        let timings = Dictionary<string, SystemTiming>(StringComparer.Ordinal)

        member _.Wrap(systems: SystemSpec list) : SystemSpec list =
            systems
            |> List.map (fun spec ->
                let timing =
                    { Name = spec.Name
                      Calls = 0L
                      ElapsedTicks = 0L
                      AllocatedBytes = 0L }

                timings.Add(spec.Name, timing)

                { spec with
                    Step =
                        fun context rng world buffer ->
                            let started = Stopwatch.GetTimestamp()
                            let allocatedBefore = GC.GetAllocatedBytesForCurrentThread()

                            try
                                spec.Step context rng world buffer
                            finally
                                timing.Calls <- timing.Calls + 1L
                                timing.ElapsedTicks <- timing.ElapsedTicks + (Stopwatch.GetTimestamp() - started)

                                timing.AllocatedBytes <-
                                    timing.AllocatedBytes
                                    + (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) })

        member _.Rows() : SystemProfileRow[] =
            let systemTicks = timings.Values |> Seq.sumBy (fun timing -> timing.ElapsedTicks)

            let toMilliseconds ticks =
                float ticks * 1000.0 / float Stopwatch.Frequency

            timings.Values
            |> Seq.map (fun timing ->
                let totalMilliseconds = toMilliseconds timing.ElapsedTicks

                { Name = timing.Name
                  Calls = timing.Calls
                  TotalMilliseconds = totalMilliseconds
                  AverageMicroseconds =
                    if timing.Calls = 0L then
                        0.0
                    else
                        totalMilliseconds * 1000.0 / float timing.Calls
                  ShareOfSystemTimePercent =
                    if systemTicks = 0L then
                        0.0
                    else
                        float timing.ElapsedTicks * 100.0 / float systemTicks
                  AllocatedBytesCurrentThread = timing.AllocatedBytes })
            |> Seq.sortByDescending _.TotalMilliseconds
            |> Array.ofSeq

    let private allScenarios =
        [ { Name = "fast"
            Mode = Simulator.RunMode.Fast }
          { Name = "replay-20-ticks"
            Mode = Simulator.RunMode.Replay 20<tickDelta> }
          { Name = "live-policy"
            Mode = Simulator.RunMode.Live LiveExecutionPolicy.defaultPolicy } ]

    let private modeName (mode: Simulator.RunMode) =
        match mode with
        | Simulator.RunMode.Fast -> "fast"
        | Simulator.RunMode.Replay interval -> sprintf "replay-%d-ticks" (int interval)
        | Simulator.RunMode.Live policy ->
            sprintf "live-worker-%d-pose-%d" (int policy.WorkerSlice) (int policy.PoseEvery)

    let private percentile (p: float) (values: float[]) =
        let ordered = Array.copy values
        Array.sortInPlace ordered
        let index = int (Math.Ceiling(p * float ordered.Length)) - 1
        ordered.[max 0 (min (ordered.Length - 1) index)]

    let private gitRevision () =
        try
            use gitProcess = new Process()

            gitProcess.StartInfo <-
                ProcessStartInfo(
                    FileName = "git",
                    Arguments = "rev-parse --short HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                )

            gitProcess.Start() |> ignore
            let output = gitProcess.StandardOutput.ReadToEnd().Trim()
            gitProcess.WaitForExit()

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

    let private defaultOutputDirectory () =
        let stamp =
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)

        Path.Combine("artifacts", "benchmarks", stamp)

    let private parsePositive (name: string) (value: string) =
        match Int32.TryParse value with
        | true, parsed when parsed > 0 -> parsed
        | _ -> invalidArg name (sprintf "%s must be a positive integer" name)

    let private parseNonNegative (name: string) (value: string) =
        match Int32.TryParse value with
        | true, parsed when parsed >= 0 -> parsed
        | _ -> invalidArg name (sprintf "%s must be a non-negative integer" name)

    let private parseSeeds (value: string) =
        let seeds =
            value.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.map (fun item ->
                match UInt64.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture) with
                | true, seed -> seed
                | _ -> invalidArg "--seeds" (sprintf "invalid unsigned seed: %s" item))

        if seeds.Length = 0 || (seeds |> Array.distinct).Length <> seeds.Length then
            invalidArg "--seeds" "provide at least one unique comma-separated seed"

        seeds

    let private nextValue (argv: string[]) (index: int) (name: string) : string =
        if index + 1 >= argv.Length then
            invalidArg name (sprintf "missing value for %s" name)

        argv.[index + 1]

    let private parseOptions (argv: string[]) =
        let mutable repetitions = 7
        let mutable warmups = 2
        let mutable seeds = None
        let mutable outputDirectory = defaultOutputDirectory ()
        let mutable scenario = None
        let mutable profile = false
        let mutable profileWorker = false
        let mutable verify = true
        let mutable traceTool = "dotnet-trace"
        let mutable help = false
        let mutable index = 0

        while index < argv.Length do
            match argv.[index] with
            | "--help"
            | "-h" ->
                help <- true
                index <- index + 1
            | "--repetitions" ->
                repetitions <- parsePositive "--repetitions" (nextValue argv index "--repetitions")
                index <- index + 2
            | "--warmups" ->
                warmups <- parseNonNegative "--warmups" (nextValue argv index "--warmups")
                index <- index + 2
            | "--seeds" ->
                seeds <- Some(parseSeeds (nextValue argv index "--seeds"))
                index <- index + 2
            | "--output" ->
                outputDirectory <- nextValue argv index "--output"
                index <- index + 2
            | "--scenario" ->
                scenario <- Some(nextValue argv index "--scenario")
                index <- index + 2
            | "--profile" ->
                profile <- true
                index <- index + 1
            | "--profile-worker" ->
                profileWorker <- true
                index <- index + 1
            | "--no-verify" ->
                verify <- false
                index <- index + 1
            | "--trace-tool" ->
                traceTool <- nextValue argv index "--trace-tool"
                index <- index + 2
            | value -> invalidArg "argv" (sprintf "unknown argument: %s" value)

        if help then
            None
        else
            Some
                { Repetitions = repetitions
                  Warmups = warmups
                  Seeds = seeds
                  OutputDirectory = Path.GetFullPath outputDirectory
                  Scenario = scenario
                  Profile = profile
                  ProfileWorker = profileWorker
                  Verify = verify
                  TraceTool = traceTool }

    let private printHelp () =
        printfn "FootballEngine benchmark and profiling runner"
        printfn ""
        printfn "  --profile                 Run benchmarks and capture CPU/GC trace + system timings"
        printfn "  --scenario fast|replay-20-ticks|full"
        printfn "  --repetitions N           Timed samples per scenario (default: 7)"
        printfn "  --warmups N               Warmup samples per scenario (default: 2)"
        printfn "  --seeds N,...             Repeat each explicit seed; warm up each seed first"
        printfn "  --output DIRECTORY        Artifact directory"
        printfn "  --no-verify               Skip same-seed determinism verification"
        printfn "  --trace-tool PATH         dotnet-trace executable (default: dotnet-trace)"

    let private scenariosFor (options: Options) : Scenario list =
        match options.Scenario with
        | None -> allScenarios
        | Some name ->
            match allScenarios |> List.tryFind (fun scenario -> scenario.Name = name) with
            | Some scenario -> [ scenario ]
            | None -> invalidArg "--scenario" (sprintf "unknown scenario: %s" name)

    let private profileScenarioName (options: Options) =
        options.Scenario |> Option.defaultValue "fast"

    let private fingerprint (outcome: MatchOutcome) : OutcomeFingerprint =
        { EventsHash = outcome.EventsHash
          Events = outcome.Events
          State = outcome.Final.State
          HomePosX = outcome.Final.Home.Frame.PosX
          HomePosY = outcome.Final.Home.Frame.PosY
          AwayPosX = outcome.Final.Away.Frame.PosX
          AwayPosY = outcome.Final.Away.Frame.PosY
          HomeStats = outcome.Final.Home.Stats
          AwayStats = outcome.Final.Away.Stats }

    let private verifyDeterminism (systems: SystemSpec list) (ctx: MatchContext) (scenario: Scenario) (seed: uint64) =
        let requireOutcome =
            function
            | Ok outcome -> outcome
            | Error error -> failwithf "benchmark match failed: %A" error

        let first = Simulator.runMatch systems ctx seed scenario.Mode |> requireOutcome
        let second = Simulator.runMatch systems ctx seed scenario.Mode |> requireOutcome
        fingerprint first = fingerprint second

    let private collectSample
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (scenario: Scenario)
        (seed: uint64)
        : Sample =
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()

        let allocatedBefore = GC.GetTotalAllocatedBytes(true)
        let gen0Before = GC.CollectionCount(0)
        let gen1Before = GC.CollectionCount(1)
        let gen2Before = GC.CollectionCount(2)
        let stopwatch = Stopwatch.StartNew()

        let outcome =
            match Simulator.runMatch systems ctx seed scenario.Mode with
            | Ok outcome -> outcome
            | Error error -> failwithf "benchmark match failed: %A" error

        stopwatch.Stop()

        { Seed = seed
          Milliseconds = stopwatch.Elapsed.TotalMilliseconds
          AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore
          Gen0 = GC.CollectionCount(0) - gen0Before
          Gen1 = GC.CollectionCount(1) - gen1Before
          Gen2 = GC.CollectionCount(2) - gen2Before
          Events = outcome.Events.Length
          Ticks = int outcome.Final.State.Tick
          ReplayBytes =
            outcome.Replay
            |> Option.map (fun replay -> replay.Snapshots.LongLength)
            |> Option.defaultValue 0L
          EventsHash = outcome.EventsHash }

    let private runScenario
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (options: Options)
        (scenario: Scenario)
        : ScenarioReport =
        match options.Seeds with
        | Some seeds when options.Warmups > 0 ->
            for seed in seeds do
                for _ = 1 to options.Warmups do
                    match Simulator.runMatch systems ctx seed scenario.Mode with
                    | Ok _ -> ()
                    | Error error -> failwithf "benchmark warmup failed: %A" error
        | None when options.Warmups > 0 ->
            for warmup in 0 .. options.Warmups - 1 do
                match Simulator.runMatch systems ctx (0xD00D0000UL + uint64 warmup) scenario.Mode with
                | Ok _ -> ()
                | Error error -> failwithf "benchmark warmup failed: %A" error
        | _ -> ()

        let samples =
            match options.Seeds with
            | Some seeds ->
                [| for seed in seeds do
                       for _ = 1 to options.Repetitions do
                           yield collectSample systems ctx scenario seed |]
            | None ->
                [| for i in 0 .. options.Repetitions - 1 ->
                       collectSample systems ctx scenario (0x1000UL + uint64 i) |]

        let deterministic =
            if options.Verify then
                samples
                |> Array.map _.Seed
                |> Array.distinct
                |> Array.forall (fun seed -> verifyDeterminism systems ctx scenario seed)
            else
                false

        let times = samples |> Array.map _.Milliseconds

        let allocations =
            samples |> Array.map (fun sample -> float sample.AllocatedBytes / 1_048_576.0)

        let hashes = samples |> Array.map _.EventsHash |> Array.distinct

        { Name = scenario.Name
          Mode = modeName scenario.Mode
          Repetitions = samples.Length
          Samples = samples
          MedianMilliseconds = percentile 0.50 times
          P95Milliseconds = percentile 0.95 times
          MedianAllocatedMegabytes = percentile 0.50 allocations
          P95AllocatedMegabytes = percentile 0.95 allocations
          AverageGen0 = samples |> Array.averageBy (fun sample -> float sample.Gen0)
          AverageGen1 = samples |> Array.averageBy (fun sample -> float sample.Gen1)
          AverageGen2 = samples |> Array.averageBy (fun sample -> float sample.Gen2)
          AverageEvents = samples |> Array.averageBy (fun sample -> float sample.Events)
          AverageTicks = samples |> Array.averageBy (fun sample -> float sample.Ticks)
          ReplayBytes = samples.[0].ReplayBytes
          Deterministic = deterministic
          DistinctEventHashes = hashes.Length }

    let private markdownReport (report: BenchmarkReport) =
        let builder = StringBuilder()
        builder.AppendLine("# FootballEngine benchmark") |> ignore
        builder.AppendLine() |> ignore
        builder.AppendLine(sprintf "Generated: `%s`" report.GeneratedUtc) |> ignore
        builder.AppendLine(sprintf "Revision: `%s`" report.GitRevision) |> ignore
        builder.AppendLine(sprintf "CPU: `%s`" report.Cpu) |> ignore
        builder.AppendLine(sprintf "Runtime: `%s`" report.Runtime) |> ignore
        builder.AppendLine() |> ignore

        builder.AppendLine("| Scenario | Median ms | P95 ms | Median alloc MB | Events | Ticks | Deterministic |")
        |> ignore

        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |") |> ignore

        report.Scenarios
        |> Array.iter (fun scenario ->
            builder.AppendLine(
                sprintf
                    "| %s | %.3f | %.3f | %.3f | %.0f | %.0f | %b |"
                    scenario.Name
                    scenario.MedianMilliseconds
                    scenario.P95Milliseconds
                    scenario.MedianAllocatedMegabytes
                    scenario.AverageEvents
                    scenario.AverageTicks
                    scenario.Deterministic
            )
            |> ignore)

        builder.AppendLine() |> ignore
        builder.AppendLine(sprintf "Trace status: `%s`" report.Trace.Status) |> ignore
        builder.ToString()

    let private writeJson (path: string) (value: 'a) =
        let options = JsonSerializerOptions(WriteIndented = true)
        File.WriteAllText(path, JsonSerializer.Serialize(value, options))

    let private writeBenchmarkReport (report: BenchmarkReport) =
        Directory.CreateDirectory(report.OutputDirectory) |> ignore
        writeJson (Path.Combine(report.OutputDirectory, "benchmark.json")) report
        File.WriteAllText(Path.Combine(report.OutputDirectory, "benchmark.md"), markdownReport report)

    let private printScenario (scenario: ScenarioReport) =
        printfn
            "scenario=%s reps=%d medianMs=%.3f p95Ms=%.3f medianAllocMb=%.3f p95AllocMb=%.3f avgGen0=%.2f avgGen1=%.2f avgGen2=%.2f avgEvents=%.0f avgTicks=%.0f replayBytes=%d deterministic=%b distinctEventHashes=%d"
            scenario.Name
            scenario.Repetitions
            scenario.MedianMilliseconds
            scenario.P95Milliseconds
            scenario.MedianAllocatedMegabytes
            scenario.P95AllocatedMegabytes
            scenario.AverageGen0
            scenario.AverageGen1
            scenario.AverageGen2
            scenario.AverageEvents
            scenario.AverageTicks
            scenario.ReplayBytes
            scenario.Deterministic
            scenario.DistinctEventHashes

    let private runBenchmark (options: Options) : BenchmarkReport =
        let ctx = BenchContext.create ()
        let systems = Scheduler.systems

        let reports =
            scenariosFor options
            |> List.map (runScenario systems ctx options)
            |> Array.ofList

        { SchemaVersion = reportSchemaVersion
          GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
          GitRevision = gitRevision ()
          Cpu = cpuModel ()
          LogicalProcessors = Environment.ProcessorCount
          OperatingSystem = Environment.OSVersion.VersionString
          Runtime = Environment.Version.ToString()
          Configuration = "BalanceConfig.defaultConfig"
          Warmups = options.Warmups
          Repetitions = options.Repetitions
          OutputDirectory = options.OutputDirectory
          Scenarios = reports
          Trace =
            { Status = "not-requested"
              NettracePath = ""
              SpeedscopePath = ""
              SystemProfilePath = ""
              Error = "" } }

    let private runSystemProfile (options: Options) =
        let scenario =
            scenariosFor
                { options with
                    Scenario = Some(profileScenarioName options) }
            |> List.exactlyOne

        let profiler = SystemProfiler()
        let systems = profiler.Wrap Scheduler.systems
        let ctx = BenchContext.create ()
        let seed = 0xFACEUL
        let managedBefore = GC.GetTotalMemory(false)
        let collectionsBefore = Array.init 3 GC.CollectionCount
        let pauseBefore = GC.GetTotalPauseDuration()
        let allThreadsBefore = GC.GetTotalAllocatedBytes(true)
        let allocatedBefore = GC.GetAllocatedBytesForCurrentThread()
        let stopwatch = Stopwatch.StartNew()

        let outcome =
            match Simulator.runMatch systems ctx seed scenario.Mode with
            | Ok outcome -> outcome
            | Error error -> failwithf "benchmark profile failed: %A" error

        stopwatch.Stop()
        let allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore
        let allThreadsAllocated = GC.GetTotalAllocatedBytes(true) - allThreadsBefore
        let managedAfter = GC.GetTotalMemory(false)
        let pause = GC.GetTotalPauseDuration() - pauseBefore
        let collectionsAfter = Array.init 3 GC.CollectionCount
        use currentProcess = Process.GetCurrentProcess()
        let peakWorkingSet = currentProcess.PeakWorkingSet64
        // Collection is outside the timed region; this is live managed heap, not RSS or total allocated bytes.
        let managedAfterCollection = GC.GetTotalMemory(true)
        let exportAllocatedBefore = GC.GetAllocatedBytesForCurrentThread()
        let exportTimer = Stopwatch.StartNew()

        let exported =
            MatchDump.export ctx outcome.Events outcome.Final.Home.Stats outcome.Final.Away.Stats
            |> function
                | Ok json -> json
                | Error error -> failwithf "benchmark export failed: %s" error

        exportTimer.Stop()
        let exportAllocated = GC.GetAllocatedBytesForCurrentThread() - exportAllocatedBefore
        let rows = profiler.Rows()
        let systemAllocated = rows |> Array.sumBy _.AllocatedBytesCurrentThread

        let report =
            { SchemaVersion = 2
              GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
              Scenario = scenario.Name
              Seed = seed
              SimulatorMilliseconds = stopwatch.Elapsed.TotalMilliseconds
              AllocatedBytesCurrentThread = allocated
              AllocatedBytesAllThreads = allThreadsAllocated
              AllocatedBytesInSystems = systemAllocated
              AllocatedBytesOutsideSystems = allocated - systemAllocated
              ManagedBytesBefore = managedBefore
              ManagedBytesAfter = managedAfter
              ManagedBytesAfterFullCollection = managedAfterCollection
              ProcessPeakWorkingSetBytes = peakWorkingSet
              FinalTick = int outcome.Final.State.Tick
              EventsHash = outcome.EventsHash
              ExportProbeMilliseconds = exportTimer.Elapsed.TotalMilliseconds
              ExportProbeAllocatedBytes = exportAllocated
              ExportProbeCharacters = exported.Length
              ExportProbeIsEmptyObject = exported = "{}"
              Gen0Collections = collectionsAfter[0] - collectionsBefore[0]
              Gen1Collections = collectionsAfter[1] - collectionsBefore[1]
              Gen2Collections = collectionsAfter[2] - collectionsBefore[2]
              GcPauseMilliseconds = pause.TotalMilliseconds
              Systems = rows }

        // The post-run heap sample must still include the result owned by the caller.
        GC.KeepAlive outcome

        writeJson (Path.Combine(options.OutputDirectory, "system-profile.json")) report

        printfn
            "system-profile=%s simulatorMs=%.3f"
            (Path.Combine(options.OutputDirectory, "system-profile.json"))
            report.SimulatorMilliseconds

    let private runExternalProfile (options: Options) : TraceReport =
        let nettracePath = Path.Combine(options.OutputDirectory, "cpu.nettrace")
        let speedscopePath = Path.Combine(options.OutputDirectory, "cpu.speedscope.json")
        let systemProfilePath = Path.Combine(options.OutputDirectory, "system-profile.json")

        try
            let processPath = Environment.ProcessPath
            let assemblyPath = Assembly.GetExecutingAssembly().Location
            let processName = Path.GetFileNameWithoutExtension processPath
            let useDotnetHost = processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)

            let trace =
                ProcessStartInfo(FileName = options.TraceTool, UseShellExecute = false, CreateNoWindow = true)

            trace.ArgumentList.Add("collect")
            trace.ArgumentList.Add("--profile")
            trace.ArgumentList.Add("gc-verbose")
            // The installed dotnet-trace version does not include CPU samples in dotnet-common.
            trace.ArgumentList.Add("--providers")
            trace.ArgumentList.Add("Microsoft-DotNETCore-SampleProfiler")
            trace.ArgumentList.Add("--format")
            trace.ArgumentList.Add("Speedscope")
            trace.ArgumentList.Add("--output")
            trace.ArgumentList.Add(nettracePath)
            trace.ArgumentList.Add("--")

            if useDotnetHost then
                trace.ArgumentList.Add(processPath)
                trace.ArgumentList.Add(assemblyPath)
            else
                trace.ArgumentList.Add(processPath)

            trace.ArgumentList.Add("--profile-worker")
            trace.ArgumentList.Add("--output")
            trace.ArgumentList.Add(options.OutputDirectory)
            trace.ArgumentList.Add("--scenario")
            trace.ArgumentList.Add(profileScenarioName options)

            use traceProcess = Process.Start trace
            traceProcess.WaitForExit()

            let filesReady =
                File.Exists nettracePath
                && File.Exists speedscopePath
                && File.Exists systemProfilePath

            let status =
                if traceProcess.ExitCode = 0 && filesReady then "completed"
                elif traceProcess.ExitCode = 0 then "incomplete"
                else "failed"

            { Status = status
              NettracePath = nettracePath
              SpeedscopePath = speedscopePath
              SystemProfilePath = systemProfilePath
              Error =
                if traceProcess.ExitCode = 0 then
                    ""
                else
                    sprintf "dotnet-trace exit code %d" traceProcess.ExitCode }
        with ex ->
            { Status = "unavailable"
              NettracePath = nettracePath
              SpeedscopePath = speedscopePath
              SystemProfilePath = systemProfilePath
              Error = ex.Message }

    [<EntryPoint>]
    let main argv =
        match parseOptions argv with
        | None ->
            printHelp ()
            0
        | Some options ->
            Directory.CreateDirectory(options.OutputDirectory) |> ignore

            if options.ProfileWorker then
                runSystemProfile options
                0
            else
                let report = runBenchmark options
                report.Scenarios |> Array.iter printScenario

                let report =
                    if options.Profile then
                        let trace = runExternalProfile options
                        { report with Trace = trace }
                    else
                        report

                writeBenchmarkReport report
                printfn "report-json=%s" (Path.Combine(options.OutputDirectory, "benchmark.json"))
                printfn "report-markdown=%s" (Path.Combine(options.OutputDirectory, "benchmark.md"))
                printfn "trace-status=%s" report.Trace.Status

                if options.Profile && report.Trace.Status <> "completed" then
                    2
                else
                    0
