module FootballEngine.Core.Tests.Program

open Expecto
open FootballEngine.Core.Tests.Diagnostics.Gate0Tests
open FootballEngine.Core.Tests.Contracts.RngTests
open FootballEngine.Core.Tests.Contracts.ClockTests
open FootballEngine.Core.Tests.Contracts.EventFlagTests
open FootballEngine.Core.Tests.Contracts.ConfigTests
open FootballEngine.Core.Tests.Systems.Ball.PhysicsTests
open FootballEngine.Core.Tests.Systems.Ball.Gate1ImpulseTests
open FootballEngine.Core.Tests.Systems.Ball.ContactTests
open FootballEngine.Core.Tests.Systems.Ball.OutcomeTests
open FootballEngine.Core.Tests.Systems.Ball.PossessionTests
open FootballEngine.Core.Tests.Stepper.SchedulerTests
open FootballEngine.Core.Tests.Stepper.FlowTests
open FootballEngine.Core.Tests.Stepper.MatchClockTests
open FootballEngine.Core.Tests.Stepper.StepperTests
open FootballEngine.Core.Tests.Stepper.DeterminismTests
open FootballEngine.Core.Tests.Stepper.MatchFactoryTests
open FootballEngine.Core.Tests.Stepper.ChainSequenceTests
open FootballEngine.Core.Tests.Systems.Referee.FoulsTests
open FootballEngine.Core.Tests.Systems.Referee.VARTests
open FootballEngine.Core.Tests.Systems.Referee.CardsTests
open FootballEngine.Core.Tests.Systems.Ball.GoalDetectionTests
open FootballEngine.Core.Tests.Systems.Players.PerceptionTests
open FootballEngine.Core.Tests.Systems.Players.DecisionTests
open FootballEngine.Core.Tests.Systems.Players.PlannerTests
open FootballEngine.Core.Tests.Systems.Players.PlannerEvaluatorTests
open FootballEngine.Core.Tests.Systems.Players.SteeringTests
open FootballEngine.Core.Tests.Systems.Players.ChainIntegrationTests
open FootballEngine.Core.Tests.Systems.Players.ChainLifetimeTests
open FootballEngine.Core.Tests.Systems.Players.PlannerBudgetTests
open FootballEngine.Core.Tests.Systems.Players.Actions.PassTests
open FootballEngine.Core.Tests.Systems.Players.Actions.ShootoutTests
open FootballEngine.Core.Tests.Systems.Team.FormationTests
open FootballEngine.Core.Tests.Systems.Team.FieldEvaluatorTests
open FootballEngine.Core.Tests.Systems.Team.LateralPairsTests
open FootballEngine.Core.Tests.Systems.Team.InterceptTableTests
open FootballEngine.Core.Tests.Systems.Team.InterceptPressingTests
open FootballEngine.Core.Tests.Systems.Team.PositionTests
open FootballEngine.Core.Tests.Systems.Team.PositioningTests
open FootballEngine.Core.Tests.Systems.Team.DecideTests
open FootballEngine.Core.Tests.Systems.Team.PassInterceptTests
open FootballEngine.Core.Tests.Systems.Team.EmergentTests
open FootballEngine.Core.Tests.Systems.Team.OffsideLineTests
open FootballEngine.Core.Tests.Systems.Manager.ManagerTests
open FootballEngine.Core.Tests.Stepper.EventApplyTests
open FootballEngine.Core.Tests.Stepper.RestartContractTests
open FootballEngine.Core.Tests.Contracts.StatsTests
open FootballEngine.Core.Tests.Contracts.ScoutingTests
open FootballEngine.Core.Tests.Contracts.SquadPlannerTests
open FootballEngine.Core.Tests.Contracts.ScoutingWorldTests
open FootballEngine.Core.Tests.Contracts.TrainingTests
open FootballEngine.Core.Tests.Contracts.TacticsTests
open FootballEngine.Core.Tests.Contracts.BoundaryTests
open FootballEngine.Core.Tests.Contracts.AutosaveQueueTests
open FootballEngine.Core.Tests.Contracts.FinanceTests
open FootballEngine.Core.Tests.Contracts.MatchSessionTests
open FootballEngine.Core.Tests.Contracts.LivePresentationTests
open FootballEngine.Core.Tests.Contracts.DataLockTests
open FootballEngine.Core.Tests.GameData.ModContractTests
open FootballEngine.Core.Tests.GameData.ModResolutionTests
open FootballEngine.Core.Tests.GameData.ModPackageIoTests
open FootballEngine.Core.Tests.World.WorldKernelTests
open FootballEngine.Core.Tests.World.TransferMarketTests
open FootballEngine.Core.Tests.World.CompetitionTests
open FootballEngine.Core.Tests.World.C07LongRunTests
open FootballEngine.Core.Tests.World.WorldGenerationTests

[<EntryPoint>]
let main argv =
    let tests =
        testList
            "FootballEngine.Core"
            [ FootballEngine.Core.Tests.Diagnostics.Gate0Tests.tests
              rngTests
              FootballEngine.Core.Tests.Contracts.MotionEvidenceTests.tests
              FootballEngine.Core.Tests.Contracts.ObservationTests.tests
              FootballEngine.Core.Tests.Contracts.ClockTests.clockTests
              eventFlagTests
              configTests
              physicsTests
              FootballEngine.Core.Tests.Systems.Ball.FootMotionTests.tests
              gate1ImpulseTests
              contactTests
              outcomeTests
              possessionTests
              schedulerTests
              flowTests
              matchClockTests
              stepperTests
              determinismTests
              matchFactoryTests
              chainSequenceTests
              foulsTests
              varTests
              cardsTests
              goalDetectionTests
              perceptionTests
              decisionTests
              plannerTests
              plannerEvaluatorTests
              steeringTests
              chainIntegrationTests
              chainLifetimeTests
              plannerBudgetTests
              FootballEngine.Core.Tests.Systems.Players.DuelContactTests.tests
              FootballEngine.Core.Tests.Systems.Players.ReactionCoherenceTests.tests
              passTests
              shootoutTests
              formationTests
              fieldEvaluatorTests
              lateralPairsTests
              interceptTableTests
              interceptPressingTests
              positionTests
              FootballEngine.Core.Tests.Systems.Team.DefensiveTransitionTests.tests
              FootballEngine.Core.Tests.Systems.Team.CollectiveAttackTests.tests
              FootballEngine.Core.Tests.Systems.Players.PlannerTests.temporalTests
              positioningTests
              decideTests
              passInterceptTests
              emergentTests
              offsideLineTests
              FootballEngine.Core.Tests.Systems.Team.TacticalStaffAnalysisTests.tests
              managerTests
              eventApplyTests
              restartContractTests
              statsTests
              scoutingTests
              squadPlannerTests
              scoutingWorldTests
              trainingTests
              tacticalPlanTests
              tacticsTests
              FootballEngine.Core.Tests.Contracts.BoundaryTests.tests
              FootballEngine.Core.Tests.Contracts.AutosaveQueueTests.tests
              FootballEngine.Core.Tests.Contracts.FinanceTests.tests
              FootballEngine.Core.Tests.Contracts.MatchSessionTests.tests
              FootballEngine.Core.Tests.Contracts.LivePresentationTests.tests
              dataLockTests
              FootballEngine.Core.Tests.GameData.ModContractTests.tests
              FootballEngine.Core.Tests.GameData.ModResolutionTests.modResolutionTests
              FootballEngine.Core.Tests.GameData.ModPackageIoTests.tests
              worldKernelTests
              FootballEngine.Core.Tests.World.TransferMarketTests.tests
              competitionTests
              c07Tests
              worldGenerationTests
              FootballEngine.Core.Tests.World.Pulse.WorldPulseTests.tests
              FootballEngine.Core.Tests.World.Pulse.MatchStoriesTests.tests
              FootballEngine.Core.Tests.World.Pulse.PressureTests.tests
              FootballEngine.Core.Tests.World.Pulse.PlayerStoriesTests.tests
              FootballEngine.Core.Tests.World.Pulse.MarketStoriesTests.tests
              FootballEngine.Core.Tests.World.Pulse.SeasonCeremonyTests.tests ]

    runTestsWithCLIArgs [] argv tests
