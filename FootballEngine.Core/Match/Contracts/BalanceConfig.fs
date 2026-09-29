namespace FootballEngine.Core.Match

/// Ball behaviour — physics, trajectory, contact, reception. One record so ball
/// tuning never leaks into agent code. (was PhysicsConfig + Interception + part of Pass)
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

/// Movement and fatigue — speeds, acceleration, separation, smoothing, decay and
/// recovery, scorer thresholds. (was Movement parts + scorer thresholds)
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
      IntentHysteresis: float
      CommittedRunDuration: int<subtick> }

/// Action outcomes — pass, shot, duel, cross, GK and dribble success curves,
/// tunable without touching agent logic. (was PassConfig, ShotConfig, DuelConfig,
/// CrossConfig, DribbleConfig, GKConfig, DecisionConfig + IndividualWeights)
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

/// Dead-ball pacing and geometry: restart delays, wall setup, corner/free-kick targets.
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
      CornerFarZoneDist: float<meter>
      ContactReadyRadius: float<meter>
      TargetTolerance: float<meter>
      MaxTargetDistance: float<meter> }

/// The collective mind — utility weights, emergent loops, blackboard windows,
/// shape. (was CollectiveWeights + UtilityWeights + MatchSituation + Influence +
/// TeamDirector/Reactive/WinProb weights)
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
      PatternWingBias: float
      TacticalFamiliarityGain: float
      LooseBallCoverDistance: float<meter>
      RestDefenseDistance: float<meter>
      WeakSideSeparation: float<meter> }

/// Referee behaviour — foul severity, cards, VAR, advantage, home-card bias.
/// (was FoulAnalysis consts + RefereeWeights + HomeAdvantage card parts)
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

/// Manager behaviour — substitution windows and thresholds. (was ManagerConfig +
/// TimingConfig parts)
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

/// Match flow — stoppage, pause durations, match importance. (was
/// MatchVolumeConfig + TimingConfig parts)
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

/// Action chain planner — controls chain depth, evaluation budget, and plan TTL.
type PlannerConfig =
    { MaxChainLength: int
      MaxEvaluateLimit: int
      PlanTTL: int<subtick> }

// ScoutConfig/CounterConfig live in ScoutingConfig.fs: Domain/Scouting.fs must
// compile before Domain/Club.fs (Club carries ScoutingMemory), and this file
// cannot — it needs Domain.TransferConfig from Domain/Manager.fs.

/// AI transfer discipline lives in Domain (SquadPlanner reads it): the
/// bench-warmer invariant and value tolerance.
type TransferConfig = FootballEngine.Domain.TransferConfig

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
      LateralSwapThreshold: float<meter>
      TargetInset: float<meter>
      CarrySearchRadius: float<meter>
      SupportForwardDistance: float<meter>
      SupportLateralDistance: float<meter>
      GkMaxAdvance: float<meter>
      GkThreatDistance: float<meter> }

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

/// Weekly training: growth curves, plan load profiles, batteries, readiness.
/// The whole triangle (develop / freshness / risk) tunes from here.
[<Struct>]
type TrainingConfig =
    { // Age curve: growth multiplier while potential gap remains
      AgeMultU18: float
      AgeMultU21: float
      AgeMultU25: float
      AgeMultU28: float
      // Veterans decline instead of growing
      DeclineStartAge: int
      DeclineChance: float
      // Weekly chance that a targeted attribute moves +1 (scaled by age mult and gap)
      DevelopChance: float
      // Chance for off-target attributes (keeps squads rounded, never spiky)
      TrickleChance: float
      // Perform plan keeps a small maintenance chance
      MaintainChance: float
      // Extra chance when the attribute group matches the team emphasis tilt
      TiltBonus: float
      // Tired players barely learn: below this condition, growth scales down
      TiredConditionThreshold: int
      TiredGrowthPenalty: float
      // Plans: condition/fitness deltas applied weekly
      DevelopConditionCost: int
      DevelopFitnessGain: int
      DevelopInjuryAdd: float
      PerformConditionGain: int
      PerformFitnessGain: int
      RestConditionGain: int
      RestFitnessLoss: int
      RehabConditionGain: int
      // Injuries: base risk while training + scaling when exhausted
      BaseInjuryRisk: float
      TiredInjuryScale: float
      // Readiness: how condition/fitness scale engine physicals in Projection
      ReadinessConditionWeight: float
      ReadinessFloor: float
      // Post-match: playing costs condition, builds sharpness; idleness dulls it
      PostMatchConditionCost: int
      PostMatchFitnessGain: int
      IdleFitnessLoss: int
      // Recovery emphasis: extra condition for the whole squad that week
      RecoveryConditionGain: int
      // Coaching staff: best rating per area blends into growth (1.0 = neutral at 10)
      CoachGrowthInfluence: float
      YouthCoachBonus: float
      // Medical staff: injury risk shaved per Physiotherapy point
      PhysioInjuryReduction: float }

/// Tactics UX — deterministic trade-off weights for each instruction option.
/// Every value here is surfaced in the Tactic UI; no hidden behaviour numbers.
[<Struct>]
type TacticalTradeoffConfig =
    { // Defensive line
      DefensiveLineHighXgBehind: float
      DefensiveLineHighSprintCost: float
      DefensiveLineDeepXgCentral: float
      DefensiveLineDeepCompactness: float
      // Pressing
      PressHighPpdaDelta: float
      PressHighSprintDistance: float
      PressHighConditionCost: float
      PressHighInjuryRisk: float
      PressLowPossessionLoss: float
      // Width
      WidthWideCentralOverloadLoss: float
      WidthWideWingExposure: float
      WidthNarrowWingOverload: float
      WidthNarrowCentralCompactness: float
      // Tempo
      TempoFastPossessionLoss: float
      TempoFastTransitionXg: float
      TempoFastPassCompletionLoss: float
      TempoSlowPossessionGain: float
      TempoSlowPassCompletionGain: float
      // Directness
      DirectnessDirectPassCompletionLoss: float
      DirectnessDirectSecondBallXg: float
      DirectnessShortPassCompletionGain: float
      // Mentality
      MentalityAttackingXgDelta: float
      MentalityAttackingXgConcededDelta: float
      MentalityDefensiveXgConcededDelta: float
      // Grade thresholds
      GradeAThreshold: float
      GradeBThreshold: float
      GradeCThreshold: float
      GradeDThreshold: float
      // Live hooks: these scale live decisions, not just analysis text
      TempoCounterUrgencyShift: float
      DirectnessLongBallUtility: float
      MentalityRiskThresholdShift: float }

/// The single tuning surface: every behavior-affecting number in the engine
/// lives here, nowhere else.
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
      Planner: PlannerConfig
      Scout: ScoutConfig
      Counter: CounterConfig
      Transfer: TransferConfig
      World: FootballEngine.Domain.WorldConfig
      TacticalTradeoffs: TacticalTradeoffConfig
      Training: TrainingConfig
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
          IntentHysteresis = 0.05
          CommittedRunDuration = 240<subtick> }

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
          CornerFarZoneDist = 12.0<meter>
          ContactReadyRadius = 1.0<meter>
          TargetTolerance = 1.0<meter>
          MaxTargetDistance = 70.0<meter> }

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
          PatternWingBias = 0.25
          TacticalFamiliarityGain = 0.15
          LooseBallCoverDistance = 7.0<meter>
          RestDefenseDistance = 18.0<meter>
          WeakSideSeparation = 10.0<meter> }

    let defaultPositioning: PositioningConfig =
        { EvalTeammateWeight = 0.3
          EvalOpponentWeight = 0.5
          EvalGoalWeight = 0.4
          EvalSearchRadius = 4.0<meter>
          EvalCandidateCount = 5
          EvalMinTeammateDist = 5.0<meter>
          EvalOppOurWeight = 0.25
          LateralSwapThreshold = 1.5<meter>
          TargetInset = 2.0<meter>
          CarrySearchRadius = 7.0<meter>
          SupportForwardDistance = 4.0<meter>
          SupportLateralDistance = 8.0<meter>
          GkMaxAdvance = 10.0<meter>
          GkThreatDistance = 24.0<meter> }

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

    let defaultPlanner: PlannerConfig =
        { MaxChainLength = 4
          MaxEvaluateLimit = 500
          PlanTTL = 480<subtick> }

    let defaultScout: ScoutConfig =
        { MemoryHalfLifeDays = 30.0
          MinConfidenceToReport = 0.3
          AiReportNoise = 0.0
          NoiseByAbility = 0.25
          RivalPruneAfterDays = 200.0
          ConfidenceMatches = 3.0
          ShortPassMaxMeters = 15.0<meter>
          LongPassMinMeters = 30.0<meter>
          NearShotMaxMeters = 16.5<meter>
          AreaShotMaxMeters = 25.0<meter> }

    let defaultCounter: CounterConfig =
        { MaxSideBias = 1.0
          BiasScale = 1.5
          MaxPressBoost = 0.5
          MaxLineDrop = 10.0
          MaxWidthTighten = 0.5
          MaxTempoShift = 0.5
          MaxDirectnessShift = 0.5
          SignatureChangePenalty = 0.6 }

    let defaultTransfer: TransferConfig =
        { BenchWarmerAgeMin = 25
          MinMatchesForSquadPlayer = 10
          MaxOver25BenchWarmers = 2
          OverpayTolerance = 0.25m }

    let defaultTacticalTradeoffs: TacticalTradeoffConfig =
        { DefensiveLineHighXgBehind = 0.18
          DefensiveLineHighSprintCost = 0.12
          DefensiveLineDeepXgCentral = 0.11
          DefensiveLineDeepCompactness = 0.15
          PressHighPpdaDelta = -1.8
          PressHighSprintDistance = 0.14
          PressHighConditionCost = 4.0
          PressHighInjuryRisk = 0.021
          PressLowPossessionLoss = 0.08
          WidthWideCentralOverloadLoss = 0.09
          WidthWideWingExposure = 0.11
          WidthNarrowWingOverload = 0.10
          WidthNarrowCentralCompactness = 0.12
          TempoFastPossessionLoss = 0.06
          TempoFastTransitionXg = 0.09
          TempoFastPassCompletionLoss = 0.06
          TempoSlowPossessionGain = 0.07
          TempoSlowPassCompletionGain = 0.05
          DirectnessDirectPassCompletionLoss = 0.08
          DirectnessDirectSecondBallXg = 0.07
          DirectnessShortPassCompletionGain = 0.06
          MentalityAttackingXgDelta = 0.09
          MentalityAttackingXgConcededDelta = 0.07
          MentalityDefensiveXgConcededDelta = 0.11
          GradeAThreshold = 0.75
          GradeBThreshold = 0.50
          GradeCThreshold = 0.25
          GradeDThreshold = 0.0
          TempoCounterUrgencyShift = 0.3
          DirectnessLongBallUtility = 0.12
          MentalityRiskThresholdShift = 0.08 }

    let defaultTraining: TrainingConfig =
        { AgeMultU18 = 1.5
          AgeMultU21 = 1.2
          AgeMultU25 = 1.0
          AgeMultU28 = 0.5
          DeclineStartAge = 31
          DeclineChance = 0.08
          DevelopChance = 0.22
          TrickleChance = 0.02
          MaintainChance = 0.05
          TiltBonus = 0.10
          TiredConditionThreshold = 55
          TiredGrowthPenalty = 0.5
          DevelopConditionCost = 8
          DevelopFitnessGain = 4
          DevelopInjuryAdd = 0.008
          PerformConditionGain = 4
          PerformFitnessGain = 6
          RestConditionGain = 18
          RestFitnessLoss = 3
          RehabConditionGain = 6
          BaseInjuryRisk = 0.004
          TiredInjuryScale = 0.03
          ReadinessConditionWeight = 0.5
          ReadinessFloor = 0.82
          PostMatchConditionCost = 12
          PostMatchFitnessGain = 8
          IdleFitnessLoss = 2
          RecoveryConditionGain = 10
          CoachGrowthInfluence = 0.6
          YouthCoachBonus = 0.5
          PhysioInjuryReduction = 0.0015 }

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
          Planner = defaultPlanner
          Scout = defaultScout
          Counter = defaultCounter
          Transfer = defaultTransfer
          World = FootballEngine.Domain.WorldConfig.defaults
          TacticalTradeoffs = defaultTacticalTradeoffs
          Training = defaultTraining
          Weights = WeightSections.defaultWeights }
