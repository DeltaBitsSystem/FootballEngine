module FootballEngine.Tests.Layer3.ShotEdgeCaseTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Assertions

let defaultClock = SimulationClock.defaultClock

let private countShotLaunched skill =
    let mutable count = 0
    for _ in 1..100 do
        let home = [| makePlayer 1 ST skill |]
        let away = [| makeGk 2 1 1 1 |]
        let ctx, state = buildSimState home [| 88.0, 34.0 |] away [| 101.0, 34.0 |]
        state |> withBallAt 88.0 34.0 |> withBallControlledBy HomeClub 1 |> ignore
        let result = ShotAction.resolve 0 ctx state defaultClock
        let isShotLaunched (e: DomainEvent) =
            match e with
            | DomainEvent.Emit me -> me.Type = MatchEventType.ShotLaunched
            | _ -> false
        if result.Events |> Array.exists isShotLaunched then
            count <- count + 1
    count

let shotEdgeCaseTests =
    testList
        "ShotEdgeCases"
        [ test "ShotAction.resolve does not throw when ball is at goal line" {
              let home = [| makePlayer 1 ST 15 |]
              let away = [| makeGk 2 15 15 15 |]
              let ctx, state = buildSimState home [| 105.0, 34.0 |] away [| 105.0, 34.0 |]
              state |> withBallAt 105.0 34.0 |> withBallControlledBy HomeClub 1 |> ignore
              engineMustNotCrash (fun () -> ShotAction.resolve 0 ctx state defaultClock |> ignore)
          }

          test "ShotAction.resolve does not throw when ball position is 0,0" {
              let home = [| makePlayer 1 ST 15 |]
              let away = [| makeGk 2 15 15 15 |]
              let ctx, state = buildSimState home [| 0.1, 0.1 |] away [| 5.0, 34.0 |]
              state |> withBallAt 0.0 0.0 |> withBallControlledBy HomeClub 1 |> ignore
              engineMustNotCrash (fun () -> ShotAction.resolve 0 ctx state defaultClock |> ignore)
          }

          test "ShotAction.resolve always returns BallUpdate in outputs when fireable" {
              let home = [| makePlayer 1 ST 18 |]
              let away = [| makeGk 2 10 10 10 |]
              for _ in 1..20 do
                  let ctx, state = buildSimState home [| 88.0, 34.0 |] away [| 101.0, 34.0 |]
                  state |> withBallAt 88.0 34.0 |> withBallControlledBy HomeClub 1 |> ignore
                  let result = ShotAction.resolve 0 ctx state defaultClock
                  if result.Events.Length > 0 then
                      shouldContainBallUpdate result.Events
          }

          test "Elite attacker produces more ShotLaunched than worst attacker (100 trials)" {
              let eliteCount = countShotLaunched 20
              let worstCount = countShotLaunched 1
              Expect.isGreaterThan eliteCount worstCount
                  $"Elite ({eliteCount}/100) should out-shoot worst ({worstCount}/100)"
          } ]
