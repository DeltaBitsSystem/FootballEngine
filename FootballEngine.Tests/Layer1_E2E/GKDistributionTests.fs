module FootballEngine.Tests.Layer1.GKDistributionTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Infrastructure.Assertions

let gkDistributionTests =
    testList
        "GKDistribution"
        [ test "GK holding ball since sub-tick 0 produces GKDistribution event within 40 ticks" {
              let ctx, state = gkHoldingScenario ()
              let result =
                  runUntilEvent
                      (fun e -> match e.Type with MatchEventType.GKDistribution _ -> true | _ -> false)
                      40 ctx state
              Expect.isSome result "GK should distribute ball within 40 ticks"
          }

          test "After GK distribution, ball control is no longer Controlled by GK" {
              let ctx, state = gkHoldingScenario ()
              let gkId = (standardHomePlayers ()).[0].Id
              let finalState, events = advanceTicks 40 ctx state
              let hadDistribution =
                  events |> List.exists (fun e ->
                      match e.Type with MatchEventType.GKDistribution _ -> true | _ -> false)
              if hadDistribution then
                  match finalState.Ball.Control with
                  | Controlled(HomeClub, pid) -> Expect.notEqual pid gkId "GK should not still hold ball"
                  | _ -> ()
              else
                  skiptest "GK did not distribute in 40 ticks"
          } ]
