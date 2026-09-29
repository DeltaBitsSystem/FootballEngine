namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module LivePresentationTests =
    let private input () =
        { MatchId = 77UL
          Context = TestBuilders.defaultCtx ()
          Seed = 0xBEEFUL
          EffectiveConfig = EffectiveConfig.defaultConfig
          EngineVersion = "live-presentation-tests" }

    let private run policy =
        MatchSession.create Scheduler.systems (input ()) (MatchRunMode.Live policy)
        |> function
            | Error error -> failtestf "Could not create live session: %A" error
            | Ok session ->
                MatchSession.finish session
                |> function
                    | Ok outcome -> outcome
                    | Error error -> failtestf "Could not finish live session: %A" error

    let tests =
        testList
            "FootballEngine.Core.Contracts.LivePresentation"
            [ testCase "presentation policy does not change deterministic outcome" (fun _ ->
                  let first =
                      { LiveExecutionPolicy.defaultPolicy with
                          WorkerSlice = 4<tickDelta>
                          PoseEvery = 4<tickDelta>
                          SemanticEvery = 10<tickDelta> }

                  let second =
                      { LiveExecutionPolicy.defaultPolicy with
                          WorkerSlice = 80<tickDelta>
                          PoseEvery = 8<tickDelta>
                          SemanticEvery = 40<tickDelta> }

                  let a = run first
                  let b = run second
                  Expect.equal a.EventsHash b.EventsHash "presentation schedules preserve event hash"
                  Expect.equal a.Final.State b.Final.State "presentation schedules preserve terminal state")

              testCase "invalid live presentation values are rejected" (fun _ ->
                  let invalid =
                      { LiveExecutionPolicy.defaultPolicy with
                          WorkerSlice = 0<tickDelta> }

                  match MatchSession.create Scheduler.systems (input ()) (MatchRunMode.Live invalid) with
                  | Error(InvalidRunMode message) ->
                      Expect.stringContains message "worker" "the policy error identifies the invalid field"
                  | other -> failtestf "expected policy validation failure, got %A" other)

              testCase "checkpoint roundtrip retains the live policy" (fun _ ->
                  let policy =
                      { LiveExecutionPolicy.defaultPolicy with
                          WorkerSlice = 12<tickDelta>
                          PoseEvery = 4<tickDelta>
                          SemanticEvery = 20<tickDelta>
                          BufferCapacity = 12
                          PresentationDelay = TimeSpan.FromMilliseconds 175.0 }

                  let session =
                      MatchSession.create Scheduler.systems (input ()) (MatchRunMode.Live policy)
                      |> function
                          | Ok value -> value
                          | Error error -> failtestf "could not create session: %A" error

                  let checkpoint = MatchSession.checkpoint session

                  let restored =
                      MatchSession.deserializeCheckpoint (MatchSession.serializeCheckpoint checkpoint)

                  match restored with
                  | Ok value ->
                      Expect.equal value.Mode (MatchRunMode.Live policy) "live policy is serialized explicitly"
                  | Error error -> failtestf "checkpoint did not roundtrip: %A" error) ]
