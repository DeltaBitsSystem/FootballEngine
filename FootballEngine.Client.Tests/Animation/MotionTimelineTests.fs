namespace FootballEngine.Client.Tests.Animation

open System
open System.Numerics
open System.Threading
open Expecto
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.Core.Match
open FootballEngine.Client.Animation
open FootballEngine.Client.Match3D

module MotionTimelineTests =
    let private config = AnimationConfig.defaultConfig

    let private unwrap =
        function
        | Ok value -> value
        | Error error -> failtestf "%A" error

    let private rig =
        lazy
            (let path =
                IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

             (CharacterAsset.load path |> unwrap).Rig)

    let private identity () =
        { PlannerVersion = 1
          ConfigHash = MotionIdentity.configHash config
          RigHash = rig.Value.Hash
          ProfileHash = "timeline-fixture"
          TrackId = 17UL }

    let private evidence actor sequence phase available =
        { Sequence = sequence
          TaskId = uint64 actor
          Actor = PlayerId actor
          Generation = 1
          Family = MotionFamily.FootStrike
          Effector =
            if actor % 2 = 0 then
                MotionEffector.LeftFoot
            else
                MotionEffector.RightFoot
          Phase = phase
          OccurredAt = (available - 1) * 1<subtick>
          AvailableAt = available * 1<subtick>
          StartedAt = 60<subtick>
          CommitAt = 65<subtick>
          EndsAt = 75<subtick>
          PredictedPoint = Vector3(1.0f, 0.0f, 0.11f)
          ConfirmedPoint =
            if phase = MotionEvidencePhase.Confirmed then
                ValueSome(Vector3(1.0f, 0.0f, 0.11f))
            else
                ValueNone
          BallCenter = Vector3(1.0f, 0.0f, 0.11f)
          IncomingVelocity = ValueNone
          OutgoingVelocity = ValueNone }

    let private boundaries () =
        let mutable position = 0.0f

        Array.init 513 (fun tick ->
            let speed =
                if tick < 40 then 0.0f
                elif tick < 80 then float32 (tick - 40) * 0.1f
                elif tick < 160 then 4.0f
                elif tick < 200 then float32 (200 - tick) * 0.1f
                else 0.0f

            position <- position + speed * config.TickSeconds

            { Tick = tick * 1<subtick>
              Roots =
                Array.init 22 (fun index ->
                    { Actor = PlayerId(index + 1)
                      Generation = 1
                      Position = Vector3(position, float32 index * 2.0f, 0.0f)
                      Velocity = Vector3(speed, 0.0f, 0.0f)
                      Facing =
                        float32 (index % 4) * MathF.PI * 0.5f
                        + float32 (max 0 (min 40 (tick - 200))) * 0.02f
                      AngularVelocity = if tick > 200 && tick < 240 then 0.8f else 0.0f
                      Condition = 1.0f - float32 index * 0.02f
                      AttentionTarget = ValueSome(Vector3(5.0f, 4.0f, 0.11f))
                      Morphology = PhysicalMorphology.standard
                      Active = true })
              Evidence =
                if tick = 60 then
                    Array.init 22 (fun index ->
                        evidence (index + 1) (uint64 (index + 1)) MotionEvidencePhase.Prepared tick)
                elif tick = 65 then
                    Array.init 22 (fun index ->
                        evidence (index + 1) (uint64 (index + 23)) MotionEvidencePhase.Confirmed tick)
                else
                    [||] })

    let private palette (runtime: MotionRuntime) (samples: MotionBoundary[]) time scratch output =
        for actor = 0 to 21 do
            let plan = runtime.TryPlan(PlayerId(actor + 1), time) |> unwrap |> ValueOption.get
            // The fixture exposes the next sporting root explicitly; archive access alone is not anticipation.
            Expect.isGreaterThan runtime.Frontier time.Tick "root interpolation has an available upper boundary"

            let root =
                Vector3.Lerp(
                    samples[time.Tick].Roots[actor].Position,
                    samples[time.Tick + 1].Roots[actor].Position,
                    time.Fraction
                )

            PoseEvaluation.evaluateInto (
                config,
                rig.Value,
                plan,
                time,
                MotionInput.vector root,
                MotionInput.facing plan.Root.Facing,
                scratch,
                output,
                actor * rig.Value.JointNodes.Length
            )
            |> ignore

    let tests =
        testList
            "MotionTimeline"
            [ testCase "all 22 complete palettes match across cadences batching pause and persisted seek" (fun _ ->
                  let samples = boundaries ()
                  let identity = identity ()
                  let recorded = MotionRuntime(config, identity)
                  let checkpoints = MotionCheckpointArchive.Writer()
                  use track = new MotionTrack.Writer()

                  for sample in samples do
                      track.Append sample |> unwrap
                      recorded.Ingest sample |> unwrap

                      if int sample.Tick % MotionTrack.checkpointInterval = 0 then
                          checkpoints.Append(recorded.Checkpoint()) |> unwrap

                  let trackBytes = track.Complete()
                  let checkpointBytes = checkpoints.Complete()

                  let replay =
                      { Replay.write [] with
                          Sections =
                              [| { Kind = 1u
                                   Version = 1
                                   Payload = trackBytes }
                                 { Kind = 2u
                                   Version = 1
                                   Payload = checkpointBytes } |] }

                  let loaded = MotionReplay.load config identity replay |> unwrap

                  Expect.isError
                      (MotionReplay.load
                          { config with
                              GazeRate = config.GazeRate + 0.1f }
                          identity
                          replay)
                      "a pinned header cannot be evaluated with changed visual config"

                  use mismatched =
                      new MatchPresentationStream(MatchRuntimeId 18UL, LiveExecutionPolicy.defaultPolicy)

                  Expect.equal
                      (mismatched.ConfigureMotion { identity with ConfigHash = "foreign" })
                      (Error IncompatibleCheckpoint)
                      "integrated live config is pinned too"

                  for invalid in
                      [| { replay with Meta = { Version = 10 } }
                         { replay with
                             Sections = [| replay.Sections[0] |] }
                         { replay with
                             Sections = [| replay.Sections[0]; replay.Sections[0] |] } |] do
                      Expect.isError
                          (MotionReplay.load config identity invalid)
                          "no legacy, absent or duplicate section path"

                  let wrongClock = Array.copy trackBytes
                  BitConverter.GetBytes(config.TickSeconds * 2.0f).CopyTo(wrongClock, 4)

                  Expect.isError
                      (MotionReplay.load
                          config
                          identity
                          { replay with
                              Sections =
                                  [| { replay.Sections[0] with
                                         Payload = wrongClock }
                                     replay.Sections[1] |] })
                      "timebase is pinned, not ignored"

                  let decoded =
                      [| for tick in [ 0; 128; 256; 384; 512 ] do
                             yield! MotionTrack.readBlock trackBytes (tick * 1<subtick>) |> unwrap |]

                  Expect.equal decoded samples "complete file preserves roots and multi-actor evidence"

                  let probes =
                      [| 1.25; 50.5; 64.25; 65.0; 80.5; 127.0; 128.0; 180.75; 219.5; 249.25 |]

                  let drive fps speed (source: MotionBoundary[]) =
                      let runtime = MotionRuntime(config, identity)
                      let scratch = PoseScratch rig.Value
                      let output = Array.create (22 * rig.Value.JointNodes.Length) Matrix4x4.Identity
                      let captured = Collections.Generic.Dictionary<float, Matrix4x4[]>()

                      let schedule =
                          Array.append
                              probes
                              [| for frame = 0 to int (250.0 * float fps * float config.TickSeconds / speed) do
                                     yield min 250.0 (float frame * speed / (float fps * float config.TickSeconds)) |]
                          |> Array.distinct
                          |> Array.sort

                      for at in schedule do
                          let time = MotionTime.ofTicks at

                          for tick = runtime.Frontier + 1 to time.Tick + 1 do
                              runtime.Ingest source[tick] |> unwrap

                          palette runtime source time scratch output

                          if Array.contains at probes then
                              captured.Add(at, Array.copy output)

                              for _ = 1 to 4 do
                                  palette runtime source time scratch output

                              Expect.equal output captured[at] "pause preserves every matrix component"

                      captured

                  let reference = drive 60 1.0 samples

                  for fps in [ 30; 60; 144; 240 ] do
                      for speed in [ 1.0; 4.0 ] do
                          let rendered = drive fps speed decoded

                          for at in probes do
                              Expect.equal
                                  rendered[at]
                                  reference[at]
                                  "same MotionTime, all 484 joint matrices, no FPS-dependent history"

                  let checkpoint =
                      MotionCheckpointArchive.preceding config identity checkpointBytes 180 |> unwrap

                  Expect.equal checkpoint.Tick 128 "indexed reconstruction starts at its preceding checkpoint"

                  let rebuilt = loaded.Reconstruct(181, CancellationToken.None) |> unwrap

                  let output = Array.create (22 * rig.Value.JointNodes.Length) Matrix4x4.Identity
                  palette rebuilt samples (MotionTime.ofTicks 180.75) (PoseScratch rig.Value) output

                  Expect.equal
                      output
                      reference[180.75]
                      "persisted seek restores complete palettes, not merely equal plans")
              testCase "sequential ingestion allocates zero bytes after warmup for 22 actors" (fun _ ->
                  let runtime = MotionRuntime(config, identity ())
                  let captured = boundaries ()

                  let samples =
                      captured
                      |> Array.mapi (fun tick sample ->
                          if tick < 256 then
                              sample
                          else
                              let repeated = captured[tick % 256]

                              { repeated with
                                  Tick = sample.Tick
                                  Evidence =
                                      repeated.Evidence
                                      |> Array.map (fun record ->
                                          { record with
                                              Sequence = record.Sequence + 44UL
                                              OccurredAt = record.OccurredAt + 256<subtick>
                                              AvailableAt = record.AvailableAt + 256<subtick>
                                              StartedAt = record.StartedAt + 256<subtick>
                                              CommitAt = record.CommitAt + 256<subtick>
                                              EndsAt = record.EndsAt + 256<subtick> }) })

                  for tick = 0 to 255 do
                      runtime.Ingest samples[tick] |> unwrap

                  let before = GC.GetAllocatedBytesForCurrentThread()

                  for tick = 256 to 511 do
                      runtime.Ingest samples[tick] |> ignore

                  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
                  Expect.equal runtime.Frontier 511 "all canonical boundaries were planned"

                  Expect.equal
                      allocated
                      0L
                      "ingestion only; producer arrays and checkpoint serialization are measured separately")
              testCase "live historical windows and pose buffers fit the two MiB managed state budget" (fun _ ->
                  let identity = identity ()
                  let rig = rig.Value
                  let before = GC.GetAllocatedBytesForCurrentThread()
                  let live = MotionRuntime(config, identity)
                  let historical = MotionRuntime(config, identity, historical = true)
                  let scratch = PoseScratch rig
                  let palettes = Array.create (22 * rig.JointNodes.Length) Matrix4x4.Identity
                  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
                  GC.KeepAlive live
                  GC.KeepAlive historical
                  GC.KeepAlive scratch
                  GC.KeepAlive palettes
                  Expect.equal live.RetentionTicks 256 "live history contract is not reduced to fit the budget"

                  Expect.equal
                      historical.RetentionTicks
                      config.RecoveryWindowTicks
                      "historical headroom is independently bounded"

                  Expect.isLessThanOrEqual
                      allocated
                      (2L * 1024L * 1024L)
                      "both windows, all runtime buffers and CPU pose scratch/palettes; excludes archive and sporting producer")
              testCase
                  "invalid input is rejected atomically and retired generations consume sequence without acquiring limbs"
                  (fun _ ->
                      let samples = boundaries ()
                      let runtime = MotionRuntime(config, identity ())
                      runtime.Ingest samples[0] |> unwrap
                      let before = runtime.Checkpoint()
                      let unavailable = evidence 1 1UL MotionEvidencePhase.Prepared 60

                      let malformed =
                          [| { samples[1] with
                                 Roots = [| samples[1].Roots[0]; samples[1].Roots[0] |] }
                             { samples[1] with
                                 Roots =
                                     [| { samples[1].Roots[0] with
                                            Condition = Single.NaN } |] }
                             { samples[1] with
                                 Evidence = [| unavailable |] } |]

                      for sample in malformed do
                          Expect.equal
                              (runtime.Ingest sample)
                              (Error InvalidMotionBoundary)
                              "invalid frontier cannot partially advance state"

                          Expect.equal (runtime.Checkpoint()) before "all previous state survives"

                      let old =
                          { unavailable with
                              OccurredAt = 0<subtick>
                              AvailableAt = 1<subtick>
                              Generation = 0 }

                      Expect.equal
                          (runtime.Ingest
                              { samples[1] with
                                  Evidence = [| { old with Sequence = 2UL } |] })
                          (Error(MotionSequenceGap(1UL, 2UL)))
                          "overflow is not silent"

                      runtime.Ingest { samples[1] with Evidence = [| old |] } |> unwrap

                      Expect.equal
                          (runtime.Checkpoint().Cursor)
                          2UL
                          "journal cursor advances even when participation no longer exists"

                      Expect.isTrue
                          (runtime.Checkpoint().Tasks |> Array.forall (fun task -> not task.Active))
                          "old generation cannot claim the new generation's body")
              testCase "checkpoint participation cursor and index damage return errors instead of exceptions" (fun _ ->
                  let samples = boundaries ()
                  let identity = identity ()
                  let runtime = MotionRuntime(config, identity)

                  for tick = 0 to 64 do
                      runtime.Ingest samples[tick] |> unwrap

                  let checkpoint = runtime.Checkpoint()

                  Expect.equal
                      (MotionCheckpointCodec.deserialize config identity (MotionCheckpointCodec.serialize checkpoint)
                       |> unwrap)
                      checkpoint
                      "active tasks and preparation state roundtrip"

                  let foreignTasks = Array.copy checkpoint.Tasks

                  foreignTasks[0] <-
                      { foreignTasks[0] with
                          Evidence =
                              { foreignTasks[0].Evidence with
                                  Generation = 2 } }

                  for damaged in
                      [| { checkpoint with Tick = -1 }
                         { checkpoint with ActorCount = -1 }
                         { checkpoint with Cursor = 1UL }
                         { checkpoint with Tasks = foreignTasks } |] do
                      Expect.equal
                          (runtime.Restore(damaged, runtime.Epoch))
                          (Error IncompatibleCheckpoint)
                          "strict typed boundary"

                  let archive = MotionCheckpointArchive.Writer()
                  let initial = MotionRuntime(config, identity)
                  initial.Ingest samples[0] |> unwrap
                  archive.Append(initial.Checkpoint()) |> unwrap
                  let bytes = archive.Complete()
                  let damaged = Array.copy bytes
                  BitConverter.GetBytes(-1).CopyTo(damaged, 16)

                  Expect.isError
                      (MotionCheckpointArchive.preceding config identity damaged 0)
                      "invalid offset yields Result"

                  Expect.isError
                      (MotionCheckpointArchive.preceding config identity bytes[0 .. bytes.Length - 2] 0)
                      "truncated archive yields Result"

                  let future = initial.Checkpoint()

                  let forged =
                      { future with
                          Tick = 128
                          Plans = future.Plans |> Array.map (fun plan -> { plan with Tick = 128 }) }

                  let wrongFrontier = Array.copy bytes
                  (MotionCheckpointCodec.serialize forged).CopyTo(wrongFrontier, 24)

                  Expect.isError
                      (MotionCheckpointArchive.preceding config identity wrongFrontier 0)
                      "payload frontier cannot disagree with the index"

                  Expect.isError
                      (MotionCheckpointArchive.preceding config identity bytes 128)
                      "absent interval cannot reuse an arbitrarily old checkpoint"

                  let mutable plan = future.Plans[0]
                  initial.Close()

                  Expect.equal
                      (initial.TryPlan(plan.Root.Actor, { Tick = 0; Fraction = 0.0f }))
                      (Error RuntimeClosed)
                      "closed debug reader"

                  Expect.isFalse
                      ((initial :> IMotionPlanSource)
                          .TryCopyPlan(plan.Root.Actor, { Tick = 0; Fraction = 0.0f }, &plan))
                      "closed palette reader")
              testCase "visual configuration rejects nonfinite parameters and unbounded buffers" (fun _ ->
                  AnimationConfig.validate config |> unwrap

                  for invalid in
                      [| { config with StepWidth = Single.NaN }
                         { config with
                             MaxGazeYaw = Single.PositiveInfinity }
                         { config with Clearance = -1.0f }
                         { config with
                             MaxActors = Int32.MaxValue }
                         { config with RecoveryWindowTicks = 0 } |] do
                      Expect.isError
                          (AnimationConfig.validate invalid)
                          "load boundary rejects poison values before buffer allocation")
              testCase
                  "presentation recovers expired plans off-thread without rewinding live state and close rejects publication"
                  (fun _ ->
                      let samples = boundaries ()

                      let create () =
                          let stream =
                              new MatchPresentationStream(MatchRuntimeId 17UL, LiveExecutionPolicy.defaultPolicy)

                          stream.ConfigureMotion(identity ()) |> unwrap
                          stream.PublishMotion samples |> unwrap
                          stream

                      use stream = create ()
                      let source = stream :> IMotionPlanSource
                      let time = { Tick = 180; Fraction = 0.5f }
                      let mutable plan = SupportPlanner.initialize config 0 samples[0].Roots[0]
                      Expect.isFalse (source.TryCopyPlan(PlayerId 1, time, &plan)) "expired window explicitly holds"
                      let ownerThread = Environment.CurrentManagedThreadId

                      // A synchronously waited thread-pool task may inline on the caller; a dedicated worker tests the intended boundary.
                      Threading.Tasks.Task.Factory
                          .StartNew((fun () ->
                              stream.RecoverMotion(
                                  (fun _ ->
                                      Expect.notEqual
                                          Environment.CurrentManagedThreadId
                                          ownerThread
                                          "archive owner is not the render caller"

                                      Ok samples[128..255]),
                                  CancellationToken.None
                               )), Threading.Tasks.TaskCreationOptions.LongRunning)
                          .GetAwaiter()
                          .GetResult()
                      |> unwrap
                      |> ignore

                      Expect.isTrue
                          (source.TryCopyPlan(PlayerId 1, time, &plan))
                          "history is available after publication"

                      Expect.equal
                          (stream.MotionCheckpoint() |> Option.get).Tick
                          512
                          "live frontier was not reset by historical recovery"

                      Expect.isTrue
                          (source.TryCopyPlan(PlayerId 2, { Tick = 196; Fraction = 0.25f }, &plan))
                          "recovery retains forward headroom, not just one actor/tick"

                      use closing = create ()
                      let closingSource = closing :> IMotionPlanSource
                      closingSource.TryCopyPlan(PlayerId 1, time, &plan) |> ignore

                      Expect.isError
                          (closing.RecoverMotion(
                              (fun _ ->
                                  closing.Close()
                                  Ok samples[128..255]),
                              CancellationToken.None
                          ))
                          "work finishing after close cannot resurrect presentation") ]
