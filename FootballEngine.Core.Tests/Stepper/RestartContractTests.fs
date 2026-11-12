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

              testCase "a throw-in receiver standing on the touchline is planned inside the field" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  // Everyone chased the ball out of play: the planned receiver starts on the line itself.
                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      world.Home.Frame.PosX[slot] <- 20.0f + float32 slot
                      world.Home.Frame.PosY[slot] <- float32 Pitch.HalfWidth

                  match
                      RestartPlanner.plan
                          ctx
                          world
                          (RestartId 1UL)
                          (RestartCause.BallCrossedTouchline None)
                          SetPieceKind.ThrowIn
                          ClubSide.Home
                          20.0<meter>
                          Pitch.HalfWidth
                          0<subtick>
                  with
                  | Error error -> failtestf "planning failed: %A" error
                  | Ok plan ->
                      let receiver =
                          plan.Targets |> Array.find (fun target -> target.Role = RestartRole.Receiver)

                      Expect.isLessThanOrEqual
                          (abs (float receiver.Position.Y))
                          (float Pitch.HalfWidth - float ctx.Config.SetPiece.ThrowInReceiverInset + 1e-9)
                          "the receiver is planned on the field, clear of the line"

                      let geometry =
                          (RestartRules.validate ctx world plan).Violations
                          |> List.filter (fun violation -> violation.Rule = RestartRules.Rule.ThrowInGeometry)

                      Expect.isEmpty geometry "positioning can satisfy the plan, so the restart cannot stall")

              // A plan that no amount of positioning can make legal freezes the match with no events;
              // full matches must keep every restart moving whatever geometry play produces.
              testCase "seeded matches never stall inside a restart" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()

                  for seed in [ 101UL; 102UL; 103UL; 104UL; 105UL ] do
                      let out =
                          Simulator.runMatch Scheduler.systems ctx seed Simulator.RunMode.Fast
                          |> TestBuilders.requireMatchOutcome

                      let planned = System.Collections.Generic.Dictionary<RestartId, int<subtick>>()
                      let issued = System.Collections.Generic.Dictionary<RestartId, int>()
                      let mutable longest = 0<subtick>

                      for event in out.Events do
                          match event.Data with
                          | EventData.RestartPlanned(id, _) ->
                              // The match holds one restart; a new award (a foul in the tick the ball went out)
                              // replaces the one in progress, which then never completes.
                              if not (planned.ContainsKey id) then
                                  planned.Clear()
                                  planned[id] <- event.Tick
                          | EventData.RestartCommandIssued(id, _) ->
                              issued[id] <- (match issued.TryGetValue id with | true, n -> n | _ -> 0) + 1
                          // The whistle ends a period with the ball dead; abandoning that restart is the law, but a
                          // restart frozen until the whistle still counts: it once hid a penalty that never was taken.
                          | EventData.HalfTime
                          | EventData.FullTime ->
                              for start in planned.Values do
                                  longest <- max longest (event.Tick - start)

                              planned.Clear()
                          | EventData.RestartCompleted id ->
                              match planned.TryGetValue id with
                              | true, start ->
                                  longest <- max longest (event.Tick - start)
                                  planned.Remove id |> ignore
                              | _ -> ()
                          | _ -> ()

                      let pending = planned |> Seq.map (fun pair -> pair.Key, int pair.Value) |> List.ofSeq

                      Expect.isEmpty
                          pending
                          (sprintf "seed %d: every planned restart completes, pending %A (match ended at tick %d)" seed pending (int out.Final.State.Tick))

                      Expect.isLessThan
                          longest
                          (SimulationClock.secondsToSubTicks SimulationClock.defaultClock 60.0<second>)
                          (sprintf "seed %d: no restart takes a minute of match time" seed)

                      // A kick the taker cannot complete is re-issued; tens of thousands of re-issues per match once
                      // hid a teammate standing on the free-kick spot.
                      let reissued = issued |> Seq.map _.Value |> Seq.fold max 0
                      Expect.isLessThanOrEqual reissued 5 (sprintf "seed %d: no restart command is re-issued endlessly" seed))

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

              testCase "corner opponents clear the spot before the physical restart can be issued" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()
                  let spot = Vec2.create (MatchMath.goalX ClubSide.Home) -Pitch.HalfWidth

                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      world.Home.Frame.PosX[slot] <- -40.0f + float32 slot * 1.5f
                      world.Home.Frame.PosY[slot] <- -24.0f + float32 slot * 3.0f

                  for slot in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX[slot] <- -20.0f + float32 slot * 1.5f
                      world.Away.Frame.PosY[slot] <- -24.0f + float32 slot * 3.0f

                  let executorSlot = 1
                  let nearbyOpponentSlot = 3
                  world.Home.Frame.PosX[executorSlot] <- float32 (spot.X - 1.0<meter>)
                  world.Home.Frame.PosY[executorSlot] <- float32 spot.Y
                  world.Away.Frame.PosX[nearbyOpponentSlot] <- float32 spot.X
                  world.Away.Frame.PosY[nearbyOpponentSlot] <- float32 spot.Y

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          (RestartId 1UL)
                          (RestartCause.BallCrossedGoalLine None)
                          SetPieceKind.Corner
                          ClubSide.Home
                          spot.X
                          spot.Y
                          0<subtick>
                      |> Result.defaultWith (fun error -> failtestf "corner planning failed: %A" error)

                  let opponentId = ctx.AwayLineup.Profiles[nearbyOpponentSlot].Id
                  let opponentTarget = plan.Targets |> Array.find (fun target -> target.Player = opponentId)
                  let required = float ctx.Config.SetPiece.WallDistance
                  let targetDistance = MatchMath.dist spot.X spot.Y opponentTarget.Position.X opponentTarget.Position.Y
                  Expect.isGreaterThanOrEqual
                      (targetDistance + float ctx.Config.SetPiece.TargetTolerance)
                      required
                      "the projected corner target is outside the exclusion radius"
                  Expect.isFalse (RestartRules.validate ctx world plan).Valid "a corner remains unauthorized until the opponent moves away"

                  let events = buffer ()
                  events.Events.Add
                      { Tick = world.State.Tick
                        Data = EventData.SetPieceAt(SetPieceKind.Corner, ClubSide.Home, spot.X, spot.Y) }

                  let mutable rng = Rng.ofSeed 102UL
                  let mutable ticks = 0
                  let mutable resumed = false
                  let mutable restartContact = false

                  while not resumed && ticks < 2000 do
                      let next, _, applied = Stepper.step Scheduler.systems (stepContext ctx) rng world events
                      rng <- next
                      ticks <- ticks + 1

                      for event in applied do
                          match event.Data with
                          | EventData.RestartContact(_, actor, SetPieceKind.Corner) when actor = plan.Executor ->
                              restartContact <- true
                          | _ -> ()

                      resumed <- restartContact && world.State.Flow = MatchFlow.Live

                  Expect.isTrue restartContact "the corner must reach the physical strike instead of repeating cancellations"
                  Expect.isTrue resumed "the completed corner returns the match to live play")

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
                          ContactDynamics.step ctx' (Rng.ofSeed 17UL) world events |> ignore
                          Expect.isLessThanOrEqual (abs frame.PosX.[slot]) 0.001f "executor remains within one millimetre of the ball on the X axis"
                          Expect.isLessThanOrEqual (abs frame.PosY.[slot]) 0.001f "executor remains within one millimetre of the ball on the Y axis"

                          Expect.isTrue
                              (MatchWorld.activeSlots frame
                               |> Array.exists (fun teammate ->
                                   teammate <> slot
                                   && (frame.PosX.[teammate] <> 0.0f || frame.PosY.[teammate] <> 0.0f)))
                              "separation still moves overlapping teammates away from the executor")

              testCase "restart executor overlap does not consume a prepared physical strike" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let world = TestBuilders.formationWorld ()
                  let systemCtx = stepContext ctx
                  let events = buffer ()

                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let frame = (MatchWorld.team world side).Frame

                      for slot in MatchWorld.activeSlots frame do
                          if side = ClubSide.Home && slot = 9 then
                              frame.PosX[slot] <- 0.0f
                              frame.PosY[slot] <- 0.0f
                          else
                              frame.PosX[slot] <- (if side = ClubSide.Home then -40.0f else 20.0f) + float32 slot * 2.0f
                              frame.PosY[slot] <- if slot % 2 = 0 then -8.0f else 8.0f

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          (RestartId 1UL)
                          MatchStart
                          SetPieceKind.KickOff
                          ClubSide.Home
                          0.0<meter>
                          0.0<meter>
                          0<subtick>
                      |> Result.defaultWith (fun error -> failtestf "kickoff planning failed: %A" error)

                  world.State <-
                      { world.State with
                          Flow =
                              MatchFlow.Restart
                                  { TestBuilders.restartState SetPieceKind.KickOff ClubSide.Home plan.Spot RestartPhase.Authorized with
                                      Plan = Some plan } }

                  RestartController.step systemCtx (Rng.ofSeed 17UL) world events |> ignore
                  let pending = world.State.Ball.PendingStrike |> Option.defaultWith (fun () -> failtest "kickoff did not prepare a strike")

                  ContactDynamics.step systemCtx (Rng.ofSeed 17UL) world events |> ignore

                  Expect.isSome world.State.Ball.PendingStrike "the executor's resting foot is not a second strike"
                  Expect.isFalse
                      (events.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.FootStrikeCancelled _ -> true
                           | _ -> false))
                      "an isolated restart executor cannot cancel its own windup"

                  world.State <- { world.State with Tick = SimTick.value pending.Impulse.ContactTick }
                  BallFootContact.step systemCtx (Rng.ofSeed 17UL) world events |> ignore
                  ContactDynamics.step systemCtx (Rng.ofSeed 17UL) world events |> ignore

                  Expect.isSome world.State.Ball.Impulse "the prepared strike commits at its scheduled contact"
                  Expect.isTrue
                      (events.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.RestartContact(id, actor, SetPieceKind.KickOff) ->
                               id = plan.Id && actor = plan.Executor
                           | _ -> false))
                      "restart completion is backed by the executor's physical strike")

              testCase "another player's physical touch cancels and replans a restart strike" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let world = TestBuilders.formationWorld ()
                  let systemCtx = stepContext ctx
                  let events = buffer ()

                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let frame = (MatchWorld.team world side).Frame

                      for slot in MatchWorld.activeSlots frame do
                          if side = ClubSide.Home && slot = 9 then
                              frame.PosX[slot] <- 0.0f
                              frame.PosY[slot] <- 0.0f
                          elif side = ClubSide.Away && slot = 5 then
                              frame.PosX[slot] <- -0.15f
                              frame.PosY[slot] <- 0.0f
                          else
                              frame.PosX[slot] <- (if side = ClubSide.Home then -40.0f else 20.0f) + float32 slot * 2.0f
                              frame.PosY[slot] <- if slot % 2 = 0 then -8.0f else 8.0f

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          (RestartId 1UL)
                          MatchStart
                          SetPieceKind.KickOff
                          ClubSide.Home
                          0.0<meter>
                          0.0<meter>
                          0<subtick>
                      |> Result.defaultWith (fun error -> failtestf "kickoff planning failed: %A" error)

                  world.State <-
                      { world.State with
                          Flow =
                              MatchFlow.Restart
                                  { TestBuilders.restartState SetPieceKind.KickOff ClubSide.Home plan.Spot RestartPhase.Authorized with
                                      Plan = Some plan } }

                  RestartController.step systemCtx (Rng.ofSeed 23UL) world events |> ignore
                  ContactDynamics.step systemCtx (Rng.ofSeed 23UL) world events |> ignore

                  Expect.isNone world.State.Ball.PendingStrike "a physically distinct player invalidates the prepared path"

                  let cancellation =
                      events.Events
                      |> Seq.tryFind (fun event ->
                          match event.Data with
                          | EventData.FootStrikeCancelled actor -> actor = plan.Executor
                          | _ -> false)
                      |> Option.defaultWith (fun () -> failtest "external contact did not publish strike cancellation")

                  events.Applied.Add cancellation
                  RestartController.step systemCtx (Rng.ofSeed 23UL) world events |> ignore

                  match world.State.Flow with
                  | MatchFlow.Restart restart ->
                      Expect.equal restart.Phase RestartPhase.Positioning "the controller retries from a fresh legal position"
                      Expect.isFalse restart.CommandIssued "a canceled command is not left marked as issued"
                  | flow -> failtestf "external contact ended the restart unexpectedly: %A" flow)

              testCase "an external goal-kick touch restores the legal spot before retry" (fun _ ->
                   let ctx = TestBuilders.formationCtx ()
                   let world = TestBuilders.formationWorld ()
                   let systemCtx = stepContext ctx
                   let events = buffer ()

                   for slot in MatchWorld.activeSlots world.Home.Frame do
                       world.Home.Frame.PosX[slot] <- -38.0f + float32 slot * 1.2f
                       world.Home.Frame.PosY[slot] <- float32 slot * 1.5f

                   for slot in MatchWorld.activeSlots world.Away.Frame do
                       world.Away.Frame.PosX[slot] <- 25.0f + float32 slot
                       world.Away.Frame.PosY[slot] <- float32 slot * 1.5f

                   let plan =
                       RestartPlanner.plan
                           ctx
                           world
                           (RestartId 1UL)
                           (RestartCause.BallCrossedGoalLine None)
                           SetPieceKind.GoalKick
                           ClubSide.Home
                           (MatchMath.ownGoalX ClubSide.Home)
                           0.0<meter>
                           0<subtick>
                       |> Result.defaultWith (fun error -> failtestf "goal-kick planning failed: %A" error)

                   let executorSlot = MatchWorld.slotOf ctx ClubSide.Home plan.Executor |> Option.get
                   world.Home.Frame.PosX[executorSlot] <- float32 plan.Spot.X
                   world.Home.Frame.PosY[executorSlot] <- float32 plan.Spot.Y

                   let contactorSlot = 5
                   world.Away.Frame.PosX[contactorSlot] <- float32 plan.Spot.X + 0.15f
                   world.Away.Frame.PosY[contactorSlot] <- float32 plan.Spot.Y

                   let restart =
                       { TestBuilders.restartState SetPieceKind.GoalKick ClubSide.Home plan.Spot RestartPhase.Authorized with
                           Id = plan.Id
                           Plan = Some plan }

                   world.State <-
                       { world.State with
                           Flow = MatchFlow.Restart restart }

                   MatchWorld.placeDeadBall world plan.Spot
                   RestartController.step systemCtx (Rng.ofSeed 37UL) world events |> ignore
                   ContactDynamics.step systemCtx (Rng.ofSeed 37UL) world events |> ignore

                   Expect.isTrue
                       (events.Events
                        |> Seq.exists (fun event ->
                            match event.Data with
                            | EventData.PhysicalContact contact -> contact.BallTouched && contact.Actor = ctx.AwayLineup.Profiles[contactorSlot].Id
                            | _ -> false))
                       "the opponent cancels the prepared strike with a physical touch"

                   Expect.isTrue
                       (events.Events
                        |> Seq.exists (fun event ->
                            match event.Data with
                            | EventData.FootStrikeCancelled actor -> actor = plan.Executor
                            | _ -> false))
                       "the external touch invalidates the pending goal kick"

                   Expect.isGreaterThan
                       (MatchMath.dist world.State.Ball.Position.X world.State.Ball.Position.Y plan.Spot.X plan.Spot.Y)
                       0.001
                       "the physical impact displaces the dead ball before the controller restores it"

                   let mutable rng, _, _ = Stepper.step Scheduler.systems systemCtx (Rng.ofSeed 37UL) world events
                   let restored = world.State.Ball

                   Expect.equal restored.Position.X plan.Spot.X "cancellation restores the legal goal-kick X"
                   Expect.equal restored.Position.Y plan.Spot.Y "cancellation restores the legal goal-kick Y"
                   Expect.equal restored.Position.Vx 0.0<meterPerSecond> "the restored ball has no residual X velocity"
                   Expect.equal restored.Position.Vy 0.0<meterPerSecond> "the restored ball has no residual Y velocity"
                   Expect.isNone restored.PendingStrike "the cancelled strike is cleared before retry"
                   Expect.isNone restored.LastTouch "an illegal pre-restart touch does not become the official restart touch"

                   let mutable ticks = 0
                   let mutable restartContact = false

                   while world.State.Flow <> MatchFlow.Live && ticks < 10000 do
                       let next, _, applied = Stepper.step Scheduler.systems systemCtx rng world events
                       rng <- next
                       ticks <- ticks + 1

                       for event in applied do
                           match event.Data with
                           | EventData.RestartContact(id, actor, SetPieceKind.GoalKick) when id = plan.Id && actor = plan.Executor ->
                               restartContact <- true
                           | _ -> ()

                   Expect.isTrue restartContact "the retry reaches the goal kick through its physical contact"
                   Expect.equal world.State.Flow MatchFlow.Live "the completed retry returns the match to live play")

              testCase "an unsuccessful restart command cannot expose the dead ball to contact" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()
                  let systemCtx = stepContext ctx
                  let events = buffer ()

                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let frame = (MatchWorld.team world side).Frame

                      for slot in MatchWorld.activeSlots frame do
                          let x = if side = ClubSide.Home then -40.0f + float32 slot else 40.0f - float32 slot
                          let y = -20.0f + float32 slot * 4.0f
                          frame.PosX[slot] <- x
                          frame.PosY[slot] <- y

                  let contactorSlot = 4
                  world.Away.Frame.PosX[contactorSlot] <- 0.0f
                  world.Away.Frame.PosY[contactorSlot] <- 0.0f

                  world.State <-
                      { world.State with
                          Flow =
                              MatchFlow.Restart
                                  { TestBuilders.restartState
                                        SetPieceKind.Corner
                                        ClubSide.Home
                                        (Vec2.create Pitch.awayGoalX -Pitch.HalfWidth)
                                        RestartPhase.AwaitingContact with
                                      CommandIssued = true } }

                  ContactDynamics.step systemCtx (Rng.ofSeed 29UL) world events |> ignore

                  Expect.equal world.State.Ball.Position.X 0.0<meter> "an issued command without a pending or committed strike keeps the ball dead"
                  Expect.isFalse
                      (events.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.PhysicalContact contact -> contact.BallTouched
                           | _ -> false))
                      "only a real restart strike opens the physical-contact window")

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

              testCase "a restart executor cannot displace the dead ball before the command" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      world.Home.Frame.PosX[slot] <- -20.0f
                      world.Home.Frame.PosY[slot] <- float32 (-24 + slot * 3)

                  for slot in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX[slot] <- 20.0f
                      world.Away.Frame.PosY[slot] <- float32 (-24 + slot * 3)

                  let executorSlot = 1
                  world.Home.Frame.PosX[executorSlot] <- -0.2f
                  world.Home.Frame.PosY[executorSlot] <- 0.0f

                  let plan =
                      match
                          RestartPlanner.plan
                              ctx
                              world
                              (RestartId 1UL)
                              RestartCause.MatchStart
                              SetPieceKind.KickOff
                              ClubSide.Home
                              0.0<meter>
                              0.0<meter>
                              0<subtick>
                      with
                      | Ok plan -> plan
                      | Error error -> failtestf "kickoff plan failed: %A" error

                  let restart =
                      { TestBuilders.restartState
                            SetPieceKind.KickOff
                            ClubSide.Home
                            (Vec2.create 0.0<meter> 0.0<meter>)
                            RestartPhase.Positioning with
                          Plan = Some plan }

                  world.State <- { world.State with Flow = MatchFlow.Restart restart }
                  world.Home.Frame.VelX[executorSlot] <- 4.0f
                  world.Home.Frame.TargetVelX[executorSlot] <- 4.0f

                  let contacts = buffer ()
                  ContactDynamics.step (stepContext ctx) (Rng.ofSeed 31UL) world contacts |> ignore

                  Expect.equal world.State.Ball.Position.X 0.0<meter> "setup movement cannot move the dead ball"
                  Expect.equal world.State.Ball.Position.Y 0.0<meter> "setup movement cannot move the dead ball"
                  Expect.equal world.State.Ball.Position.Vx 0.0<meterPerSecond> "the ball remains stationary until commanded"
                  Expect.isFalse
                      (contacts.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.PhysicalContact contact -> contact.BallTouched
                           | _ -> false))
                      "pre-command setup cannot publish a physical ball touch")

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

              testCase "penalty players beside the spot are planned outside the area and the arc" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let spotX = MatchMath.goalX ClubSide.Home - 11.0<meter>

                  // Lateral positions sweep the arc so the radial projection lands exactly on it at many angles,
                  // where rounding once sent a defender back into the area.
                  for step in -40 .. 40 do
                      let world = TestBuilders.minimalWorld ()
                      let y = float step * 0.37
                      world.Away.Frame.PosX[4] <- float32 spotX
                      world.Away.Frame.PosY[4] <- float32 y
                      world.Home.Frame.PosX[6] <- float32 spotX - 2.0f
                      world.Home.Frame.PosY[6] <- float32 (-y)

                      let plan =
                          RestartPlanner.plan
                              ctx
                              world
                              (RestartId 1UL)
                              (RestartCause.Foul(None, None))
                              SetPieceKind.Penalty
                              ClubSide.Home
                              spotX
                              0.0<meter>
                              0<subtick>
                          |> Result.defaultWith (fun error -> failtestf "penalty planning failed: %A" error)

                      for target in plan.Targets do
                          if target.Role = RestartRole.Support then
                              Expect.isFalse
                                  (Pitch.isInPenaltyArea ClubSide.Home target.Position.X target.Position.Y)
                                  (sprintf "y=%.2f: %A is planned outside the area" y target.Player)

                              Expect.isGreaterThanOrEqual
                                  (MatchMath.dist plan.Spot.X plan.Spot.Y target.Position.X target.Position.Y + 1e-6)
                                  (float ctx.Config.SetPiece.WallDistance)
                                  (sprintf "y=%.2f: %A is planned outside the arc" y target.Player)

                      let positioned = TestBuilders.minimalWorld ()

                      for target in plan.Targets do
                          let team = MatchWorld.team positioned target.Side
                          let slot = MatchWorld.slotIndex ctx target.Side target.Player
                          team.Frame.PosX[slot] <- float32 target.Position.X
                          team.Frame.PosY[slot] <- float32 target.Position.Y

                      let area =
                          (RestartRules.validate ctx positioned plan).Violations
                          |> List.filter (fun violation -> violation.Rule = RestartRules.Rule.PenaltyArea)

                      Expect.isEmpty area (sprintf "y=%.2f: players on their targets satisfy the area law" y))

              testCase "a teammate standing on the free-kick spot is moved clear before the kick" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      world.Home.Frame.PosX[slot] <- -30.0f
                      world.Home.Frame.PosY[slot] <- -30.0f + float32 slot * 5.0f
                  world.Home.Frame.PosX[2] <- 0.0f
                  world.Home.Frame.PosY[2] <- 0.0f
                  world.Home.Frame.PosX[3] <- 0.3f
                  world.Home.Frame.PosY[3] <- 0.0f

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          (RestartId 1UL)
                          (RestartCause.Foul(None, None))
                          SetPieceKind.FreeKick
                          ClubSide.Home
                          0.0<meter>
                          0.0<meter>
                          0<subtick>
                      |> Result.defaultWith (fun error -> failtestf "free-kick planning failed: %A" error)

                  let teammate = ctx.HomeLineup.Profiles[3].Id
                  Expect.equal plan.Executor ctx.HomeLineup.Profiles[2].Id "the player on the ball takes the kick"
                  let target = plan.Targets |> Array.find (fun t -> t.Player = teammate)

                  Expect.isGreaterThanOrEqual
                      (MatchMath.dist plan.Spot.X plan.Spot.Y target.Position.X target.Position.Y + 1e-6)
                      (float ctx.Config.SetPiece.BallClearance)
                      "the teammate is planned beyond the reach of his body"

                  let blocking () =
                      (RestartRules.validate ctx world plan).Violations
                      |> List.exists (fun violation ->
                          violation.Rule = RestartRules.Rule.BallClearance && violation.Player = Some teammate)

                  Expect.isTrue (blocking ()) "the kick waits while the teammate's body can touch the ball"
                  world.Home.Frame.PosX[3] <- float32 target.Position.X
                  world.Home.Frame.PosY[3] <- float32 target.Position.Y
                  Expect.isFalse (blocking ()) "the kick is allowed once he stands on his target")

              testCase "a player sent off after planning cannot hold up the restart" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      world.Home.Frame.PosX[slot] <- -30.0f
                      world.Home.Frame.PosY[slot] <- -30.0f + float32 slot * 5.0f
                  world.Home.Frame.PosX[2] <- 0.0f
                  world.Home.Frame.PosY[2] <- 0.0f
                  world.Away.Frame.PosX[5] <- 0.4f
                  world.Away.Frame.PosY[5] <- 0.0f

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          (RestartId 1UL)
                          (RestartCause.Foul(None, None))
                          SetPieceKind.FreeKick
                          ClubSide.Home
                          0.0<meter>
                          0.0<meter>
                          0<subtick>
                      |> Result.defaultWith (fun error -> failtestf "free-kick planning failed: %A" error)

                  let offender = ctx.AwayLineup.Profiles[5].Id

                  let involved () =
                      (RestartRules.validate ctx world plan).Violations
                      |> List.exists (fun violation -> violation.Player = Some offender)

                  Expect.isTrue (involved ()) "standing on the ball he blocks the kick while he is on the pitch"

                  world.Away.Frame.Occupancy[5] <-
                      OccupancyKind.Sidelined
                          { Id = offender
                            Reason = PlayerOutReason.RedCard }

                  Expect.isFalse (involved ()) "once sent off he no longer takes part in the restart")

              testCase "a loose ball never sticks to the touchline during live play" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = MatchFactory.create ctx
                  let ctx' = stepContext ctx
                  let buffer = buffer ()
                  let mutable rng = Rng.ofSeed 1UL

                  for _ in 1..90000 do
                      let next, _, _ = Stepper.step Scheduler.systems ctx' rng world buffer
                      rng <- next

                      let boundaryRestartQueued =
                          buffer.Events
                          |> Seq.exists (fun event ->
                              match event.Data with
                              | EventData.SetPiece _
                              | EventData.SetPieceAt _ -> true
                              | _ -> false)

                      if world.State.Flow = MatchFlow.Live && not boundaryRestartQueued then
                          let ball = world.State.Ball

                          if
                              ball.Control = BallControl.Free
                              && abs (float ball.Position.Y) > float Pitch.HalfWidth - 0.5
                          then
                              Expect.isGreaterThan
                                  (MatchMath.speedOf ball.Position.Vx ball.Position.Vy)
                                  0.05
                                  "a loose ball near the touchline must keep moving") ]
