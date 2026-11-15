namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module GoalDetectionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private shooter = ctx.HomeLineup.Profiles.[9].Id
    let private defLine = Pitch.awayGoalX

    let private spatial (x: float<meter>) (y: float<meter>) (z: float<meter>) : Spatial =
        { X = x
          Y = y
          Z = z
          Vx = 0.0<meterPerSecond>
          Vy = 0.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

    let private configureShot (world: World) (previous: Spatial) (current: Spatial) lastTouch =
        world.State <-
            { world.State with
                Flow = MatchFlow.Live
                Ball =
                    { TestBuilders.emptyBall with
                        Position = current
                        PreviousPosition = ValueSome previous
                        ArrivalGate = Some 0<subtick>
                        Trajectory = Some(TrajectoryIntent.Struck(shooter, 18.0<meterPerSecond>, 0.2, 0.8))
                        LastTouch = Some lastTouch
                        Control = BallControl.Airborne } }

    let private resolveCrossing (y: float<meter>) (x: float<meter>) : EventBuffer =
        let world = TestBuilders.minimalWorld ()
        let previousX = if x >= defLine then defLine - 0.5<meter> else x - 0.5<meter>
        configureShot world (spatial previousX y 0.0<meter>) (spatial x y 0.0<meter>) shooter
        let buf = buffer ()
        BallOutcome.step sctx (Rng.ofSeed 0UL) world buf |> ignore
        buf

    let private physicalRecord (actor: PlayerId) (control: ContactControlSnapshot) : ContactRecord =
        { Id = ContactId 1UL
          Actor = actor
          ActorGeneration = 0
          Other = None
          OtherGeneration = None
          Action = None
          ActionKind = None
          Tick = 0<subtick>
          Fraction = 0.5
          PointX = defLine - 0.2<meter>
          PointY = 0.0<meter>
          PointZ = 0.2<meter>
          NormalX = -1.0
          NormalY = 0.0
          NormalZ = 0.0
          SurfaceA = ContactSurface.ContactLeg
          SurfaceB = ContactSurface.ContactBall
          RelativeVelocityX = 18.0<meterPerSecond>
          RelativeVelocityY = 0.0<meterPerSecond>
          RelativeVelocityZ = 0.0<meterPerSecond>
          NormalImpulse = 1.0
          TangentialImpulse = 0.0
          SupportBefore = BodySupport.DoubleFootSupport
          SupportAfter = BodySupport.DoubleFootSupport
          BallTouched = true
          BallTouchOrder = Some 1
          ControlBefore =
            { Kind = ContactControlKind.UncontrolledBall
              Side = None
              Player = None }
          ControlAfter = control }

    let private applyPhysicalTouch (world: World) (actor: PlayerId) (control: ContactControlSnapshot) =
        let side = MatchWorld.sideOfPlayer ctx actor |> ValueOption.defaultValue ClubSide.Away
        let ballControl =
            match control.Kind, control.Side, control.Player with
            | ContactControlKind.ReceivingBall, Some ownerSide, Some player when player = actor ->
                BallControl.Receiving(ownerSide, player, world.State.Tick)
            | _ -> BallControl.Free

        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Control = ballControl
                        LastTouch = Some actor } }

        let buf = buffer ()
        buf.Events.Add
            { Tick = world.State.Tick
              Data = EventData.PhysicalContact(physicalRecord actor control) }

        BallContact.step sctx (Rng.ofSeed 0UL) world buf |> ignore
        side, buf

    let private placeAllFar (world: World) =
        for side in [ ClubSide.Home; ClubSide.Away ] do
            let frame = (MatchWorld.team world side).Frame

            for slot in MatchWorld.activeSlots frame do
                frame.PosX[slot] <- float32 (if side = ClubSide.Home then -40.0 + float slot * 2.0 else 40.0 - float slot * 2.0)
                frame.PosY[slot] <- if slot % 2 = 0 then -25.0f else 25.0f
                frame.VelX[slot] <- 0.0f
                frame.VelY[slot] <- 0.0f
                frame.TargetVelX[slot] <- 0.0f
                frame.TargetVelY[slot] <- 0.0f

    let private hasGoal (buf: EventBuffer) =
        buf.Events |> Seq.exists (fun ev -> MatchEvent.kind ev = EventKind.Goal)

    let private hasScoreEvent (buf: EventBuffer) =
        buf.Events
        |> Seq.exists (fun ev ->
            match ev.Data with
            | EventData.Goal _
            | EventData.Save _ -> true
            | _ -> false)

    let goalDetectionTests =
        testList
            "FootballEngine.Core.Systems.Ball.GoalDetectionTests"
            [ testCase "ball inside the frame scores without physical goalkeeper contact" (fun _ ->
                  // The whole ball (r = 0.11 m) must be over the line, so the centre is placed 0.2 m beyond it.
                  let buf = resolveCrossing 0.5<meter> (defLine + 0.2<meter>)
                  Expect.isTrue (hasGoal buf) "an unobstructed in-frame crossing is a goal"
                  Expect.isFalse
                      (buf.Events |> Seq.exists (fun event -> match event.Data with EventData.Save _ -> true | _ -> false))
                      "keeper attributes cannot create a save")

              testCase "wide of the posts is never a goal" (fun _ ->
                  let buf = resolveCrossing 6.0<meter> (defLine + 0.1<meter>)
                  Expect.isFalse (hasGoal buf) "wide of the posts can't score"
                  Expect.isTrue
                      (buf.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.SetPiece(SetPieceKind.GoalKick, ClubSide.Away) -> true
                           | _ -> false))
                      "an untouched wide crossing gives the defending side a goal kick")

              testCase "back of the net is a goal" (fun _ ->
                  let buf = resolveCrossing 0.5<meter> (defLine + 0.5<meter>)
                  Expect.isTrue (hasGoal buf) "crossing inside the frame scores")

              testCase "high crossing is not a goal" (fun _ ->
                  let previous = spatial (defLine - 0.5<meter>) 0.5<meter> 3.0<meter>
                  let current = spatial (defLine + 0.5<meter>) 0.5<meter> 3.0<meter>

                  Expect.isFalse
                      (BallPhysics.goalCrossing 0.11<meter> previous current
                       |> Option.exists (fun crossing -> BallPhysics.withinGoalFrame crossing.Position))
                      "crossbar height rejects the shot")

              testCase "crossing direction identifies both goal lines" (fun _ ->
                  let towardAway = spatial (Pitch.awayGoalX - 1.0<meter>) 0.0<meter> 0.0<meter>
                  let pastAway = spatial (Pitch.awayGoalX + 1.0<meter>) 0.0<meter> 0.0<meter>
                  let towardHome = spatial (Pitch.homeGoalX + 1.0<meter>) 0.0<meter> 0.0<meter>
                  let pastHome = spatial (Pitch.homeGoalX - 1.0<meter>) 0.0<meter> 0.0<meter>

                  match BallPhysics.goalCrossing 0.11<meter> towardAway pastAway, BallPhysics.goalCrossing 0.11<meter> towardHome pastHome with
                  | Some away, Some home ->
                      Expect.equal away.Side ClubSide.Home "away goal belongs to home scorer"
                      Expect.equal home.Side ClubSide.Away "home goal belongs to away scorer"
                  | other -> failtestf "expected both crossings, got %A" other)

              testCase "controlled physical interception before the line ends the shot" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let defender = ctx.AwayLineup.Profiles.[3].Id
                  let previous = spatial (defLine - 1.0<meter>) 0.0<meter> 0.0<meter>
                  let current = spatial (defLine - 0.8<meter>) 0.0<meter> 0.0<meter>
                  configureShot world previous current shooter

                  let receiving =
                      { Kind = ContactControlKind.ReceivingBall
                        Side = Some ClubSide.Away
                        Player = Some defender }

                  let _, contactEvents = applyPhysicalTouch world defender receiving
                  let outcomeEvents = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 0UL) world outcomeEvents |> ignore

                  Expect.isNone world.State.Ball.Trajectory "physical control cancels the original shot flight"
                  Expect.isFalse (hasGoal outcomeEvents) "a cancelled shot cannot score on its old label"
                  Expect.isTrue
                      (contactEvents.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.Shot(_, ShotResult.Blocked, _, _, _, _) -> true
                           | _ -> false))
                      "the controlled defender contact resolves the shot as blocked")

              testCase "loose deflection keeps shot context until its new path crosses in frame" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let defender = ctx.AwayLineup.Profiles.[3].Id
                  let previous = spatial (defLine - 0.5<meter>) 0.5<meter> 0.0<meter>
                  let current = spatial (defLine + 0.2<meter>) 0.5<meter> 0.0<meter>
                  configureShot world previous current shooter

                  let uncontrolled =
                      { Kind = ContactControlKind.UncontrolledBall
                        Side = None
                        Player = None }

                  let _, contactEvents = applyPhysicalTouch world defender uncontrolled
                  Expect.isSome world.State.Ball.Trajectory "a loose deflection retains the shot origin until outcome"
                  let outcomeEvents = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 0UL) world outcomeEvents |> ignore

                  Expect.isTrue (hasGoal outcomeEvents) "the actual post-contact crossing still scores"
                  Expect.isFalse (hasScoreEvent contactEvents) "mere contact does not decide the goal result")

              testCase "deflection outside the frame prevents a goal and awards a corner" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let defender = ctx.AwayLineup.Profiles.[3].Id
                  let previous = spatial (defLine - 0.5<meter>) 0.5<meter> 0.0<meter>
                  let current = spatial (defLine + 0.1<meter>) 5.0<meter> 0.0<meter>
                  configureShot world previous current shooter

                  let uncontrolled =
                      { Kind = ContactControlKind.UncontrolledBall
                        Side = None
                        Player = None }

                  applyPhysicalTouch world defender uncontrolled |> ignore
                  let outcomeEvents = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 0UL) world outcomeEvents |> ignore

                  Expect.isFalse (hasGoal outcomeEvents) "the deflected crossing misses the frame"
                  Expect.isTrue
                      (outcomeEvents.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.Shot(_, ShotResult.Blocked, _, _, _, _) -> true
                           | _ -> false))
                      "the actual defensive touch classifies the shot as blocked"
                  Expect.isTrue
                      (outcomeEvents.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.SetPiece(SetPieceKind.Corner, ClubSide.Home) -> true
                           | _ -> false))
                      "the defending last touch awards the attacking corner")

              testCase "goalkeeper deflection records a save only after the shot exits" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let goalkeeper = ctx.AwayLineup.Profiles.[0].Id
                  let previous = spatial (defLine - 0.5<meter>) 0.5<meter> 0.0<meter>
                  let current = spatial (defLine + 0.1<meter>) 5.0<meter> 0.0<meter>
                  configureShot world previous current shooter

                  let uncontrolled =
                      { Kind = ContactControlKind.UncontrolledBall
                        Side = None
                        Player = None }

                  applyPhysicalTouch world goalkeeper uncontrolled |> ignore
                  let outcomeEvents = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 0UL) world outcomeEvents |> ignore

                  Expect.isFalse (hasGoal outcomeEvents) "the deflected ball misses"
                  Expect.isTrue
                      (outcomeEvents.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.Save(keeper, attempt) -> keeper = goalkeeper && attempt = shooter
                           | _ -> false))
                      "a goalkeeper save is tied to a real keeper-ball touch and terminal miss"
                  Expect.isTrue
                      (outcomeEvents.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.Shot(_, ShotResult.OnTarget true, _, _, _, _) -> true
                           | _ -> false))
                      "the physical keeper contact resolves the attempt on target")

              testCase "the physical solver intercepts a live shot before its goal-line crossing" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  let defender = ctx.AwayLineup.Profiles.[3].Id
                  let away = world.Away.Frame
                  away.PosX[3] <- float32 (defLine - 0.2<meter>)
                  away.PosY[3] <- 0.0f

                  let impulse =
                      BallPhysics.createImpulse
                          shooter
                          { X = defLine - 0.6<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }
                          { X = 30.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          None
                          StrikeKind.Shot
                          (SimTick.ofSubTicks world.State.Tick)
                          ExecutionContext.OpenPlay

                  BallPhysics.applyImpulse
                      world
                      impulse
                      (TrajectoryIntent.Struck(shooter, 30.0<meterPerSecond>, 0.0, 0.8))
                      None

                  let physical = buffer ()
                  ContactDynamics.step sctx (Rng.ofSeed 3UL) world physical |> ignore

                  let hit =
                      physical.Events
                      |> Seq.exists (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record -> record.Actor = defender && record.BallTouched
                          | _ -> false)

                  Expect.isTrue hit "the swept body/ball solver confirms the interception"

                  BallContact.step sctx (Rng.ofSeed 3UL) world physical |> ignore
                  let outcome = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 3UL) world outcome |> ignore

                  Expect.isFalse (hasGoal outcome) "the post-contact ball velocity, not the initial shot label, decides the goal"
                  Expect.isSome world.State.Ball.Trajectory "a loose physical deflection retains shot provenance") ]
