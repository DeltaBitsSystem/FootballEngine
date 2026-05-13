module FootballEngine.Tests.Layer1.MatchInvariantTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Infrastructure.Assertions

let matchInvariantTests =
    testList
        "MatchInvariants"
        [ test "Engine does not throw for 200 ticks from standard start" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              engineMustNotCrash (fun () -> advanceTicks 200 ctx state |> ignore)
          }

          test "Score is non-negative after 500 ticks" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              let finalState, _ = advanceTicks 500 ctx state
              scoreIsNonNegative finalState
          }

          test "Momentum stays within [-10.0, 10.0] after 500 ticks" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              let finalState, _ = advanceTicks 500 ctx state
              Expect.isGreaterThanOrEqual finalState.Momentum -10.0 "Momentum lower bound"
              Expect.isLessThanOrEqual finalState.Momentum 10.0 "Momentum upper bound"
          }

          test "SubTick monotonically increases while match is not ended" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              let mutable s = state
              let mutable prev = -1
              for _ in 1..100 do
                  if s.Flow <> MatchEnded then
                      let result = MatchStepper.updateOne ctx defaultClock [||] s
                      Expect.isGreaterThan result.State.SubTick prev
                          $"SubTick must increase. prev={prev}, got={result.State.SubTick}"
                      prev <- result.State.SubTick
                      s <- result.State
          }

          test "Engine produces at least one event in 200 ticks from Live" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              let _, events = advanceTicks 200 ctx state
              Expect.isGreaterThan events.Length 0 "Engine should produce at least one event in 200 ticks"
          }

          test "MatchEvents buffer does not exceed 1024 entries after 5000 ticks" {
              let ctx, state = buildStandardMatch ()
              state |> withKickOffPending HomeClub |> ignore
              let finalState, _ = advanceTicks 5000 ctx state
              Expect.isLessThanOrEqual finalState.MatchEvents.Count 1024
                  "MatchEvents circular buffer must not grow unbounded"
          } ]
