namespace FootballEngine.ML

open FootballEngine.Types
open FootballEngine.Domain

/// Weights for shoot decision scoring.
/// Read by: Player/Decision/PlayerScorer.fs (shootScore)
[<CLIMutable>]
type ShootWeights = {
    /// Weight for Finishing stat in shoot technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    FinishingWeight: float
    /// Weight for LongShots stat in shoot technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    LongShotsWeight: float
    /// Weight for Composure stat in shoot technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    ComposureWeight: float
    /// Influence factor for xG in shoot opportunity quality. Range: [0,1]. Read by: PlayerScorer.fs
    XGInfluence: float
    /// Modifier for composure mental state. Range: [0,1]. Read by: PlayerScorer.fs
    ComposureStateMod: float
    /// Modifier for confidence mental state. Range: [0,1]. Read by: PlayerScorer.fs
    ConfidenceMod: float
    /// Modifier for focus mental state. Range: [0,1]. Read by: PlayerScorer.fs
    FocusMod: float
    /// Bonus for risk tolerance mental state. Range: [0,1]. Read by: PlayerScorer.fs
    RiskBonus: float
    /// Divisor for distance penalty in shoot score. Range: [1,100]. Read by: PlayerScorer.fs
    DistPenaltyDivisor: float
    /// Maximum distance penalty cap. Range: [0,1]. Read by: PlayerScorer.fs
    DistPenaltyMax: float
}

/// Weights for pass decision scoring.
/// Read by: Player/Decision/PlayerScorer.fs (passScore)
[<CLIMutable>]
type PassWeights = {
    /// Weight for Passing stat in pass technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    PassingWeight: float
    /// Weight for Vision stat in pass technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    VisionWeight: float
    /// Weight for Composure stat in pass technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    ComposureWeight: float
    /// Bonus for target availability. Range: [0,1]. Read by: PlayerScorer.fs
    TargetBonus: float
    /// Penalty for attack phase. Range: [-1,0]. Read by: PlayerScorer.fs
    AttackPhasePenalty: float
}

/// Weights for dribble decision scoring.
/// Read by: Player/Decision/PlayerScorer.fs (dribbleScore)
[<CLIMutable>]
type DribbleWeights = {
    /// Weight for Dribbling stat in dribble technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    DribblingWeight: float
    /// Weight for Agility stat in dribble technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    AgilityWeight: float
    /// Weight for Balance stat in dribble technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    BalanceWeight: float
    /// Bonus for attacking zone. Range: [0,1]. Read by: PlayerScorer.fs
    ZoneBonusAttacking: float
    /// Bonus for midfield zone. Range: [0,1]. Read by: PlayerScorer.fs
    ZoneBonusMidfield: float
    /// Penalty for high tempo. Range: [0,1]. Read by: PlayerScorer.fs
    TempoPenalty: float
    /// Penalty for pressure. Range: [0,1]. Read by: PlayerScorer.fs
    PressurePenalty: float
}

/// Weights for cross decision scoring.
/// Read by: Player/Decision/PlayerScorer.fs (crossScore)
[<CLIMutable>]
type CrossWeights = {
    /// Weight for Crossing stat in cross technical capability. Range: [0,1]. Read by: PlayerScorer.fs
    CrossingWeight: float
    /// Weight for lateralTendency profile in cross scoring. Range: [0,1]. Read by: PlayerScorer.fs
    LateralTendencyWeight: float
    /// Base lateral tendency contribution. Range: [0,1]. Read by: PlayerScorer.fs
    LateralTendencyBase: float
    /// Bonus for wing zone. Range: [0,1]. Read by: PlayerScorer.fs
    ZoneBonus: float
    /// Bonus for width. Range: [0,1]. Read by: PlayerScorer.fs
    WidthBonus: float
}

/// Weights for long ball decision scoring.
/// Read by: Player/Decision/PlayerScorer.fs (longBallScore)
[<CLIMutable>]
type LongBallWeights = {
    /// Weight for LongShots stat in long ball scoring. Range: [0,1]. Read by: PlayerScorer.fs
    LongShotsWeight: float
    /// Weight for Passing stat in long ball scoring. Range: [0,1]. Read by: PlayerScorer.fs
    PassingWeight: float
    /// Weight for Vision stat in long ball scoring. Range: [0,1]. Read by: PlayerScorer.fs
    VisionWeight: float
    /// Base distance for press calculation. Range: [0,50]. Read by: PlayerScorer.fs
    PressDistBase: float
    /// Minimum press modifier. Range: [0,1]. Read by: PlayerScorer.fs
    PressMin: float
    /// Maximum press modifier. Range: [0,2]. Read by: PlayerScorer.fs
    PressMax: float
    /// Press modifier when no opponent nearby. Range: [0,1]. Read by: PlayerScorer.fs
    PressNoOpponent: float
    /// Bonus for attack phase. Range: [0,1]. Read by: PlayerScorer.fs
    AttackPhaseBonus: float
    /// Bonus for directness. Range: [0,1]. Read by: PlayerScorer.fs
    DirectnessBonus: float
}

/// Individual action weights: shoot, pass, dribble, cross, long ball + temperature and directness blend.
/// Read by: Player/Decision/PlayerScorer.fs
[<CLIMutable>]
type IndividualWeights = {
    /// Shoot action weights. Read by: PlayerScorer.fs
    Shoot: ShootWeights
    /// Pass action weights. Read by: PlayerScorer.fs
    Pass: PassWeights
    /// Dribble action weights. Read by: PlayerScorer.fs
    Dribble: DribbleWeights
    /// Cross action weights. Read by: PlayerScorer.fs
    Cross: CrossWeights
    /// Long ball action weights. Read by: PlayerScorer.fs
    LongBall: LongBallWeights
    /// Softmax temperature for action selection. Higher = more random. Range: [0.01,1.0]. Read by: PlayerScorer.fs
    SoftmaxTemperature: float
    /// Tactic blend factor for directness in directnessFactor (0.6 default). Range: [0,1]. Read by: PlayerScorer.fs
    DirectnessBlendTactic: float
    /// Profile blend factor for directness in directnessFactor (0.4 default). Range: [0,1]. Read by: PlayerScorer.fs
    DirectnessBlendProfile: float
}

/// Personality derivation weights: map player mental/technical stats to personality traits.
/// Read by: Player/Decision/PlayerPersonality.fs (derive)
[<CLIMutable>]
type PersonalityWeights = {
    /// Weight for Vision stat in Flair derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    FlairVisionWeight: float
    /// Weight for Dribbling stat in Flair derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    FlairDribblingWeight: float
    /// Weight for Concentration stat in Consistency derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    ConsistencyConcentrationWeight: float
    /// Weight for Composure stat in Consistency derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    ConsistencyComposureWeight: float
    /// Weight for Leadership stat in Leadership derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    LeadershipWeight: float
    /// Weight for Aggression stat in Controversy derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    ControversyAggressionWeight: float
    /// Weight for Composure (inverse) in Controversy derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    ControversyComposureWeight: float
    /// Weight for WorkRate stat in Teamwork derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    TeamworkWorkRateWeight: float
    /// Weight for Positioning stat in Teamwork derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    TeamworkPositioningWeight: float
    /// Weight for Morale stat in Ambition derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    AmbitionMoraleWeight: float
    /// Weight for WorkRate stat in Ambition derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    AmbitionWorkRateWeight: float
    /// Weight for Composure stat in Pressure derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    PressureComposureWeight: float
    /// Weight for Concentration stat in Pressure derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    PressureConcentrationWeight: float
    /// Weight for Aggression stat in Sportsmanship derivation (inverse). Range: [0,1]. Read by: PlayerPersonality.fs
    SportsmanshipAggressionWeight: float
    /// Weight for Composure stat in Temperament derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    TemperamentComposureWeight: float
    /// Weight for Concentration stat in Temperament derivation. Range: [0,1]. Read by: PlayerPersonality.fs
    TemperamentConcentrationWeight: float
}

/// Map from EmergentState to DirectiveParams.
/// Read by: TeamOrchestrator/TeamDirector.fs (defaultParams)
type DirectiveParamsMap = {
    /// Compactness level delta per short pass success. Range: [0,0.1]. Read by: EmergentLoops.fs
    CompactnessSuccessDelta: float
    /// Compactness level delta per short pass failure. Range: [-0.1,0]. Read by: EmergentLoops.fs
    CompactnessFailDelta: float
    /// Compactness success threshold. Range: [0,1]. Read by: EmergentLoops.fs
    CompactnessSuccessThreshold: float
    /// Compactness fail threshold. Range: [0,1]. Read by: EmergentLoops.fs
    CompactnessFailThreshold: float
    /// Pressing intensity delta per press success. Range: [0,0.1]. Read by: EmergentLoops.fs
    PressingSuccessDelta: float
    /// Pressing intensity delta per press failure. Range: [-0.1,0]. Read by: EmergentLoops.fs
    PressingFailDelta: float
    /// Pressing success threshold. Range: [0,1]. Read by: EmergentLoops.fs
    PressingSuccessThreshold: float
    /// Pressing fail threshold. Range: [0,1]. Read by: EmergentLoops.fs
    PressingFailThreshold: float
    /// Wing play delta per flank success. Range: [0,0.1]. Read by: EmergentLoops.fs
    WingPlaySuccessDelta: float
    /// Wing play delta per flank failure. Range: [-0.1,0]. Read by: EmergentLoops.fs
    WingPlayFailDelta: float
    /// Wing play success threshold. Range: [0,1]. Read by: EmergentLoops.fs
    WingPlaySuccessThreshold: float
    /// Wing play fail threshold. Range: [0,1]. Read by: EmergentLoops.fs
    WingPlayFailThreshold: float
}

/// Emergent behavior loop weights: compactness, pressing, wing play, fatigue spiral.
/// Read by: TeamOrchestrator/EmergentLoops.fs
[<CLIMutable>]
type EmergentWeights = {
    /// Compactness adjustment on pass success. Range: [0,0.1]. Read by: EmergentLoops.fs
    CompactnessSuccessDelta: float
    /// Compactness adjustment on pass failure. Range: [-0.1,0]. Read by: EmergentLoops.fs
    CompactnessFailDelta: float
    /// Pass success threshold for compactness increase. Range: [0,1]. Read by: EmergentLoops.fs
    CompactnessSuccessThreshold: float
    /// Pass failure threshold for compactness decrease. Range: [0,1]. Read by: EmergentLoops.fs
    CompactnessFailThreshold: float
    /// Pressing adjustment on press success. Range: [0,0.1]. Read by: EmergentLoops.fs
    PressingSuccessDelta: float
    /// Pressing adjustment on press failure. Range: [-0.1,0]. Read by: EmergentLoops.fs
    PressingFailDelta: float
    /// Press success threshold for pressing increase. Range: [0,1]. Read by: EmergentLoops.fs
    PressingSuccessThreshold: float
    /// Press failure threshold for pressing decrease. Range: [0,1]. Read by: EmergentLoops.fs
    PressingFailThreshold: float
    /// Wing play adjustment on flank success. Range: [0,0.1]. Read by: EmergentLoops.fs
    WingPlaySuccessDelta: float
    /// Wing play adjustment on flank failure. Range: [-0.1,0]. Read by: EmergentLoops.fs
    WingPlayFailDelta: float
    /// Flank success threshold for wing play increase. Range: [0,1]. Read by: EmergentLoops.fs
    WingPlaySuccessThreshold: float
    /// Flank failure threshold for wing play decrease. Range: [0,1]. Read by: EmergentLoops.fs
    WingPlayFailThreshold: float
    /// Penalty per consecutive loss in fatigue spiral. Range: [0,0.1]. Read by: EmergentLoops.fs
    ConsecutiveLossPenalty: float
    /// Fatigue spiral compactness reduction factor. Range: [0,1]. Read by: EmergentLoops.fs
    FatigueSpiralCompactnessFactor: float
    /// Fatigue spiral pressing reduction factor. Range: [0,2]. Read by: EmergentLoops.fs
    FatigueSpiralPressingFactor: float
    /// Fatigue spiral tempo reduction factor. Range: [0,1]. Read by: EmergentLoops.fs
    FatigueSpiralTempoFactor: float
    /// Fatigue spiral risk appetite reduction factor. Range: [0,1]. Read by: EmergentLoops.fs
    FatigueSpiralRiskFactor: float
    /// Fatigue threshold for spiral activation. Range: [0,1]. Read by: EmergentLoops.fs
    FatigueSpiralThreshold: float
}

/// Collective modifier weights: transition, weakness, rest defense, threats, opponent shape, urgency.
/// Read by: Player/Decision/CollectiveModifiers.fs
[<CLIMutable>]
type ModifierWeights = {
    /// Multiplier for players near the ball during transition. Range: [0,5]. Read by: CollectiveModifiers.fs
    TransitionNearMult: float
    /// Multiplier for players far from ball during transition. Range: [0,2]. Read by: CollectiveModifiers.fs
    TransitionFarMult: float
    /// Distance threshold for "near" in transition. Range: [0,50]. Read by: CollectiveModifiers.fs
    TransitionNearDistance: float
    /// Support attack multiplier when on weakness zone. Range: [0,3]. Read by: CollectiveModifiers.fs
    WeaknessSupportMult: float
    /// Support attack multiplier for rest defense anchor. Range: [0,1]. Read by: CollectiveModifiers.fs
    RestDefenseSupportMult: float
    /// Cover space multiplier for rest defense anchor. Range: [0,3]. Read by: CollectiveModifiers.fs
    RestDefenseCoverMult: float
    /// Cover space multiplier when on threat zone. Range: [0,3]. Read by: CollectiveModifiers.fs
    ThreatCoverMult: float
    /// Support attack multiplier vs high line opponent. Range: [0,2]. Read by: CollectiveModifiers.fs
    HighLineSupportMult: float
    /// Press ball multiplier vs low block opponent. Range: [0,1]. Read by: CollectiveModifiers.fs
    LowBlockPressMult: float
    /// Cover space multiplier vs low block opponent. Range: [0,2]. Read by: CollectiveModifiers.fs
    LowBlockCoverMult: float
    /// Press ball urgency multiplier coefficient. Range: [0,1]. Read by: CollectiveModifiers.fs
    UrgencyPressMult: float
    /// Support attack urgency multiplier coefficient. Range: [0,1]. Read by: CollectiveModifiers.fs
    UrgencySupportMult: float
    /// Urgency threshold to activate modifiers. Range: [0,1]. Read by: CollectiveModifiers.fs
    UrgencyThreshold: float
}

/// Chemistry weights: pressing coordination and transition speed derivation from familiarity.
/// Read by: TeamOrchestrator/ChemistryTracker.fs
[<CLIMutable>]
type ChemistryWeights = {
    /// Familiarity pass bonus per successful pass. Range: [0,0.1]. Read by: ChemistryTracker.fs
    FamiliarityPassBonus: float
    /// Familiarity penalty per failed pass. Range: [-0.1,0]. Read by: ChemistryTracker.fs
    FamiliarityFailPenalty: float
    /// Familiarity time bonus per minute played. Range: [0,0.01]. Read by: ChemistryTracker.fs
    FamiliarityTimeBonus: float
    /// Pressing coordination base value. Range: [0,1]. Read by: ChemistryTracker.fs
    PressingCoordinationBase: float
    /// Pressing coordination familiarity multiplier. Range: [0,1]. Read by: ChemistryTracker.fs
    PressingCoordinationFamiliarityMult: float
    /// Transition speed base value. Range: [0,1]. Read by: ChemistryTracker.fs
    TransitionSpeedBase: float
    /// Transition speed familiarity multiplier. Range: [0,1]. Read by: ChemistryTracker.fs
    TransitionSpeedFamiliarityMult: float
}

/// Team director weights: work rate, positioning, advanced bonus for runner selection.
/// Read by: TeamOrchestrator/TeamDirector.fs (pickRunner)
[<CLIMutable>]
type TeamDirectorWeights = {
    /// Weight for WorkRate stat in runner selection. Range: [0,1]. Read by: TeamDirector.fs
    WorkRateWeight: float
    /// Weight for Positioning stat in runner selection. Range: [0,1]. Read by: TeamDirector.fs
    PositioningWeight: float
    /// Bonus for advanced position in runner selection. Range: [0,1]. Read by: TeamDirector.fs
    AdvancedBonus: float
}

/// Reactive loop weights: shape, press, fatigue deviation weights + thresholds.
/// Read by: TeamOrchestrator/ReactiveLoop.fs
[<CLIMutable>]
type ReactiveLoopWeights = {
    /// Weight for shape deviation in total deviation. Range: [0,1]. Read by: ReactiveLoop.fs
    ShapeDevWeight: float
    /// Weight for press deviation in total deviation. Range: [0,1]. Read by: ReactiveLoop.fs
    PressDevWeight: float
    /// Weight for fatigue deviation in total deviation. Range: [0,1]. Read by: ReactiveLoop.fs
    FatigueDevWeight: float
    /// Threshold for on-track classification. Range: [0,1]. Read by: ReactiveLoop.fs
    OnTrackThreshold: float
    /// Threshold for drifting classification. Range: [0,1]. Read by: ReactiveLoop.fs
    DriftingThreshold: float
}

/// Collective weights: directive params, emergent, modifiers, chemistry, team director, reactive loop.
/// Read by: TeamOrchestrator modules
[<CLIMutable>]
type CollectiveWeights = {
    /// Directive parameters map for emergent state transitions. Read by: TeamDirector.fs
    DirectiveParams: DirectiveParamsMap
    /// Emergent behavior loop weights. Read by: EmergentLoops.fs
    Emergent: EmergentWeights
    /// Collective modifier weights. Read by: CollectiveModifiers.fs
    Modifiers: ModifierWeights
    /// Chemistry tracking weights. Read by: ChemistryTracker.fs
    Chemistry: ChemistryWeights
    /// Team director weights. Read by: TeamDirector.fs
    TeamDirector: TeamDirectorWeights
    /// Reactive loop weights. Read by: ReactiveLoop.fs
    ReactiveLoop: ReactiveLoopWeights
}

/// Duel outcome weights: steepness, momentum, jitter, attacker/defender stat weights, fatigue.
/// Read by: Player/Actions/DuelAction.fs, BalanceConfig.Duel
[<CLIMutable>]
type DuelOutcomeWeights = {
    /// Steepness of logistic curve in 1v1 duels. Range: [0.8,2.0]. Read by: DuelAction.fs
    DuelSteepness: float
    /// Momentum added when attacker wins duel. Range: [0.5,2.0]. Read by: DuelAction.fs
    MomentumBonus: float
    /// Jitter applied on duel win. Range: [0,20]. Read by: DuelAction.fs
    JitterWin: float
    /// Jitter recovery rate. Range: [0,10]. Read by: DuelAction.fs
    JitterRecover: float
    /// Jitter kept after duel. Range: [0,10]. Read by: DuelAction.fs
    JitterKeep: float
    /// Speed kept after duel (m/s). Range: [0,10]. Read by: DuelAction.fs
    SpeedKeep: float
    /// Vertical speed kept after duel (m/s). Range: [0,5]. Read by: DuelAction.fs
    SpeedKeepVz: float
    /// Weight for attacker Dribbling stat. Range: [0,1]. Read by: DuelAction.fs
    AttackerDribblingWeight: float
    /// Weight for attacker Agility stat. Range: [0,1]. Read by: DuelAction.fs
    AttackerAgilityWeight: float
    /// Weight for attacker Balance stat. Range: [0,1]. Read by: DuelAction.fs
    AttackerBalanceWeight: float
    /// Weight for defender Tackling stat. Range: [0,1]. Read by: DuelAction.fs
    DefenderTacklingWeight: float
    /// Weight for defender Strength stat. Range: [0,1]. Read by: DuelAction.fs
    DefenderStrengthWeight: float
    /// Weight for defender Positioning stat. Range: [0,1]. Read by: DuelAction.fs
    DefenderPositionWeight: float
    /// Condition threshold for fatigue effects. Range: [0,100]. Read by: DuelAction.fs
    FatigueThreshold: int
    /// Fatigue decay rate. Range: [0,0.1]. Read by: DuelAction.fs
    FatigueDecay: float
}

/// Shot outcome weights: on-target, distance decay, finishing, position bonuses, GK factors.
/// Read by: Player/Actions/ShotAction.fs, BalanceConfig.Shot
[<CLIMutable>]
type ShotOutcomeWeights = {
    /// Base probability of shot on target. Range: [0.30,0.55]. Read by: ShotAction.fs
    OnTargetBase: float
    /// Distance decay rate for on-target exponential penalty. Range: [5,30]. Read by: ShotAction.fs
    OnTargetDistDecayRate: float
    /// Maximum on-target penalty from distance. Range: [0,0.5]. Read by: ShotAction.fs
    OnTargetDistMaxPenalty: float
    /// Quality gate threshold for shot attempt. Range: [0,1]. Read by: ShotAction.fs
    QualityGate: float
    /// Base angle spread for shot. Range: [0,2]. Read by: ShotAction.fs
    AngleSpreadBase: float
    /// Base vertical velocity for shot (m/s). Range: [0,10]. Read by: ShotAction.fs
    VzBase: float
    /// Variance in vertical velocity. Range: [0,5]. Read by: ShotAction.fs
    VzVariance: float
    /// Multiplier for on-target calculation. Range: [0,1]. Read by: ShotAction.fs
    OnTargetMultiplier: float
    /// Divisor for on-target distance calculation. Range: [1,50]. Read by: ShotAction.fs
    OnTargetDistDivisor: float
    /// Normalisation distance for xG (meters). Range: [10,50]. Read by: ShotAction.fs
    NormalisationDistance: float
    /// Distance to goal multiplier. Range: [0,1]. Read by: ShotAction.fs
    DistanceToGoalMultiplier: float
    /// Minimum finishing contribution. Range: [0,1]. Read by: ShotAction.fs
    FinishingMin: float
    /// Maximum finishing contribution. Range: [0,2]. Read by: ShotAction.fs
    FinishingMax: float
    /// Finishing bonus for ST position. Range: [0,3]. Read by: ShotAction.fs
    FinishingBonusST: float
    /// Finishing bonus for AM position. Range: [0,3]. Read by: ShotAction.fs
    FinishingBonusAM: float
    /// Finishing bonus for MC position. Range: [0,2]. Read by: ShotAction.fs
    FinishingBonusMC: float
    /// Finishing bonus for other positions. Range: [0,1]. Read by: ShotAction.fs
    FinishingBonusOther: float
    /// Divisor for condition factor. Range: [50,200]. Read by: ShotAction.fs
    CondFactorDivisor: float
    /// Composure multiplier for shot. Range: [0,0.5]. Read by: ShotAction.fs
    ComposureMultiplier: float
    /// Urgency multiplier for shot. Range: [0,0.5]. Read by: ShotAction.fs
    UrgencyMultiplier: float
    /// Base power divisor. Range: [10,50]. Read by: ShotAction.fs
    BasePowerDivisor: float
    /// Top spin multiplier. Range: [0,1]. Read by: ShotAction.fs
    SpinTopMultiplier: float
    /// Weight for position directness. Range: [0,1]. Read by: ShotAction.fs
    PositionDirectnessWeight: float
    /// Weight for position depth. Range: [0,1]. Read by: ShotAction.fs
    PositionDepthWeight: float
    /// Weight for position creativity. Range: [0,1]. Read by: ShotAction.fs
    PositionCreativityWeight: float
    /// Weight for distance normalisation. Range: [0,1]. Read by: ShotAction.fs
    DistNormWeight: float
    /// Weight for position bonus. Range: [0,1]. Read by: ShotAction.fs
    PositionBonusWeight: float
    /// Divisor for heavy touch. Range: [10,50]. Read by: ShotAction.fs
    HeavyTouchDivisor: float
    /// Multiplier for heavy touch. Range: [0,1]. Read by: ShotAction.fs
    HeavyTouchMultiplier: float
    /// Standard deviation for shot jitter. Range: [0,5]. Read by: ShotAction.fs
    JitterStdDev: float
    /// GK reflexes stat multiplier. Range: [0,10]. Read by: ShotAction.fs
    GkReflexesStatMult: float
    /// GK one-on-one stat multiplier. Range: [0,10]. Read by: ShotAction.fs
    GkOneOnOneStatMult: float
    /// Offset for save denominator. Range: [0,10]. Read by: ShotAction.fs
    SaveDenominatorOffset: float
    /// Margin for shot wide判定 (meters). Range: [0,10]. Read by: ShotAction.fs
    ShotWideMargin: float
}

/// Pass outcome weights: base mean, distance penalties, technical/vision weights, interception.
/// Read by: Player/Actions/PassAction.fs, BalanceConfig.Pass
[<CLIMutable>]
type PassOutcomeWeights = {
    /// Base mean for pass success. Range: [0,1]. Read by: PassAction.fs
    BaseMean: float
    /// Distance penalty per meter. Range: [0,0.05]. Read by: PassAction.fs
    DistancePenaltyPerMeter: float
    /// Long pass penalty per meter. Range: [0,0.05]. Read by: PassAction.fs
    LongPassPenaltyPerMeter: float
    /// Weight for technical stats. Range: [0,1]. Read by: PassAction.fs
    TechnicalWeight: float
    /// Weight for vision stat. Range: [0,1]. Read by: PassAction.fs
    VisionWeight: float
    /// Alpha for success shape (logistic). Range: [1,20]. Read by: PassAction.fs
    SuccessShapeAlpha: float
    /// Condition multiplier for success. Range: [0,10]. Read by: PassAction.fs
    SuccessConditionMultiplier: float
    /// Momentum on offside. Range: [0,1]. Read by: PassAction.fs
    OffsideMomentum: float
    /// Momentum on success. Range: [0,1]. Read by: PassAction.fs
    SuccessMomentum: float
    /// Momentum on fail. Range: [0,1]. Read by: PassAction.fs
    FailMomentum: float
    /// Base deflect rate. Range: [0,0.2]. Read by: PassAction.fs
    DeflectBaseRate: float
    /// Base misplaced rate. Range: [0,0.2]. Read by: PassAction.fs
    MisplacedBaseRate: float
    /// Base interception rate. Range: [0,0.2]. Read by: PassAction.fs
    InterceptBaseRate: float
    /// Interception radius (meters). Range: [0,20]. Read by: PassAction.fs
    InterceptionRadius: float
    /// Pressure distance for deflect (meters). Range: [0,20]. Read by: PassAction.fs
    PressureDistance: float
    /// Pressure multiplier for deflect. Range: [0,1]. Read by: PassAction.fs
    DeflectPressureMultiplier: float
    /// Weight for pace in interception. Range: [0,1]. Read by: PassAction.fs
    InterceptPaceWeight: float
    /// Weight for positioning in interception. Range: [0,1]. Read by: PassAction.fs
    InterceptPositioningWeight: float
    /// Scramble jitter (meters). Range: [0,10]. Read by: PassAction.fs
    ScrambleJitter: float
    /// Pass speed (m/s). Range: [5,30]. Read by: PassAction.fs
    Speed: float
    /// Pass vertical velocity (m/s). Range: [0,5]. Read by: PassAction.fs
    Vz: float
    /// Weight for distance factor in interception. Range: [0,1]. Read by: PassAction.fs
    InterceptDistFactorWeight: float
    /// Positioning contribution for interception. Range: [0,1]. Read by: PassAction.fs
    InterceptPositioningContrib: float
    /// Vision contribution for interception. Range: [0,1]. Read by: PassAction.fs
    InterceptVisionContrib: float
    /// Weight for creativity in pass scoring. Range: [0,1]. Read by: PassAction.fs
    CreativityWeight: float
    /// Weight for directness in pass scoring. Range: [0,1]. Read by: PassAction.fs
    DirectnessWeight: float
    /// Minimum mean for pass. Range: [0,0.1]. Read by: PassAction.fs
    MeanMin: float
    /// Maximum mean for pass. Range: [0.9,1]. Read by: PassAction.fs
    MeanMax: float
    /// Default nearest defender distance (meters). Range: [0,30]. Read by: PassAction.fs
    DefaultNearestDefDist: float
    /// Default tackling value. Range: [0,1]. Read by: PassAction.fs
    DefaultTackling: float
    /// Heavy touch divisor. Range: [10,50]. Read by: PassAction.fs
    HeavyTouchDivisor: float
    /// Heavy touch multiplier. Range: [0,1]. Read by: PassAction.fs
    HeavyTouchMultiplier: float
    /// Jitter standard deviation. Range: [0,5]. Read by: PassAction.fs
    JitterStdDev: float
    /// Deflected speed multiplier. Range: [0,1]. Read by: PassAction.fs
    DeflectedSpeedMult: float
    /// Deflected vertical velocity multiplier. Range: [0,1]. Read by: PassAction.fs
    DeflectedVzMult: float
    /// Maximum interception probability. Range: [0,1]. Read by: PassAction.fs
    InterceptProbMax: float
    /// Misplaced speed multiplier. Range: [0,1]. Read by: PassAction.fs
    MisplacedSpeedMult: float
    /// Long ball base mean. Range: [0,1]. Read by: PassAction.fs
    LongBallBaseMean: float
    /// Long ball long shots weight. Range: [0,1]. Read by: PassAction.fs
    LongBallLongShotsWeight: float
    /// Long ball passing weight. Range: [0,1]. Read by: PassAction.fs
    LongBallPassingWeight: float
    /// Long ball vision weight. Range: [0,1]. Read by: PassAction.fs
    LongBallVisionWeight: float
    /// Long ball success shape alpha. Range: [1,20]. Read by: PassAction.fs
    LongBallSuccessShapeAlpha: float
    /// Long ball success condition multiplier. Range: [0,20]. Read by: PassAction.fs
    LongBallSuccessConditionMultiplier: float
    /// Long ball offside momentum. Range: [0,1]. Read by: PassAction.fs
    LongBallOffsideMomentum: float
    /// Long ball success momentum. Range: [0,1]. Read by: PassAction.fs
    LongBallSuccessMomentum: float
    /// Long ball fail momentum. Range: [0,1]. Read by: PassAction.fs
    LongBallFailMomentum: float
    /// Long ball speed (m/s). Range: [5,40]. Read by: PassAction.fs
    LongBallSpeed: float
    /// Long ball vertical velocity (m/s). Range: [0,10]. Read by: PassAction.fs
    LongBallVz: float
    /// Long ball deflect multiplier. Range: [0,3]. Read by: PassAction.fs
    LongBallDeflectMult: float
    /// Long ball intercept multiplier. Range: [0,3]. Read by: PassAction.fs
    LongBallInterceptMult: float
    /// Long ball pressure contribution. Range: [0,1]. Read by: PassAction.fs
    LongBallPressureContrib: float
    /// Forward depth threshold. Range: [0,1]. Read by: PassAction.fs
    ForwardDepthThreshold: float
    /// Forward creativity threshold. Range: [0,1]. Read by: PassAction.fs
    ForwardCreativityThreshold: float
    /// Long ball scramble jitter multiplier. Range: [0,5]. Read by: PassAction.fs
    LongBallScrambleJitterMult: float
    /// Pass lead factor. Range: [0,1]. Read by: PassAction.fs
    PassLeadFactor: float
}

/// Cross outcome weights: base mean, crossing/passing weights, header duel, GK save.
/// Read by: Player/Actions/CrossAction.fs, BalanceConfig.Cross
[<CLIMutable>]
type CrossOutcomeWeights = {
    /// Base mean for cross success. Range: [0,1]. Read by: CrossAction.fs
    BaseMean: float
    /// Weight for Crossing stat. Range: [0,1]. Read by: CrossAction.fs
    CrossingWeight: float
    /// Weight for Passing stat. Range: [0,1]. Read by: CrossAction.fs
    PassingWeight: float
    /// Alpha for success shape. Range: [1,20]. Read by: CrossAction.fs
    SuccessShapeAlpha: float
    /// Condition multiplier for success. Range: [0,20]. Read by: CrossAction.fs
    SuccessConditionMultiplier: float
    /// Steepness for header duel. Range: [0.5,3]. Read by: CrossAction.fs
    HeaderDuelSteepness: float
    /// Base header accuracy. Range: [0,1]. Read by: CrossAction.fs
    HeaderAccuracyBase: float
    /// Skill multiplier for header accuracy. Range: [0,0.1]. Read by: CrossAction.fs
    HeaderAccuracySkillMult: float
    /// Base GK save probability. Range: [0,1]. Read by: CrossAction.fs
    GkSaveBase: float
    /// GK reflexes multiplier. Range: [0,1]. Read by: CrossAction.fs
    GkReflexesMult: float
    /// GK aerial reach multiplier. Range: [0,2]. Read by: CrossAction.fs
    GkAerialReachMult: float
    /// GK jump multiplier. Range: [0,2]. Read by: CrossAction.fs
    GkJumpMult: float
    /// Probability GK claims cross. Range: [0,1]. Read by: CrossAction.fs
    ClaimCrossProbability: float
    /// Momentum on cross fail. Range: [0,1]. Read by: CrossAction.fs
    FailMomentum: float
    /// Cross speed (m/s). Range: [5,30]. Read by: CrossAction.fs
    Speed: float
    /// Cross vertical velocity (m/s). Range: [0,10]. Read by: CrossAction.fs
    Vz: float
    /// Aerial threat threshold. Range: [0,1]. Read by: CrossAction.fs
    AerialThreatThreshold: float
    /// Attacking depth threshold. Range: [0,1]. Read by: CrossAction.fs
    AttackingDepthThreshold: float
    /// Default GK skill value. Range: [0,100]. Read by: CrossAction.fs
    GkSkillDefault: float
    /// GK skill divisor. Range: [50,300]. Read by: CrossAction.fs
    GkSkillDivisor: float
    /// Top spin multiplier. Range: [0,1]. Read by: CrossAction.fs
    SpinTopMult: float
    /// Side spin multiplier. Range: [0,2]. Read by: CrossAction.fs
    SpinSideMult: float
    /// Fallback speed (m/s). Range: [5,30]. Read by: CrossAction.fs
    FallbackSpeed: float
    /// Fallback vertical velocity (m/s). Range: [0,5]. Read by: CrossAction.fs
    FallbackVz: float
}

/// Tackle outcome weights: technical/positioning/strength/aggression weights, foul shape.
/// Read by: Player/Actions/DuelAction.fs, BalanceConfig.Tackle
[<CLIMutable>]
type TackleOutcomeWeights = {
    /// Weight for technical stats. Range: [0,1]. Read by: DuelAction.fs
    TechnicalWeight: float
    /// Weight for positioning stat. Range: [0,1]. Read by: DuelAction.fs
    PositioningWeight: float
    /// Weight for strength stat. Range: [0,1]. Read by: DuelAction.fs
    StrengthWeight: float
    /// Weight for aggression stat. Range: [0,1]. Read by: DuelAction.fs
    AggressionWeight: float
    /// Reduction from positioning. Range: [0,0.5]. Read by: DuelAction.fs
    PositioningReduction: float
    /// Beta for foul shape (logistic). Range: [1,20]. Read by: DuelAction.fs
    FoulShapeBeta: float
    /// Momentum on foul. Range: [0,1]. Read by: DuelAction.fs
    FoulMomentum: float
    /// Momentum on tackle success. Range: [0,1]. Read by: DuelAction.fs
    SuccessMomentum: float
    /// Momentum on tackle fail. Range: [0,1]. Read by: DuelAction.fs
    FailMomentum: float
    /// Steepness for tackle outcome. Range: [0.5,3]. Read by: DuelAction.fs
    TackleSteepness: float
}

/// Set piece outcome weights: free kick, corner, throw-in, penalty, goal kick, kick-off.
/// Read by: Player/Actions/SetPieceRoutines.fs, BalanceConfig.SetPiece
[<CLIMutable>]
type SetPieceOutcomeWeights = {
    /// Free kick target X (meters). Read by: SetPieceRoutines.fs
    FreeKickTargetX: float
    /// Free kick speed (m/s). Range: [5,30]. Read by: SetPieceRoutines.fs
    FreeKickSpeed: float
    /// Free kick vertical velocity (m/s). Range: [0,5]. Read by: SetPieceRoutines.fs
    FreeKickVz: float
    /// Free kick logistic steepness. Range: [0.5,5]. Read by: SetPieceRoutines.fs
    FreeKickSteepness: float
    /// Free kick save power threshold. Range: [0,3]. Read by: SetPieceRoutines.fs
    FreeKickSavePowerThreshold: float
    /// Free kick save variance. Range: [0,3]. Read by: SetPieceRoutines.fs
    FreeKickSaveVariance: float
    /// Free kick top spin multiplier. Range: [0,2]. Read by: SetPieceRoutines.fs
    FreeKickSpinTopMult: float
    /// Free kick side spin multiplier. Range: [0,2]. Read by: SetPieceRoutines.fs
    FreeKickSpinSideMult: float
    /// Corner box X threshold (meters). Read by: SetPieceRoutines.fs
    CornerBoxXThreshold: float
    /// Corner defender box threshold (meters). Read by: SetPieceRoutines.fs
    CornerDefenderBoxThreshold: float
    /// Corner second phase probability. Range: [0,1]. Read by: SetPieceRoutines.fs
    CornerSecondPhaseProbability: float
    /// Corner keep possession probability. Range: [0,1]. Read by: SetPieceRoutines.fs
    CornerKeepPossessionProbability: float
    /// Corner speed (m/s). Range: [5,30]. Read by: SetPieceRoutines.fs
    CornerSpeed: float
    /// Corner vertical velocity (m/s). Range: [0,5]. Read by: SetPieceRoutines.fs
    CornerVz: float
    /// Corner density base. Range: [0,10]. Read by: SetPieceRoutines.fs
    CornerDensityBase: float
    /// Corner density penalty. Range: [0,0.2]. Read by: SetPieceRoutines.fs
    CornerDensityPenalty: float
    /// Corner logistic steepness. Range: [0.5,5]. Read by: SetPieceRoutines.fs
    CornerLogisticSteepness: float
    /// Corner default defender score. Range: [0,1]. Read by: SetPieceRoutines.fs
    CornerDefScoreDefault: float
    /// Throw-in speed (m/s). Range: [5,25]. Read by: SetPieceRoutines.fs
    ThrowInSpeed: float
    /// Throw-in vertical velocity (m/s). Range: [0,5]. Read by: SetPieceRoutines.fs
    ThrowInVz: float
    /// Throw-in momentum. Range: [0,1]. Read by: SetPieceRoutines.fs
    ThrowInMomentum: float
    /// Penalty skill multiplier. Range: [0,0.1]. Read by: SetPieceRoutines.fs
    PenaltySkillMultiplier: float
    /// Penalty morale multiplier. Range: [0,0.05]. Read by: SetPieceRoutines.fs
    PenaltyMoraleMultiplier: float
    /// Penalty pressure multiplier. Range: [0,0.05]. Read by: SetPieceRoutines.fs
    PenaltyPressureMultiplier: float
    /// Penalty composure noise. Range: [0,3]. Read by: SetPieceRoutines.fs
    PenaltyComposureNoise: float
    /// Penalty logistic base. Range: [0,1]. Read by: SetPieceRoutines.fs
    PenaltyLogisticBase: float
    /// Penalty GK reflexes multiplier. Range: [0,10]. Read by: SetPieceRoutines.fs
    PenaltyGkReflexesMult: float
    /// Penalty GK handling multiplier. Range: [0,10]. Read by: SetPieceRoutines.fs
    PenaltyGkHandlingMult: float
    /// Penalty skill divisor. Range: [10,50]. Read by: SetPieceRoutines.fs
    PenaltySkillDivisor: float
    /// Penalty condition divisor. Range: [100,500]. Read by: SetPieceRoutines.fs
    PenaltyCondDivisor: float
    /// Penalty morale base. Range: [0,1]. Read by: SetPieceRoutines.fs
    PenaltyMoraleBase: float
    /// Penalty morale divisor. Range: [50,300]. Read by: SetPieceRoutines.fs
    PenaltyMoraleDivisor: float
    /// Penalty GK skill divisor. Range: [20,100]. Read by: SetPieceRoutines.fs
    PenaltyGkSkillDivisor: float
    /// Penalty speed (m/s). Range: [10,40]. Read by: SetPieceRoutines.fs
    PenaltySpeed: float
    /// Penalty base vertical velocity (m/s). Range: [0,3]. Read by: SetPieceRoutines.fs
    PenaltyVzBase: float
    /// Penalty vertical velocity variance. Range: [0,1]. Read by: SetPieceRoutines.fs
    PenaltyVzVariance: float
    /// Penalty angle spread. Range: [0,0.5]. Read by: SetPieceRoutines.fs
    PenaltyAngleSpread: float
    /// Post-shot clear probability. Range: [0,1]. Read by: SetPieceRoutines.fs
    PostShotClearProbability: float
    /// Clear speed (m/s). Range: [5,30]. Read by: SetPieceRoutines.fs
    ClearSpeed: float
    /// Clear vertical velocity (m/s). Range: [0,5]. Read by: SetPieceRoutines.fs
    ClearVz: float
    /// Clear Y standard deviation. Range: [0,20]. Read by: SetPieceRoutines.fs
    ClearYStdDev: float
    /// Goal kick fallback distance home (meters). Range: [10,50]. Read by: SetPieceRoutines.fs
    GoalKickFallbackDistHome: float
    /// Goal kick fallback distance away (meters). Range: [30,100]. Read by: SetPieceRoutines.fs
    GoalKickFallbackDistAway: float
    /// Kick-off partner offset X (meters). Range: [-10,10]. Read by: SetPieceRoutines.fs
    KickOffPartnerOffsetX: float
    /// Kick-off partner offset Y (meters). Range: [-10,10]. Read by: SetPieceRoutines.fs
    KickOffPartnerOffsetY: float
    /// Foul base rate. Range: [0,1]. Read by: SetPieceRoutines.fs
    FoulBaseRate: float
    /// Corner probability on failed cross. Range: [0,1]. Read by: SetPieceRoutines.fs
    CornerOnFailedCross: float
}

/// Goalkeeper outcome weights: catch, dive, parry, aerial reach, punch, distribution.
/// Read by: Player/Actions/GKAction.fs, BalanceConfig.GK
[<CLIMutable>]
type GKOutcomeWeights = {
    /// Catch handling multiplier. Range: [0,1]. Read by: GKAction.fs
    CatchHandlingMult: float
    /// Dive reach (meters). Range: [0,5]. Read by: GKAction.fs
    DiveReach: float
    /// Parry speed (m/s). Range: [0,20]. Read by: GKAction.fs
    ParrySpeed: float
    /// Parry deflection angle. Range: [0,1]. Read by: GKAction.fs
    ParryDeflectionAngle: float
    /// Aerial reach multiplier. Range: [0,2]. Read by: GKAction.fs
    AerialReachMult: float
    /// Jump reach multiplier. Range: [0,2]. Read by: GKAction.fs
    JumpReachMult: float
    /// Punch probability. Range: [0,1]. Read by: GKAction.fs
    PunchProbability: float
    /// Claim cross probability. Range: [0,1]. Read by: GKAction.fs
    ClaimCrossProbability: float
    /// Collection radius (meters). Range: [0,10]. Read by: GKAction.fs
    CollectionRadius: float
    /// Collection priority. Range: [0,5]. Read by: GKAction.fs
    CollectionPriority: float
    /// Throw speed (m/s). Range: [5,25]. Read by: GKAction.fs
    ThrowSpeed: float
    /// Roll speed (m/s). Range: [0,20]. Read by: GKAction.fs
    RollSpeed: float
    /// Goal kick speed (m/s). Range: [10,50]. Read by: GKAction.fs
    GoalKickSpeed: float
    /// Punt speed (m/s). Range: [10,50]. Read by: GKAction.fs
    PuntSpeed: float
    /// Distribution accuracy multiplier. Range: [0,2]. Read by: GKAction.fs
    DistributionAccuracyMult: float
    /// Distribution decision noise. Range: [0,0.5]. Read by: GKAction.fs
    DistributionDecisionNoise: float
    /// Hold time subticks. Range: [0,100]. Read by: GKAction.fs
    HoldTimeSubTicks: int
    /// Max hold subticks. Range: [0,500]. Read by: GKAction.fs
    MaxHoldSubTicks: int
    /// Back pass handling penalty. Range: [0,0.5]. Read by: GKAction.fs
    BackPassHandlingPenalty: float
    /// GK decision window subticks. Range: [0,50]. Read by: GKAction.fs
    GKDecisionWindowSubTicks: int
}

/// Expected goals weights: distance factor, angle exponent, base multiplier.
/// Read by: Player/Decision/xGCalculator.fs
[<CLIMutable>]
type XGWeights = {
    /// Exponential distance decay factor for xG. Range: [0.01,0.2]. Read by: xGCalculator.fs
    DistanceFactor: float
    /// Power-law exponent for angle in xG. Range: [0.5,3]. Read by: xGCalculator.fs
    AngleExponent: float
    /// Base multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    BaseMultiplier: float
    /// One-on-one multiplier for xG. Range: [0,2]. Read by: xGCalculator.fs
    OneOnOneMultiplier: float
    /// Set piece multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    SetPieceMultiplier: float
    /// Pressure reduction factor. Range: [0,1]. Read by: xGCalculator.fs
    PressureReduction: float
    /// Header multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    HeaderMultiplier: float
    /// Volley multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    VolleyMultiplier: float
    /// Half volley multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    HalfVolleyMultiplier: float
    /// Chip shot multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    ChipShotMultiplier: float
    /// Curler multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    CurlerMultiplier: float
    /// Driven shot multiplier for xG. Range: [0,2]. Read by: xGCalculator.fs
    DrivenShotMultiplier: float
    /// Placed shot multiplier for xG. Range: [0,2]. Read by: xGCalculator.fs
    PlacedShotMultiplier: float
    /// First time shot multiplier for xG. Range: [0,1]. Read by: xGCalculator.fs
    FirstTimeShotMultiplier: float
}

/// Interception weights: ball control radius multiplier.
/// Read by: Ball/Interception.fs
[<CLIMutable>]
type InterceptionWeights = {
    /// Ball control radius multiplier from Technical.BallControl. Range: [0,1]. Read by: Interception.fs
    BallControlRadiusMult: float
    /// Press ball intent factor for time estimation. Range: [0,1]. Read by: Interception.fs
    PressIntentFactor: float
    /// Recover ball intent factor for time estimation. Range: [0,1]. Read by: Interception.fs
    RecoverIntentFactor: float
    /// Maintain shape intent factor for time estimation. Range: [1,2]. Read by: Interception.fs
    MaintainShapeIntentFactor: float
    /// Cover space intent factor for time estimation. Range: [1,2]. Read by: Interception.fs
    CoverSpaceIntentFactor: float
}

/// All outcome weights: duel, shot, pass, cross, tackle, set piece, GK, xG, interception.
/// Read by: All action modules
[<CLIMutable>]
type OutcomeWeights = {
    /// Duel outcome weights. Read by: DuelAction.fs
    Duel: DuelOutcomeWeights
    /// Shot outcome weights. Read by: ShotAction.fs
    Shot: ShotOutcomeWeights
    /// Pass outcome weights. Read by: PassAction.fs
    Pass: PassOutcomeWeights
    /// Cross outcome weights. Read by: CrossAction.fs
    Cross: CrossOutcomeWeights
    /// Tackle outcome weights. Read by: DuelAction.fs
    Tackle: TackleOutcomeWeights
    /// Set piece outcome weights. Read by: SetPieceRoutines.fs
    SetPiece: SetPieceOutcomeWeights
    /// Goalkeeper outcome weights. Read by: GKAction.fs
    GK: GKOutcomeWeights
    /// Expected goals weights. Read by: xGCalculator.fs
    XG: XGWeights
    /// Interception weights. Read by: Interception.fs
    Interception: InterceptionWeights
}

/// Win probability weights: goal lead, xG factor, momentum, home advantage, minute pressure.
/// Read by: TeamOrchestrator/WinProbability.fs
[<CLIMutable>]
type WinProbabilityWeights = {
    /// Base probability when leading. Range: [0,1]. Read by: WinProbability.fs
    GoalLeadBase: float
    /// Base probability when drawing. Range: [0,1]. Read by: WinProbability.fs
    DrawBase: float
    /// Goal difference factor for win probability. Range: [0,0.5]. Read by: WinProbability.fs
    GoalDiffFactor: float
    /// xG factor for win probability. Range: [0,0.5]. Read by: WinProbability.fs
    XGFactor: float
    /// Home advantage factor in win probability. Range: [0,0.2]. Read by: WinProbability.fs
    HomeAdvantage: float
    /// Goal difference steepness for win probability. Range: [0,0.5]. Read by: WinProbability.fs
    GoalDiffSteepness: float
    /// xG difference steepness for win probability. Range: [0,0.5]. Read by: WinProbability.fs
    XGDiffSteepness: float
    /// Minute pressure factor (late game urgency). Range: [0,0.2]. Read by: WinProbability.fs
    MinutePressure: float
    /// Comeback bonus factor. Range: [0,0.2]. Read by: WinProbability.fs
    ComebackBonus: float
    /// Momentum positive threshold. Range: [0,5]. Read by: WinProbability.fs
    MomentumPositiveThreshold: float
    /// Momentum negative threshold. Range: [-5,0]. Read by: WinProbability.fs
    MomentumNegativeThreshold: float
    /// Momentum positive bonus. Range: [0,0.2]. Read by: WinProbability.fs
    MomentumPositiveBonus: float
    /// Momentum negative penalty. Range: [-0.2,0]. Read by: WinProbability.fs
    MomentumNegativePenalty: float
    /// Momentum linear factor. Range: [0,0.1]. Read by: WinProbability.fs
    MomentumLinearFactor: float
}

/// Utility weights: press zone bonuses, wing space, structured base, directive change threshold.
/// Read by: TeamOrchestrator/UtilityActions.fs
[<CLIMutable>]
type UtilityWeights = {
    /// Press zone bonus for high press in attacking zone. Range: [0,1]. Read by: UtilityActions.fs
    PressZoneBonus_HighAttacking: float
    /// Press zone bonus for high press in midfield zone. Range: [0,1]. Read by: UtilityActions.fs
    PressZoneBonus_HighMidfield: float
    /// Press zone bonus for high press in defensive zone. Range: [-1,0]. Read by: UtilityActions.fs
    PressZoneBonus_HighDefensive: float
    /// Press zone bonus for mid press in attacking/midfield zone. Range: [0,1]. Read by: UtilityActions.fs
    PressZoneBonus_MidAttackingMidfield: float
    /// Press zone bonus for mid press in defensive zone. Range: [-1,0]. Read by: UtilityActions.fs
    PressZoneBonus_MidDefensive: float
    /// Press zone bonus for low press. Range: [0,1]. Read by: UtilityActions.fs
    PressZoneBonus_Low: float
    /// Possession change window (subticks). Range: [0,5000]. Read by: BlackboardBuilder.fs
    PossessionChangeWindow: float
    /// Score difference press step threshold. Range: [0,1]. Read by: BlackboardBuilder.fs
    ScoreDiffPressStep: float
    /// Wing space base bonus. Range: [0,1]. Read by: UtilityActions.fs
    WingSpaceBase: float
    /// Stamina wing multiplier. Range: [0,1]. Read by: UtilityActions.fs
    StaminaWingMult: float
    /// Structured action base score. Range: [0,1]. Read by: UtilityActions.fs
    StructuredBase: float
    /// Directive change threshold (score difference to switch). Range: [0,0.5]. Read by: UtilityActions.fs
    DirectiveChangeThreshold: float
    /// Opponent high line bonus for drop deep. Range: [-1,0]. Read by: UtilityActions.fs
    DropDeepHighLinePenalty: float
    /// Lead bonus per goal for drop deep. Range: [0,1]. Read by: UtilityActions.fs
    DropDeepLeadBonus: float
    /// Time bonus for drop deep (late game). Range: [0,1]. Read by: UtilityActions.fs
    DropDeepTimeBonus: float
    /// Drop deep base score. Range: [0,1]. Read by: UtilityActions.fs
    DropDeepBase: float
    /// Counter press stamina factor. Range: [0,1]. Read by: UtilityActions.fs
    CounterPressStaminaFactor: float
    /// Counter press intensity bonus. Range: [0,1]. Read by: UtilityActions.fs
    CounterPressIntensityBonus: float
    /// Counter press base score. Range: [0,1]. Read by: UtilityActions.fs
    CounterPressBase: float
    /// Build from back no press bonus. Range: [0,1]. Read by: UtilityActions.fs
    BuildFromBackNoPressBonus: float
    /// Build from back mid press bonus. Range: [0,1]. Read by: UtilityActions.fs
    BuildFromBackMidPressBonus: float
    /// Build from back high press penalty. Range: [-1,0]. Read by: UtilityActions.fs
    BuildFromBackHighPressPenalty: float
    /// Build from back low block bonus. Range: [0,1]. Read by: UtilityActions.fs
    BuildFromBackLowBlockBonus: float
    /// Build from back base score. Range: [0,1]. Read by: UtilityActions.fs
    BuildFromBackBase: float
    /// Direct play urgency bonus (losing, late). Range: [0,1]. Read by: UtilityActions.fs
    DirectPlayUrgencyBonus: float
    /// Direct play urgency bonus (losing, any time). Range: [0,1]. Read by: UtilityActions.fs
    DirectPlayUrgencyBonusAny: float
    /// Direct play high line bonus. Range: [0,1]. Read by: UtilityActions.fs
    DirectPlayHighLineBonus: float
    /// Direct play base score. Range: [0,1]. Read by: UtilityActions.fs
    DirectPlayBase: float
    /// Sit and counter base score. Range: [0,1]. Read by: UtilityActions.fs
    SitAndCounterBase: float
    /// Sit and counter lead bonus per goal. Range: [0,1]. Read by: UtilityActions.fs
    SitAndCounterLeadBonus: float
    /// Sit and counter stamina factor. Range: [0,1]. Read by: UtilityActions.fs
    SitAndCounterStaminaFactor: float
    /// Hold possession lead bonus. Range: [0,1]. Read by: UtilityActions.fs
    HoldPossessionLeadBonus: float
    /// Hold possession draw bonus. Range: [0,1]. Read by: UtilityActions.fs
    HoldPossessionDrawBonus: float
    /// Hold possession losing penalty. Range: [-1,0]. Read by: UtilityActions.fs
    HoldPossessionLosingPenalty: float
    /// Hold possession time bonus. Range: [0,1]. Read by: UtilityActions.fs
    HoldPossessionTimeBonus: float
    /// Hold possession high press penalty. Range: [-1,0]. Read by: UtilityActions.fs
    HoldPossessionPressPenalty: float
    /// Hold possession base score. Range: [0,1]. Read by: UtilityActions.fs
    HoldPossessionBase: float
    /// Compact block losing bonus. Range: [0,1]. Read by: UtilityActions.fs
    CompactBlockLosingBonus: float
    /// Compact block winning penalty. Range: [-1,0]. Read by: UtilityActions.fs
    CompactBlockWinningPenalty: float
    /// Compact block opponent bonus. Range: [0,1]. Read by: UtilityActions.fs
    CompactBlockOpponentBonus: float
    /// Compact block time bonus. Range: [0,1]. Read by: UtilityActions.fs
    CompactBlockTimeBonus: float
    /// Compact block base score. Range: [0,1]. Read by: UtilityActions.fs
    CompactBlockBase: float
    /// High line cohesion bonus. Range: [0,1]. Read by: UtilityActions.fs
    HighLineCohesionBonus: float
    /// High line stamina factor. Range: [0,1]. Read by: UtilityActions.fs
    HighLineStaminaFactor: float
    /// High line risk penalty. Range: [-1,0]. Read by: UtilityActions.fs
    HighLineRiskPenalty: float
    /// High line base score. Range: [0,1]. Read by: UtilityActions.fs
    HighLineBase: float
    /// Pressing success bonus factor. Range: [0,1]. Read by: UtilityActions.fs
    PressingSuccessBonus: float
    /// Opponent high line + no press bonus. Range: [0,1]. Read by: UtilityActions.fs
    OpponentHighLineNoPressBonus: float
    /// Weakness zone overload bonus. Range: [0,1]. Read by: UtilityActions.fs
    OverloadWeaknessBonus: float
    /// Overload flank base score. Range: [0,1]. Read by: UtilityActions.fs
    OverloadFlankBase: float
}

/// Performance weights: stat/condition/morale weights and sigmoid curve parameters.
/// Read by: ActionMath.fs, BalanceConfig
[<CLIMutable>]
type PerformanceWeightsMap = {
    /// Stat weight for duel performance. Range: [0,1]. Read by: ActionMath.fs
    DuelStatWeight: float
    /// Condition weight for duel performance. Range: [0,1]. Read by: ActionMath.fs
    DuelConditionWeight: float
    /// Morale weight for duel performance. Range: [0,1]. Read by: ActionMath.fs
    DuelMoraleWeight: float
    /// Sigmoid curve steepness for duel. Range: [1,20]. Read by: ActionMath.fs
    DuelCurveSteepness: float
    /// Sigmoid inflection point for duel. Range: [0,1]. Read by: ActionMath.fs
    DuelCurveInflection: float
    /// Stat weight for technical performance. Range: [0,1]. Read by: ActionMath.fs
    TechnicalStatWeight: float
    /// Condition weight for technical performance. Range: [0,1]. Read by: ActionMath.fs
    TechnicalConditionWeight: float
    /// Morale weight for technical performance. Range: [0,1]. Read by: ActionMath.fs
    TechnicalMoraleWeight: float
    /// Sigmoid curve steepness for technical. Range: [1,20]. Read by: ActionMath.fs
    TechnicalCurveSteepness: float
    /// Sigmoid inflection point for technical. Range: [0,1]. Read by: ActionMath.fs
    TechnicalCurveInflection: float
    /// Stat weight for decision performance. Range: [0,1]. Read by: ActionMath.fs
    DecisionStatWeight: float
    /// Condition weight for decision performance. Range: [0,1]. Read by: ActionMath.fs
    DecisionConditionWeight: float
    /// Morale weight for decision performance. Range: [0,1]. Read by: ActionMath.fs
    DecisionMoraleWeight: float
    /// Sigmoid curve steepness for decision. Range: [1,20]. Read by: ActionMath.fs
    DecisionCurveSteepness: float
    /// Sigmoid inflection point for decision. Range: [0,1]. Read by: ActionMath.fs
    DecisionCurveInflection: float
}

/// Referee weights: card/injury probability, foul aggression, home card reduction.
/// Read by: Referee/RefereeAgent.fs
[<CLIMutable>]
type RefereeWeights = {
    /// Base probability of card per foul. Range: [0,0.05]. Read by: RefereeAgent.fs
    CardBaseProb: float
    /// Aggression multiplier for card probability. Range: [0,0.001]. Read by: RefereeAgent.fs
    CardAggressionMult: float
    /// Home team card reduction factor. Range: [0,0.5]. Read by: RefereeAgent.fs
    CardHomeReduction: float
    /// Base probability of injury per duel. Range: [0,0.005]. Read by: RefereeAgent.fs
    InjuryBaseProb: float
    /// Inverse strength multiplier for injury probability. Range: [0,0.0001]. Read by: RefereeAgent.fs
    InjuryStrengthInverseMult: float
    /// Base foul aggression threshold. Range: [0,1]. Read by: RefereeAgent.fs
    FoulAggressionBase: float
    /// Aggression multiplier for foul decision. Range: [0,1]. Read by: RefereeAgent.fs
    FoulAggressionMult: float
}

/// Environment weights: weather, pitch, crowd effects on gameplay.
/// Read by: Simulation/EnvironmentSystems.fs
[<CLIMutable>]
type EnvironmentWeights = {
    /// Clear weather ball speed modifier. Range: [0.8,1.2]. Read by: EnvironmentSystems.fs
    WeatherClearModifier: float
    /// Light rain ball speed modifier. Range: [0.8,1.0]. Read by: EnvironmentSystems.fs
    WeatherLightRainModifier: float
    /// Heavy rain ball speed modifier. Range: [0.7,0.95]. Read by: EnvironmentSystems.fs
    WeatherHeavyRainModifier: float
    /// Snow ball speed modifier. Range: [0.6,0.9]. Read by: EnvironmentSystems.fs
    WeatherSnowModifier: float
    /// Windy ball speed modifier. Range: [0.7,1.0]. Read by: EnvironmentSystems.fs
    WeatherWindyModifier: float
    /// Dry pitch slip base probability. Range: [0,0.05]. Read by: EnvironmentSystems.fs
    PitchDrySlipBase: float
    /// Damp pitch slip base probability. Range: [0,0.1]. Read by: EnvironmentSystems.fs
    PitchDampSlipBase: float
    /// Wet pitch slip base probability. Range: [0,0.2]. Read by: EnvironmentSystems.fs
    PitchWetSlipBase: float
    /// Waterlogged pitch slip base probability. Range: [0,0.3]. Read by: EnvironmentSystems.fs
    PitchWaterloggedSlipBase: float
    /// Agility reduction factor for slip probability. Range: [0,1]. Read by: EnvironmentSystems.fs
    SlipAgilityReduction: float
    /// Maximum stadium capacity reference. Range: [10000,150000]. Read by: EnvironmentSystems.fs
    CrowdMaxCapacity: float
    /// Weight for stadium capacity in home advantage. Range: [0,1]. Read by: EnvironmentSystems.fs
    CrowdCapacityWeight: float
    /// Weight for home support in home advantage. Range: [0,1]. Read by: EnvironmentSystems.fs
    CrowdSupportWeight: float
    /// Weight for momentum in home advantage. Range: [0,1]. Read by: EnvironmentSystems.fs
    CrowdMomentumWeight: float
    /// Weight for match importance in home advantage. Range: [0,1]. Read by: EnvironmentSystems.fs
    CrowdImportanceWeight: float
    /// Maximum home advantage multiplier. Range: [0,0.5]. Read by: EnvironmentSystems.fs
    CrowdMaxAdvantage: float
    /// Away pressure crowd multiplier. Range: [0,1]. Read by: EnvironmentSystems.fs
    AwayPressureCrowdMult: float
}

/// Momentum weights: event delta, decay, min/max bounds.
/// Read by: Simulation/Momentum.fs
[<CLIMutable>]
type MomentumWeights = {
    /// Delta added to momentum on event. Range: [0,2]. Read by: Momentum.fs
    EventDelta: float
    /// Decay rate per subtick. Range: [0,0.1]. Read by: Momentum.fs
    Decay: float
    /// Minimum momentum bound. Range: [-20,0]. Read by: Momentum.fs
    Min: float
    /// Maximum momentum bound. Range: [0,20]. Read by: Momentum.fs
    Max: float
    /// Half-life in seconds for momentum decay. Range: [1,60]. Read by: BlackboardBuilder.fs
    HalfLifeSeconds: float
}

/// Home advantage weights: duel, shot, pass, dribble, set play, tackle, free kick, penalty bonuses.
/// Read by: Core/HomeBonus.fs, BalanceConfig.HomeAdvantage
[<CLIMutable>]
type HomeAdvantageWeights = {
    /// Overall home advantage strength. Range: [0,2]. Read by: HomeBonus.fs
    Strength: float
    /// Home duel attack bonus. Range: [0,10]. Read by: HomeBonus.fs
    DuelAttackBonus: float
    /// Home duel defense bonus. Range: [0,10]. Read by: HomeBonus.fs
    DuelDefenseBonus: float
    /// Home shot composure bonus. Range: [0,10]. Read by: HomeBonus.fs
    ShotComposureBonus: float
    /// Home pass accuracy bonus. Range: [0,0.2]. Read by: HomeBonus.fs
    PassAccuracyBonus: float
    /// Home dribble bonus. Range: [0,10]. Read by: HomeBonus.fs
    DribbleBonus: float
    /// Home set play accuracy bonus. Range: [0,0.2]. Read by: HomeBonus.fs
    SetPlayAccuracyBonus: float
    /// Home tackle bonus. Range: [0,10]. Read by: HomeBonus.fs
    TackleBonus: float
    /// Home free kick composure bonus. Range: [0,10]. Read by: HomeBonus.fs
    FreeKickComposure: float
    /// Home penalty bonus. Range: [0,0.2]. Read by: HomeBonus.fs
    PenaltyBonus: float
    /// Home card reduction factor. Range: [0,0.5]. Read by: HomeBonus.fs
    CardReduction: float
    /// Home fatigue reduction factor. Range: [0,0.5]. Read by: HomeBonus.fs
    FatigueReduction: float
}

/// Manager weights: fatigue reaction, momentum threshold, condition thresholds, sub windows.
/// Read by: Manager/ManagerAgent.fs, BalanceConfig.Manager
[<CLIMutable>]
type ManagerWeights = {
    /// Fatigue reaction threshold (condition %). Range: [40,90]. Read by: ManagerAgent.fs
    FatigueReactionThreshold: int
    /// Sustained momentum subticks threshold. Range: [0,50000]. Read by: ManagerAgent.fs
    SustainedMomentumSubTicks: int
    /// Momentum threshold for manager reaction. Range: [-5,0]. Read by: ManagerAgent.fs
    MomentumThreshold: float
    /// Fatigue check interval (subticks). Range: [0,50000]. Read by: ManagerAgent.fs
    FatigueCheckSubTicks: int
    /// Condition threshold when losing. Range: [50,100]. Read by: ManagerAgent.fs
    ConditionThresholdLosing: int
    /// Condition threshold when drawing. Range: [50,100]. Read by: ManagerAgent.fs
    ConditionThresholdDrawing: int
    /// Condition threshold when winning. Range: [40,90]. Read by: ManagerAgent.fs
    ConditionThresholdWinning: int
}

/// Perception weights: vision radius, cone angle, peripheral, awareness, anticipation, communication.
/// Read by: Player/Perception/Perception.fs, BalanceConfig.Perception
[<CLIMutable>]
type PerceptionWeights = {
    /// Base vision radius (meters). Range: [5,50]. Read by: Perception.fs
    VisionRadiusBase: float
    /// Maximum vision radius (meters). Range: [20,80]. Read by: Perception.fs
    VisionRadiusMax: float
    /// Vision cone angle (radians). Range: [0,6.28]. Read by: Perception.fs
    VisionConeAngle: float
    /// Peripheral awareness multiplier. Range: [0,1]. Read by: Perception.fs
    PeripheralMultiplier: float
    /// Minimum awareness floor (meters). Range: [5,50]. Read by: Perception.fs
    MinimumAwarenessFloor: float
    /// Anticipation bonus radius (meters). Range: [0,20]. Read by: Perception.fs
    AnticipationBonusRadius: float
    /// Goalkeeper cone angle (radians). Range: [0,6.28]. Read by: Perception.fs
    GoalkeeperConeAngle: float
    /// Communication range (meters). Range: [5,50]. Read by: Perception.fs
    CommunicationRange: float
    /// Set piece simplified radius (meters). Range: [10,60]. Read by: Perception.fs
    SetPieceSimplifiedRadius: float
    /// Blind pass vision threshold (stat 1-20). Range: [1,20]. Read by: Perception.fs
    BlindPassVisionThreshold: int
    /// Blind pass composure threshold (stat 1-20). Range: [1,20]. Read by: Perception.fs
    BlindPassComposureThreshold: int
    /// Blind pass success penalty. Range: [0,1]. Read by: Perception.fs
    BlindPassSuccessPenalty: float
}

/// Development weights: age brackets, focus multipliers, weekly delta divisor, stat thresholds.
/// Read by: World/Phases/DevelopmentPhase.fs
[<CLIMutable>]
type DevelopmentWeights = {
    /// Max skill gap per week for U21 players. Range: [0,10]. Read by: DevelopmentPhase.fs
    AgeBracket_MaxDelta_U21: int
    /// Max skill gap per week for U25 players. Range: [0,10]. Read by: DevelopmentPhase.fs
    AgeBracket_MaxDelta_U25: int
    /// Max skill gap per week for U28 players. Range: [0,10]. Read by: DevelopmentPhase.fs
    AgeBracket_MaxDelta_U28: int
    /// Max skill gap per week for U31 players. Range: [0,10]. Read by: DevelopmentPhase.fs
    AgeBracket_MaxDelta_U31: int
    /// Max skill gap per week for U34 players. Range: [0,10]. Read by: DevelopmentPhase.fs
    AgeBracket_MaxDelta_U34: int
    /// Training focus multiplier for goalkeeping. Range: [0.5,3]. Read by: DevelopmentPhase.fs
    FocusMultiplier_Goalkeeping: float
    /// Base training focus multiplier for physical. Range: [0,1]. Read by: DevelopmentPhase.fs
    FocusMultiplier_PhysicalBase: float
    /// Pressing component of physical focus. Range: [0,1]. Read by: DevelopmentPhase.fs
    FocusMultiplier_Physical_Pressing: float
    /// Positional component of physical focus. Range: [0,1]. Read by: DevelopmentPhase.fs
    FocusMultiplier_Physical_Positional: float
    /// Training focus multiplier for mental. Range: [0.5,3]. Read by: DevelopmentPhase.fs
    FocusMultiplier_Mental: float
    /// Base training focus multiplier for technical. Range: [0,1]. Read by: DevelopmentPhase.fs
    FocusMultiplier_TechnicalBase: float
    /// Creativity component of technical focus. Range: [0,1]. Read by: DevelopmentPhase.fs
    FocusMultiplier_Technical_Creativity: float
    /// Directness component of technical focus. Range: [0,1]. Read by: DevelopmentPhase.fs
    FocusMultiplier_Technical_Directness: float
    /// Weekly delta divisor for skill development. Range: [50,300]. Read by: DevelopmentPhase.fs
    WeeklyDeltaDivisor: float
    /// Directness threshold for stat focus selection. Range: [0,1]. Read by: DevelopmentPhase.fs
    StatFocus_DirectnessThreshold: float
    /// Attacking depth threshold for stat focus selection. Range: [0,1]. Read by: DevelopmentPhase.fs
    StatFocus_AttackingDepthThreshold: float
    /// Defensive height threshold for stat focus selection. Range: [0,1]. Read by: DevelopmentPhase.fs
    StatFocus_DefensiveHeightThreshold: float
    /// Creativity threshold for stat focus selection. Range: [0,1]. Read by: DevelopmentPhase.fs
    StatFocus_CreativityThreshold: float
    /// Positive threshold for maybeStat probability. Range: [0,1]. Read by: DevelopmentPhase.fs
    MaybeStat_PositiveThreshold: float
    /// Negative threshold for maybeStat probability. Range: [0,1]. Read by: DevelopmentPhase.fs
    MaybeStat_NegativeThreshold: float
}

/// Calibration targets: target metrics for Loop C training optimization.
/// Read by: Training/ErrorCalculator.fs
[<CLIMutable>]
type CalibrationTargets = {
    /// Target goals per match (total). Range: [0,6]. Read by: ErrorCalculator.fs
    GoalsPerMatch: float
    /// Target shots per match (per team). Range: [5,30]. Read by: ErrorCalculator.fs
    ShotsPerMatch: float
    /// Target pass success rate. Range: [0.5,0.95]. Read by: ErrorCalculator.fs
    PassSuccessRate: float
    /// Target cross success rate. Range: [0.1,0.5]. Read by: ErrorCalculator.fs
    CrossSuccessRate: float
    /// Target home win percentage. Range: [0.3,0.6]. Read by: ErrorCalculator.fs
    HomeWinPct: float
    /// Target draw percentage. Range: [0.15,0.35]. Read by: ErrorCalculator.fs
    DrawPct: float
    /// Target away win percentage. Range: [0.15,0.4]. Read by: ErrorCalculator.fs
    AwayWinPct: float
    /// Target cards per match (total). Range: [0,8]. Read by: ErrorCalculator.fs
    CardsPerMatch: float
    /// Target injuries per match (total). Range: [0,3]. Read by: ErrorCalculator.fs
    InjuriesPerMatch: float
    /// Target dribbles per match (per team). Range: [5,50]. Read by: ErrorCalculator.fs
    DribblesPerMatch: float
    /// Target crosses per match (per team). Range: [5,40]. Read by: ErrorCalculator.fs
    CrossesPerMatch: float
    /// Target long balls per match (per team). Range: [10,80]. Read by: ErrorCalculator.fs
    LongBallsPerMatch: float
    /// Target duel ticks per match. Range: [100,500]. Read by: ErrorCalculator.fs
    DuelTicksPerMatch: float
}

/// Experience metrics: capture subjective "feel" of the match for Loop C training.
/// Read by: Training/MetricsAggregator.fs
[<CLIMutable>]
type ExperienceMetrics = {
    /// Variance of momentum within the match. Range: [0,100]. Read by: MetricsAggregator.fs
    MomentumVariance: float
    /// Entropy of event distribution across match minutes. Range: [0,5]. Read by: MetricsAggregator.fs
    EventDistributionEntropy: float
    /// Number of lead changes during the match. Range: [0,20]. Read by: MetricsAggregator.fs
    LeadChanges: int
    /// Frequency of goals in the last 15 minutes. Range: [0,1]. Read by: MetricsAggregator.fs
    LateGoalFrequency: float
    /// Rate of comebacks (overcoming a deficit). Range: [0,1]. Read by: MetricsAggregator.fs
    ComebackRate: float
}

/// Top-level engine weights: all sections of weights.json.
/// Loaded by: WeightsLoader.fs. Optimized by: Loop C (Training/).
/// Static per season, never modified at runtime.
[<CLIMutable>]
type EngineWeights = {
    /// Schema version for forward compatibility. Read by: WeightsLoader.fs
    Version: int
    /// Profile weights for BehavioralProfile derivation. Read by: Player.fs
    ProfileWeights: ProfileWeights
    /// Individual action weights (shoot, pass, dribble, cross, long ball). Read by: PlayerScorer.fs
    Individual: IndividualWeights
    /// Personality derivation weights. Read by: PlayerPersonality.fs
    Personality: PersonalityWeights
    /// Collective behavior weights (directive, emergent, modifiers, chemistry). Read by: TeamOrchestrator modules
    Collective: CollectiveWeights
    /// Outcome weights (duel, shot, pass, cross, tackle, set piece, GK, xG). Read by: Action modules
    Outcomes: OutcomeWeights
    /// Win probability calculation weights. Read by: WinProbability.fs
    WinProbability: WinProbabilityWeights
    /// Utility action evaluation weights. Read by: UtilityActions.fs
    Utility: UtilityWeights
    /// Performance calculation weights (duel, technical, decision). Read by: ActionMath.fs
    Performance: PerformanceWeightsMap
    /// Referee decision weights. Read by: RefereeAgent.fs
    Referee: RefereeWeights
    /// Environment effect weights (weather, pitch, crowd). Read by: EnvironmentSystems.fs
    Environment: EnvironmentWeights
    /// Match momentum weights. Read by: Momentum.fs
    Momentum: MomentumWeights
    /// Home advantage bonus weights. Read by: HomeBonus.fs
    HomeAdvantage: HomeAdvantageWeights
    /// Manager behavior weights. Read by: ManagerAgent.fs
    Manager: ManagerWeights
    /// Perception system weights. Read by: Perception.fs
    Perception: PerceptionWeights
    /// Player development weights (age brackets, focus, thresholds). Read by: DevelopmentPhase.fs
    Development: DevelopmentWeights
    /// Calibration targets for Loop C training. Read by: Training/ErrorCalculator.fs
    CalibrationTargets: CalibrationTargets
}
