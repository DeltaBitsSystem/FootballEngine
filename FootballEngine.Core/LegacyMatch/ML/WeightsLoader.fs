namespace FootballEngine.ML

open System
open System.Text.Json
open System.IO
open FootballEngine.Types
open FootballEngine.Domain

// ============================================================================
// Partial types for JSON deserialization — option fields fall back to defaults
// ============================================================================

[<CLIMutable>]
type PartialDuelConfig =
    { DuelSteepness: float option
      MomentumBonus: float option
      JitterWin: float option
      JitterRecover: float option
      JitterKeep: float option
      SpeedKeep: float option
      SpeedKeepVz: float option
      AttackerDribblingWeight: float option
      AttackerAgilityWeight: float option
      AttackerBalanceWeight: float option
      DefenderTacklingWeight: float option
      DefenderStrengthWeight: float option
      DefenderPositionWeight: float option
      FatigueThreshold: int option
      FatigueDecay: float option }

[<CLIMutable>]
type PartialShotConfig =
    { QualityGate: float option
      AngleSpreadBase: float option
      VzBase: float option
      VzVariance: float option
      NormalisationDistance: float option
      DistanceToGoalMultiplier: float option
      FinishingMin: float option
      FinishingMax: float option
      FinishingBonusST: float option
      FinishingBonusAM: float option
      FinishingBonusMC: float option
      FinishingBonusOther: float option
      CondFactorDivisor: float option
      ComposureMultiplier: float option
      UrgencyMultiplier: float option
      BasePowerDivisor: float option
      SpinTopMultiplier: float option
      PositionDirectnessWeight: float option
      PositionDepthWeight: float option
      PositionCreativityWeight: float option
      DistNormWeight: float option
      PositionBonusWeight: float option
      HeavyTouchDivisor: float option
      HeavyTouchMultiplier: float option
      JitterStdDev: float option
      GkReflexesStatMult: float option
      GkOneOnOneStatMult: float option
      SaveDenominatorOffset: float option
      ShotWideMargin: float option }

[<CLIMutable>]
type PartialPassConfig =
    { BaseMean: float option
      DistancePenaltyPerMeter: float option
      LongPassPenaltyPerMeter: float option
      TechnicalWeight: float option
      VisionWeight: float option
      SuccessShapeAlpha: float option
      SuccessConditionMultiplier: float option
      OffsideMomentum: float option
      SuccessMomentum: float option
      FailMomentum: float option
      DeflectBaseRate: float option
      MisplacedBaseRate: float option
      InterceptBaseRate: float option
      InterceptionRadius: float option
      PressureDistance: float option
      DeflectPressureMultiplier: float option
      InterceptPaceWeight: float option
      InterceptPositioningWeight: float option
      ScrambleJitter: float option
      Speed: float option
      Vz: float option
      InterceptDistFactorWeight: float option
      InterceptPositioningContrib: float option
      InterceptVisionContrib: float option
      CreativityWeight: float option
      DirectnessWeight: float option
      MeanMin: float option
      MeanMax: float option
      DefaultNearestDefDist: float option
      DefaultTackling: float option
      HeavyTouchDivisor: float option
      HeavyTouchMultiplier: float option
      JitterStdDev: float option
      DeflectedSpeedMult: float option
      DeflectedVzMult: float option
      InterceptProbMax: float option
      MisplacedSpeedMult: float option
      LongBallBaseMean: float option
      LongBallLongShotsWeight: float option
      LongBallPassingWeight: float option
      LongBallVisionWeight: float option
      LongBallSuccessShapeAlpha: float option
      LongBallSuccessConditionMultiplier: float option
      LongBallOffsideMomentum: float option
      LongBallSuccessMomentum: float option
      LongBallFailMomentum: float option
      LongBallSpeed: float option
      LongBallVz: float option
      LongBallDeflectMult: float option
      LongBallInterceptMult: float option
      LongBallPressureContrib: float option
      ForwardDepthThreshold: float option
      ForwardCreativityThreshold: float option
      LongBallScrambleJitterMult: float option
      PassLeadFactor: float option
      FlightTimePerMeter: float option
      FlightTimeMin: int option
      FlightTimeMax: int option
      LongBallFlightTimePerMeter: float option
      LongBallFlightTimeMin: int option
      LongBallFlightTimeMax: int option
      ArrivalWindow: int option }

[<CLIMutable>]
type PartialCrossConfig =
    { BaseMean: float option
      CrossingWeight: float option
      PassingWeight: float option
      SuccessShapeAlpha: float option
      SuccessConditionMultiplier: float option
      HeaderDuelSteepness: float option
      HeaderAccuracyBase: float option
      HeaderAccuracySkillMult: float option
      GkSaveBase: float option
      GkReflexesMult: float option
      GkAerialReachMult: float option
      GkJumpMult: float option
      ClaimCrossProbability: float option
      FailMomentum: float option
      Speed: float option
      Vz: float option
      AerialThreatThreshold: float option
      AttackingDepthThreshold: float option
      GkSkillDefault: float option
      GkSkillDivisor: float option
      SpinTopMult: float option
      SpinSideMult: float option
      FallbackSpeed: float option
      FallbackVz: float option }

[<CLIMutable>]
type PartialDribbleConfig =
    { TechnicalWeight: float option
      AgilityWeight: float option
      BalanceWeight: float option
      ForwardDistance: float option
      SuccessMomentum: float option
      FailMomentum: float option
      CrossProbability: float option
      PassProbability: float option
      ShotProbability: float option }

[<CLIMutable>]
type PartialTackleConfig =
    { TechnicalWeight: float option
      PositioningWeight: float option
      StrengthWeight: float option
      AggressionWeight: float option
      PositioningReduction: float option
      FoulShapeBeta: float option
      FoulMomentum: float option
      SuccessMomentum: float option
      FailMomentum: float option
      TackleSteepness: float option }

[<CLIMutable>]
type PartialSetPieceConfig =
    { FreeKickTargetX: float option
      FreeKickSpeed: float option
      FreeKickVz: float option
      FreeKickSteepness: float option
      FreeKickSavePowerThreshold: float option
      FreeKickSaveVariance: float option
      FreeKickSpinTopMult: float option
      FreeKickSpinSideMult: float option
      CornerBoxXThreshold: float option
      CornerDefenderBoxThreshold: float option
      CornerSecondPhaseProbability: float option
      CornerKeepPossessionProbability: float option
      CornerSpeed: float option
      CornerVz: float option
      CornerDensityBase: float option
      CornerDensityPenalty: float option
      CornerLogisticSteepness: float option
      CornerDefScoreDefault: float option
      ThrowInSpeed: float option
      ThrowInVz: float option
      ThrowInMomentum: float option
      PenaltySkillMultiplier: float option
      PenaltyMoraleMultiplier: float option
      PenaltyPressureMultiplier: float option
      PenaltyComposureNoise: float option
      PenaltyLogisticBase: float option
      PenaltyGkReflexesMult: float option
      PenaltyGkHandlingMult: float option
      PenaltySkillDivisor: float option
      PenaltyCondDivisor: float option
      PenaltyMoraleBase: float option
      PenaltyMoraleDivisor: float option
      PenaltyGkSkillDivisor: float option
      PenaltySpeed: float option
      PenaltyVzBase: float option
      PenaltyVzVariance: float option
      PenaltyAngleSpread: float option
      PostShotClearProbability: float option
      ClearSpeed: float option
      ClearVz: float option
      ClearYStdDev: float option
      GoalKickFallbackDistHome: float option
      GoalKickFallbackDistAway: float option
      KickOffPartnerOffsetX: float option
      KickOffPartnerOffsetY: float option
      FoulBaseRate: float option
      CornerOnFailedCross: float option
      WallMinDistance: float option
      WallPlayerSpacing: float option
      WallJumpProbabilityBase: float option
      WallBraveryWeight: float option
      WallJumpingWeight: float option
      WallSizeThreshold1: float option
      WallSizeThreshold2: float option
      WallSizeThreshold3: float option
      WallSizeThreshold4: float option
      CornerNearPostDist: float option
      CornerFarPostDist: float option
      CornerBoxDist: float option
      CornerEdgeDist: float option
      CornerStayBackCount: int option
      CornerDefNearPostDist: float option
      CornerDefFarPostDist: float option
      CornerDefBoxDist: float option
      CornerDefEdgeDist: float option
      CornerGKLineDist: float option
      ThrowInNearDist: float option
      ThrowInSupportCount: int option
      FreeKickAttackerNearDist: float option
      BallSpotInset: float option }

[<CLIMutable>]
type PartialGKConfig =
    { CatchHandlingMult: float option
      DiveReach: float option
      ParrySpeed: float option
      ParryDeflectionAngle: float option
      AerialReachMult: float option
      JumpReachMult: float option
      PunchProbability: float option
      ClaimCrossProbability: float option
      CollectionRadius: float option
      CollectionPriority: float option
      ThrowSpeed: float option
      RollSpeed: float option
      GoalKickSpeed: float option
      PuntSpeed: float option
      DistributionAccuracyMult: float option
      DistributionDecisionNoise: float option
      HoldTimeSubTicks: int option
      MaxHoldSubTicks: int option
      BackPassHandlingPenalty: float option
      GKDecisionWindowSubTicks: int option
      SaveBaseProbability: float option
      SaveReflexesWeight: float option
      SavePositioningWeight: float option
      SaveSpeedFactor: float option
      SaveAngleFactor: float option
      DistributionDecisionDelay: int option
      ShortDistributionBias: float option
      GKShortPassMaxDist: float option }

[<CLIMutable>]
type PartialHomeAdvantageConfig =
    { Strength: float option
      DuelAttackBonus: float option
      DuelDefenseBonus: float option
      ShotComposureBonus: float option
      PassAccuracyBonus: float option
      DribbleBonus: float option
      SetPlayAccuracyBonus: float option
      TackleBonus: float option
      FreeKickComposure: float option
      PenaltyBonus: float option
      CardReduction: float option
      FatigueReduction: float option }

[<CLIMutable>]
type PartialPhysicsConfig =
    { Gravity: float option
      AirDrag: float option
      GroundRestitution: float option
      GroundFriction: float option
      PostRestitution: float option
      SpinDecay: float option
      MagnusCoeff: float option
      ContactRadius: float option
      PlayerMaxForce: float option
      PlayerMassBase: float option
      PlayerMassWeightCoeff: float option
      PlayerMassStrengthCoeff: float option
      SteeringSlowRadius: float option
      SteeringFleeRadius: float option
      SteeringAlignmentWeight: float option
      CohesionWeight: float option
      TurnConstraintAgilityCoeff: float option
      TurnConstraintBaseLimit: float option
      MoveSpeedMax: float option
      MoveSpeedMin: float option
      SeparationMinDistance: float option
      BallContestSeparationRadius: float option
      SeparationStrength: float option
      SeparationAgilityMultiplier: float option
      JitterBase: float option
      JitterAgilityMultiplier: float option
      BallStopThreshold: float option
      AirborneCheckThreshold: float option
      AxisStopThreshold: float option
      AirborneRestitutionBase: float option
      AirborneRestitutionCoeff: float option
      AirborneRestitutionFloor: float option
      GroundRestitutionBase: float option
      GroundRestitutionCoeff: float option
      GroundRestitutionFloor: float option
      ChaserProximity: float option
      AirborneThreshold: float option
      ArrivalAnticipationBase: float option
      ArrivalAnticipationQuality: float option
      ArrivalCompetitionRadius: float option
      ArrivalConvergenceThreshold: float option
      ArrivalContestThreshold: float option
      ReceivingGraceSubTicks: int option }

[<CLIMutable>]
type PartialTimingConfig =
    { DuelChainDelay: TickDelay option
      DuelNextDelay: TickDelay option
      ShotDelay: TickDelay option
      FoulDelay: TickDelay option
      GoalDelay: TickDelay option
      KickOffDelay: TickDelay option
      CornerDelay: TickDelay option
      FreeKickDelay: TickDelay option
      ThrowInDelay: TickDelay option
      GoalKickDelay: TickDelay option
      InjuryDelay: TickDelay option
      ManagerReactDelay: TickDelay option
      SubsDelay: TickDelay option
      StuckBallDelay: int option
      EventWindowSubTicks: int option }

[<CLIMutable>]
type PartialMatchVolumeConfig =
    { MaxChainLength: int option
      TargetDuelTicksPerMatch: int option
      TargetShotsPerMatch: float option
      TargetDribblesPerMatch: float option
      TargetPassesPerMatch: float option
      TargetCrossesPerMatch: float option
      TargetLongBallsPerMatch: float option }

[<CLIMutable>]
type PartialManagerConfig =
    { FatigueReactionThreshold: int option
      SustainedMomentumSubTicks: int option
      MomentumThreshold: float option
      FatigueCheckSubTicks: int option
      ConditionThresholdLosing: int option
      ConditionThresholdDrawing: int option
      ConditionThresholdWinning: int option
      SubWindowMinutes: int[] option }

[<CLIMutable>]
type PartialBuildUpConfig =
    { PassSuccessBonus: float option
      DribblePenalty: float option
      LongBallPenalty: float option
      GKDistributionBonus: float option
      DCPassingBonus: float option }

[<CLIMutable>]
type PartialDecisionConfig =
    { ShootFinishingWeight: float option
      ShootLongShotsWeight: float option
      ShootComposureWeight: float option
      ShootDistNormWeight: float option
      ShootDistNormDivisor: float option
      ShootPosDirectnessWeight: float option
      ShootPosDepthWeight: float option
      ShootSTBonus: float option
      ShootDistPenaltyDivisor: float option
      ShootDistPenaltyMax: float option
      ShootDirectnessBonus: float option
      PassPassingWeight: float option
      PassVisionWeight: float option
      PassTargetBonus: float option
      PassAttackPhasePenalty: float option
      DribbleZoneBonusAttacking: float option
      DribbleZoneBonusMidfield: float option
      DribbleAttackPhaseBonus: float option
      DribbleTempoPenalty: float option
      DribblePressurePenalty: float option
      DribblePressureBlockThreshold: float option
      CrossCrossingWeight: float option
      CrossLateralTendencyWeight: float option
      CrossLateralTendencyBase: float option
      CrossZoneBonus: float option
      CrossWidthBonus: float option
      LongBallPassingWeight: float option
      LongBallVisionWeight: float option
      LongBallPressDistBase: float option
      LongBallPressMin: float option
      LongBallPressMax: float option
      LongBallPressNoOpponent: float option
      LongBallAttackPhaseBonus: float option
      LongBallDirectnessBonus: float option
      CreativityWeight: float option
      DirectnessWeight: float option
      DecisionTemperature: float option
      ShootMinThreshold: float option
      ShootDirectnessThresholdMod: float option
      PassMinThreshold: float option
      PassDirectnessThresholdMod: float option
      DribbleMinThreshold: float option
      DribbleDefZoneDirectnessMin: float option
      DribbleHighTempo: float option
      DribbleHighTempoDirectnessMin: float option
      LongBallMinThreshold: float option
      LongBallDirectnessThresholdMod: float option
      SpacePassMinScore: float option
      ShootDecisionsWeight: float option
      ShootBraveryMod: float option
      ShootConcentrationMod: float option
      PassDecisionsWeight: float option
      PassConcentrationMod: float option
      DribbleBraveryMod: float option
      ShootXGWeight: float option
      PassHighPressPenalty: float option
      DribbleAggressivePenalty: float option
      LongBallSlowTargetBonus: float option
      PassTrajectoryBonus: float option
      PersonalityFlairWeight: float option
      PersonalityTeamworkWeight: float option
      PersonalityConsistencyMod: float option
      ShootBraveryPressureMod: float option
      PassAnticipationBonus: float option }

[<CLIMutable>]
type PartialPerceptionConfig =
    { VisionRadiusBase: float option
      VisionRadiusMax: float option
      VisionConeAngle: float option
      PeripheralMultiplier: float option
      MinimumAwarenessFloor: float option
      AnticipationBonusRadius: float option
      GoalkeeperConeAngle: float option
      CommunicationRange: float option
      SetPieceSimplifiedRadius: float option
      BlindPassVisionThreshold: int option
      BlindPassComposureThreshold: int option
      BlindPassSuccessPenalty: float option }

[<CLIMutable>]
type PartialShootWeights =
    { FinishingWeight: float option
      LongShotsWeight: float option
      ComposureWeight: float option
      XGInfluence: float option
      ComposureStateMod: float option
      ConfidenceMod: float option
      FocusMod: float option
      RiskBonus: float option
      DistPenaltyDivisor: float option
      DistPenaltyMax: float option }

[<CLIMutable>]
type PartialPassWeights =
    { PassingWeight: float option
      VisionWeight: float option
      ComposureWeight: float option
      TargetBonus: float option
      AttackPhasePenalty: float option }

[<CLIMutable>]
type PartialDribbleWeights =
    { DribblingWeight: float option
      AgilityWeight: float option
      BalanceWeight: float option
      ZoneBonusAttacking: float option
      ZoneBonusMidfield: float option
      TempoPenalty: float option
      PressurePenalty: float option }

[<CLIMutable>]
type PartialCrossWeights =
    { CrossingWeight: float option
      LateralTendencyWeight: float option
      LateralTendencyBase: float option
      ZoneBonus: float option
      WidthBonus: float option }

[<CLIMutable>]
type PartialLongBallWeights =
    { LongShotsWeight: float option
      PassingWeight: float option
      VisionWeight: float option
      PressDistBase: float option
      PressMin: float option
      PressMax: float option
      PressNoOpponent: float option
      AttackPhaseBonus: float option
      DirectnessBonus: float option }

[<CLIMutable>]
type PartialIndividualWeights =
    { Shoot: PartialShootWeights option
      Pass: PartialPassWeights option
      Dribble: PartialDribbleWeights option
      Cross: PartialCrossWeights option
      LongBall: PartialLongBallWeights option
      SoftmaxTemperature: float option
      DirectnessBlendTactic: float option
      DirectnessBlendProfile: float option }

[<CLIMutable>]
type PartialDevelopmentWeights =
    { AgeBracket_MaxDelta_U21: int option
      AgeBracket_MaxDelta_U25: int option
      AgeBracket_MaxDelta_U28: int option
      AgeBracket_MaxDelta_U31: int option
      AgeBracket_MaxDelta_U34: int option
      FocusMultiplier_Goalkeeping: float option
      FocusMultiplier_PhysicalBase: float option
      FocusMultiplier_Physical_Pressing: float option
      FocusMultiplier_Physical_Positional: float option
      FocusMultiplier_Mental: float option
      FocusMultiplier_TechnicalBase: float option
      FocusMultiplier_Technical_Creativity: float option
      FocusMultiplier_Technical_Directness: float option
      WeeklyDeltaDivisor: float option
      StatFocus_DirectnessThreshold: float option
      StatFocus_AttackingDepthThreshold: float option
      StatFocus_DefensiveHeightThreshold: float option
      StatFocus_CreativityThreshold: float option
      MaybeStat_PositiveThreshold: float option
      MaybeStat_NegativeThreshold: float option }

[<CLIMutable>]
type PartialCalibrationTargets =
    { GoalsPerMatch: float option
      ShotsPerMatch: float option
      PassSuccessRate: float option
      CrossSuccessRate: float option
      HomeWinPct: float option
      DrawPct: float option
      AwayWinPct: float option
      CardsPerMatch: float option
      InjuriesPerMatch: float option
      DribblesPerMatch: float option
      CrossesPerMatch: float option
      LongBallsPerMatch: float option
      DuelTicksPerMatch: float option }

[<CLIMutable>]
type PartialPersonalityWeights =
    { FlairVisionWeight: float option
      FlairDribblingWeight: float option
      ConsistencyConcentrationWeight: float option
      ConsistencyComposureWeight: float option
      LeadershipWeight: float option
      ControversyAggressionWeight: float option
      ControversyComposureWeight: float option
      TeamworkWorkRateWeight: float option
      TeamworkPositioningWeight: float option
      AmbitionMoraleWeight: float option
      AmbitionWorkRateWeight: float option
      PressureComposureWeight: float option
      PressureConcentrationWeight: float option
      SportsmanshipAggressionWeight: float option
      TemperamentComposureWeight: float option
      TemperamentConcentrationWeight: float option }

[<CLIMutable>]
type PartialDirectiveParamsMap =
    { CompactnessSuccessDelta: float option
      CompactnessFailDelta: float option
      CompactnessSuccessThreshold: float option
      CompactnessFailThreshold: float option
      PressingSuccessDelta: float option
      PressingFailDelta: float option
      PressingSuccessThreshold: float option
      PressingFailThreshold: float option
      WingPlaySuccessDelta: float option
      WingPlayFailDelta: float option
      WingPlaySuccessThreshold: float option
      WingPlayFailThreshold: float option }

[<CLIMutable>]
type PartialEmergentWeights =
    { CompactnessSuccessDelta: float option
      CompactnessFailDelta: float option
      CompactnessSuccessThreshold: float option
      CompactnessFailThreshold: float option
      PressingSuccessDelta: float option
      PressingFailDelta: float option
      PressingSuccessThreshold: float option
      PressingFailThreshold: float option
      WingPlaySuccessDelta: float option
      WingPlayFailDelta: float option
      WingPlaySuccessThreshold: float option
      WingPlayFailThreshold: float option
      ConsecutiveLossPenalty: float option
      FatigueSpiralCompactnessFactor: float option
      FatigueSpiralPressingFactor: float option
      FatigueSpiralTempoFactor: float option
      FatigueSpiralRiskFactor: float option
      FatigueSpiralThreshold: float option }

[<CLIMutable>]
type PartialModifierWeights =
    { TransitionNearMult: float option
      TransitionFarMult: float option
      TransitionNearDistance: float option
      WeaknessSupportMult: float option
      RestDefenseSupportMult: float option
      RestDefenseCoverMult: float option
      ThreatCoverMult: float option
      HighLineSupportMult: float option
      LowBlockPressMult: float option
      LowBlockCoverMult: float option
      UrgencyPressMult: float option
      UrgencySupportMult: float option
      UrgencyThreshold: float option }

[<CLIMutable>]
type PartialChemistryWeights =
    { FamiliarityPassBonus: float option
      FamiliarityFailPenalty: float option
      FamiliarityTimeBonus: float option
      PressingCoordinationBase: float option
      PressingCoordinationFamiliarityMult: float option
      TransitionSpeedBase: float option
      TransitionSpeedFamiliarityMult: float option }

[<CLIMutable>]
type PartialTeamDirectorWeights =
    { WorkRateWeight: float option
      PositioningWeight: float option
      AdvancedBonus: float option }

[<CLIMutable>]
type PartialReactiveLoopWeights =
    { ShapeDevWeight: float option
      PressDevWeight: float option
      FatigueDevWeight: float option
      OnTrackThreshold: float option
      DriftingThreshold: float option }

[<CLIMutable>]
type PartialCollectiveWeights =
    { DirectiveParams: PartialDirectiveParamsMap option
      Emergent: PartialEmergentWeights option
      Modifiers: PartialModifierWeights option
      Chemistry: PartialChemistryWeights option
      TeamDirector: PartialTeamDirectorWeights option
      ReactiveLoop: PartialReactiveLoopWeights option }

[<CLIMutable>]
type PartialXGWeights =
    { DistanceFactor: float option
      AngleExponent: float option
      BaseMultiplier: float option
      OneOnOneMultiplier: float option
      SetPieceMultiplier: float option
      PressureReduction: float option
      HeaderMultiplier: float option
      VolleyMultiplier: float option
      HalfVolleyMultiplier: float option
      ChipShotMultiplier: float option
      CurlerMultiplier: float option
      DrivenShotMultiplier: float option
      PlacedShotMultiplier: float option
      FirstTimeShotMultiplier: float option }

[<CLIMutable>]
type PartialInterceptionWeights =
    { BallControlRadiusMult: float option
      PressIntentFactor: float option
      RecoverIntentFactor: float option
      MaintainShapeIntentFactor: float option
      CoverSpaceIntentFactor: float option }

[<CLIMutable>]
type PartialWinProbabilityWeights =
    { GoalLeadBase: float option
      DrawBase: float option
      GoalDiffFactor: float option
      XGFactor: float option
      HomeAdvantage: float option
      GoalDiffSteepness: float option
      XGDiffSteepness: float option
      MinutePressure: float option
      ComebackBonus: float option
      MomentumPositiveThreshold: float option
      MomentumNegativeThreshold: float option
      MomentumPositiveBonus: float option
      MomentumNegativePenalty: float option
      MomentumLinearFactor: float option }

[<CLIMutable>]
type PartialUtilityWeights =
    { PressZoneBonus_HighAttacking: float option
      PressZoneBonus_HighMidfield: float option
      PressZoneBonus_HighDefensive: float option
      PressZoneBonus_MidAttackingMidfield: float option
      PressZoneBonus_MidDefensive: float option
      PressZoneBonus_Low: float option
      PossessionChangeWindow: float option
      ScoreDiffPressStep: float option
      WingSpaceBase: float option
      StaminaWingMult: float option
      StructuredBase: float option
      DirectiveChangeThreshold: float option
      DropDeepHighLinePenalty: float option
      DropDeepLeadBonus: float option
      DropDeepTimeBonus: float option
      DropDeepBase: float option
      CounterPressStaminaFactor: float option
      CounterPressIntensityBonus: float option
      CounterPressBase: float option
      BuildFromBackNoPressBonus: float option
      BuildFromBackMidPressBonus: float option
      BuildFromBackHighPressPenalty: float option
      BuildFromBackLowBlockBonus: float option
      BuildFromBackBase: float option
      DirectPlayUrgencyBonus: float option
      DirectPlayUrgencyBonusAny: float option
      DirectPlayHighLineBonus: float option
      DirectPlayBase: float option
      SitAndCounterBase: float option
      SitAndCounterLeadBonus: float option
      SitAndCounterStaminaFactor: float option
      HoldPossessionLeadBonus: float option
      HoldPossessionDrawBonus: float option
      HoldPossessionLosingPenalty: float option
      HoldPossessionTimeBonus: float option
      HoldPossessionPressPenalty: float option
      HoldPossessionBase: float option
      CompactBlockLosingBonus: float option
      CompactBlockWinningPenalty: float option
      CompactBlockOpponentBonus: float option
      CompactBlockTimeBonus: float option
      CompactBlockBase: float option
      HighLineCohesionBonus: float option
      HighLineStaminaFactor: float option
      HighLineRiskPenalty: float option
      HighLineBase: float option
      PressingSuccessBonus: float option
      OpponentHighLineNoPressBonus: float option
      OverloadWeaknessBonus: float option
      OverloadFlankBase: float option }

[<CLIMutable>]
type PartialPerformanceWeightsMap =
    { DuelStatWeight: float option
      DuelConditionWeight: float option
      DuelMoraleWeight: float option
      DuelCurveSteepness: float option
      DuelCurveInflection: float option
      TechnicalStatWeight: float option
      TechnicalConditionWeight: float option
      TechnicalMoraleWeight: float option
      TechnicalCurveSteepness: float option
      TechnicalCurveInflection: float option
      DecisionStatWeight: float option
      DecisionConditionWeight: float option
      DecisionMoraleWeight: float option
      DecisionCurveSteepness: float option
      DecisionCurveInflection: float option }

[<CLIMutable>]
type PartialMatchSituationConfig =
    { ShootLosingLateMod: float option
      ShootDrawLateMod: float option
      ShootMomentumScale: float option
      PassWinningMod: float option
      PassTransitionPenalty: float option
      DribbleTransitionMod: float option
      LongBallLosingLateMod: float option
      LongBallPressureThreshold: float option
      LongBallPressureMod: float option }

[<CLIMutable>]
type PartialRefereeWeights =
    { CardBaseProb: float option
      CardAggressionMult: float option
      CardHomeReduction: float option
      InjuryBaseProb: float option
      InjuryStrengthInverseMult: float option
      FoulAggressionBase: float option
      FoulAggressionMult: float option
      HomeBonusCardThreshold: float option
      MatchImportanceWeight: float option
      DisciplineHistoryMitigation: float option
      AdvantageWindowSeconds: float option
      AdvantageCallBackWindow: float option }

[<CLIMutable>]
type PartialEnvironmentWeights =
    { WeatherClearModifier: float option
      WeatherLightRainModifier: float option
      WeatherHeavyRainModifier: float option
      WeatherSnowModifier: float option
      WeatherWindyModifier: float option
      PitchDrySlipBase: float option
      PitchDampSlipBase: float option
      PitchWetSlipBase: float option
      PitchWaterloggedSlipBase: float option
      SlipAgilityReduction: float option
      CrowdMaxCapacity: float option
      CrowdCapacityWeight: float option
      CrowdSupportWeight: float option
      CrowdMomentumWeight: float option
      CrowdImportanceWeight: float option
      CrowdMaxAdvantage: float option
      AwayPressureCrowdMult: float option }

[<CLIMutable>]
type PartialMomentumWeights =
    { EventDelta: float option
      Decay: float option
      Min: float option
      Max: float option
      HalfLifeSeconds: float option }

[<CLIMutable>]
type PartialProfileWeights =
    { PositionalFreedom_PositioningWeight: float option
      PositionalFreedom_VisionWeight: float option
      PositionalFreedom_StaminaWeight: float option
      PositionalFreedom_ConcentrationWeight: float option
      PositionalFreedom_BalanceWeight: float option
      PositionalFreedom_AgilityWeight: float option
      AttackingDepth_PaceWeight: float option
      AttackingDepth_AccelerationWeight: float option
      AttackingDepth_FinishingWeight: float option
      AttackingDepth_ComposureWeight: float option
      AttackingDepth_StaminaWeight: float option
      AttackingDepth_PositionBaseMultiplier: float option
      LateralTendency_CrossingWeight: float option
      LateralTendency_PaceWeight: float option
      DefensiveHeight_WorkRateWeight: float option
      DefensiveHeight_TacklingWeight: float option
      DefensiveHeight_PositioningWeight: float option
      DefensiveHeight_StaminaWeight: float option
      DefensiveHeight_PositionBaseMultiplier: float option
      PressingIntensity_StaminaWeight: float option
      PressingIntensity_WorkRateWeight: float option
      PressingIntensity_AggressionWeight: float option
      PressingIntensity_PaceWeight: float option
      PressingIntensity_ConcentrationWeight: float option
      RiskAppetite_PassingWeight: float option
      RiskAppetite_LongShotsWeight: float option
      RiskAppetite_VisionWeight: float option
      RiskAppetite_ComposureWeight: float option
      RiskAppetite_DribblingWeight: float option
      RiskAppetite_BraveryWeight: float option
      Directness_FinishingWeight: float option
      Directness_PaceWeight: float option
      Directness_AccelerationWeight: float option
      Directness_AggressionWeight: float option
      Directness_DribblingWeight: float option
      Directness_StrengthWeight: float option
      Directness_InversePassingWeight: float option
      CreativityWeight_PassingWeight: float option
      CreativityWeight_VisionWeight: float option
      CreativityWeight_BallControlWeight: float option
      CreativityWeight_DribblingWeight: float option
      CreativityWeight_ComposureWeight: float option
      CreativityWeight_CrossingWeight: float option
      AerialThreat_JumpingReachWeight: float option
      AerialThreat_HeadingWeight: float option
      AerialThreat_StrengthWeight: float option
      AerialThreat_BraveryWeight: float option
      HoldUpPlay_StrengthWeight: float option
      HoldUpPlay_BallControlWeight: float option
      HoldUpPlay_ComposureWeight: float option
      HoldUpPlay_PassingWeight: float option
      HoldUpPlay_HeadingWeight: float option
      HoldUpPlay_BalanceWeight: float option }

[<CLIMutable>]
type PartialBalanceConfig =
    { Duel: PartialDuelConfig option
      Shot: PartialShotConfig option
      Pass: PartialPassConfig option
      Cross: PartialCrossConfig option
      Dribble: PartialDribbleConfig option
      Tackle: PartialTackleConfig option
      SetPiece: PartialSetPieceConfig option
      GK: PartialGKConfig option
      HomeAdvantage: PartialHomeAdvantageConfig option
      Physics: PartialPhysicsConfig option
      Timing: PartialTimingConfig option
      MatchVolume: PartialMatchVolumeConfig option
      Manager: PartialManagerConfig option
      BuildUp: PartialBuildUpConfig option
      Decision: PartialDecisionConfig option
      Perception: PartialPerceptionConfig option
      Individual: PartialIndividualWeights option
      ProfileWeights: PartialProfileWeights option
      Development: PartialDevelopmentWeights option
      CalibrationTargets: PartialCalibrationTargets option
      Collective: PartialCollectiveWeights option
      Personality: PartialPersonalityWeights option
      Utility: PartialUtilityWeights option
      Referee: PartialRefereeWeights option
      Environment: PartialEnvironmentWeights option
      Momentum: PartialMomentumWeights option
      XG: PartialXGWeights option
      Interception: PartialInterceptionWeights option
      WinProbability: PartialWinProbabilityWeights option
      Performance: PartialPerformanceWeightsMap option
      MatchSituation: PartialMatchSituationConfig option }

// ============================================================================
// Merge helpers
// ============================================================================

module private Merger =

    let opt d =
        function
        | None -> d
        | Some v -> v

    let inline optUnit (d: float<'u>) (p: float option) : float<'u> =
        match p with
        | None -> d
        | Some v -> v * LanguagePrimitives.FloatWithMeasure<'u> 1.0

    let inline optInt (d: int<'u>) (p: int option) : int<'u> =
        match p with
        | None -> d
        | Some v -> v * LanguagePrimitives.Int32WithMeasure<'u> 1

    let mergeDuel (p: PartialDuelConfig option) (d: DuelConfig) : DuelConfig =
        match p with
        | None -> d
        | Some p ->

            { DuelSteepness = opt d.DuelSteepness p.DuelSteepness
              MomentumBonus = opt d.MomentumBonus p.MomentumBonus
              JitterWin = opt d.JitterWin p.JitterWin
              JitterRecover = opt d.JitterRecover p.JitterRecover
              JitterKeep = opt d.JitterKeep p.JitterKeep
              SpeedKeep = optUnit d.SpeedKeep p.SpeedKeep
              SpeedKeepVz = optUnit d.SpeedKeepVz p.SpeedKeepVz
              AttackerDribblingWeight = opt d.AttackerDribblingWeight p.AttackerDribblingWeight
              AttackerAgilityWeight = opt d.AttackerAgilityWeight p.AttackerAgilityWeight
              AttackerBalanceWeight = opt d.AttackerBalanceWeight p.AttackerBalanceWeight
              DefenderTacklingWeight = opt d.DefenderTacklingWeight p.DefenderTacklingWeight
              DefenderStrengthWeight = opt d.DefenderStrengthWeight p.DefenderStrengthWeight
              DefenderPositionWeight = opt d.DefenderPositionWeight p.DefenderPositionWeight
              FatigueThreshold = opt d.FatigueThreshold p.FatigueThreshold
              FatigueDecay = opt d.FatigueDecay p.FatigueDecay }

    let mergeShot (p: PartialShotConfig option) (d: ShotConfig) : ShotConfig =
        match p with
        | None -> d
        | Some p ->

            { QualityGate = opt d.QualityGate p.QualityGate
              AngleSpreadBase = opt d.AngleSpreadBase p.AngleSpreadBase
              VzBase = optUnit d.VzBase p.VzBase
              VzVariance = opt d.VzVariance p.VzVariance
              NormalisationDistance = optUnit d.NormalisationDistance p.NormalisationDistance
              DistanceToGoalMultiplier = opt d.DistanceToGoalMultiplier p.DistanceToGoalMultiplier
              FinishingMin = opt d.FinishingMin p.FinishingMin
              FinishingMax = opt d.FinishingMax p.FinishingMax
              FinishingBonusST = opt d.FinishingBonusST p.FinishingBonusST
              FinishingBonusAM = opt d.FinishingBonusAM p.FinishingBonusAM
              FinishingBonusMC = opt d.FinishingBonusMC p.FinishingBonusMC
              FinishingBonusOther = opt d.FinishingBonusOther p.FinishingBonusOther
              CondFactorDivisor = opt d.CondFactorDivisor p.CondFactorDivisor
              ComposureMultiplier = opt d.ComposureMultiplier p.ComposureMultiplier
              UrgencyMultiplier = opt d.UrgencyMultiplier p.UrgencyMultiplier
              BasePowerDivisor = opt d.BasePowerDivisor p.BasePowerDivisor
              SpinTopMultiplier = opt d.SpinTopMultiplier p.SpinTopMultiplier
              PositionDirectnessWeight = opt d.PositionDirectnessWeight p.PositionDirectnessWeight
              PositionDepthWeight = opt d.PositionDepthWeight p.PositionDepthWeight
              PositionCreativityWeight = opt d.PositionCreativityWeight p.PositionCreativityWeight
              DistNormWeight = opt d.DistNormWeight p.DistNormWeight
              PositionBonusWeight = opt d.PositionBonusWeight p.PositionBonusWeight
              HeavyTouchDivisor = opt d.HeavyTouchDivisor p.HeavyTouchDivisor
              HeavyTouchMultiplier = opt d.HeavyTouchMultiplier p.HeavyTouchMultiplier
              JitterStdDev = opt d.JitterStdDev p.JitterStdDev
              GkReflexesStatMult = opt d.GkReflexesStatMult p.GkReflexesStatMult
              GkOneOnOneStatMult = opt d.GkOneOnOneStatMult p.GkOneOnOneStatMult
              SaveDenominatorOffset = opt d.SaveDenominatorOffset p.SaveDenominatorOffset
              ShotWideMargin = optUnit d.ShotWideMargin p.ShotWideMargin }

    let mergePass (p: PartialPassConfig option) (d: PassConfig) : PassConfig =
        match p with
        | None -> d
        | Some p ->

            { BaseMean = opt d.BaseMean p.BaseMean
              DistancePenaltyPerMeter = opt d.DistancePenaltyPerMeter p.DistancePenaltyPerMeter
              LongPassPenaltyPerMeter = opt d.LongPassPenaltyPerMeter p.LongPassPenaltyPerMeter
              TechnicalWeight = opt d.TechnicalWeight p.TechnicalWeight
              VisionWeight = opt d.VisionWeight p.VisionWeight
              SuccessShapeAlpha = opt d.SuccessShapeAlpha p.SuccessShapeAlpha
              SuccessConditionMultiplier = opt d.SuccessConditionMultiplier p.SuccessConditionMultiplier
              OffsideMomentum = opt d.OffsideMomentum p.OffsideMomentum
              SuccessMomentum = opt d.SuccessMomentum p.SuccessMomentum
              FailMomentum = opt d.FailMomentum p.FailMomentum
              DeflectBaseRate = opt d.DeflectBaseRate p.DeflectBaseRate
              MisplacedBaseRate = opt d.MisplacedBaseRate p.MisplacedBaseRate
              InterceptBaseRate = opt d.InterceptBaseRate p.InterceptBaseRate
              InterceptionRadius = optUnit d.InterceptionRadius p.InterceptionRadius
              PressureDistance = optUnit d.PressureDistance p.PressureDistance
              DeflectPressureMultiplier = opt d.DeflectPressureMultiplier p.DeflectPressureMultiplier
              InterceptPaceWeight = opt d.InterceptPaceWeight p.InterceptPaceWeight
              InterceptPositioningWeight = opt d.InterceptPositioningWeight p.InterceptPositioningWeight
              ScrambleJitter = optUnit d.ScrambleJitter p.ScrambleJitter
              Speed = optUnit d.Speed p.Speed
              Vz = optUnit d.Vz p.Vz
              InterceptDistFactorWeight = opt d.InterceptDistFactorWeight p.InterceptDistFactorWeight
              InterceptPositioningContrib = opt d.InterceptPositioningContrib p.InterceptPositioningContrib
              InterceptVisionContrib = opt d.InterceptVisionContrib p.InterceptVisionContrib
              CreativityWeight = opt d.CreativityWeight p.CreativityWeight
              DirectnessWeight = opt d.DirectnessWeight p.DirectnessWeight
              MeanMin = opt d.MeanMin p.MeanMin
              MeanMax = opt d.MeanMax p.MeanMax
              DefaultNearestDefDist = optUnit d.DefaultNearestDefDist p.DefaultNearestDefDist
              DefaultTackling = opt d.DefaultTackling p.DefaultTackling
              HeavyTouchDivisor = opt d.HeavyTouchDivisor p.HeavyTouchDivisor
              HeavyTouchMultiplier = opt d.HeavyTouchMultiplier p.HeavyTouchMultiplier
              JitterStdDev = opt d.JitterStdDev p.JitterStdDev
              DeflectedSpeedMult = opt d.DeflectedSpeedMult p.DeflectedSpeedMult
              DeflectedVzMult = opt d.DeflectedVzMult p.DeflectedVzMult
              InterceptProbMax = opt d.InterceptProbMax p.InterceptProbMax
              MisplacedSpeedMult = opt d.MisplacedSpeedMult p.MisplacedSpeedMult
              LongBallBaseMean = opt d.LongBallBaseMean p.LongBallBaseMean
              LongBallLongShotsWeight = opt d.LongBallLongShotsWeight p.LongBallLongShotsWeight
              LongBallPassingWeight = opt d.LongBallPassingWeight p.LongBallPassingWeight
              LongBallVisionWeight = opt d.LongBallVisionWeight p.LongBallVisionWeight
              LongBallSuccessShapeAlpha = opt d.LongBallSuccessShapeAlpha p.LongBallSuccessShapeAlpha
              LongBallSuccessConditionMultiplier =
                opt d.LongBallSuccessConditionMultiplier p.LongBallSuccessConditionMultiplier
              LongBallOffsideMomentum = opt d.LongBallOffsideMomentum p.LongBallOffsideMomentum
              LongBallSuccessMomentum = opt d.LongBallSuccessMomentum p.LongBallSuccessMomentum
              LongBallFailMomentum = opt d.LongBallFailMomentum p.LongBallFailMomentum
              LongBallSpeed = optUnit d.LongBallSpeed p.LongBallSpeed
              LongBallVz = optUnit d.LongBallVz p.LongBallVz
              LongBallDeflectMult = opt d.LongBallDeflectMult p.LongBallDeflectMult
              LongBallInterceptMult = opt d.LongBallInterceptMult p.LongBallInterceptMult
              LongBallPressureContrib = opt d.LongBallPressureContrib p.LongBallPressureContrib
              ForwardDepthThreshold = opt d.ForwardDepthThreshold p.ForwardDepthThreshold
              ForwardCreativityThreshold = opt d.ForwardCreativityThreshold p.ForwardCreativityThreshold
              LongBallScrambleJitterMult = opt d.LongBallScrambleJitterMult p.LongBallScrambleJitterMult
              PassLeadFactor = opt d.PassLeadFactor p.PassLeadFactor
              FlightTimePerMeter = opt d.FlightTimePerMeter p.FlightTimePerMeter
              FlightTimeMin = optInt d.FlightTimeMin p.FlightTimeMin
              FlightTimeMax = optInt d.FlightTimeMax p.FlightTimeMax
              LongBallFlightTimePerMeter = opt d.LongBallFlightTimePerMeter p.LongBallFlightTimePerMeter
              LongBallFlightTimeMin = optInt d.LongBallFlightTimeMin p.LongBallFlightTimeMin
              LongBallFlightTimeMax = optInt d.LongBallFlightTimeMax p.LongBallFlightTimeMax
              ArrivalWindow = optInt d.ArrivalWindow p.ArrivalWindow }

    let mergeCross (p: PartialCrossConfig option) (d: CrossConfig) : CrossConfig =
        match p with
        | None -> d
        | Some p ->

            { BaseMean = opt d.BaseMean p.BaseMean
              CrossingWeight = opt d.CrossingWeight p.CrossingWeight
              PassingWeight = opt d.PassingWeight p.PassingWeight
              SuccessShapeAlpha = opt d.SuccessShapeAlpha p.SuccessShapeAlpha
              SuccessConditionMultiplier = opt d.SuccessConditionMultiplier p.SuccessConditionMultiplier
              HeaderDuelSteepness = opt d.HeaderDuelSteepness p.HeaderDuelSteepness
              HeaderAccuracyBase = opt d.HeaderAccuracyBase p.HeaderAccuracyBase
              HeaderAccuracySkillMult = opt d.HeaderAccuracySkillMult p.HeaderAccuracySkillMult
              GkSaveBase = opt d.GkSaveBase p.GkSaveBase
              GkReflexesMult = opt d.GkReflexesMult p.GkReflexesMult
              GkAerialReachMult = opt d.GkAerialReachMult p.GkAerialReachMult
              GkJumpMult = opt d.GkJumpMult p.GkJumpMult
              ClaimCrossProbability = opt d.ClaimCrossProbability p.ClaimCrossProbability
              FailMomentum = opt d.FailMomentum p.FailMomentum
              Speed = optUnit d.Speed p.Speed
              Vz = optUnit d.Vz p.Vz
              AerialThreatThreshold = opt d.AerialThreatThreshold p.AerialThreatThreshold
              AttackingDepthThreshold = opt d.AttackingDepthThreshold p.AttackingDepthThreshold
              GkSkillDefault = opt d.GkSkillDefault p.GkSkillDefault
              GkSkillDivisor = opt d.GkSkillDivisor p.GkSkillDivisor
              SpinTopMult = opt d.SpinTopMult p.SpinTopMult
              SpinSideMult = opt d.SpinSideMult p.SpinSideMult
              FallbackSpeed = optUnit d.FallbackSpeed p.FallbackSpeed
              FallbackVz = optUnit d.FallbackVz p.FallbackVz }

    let mergeDribble (p: PartialDribbleConfig option) (d: DribbleConfig) : DribbleConfig =
        match p with
        | None -> d
        | Some p ->

            { TechnicalWeight = opt d.TechnicalWeight p.TechnicalWeight
              AgilityWeight = opt d.AgilityWeight p.AgilityWeight
              BalanceWeight = opt d.BalanceWeight p.BalanceWeight
              ForwardDistance = opt d.ForwardDistance p.ForwardDistance
              SuccessMomentum = opt d.SuccessMomentum p.SuccessMomentum
              FailMomentum = opt d.FailMomentum p.FailMomentum
              CrossProbability = opt d.CrossProbability p.CrossProbability
              PassProbability = opt d.PassProbability p.PassProbability
              ShotProbability = opt d.ShotProbability p.ShotProbability }

    let mergeTackle (p: PartialTackleConfig option) (d: TackleConfig) : TackleConfig =
        match p with
        | None -> d
        | Some p ->

            { TechnicalWeight = opt d.TechnicalWeight p.TechnicalWeight
              PositioningWeight = opt d.PositioningWeight p.PositioningWeight
              StrengthWeight = opt d.StrengthWeight p.StrengthWeight
              AggressionWeight = opt d.AggressionWeight p.AggressionWeight
              PositioningReduction = opt d.PositioningReduction p.PositioningReduction
              FoulShapeBeta = opt d.FoulShapeBeta p.FoulShapeBeta
              FoulMomentum = opt d.FoulMomentum p.FoulMomentum
              SuccessMomentum = opt d.SuccessMomentum p.SuccessMomentum
              FailMomentum = opt d.FailMomentum p.FailMomentum
              TackleSteepness = opt d.TackleSteepness p.TackleSteepness }

    let mergeSetPiece (p: PartialSetPieceConfig option) (d: SetPieceConfig) : SetPieceConfig =
        match p with
        | None -> d
        | Some p ->

            { FreeKickTargetX = optUnit d.FreeKickTargetX p.FreeKickTargetX
              FreeKickSpeed = optUnit d.FreeKickSpeed p.FreeKickSpeed
              FreeKickVz = optUnit d.FreeKickVz p.FreeKickVz
              FreeKickSteepness = opt d.FreeKickSteepness p.FreeKickSteepness
              FreeKickSavePowerThreshold = opt d.FreeKickSavePowerThreshold p.FreeKickSavePowerThreshold
              FreeKickSaveVariance = opt d.FreeKickSaveVariance p.FreeKickSaveVariance
              FreeKickSpinTopMult = opt d.FreeKickSpinTopMult p.FreeKickSpinTopMult
              FreeKickSpinSideMult = opt d.FreeKickSpinSideMult p.FreeKickSpinSideMult
              CornerBoxXThreshold = optUnit d.CornerBoxXThreshold p.CornerBoxXThreshold
              CornerDefenderBoxThreshold = optUnit d.CornerDefenderBoxThreshold p.CornerDefenderBoxThreshold
              CornerSecondPhaseProbability = opt d.CornerSecondPhaseProbability p.CornerSecondPhaseProbability
              CornerKeepPossessionProbability = opt d.CornerKeepPossessionProbability p.CornerKeepPossessionProbability
              CornerSpeed = optUnit d.CornerSpeed p.CornerSpeed
              CornerVz = optUnit d.CornerVz p.CornerVz
              CornerDensityBase = opt d.CornerDensityBase p.CornerDensityBase
              CornerDensityPenalty = opt d.CornerDensityPenalty p.CornerDensityPenalty
              CornerLogisticSteepness = opt d.CornerLogisticSteepness p.CornerLogisticSteepness
              CornerDefScoreDefault = opt d.CornerDefScoreDefault p.CornerDefScoreDefault
              ThrowInSpeed = optUnit d.ThrowInSpeed p.ThrowInSpeed
              ThrowInVz = optUnit d.ThrowInVz p.ThrowInVz
              ThrowInMomentum = opt d.ThrowInMomentum p.ThrowInMomentum
              PenaltySkillMultiplier = opt d.PenaltySkillMultiplier p.PenaltySkillMultiplier
              PenaltyMoraleMultiplier = opt d.PenaltyMoraleMultiplier p.PenaltyMoraleMultiplier
              PenaltyPressureMultiplier = opt d.PenaltyPressureMultiplier p.PenaltyPressureMultiplier
              PenaltyComposureNoise = opt d.PenaltyComposureNoise p.PenaltyComposureNoise
              PenaltyLogisticBase = opt d.PenaltyLogisticBase p.PenaltyLogisticBase
              PenaltyGkReflexesMult = opt d.PenaltyGkReflexesMult p.PenaltyGkReflexesMult
              PenaltyGkHandlingMult = opt d.PenaltyGkHandlingMult p.PenaltyGkHandlingMult
              PenaltySkillDivisor = opt d.PenaltySkillDivisor p.PenaltySkillDivisor
              PenaltyCondDivisor = opt d.PenaltyCondDivisor p.PenaltyCondDivisor
              PenaltyMoraleBase = opt d.PenaltyMoraleBase p.PenaltyMoraleBase
              PenaltyMoraleDivisor = opt d.PenaltyMoraleDivisor p.PenaltyMoraleDivisor
              PenaltyGkSkillDivisor = opt d.PenaltyGkSkillDivisor p.PenaltyGkSkillDivisor
              PenaltySpeed = optUnit d.PenaltySpeed p.PenaltySpeed
              PenaltyVzBase = optUnit d.PenaltyVzBase p.PenaltyVzBase
              PenaltyVzVariance = opt d.PenaltyVzVariance p.PenaltyVzVariance
              PenaltyAngleSpread = opt d.PenaltyAngleSpread p.PenaltyAngleSpread
              PostShotClearProbability = opt d.PostShotClearProbability p.PostShotClearProbability
              ClearSpeed = optUnit d.ClearSpeed p.ClearSpeed
              ClearVz = optUnit d.ClearVz p.ClearVz
              ClearYStdDev = opt d.ClearYStdDev p.ClearYStdDev
              GoalKickFallbackDistHome = optUnit d.GoalKickFallbackDistHome p.GoalKickFallbackDistHome
              GoalKickFallbackDistAway = optUnit d.GoalKickFallbackDistAway p.GoalKickFallbackDistAway
              KickOffPartnerOffsetX = optUnit d.KickOffPartnerOffsetX p.KickOffPartnerOffsetX
              KickOffPartnerOffsetY = optUnit d.KickOffPartnerOffsetY p.KickOffPartnerOffsetY
              FoulBaseRate = opt d.FoulBaseRate p.FoulBaseRate
              CornerOnFailedCross = opt d.CornerOnFailedCross p.CornerOnFailedCross
              WallMinDistance = optUnit d.WallMinDistance p.WallMinDistance
              WallPlayerSpacing = optUnit d.WallPlayerSpacing p.WallPlayerSpacing
              WallJumpProbabilityBase = opt d.WallJumpProbabilityBase p.WallJumpProbabilityBase
              WallBraveryWeight = opt d.WallBraveryWeight p.WallBraveryWeight
              WallJumpingWeight = opt d.WallJumpingWeight p.WallJumpingWeight
              WallSizeThreshold1 = optUnit d.WallSizeThreshold1 p.WallSizeThreshold1
              WallSizeThreshold2 = optUnit d.WallSizeThreshold2 p.WallSizeThreshold2
              WallSizeThreshold3 = optUnit d.WallSizeThreshold3 p.WallSizeThreshold3
              WallSizeThreshold4 = optUnit d.WallSizeThreshold4 p.WallSizeThreshold4
              CornerNearPostDist = optUnit d.CornerNearPostDist p.CornerNearPostDist
              CornerFarPostDist = optUnit d.CornerFarPostDist p.CornerFarPostDist
              CornerBoxDist = optUnit d.CornerBoxDist p.CornerBoxDist
              CornerEdgeDist = optUnit d.CornerEdgeDist p.CornerEdgeDist
              CornerStayBackCount = opt d.CornerStayBackCount p.CornerStayBackCount
              CornerDefNearPostDist = optUnit d.CornerDefNearPostDist p.CornerDefNearPostDist
              CornerDefFarPostDist = optUnit d.CornerDefFarPostDist p.CornerDefFarPostDist
              CornerDefBoxDist = optUnit d.CornerDefBoxDist p.CornerDefBoxDist
              CornerDefEdgeDist = optUnit d.CornerDefEdgeDist p.CornerDefEdgeDist
              CornerGKLineDist = optUnit d.CornerGKLineDist p.CornerGKLineDist
              ThrowInNearDist = optUnit d.ThrowInNearDist p.ThrowInNearDist
              ThrowInSupportCount = opt d.ThrowInSupportCount p.ThrowInSupportCount
              FreeKickAttackerNearDist = optUnit d.FreeKickAttackerNearDist p.FreeKickAttackerNearDist
              BallSpotInset = optUnit d.BallSpotInset p.BallSpotInset }

    let mergeGK (p: PartialGKConfig option) (d: GKConfig) : GKConfig =
        match p with
        | None -> d
        | Some p ->

            { CatchHandlingMult = opt d.CatchHandlingMult p.CatchHandlingMult
              DiveReach = optUnit d.DiveReach p.DiveReach
              ParrySpeed = optUnit d.ParrySpeed p.ParrySpeed
              ParryDeflectionAngle = opt d.ParryDeflectionAngle p.ParryDeflectionAngle
              AerialReachMult = opt d.AerialReachMult p.AerialReachMult
              JumpReachMult = opt d.JumpReachMult p.JumpReachMult
              PunchProbability = opt d.PunchProbability p.PunchProbability
              ClaimCrossProbability = opt d.ClaimCrossProbability p.ClaimCrossProbability
              CollectionRadius = optUnit d.CollectionRadius p.CollectionRadius
              CollectionPriority = opt d.CollectionPriority p.CollectionPriority
              ThrowSpeed = optUnit d.ThrowSpeed p.ThrowSpeed
              RollSpeed = optUnit d.RollSpeed p.RollSpeed
              GoalKickSpeed = optUnit d.GoalKickSpeed p.GoalKickSpeed
              PuntSpeed = optUnit d.PuntSpeed p.PuntSpeed
              DistributionAccuracyMult = opt d.DistributionAccuracyMult p.DistributionAccuracyMult
              DistributionDecisionNoise = opt d.DistributionDecisionNoise p.DistributionDecisionNoise
              HoldTimeSubTicks = optInt d.HoldTimeSubTicks p.HoldTimeSubTicks
              MaxHoldSubTicks = optInt d.MaxHoldSubTicks p.MaxHoldSubTicks
              BackPassHandlingPenalty = opt d.BackPassHandlingPenalty p.BackPassHandlingPenalty
              GKDecisionWindowSubTicks = optInt d.GKDecisionWindowSubTicks p.GKDecisionWindowSubTicks
              SaveBaseProbability = opt d.SaveBaseProbability p.SaveBaseProbability
              SaveReflexesWeight = opt d.SaveReflexesWeight p.SaveReflexesWeight
              SavePositioningWeight = opt d.SavePositioningWeight p.SavePositioningWeight
              SaveSpeedFactor = opt d.SaveSpeedFactor p.SaveSpeedFactor
              SaveAngleFactor = opt d.SaveAngleFactor p.SaveAngleFactor
              DistributionDecisionDelay = optInt d.DistributionDecisionDelay p.DistributionDecisionDelay
              ShortDistributionBias = opt d.ShortDistributionBias p.ShortDistributionBias
              GKShortPassMaxDist = optUnit d.GKShortPassMaxDist p.GKShortPassMaxDist }

    let mergeHomeAdv (p: PartialHomeAdvantageConfig option) (d: HomeAdvantageConfig) : HomeAdvantageConfig =
        match p with
        | None -> d
        | Some p ->

            { Strength = opt d.Strength p.Strength
              DuelAttackBonus = opt d.DuelAttackBonus p.DuelAttackBonus
              DuelDefenseBonus = opt d.DuelDefenseBonus p.DuelDefenseBonus
              ShotComposureBonus = opt d.ShotComposureBonus p.ShotComposureBonus
              PassAccuracyBonus = opt d.PassAccuracyBonus p.PassAccuracyBonus
              DribbleBonus = opt d.DribbleBonus p.DribbleBonus
              SetPlayAccuracyBonus = opt d.SetPlayAccuracyBonus p.SetPlayAccuracyBonus
              TackleBonus = opt d.TackleBonus p.TackleBonus
              FreeKickComposure = opt d.FreeKickComposure p.FreeKickComposure
              PenaltyBonus = opt d.PenaltyBonus p.PenaltyBonus
              CardReduction = opt d.CardReduction p.CardReduction
              FatigueReduction = opt d.FatigueReduction p.FatigueReduction }

    let mergePhysics (p: PartialPhysicsConfig option) (d: PhysicsConfig) : PhysicsConfig =
        match p with
        | None -> d
        | Some p ->

            { Gravity = optUnit d.Gravity p.Gravity
              AirDrag = opt d.AirDrag p.AirDrag
              GroundRestitution = opt d.GroundRestitution p.GroundRestitution
              GroundFriction = opt d.GroundFriction p.GroundFriction
              PostRestitution = opt d.PostRestitution p.PostRestitution
              SpinDecay = opt d.SpinDecay p.SpinDecay
              MagnusCoeff = opt d.MagnusCoeff p.MagnusCoeff
              ContactRadius = optUnit d.ContactRadius p.ContactRadius
              PlayerMaxForce = optUnit d.PlayerMaxForce p.PlayerMaxForce
              PlayerMassBase = opt d.PlayerMassBase p.PlayerMassBase
              PlayerMassWeightCoeff = opt d.PlayerMassWeightCoeff p.PlayerMassWeightCoeff
              PlayerMassStrengthCoeff = opt d.PlayerMassStrengthCoeff p.PlayerMassStrengthCoeff
              SteeringSlowRadius = optUnit d.SteeringSlowRadius p.SteeringSlowRadius
              SteeringFleeRadius = optUnit d.SteeringFleeRadius p.SteeringFleeRadius
              SteeringAlignmentWeight = opt d.SteeringAlignmentWeight p.SteeringAlignmentWeight
              CohesionWeight = opt d.CohesionWeight p.CohesionWeight
              TurnConstraintAgilityCoeff = opt d.TurnConstraintAgilityCoeff p.TurnConstraintAgilityCoeff
              TurnConstraintBaseLimit = opt d.TurnConstraintBaseLimit p.TurnConstraintBaseLimit
              MoveSpeedMax = optUnit d.MoveSpeedMax p.MoveSpeedMax
              MoveSpeedMin = optUnit d.MoveSpeedMin p.MoveSpeedMin
              SeparationMinDistance = optUnit d.SeparationMinDistance p.SeparationMinDistance
              BallContestSeparationRadius = optUnit d.BallContestSeparationRadius p.BallContestSeparationRadius
              SeparationStrength = opt d.SeparationStrength p.SeparationStrength
              SeparationAgilityMultiplier = opt d.SeparationAgilityMultiplier p.SeparationAgilityMultiplier
              JitterBase = opt d.JitterBase p.JitterBase
              JitterAgilityMultiplier = opt d.JitterAgilityMultiplier p.JitterAgilityMultiplier
              BallStopThreshold = opt d.BallStopThreshold p.BallStopThreshold
              AirborneCheckThreshold = optUnit d.AirborneCheckThreshold p.AirborneCheckThreshold
              AxisStopThreshold = opt d.AxisStopThreshold p.AxisStopThreshold
              AirborneRestitutionBase = opt d.AirborneRestitutionBase p.AirborneRestitutionBase
              AirborneRestitutionCoeff = opt d.AirborneRestitutionCoeff p.AirborneRestitutionCoeff
              AirborneRestitutionFloor = opt d.AirborneRestitutionFloor p.AirborneRestitutionFloor
              GroundRestitutionBase = opt d.GroundRestitutionBase p.GroundRestitutionBase
              GroundRestitutionCoeff = opt d.GroundRestitutionCoeff p.GroundRestitutionCoeff
              GroundRestitutionFloor = opt d.GroundRestitutionFloor p.GroundRestitutionFloor
              ChaserProximity = optUnit d.ChaserProximity p.ChaserProximity
              AirborneThreshold = optUnit d.AirborneThreshold p.AirborneThreshold
              ArrivalAnticipationBase = opt d.ArrivalAnticipationBase p.ArrivalAnticipationBase
              ArrivalAnticipationQuality = opt d.ArrivalAnticipationQuality p.ArrivalAnticipationQuality
              ArrivalCompetitionRadius = optUnit d.ArrivalCompetitionRadius p.ArrivalCompetitionRadius
              ArrivalConvergenceThreshold = optUnit d.ArrivalConvergenceThreshold p.ArrivalConvergenceThreshold
              ArrivalContestThreshold = opt d.ArrivalContestThreshold p.ArrivalContestThreshold
              ReceivingGraceSubTicks = opt d.ReceivingGraceSubTicks p.ReceivingGraceSubTicks }

    let mergeTiming (p: PartialTimingConfig option) (d: TimingConfig) : TimingConfig =
        match p with
        | None -> d
        | Some p ->

            { DuelChainDelay = opt d.DuelChainDelay p.DuelChainDelay
              DuelNextDelay = opt d.DuelNextDelay p.DuelNextDelay
              ShotDelay = opt d.ShotDelay p.ShotDelay
              FoulDelay = opt d.FoulDelay p.FoulDelay
              GoalDelay = opt d.GoalDelay p.GoalDelay
              KickOffDelay = opt d.KickOffDelay p.KickOffDelay
              CornerDelay = opt d.CornerDelay p.CornerDelay
              FreeKickDelay = opt d.FreeKickDelay p.FreeKickDelay
              ThrowInDelay = opt d.ThrowInDelay p.ThrowInDelay
              GoalKickDelay = opt d.GoalKickDelay p.GoalKickDelay
              InjuryDelay = opt d.InjuryDelay p.InjuryDelay
              ManagerReactDelay = opt d.ManagerReactDelay p.ManagerReactDelay
              SubsDelay = opt d.SubsDelay p.SubsDelay
              StuckBallDelay = optInt d.StuckBallDelay p.StuckBallDelay
              EventWindowSubTicks = opt d.EventWindowSubTicks p.EventWindowSubTicks }

    let mergeMatchVolume (p: PartialMatchVolumeConfig option) (d: MatchVolumeConfig) : MatchVolumeConfig =
        match p with
        | None -> d
        | Some p ->

            { MaxChainLength = opt d.MaxChainLength p.MaxChainLength
              TargetDuelTicksPerMatch = opt d.TargetDuelTicksPerMatch p.TargetDuelTicksPerMatch
              TargetShotsPerMatch = opt d.TargetShotsPerMatch p.TargetShotsPerMatch
              TargetDribblesPerMatch = opt d.TargetDribblesPerMatch p.TargetDribblesPerMatch
              TargetPassesPerMatch = opt d.TargetPassesPerMatch p.TargetPassesPerMatch
              TargetCrossesPerMatch = opt d.TargetCrossesPerMatch p.TargetCrossesPerMatch
              TargetLongBallsPerMatch = opt d.TargetLongBallsPerMatch p.TargetLongBallsPerMatch }

    let mergeManager (p: PartialManagerConfig option) (d: ManagerConfig) : ManagerConfig =
        match p with
        | None -> d
        | Some p ->

            { FatigueReactionThreshold = opt d.FatigueReactionThreshold p.FatigueReactionThreshold
              SustainedMomentumSubTicks = opt d.SustainedMomentumSubTicks p.SustainedMomentumSubTicks
              MomentumThreshold = opt d.MomentumThreshold p.MomentumThreshold
              FatigueCheckSubTicks = opt d.FatigueCheckSubTicks p.FatigueCheckSubTicks
              ConditionThresholdLosing = opt d.ConditionThresholdLosing p.ConditionThresholdLosing
              ConditionThresholdDrawing = opt d.ConditionThresholdDrawing p.ConditionThresholdDrawing
              ConditionThresholdWinning = opt d.ConditionThresholdWinning p.ConditionThresholdWinning
              SubWindowMinutes = opt d.SubWindowMinutes p.SubWindowMinutes }

    let mergeBuildUp (p: PartialBuildUpConfig option) (d: BuildUpConfig) : BuildUpConfig =
        match p with
        | None -> d
        | Some p ->

            { PassSuccessBonus = opt d.PassSuccessBonus p.PassSuccessBonus
              DribblePenalty = opt d.DribblePenalty p.DribblePenalty
              LongBallPenalty = opt d.LongBallPenalty p.LongBallPenalty
              GKDistributionBonus = opt d.GKDistributionBonus p.GKDistributionBonus
              DCPassingBonus = opt d.DCPassingBonus p.DCPassingBonus }

    let mergeDecision (p: PartialDecisionConfig option) (d: DecisionConfig) : DecisionConfig =
        match p with
        | None -> d
        | Some p ->

            { ShootFinishingWeight = opt d.ShootFinishingWeight p.ShootFinishingWeight
              ShootLongShotsWeight = opt d.ShootLongShotsWeight p.ShootLongShotsWeight
              ShootComposureWeight = opt d.ShootComposureWeight p.ShootComposureWeight
              ShootDistNormWeight = opt d.ShootDistNormWeight p.ShootDistNormWeight
              ShootDistNormDivisor = opt d.ShootDistNormDivisor p.ShootDistNormDivisor
              ShootPosDirectnessWeight = opt d.ShootPosDirectnessWeight p.ShootPosDirectnessWeight
              ShootPosDepthWeight = opt d.ShootPosDepthWeight p.ShootPosDepthWeight
              ShootSTBonus = opt d.ShootSTBonus p.ShootSTBonus
              ShootDistPenaltyDivisor = opt d.ShootDistPenaltyDivisor p.ShootDistPenaltyDivisor
              ShootDistPenaltyMax = opt d.ShootDistPenaltyMax p.ShootDistPenaltyMax
              ShootDirectnessBonus = opt d.ShootDirectnessBonus p.ShootDirectnessBonus
              PassPassingWeight = opt d.PassPassingWeight p.PassPassingWeight
              PassVisionWeight = opt d.PassVisionWeight p.PassVisionWeight
              PassTargetBonus = opt d.PassTargetBonus p.PassTargetBonus
              PassAttackPhasePenalty = opt d.PassAttackPhasePenalty p.PassAttackPhasePenalty
              DribbleZoneBonusAttacking = opt d.DribbleZoneBonusAttacking p.DribbleZoneBonusAttacking
              DribbleZoneBonusMidfield = opt d.DribbleZoneBonusMidfield p.DribbleZoneBonusMidfield
              DribbleAttackPhaseBonus = opt d.DribbleAttackPhaseBonus p.DribbleAttackPhaseBonus
              DribbleTempoPenalty = opt d.DribbleTempoPenalty p.DribbleTempoPenalty
              DribblePressurePenalty = opt d.DribblePressurePenalty p.DribblePressurePenalty
              DribblePressureBlockThreshold = opt d.DribblePressureBlockThreshold p.DribblePressureBlockThreshold
              CrossCrossingWeight = opt d.CrossCrossingWeight p.CrossCrossingWeight
              CrossLateralTendencyWeight = opt d.CrossLateralTendencyWeight p.CrossLateralTendencyWeight
              CrossLateralTendencyBase = opt d.CrossLateralTendencyBase p.CrossLateralTendencyBase
              CrossZoneBonus = opt d.CrossZoneBonus p.CrossZoneBonus
              CrossWidthBonus = opt d.CrossWidthBonus p.CrossWidthBonus
              LongBallPassingWeight = opt d.LongBallPassingWeight p.LongBallPassingWeight
              LongBallVisionWeight = opt d.LongBallVisionWeight p.LongBallVisionWeight
              LongBallPressDistBase = opt d.LongBallPressDistBase p.LongBallPressDistBase
              LongBallPressMin = opt d.LongBallPressMin p.LongBallPressMin
              LongBallPressMax = opt d.LongBallPressMax p.LongBallPressMax
              LongBallPressNoOpponent = opt d.LongBallPressNoOpponent p.LongBallPressNoOpponent
              LongBallAttackPhaseBonus = opt d.LongBallAttackPhaseBonus p.LongBallAttackPhaseBonus
              LongBallDirectnessBonus = opt d.LongBallDirectnessBonus p.LongBallDirectnessBonus
              CreativityWeight = opt d.CreativityWeight p.CreativityWeight
              DirectnessWeight = opt d.DirectnessWeight p.DirectnessWeight
              DecisionTemperature = opt d.DecisionTemperature p.DecisionTemperature
              ShootMinThreshold = opt d.ShootMinThreshold p.ShootMinThreshold
              ShootDirectnessThresholdMod = opt d.ShootDirectnessThresholdMod p.ShootDirectnessThresholdMod
              PassMinThreshold = opt d.PassMinThreshold p.PassMinThreshold
              PassDirectnessThresholdMod = opt d.PassDirectnessThresholdMod p.PassDirectnessThresholdMod
              DribbleMinThreshold = opt d.DribbleMinThreshold p.DribbleMinThreshold
              DribbleDefZoneDirectnessMin = opt d.DribbleDefZoneDirectnessMin p.DribbleDefZoneDirectnessMin
              DribbleHighTempo = opt d.DribbleHighTempo p.DribbleHighTempo
              DribbleHighTempoDirectnessMin = opt d.DribbleHighTempoDirectnessMin p.DribbleHighTempoDirectnessMin
              LongBallMinThreshold = opt d.LongBallMinThreshold p.LongBallMinThreshold
              LongBallDirectnessThresholdMod = opt d.LongBallDirectnessThresholdMod p.LongBallDirectnessThresholdMod
              SpacePassMinScore = opt d.SpacePassMinScore p.SpacePassMinScore
              ShootDecisionsWeight = opt d.ShootDecisionsWeight p.ShootDecisionsWeight
              ShootBraveryMod = opt d.ShootBraveryMod p.ShootBraveryMod
              ShootConcentrationMod = opt d.ShootConcentrationMod p.ShootConcentrationMod
              PassDecisionsWeight = opt d.PassDecisionsWeight p.PassDecisionsWeight
              PassConcentrationMod = opt d.PassConcentrationMod p.PassConcentrationMod
              DribbleBraveryMod = opt d.DribbleBraveryMod p.DribbleBraveryMod
              ShootXGWeight = opt d.ShootXGWeight p.ShootXGWeight
              PassHighPressPenalty = opt d.PassHighPressPenalty p.PassHighPressPenalty
              DribbleAggressivePenalty = opt d.DribbleAggressivePenalty p.DribbleAggressivePenalty
              LongBallSlowTargetBonus = opt d.LongBallSlowTargetBonus p.LongBallSlowTargetBonus
              PassTrajectoryBonus = opt d.PassTrajectoryBonus p.PassTrajectoryBonus
              PersonalityFlairWeight = opt d.PersonalityFlairWeight p.PersonalityFlairWeight
              PersonalityTeamworkWeight = opt d.PersonalityTeamworkWeight p.PersonalityTeamworkWeight
              PersonalityConsistencyMod = opt d.PersonalityConsistencyMod p.PersonalityConsistencyMod
              ShootBraveryPressureMod = opt d.ShootBraveryPressureMod p.ShootBraveryPressureMod
              PassAnticipationBonus = opt d.PassAnticipationBonus p.PassAnticipationBonus }

    let mergePerception (p: PartialPerceptionConfig option) (d: PerceptionConfig) : PerceptionConfig =
        match p with
        | None -> d
        | Some p ->

            { VisionRadiusBase = optUnit d.VisionRadiusBase p.VisionRadiusBase
              VisionRadiusMax = optUnit d.VisionRadiusMax p.VisionRadiusMax
              VisionConeAngle = opt d.VisionConeAngle p.VisionConeAngle
              PeripheralMultiplier = opt d.PeripheralMultiplier p.PeripheralMultiplier
              MinimumAwarenessFloor = optUnit d.MinimumAwarenessFloor p.MinimumAwarenessFloor
              AnticipationBonusRadius = optUnit d.AnticipationBonusRadius p.AnticipationBonusRadius
              GoalkeeperConeAngle = opt d.GoalkeeperConeAngle p.GoalkeeperConeAngle
              CommunicationRange = optUnit d.CommunicationRange p.CommunicationRange
              SetPieceSimplifiedRadius = optUnit d.SetPieceSimplifiedRadius p.SetPieceSimplifiedRadius
              BlindPassVisionThreshold = opt d.BlindPassVisionThreshold p.BlindPassVisionThreshold
              BlindPassComposureThreshold = opt d.BlindPassComposureThreshold p.BlindPassComposureThreshold
              BlindPassSuccessPenalty = opt d.BlindPassSuccessPenalty p.BlindPassSuccessPenalty }

    let mergeIndividual (p: PartialIndividualWeights option) (d: IndividualWeights) : IndividualWeights =
        match p with
        | None -> d
        | Some p ->

            let mergeShoot (sp: PartialShootWeights option) (sd: ShootWeights) : ShootWeights =
                match sp with
                | None -> sd
                | Some sp ->

                    { FinishingWeight = opt sd.FinishingWeight sp.FinishingWeight
                      LongShotsWeight = opt sd.LongShotsWeight sp.LongShotsWeight
                      ComposureWeight = opt sd.ComposureWeight sp.ComposureWeight
                      XGInfluence = opt sd.XGInfluence sp.XGInfluence
                      ComposureStateMod = opt sd.ComposureStateMod sp.ComposureStateMod
                      ConfidenceMod = opt sd.ConfidenceMod sp.ConfidenceMod
                      FocusMod = opt sd.FocusMod sp.FocusMod
                      RiskBonus = opt sd.RiskBonus sp.RiskBonus
                      DistPenaltyDivisor = opt sd.DistPenaltyDivisor sp.DistPenaltyDivisor
                      DistPenaltyMax = opt sd.DistPenaltyMax sp.DistPenaltyMax }

            let mergePass (pp: PartialPassWeights option) (pd: PassWeights) : PassWeights =
                match pp with
                | None -> pd
                | Some pp ->

                    { PassingWeight = opt pd.PassingWeight pp.PassingWeight
                      VisionWeight = opt pd.VisionWeight pp.VisionWeight
                      ComposureWeight = opt pd.ComposureWeight pp.ComposureWeight
                      TargetBonus = opt pd.TargetBonus pp.TargetBonus
                      AttackPhasePenalty = opt pd.AttackPhasePenalty pp.AttackPhasePenalty }

            let mergeDribble (dp: PartialDribbleWeights option) (dd: DribbleWeights) : DribbleWeights =
                match dp with
                | None -> dd
                | Some dp ->

                    { DribblingWeight = opt dd.DribblingWeight dp.DribblingWeight
                      AgilityWeight = opt dd.AgilityWeight dp.AgilityWeight
                      BalanceWeight = opt dd.BalanceWeight dp.BalanceWeight
                      ZoneBonusAttacking = opt dd.ZoneBonusAttacking dp.ZoneBonusAttacking
                      ZoneBonusMidfield = opt dd.ZoneBonusMidfield dp.ZoneBonusMidfield
                      TempoPenalty = opt dd.TempoPenalty dp.TempoPenalty
                      PressurePenalty = opt dd.PressurePenalty dp.PressurePenalty }

            let mergeCross (cp: PartialCrossWeights option) (cd: CrossWeights) : CrossWeights =
                match cp with
                | None -> cd
                | Some cp ->

                    { CrossingWeight = opt cd.CrossingWeight cp.CrossingWeight
                      LateralTendencyWeight = opt cd.LateralTendencyWeight cp.LateralTendencyWeight
                      LateralTendencyBase = opt cd.LateralTendencyBase cp.LateralTendencyBase
                      ZoneBonus = opt cd.ZoneBonus cp.ZoneBonus
                      WidthBonus = opt cd.WidthBonus cp.WidthBonus }

            let mergeLongBall (lp: PartialLongBallWeights option) (ld: LongBallWeights) : LongBallWeights =
                match lp with
                | None -> ld
                | Some lp ->

                    { LongShotsWeight = opt ld.LongShotsWeight lp.LongShotsWeight
                      PassingWeight = opt ld.PassingWeight lp.PassingWeight
                      VisionWeight = opt ld.VisionWeight lp.VisionWeight
                      PressDistBase = opt ld.PressDistBase lp.PressDistBase
                      PressMin = opt ld.PressMin lp.PressMin
                      PressMax = opt ld.PressMax lp.PressMax
                      PressNoOpponent = opt ld.PressNoOpponent lp.PressNoOpponent
                      AttackPhaseBonus = opt ld.AttackPhaseBonus lp.AttackPhaseBonus
                      DirectnessBonus = opt ld.DirectnessBonus lp.DirectnessBonus }

            { Shoot = mergeShoot p.Shoot d.Shoot
              Pass = mergePass p.Pass d.Pass
              Dribble = mergeDribble p.Dribble d.Dribble
              Cross = mergeCross p.Cross d.Cross
              LongBall = mergeLongBall p.LongBall d.LongBall
              SoftmaxTemperature = opt d.SoftmaxTemperature p.SoftmaxTemperature
              DirectnessBlendTactic = opt d.DirectnessBlendTactic p.DirectnessBlendTactic
              DirectnessBlendProfile = opt d.DirectnessBlendProfile p.DirectnessBlendProfile }

    let mergeProfile (p: PartialProfileWeights option) (d: ProfileWeights) : ProfileWeights =
        match p with
        | None -> d
        | Some p ->

            { PositionalFreedom_PositioningWeight =
                opt d.PositionalFreedom_PositioningWeight p.PositionalFreedom_PositioningWeight
              PositionalFreedom_VisionWeight = opt d.PositionalFreedom_VisionWeight p.PositionalFreedom_VisionWeight
              PositionalFreedom_StaminaWeight = opt d.PositionalFreedom_StaminaWeight p.PositionalFreedom_StaminaWeight
              PositionalFreedom_ConcentrationWeight =
                opt d.PositionalFreedom_ConcentrationWeight p.PositionalFreedom_ConcentrationWeight
              PositionalFreedom_BalanceWeight = opt d.PositionalFreedom_BalanceWeight p.PositionalFreedom_BalanceWeight
              PositionalFreedom_AgilityWeight = opt d.PositionalFreedom_AgilityWeight p.PositionalFreedom_AgilityWeight
              AttackingDepth_PaceWeight = opt d.AttackingDepth_PaceWeight p.AttackingDepth_PaceWeight
              AttackingDepth_AccelerationWeight =
                opt d.AttackingDepth_AccelerationWeight p.AttackingDepth_AccelerationWeight
              AttackingDepth_FinishingWeight = opt d.AttackingDepth_FinishingWeight p.AttackingDepth_FinishingWeight
              AttackingDepth_ComposureWeight = opt d.AttackingDepth_ComposureWeight p.AttackingDepth_ComposureWeight
              AttackingDepth_StaminaWeight = opt d.AttackingDepth_StaminaWeight p.AttackingDepth_StaminaWeight
              AttackingDepth_PositionBaseMultiplier =
                opt d.AttackingDepth_PositionBaseMultiplier p.AttackingDepth_PositionBaseMultiplier
              LateralTendency_CrossingWeight = opt d.LateralTendency_CrossingWeight p.LateralTendency_CrossingWeight
              LateralTendency_PaceWeight = opt d.LateralTendency_PaceWeight p.LateralTendency_PaceWeight
              DefensiveHeight_WorkRateWeight = opt d.DefensiveHeight_WorkRateWeight p.DefensiveHeight_WorkRateWeight
              DefensiveHeight_TacklingWeight = opt d.DefensiveHeight_TacklingWeight p.DefensiveHeight_TacklingWeight
              DefensiveHeight_PositioningWeight =
                opt d.DefensiveHeight_PositioningWeight p.DefensiveHeight_PositioningWeight
              DefensiveHeight_StaminaWeight = opt d.DefensiveHeight_StaminaWeight p.DefensiveHeight_StaminaWeight
              DefensiveHeight_PositionBaseMultiplier =
                opt d.DefensiveHeight_PositionBaseMultiplier p.DefensiveHeight_PositionBaseMultiplier
              PressingIntensity_StaminaWeight = opt d.PressingIntensity_StaminaWeight p.PressingIntensity_StaminaWeight
              PressingIntensity_WorkRateWeight =
                opt d.PressingIntensity_WorkRateWeight p.PressingIntensity_WorkRateWeight
              PressingIntensity_AggressionWeight =
                opt d.PressingIntensity_AggressionWeight p.PressingIntensity_AggressionWeight
              PressingIntensity_PaceWeight = opt d.PressingIntensity_PaceWeight p.PressingIntensity_PaceWeight
              PressingIntensity_ConcentrationWeight =
                opt d.PressingIntensity_ConcentrationWeight p.PressingIntensity_ConcentrationWeight
              RiskAppetite_PassingWeight = opt d.RiskAppetite_PassingWeight p.RiskAppetite_PassingWeight
              RiskAppetite_LongShotsWeight = opt d.RiskAppetite_LongShotsWeight p.RiskAppetite_LongShotsWeight
              RiskAppetite_VisionWeight = opt d.RiskAppetite_VisionWeight p.RiskAppetite_VisionWeight
              RiskAppetite_ComposureWeight = opt d.RiskAppetite_ComposureWeight p.RiskAppetite_ComposureWeight
              RiskAppetite_DribblingWeight = opt d.RiskAppetite_DribblingWeight p.RiskAppetite_DribblingWeight
              RiskAppetite_BraveryWeight = opt d.RiskAppetite_BraveryWeight p.RiskAppetite_BraveryWeight
              Directness_FinishingWeight = opt d.Directness_FinishingWeight p.Directness_FinishingWeight
              Directness_PaceWeight = opt d.Directness_PaceWeight p.Directness_PaceWeight
              Directness_AccelerationWeight = opt d.Directness_AccelerationWeight p.Directness_AccelerationWeight
              Directness_AggressionWeight = opt d.Directness_AggressionWeight p.Directness_AggressionWeight
              Directness_DribblingWeight = opt d.Directness_DribblingWeight p.Directness_DribblingWeight
              Directness_StrengthWeight = opt d.Directness_StrengthWeight p.Directness_StrengthWeight
              Directness_InversePassingWeight = opt d.Directness_InversePassingWeight p.Directness_InversePassingWeight
              CreativityWeight_PassingWeight = opt d.CreativityWeight_PassingWeight p.CreativityWeight_PassingWeight
              CreativityWeight_VisionWeight = opt d.CreativityWeight_VisionWeight p.CreativityWeight_VisionWeight
              CreativityWeight_BallControlWeight =
                opt d.CreativityWeight_BallControlWeight p.CreativityWeight_BallControlWeight
              CreativityWeight_DribblingWeight =
                opt d.CreativityWeight_DribblingWeight p.CreativityWeight_DribblingWeight
              CreativityWeight_ComposureWeight =
                opt d.CreativityWeight_ComposureWeight p.CreativityWeight_ComposureWeight
              CreativityWeight_CrossingWeight = opt d.CreativityWeight_CrossingWeight p.CreativityWeight_CrossingWeight
              AerialThreat_JumpingReachWeight = opt d.AerialThreat_JumpingReachWeight p.AerialThreat_JumpingReachWeight
              AerialThreat_HeadingWeight = opt d.AerialThreat_HeadingWeight p.AerialThreat_HeadingWeight
              AerialThreat_StrengthWeight = opt d.AerialThreat_StrengthWeight p.AerialThreat_StrengthWeight
              AerialThreat_BraveryWeight = opt d.AerialThreat_BraveryWeight p.AerialThreat_BraveryWeight
              HoldUpPlay_StrengthWeight = opt d.HoldUpPlay_StrengthWeight p.HoldUpPlay_StrengthWeight
              HoldUpPlay_BallControlWeight = opt d.HoldUpPlay_BallControlWeight p.HoldUpPlay_BallControlWeight
              HoldUpPlay_ComposureWeight = opt d.HoldUpPlay_ComposureWeight p.HoldUpPlay_ComposureWeight
              HoldUpPlay_PassingWeight = opt d.HoldUpPlay_PassingWeight p.HoldUpPlay_PassingWeight
              HoldUpPlay_HeadingWeight = opt d.HoldUpPlay_HeadingWeight p.HoldUpPlay_HeadingWeight
              HoldUpPlay_BalanceWeight = opt d.HoldUpPlay_BalanceWeight p.HoldUpPlay_BalanceWeight }

    let mergeDevelopment (p: PartialDevelopmentWeights option) (d: DevelopmentWeights) : DevelopmentWeights =
        match p with
        | None -> d
        | Some p ->

            { AgeBracket_MaxDelta_U21 = opt d.AgeBracket_MaxDelta_U21 p.AgeBracket_MaxDelta_U21
              AgeBracket_MaxDelta_U25 = opt d.AgeBracket_MaxDelta_U25 p.AgeBracket_MaxDelta_U25
              AgeBracket_MaxDelta_U28 = opt d.AgeBracket_MaxDelta_U28 p.AgeBracket_MaxDelta_U28
              AgeBracket_MaxDelta_U31 = opt d.AgeBracket_MaxDelta_U31 p.AgeBracket_MaxDelta_U31
              AgeBracket_MaxDelta_U34 = opt d.AgeBracket_MaxDelta_U34 p.AgeBracket_MaxDelta_U34
              FocusMultiplier_Goalkeeping = opt d.FocusMultiplier_Goalkeeping p.FocusMultiplier_Goalkeeping
              FocusMultiplier_PhysicalBase = opt d.FocusMultiplier_PhysicalBase p.FocusMultiplier_PhysicalBase
              FocusMultiplier_Physical_Pressing =
                opt d.FocusMultiplier_Physical_Pressing p.FocusMultiplier_Physical_Pressing
              FocusMultiplier_Physical_Positional =
                opt d.FocusMultiplier_Physical_Positional p.FocusMultiplier_Physical_Positional
              FocusMultiplier_Mental = opt d.FocusMultiplier_Mental p.FocusMultiplier_Mental
              FocusMultiplier_TechnicalBase = opt d.FocusMultiplier_TechnicalBase p.FocusMultiplier_TechnicalBase
              FocusMultiplier_Technical_Creativity =
                opt d.FocusMultiplier_Technical_Creativity p.FocusMultiplier_Technical_Creativity
              FocusMultiplier_Technical_Directness =
                opt d.FocusMultiplier_Technical_Directness p.FocusMultiplier_Technical_Directness
              WeeklyDeltaDivisor = opt d.WeeklyDeltaDivisor p.WeeklyDeltaDivisor
              StatFocus_DirectnessThreshold = opt d.StatFocus_DirectnessThreshold p.StatFocus_DirectnessThreshold
              StatFocus_AttackingDepthThreshold =
                opt d.StatFocus_AttackingDepthThreshold p.StatFocus_AttackingDepthThreshold
              StatFocus_DefensiveHeightThreshold =
                opt d.StatFocus_DefensiveHeightThreshold p.StatFocus_DefensiveHeightThreshold
              StatFocus_CreativityThreshold = opt d.StatFocus_CreativityThreshold p.StatFocus_CreativityThreshold
              MaybeStat_PositiveThreshold = opt d.MaybeStat_PositiveThreshold p.MaybeStat_PositiveThreshold
              MaybeStat_NegativeThreshold = opt d.MaybeStat_NegativeThreshold p.MaybeStat_NegativeThreshold }

    let mergeCalTargets (p: PartialCalibrationTargets option) (d: CalibrationTargets) : CalibrationTargets =
        match p with
        | None -> d
        | Some p ->

            { GoalsPerMatch = opt d.GoalsPerMatch p.GoalsPerMatch
              ShotsPerMatch = opt d.ShotsPerMatch p.ShotsPerMatch
              PassSuccessRate = opt d.PassSuccessRate p.PassSuccessRate
              CrossSuccessRate = opt d.CrossSuccessRate p.CrossSuccessRate
              HomeWinPct = opt d.HomeWinPct p.HomeWinPct
              DrawPct = opt d.DrawPct p.DrawPct
              AwayWinPct = opt d.AwayWinPct p.AwayWinPct
              CardsPerMatch = opt d.CardsPerMatch p.CardsPerMatch
              InjuriesPerMatch = opt d.InjuriesPerMatch p.InjuriesPerMatch
              DribblesPerMatch = opt d.DribblesPerMatch p.DribblesPerMatch
              CrossesPerMatch = opt d.CrossesPerMatch p.CrossesPerMatch
              LongBallsPerMatch = opt d.LongBallsPerMatch p.LongBallsPerMatch
              DuelTicksPerMatch = opt d.DuelTicksPerMatch p.DuelTicksPerMatch }

    let mergePersonality (p: PartialPersonalityWeights option) (d: PersonalityWeights) : PersonalityWeights =
        match p with
        | None -> d
        | Some p ->

            { FlairVisionWeight = opt d.FlairVisionWeight p.FlairVisionWeight
              FlairDribblingWeight = opt d.FlairDribblingWeight p.FlairDribblingWeight
              ConsistencyConcentrationWeight = opt d.ConsistencyConcentrationWeight p.ConsistencyConcentrationWeight
              ConsistencyComposureWeight = opt d.ConsistencyComposureWeight p.ConsistencyComposureWeight
              LeadershipWeight = opt d.LeadershipWeight p.LeadershipWeight
              ControversyAggressionWeight = opt d.ControversyAggressionWeight p.ControversyAggressionWeight
              ControversyComposureWeight = opt d.ControversyComposureWeight p.ControversyComposureWeight
              TeamworkWorkRateWeight = opt d.TeamworkWorkRateWeight p.TeamworkWorkRateWeight
              TeamworkPositioningWeight = opt d.TeamworkPositioningWeight p.TeamworkPositioningWeight
              AmbitionMoraleWeight = opt d.AmbitionMoraleWeight p.AmbitionMoraleWeight
              AmbitionWorkRateWeight = opt d.AmbitionWorkRateWeight p.AmbitionWorkRateWeight
              PressureComposureWeight = opt d.PressureComposureWeight p.PressureComposureWeight
              PressureConcentrationWeight = opt d.PressureConcentrationWeight p.PressureConcentrationWeight
              SportsmanshipAggressionWeight = opt d.SportsmanshipAggressionWeight p.SportsmanshipAggressionWeight
              TemperamentComposureWeight = opt d.TemperamentComposureWeight p.TemperamentComposureWeight
              TemperamentConcentrationWeight = opt d.TemperamentConcentrationWeight p.TemperamentConcentrationWeight }

    let mergeCollective (p: PartialCollectiveWeights option) (d: CollectiveWeights) : CollectiveWeights =
        match p with
        | None -> d
        | Some p ->

            let mergeDirectiveParams
                (dp: PartialDirectiveParamsMap option)
                (dd: DirectiveParamsMap)
                : DirectiveParamsMap =
                match dp with
                | None -> dd
                | Some dp ->

                    { CompactnessSuccessDelta = opt dd.CompactnessSuccessDelta dp.CompactnessSuccessDelta
                      CompactnessFailDelta = opt dd.CompactnessFailDelta dp.CompactnessFailDelta
                      CompactnessSuccessThreshold = opt dd.CompactnessSuccessThreshold dp.CompactnessSuccessThreshold
                      CompactnessFailThreshold = opt dd.CompactnessFailThreshold dp.CompactnessFailThreshold
                      PressingSuccessDelta = opt dd.PressingSuccessDelta dp.PressingSuccessDelta
                      PressingFailDelta = opt dd.PressingFailDelta dp.PressingFailDelta
                      PressingSuccessThreshold = opt dd.PressingSuccessThreshold dp.PressingSuccessThreshold
                      PressingFailThreshold = opt dd.PressingFailThreshold dp.PressingFailThreshold
                      WingPlaySuccessDelta = opt dd.WingPlaySuccessDelta dp.WingPlaySuccessDelta
                      WingPlayFailDelta = opt dd.WingPlayFailDelta dp.WingPlayFailDelta
                      WingPlaySuccessThreshold = opt dd.WingPlaySuccessThreshold dp.WingPlaySuccessThreshold
                      WingPlayFailThreshold = opt dd.WingPlayFailThreshold dp.WingPlayFailThreshold }

            let mergeEmergent (ep: PartialEmergentWeights option) (ed: EmergentWeights) : EmergentWeights =
                match ep with
                | None -> ed
                | Some ep ->

                    { CompactnessSuccessDelta = opt ed.CompactnessSuccessDelta ep.CompactnessSuccessDelta
                      CompactnessFailDelta = opt ed.CompactnessFailDelta ep.CompactnessFailDelta
                      CompactnessSuccessThreshold = opt ed.CompactnessSuccessThreshold ep.CompactnessSuccessThreshold
                      CompactnessFailThreshold = opt ed.CompactnessFailThreshold ep.CompactnessFailThreshold
                      PressingSuccessDelta = opt ed.PressingSuccessDelta ep.PressingSuccessDelta
                      PressingFailDelta = opt ed.PressingFailDelta ep.PressingFailDelta
                      PressingSuccessThreshold = opt ed.PressingSuccessThreshold ep.PressingSuccessThreshold
                      PressingFailThreshold = opt ed.PressingFailThreshold ep.PressingFailThreshold
                      WingPlaySuccessDelta = opt ed.WingPlaySuccessDelta ep.WingPlaySuccessDelta
                      WingPlayFailDelta = opt ed.WingPlayFailDelta ep.WingPlayFailDelta
                      WingPlaySuccessThreshold = opt ed.WingPlaySuccessThreshold ep.WingPlaySuccessThreshold
                      WingPlayFailThreshold = opt ed.WingPlayFailThreshold ep.WingPlayFailThreshold
                      ConsecutiveLossPenalty = opt ed.ConsecutiveLossPenalty ep.ConsecutiveLossPenalty
                      FatigueSpiralCompactnessFactor =
                        opt ed.FatigueSpiralCompactnessFactor ep.FatigueSpiralCompactnessFactor
                      FatigueSpiralPressingFactor = opt ed.FatigueSpiralPressingFactor ep.FatigueSpiralPressingFactor
                      FatigueSpiralTempoFactor = opt ed.FatigueSpiralTempoFactor ep.FatigueSpiralTempoFactor
                      FatigueSpiralRiskFactor = opt ed.FatigueSpiralRiskFactor ep.FatigueSpiralRiskFactor
                      FatigueSpiralThreshold = opt ed.FatigueSpiralThreshold ep.FatigueSpiralThreshold }

            let mergeModifiers (mp: PartialModifierWeights option) (md: ModifierWeights) : ModifierWeights =
                match mp with
                | None -> md
                | Some mp ->

                    { TransitionNearMult = opt md.TransitionNearMult mp.TransitionNearMult
                      TransitionFarMult = opt md.TransitionFarMult mp.TransitionFarMult
                      TransitionNearDistance = opt md.TransitionNearDistance mp.TransitionNearDistance
                      WeaknessSupportMult = opt md.WeaknessSupportMult mp.WeaknessSupportMult
                      RestDefenseSupportMult = opt md.RestDefenseSupportMult mp.RestDefenseSupportMult
                      RestDefenseCoverMult = opt md.RestDefenseCoverMult mp.RestDefenseCoverMult
                      ThreatCoverMult = opt md.ThreatCoverMult mp.ThreatCoverMult
                      HighLineSupportMult = opt md.HighLineSupportMult mp.HighLineSupportMult
                      LowBlockPressMult = opt md.LowBlockPressMult mp.LowBlockPressMult
                      LowBlockCoverMult = opt md.LowBlockCoverMult mp.LowBlockCoverMult
                      UrgencyPressMult = opt md.UrgencyPressMult mp.UrgencyPressMult
                      UrgencySupportMult = opt md.UrgencySupportMult mp.UrgencySupportMult
                      UrgencyThreshold = opt md.UrgencyThreshold mp.UrgencyThreshold }

            let mergeChemistry (cp: PartialChemistryWeights option) (cd: ChemistryWeights) : ChemistryWeights =
                match cp with
                | None -> cd
                | Some cp ->

                    { FamiliarityPassBonus = opt cd.FamiliarityPassBonus cp.FamiliarityPassBonus
                      FamiliarityFailPenalty = opt cd.FamiliarityFailPenalty cp.FamiliarityFailPenalty
                      FamiliarityTimeBonus = opt cd.FamiliarityTimeBonus cp.FamiliarityTimeBonus
                      PressingCoordinationBase = opt cd.PressingCoordinationBase cp.PressingCoordinationBase
                      PressingCoordinationFamiliarityMult =
                        opt cd.PressingCoordinationFamiliarityMult cp.PressingCoordinationFamiliarityMult
                      TransitionSpeedBase = opt cd.TransitionSpeedBase cp.TransitionSpeedBase
                      TransitionSpeedFamiliarityMult =
                        opt cd.TransitionSpeedFamiliarityMult cp.TransitionSpeedFamiliarityMult }

            let mergeTeamDirector
                (tp: PartialTeamDirectorWeights option)
                (td: TeamDirectorWeights)
                : TeamDirectorWeights =
                match tp with
                | None -> td
                | Some tp ->

                    { WorkRateWeight = opt td.WorkRateWeight tp.WorkRateWeight
                      PositioningWeight = opt td.PositioningWeight tp.PositioningWeight
                      AdvancedBonus = opt td.AdvancedBonus tp.AdvancedBonus }

            let mergeReactiveLoop
                (rp: PartialReactiveLoopWeights option)
                (rd: ReactiveLoopWeights)
                : ReactiveLoopWeights =
                match rp with
                | None -> rd
                | Some rp ->

                    { ShapeDevWeight = opt rd.ShapeDevWeight rp.ShapeDevWeight
                      PressDevWeight = opt rd.PressDevWeight rp.PressDevWeight
                      FatigueDevWeight = opt rd.FatigueDevWeight rp.FatigueDevWeight
                      OnTrackThreshold = opt rd.OnTrackThreshold rp.OnTrackThreshold
                      DriftingThreshold = opt rd.DriftingThreshold rp.DriftingThreshold }

            { DirectiveParams = mergeDirectiveParams p.DirectiveParams d.DirectiveParams
              Emergent = mergeEmergent p.Emergent d.Emergent
              Modifiers = mergeModifiers p.Modifiers d.Modifiers
              Chemistry = mergeChemistry p.Chemistry d.Chemistry
              TeamDirector = mergeTeamDirector p.TeamDirector d.TeamDirector
              ReactiveLoop = mergeReactiveLoop p.ReactiveLoop d.ReactiveLoop }

    let mergeXG (p: PartialXGWeights option) (d: XGWeights) : XGWeights =
        match p with
        | None -> d
        | Some p ->

            { DistanceFactor = opt d.DistanceFactor p.DistanceFactor
              AngleExponent = opt d.AngleExponent p.AngleExponent
              BaseMultiplier = opt d.BaseMultiplier p.BaseMultiplier
              OneOnOneMultiplier = opt d.OneOnOneMultiplier p.OneOnOneMultiplier
              SetPieceMultiplier = opt d.SetPieceMultiplier p.SetPieceMultiplier
              PressureReduction = opt d.PressureReduction p.PressureReduction
              HeaderMultiplier = opt d.HeaderMultiplier p.HeaderMultiplier
              VolleyMultiplier = opt d.VolleyMultiplier p.VolleyMultiplier
              HalfVolleyMultiplier = opt d.HalfVolleyMultiplier p.HalfVolleyMultiplier
              ChipShotMultiplier = opt d.ChipShotMultiplier p.ChipShotMultiplier
              CurlerMultiplier = opt d.CurlerMultiplier p.CurlerMultiplier
              DrivenShotMultiplier = opt d.DrivenShotMultiplier p.DrivenShotMultiplier
              PlacedShotMultiplier = opt d.PlacedShotMultiplier p.PlacedShotMultiplier
              FirstTimeShotMultiplier = opt d.FirstTimeShotMultiplier p.FirstTimeShotMultiplier }

    let mergeInterception (p: PartialInterceptionWeights option) (d: InterceptionWeights) : InterceptionWeights =
        match p with
        | None -> d
        | Some p ->

            { BallControlRadiusMult = opt d.BallControlRadiusMult p.BallControlRadiusMult
              PressIntentFactor = opt d.PressIntentFactor p.PressIntentFactor
              RecoverIntentFactor = opt d.RecoverIntentFactor p.RecoverIntentFactor
              MaintainShapeIntentFactor = opt d.MaintainShapeIntentFactor p.MaintainShapeIntentFactor
              CoverSpaceIntentFactor = opt d.CoverSpaceIntentFactor p.CoverSpaceIntentFactor }

    let mergeWinProb (p: PartialWinProbabilityWeights option) (d: WinProbabilityWeights) : WinProbabilityWeights =
        match p with
        | None -> d
        | Some p ->

            { GoalLeadBase = opt d.GoalLeadBase p.GoalLeadBase
              DrawBase = opt d.DrawBase p.DrawBase
              GoalDiffFactor = opt d.GoalDiffFactor p.GoalDiffFactor
              XGFactor = opt d.XGFactor p.XGFactor
              HomeAdvantage = opt d.HomeAdvantage p.HomeAdvantage
              GoalDiffSteepness = opt d.GoalDiffSteepness p.GoalDiffSteepness
              XGDiffSteepness = opt d.XGDiffSteepness p.XGDiffSteepness
              MinutePressure = opt d.MinutePressure p.MinutePressure
              ComebackBonus = opt d.ComebackBonus p.ComebackBonus
              MomentumPositiveThreshold = opt d.MomentumPositiveThreshold p.MomentumPositiveThreshold
              MomentumNegativeThreshold = opt d.MomentumNegativeThreshold p.MomentumNegativeThreshold
              MomentumPositiveBonus = opt d.MomentumPositiveBonus p.MomentumPositiveBonus
              MomentumNegativePenalty = opt d.MomentumNegativePenalty p.MomentumNegativePenalty
              MomentumLinearFactor = opt d.MomentumLinearFactor p.MomentumLinearFactor }

    let mergeUtility (p: PartialUtilityWeights option) (d: UtilityWeights) : UtilityWeights =
        match p with
        | None -> d
        | Some p ->

            { PressZoneBonus_HighAttacking = opt d.PressZoneBonus_HighAttacking p.PressZoneBonus_HighAttacking
              PressZoneBonus_HighMidfield = opt d.PressZoneBonus_HighMidfield p.PressZoneBonus_HighMidfield
              PressZoneBonus_HighDefensive = opt d.PressZoneBonus_HighDefensive p.PressZoneBonus_HighDefensive
              PressZoneBonus_MidAttackingMidfield =
                opt d.PressZoneBonus_MidAttackingMidfield p.PressZoneBonus_MidAttackingMidfield
              PressZoneBonus_MidDefensive = opt d.PressZoneBonus_MidDefensive p.PressZoneBonus_MidDefensive
              PressZoneBonus_Low = opt d.PressZoneBonus_Low p.PressZoneBonus_Low
              PossessionChangeWindow = opt d.PossessionChangeWindow p.PossessionChangeWindow
              ScoreDiffPressStep = opt d.ScoreDiffPressStep p.ScoreDiffPressStep
              WingSpaceBase = opt d.WingSpaceBase p.WingSpaceBase
              StaminaWingMult = opt d.StaminaWingMult p.StaminaWingMult
              StructuredBase = opt d.StructuredBase p.StructuredBase
              DirectiveChangeThreshold = opt d.DirectiveChangeThreshold p.DirectiveChangeThreshold
              DropDeepHighLinePenalty = opt d.DropDeepHighLinePenalty p.DropDeepHighLinePenalty
              DropDeepLeadBonus = opt d.DropDeepLeadBonus p.DropDeepLeadBonus
              DropDeepTimeBonus = opt d.DropDeepTimeBonus p.DropDeepTimeBonus
              DropDeepBase = opt d.DropDeepBase p.DropDeepBase
              CounterPressStaminaFactor = opt d.CounterPressStaminaFactor p.CounterPressStaminaFactor
              CounterPressIntensityBonus = opt d.CounterPressIntensityBonus p.CounterPressIntensityBonus
              CounterPressBase = opt d.CounterPressBase p.CounterPressBase
              BuildFromBackNoPressBonus = opt d.BuildFromBackNoPressBonus p.BuildFromBackNoPressBonus
              BuildFromBackMidPressBonus = opt d.BuildFromBackMidPressBonus p.BuildFromBackMidPressBonus
              BuildFromBackHighPressPenalty = opt d.BuildFromBackHighPressPenalty p.BuildFromBackHighPressPenalty
              BuildFromBackLowBlockBonus = opt d.BuildFromBackLowBlockBonus p.BuildFromBackLowBlockBonus
              BuildFromBackBase = opt d.BuildFromBackBase p.BuildFromBackBase
              DirectPlayUrgencyBonus = opt d.DirectPlayUrgencyBonus p.DirectPlayUrgencyBonus
              DirectPlayUrgencyBonusAny = opt d.DirectPlayUrgencyBonusAny p.DirectPlayUrgencyBonusAny
              DirectPlayHighLineBonus = opt d.DirectPlayHighLineBonus p.DirectPlayHighLineBonus
              DirectPlayBase = opt d.DirectPlayBase p.DirectPlayBase
              SitAndCounterBase = opt d.SitAndCounterBase p.SitAndCounterBase
              SitAndCounterLeadBonus = opt d.SitAndCounterLeadBonus p.SitAndCounterLeadBonus
              SitAndCounterStaminaFactor = opt d.SitAndCounterStaminaFactor p.SitAndCounterStaminaFactor
              HoldPossessionLeadBonus = opt d.HoldPossessionLeadBonus p.HoldPossessionLeadBonus
              HoldPossessionDrawBonus = opt d.HoldPossessionDrawBonus p.HoldPossessionDrawBonus
              HoldPossessionLosingPenalty = opt d.HoldPossessionLosingPenalty p.HoldPossessionLosingPenalty
              HoldPossessionTimeBonus = opt d.HoldPossessionTimeBonus p.HoldPossessionTimeBonus
              HoldPossessionPressPenalty = opt d.HoldPossessionPressPenalty p.HoldPossessionPressPenalty
              HoldPossessionBase = opt d.HoldPossessionBase p.HoldPossessionBase
              CompactBlockLosingBonus = opt d.CompactBlockLosingBonus p.CompactBlockLosingBonus
              CompactBlockWinningPenalty = opt d.CompactBlockWinningPenalty p.CompactBlockWinningPenalty
              CompactBlockOpponentBonus = opt d.CompactBlockOpponentBonus p.CompactBlockOpponentBonus
              CompactBlockTimeBonus = opt d.CompactBlockTimeBonus p.CompactBlockTimeBonus
              CompactBlockBase = opt d.CompactBlockBase p.CompactBlockBase
              HighLineCohesionBonus = opt d.HighLineCohesionBonus p.HighLineCohesionBonus
              HighLineStaminaFactor = opt d.HighLineStaminaFactor p.HighLineStaminaFactor
              HighLineRiskPenalty = opt d.HighLineRiskPenalty p.HighLineRiskPenalty
              HighLineBase = opt d.HighLineBase p.HighLineBase
              PressingSuccessBonus = opt d.PressingSuccessBonus p.PressingSuccessBonus
              OpponentHighLineNoPressBonus = opt d.OpponentHighLineNoPressBonus p.OpponentHighLineNoPressBonus
              OverloadWeaknessBonus = opt d.OverloadWeaknessBonus p.OverloadWeaknessBonus
              OverloadFlankBase = opt d.OverloadFlankBase p.OverloadFlankBase }

    let mergePerf (p: PartialPerformanceWeightsMap option) (d: PerformanceWeightsMap) : PerformanceWeightsMap =
        match p with
        | None -> d
        | Some p ->

            { DuelStatWeight = opt d.DuelStatWeight p.DuelStatWeight
              DuelConditionWeight = opt d.DuelConditionWeight p.DuelConditionWeight
              DuelMoraleWeight = opt d.DuelMoraleWeight p.DuelMoraleWeight
              DuelCurveSteepness = opt d.DuelCurveSteepness p.DuelCurveSteepness
              DuelCurveInflection = opt d.DuelCurveInflection p.DuelCurveInflection
              TechnicalStatWeight = opt d.TechnicalStatWeight p.TechnicalStatWeight
              TechnicalConditionWeight = opt d.TechnicalConditionWeight p.TechnicalConditionWeight
              TechnicalMoraleWeight = opt d.TechnicalMoraleWeight p.TechnicalMoraleWeight
              TechnicalCurveSteepness = opt d.TechnicalCurveSteepness p.TechnicalCurveSteepness
              TechnicalCurveInflection = opt d.TechnicalCurveInflection p.TechnicalCurveInflection
              DecisionStatWeight = opt d.DecisionStatWeight p.DecisionStatWeight
              DecisionConditionWeight = opt d.DecisionConditionWeight p.DecisionConditionWeight
              DecisionMoraleWeight = opt d.DecisionMoraleWeight p.DecisionMoraleWeight
              DecisionCurveSteepness = opt d.DecisionCurveSteepness p.DecisionCurveSteepness
              DecisionCurveInflection = opt d.DecisionCurveInflection p.DecisionCurveInflection }

    let mergeMatchSituation (p: PartialMatchSituationConfig option) (d: MatchSituationConfig) : MatchSituationConfig =
        match p with
        | None -> d
        | Some p ->

            { ShootLosingLateMod = opt d.ShootLosingLateMod p.ShootLosingLateMod
              ShootDrawLateMod = opt d.ShootDrawLateMod p.ShootDrawLateMod
              ShootMomentumScale = opt d.ShootMomentumScale p.ShootMomentumScale
              PassWinningMod = opt d.PassWinningMod p.PassWinningMod
              PassTransitionPenalty = opt d.PassTransitionPenalty p.PassTransitionPenalty
              DribbleTransitionMod = opt d.DribbleTransitionMod p.DribbleTransitionMod
              LongBallLosingLateMod = opt d.LongBallLosingLateMod p.LongBallLosingLateMod
              LongBallPressureThreshold = opt d.LongBallPressureThreshold p.LongBallPressureThreshold
              LongBallPressureMod = opt d.LongBallPressureMod p.LongBallPressureMod }

    let mergeReferee (p: PartialRefereeWeights option) (d: RefereeWeights) : RefereeWeights =
        match p with
        | None -> d
        | Some p ->

            { CardBaseProb = opt d.CardBaseProb p.CardBaseProb
              CardAggressionMult = opt d.CardAggressionMult p.CardAggressionMult
              CardHomeReduction = opt d.CardHomeReduction p.CardHomeReduction
              InjuryBaseProb = opt d.InjuryBaseProb p.InjuryBaseProb
              InjuryStrengthInverseMult = opt d.InjuryStrengthInverseMult p.InjuryStrengthInverseMult
              FoulAggressionBase = opt d.FoulAggressionBase p.FoulAggressionBase
              FoulAggressionMult = opt d.FoulAggressionMult p.FoulAggressionMult
              HomeBonusCardThreshold = opt d.HomeBonusCardThreshold p.HomeBonusCardThreshold
              MatchImportanceWeight = opt d.MatchImportanceWeight p.MatchImportanceWeight
              DisciplineHistoryMitigation = opt d.DisciplineHistoryMitigation p.DisciplineHistoryMitigation
              AdvantageWindowSeconds = opt d.AdvantageWindowSeconds p.AdvantageWindowSeconds
              AdvantageCallBackWindow = opt d.AdvantageCallBackWindow p.AdvantageCallBackWindow }

    let mergeEnvironment (p: PartialEnvironmentWeights option) (d: EnvironmentWeights) : EnvironmentWeights =
        match p with
        | None -> d
        | Some p ->

            { WeatherClearModifier = opt d.WeatherClearModifier p.WeatherClearModifier
              WeatherLightRainModifier = opt d.WeatherLightRainModifier p.WeatherLightRainModifier
              WeatherHeavyRainModifier = opt d.WeatherHeavyRainModifier p.WeatherHeavyRainModifier
              WeatherSnowModifier = opt d.WeatherSnowModifier p.WeatherSnowModifier
              WeatherWindyModifier = opt d.WeatherWindyModifier p.WeatherWindyModifier
              PitchDrySlipBase = opt d.PitchDrySlipBase p.PitchDrySlipBase
              PitchDampSlipBase = opt d.PitchDampSlipBase p.PitchDampSlipBase
              PitchWetSlipBase = opt d.PitchWetSlipBase p.PitchWetSlipBase
              PitchWaterloggedSlipBase = opt d.PitchWaterloggedSlipBase p.PitchWaterloggedSlipBase
              SlipAgilityReduction = opt d.SlipAgilityReduction p.SlipAgilityReduction
              CrowdMaxCapacity = opt d.CrowdMaxCapacity p.CrowdMaxCapacity
              CrowdCapacityWeight = opt d.CrowdCapacityWeight p.CrowdCapacityWeight
              CrowdSupportWeight = opt d.CrowdSupportWeight p.CrowdSupportWeight
              CrowdMomentumWeight = opt d.CrowdMomentumWeight p.CrowdMomentumWeight
              CrowdImportanceWeight = opt d.CrowdImportanceWeight p.CrowdImportanceWeight
              CrowdMaxAdvantage = opt d.CrowdMaxAdvantage p.CrowdMaxAdvantage
              AwayPressureCrowdMult = opt d.AwayPressureCrowdMult p.AwayPressureCrowdMult }

    let mergeMomentum (p: PartialMomentumWeights option) (d: MomentumWeights) : MomentumWeights =
        match p with
        | None -> d
        | Some p ->

            { EventDelta = opt d.EventDelta p.EventDelta
              Decay = opt d.Decay p.Decay
              Min = opt d.Min p.Min
              Max = opt d.Max p.Max
              HalfLifeSeconds = opt d.HalfLifeSeconds p.HalfLifeSeconds }

    let mergeWithDefaults (p: PartialBalanceConfig) (d: BalanceConfig) : BalanceConfig =
        { Duel = mergeDuel p.Duel d.Duel
          Shot = mergeShot p.Shot d.Shot
          Pass = mergePass p.Pass d.Pass
          Cross = mergeCross p.Cross d.Cross
          Dribble = mergeDribble p.Dribble d.Dribble
          Tackle = mergeTackle p.Tackle d.Tackle
          SetPiece = mergeSetPiece p.SetPiece d.SetPiece
          GK = mergeGK p.GK d.GK
          OutfieldReception = d.OutfieldReception
          HomeAdvantage = mergeHomeAdv p.HomeAdvantage d.HomeAdvantage
          Physics = mergePhysics p.Physics d.Physics
          Timing = mergeTiming p.Timing d.Timing
          MatchVolume = mergeMatchVolume p.MatchVolume d.MatchVolume
          Manager = mergeManager p.Manager d.Manager
          BuildUp = mergeBuildUp p.BuildUp d.BuildUp
          Decision = mergeDecision p.Decision d.Decision
          Perception = mergePerception p.Perception d.Perception
          Individual = mergeIndividual p.Individual d.Individual
          ProfileWeights = mergeProfile p.ProfileWeights d.ProfileWeights
          Development = mergeDevelopment p.Development d.Development
          CalibrationTargets = mergeCalTargets p.CalibrationTargets d.CalibrationTargets
          Collective = mergeCollective p.Collective d.Collective
          Personality = mergePersonality p.Personality d.Personality
          Utility = mergeUtility p.Utility d.Utility
          Referee = mergeReferee p.Referee d.Referee
          Environment = mergeEnvironment p.Environment d.Environment
          Momentum = mergeMomentum p.Momentum d.Momentum
          XG = mergeXG p.XG d.XG
          Interception = mergeInterception p.Interception d.Interception
          WinProbability = mergeWinProb p.WinProbability d.WinProbability
          Performance = mergePerf p.Performance d.Performance
          MatchSituation = mergeMatchSituation p.MatchSituation d.MatchSituation
          Influence = d.Influence
          Diagnostics = d.Diagnostics }

// ============================================================================
// Public API
// ============================================================================

module WeightsLoader =

    let mergeWithDefaults (p: PartialBalanceConfig) (d: BalanceConfig) : BalanceConfig = Merger.mergeWithDefaults p d

    let load (path: string) : Result<BalanceConfig, string> =
        try
            if not (File.Exists path) then
                Ok BalanceConfig.defaultConfig
            else
                let json = File.ReadAllText path
                let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
                let partial = JsonSerializer.Deserialize<PartialBalanceConfig>(json, opts)
                Ok(Merger.mergeWithDefaults partial BalanceConfig.defaultConfig)
        with ex ->
            Error $"WeightsLoader: {ex.Message}"
