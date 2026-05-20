module FootballEngine.Tests.Program

open Expecto
open Expecto.Logging
open FootballEngine.Tests.EngineTests
open FootballEngine.Tests.WorldTests
open FootballEngine.Tests.TrainingSystemTests
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners
open FootballEngine.Tests.Infrastructure.Assertions
open FootballEngine.Tests.Layer1.FlowTransitionTests
open FootballEngine.Tests.Layer1.GoalCycleTests
open FootballEngine.Tests.Layer1.GKDistributionTests
open FootballEngine.Tests.Layer1.SetPieceFlowTests
open FootballEngine.Tests.Layer1.MatchInvariantTests
open FootballEngine.Tests.Layer2.ApplyOutputContractTests
open FootballEngine.Tests.Layer2.BallSystemTests
open FootballEngine.Tests.Layer2.ChemistryTests
open FootballEngine.Tests.Layer2.EmergentLoopsTests
open FootballEngine.Tests.Layer2.AdaptiveTacticsTests
open FootballEngine.Tests.Layer2.VARTests
open FootballEngine.Tests.Layer2.HandballDetectorTests
open FootballEngine.Tests.Layer2.StatisticalContractsTests
open FootballEngine.Tests.Layer3.ShotEdgeCaseTests
open FootballEngine.Tests.Layer3.GKEdgeCaseTests
open FootballEngine.Tests.Layer3.PassEdgeCaseTests
open FootballEngine.Tests.Layer3.PlayerPersonalityTests
open FootballEngine.Tests.Layer3.MathPipelinesTests

[<EntryPoint>]
let main argv =

    let matchEngineTests =
        testList
            "MatchEngine"
            [ flowTransitionTests
              goalCycleTests
              gkDistributionTests
              setPieceFlowTests
              matchInvariantTests
              applyOutputContractTests
              ballSystemTests
              chemistryTests
              emergentLoopsTests
              adaptiveTacticsTests
              varTests
              handballDetectorTests
              statisticalContractsTests |> testSequenced
              shotEdgeCaseTests
              gkEdgeCaseTests
              passEdgeCaseTests
              playerPersonalityTests
              tests ]

    let engineTests =
        testList "Engine" [ batchTests; doubleSimGuardTests; standingUpdateTests; fixtureIntegrityTests ]

    let worldTests =
        testList
            "World"
            [ gameStateIntegrityTests
              playerDataTests
              seasonProgressTests
              lineupTests
              isSeasonOverTests ]

    let trainingTests = testList "Training" [ trainingSystemTests ]

    let all =
        testList "FootballEngine" [ matchEngineTests; engineTests; worldTests; trainingTests ]

    runTestsWithCLIArgs [ Verbosity LogLevel.Verbose ] argv all
