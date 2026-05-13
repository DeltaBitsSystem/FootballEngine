module FootballEngine.Tests.Layer1.FlowTransitionTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners

let flowTransitionTests =
    testList
        "FlowTransition"
        [ test "RestartDelay with RemainingTicks=3 reaches Live within 10 ticks" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              let result = runUntilFlow (fun f -> f = Live) 10 ctx state
              Expect.isSome result "Should reach Live from RestartDelay(3)"
          }

          test "GoalPause with RemainingTicks=3 transitions to RestartDelay KickOff for AwayClub" {
              let ctx, state = buildStandardMatch ()

              state.Flow <-
                  GoalPause
                      { ScoringTeam = HomeClub
                        ScorerId = None
                        IsOwnGoal = false
                        RemainingTicks = 3
                        VARRequested = false }

              let result =
                  runUntilFlow
                      (fun f ->
                          match f with
                          | RestartDelay r -> r.Kind = SetPieceKind.KickOff
                          | _ -> false)
                      10
                      ctx
                      state

              Expect.isSome result "GoalPause should transition to RestartDelay KickOff"
              let finalState, _ = result.Value

              match finalState.Flow with
              | RestartDelay r -> Expect.equal r.Team AwayClub "KickOff awarded to team that conceded"
              | other -> failtestf $"Expected RestartDelay, got %A{other}"
          }

          test "HalfTimePause transitions to RestartDelay KickOff for AwayClub" {
              let ctx, state = buildStandardMatch ()
              state.Flow <- HalfTimePause 2

              let result =
                  runUntilFlow
                      (fun f ->
                          match f with
                          | RestartDelay r -> r.Team = AwayClub
                          | _ -> false)
                      10
                      ctx
                      state

              Expect.isSome result "HalfTime should restart with AwayClub kick-off"
          }

          test "FullTimeReview immediately transitions to MatchEnded" {
              let ctx, state = buildStandardMatch ()
              state.Flow <- FullTimeReview
              let result = MatchStepper.updateOne ctx defaultClock [||] state
              Expect.equal result.State.Flow MatchEnded "FullTimeReview transitions to MatchEnded in one tick"
          }

          test "SubTick increments by 1 each call while not MatchEnded" {
              let ctx, state = buildStandardMatch ()
              state |> withLive |> ignore
              let before = state.SubTick
              let result = MatchStepper.updateOne ctx defaultClock [||] state
              Expect.equal result.State.SubTick (before + 1) "SubTick must increment by 1"
          }

          test "SubTick does not increment when MatchEnded" {
              let ctx, state = buildStandardMatch ()
              state.Flow <- MatchEnded
              let before = state.SubTick
              let result = MatchStepper.updateOne ctx defaultClock [||] state
              Expect.equal result.State.SubTick before "SubTick must not increment after MatchEnded"
          }

          test "InjuryPause with RemainingTicks=2 resolves to FreeKick RestartDelay" {
              let ctx, state = buildStandardMatch ()

              state.Flow <-
                  InjuryPause
                      { PlayerId = 1
                        Team = HomeClub
                        Severity = 1
                        RemainingTicks = 2
                        CanContinue = None }

              let result =
                  runUntilFlow
                      (fun f ->
                          match f with
                          | RestartDelay r -> r.Kind = SetPieceKind.FreeKick
                          | _ -> false)
                      10
                      ctx
                      state

              Expect.isSome result "InjuryPause should resolve to FreeKick restart"
          } ]
