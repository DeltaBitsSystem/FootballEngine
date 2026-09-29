namespace FootballEngine.Core.Tests.Systems.Players

open System
open Expecto
open FootballEngine.Core.Match

module PlannerEvaluatorTests =

    let private weights: WeightSectionsPlanner =
        { ProgressionWeight = 0.6
          ShootValueWeight = 0.35
          ShootFinishingAssumption = 75.0
          DangerBase = [| 20.0; 10.0; 5.0; 3.0; 2.0; 1.0; 0.0 |]
          DangerZoneWeights = [| 1.0; 0.5; 0.25 |]
          PossessionSecurityWeight = 0.8 }

    let private state (bx: float) (ours: (int * float * float) list) (opps: (int * float * float) list) : ChainState =
        { BallX = bx * 1.0<meter>
          BallY = 0.0<meter>
          CarrierSlot = 5
          Side = ClubSide.Home
          OurPos =
            ours
            |> List.toArray
            |> Array.map (fun (s, x, y) -> struct (s, x * 1.0<meter>, y * 1.0<meter>))
          OppPos =
            opps
            |> List.toArray
            |> Array.map (fun (s, x, y) -> struct (s, x * 1.0<meter>, y * 1.0<meter>))
          OffsideLine = 45.0<meter>
          Dir = 1.0 }

    let private farOpps =
        [ 1, -40.0, 25.0
          2, -40.0, -25.0
          3, -40.0, 10.0
          4, -40.0, -10.0
          5, -40.0, 0.0 ]

    let plannerEvaluatorTests =
        testList
            "FootballEngine.Core.Systems.Planner.PlannerEvaluatorTests"
            [ testCase "value grows as ball advances toward the goal" (fun _ ->
                  let v10 = Planner.value weights (state 10.0 [ 5, 0.0, 0.0 ] farOpps)
                  let v25 = Planner.value weights (state 25.0 [ 5, 25.0, 0.0 ] farOpps)
                  let v40 = Planner.value weights (state 40.0 [ 5, 40.0, 0.0 ] farOpps)
                  Expect.isLessThan v10 v25 "v10 < v25"
                  Expect.isLessThan v25 v40 "v25 < v40")

              testCase "value drops when opponents cluster near the ball" (fun _ ->
                  let nearOpps = [ 1, 2.0, 0.0; 2, -1.0, 1.5; 3, -1.0, -1.5; 4, 0.0, 0.0 ]
                  let vFar = Planner.value weights (state 15.0 [ 5, 15.0, 0.0 ] farOpps)
                  let vNear = Planner.value weights (state 15.0 [ 5, 15.0, 0.0 ] nearOpps)
                  Expect.isGreaterThan vFar vNear "far opponents yield higher value")

              testCase "shoot bonus raises value for a state within shooting range" (fun _ ->
                  let vShoot = Planner.value weights (state 30.0 [ 5, 30.0, 0.0 ] farOpps)
                  let vNoShot = Planner.value weights (state 10.0 [ 5, 10.0, 0.0 ] farOpps)
                  Expect.isGreaterThan vShoot vNoShot "shooting position has higher value")

              testCase "value is deterministic" (fun _ ->
                  let a = Planner.value weights (state 20.0 [ 5, 20.0, 0.0 ] farOpps)
                  let b = Planner.value weights (state 20.0 [ 5, 20.0, 0.0 ] farOpps)
                  Expect.equal a b "same state yields same value")

              testCase "value is finite for on-pitch states" (fun _ ->
                  let states =
                      [ state 0.0 [ 5, 0.0, 0.0 ] farOpps
                        state 25.0 [ 5, 25.0, 0.0 ] farOpps
                        state 50.0 [ 5, 50.0, 0.0 ] farOpps ]

                  for s in states do
                      let v = Planner.value weights s
                      Expect.isFalse (Double.IsNaN v) "value must not be NaN"
                      Expect.isFalse (Double.IsInfinity v) "value must not be Infinity") ]
