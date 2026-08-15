namespace FootballEngine.Types

open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.SimulationClock



type PerformanceConfig =
    {
        /// Human-readable name for logging and diagnostics
        Name: string
        StatWeight: float
        ConditionWeight: float
        MoraleWeight: float
        CurveSteepness: float
        /// Inflection point of the sigmoid. Fixed at 0.5 for symmetric distributions.
        CurveInflection: float
    }

module PerformanceDefaults =
    let duelPerformanceConfig =
        { Name = "duel"
          StatWeight = 0.70
          ConditionWeight = 0.20
          MoraleWeight = 0.10
          CurveSteepness = 8.0
          CurveInflection = 0.5 }

    let technicalPerformanceConfig =
        { Name = "technical"
          StatWeight = 0.75
          ConditionWeight = 0.15
          MoraleWeight = 0.10
          CurveSteepness = 10.0
          CurveInflection = 0.5 }

    let decisionPerformanceConfig =
        { Name = "decision"
          StatWeight = 0.85
          ConditionWeight = 0.10
          MoraleWeight = 0.05
          CurveSteepness = 6.0
          CurveInflection = 0.4 }

// ============================================================================
// Balance Config Records
// ============================================================================

type DuelConfig =
    {
        /// Steepness of the logistic curve in 1v1 duels.
        /// Calibration range: [0.8, 2.0].
        /// At 1.2: skill diff 0.3 → 59% win (realistic ~52-55%).
        /// At 3.0: skill diff 0.3 → 71% win (too swingy).
        DuelSteepness: float
        /// Momentum added to state when attacker wins a duel.
        /// Range: [0.5, 2.0].
        MomentumBonus: float
        JitterWin: float
        JitterRecover: float
        JitterKeep: float
        SpeedKeep: float<meter / second>
        SpeedKeepVz: float<meter / second>
        AttackerDribblingWeight: float
        AttackerAgilityWeight: float
        AttackerBalanceWeight: float
        DefenderTacklingWeight: float
        DefenderStrengthWeight: float
        DefenderPositionWeight: float
        FatigueThreshold: int
        FatigueDecay: float
    }

type ShotConfig =
    { QualityGate: float
      AngleSpreadBase: float
      VzBase: float<meter / second>
      VzVariance: float
      NormalisationDistance: float<meter>
      DistanceToGoalMultiplier: float
      FinishingMin: float
      FinishingMax: float
      FinishingBonusST: float
      FinishingBonusAM: float
      FinishingBonusMC: float
      FinishingBonusOther: float
      CondFactorDivisor: float
      ComposureMultiplier: float
      UrgencyMultiplier: float
      BasePowerDivisor: float
      SpinTopMultiplier: float
      PositionDirectnessWeight: float
      PositionDepthWeight: float
      PositionCreativityWeight: float
      DistNormWeight: float
      PositionBonusWeight: float
      HeavyTouchDivisor: float
      HeavyTouchMultiplier: float
      JitterStdDev: float
      GkReflexesStatMult: float
      GkOneOnOneStatMult: float
      SaveDenominatorOffset: float
      ShotWideMargin: float<meter> }

type PassConfig =
    { BaseMean: float
      DistancePenaltyPerMeter: float
      LongPassPenaltyPerMeter: float
      TechnicalWeight: float
      VisionWeight: float
      SuccessShapeAlpha: float
      SuccessConditionMultiplier: float
      OffsideMomentum: float
      SuccessMomentum: float
      FailMomentum: float
      DeflectBaseRate: float
      MisplacedBaseRate: float
      InterceptBaseRate: float
      InterceptionRadius: float<meter>
      PressureDistance: float<meter>
      DeflectPressureMultiplier: float
      InterceptPaceWeight: float
      InterceptPositioningWeight: float
      ScrambleJitter: float<meter>
      Speed: float<meter / second>
      Vz: float<meter / second>
      InterceptDistFactorWeight: float
      InterceptPositioningContrib: float
      InterceptVisionContrib: float
      CreativityWeight: float
      DirectnessWeight: float
      MeanMin: float
      MeanMax: float
      DefaultNearestDefDist: float<meter>
      DefaultTackling: float
      HeavyTouchDivisor: float
      HeavyTouchMultiplier: float
      JitterStdDev: float
      DeflectedSpeedMult: float
      DeflectedVzMult: float
      InterceptProbMax: float
      MisplacedSpeedMult: float
      LongBallBaseMean: float
      LongBallLongShotsWeight: float
      LongBallPassingWeight: float
      LongBallVisionWeight: float
      LongBallSuccessShapeAlpha: float
      LongBallSuccessConditionMultiplier: float
      LongBallOffsideMomentum: float
      LongBallSuccessMomentum: float
      LongBallFailMomentum: float
      LongBallSpeed: float<meter / second>
      LongBallVz: float<meter / second>
      LongBallDeflectMult: float
      LongBallInterceptMult: float
      LongBallPressureContrib: float
      ForwardDepthThreshold: float
      ForwardCreativityThreshold: float
      LongBallScrambleJitterMult: float
      PassLeadFactor: float
      FlightTimePerMeter: float
      FlightTimeMin: int<subtick>
      FlightTimeMax: int<subtick>
      LongBallFlightTimePerMeter: float
      LongBallFlightTimeMin: int<subtick>
      LongBallFlightTimeMax: int<subtick>
      ArrivalWindow: int<subtick> }

type CrossConfig =
    { BaseMean: float
      CrossingWeight: float
      PassingWeight: float
      SuccessShapeAlpha: float
      SuccessConditionMultiplier: float
      HeaderDuelSteepness: float
      HeaderAccuracyBase: float
      HeaderAccuracySkillMult: float
      GkSaveBase: float
      GkReflexesMult: float
      GkAerialReachMult: float
      GkJumpMult: float
      ClaimCrossProbability: float
      FailMomentum: float
      Speed: float<meter / second>
      Vz: float<meter / second>
      AerialThreatThreshold: float
      AttackingDepthThreshold: float
      GkSkillDefault: float
      GkSkillDivisor: float
      SpinTopMult: float
      SpinSideMult: float
      FallbackSpeed: float<meter / second>
      FallbackVz: float<meter / second> }

type DribbleConfig =
    { TechnicalWeight: float
      AgilityWeight: float
      BalanceWeight: float
      ForwardDistance: float
      SuccessMomentum: float
      FailMomentum: float
      CrossProbability: float
      PassProbability: float
      ShotProbability: float }

type TackleConfig =
    { TechnicalWeight: float
      PositioningWeight: float
      StrengthWeight: float
      AggressionWeight: float
      PositioningReduction: float
      FoulShapeBeta: float
      FoulMomentum: float
      SuccessMomentum: float
      FailMomentum: float
      TackleSteepness: float }

type SetPieceConfig =
    { FreeKickTargetX: float<meter>
      FreeKickSpeed: float<meter / second>
      FreeKickVz: float<meter / second>
      FreeKickSteepness: float
      FreeKickSavePowerThreshold: float
      FreeKickSaveVariance: float
      FreeKickSpinTopMult: float
      FreeKickSpinSideMult: float
      CornerBoxXThreshold: float<meter>
      CornerDefenderBoxThreshold: float<meter>
      CornerSecondPhaseProbability: float
      CornerKeepPossessionProbability: float
      CornerSpeed: float<meter / second>
      CornerVz: float<meter / second>
      CornerDensityBase: float
      CornerDensityPenalty: float
      CornerLogisticSteepness: float
      CornerDefScoreDefault: float
      ThrowInSpeed: float<meter / second>
      ThrowInVz: float<meter / second>
      ThrowInMomentum: float
      PenaltySkillMultiplier: float
      PenaltyMoraleMultiplier: float
      PenaltyPressureMultiplier: float
      PenaltyComposureNoise: float
      PenaltyLogisticBase: float
      PenaltyGkReflexesMult: float
      PenaltyGkHandlingMult: float
      PenaltySkillDivisor: float
      PenaltyCondDivisor: float
      PenaltyMoraleBase: float
      PenaltyMoraleDivisor: float
      PenaltyGkSkillDivisor: float
      PenaltySpeed: float<meter / second>
      PenaltyVzBase: float<meter / second>
      PenaltyVzVariance: float
      PenaltyAngleSpread: float
      PostShotClearProbability: float
      ClearSpeed: float<meter / second>
      ClearVz: float<meter / second>
      ClearYStdDev: float
      GoalKickFallbackDistHome: float<meter>
      GoalKickFallbackDistAway: float<meter>
      KickOffPartnerOffsetX: float<meter>
      KickOffPartnerOffsetY: float<meter>
      FoulBaseRate: float
      CornerOnFailedCross: float
      WallMinDistance: float<meter>
      WallPlayerSpacing: float<meter>
      WallJumpProbabilityBase: float
      WallBraveryWeight: float
      WallJumpingWeight: float
      WallSizeThreshold1: float<meter>
      WallSizeThreshold2: float<meter>
      WallSizeThreshold3: float<meter>
      WallSizeThreshold4: float<meter>
      CornerNearPostDist: float<meter>
      CornerFarPostDist: float<meter>
      CornerBoxDist: float<meter>
      CornerEdgeDist: float<meter>
      CornerStayBackCount: int
      CornerDefNearPostDist: float<meter>
      CornerDefFarPostDist: float<meter>
      CornerDefBoxDist: float<meter>
      CornerDefEdgeDist: float<meter>
      CornerGKLineDist: float<meter>
      ThrowInNearDist: float<meter>
      ThrowInSupportCount: int
      FreeKickAttackerNearDist: float<meter>
      BallSpotInset: float<meter> }

type HomeAdvantageConfig =
    { Strength: float
      DuelAttackBonus: float
      DuelDefenseBonus: float
      ShotComposureBonus: float
      PassAccuracyBonus: float
      DribbleBonus: float
      SetPlayAccuracyBonus: float
      TackleBonus: float
      FreeKickComposure: float
      PenaltyBonus: float
      CardReduction: float
      FatigueReduction: float }

type PhysicsConfig =
    { Gravity: float<meter / second^2>
      AirDrag: float
      GroundRestitution: float
      GroundFriction: float
      PostRestitution: float
      SpinDecay: float
      MagnusCoeff: float
      ContactRadius: float<meter>
      PlayerMaxForce: float<meter / second^2>
      PlayerMassBase: float
      PlayerMassWeightCoeff: float
      PlayerMassStrengthCoeff: float
      SteeringSlowRadius: float<meter>
      SteeringFleeRadius: float<meter>
      SteeringAlignmentWeight: float
      CohesionWeight: float
      TurnConstraintAgilityCoeff: float
      TurnConstraintBaseLimit: float
      MoveSpeedMax: float<meter / second>
      MoveSpeedMin: float<meter / second>
      SeparationMinDistance: float<meter>
      BallContestSeparationRadius: float<meter>
      SeparationStrength: float
      SeparationAgilityMultiplier: float
      JitterBase: float
      JitterAgilityMultiplier: float
      BallStopThreshold: float
      AirborneCheckThreshold: float<meter>
      AxisStopThreshold: float
      AirborneRestitutionBase: float
      AirborneRestitutionCoeff: float
      AirborneRestitutionFloor: float
      GroundRestitutionBase: float
      GroundRestitutionCoeff: float
      GroundRestitutionFloor: float
      ChaserProximity: float<meter>
      AirborneThreshold: float<meter>
      ArrivalAnticipationBase: float
      ArrivalAnticipationQuality: float
      ArrivalCompetitionRadius: float<meter>
      ArrivalConvergenceThreshold: float<meter / second>
      ArrivalContestThreshold: float
      ReceivingGraceSubTicks: int }

type GKConfig =
    { CatchHandlingMult: float
      DiveReach: float<meter>
      ParrySpeed: float<meter / second>
      ParryDeflectionAngle: float
      AerialReachMult: float
      JumpReachMult: float
      PunchProbability: float
      ClaimCrossProbability: float
      CollectionRadius: float<meter>
      CollectionPriority: float
      ThrowSpeed: float<meter / second>
      RollSpeed: float<meter / second>
      GoalKickSpeed: float<meter / second>
      PuntSpeed: float<meter / second>
      DistributionAccuracyMult: float
      DistributionDecisionNoise: float
      HoldTimeSubTicks: int<tickDelta>
      MaxHoldSubTicks: int<tickDelta>
      BackPassHandlingPenalty: float
      GKDecisionWindowSubTicks: int<tickDelta>
      SaveBaseProbability: float
      SaveReflexesWeight: float
      SavePositioningWeight: float
      SaveSpeedFactor: float
      SaveAngleFactor: float
      DistributionDecisionDelay: int<subtick>
      ShortDistributionBias: float
      GKShortPassMaxDist: float<meter> }

type OutfieldReceptionConfig =
    { ReceptionBaseProbability: float
      BallControlWeight: float
      PassQualityWeight: float
      BallSpeedPenalty: float
      ReceptionMaxProbability: float }

type TimingConfig =
    { DuelChainDelay: TickDelay
      DuelNextDelay: TickDelay
      ShotDelay: TickDelay
      FoulDelay: TickDelay
      GoalDelay: TickDelay
      KickOffDelay: TickDelay
      CornerDelay: TickDelay
      FreeKickDelay: TickDelay
      ThrowInDelay: TickDelay
      GoalKickDelay: TickDelay
      InjuryDelay: TickDelay
      ManagerReactDelay: TickDelay
      SubsDelay: TickDelay
      StuckBallDelay: int<tickDelta>
      EventWindowSubTicks: int }

type MatchVolumeConfig =
    {
        MaxChainLength: int
        TargetDuelTicksPerMatch: int
        /// Target shots per team per match. Real football: ~13-14.
        TargetShotsPerMatch: float
        TargetDribblesPerMatch: float
        /// Target passes per team per match. Real football: ~400.
        TargetPassesPerMatch: float
        TargetCrossesPerMatch: float
        TargetLongBallsPerMatch: float
    }

type ManagerConfig =
    { FatigueReactionThreshold: int
      SustainedMomentumSubTicks: int
      MomentumThreshold: float
      FatigueCheckSubTicks: int
      ConditionThresholdLosing: int
      ConditionThresholdDrawing: int
      ConditionThresholdWinning: int
      SubWindowMinutes: int[] }

type BuildUpConfig =
    { PassSuccessBonus: float
      DribblePenalty: float
      LongBallPenalty: float
      GKDistributionBonus: float
      DCPassingBonus: float }

type DecisionConfig =
    { ShootFinishingWeight: float
      ShootLongShotsWeight: float
      ShootComposureWeight: float
      ShootDistNormWeight: float
      ShootDistNormDivisor: float
      ShootPosDirectnessWeight: float
      ShootPosDepthWeight: float
      ShootSTBonus: float
      ShootDistPenaltyDivisor: float
      ShootDistPenaltyMax: float
      ShootDirectnessBonus: float
      PassPassingWeight: float
      PassVisionWeight: float
      PassTargetBonus: float
      PassAttackPhasePenalty: float
      DribbleZoneBonusAttacking: float
      DribbleZoneBonusMidfield: float
      DribbleAttackPhaseBonus: float
      DribbleTempoPenalty: float
      DribblePressurePenalty: float
      DribblePressureBlockThreshold: float
      CrossCrossingWeight: float
      CrossLateralTendencyWeight: float
      CrossLateralTendencyBase: float
      CrossZoneBonus: float
      CrossWidthBonus: float
      LongBallPassingWeight: float
      LongBallVisionWeight: float
      LongBallPressDistBase: float
      LongBallPressMin: float
      LongBallPressMax: float
      LongBallPressNoOpponent: float
      LongBallAttackPhaseBonus: float
      LongBallDirectnessBonus: float
      CreativityWeight: float
      DirectnessWeight: float
      DecisionTemperature: float
      // ----------------------------------------------------------
      // Decision thresholds — calibrated against PlayerScorer output
      // ranges. Moving these here removes all magic numbers from
      // PlayerDecision.fs. Tune here, never in logic code.
      // ----------------------------------------------------------
      // Shoot: scorer output ~0.20 (25m, avg) to ~0.82 (8m, elite ST).
      // 0.35 lets decent strikers shoot from medium range; direct teams
      // get a further reduction via ShootDirectnessThresholdMod.
      ShootMinThreshold: float // base minimum to attempt a shot
      ShootDirectnessThresholdMod: float // subtracted × t.Directness
      // Pass: scorer output reliably ~0.40-0.65 for any player with a
      // target. 0.25 blocks only truly broken passes (no vision, pressed,
      // bad target). Direct teams raise the bar slightly.
      PassMinThreshold: float
      PassDirectnessThresholdMod: float
      // Dribble: scorer output ~0.20-0.70. 0.15 blocks only the worst
      // cases; tactical gates (zone, tempo) add further context.
      DribbleMinThreshold: float
      DribbleDefZoneDirectnessMin: float // min profile.Directness to dribble in def zone
      DribbleHighTempo: float // t.Tempo above which directness check kicks in
      DribbleHighTempoDirectnessMin: float // min profile.Directness when tempo is high
      // Long ball: scorer output ~0.20-0.55. 0.15 lets pressed defenders
      // hoof it; direct teams lower bar further.
      LongBallMinThreshold: float
      LongBallDirectnessThresholdMod: float
      // Space pass: minimum SpaceScorer score to enter as candidate.
      SpacePassMinScore: float
      // ----------------------------------------------------------
      // Mental attribute modifiers (Change 1)
      // ----------------------------------------------------------
      ShootDecisionsWeight: float
      ShootBraveryMod: float
      ShootConcentrationMod: float
      PassDecisionsWeight: float
      PassConcentrationMod: float
      DribbleBraveryMod: float
      // ----------------------------------------------------------
      // xG integration (Change 3)
      // ----------------------------------------------------------
      ShootXGWeight: float
      // ----------------------------------------------------------
      // OpponentModel impact (Change 4)
      // ----------------------------------------------------------
      PassHighPressPenalty: float
      DribbleAggressivePenalty: float
      LongBallSlowTargetBonus: float
      // ----------------------------------------------------------
      // Pass trajectory (Change 5)
      // ----------------------------------------------------------
      PassTrajectoryBonus: float
      // ----------------------------------------------------------
      // Personality weights (Change 6)
      // ----------------------------------------------------------
      PersonalityFlairWeight: float
      PersonalityTeamworkWeight: float
      PersonalityConsistencyMod: float
      // ----------------------------------------------------------
      // Bravery pressure resistance (Change 7)
      // ----------------------------------------------------------
      ShootBraveryPressureMod: float
      // ----------------------------------------------------------
      // Anticipation bonus (Change 8)
      // ----------------------------------------------------------
      PassAnticipationBonus: float }

type PerceptionConfig =
    { VisionRadiusBase: float<meter>
      VisionRadiusMax: float<meter>
      VisionConeAngle: float
      PeripheralMultiplier: float
      MinimumAwarenessFloor: float<meter>
      AnticipationBonusRadius: float<meter>
      GoalkeeperConeAngle: float
      CommunicationRange: float<meter>
      SetPieceSimplifiedRadius: float<meter>
      BlindPassVisionThreshold: int
      BlindPassComposureThreshold: int
      BlindPassSuccessPenalty: float }

type ShootWeights =
    { FinishingWeight: float
      LongShotsWeight: float
      ComposureWeight: float
      XGInfluence: float
      ComposureStateMod: float
      ConfidenceMod: float
      FocusMod: float
      RiskBonus: float
      DistPenaltyDivisor: float
      DistPenaltyMax: float }

type PassWeights =
    { PassingWeight: float
      VisionWeight: float
      ComposureWeight: float
      TargetBonus: float
      AttackPhasePenalty: float }

type DribbleWeights =
    { DribblingWeight: float
      AgilityWeight: float
      BalanceWeight: float
      ZoneBonusAttacking: float
      ZoneBonusMidfield: float
      TempoPenalty: float
      PressurePenalty: float }

type CrossWeights =
    { CrossingWeight: float
      LateralTendencyWeight: float
      LateralTendencyBase: float
      ZoneBonus: float
      WidthBonus: float }

type LongBallWeights =
    { LongShotsWeight: float
      PassingWeight: float
      VisionWeight: float
      PressDistBase: float
      PressMin: float
      PressMax: float
      PressNoOpponent: float
      AttackPhaseBonus: float
      DirectnessBonus: float }

type IndividualWeights =
    { Shoot: ShootWeights
      Pass: PassWeights
      Dribble: DribbleWeights
      Cross: CrossWeights
      LongBall: LongBallWeights
      SoftmaxTemperature: float
      DirectnessBlendTactic: float
      DirectnessBlendProfile: float }

type DevelopmentWeights =
    { AgeBracket_MaxDelta_U21: int
      AgeBracket_MaxDelta_U25: int
      AgeBracket_MaxDelta_U28: int
      AgeBracket_MaxDelta_U31: int
      AgeBracket_MaxDelta_U34: int
      FocusMultiplier_Goalkeeping: float
      FocusMultiplier_PhysicalBase: float
      FocusMultiplier_Physical_Pressing: float
      FocusMultiplier_Physical_Positional: float
      FocusMultiplier_Mental: float
      FocusMultiplier_TechnicalBase: float
      FocusMultiplier_Technical_Creativity: float
      FocusMultiplier_Technical_Directness: float
      WeeklyDeltaDivisor: float
      StatFocus_DirectnessThreshold: float
      StatFocus_AttackingDepthThreshold: float
      StatFocus_DefensiveHeightThreshold: float
      StatFocus_CreativityThreshold: float
      MaybeStat_PositiveThreshold: float
      MaybeStat_NegativeThreshold: float }

type CalibrationTargets =
    { GoalsPerMatch: float
      ShotsPerMatch: float
      PassSuccessRate: float
      CrossSuccessRate: float
      HomeWinPct: float
      DrawPct: float
      AwayWinPct: float
      CardsPerMatch: float
      InjuriesPerMatch: float
      DribblesPerMatch: float
      CrossesPerMatch: float
      LongBallsPerMatch: float
      DuelTicksPerMatch: float }

[<CLIMutable>]
type PersonalityWeights =
    { FlairVisionWeight: float
      FlairDribblingWeight: float
      ConsistencyConcentrationWeight: float
      ConsistencyComposureWeight: float
      LeadershipWeight: float
      ControversyAggressionWeight: float
      ControversyComposureWeight: float
      TeamworkWorkRateWeight: float
      TeamworkPositioningWeight: float
      AmbitionMoraleWeight: float
      AmbitionWorkRateWeight: float
      PressureComposureWeight: float
      PressureConcentrationWeight: float
      SportsmanshipAggressionWeight: float
      TemperamentComposureWeight: float
      TemperamentConcentrationWeight: float }

[<CLIMutable>]
type DirectiveParamsMap =
    { CompactnessSuccessDelta: float
      CompactnessFailDelta: float
      CompactnessSuccessThreshold: float
      CompactnessFailThreshold: float
      PressingSuccessDelta: float
      PressingFailDelta: float
      PressingSuccessThreshold: float
      PressingFailThreshold: float
      WingPlaySuccessDelta: float
      WingPlayFailDelta: float
      WingPlaySuccessThreshold: float
      WingPlayFailThreshold: float }

[<CLIMutable>]
type EmergentWeights =
    { CompactnessSuccessDelta: float
      CompactnessFailDelta: float
      CompactnessSuccessThreshold: float
      CompactnessFailThreshold: float
      PressingSuccessDelta: float
      PressingFailDelta: float
      PressingSuccessThreshold: float
      PressingFailThreshold: float
      WingPlaySuccessDelta: float
      WingPlayFailDelta: float
      WingPlaySuccessThreshold: float
      WingPlayFailThreshold: float
      ConsecutiveLossPenalty: float
      FatigueSpiralCompactnessFactor: float
      FatigueSpiralPressingFactor: float
      FatigueSpiralTempoFactor: float
      FatigueSpiralRiskFactor: float
      FatigueSpiralThreshold: float }

[<CLIMutable>]
type ModifierWeights =
    { TransitionNearMult: float
      TransitionFarMult: float
      TransitionNearDistance: float
      WeaknessSupportMult: float
      RestDefenseSupportMult: float
      RestDefenseCoverMult: float
      ThreatCoverMult: float
      HighLineSupportMult: float
      LowBlockPressMult: float
      LowBlockCoverMult: float
      UrgencyPressMult: float
      UrgencySupportMult: float
      UrgencyThreshold: float }

[<CLIMutable>]
type ChemistryWeights =
    { FamiliarityPassBonus: float
      FamiliarityFailPenalty: float
      FamiliarityTimeBonus: float
      PressingCoordinationBase: float
      PressingCoordinationFamiliarityMult: float
      TransitionSpeedBase: float
      TransitionSpeedFamiliarityMult: float }

[<CLIMutable>]
type TeamDirectorWeights =
    { WorkRateWeight: float
      PositioningWeight: float
      AdvancedBonus: float }

[<CLIMutable>]
type ReactiveLoopWeights =
    { ShapeDevWeight: float
      PressDevWeight: float
      FatigueDevWeight: float
      OnTrackThreshold: float
      DriftingThreshold: float }

[<CLIMutable>]
type CollectiveWeights =
    { DirectiveParams: DirectiveParamsMap
      Emergent: EmergentWeights
      Modifiers: ModifierWeights
      Chemistry: ChemistryWeights
      TeamDirector: TeamDirectorWeights
      ReactiveLoop: ReactiveLoopWeights }

[<CLIMutable>]
type XGWeights =
    { DistanceFactor: float
      AngleExponent: float
      BaseMultiplier: float
      OneOnOneMultiplier: float
      SetPieceMultiplier: float
      PressureReduction: float
      HeaderMultiplier: float
      VolleyMultiplier: float
      HalfVolleyMultiplier: float
      ChipShotMultiplier: float
      CurlerMultiplier: float
      DrivenShotMultiplier: float
      PlacedShotMultiplier: float
      FirstTimeShotMultiplier: float }

[<CLIMutable>]
type InterceptionWeights =
    { BallControlRadiusMult: float
      PressIntentFactor: float
      RecoverIntentFactor: float
      MaintainShapeIntentFactor: float
      CoverSpaceIntentFactor: float }

[<CLIMutable>]
type WinProbabilityWeights =
    { GoalLeadBase: float
      DrawBase: float
      GoalDiffFactor: float
      XGFactor: float
      HomeAdvantage: float
      GoalDiffSteepness: float
      XGDiffSteepness: float
      MinutePressure: float
      ComebackBonus: float
      MomentumPositiveThreshold: float
      MomentumNegativeThreshold: float
      MomentumPositiveBonus: float
      MomentumNegativePenalty: float
      MomentumLinearFactor: float }

[<CLIMutable>]
type UtilityWeights =
    { PressZoneBonus_HighAttacking: float
      PressZoneBonus_HighMidfield: float
      PressZoneBonus_HighDefensive: float
      PressZoneBonus_MidAttackingMidfield: float
      PressZoneBonus_MidDefensive: float
      PressZoneBonus_Low: float
      PossessionChangeWindow: float
      ScoreDiffPressStep: float
      WingSpaceBase: float
      StaminaWingMult: float
      StructuredBase: float
      DirectiveChangeThreshold: float
      DropDeepHighLinePenalty: float
      DropDeepLeadBonus: float
      DropDeepTimeBonus: float
      DropDeepBase: float
      CounterPressStaminaFactor: float
      CounterPressIntensityBonus: float
      CounterPressBase: float
      BuildFromBackNoPressBonus: float
      BuildFromBackMidPressBonus: float
      BuildFromBackHighPressPenalty: float
      BuildFromBackLowBlockBonus: float
      BuildFromBackBase: float
      DirectPlayUrgencyBonus: float
      DirectPlayUrgencyBonusAny: float
      DirectPlayHighLineBonus: float
      DirectPlayBase: float
      SitAndCounterBase: float
      SitAndCounterLeadBonus: float
      SitAndCounterStaminaFactor: float
      HoldPossessionLeadBonus: float
      HoldPossessionDrawBonus: float
      HoldPossessionLosingPenalty: float
      HoldPossessionTimeBonus: float
      HoldPossessionPressPenalty: float
      HoldPossessionBase: float
      CompactBlockLosingBonus: float
      CompactBlockWinningPenalty: float
      CompactBlockOpponentBonus: float
      CompactBlockTimeBonus: float
      CompactBlockBase: float
      HighLineCohesionBonus: float
      HighLineStaminaFactor: float
      HighLineRiskPenalty: float
      HighLineBase: float
      PressingSuccessBonus: float
      OpponentHighLineNoPressBonus: float
      OverloadWeaknessBonus: float
      OverloadFlankBase: float }

[<CLIMutable>]
type PerformanceWeightsMap =
    { DuelStatWeight: float
      DuelConditionWeight: float
      DuelMoraleWeight: float
      DuelCurveSteepness: float
      DuelCurveInflection: float
      TechnicalStatWeight: float
      TechnicalConditionWeight: float
      TechnicalMoraleWeight: float
      TechnicalCurveSteepness: float
      TechnicalCurveInflection: float
      DecisionStatWeight: float
      DecisionConditionWeight: float
      DecisionMoraleWeight: float
      DecisionCurveSteepness: float
      DecisionCurveInflection: float }

[<CLIMutable>]
type RefereeWeights =
    { CardBaseProb: float
      CardAggressionMult: float
      CardHomeReduction: float
      InjuryBaseProb: float
      InjuryStrengthInverseMult: float
      FoulAggressionBase: float
      FoulAggressionMult: float
      HomeBonusCardThreshold: float
      MatchImportanceWeight: float
      DisciplineHistoryMitigation: float
      AdvantageWindowSeconds: float
      AdvantageCallBackWindow: float }

[<CLIMutable>]
type EnvironmentWeights =
    { WeatherClearModifier: float
      WeatherLightRainModifier: float
      WeatherHeavyRainModifier: float
      WeatherSnowModifier: float
      WeatherWindyModifier: float
      PitchDrySlipBase: float
      PitchDampSlipBase: float
      PitchWetSlipBase: float
      PitchWaterloggedSlipBase: float
      SlipAgilityReduction: float
      CrowdMaxCapacity: float
      CrowdCapacityWeight: float
      CrowdSupportWeight: float
      CrowdMomentumWeight: float
      CrowdImportanceWeight: float
      CrowdMaxAdvantage: float
      AwayPressureCrowdMult: float }

[<CLIMutable>]
type MomentumWeights =
    { EventDelta: float
      Decay: float
      Min: float
      Max: float
      HalfLifeSeconds: float }

type DiagnosticsConfig =
    { TraceDecisions: bool
      TracePlayerId: int option
      MaxTracesPerMatch: int }

type MatchSituationConfig =
    { ShootLosingLateMod: float
      ShootDrawLateMod: float
      ShootMomentumScale: float
      PassWinningMod: float
      PassTransitionPenalty: float
      DribbleTransitionMod: float
      LongBallLosingLateMod: float
      LongBallPressureThreshold: float
      LongBallPressureMod: float }

type InfluenceConfig =
    { GridCols: int
      GridRows: int
      SigmaBase: float
      SigmaAttributeScale: float
      VelocityBiasStrength: float
      BallGravityFactor: float
      BallGravitySigma: float
      ControlThreshold: float
      UpdateFrequencyTicks: int }

type BalanceConfig =
    { Duel: DuelConfig
      Shot: ShotConfig
      Pass: PassConfig
      Cross: CrossConfig
      Dribble: DribbleConfig
      Tackle: TackleConfig
      SetPiece: SetPieceConfig
      GK: GKConfig
      OutfieldReception: OutfieldReceptionConfig
      HomeAdvantage: HomeAdvantageConfig
      Physics: PhysicsConfig
      Timing: TimingConfig
      MatchVolume: MatchVolumeConfig
      Manager: ManagerConfig
      BuildUp: BuildUpConfig
      Decision: DecisionConfig
      Perception: PerceptionConfig
      Individual: IndividualWeights
      ProfileWeights: FootballEngine.Domain.ProfileWeights
      Development: DevelopmentWeights
      CalibrationTargets: CalibrationTargets
      Collective: CollectiveWeights
      Personality: PersonalityWeights
      Utility: UtilityWeights
      Referee: RefereeWeights
      Environment: EnvironmentWeights
      Momentum: MomentumWeights
      XG: XGWeights
      Interception: InterceptionWeights
      WinProbability: WinProbabilityWeights
      Performance: PerformanceWeightsMap
      MatchSituation: MatchSituationConfig
      Influence: InfluenceConfig
      Diagnostics: DiagnosticsConfig }

// ============================================================================
// Default config value
// ============================================================================

module BalanceConfig =

    let private clock = defaultClock

    let defaultConfig: BalanceConfig =
        { Duel =
            { DuelSteepness = 1.2
              MomentumBonus = 0.50
              JitterWin = 8.0
              JitterRecover = 2.0
              JitterKeep = 2.5
              SpeedKeep = 3.0<meter / second>
              SpeedKeepVz = 0.20<meter / second>
              AttackerDribblingWeight = 0.50
              AttackerAgilityWeight = 0.30
              AttackerBalanceWeight = 0.20
              DefenderTacklingWeight = 0.50
              DefenderStrengthWeight = 0.30
              DefenderPositionWeight = 0.20
              FatigueThreshold = 50
              FatigueDecay = 0.04 }
          Shot =
            { QualityGate = 0.2
              AngleSpreadBase = 0.15
              VzBase = LongBallVz
              VzVariance = 1.5
              NormalisationDistance = 30.0<meter>
              DistanceToGoalMultiplier = 0.15
              FinishingMin = 0.20
              FinishingMax = 1.00
              FinishingBonusST = 2.0
              FinishingBonusAM = 2.0
              FinishingBonusMC = 1.2
              FinishingBonusOther = 0.5
              CondFactorDivisor = 100.0
              ComposureMultiplier = 0.1
              UrgencyMultiplier = 0.05
              BasePowerDivisor = 20.0
              SpinTopMultiplier = 0.4
              PositionDirectnessWeight = 0.3
              PositionDepthWeight = 0.2
              PositionCreativityWeight = 0.1
              DistNormWeight = 0.7
              PositionBonusWeight = 0.3
              HeavyTouchDivisor = 20.0
              HeavyTouchMultiplier = 0.25
              JitterStdDev = 2.0
              GkReflexesStatMult = 3.5
              GkOneOnOneStatMult = 5.0
              SaveDenominatorOffset = 3.0
              ShotWideMargin = 3.0<meter> }
          Pass =
            { BaseMean = 0.65
              DistancePenaltyPerMeter = 0.012
              LongPassPenaltyPerMeter = 0.020
              TechnicalWeight = 0.15
              VisionWeight = 0.15
              SuccessShapeAlpha = 10.0
              SuccessConditionMultiplier = 6.0
              OffsideMomentum = 0.30
              SuccessMomentum = 0.30
              FailMomentum = 0.50
              DeflectBaseRate = 0.06
              MisplacedBaseRate = 0.03
              InterceptBaseRate = 0.05
              InterceptionRadius = 5.0<meter>
              PressureDistance = 8.0<meter>
              DeflectPressureMultiplier = 0.12
              InterceptPaceWeight = 0.35
              InterceptPositioningWeight = 0.45
              ScrambleJitter = 3.0<meter>
              Speed = PassSpeed
              Vz = PassVz
              InterceptDistFactorWeight = 0.3
              InterceptPositioningContrib = 0.15
              InterceptVisionContrib = 0.10
              CreativityWeight = 0.06
              DirectnessWeight = 0.06
              MeanMin = 0.01
              MeanMax = 0.99
              DefaultNearestDefDist = 10.0<meter>
              DefaultTackling = 0.5
              HeavyTouchDivisor = 20.0
              HeavyTouchMultiplier = 0.25
              JitterStdDev = 2.0
              DeflectedSpeedMult = 0.6
              DeflectedVzMult = 0.5
              InterceptProbMax = 0.8
              MisplacedSpeedMult = 0.7
              LongBallBaseMean = 0.40
              LongBallLongShotsWeight = 0.20
              LongBallPassingWeight = 0.20
              LongBallVisionWeight = 0.15
              LongBallSuccessShapeAlpha = 5.0
              LongBallSuccessConditionMultiplier = 10.0
              LongBallOffsideMomentum = 0.40
              LongBallSuccessMomentum = 0.50
              LongBallFailMomentum = 0.40
              LongBallSpeed = LongBallSpeed
              LongBallVz = LongBallVz
              LongBallDeflectMult = 1.5
              LongBallInterceptMult = 1.5
              LongBallPressureContrib = 0.3
              ForwardDepthThreshold = 0.5
              ForwardCreativityThreshold = 0.4
              LongBallScrambleJitterMult = 2.0
              PassLeadFactor = 0.30
              FlightTimePerMeter = 1.5
              FlightTimeMin = 40<subtick>
              FlightTimeMax = 80<subtick>
              LongBallFlightTimePerMeter = 1.0
              LongBallFlightTimeMin = 60<subtick>
              LongBallFlightTimeMax = 120<subtick>
              ArrivalWindow = 10<subtick> }
          Cross =
            { BaseMean = 0.50
              CrossingWeight = 0.25
              PassingWeight = 0.10
              SuccessShapeAlpha = 6.0
              SuccessConditionMultiplier = 8.0
              HeaderDuelSteepness = 1.5
              HeaderAccuracyBase = 0.25
              HeaderAccuracySkillMult = 0.03
              GkSaveBase = 0.25
              GkReflexesMult = 0.30
              GkAerialReachMult = 1.2
              GkJumpMult = 0.8
              ClaimCrossProbability = 0.25
              FailMomentum = 0.30
              Speed = CrossSpeed
              Vz = CrossVz
              AerialThreatThreshold = 0.4
              AttackingDepthThreshold = 0.5
              GkSkillDefault = 50.0
              GkSkillDivisor = 150.0
              SpinTopMult = 0.2
              SpinSideMult = 0.8
              FallbackSpeed = 15.0<meter / second>
              FallbackVz = 2.0<meter / second> }
          Dribble =
            { TechnicalWeight = 0.50
              AgilityWeight = 0.30
              BalanceWeight = 0.20
              ForwardDistance = 5.0
              SuccessMomentum = 0.50
              FailMomentum = 0.60
              CrossProbability = 0.05
              PassProbability = 0.75
              ShotProbability = 0.20 }
          Tackle =
            { TechnicalWeight = 0.50
              PositioningWeight = 0.30
              StrengthWeight = 0.20
              AggressionWeight = 0.15
              PositioningReduction = 0.10
              FoulShapeBeta = 10.0
              FoulMomentum = 0.30
              SuccessMomentum = 0.80
              FailMomentum = 0.50
              TackleSteepness = 1.5 }
          SetPiece =
            { FreeKickTargetX = GoalLineHome - PenaltyAreaDepth
              FreeKickSpeed = 16.0<meter / second>
              FreeKickVz = 0.50<meter / second>
              FreeKickSteepness = 2.0
              FreeKickSavePowerThreshold = 1.5
              FreeKickSaveVariance = 1.5
              FreeKickSpinTopMult = 0.5
              FreeKickSpinSideMult = 0.9
              CornerBoxXThreshold = GoalLineHome - PenaltyAreaDepth
              CornerDefenderBoxThreshold = GoalLineHome - PenaltyAreaDepth - 5.0<meter>
              CornerSecondPhaseProbability = 0.35
              CornerKeepPossessionProbability = 0.55
              CornerSpeed = 14.0<meter / second>
              CornerVz = 1.0<meter / second>
              CornerDensityBase = 3.0
              CornerDensityPenalty = 0.05
              CornerLogisticSteepness = 2.5
              CornerDefScoreDefault = 0.5
              ThrowInSpeed = 12.0<meter / second>
              ThrowInVz = 0.50<meter / second>
              ThrowInMomentum = 0.10
              PenaltySkillMultiplier = 0.04
              PenaltyMoraleMultiplier = 0.01
              PenaltyPressureMultiplier = 0.01
              PenaltyComposureNoise = 1.40
              PenaltyLogisticBase = 0.70
              PenaltyGkReflexesMult = 3.5
              PenaltyGkHandlingMult = 3.0
              PenaltySkillDivisor = 25.0
              PenaltyCondDivisor = 200.0
              PenaltyMoraleBase = 0.7
              PenaltyMoraleDivisor = 166.6
              PenaltyGkSkillDivisor = 40.0
              PenaltySpeed = 28.0<meter / second>
              PenaltyVzBase = 0.8<meter / second>
              PenaltyVzVariance = 0.3
              PenaltyAngleSpread = 0.12
              PostShotClearProbability = 0.40
              ClearSpeed = 16.0<meter / second>
              ClearVz = 1.5<meter / second>
              ClearYStdDev = 10.0
              GoalKickFallbackDistHome = 30.0<meter>
              GoalKickFallbackDistAway = 75.0<meter>
              KickOffPartnerOffsetX = -3.0<meter>
              KickOffPartnerOffsetY = 2.0<meter>
              FoulBaseRate = 0.35
              CornerOnFailedCross = 0.85
              WallMinDistance = 9.15<meter>
              WallPlayerSpacing = 0.65<meter>
              WallJumpProbabilityBase = 0.6
              WallBraveryWeight = 0.35
              WallJumpingWeight = 0.65
              WallSizeThreshold1 = 16.5<meter>
              WallSizeThreshold2 = 20.0<meter>
              WallSizeThreshold3 = 25.0<meter>
              WallSizeThreshold4 = 32.0<meter>
              CornerNearPostDist = 1.0<meter>
              CornerFarPostDist = 1.0<meter>
              CornerBoxDist = 6.0<meter>
              CornerEdgeDist = 18.0<meter>
              CornerStayBackCount = 2
              CornerDefNearPostDist = 1.0<meter>
              CornerDefFarPostDist = 1.0<meter>
              CornerDefBoxDist = 5.5<meter>
              CornerDefEdgeDist = 16.5<meter>
              CornerGKLineDist = 0.5<meter>
              ThrowInNearDist = 5.0<meter>
              ThrowInSupportCount = 3
              FreeKickAttackerNearDist = 3.0<meter>
              BallSpotInset = 0.2<meter> }
          GK =
            { CatchHandlingMult = 0.85
              DiveReach = 2.5<meter>
              ParrySpeed = 8.0<meter / second>
              ParryDeflectionAngle = 0.4
              AerialReachMult = 1.2
              JumpReachMult = 0.8
              PunchProbability = 0.35
              ClaimCrossProbability = 0.25
              CollectionRadius = 3.0<meter>
              CollectionPriority = 2.0
              ThrowSpeed = 12.0<meter / second>
              RollSpeed = 8.0<meter / second>
              GoalKickSpeed = 30.0<meter / second>
              PuntSpeed = 25.0<meter / second>
              DistributionAccuracyMult = 1.0
              DistributionDecisionNoise = 0.08
              HoldTimeSubTicks = 20<tickDelta>
              MaxHoldSubTicks = 240<tickDelta>
              BackPassHandlingPenalty = 0.15
              GKDecisionWindowSubTicks = 16<tickDelta>
              SaveBaseProbability = 0.35
              SaveReflexesWeight = 0.30
              SavePositioningWeight = 0.20
              SaveSpeedFactor = 0.015
              SaveAngleFactor = 0.01
              DistributionDecisionDelay = 10<subtick>
              ShortDistributionBias = 0.6
              GKShortPassMaxDist = 30.0<meter> }
          OutfieldReception =
            { ReceptionBaseProbability = 0.60
              BallControlWeight = 0.30
              PassQualityWeight = 0.25
              BallSpeedPenalty = 0.008
              ReceptionMaxProbability = 0.95 }
          HomeAdvantage =
            { Strength = 1.0
              DuelAttackBonus = 4.0
              DuelDefenseBonus = 2.0
              ShotComposureBonus = 4.0
              PassAccuracyBonus = 0.05
              DribbleBonus = 2.0
              SetPlayAccuracyBonus = 0.04
              TackleBonus = 6.0
              FreeKickComposure = 5.0
              PenaltyBonus = 0.04
              CardReduction = 0.20
              FatigueReduction = 0.10 }
          Physics =
            { Gravity = Gravity
              AirDrag = BallAirDrag
              GroundRestitution = BallGroundRestitution
              GroundFriction = BallGroundFriction
              PostRestitution = BallPostRestitution
              SpinDecay = BallSpinDecay
              MagnusCoeff = BallMagnusCoeff
              ContactRadius = BallContactRadius
              PlayerMaxForce = 25.0<meter / second^2>
              PlayerMassBase = 70.0
              PlayerMassWeightCoeff = 0.30
              PlayerMassStrengthCoeff = 0.50
              SteeringSlowRadius = SteeringSlowRadius
              SteeringFleeRadius = 8.0<meter>
              SteeringAlignmentWeight = 0.30
              CohesionWeight = 0.08
              TurnConstraintAgilityCoeff = TurnConstraintAgilityCoeff
              TurnConstraintBaseLimit = TurnConstraintBase
              MoveSpeedMax = PlayerSpeedMax
              MoveSpeedMin = PlayerSpeedMin
              SeparationMinDistance = PlayerSeparationRadius
              BallContestSeparationRadius = BallContestSeparationRadius
              SeparationStrength = 0.10
              SeparationAgilityMultiplier = 0.15
              JitterBase = 0.30
              JitterAgilityMultiplier = 0.50
              BallStopThreshold = 0.15
              AirborneCheckThreshold = 0.05<meter>
              AxisStopThreshold = 0.5
              AirborneRestitutionBase = 0.35
              AirborneRestitutionCoeff = 0.25
              AirborneRestitutionFloor = 0.008
              GroundRestitutionBase = 0.50
              GroundRestitutionCoeff = 0.20
              GroundRestitutionFloor = 0.005
              ChaserProximity = 1.0<meter>
              AirborneThreshold = 0.3<meter>
              ArrivalAnticipationBase = 0.4
              ArrivalAnticipationQuality = 0.8
              ArrivalCompetitionRadius = 2.0<meter>
              ArrivalConvergenceThreshold = -2.0<meter / second>
              ArrivalContestThreshold = 0.1
              ReceivingGraceSubTicks = 6 }
          Timing =
            { DuelChainDelay = TickDelay.ofSeconds clock 4.0 1.0 2.0 7.0
              DuelNextDelay = TickDelay.ofSeconds clock 24.0 5.0 12.0 38.0
              ShotDelay = TickDelay.ofSeconds clock 2.0 0.5 1.0 4.0
              FoulDelay = TickDelay.ofSeconds clock 5.0 1.5 3.0 9.0
              GoalDelay = TickDelay.ofSeconds clock 28.0 4.0 20.0 38.0
              KickOffDelay = TickDelay.ofSeconds clock 1.0 0.0 1.0 1.0
              CornerDelay = TickDelay.ofSeconds clock 11.0 2.0 7.0 16.0
              FreeKickDelay = TickDelay.ofSeconds clock 10.0 2.0 6.0 15.0
              ThrowInDelay = TickDelay.ofSeconds clock 5.0 1.0 3.0 8.0
              GoalKickDelay = TickDelay.ofSeconds clock 6.0 1.0 4.0 9.0
              InjuryDelay = TickDelay.ofSeconds clock 30.0 6.0 20.0 45.0
              ManagerReactDelay = TickDelay.ofSeconds clock 8.0 2.5 5.0 15.0
              SubsDelay = TickDelay.ofSeconds clock 22.0 3.0 14.0 30.0
              StuckBallDelay = secondsToSubTicks clock 5.0 |> int |> (fun x -> x * 1<tickDelta>)
              EventWindowSubTicks = 1800 }
          MatchVolume =
            { MaxChainLength = 6
              TargetDuelTicksPerMatch = 228
              TargetShotsPerMatch = 14.0
              TargetDribblesPerMatch = 25.0
              TargetPassesPerMatch = 400.0
              TargetCrossesPerMatch = 20.0
              TargetLongBallsPerMatch = 40.0 }
          Manager =
            { FatigueReactionThreshold = 60
              SustainedMomentumSubTicks = secondsToSubTicks clock 600 |> int
              MomentumThreshold = -2.0
              FatigueCheckSubTicks = secondsToSubTicks clock 120 |> int
              ConditionThresholdLosing = 75
              ConditionThresholdDrawing = 65
              ConditionThresholdWinning = 55
              SubWindowMinutes = [| 60; 75; 85 |] }
          BuildUp =
            { PassSuccessBonus = 0.06
              DribblePenalty = 0.12
              LongBallPenalty = 0.08
              GKDistributionBonus = 0.08
              DCPassingBonus = 0.05 }
          Decision =
            { ShootFinishingWeight = 0.35
              ShootLongShotsWeight = 0.15
              ShootComposureWeight = 0.20
              ShootDistNormWeight = 0.20
              ShootDistNormDivisor = 30.0
              ShootPosDirectnessWeight = 0.10
              ShootPosDepthWeight = 0.08
              ShootSTBonus = 0.20
              ShootDistPenaltyDivisor = 50.0
              ShootDistPenaltyMax = 0.5
              ShootDirectnessBonus = 0.15
              PassPassingWeight = 0.40
              PassVisionWeight = 0.30
              PassTargetBonus = 0.10
              PassAttackPhasePenalty = -0.03
              DribbleZoneBonusAttacking = 0.1
              DribbleZoneBonusMidfield = 0.05
              DribbleAttackPhaseBonus = 0.05
              DribbleTempoPenalty = 0.20
              DribblePressurePenalty = 0.15
              DribblePressureBlockThreshold = 0.7
              CrossCrossingWeight = 0.60
              CrossLateralTendencyWeight = 0.60
              CrossLateralTendencyBase = 0.10
              CrossZoneBonus = 0.15
              CrossWidthBonus = 0.25
              LongBallPassingWeight = 0.30
              LongBallVisionWeight = 0.20
              LongBallPressDistBase = 10.0
              LongBallPressMin = 0.3
              LongBallPressMax = 1.0
              LongBallPressNoOpponent = 0.7
              LongBallAttackPhaseBonus = 0.05
              LongBallDirectnessBonus = 0.20
              CreativityWeight = 0.10
              DirectnessWeight = 0.06
              DecisionTemperature = 0.25
              // Decision thresholds — see DecisionConfig comments for derivation
              ShootMinThreshold = 0.35 // ~avg striker from 20m passes
              ShootDirectnessThresholdMod = 0.10 // directness=1.0 -> threshold=0.25
              PassMinThreshold = 0.25 // only blocks truly broken passes
              PassDirectnessThresholdMod = 0.08 // directness=1.0 -> threshold=0.17
              DribbleMinThreshold = 0.15
              DribbleDefZoneDirectnessMin = 0.20
              DribbleHighTempo = 0.70
              DribbleHighTempoDirectnessMin = 0.40
              LongBallMinThreshold = 0.15
              LongBallDirectnessThresholdMod = 0.06 // directness=1.0 -> threshold=0.09
              SpacePassMinScore = 0.25
              // Mental attribute modifiers (Change 1)
              ShootDecisionsWeight = 0.15
              ShootBraveryMod = 0.12
              ShootConcentrationMod = 0.08
              PassDecisionsWeight = 0.12
              PassConcentrationMod = 0.08
              DribbleBraveryMod = 0.15
              // xG integration (Change 3)
              ShootXGWeight = 0.20
              // OpponentModel impact (Change 4)
              PassHighPressPenalty = 0.07
              DribbleAggressivePenalty = 0.08
              LongBallSlowTargetBonus = 0.12
              // Pass trajectory (Change 5)
              PassTrajectoryBonus = 0.15
              // Personality weights (Change 6)
              PersonalityFlairWeight = 0.60
              PersonalityTeamworkWeight = 0.45
              PersonalityConsistencyMod = 0.30
              // Bravery pressure resistance (Change 7)
              ShootBraveryPressureMod = 0.20
              // Anticipation bonus (Change 8)
              PassAnticipationBonus = 0.12 }
          Perception =
            { VisionRadiusBase = 20.0<meter>
              VisionRadiusMax = 45.0<meter>
              VisionConeAngle = 2.094 // 120 degrees in radians
              PeripheralMultiplier = 0.4
              MinimumAwarenessFloor = 20.0<meter>
              AnticipationBonusRadius = 5.0<meter>
              GoalkeeperConeAngle = 3.1416 // 180 degrees
              CommunicationRange = 15.0<meter>
              SetPieceSimplifiedRadius = 30.0<meter>
              BlindPassVisionThreshold = 15
              BlindPassComposureThreshold = 14
              BlindPassSuccessPenalty = 0.30 }
          Individual =
            { Shoot =
                { FinishingWeight = 0.35
                  LongShotsWeight = 0.15
                  ComposureWeight = 0.20
                  XGInfluence = 0.20
                  ComposureStateMod = 0.12
                  ConfidenceMod = 0.08
                  FocusMod = 0.06
                  RiskBonus = 0.075
                  DistPenaltyDivisor = 50.0
                  DistPenaltyMax = 0.5 }
              Pass =
                { PassingWeight = 0.40
                  VisionWeight = 0.30
                  ComposureWeight = 0.08
                  TargetBonus = 0.10
                  AttackPhasePenalty = -0.03 }
              Dribble =
                { DribblingWeight = 0.50
                  AgilityWeight = 0.30
                  BalanceWeight = 0.20
                  ZoneBonusAttacking = 0.1
                  ZoneBonusMidfield = 0.05
                  TempoPenalty = 0.20
                  PressurePenalty = 0.15 }
              Cross =
                { CrossingWeight = 0.60
                  LateralTendencyWeight = 0.60
                  LateralTendencyBase = 0.10
                  ZoneBonus = 0.15
                  WidthBonus = 0.25 }
              LongBall =
                { LongShotsWeight = 0.0
                  PassingWeight = 0.30
                  VisionWeight = 0.20
                  PressDistBase = 10.0
                  PressMin = 0.3
                  PressMax = 1.0
                  PressNoOpponent = 0.7
                  AttackPhaseBonus = 0.05
                  DirectnessBonus = 0.20 }
              SoftmaxTemperature = 0.15
              DirectnessBlendTactic = 0.6
              DirectnessBlendProfile = 0.4 }
          ProfileWeights =
            { PositionalFreedom_PositioningWeight = 0.15
              PositionalFreedom_VisionWeight = 0.15
              PositionalFreedom_StaminaWeight = 0.20
              PositionalFreedom_ConcentrationWeight = 0.10
              PositionalFreedom_BalanceWeight = 0.15
              PositionalFreedom_AgilityWeight = 0.25
              AttackingDepth_PaceWeight = 0.15
              AttackingDepth_AccelerationWeight = 0.12
              AttackingDepth_FinishingWeight = 0.08
              AttackingDepth_ComposureWeight = 0.05
              AttackingDepth_StaminaWeight = 0.10
              AttackingDepth_PositionBaseMultiplier = 0.50
              LateralTendency_CrossingWeight = 0.15
              LateralTendency_PaceWeight = 0.10
              DefensiveHeight_WorkRateWeight = 0.15
              DefensiveHeight_TacklingWeight = 0.10
              DefensiveHeight_PositioningWeight = 0.12
              DefensiveHeight_StaminaWeight = 0.08
              DefensiveHeight_PositionBaseMultiplier = 0.55
              PressingIntensity_StaminaWeight = 0.30
              PressingIntensity_WorkRateWeight = 0.25
              PressingIntensity_AggressionWeight = 0.20
              PressingIntensity_PaceWeight = 0.15
              PressingIntensity_ConcentrationWeight = 0.10
              RiskAppetite_PassingWeight = 0.15
              RiskAppetite_LongShotsWeight = 0.15
              RiskAppetite_VisionWeight = 0.20
              RiskAppetite_ComposureWeight = 0.20
              RiskAppetite_DribblingWeight = 0.15
              RiskAppetite_BraveryWeight = 0.15
              Directness_FinishingWeight = 0.20
              Directness_PaceWeight = 0.20
              Directness_AccelerationWeight = 0.15
              Directness_AggressionWeight = 0.15
              Directness_DribblingWeight = 0.10
              Directness_StrengthWeight = 0.10
              Directness_InversePassingWeight = 0.10
              CreativityWeight_PassingWeight = 0.25
              CreativityWeight_VisionWeight = 0.30
              CreativityWeight_BallControlWeight = 0.15
              CreativityWeight_DribblingWeight = 0.10
              CreativityWeight_ComposureWeight = 0.10
              CreativityWeight_CrossingWeight = 0.10
              AerialThreat_JumpingReachWeight = 0.35
              AerialThreat_HeadingWeight = 0.30
              AerialThreat_StrengthWeight = 0.20
              AerialThreat_BraveryWeight = 0.15
              HoldUpPlay_StrengthWeight = 0.25
              HoldUpPlay_BallControlWeight = 0.20
              HoldUpPlay_ComposureWeight = 0.15
              HoldUpPlay_PassingWeight = 0.15
              HoldUpPlay_HeadingWeight = 0.10
              HoldUpPlay_BalanceWeight = 0.15 }
          Development =
            { AgeBracket_MaxDelta_U21 = 4
              AgeBracket_MaxDelta_U25 = 3
              AgeBracket_MaxDelta_U28 = 2
              AgeBracket_MaxDelta_U31 = 1
              AgeBracket_MaxDelta_U34 = 0
              FocusMultiplier_Goalkeeping = 1.5
              FocusMultiplier_PhysicalBase = 0.5
              FocusMultiplier_Physical_Pressing = 1.0
              FocusMultiplier_Physical_Positional = 0.5
              FocusMultiplier_Mental = 1.2
              FocusMultiplier_TechnicalBase = 0.5
              FocusMultiplier_Technical_Creativity = 1.0
              FocusMultiplier_Technical_Directness = 0.5
              WeeklyDeltaDivisor = 120.0
              StatFocus_DirectnessThreshold = 0.5
              StatFocus_AttackingDepthThreshold = 0.5
              StatFocus_DefensiveHeightThreshold = 0.5
              StatFocus_CreativityThreshold = 0.5
              MaybeStat_PositiveThreshold = 0.35
              MaybeStat_NegativeThreshold = 0.50 }
          CalibrationTargets =
            { GoalsPerMatch = 2.75
              ShotsPerMatch = 14.0
              PassSuccessRate = 0.65
              CrossSuccessRate = 0.50
              HomeWinPct = 0.475
              DrawPct = 0.25
              AwayWinPct = 0.275
              CardsPerMatch = 3.0
              InjuriesPerMatch = 0.5
              DribblesPerMatch = 25.0
              CrossesPerMatch = 20.0
              LongBallsPerMatch = 40.0
              DuelTicksPerMatch = 228.0 }
          Collective =
            { DirectiveParams =
                { CompactnessSuccessDelta = 0.05
                  CompactnessFailDelta = -0.05
                  CompactnessSuccessThreshold = 0.7
                  CompactnessFailThreshold = 0.5
                  PressingSuccessDelta = 0.05
                  PressingFailDelta = -0.08
                  PressingSuccessThreshold = 0.6
                  PressingFailThreshold = 0.4
                  WingPlaySuccessDelta = 0.05
                  WingPlayFailDelta = -0.05
                  WingPlaySuccessThreshold = 0.55
                  WingPlayFailThreshold = 0.4 }
              Emergent =
                { CompactnessSuccessDelta = 0.05
                  CompactnessFailDelta = -0.05
                  CompactnessSuccessThreshold = 0.7
                  CompactnessFailThreshold = 0.5
                  PressingSuccessDelta = 0.05
                  PressingFailDelta = -0.08
                  PressingSuccessThreshold = 0.6
                  PressingFailThreshold = 0.4
                  WingPlaySuccessDelta = 0.05
                  WingPlayFailDelta = -0.05
                  WingPlaySuccessThreshold = 0.55
                  WingPlayFailThreshold = 0.4
                  ConsecutiveLossPenalty = 0.03
                  FatigueSpiralCompactnessFactor = 0.2
                  FatigueSpiralPressingFactor = 1.2
                  FatigueSpiralTempoFactor = 0.6
                  FatigueSpiralRiskFactor = 0.5
                  FatigueSpiralThreshold = 0.0 }
              Modifiers =
                { TransitionNearMult = 2.5
                  TransitionFarMult = 0.7
                  TransitionNearDistance = 15.0
                  WeaknessSupportMult = 1.4
                  RestDefenseSupportMult = 0.3
                  RestDefenseCoverMult = 1.5
                  ThreatCoverMult = 1.5
                  HighLineSupportMult = 1.3
                  LowBlockPressMult = 0.7
                  LowBlockCoverMult = 1.2
                  UrgencyPressMult = 0.5
                  UrgencySupportMult = 0.3
                  UrgencyThreshold = 0.7 }
              Chemistry =
                { FamiliarityPassBonus = 0.02
                  FamiliarityFailPenalty = -0.005
                  FamiliarityTimeBonus = 0.001
                  PressingCoordinationBase = 0.1
                  PressingCoordinationFamiliarityMult = 0.8
                  TransitionSpeedBase = 0.05
                  TransitionSpeedFamiliarityMult = 0.9 }
              TeamDirector =
                { WorkRateWeight = 0.3
                  PositioningWeight = 0.4
                  AdvancedBonus = 0.5 }
              ReactiveLoop =
                { ShapeDevWeight = 0.4
                  PressDevWeight = 0.3
                  FatigueDevWeight = 0.3
                  OnTrackThreshold = 0.15
                  DriftingThreshold = 0.35 } }
          Personality =
            { FlairVisionWeight = 0.6
              FlairDribblingWeight = 0.4
              ConsistencyConcentrationWeight = 0.5
              ConsistencyComposureWeight = 0.5
              LeadershipWeight = 1.0
              ControversyAggressionWeight = 0.5
              ControversyComposureWeight = 0.5
              TeamworkWorkRateWeight = 0.6
              TeamworkPositioningWeight = 0.4
              AmbitionMoraleWeight = 0.5
              AmbitionWorkRateWeight = 0.5
              PressureComposureWeight = 0.7
              PressureConcentrationWeight = 0.3
              SportsmanshipAggressionWeight = 0.6
              TemperamentComposureWeight = 0.5
              TemperamentConcentrationWeight = 0.5 }
          Utility =
            { PressZoneBonus_HighAttacking = 0.4
              PressZoneBonus_HighMidfield = 0.1
              PressZoneBonus_HighDefensive = -0.3
              PressZoneBonus_MidAttackingMidfield = 0.3
              PressZoneBonus_MidDefensive = -0.1
              PressZoneBonus_Low = 0.1
              PossessionChangeWindow = 80.0
              ScoreDiffPressStep = 0.35
              WingSpaceBase = 0.3
              StaminaWingMult = 0.2
              StructuredBase = 0.25
              DirectiveChangeThreshold = 0.15
              DropDeepHighLinePenalty = -0.2
              DropDeepLeadBonus = 0.2
              DropDeepTimeBonus = 0.3
              DropDeepBase = 0.3
              CounterPressStaminaFactor = 0.5
              CounterPressIntensityBonus = 0.3
              CounterPressBase = 0.5
              BuildFromBackNoPressBonus = 0.4
              BuildFromBackMidPressBonus = 0.1
              BuildFromBackHighPressPenalty = -0.2
              BuildFromBackLowBlockBonus = 0.2
              BuildFromBackBase = 0.3
              DirectPlayUrgencyBonus = 0.4
              DirectPlayUrgencyBonusAny = 0.2
              DirectPlayHighLineBonus = 0.3
              DirectPlayBase = 0.2
              SitAndCounterBase = 0.1
              SitAndCounterLeadBonus = 0.15
              SitAndCounterStaminaFactor = 0.2
              HoldPossessionLeadBonus = 0.3
              HoldPossessionDrawBonus = 0.1
              HoldPossessionLosingPenalty = -0.2
              HoldPossessionTimeBonus = 0.3
              HoldPossessionPressPenalty = -0.2
              HoldPossessionBase = 0.3
              CompactBlockLosingBonus = 0.2
              CompactBlockWinningPenalty = -0.1
              CompactBlockOpponentBonus = 0.2
              CompactBlockTimeBonus = 0.2
              CompactBlockBase = 0.3
              HighLineCohesionBonus = 0.3
              HighLineStaminaFactor = 0.2
              HighLineRiskPenalty = -0.2
              HighLineBase = 0.2
              PressingSuccessBonus = 0.2
              OpponentHighLineNoPressBonus = 0.3
              OverloadWeaknessBonus = 0.4
              OverloadFlankBase = 0.3 }
          Referee =
            { CardBaseProb = 0.010
              CardAggressionMult = 0.0004
              CardHomeReduction = 0.20
              InjuryBaseProb = 0.0008
              InjuryStrengthInverseMult = 0.00002
              FoulAggressionBase = 0.25
              FoulAggressionMult = 0.35
              HomeBonusCardThreshold = 0.10
              MatchImportanceWeight = 0.15
              DisciplineHistoryMitigation = 0.05
              AdvantageWindowSeconds = 2.0
              AdvantageCallBackWindow = 3.0 }
          Environment =
            { WeatherClearModifier = 1.0
              WeatherLightRainModifier = 0.95
              WeatherHeavyRainModifier = 0.85
              WeatherSnowModifier = 0.8
              WeatherWindyModifier = 0.9
              PitchDrySlipBase = 0.01
              PitchDampSlipBase = 0.03
              PitchWetSlipBase = 0.08
              PitchWaterloggedSlipBase = 0.15
              SlipAgilityReduction = 0.5
              CrowdMaxCapacity = 80000.0
              CrowdCapacityWeight = 0.3
              CrowdSupportWeight = 0.3
              CrowdMomentumWeight = 0.2
              CrowdImportanceWeight = 0.2
              CrowdMaxAdvantage = 0.15
              AwayPressureCrowdMult = 0.5 }
          Momentum =
            { EventDelta = 0.5
              Decay = 0.02
              Min = -10.0
              Max = 10.0
              HalfLifeSeconds = 15.0 }
          XG =
            { DistanceFactor = 0.033
              AngleExponent = 1.0
              BaseMultiplier = 0.8
              OneOnOneMultiplier = 1.3
              SetPieceMultiplier = 0.8
              PressureReduction = 0.5
              HeaderMultiplier = 0.7
              VolleyMultiplier = 0.6
              HalfVolleyMultiplier = 0.75
              ChipShotMultiplier = 0.85
              CurlerMultiplier = 0.9
              DrivenShotMultiplier = 1.1
              PlacedShotMultiplier = 1.0
              FirstTimeShotMultiplier = 0.8 }
          Interception =
            { BallControlRadiusMult = 0.20
              PressIntentFactor = 0.7
              RecoverIntentFactor = 0.7
              MaintainShapeIntentFactor = 1.4
              CoverSpaceIntentFactor = 1.2 }
          WinProbability =
            { GoalLeadBase = 0.55
              DrawBase = 0.50
              GoalDiffFactor = 0.1
              XGFactor = 0.15
              HomeAdvantage = 0.0
              GoalDiffSteepness = 0.1
              XGDiffSteepness = 0.15
              MinutePressure = 0.0
              ComebackBonus = 0.0
              MomentumPositiveThreshold = 2.0
              MomentumNegativeThreshold = -2.0
              MomentumPositiveBonus = 0.08
              MomentumNegativePenalty = -0.08
              MomentumLinearFactor = 0.04 }
          Performance =
            { DuelStatWeight = 0.70
              DuelConditionWeight = 0.20
              DuelMoraleWeight = 0.10
              DuelCurveSteepness = 8.0
              DuelCurveInflection = 0.5
              TechnicalStatWeight = 0.75
              TechnicalConditionWeight = 0.15
              TechnicalMoraleWeight = 0.10
              TechnicalCurveSteepness = 10.0
              TechnicalCurveInflection = 0.5
              DecisionStatWeight = 0.85
              DecisionConditionWeight = 0.10
              DecisionMoraleWeight = 0.05
              DecisionCurveSteepness = 6.0
              DecisionCurveInflection = 0.4 }
          MatchSituation =
            { ShootLosingLateMod = 1.35
              ShootDrawLateMod = 1.15
              ShootMomentumScale = 0.30
              PassWinningMod = 1.20
              PassTransitionPenalty = 0.80
              DribbleTransitionMod = 1.20
              LongBallLosingLateMod = 1.30
              LongBallPressureThreshold = 0.60
              LongBallPressureMod = 1.25 }
          Influence =
            { GridCols = 10
              GridRows = 7
              SigmaBase = 10.0
              SigmaAttributeScale = 8.0
              VelocityBiasStrength = 0.4
              BallGravityFactor = 0.6
              BallGravitySigma = 20.0
              ControlThreshold = 0.3
              UpdateFrequencyTicks = 10 }
          Diagnostics =
            { TraceDecisions = false
              TracePlayerId = None
              MaxTracesPerMatch = 500 } }
