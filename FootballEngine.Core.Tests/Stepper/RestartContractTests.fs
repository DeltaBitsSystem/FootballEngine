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

                  Stepper.step Scheduler.systems (stepContext ctx) (Rng.ofSeed 5UL) world buffer |> ignore

                  Expect.equal
                      world.Officials.Signal
                      (RefereeSignal.RestartDirection(SetPieceKind.Corner, ClubSide.Home))
                      "preparation exposes the awarded corner rather than a generic whistle")

              testCase "restart preparation moves through steering" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let world = TestBuilders.formationWorld ()
                  let executor, restart = ctx.HomeLineup.Profiles.[9].Id, TestBuilders.restartState SetPieceKind.KickOff ClubSide.Home (Vec2.create 0.0<meter> 0.0<meter>) RestartPhase.Positioning

                  world.State <- { world.State with Flow = MatchFlow.Restart restart }
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
                           slot <> executorSlot
                           && world.Home.Frame.PosX.[slot] <> before.[slot]))
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
                      (events |> Seq.exists (fun event -> event.Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Away)))
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
