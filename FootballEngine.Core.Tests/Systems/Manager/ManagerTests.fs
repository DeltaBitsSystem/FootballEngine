namespace FootballEngine.Core.Tests.Systems.Manager

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ManagerTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private minuteTick (minute: int) : int<subtick> =
        minute * int SimulationClock.minuteSubticks * 1<subtick>

    let managerTests =
        testList
            "FootballEngine.Core.Systems.Manager.ManagerTests"
            [ testCase "substitutes a very tired player" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  // A clearly exhausted outfield player at a sub-allowed minute.
                  for s in 1..10 do
                      world.Home.Frame.Condition.[s] <- 0.9f

                  world.Home.Frame.Condition.[5] <- 0.2f

                  // Reserve on the bench: sideline slot 10 manually.
                  world.Home.Frame.Occupancy.[10] <-
                      OccupancyKind.Sidelined
                          { Id = ctx.HomeLineup.Profiles.[10].Id
                            Reason = PlayerOutReason.Substituted }

                  world.State <-
                      { world.State with
                          Tick = minuteTick 60 }

                  let b = buffer ()
                  ManagerSystem.step sctx (Rng.ofSeed 42UL) world b |> ignore

                  let subEvents =
                      b.Events
                      |> Seq.filter (fun ev -> MatchEvent.kind ev = EventKind.Substitution)
                      |> Seq.toList

                  Expect.isNonEmpty subEvents "a substitution happens for the exhausted player")

              testCase "red card compacts the shape" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.Home.Directive.Kind <- DirectiveKind.Balanced

                  let pid = ctx.HomeLineup.Profiles.[4].Id
                  let b = buffer ()

                  b.Applied.Add
                      { Tick = world.State.Tick
                        Data = EventData.RedCardIssued pid }

                  ManagerSystem.step sctx (Rng.ofSeed 42UL) world b |> ignore

                  Expect.equal world.Home.Directive.Kind DirectiveKind.Defend "red card switches to a compact shape")

              testCase "losing late pushes attack" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          HomeScore = 0
                          AwayScore = 1 }

                  world.State <-
                      { world.State with
                          Tick = minuteTick 80 }

                  ManagerSystem.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.equal world.Home.Directive.Kind DirectiveKind.Attack "losing late pushes forward") ]
