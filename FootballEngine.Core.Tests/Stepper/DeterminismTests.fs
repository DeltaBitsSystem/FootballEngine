namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DeterminismTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private systems = Scheduler.systems

    let determinismTests =
        testList
            "FootballEngine.Core.Stepper.DeterminismTests"
            [ testCase "Invalid live policy returns an explicit error without throwing" (fun _ ->
                  let policy =
                      { LiveExecutionPolicy.defaultPolicy with
                          WorkerSlice = 0<tickDelta> }

                  match Simulator.runMatch systems ctx 42UL (Simulator.RunMode.Live policy) with
                  | Error(InvalidRunMode _) -> ()
                  | result -> failtestf "expected InvalidRunMode, got %A" result)
              testCase "Same seed reproduces the same match" (fun _ ->
                  let a =
                      Simulator.runMatch systems ctx 0xDEADBEEFUL Simulator.RunMode.Fast
                      |> TestBuilders.requireMatchOutcome

                  let b =
                      Simulator.runMatch systems ctx 0xDEADBEEFUL Simulator.RunMode.Fast
                      |> TestBuilders.requireMatchOutcome

                  Expect.equal a.EventsHash b.EventsHash "event hash identical"
                  Expect.equal a.Events.Length b.Events.Length "event count identical"
                  Expect.equal a.Final.State.HomeScore b.Final.State.HomeScore "home score"
                  Expect.equal a.Final.State.AwayScore b.Final.State.AwayScore "away score"
                  Expect.equal a.Final.State.Tick b.Final.State.Tick "final tick")

              testCase "Match runs to a terminal state" (fun _ ->
                  let out =
                      Simulator.runMatch systems ctx 42UL Simulator.RunMode.Fast
                      |> TestBuilders.requireMatchOutcome

                  Expect.equal out.Final.State.Flow MatchFlow.MatchEnded "match ended"
                  Expect.isLessThan (int out.Final.State.Tick) 400000 "guard not hit"
                  Expect.isTrue (out.Final.State.HomeScore >= 0) "home score sane"
                  Expect.isTrue (out.Final.State.AwayScore >= 0) "away score sane")

              testCase "Replay mode returns serializable snapshots" (fun _ ->
                  let out =
                      Simulator.runMatch systems ctx 7UL (Simulator.RunMode.Archive 4000<tickDelta>)
                      |> TestBuilders.requireMatchOutcome

                  match out.Replay with
                  | None -> failtest "replay data expected"
                  | Some replay ->
                      Expect.isNonEmpty replay.Snapshots "snapshots present"
                      let first = Replay.read replay |> Seq.head
                      Expect.equal first.Tick 0<subtick> "first snapshot at kickoff"

                      let firstId =
                          match first.HomePlayerIds[0] with
                          | FootballEngine.Core.Match.PlayerId id -> id

                      let secondId =
                          match first.HomePlayerIds[1] with
                          | FootballEngine.Core.Match.PlayerId id -> id

                      let duplicateIds = Array.copy first.HomePlayerIds
                      duplicateIds[1] <- duplicateIds[0]

                      let duplicateRoster =
                          { first with
                              HomePlayerIds = duplicateIds }

                      let duplicateRosterRejected =
                          try
                              Replay.write [ duplicateRoster ] |> ignore
                              false
                          with :? System.ArgumentException ->
                              true

                      Expect.isTrue duplicateRosterRejected "duplicate stable player IDs are rejected at write time"

                      let duplicateHeader = Array.copy replay.Snapshots

                      System.BitConverter.GetBytes(firstId)
                      |> fun value -> Array.blit value 0 duplicateHeader 32 4

                      let duplicateHeaderRejected =
                          try
                              Replay.readFrame
                                  { replay with
                                      Snapshots = duplicateHeader
                                      HomePlayerIds = duplicateIds }
                                  0
                              |> ignore

                              false
                          with :? System.ArgumentException ->
                              true

                      Expect.isTrue duplicateHeaderRejected "duplicate roster IDs are rejected at read time"

                      Expect.notEqual firstId secondId "the source fixture contains distinct player IDs") ]
