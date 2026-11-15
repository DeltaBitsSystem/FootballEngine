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
                 GaitPhase = 0.0f
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
            [ testCase "22 coherent roots retain planted anchors and step during standing pivots" (fun _ ->
                  let plans =
                      Array.init 22 (fun actor ->
                          let root =
                              { (boundary 0).Roots[0] with
                                  Actor = PlayerId(actor + 1)
                                  Position = Vector3(0.0f, float32 actor * 2.0f, 0.0f)
                                  Velocity = Vector3.Zero }

                          SupportPlanner.initialize config 0 root)

                  let mutable pivotSteps = 0

                  for tick in 1..300 do
                      for actor in 0..21 do
                          let previous = plans[actor]

                          let speed =
                              if tick < 40 then 0.0f
                              elif tick < 80 then float32 (tick - 40) * 0.08f
                              elif tick < 140 then 3.2f
                              elif tick < 180 then float32 (180 - tick) * 0.08f
                              else 0.0f

                          let velocity =
                              match actor % 3 with
                              | 0 -> Vector3(speed, 0.0f, 0.0f)
                              | 1 -> Vector3(-speed, 0.0f, 0.0f)
                              | _ -> Vector3(0.0f, speed, 0.0f)

                          let pivot = min 1.0f (max 0.0f (float32 (tick - 200) / 80.0f))
                          let facing = pivot * System.MathF.PI

                          let root =
                              { previous.Root with
                                  Position = previous.Root.Position + velocity * config.TickSeconds
                                  Velocity = velocity
                                  Facing = facing
                                  AngularVelocity =
                                      if tick > 200 && tick < 280 then
                                          System.MathF.PI / (80.0f * config.TickSeconds)
                                      else
                                          0.0f }

                          let next = SupportPlanner.advance config tick root previous

                          for oldFoot, newFoot in [| previous.Left, next.Left; previous.Right, next.Right |] do
                              if oldFoot.Phase = SupportPhase.Support && newFoot.Phase = SupportPhase.Support then
                                  Expect.equal
                                      newFoot.Anchor
                                      oldFoot.Anchor
                                      "a planted anchor is never dragged by the root"

                              if
                                  oldFoot.Phase <> SupportPhase.Support
                                  && newFoot.Phase <> SupportPhase.Support
                                  && oldFoot.Segment.Start <> newFoot.Segment.Start
                              then
                                  let struct (a, av) =
                                      Trajectory.evaluate config.TickSeconds (float32 tick) oldFoot.Segment

                                  let struct (b, bv) =
                                      Trajectory.evaluate config.TickSeconds (float32 tick) newFoot.Segment

                                  Expect.isLessThan (Vector3.Distance(a, b)) 0.00001f "retarget position continuity"
                                  Expect.isLessThan (Vector3.Distance(av, bv)) 0.0001f "retarget velocity continuity"

                              if
                                  tick > 200
                                  && speed = 0.0f
                                  && oldFoot.Phase = SupportPhase.Support
                                  && newFoot.Phase = SupportPhase.Swing
                              then
                                  pivotSteps <- pivotSteps + 1

                          Expect.isFalse
                              (next.Left.Phase = SupportPhase.Swing && next.Right.Phase = SupportPhase.Swing)
                              "only one foot releases at once"

                          plans[actor] <- next

                  Expect.isGreaterThan pivotSteps 0 "turning on the spot triggers steps")
              testCase "running permits brief flight without inventing planted anchors" (fun _ ->
                  let root =
                      { (boundary 0).Roots[0] with
                          Position = Vector3.Zero
                          Velocity = Vector3(6.0f, 0.0f, 0.0f) }

                  let mutable plan = SupportPlanner.initialize config 0 root
                  let mutable flightTicks = 0

                  for tick = 1 to 120 do
                      let nextRoot =
                          { plan.Root with
                              Position = plan.Root.Position + plan.Root.Velocity * config.TickSeconds }

                      let next = SupportPlanner.advance config tick nextRoot plan

                      let airborne phase =
                          phase = SupportPhase.Swing || phase = SupportPhase.Landing

                      if airborne next.Left.Phase && airborne next.Right.Phase then
                          flightTicks <- flightTicks + 1

                      for oldFoot, newFoot in [| plan.Left, next.Left; plan.Right, next.Right |] do
                          if oldFoot.Phase = SupportPhase.Support && newFoot.Phase = SupportPhase.Support then
                              Expect.equal oldFoot.Anchor newFoot.Anchor "a support anchor stays fixed"

                      plan <- next

                  Expect.isGreaterThan flightTicks 0 "sprint has bounded flight rather than forced sliding")
              testCase "near-ball gaze is bounded and shared between neck and head" (fun _ ->
                  let path =
                      System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                  let rig = (CharacterAsset.load path |> unwrap).Rig

                  let root =
                      { (boundary 0).Roots[0] with
                          Position = Vector3.Zero
                          Velocity = Vector3.Zero
                          AttentionTarget = ValueSome(Vector3(0.15f, 0.1f, 0.11f)) }

                  let plan = SupportPlanner.initialize config 0 root

                  Expect.isLessThanOrEqual
                      (abs plan.Gaze.Y)
                      config.MaxGazePitch
                      "near target cannot force a vertical head bend"

                  Expect.isLessThanOrEqual (abs plan.Gaze.X) config.MaxGazeYaw "head cannot turn behind the player"
                  let local = Array.zeroCreate<RigLocalPose> rig.Bind.Length
                  BodyPose.initialize config rig plan local

                  let delta node =
                      Quaternion.Normalize(Quaternion.Inverse(rig.Bind[node].Rotation) * local[node].Rotation)

                  let neck = delta rig.Neck
                  let head = delta rig.Head
                  Expect.isGreaterThan (1.0f - abs neck.W) 0.00001f "neck participates in gaze"
                  Expect.isGreaterThan (1.0f - abs head.W) 0.00001f "head participates in gaze"

                  Expect.isLessThan
                      (2.0f * System.MathF.Acos(min 1.0f (abs neck.W)))
                      0.5f
                      "neck rotation remains conservative")
              testCase "procedural running arms counter the stride and elbows bend behind shoulders" (fun _ ->
                  let path =
                      System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                  let rig = (CharacterAsset.load path |> unwrap).Rig

                  let root =
                      { (boundary 0).Roots[0] with
                          Position = Vector3.Zero
                          Velocity = Vector3(4.0f, 0.0f, 0.0f) }

                  let initial = SupportPlanner.initialize config 0 root

                  let plan =
                      { initial with
                          Left =
                              { initial.Left with
                                  Anchor = initial.Left.Anchor + Vector3(0.3f, 0.0f, 0.0f) }
                          Right =
                              { initial.Right with
                                  Anchor = initial.Right.Anchor - Vector3(0.3f, 0.0f, 0.0f) } }

                  let scratch = PoseScratch rig
                  let output = Array.create rig.JointNodes.Length Matrix4x4.Identity

                  PoseEvaluation.evaluateInto (
                      config,
                      rig,
                      plan,
                      { Tick = 0; Fraction = 0.0f },
                      Vector3.Zero,
                      MotionInput.facing root.Facing,
                      scratch,
                      output,
                      0
                  )
                  |> ignore

                  for chain in [| rig.LeftArm; rig.RightArm |] do
                      let shoulder = scratch.World[chain.Upper].Translation
                      let elbow = scratch.World[chain.Lower].Translation
                      let hand = scratch.World[chain.End].Translation
                      Expect.isLessThan elbow.Z (shoulder.Z + 0.001f) "elbow flexes behind the shoulder"
                      Expect.isLessThan hand.Y shoulder.Y "hand remains below shoulder"
                      Expect.isGreaterThan (shoulder.X * hand.X) 0.0f "hands remain on their own side"

                  Expect.isLessThan
                      scratch.World[rig.LeftArm.End].Translation.Z
                      scratch.World[rig.RightArm.End].Translation.Z
                      "arms counter the opposite legs")
              testCase
                  "both feet consume strike reception dribble and physical contacts without moving the root"
                  (fun _ ->
                      let path =
                          System.IO.Path.Combine(
                              System.AppContext.BaseDirectory,
                              "Assets",
                              "MatchViewer",
                              "player.glb"
                          )

                      let rig = (CharacterAsset.load path |> unwrap).Rig

                      for family in
                          [| MotionFamily.FootStrike
                             MotionFamily.Reception
                             MotionFamily.Dribble
                             MotionFamily.PhysicalContact |] do
                          for isLeft in [| true; false |] do
                              let runtime = MotionRuntime(config, identity)
                              let point = Vector3(0.20f, (if isLeft then 0.18f else -0.18f), 0.07f)

                              let task =
                                  { Sequence = 1UL
                                    TaskId = 7UL
                                    Actor = PlayerId 1
                                    Generation = 1
                                    Family = family
                                    Effector =
                                      if isLeft then
                                          MotionEffector.LeftFoot
                                      else
                                          MotionEffector.RightFoot
                                    Phase = MotionEvidencePhase.Prepared
                                    OccurredAt = 1<subtick>
                                    AvailableAt = 1<subtick>
                                    StartedAt = 1<subtick>
                                    CommitAt = 12<subtick>
                                    EndsAt = 20<subtick>
                                    PredictedPoint = point
                                    ConfirmedPoint = ValueNone
                                    BallCenter = point + Vector3(0.11f, 0.0f, 0.0f)
                                    IncomingVelocity = ValueNone
                                    OutgoingVelocity = ValueNone }

                              for tick in 0..12 do
                                  let original = boundary tick

                                  let records =
                                      if tick = 1 then
                                          [| task |]
                                      elif tick = 8 then
                                          [| { task with
                                                 Sequence = 2UL
                                                 Phase = MotionEvidencePhase.Confirmed
                                                 OccurredAt = 8<subtick>
                                                 AvailableAt = 8<subtick>
                                                 ConfirmedPoint = ValueSome point } |]
                                      else
                                          [||]

                                  let root =
                                      { original.Roots[0] with
                                          Position = Vector3.Zero
                                          Velocity = Vector3.Zero }

                                  runtime.Ingest
                                      { original with
                                          Roots = [| root |]
                                          Evidence = records }
                                  |> unwrap

                              let plan =
                                  runtime.TryPlan(PlayerId 1, { Tick = 12; Fraction = 0.0f })
                                  |> unwrap
                                  |> ValueOption.get

                              let scratch = PoseScratch rig
                              let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity

                              let result =
                                  PoseEvaluation.evaluateInto (
                                      config,
                                      rig,
                                      plan,
                                      { Tick = 12; Fraction = 0.0f },
                                      Vector3.Zero,
                                      MotionInput.facing plan.Root.Facing,
                                      scratch,
                                      palette,
                                      0
                                  )

                              let leg = if isLeft then rig.LeftLeg else rig.RightLeg
                              let actual = Vector3.Transform(leg.BallSocket, scratch.World[leg.End])

                              let expected =
                                  MotionInput.localPoint
                                      Vector3.Zero
                                      (MotionInput.facing plan.Root.Facing)
                                      (MotionInput.vector point)

                              Expect.isLessThan
                                  (Vector3.Distance(actual, expected))
                                  0.01f
                                  (sprintf "actual boot contact for %A, left=%b, residual=%A" family isLeft result)

                              Expect.equal plan.Root.Position Vector3.Zero "animation cannot move the sporting root")
              testCase "unreachable contact returns a residual and replacement cannot inherit the task" (fun _ ->
                  let path =
                      System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                  let rig = (CharacterAsset.load path |> unwrap).Rig
                  let runtime = MotionRuntime(config, identity)

                  let root =
                      { (boundary 0).Roots[0] with
                          Position = Vector3.Zero
                          Velocity = Vector3.Zero }

                  let task =
                      { Sequence = 1UL
                        TaskId = 9UL
                        Actor = root.Actor
                        Generation = 1
                        Family = MotionFamily.Reception
                        Effector = MotionEffector.RightFoot
                        Phase = MotionEvidencePhase.Prepared
                        OccurredAt = 1<subtick>
                        AvailableAt = 1<subtick>
                        StartedAt = 1<subtick>
                        CommitAt = 3<subtick>
                        EndsAt = 10<subtick>
                        PredictedPoint = Vector3(20.0f, 0.0f, 0.1f)
                        ConfirmedPoint = ValueNone
                        BallCenter = Vector3(20.1f, 0.0f, 0.1f)
                        IncomingVelocity = ValueNone
                        OutgoingVelocity = ValueNone }

                  for tick in 0..3 do
                      runtime.Ingest
                          { Tick = tick * 1<subtick>
                            Roots = [| root |]
                            Evidence = if tick = 1 then [| task |] else [||] }
                      |> unwrap

                  let plan =
                      runtime.TryPlan(root.Actor, { Tick = 3; Fraction = 0.0f })
                      |> unwrap
                      |> ValueOption.get

                  let scratch = PoseScratch rig
                  let output = Array.create rig.JointNodes.Length Matrix4x4.Identity

                  let struct (_, residual) =
                      PoseEvaluation.evaluateInto (
                          config,
                          rig,
                          plan,
                          { Tick = 3; Fraction = 0.0f },
                          Vector3.Zero,
                          MotionInput.facing root.Facing,
                          scratch,
                          output,
                          0
                      )

                  match residual with
                  | ValueSome(UnreachableContact(_, MotionEffector.RightFoot, distance)) ->
                      Expect.isGreaterThan distance 1.0f "impossible task is not manufactured"
                  | _ -> failtestf "expected unreachable contact, got %A" residual

                  runtime.Ingest
                      { Tick = 4<subtick>
                        Roots = [| { root with Generation = 2 } |]
                        Evidence = [||] }
                  |> unwrap

                  let replaced =
                      runtime.TryPlan(root.Actor, { Tick = 4; Fraction = 0.0f })
                      |> unwrap
                      |> ValueOption.get

                  Expect.isFalse
                      replaced.RightOverlay.Active
                      "a replacement generation cannot inherit the previous task")
              testCase
                  "foot task is retained, cancelled continuously, and late confirmation never delays commit"
                  (fun _ ->
                      let runtime = MotionRuntime(config, identity)

                      let evidence =
                          { Sequence = 1UL
                            TaskId = 12UL
                            Actor = PlayerId 1
                            Generation = 1
                            Family = MotionFamily.FootStrike
                            Effector = MotionEffector.LeftFoot
                            Phase = MotionEvidencePhase.Prepared
                            OccurredAt = 1<subtick>
                            AvailableAt = 1<subtick>
                            StartedAt = 1<subtick>
                            CommitAt = 12<subtick>
                            EndsAt = 20<subtick>
                            PredictedPoint = Vector3(0.2f, 0.0f, 0.1f)
                            ConfirmedPoint = ValueNone
                            BallCenter = Vector3(0.2f, 0.0f, 0.11f)
                            IncomingVelocity = ValueNone
                            OutgoingVelocity = ValueNone }

                      let stationary tick records =
                          let b = boundary tick

                          { b with
                              Roots =
                                  [| { b.Roots[0] with
                                         Position = Vector3.Zero
                                         Velocity = Vector3.Zero } |]
                              Evidence = records }

                      runtime.Ingest(stationary 0 [||]) |> unwrap
                      runtime.Ingest(stationary 1 [| evidence |]) |> unwrap

                      let get tick =
                          runtime.TryPlan(PlayerId 1, { Tick = tick; Fraction = 0.0f })
                          |> unwrap
                          |> ValueOption.get

                      let published = get 1
                      Expect.isTrue published.LeftOverlay.Active "prepared task controls the foot"
                      Expect.equal published.LeftOverlay.Preparation.Finish 12.0f "sporting commitment remains exact"

                      for tick in 2..4 do
                          runtime.Ingest(stationary tick [||]) |> unwrap

                      let before = get 4

                      let struct (beforePoint, beforeVelocity) =
                          TaskPlanner.overlayPoint config 5.0f before.LeftOverlay

                      let struct (beforeMix, beforeMixVelocity) =
                          TaskPlanner.socketState config.TickSeconds 5.0f before.LeftOverlay

                      let cancelled =
                          { evidence with
                              Sequence = 2UL
                              Phase = MotionEvidencePhase.Cancelled
                              OccurredAt = 5<subtick>
                              AvailableAt = 5<subtick> }

                      runtime.Ingest(stationary 5 [| cancelled |]) |> unwrap
                      let recovery = (get 5).LeftOverlay

                      let struct (afterPoint, afterVelocity) =
                          TaskPlanner.overlayPoint config 5.0f recovery

                      let struct (afterMix, afterMixVelocity) =
                          TaskPlanner.socketState config.TickSeconds 5.0f recovery

                      Expect.isLessThan
                          (Vector3.Distance(beforePoint, afterPoint))
                          0.00001f
                          "cancellation preserves the blended point"

                      Expect.isLessThan
                          (Vector3.Distance(beforeVelocity, afterVelocity))
                          0.00001f
                          "cancellation preserves point velocity"

                      Expect.isLessThan (abs (beforeMix - afterMix)) 0.00001f "socket remains continuous"

                      Expect.isLessThan
                          (abs (beforeMixVelocity - afterMixVelocity))
                          0.00001f
                          "socket velocity remains continuous"

                      let character =
                          CharacterAsset.load (
                              System.IO.Path.Combine(
                                  System.AppContext.BaseDirectory,
                                  "Assets",
                                  "MatchViewer",
                                  "player.glb"
                              )
                          )
                          |> unwrap

                      let rig = character.Rig
                      let scratch = PoseScratch rig
                      let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity

                      let pose plan =
                          PoseEvaluation.evaluateInto (
                              config,
                              rig,
                              plan,
                              { Tick = 5; Fraction = 0.0f },
                              Vector3.Zero,
                              MotionInput.facing plan.Root.Facing,
                              scratch,
                              palette,
                              0
                          )
                          |> ignore

                          scratch.World[rig.LeftLeg.End]

                      let ankleBefore = pose before
                      let ankleAfter = pose (get 5)

                      Expect.isLessThan
                          (Vector3.Distance(ankleBefore.Translation, ankleAfter.Translation))
                          0.001f
                          "cancellation cannot snap the boot pivot"

                      let deadline = recovery.Recovery.Finish

                      for tick in 6 .. int deadline + 1 do
                          runtime.Ingest(stationary tick [||]) |> unwrap

                      Expect.isFalse
                          (get (int deadline + 1)).LeftOverlay.Active
                          "cancellation recovery ends without extending its deadline"

                      Expect.equal (get 1) published "published plan never rewritten"

                      let late = MotionRuntime(config, identity)

                      for tick in 0..11 do
                          late.Ingest(stationary tick [||]) |> unwrap

                      let confirmation =
                          { evidence with
                              Phase = MotionEvidencePhase.Confirmed
                              OccurredAt = 12<subtick>
                              AvailableAt = 12<subtick>
                              ConfirmedPoint = ValueSome evidence.PredictedPoint }

                      late.Ingest(stationary 12 [| confirmation |]) |> unwrap

                      let latePlan =
                          late.TryPlan(PlayerId 1, { Tick = 12; Fraction = 0.0f })
                          |> unwrap
                          |> ValueOption.get

                      Expect.isFalse latePlan.LeftOverlay.Active "late confirmation does not invent preparation"
                      Expect.isGreaterThan late.ResidualCount 0UL "late confirmation is reported")
              testCase "seek reconstruction uses only available canonical boundaries from a complete block" (fun _ ->
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

                  // The sole height is whatever the live boot authors; sampling it keeps the planted-contact check
                  // tied to real sole vertices instead of one pack's constant.
                  let soleY = character.Batches[0].Vertices |> Array.map _.Position.Y |> Array.min

                  let sole =
                      character.Batches[0].Vertices
                      |> Array.filter (fun vertex ->
                          abs (vertex.Position.Y - soleY) < 0.00001f
                          && abs vertex.Position.X > 0.04f
                          && abs vertex.Position.X < 0.17f)

                  Expect.isGreaterThan sole.Length 0 "the test samples actual boot sole vertices"

                  for vertex in sole do
                      let joints, weights = vertex.Joints, vertex.Weights

                      let posed =
                          Vector3.Transform(vertex.Position, palette[int joints.X]) * weights.X
                          + Vector3.Transform(vertex.Position, palette[int joints.Y]) * weights.Y
                          + Vector3.Transform(vertex.Position, palette[int joints.Z]) * weights.Z
                          + Vector3.Transform(vertex.Position, palette[int joints.W]) * weights.W

                      Expect.isLessThan (abs posed.Y) 0.001f "the skinned sole is within one millimetre of ground"

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

                  let body = measure (fun () -> BodyPose.initialize config rig plan scratch.Local)

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
              testCase "swing retarget preserves foot orientation and skinned boot vertices" (fun _ ->
                  let path =
                      System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                  let character = CharacterAsset.load path |> unwrap
                  let rig = character.Rig

                  let root =
                      { (boundary 0).Roots[0] with
                          Position = Vector3.Zero
                          Velocity = Vector3.Zero }

                  let initial = SupportPlanner.initialize config 0 root

                  let segment =
                      { Start = 0.0f
                        Finish = 10.0f
                        From = initial.Left.Anchor
                        Target = initial.Left.Anchor + Vector3(0.2f, 0.0f, 0.0f)
                        FromVelocity = Vector3.Zero
                        TargetVelocity = Vector3.Zero
                        Clearance = config.Clearance }

                  let foot =
                      { initial.Left with
                          Phase = SupportPhase.Swing
                          ReleaseAt = 0.0f
                          Segment = segment }

                  let previous = { initial with Left = foot }

                  let next =
                      { previous with
                          Left =
                              { foot with
                                  Segment =
                                      Trajectory.retarget
                                          config.TickSeconds
                                          4.0f
                                          (segment.Target + Vector3(0.04f, 0.0f, 0.0f))
                                          10.0f
                                          segment } }

                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity

                  let sample plan =
                      PoseEvaluation.evaluateInto (
                          config,
                          rig,
                          plan,
                          { Tick = 4; Fraction = 0.0f },
                          Vector3.Zero,
                          MotionInput.facing root.Facing,
                          scratch,
                          palette,
                          0
                      )
                      |> ignore

                      let ankle = scratch.World[rig.LeftLeg.End]
                      let joint = rig.JointNodes |> Array.findIndex ((=) rig.LeftLeg.End)

                      let vertex =
                          character.Batches[0].Vertices
                          |> Array.find (fun v -> int v.Joints.X = joint && v.Weights.X > 0.8f)

                      struct (ankle, Vector3.Transform(vertex.Position, palette[joint]))

                  let struct (before, beforeVertex) = sample previous
                  let struct (after, afterVertex) = sample next

                  Expect.isLessThan
                      (Vector3.Distance(before.Translation, after.Translation))
                      0.0001f
                      "ankle position has no retarget snap"

                  Expect.isLessThan
                      (Vector3.Distance(beforeVertex, afterVertex))
                      0.0001f
                      "boot vertex has no retarget snap")
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
