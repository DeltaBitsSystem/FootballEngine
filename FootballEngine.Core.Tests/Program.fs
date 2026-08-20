module FootballEngine.Core.Tests.Program

open Expecto
open FootballEngine.Core.Tests.Contracts.RngTests
open FootballEngine.Core.Tests.Contracts.ClockTests
open FootballEngine.Core.Tests.Contracts.EventFlagTests
open FootballEngine.Core.Tests.Contracts.ConfigTests
open FootballEngine.Core.Tests.Contracts.ConfigMigrationTests
open FootballEngine.Core.Tests.Systems.Ball.PhysicsTests
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
open FootballEngine.Core.Tests.Systems.Players.ChainIntegrationTests
open FootballEngine.Core.Tests.Systems.Players.ChainLifetimeTests
open FootballEngine.Core.Tests.Systems.Players.PlannerBudgetTests
open FootballEngine.Core.Tests.Systems.Players.Actions.PassTests
open FootballEngine.Core.Tests.Systems.Players.Actions.SetPieceTests
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

[<EntryPoint>]
let main argv =
    let tests =
        testList
            "FootballEngine.Core"
            [ rngTests
              clockTests
              eventFlagTests
              configTests
              configMigrationTests
              physicsTests
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
              chainIntegrationTests
              chainLifetimeTests
              plannerBudgetTests
              passTests
              setPieceTests
              shootoutTests
              formationTests
              fieldEvaluatorTests
              lateralPairsTests
              interceptTableTests
              interceptPressingTests
              positionTests
              positioningTests
              decideTests
              passInterceptTests
              emergentTests
              offsideLineTests
              managerTests
              eventApplyTests
              restartContractTests
              statsTests
              scoutingTests
              squadPlannerTests
              scoutingWorldTests ]

    runTestsWithCLIArgs [] argv tests
