namespace FootballEngine.Client.Tests.Animation

open System.Numerics
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Client.Animation
open FootballEngine.Client.Match3D

module MotionRuntimeTests =
    let private config = AnimationConfig.defaultConfig

    let private identity =
        { PlannerVersion = 1
          ConfigHash = "fixture-config"
          RigHash = "fixture-rig"
          ProfileHash = "fixture-profile"
          TrackId = 1UL }

    let private boundary tick =
        { Tick = tick * 1<subtick>
          Roots =
            [| { Actor = PlayerId 1
                 Generation = 1
                 Position = Vector3(float32 tick * 0.05f, 0.0f, 0.0f)
                 Velocity = Vector3(2.0f, 0.0f, 0.0f)
                 Facing = 0.0f
                 AngularVelocity = 0.0f
                 Condition = 1.0f
                 AttentionTarget = ValueSome(Vector3(5.0f, 2.0f, 0.11f))
                 Morphology = PhysicalMorphology.standard
                 Active = true } |]
          Evidence = [||] }

    let private unwrap =
        function
        | Ok value -> value
        | Error error -> failtestf "%A" error

    let tests =
        testList
            "ProceduralMotion"
            [ testCase "seek reconstruction uses only available canonical boundaries from a complete block" (fun _ ->
                  let original = MotionRuntime(config, identity)

                  for tick = 0 to 128 do
                      original.Ingest(boundary tick) |> unwrap

                  let checkpoint = original.Checkpoint()

                  for tick = 129 to 200 do
                      original.Ingest(boundary tick) |> unwrap

                  let completeBlock =
                      [| for tick = 128 to 255 do
                             yield boundary tick |]

                  let rebuilt =
                      MotionReconstruction.rebuild
                          config
                          identity
                          checkpoint
                          180
                          (fun _ -> Ok completeBlock)
                          System.Threading.CancellationToken.None
                      |> unwrap

                  Expect.equal rebuilt.Frontier 180 "future archive rows are not ingested"

                  Expect.equal
                      (rebuilt.TryPlan(PlayerId 1, { Tick = 180; Fraction = 0.5f }) |> unwrap)
                      (original.TryPlan(PlayerId 1, { Tick = 180; Fraction = 0.5f }) |> unwrap)
                      "identical restored plan"

                  let epoch = original.BeginSeek()
                  original.PublishRecovery(rebuilt, epoch) |> unwrap
                  Expect.equal original.Frontier 200 "historical recovery cannot rewind the live planner"
                  original.BeginSeek() |> ignore

                  Expect.equal
                      (original.PublishRecovery(rebuilt, epoch))
                      (Error StaleEpoch)
                      "obsolete work cannot publish"

                  use cancel = new System.Threading.CancellationTokenSource()
                  cancel.Cancel()

                  Expect.isError
                      (MotionReconstruction.rebuild
                          config
                          identity
                          checkpoint
                          180
                          (fun _ -> Ok completeBlock)
                          cancel.Token)
                      "cancelled seek has no partial publication")
              testCase "binary checkpoint roundtrip preserves the entire state and rejects foreign identities" (fun _ ->
                  let runtime = MotionRuntime(config, identity)

                  for tick = 0 to 80 do
                      runtime.Ingest(boundary tick) |> unwrap

                  let checkpoint = runtime.Checkpoint()
                  let bytes = MotionCheckpointCodec.serialize checkpoint
                  let restored = MotionCheckpointCodec.deserialize config identity bytes |> unwrap

                  Expect.equal
                      restored
                      checkpoint
                      "roots, curves, velocities, phases, anchors, filters, gaze, seeds, tasks and cursor"

                  for foreign in
                      [| { identity with PlannerVersion = 2 }
                         { identity with ConfigHash = "foreign" }
                         { identity with RigHash = "foreign" }
                         { identity with
                             ProfileHash = "foreign" }
                         { identity with TrackId = 2UL } |] do
                      Expect.isError
                          (MotionCheckpointCodec.deserialize config foreign bytes)
                          "every identity component is pinned"

                  Expect.isError
                      (MotionCheckpointCodec.deserialize config identity (Array.append bytes [| 0uy |]))
                      "trailing bytes rejected"

                  Expect.isError
                      (MotionCheckpointCodec.deserialize config identity bytes[0 .. bytes.Length - 2])
                      "truncation rejected")
              testCase "malformed finite state cannot enter a checkpoint restoration" (fun _ ->
                  let runtime = MotionRuntime(config, identity)
                  runtime.Ingest(boundary 0) |> unwrap
                  let checkpoint = runtime.Checkpoint()
                  let plans = Array.copy checkpoint.Plans

                  plans[0] <-
                      { plans[0] with
                          Lean = Vector3(System.Single.NaN, 0.0f, 0.0f) }

                  let invalid = { checkpoint with Plans = plans }

                  Expect.isError
                      (MotionCheckpointCodec.deserialize config identity (MotionCheckpointCodec.serialize invalid))
                      "nonfinite payload rejected explicitly"

                  Expect.equal
                      (runtime.Restore(invalid, runtime.Epoch))
                      (Error IncompatibleCheckpoint)
                      "typed payload is validated too")
              testCase "indexed checkpoints bound reconstruction independently of match duration" (fun _ ->
                  let runtime = MotionRuntime(config, identity)
                  let archive = MotionCheckpointArchive.Writer()

                  for tick = 0 to 256 do
                      runtime.Ingest(boundary tick) |> unwrap

                      if tick % MotionTrack.checkpointInterval = 0 then
                          archive.Append(runtime.Checkpoint()) |> unwrap

                  let bytes = archive.Complete()

                  let preceding =
                      MotionCheckpointArchive.preceding config identity bytes 255 |> unwrap

                  Expect.equal preceding.Tick 128 "preceding interval checkpoint"
                  Expect.isLessThan (255 - preceding.Tick) MotionTrack.checkpointInterval "bounded forward replay"
                  Expect.equal (archive.Complete()) bytes "export is idempotent"
                  Expect.isError (archive.Append(runtime.Checkpoint())) "closed archive cannot accept stale work")
              testCase "config hash changes when visual timing or attention changes" (fun _ ->
                  Expect.notEqual
                      (MotionIdentity.configHash config)
                      (MotionIdentity.configHash
                          { config with
                              GazeRate = config.GazeRate + 0.1f })
                      "gaze is checkpoint identity"

                  Expect.notEqual
                      (MotionIdentity.configHash config)
                      (MotionIdentity.configHash
                          { config with
                              TickSeconds = config.TickSeconds * 2.0f })
                      "canonical tick duration is pinned")
              testCase "real rig support sockets remain planted and segment lengths are conserved" (fun _ ->
                  let path =
                      System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                  let character = CharacterAsset.load path |> unwrap
                  let rig = character.Rig
                  RigDefinition.conform rig PhysicalMorphology.standard |> unwrap

                  let root =
                      { (boundary 0).Roots[0] with
                          Velocity = Vector3.Zero
                          AttentionTarget = ValueSome(Vector3(12.0f, 2.0f, 0.11f)) }

                  let plan = SupportPlanner.initialize config 0 root
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity

                  let struct (left, right) =
                      PoseEvaluation.evaluateInto (
                          config,
                          rig,
                          plan,
                          { Tick = 0; Fraction = 0.0f },
                          Vector3.Zero,
                          MotionInput.facing root.Facing,
                          scratch,
                          palette,
                          0
                      )

                  Expect.isTrue left.IsNone "standing support is reachable"
                  Expect.isTrue right.IsNone "standing support is reachable"

                  for chain, foot in [| rig.LeftLeg, plan.Left; rig.RightLeg, plan.Right |] do
                      let socket = Vector3.Transform(chain.Socket, scratch.World[chain.End])

                      let expected =
                          MotionInput.localPoint Vector3.Zero (MotionInput.facing root.Facing) foot.Anchor

                      Expect.isLessThan
                          (Vector3.Distance(socket, expected))
                          0.01f
                          "actual sole socket, not a toe proxy"

                      Expect.isLessThan
                          (abs (
                              Vector3.Distance(
                                  scratch.World[chain.Upper].Translation,
                                  scratch.World[chain.Lower].Translation
                              )
                              - rig.Bind[chain.Lower].Translation.Length()
                          ))
                          0.00001f
                          "upper segment rigid"

                      Expect.isLessThan
                          (abs (
                              Vector3.Distance(
                                  scratch.World[chain.Lower].Translation,
                                  scratch.World[chain.End].Translation
                              )
                              - rig.Bind[chain.End].Translation.Length()
                          ))
                          0.00001f
                          "lower segment rigid"

                  for matrix in palette do
                      Expect.isTrue
                          (System.Single.IsFinite matrix.M11
                           && System.Single.IsFinite matrix.M22
                           && System.Single.IsFinite matrix.M33
                           && System.Single.IsFinite matrix.M41
                           && System.Single.IsFinite matrix.M42
                           && System.Single.IsFinite matrix.M43)
                          "finite palette")
              testCase "real rig pose kernel allocates zero bytes after warmup" (fun _ ->
                  let path =
                      System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                  let character = CharacterAsset.load path |> unwrap
                  let rig = character.Rig

                  let root =
                      { (boundary 0).Roots[0] with
                          Velocity = Vector3.Zero }

                  let plan = SupportPlanner.initialize config 0 root
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  let time = { Tick = 0; Fraction = 0.0f }
                  let yaw = MotionInput.facing root.Facing

                  for _ = 1 to 100 do
                      PoseEvaluation.evaluateInto (config, rig, plan, time, Vector3.Zero, yaw, scratch, palette, 0)
                      |> ignore

                  let before = System.GC.GetAllocatedBytesForCurrentThread()

                  for _ = 1 to 1000 do
                      PoseEvaluation.evaluateInto (config, rig, plan, time, Vector3.Zero, yaw, scratch, palette, 0)
                      |> ignore

                  let total = System.GC.GetAllocatedBytesForCurrentThread() - before

                  let measure action =
                      action ()
                      let start = System.GC.GetAllocatedBytesForCurrentThread()

                      for _ = 1 to 1000 do
                          action ()

                      System.GC.GetAllocatedBytesForCurrentThread() - start

                  let body = measure (fun () -> BodyPose.initialize rig plan scratch.Local)

                  let foot =
                      measure (fun () -> PoseEvaluation.footTarget config 0.0f plan.Left |> ignore)

                  let limb =
                      measure (fun () ->
                          ConstraintSolve.limb (
                              rig,
                              scratch.World,
                              root.Actor,
                              MotionEffector.LeftFoot,
                              rig.LeftLeg,
                              Vector3(0.1f, 0.0f, 0.1f)
                          )
                          |> ignore)

                  let orientation =
                      measure (fun () ->
                          ConstraintSolve.orient rig scratch.World rig.LeftLeg Vector3.UnitY Vector3.UnitZ)

                  Expect.equal
                      total
                      0L
                      (sprintf
                          "kernel only; body=%d foot=%d limb=%d orientation=%d; presentation measured separately"
                          body
                          foot
                          limb
                          orientation))
              testCase "gaze tracks only the supplied ball target and stays bounded" (fun _ ->
                  let root =
                      { (boundary 0).Roots[0] with
                          Facing = 0.0f
                          AttentionTarget = ValueSome(Vector3(10.0f, 4.0f, 0.11f)) }

                  let plan = SupportPlanner.initialize config 0 root
                  Expect.isLessThan (abs plan.Gaze.X) config.MaxGazeYaw "anatomical yaw bound"
                  Expect.isGreaterThan (abs plan.Gaze.X) 0.1f "ball is not ignored"

                  let nextRoot =
                      { root with
                          AttentionTarget = ValueSome(Vector3(-10.0f, -4.0f, 0.11f)) }

                  let next = SupportPlanner.advance config 1 nextRoot plan

                  Expect.isLessThanOrEqual
                      (abs (next.Gaze.X - plan.Gaze.X))
                      (config.GazeRate * config.TickSeconds + 0.00001f)
                      "gaze cannot snap to new target")
              testCase "trajectory derivative is with respect to simulation seconds" (fun _ ->
                  let curve =
                      { Start = 0.0f
                        Finish = 40.0f
                        From = Vector3.Zero
                        Target = Vector3.UnitX
                        FromVelocity = Vector3.Zero
                        TargetVelocity = Vector3.Zero
                        Clearance = 0.1f }

                  let struct (_, velocity) = Trajectory.evaluate config.TickSeconds 20.0f curve
                  let struct (before, _) = Trajectory.evaluate config.TickSeconds 19.99f curve
                  let struct (after, _) = Trajectory.evaluate config.TickSeconds 20.01f curve
                  let measured = (after - before) / (0.02f * config.TickSeconds)
                  Expect.isLessThan (Vector3.Distance(velocity, measured)) 0.002f "correct derivative and units"
                  let struct (_, startVelocity) = Trajectory.evaluate config.TickSeconds 0.0f curve
                  let struct (_, endVelocity) = Trajectory.evaluate config.TickSeconds 40.0f curve
                  Expect.equal startVelocity Vector3.Zero "C1 start"
                  Expect.equal endVelocity Vector3.Zero "C1 finish")
              testCase "retarget preserves current position and velocity" (fun _ ->
                  let curve =
                      { Start = 0.0f
                        Finish = 10.0f
                        From = Vector3.Zero
                        Target = Vector3.UnitX
                        FromVelocity = Vector3.Zero
                        TargetVelocity = Vector3.Zero
                        Clearance = 0.0f }

                  let revised = Trajectory.retarget config.TickSeconds 4.0f Vector3.UnitZ 12.0f curve

                  Expect.equal
                      (Trajectory.evaluate config.TickSeconds 4.0f revised)
                      (Trajectory.evaluate config.TickSeconds 4.0f curve)
                      "C1 at revision")
              testCase "evaluation cadence never advances planner and checkpoints restore identical future" (fun _ ->
                  let original = MotionRuntime(config, identity)

                  for tick = 0 to 80 do
                      original.Ingest(boundary tick) |> unwrap

                  let checkpoint = original.Checkpoint()
                  let time = { Tick = 80; Fraction = 0.25f }
                  let expected = original.TryPlan(PlayerId 1, time) |> unwrap

                  for fps in [ 30; 60; 144; 240 ] do
                      for _ = 1 to fps * 4 do
                          Expect.equal
                              (original.TryPlan(PlayerId 1, time) |> unwrap)
                              expected
                              "pure same-time pose input"

                  let restored = MotionRuntime(config, identity)
                  let epoch = restored.BeginSeek()
                  restored.Restore(checkpoint, epoch) |> unwrap

                  for tick = 81 to 100 do
                      original.Ingest(boundary tick) |> unwrap
                      restored.Ingest(boundary tick) |> unwrap

                  Expect.equal
                      (restored.Checkpoint())
                      (original.Checkpoint())
                      "full phase anchor filter seed cursor restored")
              testCase "stale seek and closed runtime cannot publish" (fun _ ->
                  let runtime = MotionRuntime(config, identity)
                  runtime.Ingest(boundary 0) |> unwrap
                  let checkpoint = runtime.Checkpoint()
                  let oldEpoch = runtime.BeginSeek()
                  runtime.BeginSeek() |> ignore

                  Expect.equal
                      (runtime.Restore(checkpoint, oldEpoch))
                      (Error StaleEpoch)
                      "obsolete reconstruction rejected"

                  runtime.Close()
                  Expect.equal (runtime.Ingest(boundary 1)) (Error RuntimeClosed) "close prevents publication")
              testCase "incompatible checkpoint and noncanonical cadence are explicit errors" (fun _ ->
                  let runtime = MotionRuntime(config, identity)
                  runtime.Ingest(boundary 0) |> unwrap

                  Expect.equal
                      (runtime.Ingest(boundary 2))
                      (Error(NonCanonicalBoundary(1, 2)))
                      "missing ticks cannot silently reset gait"

                  let foreign =
                      { runtime.Checkpoint() with
                          Identity = { identity with RigHash = "foreign" } }

                  Expect.equal
                      (runtime.Restore(foreign, runtime.Epoch))
                      (Error IncompatibleCheckpoint)
                      "no legacy or incompatible restore") ]
