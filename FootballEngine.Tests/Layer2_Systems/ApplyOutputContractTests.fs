module FootballEngine.Tests.Layer2.ApplyOutputContractTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Infrastructure.Assertions

let applyOutputContractTests =
    testList
        "ApplyOutputContracts"
        [ test "BallUpdate sets state.Ball" {
              let newBall =
                  { SimStateOps.defaultBall with
                      Control = Controlled(HomeClub, 1) }

              let state, _ = applyOneOutput (BallUpdate newBall)

              match state.Ball.Control with
              | Controlled(HomeClub, 1) -> ()
              | other -> failtestf $"Expected Controlled(HomeClub, 1), got %A{other}"
          }

          test "FlowChange Live sets state.Flow to Live" {
              let state, _ = applyOneOutput (FlowChange Live)
              Expect.equal state.Flow Live "Flow must be Live"
          }

          test "FlowChange MatchEnded sets state.Flow to MatchEnded" {
              let state, _ = applyOneOutput (FlowChange MatchEnded)
              Expect.equal state.Flow MatchEnded "Flow must be MatchEnded"
          }

          test "ScoreGoal(HomeClub) increments home score" {
              let state, _ = applyOneOutput (ScoreGoal(HomeClub, None, false))
              shouldHaveScore 1 0 state
          }

          test "ScoreGoal(AwayClub) increments away score" {
              let state, _ = applyOneOutput (ScoreGoal(AwayClub, None, false))
              shouldHaveScore 0 1 state
          }

          test "MomentumUpdate adds to momentum" {
              let state, _ = applyOneOutput (MomentumUpdate 5.0)
              Expect.equal state.Momentum 5.0 "Momentum should be 5.0"
          }

          test "MomentumUpdate clamps at +10.0" {
              let state, _ = applyOneOutput (MomentumUpdate 15.0)
              Expect.equal state.Momentum 10.0 "Momentum should be clamped to 10.0"
          }

          test "MomentumUpdate clamps at -10.0" {
              let state, _ = applyOneOutput (MomentumUpdate -15.0)
              Expect.equal state.Momentum -10.0 "Momentum should be clamped to -10.0"
          }

          test "Emit adds event to events list and state.MatchEvents" {
              let ev =
                  { SubTick = 0
                    PlayerId = 1
                    ClubId = 100
                    Type = Goal
                    Context = EventContext.empty }

              let state, events = applyOneOutput (Emit ev)
              Expect.contains events ev "Event should be in returned events"

              Expect.isTrue
                  (state.MatchEvents |> Seq.exists (fun e -> e.Type = Goal))
                  "Event should be in state.MatchEvents"
          }

          test "HomeFrame(SetPosition) updates home player position" {
              let state, _ = applyOneOutput (HomeFrame(SetPosition(0, 30.0<meter>, 20.0<meter>)))

              Expect.floatClose
                  Accuracy.medium
                  (float state.Home.Frame.Physics.PosX[0])
                  30.0
                  "Home player 0 X should be 30.0"
          }

          test "AwayFrame(SetPosition) updates away player position" {
              let state, _ = applyOneOutput (AwayFrame(SetPosition(0, 70.0<meter>, 20.0<meter>)))

              Expect.floatClose
                  Accuracy.medium
                  (float state.Away.Frame.Physics.PosX[0])
                  70.0
                  "Away player 0 X should be 70.0"
          }

          test "BallXSmoothUpdate sets BallXSmooth" {
              let state, _ = applyOneOutput (BallXSmoothUpdate 60.0<meter>)
              Expect.equal state.BallXSmooth 60.0<meter> "BallXSmooth should be 60.0"
          }

          test "LastAttackingClubSet sets LastAttackingClub" {
              let state, _ = applyOneOutput (LastAttackingClubSet HomeClub)
              Expect.equal state.LastAttackingClub HomeClub "LastAttackingClub should be HomeClub"
          }

          test "ScoreGoalAdjust with -1 decrements home score" {
              let state, _ = applyOneOutput (ScoreGoalAdjust(HomeClub, -1))
              Expect.equal state.HomeScore 0 "Home score should remain 0 (max(0, -1))"
          }

          test "StoppageTimeAdd does not crash" {
              engineMustNotCrash (fun () -> applyOneOutput (StoppageTimeAdd(100, StoppageReason.GoalDelay)) |> ignore)
          } ]
