module FootballEngine.Tests.Diagnostics.DiagnosticTests

open Expecto
open FootballEngine.Tests.Diagnostics.FootballRanges
open FootballEngine.Tests.Diagnostics.MatchDiagnoster

let private check (r: Range) (value: float) =
    if value < r.Low then
        failtestf "FAIL: %s = %.1f (expected %.0f-%.0f). %s" r.Name value r.Low r.High r.FailLowHint
    elif value > r.High then
        failtestf "FAIL: %s = %.1f (expected %.0f-%.0f). %s" r.Name value r.Low r.High r.FailHighHint
    else
        printfn "PASS: %s = %.1f" r.Name value

let diagnosticTests =
    testList
        "Diagnostics"
        [ test "1. goals per match are realistic" {
              let b = batchBalanced.Value
              check goals b.AvgGoalsPer90
          }

          test "2. shot conversion is realistic" {
              let b = batchBalanced.Value
              check shotConversion b.ShotConversionPct
          }

          test "3. goals without shots = 0" {
              let b = batchBalanced.Value

              if b.GoalsWithNoShot > 0 then
                  failtestf "FAIL: GoalsWithNoShot = %d (expected 0). Goals bypassing ShotAction" b.GoalsWithNoShot
              else
                  printfn "PASS: GoalsWithNoShot = 0"
          }

          test "4. pass accuracy is realistic" {
              let b = batchBalanced.Value
              check passAccuracy b.PassAccuracyPct
          }

          test "5. pass volume is realistic" {
              let b = batchBalanced.Value
              check passVolume b.AvgPassesPer90
          }

          test "6. corners are realistic" {
              let b = batchBalanced.Value
              check corners b.AvgCornersPer90
          }

          test "7. fouls are realistic" {
              let b = batchBalanced.Value
              check fouls b.AvgFoulsPer90
          }

          test "8. dribbles are realistic" {
              let b = batchBalanced.Value
              check dribbles b.AvgDribblesPer90
          }

          test "9. possession split is balanced" {
              let b = batchBalanced.Value
              check possessionHome b.PossessionHomePct
          }

          test "10. home advantage exists" {
              let b = batchBalanced.Value

              if b.HomeWins <= b.AwayWins then
                  failtestf "FAIL: home wins %d, away wins %d (expected home > away)" b.HomeWins b.AwayWins
              else
                  printfn "PASS: home wins %d, away wins %d, draws %d" b.HomeWins b.AwayWins b.Draws
          }

          test "11. strong team beats weak team" {
              let b = batchStrongVsWeak.Value
              let winPct = float b.HomeWins / float b.MatchCount * 100.0

              if winPct < 60.0 then
                  failtestf "FAIL: strong team wins %.0f%% (expected >60%%). %s" winPct goals.FailLowHint
              else
                  printfn "PASS: strong team wins %.0f%% (%d/%d)" winPct b.HomeWins b.MatchCount
          }

          test "12. GK quality matters" {
              let weak = batchWeakGK.Value
              let elite = batchEliteGK.Value
              let weakConceded = weak.AvgGoalsPer90
              let eliteConceded = elite.AvgGoalsPer90

              if weakConceded <= eliteConceded then
                  failtestf
                      "FAIL: weak GK conceded %.1f, elite GK conceded %.1f (expected weak > elite)"
                      weakConceded
                      eliteConceded
              else
                  printfn "PASS: weak GK conceded %.1f, elite GK conceded %.1f" weakConceded eliteConceded
          }

          test "13. shots volume is realistic" {
              let b = batchBalanced.Value
              check shots b.AvgShotsPer90
          }

          test "14. losing team pushes" {
              let b = runLosingPushMatch ()
              Expect.isGreaterThan b.AvgShotsPer90 5.0 $"losing team should generate shots, got {b.AvgShotsPer90}"
          }

          test "15. ball stays in bounds" {
              let b = batchBalanced.Value

              if not b.BallInBounds then
                  failtestf "FAIL: ball went out of bounds. check BallPhysics pitch clamping"
              else
                  printfn "PASS: ball stayed in bounds"
          }

          test "16. players move" {
              let b = batchBalanced.Value

              if not b.PlayersMoved then
                  failtestf "FAIL: players didn't move. check MovementEngine, PhysicsSystem"
              else
                  printfn "PASS: players moved during match"
          }

          test "17. fatigue accumulates" {
              let b = batchBalanced.Value

              if not b.FatigueAccumulated then
                  failtestf "FAIL: no fatigue accumulated. check FatiguePipeline"
              else
                  printfn "PASS: fatigue accumulated"
          }

          test "18. match reaches fulltime" {
              let b = batchBalanced.Value

              if not b.MatchReachedFulltime then
                  failtestf "FAIL: match didn't reach fulltime. check MatchStepper flow transitions"
              else
                  printfn "PASS: match reached fulltime"
          }

          test "19. no crashes in 10 matches" {
              let b = batchBalanced.Value

              if b.Exceptions > 0 then
                  failtestf "FAIL: %d exceptions in %d matches" b.Exceptions b.MatchCount
              else
                  printfn "PASS: 0 exceptions in %d matches" b.MatchCount
          }

          test "20. result distribution is realistic over 10 matches" {
              let b = batchBalanced10.Value
              let n = float b.MatchCount
              let drawPct = float b.Draws / n * 100.0
              let homeWinPct = float b.HomeWins / n * 100.0

              printfn "RESULTS: Home=%d Away=%d Draws=%d (n=%d)" b.HomeWins b.AwayWins b.Draws b.MatchCount
              printfn "  HomeWin%%=%.0f Draw%%=%.0f" homeWinPct drawPct

              check drawRate drawPct
              check homeWinRate homeWinPct

              Expect.isGreaterThan
                  (b.HomeWins + b.AwayWins)
                  0
                  $"no wins at all in {b.MatchCount} matches — check finishing and urgency"
          } ]
