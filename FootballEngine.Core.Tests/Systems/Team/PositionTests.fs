namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PositionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    /// Places a team's frame so slot order is predictable; then forces a fresh
    /// directive so assignRoles runs.
    let private freshDirective (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        team.Directive.Kind <- DirectiveKind.Attack
        team.Directive.SinceTick <- tick - 1<subtick>

    let positionTests =
        testList
            "FootballEngine.Core.Systems.Team.PositionTests"
            [ testCase "role assignment is deterministic and role-biased" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  // Ball near slot 5; slot 6 far back.
                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { X = 10.0<meter>
                                        Y = 0.0<meter>
                                        Z = 0.0<meter>
                                        Vx = 0.0<meterPerSecond>
                                        Vy = 0.0<meterPerSecond>
                                        Vz = 0.0<meterPerSecond> } } }

                  world.Home.Frame.PosX.[5] <- 10.0f
                  world.Home.Frame.PosY.[5] <- 0.0f
                  world.Home.Frame.PosX.[6] <- -40.0f
                  world.Home.Frame.PosY.[6] <- 0.0f

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isGreaterThan
                      world.Home.Frame.RoleBias.[5]
                      world.Home.Frame.RoleBias.[6]
                      "the player nearest the ball gets the stronger press role")

              testCase "GK never gets the press role" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let gkBias = world.Home.Frame.RoleBias.[0]

                  let maxOutfield =
                      [ for s in MatchWorld.activeSlots world.Home.Frame do
                            if s <> 0 then
                                world.Home.Frame.RoleBias.[s] ]
                      |> List.max

                  Expect.isLessThan gkBias maxOutfield "the keeper keeps a neutral role")

              testCase "reassigns only after a directive change" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  let team = MatchWorld.team world ClubSide.Home
                  team.Directive.SinceTick <- 0<subtick>
                  let before = team.Frame.RoleBias.[5]

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.equal team.Frame.RoleBias.[5] before "no directive change -> no reassignment") ]
