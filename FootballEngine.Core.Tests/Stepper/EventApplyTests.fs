namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module EventApplyTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private apply (world: World) (ev: MatchEvent) : unit = EventApply.apply ctx world ev

    let eventApplyTests =
        testList
            "FootballEngine.Core.Stepper.EventApplyTests"
            [ testCase "foul restart places the ball at the spot" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let fouler = ctx.HomeLineup.Profiles.[5].Id
                  let fouled = ctx.AwayLineup.Profiles.[6].Id

                  // The ball sits where the foul happened before the restart.
                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { X = 20.0<meter>
                                        Y = 5.0<meter>
                                        Z = 0.0<meter>
                                        Vx = 0.0<meterPerSecond>
                                        Vy = 0.0<meterPerSecond>
                                        Vz = 0.0<meterPerSecond> } } }

                  apply
                      world
                      { Tick = 0<subtick>
                        Data =
                          EventData.Foul(
                              fouler,
                              fouled,
                              FoulSeverity.Tactical,
                              CardDecision.NoCard,
                              (20.0<meter>, 5.0<meter>)
                          ) }

                  apply
                      world
                      { Tick = 1<subtick>
                        Data = EventData.SetPiece(SetPieceKind.FreeKick, ClubSide.Away) }

                  match world.State.Flow with
                  | MatchFlow.Restart restart ->
                      Expect.equal restart.Kind SetPieceKind.FreeKick "restart is a free kick"
                      Expect.equal restart.Side ClubSide.Away "free kick for the fouled side"
                      Expect.isGreaterThan (float restart.Spot.Y) 0.0 "ball placed at the foul spot"
                  | other -> failtestf "expected a restart delay, got %A" other)

              testCase "penalty restart places the ball on the spot" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let fouler = ctx.HomeLineup.Profiles.[5].Id
                  let fouled = ctx.AwayLineup.Profiles.[6].Id

                  apply
                      world
                      { Tick = 0<subtick>
                        Data =
                          EventData.Foul(
                              fouler,
                              fouled,
                              FoulSeverity.RedWorthy,
                              CardDecision.NoCard,
                              (-42.0<meter>, 0.0<meter>)
                          ) }

                  apply
                      world
                      { Tick = 1<subtick>
                        Data = EventData.SetPiece(SetPieceKind.Penalty, ClubSide.Away) }

                  match world.State.Flow with
                  | MatchFlow.Restart restart ->
                      Expect.equal restart.Kind SetPieceKind.Penalty "restart is a penalty"
                      Expect.equal restart.Side ClubSide.Away "penalty for the fouled side"
                  | other -> failtestf "expected a restart delay, got %A" other)

              testCase "goal flip updates the scoreboard only once" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let scorer = ctx.HomeLineup.Profiles.[9].Id

                  apply
                      world
                      { Tick = 0<subtick>
                        Data = EventData.Goal(ClubSide.Home, Some scorer, None, 0.6) }

                  Expect.equal world.State.HomeScore 1 "home scored") ]
