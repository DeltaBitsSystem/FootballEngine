namespace FootballEngine.Core.Tests.Stepper

open Expecto
open Microsoft.FSharp.Reflection
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module CognitionThreadTests =

    let private session seed =
        let input =
            { MatchId = seed
              Context = TestBuilders.defaultCtx ()
              Seed = seed
              EffectiveConfig = EffectiveConfig.defaultConfig
              EngineVersion = "tests" }

        match MatchSession.create Scheduler.systems input MatchRunMode.Fast with
        | Ok session -> session
        | Error error -> failtestf "session rejected: %A" error

    let private names (fields: System.Reflection.PropertyInfo[]) = fields |> Array.map _.Name |> Set.ofArray

    let ownershipTests =
        testList
            "FootballEngine.Core.Stepper.CognitionOwnership"
            [ testCase "every frame field is either copied for cognition or shared on purpose" (fun _ ->
                  let declared = names (FSharpType.GetRecordFields typeof<TeamFrame>)
                  let body = Set.ofList CognitionView.bodyFrameFields
                  let shared = Set.ofList CognitionView.sharedFrameFields
                  Expect.isEmpty (Set.intersect body shared) "no frame field has two owners"
                  Expect.equal (Set.union body shared) declared "a new frame field must be given an owner in CognitionView")

              testCase "every team field is either the body's, fixed, or handed back from cognition" (fun _ ->
                  let declared = names (FSharpType.GetRecordFields typeof<TeamWorld>)

                  let lists =
                      [ CognitionView.bodyTeamFields; CognitionView.fixedTeamFields; CognitionView.cognitionTeamFields ]
                      |> List.map Set.ofList

                  Expect.equal (lists |> List.sumBy Set.count) (Set.unionMany lists |> Set.count) "no team field has two owners"
                  Expect.equal (Set.unionMany lists) declared "a new team field must be given an owner in CognitionView") ]

    let matchTests =
        testList
            "FootballEngine.Core.Stepper.CognitionThread"
            [ testCase "cognition on its own thread plays the same match, tick by tick" (fun _ ->
                  // Any write cognition makes outside what it owns, or any read of state the body changes during a
                  // tick, makes the threaded match drift from the one-thread match: the first such tick is reported.
                  let oneThread = session 11UL
                  let ownThread = session 11UL
                  MatchSession.runCognitionOnOwnThread ownThread

                  try
                      while oneThread.World.State.Flow <> MatchFlow.MatchEnded do
                          let tick = oneThread.World.State.Tick + 1<subtick>

                          let one =
                              MatchSession.advanceTo tick oneThread |> Result.defaultWith (fun e -> failtestf "%A" e)

                          let two =
                              MatchSession.advanceTo tick ownThread |> Result.defaultWith (fun e -> failtestf "%A" e)

                          if one.Events <> two.Events then
                              failtestf
                                  "tick %d diverges\n  one thread: %A\n  own thread: %A"
                                  (int tick)
                                  (one.Events |> Array.map _.Data)
                                  (two.Events |> Array.map _.Data)

                      Expect.equal ownThread.World.State.Flow MatchFlow.MatchEnded "both matches end together"
                      Expect.equal ownThread.World.State.HomeScore oneThread.World.State.HomeScore "same home score"
                      Expect.equal ownThread.World.State.AwayScore oneThread.World.State.AwayScore "same away score"
                  finally
                      MatchSession.releaseCognition ownThread) ]
