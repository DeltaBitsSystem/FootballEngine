namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ChainSequenceTests =

    let private ctx = TestBuilders.defaultCtx ()
    let private systems = Scheduler.systems

    let private consecutivePasses (events: MatchEvent list) (side: ClubSide) : int =
        let homeIds = ctx.HomeLineup.Profiles |> Array.map (fun p -> p.Id) |> Set.ofArray

        let awayIds = ctx.AwayLineup.Profiles |> Array.map (fun p -> p.Id) |> Set.ofArray

        let isSide (id: PlayerId) =
            match side with
            | ClubSide.Home -> homeIds.Contains id
            | ClubSide.Away -> awayIds.Contains id

        let rec loop best current (prevRecv: PlayerId option) evs =
            match evs with
            | [] -> max best current
            | ev :: rest ->
                match ev.Data with
                | EventData.Pass(passer, Some recv, _, _, true, _, _, _, _) when isSide passer ->
                    if prevRecv.IsSome && passer = prevRecv.Value then
                        loop best (current + 1) (Some recv) rest
                    else
                        loop (max best current) 1 (Some recv) rest
                | _ -> loop best current prevRecv rest

        loop 0 0 None events

    let chainSequenceTests =
        testList
            "FootballEngine.Core.Systems.PlayerDecision.Chain.Sequence"
            [ testCase "multi-pass sequences occur in seeded matches" (fun _ ->
                  for seed in [ 1UL; 2UL; 3UL; 4UL; 5UL ] do
                      let out =
                          Simulator.runMatch systems ctx seed Simulator.RunMode.Fast
                          |> TestBuilders.requireMatchOutcome

                      let maxHome = consecutivePasses out.Events ClubSide.Home
                      let maxAway = consecutivePasses out.Events ClubSide.Away
                      let maxRun = max maxHome maxAway

                      Expect.isGreaterThanOrEqual
                          maxRun
                          3
                          (sprintf "seed %d: at least 3+ consecutive pass sequence" seed))

              testCase "statistical windows stay in tolerance" (fun _ ->
                  let homePasses = ResizeArray()
                  let awayPasses = ResizeArray()

                  for seed in [ 101UL; 102UL; 103UL; 104UL; 105UL ] do
                      let out =
                          Simulator.runMatch systems ctx seed Simulator.RunMode.Fast
                          |> TestBuilders.requireMatchOutcome

                      homePasses.Add out.Final.Home.Stats.PassesCompleted
                      awayPasses.Add out.Final.Away.Stats.PassesCompleted

                  let avgHome = homePasses |> Seq.averageBy float
                  let avgAway = awayPasses |> Seq.averageBy float

                  Expect.isGreaterThan avgHome 100.0 "home passes completed average > 100"
                  Expect.isGreaterThan avgAway 100.0 "away passes completed average > 100"
                  Expect.isLessThan avgHome 1500.0 "home passes completed average remains bounded"
                  Expect.isLessThan avgAway 1500.0 "away passes completed average remains bounded")

              testCase "same seed produces same event hash" (fun _ ->
                  let a =
                      Simulator.runMatch systems ctx 777UL Simulator.RunMode.Fast
                      |> TestBuilders.requireMatchOutcome

                  let b =
                      Simulator.runMatch systems ctx 777UL Simulator.RunMode.Fast
                      |> TestBuilders.requireMatchOutcome

                  Expect.equal a.EventsHash b.EventsHash "deterministic hash"
                  Expect.equal a.Events.Length b.Events.Length "deterministic count") ]
