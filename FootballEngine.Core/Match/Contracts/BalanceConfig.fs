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
      SaveDistancePenalty: float<meter> }

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
      DecisionSoftmaxTemperature: float }

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
      CornerTargetRadius: float<meter> }

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
      ChemistryCohesionWeight: float }

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
      InjurySubCondition: float }

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
      FullTimeReviewDuration: float<second> }

type BalanceConfig =
    { Ball: BallConfig
      Movement: MovementConfig
      Actions: ActionsConfig
      SetPiece: SetPieceConfig
      Team: TeamConfig
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
          SaveDistancePenalty = 12.0<meter> }

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
          ConditionDecayPerSecond = 0.02
          RecoveryPerSecond = 0.005
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
          ShotQualityGate = 0.5
          ShotAngleSpread = 0.3
          ShotDistanceToGoalMultiplier = 0.02
          ShotVzBase = 0.4
          DuelSteepness = 8.0
          DuelMomentumBonus = 0.1
          DuelFoulChance = 0.12
          DuelFoulSeverityBias = 0.2
          CrossLateralnessMin = 0.3
          CrossTargetRadius = 12.0<meter>
          GkPunchChance = 0.15
          GkParryChance = 0.1
          GkDistributionPressureRadius = 15.0<meter>
          DribbleBaseSuccess = 0.55
          DribbleRiskWeight = 0.2
          TackleBaseSuccess = 0.5
          DecisionSoftmaxTemperature = 1.0 }

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
          CornerTargetRadius = 10.0<meter> }

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
          ChemistryCohesionWeight = 0.3 }

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
          InjurySubCondition = 0.5 }

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
          FullTimeReviewDuration = 5.0<second> }

    let defaultConfig: BalanceConfig =
        { Ball = defaultBall
          Movement = defaultMovement
          Actions = defaultActions
          SetPiece = defaultSetPiece
          Team = defaultTeam
          Referee = defaultReferee
          Manager = defaultManager
          Environment = defaultEnvironment
          Match = defaultMatch
          Weights = WeightSections.defaultWeights }
