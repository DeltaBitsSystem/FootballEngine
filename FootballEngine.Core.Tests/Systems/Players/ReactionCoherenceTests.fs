namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ReactionCoherenceTests =
    let private ctx = TestBuilders.formationCtx ()

    // The stepped cases prove what a decision sees when it is made; how long the carrier then deliberates is its
    // own contract (DeliberationTests), so here the choice is taken on the tick the decision begins.
    let private system =
        { Ctx =
            { ctx with
                Config =
                    { ctx.Config with
                        Planner =
                            { ctx.Config.Planner with
                                DeliberationTime = 0<subtick> } } }
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Applied = ResizeArray()
          Flags = { Bits = 0UL } }

    let private proposal =
        { Choice = ContestDuel
          DecidedAt = -1<subtick>
          ExpiresAt = 100<subtick> }

    let private names =
        set
            [ "PlayerPerception"
              "PlayerIntent"
              "PlayerSteering"
              "PlayerDecision"
              "Actions" ]

    let private worldAt tick =
        let world = MatchFactory.create ctx

        world.State <-
            { world.State with
                Tick = tick * 1<subtick>
                Flow = MatchFlow.Live }

        world

    let private event tick data =
        { Tick = tick * 1<subtick>
          Data = data }

    let private unwrap =
        function
        | Ok value -> value
        | Error error -> failtestf "unexpected error: %A" error

    let tests =
        testList
            "ReactionCoherence"
            [ testCase "committed possession refreshes before actions at every phase" (fun _ ->
                  for phase in 0..7 do
                      let world = worldAt phase
                      let trace = ResizeArray<string>()
                      let mutable proposalAtActions = false
                      let mutable pressureAtActions = -1
                      let mutable cognitiveAtActions = -1.0f
                      let mutable laneAtActions = true
                      let mutable intentAtActions = IntentCode.None
                      let carrier = world.Home.RosterIds[5]
                      world.Home.Frame.PressCount[5] <- -123
                      world.Home.Frame.Cognitive[5] <- -1.0f
                      world.Home.Frame.PosX[5] <- 0.0f
                      world.Home.Frame.PosY[5] <- 0.0f
                      world.Home.Frame.PosX[6] <- 10.0f
                      world.Home.Frame.PosY[6] <- 0.0f
                      world.Home.Frame.LaneClear[6] <- true

                      for slot in MatchWorld.activeSlots world.Away.Frame do
                          world.Away.Frame.PosX[slot] <- 30.0f
                          world.Away.Frame.PosY[slot] <- 25.0f

                      world.Away.Frame.PosX[5] <- 1.5f
                      world.Away.Frame.PosY[5] <- 0.0f
                      world.Away.Frame.PosX[6] <- 2.0f
                      world.Away.Frame.PosY[6] <- 0.0f

                      let systems =
                          Scheduler.systems
                          |> List.filter (fun spec -> names.Contains spec.Name)
                          |> List.map (fun spec ->
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          trace.Add spec.Name

                                          if spec.Name = "Actions" then
                                              proposalAtActions <- world.Home.Proposals.ContainsKey 5
                                              pressureAtActions <- world.Home.Frame.PressCount[5]
                                              cognitiveAtActions <- world.Home.Frame.Cognitive[5]
                                              laneAtActions <- world.Home.Frame.LaneClear[6]
                                              intentAtActions <- world.Home.Frame.Intent.Kind[5]

                                          spec.Step ctx rng world buffer })

                      let buf = buffer ()
                      buf.Events.Add(event phase (EventData.Possession(ClubSide.Home, carrier)))
                      Stepper.step systems system (Rng.ofSeed 2UL) world buf |> ignore

                      Expect.sequenceEqual
                          trace
                          [ "PlayerPerception"
                            "PlayerIntent"
                            "PlayerDecision"
                            "PlayerSteering"
                            "Actions" ]
                          "each system executes once in original order"

                      Expect.isTrue proposalAtActions "scheduled decision produced a proposal before Actions"
                      Expect.equal pressureAtActions 2 "perception sees the two nearby opponents"
                      Expect.isGreaterThan cognitiveAtActions 0.0f "cognition refreshed before the decision"
                      Expect.isFalse laneAtActions "two blockers close the measured lane"
                      Expect.equal intentAtActions IntentCode.BallCarry "carrier intent is refreshed before Actions")
              testCase "complete scheduler refreshes a reachable committed carrier in every phase" (fun _ ->
                  for phase in 0..7 do
                      let world = worldAt phase
                      let actor = world.Home.RosterIds[5]
                      world.Home.Frame.PosX[5] <- 0.0f
                      world.Home.Frame.PosY[5] <- 0.0f
                      world.Home.Frame.Cognitive[5] <- -1.0f

                      for slot in MatchWorld.activeSlots world.Away.Frame do
                          world.Away.Frame.PosX[slot] <- 30.0f
                          world.Away.Frame.PosY[slot] <- 20.0f

                      world.State <-
                          { world.State with
                              Ball = TestBuilders.emptyBall }

                      let trace = ResizeArray<string>()
                      let mutable atActions = None

                      let systems =
                          Scheduler.systems
                          |> List.map (fun spec ->
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          if names.Contains spec.Name then
                                              trace.Add spec.Name

                                          if spec.Name = "Actions" then
                                              atActions <-
                                                  Some(
                                                      world.State.Ball.Control,
                                                      world.Home.Frame.Cognitive[5],
                                                      world.Home.Frame.Intent.Kind[5],
                                                      world.Home.Proposals.ContainsKey 5
                                                  )

                                          spec.Step ctx rng world buffer })

                      let buf = buffer ()
                      buf.Events.Add(event phase (EventData.Possession(ClubSide.Home, actor)))
                      Stepper.step systems system (Rng.ofSeed 2UL) world buf |> ignore

                      Expect.sequenceEqual
                          trace
                          [ "PlayerPerception"
                            "PlayerIntent"
                            "PlayerDecision"
                            "PlayerSteering"
                            "Actions" ]
                          "full scheduler preserves order and executes once"

                      match atActions with
                      | Some(control, cognitive, intent, proposed) ->
                          Expect.equal
                              control
                              (BallControl.Controlled(ClubSide.Home, actor))
                              "fixture remains a reachable carrier through earlier systems"

                          Expect.isGreaterThan cognitive 0.0f "perception refreshed actual carrier"
                          Expect.equal intent IntentCode.BallCarry "intent reflects committed possession"
                          Expect.isTrue proposed "scheduled decision is available at Actions boundary"
                      | None -> failtest "Actions did not execute")
              testCase "restart completion refreshes perception before Live decision" (fun _ ->
                  let world = worldAt 4
                  let actor = world.Home.RosterIds[5]

                  let restart =
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Completed

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          restart.Id
                          restart.Cause
                          restart.Kind
                          restart.Side
                          0.0<meter>
                          0.0<meter>
                          0<subtick>
                      |> unwrap

                  world.State <-
                      { world.State with
                          Flow =
                              MatchFlow.Restart
                                  { restart with
                                      Plan = Some plan
                                      CommandIssued = true
                                      ContactObserved = true }
                          Ball = TestBuilders.emptyBall }

                  world.Home.Frame.Cognitive[5] <- -1.0f
                  world.Home.Frame.PressCount[5] <- -1
                  world.Home.Frame.PosX[5] <- 0.0f
                  world.Home.Frame.PosY[5] <- 0.0f
                  world.Home.Frame.PosX[6] <- 10.0f
                  world.Home.Frame.PosY[6] <- 0.0f

                  for slot in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX[slot] <- 30.0f
                      world.Away.Frame.PosY[slot] <- 20.0f

                  world.Away.Frame.PosX[5] <- 1.5f
                  world.Away.Frame.PosY[5] <- 0.0f
                  world.Away.Frame.PosX[6] <- 2.0f
                  world.Away.Frame.PosY[6] <- 0.0f
                  let mutable perceived = false
                  let mutable cognitiveAtDecision = None
                  let mutable pressureAtDecision = None
                  let mutable laneAtDecision = None
                  let mutable choiceAtActions = false

                  let systems =
                      Scheduler.systems
                      |> List.map (fun spec ->
                          { spec with
                              Step =
                                  fun ctx rng world buffer ->
                                      if spec.Name = "PlayerPerception" then
                                          perceived <- true

                                      if spec.Name = "PlayerDecision" then
                                          cognitiveAtDecision <- Some world.Home.Frame.Cognitive[5]
                                          pressureAtDecision <- Some world.Home.Frame.PressCount[5]
                                          laneAtDecision <- Some world.Home.Frame.LaneClear[6]

                                      if spec.Name = "Actions" then
                                          choiceAtActions <- world.Home.Proposals.ContainsKey 5

                                      spec.Step ctx rng world buffer })

                  let buf = buffer ()
                  buf.Events.Add(event 4 (EventData.Possession(ClubSide.Home, actor)))
                  let rng, _, _ = Stepper.step systems system (Rng.ofSeed 1UL) world buf
                  Expect.equal world.State.Flow MatchFlow.Live "restart controller enters Live"
                  // Cognition reads the tick as it began, so the first Live decision comes one tick after the controller.
                  buf.Events.Add(event 5 (EventData.Possession(ClubSide.Home, actor)))
                  Stepper.step systems system rng world buf |> ignore
                  Expect.isTrue perceived "perception executes during movement-allowed restart"

                  Expect.isTrue
                      (cognitiveAtDecision |> Option.exists (fun cognitive -> cognitive > 0.0f))
                      "decision sees refreshed cognitive state"

                  Expect.equal pressureAtDecision (Some 2) "decision sees current pressure"
                  Expect.equal laneAtDecision (Some false) "decision sees current blocked lane"
                  Expect.isTrue choiceAtActions "fresh choice reaches Actions")
              testCase "real restart contact wakes perception on completed drain in every phase" (fun _ ->
                  for phase in 0..7 do
                      let completedTick = 8 + phase
                      let world = worldAt (completedTick - 1)
                      let actor = world.Home.RosterIds[5]

                      let restart =
                          TestBuilders.restartState
                              SetPieceKind.KickOff
                              ClubSide.Home
                              (Vec2.create 0.0<meter> 0.0<meter>)
                              RestartPhase.AwaitingContact

                      let plan =
                          RestartPlanner.plan
                              ctx
                              world
                              restart.Id
                              restart.Cause
                              restart.Kind
                              restart.Side
                              0.0<meter>
                              0.0<meter>
                              0<subtick>
                          |> unwrap

                      world.Home.Frame.PosX[5] <- 0.0f
                      world.Home.Frame.PosY[5] <- 0.0f

                      let point: Vec3 =
                          { X = 0.0<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }

                      let motion =
                          { Actor = plan.Executor
                            Foot = ContactFoot.Right
                            Kind = FootTouchKind.Receive
                            Surface = StrikeSurface.Inside
                            StartedAt = 0<subtick>
                            ContactAt = 0<subtick>
                            EndsAt = 100<subtick>
                            Point = point
                            BallCenter = point }

                      world.State <-
                          { world.State with
                              Flow =
                                  MatchFlow.Restart
                                      { restart with
                                          Plan = Some plan
                                          CommandIssued = true }
                              Ball =
                                  { TestBuilders.emptyBall with
                                      FootMotion = Some motion } }

                      let mutable perceptionTicks = []
                      let mutable decisionTicks = []
                      let mutable preserved = true

                      let systems =
                          Scheduler.systems
                          |> List.map (fun spec ->
                              if spec.Name = "PlayerPerception" then
                                  { spec with
                                      Step =
                                          fun ctx rng world buffer ->
                                              perceptionTicks <- int world.State.Tick :: perceptionTicks
                                              let flow, foot = world.State.Flow, world.State.Ball.FootMotion
                                              let intent = Array.copy world.Home.Frame.Intent.Kind
                                              let targetX = Array.copy world.Home.Frame.Intent.TargetX
                                              let targetY = Array.copy world.Home.Frame.Intent.TargetY
                                              let next = spec.Step ctx rng world buffer

                                              preserved <-
                                                  preserved
                                                  && flow = world.State.Flow
                                                  && foot = world.State.Ball.FootMotion
                                                  && intent = world.Home.Frame.Intent.Kind
                                                  && targetX = world.Home.Frame.Intent.TargetX
                                                  && targetY = world.Home.Frame.Intent.TargetY

                                              next }
                              elif spec.Name = "PlayerDecision" then
                                  { spec with
                                      Step =
                                          fun ctx rng world buffer ->
                                              decisionTicks <- int world.State.Tick :: decisionTicks
                                              spec.Step ctx rng world buffer }
                              else
                                  spec)

                      let buf = buffer ()

                      buf.Events.Add(
                          event (completedTick - 1) (EventData.RestartContact(plan.Id, plan.Executor, plan.Kind))
                      )

                      let rng, _, applied = Stepper.step systems system (Rng.ofSeed 1UL) world buf

                      Expect.isTrue
                          (applied
                           |> Seq.exists (fun ev ->
                               ev.Data = EventData.RestartContact(plan.Id, plan.Executor, plan.Kind)))
                          "contact was drained"

                      match world.State.Flow with
                      | MatchFlow.Restart state ->
                          Expect.equal state.Phase RestartPhase.Completed "contact marks restart completed"
                      | other -> failtestf "expected completed restart, got %A" other

                      Expect.isEmpty decisionTicks "decision cannot run during AwaitingContact"

                      Expect.isTrue
                          (buf.Events
                           |> Seq.exists (fun ev -> ev.Data = EventData.RestartCompleted plan.Id))
                          "controller queued completion"

                      world.Home.Frame.Cognitive[5] <- -1.0f
                      buf.Events.Add(event completedTick (EventData.Possession(ClubSide.Home, actor)))
                      let rng, _, completed = Stepper.step systems system rng world buf

                      Expect.isTrue
                          (completed |> Seq.exists (fun ev -> ev.Data = EventData.RestartCompleted plan.Id))
                          "completion was drained next step"

                      Expect.equal world.State.Flow MatchFlow.Live "controller advances to Live after perception"

                      Expect.equal
                          (perceptionTicks |> List.filter ((=) completedTick) |> List.length)
                          1
                          "completion and cadence fire perception once"

                      Expect.isFalse
                          (decisionTicks |> List.contains completedTick)
                          "decision read the tick while it was still a restart"

                      buf.Events.Add(event (completedTick + 1) (EventData.Possession(ClubSide.Home, actor)))
                      Stepper.step systems system rng world buf |> ignore

                      Expect.equal
                          (decisionTicks |> List.filter ((=) (completedTick + 1)) |> List.length)
                          1
                          "decision runs on the first tick that begins Live"

                      Expect.isGreaterThan world.Home.Frame.Cognitive[5] 0.0f "carrier cognition refreshed"
                      Expect.isTrue preserved "perception does not overwrite restart, motion or intent")
              testCase "restart completion alone wakes perception without possession" (fun _ ->
                  let world = worldAt 3

                  let restart =
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Completed

                  let plan =
                      RestartPlanner.plan
                          ctx
                          world
                          restart.Id
                          restart.Cause
                          restart.Kind
                          restart.Side
                          0.0<meter>
                          0.0<meter>
                          0<subtick>
                      |> unwrap

                  world.State <-
                      { world.State with
                          Flow =
                              MatchFlow.Restart
                                  { restart with
                                      Plan = Some plan
                                      CommandIssued = true
                                      ContactObserved = true }
                          Ball = TestBuilders.emptyBall }

                  let mutable calls = 0

                  let systems =
                      Scheduler.systems
                      |> List.map (fun spec ->
                          if spec.Name <> "PlayerPerception" then
                              spec
                          else
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          calls <- calls + 1
                                          spec.Step ctx rng world buffer })

                  let buf = buffer ()
                  buf.Events.Add(event 2 (EventData.RestartCompleted plan.Id))
                  Stepper.step systems system (Rng.ofSeed 1UL) world buf |> ignore
                  Expect.equal calls 1 "completion flag alone wakes off-cadence perception"
                  Expect.equal world.State.Flow MatchFlow.Live "controller enters live")
              testCase "perception respects restart cadence and excludes pauses" (fun _ ->
                  let restart =
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Positioning

                  let perception =
                      Scheduler.systems |> List.find (fun spec -> spec.Name = "PlayerPerception")

                  let run tick flow withEvent =
                      let world = worldAt tick
                      world.State <- { world.State with Flow = flow }
                      let mutable count = 0

                      let wrapped =
                          { perception with
                              Step =
                                  fun ctx rng world buffer ->
                                      count <- count + 1
                                      perception.Step ctx rng world buffer }

                      let buf = buffer ()

                      if withEvent then
                          buf.Events.Add(event tick (EventData.TeamInstruction ClubSide.Home))

                      Stepper.step [ wrapped ] system (Rng.ofSeed 1UL) world buf |> ignore
                      count

                  Expect.equal
                      (run 1 (MatchFlow.Restart restart) false)
                      0
                      "restart without event retains 8-tick cadence"

                  Expect.equal (run 8 (MatchFlow.Restart restart) true) 1 "event and cadence execute once"

                  Expect.equal
                      (run 8 (MatchFlow.GoalPause(3<tickDelta>, ClubSide.Home, None)) true)
                      0
                      "goal pause excludes perception"

                  Expect.equal (run 8 (MatchFlow.HalfTime 3<tickDelta>) true) 0 "halftime excludes perception")
              testCase "steering proposals feed the sole physical integration in each subtick" (fun _ ->
                  let world = worldAt 1
                  world.Home.Frame.PosX[5] <- 0.0f
                  world.Home.Frame.PosY[5] <- 0.0f
                  world.Home.Frame.VelX[5] <- 0.0f
                  world.Home.Frame.VelY[5] <- 0.0f

                  world.State <-
                      { world.State with
                          Ball = TestBuilders.emptyBall }

                  let trace = ResizeArray<string * int>()

                  let systems =
                      Scheduler.systems
                      |> List.map (fun spec ->
                          if spec.Name <> "ContactDynamics" && spec.Name <> "PlayerSteering" then
                              spec
                          else
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          trace.Add(spec.Name, int world.State.Tick)
                                          spec.Step ctx rng world buffer })

                  let buf = buffer ()
                  buf.Events.Add(event 1 (EventData.Possession(ClubSide.Home, world.Home.RosterIds[5])))
                  let rng, _, _ = Stepper.step systems system (Rng.ofSeed 2UL) world buf
                  let _, _, _ = Stepper.step systems system rng world buf

                  Expect.sequenceEqual
                      trace
                      [ ("PlayerSteering", 1)
                        ("ContactDynamics", 1)
                        ("PlayerSteering", 2)
                        ("ContactDynamics", 2) ]
                      "steering supplies objectives before one integration per subtick")
              testCase "no event outside cadence does not wake added systems" (fun _ ->
                  let world = worldAt 1
                  let trace = ResizeArray<string>()

                  let systems =
                      Scheduler.systems
                      |> List.filter (fun spec -> names.Contains spec.Name)
                      |> List.map (fun spec ->
                          { spec with
                              Step =
                                  fun ctx rng world buffer ->
                                      trace.Add spec.Name
                                      spec.Step ctx rng world buffer })

                  Stepper.step systems system (Rng.ofSeed 2UL) world (buffer ()) |> ignore
                  Expect.isFalse (trace.Contains "PlayerPerception") "no perception wake"
                  Expect.isFalse (trace.Contains "PlayerDecision") "no decision wake"
                  Expect.isFalse (trace.Contains "PlayerSteering") "no steering wake")
              testCase "new system events wait for the next drain before waking perception" (fun _ ->
                  let world = worldAt 1

                  world.State <-
                      { world.State with
                          Ball = TestBuilders.emptyBall }

                  let actor = world.Home.RosterIds[5]
                  let mutable perceptionTicks = []

                  let systems =
                      Scheduler.systems
                      |> List.map (fun spec ->
                          if spec.Name <> "PlayerPerception" then
                              spec
                          else
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          perceptionTicks <- int world.State.Tick :: perceptionTicks
                                          spec.Step ctx rng world buffer })

                  let producer: SystemSpec =
                      { Name = "FixtureProducer"
                        Side = SystemSide.Body
                        Triggers = [ Trigger.EveryTick ]
                        Step =
                          fun _ rng world buffer ->
                              if world.State.Tick = 1<subtick> then
                                  buffer.Events.Add(event 1 (EventData.Possession(ClubSide.Home, actor)))

                              rng }

                  let buf = buffer ()

                  let rng, _, applied =
                      Stepper.step (systems @ [ producer ]) system (Rng.ofSeed 1UL) world buf

                  Expect.isFalse (perceptionTicks |> List.contains 1) "uncommitted buffer event cannot wake perception"

                  Expect.isFalse
                      (applied
                       |> Seq.exists (fun ev -> ev.Data = EventData.Possession(ClubSide.Home, actor)))
                      "produced event not yet applied"

                  let _, _, nextApplied = Stepper.step (systems @ [ producer ]) system rng world buf

                  Expect.isTrue
                      (nextApplied
                       |> Seq.exists (fun ev -> ev.Data = EventData.Possession(ClubSide.Home, actor)))
                      "event applied in next drain"

                  Expect.isTrue (perceptionTicks |> List.contains 2) "perception wakes at that drain")
              testCase "owner transitions invalidate both teams while duplicate preserves proposal" (fun _ ->
                  let world = worldAt 1

                  let home, other, away =
                      world.Home.RosterIds[5], world.Home.RosterIds[6], world.Away.RosterIds[5]

                  let apply tick data =
                      EventApply.apply ctx world (event tick data)

                  apply 1 (EventData.Possession(ClubSide.Home, home))
                  world.Home.PendingActions[5] <- proposal
                  world.Away.PendingActions[5] <- proposal
                  apply 2 (EventData.Possession(ClubSide.Home, home))
                  Expect.isTrue (world.Home.PendingActions.ContainsKey 5) "duplicate preserves actual owner's proposal"
                  apply 3 (EventData.Possession(ClubSide.Home, other))

                  Expect.equal
                      (world.Home.PendingActions.Count, world.Away.PendingActions.Count)
                      (0, 0)
                      "same team new owner clears both"

                  world.Home.PendingActions[5] <- proposal
                  apply 4 (EventData.Possession(ClubSide.Away, away))
                  apply 5 (EventData.Possession(ClubSide.Home, home))
                  Expect.equal world.Home.PendingActions.Count 0 "returning carrier cannot reuse old choice"
                  world.Home.PendingActions[5] <- proposal
                  world.Away.PendingActions[5] <- proposal
                  apply 6 (EventData.BallLoose(0.0<meter>, 0.0<meter>))

                  Expect.equal
                      (world.Home.PendingActions.Count, world.Away.PendingActions.Count)
                      (0, 0)
                      "loose clears both"

                  world.Home.PendingActions[5] <- proposal
                  apply 7 (EventData.Possession(ClubSide.Home, home))
                  Expect.equal world.Home.PendingActions.Count 0 "recovery from loose clears stale choices")
              testCase "same-side carrier change chooses for the new actor before Actions" (fun _ ->
                  let world = worldAt 1
                  let oldActor, newActor = world.Home.RosterIds[5], world.Home.RosterIds[6]
                  world.Home.Frame.PosX[6] <- 0.0f
                  world.Home.Frame.PosY[6] <- 0.0f

                  world.State <-
                      { world.State with
                          Ball =
                              { TestBuilders.emptyBall with
                                  Control = BallControl.Controlled(ClubSide.Home, oldActor) } }

                  world.Home.PendingActions[5] <- proposal
                  let mutable boundary = None

                  let systems =
                      Scheduler.systems
                      |> List.map (fun spec ->
                          if spec.Name <> "Actions" then
                              spec
                          else
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          boundary <-
                                              Some(
                                                  world.Home.PendingActions.ContainsKey 5
                                                  || world.Home.Proposals.ContainsKey 5,
                                                  world.Home.Proposals.ContainsKey 6,
                                                  world.State.Ball.Control
                                              )

                                          spec.Step ctx rng world buffer })

                  let buf = buffer ()
                  buf.Events.Add(event 1 (EventData.Possession(ClubSide.Home, newActor)))
                  Stepper.step systems system (Rng.ofSeed 4UL) world buf |> ignore

                  Expect.equal
                      boundary
                      (Some(false, true, BallControl.Controlled(ClubSide.Home, newActor)))
                      "stale old choice removed and new actor chosen before execution")
              testCase "two possessions in one drain retain order and leave final owner" (fun _ ->
                  let world = worldAt 1
                  let buf = buffer ()
                  world.Home.PendingActions[5] <- proposal
                  buf.Events.Add(event 1 (EventData.Possession(ClubSide.Home, world.Home.RosterIds[5])))
                  buf.Events.Add(event 1 (EventData.Possession(ClubSide.Away, world.Away.RosterIds[5])))
                  let _, _, applied = Stepper.step [] system (Rng.ofSeed 1UL) world buf

                  Expect.sequenceEqual
                      (applied |> Seq.map _.Data)
                      [ EventData.Possession(ClubSide.Home, world.Home.RosterIds[5])
                        EventData.Possession(ClubSide.Away, world.Away.RosterIds[5]) ]
                      "drain preserves input order"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[5]))
                      "second owner wins"

                  Expect.equal world.Home.PendingActions.Count 0 "old proposal removed")
              testCase "restart clears proposals without touching unrelated team state" (fun _ ->
                  let world = worldAt 1
                  world.Home.PendingActions[5] <- proposal
                  world.Away.PendingActions[5] <- proposal
                  EventApply.apply ctx world (event 1 (EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Home)))

                  Expect.equal
                      (world.Home.PendingActions.Count, world.Away.PendingActions.Count)
                      (0, 0)
                      "new restart invalidates choices")
              testCase "pending strike suppresses decision and preserves motion and RNG" (fun _ ->
                  let world = worldAt 1
                  let pid = world.Home.RosterIds[5]
                  world.Home.Frame.PosX[5] <- 0.0f
                  world.Home.Frame.PosY[5] <- 0.0f

                  world.State <-
                      { world.State with
                          Ball =
                              { TestBuilders.emptyBall with
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  let impulse =
                      BallPhysics.createImpulse
                          pid
                          { X = 0.0<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }
                          { X = 15.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          (Some { X = 20.0<meter>; Y = 0.0<meter> })
                          StrikeKind.Pass
                          (SimTick.ofSubTicks world.State.Tick)
                          ExecutionContext.OpenPlay

                  let buf = buffer ()
                  BallFootContact.request system world buf impulse TrajectoryIntent.Uncontrolled None
                  let pending, motion = world.State.Ball.PendingStrike, world.State.Ball.FootMotion
                  Expect.isSome pending "real strike is pending"
                  let rng = Rng.ofSeed 3UL
                  let after = PlayerDecision.step system rng world buf
                  Expect.equal after rng "no decision random draw"
                  Expect.equal world.Home.PendingActions.Count 0 "no new proposal"
                  Expect.equal world.State.Ball.PendingStrike pending "strike remains committed"
                  Expect.equal world.State.Ball.FootMotion motion "motion remains intact"
                  let mutable decisionSeen = false
                  let mutable decisionRngUnchanged = false

                  let schedule =
                      Scheduler.systems
                      |> List.map (fun spec ->
                          if spec.Name <> "PlayerDecision" then
                              spec
                          else
                              { spec with
                                  Step =
                                      fun ctx rng world buffer ->
                                          decisionSeen <- true
                                          let next = spec.Step ctx rng world buffer
                                          decisionRngUnchanged <- next = rng
                                          next })

                  buf.Events.Add(event 1 (EventData.TeamInstruction ClubSide.Home))
                  Stepper.step schedule system rng world buf |> ignore
                  Expect.isTrue decisionSeen "event wakes scheduled decision during windup"
                  Expect.isTrue decisionRngUnchanged "scheduled decision consumes no RNG"

                  Expect.equal
                      world.State.Ball.PendingStrike
                      pending
                      "normal scheduler preserves pending physical strike"

                  Expect.equal world.State.Ball.FootMotion motion "normal scheduler preserves its motion"
                  Expect.equal world.Home.PendingActions.Count 0 "no decision created during windup")
              testCase "red and injury release the removed player and exclude inactive pass targets" (fun _ ->
                  for injury in [ false; true ] do
                      let world = worldAt 1
                      let removed = world.Home.RosterIds[6]
                      world.Home.PendingActions[6] <- proposal
                      world.Home.PendingActions[5] <- proposal

                      let data =
                          if injury then
                              EventData.Injury(removed, 1)
                          else
                              EventData.RedCardIssued removed

                      EventApply.apply ctx world (event 1 data)

                      Expect.isFalse
                          (world.Home.PendingActions.ContainsKey 6)
                          "releaseSlot removes outgoing player's choice"

                      Expect.isTrue
                          (world.Home.PendingActions.ContainsKey 5)
                          "unrelated choice is not globally cleared"

                      Expect.isFalse (MatchWorld.isActive world.Home.Frame 6) "removed player is inactive"
                      let targets = PlayerDecision.passOptionsFor ctx world ClubSide.Home 5

                      Expect.isFalse
                          (targets |> List.exists (fun (_, _, target) -> int target = 6))
                          "inactive player is not a pass target")
              testCase "red card and injury wake fresh off-cadence decisions" (fun _ ->
                  for injury in [ false; true ] do
                      let world = worldAt 1
                      let actor, removed = world.Home.RosterIds[5], world.Home.RosterIds[6]

                      world.State <-
                          { world.State with
                              Ball =
                                  { TestBuilders.emptyBall with
                                      Control = BallControl.Controlled(ClubSide.Home, actor) } }

                      world.Home.PendingActions[6] <- proposal
                      let mutable decision = false

                      let systems =
                          Scheduler.systems
                          |> List.map (fun spec ->
                              if spec.Name <> "PlayerDecision" then
                                  spec
                              else
                                  { spec with
                                      Step =
                                          fun ctx rng world buffer ->
                                              decision <- true
                                              spec.Step ctx rng world buffer })

                      let buf = buffer ()

                      let data =
                          if injury then
                              EventData.Injury(removed, 1)
                          else
                              EventData.RedCardIssued removed

                      buf.Events.Add(event 1 data)
                      Stepper.step systems system (Rng.ofSeed 3UL) world buf |> ignore
                      Expect.isTrue decision "participant event wakes decision"

                      Expect.isFalse
                          (world.Home.PendingActions.ContainsKey 6 || world.Home.Proposals.ContainsKey 6)
                          "removed player's choice is released"

                      match world.Home.Proposals.TryGetValue 5 with
                      | true, { Choice = DeliverPass target } ->
                          Expect.notEqual target 6 "new choice never targets removed player"
                      | true, _ -> ()
                      | _ -> failtest "active carrier did not produce a fresh off-cadence choice")
              testCase "substitution releases both slots and changes active identity" (fun _ ->
                  let lineup = TestBuilders.makeLineup 0 16

                  let ctx16 =
                      { ctx with
                          HomeLineup = lineup
                          HomeParticipants = MatchLineupBoundary.fromLineup lineup }

                  let world = MatchFactory.create ctx16
                  let outgoing, incoming = world.Home.RosterIds[5], world.Home.RosterIds[11]
                  world.Home.PendingActions[5] <- proposal
                  world.Home.PendingActions[11] <- proposal
                  EventApply.apply ctx16 world (event 1 (EventData.SubstitutionApplied(outgoing, incoming)))
                  Expect.isFalse (world.Home.PendingActions.ContainsKey 5) "outgoing choice released"
                  Expect.isFalse (world.Home.PendingActions.ContainsKey 11) "incoming slot starts clean"
                  Expect.isFalse (MatchWorld.isActive world.Home.Frame 5) "outgoing identity is inactive"
                  Expect.isTrue (MatchWorld.isActive world.Home.Frame 11) "incoming identity is active"
                  Expect.equal world.Home.RosterIds[11] incoming "live slot retains incoming identity"
                  let options = PlayerDecision.passOptionsFor ctx16 world ClubSide.Home 6

                  Expect.isFalse
                      (options |> List.exists (fun (_, _, target) -> int target = 5))
                      "outgoing player cannot be targeted")
              testCase "future instruction only clears when applied and wakes services" (fun _ ->
                  let mutable trace = []

                  let systems =
                      Scheduler.systems
                      |> List.filter (fun spec -> spec.Name = "PlayerPerception" || spec.Name = "PlayerDecision")
                      |> List.map (fun spec ->
                          { spec with
                              Step =
                                  fun ctx rng world buffer ->
                                      trace <- (int world.State.Tick, spec.Name) :: trace
                                      spec.Step ctx rng world buffer })

                  let input =
                      { MatchId = 8UL
                        Seed = 8UL
                        Context = ctx
                        EffectiveConfig = EffectiveConfig.defaultConfig
                        EngineVersion = "reaction-test" }

                  let session = MatchSession.create systems input MatchRunMode.Fast |> unwrap

                  session.World.State <-
                      { session.World.State with
                          Flow = MatchFlow.Live
                          Ball = TestBuilders.emptyBall }

                  session.World.Home.PendingActions[5] <- proposal
                  session.World.Away.PendingActions[5] <- proposal

                  let command =
                      { EffectiveTick = 3<subtick>
                        Sequence = 0UL
                        Revision = input.EffectiveConfig.Revision
                        Manager = 1UL
                        Action = ChangeTactics session.World.Home.Tactics }

                  MatchSession.receive command session |> unwrap
                  Expect.isTrue (session.World.Home.PendingActions.ContainsKey 5) "enqueue does not invalidate"
                  MatchSession.advanceTo 3<subtick> session |> unwrap |> ignore
                  Expect.isTrue (session.World.Home.PendingActions.ContainsKey 5) "future instruction has not applied"
                  MatchSession.advanceTo 4<subtick> session |> unwrap |> ignore

                  Expect.isFalse
                      (session.World.Home.PendingActions.ContainsKey 5)
                      "effective instruction invalidates its side"

                  Expect.isTrue (session.World.Away.PendingActions.ContainsKey 5) "other side is not cleared"

                  Expect.isTrue
                      (trace |> List.contains (3, "PlayerPerception"))
                      "effective instruction wakes perception"

                  Expect.isTrue (trace |> List.contains (3, "PlayerDecision")) "effective instruction wakes decision")
              testCase "live checkpoint with outstanding strike or proposal restores sporting frontier" (fun _ ->
                  let input =
                      { MatchId = 8UL
                        Seed = 8UL
                        Context = ctx
                        EffectiveConfig = EffectiveConfig.defaultConfig
                        EngineVersion = "reaction-checkpoint" }

                  let live =
                      MatchSession.create Scheduler.systems input (MatchRunMode.Live LiveExecutionPolicy.defaultPolicy)
                      |> unwrap

                  let mutable found = false

                  while int live.World.State.Tick < 5000 && not found do
                      MatchSession.advanceTo (live.World.State.Tick + 1<subtick>) live
                      |> unwrap
                      |> ignore

                      found <-
                          live.World.State.Ball.PendingStrike.IsSome
                          || live.World.Home.PendingActions.Count > 0
                          || live.World.Away.PendingActions.Count > 0

                  Expect.isTrue found "natural session reaches a physical strike or proposal frontier"

                  let restored =
                      MatchSession.restore Scheduler.systems input (MatchSession.checkpoint live)
                      |> unwrap

                  let pending (team: TeamWorld) =
                      team.PendingActions
                      |> Seq.map (fun entry -> entry.Key, entry.Value)
                      |> Seq.sortBy fst
                      |> Seq.toList

                  Expect.equal restored.World.State live.World.State "checkpoint restores sporting state"
                  Expect.equal restored.Rng live.Rng "checkpoint restores RNG"

                  Expect.sequenceEqual
                      restored.World.Home.Frame.PosX
                      live.World.Home.Frame.PosX
                      "checkpoint restores home positions"

                  Expect.sequenceEqual
                      restored.World.Away.Frame.PosY
                      live.World.Away.Frame.PosY
                      "checkpoint restores away positions"

                  Expect.equal
                      (pending restored.World.Home, pending restored.World.Away)
                      (pending live.World.Home, pending live.World.Away)
                      "checkpoint reproduces proposals"

                  let target = live.World.State.Tick + 50<subtick>
                  MatchSession.advanceTo target live |> unwrap |> ignore

                  while restored.World.State.Tick < target do
                      MatchSession.advanceTo (restored.World.State.Tick + 1<subtick>) restored
                      |> unwrap
                      |> ignore

                  Expect.equal restored.World.State live.World.State "chunking preserves state"
                  Expect.equal restored.Rng live.Rng "chunking preserves RNG"

                  Expect.sequenceEqual
                      restored.World.Home.Frame.PosX
                      live.World.Home.Frame.PosX
                      "chunking preserves home positions"

                  Expect.sequenceEqual
                      restored.World.Away.Frame.PosY
                      live.World.Away.Frame.PosY
                      "chunking preserves away positions"

                  Expect.equal (List.ofSeq restored.Events) (List.ofSeq live.Events) "chunking preserves events"

                  Expect.equal
                      (pending restored.World.Home, pending restored.World.Away)
                      (pending live.World.Home, pending live.World.Away)
                      "chunking preserves proposals")
              testCase "live replay and capture settings preserve sporting outcomes" (fun _ ->
                  let input =
                      { MatchId = 9UL
                        Seed = 9UL
                        Context = ctx
                        EffectiveConfig = EffectiveConfig.defaultConfig
                        EngineVersion = "reaction-modes" }

                  let live =
                      MatchSession.create Scheduler.systems input (MatchRunMode.Live LiveExecutionPolicy.defaultPolicy)
                      |> unwrap

                  let replay =
                      MatchSession.create Scheduler.systems input (MatchRunMode.Archive 5<tickDelta>)
                      |> unwrap

                  replay.World.Observation.Enabled <- false

                  let command =
                      { EffectiveTick = 10<subtick>
                        Sequence = 0UL
                        Revision = input.EffectiveConfig.Revision
                        Manager = 1UL
                        Action = ChangeTactics live.World.Home.Tactics }

                  MatchSession.receive command live |> unwrap
                  MatchSession.receive command replay |> unwrap
                  MatchSession.advanceTo 1000<subtick> live |> unwrap |> ignore

                  while replay.World.State.Tick < 1000<subtick> do
                      MatchSession.advanceTo (min 1000<subtick> (replay.World.State.Tick + 37<subtick>)) replay
                      |> unwrap
                      |> ignore

                  let pending (team: TeamWorld) =
                      team.PendingActions
                      |> Seq.map (fun entry -> entry.Key, entry.Value)
                      |> Seq.sortBy fst
                      |> Seq.toList

                  Expect.equal
                      replay.World.State
                      live.World.State
                      "capture and presentation mode do not change sporting state"

                  Expect.equal replay.Rng live.Rng "capture and chunks do not change RNG"
                  Expect.sequenceEqual replay.World.Home.Frame.PosX live.World.Home.Frame.PosX "home geometry agrees"
                  Expect.sequenceEqual replay.World.Away.Frame.PosY live.World.Away.Frame.PosY "away geometry agrees"

                  Expect.equal
                      (List.ofSeq replay.Events)
                      (List.ofSeq live.Events)
                      "committed event stream is identical"

                  Expect.equal
                      (pending replay.World.Home, pending replay.World.Away)
                      (pending live.World.Home, pending live.World.Away)
                      "proposals agree")
              testCase "launched pass wakes perception and steering but not off-cadence decision" (fun _ ->
                  let world = worldAt 1
                  let trace = ResizeArray<string>()

                  let systems =
                      Scheduler.systems
                      |> List.filter (fun spec -> names.Contains spec.Name)
                      |> List.map (fun spec ->
                          { spec with
                              Step =
                                  fun ctx rng world buffer ->
                                      trace.Add spec.Name
                                      spec.Step ctx rng world buffer })

                  let buf = buffer ()
                  let passer = world.Home.RosterIds[5]

                  buf.Events.Add(
                      event
                          1
                          (EventData.Pass(
                              passer,
                              None,
                              10.0<meter>,
                              1.0,
                              false,
                              0.0<meter>,
                              0.0<meter>,
                              10.0<meter>,
                              0.0<meter>
                          ))
                  )

                  Stepper.step systems system (Rng.ofSeed 1UL) world buf |> ignore
                  Expect.isTrue (trace.Contains "PlayerPerception") "perception observes launch"
                  Expect.isTrue (trace.Contains "PlayerSteering") "steering reacts"
                  Expect.isFalse (trace.Contains "PlayerDecision") "do not plan attack for a launch") ]
