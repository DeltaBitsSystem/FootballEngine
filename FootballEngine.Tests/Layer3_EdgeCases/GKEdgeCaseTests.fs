module FootballEngine.Tests.Layer3.GKEdgeCaseTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Assertions

let defaultClock = SimulationClock.defaultClock

let gkEdgeCaseTests =
    testList
        "GKEdgeCases"
        [ test "GKAction.resolve does not throw when ball is not controlled by GK" {
              let home = [| makeGk 1 15 15 15 |]
              let away = [| makePlayer 2 ST 15 |]
              let ctx, state = buildSimState home [| 5.0, 34.0 |] away [| 88.0, 34.0 |]
              state |> withBallAt 88.0 34.0 |> withBallControlledBy AwayClub 2 |> ignore
              engineMustNotCrash (fun () -> GKAction.resolve 0 ctx state defaultClock |> ignore)
          }

          test "GKAction.resolve returns empty events when ball is not GK's" {
              let home = [| makeGk 1 15 15 15 |]
              let away = [| makePlayer 2 ST 15 |]
              let ctx, state = buildSimState home [| 5.0, 34.0 |] away [| 88.0, 34.0 |]
              state |> withBallAt 88.0 34.0 |> withBallControlledBy AwayClub 2 |> ignore
              let events, _ = GKAction.resolve 0 ctx state defaultClock
              Expect.isEmpty events "GK should produce no events when ball is not theirs"
          }

          test "GKAction.resolve with GKHoldSinceSubTick=0 at subTick=0 produces no distribution" {
              let home = [| makeGk 1 15 15 15; makePlayer 2 MC 15; makePlayer 3 ST 15 |]
              let away = [| makePlayer 4 ST 15 |]
              let ctx, state = buildSimState home [| 5.0, 34.0; 30.0, 34.0; 50.0, 34.0 |] away [| 80.0, 34.0 |]
              state |> withBallAt 5.0 34.0 |> withBallControlledBy HomeClub 1 |> withGKHoldingSince 0 |> ignore
              let events, _ = GKAction.resolve 0 ctx state defaultClock
              let hasDistribution =
                  events |> List.exists (fun e -> match e.Type with MatchEventType.GKDistribution _ -> true | _ -> false)
              Expect.isFalse hasDistribution "GK should not distribute at sub-tick 0 (window=16)"
          } ]
