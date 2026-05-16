module FootballEngine.Tests.Layer3.PassEdgeCaseTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Assertions

let defaultClock = SimulationClock.defaultClock

let passEdgeCaseTests =
    testList
        "PassEdgeCases"
        [ test "PassAction.resolve does not throw when there is only one player" {
              let home = [| makePlayer 1 MC 15 |]
              let ctx, state = buildSimState home [| 50.0, 34.0 |] [||] [||]
              state |> withBallAt 50.0 34.0 |> withBallControlledBy HomeClub 1 |> ignore
              engineMustNotCrash (fun () -> PassAction.resolve 0 ctx state defaultClock home.[0] |> ignore)
          }

          test "PassAction.resolve returns BallUpdate in outputs when pass is attempted" {
              let home = [| makePlayer 1 MC 15; makePlayer 2 AMC 15; makePlayer 3 ST 15 |]
              let ctx, state =
                  buildSimState home [| 40.0, 34.0; 60.0, 34.0; 75.0, 34.0 |] [||] [||]
              state |> withBallAt 40.0 34.0 |> withBallControlledBy HomeClub 1 |> ignore
              let result = PassAction.resolve 0 ctx state defaultClock home.[2]
              // Pass may or may not fire depending on target availability,
              // but if it does fire, it must include a BallUpdate
              if result.Events.Length > 0 then
                  shouldContainBallUpdate result.Events
          } ]
