module FootballEngine.Tests.Layer1.SetPieceFlowTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Infrastructure.Assertions

let setPieceFlowTests =
    testList
        "SetPieceFlow"
        [ test "KickOff RestartDelay(0) emits KickOff event" {
              let ctx, state = buildStandardMatch ()

              state.Flow <-
                  RestartDelay
                      { Kind = SetPieceKind.KickOff
                        Team = HomeClub
                        Cause = InitialKickOff
                        RemainingTicks = 0 * 1<tickDelta> }

              let result = MatchStepper.updateOne ctx defaultClock [||] state
              shouldContainKickOff (result.Events |> Seq.toList)
          }

          test "After KickOff RestartDelay(0), flow becomes Live" {
              let ctx, state = buildStandardMatch ()

              state.Flow <-
                  RestartDelay
                      { Kind = SetPieceKind.KickOff
                        Team = HomeClub
                        Cause = InitialKickOff
                        RemainingTicks = 0 * 1<tickDelta> }

              let result = MatchStepper.updateOne ctx defaultClock [||] state
              Expect.equal result.State.Flow Live "Flow should be Live after kick-off"
          }

          test "RestartDelay with RemainingTicks=5 decrements by 1 per tick" {
              let ctx, state = buildStandardMatch ()

              state.Flow <-
                  RestartDelay
                      { Kind = SetPieceKind.KickOff
                        Team = HomeClub
                        Cause = InitialKickOff
                        RemainingTicks = 5 * 1<tickDelta> }

              let result = MatchStepper.updateOne ctx defaultClock [||] state

              match result.State.Flow with
              | RestartDelay r -> Expect.equal r.RemainingTicks (4 * 1<tickDelta>) "RemainingTicks must decrement by 1"
              | other -> failtestf $"Expected RestartDelay, got %A{other}"
          } ]
