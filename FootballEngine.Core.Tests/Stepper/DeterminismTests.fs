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
            [ testCase "Same seed reproduces the same match" (fun _ ->
                  let a = Simulator.runMatch systems ctx 0xDEADBEEFUL Simulator.RunMode.Fast None
                  let b = Simulator.runMatch systems ctx 0xDEADBEEFUL Simulator.RunMode.Fast None

                  Expect.equal a.EventsHash b.EventsHash "event hash identical"
                  Expect.equal a.Events.Length b.Events.Length "event count identical"
                  Expect.equal a.Final.State.HomeScore b.Final.State.HomeScore "home score"
                  Expect.equal a.Final.State.AwayScore b.Final.State.AwayScore "away score"
                  Expect.equal a.Final.State.Tick b.Final.State.Tick "final tick")

              testCase "Match runs to a terminal state" (fun _ ->
                  let out = Simulator.runMatch systems ctx 42UL Simulator.RunMode.Fast None

                  Expect.equal out.Final.State.Flow MatchFlow.MatchEnded "match ended"
                  Expect.isLessThan (int out.Final.State.Tick) 400000 "guard not hit"
                  Expect.isTrue (out.Final.State.HomeScore >= 0) "home score sane"
                  Expect.isTrue (out.Final.State.AwayScore >= 0) "away score sane")

              testCase "Replay mode returns serializable snapshots" (fun _ ->
                  let out =
                      Simulator.runMatch systems ctx 7UL (Simulator.RunMode.Replay 4000<tickDelta>) None

                  match out.Replay with
                  | None -> failtest "replay data expected"
                  | Some replay ->
                      Expect.isNonEmpty replay.Snapshots "snapshots present"
                      Expect.equal ((Replay.read replay |> Seq.head).Tick) 0<subtick> "first snapshot at kickoff") ]
