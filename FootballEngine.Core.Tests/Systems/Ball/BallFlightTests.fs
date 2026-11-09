namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Air, goal frame and launch solving: each case pins a physical behaviour a viewer or a match result depends on,
/// with opposite-sign and no-effect controls so an inverted or disabled implementation fails.
module BallFlightTests =

    let private cfg = BalanceConfig.defaultBall
    let private dt = 0.025
    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private ballAt x y z vx vy vz (wx: float) (wy: float) (wz: float) =
        { TestBuilders.emptyBall with
            Position =
                { X = x * 1.0<meter>
                  Y = y * 1.0<meter>
                  Z = z * 1.0<meter>
                  Vx = vx * 1.0<meterPerSecond>
                  Vy = vy * 1.0<meterPerSecond>
                  Vz = vz * 1.0<meterPerSecond> }
            Spin =
                { X = wx * 1.0<radianPerSecond>
                  Y = wy * 1.0<radianPerSecond>
                  Z = wz * 1.0<radianPerSecond> }
            Control = BallControl.Airborne }

    let private run seconds (ball: BallState) =
        let mutable state = ball

        for _ in 1 .. int (round (seconds / dt)) do
            state <- BallDynamics.integrate cfg dt 0<subtick> state

        state

    /// Steps until the centre crosses x = plane, returning the interpolated underside position there.
    let private crossingAt plane (ball: BallState) =
        let mutable state = ball
        let mutable result = None
        let mutable steps = 0

        while result.IsNone && steps < 400 do
            let next = BallDynamics.integrate cfg dt 0<subtick> state

            if float next.Position.X >= plane then
                let f = (plane - float state.Position.X) / (float next.Position.X - float state.Position.X)

                result <-
                    Some(
                        float state.Position.Y + f * (float next.Position.Y - float state.Position.Y),
                        float state.Position.Z + f * (float next.Position.Z - float state.Position.Z)
                    )

            state <- next
            steps <- steps + 1

        result

    let private speedOf (ball: BallState) =
        let p = ball.Position
        sqrt (float p.Vx ** 2.0 + float p.Vy ** 2.0 + float p.Vz ** 2.0)

    [<Tests>]
    let tests =
        testList
            "BallFlight"
            [ testCase "drag crisis: slow balls lose a larger share of speed per metre than driven ones" (fun _ ->
                  let relativeLossPerMetre speed =
                      BallAero.dragDeceleration cfg speed / speed / speed

                  Expect.isGreaterThan (relativeLossPerMetre 8.0) (1.8 * relativeLossPerMetre 25.0) "Cd falls through the crisis"
                  Expect.floatClose { absolute = 0.005; relative = 0.0 } (BallAero.dragCoefficient cfg 2.0) cfg.DragSubcritical "subcritical plateau"
                  Expect.floatClose { absolute = 0.005; relative = 0.0 } (BallAero.dragCoefficient cfg 30.0) cfg.DragSupercritical "supercritical plateau")

              testCase "a 30 m/s strike keeps most but not all of its pace over 0.6 s of flight" (fun _ ->
                  // Started high enough that no bounce interferes: only air acts over these 0.6 s.
                  let driven = run 0.6 (ballAt 0.0 0.0 1.0 30.0 0.0 3.0 0.0 0.0 0.0)
                  Expect.isGreaterThan (speedOf driven / 30.0) 0.75 "drag is moderate after the crisis"
                  Expect.isLessThan (speedOf driven / 30.0) 0.97 "but a real ball still slows")

              testCase "sidespin curls in the direction of ω × v, and reverses with it" (fun _ ->
                  let left = run 1.0 (ballAt 0.0 0.0 0.5 25.0 0.0 3.0 0.0 0.0 40.0)
                  let right = run 1.0 (ballAt 0.0 0.0 0.5 25.0 0.0 3.0 0.0 0.0 -40.0)
                  let none = run 1.0 (ballAt 0.0 0.0 0.5 25.0 0.0 3.0 0.0 0.0 0.0)
                  Expect.isGreaterThan (float left.Position.Y) 0.5 "ω = +z bends toward +y"
                  Expect.isLessThan (float right.Position.Y) -0.5 "reversed spin bends the other way"
                  Expect.floatClose Accuracy.medium (float none.Position.Y) 0.0 "no spin, no curl"
                  Expect.floatClose Accuracy.medium (float left.Position.Y) (-float right.Position.Y) "the effect is symmetric")

              testCase "topspin dips and backspin floats relative to no spin" (fun _ ->
                  let height (wy: float) =
                      float (run 0.5 (ballAt 0.0 0.0 0.5 25.0 0.0 5.0 0.0 wy 0.0)).Position.Z

                  Expect.isLessThan (height 40.0) (height 0.0) "topspin (ω = +y for +x travel) dips"
                  Expect.isGreaterThan (height -40.0) (height 0.0) "backspin floats")

              testCase "a fast shot cannot tunnel through a post between ticks" (fun _ ->
                  let postY = float Pitch.GoalHalfWidth + float Pitch.PostRadius
                  // 36 m/s covers 0.9 m per tick, several times the 0.34 m contact diameter.
                  let start = ballAt (float Pitch.awayGoalX - 0.5) postY 0.4 36.0 0.0 0.0 0.0 0.0 0.0
                  let after = BallDynamics.integrate cfg dt 7<subtick> start
                  Expect.isLessThan (float after.Position.Vx) 0.0 "the post sends the ball back"
                  Expect.isLessThan (float after.Position.X) (float Pitch.awayGoalX) "the ball stays in front of the line"

                  match after.FrameContact with
                  | ValueSome contact ->
                      Expect.equal contact.Part GoalFramePart.Post "post contact recorded"
                      Expect.equal contact.Goal ClubSide.Away "the away side defends the +x goal"
                      Expect.equal contact.Tick 7<subtick> "contact carries the tick"
                  | ValueNone -> failtest "no frame contact recorded"

                  Expect.isLessThan (speedOf after) (36.0 * cfg.WoodworkRestitution + 0.5) "restitution removes energy")

              testCase "a rising shot rebounds down off the crossbar" (fun _ ->
                  let barCentre = float Pitch.GoalHeight + float Pitch.PostRadius
                  let underside = barCentre - float cfg.Radius - 0.05
                  let start = ballAt (float Pitch.awayGoalX - 0.6) 0.0 underside 25.0 0.0 0.0 0.0 0.0 0.0
                  let after = run 0.05 start

                  match after.FrameContact with
                  | ValueSome contact -> Expect.equal contact.Part GoalFramePart.Crossbar "bar contact recorded"
                  | ValueNone -> failtest "no bar contact"

                  Expect.isLessThan (float after.Position.Vx) 0.0 "the bar turns the ball back")

              testCase "the net stops a scored ball inside the goal" (fun _ ->
                  let start = ballAt (float Pitch.awayGoalX + 0.3) 1.0 0.8 28.0 2.0 1.0 0.0 0.0 0.0
                  let after = run 1.5 start
                  let limit = float Pitch.awayGoalX + float Pitch.GoalDepth + 0.02
                  Expect.isLessThanOrEqual (float after.Position.X) limit "the back net holds the ball"
                  Expect.isLessThanOrEqual (abs (float after.Position.Y)) (float Pitch.GoalHalfWidth) "the side net holds it"
                  Expect.isLessThan (speedOf after) 3.0 "the net absorbs the shot")

              testCase "side netting turns away a ball arriving from outside the goal" (fun _ ->
                  let start = ballAt (float Pitch.awayGoalX + 1.0) 6.0 0.5 0.0 -12.0 0.0 0.0 0.0 0.0
                  let after = run 0.6 start
                  Expect.isGreaterThan (float after.Position.Y) (float Pitch.GoalHalfWidth) "the ball never enters through the side")

              testCase "the launch solver lands curled and straight shots on the aim point" (fun _ ->
                  let origin: Vec3 = { X = 30.0<meter>; Y = 10.0<meter>; Z = 0.0<meter> }
                  let target: Vec3 = { X = Pitch.awayGoalX; Y = -2.0<meter>; Z = 1.5<meter> }

                  for side in [ -40.0; 0.0; 40.0 ] do
                      let spin =
                          { Top = 0.0<radianPerSecond>
                            Side = side * 1.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }

                      let launch = BallFlight.solve cfg dt origin target 28.0<meterPerSecond> spin
                      let v = launch.Velocity

                      let ball =
                          { TestBuilders.emptyBall with
                              Position =
                                  { X = origin.X
                                    Y = origin.Y
                                    Z = origin.Z
                                    Vx = v.X
                                    Vy = v.Y
                                    Vz = v.Z }
                              Spin = BallPhysics.worldSpin v spin
                              Control = BallControl.Airborne }

                      match crossingAt (float Pitch.awayGoalX) ball with
                      | Some(y, z) ->
                          Expect.floatClose { absolute = 0.08; relative = 0.0 } y -2.0 $"lateral aim with side spin {side}"
                          Expect.floatClose { absolute = 0.08; relative = 0.0 } z 1.5 $"height aim with side spin {side}"
                      | None -> failtest "the solved ball never reached the goal line"

                      Expect.isGreaterThan launch.Seconds 0.5 "flight time is reported")

              testCase "a solved ground pass reaches its distance at the arrival speed and time the integrator produces" (fun _ ->
                  // Distances straddle the drag crisis: short passes roll subcritical, long ones start supercritical.
                  for distance, arrival in [ 8.0, 5.0; 20.0, 8.0; 40.0, 11.0 ] do
                      let solved = BallAero.groundPass cfg arrival distance
                      let radius = float cfg.Radius
                      let v = solved.LaunchSpeed

                      let mutable state =
                          { ballAt 0.0 0.0 0.0 v 0.0 0.0 0.0 (v / radius) 0.0 with
                              Control = BallControl.Free }

                      let mutable elapsed = 0.0

                      while float state.Position.X < distance && elapsed < 20.0 do
                          state <- BallDynamics.integrate cfg dt 0<subtick> state
                          elapsed <- elapsed + dt

                      Expect.isLessThan (abs (elapsed - solved.Seconds)) (2.0 * dt) $"{distance} m pass arrives on time"
                      Expect.isLessThan (abs (float state.Position.Vx - arrival)) 0.25 $"{distance} m pass arrives at {arrival} m/s"

                  let soft = BallAero.groundPass cfg 3.0 20.0
                  let firm = BallAero.groundPass cfg 9.0 20.0
                  Expect.isLessThan soft.LaunchSpeed firm.LaunchSpeed "a softer arrival needs a softer strike"
                  Expect.isGreaterThan soft.Seconds firm.Seconds "and takes longer to arrive")

              testCase "an unreachable target falls short instead of failing" (fun _ ->
                  let launch =
                      BallFlight.solve
                          cfg
                          dt
                          { X = 0.0<meter>; Y = 0.0<meter>; Z = 0.0<meter> }
                          { X = 60.0<meter>; Y = 0.0<meter>; Z = 1.0<meter> }
                          10.0<meterPerSecond>
                          { Top = 0.0<radianPerSecond>; Side = 0.0<radianPerSecond>; Roll = 0.0<radianPerSecond> }

                  Expect.isTrue (System.Double.IsFinite(float launch.Velocity.Z)) "the closest launch is returned"
                  Expect.floatClose Accuracy.medium (sqrt (float launch.Velocity.X ** 2.0 + float launch.Velocity.Y ** 2.0 + float launch.Velocity.Z ** 2.0)) 10.0 "speed is preserved")

              testCase "woodwork contact in the current tick emits one Woodwork event" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let tick = world.State.Tick

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { (ballAt 50.0 3.0 0.5 -8.0 0.0 0.0 0.0 0.0 0.0) with
                                  PreviousPosition = ValueSome (ballAt 50.2 3.0 0.5 -8.0 0.0 0.0 0.0 0.0 0.0).Position
                                  FrameContact =
                                      ValueSome
                                          { Tick = tick
                                            Part = GoalFramePart.Post
                                            Goal = ClubSide.Away
                                            Position = { X = 52.4<meter>; Y = 3.5<meter>; Z = 0.5<meter> } } } }

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 1UL) world buf |> ignore

                  let woodwork =
                      buf.Events
                      |> Seq.filter (fun e -> match e.Data with EventData.Woodwork(ClubSide.Away, GoalFramePart.Post, _, _, _) -> true | _ -> false)
                      |> Seq.length

                  Expect.equal woodwork 1 "one woodwork event"

                  world.State <- { world.State with Tick = tick + 1<subtick> }
                  let later = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 1UL) world later |> ignore

                  Expect.isFalse
                      (later.Events |> Seq.exists (fun e -> match e.Data with EventData.Woodwork _ -> true | _ -> false))
                      "a stale contact is not reported again")

              testCase "a header or deflection over the line in the frame is a goal and keeps flying" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let attacker = ctx.HomeLineup.Profiles.[9].Id
                  let line = float Pitch.awayGoalX
                  let previous = (ballAt (line - 0.1) 0.5 1.2 9.0 0.0 0.0 0.0 0.0 0.0).Position

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { (ballAt (line + 0.2) 0.5 1.2 9.0 0.0 0.0 0.0 0.0 0.0) with
                                  PreviousPosition = ValueSome previous
                                  Trajectory = None
                                  LastTouch = Some attacker } }

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 1UL) world buf |> ignore

                  Expect.isTrue
                      (buf.Events
                       |> Seq.exists (fun e -> match e.Data with EventData.Goal(ClubSide.Home, Some scorer, _, _) -> scorer = attacker | _ -> false))
                      "the toucher scores"

                  Expect.isGreaterThan (float world.State.Ball.Position.Vx) 0.0 "the ball carries on into the net") ]
