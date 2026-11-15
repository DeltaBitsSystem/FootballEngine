namespace FootballEngine.Core.Tests.Kernels

open System
open Expecto
open FootballEngine.Core.Match

/// The rolled-ball race with its exclusion and interleaved first arrival against `stepsToReach`, the plain
/// per-body scan the exclusion must reproduce exactly.
module RaceTests =
    let private ballConfig = BalanceConfig.defaultConfig.Ball

    let private paramsOf speed maxSteps : InterceptTable.InterceptParams =
        { Ball = ballConfig
          PlayerSpeedPerSecond = speed * 1.0<meterPerSecond>
          PlayerAcceleration = 8.0<meterPerSecondSquared>
          ControlRadius = 2.5<meter>
          MaxSteps = maxSteps
          // Exact equality with the stepped scan needs the stepped roll; the table has its own bound tests.
          RollTableResolution = 0.01<meterPerSecond>
          RollTableMaxSpeed = 0.0<meterPerSecond> }

    let private spatial x y vx vy : Spatial =
        { X = x * 1.0<meter>
          Y = y * 1.0<meter>
          Z = 0.0<meter>
          Vx = vx * 1.0<meterPerSecond>
          Vy = vy * 1.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

    let private scan p ball (x: float, y: float) =
        InterceptTable.stepsToReach p ball (x * 1.0<meter>) (y * 1.0<meter>)

    let private reachAt (p: InterceptTable.InterceptParams) n =
        float p.ControlRadius + InterceptTable.reachableDistance p (float n * float InterceptTable.dt)

    /// The exclusion rule with a configurable slack, to show the shipped slack is what keeps it exact.
    let private excludedWith (slack: float) (p: InterceptTable.InterceptParams) (ball: Spatial) (x: float, y: float) =
        let speed = sqrt (float ball.Vx * float ball.Vx + float ball.Vy * float ball.Vy)
        let dirX, dirY = if speed > 0.0 then float ball.Vx / speed, float ball.Vy / speed else 0.0, 0.0
        let length = speed * float InterceptTable.dt * float p.MaxSteps
        let along = (x - float ball.X) * dirX + (y - float ball.Y) * dirY
        let t = max 0.0 (min length along)
        let dx, dy = x - (float ball.X + dirX * t), y - (float ball.Y + dirY * t)
        sqrt (dx * dx + dy * dy) > reachAt p p.MaxSteps + slack

    let private randomBall (rng: Random) =
        let angle = rng.NextDouble() * 2.0 * Math.PI
        let speed = if rng.Next 6 = 0 then 0.0 else rng.NextDouble() * 35.0
        spatial (rng.NextDouble() * 80.0 - 40.0) (rng.NextDouble() * 50.0 - 25.0) (speed * cos angle) (speed * sin angle)

    let private randomBodies (rng: Random) count =
        Array.init count (fun _ -> rng.NextDouble() * 100.0 - 50.0, rng.NextDouble() * 70.0 - 35.0)

    /// Bodies exactly on and just either side of the reach circle around a roll point, in every direction.
    let private boundaryBodies (rng: Random) (p: InterceptTable.InterceptParams) (ball: Spatial) =
        [| for _ in 1..40 do
               let step = rng.Next(0, p.MaxSteps + 1)
               let point = InterceptTable.ballInertiaPoint p ball step
               let angle = rng.NextDouble() * 2.0 * Math.PI

               for offset in [ -0.05; -1e-9; 0.0; 1e-9; 0.05 ] do
                   let radius = reachAt p step + offset
                   yield float point.X + radius * cos angle, float point.Y + radius * sin angle |]

    let private paramSets =
        [ paramsOf 8.0 50; paramsOf 8.0 80; paramsOf 1.0 50; paramsOf 0.0 50 ]

    let private limits (p: InterceptTable.InterceptParams) =
        [ -1; 0; 1; 10; p.MaxSteps - 1; p.MaxSteps; p.MaxSteps + 5 ]

    let tests =
        testList
            "FootballEngine.Core.Kernels.RaceTests"
            [ testCase "StepsFor, ReachesWithin and FirstArrival equal the full scan on random and boundary bodies" (fun _ ->
                  let rng = Random 20261115
                  let mutable bodiesChecked = 0

                  for p in paramSets do
                      for _ in 1..120 do
                          let ball = randomBall rng
                          let bodies = Array.append (randomBodies rng 22) (boundaryBodies rng p ball)
                          let rolled = InterceptTable.RolledBalls.Rent(p, ball)
                          let expected = bodies |> Array.map (scan p ball)

                          bodies
                          |> Array.iteri (fun i (x, y) ->
                              let steps = rolled.StepsFor(x * 1.0<meter>, y * 1.0<meter>)
                              Expect.equal steps expected[i] "StepsFor is the full scan"

                              for limit in limits p do
                                  Expect.equal
                                      (rolled.ReachesWithin(x * 1.0<meter>, y * 1.0<meter>, limit))
                                      (expected[i] <= limit)
                                      "ReachesWithin is StepsFor <= limit"

                              bodiesChecked <- bodiesChecked + 1)

                          let xs = bodies |> Array.map fst
                          let ys = bodies |> Array.map snd

                          for count in [ 0; 1; 5; 22; bodies.Length ] do
                              for limit in limits p do
                                  let reference = Array.fold min limit expected[.. count - 1]
                                  Expect.equal (rolled.FirstArrival(xs, ys, count, limit)) reference "FirstArrival is min(limit, min StepsFor)"

                          InterceptTable.RolledBalls.Return rolled

                  Expect.isGreaterThan bodiesChecked 20000 "enough bodies were compared")
              testCase "a body reaching on the last step before the horizon is never excluded" (fun _ ->
                  // A slow body makes the per-step reach gap (0.025 m) smaller than the 0.1 m error injected below.
                  let p = paramsOf 1.0 50
                  let ball = spatial 0.0 0.0 20.0 0.0
                  let point = InterceptTable.ballInertiaPoint p ball (p.MaxSteps - 1)
                  let body = float point.X, float point.Y + reachAt p (p.MaxSteps - 1) - 0.001
                  Expect.isLessThan (scan p ball body) p.MaxSteps "the body does reach before the horizon"
                  Expect.isFalse (excludedWith 1e-6 p ball body) "the shipped rule keeps it")
              testCase "the exclusion slack is sensitive: a 0.1 m error and a missing slack both break exactness" (fun _ ->
                  let rng = Random 77
                  let wrongBy (slack: float) (p: InterceptTable.InterceptParams) =
                      let mutable wrong = 0

                      for _ in 1..400 do
                          let ball = randomBall rng

                          for body in boundaryBodies rng p ball do
                              if excludedWith slack p ball body && scan p ball body < p.MaxSteps then
                                  wrong <- wrong + 1

                      wrong

                  Expect.equal (wrongBy 1e-6 (paramsOf 1.0 50)) 0 "the shipped slack never excludes a reaching body"
                  Expect.equal (wrongBy 1e-6 (paramsOf 0.0 50)) 0 "nor with a body that never outruns its reach"
                  Expect.isGreaterThan (wrongBy -0.1 (paramsOf 1.0 50)) 0 "a 0.1 m error excludes reaching bodies"
                  // A body that never outruns its reach sits on one circle around the ball's start: the distance to the
                  // segment then differs from the circle radius only by rounding, which only the slack absorbs.
                  let still = paramsOf 0.0 50
                  let mutable wrong = 0

                  for _ in 1..20000 do
                      let angle = rng.NextDouble() * 2.0 * Math.PI
                      let speed = 1.0 + rng.NextDouble() * 30.0
                      let ball = spatial 0.0 0.0 (speed * cos angle) (speed * sin angle)
                      let radius = float still.ControlRadius * (1.0 - float (rng.Next 4) * 1e-16)
                      let body = -radius * sin angle, radius * cos angle

                      if excludedWith 0.0 still ball body && scan still ball body < still.MaxSteps then
                          wrong <- wrong + 1

                  Expect.isGreaterThan wrong 0 "no slack excludes bodies on the circle") ]
