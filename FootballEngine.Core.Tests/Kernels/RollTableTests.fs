namespace FootballEngine.Core.Tests.Kernels

open System
open Expecto
open FootballEngine.Core.Match

/// The tabulated 1D roll against the stepped roll it replaces: a bound in smooth cells, the exact stepped roll in
/// cells where a node's ball stops inside the horizon.
module RollTableTests =
    let private config = BalanceConfig.defaultConfig

    let private p =
        InterceptTable.parameters config.Intercept config.Movement config.Ball

    let private table = InterceptTable.RollTables.For p
    let private resolution = float p.RollTableResolution
    let private dt = float InterceptTable.dt

    /// Independent restatement of the stepped 1D roll: decelerate, then move.
    let private exactRoll (v0: float) : float[] =
        let s = Array.zeroCreate (p.MaxSteps + 1)
        let mutable v = v0

        for n in 1 .. p.MaxSteps do
            if v > 1e-9 then
                let loss =
                    (config.Ball.RollingResistance * float config.Ball.Gravity
                     + BallAero.dragDeceleration config.Ball v)
                    * dt

                v <- max 0.0 (v - loss)

            s[n] <- s[n - 1] + v * dt

        s

    let private worstError (cell: int) (v0: float) =
        let reference = exactRoll v0
        Seq.max (seq { for n in 0 .. p.MaxSteps -> abs (table.At(cell, v0, n) - reference[n]) })

    let private midpoints = [| for j in 0..4498 -> j, (float j + 0.5) * resolution |]

    let private randomSpeeds =
        let rng = Random 20261115

        [| for _ in 1..20000 ->
               (if rng.Next 4 = 0 then
                    rng.NextDouble() * 3.0
                else
                    rng.NextDouble() * 44.9) |]

    let private spatial x y vx vy : Spatial =
        { X = x * 1.0<meter>
          Y = y * 1.0<meter>
          Z = 0.0<meter>
          Vx = vx * 1.0<meterPerSecond>
          Vy = vy * 1.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

    let tests =
        testList
            "FootballEngine.Core.Kernels.RollTableTests"
            [ testCase
                  "smooth cells agree with the stepped roll within 1e-6 m at midpoints and random launch speeds"
                  (fun _ ->
                      let mutable served = 0

                      for v0 in Array.append (Array.map snd midpoints) randomSpeeds do
                          let cell = table.Cell v0

                          if cell >= 0 then
                              served <- served + 1
                              Expect.isLessThan (worstError cell v0) 1e-6 $"table error at v0 = {v0}"
                          else
                              // Only balls that stop inside the horizon may fall back; anything faster must hit the table.
                              Expect.isLessThan v0 2.0 $"a launch at {v0} m/s fell back to stepping"

                      // A quarter of the random launches are slow on purpose, so about 22 000 of 24 499 are served.
                      Expect.isGreaterThan served 21000 "the table serves every launch that keeps rolling")
              testCase
                  "cells with a stopping node are not served and are the cells interpolation would get wrong"
                  (fun _ ->
                      let unserved = midpoints |> Array.filter (fun (_, v0) -> table.Cell v0 < 0)
                      Expect.isGreaterThan unserved.Length 0 "slow cells exist"
                      Expect.isLessThan unserved.Length 300 "only almost-stopped balls fall back"
                      Expect.isTrue (unserved |> Array.forall (fun (_, v0) -> v0 < 2.0)) "only slow launches fall back"

                      // Sensitivity: without the kink rule the bound is broken (6e-5 m measured against 1e-6 m).
                      let worstWithoutRule =
                          unserved |> Array.map (fun (j, v0) -> worstError j v0) |> Array.max

                      Expect.isGreaterThan
                          worstWithoutRule
                          1e-5
                          "interpolating across a stopping node exceeds the bound")
              testCase "launches at rest, above the table and at the last node are never served" (fun _ ->
                  Expect.equal (table.Cell 0.0) -1 "at rest"
                  Expect.equal (table.Cell 45.0) -1 "at the cap"
                  Expect.equal (table.Cell 80.0) -1 "above the cap"
                  Expect.equal (table.Cell -3.0) -1 "negative")
              testCase "a disabled table serves nothing" (fun _ ->
                  let off =
                      InterceptTable.RollTables.For
                          { p with
                              RollTableMaxSpeed = 0.0<meterPerSecond> }

                  Expect.equal (off.Cell 20.0) -1 "no cell")
              testCase
                  "the race through RolledBall equals the exact scan: random bodies at every speed, boundary bodies where the roll is stepped"
                  (fun _ ->
                      let rng = Random 4242

                      let reachAt n =
                          float p.ControlRadius + InterceptTable.reachableDistance p (float n * dt)

                      let mutable compared = 0

                      for round in 1..600 do
                          let slow = round % 3 = 0

                          let speed =
                              if slow then
                                  0.05 + rng.NextDouble() * 1.1
                              else
                                  rng.NextDouble() * 40.0

                          let angle = rng.NextDouble() * 2.0 * Math.PI
                          let ball = spatial 0.0 0.0 (speed * cos angle) (speed * sin angle)
                          let rolled = InterceptTable.RolledBalls.Rent(p, ball)

                          let bodies =
                              [| for _ in 1..30 -> rng.NextDouble() * 60.0 - 30.0, rng.NextDouble() * 40.0 - 20.0
                                 if slow then
                                     for _ in 1..10 do
                                         let step = rng.Next(0, p.MaxSteps + 1)
                                         let point = InterceptTable.ballInertiaPoint p ball step
                                         let a = rng.NextDouble() * 2.0 * Math.PI

                                         for offset in [ -1e-9; 1e-9 ] do
                                             let radius = reachAt step + offset
                                             yield float point.X + radius * cos a, float point.Y + radius * sin a |]

                          for (x, y) in bodies do
                              let expected = InterceptTable.stepsToReach p ball (x * 1.0<meter>) (y * 1.0<meter>)

                              Expect.equal
                                  (rolled.StepsFor(x * 1.0<meter>, y * 1.0<meter>))
                                  expected
                                  "StepsFor is the exact scan"

                              compared <- compared + 1

                          InterceptTable.RolledBalls.Return rolled

                      Expect.isGreaterThan compared 20000 "enough bodies were compared") ]
