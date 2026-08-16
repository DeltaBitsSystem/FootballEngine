namespace FootballEngine.Core.Match

/// Physics constants, trajectory, contact, reception. (was PhysicsConfig + Interception + part of Pass)
[<Struct>]
type BallConfig =
    { Gravity: float<meterPerSecondSquared>
      AirDrag: float
      GroundFriction: float
      BounceRestitution: float
      BounceFriction: float
      StopSpeedCutoff: float<meterPerSecond>
      MaxShotSpeed: float<meterPerSecond>
      MagnusFactor: float
      SpinDecay: float
      ArrivalGateFactor: float
      ReceptionGrace: int<subtick>
      ContestSpeedGap: float<meterPerSecond>
      InterceptionBonus: float
      SaveReflexWeight: float
      SavePositioningWeight: float
      SaveDistancePenalty: float<meter>
      ContactInterceptorMargin: float<meter>
      ContactReceiverQualityBonus: float
      ReceptionBase: float
      ReceptionSpacePenalty: float
      ShotSaveBaseProbability: float
      ShotGoalXGMultiplier: float
      ShotGoalGKDivisor: float
      InterceptionPaceWeight: float
      InterceptionIntentBoost: float }

/// Speeds, acceleration, separation, fatigue decay, smoothing, scorer thresholds. (was Movement parts + scorer thresholds)
[<Struct>]
type MovementConfig =
    { SprintSpeed: float<meterPerSecond>
      JogSpeed: float<meterPerSecond>
      WalkSpeed: float<meterPerSecond>
      Acceleration: float<meterPerSecondSquared>
      SeparationRadius: float<meter>
      SeparationStrength: float
      TurningRate: float<radianPerSecond>
      Smoothing: float
      FatigueSpeedDecay: float
      ConditionDecayPerSecond: float
      RecoveryPerSecond: float
      ReactiveRadius: float<meter>
      ConditionImpactThreshold: float
      IntentFloor: float
      IntentHysteresis: float }

/// Pass/shot/duel/cross/GK/dribble knobs. (was PassConfig, ShotConfig, DuelConfig, CrossConfig, DribbleConfig, GKConfig, DecisionConfig + IndividualWeights)
[<Struct>]
type ActionsConfig =
    { PassBaseQuality: float
      PassDistancePenaltyPerMeter: float
      PassLongThreshold: float<meter>
      PassVisionWeight: float
      PassTechnicalWeight: float
      PassJitterBase: float
      PassHeavyTouchChance: float
      PassLaunchSpeed: float<meterPerSecond>
      ShotQualityGate: float
      ShotAngleSpread: float
      ShotDistanceToGoalMultiplier: float
      ShotVzBase: float
      DuelSteepness: float
      DuelMomentumBonus: float
      DuelFoulChance: float
      DuelFoulSeverityBias: float
      CrossLateralnessMin: float
      CrossTargetRadius: float<meter>
      GkPunchChance: float
      GkParryChance: float
      GkDistributionPressureRadius: float<meter>
      DribbleBaseSuccess: float
      DribbleRiskWeight: float
      TackleBaseSuccess: float
      DecisionSoftmaxTemperature: float
      ContestFoulChance: float
      PerceptionConeAngle: float
      PerceptionConeRange: float<meter>
      LaneClearBlockRadius: float<meter>
      CognitiveFatigueSteepness: float
      CognitiveFatigueFloor: float
      MemoryPassFailWeight: float
      MemoryDuelWeight: float
      MemoryMentalDecay: float }

[<Struct>]
type SetPieceConfig =
    { KickoffDelay: float<second>
      ThrowInDelay: float<second>
      CornerDelay: float<second>
      GoalKickDelay: float<second>
      FreeKickDelay: float<second>
      PenaltyDelay: float<second>
      WallDistance: float<meter>
      WallPlayers: int
      PenaltyQualityBonus: float
      FreeKickShotChance: float
      CornerTargetRadius: float<meter>
      DirectFreeKickRange: float<meter>
      WallArcSpread: float<meter>
      CornerNearZoneDist: float<meter>
      CornerFarZoneDist: float<meter> }

/// Utility weights, emergent loops, blackboard windows, shape. (was CollectiveWeights + UtilityWeights + MatchSituation + Influence + TeamDirector/Reactive/WinProb weights)
[<Struct>]
type TeamConfig =
    { UtilityAttackingWeight: float
      UtilityDefendingWeight: float
      UtilityPressWeight: float
      UtilityCounterWeight: float
      UtilityBalancedWeight: float
      DirectiveHysteresis: float
      OpponentShapeWindow: int<subtick>
      OpponentPressureRadius: float<meter>
      UrgencyMaxDeficit: int
      MomentumStreakHalfLife: int<subtick>
      TransitionWindow: int<subtick>
      ShapeBaseLine: float<meter>
      ShapeBallPull: float
      ShapeCompactShift: float<meter>
      ShapeWidthSpread: float<meter>
      EmergentCompactnessStep: float
      EmergentPressingStep: float
      EmergentWingplayStep: float
      ChemistryCohesionWeight: float
      RoleBiasStrength: float
      RolePressRadius: float<meter>
      RoleAnchorRadius: float<meter>
      WinProbGoalDiffWeight: float
      WinProbXgDiffWeight: float
      WinProbMomentumWeight: float
      WinProbMinuteWeight: float
      RiskLateMinute: int<matchMin>
      PatternWindow: int<subtick>
      PatternCenterBias: float
      PatternWingBias: float }

/// Foul severity, cards, VAR, advantage, home-card. (was FoulAnalysis consts + RefereeWeights + HomeAdvantage card parts)
[<Struct>]
type RefereeConfig =
    { FoulMinorChance: float
      FoulTacticalChance: float
      FoulYellowWorthyChance: float
      FoulRedWorthyChance: float
      SecondYellowSuspension: bool
      VarGoalReviewDelay: float<second>
      VarOffsideMargin: float<meter>
      AdvantageWindow: int<subtick>
      HomeCardBonus: float
      CardAccumulationThreshold: int
      LastManFactor: float
      PromisingAttackFactor: float
      ZoneFactor: float }

/// Sub windows and thresholds. (was ManagerConfig + TimingConfig parts)
[<Struct>]
type ManagerConfig =
    { MaxSubs: int
      FirstSubWindowMin: int<matchMin>
      SecondSubWindowMin: int<matchMin>
      ThirdSubWindowMin: int<matchMin>
      ConditionSubThreshold: float
      AttackingSubGoalDeficit: int
      InjurySubCondition: float
      RedCardCompact: bool
      DrawAttackingMinute: int<matchMin>
      WinningDefensiveMinute: int<matchMin> }

/// Home advantage, crowd, weather hooks. (was HomeAdvantageConfig)
[<Struct>]
type EnvironmentConfig =
    { HomeAdvantageStrength: float
      CrowdInfluence: float
      WeatherEffect: float
      PitchQuality: float }

/// Volume, stoppage, match importance. (was MatchVolumeConfig + TimingConfig parts)
[<Struct>]
type MatchConfig =
    { MatchImportanceWeight: float
      StoppageBasePerHalf: float<second>
      StoppagePerGoal: float<second>
      StoppagePerCard: float<second>
      StoppagePerSub: float<second>
      StoppagePerFoul: float<second>
      StoppageMax: float<second>
      HalfTimeDuration: float<second>
      GoalPauseDuration: float<second>
      VarReviewDuration: float<second>
      InjuryPauseDuration: float<second>
      FullTimeReviewDuration: float<second>
      ExtraTimePeriodDuration: float<second>
      ShootoutRounds: int }

/// Collective positioning (HELIOS formation + Voronoi evaluator + lateral pairs).
[<Struct>]
type PositioningConfig =
    { EvalTeammateWeight: float
      EvalOpponentWeight: float
      EvalGoalWeight: float
      EvalSearchRadius: float<meter>
      EvalCandidateCount: int
      EvalMinTeammateDist: float<meter>
      EvalOppOurWeight: float
      LateralSwapThreshold: float<meter> }

/// RoboCup InterceptTable horizon: control radius and step budget.
[<Struct>]
type InterceptConfig =
    { ControlRadius: float<meter>
      MaxSteps: int }

/// Gliders-style pressing: the step allowance per role before chasing a loose
/// ball, and the horizon beyond which nobody chases.
[<Struct>]
type PressConfig =
    { BaseAllowance: int
      MidfieldAllowance: int
      FullBackAllowance: int
      MaxPressStep: int }

/// HELIOS updateSituation: the intercept-step lead that commits the team to a
/// phase.
[<Struct>]
type SituationConfig = { LeadSteps: int }

/// F4 — pass feasibility risk: the step margin the ball needs over the fastest
/// interceptor, plus a bonus when the pass crosses the offside line diagonally.
[<Struct>]
type PassInterceptConfig =
    { BaseRisk: int; OffsideCrossRisk: int }

type BalanceConfig =
    { Ball: BallConfig
      Movement: MovementConfig
      Actions: ActionsConfig
      SetPiece: SetPieceConfig
      Team: TeamConfig
      Positioning: PositioningConfig
      Intercept: InterceptConfig
      Press: PressConfig
      Situation: SituationConfig
      PassIntercept: PassInterceptConfig
      Referee: RefereeConfig
      Manager: ManagerConfig
      Environment: EnvironmentConfig
      Match: MatchConfig
      Weights: WeightSections }

module BalanceConfig =

    let defaultBall: BallConfig =
        { Gravity = 9.81<meterPerSecondSquared>
          AirDrag = 0.999
          GroundFriction = 0.985
          BounceRestitution = 0.6
          BounceFriction = 0.7
          StopSpeedCutoff = 0.5<meterPerSecond>
          MaxShotSpeed = 32.0<meterPerSecond>
          MagnusFactor = 0.15
          SpinDecay = 0.99
          ArrivalGateFactor = 1.0
          ReceptionGrace = 6<subtick>
          ContestSpeedGap = 8.0<meterPerSecond>
          InterceptionBonus = 0.1
          SaveReflexWeight = 0.5
          SavePositioningWeight = 0.3
          SaveDistancePenalty = 12.0<meter>
          ContactInterceptorMargin = 2.2<meter>
          ContactReceiverQualityBonus = 4.0
          ReceptionBase = 0.5
          ReceptionSpacePenalty = 0.04
          ShotSaveBaseProbability = 0.45
          ShotGoalXGMultiplier = 1.1
          ShotGoalGKDivisor = 600.0
          InterceptionPaceWeight = 0.12
          InterceptionIntentBoost = 1.25 }

    let defaultMovement: MovementConfig =
        { SprintSpeed = 8.5<meterPerSecond>
          JogSpeed = 5.0<meterPerSecond>
          WalkSpeed = 1.5<meterPerSecond>
          Acceleration = 4.0<meterPerSecondSquared>
          SeparationRadius = 1.8<meter>
          SeparationStrength = 0.5
          TurningRate = 3.0<radianPerSecond>
          Smoothing = 0.8
          FatigueSpeedDecay = 0.4
          ConditionDecayPerSecond = 0.00006
          RecoveryPerSecond = 0.01
          ReactiveRadius = 3.0<meter>
          ConditionImpactThreshold = 0.1
          IntentFloor = 0.2
          IntentHysteresis = 0.05 }

    let defaultActions: ActionsConfig =
        { PassBaseQuality = 0.7
          PassDistancePenaltyPerMeter = 0.004
          PassLongThreshold = 25.0<meter>
          PassVisionWeight = 0.3
          PassTechnicalWeight = 0.3
          PassJitterBase = 0.2
          PassHeavyTouchChance = 0.05
          PassLaunchSpeed = 22.0<meterPerSecond>
          ShotQualityGate = 0.5
          ShotAngleSpread = 0.3
          ShotDistanceToGoalMultiplier = 0.02
          ShotVzBase = 0.4
          DuelSteepness = 8.0
          DuelMomentumBonus = 0.1
          DuelFoulChance = 0.15
          DuelFoulSeverityBias = 0.2
          CrossLateralnessMin = 0.3
          CrossTargetRadius = 12.0<meter>
          GkPunchChance = 0.15
          GkParryChance = 0.1
          GkDistributionPressureRadius = 15.0<meter>
          DribbleBaseSuccess = 0.55
          DribbleRiskWeight = 0.2
          TackleBaseSuccess = 0.5
          DecisionSoftmaxTemperature = 1.0
          ContestFoulChance = 0.05
          PerceptionConeAngle = 0.6
          PerceptionConeRange = 35.0<meter>
          LaneClearBlockRadius = 2.5<meter>
          CognitiveFatigueSteepness = 3.0
          CognitiveFatigueFloor = 0.5
          MemoryPassFailWeight = 0.25
          MemoryDuelWeight = 0.2
          MemoryMentalDecay = 0.999 }

    let defaultSetPiece: SetPieceConfig =
        { KickoffDelay = 2.0<second>
          ThrowInDelay = 3.0<second>
          CornerDelay = 3.0<second>
          GoalKickDelay = 4.0<second>
          FreeKickDelay = 3.0<second>
          PenaltyDelay = 4.0<second>
          WallDistance = 9.15<meter>
          WallPlayers = 3
          PenaltyQualityBonus = 0.3
          FreeKickShotChance = 0.4
          CornerTargetRadius = 10.0<meter>
          DirectFreeKickRange = 32.0<meter>
          WallArcSpread = 1.5<meter>
          CornerNearZoneDist = 6.0<meter>
          CornerFarZoneDist = 12.0<meter> }

    let defaultTeam: TeamConfig =
        { UtilityAttackingWeight = 1.0
          UtilityDefendingWeight = 1.0
          UtilityPressWeight = 1.0
          UtilityCounterWeight = 1.0
          UtilityBalancedWeight = 1.0
          DirectiveHysteresis = 0.08
          OpponentShapeWindow = 120<subtick>
          OpponentPressureRadius = 8.0<meter>
          UrgencyMaxDeficit = 2
          MomentumStreakHalfLife = 240<subtick>
          TransitionWindow = 60<subtick>
          ShapeBaseLine = 42.0<meter>
          ShapeBallPull = 0.3
          ShapeCompactShift = 4.0<meter>
          ShapeWidthSpread = 6.0<meter>
          EmergentCompactnessStep = 0.02
          EmergentPressingStep = 0.02
          EmergentWingplayStep = 0.02
          ChemistryCohesionWeight = 0.3
          RoleBiasStrength = 0.3
          RolePressRadius = 8.0<meter>
          RoleAnchorRadius = 35.0<meter>
          WinProbGoalDiffWeight = 0.4
          WinProbXgDiffWeight = 0.15
          WinProbMomentumWeight = 0.1
          WinProbMinuteWeight = 0.005
          RiskLateMinute = 75<matchMin>
          PatternWindow = 600<subtick>
          PatternCenterBias = 0.15
          PatternWingBias = 0.25 }

    let defaultPositioning: PositioningConfig =
        { EvalTeammateWeight = 0.3
          EvalOpponentWeight = 0.5
          EvalGoalWeight = 0.4
          EvalSearchRadius = 4.0<meter>
          EvalCandidateCount = 5
          EvalMinTeammateDist = 5.0<meter>
          EvalOppOurWeight = 0.25
          LateralSwapThreshold = 1.5<meter> }

    let defaultIntercept: InterceptConfig =
        { ControlRadius = 2.5<meter>
          MaxSteps = 50 }

    let defaultPress: PressConfig =
        { BaseAllowance = 13
          MidfieldAllowance = 7
          FullBackAllowance = 23
          MaxPressStep = 30 }

    let defaultSituation: SituationConfig = { LeadSteps = 2 }

    let defaultPassIntercept: PassInterceptConfig =
        { BaseRisk = 0; OffsideCrossRisk = 2 }

    let defaultReferee: RefereeConfig =
        { FoulMinorChance = 0.55
          FoulTacticalChance = 0.25
          FoulYellowWorthyChance = 0.15
          FoulRedWorthyChance = 0.05
          SecondYellowSuspension = true
          VarGoalReviewDelay = 15.0<second>
          VarOffsideMargin = 0.1<meter>
          AdvantageWindow = 80<subtick>
          HomeCardBonus = 0.05
          CardAccumulationThreshold = 4
          LastManFactor = 0.3
          PromisingAttackFactor = 0.2
          ZoneFactor = 0.2 }

    let defaultManager: ManagerConfig =
        { MaxSubs = 5
          FirstSubWindowMin = 45<matchMin>
          SecondSubWindowMin = 60<matchMin>
          ThirdSubWindowMin = 75<matchMin>
          ConditionSubThreshold = 0.35
          AttackingSubGoalDeficit = 1
          InjurySubCondition = 0.5
          RedCardCompact = true
          DrawAttackingMinute = 70<matchMin>
          WinningDefensiveMinute = 80<matchMin> }

    let defaultEnvironment: EnvironmentConfig =
        { HomeAdvantageStrength = 0.1
          CrowdInfluence = 0.3
          WeatherEffect = 0.0
          PitchQuality = 1.0 }

    let defaultMatch: MatchConfig =
        { MatchImportanceWeight = 1.0
          StoppageBasePerHalf = 60.0<second>
          StoppagePerGoal = 30.0<second>
          StoppagePerCard = 15.0<second>
          StoppagePerSub = 10.0<second>
          StoppagePerFoul = 3.0<second>
          StoppageMax = 300.0<second>
          HalfTimeDuration = 900.0<second>
          GoalPauseDuration = 25.0<second>
          VarReviewDuration = 15.0<second>
          InjuryPauseDuration = 60.0<second>
          FullTimeReviewDuration = 5.0<second>
          ExtraTimePeriodDuration = 900.0<second>
          ShootoutRounds = 5 }

    let defaultConfig: BalanceConfig =
        { Ball = defaultBall
          Movement = defaultMovement
          Actions = defaultActions
          SetPiece = defaultSetPiece
          Team = defaultTeam
          Positioning = defaultPositioning
          Intercept = defaultIntercept
          Press = defaultPress
          Situation = defaultSituation
          PassIntercept = defaultPassIntercept
          Referee = defaultReferee
          Manager = defaultManager
          Environment = defaultEnvironment
          Match = defaultMatch
          Weights = WeightSections.defaultWeights }
