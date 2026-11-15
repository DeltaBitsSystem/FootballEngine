namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ContactTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private place (world: World) (side: ClubSide) (slot: int) (x: float<meter>) (y: float<meter>) : unit =
        let team = MatchWorld.team world side
        team.Frame.PosX.[slot] <- float32 x
        team.Frame.PosY.[slot] <- float32 y

    let private placeAllFar (world: World) : unit =
        for s in [ ClubSide.Home; ClubSide.Away ] do
            for i in MatchWorld.activeSlots (MatchWorld.team world s).Frame do
                let x =
                    if s = ClubSide.Home then
                        -40.0 + float i
                    else
                        40.0 - float i

                let y = -20.0 + float i * 4.0
                place world s i (x * 1.0<meter>) (y * 1.0<meter>)

    let private setBall (world: World) (x: float<meter>) (y: float<meter>) : unit =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = x
                                Y = y } } }

    let private customProfile
        (slot: int)
        (pace: float32)
        (anticipation: float32)
        (positioning: float32)
        (c: MatchContext)
        : MatchContext =
        c.HomeLineup.Profiles.[slot] <-
            { c.HomeLineup.Profiles.[slot] with
                Pace = pace
                Anticipation = anticipation
                Positioning = positioning }

        c

    let private customAwayProfile
        (slot: int)
        (pace: float32)
        (anticipation: float32)
        (positioning: float32)
        (c: MatchContext)
        : MatchContext =
        c.AwayLineup.Profiles.[slot] <-
            { c.AwayLineup.Profiles.[slot] with
                Pace = pace
                Anticipation = anticipation
                Positioning = positioning }

        c

    let private hasEvent (buf: EventBuffer) (filter: EventData -> bool) : bool =
        buf.Events |> Seq.exists (fun ev -> filter ev.Data)

    let private applyEvents (c: MatchContext) (world: World) (buf: EventBuffer) : unit =
        for ev in buf.Events do
            EventApply.apply c world ev

    let private prepareTrap (world: World) =
        let action =
            ContactActionPlanner.prepare sctx world ClubSide.Home 5 ContactActionKind.ContactTrap None
            |> Option.defaultWith (fun () -> failtest "receiver fixture must be eligible to prepare")

        // These solver fixtures start at commitment with an exact effector; preparation timing is tested separately.
        world.State <-
            { world.State with
                Tick = action.CommitAt }

        world.Home.Frame.ContactActions[5] <-
            Some
                { action with
                    Phase = ContactActionPhase.ContactActionCommitted }

    let contactTests =
        testList
            "FootballEngine.Core.Systems.Ball.ContactTests"
            [ testCase "arrival gate alone cannot complete a pass" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id

                  let impulse =
                      BallPhysics.createImpulse
                          passer
                          { X = 5.0<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }
                          { X = 0.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          None
                          StrikeKind.Pass
                          (SimTick.ofSubTicks 0<subtick>)
                          ExecutionContext.OpenPlay

                  BallPhysics.applyImpulse
                      world
                      impulse
                      (TrajectoryIntent.Aimed(passer, receiver, 1.0))
                      (Some 0<subtick>)

                  place world ClubSide.Home 5 5.0<meter> 0.0<meter>
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  let completed =
                      buf.Events
                      |> Seq.find (fun event ->
                          match event.Data with
                          | EventData.Pass _ -> true
                          | _ -> false)

                  Expect.equal
                      (world.Observation.ConsumeCause completed)
                      (ValueSome(ActionCause impulse.ActionId))
                      "the missed flight remains linked to the strike"

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Pass(_, Some r, _, _, false, _, _, _, _) -> r = receiver
                          | _ -> false)
                      "arrival without a physical touch is incomplete"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Contesting ClubSide.Home)
                      "the missed pass remains loose")

              testCase "nearby defender cannot intercept by proximity" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id
                  place world ClubSide.Home 5 20.0<meter> 0.0<meter>
                  place world ClubSide.Away 3 5.0<meter> 0.0<meter>
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 0.6)) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Interception _ -> true
                          | _ -> false))
                      "only a physical contact can intercept"

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Possession _ -> true
                          | _ -> false))
                      "arrival does not award possession"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Contesting ClubSide.Home)
                      "the ball stays unresolved")

              testCase "stationary aimed ball with no contact becomes loose" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id
                  setBall world 48.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Airborne
                                  StationarySince = Some 0<subtick>
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  Expect.isTrue
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.BallLoose _ -> true
                          | _ -> false))
                      "a missed stationary arrival is visible as a loose ball"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Contesting ClubSide.Home)
                      "the ball awaits a real touch"

                  Expect.isNone world.State.Ball.Trajectory "the expired arrival no longer owns the ball")

              testCase "moving flight cannot expire solely because its arrival estimate elapsed" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  let passer = ctx.HomeLineup.Profiles[4].Id
                  let receiver = ctx.HomeLineup.Profiles[5].Id

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Airborne
                                  Position =
                                      { world.State.Ball.Position with
                                          Vx = 4.0<meterPerSecond> }
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let original = world.State.Ball
                  let events = buffer ()
                  let rng = Rng.ofSeed 31UL

                  Expect.equal
                      (BallContact.step sctx rng world events)
                      rng
                      "an estimate cannot consume execution randomness"

                  Expect.equal world.State.Ball original "drag or receiver movement can delay a real arrival"
                  Expect.isEmpty events.Events "no failed pass or loose-ball event without a physical cause")

              testCase "No arrival gate means nothing happens" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = None
                                  Trajectory =
                                      Some(
                                          TrajectoryIntent.Aimed(
                                              ctx.HomeLineup.Profiles.[4].Id,
                                              ctx.HomeLineup.Profiles.[5].Id,
                                              0.8
                                          )
                                      ) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  Expect.isEmpty buf.Events "no events"
                  Expect.equal world.State.Ball.Control BallControl.Free "ball untouched")

              testCase "expired clearance gate releases the ball" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  setBall world 0.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Airborne
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some TrajectoryIntent.Cleared } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isTrue
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.BallLoose _ -> true
                          | _ -> false))
                      "an expired clearance becomes visibly loose"

                  Expect.isNone world.State.Ball.Trajectory "the arrival gate no longer owns the flight")

              testCase "Gate in the future is not resolved yet" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Tick = 0<subtick>
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 100<subtick>
                                  Trajectory =
                                      Some(
                                          TrajectoryIntent.Aimed(
                                              ctx.HomeLineup.Profiles.[4].Id,
                                              ctx.HomeLineup.Profiles.[5].Id,
                                              0.8
                                          )
                                      ) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "no events")

              testCase "nearby players cannot win an arrival race without contact" (fun _ ->
                  let c =
                      TestBuilders.defaultCtx ()
                      |> customProfile 5 90.0f 20.0f 20.0f
                      |> customAwayProfile 3 40.0f 90.0f 90.0f

                  let sctx' =
                      { Ctx = c
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = c.HomeLineup.Profiles.[4].Id
                  let receiver = c.HomeLineup.Profiles.[5].Id

                  let _ = place world ClubSide.Home 5 0.7<meter> 0.0<meter>
                  let _ = place world ClubSide.Away 3 0.5<meter> 0.0<meter>
                  setBall world 0.0<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Home).Frame.Intent.Kind.[5] <- IntentCode.ExecuteRun

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents c world buf

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Interception _ -> true
                          | _ -> false))
                      "proximity cannot create an interception"

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Possession _ -> true
                          | _ -> false))
                      "no contact, no possession"

                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "the ball remains loose")

              testCase "a free lane still requires physical contact" (fun _ ->
                  let c =
                      TestBuilders.defaultCtx ()
                      |> customProfile 5 90.0f 20.0f 20.0f
                      |> customAwayProfile 3 25.0f 90.0f 90.0f

                  let sctx' =
                      { Ctx = c
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = c.HomeLineup.Profiles.[4].Id
                  let receiver = c.HomeLineup.Profiles.[5].Id

                  let _ = place world ClubSide.Home 5 2.4<meter> 0.0<meter>
                  let _ = place world ClubSide.Away 3 2.0<meter> 0.0<meter>
                  setBall world 0.0<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Home).Frame.Intent.Kind.[5] <- IntentCode.ExecuteRun

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = None
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  Expect.isEmpty buf.Events "a free lane is not a physical contact"
                  place world ClubSide.Home 5 0.7<meter> 0.0<meter> |> ignore
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents c world buf

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Interception _ -> true
                          | _ -> false))
                      "no interception: the receiver arrived first"

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Possession _ -> true
                          | _ -> false))
                      "a clear lane is not possession"

                  Expect.equal world.State.Ball.Control BallControl.Free "the untouched ball stays free")

              testCase "a pursuing receiver prepares before impact and cannot bypass recovery" (fun _ ->
                  let config =
                      { ctx.Config with
                          ContactAction =
                              { ctx.Config.ContactAction with
                                  ExecutionErrorScale = 0.0<meter> } }

                  let system =
                      { sctx with
                          Ctx = { ctx with Config = config } }

                  let run preparing =
                      let world = TestBuilders.minimalWorld ()
                      placeAllFar world
                      world.Home.Frame.PosX[5] <- 0.0f
                      world.Home.Frame.PosY[5] <- 0.0f
                      world.Home.Frame.BodyFacing[5] <- 0.0f
                      world.Home.Frame.Intent.Kind[5] <- IntentCode.Recover

                      world.State <-
                          { world.State with
                              Ball =
                                  { world.State.Ball with
                                      Control = BallControl.Free
                                      Position =
                                          { world.State.Ball.Position with
                                              X = 1.2<meter>
                                              Vx = -12.0<meterPerSecond> } } }

                      let mutable firstContact = None
                      let mutable preparation = None
                      let mutable rng = Rng.ofSeed 91UL

                      for tick in 0..5 do
                          world.State <-
                              { world.State with
                                  Tick = tick * 1<subtick> }

                          let events = buffer ()
                          rng <- CognitionLatch.step system rng world events

                          if preparing && tick % 2 = 0 then
                              rng <- Actions.step system rng world events

                              if preparation.IsNone then
                                  preparation <- world.Home.Frame.ContactActions[5]

                          rng <- ContactDynamics.step system rng world events

                          for event in events.Events do
                              match event.Data with
                              | EventData.PhysicalContact contact when contact.BallTouched && firstContact.IsNone ->
                                  firstContact <- Some contact
                              | _ -> ()

                      let contact =
                          firstContact
                          |> Option.defaultWith (fun () -> failtest "the real swept path must hit the boot")

                      world, preparation, contact

                  let passive, _, passiveContact = run false
                  let active, prepared, activeContact = run true

                  let action =
                      prepared
                      |> Option.defaultWith (fun () -> failtest "the pursuing intent must prepare a trap")

                  Expect.equal
                      passiveContact.ControlAfter.Kind
                      ContactControlKind.UncontrolledBall
                      "a fast passive impact is not a prepared reception"

                  Expect.equal
                      activeContact.ControlAfter.Kind
                      ContactControlKind.ReceivingBall
                      "the same reachable flight can be cushioned after preparation"

                  Expect.equal
                      activeContact.Action
                      (Some action.Id)
                      "the confirmed touch belongs to the actual preparation"

                  Expect.isGreaterThanOrEqual activeContact.Tick action.CommitAt "no effective trap before commitment"

                  Expect.isLessThan
                      activeContact.Tick
                      (action.CommitAt + config.ContactAction.CommittedDuration)
                      "contact is inside the committed window"

                  Expect.isNone
                      (ContactActionPlanner.prepare system active ClubSide.Home 5 ContactActionKind.ContactTrap None)
                      "a completed touch cannot erase its recovery period"

                  Expect.isNone
                      passive.Home.Frame.ContactActions[5]
                      "collision geometry cannot invent a player's intention")

              testCase "physical foot contact precedes possession" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  let actor = ctx.HomeLineup.Profiles.[5].Id
                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let frame = world.Home.Frame
                  let footX = float ctx.HomeLineup.Profiles.[5].Morphology.FootLength * 0.4
                  frame.PosX[5] <- 0.0f
                  frame.PosY[5] <- 0.0f
                  frame.BodyFacing[5] <- 0.0f

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { TestBuilders.emptyBall with
                                  Position =
                                      { TestBuilders.emptyBall.Position with
                                          X = footX * 1.0<meter>
                                          Y = 0.0<meter>
                                          Vx = 0.5<meterPerSecond> }
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, actor, 1.0))
                                  ArrivalGate = Some 100<subtick> } }

                  let physical = buffer ()
                  ContactDynamics.step sctx (Rng.ofSeed 9UL) world physical |> ignore

                  let contact =
                      physical.Events
                      |> Seq.tryPick (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record when record.BallTouched -> Some record
                          | _ -> None)
                      |> Option.defaultWith (fun () -> failtest "physical integrator found no ball touch")

                  BallContact.step sctx (Rng.ofSeed 9UL) world physical |> ignore

                  Expect.equal contact.Actor actor "contact evidence identifies the participant"
                  Expect.equal contact.SurfaceA ContactSurface.ContactFoot "a soft low-speed touch is made by the foot"

                  Expect.equal
                      contact.ControlAfter.Kind
                      ContactControlKind.ReceivingBall
                      "control candidate follows physical contact"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Receiving(ClubSide.Home, actor, 0<subtick>))
                      "touch starts a receiving grace period"

                  Expect.isTrue
                      (hasEvent physical (fun event ->
                          match event with
                          | EventData.Pass(_, Some receiver, _, _, true, _, _, _, _) -> receiver = actor
                          | _ -> false))
                      "the intended receiver completes the pass only on physical touch"

                  Expect.isFalse
                      (hasEvent physical (fun event ->
                          match event with
                          | EventData.Possession _ -> true
                          | _ -> false))
                      "contact is not instant possession"

                  for event in physical.Events do
                      EventApply.apply ctx world event

                  world.State <-
                      { world.State with
                          Tick = ctx.Config.Ball.ReceptionGrace }

                  let gained = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 9UL) world gained |> ignore

                  Expect.isTrue
                      (hasEvent gained (fun event ->
                          match event with
                          | EventData.Possession(ClubSide.Home, player) -> player = actor
                          | _ -> false))
                      "possession follows the physical reception"

                  for event in gained.Events do
                      EventApply.apply ctx world event

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Controlled(ClubSide.Home, actor))
                      "the reducer commits control after grace")

              testCase "a fast pass can be trapped by a physical foot contact" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  prepareTrap world
                  let actor = ctx.HomeLineup.Profiles.[5].Id
                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let frame = world.Home.Frame
                  let footX = float ctx.HomeLineup.Profiles.[5].Morphology.FootLength * 0.4
                  frame.PosX[5] <- 0.0f
                  frame.PosY[5] <- 0.0f
                  frame.BodyFacing[5] <- 0.0f

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { TestBuilders.emptyBall with
                                  Position =
                                      { TestBuilders.emptyBall.Position with
                                          X = (footX + 0.15) * 1.0<meter>
                                          Y = 0.0<meter>
                                          Vx = -15.0<meterPerSecond> }
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, actor, 1.0))
                                  ArrivalGate = Some 100<subtick> } }

                  let physical = buffer ()
                  ContactDynamics.step sctx (Rng.ofSeed 13UL) world physical |> ignore

                  let contact =
                      physical.Events
                      |> Seq.tryPick (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record when record.BallTouched -> Some record
                          | _ -> None)
                      |> Option.defaultWith (fun () -> failtest "physical integrator found no fast ball touch")

                  Expect.equal contact.SurfaceA ContactSurface.ContactFoot "the fast pass reaches the boot"

                  Expect.equal
                      contact.ControlAfter.Kind
                      ContactControlKind.ReceivingBall
                      "cushioning follows a real foot impact"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Receiving(ClubSide.Home, actor, world.State.Tick))
                      "the prepared trap begins physical reception"

                  Expect.isLessThanOrEqual
                      (MatchMath.speedOf world.State.Ball.Position.Vx world.State.Ball.Position.Vy)
                      (float ctx.Config.Contact.BallControlSpeed)
                      "the foot impulse removes the incoming pass speed"

                  BallContact.step sctx (Rng.ofSeed 13UL) world physical |> ignore

                  Expect.isTrue
                      (hasEvent physical (fun event ->
                          match event with
                          | EventData.Pass(_, Some receiver, _, _, true, _, _, _, _) -> receiver = actor
                          | _ -> false))
                      "a completed delivery needs the physical trap")

              testCase "the planted heel can receive a ball from behind the player's facing" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  prepareTrap world
                  let actor = ctx.HomeLineup.Profiles.[5].Id
                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let frame = world.Home.Frame
                  frame.PosX[5] <- 0.0f
                  frame.PosY[5] <- 0.0f
                  frame.BodyFacing[5] <- 0.0f

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { TestBuilders.emptyBall with
                                  Position =
                                      { TestBuilders.emptyBall.Position with
                                          X = -0.4<meter>
                                          Vx = 12.0<meterPerSecond> }
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, actor, 1.0))
                                  ArrivalGate = Some 100<subtick> } }

                  let physical = buffer ()
                  ContactDynamics.step sctx (Rng.ofSeed 17UL) world physical |> ignore

                  let contact =
                      physical.Events
                      |> Seq.tryPick (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record when record.BallTouched -> Some record
                          | _ -> None)
                      |> Option.defaultWith (fun () -> failtest "the heel sphere intersects the swept ball path")

                  Expect.equal contact.Actor actor "the receiving player caused the touch"
                  Expect.equal contact.SurfaceA ContactSurface.ContactFoot "heel contact remains a foot contact"

                  Expect.equal
                      contact.ControlAfter.Kind
                      ContactControlKind.ReceivingBall
                      "the supported low-speed heel touch controls"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Receiving(ClubSide.Home, actor, world.State.Tick))
                      "the contact starts normal receiving grace")

              testCase "either planted forefoot can receive a lateral pass" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  prepareTrap world
                  let actor = ctx.HomeLineup.Profiles.[5].Id
                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let frame = world.Home.Frame
                  frame.PosX[5] <- 0.0f
                  frame.PosY[5] <- 0.0f
                  frame.BodyFacing[5] <- 0.0f

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { TestBuilders.emptyBall with
                                  Position =
                                      { TestBuilders.emptyBall.Position with
                                          X = 0.1<meter>
                                          Y = -0.5<meter>
                                          Vy = 12.0<meterPerSecond> }
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, actor, 1.0))
                                  ArrivalGate = Some 100<subtick> } }

                  let physical = buffer ()
                  ContactDynamics.step sctx (Rng.ofSeed 23UL) world physical |> ignore

                  let contact =
                      physical.Events
                      |> Seq.tryPick (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record when record.BallTouched -> Some record
                          | _ -> None)
                      |> Option.defaultWith (fun () ->
                          failtest "the lateral forefoot sphere intersects the swept ball path")

                  Expect.equal contact.Actor actor "the receiver's foot causes the touch"
                  Expect.equal contact.SurfaceA ContactSurface.ContactFoot "a lateral reception remains a foot touch"

                  Expect.equal
                      contact.ControlAfter.Kind
                      ContactControlKind.ReceivingBall
                      "the supported lateral trap controls the slowed ball")

              testCase
                  "first touch follows technique: the same fast pass is cushioned by a good touch and bounces off a poor one"
                  (fun _ ->
                      let touchWith (touch: float32) (stable: bool) =
                          let profiles = Array.copy ctx.HomeLineup.Profiles

                          profiles[5] <-
                              { profiles[5] with
                                  Dribbling = touch
                                  Passing = touch }

                          let c =
                              { ctx with
                                  HomeLineup =
                                      { ctx.HomeLineup with
                                          Profiles = profiles } }

                          let system =
                              { Ctx = c
                                Clock = SimulationClock.defaultClock }

                          let world = TestBuilders.minimalWorld ()
                          placeAllFar world
                          let frame = world.Home.Frame
                          frame.PosX[5] <- 0.0f
                          frame.PosY[5] <- 0.0f
                          frame.BodyFacing[5] <- 0.0f

                          if not stable then
                              frame.ContactState[5] <-
                                  { frame.ContactState[5] with
                                      Status = BodyContactStatus.BodyStaggering }

                          let action =
                              ContactActionPlanner.prepare
                                  system
                                  world
                                  ClubSide.Home
                                  5
                                  ContactActionKind.ContactTrap
                                  None
                              |> Option.defaultWith (fun () -> failtest "receiver must be eligible to prepare")

                          world.Home.Frame.ContactActions[5] <-
                              Some
                                  { action with
                                      Phase = ContactActionPhase.ContactActionCommitted }

                          let footX = float profiles[5].Morphology.FootLength * 0.4

                          world.State <-
                              { world.State with
                                  Tick = action.CommitAt
                                  Flow = MatchFlow.Live
                                  Ball =
                                      { TestBuilders.emptyBall with
                                          Position =
                                              { TestBuilders.emptyBall.Position with
                                                  X = (footX + 0.15) * 1.0<meter>
                                                  Vx = -15.0<meterPerSecond> } } }

                          let physical = buffer ()
                          ContactDynamics.step system (Rng.ofSeed 13UL) world physical |> ignore

                          let record =
                              physical.Events
                              |> Seq.tryPick (fun event ->
                                  match event.Data with
                                  | EventData.PhysicalContact record when record.BallTouched -> Some record
                                  | _ -> None)
                              |> Option.defaultWith (fun () -> failtest "the pass must reach the boot")

                          record, MatchMath.speedOf world.State.Ball.Position.Vx world.State.Ball.Position.Vy

                      let good, goodSpeed = touchWith 90.0f true
                      let poor, poorSpeed = touchWith 20.0f true
                      let _, staggeredSpeed = touchWith 55.0f false
                      let _, steadySpeed = touchWith 55.0f true

                      Expect.equal
                          good.ControlAfter.Kind
                          ContactControlKind.ReceivingBall
                          "a good first touch kills the pass"

                      Expect.equal
                          poor.ControlAfter.Kind
                          ContactControlKind.UncontrolledBall
                          "a poor first touch loses it"

                      Expect.isGreaterThan poorSpeed (goodSpeed + 1.0) "and the ball runs off the poor touch"
                      Expect.isGreaterThan staggeredSpeed steadySpeed "the same receiver controls worse off balance")
              testCase "a prepared trap cushions a glancing ball that passive friction cannot control" (fun _ ->
                  // Regression: Coulomb friction bounded by the small normal impulse of a grazing touch left most
                  // receptions uncontrollable, so loose balls pinballed for ~20% of match time.
                  let glancing (absorption: float) (offset: float) =
                      let c =
                          { ctx with
                              Config =
                                  { ctx.Config with
                                      Contact =
                                          { ctx.Config.Contact with
                                              TrapAbsorptionBase = absorption
                                              TrapAbsorptionSkill = 0.0 } } }

                      let system =
                          { Ctx = c
                            Clock = SimulationClock.defaultClock }

                      let world = TestBuilders.minimalWorld ()
                      placeAllFar world
                      let frame = world.Home.Frame
                      frame.PosX[5] <- 0.0f
                      frame.PosY[5] <- 0.0f
                      frame.BodyFacing[5] <- 0.0f

                      world.State <-
                          { world.State with
                              Flow = MatchFlow.Live
                              Ball =
                                  { TestBuilders.emptyBall with
                                      Position =
                                          { TestBuilders.emptyBall.Position with
                                              X = 0.1<meter>
                                              Y = -0.5<meter> } } }

                      let action =
                          ContactActionPlanner.prepare system world ClubSide.Home 5 ContactActionKind.ContactTrap None
                          |> Option.defaultWith (fun () -> failtest "receiver must be eligible to prepare")

                      world.Home.Frame.ContactActions[5] <-
                          Some
                              { action with
                                  Phase = ContactActionPhase.ContactActionCommitted }

                      world.State <-
                          { world.State with
                              Tick = action.CommitAt
                              Ball =
                                  { TestBuilders.emptyBall with
                                      Position =
                                          { TestBuilders.emptyBall.Position with
                                              X = offset * 1.0<meter>
                                              Y = -0.5<meter>
                                              Vy = 12.0<meterPerSecond> } } }

                      let physical = buffer ()
                      ContactDynamics.step system (Rng.ofSeed 29UL) world physical |> ignore

                      physical.Events
                      |> Seq.tryPick (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record when
                              record.BallTouched && record.SurfaceA = ContactSurface.ContactFoot
                              ->
                              Some record
                          | _ -> None)

                  let isGlancing (record: ContactRecord) = abs record.NormalY < 0.5

                  let offset =
                      [ 0.10..0.01..0.45 ]
                      |> List.tryFind (fun offset -> glancing 0.0 offset |> Option.exists isGlancing)
                      |> Option.defaultWith (fun () -> failtest "fixture must produce a grazing foot contact")

                  let passive = (glancing 0.0 offset).Value

                  let trapped =
                      glancing 0.9 offset
                      |> Option.defaultWith (fun () -> failtest "same geometry must touch")

                  Expect.equal
                      passive.ControlAfter.Kind
                      ContactControlKind.UncontrolledBall
                      "negative control: Coulomb-only trap leaves the grazing ball loose"

                  Expect.equal
                      trapped.ControlAfter.Kind
                      ContactControlKind.ReceivingBall
                      "the cushioning foot controls the same grazing ball") ]
