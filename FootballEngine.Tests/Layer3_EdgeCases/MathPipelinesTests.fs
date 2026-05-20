module FootballEngine.Tests.Layer3.MathPipelinesTests

open Expecto
open FootballEngine.MathPipelines

let tests =
    testList
        "MathPipelines"
        [ testList
              "normStat"
              [ testCase "10 maps to 0.5" <| fun () ->
                  let result = normStat 10
                  Expect.equal result 0.5 "normStat 10 should be 0.5"

                testCase "20 maps to 1.0" <| fun () ->
                  let result = normStat 20
                  Expect.equal result 1.0 "normStat 20 should be 1.0"

                testCase "1 maps to 0.05" <| fun () ->
                  let result = normStat 1
                  Expect.equal result 0.05 "normStat 1 should be 0.05" ]

          testList
              "normCondition"
              [ testCase "50 maps to 0.5" <| fun () ->
                  let result = normCondition 50
                  Expect.equal result 0.5 "normCondition 50 should be 0.5"

                testCase "100 maps to 1.0" <| fun () ->
                  let result = normCondition 100
                  Expect.equal result 1.0 "normCondition 100 should be 1.0"

                testCase "0 maps to 0.0" <| fun () ->
                  let result = normCondition 0
                  Expect.equal result 0.0 "normCondition 0 should be 0.0" ]

          testList
              "normalizeScore"
              [ testCase "score=1.0 max=2.0 yields 0.5" <| fun () ->
                  let result = normalizeScore 2.0 1.0
                  Expect.equal result 0.5 "normalizeScore 1.0/2.0 should be 0.5"

                testCase "max=0 yields 0.0" <| fun () ->
                  let result = normalizeScore 0.0 1.0
                  Expect.equal result 0.0 "normalizeScore with max=0 should be 0.0"

                testCase "score > maxPossible clamped to 1.0" <| fun () ->
                  let result = normalizeScore 2.0 5.0
                  Expect.equal result 1.0 "score exceeding max should clamp to 1.0"

                testCase "score < 0 clamped to 0.0" <| fun () ->
                  let result = normalizeScore 2.0 -1.0
                  Expect.equal result 0.0 "negative score should clamp to 0.0" ]

          testList
              "xgDistanceComponent"
              [ testCase "distance=0 yields 1.0" <| fun () ->
                  let result = xgDistanceComponent 0.033 0.0
                  Expect.equal result 1.0 "xg at distance 0 should be 1.0"

                testCase "factor=0.033 dist=30 yields ~0.37" <| fun () ->
                  let result = xgDistanceComponent 0.033 30.0
                  Expect.isLessThan (abs (result - 0.3716)) 0.01 $"xg at 30m should be ~0.37, got {result}" ]

          testList
              "applySkillGapCurve"
              [ testCase "diff=0 yields 0.5" <| fun () ->
                  let result = applySkillGapCurve 1.2 0.0
                  Expect.equal result 0.5 "skill gap at diff=0 should be 0.5"

                testCase "steepness=1.2 diff=0.3 yields ~0.59" <| fun () ->
                  let result = applySkillGapCurve 1.2 0.3
                  Expect.isLessThan (abs (result - 0.589)) 0.01 $"skill gap should be ~0.59, got {result}" ]

          testList
              "applyConditionDecay"
              [ testCase "condition=100 score=1.0 yields 1.0" <| fun () ->
                  let result = applyConditionDecay 100.0f 1.0
                  Expect.equal result 1.0 "full condition should yield full score"

                testCase "condition=0 score=1.0 yields 0.9" <| fun () ->
                  let result = applyConditionDecay 0.0f 1.0
                  Expect.equal result 0.9 "zero condition should yield 0.9 base" ]

          testList
              "applyFatigueCollapse"
              [ testCase "condition=100 above threshold yields 1.0" <| fun () ->
                  let result = applyFatigueCollapse 50 0.04 100
                  Expect.equal result 1.0 "condition above threshold should be 1.0"

                testCase "condition=0 below threshold yields exp value" <| fun () ->
                  let result = applyFatigueCollapse 50 0.04 0
                  let expected = System.Math.Exp(-(0.5 - 0.0) * 0.04)
                  Expect.equal result expected "condition=0 should match exp formula" ]

          testList
              "normStadiumCapacity"
              [ testCase "60000/80000 yields 0.75" <| fun () ->
                  let result = normStadiumCapacity 60000 80000
                  Expect.equal result 0.75 "60000/80000 should be 0.75"

                testCase "80000/80000 yields 1.0" <| fun () ->
                  let result = normStadiumCapacity 80000 80000
                  Expect.equal result 1.0 "max capacity should be 1.0" ] ]
