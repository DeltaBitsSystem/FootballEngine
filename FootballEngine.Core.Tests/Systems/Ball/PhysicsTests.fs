namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PhysicsTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private stepBall (world: World) (tick: int<subtick>) : World =
        world.State <- { world.State with Tick = tick }
        BallPhysics.step sctx (Rng.ofSeed 7UL) world (buffer ()) |> ignore
        world

    let physicsTests =
        testList
            "FootballEngine.Core.Systems.Ball.PhysicsTests"
            [ testCase "Controlled ball is not integrated" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  let world = stepBall world 0<subtick>

                  match world.State.Ball.Control with
                  | BallControl.Controlled(_, pid') -> Expect.equal pid' pid "carrier unchanged"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "Free ball with velocity advances and decays" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let v0 = 10.0<meterPerSecond>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          Vx = v0 } } }

                  let world = stepBall world 0<subtick>
                  let dt = float (SimulationClock.dt sctx.Clock)

                  Expect.isGreaterThan (float world.State.Ball.Position.X) 0.0 "ball moved forward"
                  Expect.isLessThan (float world.State.Ball.Position.X) (10.0 * dt) "drag applied"
                  Expect.isLessThan (float world.State.Ball.Position.Vx) (float v0) "speed decayed")

              testCase "Below stop cutoff the ball freezes and marks stationary" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          Vx = 0.1<meterPerSecond> } } }

                  let world = stepBall world 0<subtick>

                  Expect.equal (float world.State.Ball.Position.Vx) 0.0 "vx frozen"
                  Expect.equal (float world.State.Ball.Position.Vy) 0.0 "vy frozen"
                  Expect.equal (float world.State.Ball.Position.Vz) 0.0 "vz frozen"
                  Expect.equal world.State.Ball.StationarySince (Some 0<subtick>) "stationary marked")

              testCase "Falling ball bounces" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          Z = 0.0<meter>
                                          Vz = -2.0<meterPerSecond> } } }

                  let world = stepBall world 0<subtick>

                  Expect.isGreaterThan (float world.State.Ball.Position.Vz) 0.0 "bounced upward"
                  Expect.isGreaterThan (float world.State.Ball.Position.Z) 0.0 "left the ground")

              testCase "Position is clamped to pitch margins" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { X = 1000.0<meter>
                                        Y = 0.0<meter>
                                        Z = 0.0<meter>
                                        Vx = 10.0<meterPerSecond>
                                        Vy = 0.0<meterPerSecond>
                                        Vz = 0.0<meterPerSecond> } } }

                  let world = stepBall world 0<subtick>

                  Expect.equal (float world.State.Ball.Position.X) (float Pitch.HalfLength + 5.0) "x clamped"
                  Expect.equal (float world.State.Ball.Position.Y) 0.0 "y unchanged")

              testCase "Air drag reduces speed over ticks" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          Vx = 10.0<meterPerSecond> } } }

                  let world = stepBall world 0<subtick>
                  let afterOne = float world.State.Ball.Position.Vx
                  let world = stepBall world 1<subtick>
                  let afterTwo = float world.State.Ball.Position.Vx

                  Expect.isLessThan afterTwo afterOne "decays monotonically")

              testCase "Spin decays toward zero" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Spin =
                                      { Top = 5.0<radianPerSecond>
                                        Side = -3.0<radianPerSecond> } } }

                  let world = stepBall world 0<subtick>

                  Expect.isLessThan (abs (float world.State.Ball.Spin.Top)) 5.0 "topspin decayed"
                  Expect.isLessThan (abs (float world.State.Ball.Spin.Side)) 3.0 "sidespin decayed")

              testCase "Impulse integration keeps the previous position" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let origin: Vec3 =
                      { X = 0.0<meter>
                        Y = 0.0<meter>
                        Z = 0.0<meter> }

                  let impulse =
                      BallPhysics.createImpulse
                          (PlayerId 1)
                          origin
                          { X = 10.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          None
                          StrikeKind.Pass
                          (SimTick.ofSubTicks 0<subtick>)
                          ExecutionContext.OpenPlay

                  BallPhysics.applyImpulse world impulse TrajectoryIntent.Uncontrolled None
                  let world = stepBall world 0<subtick>

                  Expect.isGreaterThan (float world.State.Ball.Position.X) 0.0 "impulse moved the ball"

                  match world.State.Ball.PreviousPosition with
                  | Some previous -> Expect.equal previous.X origin.X "integration retained the prior position"
                  | None -> failtest "expected a previous position after integration") ]
