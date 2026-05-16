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

              let state, _ = applyOneDomainEvent (DomainEvent.BallUpdate newBall)

              match state.Ball.Control with
              | Controlled(HomeClub, 1) -> ()
              | other -> failtestf $"Expected Controlled(HomeClub, 1), got %A{other}"
          }

          test "FlowChange Live sets state.Flow to Live" {
              let state, _ = applyOneDomainEvent (DomainEvent.FlowChange Live)
              Expect.equal state.Flow Live "Flow must be Live"
          }

          test "FlowChange MatchEnded sets state.Flow to MatchEnded" {
              let state, _ = applyOneDomainEvent (DomainEvent.FlowChange MatchEnded)
              Expect.equal state.Flow MatchEnded "Flow must be MatchEnded"
          }

          test "ScoreGoal(HomeClub) increments home score" {
              let state, _ = applyOneDomainEvent (DomainEvent.ScoreGoal(HomeClub, None, false))
              shouldHaveScore 1 0 state
          }

          test "ScoreGoal(AwayClub) increments away score" {
              let state, _ = applyOneDomainEvent (DomainEvent.ScoreGoal(AwayClub, None, false))
              shouldHaveScore 0 1 state
          }

          test "MomentumDelta adds to momentum" {
              let state, _ = applyOneDomainEvent (DomainEvent.MomentumDelta 5.0)
              Expect.equal state.Momentum 5.0 "Momentum should be 5.0"
          }

          test "MomentumDelta clamps at +10.0" {
              let state, _ = applyOneDomainEvent (DomainEvent.MomentumDelta 15.0)
              Expect.equal state.Momentum 10.0 "Momentum should be clamped to 10.0"
          }

          test "MomentumDelta clamps at -10.0" {
              let state, _ = applyOneDomainEvent (DomainEvent.MomentumDelta -15.0)
              Expect.equal state.Momentum -10.0 "Momentum should be clamped to -10.0"
          }

          test "Emit adds event to events list and state.MatchEvents" {
              let ev =
                  { SubTick = 0
                    PlayerId = 1
                    ClubId = 100
                    Type = Goal
                    Context = EventContext.empty }

              let state, events = applyOneDomainEvent (DomainEvent.Emit ev)
              Expect.contains events ev "Event should be in returned events"

              Expect.isTrue
                  (state.MatchEvents |> Seq.exists (fun e -> e.Type = Goal))
                  "Event should be in state.MatchEvents"
          }

          test "LastAttackingClubSet sets LastAttackingClub" {
              let state, _ = applyOneDomainEvent (DomainEvent.LastAttackingClubSet HomeClub)
              Expect.equal state.LastAttackingClub HomeClub "LastAttackingClub should be HomeClub"
          }

          test "ScoreGoalAdjust with -1 decrements home score" {
              let state, _ = applyOneDomainEvent (DomainEvent.ScoreGoalAdjust(HomeClub, -1))
              Expect.equal state.HomeScore 0 "Home score should remain 0 (max(0, -1))"
          }

          test "StoppageTimeAdd does not crash" {
              engineMustNotCrash (fun () -> applyOneDomainEvent (DomainEvent.StoppageTimeAdd(100, StoppageReason.GoalDelay)) |> ignore)
          } ]
