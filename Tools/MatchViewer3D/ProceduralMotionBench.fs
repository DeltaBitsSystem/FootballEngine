namespace FootballEngine.MatchViewer3D.Probe

open System
open System.Diagnostics
open System.IO
open System.Numerics
open System.Threading
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Client.Animation
open FootballEngine.Client.Match3D

module ProceduralMotionBench =
    type Measurement =
        { Samples: int
          MedianMilliseconds: float
          P95Milliseconds: float
          P99Milliseconds: float
          MaxMilliseconds: float
          AllocatedBytes: int64
          Gen0: int
          Gen1: int
          Gen2: int }

    let private require =
        function
        | Ok value -> value
        | Error error -> failwithf "motion benchmark failed: %A" error

    let private measure iterations action =
        let timings = Array.zeroCreate<float> iterations
        let collections = Array.init 3 GC.CollectionCount
        let allocated = GC.GetAllocatedBytesForCurrentThread()

        for index = 0 to iterations - 1 do
            let start = Stopwatch.GetTimestamp()
            action index
            timings[index] <- Stopwatch.GetElapsedTime(start).TotalMilliseconds

        let bytes = GC.GetAllocatedBytesForCurrentThread() - allocated
        Array.sortInPlace timings

        let percentile fraction =
            timings[max 0 (int (ceil (fraction * float iterations)) - 1)]

        { Samples = iterations
          MedianMilliseconds = percentile 0.5
          P95Milliseconds = percentile 0.95
          P99Milliseconds = percentile 0.99
          MaxMilliseconds = Array.last timings
          AllocatedBytes = bytes
          Gen0 = GC.CollectionCount(0) - collections[0]
          Gen1 = GC.CollectionCount(1) - collections[1]
          Gen2 = GC.CollectionCount(2) - collections[2] }

    let run outputPath =
        let config = AnimationConfig.defaultConfig

        let character =
            CharacterAsset.load (Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb"))
            |> require

        let rig = character.Rig

        let identity =
            { PlannerVersion = 1
              ConfigHash = MotionIdentity.configHash config
              RigHash = rig.Hash
              ProfileHash = "headless-synthetic-standard-morphology"
              TrackId = 17UL }

        let samples =
            Array.init 2305 (fun tick ->
                let phase = tick % 320

                let speed =
                    if phase < 80 then float32 phase * 0.05f
                    elif phase < 200 then 4.0f
                    elif phase < 280 then float32 (280 - phase) * 0.05f
                    else 0.0f

                { Tick = tick * 1<subtick>
                  Roots =
                    Array.init 22 (fun actor ->
                        { Actor = PlayerId(actor + 1)
                          Generation = 1
                          Position = Vector3(float32 tick * config.TickSeconds * 2.0f, float32 actor * 2.0f, 0.0f)
                          Velocity = Vector3(speed, 0.0f, 0.0f)
                          Facing = float32 (actor % 4) * MathF.PI * 0.5f + float32 (max 0 (phase - 280)) * 0.025f
                          AngularVelocity = if phase >= 280 then 1.0f else 0.0f
                          Condition = 1.0f - float32 actor * 0.02f
                          AttentionTarget = ValueSome(Vector3(40.0f, 4.0f, 0.11f))
                          Morphology = PhysicalMorphology.standard
                          Active = true })
                  Evidence = [||] })

        let scratch = PoseScratch rig
        let output = Array.create (22 * rig.JointNodes.Length) Matrix4x4.Identity
        let initialPlan = SupportPlanner.initialize config 0 samples[0].Roots[0]

        let evaluate (runtime: MotionRuntime) tick =
            let time = { Tick = tick; Fraction = 0.5f }

            for actor = 0 to 21 do
                let mutable plan = initialPlan

                if not ((runtime :> IMotionPlanSource).TryCopyPlan(PlayerId(actor + 1), time, &plan)) then
                    failwith "benchmark plan is outside its retained frontier"

                PoseEvaluation.evaluateInto (
                    config,
                    rig,
                    plan,
                    time,
                    MotionInput.vector plan.Root.Position,
                    MotionInput.facing plan.Root.Facing,
                    scratch,
                    output,
                    actor * rig.JointNodes.Length
                )
                |> ignore

        let warm = MotionRuntime(config, identity)

        for tick = 0 to 256 do
            warm.Ingest samples[tick] |> require

        for _ = 1 to 1000 do
            evaluate warm 200

        let allocated = GC.GetAllocatedBytesForCurrentThread()
        let runtime = MotionRuntime(config, identity)
        let stateBytes = GC.GetAllocatedBytesForCurrentThread() - allocated
        let allocated = GC.GetAllocatedBytesForCurrentThread()
        let historical = MotionRuntime(config, identity, historical = true)
        let historyBytes = GC.GetAllocatedBytesForCurrentThread() - allocated
        GC.KeepAlive historical

        for tick = 0 to 256 do
            runtime.Ingest samples[tick] |> require

        let ingest =
            measure 2048 (fun index -> runtime.Ingest samples[index + 257] |> require)

        let pose = measure 4096 (fun index -> evaluate runtime (2177 + index % 128))

        let combined = MotionRuntime(config, identity)

        for tick = 0 to 256 do
            combined.Ingest samples[tick] |> require

        let endToEnd =
            measure 2048 (fun index ->
                let tick = index + 257
                combined.Ingest samples[tick] |> require
                evaluate combined tick)

        use track = new MotionTrack.Writer()
        let archive = MotionCheckpointArchive.Writer()
        let captured = MotionRuntime(config, identity)

        for sample in samples do
            track.Append sample |> require
            captured.Ingest sample |> require

            if int sample.Tick % MotionTrack.checkpointInterval = 0 then
                archive.Append(captured.Checkpoint()) |> require

        let trackBytes = track.Complete()
        let checkpointBytes = archive.Complete()

        let seek =
            measure 64 (fun index ->
                let tick = 127 + (index % 17) * 128

                let checkpoint =
                    MotionCheckpointArchive.preceding config identity checkpointBytes tick
                    |> require

                let rebuilt =
                    MotionReconstruction.rebuild
                        config
                        identity
                        checkpoint
                        tick
                        (MotionTrack.readBlock trackBytes)
                        CancellationToken.None
                    |> require

                evaluate rebuilt tick)

        let report =
            {| SchemaVersion = 1
               GeneratedUtc = DateTime.UtcNow
               Runtime = Environment.Version.ToString()
               Cpu =
                File.ReadLines("/proc/cpuinfo")
                |> Seq.find (fun line -> line.StartsWith("model name"))
               Configuration = "Release, synthetic sporting roots; no artistic/contact-quality approval"
               Actors = 22
               JointsPerActor = rig.JointNodes.Length
               WarmupBoundaries = 257
               WarmupPoses = 1000
               Identity = identity
               RuntimeConstructionBytes = stateBytes
               HistoryConstructionBytes = historyBytes
               TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")
               MotionTrackBytes = trackBytes.Length
               CheckpointArchiveBytes = checkpointBytes.Length
               Ingest = ingest
               Pose = pose
               EndToEnd = endToEnd
               Seek127Boundaries = seek |}

        let json =
            JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true))

        let path = Path.GetFullPath outputPath
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllText(path, json)
        Console.WriteLine(json)
        Console.WriteLine($"report={path}")
        0
