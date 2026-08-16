namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DecisionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private memWith (passFail: float) (duelSuccess: float) (duelFail: float) (shot: float) : PlayerMemory =
        { LastTouchTick = 0<subtick>
          SuccessfulDuels = 0
          FailedDuels = 0
          PassesMade = 0
          PassesCompleted = 0
          ShotsTaken = 0
          Yellows = 0
          CriticalFlagged = false
          PassFailMod = passFail
          PassSuccessMod = 0.0
          DuelFailMod = duelFail
          DuelSuccessMod = duelSuccess
          ShotSuccessMod = shot
          StreakMod = 0.0
          LastMemoryDecayTick = 0<subtick> }

    let decisionTests =
        testList
            "FootballEngine.Core.Systems.Players.DecisionTests"
            [ testCase "failing pass streak raises the pass penalty" (fun _ ->
                  let clean = PlayerDecision.passMemoryPenalty ctx (memWith 0.0 0.0 0.0 0.0)
                  let failing = PlayerDecision.passMemoryPenalty ctx (memWith 1.4 0.0 0.0 0.0)

                  Expect.equal clean 0.0 "no streak -> no penalty"
                  Expect.isGreaterThan failing clean "a failing streak makes passes less attractive")

              testCase "duel success memory raises dribble appetite" (fun _ ->
                  let cold = PlayerDecision.duelMemoryBias ctx (memWith 0.0 0.0 1.2 0.0)
                  let hot = PlayerDecision.duelMemoryBias ctx (memWith 0.0 1.5 0.0 0.0)

                  Expect.isGreaterThan hot cold "duel confidence boosts dribbling")

              testCase "shot failures cool the shooter" (fun _ ->
                  let cold = PlayerDecision.shotMemoryBias ctx (memWith 0.0 0.0 0.0 -1.5)
                  let neutral = PlayerDecision.shotMemoryBias ctx (memWith 0.0 0.0 0.0 0.0)

                  Expect.equal neutral 0.0 "neutral shot memory has no bias"
                  Expect.isLessThan cold neutral "shot failures reduce shot utility")

              testCase "effective temperature drops when cognitively tired" (fun _ ->
                  let fresh = PlayerDecision.effectiveTemperature ctx.Config 1.0
                  let tired = PlayerDecision.effectiveTemperature ctx.Config 0.2

                  Expect.isLessThan tired fresh "fatigue freezes the softmax into conservative choices") ]
