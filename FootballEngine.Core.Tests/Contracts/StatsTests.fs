namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module StatsTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private accumulate (events: MatchEvent list) : World =
        let world = TestBuilders.minimalWorld ()

        for ev in events do
            EventApply.apply ctx world ev

        world

    let statsTests =
        testList
            "FootballEngine.Core.Contracts.StatsTests"
            [ test "shots and xG accumulate" {
                  let pid = ctx.HomeLineup.Profiles.[9].Id

                  let world =
                      accumulate
                          [ { Tick = 0<subtick>
                              Data =
                                EventData.Shot(
                                    pid,
                                    ShotResult.OnTarget false,
                                    0.5,
                                    20.0<meterPerSecond>,
                                    30.0<meter>,
                                    0.0<meter>
                                ) }
                            { Tick = 1<subtick>
                              Data =
                                EventData.Shot(
                                    pid,
                                    ShotResult.OffTarget,
                                    0.3,
                                    22.0<meterPerSecond>,
                                    30.0<meter>,
                                    0.0<meter>
                                ) } ]

                  Expect.equal world.Home.Stats.Shots 2 "two shots counted"
                  Expect.isGreaterThan world.Home.Stats.XgSum 0.79 "xG accumulated"
                  Expect.equal world.Home.Stats.OnTarget 1 "one on target"
              }

              test "passes completed and dribbles tracked" {
                  let passer = ctx.HomeLineup.Profiles.[5].Id
                  let dribbler = ctx.HomeLineup.Profiles.[8].Id

                  let world =
                      accumulate
                          [ { Tick = 0<subtick>
                              Data =
                                EventData.Pass(
                                    passer,
                                    Some dribbler,
                                    12.0<meter>,
                                    0.7,
                                    true,
                                    0.0<meter>,
                                    0.0<meter>,
                                    12.0<meter>,
                                    0.0<meter>
                                ) }
                            { Tick = 1<subtick>
                              Data =
                                EventData.Pass(
                                    passer,
                                    Some dribbler,
                                    12.0<meter>,
                                    0.5,
                                    false,
                                    0.0<meter>,
                                    0.0<meter>,
                                    12.0<meter>,
                                    0.0<meter>
                                ) }
                            { Tick = 2<subtick>
                              Data = EventData.Dribble(dribbler, true, 20.0<meter>, 0.0<meter>) }
                            { Tick = 3<subtick>
                              Data = EventData.CrossAttempt(dribbler, 30.0<meter>, 10.0<meter>) } ]

                  Expect.equal world.Home.Stats.Passes 2 "passes counted"
                  Expect.equal world.Home.Stats.PassesCompleted 1 "one completed"
                  Expect.equal world.Home.Stats.Dribbles 1 "one successful dribble"
                  Expect.equal world.Home.Stats.Crosses 1 "one cross counted"
              }

              test "goals and corners recorded" {
                  let scorer = ctx.HomeLineup.Profiles.[9].Id

                  let world =
                      accumulate
                          [ { Tick = 0<subtick>
                              Data = EventData.Goal(ClubSide.Home, Some scorer, None, 0.6) }
                            { Tick = 1<subtick>
                              Data = EventData.SetPiece(SetPieceKind.Corner, ClubSide.Home) } ]

                  Expect.equal world.State.HomeScore 1 "goal on the board"
                  Expect.equal world.Home.Stats.Corners 1 "corner counted"
              } ]
