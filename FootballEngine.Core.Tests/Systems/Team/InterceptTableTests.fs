namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// InterceptTable (RoboCup HELIOS, adapted): exact stationary-ball geometry,
/// monotonicity under a moving ball, determinism, the unreachable horizon,
/// aggregate exclusion of GK/sidelined, and the inertia point against the
/// authoritative BallDynamics integrator.
module InterceptTableTests =

    let private testParams: InterceptTable.InterceptParams =
        { Ball = BalanceConfig.defaultConfig.Ball
          PlayerSpeedPerSecond = 8.0<meterPerSecond>
          ControlRadius = 2.5<meter>
          MaxSteps = 50 }

    let private ball
        (x: float<meter>)
        (y: float<meter>)
        (vx: float<meterPerSecond>)
        (vy: float<meterPerSecond>)
        : Spatial =
        { X = x
          Y = y
          Z = 0.0<meter>
          Vx = vx
          Vy = vy
          Vz = 0.0<meterPerSecond> }

    let interceptTableTests =
        testList
            "FootballEngine.Core.Systems.Team.InterceptTableTests"
            [ testCase "stationary ball: closer player has lower steps" (fun _ ->
                  let b = ball 10.0<meter> 0.0<meter> 0.0<meterPerSecond> 0.0<meterPerSecond>
                  let near = InterceptTable.stepsToReach testParams b 0.0<meter> 0.0<meter>
                  let far = InterceptTable.stepsToReach testParams b 30.0<meter> 0.0<meter>
                  Expect.isLessThan near far "the closer player reaches first"
                  // ceil((10 - 2.5) / (8 * 0.025)) = ceil(37.5) = 38
                  Expect.equal near 38 "stationary ball closed-form step is exact")

              testCase "moving ball: a player ahead of a fast ball gets a low step" (fun _ ->
                  let b = ball 0.0<meter> 0.0<meter> 10.0<meterPerSecond> 0.0<meterPerSecond>
                  let ahead = InterceptTable.stepsToReach testParams b 15.0<meter> 0.0<meter>
                  let behind = InterceptTable.stepsToReach testParams b -5.0<meter> 0.0<meter>
                  Expect.isLessThan ahead 38 "ahead of a fast ball the interception is fast"
                  Expect.isGreaterThanOrEqual behind 38 "a ball running away takes the whole horizon"
                  Expect.isLessThan ahead behind "the ahead player beats the chaser")

              testCase "deterministic: same inputs yield identical steps" (fun _ ->
                  let b = ball 5.0<meter> 3.0<meter> 4.0<meterPerSecond> -2.0<meterPerSecond>
                  let a = InterceptTable.stepsToReach testParams b 0.0<meter> 0.0<meter>
                  let c = InterceptTable.stepsToReach testParams b 0.0<meter> 0.0<meter>
                  Expect.equal a c "identical inputs produce identical steps")

              testCase "unreachable returns MaxSteps" (fun _ ->
                  let b = ball 200.0<meter> 0.0<meter> 0.0<meterPerSecond> 0.0<meterPerSecond>
                  let steps = InterceptTable.stepsToReach testParams b 0.0<meter> 0.0<meter>
                  Expect.equal steps testParams.MaxSteps "a far stationary ball is unreachable within the horizon")

              testCase "compute aggregates exclude GK and sidelined" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  let before = InterceptTable.compute testParams world ClubSide.Home
                  Expect.isGreaterThanOrEqual before.SelfStep 0 "self step is a valid non-negative step"

                  // GK at slot 0 moved far away must not change SelfStep.
                  world.Home.Frame.PosX.[0] <- -200.0f
                  world.Home.Frame.PosY.[0] <- 0.0f
                  let afterGk = InterceptTable.compute testParams world ClubSide.Home
                  Expect.equal afterGk.SelfStep before.SelfStep "the goalkeeper never contributes to SelfStep"

                  // Sidelining a non-best outfielder must not change SelfStep either.
                  world.Home.Frame.Occupancy.[1] <-
                      OccupancyKind.Sidelined
                          { Id = PlayerId 999
                            Reason = PlayerOutReason.RedCard }

                  let afterSidelined = InterceptTable.compute testParams world ClubSide.Home
                  Expect.equal afterSidelined.SelfStep before.SelfStep "sidelined players never contribute to SelfStep"

                  Expect.equal
                      afterSidelined.BallStep
                      (min afterSidelined.SelfStep (min afterSidelined.TeammateStep afterSidelined.OpponentStep))
                      "BallStep is the min of self, teammate and opponent")

              testCase "ballInertiaPoint matches the authoritative rolling integration" (fun _ ->
                  let cfg = testParams.Ball
                  let vx, vy = 14.0, -5.0
                  let radius = float cfg.Radius
                  // Pure rolling spin removes slip, so the integrator applies only turf resistance and air drag.
                  let mutable state =
                      { TestBuilders.emptyBall with
                          Position = ball 2.0<meter> -1.0<meter> (vx * 1.0<meterPerSecond>) (vy * 1.0<meterPerSecond>)
                          Spin =
                            { X = -vy / radius * 1.0<radianPerSecond>
                              Y = vx / radius * 1.0<radianPerSecond>
                              Z = 0.0<radianPerSecond> } }

                  for _ in 1..20 do
                      state <- BallDynamics.integrate cfg 0.025 0<subtick> state

                  let predicted = InterceptTable.ballInertiaPoint testParams (ball 2.0<meter> -1.0<meter> (vx * 1.0<meterPerSecond>) (vy * 1.0<meterPerSecond>)) 20
                  let close (a: float) (c: float) = abs (a - c) < 1e-6
                  Expect.isTrue (close (float predicted.X) (float state.Position.X)) "X matches BallDynamics"
                  Expect.isTrue (close (float predicted.Y) (float state.Position.Y)) "Y matches BallDynamics"
                  Expect.isTrue (close (float predicted.Vx) (float state.Position.Vx)) "speed decays as BallDynamics"
                  Expect.equal predicted.Z 0.0<meter> "Z is zero") ]
