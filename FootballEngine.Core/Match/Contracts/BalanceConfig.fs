namespace FootballEngine.Core.Match

/// Ball behaviour — physics, trajectory, contact, reception. One record so ball
/// tuning never leaks into agent code. (was PhysicsConfig + Interception + part of Pass)
[<Struct>]
type BallConfig =
    { Gravity: float<meterPerSecondSquared>
      AirDrag: float
      Radius: float<meter>
      InertiaRatio: float
      SlipFriction: float
      RollingResistance: float
      FootReach: float<meter>
      FootContactHeight: float<meter>
      KickWindup: int<subtick>
      KickRecovery: int<subtick>
      CarryTouchInterval: int<subtick>
      CarryLeadTime: float<second>
      CarryOffset: float<meter>
      CarryMaxSpeed: float<meterPerSecond>
      CarryTechniqueError: float
      PassRollingSpinRatio: float
      ReceptionVelocityRetention: float
      BounceRestitution: float
      BounceFriction: float
      StopSpeedCutoff: float<meterPerSecond>
      MaxShotSpeed: float<meterPerSecond>
      MagnusFactor: float
      SpinDecay: float
      ReceptionGrace: int<subtick>
      InterceptionBonus: float
      SaveReflexWeight: float
      SavePositioningWeight: float
      ShootoutSaveDistancePenalty: float<meter>
      ReceptionBase: float
      ReceptionSpacePenalty: float
      ShootoutSaveBaseProbability: float
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
      TurningAcceleration: float<radianPerSecondSquared>
      FacingIntentDistance: float<meter>
      StandingHeight: float<meter>
      JumpSpeed: float<meterPerSecond>
      AerialIntentRadius: float<meter>
      Smoothing: float
      FatigueSpeedDecay: float
      ConditionDecayPerSecond: float
      RecoveryPerSecond: float
      ReactiveRadius: float<meter>
      ConditionImpactThreshold: float
      IntentFloor: float
      IntentHysteresis: float
      CommittedRunDuration: int<subtick> }

/// Fixed-step collision, balance and recovery controls. Geometry fallbacks are
/// explicit so missing player morphology never silently changes contact reach.
[<Struct>]
type ContactConfig =
    { BodyRadiusFallback: float<meter>
      BodyRadiusScale: float
      CapsuleLowerHeightRatio: float
      CapsuleUpperHeightRatio: float
      HeadRadius: float<meter>
      LegRadius: float<meter>
      FootRadius: float<meter>
      HandRadius: float<meter>
      FallbackMass: float<kilogram>
      BallMass: float<kilogram>
      BodyRestitution: float
      BodyFriction: float
      BallBodyRestitution: float
      BallFootRestitution: float
      BallBodyFriction: float
      BallControlSpeed: float<meterPerSecond>
      MaximumTrapSpeed: float<meterPerSecond>
      TrapTangentialAbsorption: float
      SupportedInverseMassScale: float
      AirborneInverseMassScale: float
      StabilityRecoveryPerSecond: float
      StaggerImpulseThreshold: float
      FallImpulseThreshold: float
      RecoverStabilityThreshold: float
      StaggerAccelerationScale: float
      FallingAccelerationScale: float
      PositionCorrectionPercent: float
      MaximumPositionCorrection: float<meter>
      VelocitySlop: float<meterPerSecond>
      ToiTolerance: float
      SimultaneousTolerance: float
      SolverIterations: int
      MaximumImpactIterations: int
      CurvedPathSegments: int
      MaximumPathError: float<meter> }

/// Temporal commitments are separate from the solver's laws and from team
/// movement controls, so agents can change intent without changing physics.
[<Struct>]
type ContactActionConfig =
    { ContainPreparation: int<subtick>
      ShieldPreparation: int<subtick>
      ShoulderPreparation: int<subtick>
      StandingTacklePreparation: int<subtick>
      SlidingTacklePreparation: int<subtick>
      TrapPreparation: int<subtick>
      StrikePreparation: int<subtick>
      HeaderPreparation: int<subtick>
      CommittedDuration: int<subtick>
      RecoveryDuration: int<subtick>
      FootReach: float<meter>
      HeadReach: float<meter>
      MaximumReorientation: float<radian>
      ExecutionErrorScale: float<meter>
      MaximumExecutionError: float<meter>
      ShieldOffset: float<meter>
      NewImpactThreshold: float<meterPerSecond>
      CarelessRelativeSpeed: float<meterPerSecond>
      RecklessRelativeSpeed: float<meterPerSecond>
      ExcessiveRelativeSpeed: float<meterPerSecond>
      DangerousBallFirstSpeed: float<meterPerSecond> }

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
      PassDistanceSpeedGain: float
      MaxPassExecutionDistance: float<meter>
      ShotQualityGate: float
      ShotAngleSpread: float
      ShotDistanceToGoalMultiplier: float
      ShotVzBase: float
      DuelSteepness: float
      DuelMomentumBonus: float
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
type DefensiveTransitionConfig =
    { ReevaluateTicks: int<tickDelta>
      Commitment: int<subtick>
      MaxPressDuration: int<subtick>
      StabilizationHold: int<subtick>
      BaseArrivalWindow: float<second>
      PressArrivalExtension: float<second>
      LocalArrivalWindow: float<second>
      CoverArrivalWindow: float<second>
      HandoffAdvantage: float<second>
      MinimumPressIntensity: float
      MinimumPressCondition: float
      MinimumLocalPlayers: int
      LocalSurplus: int
      ApproachOffset: float<meter>
      CarrierPrediction: float<second>
      CoverDepth: float<meter>
      BeatenMargin: float<meter>
      EscapeProgress: float<meter>
      ThreatGoalDistance: float<meter>
      CentralHalfWidth: float<meter>
      DepthSafety: float<meter>
      ScreenFraction: float
      RetreatBallGap: float<meter>
      RetreatWidthScale: float
      SettledTolerance: float<meter>
      MaximumScreens: int
      CoverEffort: float
      ScreenEffort: float
      RecoveryEffort: float }

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

/// Bounds individual continuation search independently of collective commitments.
type PlannerConfig =
    { Enabled: bool
      MaxChainLength: int
      MaxEvaluateLimit: int
      BeamWidth: int
      DribbleDuration: float<second>
      MinimumPassDistance: float<meter>
      ShootingRange: float<meter> }

type CollectiveAttackConfig =
    { Enabled: bool
      Duration: int<subtick>
      Recovery: int<subtick>
      CounterHold: int<subtick>
      SupportDepth: float<meter>
      SupportWidth: float<meter>
      RunDepth: float<meter>
      SwitchWidth: float<meter>
      SecurityDepth: float<meter>
      OffsideMargin: float<meter>
      MaxArrival: float<second>
      LaneRadius: float<meter>
      SpaceScale: float<meter>
      MinimumCondition: float
      MinimumScore: float
      MinimumDutySeparation: float<meter>
      PressureRadius: float<meter>
      PressureCount: int
      LearningEnabled: bool
      LearningWeight: float
      Exploration: float
      PriorStrength: float
      EvidenceCap: float
      SeasonalRetention: float
      CooperationWeight: float
      ProgressReward: float
      SpaceReward: float
      ConnectionReward: float
      ThreatReward: float
      LossPenalty: float
      IdlePenalty: float
      ExposurePenalty: float
      TerritoryBoundary: float<meter>
      AnticipationHorizon: float<second>
      AnticipationConfidence: float }

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
      Contact: ContactConfig
      ContactAction: ContactActionConfig
      Actions: ActionsConfig
      SetPiece: SetPieceConfig
      Team: TeamConfig
      DefensiveTransition: DefensiveTransitionConfig
      Positioning: PositioningConfig
      Intercept: InterceptConfig
      Situation: SituationConfig
      PassIntercept: PassInterceptConfig
      Referee: RefereeConfig
      Manager: ManagerConfig
      Environment: EnvironmentConfig
      Match: MatchConfig
      Planner: PlannerConfig
      CollectiveAttack: CollectiveAttackConfig
      Scout: ScoutConfig
      Counter: CounterConfig
      Transfer: TransferConfig
      World: FootballEngine.Domain.WorldConfig
      TacticalTradeoffs: TacticalTradeoffConfig
      Training: TrainingConfig
      Weights: WeightSections }

module BalanceConfig =

    let defaultCollectiveAttack: CollectiveAttackConfig =
        // Episodes last several deliveries, but stale geometry and unsupported runs remain costly.
        { Enabled = true; Duration = 480<subtick>; Recovery = 80<subtick>; CounterHold = 60<subtick>
          LearningEnabled = true; MinimumDutySeparation = 3.0<meter>; PressureRadius = 8.0<meter>; PressureCount = 2
          SupportDepth = 6.0<meter>; SupportWidth = 8.0<meter>; RunDepth = 13.0<meter>
          SwitchWidth = 24.0<meter>; SecurityDepth = 9.0<meter>; OffsideMargin = 1.5<meter>
          MaxArrival = 4.0<second>; LaneRadius = 2.0<meter>; SpaceScale = 12.0<meter>
          MinimumCondition = 0.3; MinimumScore = 0.1; LearningWeight = 0.45; Exploration = 0.18
          PriorStrength = 3.0; EvidenceCap = 48.0; SeasonalRetention = 0.65; CooperationWeight = 0.22
          ProgressReward = 0.4; SpaceReward = 0.15; ConnectionReward = 0.12; ThreatReward = 0.5
          LossPenalty = 0.65; IdlePenalty = 0.18; ExposurePenalty = 0.15
          TerritoryBoundary = 20.0<meter>; AnticipationHorizon = 1.5<second>; AnticipationConfidence = 0.65 }

    let defaultDefensiveTransition: DefensiveTransitionConfig =
        // A short commitment tolerates geometry noise; depth danger still overrides it immediately.
        { ReevaluateTicks = 4<tickDelta>; Commitment = 24<subtick>; MaxPressDuration = 240<subtick>
          StabilizationHold = 24<subtick>
          BaseArrivalWindow = 1.2<second>; PressArrivalExtension = 0.8<second>
          LocalArrivalWindow = 2.0<second>; CoverArrivalWindow = 1.8<second>; HandoffAdvantage = 0.25<second>
          MinimumPressIntensity = 0.3; MinimumPressCondition = 0.55; MinimumLocalPlayers = 3; LocalSurplus = 1
          ApproachOffset = 0.75<meter>; CarrierPrediction = 0.15<second>; CoverDepth = 6.0<meter>
          BeatenMargin = 2.5<meter>; EscapeProgress = 10.0<meter>; ThreatGoalDistance = 28.0<meter>
          CentralHalfWidth = 18.0<meter>; DepthSafety = 3.0<meter>; ScreenFraction = 0.45
          RetreatBallGap = 8.0<meter>; RetreatWidthScale = 0.6; SettledTolerance = 3.0<meter>
          MaximumScreens = 2; CoverEffort = 0.85; ScreenEffort = 0.85; RecoveryEffort = 1.0 }

    let defaultBall: BallConfig =
        { Gravity = 9.81<meterPerSecondSquared>
          AirDrag = 0.999
          Radius = 0.11<meter>
          // A football is approximately a thin spherical shell, not a solid sphere.
          InertiaRatio = 2.0 / 3.0
          SlipFriction = 0.35
          RollingResistance = 0.025
          FootReach = 1.05<meter>
          FootContactHeight = 0.45<meter>
          KickWindup = 6<subtick>
          KickRecovery = 12<subtick>
          CarryTouchInterval = 7<subtick>
          CarryLeadTime = 0.12<second>
          CarryOffset = 0.32<meter>
          CarryMaxSpeed = 9.0<meterPerSecond>
          CarryTechniqueError = 0.6
          // Ground passes leave the boot close to rolling; zero spin would lose substantial range while skidding.
          PassRollingSpinRatio = 0.8
          ReceptionVelocityRetention = 0.12
          BounceRestitution = 0.6
          BounceFriction = 0.7
          StopSpeedCutoff = 0.06<meterPerSecond>
          MaxShotSpeed = 32.0<meterPerSecond>
          MagnusFactor = 0.02
          SpinDecay = 0.99
          ReceptionGrace = 6<subtick>
          InterceptionBonus = 0.1
          SaveReflexWeight = 0.5
          SavePositioningWeight = 0.3
          ShootoutSaveDistancePenalty = 12.0<meter>
          ReceptionBase = 0.5
          ReceptionSpacePenalty = 0.04
          ShootoutSaveBaseProbability = 0.45
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
          TurningAcceleration = 12.0<radianPerSecondSquared>
          FacingIntentDistance = 0.60<meter>
          StandingHeight = 1.85<meter>
          JumpSpeed = 3.6<meterPerSecond>
          AerialIntentRadius = 1.6<meter>
          Smoothing = 0.8
          FatigueSpeedDecay = 0.4
          ConditionDecayPerSecond = 0.00006
          RecoveryPerSecond = 0.01
          ReactiveRadius = 3.0<meter>
          ConditionImpactThreshold = 0.1
          IntentFloor = 0.2
          IntentHysteresis = 0.05
          CommittedRunDuration = 240<subtick> }

    let defaultContact: ContactConfig =
        { BodyRadiusFallback = 0.31<meter>
          BodyRadiusScale = 0.5
          CapsuleLowerHeightRatio = 0.20
          CapsuleUpperHeightRatio = 0.74
          HeadRadius = 0.12<meter>
          LegRadius = 0.12<meter>
          FootRadius = 0.11<meter>
          HandRadius = 0.10<meter>
          FallbackMass = 75.0<kilogram>
          BallMass = 0.43<kilogram>
          BodyRestitution = 0.02
          BodyFriction = 0.58
          BallBodyRestitution = 0.38
          BallFootRestitution = 0.05
          BallBodyFriction = 0.42
          BallControlSpeed = 2.5<meterPerSecond>
          MaximumTrapSpeed = 26.0<meterPerSecond>
          // A prepared trap cushions the ball with a moving foot; passive Coulomb friction alone left every
          // glancing reception uncontrollable, so free balls pinballed for ~20% of match time.
          TrapTangentialAbsorption = 0.9
          SupportedInverseMassScale = 0.35
          AirborneInverseMassScale = 1.0
          StabilityRecoveryPerSecond = 0.85
          StaggerImpulseThreshold = 2.4
          FallImpulseThreshold = 5.5
          RecoverStabilityThreshold = 0.76
          StaggerAccelerationScale = 0.55
          FallingAccelerationScale = 0.08
          PositionCorrectionPercent = 0.22
          MaximumPositionCorrection = 0.02<meter>
          VelocitySlop = 0.05<meterPerSecond>
          ToiTolerance = 0.000001
          SimultaneousTolerance = 0.00025
          SolverIterations = 4
          MaximumImpactIterations = 8
          CurvedPathSegments = 4
          MaximumPathError = 0.01<meter> }

    let defaultContactAction: ContactActionConfig =
        { ContainPreparation = 1<subtick>
          ShieldPreparation = 2<subtick>
          ShoulderPreparation = 3<subtick>
          StandingTacklePreparation = 4<subtick>
          SlidingTacklePreparation = 6<subtick>
          TrapPreparation = 2<subtick>
          StrikePreparation = 6<subtick>
          HeaderPreparation = 4<subtick>
          CommittedDuration = 4<subtick>
          RecoveryDuration = 12<subtick>
          FootReach = 0.46<meter>
          HeadReach = 0.28<meter>
          MaximumReorientation = 0.45<radian>
          ExecutionErrorScale = 0.14<meter>
          MaximumExecutionError = 0.65<meter>
          ShieldOffset = 0.34<meter>
          NewImpactThreshold = 0.72<meterPerSecond>
          CarelessRelativeSpeed = 1.2<meterPerSecond>
          RecklessRelativeSpeed = 2.5<meterPerSecond>
          ExcessiveRelativeSpeed = 4.2<meterPerSecond>
          DangerousBallFirstSpeed = 3.6<meterPerSecond> }

    let defaultActions: ActionsConfig =
        { PassBaseQuality = 0.7
          PassDistancePenaltyPerMeter = 0.004
          PassLongThreshold = 25.0<meter>
          PassVisionWeight = 0.3
          PassTechnicalWeight = 0.3
          PassJitterBase = 0.2
          PassHeavyTouchChance = 0.05
          PassLaunchSpeed = 22.0<meterPerSecond>
          PassDistanceSpeedGain = 0.1
          MaxPassExecutionDistance = 65.0<meter>
          ShotQualityGate = 0.5
          ShotAngleSpread = 0.3
          ShotDistanceToGoalMultiplier = 0.02
          ShotVzBase = 0.4
          DuelSteepness = 8.0
          DuelMomentumBonus = 0.1
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
        { Enabled = true
          MaxChainLength = 4
          MaxEvaluateLimit = 500
          BeamWidth = 3
          DribbleDuration = 0.5<second>
          MinimumPassDistance = 0.5<meter>
          ShootingRange = 32.0<meter> }

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
          Contact = defaultContact
          ContactAction = defaultContactAction
          Actions = defaultActions
          SetPiece = defaultSetPiece
          Team = defaultTeam
          DefensiveTransition = defaultDefensiveTransition
          Positioning = defaultPositioning
          Intercept = defaultIntercept
          Situation = defaultSituation
          PassIntercept = defaultPassIntercept
          Referee = defaultReferee
          Manager = defaultManager
          Environment = defaultEnvironment
          Match = defaultMatch
          Planner = defaultPlanner
          CollectiveAttack = defaultCollectiveAttack
          Scout = defaultScout
          Counter = defaultCounter
          Transfer = defaultTransfer
          World = FootballEngine.Domain.WorldConfig.defaults
          TacticalTradeoffs = defaultTacticalTradeoffs
          Training = defaultTraining
          Weights = WeightSections.defaultWeights }
