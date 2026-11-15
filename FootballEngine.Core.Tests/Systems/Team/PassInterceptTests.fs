namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// F4 — the carrier evaluates pass feasibility with the RoboCup interceptor
/// model: a pass is only an option if the launched ball reaches the receiver
/// before the fastest opponent, plus a risk margin that grows when the pass
/// crosses the offside line diagonally.
module PassInterceptTests =

    let private testParams: InterceptTable.InterceptParams =
        { Ball = BalanceConfig.defaultConfig.Ball
          PlayerSpeedPerSecond = 8.0<meterPerSecond>
          PlayerAcceleration = 8.0<meterPerSecondSquared>
          ControlRadius = 2.5<meter>
          MaxSteps = 50
          RollTableResolution = 0.01<meterPerSecond>
          RollTableMaxSpeed = 45.0<meterPerSecond> }

    let private launchedBall (vx: float<meterPerSecond>) (vy: float<meterPerSecond>) : Spatial =
        { X = 0.0<meter>
          Y = 0.0<meter>
          Z = 0.0<meter>
          Vx = vx
          Vy = vy
          Vz = 0.0<meterPerSecond> }

    /// World where Home's receiver slot sits at (rx, ry), Away's outfielders
    /// default to far, faraway spots and the given placements overwrite them.
    let private worldWith (receiverSlot: int) (rx: float) (ry: float) (awaySlots: (int * float * float) list) : World =
        let world = TestBuilders.formationWorld ()

        for slot in 1..10 do
            world.Away.Frame.PosX.[slot] <- -40.0f
            world.Away.Frame.PosY.[slot] <- float32 (if slot % 2 = 0 then 25.0 else -25.0)

        for slot, x, y in awaySlots do
            world.Away.Frame.PosX.[slot] <- float32 x
            world.Away.Frame.PosY.[slot] <- float32 y

        world.Home.Frame.PosX.[receiverSlot] <- float32 rx
        world.Home.Frame.PosY.[receiverSlot] <- float32 ry
        world

    let private homeLine (world: World) (ctx: MatchContext) : float<meter> =
        MatchWorld.offsideLine ctx world ClubSide.Home

    let passInterceptTests =
        testList
            "FootballEngine.Core.Systems.Team.PassInterceptTests"
            [ testCase "pass with opponent arriving earlier is rejected" (fun _ ->
                  let world = worldWith 9 22.0 0.0 [ 1, 12.0, 0.0 ]
                  let ball = launchedBall 22.0<meterPerSecond> 0.0<meterPerSecond>
                  let cfg = { BaseRisk = 0; OffsideCrossRisk = 2 }
                  let ctx = TestBuilders.formationCtx ()

                  Expect.isFalse
                      (PassIntercept.feasible cfg testParams world ClubSide.Home 9 ball (homeLine world ctx))
                      "a rival on the trajectory beats the receiver")

              testCase "open lane is accepted" (fun _ ->
                  let world = worldWith 9 22.0 0.0 [ 1, 0.0, 30.0; 2, -20.0, -30.0 ]
                  let ball = launchedBall 22.0<meterPerSecond> 0.0<meterPerSecond>
                  let cfg = { BaseRisk = 0; OffsideCrossRisk = 2 }
                  let ctx = TestBuilders.formationCtx ()

                  Expect.isTrue
                      (PassIntercept.feasible cfg testParams world ClubSide.Home 9 ball (homeLine world ctx))
                      "no opponent can reach the pass in time")

              testCase "offside-crossing diagonal pass accepted only with risk bonus" (fun _ ->
                  let sq = sqrt 2.0
                  let rx = 22.0 / sq
                  let ry = 22.0 / sq
                  let rival = 21.0 / sq
                  let world = worldWith 9 rx ry [ 1, rival, rival ]
                  let v = 22.0 / sq
                  let ball = launchedBall (v * 1.0<meterPerSecond>) (v * 1.0<meterPerSecond>)
                  let ctx = TestBuilders.formationCtx ()

                  Expect.isFalse
                      (PassIntercept.feasible
                          { BaseRisk = 0; OffsideCrossRisk = 0 }
                          testParams
                          world
                          ClubSide.Home
                          9
                          ball
                          (homeLine world ctx))
                      "without the offside-cross risk the pass is not feasible"

                  Expect.isTrue
                      (PassIntercept.feasible
                          { BaseRisk = 0; OffsideCrossRisk = 2 }
                          testParams
                          world
                          ClubSide.Home
                          9
                          ball
                          (homeLine world ctx))
                      "the offside-cross risk bonus makes the diagonal pass feasible")

              testCase "feasible is deterministic" (fun _ ->
                  let world = worldWith 9 22.0 0.0 [ 1, 12.0, 0.0 ]
                  let ball = launchedBall 22.0<meterPerSecond> 0.0<meterPerSecond>
                  let cfg = { BaseRisk = 0; OffsideCrossRisk = 2 }
                  let ctx = TestBuilders.formationCtx ()

                  Expect.equal
                      (PassIntercept.feasible cfg testParams world ClubSide.Home 9 ball (homeLine world ctx))
                      (PassIntercept.feasible cfg testParams world ClubSide.Home 9 ball (homeLine world ctx))
                      "repeated calls agree")

              testCase "passArrivalStep is shorter for a closer receiver" (fun _ ->
                  let near = worldWith 9 10.0 0.0 []
                  let far = worldWith 9 30.0 0.0 []
                  let ball = launchedBall 22.0<meterPerSecond> 0.0<meterPerSecond>

                  Expect.isLessThan
                      (PassIntercept.passArrivalStep testParams near ClubSide.Home 9 ball)
                      (PassIntercept.passArrivalStep testParams far ClubSide.Home 9 ball)
                      "the closer receiver controls the ball sooner")

              testCase "interceptorStep is the min over opponent slots" (fun _ ->
                  let world = worldWith 9 22.0 0.0 [ 1, 5.0, 0.0; 2, 15.0, 0.0 ]
                  let ball = launchedBall 22.0<meterPerSecond> 0.0<meterPerSecond>
                  let stepNear = InterceptTable.stepsToReach testParams ball 5.0<meter> 0.0<meter>
                  let stepFar = InterceptTable.stepsToReach testParams ball 15.0<meter> 0.0<meter>

                  Expect.equal
                      (PassIntercept.interceptorStep testParams world ClubSide.Home ball)
                      (min stepNear stepFar)
                      "the fastest opponent slot wins over the slowest")

              testCase "integration: blocked receiver yields no pass option" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let world = TestBuilders.formationWorld ()

                  world.Home.Frame.PosX.[5] <- 0.0f
                  world.Home.Frame.PosY.[5] <- 0.0f
                  world.Home.Frame.PosX.[9] <- 6.0f
                  world.Home.Frame.PosY.[9] <- 0.0f

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 0.0<meter>
                                          Y = 0.0<meter> } } }

                  let hasStriker (opts: (float * int * float) list) =
                      opts |> List.exists (fun (_, code, tgt) -> code = 1 && tgt = 9.0)

                  world.Away.Frame.PosX.[5] <- 3.0f
                  world.Away.Frame.PosY.[5] <- 0.0f

                  Expect.isFalse
                      (hasStriker (PlayerDecision.passOptionsFor ctx world ClubSide.Home 5))
                      "a rival on the lane blocks the striker as a pass target"

                  world.Away.Frame.PosX.[5] <- 15.0f
                  world.Away.Frame.PosY.[5] <- 0.0f

                  Expect.isTrue
                      (hasStriker (PlayerDecision.passOptionsFor ctx world ClubSide.Home 5))
                      "an open striker is offered as a pass target") ]
