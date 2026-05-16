module FootballEngine.Tests.Layer1.GoalCycleTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Infrastructure.Assertions

let goalCycleTests =
    testList
        "GoalCycle"
        [ test "ScoreGoal output increments HomeScore from 0 to 1" {
              let state, _ = applyOneDomainEvent (DomainEvent.ScoreGoal(HomeClub, Some 1, false))
              shouldHaveScore 1 0 state
          }

          test "ScoreGoal output increments AwayScore from 0 to 1" {
              let state, _ = applyOneDomainEvent (DomainEvent.ScoreGoal(AwayClub, None, false))
              shouldHaveScore 0 1 state
          }

          test "GoalPause with 5 ticks → RestartDelay → Live cycle completes within 300 ticks" {
              let ctx, state = buildStandardMatch ()
              state.Flow <-
                   GoalPause { ScoringTeam = HomeClub; ScorerId = None;
                               IsOwnGoal = false; RemainingTicks = 5 * 1<tickDelta>; VARRequested = false }
              let phase1 =
                  runUntilFlow (fun f -> match f with RestartDelay _ -> true | _ -> false) 50 ctx state
              Expect.isSome phase1 "Should reach RestartDelay after GoalPause"
              let stateAfterPause, _ = phase1.Value
              let phase2 = runUntilFlow (fun f -> f = Live) 250 ctx stateAfterPause
              Expect.isSome phase2 "Should reach Live after RestartDelay"
          }

          test "Score does not go negative after 200 ticks from standard start" {
              let ctx, state = buildStandardMatch ()
              let finalState, _ = advanceTicks 200 ctx state
              scoreIsNonNegative finalState
          } ]
