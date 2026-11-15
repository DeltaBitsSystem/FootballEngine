namespace FootballEngine.Core.Tests.Systems.Ball

open System.Numerics
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module FootMotionTests =
    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private controlled () =
        let world = TestBuilders.minimalWorld ()
        world.Home.Frame.PosX[5] <- 0.0f
        world.Home.Frame.PosY[5] <- 0.0f
        let actor = ctx.HomeLineup.Profiles[5].Id

        world.State <-
            { world.State with
                Flow = MatchFlow.Live
                Ball =
                    { world.State.Ball with
                        Control = BallControl.Controlled(ClubSide.Home, actor) } }

        world

    let private request world buffer =
        let impulse =
            BallPhysics.createImpulse
                ctx.HomeLineup.Profiles[5].Id
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

        BallFootContact.request sctx world buffer impulse TrajectoryIntent.Uncontrolled None

    let tests =
        testList
            "FootMotion"
            [ testCase "strike evidence captures exact system and published boundary with pre post velocity" (fun _ ->
                  let world, events = controlled (), buffer ()
                  request world events
                  let pending = world.State.Ball.PendingStrike.Value
                  let preparation = Array.zeroCreate<MotionEvidence> 4

                  Expect.equal
                      (world.Motion.Read(1UL, 0<subtick>, preparation))
                      (Ok(struct (1UL, 0)))
                      "request is not published in its producing tick"

                  world.Motion.Read(1UL, 1<subtick>, preparation) |> ignore
                  let planned = preparation[0]
                  let contact = SimTick.value pending.Impulse.ContactTick
                  Expect.equal planned.CommitAt (contact + 1<subtick>) "commit is the post step boundary"
                  world.State <- { world.State with Tick = contact }
                  let incoming = world.State.Ball.Position
                  BallFootContact.step sctx (Rng.ofSeed 1UL) world events |> ignore

                  Expect.equal
                      (world.Motion.Read(2UL, contact, preparation))
                      (Ok(struct (2UL, 0)))
                      "confirmation unavailable until boundary"

                  Expect.equal
                      (world.Motion.Read(2UL, contact + 1<subtick>, preparation))
                      (Ok(struct (3UL, 1)))
                      "exact boundary, no one tick tolerance"

                  let confirmed = preparation[0]
                  Expect.equal confirmed.TaskId planned.TaskId "confirmation belongs to preparation"
                  Expect.equal confirmed.OccurredAt contact "impulse producing tick"
                  Expect.equal confirmed.CommitAt (contact + 1<subtick>) "envelope and commit agree"
                  Expect.equal confirmed.PredictedPoint planned.PredictedPoint "prediction is not overwritten"

                  Expect.equal
                      confirmed.IncomingVelocity
                      (ValueSome(Vector3(float32 incoming.Vx, float32 incoming.Vy, float32 incoming.Vz)))
                      "captured before impulse"

                  let outgoing = world.State.Ball.Position

                  Expect.equal
                      confirmed.OutgoingVelocity
                      (ValueSome(Vector3(float32 outgoing.Vx, float32 outgoing.Vy, float32 outgoing.Vz)))
                      "captured after impulse"

                  Expect.isTrue confirmed.ConfirmedPoint.IsSome "real surface point captured")
              testCase "reception evidence is reactive and available only after momentum absorption" (fun _ ->
                  let world, events = controlled (), buffer ()

                  world.State <-
                      { world.State with
                          Tick = 8<subtick>
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          Vx = 12.0<meterPerSecond> } } }

                  // The first touch of a carry absorbs the pass down to 2 m/s; this is the producer Possession uses.
                  BallFootContact.recordTouch
                      sctx
                      world
                      events
                      ClubSide.Home
                      5
                      ContactFoot.Right
                      FootTouchKind.Receive
                      -10.0
                      0.0
                      0.0

                  let output = Array.zeroCreate<MotionEvidence> 2

                  Expect.equal
                      (world.Motion.Read(1UL, 8<subtick>, output))
                      (Ok(struct (1UL, 0)))
                      "no invented anticipation"

                  world.Motion.Read(1UL, 9<subtick>, output) |> ignore
                  Expect.equal output[0].Family MotionFamily.Reception "explicit receiving producer"
                  Expect.equal output[0].CommitAt 9<subtick> "exact touch boundary"
                  Expect.equal output[0].IncomingVelocity.Value.X 12.0f "incoming velocity preserved"

                  Expect.equal output[0].OutgoingVelocity.Value.X 2.0f "outgoing is absorption result")
              testCase "steady motion integration allocates no managed memory" (fun _ ->
                  let ball =
                      { TestBuilders.emptyBall with
                          Position =
                              { TestBuilders.emptyBall.Position with
                                  Z = 1.0<meter>
                                  Vx = 12.0<meterPerSecond> } }

                  BallDynamics.integrate ctx.Config.Ball 0.025 1<subtick> ball |> ignore
                  let before = System.GC.GetAllocatedBytesForCurrentThread()
                  let mutable checksum = 0.0

                  for tick = 1 to 1000 do
                      let result = BallDynamics.integrate ctx.Config.Ball 0.025 (tick * 1<subtick>) ball
                      checksum <- checksum + float result.Position.X

                  let allocated = System.GC.GetAllocatedBytesForCurrentThread() - before
                  Expect.equal allocated 0L "value history and scalar kernel do not allocate per tick"
                  Expect.isGreaterThan checksum 0.0 "results are consumed")
              testCase "reachable contact search allocates no candidate arrays or boxes" (fun _ ->
                  let world = controlled ()
                  BallContact.tryReachableContact ctx world 0.0<meter> 0.0<meter> None |> ignore
                  let before = System.GC.GetAllocatedBytesForCurrentThread()
                  let mutable count = 0

                  for _ = 1 to 1000 do
                      match BallContact.tryReachableContact ctx world 0.0<meter> 0.0<meter> None with
                      | ValueSome _ -> count <- count + 1
                      | ValueNone -> ()

                  let allocated = System.GC.GetAllocatedBytesForCurrentThread() - before
                  Expect.equal allocated 0L "slot scan and winner use value types"
                  Expect.equal count 1000 "reachable winner is consumed")
              testCase "a moving loose ball is trapped at physical reach without teleporting" (fun _ ->
                  let world, events = controlled (), buffer ()
                  let footX = float ctx.HomeLineup.Profiles[5].Morphology.FootLength * 0.4

                  for slot = 0 to world.Home.Frame.PosX.Length - 1 do
                      if slot = 5 then
                          world.Home.Frame.PosX[slot] <- 0.0f
                          world.Home.Frame.PosY[slot] <- 0.0f
                      else
                          world.Home.Frame.PosX[slot] <- -40.0f + float32 slot
                          world.Home.Frame.PosY[slot] <- -20.0f + float32 slot * 4.0f

                      world.Away.Frame.PosX[slot] <- 40.0f - float32 slot
                      world.Away.Frame.PosY[slot] <- -20.0f + float32 slot * 4.0f

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Free
                                  Position =
                                      { world.State.Ball.Position with
                                          X = footX * 1.0<meter>
                                          Vx = 12.0<meterPerSecond> } } }

                  let origin = world.State.Ball.Position
                  ContactDynamics.step sctx (Rng.ofSeed 1UL) world events |> ignore
                  BallPossession.step sctx (Rng.ofSeed 1UL) world events |> ignore

                  Expect.isLessThan
                      (abs (float world.State.Ball.Position.Vx))
                      12.0
                      "physical impact transfers momentum and loses energy"

                  Expect.isLessThanOrEqual
                      (abs (float (world.State.Ball.Position.X - origin.X)))
                      (12.0 * float (SimulationClock.dt sctx.Clock) + 0.01)
                      "the collision advances only through the fixed subtick"

                  Expect.isTrue
                      (events.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.PhysicalContact contact -> contact.BallTouched
                           | _ -> false))
                      (sprintf
                          "first touch is visible as physical contact evidence: %A"
                          (events.Events |> Seq.map _.Data |> Seq.toList)))
              testCase "a ball above foot reach cannot produce a foot contact" (fun _ ->
                  let world, events = controlled (), buffer ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Free
                                  Position =
                                      { world.State.Ball.Position with
                                          Z = 2.0<meter> } } }

                  BallPossession.step sctx (Rng.ofSeed 1UL) world events |> ignore
                  Expect.isEmpty events.Events "horizontal proximity cannot trap an unreachable high ball")
              testCase "off-centre strike spin follows the contact lever and shell inertia" (fun _ ->
                  let world, events = controlled (), buffer ()
                  request world events
                  let requested = world.State.Ball.PendingStrike.Value.Impulse

                  let requested =
                      { requested with
                          AngularVelocity =
                              { requested.AngularVelocity with
                                  Top = 30.0<radianPerSecond>
                                  Side = 8.0<radianPerSecond> } }

                  let point, angular =
                      BallPhysics.strikeContact ctx.Config.Ball world.State.Ball requested

                  let rx, ry, rz =
                      float point.X, float point.Y, float point.Z - float ctx.Config.Ball.Radius

                  let inertia = ctx.Config.Ball.InertiaRatio * float ctx.Config.Ball.Radius ** 2.0
                  let spin = BallPhysics.worldSpin requested.LinearVelocity angular

                  Expect.floatClose
                      Accuracy.high
                      (sqrt (rx * rx + ry * ry + rz * rz))
                      (float ctx.Config.Ball.Radius)
                      "contact lies on the sphere surface"

                  Expect.floatClose Accuracy.high (float spin.Y) (rz * 15.0 / inertia) "topspin is explained by torque"

                  Expect.floatClose
                      Accuracy.high
                      (float spin.Z)
                      (-ry * 15.0 / inertia)
                      "sidespin is explained by torque")
              testCase "a stationary ball cannot acquire gravity jitter or invented rotation" (fun _ ->
                  let mutable ball = TestBuilders.emptyBall

                  for tick = 1 to 1000 do
                      ball <- BallDynamics.integrate ctx.Config.Ball 0.025 (tick * 1<subtick>) ball

                  Expect.equal ball.Position TestBuilders.emptyBall.Position "rest is an equilibrium"
                  Expect.equal ball.Orientation Quaternion.Identity "rest does not spin")
              testCase "sliding friction transfers momentum into rolling without creating energy" (fun _ ->
                  let cfg = ctx.Config.Ball

                  let ball =
                      { TestBuilders.emptyBall with
                          Position =
                              { TestBuilders.emptyBall.Position with
                                  Vx = 12.0<meterPerSecond> } }

                  let next = BallDynamics.integrate cfg 0.025 1<subtick> ball

                  let energy =
                      float next.Position.Vx ** 2.0
                      + cfg.InertiaRatio * float cfg.Radius ** 2.0 * float next.Spin.Y ** 2.0

                  Expect.isGreaterThan (float next.Spin.Y) 0.0 "ground contact starts physical rotation"
                  Expect.isLessThan energy 144.0 "translation plus rotational energy dissipates")
              testCase "sidespin curvature reverses with travel direction" (fun _ ->
                  let run velocity =
                      BallDynamics.integrate
                          ctx.Config.Ball
                          0.025
                          1<subtick>
                          { TestBuilders.emptyBall with
                              Position =
                                  { TestBuilders.emptyBall.Position with
                                      Z = 1.0<meter>
                                      Vx = velocity }
                              Spin =
                                  { X = 0.0<radianPerSecond>
                                    Y = 0.0<radianPerSecond>
                                    Z = 20.0<radianPerSecond> } }

                  let forward, backward = run 15.0<meterPerSecond>, run -15.0<meterPerSecond>
                  Expect.isGreaterThan (float forward.Position.Vy) 0.0 "forward curves left"
                  Expect.isLessThan (float backward.Position.Vy) 0.0 "reverse travel reverses lateral acceleration")
              testCase "landing resolves within the tick without penetrating the ground" (fun _ ->
                  let ball =
                      { TestBuilders.emptyBall with
                          Position =
                              { TestBuilders.emptyBall.Position with
                                  Z = 0.01<meter>
                                  Vz = -8.0<meterPerSecond> } }

                  let next = BallDynamics.integrate ctx.Config.Ball 0.025 1<subtick> ball
                  Expect.isGreaterThan (float next.Position.Z) 0.0 "remaining tick integrates the rebound"
                  Expect.isGreaterThan (float next.Position.Vz) 0.0 "impact happened before the end of the tick")
              testCase "orientation remains unit length and is independent of replay history" (fun _ ->
                  let ball =
                      { TestBuilders.emptyBall with
                          Spin =
                              { X = 10.0<radianPerSecond>
                                Y = 20.0<radianPerSecond>
                                Z = 3.0<radianPerSecond> } }

                  let mutable rotation = Quaternion.Identity

                  for _ = 1 to 10000 do
                      rotation <- BallDynamics.orientationStep rotation ball.Spin 0.025

                  Expect.floatClose Accuracy.medium (float (rotation.Length())) 1.0 "normalized quaternion"

                  Expect.equal
                      (BallDynamics.integrate ctx.Config.Ball 0.025 1<subtick> ball)
                      (BallDynamics.integrate ctx.Config.Ball 0.025 1<subtick> ball)
                      "pure integration")
              testCase "strike prepares before applying exactly one reachable contact" (fun _ ->
                  let world, events = controlled (), buffer ()
                  request world events
                  Expect.isSome world.State.Ball.PendingStrike "preparation exists"
                  Expect.equal world.State.Ball.Position.Vx 0.0<meterPerSecond> "no pre-contact impulse"

                  for tick = 1 to int ctx.Config.Ball.KickWindup do
                      world.State <-
                          { world.State with
                              Tick = tick * 1<subtick> }

                      BallFootContact.step sctx (Rng.ofSeed 1UL) world events |> ignore

                  Expect.isNone world.State.Ball.PendingStrike "contact consumes request"
                  // The kicker's technique turns the strike by a fraction of a degree, so the impulse is its speed.
                  Expect.floatClose
                      Accuracy.medium
                      (MatchMath.speedOf world.State.Ball.Position.Vx world.State.Ball.Position.Vy)
                      15.0
                      "impulse at contact"

                  BallFootContact.step sctx (Rng.ofSeed 1UL) world events |> ignore

                  Expect.equal
                      (events.Events
                       |> Seq.filter (fun event ->
                           match event.Data with
                           | EventData.FootContact _ -> true
                           | _ -> false)
                       |> Seq.length)
                      1
                      "exactly one contact")
              testCase "an open-play pass retargets to the receiver at committed contact after windup" (fun _ ->
                  let world, events = controlled (), buffer ()
                  let passer = ctx.HomeLineup.Profiles[5].Id
                  let receiver = ctx.HomeLineup.Profiles[6].Id

                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      if slot <> 5 && slot <> 6 then
                          world.Home.Frame.PosX[slot] <- -30.0f + float32 slot
                          world.Home.Frame.PosY[slot] <- -20.0f + float32 slot * 3.0f

                  for slot in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX[slot] <- 30.0f
                      world.Away.Frame.PosY[slot] <- -20.0f + float32 slot * 3.0f

                  let requestedTarget = Some { X = 20.0<meter>; Y = 0.0<meter> }

                  let impulse =
                      BallPhysics.createImpulse
                          passer
                          { X = 0.0<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }
                          { X = 15.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          requestedTarget
                          StrikeKind.Pass
                          (SimTick.ofSubTicks world.State.Tick)
                          ExecutionContext.OpenPlay

                  BallFootContact.request
                      sctx
                      world
                      events
                      impulse
                      (TrajectoryIntent.Aimed(passer, receiver, 0.8))
                      (Some 0<subtick>)

                  let pending = world.State.Ball.PendingStrike.Value
                  let preparedGate = pending.ArrivalGate.Value
                  world.Home.Frame.PosX[6] <- 8.0f
                  world.Home.Frame.PosY[6] <- 4.0f
                  world.Home.Frame.VelX[6] <- 2.0f
                  world.Home.Frame.VelY[6] <- -1.0f

                  world.State <-
                      { world.State with
                          Tick = SimTick.value pending.Impulse.ContactTick }

                  let contactTick = world.State.Tick
                  let origin = world.State.Ball.Position
                  BallFootContact.step sctx (Rng.ofSeed 41UL) world events |> ignore

                  let committed = world.State.Ball.Impulse.Value
                  let target = committed.IntendedTarget.Value
                  let cfg = ctx.Config

                  // A ground pass decelerates, so the lead is the receiver's motion over the solved rolling time to
                  // the lead point itself (a fixed point), not over distance divided by launch speed.
                  let solved =
                      BallAero.pass
                          cfg.Ball
                          cfg.Actions.PassArrivalSpeed
                          cfg.Actions.PassArrivalSpeedGain
                          (sqrt (
                              (float target.X - float origin.X) ** 2.0
                              + (float target.Y - float origin.Y) ** 2.0
                          ))

                  let flightSeconds = solved.Seconds

                  Expect.isLessThan
                      (abs (float target.X - (8.0 + 2.0 * flightSeconds)))
                      0.01
                      "the target follows the receiver's current position and forward velocity"

                  Expect.isLessThan
                      (abs (float target.Y - (4.0 - 1.0 * flightSeconds)))
                      0.01
                      "the target follows lateral receiver motion during windup"

                  Expect.isGreaterThan
                      (float target.X)
                      8.5
                      "the lead is ahead of the receiver, not at his current position"

                  Expect.notEqual
                      committed.IntendedTarget
                      requestedTarget
                      "the committed trajectory replaces the stale pre-windup aim"

                  let speed =
                      sqrt (
                          float committed.LinearVelocity.X ** 2.0
                          + float committed.LinearVelocity.Y ** 2.0
                      )

                  Expect.isLessThan
                      (abs (speed - float solved.LaunchSpeed))
                      1e-6
                      "the committed launch speed is the solved ground-pass speed"

                  let travelTicks =
                      max 1 (int (ceil (flightSeconds * float sctx.Clock.SubTicksPerSecond)))
                      * 1<subtick>

                  Expect.equal
                      world.State.Ball.ArrivalGate
                      (Some(contactTick + travelTicks))
                      "the arrival gate is recomputed from the committed target and launch speed"

                  Expect.isGreaterThan
                      (Option.get world.State.Ball.ArrivalGate)
                      preparedGate
                      "the post-windup flight gate replaces the preparation-time estimate")
              testCase "leaving reach or losing ownership cancels instead of kicking remotely" (fun _ ->
                  for loseOwnership in [ false; true ] do
                      let world, events = controlled (), buffer ()
                      request world events

                      if loseOwnership then
                          world.State <-
                              { world.State with
                                  Ball =
                                      { world.State.Ball with
                                          Control = BallControl.Free } }
                      else
                          world.Home.Frame.PosX[5] <- 8.0f

                      BallFootContact.step sctx (Rng.ofSeed 1UL) world events |> ignore
                      Expect.isNone world.State.Ball.PendingStrike "request cancelled"
                      Expect.equal world.State.Ball.Position.Vx 0.0<meterPerSecond> "no phantom impulse")
              testCase "a dribble touch changes velocity but never changes position" (fun _ ->
                  let world, events = controlled (), buffer ()
                  let origin = world.State.Ball.Position
                  BallPossession.step sctx (Rng.ofSeed 1UL) world events |> ignore
                  Expect.equal world.State.Ball.Position.X origin.X "not tethered"
                  Expect.isGreaterThan (float world.State.Ball.Position.Vx) 0.0 "finite touch impulse"
                  Expect.isSome world.State.Ball.FootMotion "contact is visible to replay")
              testCase "current replay preserves contact and orientation without a legacy reader" (fun _ ->
                  let world, events = controlled (), buffer ()
                  request world events
                  let rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f)

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Orientation = rotation } }

                  let snapshot = Replay.capture world
                  let replay = Replay.write [ snapshot ]
                  let restored = Replay.readFrame replay 0
                  Expect.equal restored.BallOrientation rotation "rotation survives serialization"
                  let expected, actual = snapshot.FootMotion.Value, restored.FootMotion.Value

                  Expect.equal
                      (actual.Actor, actual.Foot, actual.Kind, actual.StartedAt, actual.ContactAt, actual.EndsAt)
                      (expected.Actor,
                       expected.Foot,
                       expected.Kind,
                       expected.StartedAt,
                       expected.ContactAt,
                       expected.EndsAt)
                      "contact identity and timeline survive serialization"

                  Expect.floatClose
                      Accuracy.high
                      (float actual.Point.X)
                      (float expected.Point.X)
                      "contact location preserves float32 precision"

                  Expect.floatClose
                      Accuracy.high
                      (float actual.BallCenter.Z)
                      (float expected.BallCenter.Z)
                      "centre preserves float32 precision"

                  Expect.equal restored.BallRadius snapshot.BallRadius "physical radius survives serialization"

                  Expect.throws
                      (fun () -> Replay.readFrame { replay with Meta = { Version = 9 } } 0 |> ignore)
                      "legacy format is rejected") ]
