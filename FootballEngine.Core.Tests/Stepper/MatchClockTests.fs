namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module MatchClockTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let matchClockTests =
        testList
            "FootballEngine.Core.Stepper.MatchClockTests"
            [ testCase "Default clock is 40 subticks per second" (fun _ ->
                  Expect.equal SimulationClock.defaultClock.SubTicksPerSecond 40 "40 subticks/s"
                  Expect.equal (float (SimulationClock.dt SimulationClock.defaultClock)) 0.025 "dt = 1/40 s"
                  Expect.equal (float (SimulationClock.dtPlayer SimulationClock.defaultClock)) 0.05 "dtPlayer = 2/40 s")

              testCase "Match minute derives from tick" (fun _ ->
                  Expect.equal (SimulationClock.toMatchMinute 0<subtick>) 0<matchMin> "tick 0 is minute 0"
                  Expect.equal (SimulationClock.toMatchMinute 2399<subtick>) 0<matchMin> "tick 2399 is minute 0"
                  Expect.equal (SimulationClock.toMatchMinute 2400<subtick>) 1<matchMin> "tick 2400 is minute 1"
                  Expect.equal (SimulationClock.toMatchMinute 108000<subtick>) 45<matchMin> "tick 108000 is minute 45"
                  Expect.equal (SimulationClock.toMatchMinute 216000<subtick>) 90<matchMin> "tick 216000 is minute 90")

              testCase "Clock advances while Live" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.equal world.State.Clock.Tick 1<subtick> "clock advanced")

              testCase "Clock advances during a restart delay" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let plan =
                      { Kind = SetPieceKind.Corner
                        Side = ClubSide.Home
                        X = Pitch.awayGoalX
                        Y = Pitch.HalfWidth }

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.RestartDelay(4<tickDelta>, plan) }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.equal world.State.Clock.Tick 1<subtick> "clock advanced during delay")

              testCase "Clock freezes during pauses and halftime" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.GoalPause(4<tickDelta>, ClubSide.Home, None) }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.equal world.State.Clock.Tick 0<subtick> "clock frozen in pause"

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.HalfTime(4<tickDelta>) }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.equal world.State.Clock.Tick 0<subtick> "clock frozen at halftime") ]
