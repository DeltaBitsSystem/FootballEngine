module FootballEngine.Tests.Layer2.StatisticalContractsTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Simulation
open FootballEngine.Tests.Helpers


let private countWhere f xs = xs |> Seq.filter f |> Seq.length

let private pct num den =
    if den = 0 then
        "N/A"
    else
        sprintf "%.1f%%" (float num / float den * 100.0)

let private avg total n =
    if n = 0 then 0.0 else float total / float n

let private divLine () = printfn $"%s{System.String('-', 60)}"

let statisticalContractsTests =
    testList
        "StatisticalContracts"
        [ testCase "all statistical contracts hold over 20 matches"
          <| fun () ->

              let game = loadGame ()
              let clubs = game.Clubs |> Map.toArray |> Array.map snd |> Array.take 2

              let replays =
                  Array.Parallel.init 20 (fun i ->
                      let hi = i % clubs.Length
                      let ai = (hi + 1) % clubs.Length
                      MatchSimulator.trySimulateMatchFull clubs[hi] clubs[ai] game.Players game.Staff game.ProfileCache)

              let okReplays =
                  replays
                  |> Array.choose (function
                      | Ok r -> Some r
                      | _ -> None)

              let nMatches = okReplays.Length

              let mutable totalGoals = 0
              let mutable totalOwnGoals = 0
              let mutable homeWins = 0
              let mutable awayWins = 0
              let mutable draws = 0

              let mutable nShotLaunched = 0
              let mutable nShotOnTarget = 0
              let mutable nShotOffTarget = 0
              let mutable nShotBlocked = 0
              let mutable nSave = 0
              let mutable nSaveCaught = 0
              let mutable nSaveParried = 0
              let mutable nGoalWithNoShot = 0
              let mutable nGoalFromLaunched = 0
              let mutable nPassCompleted = 0
              let mutable nPassMisplaced = 0
              let mutable nPassIntercepted = 0
              let mutable nPassDeflected = 0
              let mutable nPassLaunched = 0
              let mutable nDribbleSuccess = 0
              let mutable nDribbleFail = 0
              let mutable nTackleSuccess = 0
              let mutable nTackleFail = 0
              let mutable nCorner = 0
              let mutable nFreeKick = 0
              let mutable nPenaltyScored = 0
              let mutable nPenaltyMissed = 0
              let mutable nCrossAttempt = 0
              let mutable nCrossSuccess = 0
              let mutable nLongBall = 0
              let mutable nFoul = 0
              let mutable nYellow = 0
              let mutable nRed = 0
              let mutable nInjury = 0
              let mutable nGKDistribution = 0

              let perMatchGoals = System.Collections.Generic.List<int>()
              let perMatchShots = System.Collections.Generic.List<int>()

              for replay in okReplays do
                  let h = replay.Final.HomeScore
                  let a = replay.Final.AwayScore
                  let events = replay.Events

                  if h > a then homeWins <- homeWins + 1
                  elif a > h then awayWins <- awayWins + 1
                  else draws <- draws + 1

                  totalGoals <- totalGoals + h + a
                  perMatchGoals.Add(h + a)

                  let matchShotLaunched = countWhere (fun e -> e.Type = ShotLaunched) events
                  let matchGoals = countWhere (fun e -> e.Type = Goal || e.Type = OwnGoal) events

                  if matchShotLaunched = 0 && matchGoals > 0 then
                      nGoalWithNoShot <- nGoalWithNoShot + matchGoals
                  elif matchShotLaunched > 0 then
                      nGoalFromLaunched <- nGoalFromLaunched + matchGoals

                  let matchShots =
                      countWhere
                          (fun e ->
                              match e.Type with
                              | ShotLaunched
                              | ShotOnTarget
                              | ShotOffTarget
                              | ShotBlocked -> true
                              | _ -> false)
                          events

                  perMatchShots.Add(matchShots)

                  for e in events do
                      match e.Type with
                      | OwnGoal -> totalOwnGoals <- totalOwnGoals + 1
                      | ShotLaunched -> nShotLaunched <- nShotLaunched + 1
                      | ShotOnTarget -> nShotOnTarget <- nShotOnTarget + 1
                      | ShotOffTarget -> nShotOffTarget <- nShotOffTarget + 1
                      | ShotBlocked -> nShotBlocked <- nShotBlocked + 1
                      | Save -> nSave <- nSave + 1
                      | SaveCaught _ -> nSaveCaught <- nSaveCaught + 1
                      | SaveParried _ -> nSaveParried <- nSaveParried + 1
                      | PassCompleted _ -> nPassCompleted <- nPassCompleted + 1
                      | PassLaunched _ -> nPassLaunched <- nPassLaunched + 1
                      | PassMisplaced _ -> nPassMisplaced <- nPassMisplaced + 1
                      | PassIntercepted _ -> nPassIntercepted <- nPassIntercepted + 1
                      | PassDeflected _ -> nPassDeflected <- nPassDeflected + 1
                      | DribbleSuccess -> nDribbleSuccess <- nDribbleSuccess + 1
                      | DribbleFail -> nDribbleFail <- nDribbleFail + 1
                      | TackleSuccess -> nTackleSuccess <- nTackleSuccess + 1
                      | TackleFail -> nTackleFail <- nTackleFail + 1
                      | Corner -> nCorner <- nCorner + 1
                      | FreeKick _ -> nFreeKick <- nFreeKick + 1
                      | PenaltyAwarded s ->
                          if s then
                              nPenaltyScored <- nPenaltyScored + 1
                          else
                              nPenaltyMissed <- nPenaltyMissed + 1
                      | CrossAttempt s ->
                          nCrossAttempt <- nCrossAttempt + 1

                          if s then
                              nCrossSuccess <- nCrossSuccess + 1
                      | LongBall _ -> nLongBall <- nLongBall + 1
                      | FoulCommitted -> nFoul <- nFoul + 1
                      | YellowCard -> nYellow <- nYellow + 1
                      | RedCard -> nRed <- nRed + 1
                      | Injury _ -> nInjury <- nInjury + 1
                      | GKDistribution _ -> nGKDistribution <- nGKDistribution + 1
                      | _ -> ()

              divLine ()
              printfn $"STATISTICAL CONTRACTS — %d{nMatches} matches"
              divLine ()
              printfn ""
              printfn "[ RESULTS ]"
              printfn $"  Home wins: %d{homeWins}  Away wins: %d{awayWins}  Draws: %d{draws}"
              printfn $"  Total goals: %d{totalGoals}  Own goals: %d{totalOwnGoals}"
              printfn $"  Avg goals/match: %.2f{avg totalGoals nMatches}"
              printfn $"  Goals range: %d{Seq.min perMatchGoals} - %d{Seq.max perMatchGoals} per match"
              printfn ""
              printfn "[ SHOT PIPELINE ]"
              printfn $"  ShotLaunched  : %d{nShotLaunched}  (%.1f{avg nShotLaunched nMatches}/match)"
              printfn $"  ShotOnTarget  : %d{nShotOnTarget}  (%.1f{avg nShotOnTarget nMatches}/match)"
              printfn $"  ShotOffTarget : %d{nShotOffTarget}  (%.1f{avg nShotOffTarget nMatches}/match)"
              printfn $"  ShotBlocked   : %d{nShotBlocked}  (%.1f{avg nShotBlocked nMatches}/match)"
              printfn $"  Save          : %d{nSave}  (%.1f{avg nSave nMatches}/match)"
              printfn $"  SaveCaught    : %d{nSaveCaught}"
              printfn $"  SaveParried   : %d{nSaveParried}"
              printfn $"  Goals         : %d{totalGoals}  (%.1f{avg totalGoals nMatches}/match)"

              let totalShotEvents = nShotLaunched + nShotOnTarget + nShotOffTarget + nShotBlocked
              printfn ""
              printfn "  Shot->Goal conversion    : %s  (expected ~5-15%%)" (pct totalGoals totalShotEvents)
              printfn "  OnTarget->Goal conversion: %s  (expected ~20-40%%)" (pct totalGoals nShotOnTarget)
              printfn $"  Goals with NO ShotLaunched in match: %d{nGoalWithNoShot}  <- BUG if > 0"
              printfn $"  Goals with ShotLaunched present    : %d{nGoalFromLaunched}"
              printfn ""
              printfn "[ PASSING ]"

              let nPassAll = nPassCompleted + nPassMisplaced + nPassIntercepted + nPassDeflected
              printfn $"  PassLaunched  : %d{nPassLaunched}  (%.1f{avg nPassLaunched nMatches}/match)"
              printfn $"  PassCompleted : %d{nPassCompleted}  (%.1f{avg nPassCompleted nMatches}/match)"
              printfn $"  PassMisplaced : %d{nPassMisplaced}"
              printfn $"  PassIntercept : %d{nPassIntercepted}"
              printfn $"  PassDeflected : %d{nPassDeflected}"
              printfn "  Completion rate: %s  (expected ~65-85%%)" (pct nPassCompleted nPassAll)
              printfn ""
              printfn "[ DUELS ]"

              printfn
                  $"  DribbleSuccess: %d{nDribbleSuccess}  DribbleFail: %d{nDribbleFail}  rate: %s{pct nDribbleSuccess (nDribbleSuccess + nDribbleFail)}"

              printfn
                  $"  TackleSuccess : %d{nTackleSuccess}  TackleFail : %d{nTackleFail}  rate: %s{pct nTackleSuccess (nTackleSuccess + nTackleFail)}"

              printfn ""
              printfn "[ SET PIECES ]"
              printfn $"  Corners   : %d{nCorner}  (%.1f{avg nCorner nMatches}/match)"
              printfn $"  FreeKicks : %d{nFreeKick}  (%.1f{avg nFreeKick nMatches}/match)"
              printfn $"  Penalties : scored=%d{nPenaltyScored}  missed=%d{nPenaltyMissed}"

              printfn
                  $"  Cross att : %d{nCrossAttempt}  success=%d{nCrossSuccess}  rate: %s{pct nCrossSuccess nCrossAttempt}"

              printfn $"  LongBalls : %d{nLongBall}  (%.1f{avg nLongBall nMatches}/match)"
              printfn ""
              printfn "[ DISCIPLINE ]"
              printfn $"  Fouls  : %d{nFoul}  (%.1f{avg nFoul nMatches}/match)"
              printfn $"  Yellow : %d{nYellow}  Red: %d{nRed}  Injury: %d{nInjury}"
              printfn ""
              printfn "[ GK ]"
              printfn $"  GKDistribution: %d{nGKDistribution}  (%.1f{avg nGKDistribution nMatches}/match)"
              divLine ()

              let avgGoals = avg totalGoals nMatches
              Expect.isGreaterThanOrEqual avgGoals 1.5 $"avg goals/match = {avgGoals:F2}, expected >= 1.5"
              Expect.isLessThanOrEqual avgGoals 10.0 $"avg goals/match = {avgGoals:F2}, expected <= 10.0"

              Expect.equal
                  nGoalWithNoShot
                  0
                  $"goals in matches with zero ShotLaunched = {nGoalWithNoShot}. Goals bypassing ShotAction."

              let avgShotEvents = avg totalShotEvents nMatches

              Expect.isGreaterThanOrEqual
                  avgShotEvents
                  5.0
                  $"avg shot-pipeline events/match = {avgShotEvents:F1}, expected >= 5"

              if totalShotEvents > 0 then
                  let conv = float totalGoals / float totalShotEvents * 100.0

                  Expect.isLessThanOrEqual
                      conv
                      25.0
                      $"shot conversion = {conv:F1}%%, expected <= 25%%. Goals bypassing GK."

              let avgPasses = avg nPassCompleted nMatches
              Expect.isGreaterThanOrEqual avgPasses 20.0 $"avg completed passes/match = {avgPasses:F1}, expected >= 20"

              if nPassAll > 0 then
                  let passRate = float nPassCompleted / float nPassAll * 100.0
                  Expect.isGreaterThanOrEqual passRate 55.0 $"pass completion = {passRate:F1}%%, expected >= 55%%"
                  Expect.isLessThanOrEqual passRate 95.0 $"pass completion = {passRate:F1}%%, expected <= 95%%"

              Expect.isGreaterThanOrEqual
                  (avg nCorner nMatches)
                  2.0
                  $"avg corners/match = {avg nCorner nMatches:F2}, expected >= 2"

              Expect.isGreaterThanOrEqual
                  (avg nFoul nMatches)
                  5.0
                  $"avg fouls/match = {avg nFoul nMatches:F2}, expected >= 5"

              Expect.isGreaterThan
                  homeWins
                  awayWins
                  $"home wins = {homeWins}, away wins = {awayWins}: expected home advantage" ]
