module FootballEngine.Tests.Layer2.StatisticalContractsTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Diagnostics.MatchDiagnoster

let private fullMatch = 10 * 40 * 60

let private baseOutfield =
    [| makePlayer 2 DL 13
       makePlayer 3 DC 13
       makePlayer 4 DC 13
       makePlayer 5 DR 13
       makePlayer 6 DM 13
       makePlayer 7 MC 13
       makePlayer 8 MC 13
       makePlayer 9 AML 14
       makePlayer 10 AMC 14
       makePlayer 11 ST 15 |]

let private basePositions =
    [| 50.0, 34.0
       35.0, 10.0
       38.0, 34.0
       38.0, 54.0
       35.0, 68.0
       45.0, 34.0
       52.0, 20.0
       52.0, 54.0
       60.0, 10.0
       62.0, 34.0
       70.0, 34.0 |]

let private runFullMatch (homePlayers: Player[]) (awayPlayers: Player[]) (homeTactics: TeamTactics option) =
    try
        let ctx, state = buildSimState homePlayers basePositions awayPlayers basePositions
        state.Flow <- MatchFlow.Live

        match homeTactics with
        | Some t -> state.Home.Tactics <- t
        | None -> ()

        let final, events = advanceTicks fullMatch ctx state
        Some(final, events)
    with _ ->
        None

let private runMatchBatch (n: int) (homePlayers: Player[]) (awayPlayers: Player[]) (homeTactics: TeamTactics option) =
    Array.Parallel.init n (fun _ -> runFullMatch homePlayers awayPlayers homeTactics)
    |> Array.choose id

let private countWhere f xs = xs |> Seq.filter f |> Seq.length

let private pct num den =
    if den = 0 then
        "N/A"
    else
        sprintf "%.1f%%" (float num / float den * 100.0)

let private avg total n =
    if n = 0 then 0.0 else float total / float n

let statisticalContractsTests =
    testList
        "StatisticalContracts"
        [ testCase "all statistical contracts hold over balanced matches"
          <| fun () ->
              let b = batchBalanced.Value

              let avgGoals = b.AvgGoalsPer90
              Expect.isGreaterThanOrEqual avgGoals 1.5 $"avg goals/match = {avgGoals:F2}, expected >= 1.5"
              Expect.isLessThanOrEqual avgGoals 10.0 $"avg goals/match = {avgGoals:F2}, expected <= 10.0"
              Expect.equal b.GoalsWithNoShot 0 $"goals with zero ShotLaunched = {b.GoalsWithNoShot}"

              let avgShotEvents = b.AvgShotsPer90

              Expect.isGreaterThanOrEqual
                  avgShotEvents
                  5.0
                  $"avg shot-pipeline events/match = {avgShotEvents:F1}, expected >= 5"

              if b.ShotConversionPct > 0.0 then
                  Expect.isLessThanOrEqual
                      b.ShotConversionPct
                      25.0
                      $"shot conversion = {b.ShotConversionPct:F1}%%, expected <= 25%%"

              let avgPasses = b.AvgPassesPer90
              Expect.isGreaterThanOrEqual avgPasses 20.0 $"avg completed passes/match = {avgPasses:F1}, expected >= 20"

              if b.PassAccuracyPct > 0.0 then
                  Expect.isGreaterThanOrEqual
                      b.PassAccuracyPct
                      55.0
                      $"pass completion = {b.PassAccuracyPct:F1}%%, expected >= 55%%"

                  Expect.isLessThanOrEqual
                      b.PassAccuracyPct
                      95.0
                      $"pass completion = {b.PassAccuracyPct:F1}%%, expected <= 95%%"

              Expect.isGreaterThanOrEqual
                  b.AvgCornersPer90
                  2.0
                  $"avg corners/match = {b.AvgCornersPer90:F2}, expected >= 2"

              Expect.isGreaterThanOrEqual b.AvgFoulsPer90 5.0 $"avg fouls/match = {b.AvgFoulsPer90:F2}, expected >= 5"

              Expect.isGreaterThan
                  b.HomeWins
                  b.AwayWins
                  $"home wins = {b.HomeWins}, away wins = {b.AwayWins}: expected home advantage"

          testCase "dominant team wins majority over weak team"
          <| fun () ->
              let b = batchStrongVsWeak.Value
              let winPct = float b.HomeWins / float b.MatchCount * 100.0
              Expect.isGreaterThanOrEqual winPct 60.0 $"strong team wins {winPct:F0}%%, expected >= 60%%"

          testCase "tactics affect shot/pass distribution"
          <| fun () ->
              let homePlayers = Array.append [| makeGk 1 13 13 13 |] baseOutfield
              let awayPlayers = Array.append [| makeGk 12 13 13 13 |] baseOutfield

              let countShotEvents (events: MatchEvent list) =
                  events
                  |> List.filter (fun e ->
                      match e.Type with
                      | MatchEventType.ShotLaunched
                      | MatchEventType.ShotOnTarget
                      | MatchEventType.ShotOffTarget
                      | MatchEventType.ShotBlocked -> true
                      | _ -> false)
                  |> List.length

              let attResults =
                  runMatchBatch 3 homePlayers awayPlayers (Some TeamTactics.Attacking)

              let defResults =
                  runMatchBatch 3 homePlayers awayPlayers (Some TeamTactics.Defensive)

              let attShots = attResults |> Array.sumBy (fun (_, events) -> countShotEvents events)
              let defShots = defResults |> Array.sumBy (fun (_, events) -> countShotEvents events)

              Expect.isGreaterThan
                  attShots
                  defShots
                  $"attacking shots ({attShots}) should exceed defensive shots ({defShots})" ]
