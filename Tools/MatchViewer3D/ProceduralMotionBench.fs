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
            { PlannerVersion = MotionIdentity.plannerVersion
              ConfigHash = MotionIdentity.configHash config
              RigHash = rig.Hash
              ProfileHash = "headless-synthetic-standard-morphology"
              TrackId = 17UL }

        let mutable position = 0.0f
        let mutable sequence = 0UL
        let samples =
            Array.init 2305 (fun tick ->
                let phase = tick % 320

                let speed =
                    if phase < 80 then float32 phase * 0.08f
                    elif phase < 200 then 6.4f
                    elif phase < 280 then float32 (280 - phase) * 0.08f
                    else 0.0f

                if tick > 0 then position <- position + speed * config.TickSeconds

                let taskPhase = tick % 160
                let evidence =
                    if taskPhase = 60 || taskPhase = 64 then
                        Array.init 22 (fun actor ->
                            sequence <- sequence + 1UL
                            let target = Vector3(position + 0.25f, float32 actor * 2.0f, 0.11f)
                            { Sequence = sequence; TaskId = uint64 (tick / 160 * 22 + actor + 1)
                              Actor = PlayerId(actor + 1); Generation = 1; Family = MotionFamily.FootStrike
                              Effector = if actor % 2 = 0 then MotionEffector.LeftFoot else MotionEffector.RightFoot
                              Phase = if taskPhase = 60 then MotionEvidencePhase.Prepared else MotionEvidencePhase.Confirmed
                              OccurredAt = tick * 1<subtick>; AvailableAt = tick * 1<subtick>
                              StartedAt = (tick / 160 * 160 + 60) * 1<subtick>
                              CommitAt = (tick / 160 * 160 + 68) * 1<subtick>
                              EndsAt = (tick / 160 * 160 + 80) * 1<subtick>
                              PredictedPoint = target; ConfirmedPoint = if taskPhase = 64 then ValueSome target else ValueNone
                              BallCenter = target + Vector3(0.11f, 0.0f, 0.0f)
                              IncomingVelocity = ValueNone; OutgoingVelocity = ValueNone })
                    else
                        [||]

                { Tick = tick * 1<subtick>
                  Roots =
                    Array.init 22 (fun actor ->
                        { Actor = PlayerId(actor + 1)
                          Generation = 1
                          Position = Vector3(position, float32 actor * 2.0f, 0.0f)
                          Velocity = Vector3(speed, 0.0f, 0.0f)
                          Facing = float32 (actor % 4) * MathF.PI * 0.5f + float32 (max 0 (phase - 280)) * 0.025f
                          AngularVelocity = if phase >= 280 then 1.0f else 0.0f
                          Condition = 1.0f - float32 actor * 0.02f
                          AttentionTarget = ValueSome(Vector3(40.0f, 4.0f, 0.11f))
                          Morphology = PhysicalMorphology.standard
                          Active = true })
                  Evidence = evidence })

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

        let quality = MotionRuntime(config, identity)
        let qualityScratch = PoseScratch rig
        let qualityPalette = Array.create rig.JointNodes.Length Matrix4x4.Identity
        let mutable maxSupportError = 0.0f
        let mutable supportContext = ""
        let mutable maxReachableSupportError = 0.0f
        let mutable maxPenetration = 0.0f
        let mutable penetrationTick = 0
        let mutable penetrationActor = 0
        let mutable penetrationContext = ""
        let mutable maxBoundaryAnkleJump = 0.0f
        let mutable boundaryContext = ""
        let mutable maxContactError = 0.0f
        let mutable supportSamples = 0
        let mutable unreachableSupports = 0
        let mutable nonfiniteMatrices = 0
        let mutable contactSamples = 0
        let supportBySpeed = Array.zeroCreate<int> 4
        let unreachableBySpeed = Array.zeroCreate<int> 4
        let soles =
            character.Batches[0].Vertices
            |> Array.filter (fun vertex -> abs (vertex.Position.Y - 0.005f) < 0.00001f && abs vertex.Position.X > 0.04f && abs vertex.Position.X < 0.17f)

        for tick = 0 to 511 do
            quality.Ingest samples[tick] |> require
            let time = { Tick = tick; Fraction = 0.0f }
            for actor = 0 to 21 do
                let plan = quality.TryPlan(PlayerId(actor + 1), time) |> require |> ValueOption.get
                let rootPosition = MotionInput.vector plan.Root.Position
                let yaw = MotionInput.facing plan.Root.Facing
                let rotation = Matrix4x4.CreateRotationY yaw
                let mutable previousSnapshot = plan
                let previousAnkle =
                    if tick > 0 then
                        let previous = quality.TryPlan(plan.Root.Actor, { Tick = tick - 1; Fraction = 0.0f }) |> require |> ValueOption.get
                        previousSnapshot <- previous
                        PoseEvaluation.evaluateInto (config, rig, previous, time, rootPosition, yaw, qualityScratch, qualityPalette, 0) |> ignore
                        struct (qualityScratch.World[rig.LeftLeg.End].Translation, qualityScratch.World[rig.RightLeg.End].Translation)
                    else struct (Vector3.Zero, Vector3.Zero)
                let struct (leftResidual, rightResidual) = PoseEvaluation.evaluateInto (config, rig, plan, time, rootPosition, yaw, qualityScratch, qualityPalette, 0)
                if tick > 0 then
                    let struct (left, right) = previousAnkle
                    let jump = max (Vector3.Distance(left, qualityScratch.World[rig.LeftLeg.End].Translation)) (Vector3.Distance(right, qualityScratch.World[rig.RightLeg.End].Translation))
                    if jump > maxBoundaryAnkleJump then
                        maxBoundaryAnkleJump <- jump
                        boundaryContext <- sprintf "tick=%d actor=%d speed=%f left=%A->%A right=%A->%A tasks=%b/%b acceleration=%A->%A lean=%A->%A" tick (actor + 1) (Vector2(plan.Root.Velocity.X, plan.Root.Velocity.Y).Length()) previousSnapshot.Left.Phase plan.Left.Phase previousSnapshot.Right.Phase plan.Right.Phase plan.LeftOverlay.Active plan.RightOverlay.Active previousSnapshot.Acceleration plan.Acceleration previousSnapshot.Lean plan.Lean
                for chain, foot, overlay, residual in [| rig.LeftLeg, plan.Left, plan.LeftOverlay, leftResidual; rig.RightLeg, plan.Right, plan.RightOverlay, rightResidual |] do
                    let worldPoint socket = Vector3.Transform(Vector3.Transform(socket, qualityScratch.World[chain.End]), rotation) + rootPosition
                    if foot.Phase = SupportPhase.Support && not overlay.Active then
                        let speed = Vector2(plan.Root.Velocity.X, plan.Root.Velocity.Y).Length()
                        let bucket = if speed < 0.1f then 0 elif speed < 2.0f then 1 elif speed < 4.0f then 2 else 3
                        supportBySpeed[bucket] <- supportBySpeed[bucket] + 1
                        let error = Vector3.Distance(worldPoint chain.Socket, foot.Anchor)
                        if error > maxSupportError then
                            maxSupportError <- error
                            supportContext <- sprintf "tick=%d actor=%d speed=%f foot=%A other=%A anchor=%A root=%A residual=%A" tick (actor + 1) speed foot.Phase (if chain.Upper = rig.LeftLeg.Upper then plan.Right.Phase else plan.Left.Phase) foot.Anchor plan.Root.Position residual
                        supportSamples <- supportSamples + 1
                        if residual.IsSome then
                            unreachableSupports <- unreachableSupports + 1
                            unreachableBySpeed[bucket] <- unreachableBySpeed[bucket] + 1
                        else maxReachableSupportError <- max maxReachableSupportError error
                    if overlay.Active && abs (float32 tick - overlay.Preparation.Finish) < 0.0001f then
                        let error = Vector3.Distance(worldPoint chain.BallSocket, overlay.Preparation.Target)
                        maxContactError <- max maxContactError error
                        contactSamples <- contactSamples + 1
                for vertex in soles do
                    let joints, weights = vertex.Joints, vertex.Weights
                    let position =
                        Vector3.Transform(vertex.Position, qualityPalette[int joints.X]) * weights.X
                        + Vector3.Transform(vertex.Position, qualityPalette[int joints.Y]) * weights.Y
                        + Vector3.Transform(vertex.Position, qualityPalette[int joints.Z]) * weights.Z
                        + Vector3.Transform(vertex.Position, qualityPalette[int joints.W]) * weights.W
                    let penetration = max 0.0f -position.Y
                    if penetration > maxPenetration then
                        maxPenetration <- penetration
                        penetrationTick <- tick
                        penetrationActor <- actor + 1
                        penetrationContext <- sprintf "vertex=%A posed=%A left=%A right=%A leftTask=%b rightTask=%b root=%A residuals=%A/%A" vertex.Position position plan.Left.Phase plan.Right.Phase plan.LeftOverlay.Active plan.RightOverlay.Active plan.Root.Position leftResidual rightResidual
                for matrix in qualityPalette do
                    if not (Single.IsFinite matrix.M11 && Single.IsFinite matrix.M22 && Single.IsFinite matrix.M33 && Single.IsFinite matrix.M41 && Single.IsFinite matrix.M42 && Single.IsFinite matrix.M43) then
                        nonfiniteMatrices <- nonfiniteMatrices + 1

        let report =
            {| SchemaVersion = 1
               GeneratedUtc = DateTime.UtcNow
               Runtime = Environment.Version.ToString()
               Cpu =
                File.ReadLines("/proc/cpuinfo")
                |> Seq.find (fun line -> line.StartsWith("model name"))
               Configuration = "Release, 22 kinematically integrated synthetic roots with prepared and confirmed foot tasks; CPU only"
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
               Quality = {| SupportSamples = supportSamples; UnreachableSupports = unreachableSupports
                            MaxSupportErrorMetres = maxSupportError; SupportContext = supportContext; MaxReachableSupportErrorMetres = maxReachableSupportError
                            MaxSolePenetrationMetres = maxPenetration; MaxBoundaryAnkleJumpMetres = maxBoundaryAnkleJump; BoundaryContext = boundaryContext
                            PenetrationTick = penetrationTick; PenetrationActor = penetrationActor; PenetrationContext = penetrationContext
                            ContactSamples = contactSamples; MaxContactErrorMetres = maxContactError; NonfiniteMatrices = nonfiniteMatrices
                            SupportBySpeed = supportBySpeed; UnreachableBySpeed = unreachableBySpeed |}
               Seek127Boundaries = seek |}

        let json =
            JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true))

        let path = Path.GetFullPath outputPath
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllText(path, json)
        Console.WriteLine(json)
        Console.WriteLine($"report={path}")
        0
