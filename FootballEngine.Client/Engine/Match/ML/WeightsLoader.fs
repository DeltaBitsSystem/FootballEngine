namespace FootballEngine.ML

open System
open System.Text.Json
open System.IO
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.SimulationClock
open FootballEngine.Domain

// ============================================================================
// PARTIAL TYPES — CLIMutable DTOs for JSON deserialization
// All fields are option types so missing fields fall back to defaults
// ============================================================================

[<CLIMutable>]
type PartialProfileWeights = {
    PositionalFreedom_PositioningWeight: float option
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
    HoldUpPlay_BalanceWeight: float option
}

[<CLIMutable>]
type PartialShootWeights = {
    FinishingWeight: float option
    LongShotsWeight: float option
    ComposureWeight: float option
    XGInfluence: float option
    ComposureStateMod: float option
    ConfidenceMod: float option
    FocusMod: float option
    RiskBonus: float option
    DistPenaltyDivisor: float option
    DistPenaltyMax: float option
}

[<CLIMutable>]
type PartialPassWeights = {
    PassingWeight: float option
    VisionWeight: float option
    ComposureWeight: float option
    TargetBonus: float option
    AttackPhasePenalty: float option
}

[<CLIMutable>]
type PartialDribbleWeights = {
    DribblingWeight: float option
    AgilityWeight: float option
    BalanceWeight: float option
    ZoneBonusAttacking: float option
    ZoneBonusMidfield: float option
    TempoPenalty: float option
    PressurePenalty: float option
}

[<CLIMutable>]
type PartialCrossWeights = {
    CrossingWeight: float option
    LateralTendencyWeight: float option
    LateralTendencyBase: float option
    ZoneBonus: float option
    WidthBonus: float option
}

[<CLIMutable>]
type PartialLongBallWeights = {
    LongShotsWeight: float option
    PassingWeight: float option
    VisionWeight: float option
    PressDistBase: float option
    PressMin: float option
    PressMax: float option
    PressNoOpponent: float option
    AttackPhaseBonus: float option
    DirectnessBonus: float option
}

[<CLIMutable>]
type PartialIndividualWeights = {
    Shoot: PartialShootWeights option
    Pass: PartialPassWeights option
    Dribble: PartialDribbleWeights option
    Cross: PartialCrossWeights option
    LongBall: PartialLongBallWeights option
    SoftmaxTemperature: float option
    DirectnessBlendTactic: float option
    DirectnessBlendProfile: float option
}

[<CLIMutable>]
type PartialPersonalityWeights = {
    FlairVisionWeight: float option
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
    TemperamentConcentrationWeight: float option
}

[<CLIMutable>]
type PartialDirectiveParamsMap = {
    CompactnessSuccessDelta: float option
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
}

[<CLIMutable>]
type PartialEmergentWeights = {
    CompactnessSuccessDelta: float option
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
    FatigueSpiralThreshold: float option
}

[<CLIMutable>]
type PartialModifierWeights = {
    TransitionNearMult: float option
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
    UrgencyThreshold: float option
}

[<CLIMutable>]
type PartialChemistryWeights = {
    FamiliarityPassBonus: float option
    FamiliarityFailPenalty: float option
    FamiliarityTimeBonus: float option
    PressingCoordinationBase: float option
    PressingCoordinationFamiliarityMult: float option
    TransitionSpeedBase: float option
    TransitionSpeedFamiliarityMult: float option
}

[<CLIMutable>]
type PartialTeamDirectorWeights = {
    WorkRateWeight: float option
    PositioningWeight: float option
    AdvancedBonus: float option
}

[<CLIMutable>]
type PartialReactiveLoopWeights = {
    ShapeDevWeight: float option
    PressDevWeight: float option
    FatigueDevWeight: float option
    OnTrackThreshold: float option
    DriftingThreshold: float option
}

[<CLIMutable>]
type PartialCollectiveWeights = {
    DirectiveParams: PartialDirectiveParamsMap option
    Emergent: PartialEmergentWeights option
    Modifiers: PartialModifierWeights option
    Chemistry: PartialChemistryWeights option
    TeamDirector: PartialTeamDirectorWeights option
    ReactiveLoop: PartialReactiveLoopWeights option
}

[<CLIMutable>]
type PartialDuelOutcomeWeights = {
    DuelSteepness: float option
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
    FatigueDecay: float option
}

[<CLIMutable>]
type PartialShotOutcomeWeights = {
    OnTargetBase: float option
    OnTargetDistDecayRate: float option
    OnTargetDistMaxPenalty: float option
    QualityGate: float option
    AngleSpreadBase: float option
    VzBase: float option
    VzVariance: float option
    OnTargetMultiplier: float option
    OnTargetDistDivisor: float option
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
    ShotWideMargin: float option
}

[<CLIMutable>]
type PartialPassOutcomeWeights = {
    BaseMean: float option
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
}

[<CLIMutable>]
type PartialCrossOutcomeWeights = {
    BaseMean: float option
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
    FallbackVz: float option
}

[<CLIMutable>]
type PartialTackleOutcomeWeights = {
    TechnicalWeight: float option
    PositioningWeight: float option
    StrengthWeight: float option
    AggressionWeight: float option
    PositioningReduction: float option
    FoulShapeBeta: float option
    FoulMomentum: float option
    SuccessMomentum: float option
    FailMomentum: float option
    TackleSteepness: float option
}

[<CLIMutable>]
type PartialSetPieceOutcomeWeights = {
    FreeKickTargetX: float option
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
}

[<CLIMutable>]
type PartialGKOutcomeWeights = {
    CatchHandlingMult: float option
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
}

[<CLIMutable>]
type PartialXGWeights = {
    DistanceFactor: float option
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
    FirstTimeShotMultiplier: float option
}

[<CLIMutable>]
type PartialInterceptionWeights = {
    BallControlRadiusMult: float option
    PressIntentFactor: float option
    RecoverIntentFactor: float option
    MaintainShapeIntentFactor: float option
    CoverSpaceIntentFactor: float option
}

[<CLIMutable>]
type PartialOutcomeWeights = {
    Duel: PartialDuelOutcomeWeights option
    Shot: PartialShotOutcomeWeights option
    Pass: PartialPassOutcomeWeights option
    Cross: PartialCrossOutcomeWeights option
    Tackle: PartialTackleOutcomeWeights option
    SetPiece: PartialSetPieceOutcomeWeights option
    GK: PartialGKOutcomeWeights option
    XG: PartialXGWeights option
    Interception: PartialInterceptionWeights option
}

[<CLIMutable>]
type PartialWinProbabilityWeights = {
    GoalLeadBase: float option
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
    MomentumLinearFactor: float option
}

[<CLIMutable>]
type PartialUtilityWeights = {
    PressZoneBonus_HighAttacking: float option
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
    OverloadFlankBase: float option
}

[<CLIMutable>]
type PartialPerformanceWeightsMap = {
    DuelStatWeight: float option
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
    DecisionCurveInflection: float option
}

[<CLIMutable>]
type PartialRefereeWeights = {
    CardBaseProb: float option
    CardAggressionMult: float option
    CardHomeReduction: float option
    InjuryBaseProb: float option
    InjuryStrengthInverseMult: float option
    FoulAggressionBase: float option
    FoulAggressionMult: float option
}

[<CLIMutable>]
type PartialEnvironmentWeights = {
    WeatherClearModifier: float option
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
    AwayPressureCrowdMult: float option
}

[<CLIMutable>]
type PartialMomentumWeights = {
    EventDelta: float option
    Decay: float option
    Min: float option
    Max: float option
    HalfLifeSeconds: float option
}

[<CLIMutable>]
type PartialHomeAdvantageWeights = {
    Strength: float option
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
    FatigueReduction: float option
}

[<CLIMutable>]
type PartialManagerWeights = {
    FatigueReactionThreshold: int option
    SustainedMomentumSubTicks: int option
    MomentumThreshold: float option
    FatigueCheckSubTicks: int option
    ConditionThresholdLosing: int option
    ConditionThresholdDrawing: int option
    ConditionThresholdWinning: int option
}

[<CLIMutable>]
type PartialPerceptionWeights = {
    VisionRadiusBase: float option
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
    BlindPassSuccessPenalty: float option
}

[<CLIMutable>]
type PartialDevelopmentWeights = {
    AgeBracket_MaxDelta_U21: int option
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
    MaybeStat_NegativeThreshold: float option
}

[<CLIMutable>]
type PartialCalibrationTargets = {
    GoalsPerMatch: float option
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
    DuelTicksPerMatch: float option
}

[<CLIMutable>]
type PartialEngineWeights = {
    Version: int option
    ProfileWeights: PartialProfileWeights option
    Individual: PartialIndividualWeights option
    Personality: PartialPersonalityWeights option
    Collective: PartialCollectiveWeights option
    Outcomes: PartialOutcomeWeights option
    WinProbability: PartialWinProbabilityWeights option
    Utility: PartialUtilityWeights option
    Performance: PartialPerformanceWeightsMap option
    Referee: PartialRefereeWeights option
    Environment: PartialEnvironmentWeights option
    Momentum: PartialMomentumWeights option
    HomeAdvantage: PartialHomeAdvantageWeights option
    Manager: PartialManagerWeights option
    Perception: PartialPerceptionWeights option
    Development: PartialDevelopmentWeights option
    CalibrationTargets: PartialCalibrationTargets option
}

// ============================================================================
// DEFAULTS — Complete EngineWeights matching BalanceConfig.defaultConfig
// ============================================================================

module EngineWeightDefaults =

    let private clock = defaultClock

    let defaults : EngineWeights = {
        Version = 1

        ProfileWeights = {
            PositionalFreedom_PositioningWeight = 0.15
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
            HoldUpPlay_BalanceWeight = 0.15
        }

        Individual = {
            Shoot = {
                FinishingWeight = 0.35
                LongShotsWeight = 0.15
                ComposureWeight = 0.20
                XGInfluence = 0.20
                ComposureStateMod = 0.12
                ConfidenceMod = 0.08
                FocusMod = 0.06
                RiskBonus = 0.075
                DistPenaltyDivisor = 50.0
                DistPenaltyMax = 0.5
            }
            Pass = {
                PassingWeight = 0.40
                VisionWeight = 0.30
                ComposureWeight = 0.0
                TargetBonus = 0.10
                AttackPhasePenalty = -0.03
            }
            Dribble = {
                DribblingWeight = 0.50
                AgilityWeight = 0.30
                BalanceWeight = 0.20
                ZoneBonusAttacking = 0.1
                ZoneBonusMidfield = 0.05
                TempoPenalty = 0.20
                PressurePenalty = 0.15
            }
            Cross = {
                CrossingWeight = 0.60
                LateralTendencyWeight = 0.60
                LateralTendencyBase = 0.10
                ZoneBonus = 0.15
                WidthBonus = 0.25
            }
            LongBall = {
                LongShotsWeight = 0.0
                PassingWeight = 0.30
                VisionWeight = 0.20
                PressDistBase = 10.0
                PressMin = 0.3
                PressMax = 1.0
                PressNoOpponent = 0.7
                AttackPhaseBonus = 0.05
                DirectnessBonus = 0.20
            }
            SoftmaxTemperature = 0.15
            DirectnessBlendTactic = 0.6
            DirectnessBlendProfile = 0.4
        }

        Personality = {
            FlairVisionWeight = 0.6
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
            TemperamentConcentrationWeight = 0.5
        }

        Collective = {
            DirectiveParams = {
                CompactnessSuccessDelta = 0.05
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
            }
            Emergent = {
                CompactnessSuccessDelta = 0.05
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
                FatigueSpiralThreshold = 0.0
            }
            Modifiers = {
                TransitionNearMult = 2.5
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
                UrgencyThreshold = 0.7
            }
            Chemistry = {
                FamiliarityPassBonus = 0.02
                FamiliarityFailPenalty = -0.005
                FamiliarityTimeBonus = 0.001
                PressingCoordinationBase = 0.1
                PressingCoordinationFamiliarityMult = 0.8
                TransitionSpeedBase = 0.05
                TransitionSpeedFamiliarityMult = 0.9
            }
            TeamDirector = {
                WorkRateWeight = 0.3
                PositioningWeight = 0.4
                AdvancedBonus = 0.5
            }
            ReactiveLoop = {
                ShapeDevWeight = 0.4
                PressDevWeight = 0.3
                FatigueDevWeight = 0.3
                OnTrackThreshold = 0.15
                DriftingThreshold = 0.35
            }
        }

        Outcomes = {
            Duel = {
                DuelSteepness = 1.2
                MomentumBonus = 0.50
                JitterWin = 8.0
                JitterRecover = 2.0
                JitterKeep = 2.5
                SpeedKeep = 3.0
                SpeedKeepVz = 0.20
                AttackerDribblingWeight = 0.50
                AttackerAgilityWeight = 0.30
                AttackerBalanceWeight = 0.20
                DefenderTacklingWeight = 0.50
                DefenderStrengthWeight = 0.30
                DefenderPositionWeight = 0.20
                FatigueThreshold = 50
                FatigueDecay = 0.04
            }
            Shot = {
                OnTargetBase = 0.25
                OnTargetDistDecayRate = 15.0
                OnTargetDistMaxPenalty = 0.15
                QualityGate = 0.35
                AngleSpreadBase = 0.80
                VzBase = float LongBallVz
                VzVariance = 1.5
                OnTargetMultiplier = 0.20
                OnTargetDistDivisor = 20.0
                NormalisationDistance = 30.0
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
                ShotWideMargin = 3.0
            }
            Pass = {
                BaseMean = 0.65
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
                InterceptionRadius = 5.0
                PressureDistance = 8.0
                DeflectPressureMultiplier = 0.12
                InterceptPaceWeight = 0.35
                InterceptPositioningWeight = 0.45
                ScrambleJitter = 3.0
                Speed = float PassSpeed
                Vz = float PassVz
                InterceptDistFactorWeight = 0.3
                InterceptPositioningContrib = 0.15
                InterceptVisionContrib = 0.10
                CreativityWeight = 0.06
                DirectnessWeight = 0.06
                MeanMin = 0.01
                MeanMax = 0.99
                DefaultNearestDefDist = 10.0
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
                LongBallSpeed = float LongBallSpeed
                LongBallVz = float LongBallVz
                LongBallDeflectMult = 1.5
                LongBallInterceptMult = 1.5
                LongBallPressureContrib = 0.3
                ForwardDepthThreshold = 0.5
                ForwardCreativityThreshold = 0.4
                LongBallScrambleJitterMult = 2.0
                PassLeadFactor = 0.30
            }
            Cross = {
                BaseMean = 0.50
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
                Speed = float CrossSpeed
                Vz = float CrossVz
                AerialThreatThreshold = 0.4
                AttackingDepthThreshold = 0.5
                GkSkillDefault = 50.0
                GkSkillDivisor = 150.0
                SpinTopMult = 0.2
                SpinSideMult = 0.8
                FallbackSpeed = 15.0
                FallbackVz = 2.0
            }
            Tackle = {
                TechnicalWeight = 0.50
                PositioningWeight = 0.30
                StrengthWeight = 0.20
                AggressionWeight = 0.15
                PositioningReduction = 0.10
                FoulShapeBeta = 10.0
                FoulMomentum = 0.30
                SuccessMomentum = 0.80
                FailMomentum = 0.50
                TackleSteepness = 1.5
            }
            SetPiece = {
                FreeKickTargetX = float (GoalLineHome - PenaltyAreaDepth)
                FreeKickSpeed = 16.0
                FreeKickVz = 0.50
                FreeKickSteepness = 2.0
                FreeKickSavePowerThreshold = 1.5
                FreeKickSaveVariance = 1.5
                FreeKickSpinTopMult = 0.5
                FreeKickSpinSideMult = 0.9
                CornerBoxXThreshold = float (GoalLineHome - PenaltyAreaDepth)
                CornerDefenderBoxThreshold = float (GoalLineHome - PenaltyAreaDepth - 5.0<meter>)
                CornerSecondPhaseProbability = 0.35
                CornerKeepPossessionProbability = 0.55
                CornerSpeed = 14.0
                CornerVz = 1.0
                CornerDensityBase = 3.0
                CornerDensityPenalty = 0.05
                CornerLogisticSteepness = 2.5
                CornerDefScoreDefault = 0.5
                ThrowInSpeed = 12.0
                ThrowInVz = 0.50
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
                PenaltySpeed = 28.0
                PenaltyVzBase = 0.8
                PenaltyVzVariance = 0.3
                PenaltyAngleSpread = 0.12
                PostShotClearProbability = 0.40
                ClearSpeed = 16.0
                ClearVz = 1.5
                ClearYStdDev = 10.0
                GoalKickFallbackDistHome = 30.0
                GoalKickFallbackDistAway = 75.0
                KickOffPartnerOffsetX = -3.0
                KickOffPartnerOffsetY = 2.0
                FoulBaseRate = 0.35
                CornerOnFailedCross = 0.85
            }
            GK = {
                CatchHandlingMult = 0.85
                DiveReach = 2.5
                ParrySpeed = 8.0
                ParryDeflectionAngle = 0.4
                AerialReachMult = 1.2
                JumpReachMult = 0.8
                PunchProbability = 0.35
                ClaimCrossProbability = 0.25
                CollectionRadius = 3.0
                CollectionPriority = 2.0
                ThrowSpeed = 12.0
                RollSpeed = 8.0
                GoalKickSpeed = 30.0
                PuntSpeed = 25.0
                DistributionAccuracyMult = 1.0
                DistributionDecisionNoise = 0.08
                HoldTimeSubTicks = 20
                MaxHoldSubTicks = 240
                BackPassHandlingPenalty = 0.15
                GKDecisionWindowSubTicks = 16
            }
            XG = {
                // Current xG uses linear (1.0 - d/30.0), equivalent to exp(-0.033*d) ≈ 1-0.033d near 0
                DistanceFactor = 0.033
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
                FirstTimeShotMultiplier = 0.8
            }
            Interception = {
                BallControlRadiusMult = 0.20
                PressIntentFactor = 0.7
                RecoverIntentFactor = 0.7
                MaintainShapeIntentFactor = 1.4
                CoverSpaceIntentFactor = 1.2
            }
        }

        WinProbability = {
            GoalLeadBase = 0.55
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
            MomentumLinearFactor = 0.04
        }

        Utility = {
            PressZoneBonus_HighAttacking = 0.4
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
            OverloadFlankBase = 0.3
        }

        Performance = {
            DuelStatWeight = 0.70
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
            DecisionCurveInflection = 0.4
        }

        Referee = {
            CardBaseProb = 0.010
            CardAggressionMult = 0.0004
            CardHomeReduction = 0.20
            InjuryBaseProb = 0.0008
            InjuryStrengthInverseMult = 0.00002
            FoulAggressionBase = 0.25
            FoulAggressionMult = 0.35
        }

        Environment = {
            WeatherClearModifier = 1.0
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
            AwayPressureCrowdMult = 0.5
        }

        Momentum = {
            EventDelta = 0.5
            Decay = 0.02
            Min = -10.0
            Max = 10.0
            HalfLifeSeconds = 15.0
        }

        HomeAdvantage = {
            Strength = 1.0
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
            FatigueReduction = 0.10
        }

        Manager = {
            FatigueReactionThreshold = 60
            SustainedMomentumSubTicks = int (secondsToSubTicks clock 600 |> float)
            MomentumThreshold = -2.0
            FatigueCheckSubTicks = int (secondsToSubTicks clock 120 |> float)
            ConditionThresholdLosing = 75
            ConditionThresholdDrawing = 65
            ConditionThresholdWinning = 55
        }

        Perception = {
            VisionRadiusBase = 20.0
            VisionRadiusMax = 45.0
            VisionConeAngle = 2.094
            PeripheralMultiplier = 0.4
            MinimumAwarenessFloor = 20.0
            AnticipationBonusRadius = 5.0
            GoalkeeperConeAngle = 3.1416
            CommunicationRange = 15.0
            SetPieceSimplifiedRadius = 30.0
            BlindPassVisionThreshold = 15
            BlindPassComposureThreshold = 14
            BlindPassSuccessPenalty = 0.30
        }

        Development = {
            AgeBracket_MaxDelta_U21 = 4
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
            MaybeStat_NegativeThreshold = 0.50
        }

        CalibrationTargets = {
            GoalsPerMatch = 2.75
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
            DuelTicksPerMatch = 228.0
        }
    }

// ============================================================================
// MERGE WITH DEFAULTS — field-level recursive merge
// ============================================================================

module private Merger =

    let opt d = function None -> d | Some v -> v

    let mergeProfile (p: PartialProfileWeights) (d: ProfileWeights) : ProfileWeights = {
        PositionalFreedom_PositioningWeight = opt d.PositionalFreedom_PositioningWeight p.PositionalFreedom_PositioningWeight
        PositionalFreedom_VisionWeight = opt d.PositionalFreedom_VisionWeight p.PositionalFreedom_VisionWeight
        PositionalFreedom_StaminaWeight = opt d.PositionalFreedom_StaminaWeight p.PositionalFreedom_StaminaWeight
        PositionalFreedom_ConcentrationWeight = opt d.PositionalFreedom_ConcentrationWeight p.PositionalFreedom_ConcentrationWeight
        PositionalFreedom_BalanceWeight = opt d.PositionalFreedom_BalanceWeight p.PositionalFreedom_BalanceWeight
        PositionalFreedom_AgilityWeight = opt d.PositionalFreedom_AgilityWeight p.PositionalFreedom_AgilityWeight
        AttackingDepth_PaceWeight = opt d.AttackingDepth_PaceWeight p.AttackingDepth_PaceWeight
        AttackingDepth_AccelerationWeight = opt d.AttackingDepth_AccelerationWeight p.AttackingDepth_AccelerationWeight
        AttackingDepth_FinishingWeight = opt d.AttackingDepth_FinishingWeight p.AttackingDepth_FinishingWeight
        AttackingDepth_ComposureWeight = opt d.AttackingDepth_ComposureWeight p.AttackingDepth_ComposureWeight
        AttackingDepth_StaminaWeight = opt d.AttackingDepth_StaminaWeight p.AttackingDepth_StaminaWeight
        AttackingDepth_PositionBaseMultiplier = opt d.AttackingDepth_PositionBaseMultiplier p.AttackingDepth_PositionBaseMultiplier
        LateralTendency_CrossingWeight = opt d.LateralTendency_CrossingWeight p.LateralTendency_CrossingWeight
        LateralTendency_PaceWeight = opt d.LateralTendency_PaceWeight p.LateralTendency_PaceWeight
        DefensiveHeight_WorkRateWeight = opt d.DefensiveHeight_WorkRateWeight p.DefensiveHeight_WorkRateWeight
        DefensiveHeight_TacklingWeight = opt d.DefensiveHeight_TacklingWeight p.DefensiveHeight_TacklingWeight
        DefensiveHeight_PositioningWeight = opt d.DefensiveHeight_PositioningWeight p.DefensiveHeight_PositioningWeight
        DefensiveHeight_StaminaWeight = opt d.DefensiveHeight_StaminaWeight p.DefensiveHeight_StaminaWeight
        DefensiveHeight_PositionBaseMultiplier = opt d.DefensiveHeight_PositionBaseMultiplier p.DefensiveHeight_PositionBaseMultiplier
        PressingIntensity_StaminaWeight = opt d.PressingIntensity_StaminaWeight p.PressingIntensity_StaminaWeight
        PressingIntensity_WorkRateWeight = opt d.PressingIntensity_WorkRateWeight p.PressingIntensity_WorkRateWeight
        PressingIntensity_AggressionWeight = opt d.PressingIntensity_AggressionWeight p.PressingIntensity_AggressionWeight
        PressingIntensity_PaceWeight = opt d.PressingIntensity_PaceWeight p.PressingIntensity_PaceWeight
        PressingIntensity_ConcentrationWeight = opt d.PressingIntensity_ConcentrationWeight p.PressingIntensity_ConcentrationWeight
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
        CreativityWeight_BallControlWeight = opt d.CreativityWeight_BallControlWeight p.CreativityWeight_BallControlWeight
        CreativityWeight_DribblingWeight = opt d.CreativityWeight_DribblingWeight p.CreativityWeight_DribblingWeight
        CreativityWeight_ComposureWeight = opt d.CreativityWeight_ComposureWeight p.CreativityWeight_ComposureWeight
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
        HoldUpPlay_BalanceWeight = opt d.HoldUpPlay_BalanceWeight p.HoldUpPlay_BalanceWeight
    }

    let mergeShoot (p: PartialShootWeights) (d: ShootWeights) : ShootWeights = {
        FinishingWeight = opt d.FinishingWeight p.FinishingWeight
        LongShotsWeight = opt d.LongShotsWeight p.LongShotsWeight
        ComposureWeight = opt d.ComposureWeight p.ComposureWeight
        XGInfluence = opt d.XGInfluence p.XGInfluence
        ComposureStateMod = opt d.ComposureStateMod p.ComposureStateMod
        ConfidenceMod = opt d.ConfidenceMod p.ConfidenceMod
        FocusMod = opt d.FocusMod p.FocusMod
        RiskBonus = opt d.RiskBonus p.RiskBonus
        DistPenaltyDivisor = opt d.DistPenaltyDivisor p.DistPenaltyDivisor
        DistPenaltyMax = opt d.DistPenaltyMax p.DistPenaltyMax
    }

    let mergePass (p: PartialPassWeights) (d: PassWeights) : PassWeights = {
        PassingWeight = opt d.PassingWeight p.PassingWeight
        VisionWeight = opt d.VisionWeight p.VisionWeight
        ComposureWeight = opt d.ComposureWeight p.ComposureWeight
        TargetBonus = opt d.TargetBonus p.TargetBonus
        AttackPhasePenalty = opt d.AttackPhasePenalty p.AttackPhasePenalty
    }

    let mergeDribble (p: PartialDribbleWeights) (d: DribbleWeights) : DribbleWeights = {
        DribblingWeight = opt d.DribblingWeight p.DribblingWeight
        AgilityWeight = opt d.AgilityWeight p.AgilityWeight
        BalanceWeight = opt d.BalanceWeight p.BalanceWeight
        ZoneBonusAttacking = opt d.ZoneBonusAttacking p.ZoneBonusAttacking
        ZoneBonusMidfield = opt d.ZoneBonusMidfield p.ZoneBonusMidfield
        TempoPenalty = opt d.TempoPenalty p.TempoPenalty
        PressurePenalty = opt d.PressurePenalty p.PressurePenalty
    }

    let mergeCross (p: PartialCrossWeights) (d: CrossWeights) : CrossWeights = {
        CrossingWeight = opt d.CrossingWeight p.CrossingWeight
        LateralTendencyWeight = opt d.LateralTendencyWeight p.LateralTendencyWeight
        LateralTendencyBase = opt d.LateralTendencyBase p.LateralTendencyBase
        ZoneBonus = opt d.ZoneBonus p.ZoneBonus
        WidthBonus = opt d.WidthBonus p.WidthBonus
    }

    let mergeLongBall (p: PartialLongBallWeights) (d: LongBallWeights) : LongBallWeights = {
        LongShotsWeight = opt d.LongShotsWeight p.LongShotsWeight
        PassingWeight = opt d.PassingWeight p.PassingWeight
        VisionWeight = opt d.VisionWeight p.VisionWeight
        PressDistBase = opt d.PressDistBase p.PressDistBase
        PressMin = opt d.PressMin p.PressMin
        PressMax = opt d.PressMax p.PressMax
        PressNoOpponent = opt d.PressNoOpponent p.PressNoOpponent
        AttackPhaseBonus = opt d.AttackPhaseBonus p.AttackPhaseBonus
        DirectnessBonus = opt d.DirectnessBonus p.DirectnessBonus
    }

    let mergeIndividual (p: PartialIndividualWeights) (d: IndividualWeights) : IndividualWeights = {
        Shoot = mergeShoot (Option.defaultValue { FinishingWeight = None; LongShotsWeight = None; ComposureWeight = None; XGInfluence = None; ComposureStateMod = None; ConfidenceMod = None; FocusMod = None; RiskBonus = None; DistPenaltyDivisor = None; DistPenaltyMax = None } p.Shoot) d.Shoot
        Pass = mergePass (Option.defaultValue { PassingWeight = None; VisionWeight = None; ComposureWeight = None; TargetBonus = None; AttackPhasePenalty = None } p.Pass) d.Pass
        Dribble = mergeDribble (Option.defaultValue { DribblingWeight = None; AgilityWeight = None; BalanceWeight = None; ZoneBonusAttacking = None; ZoneBonusMidfield = None; TempoPenalty = None; PressurePenalty = None } p.Dribble) d.Dribble
        Cross = mergeCross (Option.defaultValue { CrossingWeight = None; LateralTendencyWeight = None; LateralTendencyBase = None; ZoneBonus = None; WidthBonus = None } p.Cross) d.Cross
        LongBall = mergeLongBall (Option.defaultValue { LongShotsWeight = None; PassingWeight = None; VisionWeight = None; PressDistBase = None; PressMin = None; PressMax = None; PressNoOpponent = None; AttackPhaseBonus = None; DirectnessBonus = None } p.LongBall) d.LongBall
        SoftmaxTemperature = opt d.SoftmaxTemperature p.SoftmaxTemperature
        DirectnessBlendTactic = opt d.DirectnessBlendTactic p.DirectnessBlendTactic
        DirectnessBlendProfile = opt d.DirectnessBlendProfile p.DirectnessBlendProfile
    }

    let mergePersonality (p: PartialPersonalityWeights) (d: PersonalityWeights) : PersonalityWeights = {
        FlairVisionWeight = opt d.FlairVisionWeight p.FlairVisionWeight
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
        TemperamentConcentrationWeight = opt d.TemperamentConcentrationWeight p.TemperamentConcentrationWeight
    }

    let mergeDirectiveParams (p: PartialDirectiveParamsMap) (d: DirectiveParamsMap) : DirectiveParamsMap = {
        CompactnessSuccessDelta = opt d.CompactnessSuccessDelta p.CompactnessSuccessDelta
        CompactnessFailDelta = opt d.CompactnessFailDelta p.CompactnessFailDelta
        CompactnessSuccessThreshold = opt d.CompactnessSuccessThreshold p.CompactnessSuccessThreshold
        CompactnessFailThreshold = opt d.CompactnessFailThreshold p.CompactnessFailThreshold
        PressingSuccessDelta = opt d.PressingSuccessDelta p.PressingSuccessDelta
        PressingFailDelta = opt d.PressingFailDelta p.PressingFailDelta
        PressingSuccessThreshold = opt d.PressingSuccessThreshold p.PressingSuccessThreshold
        PressingFailThreshold = opt d.PressingFailThreshold p.PressingFailThreshold
        WingPlaySuccessDelta = opt d.WingPlaySuccessDelta p.WingPlaySuccessDelta
        WingPlayFailDelta = opt d.WingPlayFailDelta p.WingPlayFailDelta
        WingPlaySuccessThreshold = opt d.WingPlaySuccessThreshold p.WingPlaySuccessThreshold
        WingPlayFailThreshold = opt d.WingPlayFailThreshold p.WingPlayFailThreshold
    }

    let mergeEmergent (p: PartialEmergentWeights) (d: EmergentWeights) : EmergentWeights = {
        CompactnessSuccessDelta = opt d.CompactnessSuccessDelta p.CompactnessSuccessDelta
        CompactnessFailDelta = opt d.CompactnessFailDelta p.CompactnessFailDelta
        CompactnessSuccessThreshold = opt d.CompactnessSuccessThreshold p.CompactnessSuccessThreshold
        CompactnessFailThreshold = opt d.CompactnessFailThreshold p.CompactnessFailThreshold
        PressingSuccessDelta = opt d.PressingSuccessDelta p.PressingSuccessDelta
        PressingFailDelta = opt d.PressingFailDelta p.PressingFailDelta
        PressingSuccessThreshold = opt d.PressingSuccessThreshold p.PressingSuccessThreshold
        PressingFailThreshold = opt d.PressingFailThreshold p.PressingFailThreshold
        WingPlaySuccessDelta = opt d.WingPlaySuccessDelta p.WingPlaySuccessDelta
        WingPlayFailDelta = opt d.WingPlayFailDelta p.WingPlayFailDelta
        WingPlaySuccessThreshold = opt d.WingPlaySuccessThreshold p.WingPlaySuccessThreshold
        WingPlayFailThreshold = opt d.WingPlayFailThreshold p.WingPlayFailThreshold
        ConsecutiveLossPenalty = opt d.ConsecutiveLossPenalty p.ConsecutiveLossPenalty
        FatigueSpiralCompactnessFactor = opt d.FatigueSpiralCompactnessFactor p.FatigueSpiralCompactnessFactor
        FatigueSpiralPressingFactor = opt d.FatigueSpiralPressingFactor p.FatigueSpiralPressingFactor
        FatigueSpiralTempoFactor = opt d.FatigueSpiralTempoFactor p.FatigueSpiralTempoFactor
        FatigueSpiralRiskFactor = opt d.FatigueSpiralRiskFactor p.FatigueSpiralRiskFactor
        FatigueSpiralThreshold = opt d.FatigueSpiralThreshold p.FatigueSpiralThreshold
    }

    let mergeModifiers (p: PartialModifierWeights) (d: ModifierWeights) : ModifierWeights = {
        TransitionNearMult = opt d.TransitionNearMult p.TransitionNearMult
        TransitionFarMult = opt d.TransitionFarMult p.TransitionFarMult
        TransitionNearDistance = opt d.TransitionNearDistance p.TransitionNearDistance
        WeaknessSupportMult = opt d.WeaknessSupportMult p.WeaknessSupportMult
        RestDefenseSupportMult = opt d.RestDefenseSupportMult p.RestDefenseSupportMult
        RestDefenseCoverMult = opt d.RestDefenseCoverMult p.RestDefenseCoverMult
        ThreatCoverMult = opt d.ThreatCoverMult p.ThreatCoverMult
        HighLineSupportMult = opt d.HighLineSupportMult p.HighLineSupportMult
        LowBlockPressMult = opt d.LowBlockPressMult p.LowBlockPressMult
        LowBlockCoverMult = opt d.LowBlockCoverMult p.LowBlockCoverMult
        UrgencyPressMult = opt d.UrgencyPressMult p.UrgencyPressMult
        UrgencySupportMult = opt d.UrgencySupportMult p.UrgencySupportMult
        UrgencyThreshold = opt d.UrgencyThreshold p.UrgencyThreshold
    }

    let mergeChemistry (p: PartialChemistryWeights) (d: ChemistryWeights) : ChemistryWeights = {
        FamiliarityPassBonus = opt d.FamiliarityPassBonus p.FamiliarityPassBonus
        FamiliarityFailPenalty = opt d.FamiliarityFailPenalty p.FamiliarityFailPenalty
        FamiliarityTimeBonus = opt d.FamiliarityTimeBonus p.FamiliarityTimeBonus
        PressingCoordinationBase = opt d.PressingCoordinationBase p.PressingCoordinationBase
        PressingCoordinationFamiliarityMult = opt d.PressingCoordinationFamiliarityMult p.PressingCoordinationFamiliarityMult
        TransitionSpeedBase = opt d.TransitionSpeedBase p.TransitionSpeedBase
        TransitionSpeedFamiliarityMult = opt d.TransitionSpeedFamiliarityMult p.TransitionSpeedFamiliarityMult
    }

    let mergeTeamDirector (p: PartialTeamDirectorWeights) (d: TeamDirectorWeights) : TeamDirectorWeights = {
        WorkRateWeight = opt d.WorkRateWeight p.WorkRateWeight
        PositioningWeight = opt d.PositioningWeight p.PositioningWeight
        AdvancedBonus = opt d.AdvancedBonus p.AdvancedBonus
    }

    let mergeReactiveLoop (p: PartialReactiveLoopWeights) (d: ReactiveLoopWeights) : ReactiveLoopWeights = {
        ShapeDevWeight = opt d.ShapeDevWeight p.ShapeDevWeight
        PressDevWeight = opt d.PressDevWeight p.PressDevWeight
        FatigueDevWeight = opt d.FatigueDevWeight p.FatigueDevWeight
        OnTrackThreshold = opt d.OnTrackThreshold p.OnTrackThreshold
        DriftingThreshold = opt d.DriftingThreshold p.DriftingThreshold
    }

    let mergeCollective (p: PartialCollectiveWeights) (d: CollectiveWeights) : CollectiveWeights = {
        DirectiveParams = mergeDirectiveParams (Option.defaultValue { CompactnessSuccessDelta = None; CompactnessFailDelta = None; CompactnessSuccessThreshold = None; CompactnessFailThreshold = None; PressingSuccessDelta = None; PressingFailDelta = None; PressingSuccessThreshold = None; PressingFailThreshold = None; WingPlaySuccessDelta = None; WingPlayFailDelta = None; WingPlaySuccessThreshold = None; WingPlayFailThreshold = None } p.DirectiveParams) d.DirectiveParams
        Emergent = mergeEmergent (Option.defaultValue { CompactnessSuccessDelta = None; CompactnessFailDelta = None; CompactnessSuccessThreshold = None; CompactnessFailThreshold = None; PressingSuccessDelta = None; PressingFailDelta = None; PressingSuccessThreshold = None; PressingFailThreshold = None; WingPlaySuccessDelta = None; WingPlayFailDelta = None; WingPlaySuccessThreshold = None; WingPlayFailThreshold = None; ConsecutiveLossPenalty = None; FatigueSpiralCompactnessFactor = None; FatigueSpiralPressingFactor = None; FatigueSpiralTempoFactor = None; FatigueSpiralRiskFactor = None; FatigueSpiralThreshold = None } p.Emergent) d.Emergent
        Modifiers = mergeModifiers (Option.defaultValue { TransitionNearMult = None; TransitionFarMult = None; TransitionNearDistance = None; WeaknessSupportMult = None; RestDefenseSupportMult = None; RestDefenseCoverMult = None; ThreatCoverMult = None; HighLineSupportMult = None; LowBlockPressMult = None; LowBlockCoverMult = None; UrgencyPressMult = None; UrgencySupportMult = None; UrgencyThreshold = None } p.Modifiers) d.Modifiers
        Chemistry = mergeChemistry (Option.defaultValue { FamiliarityPassBonus = None; FamiliarityFailPenalty = None; FamiliarityTimeBonus = None; PressingCoordinationBase = None; PressingCoordinationFamiliarityMult = None; TransitionSpeedBase = None; TransitionSpeedFamiliarityMult = None } p.Chemistry) d.Chemistry
        TeamDirector = mergeTeamDirector (Option.defaultValue { WorkRateWeight = None; PositioningWeight = None; AdvancedBonus = None } p.TeamDirector) d.TeamDirector
        ReactiveLoop = mergeReactiveLoop (Option.defaultValue { ShapeDevWeight = None; PressDevWeight = None; FatigueDevWeight = None; OnTrackThreshold = None; DriftingThreshold = None } p.ReactiveLoop) d.ReactiveLoop
    }

    let mergeDuel (p: PartialDuelOutcomeWeights) (d: DuelOutcomeWeights) : DuelOutcomeWeights = {
        DuelSteepness = opt d.DuelSteepness p.DuelSteepness
        MomentumBonus = opt d.MomentumBonus p.MomentumBonus
        JitterWin = opt d.JitterWin p.JitterWin
        JitterRecover = opt d.JitterRecover p.JitterRecover
        JitterKeep = opt d.JitterKeep p.JitterKeep
        SpeedKeep = opt d.SpeedKeep p.SpeedKeep
        SpeedKeepVz = opt d.SpeedKeepVz p.SpeedKeepVz
        AttackerDribblingWeight = opt d.AttackerDribblingWeight p.AttackerDribblingWeight
        AttackerAgilityWeight = opt d.AttackerAgilityWeight p.AttackerAgilityWeight
        AttackerBalanceWeight = opt d.AttackerBalanceWeight p.AttackerBalanceWeight
        DefenderTacklingWeight = opt d.DefenderTacklingWeight p.DefenderTacklingWeight
        DefenderStrengthWeight = opt d.DefenderStrengthWeight p.DefenderStrengthWeight
        DefenderPositionWeight = opt d.DefenderPositionWeight p.DefenderPositionWeight
        FatigueThreshold = opt d.FatigueThreshold p.FatigueThreshold
        FatigueDecay = opt d.FatigueDecay p.FatigueDecay
    }

    let mergeShotOutcome (p: PartialShotOutcomeWeights) (d: ShotOutcomeWeights) : ShotOutcomeWeights = {
        OnTargetBase = opt d.OnTargetBase p.OnTargetBase
        OnTargetDistDecayRate = opt d.OnTargetDistDecayRate p.OnTargetDistDecayRate
        OnTargetDistMaxPenalty = opt d.OnTargetDistMaxPenalty p.OnTargetDistMaxPenalty
        QualityGate = opt d.QualityGate p.QualityGate
        AngleSpreadBase = opt d.AngleSpreadBase p.AngleSpreadBase
        VzBase = opt d.VzBase p.VzBase
        VzVariance = opt d.VzVariance p.VzVariance
        OnTargetMultiplier = opt d.OnTargetMultiplier p.OnTargetMultiplier
        OnTargetDistDivisor = opt d.OnTargetDistDivisor p.OnTargetDistDivisor
        NormalisationDistance = opt d.NormalisationDistance p.NormalisationDistance
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
        ShotWideMargin = opt d.ShotWideMargin p.ShotWideMargin
    }

    let mergePassOutcome (p: PartialPassOutcomeWeights) (d: PassOutcomeWeights) : PassOutcomeWeights = {
        BaseMean = opt d.BaseMean p.BaseMean
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
        InterceptionRadius = opt d.InterceptionRadius p.InterceptionRadius
        PressureDistance = opt d.PressureDistance p.PressureDistance
        DeflectPressureMultiplier = opt d.DeflectPressureMultiplier p.DeflectPressureMultiplier
        InterceptPaceWeight = opt d.InterceptPaceWeight p.InterceptPaceWeight
        InterceptPositioningWeight = opt d.InterceptPositioningWeight p.InterceptPositioningWeight
        ScrambleJitter = opt d.ScrambleJitter p.ScrambleJitter
        Speed = opt d.Speed p.Speed
        Vz = opt d.Vz p.Vz
        InterceptDistFactorWeight = opt d.InterceptDistFactorWeight p.InterceptDistFactorWeight
        InterceptPositioningContrib = opt d.InterceptPositioningContrib p.InterceptPositioningContrib
        InterceptVisionContrib = opt d.InterceptVisionContrib p.InterceptVisionContrib
        CreativityWeight = opt d.CreativityWeight p.CreativityWeight
        DirectnessWeight = opt d.DirectnessWeight p.DirectnessWeight
        MeanMin = opt d.MeanMin p.MeanMin
        MeanMax = opt d.MeanMax p.MeanMax
        DefaultNearestDefDist = opt d.DefaultNearestDefDist p.DefaultNearestDefDist
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
        LongBallSuccessConditionMultiplier = opt d.LongBallSuccessConditionMultiplier p.LongBallSuccessConditionMultiplier
        LongBallOffsideMomentum = opt d.LongBallOffsideMomentum p.LongBallOffsideMomentum
        LongBallSuccessMomentum = opt d.LongBallSuccessMomentum p.LongBallSuccessMomentum
        LongBallFailMomentum = opt d.LongBallFailMomentum p.LongBallFailMomentum
        LongBallSpeed = opt d.LongBallSpeed p.LongBallSpeed
        LongBallVz = opt d.LongBallVz p.LongBallVz
        LongBallDeflectMult = opt d.LongBallDeflectMult p.LongBallDeflectMult
        LongBallInterceptMult = opt d.LongBallInterceptMult p.LongBallInterceptMult
        LongBallPressureContrib = opt d.LongBallPressureContrib p.LongBallPressureContrib
        ForwardDepthThreshold = opt d.ForwardDepthThreshold p.ForwardDepthThreshold
        ForwardCreativityThreshold = opt d.ForwardCreativityThreshold p.ForwardCreativityThreshold
        LongBallScrambleJitterMult = opt d.LongBallScrambleJitterMult p.LongBallScrambleJitterMult
        PassLeadFactor = opt d.PassLeadFactor p.PassLeadFactor
    }

    let mergeCrossOutcome (p: PartialCrossOutcomeWeights) (d: CrossOutcomeWeights) : CrossOutcomeWeights = {
        BaseMean = opt d.BaseMean p.BaseMean
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
        Speed = opt d.Speed p.Speed
        Vz = opt d.Vz p.Vz
        AerialThreatThreshold = opt d.AerialThreatThreshold p.AerialThreatThreshold
        AttackingDepthThreshold = opt d.AttackingDepthThreshold p.AttackingDepthThreshold
        GkSkillDefault = opt d.GkSkillDefault p.GkSkillDefault
        GkSkillDivisor = opt d.GkSkillDivisor p.GkSkillDivisor
        SpinTopMult = opt d.SpinTopMult p.SpinTopMult
        SpinSideMult = opt d.SpinSideMult p.SpinSideMult
        FallbackSpeed = opt d.FallbackSpeed p.FallbackSpeed
        FallbackVz = opt d.FallbackVz p.FallbackVz
    }

    let mergeTackle (p: PartialTackleOutcomeWeights) (d: TackleOutcomeWeights) : TackleOutcomeWeights = {
        TechnicalWeight = opt d.TechnicalWeight p.TechnicalWeight
        PositioningWeight = opt d.PositioningWeight p.PositioningWeight
        StrengthWeight = opt d.StrengthWeight p.StrengthWeight
        AggressionWeight = opt d.AggressionWeight p.AggressionWeight
        PositioningReduction = opt d.PositioningReduction p.PositioningReduction
        FoulShapeBeta = opt d.FoulShapeBeta p.FoulShapeBeta
        FoulMomentum = opt d.FoulMomentum p.FoulMomentum
        SuccessMomentum = opt d.SuccessMomentum p.SuccessMomentum
        FailMomentum = opt d.FailMomentum p.FailMomentum
        TackleSteepness = opt d.TackleSteepness p.TackleSteepness
    }

    let mergeSetPiece (p: PartialSetPieceOutcomeWeights) (d: SetPieceOutcomeWeights) : SetPieceOutcomeWeights = {
        FreeKickTargetX = opt d.FreeKickTargetX p.FreeKickTargetX
        FreeKickSpeed = opt d.FreeKickSpeed p.FreeKickSpeed
        FreeKickVz = opt d.FreeKickVz p.FreeKickVz
        FreeKickSteepness = opt d.FreeKickSteepness p.FreeKickSteepness
        FreeKickSavePowerThreshold = opt d.FreeKickSavePowerThreshold p.FreeKickSavePowerThreshold
        FreeKickSaveVariance = opt d.FreeKickSaveVariance p.FreeKickSaveVariance
        FreeKickSpinTopMult = opt d.FreeKickSpinTopMult p.FreeKickSpinTopMult
        FreeKickSpinSideMult = opt d.FreeKickSpinSideMult p.FreeKickSpinSideMult
        CornerBoxXThreshold = opt d.CornerBoxXThreshold p.CornerBoxXThreshold
        CornerDefenderBoxThreshold = opt d.CornerDefenderBoxThreshold p.CornerDefenderBoxThreshold
        CornerSecondPhaseProbability = opt d.CornerSecondPhaseProbability p.CornerSecondPhaseProbability
        CornerKeepPossessionProbability = opt d.CornerKeepPossessionProbability p.CornerKeepPossessionProbability
        CornerSpeed = opt d.CornerSpeed p.CornerSpeed
        CornerVz = opt d.CornerVz p.CornerVz
        CornerDensityBase = opt d.CornerDensityBase p.CornerDensityBase
        CornerDensityPenalty = opt d.CornerDensityPenalty p.CornerDensityPenalty
        CornerLogisticSteepness = opt d.CornerLogisticSteepness p.CornerLogisticSteepness
        CornerDefScoreDefault = opt d.CornerDefScoreDefault p.CornerDefScoreDefault
        ThrowInSpeed = opt d.ThrowInSpeed p.ThrowInSpeed
        ThrowInVz = opt d.ThrowInVz p.ThrowInVz
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
        PenaltySpeed = opt d.PenaltySpeed p.PenaltySpeed
        PenaltyVzBase = opt d.PenaltyVzBase p.PenaltyVzBase
        PenaltyVzVariance = opt d.PenaltyVzVariance p.PenaltyVzVariance
        PenaltyAngleSpread = opt d.PenaltyAngleSpread p.PenaltyAngleSpread
        PostShotClearProbability = opt d.PostShotClearProbability p.PostShotClearProbability
        ClearSpeed = opt d.ClearSpeed p.ClearSpeed
        ClearVz = opt d.ClearVz p.ClearVz
        ClearYStdDev = opt d.ClearYStdDev p.ClearYStdDev
        GoalKickFallbackDistHome = opt d.GoalKickFallbackDistHome p.GoalKickFallbackDistHome
        GoalKickFallbackDistAway = opt d.GoalKickFallbackDistAway p.GoalKickFallbackDistAway
        KickOffPartnerOffsetX = opt d.KickOffPartnerOffsetX p.KickOffPartnerOffsetX
        KickOffPartnerOffsetY = opt d.KickOffPartnerOffsetY p.KickOffPartnerOffsetY
        FoulBaseRate = opt d.FoulBaseRate p.FoulBaseRate
        CornerOnFailedCross = opt d.CornerOnFailedCross p.CornerOnFailedCross
    }

    let mergeGK (p: PartialGKOutcomeWeights) (d: GKOutcomeWeights) : GKOutcomeWeights = {
        CatchHandlingMult = opt d.CatchHandlingMult p.CatchHandlingMult
        DiveReach = opt d.DiveReach p.DiveReach
        ParrySpeed = opt d.ParrySpeed p.ParrySpeed
        ParryDeflectionAngle = opt d.ParryDeflectionAngle p.ParryDeflectionAngle
        AerialReachMult = opt d.AerialReachMult p.AerialReachMult
        JumpReachMult = opt d.JumpReachMult p.JumpReachMult
        PunchProbability = opt d.PunchProbability p.PunchProbability
        ClaimCrossProbability = opt d.ClaimCrossProbability p.ClaimCrossProbability
        CollectionRadius = opt d.CollectionRadius p.CollectionRadius
        CollectionPriority = opt d.CollectionPriority p.CollectionPriority
        ThrowSpeed = opt d.ThrowSpeed p.ThrowSpeed
        RollSpeed = opt d.RollSpeed p.RollSpeed
        GoalKickSpeed = opt d.GoalKickSpeed p.GoalKickSpeed
        PuntSpeed = opt d.PuntSpeed p.PuntSpeed
        DistributionAccuracyMult = opt d.DistributionAccuracyMult p.DistributionAccuracyMult
        DistributionDecisionNoise = opt d.DistributionDecisionNoise p.DistributionDecisionNoise
        HoldTimeSubTicks = opt d.HoldTimeSubTicks p.HoldTimeSubTicks
        MaxHoldSubTicks = opt d.MaxHoldSubTicks p.MaxHoldSubTicks
        BackPassHandlingPenalty = opt d.BackPassHandlingPenalty p.BackPassHandlingPenalty
        GKDecisionWindowSubTicks = opt d.GKDecisionWindowSubTicks p.GKDecisionWindowSubTicks
    }

    let mergeXG (p: PartialXGWeights) (d: XGWeights) : XGWeights = {
        DistanceFactor = opt d.DistanceFactor p.DistanceFactor
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
        FirstTimeShotMultiplier = opt d.FirstTimeShotMultiplier p.FirstTimeShotMultiplier
    }

    let mergeInterception (p: PartialInterceptionWeights) (d: InterceptionWeights) : InterceptionWeights = {
        BallControlRadiusMult = opt d.BallControlRadiusMult p.BallControlRadiusMult
        PressIntentFactor = opt d.PressIntentFactor p.PressIntentFactor
        RecoverIntentFactor = opt d.RecoverIntentFactor p.RecoverIntentFactor
        MaintainShapeIntentFactor = opt d.MaintainShapeIntentFactor p.MaintainShapeIntentFactor
        CoverSpaceIntentFactor = opt d.CoverSpaceIntentFactor p.CoverSpaceIntentFactor
    }

    let mergeOutcomes (p: PartialOutcomeWeights) (d: OutcomeWeights) : OutcomeWeights = {
        Duel = mergeDuel (Option.defaultValue { DuelSteepness = None; MomentumBonus = None; JitterWin = None; JitterRecover = None; JitterKeep = None; SpeedKeep = None; SpeedKeepVz = None; AttackerDribblingWeight = None; AttackerAgilityWeight = None; AttackerBalanceWeight = None; DefenderTacklingWeight = None; DefenderStrengthWeight = None; DefenderPositionWeight = None; FatigueThreshold = None; FatigueDecay = None } p.Duel) d.Duel
        Shot = mergeShotOutcome (Option.defaultValue { OnTargetBase = None; OnTargetDistDecayRate = None; OnTargetDistMaxPenalty = None; QualityGate = None; AngleSpreadBase = None; VzBase = None; VzVariance = None; OnTargetMultiplier = None; OnTargetDistDivisor = None; NormalisationDistance = None; DistanceToGoalMultiplier = None; FinishingMin = None; FinishingMax = None; FinishingBonusST = None; FinishingBonusAM = None; FinishingBonusMC = None; FinishingBonusOther = None; CondFactorDivisor = None; ComposureMultiplier = None; UrgencyMultiplier = None; BasePowerDivisor = None; SpinTopMultiplier = None; PositionDirectnessWeight = None; PositionDepthWeight = None; PositionCreativityWeight = None; DistNormWeight = None; PositionBonusWeight = None; HeavyTouchDivisor = None; HeavyTouchMultiplier = None; JitterStdDev = None; GkReflexesStatMult = None; GkOneOnOneStatMult = None; SaveDenominatorOffset = None; ShotWideMargin = None } p.Shot) d.Shot
        Pass = mergePassOutcome (Option.defaultValue { BaseMean = None; DistancePenaltyPerMeter = None; LongPassPenaltyPerMeter = None; TechnicalWeight = None; VisionWeight = None; SuccessShapeAlpha = None; SuccessConditionMultiplier = None; OffsideMomentum = None; SuccessMomentum = None; FailMomentum = None; DeflectBaseRate = None; MisplacedBaseRate = None; InterceptBaseRate = None; InterceptionRadius = None; PressureDistance = None; DeflectPressureMultiplier = None; InterceptPaceWeight = None; InterceptPositioningWeight = None; ScrambleJitter = None; Speed = None; Vz = None; InterceptDistFactorWeight = None; InterceptPositioningContrib = None; InterceptVisionContrib = None; CreativityWeight = None; DirectnessWeight = None; MeanMin = None; MeanMax = None; DefaultNearestDefDist = None; DefaultTackling = None; HeavyTouchDivisor = None; HeavyTouchMultiplier = None; JitterStdDev = None; DeflectedSpeedMult = None; DeflectedVzMult = None; InterceptProbMax = None; MisplacedSpeedMult = None; LongBallBaseMean = None; LongBallLongShotsWeight = None; LongBallPassingWeight = None; LongBallVisionWeight = None; LongBallSuccessShapeAlpha = None; LongBallSuccessConditionMultiplier = None; LongBallOffsideMomentum = None; LongBallSuccessMomentum = None; LongBallFailMomentum = None; LongBallSpeed = None; LongBallVz = None; LongBallDeflectMult = None; LongBallInterceptMult = None; LongBallPressureContrib = None; ForwardDepthThreshold = None; ForwardCreativityThreshold = None; LongBallScrambleJitterMult = None; PassLeadFactor = None } p.Pass) d.Pass
        Cross = mergeCrossOutcome (Option.defaultValue { BaseMean = None; CrossingWeight = None; PassingWeight = None; SuccessShapeAlpha = None; SuccessConditionMultiplier = None; HeaderDuelSteepness = None; HeaderAccuracyBase = None; HeaderAccuracySkillMult = None; GkSaveBase = None; GkReflexesMult = None; GkAerialReachMult = None; GkJumpMult = None; ClaimCrossProbability = None; FailMomentum = None; Speed = None; Vz = None; AerialThreatThreshold = None; AttackingDepthThreshold = None; GkSkillDefault = None; GkSkillDivisor = None; SpinTopMult = None; SpinSideMult = None; FallbackSpeed = None; FallbackVz = None } p.Cross) d.Cross
        Tackle = mergeTackle (Option.defaultValue { TechnicalWeight = None; PositioningWeight = None; StrengthWeight = None; AggressionWeight = None; PositioningReduction = None; FoulShapeBeta = None; FoulMomentum = None; SuccessMomentum = None; FailMomentum = None; TackleSteepness = None } p.Tackle) d.Tackle
        SetPiece = mergeSetPiece (Option.defaultValue { FreeKickTargetX = None; FreeKickSpeed = None; FreeKickVz = None; FreeKickSteepness = None; FreeKickSavePowerThreshold = None; FreeKickSaveVariance = None; FreeKickSpinTopMult = None; FreeKickSpinSideMult = None; CornerBoxXThreshold = None; CornerDefenderBoxThreshold = None; CornerSecondPhaseProbability = None; CornerKeepPossessionProbability = None; CornerSpeed = None; CornerVz = None; CornerDensityBase = None; CornerDensityPenalty = None; CornerLogisticSteepness = None; CornerDefScoreDefault = None; ThrowInSpeed = None; ThrowInVz = None; ThrowInMomentum = None; PenaltySkillMultiplier = None; PenaltyMoraleMultiplier = None; PenaltyPressureMultiplier = None; PenaltyComposureNoise = None; PenaltyLogisticBase = None; PenaltyGkReflexesMult = None; PenaltyGkHandlingMult = None; PenaltySkillDivisor = None; PenaltyCondDivisor = None; PenaltyMoraleBase = None; PenaltyMoraleDivisor = None; PenaltyGkSkillDivisor = None; PenaltySpeed = None; PenaltyVzBase = None; PenaltyVzVariance = None; PenaltyAngleSpread = None; PostShotClearProbability = None; ClearSpeed = None; ClearVz = None; ClearYStdDev = None; GoalKickFallbackDistHome = None; GoalKickFallbackDistAway = None; KickOffPartnerOffsetX = None; KickOffPartnerOffsetY = None; FoulBaseRate = None; CornerOnFailedCross = None } p.SetPiece) d.SetPiece
        GK = mergeGK (Option.defaultValue { CatchHandlingMult = None; DiveReach = None; ParrySpeed = None; ParryDeflectionAngle = None; AerialReachMult = None; JumpReachMult = None; PunchProbability = None; ClaimCrossProbability = None; CollectionRadius = None; CollectionPriority = None; ThrowSpeed = None; RollSpeed = None; GoalKickSpeed = None; PuntSpeed = None; DistributionAccuracyMult = None; DistributionDecisionNoise = None; HoldTimeSubTicks = None; MaxHoldSubTicks = None; BackPassHandlingPenalty = None; GKDecisionWindowSubTicks = None } p.GK) d.GK
        XG = mergeXG (Option.defaultValue { DistanceFactor = None; AngleExponent = None; BaseMultiplier = None; OneOnOneMultiplier = None; SetPieceMultiplier = None; PressureReduction = None; HeaderMultiplier = None; VolleyMultiplier = None; HalfVolleyMultiplier = None; ChipShotMultiplier = None; CurlerMultiplier = None; DrivenShotMultiplier = None; PlacedShotMultiplier = None; FirstTimeShotMultiplier = None } p.XG) d.XG
        Interception = mergeInterception (Option.defaultValue { BallControlRadiusMult = None; PressIntentFactor = None; RecoverIntentFactor = None; MaintainShapeIntentFactor = None; CoverSpaceIntentFactor = None } p.Interception) d.Interception
    }

    let mergeWinProb (p: PartialWinProbabilityWeights) (d: WinProbabilityWeights) : WinProbabilityWeights = {
        GoalLeadBase = opt d.GoalLeadBase p.GoalLeadBase
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
        MomentumLinearFactor = opt d.MomentumLinearFactor p.MomentumLinearFactor
    }

    let mergeUtility (p: PartialUtilityWeights) (d: UtilityWeights) : UtilityWeights = {
        PressZoneBonus_HighAttacking = opt d.PressZoneBonus_HighAttacking p.PressZoneBonus_HighAttacking
        PressZoneBonus_HighMidfield = opt d.PressZoneBonus_HighMidfield p.PressZoneBonus_HighMidfield
        PressZoneBonus_HighDefensive = opt d.PressZoneBonus_HighDefensive p.PressZoneBonus_HighDefensive
        PressZoneBonus_MidAttackingMidfield = opt d.PressZoneBonus_MidAttackingMidfield p.PressZoneBonus_MidAttackingMidfield
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
        OverloadFlankBase = opt d.OverloadFlankBase p.OverloadFlankBase
    }

    let mergePerformance (p: PartialPerformanceWeightsMap) (d: PerformanceWeightsMap) : PerformanceWeightsMap = {
        DuelStatWeight = opt d.DuelStatWeight p.DuelStatWeight
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
        DecisionCurveInflection = opt d.DecisionCurveInflection p.DecisionCurveInflection
    }

    let mergeReferee (p: PartialRefereeWeights) (d: RefereeWeights) : RefereeWeights = {
        CardBaseProb = opt d.CardBaseProb p.CardBaseProb
        CardAggressionMult = opt d.CardAggressionMult p.CardAggressionMult
        CardHomeReduction = opt d.CardHomeReduction p.CardHomeReduction
        InjuryBaseProb = opt d.InjuryBaseProb p.InjuryBaseProb
        InjuryStrengthInverseMult = opt d.InjuryStrengthInverseMult p.InjuryStrengthInverseMult
        FoulAggressionBase = opt d.FoulAggressionBase p.FoulAggressionBase
        FoulAggressionMult = opt d.FoulAggressionMult p.FoulAggressionMult
    }

    let mergeEnvironment (p: PartialEnvironmentWeights) (d: EnvironmentWeights) : EnvironmentWeights = {
        WeatherClearModifier = opt d.WeatherClearModifier p.WeatherClearModifier
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
        AwayPressureCrowdMult = opt d.AwayPressureCrowdMult p.AwayPressureCrowdMult
    }

    let mergeMomentum (p: PartialMomentumWeights) (d: MomentumWeights) : MomentumWeights = {
        EventDelta = opt d.EventDelta p.EventDelta
        Decay = opt d.Decay p.Decay
        Min = opt d.Min p.Min
        Max = opt d.Max p.Max
        HalfLifeSeconds = opt d.HalfLifeSeconds p.HalfLifeSeconds
    }

    let mergeHomeAdv (p: PartialHomeAdvantageWeights) (d: HomeAdvantageWeights) : HomeAdvantageWeights = {
        Strength = opt d.Strength p.Strength
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
        FatigueReduction = opt d.FatigueReduction p.FatigueReduction
    }

    let mergeManager (p: PartialManagerWeights) (d: ManagerWeights) : ManagerWeights = {
        FatigueReactionThreshold = opt d.FatigueReactionThreshold p.FatigueReactionThreshold
        SustainedMomentumSubTicks = opt d.SustainedMomentumSubTicks p.SustainedMomentumSubTicks
        MomentumThreshold = opt d.MomentumThreshold p.MomentumThreshold
        FatigueCheckSubTicks = opt d.FatigueCheckSubTicks p.FatigueCheckSubTicks
        ConditionThresholdLosing = opt d.ConditionThresholdLosing p.ConditionThresholdLosing
        ConditionThresholdDrawing = opt d.ConditionThresholdDrawing p.ConditionThresholdDrawing
        ConditionThresholdWinning = opt d.ConditionThresholdWinning p.ConditionThresholdWinning
    }

    let mergePerception (p: PartialPerceptionWeights) (d: PerceptionWeights) : PerceptionWeights = {
        VisionRadiusBase = opt d.VisionRadiusBase p.VisionRadiusBase
        VisionRadiusMax = opt d.VisionRadiusMax p.VisionRadiusMax
        VisionConeAngle = opt d.VisionConeAngle p.VisionConeAngle
        PeripheralMultiplier = opt d.PeripheralMultiplier p.PeripheralMultiplier
        MinimumAwarenessFloor = opt d.MinimumAwarenessFloor p.MinimumAwarenessFloor
        AnticipationBonusRadius = opt d.AnticipationBonusRadius p.AnticipationBonusRadius
        GoalkeeperConeAngle = opt d.GoalkeeperConeAngle p.GoalkeeperConeAngle
        CommunicationRange = opt d.CommunicationRange p.CommunicationRange
        SetPieceSimplifiedRadius = opt d.SetPieceSimplifiedRadius p.SetPieceSimplifiedRadius
        BlindPassVisionThreshold = opt d.BlindPassVisionThreshold p.BlindPassVisionThreshold
        BlindPassComposureThreshold = opt d.BlindPassComposureThreshold p.BlindPassComposureThreshold
        BlindPassSuccessPenalty = opt d.BlindPassSuccessPenalty p.BlindPassSuccessPenalty
    }

    let mergeDevelopment (p: PartialDevelopmentWeights) (d: DevelopmentWeights) : DevelopmentWeights = {
        AgeBracket_MaxDelta_U21 = opt d.AgeBracket_MaxDelta_U21 p.AgeBracket_MaxDelta_U21
        AgeBracket_MaxDelta_U25 = opt d.AgeBracket_MaxDelta_U25 p.AgeBracket_MaxDelta_U25
        AgeBracket_MaxDelta_U28 = opt d.AgeBracket_MaxDelta_U28 p.AgeBracket_MaxDelta_U28
        AgeBracket_MaxDelta_U31 = opt d.AgeBracket_MaxDelta_U31 p.AgeBracket_MaxDelta_U31
        AgeBracket_MaxDelta_U34 = opt d.AgeBracket_MaxDelta_U34 p.AgeBracket_MaxDelta_U34
        FocusMultiplier_Goalkeeping = opt d.FocusMultiplier_Goalkeeping p.FocusMultiplier_Goalkeeping
        FocusMultiplier_PhysicalBase = opt d.FocusMultiplier_PhysicalBase p.FocusMultiplier_PhysicalBase
        FocusMultiplier_Physical_Pressing = opt d.FocusMultiplier_Physical_Pressing p.FocusMultiplier_Physical_Pressing
        FocusMultiplier_Physical_Positional = opt d.FocusMultiplier_Physical_Positional p.FocusMultiplier_Physical_Positional
        FocusMultiplier_Mental = opt d.FocusMultiplier_Mental p.FocusMultiplier_Mental
        FocusMultiplier_TechnicalBase = opt d.FocusMultiplier_TechnicalBase p.FocusMultiplier_TechnicalBase
        FocusMultiplier_Technical_Creativity = opt d.FocusMultiplier_Technical_Creativity p.FocusMultiplier_Technical_Creativity
        FocusMultiplier_Technical_Directness = opt d.FocusMultiplier_Technical_Directness p.FocusMultiplier_Technical_Directness
        WeeklyDeltaDivisor = opt d.WeeklyDeltaDivisor p.WeeklyDeltaDivisor
        StatFocus_DirectnessThreshold = opt d.StatFocus_DirectnessThreshold p.StatFocus_DirectnessThreshold
        StatFocus_AttackingDepthThreshold = opt d.StatFocus_AttackingDepthThreshold p.StatFocus_AttackingDepthThreshold
        StatFocus_DefensiveHeightThreshold = opt d.StatFocus_DefensiveHeightThreshold p.StatFocus_DefensiveHeightThreshold
        StatFocus_CreativityThreshold = opt d.StatFocus_CreativityThreshold p.StatFocus_CreativityThreshold
        MaybeStat_PositiveThreshold = opt d.MaybeStat_PositiveThreshold p.MaybeStat_PositiveThreshold
        MaybeStat_NegativeThreshold = opt d.MaybeStat_NegativeThreshold p.MaybeStat_NegativeThreshold
    }

    let mergeCalTargets (p: PartialCalibrationTargets) (d: CalibrationTargets) : CalibrationTargets = {
        GoalsPerMatch = opt d.GoalsPerMatch p.GoalsPerMatch
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
        DuelTicksPerMatch = opt d.DuelTicksPerMatch p.DuelTicksPerMatch
    }

// ============================================================================
// PUBLIC API
// ============================================================================

module WeightsLoader =

    let mergeWithDefaults (partial: PartialEngineWeights) (defaults: EngineWeights) : EngineWeights = {
        Version = Option.defaultValue defaults.Version partial.Version
        ProfileWeights = Merger.mergeProfile (Option.defaultValue { PositionalFreedom_PositioningWeight = None; PositionalFreedom_VisionWeight = None; PositionalFreedom_StaminaWeight = None; PositionalFreedom_ConcentrationWeight = None; PositionalFreedom_BalanceWeight = None; PositionalFreedom_AgilityWeight = None; AttackingDepth_PaceWeight = None; AttackingDepth_AccelerationWeight = None; AttackingDepth_FinishingWeight = None; AttackingDepth_ComposureWeight = None; AttackingDepth_StaminaWeight = None; AttackingDepth_PositionBaseMultiplier = None; LateralTendency_CrossingWeight = None; LateralTendency_PaceWeight = None; DefensiveHeight_WorkRateWeight = None; DefensiveHeight_TacklingWeight = None; DefensiveHeight_PositioningWeight = None; DefensiveHeight_StaminaWeight = None; DefensiveHeight_PositionBaseMultiplier = None; PressingIntensity_StaminaWeight = None; PressingIntensity_WorkRateWeight = None; PressingIntensity_AggressionWeight = None; PressingIntensity_PaceWeight = None; PressingIntensity_ConcentrationWeight = None; RiskAppetite_PassingWeight = None; RiskAppetite_LongShotsWeight = None; RiskAppetite_VisionWeight = None; RiskAppetite_ComposureWeight = None; RiskAppetite_DribblingWeight = None; RiskAppetite_BraveryWeight = None; Directness_FinishingWeight = None; Directness_PaceWeight = None; Directness_AccelerationWeight = None; Directness_AggressionWeight = None; Directness_DribblingWeight = None; Directness_StrengthWeight = None; Directness_InversePassingWeight = None; CreativityWeight_PassingWeight = None; CreativityWeight_VisionWeight = None; CreativityWeight_BallControlWeight = None; CreativityWeight_DribblingWeight = None; CreativityWeight_ComposureWeight = None; CreativityWeight_CrossingWeight = None; AerialThreat_JumpingReachWeight = None; AerialThreat_HeadingWeight = None; AerialThreat_StrengthWeight = None; AerialThreat_BraveryWeight = None; HoldUpPlay_StrengthWeight = None; HoldUpPlay_BallControlWeight = None; HoldUpPlay_ComposureWeight = None; HoldUpPlay_PassingWeight = None; HoldUpPlay_HeadingWeight = None; HoldUpPlay_BalanceWeight = None } partial.ProfileWeights) defaults.ProfileWeights
        Individual = Merger.mergeIndividual (Option.defaultValue { Shoot = None; Pass = None; Dribble = None; Cross = None; LongBall = None; SoftmaxTemperature = None; DirectnessBlendTactic = None; DirectnessBlendProfile = None } partial.Individual) defaults.Individual
        Personality = Merger.mergePersonality (Option.defaultValue { FlairVisionWeight = None; FlairDribblingWeight = None; ConsistencyConcentrationWeight = None; ConsistencyComposureWeight = None; LeadershipWeight = None; ControversyAggressionWeight = None; ControversyComposureWeight = None; TeamworkWorkRateWeight = None; TeamworkPositioningWeight = None; AmbitionMoraleWeight = None; AmbitionWorkRateWeight = None; PressureComposureWeight = None; PressureConcentrationWeight = None; SportsmanshipAggressionWeight = None; TemperamentComposureWeight = None; TemperamentConcentrationWeight = None } partial.Personality) defaults.Personality
        Collective = Merger.mergeCollective (Option.defaultValue { DirectiveParams = None; Emergent = None; Modifiers = None; Chemistry = None; TeamDirector = None; ReactiveLoop = None } partial.Collective) defaults.Collective
        Outcomes = Merger.mergeOutcomes (Option.defaultValue { Duel = None; Shot = None; Pass = None; Cross = None; Tackle = None; SetPiece = None; GK = None; XG = None; Interception = None } partial.Outcomes) defaults.Outcomes
        WinProbability = Merger.mergeWinProb (Option.defaultValue { GoalLeadBase = None; DrawBase = None; GoalDiffFactor = None; XGFactor = None; HomeAdvantage = None; GoalDiffSteepness = None; XGDiffSteepness = None; MinutePressure = None; ComebackBonus = None; MomentumPositiveThreshold = None; MomentumNegativeThreshold = None; MomentumPositiveBonus = None; MomentumNegativePenalty = None; MomentumLinearFactor = None } partial.WinProbability) defaults.WinProbability
        Utility = Merger.mergeUtility (Option.defaultValue { PressZoneBonus_HighAttacking = None; PressZoneBonus_HighMidfield = None; PressZoneBonus_HighDefensive = None; PressZoneBonus_MidAttackingMidfield = None; PressZoneBonus_MidDefensive = None; PressZoneBonus_Low = None; PossessionChangeWindow = None; ScoreDiffPressStep = None; WingSpaceBase = None; StaminaWingMult = None; StructuredBase = None; DirectiveChangeThreshold = None; DropDeepHighLinePenalty = None; DropDeepLeadBonus = None; DropDeepTimeBonus = None; DropDeepBase = None; CounterPressStaminaFactor = None; CounterPressIntensityBonus = None; CounterPressBase = None; BuildFromBackNoPressBonus = None; BuildFromBackMidPressBonus = None; BuildFromBackHighPressPenalty = None; BuildFromBackLowBlockBonus = None; BuildFromBackBase = None; DirectPlayUrgencyBonus = None; DirectPlayUrgencyBonusAny = None; DirectPlayHighLineBonus = None; DirectPlayBase = None; SitAndCounterBase = None; SitAndCounterLeadBonus = None; SitAndCounterStaminaFactor = None; HoldPossessionLeadBonus = None; HoldPossessionDrawBonus = None; HoldPossessionLosingPenalty = None; HoldPossessionTimeBonus = None; HoldPossessionPressPenalty = None; HoldPossessionBase = None; CompactBlockLosingBonus = None; CompactBlockWinningPenalty = None; CompactBlockOpponentBonus = None; CompactBlockTimeBonus = None; CompactBlockBase = None; HighLineCohesionBonus = None; HighLineStaminaFactor = None; HighLineRiskPenalty = None; HighLineBase = None; PressingSuccessBonus = None; OpponentHighLineNoPressBonus = None; OverloadWeaknessBonus = None; OverloadFlankBase = None } partial.Utility) defaults.Utility
        Performance = Merger.mergePerformance (Option.defaultValue { DuelStatWeight = None; DuelConditionWeight = None; DuelMoraleWeight = None; DuelCurveSteepness = None; DuelCurveInflection = None; TechnicalStatWeight = None; TechnicalConditionWeight = None; TechnicalMoraleWeight = None; TechnicalCurveSteepness = None; TechnicalCurveInflection = None; DecisionStatWeight = None; DecisionConditionWeight = None; DecisionMoraleWeight = None; DecisionCurveSteepness = None; DecisionCurveInflection = None } partial.Performance) defaults.Performance
        Referee = Merger.mergeReferee (Option.defaultValue { CardBaseProb = None; CardAggressionMult = None; CardHomeReduction = None; InjuryBaseProb = None; InjuryStrengthInverseMult = None; FoulAggressionBase = None; FoulAggressionMult = None } partial.Referee) defaults.Referee
        Environment = Merger.mergeEnvironment (Option.defaultValue { WeatherClearModifier = None; WeatherLightRainModifier = None; WeatherHeavyRainModifier = None; WeatherSnowModifier = None; WeatherWindyModifier = None; PitchDrySlipBase = None; PitchDampSlipBase = None; PitchWetSlipBase = None; PitchWaterloggedSlipBase = None; SlipAgilityReduction = None; CrowdMaxCapacity = None; CrowdCapacityWeight = None; CrowdSupportWeight = None; CrowdMomentumWeight = None; CrowdImportanceWeight = None; CrowdMaxAdvantage = None; AwayPressureCrowdMult = None } partial.Environment) defaults.Environment
        Momentum = Merger.mergeMomentum (Option.defaultValue { EventDelta = None; Decay = None; Min = None; Max = None; HalfLifeSeconds = None } partial.Momentum) defaults.Momentum
        HomeAdvantage = Merger.mergeHomeAdv (Option.defaultValue { Strength = None; DuelAttackBonus = None; DuelDefenseBonus = None; ShotComposureBonus = None; PassAccuracyBonus = None; DribbleBonus = None; SetPlayAccuracyBonus = None; TackleBonus = None; FreeKickComposure = None; PenaltyBonus = None; CardReduction = None; FatigueReduction = None } partial.HomeAdvantage) defaults.HomeAdvantage
        Manager = Merger.mergeManager (Option.defaultValue { FatigueReactionThreshold = None; SustainedMomentumSubTicks = None; MomentumThreshold = None; FatigueCheckSubTicks = None; ConditionThresholdLosing = None; ConditionThresholdDrawing = None; ConditionThresholdWinning = None } partial.Manager) defaults.Manager
        Perception = Merger.mergePerception (Option.defaultValue { VisionRadiusBase = None; VisionRadiusMax = None; VisionConeAngle = None; PeripheralMultiplier = None; MinimumAwarenessFloor = None; AnticipationBonusRadius = None; GoalkeeperConeAngle = None; CommunicationRange = None; SetPieceSimplifiedRadius = None; BlindPassVisionThreshold = None; BlindPassComposureThreshold = None; BlindPassSuccessPenalty = None } partial.Perception) defaults.Perception
        Development = Merger.mergeDevelopment (Option.defaultValue { AgeBracket_MaxDelta_U21 = None; AgeBracket_MaxDelta_U25 = None; AgeBracket_MaxDelta_U28 = None; AgeBracket_MaxDelta_U31 = None; AgeBracket_MaxDelta_U34 = None; FocusMultiplier_Goalkeeping = None; FocusMultiplier_PhysicalBase = None; FocusMultiplier_Physical_Pressing = None; FocusMultiplier_Physical_Positional = None; FocusMultiplier_Mental = None; FocusMultiplier_TechnicalBase = None; FocusMultiplier_Technical_Creativity = None; FocusMultiplier_Technical_Directness = None; WeeklyDeltaDivisor = None; StatFocus_DirectnessThreshold = None; StatFocus_AttackingDepthThreshold = None; StatFocus_DefensiveHeightThreshold = None; StatFocus_CreativityThreshold = None; MaybeStat_PositiveThreshold = None; MaybeStat_NegativeThreshold = None } partial.Development) defaults.Development
        CalibrationTargets = Merger.mergeCalTargets (Option.defaultValue { GoalsPerMatch = None; ShotsPerMatch = None; PassSuccessRate = None; CrossSuccessRate = None; HomeWinPct = None; DrawPct = None; AwayWinPct = None; CardsPerMatch = None; InjuriesPerMatch = None; DribblesPerMatch = None; CrossesPerMatch = None; LongBallsPerMatch = None; DuelTicksPerMatch = None } partial.CalibrationTargets) defaults.CalibrationTargets
    }

    /// Converts EngineWeights to BalanceConfig.
    /// Sections not in EngineWeights (Physics, Timing, MatchVolume, BuildUp, Decision, GK)
    /// use BalanceConfig.defaultConfig values directly.
    let toBalanceConfig (w: EngineWeights) : BalanceConfig =
        let dc = BalanceConfig.defaultConfig
        { Duel = {
            DuelSteepness = w.Outcomes.Duel.DuelSteepness
            MomentumBonus = w.Outcomes.Duel.MomentumBonus
            JitterWin = w.Outcomes.Duel.JitterWin
            JitterRecover = w.Outcomes.Duel.JitterRecover
            JitterKeep = w.Outcomes.Duel.JitterKeep
            SpeedKeep = w.Outcomes.Duel.SpeedKeep * 1.0<meter/second>
            SpeedKeepVz = w.Outcomes.Duel.SpeedKeepVz * 1.0<meter/second>
            AttackerDribblingWeight = w.Outcomes.Duel.AttackerDribblingWeight
            AttackerAgilityWeight = w.Outcomes.Duel.AttackerAgilityWeight
            AttackerBalanceWeight = w.Outcomes.Duel.AttackerBalanceWeight
            DefenderTacklingWeight = w.Outcomes.Duel.DefenderTacklingWeight
            DefenderStrengthWeight = w.Outcomes.Duel.DefenderStrengthWeight
            DefenderPositionWeight = w.Outcomes.Duel.DefenderPositionWeight
            FatigueThreshold = w.Outcomes.Duel.FatigueThreshold
            FatigueDecay = w.Outcomes.Duel.FatigueDecay
          }
          Shot = {
            QualityGate = w.Outcomes.Shot.QualityGate
            AngleSpreadBase = w.Outcomes.Shot.AngleSpreadBase
            VzBase = w.Outcomes.Shot.VzBase * 1.0<meter/second>
            VzVariance = w.Outcomes.Shot.VzVariance
            OnTargetBase = w.Outcomes.Shot.OnTargetBase
            OnTargetMultiplier = w.Outcomes.Shot.OnTargetMultiplier
            OnTargetDistDecayRate = w.Outcomes.Shot.OnTargetDistDecayRate
            OnTargetDistMaxPenalty = w.Outcomes.Shot.OnTargetDistMaxPenalty
            OnTargetDistDivisor = w.Outcomes.Shot.OnTargetDistDivisor
            NormalisationDistance = w.Outcomes.Shot.NormalisationDistance * 1.0<meter>
            DistanceToGoalMultiplier = w.Outcomes.Shot.DistanceToGoalMultiplier
            FinishingMin = w.Outcomes.Shot.FinishingMin
            FinishingMax = w.Outcomes.Shot.FinishingMax
            FinishingBonusST = w.Outcomes.Shot.FinishingBonusST
            FinishingBonusAM = w.Outcomes.Shot.FinishingBonusAM
            FinishingBonusMC = w.Outcomes.Shot.FinishingBonusMC
            FinishingBonusOther = w.Outcomes.Shot.FinishingBonusOther
            CondFactorDivisor = w.Outcomes.Shot.CondFactorDivisor
            ComposureMultiplier = w.Outcomes.Shot.ComposureMultiplier
            UrgencyMultiplier = w.Outcomes.Shot.UrgencyMultiplier
            BasePowerDivisor = w.Outcomes.Shot.BasePowerDivisor
            SpinTopMultiplier = w.Outcomes.Shot.SpinTopMultiplier
            PositionDirectnessWeight = w.Outcomes.Shot.PositionDirectnessWeight
            PositionDepthWeight = w.Outcomes.Shot.PositionDepthWeight
            PositionCreativityWeight = w.Outcomes.Shot.PositionCreativityWeight
            DistNormWeight = w.Outcomes.Shot.DistNormWeight
            PositionBonusWeight = w.Outcomes.Shot.PositionBonusWeight
            HeavyTouchDivisor = w.Outcomes.Shot.HeavyTouchDivisor
            HeavyTouchMultiplier = w.Outcomes.Shot.HeavyTouchMultiplier
            JitterStdDev = w.Outcomes.Shot.JitterStdDev
            GkReflexesStatMult = w.Outcomes.Shot.GkReflexesStatMult
            GkOneOnOneStatMult = w.Outcomes.Shot.GkOneOnOneStatMult
            SaveDenominatorOffset = w.Outcomes.Shot.SaveDenominatorOffset
            ShotWideMargin = w.Outcomes.Shot.ShotWideMargin * 1.0<meter>
          }
          Pass = dc.Pass  // Not in EngineWeights v1 — using defaultConfig values
          Cross = {
            BaseMean = w.Outcomes.Cross.BaseMean
            CrossingWeight = w.Outcomes.Cross.CrossingWeight
            PassingWeight = w.Outcomes.Cross.PassingWeight
            SuccessShapeAlpha = w.Outcomes.Cross.SuccessShapeAlpha
            SuccessConditionMultiplier = w.Outcomes.Cross.SuccessConditionMultiplier
            HeaderDuelSteepness = w.Outcomes.Cross.HeaderDuelSteepness
            HeaderAccuracyBase = w.Outcomes.Cross.HeaderAccuracyBase
            HeaderAccuracySkillMult = w.Outcomes.Cross.HeaderAccuracySkillMult
            GkSaveBase = w.Outcomes.Cross.GkSaveBase
            GkReflexesMult = w.Outcomes.Cross.GkReflexesMult
            GkAerialReachMult = w.Outcomes.Cross.GkAerialReachMult
            GkJumpMult = w.Outcomes.Cross.GkJumpMult
            ClaimCrossProbability = w.Outcomes.Cross.ClaimCrossProbability
            FailMomentum = w.Outcomes.Cross.FailMomentum
            Speed = w.Outcomes.Cross.Speed * 1.0<meter/second>
            Vz = w.Outcomes.Cross.Vz * 1.0<meter/second>
            AerialThreatThreshold = w.Outcomes.Cross.AerialThreatThreshold
            AttackingDepthThreshold = w.Outcomes.Cross.AttackingDepthThreshold
            GkSkillDefault = w.Outcomes.Cross.GkSkillDefault
            GkSkillDivisor = w.Outcomes.Cross.GkSkillDivisor
            SpinTopMult = w.Outcomes.Cross.SpinTopMult
            SpinSideMult = w.Outcomes.Cross.SpinSideMult
            FallbackSpeed = w.Outcomes.Cross.FallbackSpeed * 1.0<meter/second>
            FallbackVz = w.Outcomes.Cross.FallbackVz * 1.0<meter/second>
          }
          Dribble = dc.Dribble  // Not in EngineWeights v1
          Tackle = {
            TechnicalWeight = w.Outcomes.Tackle.TechnicalWeight
            PositioningWeight = w.Outcomes.Tackle.PositioningWeight
            StrengthWeight = w.Outcomes.Tackle.StrengthWeight
            AggressionWeight = w.Outcomes.Tackle.AggressionWeight
            PositioningReduction = w.Outcomes.Tackle.PositioningReduction
            FoulShapeBeta = w.Outcomes.Tackle.FoulShapeBeta
            FoulMomentum = w.Outcomes.Tackle.FoulMomentum
            SuccessMomentum = w.Outcomes.Tackle.SuccessMomentum
            FailMomentum = w.Outcomes.Tackle.FailMomentum
            TackleSteepness = w.Outcomes.Tackle.TackleSteepness
          }
          SetPiece = dc.SetPiece  // Not in EngineWeights v1
          GK = dc.GK  // Not in EngineWeights v1
          HomeAdvantage = {
            Strength = w.HomeAdvantage.Strength
            DuelAttackBonus = w.HomeAdvantage.DuelAttackBonus
            DuelDefenseBonus = w.HomeAdvantage.DuelDefenseBonus
            ShotComposureBonus = w.HomeAdvantage.ShotComposureBonus
            PassAccuracyBonus = w.HomeAdvantage.PassAccuracyBonus
            DribbleBonus = w.HomeAdvantage.DribbleBonus
            SetPlayAccuracyBonus = w.HomeAdvantage.SetPlayAccuracyBonus
            TackleBonus = w.HomeAdvantage.TackleBonus
            FreeKickComposure = w.HomeAdvantage.FreeKickComposure
            PenaltyBonus = w.HomeAdvantage.PenaltyBonus
            CardReduction = w.HomeAdvantage.CardReduction
            FatigueReduction = w.HomeAdvantage.FatigueReduction
          }
          Physics = dc.Physics  // Not in EngineWeights v1
          Timing = dc.Timing  // Not in EngineWeights v1
          MatchVolume = dc.MatchVolume  // Not in EngineWeights v1
          Manager = {
            FatigueReactionThreshold = w.Manager.FatigueReactionThreshold
            SustainedMomentumSubTicks = w.Manager.SustainedMomentumSubTicks
            MomentumThreshold = w.Manager.MomentumThreshold
            FatigueCheckSubTicks = w.Manager.FatigueCheckSubTicks
            ConditionThresholdLosing = w.Manager.ConditionThresholdLosing
            ConditionThresholdDrawing = w.Manager.ConditionThresholdDrawing
            ConditionThresholdWinning = w.Manager.ConditionThresholdWinning
            SubWindowMinutes = dc.Manager.SubWindowMinutes  // Not in EngineWeights v1
          }
          BuildUp = dc.BuildUp  // Not in EngineWeights v1
          Decision = dc.Decision  // Not in EngineWeights v1
          Perception = {
            VisionRadiusBase = w.Perception.VisionRadiusBase * 1.0<meter>
            VisionRadiusMax = w.Perception.VisionRadiusMax * 1.0<meter>
            VisionConeAngle = w.Perception.VisionConeAngle
            PeripheralMultiplier = w.Perception.PeripheralMultiplier
            MinimumAwarenessFloor = w.Perception.MinimumAwarenessFloor * 1.0<meter>
            AnticipationBonusRadius = w.Perception.AnticipationBonusRadius * 1.0<meter>
            GoalkeeperConeAngle = w.Perception.GoalkeeperConeAngle
            CommunicationRange = w.Perception.CommunicationRange * 1.0<meter>
            SetPieceSimplifiedRadius = w.Perception.SetPieceSimplifiedRadius * 1.0<meter>
            BlindPassVisionThreshold = w.Perception.BlindPassVisionThreshold
            BlindPassComposureThreshold = w.Perception.BlindPassComposureThreshold
            BlindPassSuccessPenalty = w.Perception.BlindPassSuccessPenalty
          }
        }

    /// Reads weights.json, merges missing fields with defaults.
    let load (path: string) : Result<EngineWeights, string> =
        try
            let json = File.ReadAllText path
            let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
            let partial = JsonSerializer.Deserialize<PartialEngineWeights>(json, opts)
            Ok (mergeWithDefaults partial EngineWeightDefaults.defaults)
        with ex ->
            Error $"WeightsLoader.load: {ex.Message}"
