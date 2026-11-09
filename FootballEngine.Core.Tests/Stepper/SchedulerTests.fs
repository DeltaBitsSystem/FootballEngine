namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module SchedulerTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private markerSystem: SystemSpec =
        { Name = "Marker"
          Triggers = []
          Step =
            fun _ rng world buffer ->
                buffer.Events.Add
                    { Tick = world.State.Tick
                      Data = EventData.MomentumShift ClubSide.Home }

                rng }

    let private countMarks (buf: EventBuffer) : int =
        buf.Events
        |> Seq.filter (fun ev ->
            match ev.Data with
            | EventData.MomentumShift ClubSide.Home -> true
            | _ -> false)
        |> Seq.length

    let schedulerTests =
        testList
            "FootballEngine.Core.Stepper.SchedulerTests"
            [ testCase "Drained events are applied, flagged, and cleared" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  let buf = buffer ()

                  buf.Events.Add
                      { Tick = 0<subtick>
                        Data = EventData.Possession(ClubSide.Home, pid) }

                  let rng, _, applied = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.equal applied.Count 1 "one event applied"

                  match applied[0].Data with
                  | EventData.Possession(ClubSide.Home, pid') -> Expect.equal pid' pid "same event"
                  | other -> failtestf "expected Possession, got %A" other

                  Expect.equal buf.Events.Count 0 "buffer cleared"
                  Expect.equal buf.Applied.Count 1 "applied kept"

                  Expect.isTrue
                      (EventFlags.intersects buf.Flags (EventFlags.ofKind EventKind.PossessionGained))
                      "flag set"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid') -> Expect.equal pid' pid "ball controlled"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "EveryN fires on multiples of N" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.EveryN 5<tickDelta> ] }

                  let buf = buffer ()
                  let mutable total = 0

                  for t in 0..12 do
                      world.State <-
                          { world.State with
                              Tick = t * 1<subtick> }

                      let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                      total <- total + countMarks buf

                  Expect.equal total 3 "fires at ticks 0, 5, 10")

              testCase "WhenLive fires only while the flow is Live" (fun _ ->
                  let world, restart =
                      TestBuilders.minimalWorld (),
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Positioning

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Restart restart }

                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.WhenLive Trigger.EveryTick ] }

                  let buf = buffer ()
                  let mutable total = 0

                  for t in 0..5 do
                      world.State <-
                          { world.State with
                              Tick = t * 1<subtick> }

                      let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                      total <- total + countMarks buf

                  Expect.equal total 0 "setup and ready phases never become Live without an impulse")

              testCase "WhenMovementAllowed fires only for Live and Restart" (fun _ ->
                  let restart =
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Positioning

                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.WhenMovementAllowed Trigger.EveryTick ] }

                  let run flow =
                      let world = TestBuilders.minimalWorld ()
                      world.State <- { world.State with Flow = flow }
                      let buf = buffer ()
                      let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                      countMarks buf

                  Expect.equal (run MatchFlow.Live) 1 "movement is allowed in Live"
                  Expect.equal (run (MatchFlow.Restart restart)) 1 "movement is allowed in Restart"

                  Expect.equal
                      (run (MatchFlow.GoalPause(3<tickDelta>, ClubSide.Home, Some ctx.HomeLineup.Profiles.[1].Id)))
                      0
                      "goal pause blocks movement"

                  Expect.equal (run (MatchFlow.VARReview(3<tickDelta>, VARIncident.Offside))) 0 "VAR blocks movement"
                  Expect.equal (run (MatchFlow.HalfTime(3<tickDelta>))) 0 "halftime blocks movement"
                  Expect.equal (run MatchFlow.MatchEnded) 0 "ended match blocks movement")

              testCase "WhenBallDead fires only while a goal pause or VAR review holds play" (fun _ ->
                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.WhenBallDead Trigger.EveryTick ] }

                  let run flow =
                      let world = TestBuilders.minimalWorld ()
                      world.State <- { world.State with Flow = flow }
                      let buf = buffer ()
                      let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                      countMarks buf

                  Expect.equal
                      (run (MatchFlow.GoalPause(3<tickDelta>, ClubSide.Home, Some ctx.HomeLineup.Profiles.[1].Id)))
                      1
                      "a scored ball still flies during the goal pause"

                  Expect.equal (run (MatchFlow.VARReview(3<tickDelta>, VARIncident.Offside))) 1 "and during VAR"
                  Expect.equal (run MatchFlow.Live) 0 "live play has its own ball physics"
                  Expect.equal (run (MatchFlow.HalfTime(3<tickDelta>))) 0 "halftime is not a dead-ball flight")

              testCase "OnEvent fires only when a matching event is drained" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.OnEvent(EventFlags.ofKind EventKind.Foul) ] }

                  let buf = buffer ()

                  let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                  Expect.equal (countMarks buf) 0 "no event, no fire"

                  buf.Events.Add
                      { Tick = 1<subtick>
                        Data =
                          EventData.Foul(
                              ctx.AwayLineup.Profiles.[5].Id,
                              ctx.HomeLineup.Profiles.[5].Id,
                              FoulSeverity.Minor,
                              CardDecision.NoCard,
                              (0.0<meter>, 0.0<meter>)
                          ) }

                  let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                  Expect.equal (countMarks buf) 1 "fired on foul event")

              testCase "OnEventOrEveryN fires on event or cadence" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.OnEventOrEveryN(EventFlags.ofKind EventKind.Foul, 7<tickDelta>) ] }

                  let buf = buffer ()

                  let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                  Expect.equal (countMarks buf) 1 "fires at tick 0"

                  buf.Events.Add
                      { Tick = 1<subtick>
                        Data =
                          EventData.Foul(
                              ctx.AwayLineup.Profiles.[5].Id,
                              ctx.HomeLineup.Profiles.[5].Id,
                              FoulSeverity.Minor,
                              CardDecision.NoCard,
                              (0.0<meter>, 0.0<meter>)
                          ) }

                  let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                  Expect.equal (countMarks buf) 1 "fires on the foul")

              testCase "EveryMinute fires at minute boundaries" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let system =
                      { markerSystem with
                          Triggers = [ Trigger.EveryMinute(2<matchMin>, 0<matchMin>) ] }

                  let buf = buffer ()
                  let mutable total = 0

                  for t in 0..4800 do
                      world.State <-
                          { world.State with
                              Tick = t * 1<subtick> }

                      let _, _, _ = Stepper.step [ system ] sctx (Rng.ofSeed 3UL) world buf
                      total <- total + countMarks buf

                  Expect.equal total 2 "fires at minutes 0 and 2") ]
