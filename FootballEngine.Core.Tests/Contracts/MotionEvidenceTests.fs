namespace FootballEngine.Core.Tests.Contracts

open System.Numerics
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module MotionEvidenceTests =
    let private record tick phase =
        { Sequence = 0UL
          TaskId = 1UL
          Actor = PlayerId 7
          Generation = 0
          Family = MotionFamily.FootStrike
          Effector = MotionEffector.LeftFoot
          Phase = phase
          OccurredAt = tick * 1<subtick>
          AvailableAt = (tick + 1) * 1<subtick>
          StartedAt = 1<subtick>
          CommitAt = 4<subtick>
          EndsAt = 7<subtick>
          PredictedPoint = Vector3.Zero
          ConfirmedPoint = ValueNone
          BallCenter = Vector3.Zero
          IncomingVelocity = ValueNone
          OutgoingVelocity = ValueNone }

    let tests =
        testList
            "MotionEvidence"
            [ testCase "indexed archive roundtrip reconstructs at most 128 canonical boundaries" (fun _ ->
                  use writer = new MotionTrack.Writer()

                  for tick = 0 to 256 do
                      let boundary =
                          { Tick = tick * 1<subtick>
                            Roots = [||]
                            Evidence = [| record tick MotionEvidencePhase.Confirmed |] }

                      Expect.equal (writer.Append boundary) (Ok()) "contiguous boundary accepted"

                  let bytes = writer.Complete()

                  match MotionTrack.readBlock bytes 130<subtick> with
                  | Error message -> failtest message
                  | Ok boundaries ->
                      Expect.equal boundaries.Length 128 "seek work bounded by index interval"
                      Expect.equal boundaries[0].Tick 128<subtick> "preceding block"

                      Expect.equal
                          boundaries[2].Evidence[0]
                          (record 130 MotionEvidencePhase.Confirmed)
                          "all evidence fields survive"

                  let incompatible = Array.copy bytes
                  incompatible[0] <- 0uy
                  Expect.isError (MotionTrack.readBlock incompatible 0<subtick>) "no legacy reader")
              testCase "motion archive rejects overlapping missing and truncated index ranges" (fun _ ->
                  use writer = new MotionTrack.Writer()

                  for tick = 0 to 128 do
                      writer.Append
                          { Tick = tick * 1<subtick>
                            Roots = [||]
                            Evidence = [||] }
                      |> ignore

                  let bytes = writer.Complete()

                  let edit (offset: int) value =
                      let damaged = Array.copy bytes
                      System.BitConverter.GetBytes(value: int).CopyTo(damaged, offset)
                      damaged

                  for damaged in
                      [| edit 28 127
                         edit 32 129
                         edit 36 44
                         edit 16 127
                         edit 24 0
                         Array.append bytes [| 0uy |]
                         bytes[0 .. bytes.Length - 2] |] do
                      Expect.isError
                          (MotionTrack.readBlock damaged 0<subtick>)
                          "entire index is checked even when seeking the first block"

                  Expect.isError (MotionTrack.readBlock bytes -1<subtick>) "negative seek is rejected")
              testCase "live and replay capture every tick regardless of pose cadence and batching" (fun _ ->
                  let input =
                      { MatchId = 17UL
                        Context = TestBuilders.defaultCtx ()
                        Seed = 17UL
                        EffectiveConfig = EffectiveConfig.defaultConfig
                        EngineVersion = "motion-fixture" }

                  let unwrap =
                      function
                      | Ok value -> value
                      | Error error -> failtestf "%A" error

                  let live =
                      MatchSession.create
                          Scheduler.systems
                          input
                          (MatchRunMode.Live
                              { LiveExecutionPolicy.defaultPolicy with
                                  PoseEvery = 8<tickDelta> })
                      |> unwrap

                  let replay =
                      MatchSession.create Scheduler.systems input (MatchRunMode.Replay 16<tickDelta>)
                      |> unwrap

                  let batch = MatchSession.advanceTo 64<subtick> replay |> unwrap
                  let incremental = ResizeArray<MotionBoundary>()

                  for frontier = 1 to 64 do
                      let advance = MatchSession.advanceTo (frontier * 1<subtick>) live |> unwrap
                      incremental.AddRange advance.Motion

                  Expect.equal
                      batch.Motion
                      (incremental.ToArray())
                      "canonical evidence and roots independent of transport cadence"

                  Expect.equal batch.Motion.Length 64 "no lost boundaries between snapshots"
                  Expect.equal live.Rng replay.Rng "motion capture consumes no sports RNG")
              testCase "complete archive does not reveal unavailable confirmation" (fun _ ->
                  let track = MotionEvidenceJournal(8)
                  track.Append(record 0 MotionEvidencePhase.Prepared) |> ignore
                  track.Append(record 3 MotionEvidencePhase.Confirmed) |> ignore
                  let output = Array.zeroCreate<MotionEvidence> 8

                  Expect.equal
                      (track.Read(1UL, 0<subtick>, output))
                      (Ok(struct (1UL, 0)))
                      "no evidence before publishing"

                  Expect.equal
                      (track.Read(1UL, 1<subtick>, output))
                      (Ok(struct (2UL, 1)))
                      "only preparation is available"

                  Expect.equal
                      output[0].Phase
                      MotionEvidencePhase.Prepared
                      "future contact cannot influence preparation"

                  Expect.equal
                      (track.Read(2UL, 3<subtick>, output))
                      (Ok(struct (2UL, 0)))
                      "system occurrence is not publication"

                  Expect.equal (track.Read(2UL, 4<subtick>, output)) (Ok(struct (3UL, 1))) "exact publication frontier")
              testCase "batching and incremental reads preserve all contacts between snapshots" (fun _ ->
                  let track = MotionEvidenceJournal(8)

                  for tick = 0 to 5 do
                      track.Append(record tick MotionEvidencePhase.Confirmed) |> ignore

                  let batch = Array.zeroCreate<MotionEvidence> 8
                  let single = Array.zeroCreate<MotionEvidence> 1
                  Expect.equal (track.Read(1UL, 6<subtick>, batch)) (Ok(struct (7UL, 6))) "all contacts retained"
                  let mutable cursor = 1UL

                  for frontier = 1 to 6 do
                      Expect.equal
                          (track.Read(cursor, frontier * 1<subtick>, single))
                          (Ok(struct (cursor + 1UL, 1)))
                          "one canonical boundary"

                      Expect.equal single[0] batch[frontier - 1] "batching cannot alter evidence"
                      cursor <- cursor + 1UL)
              testCase "late revision and cancellation append without rewriting published preparation" (fun _ ->
                  let track = MotionEvidenceJournal(8)
                  track.Append(record 0 MotionEvidencePhase.Prepared) |> ignore
                  let output = Array.zeroCreate<MotionEvidence> 8
                  track.Read(1UL, 1<subtick>, output) |> ignore
                  let original = output[0]
                  track.Append(record 6 MotionEvidencePhase.Confirmed) |> ignore
                  track.Append(record 7 MotionEvidencePhase.Cancelled) |> ignore
                  track.Read(1UL, 8<subtick>, output) |> ignore
                  Expect.equal output[0] original "past record remains immutable"
                  Expect.equal output[1].AvailableAt 7<subtick> "late confirmation is available only now"
                  Expect.equal output[2].Phase MotionEvidencePhase.Cancelled "cancel is its own occurrence")
              testCase "overflow requires explicit recovery rather than silently dropping tasks" (fun _ ->
                  let track = MotionEvidenceJournal(2)

                  for tick = 0 to 3 do
                      track.Append(record tick MotionEvidencePhase.Confirmed) |> ignore

                  let output = Array.zeroCreate<MotionEvidence> 2
                  Expect.equal (track.Read(1UL, 4<subtick>, output)) (Error(MotionOverflow 3UL)) "caller must recover"

                  Expect.equal
                      (track.Read(6UL, 4<subtick>, output))
                      (Error MotionFutureCursor)
                      "invalid cursor rejected") ]
