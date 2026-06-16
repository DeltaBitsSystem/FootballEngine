module FootballEngine.Tests.Layer2.BallSystemTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Assertions

let defaultClock = SimulationClock.defaultClock

let ballSystemTests =
    testList
        "BallSystem"
        [ test "BallSystem always emits exactly one BallUpdate" {
              let ctx, state = buildStandardMatch ()
              let buffer = ResizeArray<DomainEvent>(16)
              BallSystem.run ctx state defaultClock buffer

              let ballUpdates =
                  buffer
                  |> Seq.filter (fun o ->
                      match o with
                      | BallUpdate _ -> true
                      | _ -> false)
                  |> Seq.length

              Expect.equal ballUpdates 1 "BallSystem must emit exactly one BallUpdate per tick"
          }

          test "BallSystem does not crash with single player and free ball" {
              let home = [| makePlayer 1 MC 15 |]
              let ctx, state = buildSimState home [| 30.0, 34.0 |] [||] [||]
              state |> withBallAt 30.0 34.0 |> withBallFree |> ignore
              let buffer = ResizeArray<DomainEvent>(16)
              engineMustNotCrash (fun () -> BallSystem.run ctx state defaultClock buffer)
          }

          test "BallSystem does not crash when ball is already Controlled" {
              let home = [| makePlayer 1 MC 15 |]
              let ctx, state = buildSimState home [| 30.0, 34.0 |] [||] [||]
              state |> withBallAt 30.0 34.0 |> withBallControlledBy HomeClub 1 |> ignore
              let buffer = ResizeArray<DomainEvent>(16)
              engineMustNotCrash (fun () -> BallSystem.run ctx state defaultClock buffer)
          } ]
