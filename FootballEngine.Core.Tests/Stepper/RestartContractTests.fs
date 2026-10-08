namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Restart tests cover the state machine and physical preparation contract.
module RestartContractTests =

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private stepContext ctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let restartContractTests =
        testList
            "FootballEngine.Core.Stepper.RestartContractTests"
            [ testCase "throw-in restart places the ball inside the field" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  MatchWorld.updateBall world (fun ball ->
                      { ball with
                          Position =
                              { ball.Position with
                                  X = 20.0<meter>
                                  Y = Pitch.HalfWidth } })

                  EventApply.apply
                      ctx
                      world
                      { Tick = 0<subtick>
                        Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Home) }

                  match world.State.Flow with
                  | MatchFlow.Restart restart ->
                      Expect.isLessThan
                          (abs (float restart.Spot.Y))
                          (float Pitch.HalfWidth - 0.1)
                          "the throw-in ball must sit inside the touchline"
                  | flow -> failtestf "expected a restart delay, got %A" flow)

              testCase "restart preparation signals its football cause" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()
                  let buffer = buffer ()

                  buffer.Events.Add
                      { Tick = world.State.Tick
                        Data = EventData.SetPiece(SetPieceKind.Corner, ClubSide.Home) }

                  Stepper.step Scheduler.systems (stepContext ctx) (Rng.ofSeed 5UL) world buffer
                  |> ignore

                  Expect.equal
                      world.Officials.Signal
                      (RefereeSignal.RestartDirection(SetPieceKind.Corner, ClubSide.Home))
                      "preparation exposes the awarded corner rather than a generic whistle")

              testCase "free-kick walls remain legal when radial targets clip pitch boundaries" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()

                  for x in [ -51.0; 7.318; 51.0 ] do
                      for y in [ -33.0; 33.0 ] do
                          let world = TestBuilders.minimalWorld ()
                          world.Away.Frame.PosX[1] <- float32 (x + 0.5)
                          world.Away.Frame.PosY[1] <- float32 (y + (if y < 0.0 then -0.5 else 0.5))

                          match
                              RestartPlanner.plan
                                  ctx
                                  world
                                  (RestartId 1UL)
                                  (RestartCause.Foul(None, None))
                                  SetPieceKind.FreeKick
                                  ClubSide.Home
                                  (x * 1.0<meter>)
                                  (y * 1.0<meter>)
                                  0<subtick>
                          with
                          | Error error -> failtestf "planning failed: %A" error
                          | Ok plan ->
                              for target in plan.Targets do
                                  if target.Role = RestartRole.Wall then
                                      let distance =
                                          MatchMath.dist plan.Spot.X plan.Spot.Y target.Position.X target.Position.Y

                                      Expect.isGreaterThanOrEqual
                                          (distance + float ctx.Config.SetPiece.TargetTolerance)
                                          (float ctx.Config.SetPiece.WallDistance)
                                          "clipping cannot violate exclusion distance"

                                      Expect.isLessThanOrEqual
                                          (abs (float target.Position.X))
                                          (float Pitch.HalfLength)
                                          "target stays inside goal lines"

                                      Expect.isLessThanOrEqual
                                          (abs (float target.Position.Y))
                                          (float Pitch.HalfWidth)
                                          "target stays inside touchlines")

              testCase "seed 11 completes the previously blocked boundary free kick" (fun _ ->
                  let input =
                      { MatchId = 11UL
                        Context = TestBuilders.defaultCtx ()
                        Seed = 11UL
                        EffectiveConfig = EffectiveConfig.defaultConfig
                        EngineVersion = "restart-regression" }

                  let unwrap =
                      function
                      | Ok value -> value
                      | Error error -> failtestf "%A" error

                  let session =
                      MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

                  MatchSession.advanceTo 160000<subtick> session |> unwrap |> ignore

                  Expect.isTrue
                      (session.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.RestartCompleted(RestartId 7UL) -> true
                           | _ -> false))
                      "legal wall targets allow physical preparation and execution without forced authorization")

              testCase "kickoff targets remain outside the centre circle after projection" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let opponents = MatchWorld.oppOf side
                      let frame = (MatchWorld.team world opponents).Frame

                      for slot in MatchWorld.activeSlots frame do
                          frame.PosX.[slot] <- -0.008f
                          frame.PosY.[slot] <- -0.001584f

                      match
                          RestartPlanner.plan
                              ctx
                              world
                              (RestartId 1UL)
                              MatchStart
                              SetPieceKind.KickOff
                              side
                              0.0<meter>
                              0.0<meter>
                              0<subtick>
                      with
                      | Error error -> failtestf "kickoff planning failed: %A" error
                      | Ok plan ->
                          for target in plan.Targets do
                              if target.Side = opponents then
                                  Expect.isGreaterThanOrEqual
                                      (MatchMath.dist target.Position.X target.Position.Y 0.0<meter> 0.0<meter>)
                                      9.15
                                      "projected opponent targets satisfy the strict legal radius")

              testCase "restart executor holds the contact spot while clustered teammates yield" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let frame = (MatchWorld.team world side).Frame
                      Array.fill frame.PosX 0 frame.SlotCount 0.0f
                      Array.fill frame.PosY 0 frame.SlotCount 0.0f

                      match
                          RestartPlanner.plan
                              ctx
                              world
                              (RestartId 1UL)
                              MatchStart
                              SetPieceKind.KickOff
                              side
                              0.0<meter>
                              0.0<meter>
                              0<subtick>
                      with
                      | Error error -> failtestf "kickoff planning failed: %A" error
                      | Ok plan ->
                          let restart =
                              TestBuilders.restartState SetPieceKind.KickOff side plan.Spot RestartPhase.Positioning

                          world.State <-
                              { world.State with
                                  Flow = MatchFlow.Restart { restart with Plan = Some plan } }

                          let slot = MatchWorld.slotOf ctx side plan.Executor |> Option.get
                          let events = buffer ()
                          let ctx' = stepContext ctx
                          RestartController.step ctx' (Rng.ofSeed 17UL) world events |> ignore
                          PlayerSteering.step ctx' (Rng.ofSeed 17UL) world events |> ignore
                          Expect.equal frame.PosX.[slot] 0.0f "executor remains at the ball on the X axis"
                          Expect.equal frame.PosY.[slot] 0.0f "executor remains at the ball on the Y axis"

                          Expect.isTrue
                              (MatchWorld.activeSlots frame
                               |> Array.exists (fun teammate ->
                                   teammate <> slot
                                   && (frame.PosX.[teammate] <> 0.0f || frame.PosY.[teammate] <> 0.0f)))
                              "separation still moves overlapping teammates away from the executor")

              testCase "restart preparation moves through steering" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let world = TestBuilders.formationWorld ()

                  let executor, restart =
                      ctx.HomeLineup.Profiles.[9].Id,
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Positioning

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Restart restart }

                  let before = Array.copy world.Home.Frame.PosX
                  let mutable rng = Rng.ofSeed 17UL
                  let ctx' = stepContext ctx

                  for _ in 1..8 do
                      let next, _, _ = Stepper.step Scheduler.systems ctx' rng world (buffer ())
                      rng <- next

                  let executorSlot = MatchWorld.slotOf ctx ClubSide.Home executor |> Option.get

                  Expect.isTrue
                      (MatchWorld.activeSlots world.Home.Frame
                       |> Array.exists (fun slot ->
                           slot <> executorSlot && world.Home.Frame.PosX.[slot] <> before.[slot]))
                      "non-executors move toward preparation targets"

                  Expect.equal world.State.Ball.Position.X 0.0<meter> "the dead ball remains fixed")

              testCase "controlled ball crossing the line executes a real throw-in" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  let ctx' = stepContext ctx
                  let events = ResizeArray<MatchEvent>()
                  let mutable rng = Rng.ofSeed 23UL
                  let buffer = buffer ()
                  let mutable restartImpulse = None

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, pid)
                                  LastTouch = Some pid
                                  Position =
                                      { world.State.Ball.Position with
                                          Y = Pitch.HalfWidth + 0.5<meter> } } }

                  for _ in 1..4000 do
                      let next, _, applied = Stepper.step Scheduler.systems ctx' rng world buffer
                      rng <- next

                      for event in applied do
                          events.Add event

                      match world.State.Ball.Impulse with
                      | Some impulse when impulse.StrikeKind = StrikeKind.Restart SetPieceKind.ThrowIn ->
                          restartImpulse <- Some impulse
                      | _ -> ()

                  Expect.isTrue
                      (events
                       |> Seq.exists (fun event ->
                           event.Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Away)))
                      "the referee awards the throw-in"

                  match restartImpulse with
                  | Some impulse ->
                      Expect.equal
                          impulse.StrikeKind
                          (StrikeKind.Restart SetPieceKind.ThrowIn)
                          "the restart uses the normal ball impulse path"
                  | None -> failtest "the throw-in never launched the ball")

              testCase "all restart kinds execute through an authoritative restart impulse" (fun _ ->
                  for kind in
                      [ SetPieceKind.KickOff
                        SetPieceKind.ThrowIn
                        SetPieceKind.Corner
                        SetPieceKind.GoalKick
                        SetPieceKind.FreeKick
                        SetPieceKind.Penalty ] do
                      let ctx = TestBuilders.defaultCtx ()
                      let world = TestBuilders.minimalWorld ()
                      let ctx' = stepContext ctx
                      let buffer = buffer ()

                      buffer.Events.Add
                          { Tick = world.State.Tick
                            Data = EventData.SetPiece(kind, ClubSide.Home) }

                      let mutable rng = Rng.ofSeed 101UL
                      let mutable started = false
                      let mutable restartImpulse = None
                      let mutable ticks = 0

                      while (not started || world.State.Flow <> MatchFlow.Live) && ticks < 10000 do
                          let next, _, _ = Stepper.step Scheduler.systems ctx' rng world buffer
                          rng <- next
                          started <- true
                          ticks <- ticks + 1

                          match world.State.Ball.Impulse with
                          | Some impulse when impulse.StrikeKind = StrikeKind.Restart kind ->
                              restartImpulse <- Some impulse
                          | _ -> ()

                          for entry in world.Observation.Snapshot().Entries do
                              match entry.Data with
                              | ImpulseApplied impulse when impulse.StrikeKind = StrikeKind.Restart kind ->
                                  restartImpulse <- Some impulse
                              | _ -> ()

                      Expect.equal world.State.Flow MatchFlow.Live (sprintf "%A returns to Live" kind)

                      match restartImpulse with
                      | Some impulse ->
                          Expect.equal impulse.StrikeKind (StrikeKind.Restart kind) "restart strike kind is retained"

                          Expect.equal
                              impulse.ExecutionContext
                              (ExecutionContext.SetPiece kind)
                              "set-piece execution context is retained"
                      | None -> failtestf "%A did not create an impulse" kind)

              testCase "a loose ball never sticks to the touchline during live play" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = MatchFactory.create ctx
                  let ctx' = stepContext ctx
                  let buffer = buffer ()
                  let mutable rng = Rng.ofSeed 1UL

                  for _ in 1..90000 do
                      let next, _, _ = Stepper.step Scheduler.systems ctx' rng world buffer
                      rng <- next

                      if world.State.Flow = MatchFlow.Live then
                          let ball = world.State.Ball

                          if
                              ball.Control = BallControl.Free
                              && abs (float ball.Position.Y) > float Pitch.HalfWidth - 0.5
                          then
                              Expect.isGreaterThan
                                  (MatchMath.speedOf ball.Position.Vx ball.Position.Vy)
                                  0.05
                                  "a loose ball near the touchline must keep moving") ]
