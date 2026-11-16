namespace FootballEngine.Core.Match

/// Ball behaviour — physics, trajectory, contact, reception. One record so ball
/// tuning never leaks into agent code. (was PhysicsConfig + Interception + part of Pass)
[<Struct>]
type BallConfig =
    {
        Gravity: float<meterPerSecondSquared>
        /// kg/m³; with Mass and Radius it fixes how strongly the air acts on this ball.
        AirDensity: float
        /// kg
        Mass: float
        DragSubcritical: float
        DragSupercritical: float
        DragCrisisSpeed: float<meterPerSecond>
        DragCrisisWidth: float<meterPerSecond>
        /// Lift coefficient per unit spin parameter (r·ω/v) before saturation.
        LiftSlope: float
        MaxLift: float
        WoodworkRestitution: float
        WoodworkFriction: float
        NetRestitution: float
        NetFriction: float
        Radius: float<meter>
        InertiaRatio: float
        SlipFriction: float
        RollingResistance: float
        FootReach: float<meter>
        FootContactHeight: float<meter>
        KickWindup: int<subtick>
        KickRecovery: int<subtick>
        CarryTouchInterval: int<subtick>
        CarryOffset: float<meter>
        CarryMaxSpeed: float<meterPerSecond>
        CarryTechniqueError: float
        PassRollingSpinRatio: float
        BounceRestitution: float
        BounceFriction: float
        StopSpeedCutoff: float<meterPerSecond>
        MaxShotSpeed: float<meterPerSecond>
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
        InterceptionIntentBoost: float
    }

/// Movement and fatigue — speeds, acceleration, separation, smoothing, decay and
/// recovery, scorer thresholds. (was Movement parts + scorer thresholds)
[<Struct>]
type MovementConfig =
    {
        SprintSpeed: float<meterPerSecond>
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
        /// A set-piece run sidesteps any body closer than this that stands ahead on its path.
        SetPieceAvoidRadius: float<meter>
        ConditionImpactThreshold: float
        IntentFloor: float
        IntentHysteresis: float
        CommittedRunDuration: int<subtick>
        /// Share of the jog-to-sprint range a carrier uses with the ball at their feet.
        CarryEffort: float
    }

/// Stride clock. Step frequency rises with speed and falls with leg length (pendulum scaling), so ball touches
/// and the viewer's feet share one cadence.
[<Struct>]
type GaitConfig =
    {
        StepFrequencyBase: float
        StepFrequencyPerSpeed: float
        StepFrequencyMax: float
        ReferenceLegLength: float<meter>
        StandingSpeed: float<meterPerSecond>
        CarryTouchPhase: float
        /// Cycles before a carry touch at which the touching foot leaves the ground to swing to the ball.
        CarrySwingLead: float
    }

/// How one part of the boot shapes a kicked ball. `Spread` is the direction error (radians, one sigma) of a player
/// with no technique. `SideSpinPerSpeed` is the side spin per m/s of launch; its sense belongs to the surface: the
/// inside curls the ball toward the kicking foot's inside, the outside away from it.
[<Struct>]
type StrikeSurfaceConfig =
    { Spread: float
      SideSpinPerSpeed: float }

/// Contact surfaces of the boot. The surface is chosen from the foot, the direction across the body and the launch
/// speed; technique, pressure and balance at contact set how far a delivery strays from its aim.
[<Struct>]
type StrikeTechniqueConfig =
    {
        Inside: StrikeSurfaceConfig
        Instep: StrikeSurfaceConfig
        Outside: StrikeSurfaceConfig
        /// Angle (radians) toward the kicking foot's outer side beyond which the outside of the boot is used.
        OutsideAngle: float
        InstepSpeed: float<meterPerSecond>
        /// Share of the surface spread a perfect technique removes.
        TechniqueShare: float
        PressureRadius: float<meter>
        PressureSpreadPerOpponent: float
        /// Share of the pressure effect a fully composed player ignores.
        ComposureShield: float
        OffBalanceSpread: float
    }

/// Fixed-step collision, balance and recovery controls. Geometry fallbacks are
/// explicit so missing player morphology never silently changes contact reach.
[<Struct>]
type ContactConfig =
    {
        BodyRadiusFallback: float<meter>
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
        /// Share of the sliding speed a trap of no technique absorbs; technique adds `TrapAbsorptionSkill`.
        TrapAbsorptionBase: float
        TrapAbsorptionSkill: float
        /// Extra restitution of a trap of no technique: a poor first touch bounces off the boot.
        TrapPoorRestitution: float
        /// Factor on a trap's lost technique while the receiver is off balance.
        OffBalanceTrapPenalty: float
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
        MaximumPathError: float<meter>
        /// Ticks after a strike during which the striker's own body cannot touch the departing ball.
        StrikeSeparation: int<subtick>
    }

/// Shot-stopping: how fast a keeper reads a strike, how far a dive carries his hands, and what his hands can hold.
type KeeperConfig =
    {
        /// Seconds before an unanticipating keeper (0) reacts to a strike.
        ReactionBase: float<second>
        /// Seconds a fully anticipating keeper (100) saves off ReactionBase.
        ReactionSkill: float<second>
        /// Shoulder-to-glove reach without a dive.
        ArmReach: float<meter>
        /// Shoulder height as a share of stature; the dive is measured from here.
        ShoulderHeightRatio: float
        /// Seconds spent loading the push-off before the body leaves the ground.
        DiveSetup: float<second>
        /// Lateral dive speed of an average-agility keeper.
        DiveSpeed: float<meterPerSecond>
        /// Longest body extension a dive adds to arm reach.
        MaxDive: float<meter>
        /// Two gloved hands present a larger, softer surface than one bare hand.
        HandRadius: float<meter>
        /// Hand placement error (one standard deviation) of an unpositioned keeper at full stretch.
        HandError: float<meter>
        /// Ticks either side of the planned contact during which the hands are in place.
        HandWindow: int<subtick>
        /// Fastest ball a set keeper with perfect handling holds rather than parries.
        CatchSpeed: float<meterPerSecond>
        /// Restitution of a parry: the hands absorb most of the shot's energy.
        ParryRestitution: float
        /// Seconds a keeper keeps a caught ball before distributing it.
        HoldSeconds: float<second>
    }

/// Temporal commitments are separate from the solver's laws and from team
/// movement controls, so agents can change intent without changing physics.
[<Struct>]
type ContactActionConfig =
    {
        ContainPreparation: int<subtick>
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
        /// Horizontal reach of a sliding tackle's boot from the hips, the body travelling on the ground behind it.
        SlidingReach: float<meter>
        /// A receiver keeps running to where the pass was aimed while the rolling ball will arrive within this of it;
        /// re-solving the meet point for every small deviation turns him away from a run the passer already led.
        ReceptionTargetTolerance: float<meter>
        /// A tackle is only launched when the carrier's body stays at least this far off the boot's path to the ball;
        /// closer, the boot would go through his legs, which is a trip rather than a challenge.
        TackleBodyClearance: float<meter>
        HeadReach: float<meter>
        MaximumReorientation: float<radian>
        ExecutionErrorScale: float<meter>
        MaximumExecutionError: float<meter>
        ShieldOffset: float<meter>
        NewImpactThreshold: float<meterPerSecond>
        CarelessRelativeSpeed: float<meterPerSecond>
        RecklessRelativeSpeed: float<meterPerSecond>
        ExcessiveRelativeSpeed: float<meterPerSecond>
        DangerousBallFirstSpeed: float<meterPerSecond>
    }

/// Action outcomes — pass, shot, duel, cross, GK and dribble success curves,
/// tunable without touching agent logic. (was PassConfig, ShotConfig, DuelConfig,
/// CrossConfig, DribbleConfig, GKConfig, DecisionConfig + IndividualWeights)
[<Struct>]
type ActionsConfig =
    {
        /// How far a player sees teammates and opponents when he perceives the pitch.
        PerceptionRadius: float<meter>
        /// An idle carrier with an opponent this close decides at once instead of at the decision cadence.
        ReflexDecisionRadius: float<meter>
        PassBaseQuality: float
        PassDistancePenaltyPerMeter: float
        PassLongThreshold: float<meter>
        PassVisionWeight: float
        PassTechnicalWeight: float
        PassJitterBase: float
        PassHeavyTouchChance: float
        /// Speed a ground pass should still carry at the receiver; launch speed is solved from the roll model.
        PassArrivalSpeed: float<meterPerSecond>
        /// Extra arrival speed per metre: longer passes are driven so defenders get less time to close them.
        PassArrivalSpeedGain: float
        MaxPassExecutionDistance: float<meter>
        ShotQualityGate: float
        /// Execution error (rad, one standard deviation) of a world-class finisher striking unopposed.
        ShotErrorBase: float
        /// Extra execution error (rad) for a finisher at 0 rather than 100.
        ShotErrorSkill: float
        /// Vertical error as a share of horizontal: height is controlled by the strike, direction by the body.
        ShotVerticalErrorRatio: float
        /// Gap kept inside the post by a finisher at 0 when picking a corner; better finishers aim tighter.
        ShotPostMargin: float<meter>
        ShotDistanceToGoalMultiplier: float
        /// Upper bound of the sidespin a shooter can impart to curl the ball around the keeper.
        ShotCurlSpin: float<radianPerSecond>
        /// Strike speed of a finisher with no strength, struck for power rather than placement.
        ShotSpeedBase: float<meterPerSecond>
        /// Extra full-power strike speed of a finisher at Strength 100.
        ShotSpeedStrength: float<meterPerSecond>
        /// Share of strike speed given up to place a finish tight to the post with the side of the foot.
        ShotPlacementSpeedCost: float
        CrossArrivalHeight: float<meter>
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
        MemoryMentalDecay: float
    }

/// Dead-ball pacing and geometry: restart delays, wall setup, corner/free-kick targets.
[<Struct>]
type SetPieceConfig =
    {
        KickoffDelay: float<second>
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
        /// How far behind the corner spot the taker waits, along the goal line into the pitch.
        CornerTakerOffset: float<meter>
        /// How far every player but the executor stands from the ball before the kick is authorized.
        BallClearance: float<meter>
        TargetTolerance: float<meter>
        MaxTargetDistance: float<meter>
        /// How far inside the touchline a throw-in receiver is placed; the law needs the receiver on
        /// the field, and a player chasing the ball out of play is often standing on the line itself.
        ThrowInReceiverInset: float<meter>
    }

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
    {
        ReevaluateTicks: int<tickDelta>
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
        /// Within this distance of its own goal a retreating side still sends its quickest defender to the ball:
        /// a block that leaves the carrier free lets him walk into the box.
        EngageGoalDistance: float<meter>
        CentralHalfWidth: float<meter>
        DepthSafety: float<meter>
        ScreenFraction: float
        RetreatBallGap: float<meter>
        RetreatWidthScale: float
        SettledTolerance: float<meter>
        MaximumScreens: int
        CoverEffort: float
        ScreenEffort: float
        RecoveryEffort: float
    }

[<Struct>]
type RefereeConfig =
    {
        /// Chance each physically judged severity is sanctioned: careless fouls are booked only for what the contact
        /// alone cannot show (a promising attack stopped, persistent infringement); reckless means a caution and
        /// excessive force a dismissal (Law 12), short of the referee misjudging it.
        FoulMinorChance: float
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
        /// Multiplies the discretionary booking chance (careless fouls and the last-man bonus) of an already booked
        /// player: referees caution a booked player again for a reckless foul, rarely for a careless one.
        BookedLeniency: float
        /// Same for a challenge judged reckless: severity reads the offender's speed only, a proxy referees resolve in
        /// a booked player's favour when it is borderline.
        BookedRecklessLeniency: float
        PromisingAttackFactor: float
        ZoneFactor: float
    }

/// Manager behaviour — substitution windows and thresholds. (was ManagerConfig +
/// TimingConfig parts)
[<Struct>]
type ManagerConfig =
    {
        MaxSubs: int
        FirstSubWindowMin: int<matchMin>
        SecondSubWindowMin: int<matchMin>
        ThirdSubWindowMin: int<matchMin>
        ConditionSubThreshold: float
        AttackingSubGoalDeficit: int
        InjurySubCondition: float
        RedCardCompact: bool
        DrawAttackingMinute: int<matchMin>
        WinningDefensiveMinute: int<matchMin>
        /// Condition (0..1) under which a player is replaced, by the score: a side that is losing changes tired
        /// legs sooner, one that is winning keeps its shape longer.
        ConditionThresholdWinning: float
        ConditionThresholdDrawing: float
        ConditionThresholdLosing: float
    }

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
    {
        MatchImportanceWeight: float
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
        ShootoutRounds: int
        /// Events each team remembers for its emergent read of the match (channels, momentum).
        EventWindowCapacity: int
    }

/// The carrier's choice in goal probability: every option is worth what it is expected to add to his side's
/// chance of scoring minus what it gives the opponent, so a shot, a pass and a carry compare on one scale and a
/// player's skill changes how likely each is to work, not how much he likes it.
type ExpectedValueConfig =
    {
        /// Extra chance, over the floor, that a possession at the goal mouth ends in a goal.
        ThreatPeak: float
        /// Distance over which that chance falls by e.
        ThreatDecay: float<meter>
        /// Chance a possession anywhere still ends in a goal.
        ThreatFloor: float
        /// Race margin, in intercept steps, that turns a coin-flip pass into a near-certain one.
        PassMarginScale: float
        /// Margin steps an excellent passer (Passing 100) gains over an average one (60): weight and accuracy.
        PassSkillSteps: float
        /// Lowest race margin a carrier still considers; below it the pass is not on at all.
        PassMinimumMargin: int
        /// Metres a carry gains before the next decision.
        CarryAdvance: float<meter>
        /// Chance an average dribbler keeps the ball against an average tackler with nobody else near.
        CarryKeepBase: float
        /// Change in that chance per point of Dribbling over the nearest opponent's Tackling (0..100 scale).
        CarrySkillWeight: float
        /// Lost keep chance per opponent within reach.
        CarryPressurePenalty: float
        /// Distance at which an opponent closes a carry or an idle ball.
        PressureRadius: float<meter>
        /// Share of a shot's chance each outfield opponent inside the ball-to-posts triangle takes away.
        ShotBlockShare: float
        /// Largest angle in radians between the carrier's body facing and the goal at which a shot is an option;
        /// beyond it the carrier has to turn first.
        ShotTurnLimit: float
        /// Lost keep chance per opponent standing in the carrier's path to goal.
        CarryBlockPenalty: float
        /// How far ahead, inside a cone toward goal, an opponent blocks a carry.
        CarryBlockRange: float<meter>
        /// Chance a cross by an average crosser (60) finds a teammate's head.
        CrossBaseSuccess: float
        /// Chance a met cross becomes a goal.
        HeaderConversion: float
        /// Share of the opponent's threat at the spot a clearance concedes.
        ClearConcession: float
        /// Chance a ball idle beside a nearby opponent is lost before the next action.
        LossNear: float
        /// Distance over which that chance fades.
        LossRange: float<meter>
        /// Share of that loss a teammate within support distance prevents.
        SupportRelief: float
        SupportDistance: float<meter>
        /// Goal probability per unit of the style terms (tactics, memory, personality, collective intent), which
        /// still nudge choices between options of similar value.
        StyleValue: float
        /// Softmax temperature in goal probability: options closer than this are chosen almost evenly.
        Temperature: float
        /// How far beyond the offside line a receiver must be before the passer sees it: nobody reads the line to
        /// the centimetre, and the runs inside that margin are where real offsides come from.
        OffsideReadMargin: float<meter>
        /// Style terms, in their own units and scaled by StyleValue: a defending phase's reluctance to pass, how
        /// teamwork, concentration and the receiver's consistency favour a pass, bravery and flair a shot, the
        /// side's width a cross, bravery a carry, and a deep line's reluctance to carry.
        StylePhasePenalty: float
        StyleTeamwork: float
        StyleConcentration: float
        StyleConsistency: float
        StyleShotBravery: float
        StyleFlair: float
        StyleCrossWidth: float
        StyleCarryBravery: float
        StyleCarryTempo: float
        /// Finishing assumed for a shot later in a planned chain, whose shooter is not settled yet.
        ChainFinishing: float
    }

/// Bounds individual continuation search independently of collective commitments.
type PlannerConfig =
    { Enabled: bool
      MaxChainLength: int
      MaxEvaluateLimit: int
      BeamWidth: int
      DribbleDuration: float<second>
      MinimumPassDistance: float<meter>
      ShootingRange: float<meter>
      PassTableResolution: float<meter>
      DeliberationTime: int<subtick> }

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
    {
        EvalTeammateWeight: float
        EvalOpponentWeight: float
        EvalGoalWeight: float
        EvalSearchRadius: float<meter>
        EvalCandidateCount: int
        RefineGroups: int
        EvalMinTeammateDist: float<meter>
        EvalOppOurWeight: float
        LateralSwapThreshold: float<meter>
        TargetInset: float<meter>
        CarrySearchRadius: float<meter>
        SupportForwardDistance: float<meter>
        /// Room left to the goal line under which a carrier's lateral carry always cuts inside, toward goal; 0 disables.
        CarryCutInsideDepth: float<meter>
        SupportLateralDistance: float<meter>
        /// Farthest a keeper stands off his line, reached only when play is deep in the other half.
        GkMaxAdvance: float<meter>
        /// Keeper depth per metre of ball distance: close balls pin him to his line, far ones let him sweep.
        GkDepthRatio: float
    }

/// RoboCup InterceptTable horizon: control radius and step budget.
[<Struct>]
type InterceptConfig =
    {
        ControlRadius: float<meter>
        MaxSteps: int
        /// Launch-speed grid step of the tabulated rolling ball used by races.
        RollTableResolution: float<meterPerSecond>
        /// Fastest launch speed the tabulated roll covers; faster balls are rolled step by step.
        RollTableMaxSpeed: float<meterPerSecond>
    }

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
/// A share per lineup role, in SlotRole order.
type QuickRoleWeights =
    { Keeper: float
      CentreBack: float
      Fullback: float
      Holding: float
      Central: float
      Playmaker: float
      Wide: float
      Forward: float }

/// The background match: a Poisson model of the engine's results, for fixtures outside the competitions the
/// user's club plays in. Every rate is fitted against full-engine matches (Training quick-fit), so a league
/// played this way has the engine's distributions without its ticks.
type QuickMatchConfig =
    {
        GoalsPerTeam: float
        HomeAdvantage: float
        /// Log change of a team's scoring rate per point of mean starter attribute (0..100) over its opponent.
        StrengthSensitivity: float
        AssistedShare: float
        ScorerByRole: QuickRoleWeights
        AssistByRole: QuickRoleWeights
        YellowsPerTeam: float
        RedsPerTeam: float
        FoulsPerTeam: float
        InjuriesPerTeam: float
        SubstitutionsPerTeam: float
        FirstSubstitutionMinute: int
        /// Foul and card share of a player with no aggression at all, so calm players are rarely but still booked.
        CalmFoulShare: float
        /// Injury share of a player with no proneness, so robust players can still be hurt.
        RobustInjuryShare: float
    }

type BalanceConfig =
    { Ball: BallConfig
      Movement: MovementConfig
      Gait: GaitConfig
      StrikeTechnique: StrikeTechniqueConfig
      Contact: ContactConfig
      ContactAction: ContactActionConfig
      Keeper: KeeperConfig
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
      ExpectedValue: ExpectedValueConfig
      CollectiveAttack: CollectiveAttackConfig
      Scout: ScoutConfig
      Counter: CounterConfig
      RivalManager: RivalManagerConfig
      Transfer: TransferConfig
      World: FootballEngine.Domain.WorldConfig
      TacticalTradeoffs: TacticalTradeoffConfig
      Training: TrainingConfig
      QuickMatch: QuickMatchConfig }

module BalanceConfig =

    let defaultCollectiveAttack: CollectiveAttackConfig =
        // Episodes last several deliveries, but stale geometry and unsupported runs remain costly.
        { Enabled = true
          Duration = 480<subtick>
          Recovery = 80<subtick>
          CounterHold = 60<subtick>
          LearningEnabled = true
          MinimumDutySeparation = 3.0<meter>
          PressureRadius = 8.0<meter>
          PressureCount = 2
          SupportDepth = 6.0<meter>
          SupportWidth = 8.0<meter>
          RunDepth = 13.0<meter>
          SwitchWidth = 24.0<meter>
          SecurityDepth = 9.0<meter>
          OffsideMargin = 1.5<meter>
          MaxArrival = 4.0<second>
          LaneRadius = 2.0<meter>
          SpaceScale = 12.0<meter>
          MinimumCondition = 0.3
          MinimumScore = 0.1
          LearningWeight = 0.45
          Exploration = 0.18
          PriorStrength = 3.0
          EvidenceCap = 48.0
          SeasonalRetention = 0.65
          CooperationWeight = 0.22
          ProgressReward = 0.4
          SpaceReward = 0.15
          ConnectionReward = 0.12
          ThreatReward = 0.5
          LossPenalty = 0.65
          IdlePenalty = 0.18
          ExposurePenalty = 0.15
          // The horizon covers a ~30 m ground pass at real pass speeds (~2.2 s), so the planner can chain the
          // switches and through balls players actually play.
          TerritoryBoundary = 20.0<meter>
          AnticipationHorizon = 2.5<second>
          AnticipationConfidence = 0.65 }

    let defaultDefensiveTransition: DefensiveTransitionConfig =
        // A short commitment tolerates geometry noise; depth danger still overrides it immediately.
        { ReevaluateTicks = 4<tickDelta>
          Commitment = 24<subtick>
          MaxPressDuration = 240<subtick>
          StabilizationHold = 24<subtick>
          BaseArrivalWindow = 1.2<second>
          PressArrivalExtension = 0.8<second>
          LocalArrivalWindow = 2.0<second>
          CoverArrivalWindow = 1.8<second>
          HandoffAdvantage = 0.25<second>
          MinimumPressIntensity = 0.3
          MinimumPressCondition = 0.55
          MinimumLocalPlayers = 3
          LocalSurplus = 1
          ApproachOffset = 0.75<meter>
          CarrierPrediction = 0.15<second>
          CoverDepth = 6.0<meter>
          BeatenMargin = 2.5<meter>
          EscapeProgress = 10.0<meter>
          ThreatGoalDistance = 28.0<meter>
          // The edge of the defensive third: where a low block's line of engagement sits.
          EngageGoalDistance = 35.0<meter>
          CentralHalfWidth = 18.0<meter>
          DepthSafety = 3.0<meter>
          ScreenFraction = 0.45
          RetreatBallGap = 8.0<meter>
          RetreatWidthScale = 0.6
          SettledTolerance = 3.0<meter>
          MaximumScreens = 2
          CoverEffort = 0.85
          ScreenEffort = 0.85
          RecoveryEffort = 1.0 }

    let defaultBall: BallConfig =
        { Gravity = 9.81<meterPerSecondSquared>
          AirDensity = 1.2
          Mass = 0.43
          // Wind-tunnel ranges for modern 32-or-fewer-panel balls: Cd ≈ 0.45 before the drag crisis, ≈ 0.2 after it,
          // with the transition around 12 m/s. The crisis is why driven shots hold their pace while lobs die.
          DragSubcritical = 0.45
          DragSupercritical = 0.2
          DragCrisisSpeed = 12.0<meterPerSecond>
          DragCrisisWidth = 1.5<meterPerSecond>
          // Measured lift grows roughly linearly with the spin parameter and saturates near 0.3–0.35.
          LiftSlope = 1.0
          MaxLift = 0.33
          // Aluminium goal frames return about two thirds of the normal speed; nets keep almost nothing.
          WoodworkRestitution = 0.65
          WoodworkFriction = 0.25
          NetRestitution = 0.08
          NetFriction = 0.6
          Radius = 0.11<meter>
          // A football is approximately a thin spherical shell, not a solid sphere.
          InertiaRatio = 2.0 / 3.0
          SlipFriction = 0.35
          // FIFA's ball-roll test (1 m ramp, ~3.7 m/s at the foot) must stop in 4-10 m on match turf, i.e. an
          // effective coefficient of ~0.07-0.18; 0.1 sits mid-range. 0.025 rolled the test ball 28 m.
          RollingResistance = 0.1
          FootReach = 1.05<meter>
          FootContactHeight = 0.45<meter>
          KickWindup = 6<subtick>
          KickRecovery = 12<subtick>
          CarryTouchInterval = 7<subtick>
          CarryOffset = 0.32<meter>
          CarryMaxSpeed = 9.0<meterPerSecond>
          CarryTechniqueError = 0.6
          // Ground passes leave the boot close to rolling; zero spin would lose substantial range while skidding.
          PassRollingSpinRatio = 0.975
          BounceRestitution = 0.6
          BounceFriction = 0.7
          StopSpeedCutoff = 0.06<meterPerSecond>
          MaxShotSpeed = 32.0<meterPerSecond>
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

    let defaultStrikeTechnique: StrikeTechniqueConfig =
        // A side-foot pass by an average passer (0.6) strays ~1.3 degrees, ~0.45 m over 20 m, a poor one (0.3) twice
        // that: technique has to separate passers, not only the boot. The instep trades precision for pace, and
        // the outside of the boot is the least precise but curls away from the body.
        { Inside =
            { Spread = 0.05
              SideSpinPerSpeed = 0.1 }
          Instep =
            { Spread = 0.08
              SideSpinPerSpeed = 0.0 }
          Outside =
            { Spread = 0.10
              SideSpinPerSpeed = 0.3 }
          // A side-foot pass beyond ~35 degrees toward the kicking foot's outer side needs the hips turned; a
          // player in stride flicks it with the outside instead.
          OutsideAngle = 0.6
          // Side-foot passes rarely exceed ~20 m/s; driven balls and clearances are struck with the laces.
          InstepSpeed = 20.0<meterPerSecond>
          TechniqueShare = 0.9
          // A challenger within ~2.5 m forces a hurried strike.
          PressureRadius = 2.5<meter>
          PressureSpreadPerOpponent = 0.4
          ComposureShield = 0.5
          OffBalanceSpread = 2.0 }

    let defaultGait: GaitConfig =
        // Running cadence is nearly flat, ~2.7 steps/s at 3 m/s to ~3.2 at 5.5, and climbs toward ~4 at a sprint;
        // the CMU locomotion corpus measures 1.4-1.8 cycles/s between 3 and 5 m/s, matching this within ~10 %.
        { StepFrequencyBase = 2.0
          StepFrequencyPerSpeed = 0.22
          StepFrequencyMax = 4.6
          ReferenceLegLength = 0.9<meter>
          // Below this the player shuffles in place; the phase holds so a restart does not begin mid-swing.
          StandingSpeed = 0.3<meterPerSecond>
          // Phase after the support foot's strike at which the swing foot passes under the hips. CMU runs at
          // 3-6 m/s keep the striking foot down for the first ~0.2 cycle; its middle is where a dribbler meets the
          // ball with the other foot planted.
          CarryTouchPhase = 0.08
          // Toe-off of the touching foot: CMU stance lasts ~0.2 cycle, so the foot lifts ~0.3 cycle before it
          // passes under the hips at mid-swing.
          CarrySwingLead = 0.3 }

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
          // Two bodies meet at ~0.5 m; starting the sidestep a stride earlier lets a defender leaving the corner
          // flag walk round the taker walking in, instead of the two pressing chest to chest until full time.
          SetPieceAvoidRadius = 1.5<meter>
          ConditionImpactThreshold = 0.1
          IntentFloor = 0.2
          IntentHysteresis = 0.05
          CommittedRunDuration = 240<subtick>
          // Close control caps a carrier near 5.5 m/s: tracking data puts dribbling well below a free sprint, a
          // touch every stride cannot be kept up at full speed, and a defender at full effort can stay in his path.
          CarryEffort = 0.15 }

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
          // glancing reception uncontrollable, so free balls pinballed for ~20% of match time. An average
          // technique (0.65) absorbs the 0.9 that achieved that.
          TrapAbsorptionBase = 0.75
          TrapAbsorptionSkill = 0.23
          // The poor share is squared: a technique of 0.3 returns ~0.2 of a head-on pass, a 15 m/s ball
          // leaves at ~3 m/s, above control speed, while 0.6 keeps it near 1.5 m/s.
          TrapPoorRestitution = 0.3
          OffBalanceTrapPenalty = 1.6
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
          // Two segments follow a curling ball as closely as four for every held-out statistic (48 matches) at 0.6 s less
          // per match.
          CurvedPathSegments = 2
          MaximumPathError = 0.01<meter>
          // The kicking leg and body still overlap the ball while it leaves; at even a soft 9 m/s pass it is
          // ~0.9 m clear after 4 ticks, so a later touch by the striker is a genuine second touch.
          StrikeSeparation = 4<subtick> }

    let defaultKeeper: KeeperConfig =
        // Elite keepers react to a strike in ~0.2-0.3 s and cover ~2.5 m of goal from the centre at full stretch.
        { ReactionBase = 0.34<second>
          ReactionSkill = 0.14<second>
          ArmReach = 0.75<meter>
          ShoulderHeightRatio = 0.82
          DiveSetup = 0.12<second>
          DiveSpeed = 4.2<meterPerSecond>
          MaxDive = 2.0<meter>
          HandRadius = 0.15<meter>
          // Calibrated so keepers stop roughly the top-flight share (~70 %) of shots on target; 0.3 m let 4 in 10 through,
          // and 0.2 m fell to ~66 % once open play stopped being cut short by frozen restarts.
          HandError = 0.16<meter>
          HandWindow = 3<subtick>
          // Driven shots above ~20 m/s are usually parried even by good handlers.
          CatchSpeed = 20.0<meterPerSecond>
          ParryRestitution = 0.25
          HoldSeconds = 2.0<second> }

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
          // A 0.85 m leg (~0.47 of stature) with the pelvis dropped to ~0.6 m in a lunge reaches ~0.6 m horizontally;
          // the striking surface sits ~0.1 m beyond the ankle. Further than this the body would have to step first.
          FootReach = 0.7<meter>
          // The full 0.95 m leg goes out flat while the hips drop and keep sliding, so the boot meets a ball well beyond
          // a standing lunge; ~1.5 m is what a defender closing from just out of standing range can still reach.
          SlidingReach = 1.5<meter>
          // Beyond a boot-length either side the ball is off the line he is running; he then re-solves the meet point.
          ReceptionTargetTolerance = 1.0<meter>
          // Boot (0.16 m envelope) plus the carrier's leg (0.12 m): the swept contact radius ContactDynamics tests.
          TackleBodyClearance = 0.28<meter>
          HeadReach = 0.28<meter>
          MaximumReorientation = 0.45<radian>
          ExecutionErrorScale = 0.14<meter>
          MaximumExecutionError = 0.65<meter>
          ShieldOffset = 0.34<meter>
          NewImpactThreshold = 0.72<meterPerSecond>
          CarelessRelativeSpeed = 1.2<meterPerSecond>
          // Read against the offender's own speed. A defender at a jog (3-4 m/s) who catches the leg is careless;
          // reckless needs the momentum of a committed run, and excessive force near top sprint (8.5 m/s) into the
          // leg, which keeps straight reds near the top-flight ~0.1 per match.
          RecklessRelativeSpeed = 5.0<meterPerSecond>
          ExcessiveRelativeSpeed = 7.5<meterPerSecond>
          DangerousBallFirstSpeed = 5.1<meterPerSecond> }

    let defaultActions: ActionsConfig =
        { // A twenty-five metre picture: what a player takes in with a scan before receiving.
          PerceptionRadius = 25.0<meter>
          // About two strides: a defender this close takes the ball before the next scheduled decision.
          ReflexDecisionRadius = 2.5<meter>
          PassBaseQuality = 0.7
          PassDistancePenaltyPerMeter = 0.004
          PassLongThreshold = 25.0<meter>
          PassVisionWeight = 0.3
          PassTechnicalWeight = 0.3
          PassJitterBase = 0.2
          PassHeavyTouchChance = 0.05
          // Professional ground passes are struck at ~12-20 m/s; a 20 m ball still arrives at ~11 m/s, firm
          // enough that defenders get little time to close it, while a 50 m ground ball is not hit at 35 m/s.
          PassArrivalSpeed = 9.0<meterPerSecond>
          PassArrivalSpeedGain = 0.12
          MaxPassExecutionDistance = 65.0<meter>
          ShotQualityGate = 0.5
          // An average finisher (0.6) keeps the 0.36 error that held ~36 % of shots on target; the skill share is
          // large so a poor finisher (0.3) sprays twice as wide as a good one (0.9), as conversion rates differ.
          ShotErrorBase = 0.14
          ShotErrorSkill = 0.5
          ShotVerticalErrorRatio = 0.6
          ShotPostMargin = 1.2<meter>
          ShotDistanceToGoalMultiplier = 0.02
          // A curled finish carries roughly 5–8 rev/s of sidespin.
          ShotCurlSpin = 40.0<radianPerSecond>
          // Driven top-flight shots reach ~28 m/s and placed side-foot finishes ~15 m/s. 0.45 brings saves to ~70 %
          // of shots on target; 0.35 left keepers stopping under 60 %.
          ShotSpeedBase = 20.0<meterPerSecond>
          ShotSpeedStrength = 9.0<meterPerSecond>
          ShotPlacementSpeedCost = 0.45
          // Crosses are aimed to meet the receiver between knee and chest, where both feet and head can play them.
          CrossArrivalHeight = 0.9<meter>
          DuelSteepness = 8.0
          DuelMomentumBonus = 0.1
          DuelFoulSeverityBias = 0.2
          CrossLateralnessMin = 0.3
          CrossTargetRadius = 12.0<meter>
          GkPunchChance = 0.15
          GkParryChance = 0.1
          GkDistributionPressureRadius = 15.0<meter>
          DribbleBaseSuccess = 0.375
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
          // A step behind the ball, well inside foot reach: a taker waiting a full metre back sat on the edge of
          // reach, was authorized, and had every strike cancelled for being out of reach.
          CornerTakerOffset = 0.6<meter>
          // A body plus leg meets the ball up to ~1 m away; a teammate left standing on the spot touched the ball
          // first, cancelled the kick and restarted the same free kick hundreds of times.
          BallClearance = 2.0<meter>
          TargetTolerance = 1.0<meter>
          MaxTargetDistance = 70.0<meter>
          ThrowInReceiverInset = 3.0<meter> }

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
          // Outfielders re-read the local spacing one group per update, about every 0.2 s each: closer to how
          // often a person re-evaluates where to stand than the whole team ten times a second, at half the cost.
          RefineGroups = 2
          EvalMinTeammateDist = 5.0<meter>
          EvalOppOurWeight = 0.25
          LateralSwapThreshold = 1.5<meter>
          TargetInset = 2.0<meter>
          CarrySearchRadius = 7.0<meter>
          SupportForwardDistance = 4.0<meter>
          // Running outward along the byline took the carrier away from goal and he shot with his back to it. At
          // 4 m, the whole backward-step zone, every byline carry ended in the six yard box (6.5 goals a match);
          // 3 m keeps shots and goals in their bands on held-out seeds.
          CarryCutInsideDepth = 3.0<meter>
          SupportLateralDistance = 8.0<meter>
          GkMaxAdvance = 18.0<meter>
          // A ball at the box edge (~20 m) leaves the keeper ~4 m out; at the halfway line ~11 m.
          GkDepthRatio = 0.2 }

    let defaultIntercept: InterceptConfig =
        { ControlRadius = 2.5<meter>
          MaxSteps = 50
          // Linear interpolation in launch speed over the smooth roll curve errs ~6e-7 m at 0.01 m/s, far below
          // the 2.5 m control radius, and the table stays ~2 MB.
          RollTableResolution = 0.01<meterPerSecond>
          // Covers realistic ground launches; a faster ball is stepped exactly, so the cap costs speed, never correctness.
          RollTableMaxSpeed = 45.0<meterPerSecond> }

    let defaultSituation: SituationConfig = { LeadSteps = 2 }

    let defaultPassIntercept: PassInterceptConfig =
        { BaseRisk = 0; OffsideCrossRisk = 2 }

    let defaultReferee: RefereeConfig =
        // Top-flight referees book roughly one foul in six; nearly all of those are careless fouls stopping attacks.
        { FoulMinorChance = 0.0
          // Generated squads commit ~27 tactical fouls a match; 0.05 books ~1.4 of them, cards ~8 with the rest.
          FoulTacticalChance = 0.05
          FoulYellowWorthyChance = 0.85
          FoulRedWorthyChance = 0.7
          SecondYellowSuspension = true
          VarGoalReviewDelay = 15.0<second>
          VarOffsideMargin = 0.1<meter>
          AdvantageWindow = 80<subtick>
          HomeCardBonus = 0.05
          CardAccumulationThreshold = 4
          // Added to the booking chance when the foul stops the last defender's man near goal. The test is a
          // proximity heuristic that also catches harmless fouls, so at 0.45 it booked ~3 per match on its own.
          LastManFactor = 0.25
          // Without it second yellows ran at ~2.2 dismissals a match (real top flight ~0.15): fouls concentrate on
          // the same deepest defenders, and every careless one risked the second caution.
          BookedLeniency = 0.1
          // Measured on the corpus: 14 of 23 remaining dismissals followed a speed-judged reckless foul by a booked player;
          // 0.2 brings dismissals to ~0.38 a match, about 0.15 of them straight reds.
          BookedRecklessLeniency = 0.2
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
          WinningDefensiveMinute = 80<matchMin>
          ConditionThresholdWinning = 0.55
          ConditionThresholdDrawing = 0.65
          ConditionThresholdLosing = 0.75 }

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
          ShootoutRounds = 5
          // About 45 s of play at the usual event rate: long enough for a pattern, short enough to change.
          EventWindowCapacity = 1800 }

    let defaultExpectedValue: ExpectedValueConfig =
        { // Above the believed xG (MatchMath.xGFrom) beyond ~13 m and twice it at 30 m: a possession there can
          // still become a better chance, so a shot from range is taken only when keeping the ball is unlikely.
          ThreatPeak = 1.0
          ThreatDecay = 20.0<meter>
          // About 1.4 goals from ~100 possessions a team: a ball anywhere is worth something.
          ThreatFloor = 0.012
          // A 0.25 s margin is still only ~73 % safe: the race assumes the receiver's touch and the interceptor's
          // read, and real passes into contested space fail about one time in five.
          PassMarginScale = 10.0
          PassSkillSteps = 6.0
          PassMinimumMargin = -6
          CarryAdvance = 5.0<meter>
          CarryKeepBase = 0.95
          // Zero: the duel itself already reads Dribbling against Tackling. Adding it to the belief made a skilled
          // side hold the ball on longer than the duel pays and turned +20 in either attribute into a losing side.
          CarrySkillWeight = 0.0
          CarryPressurePenalty = 0.12
          PressureRadius = 4.0<meter>
          // Two bodies in the lane leave a shot about a third of its open chance; the engine blocks that often.
          ShotBlockShare = 0.4
          // About 100 degrees: a striker can open the hips across the body, but nobody shoots at a goal behind him.
          ShotTurnLimit = 1.75
          // A settled block of four or five in front leaves a carry little chance; space ahead is what a carry needs.
          CarryBlockPenalty = 0.08
          CarryBlockRange = 15.0<meter>
          // Roughly one cross in four is met; a met cross scores about one time in eight.
          CrossBaseSuccess = 0.25
          HeaderConversion = 0.12
          ClearConcession = 0.3
          LossNear = 1.0
          LossRange = 4.0<meter>
          SupportRelief = 0.3
          SupportDistance = 10.0<meter>
          // The old utilities spanned ~1 for options a few hundredths of a goal apart.
          StyleValue = 0.02
          Temperature = 0.0005
          StylePhasePenalty = -0.03
          StyleTeamwork = 0.45
          StyleConcentration = 0.08
          StyleConsistency = 0.3
          StyleShotBravery = 0.12
          StyleFlair = 0.6
          StyleCrossWidth = 0.25
          StyleCarryBravery = 0.15
          StyleCarryTempo = 0.2
          // About a stride: assistant referees flag runners a stride off that the passer played as level.
          OffsideReadMargin = 1.0<meter>
          ChainFinishing = 60.0 }

    let defaultPlanner: PlannerConfig =
        { Enabled = true
          MaxChainLength = 4
          MaxEvaluateLimit = 500
          BeamWidth = 3
          DribbleDuration = 0.5<second>
          MinimumPassDistance = 0.5<meter>
          ShootingRange = 32.0<meter>
          // The planner reads candidate passes from solutions sampled this far apart instead of solving each one:
          // at 5 cm the interpolated flight stays within a millisecond of the exact roll.
          PassTableResolution = 0.05<meter>
          // Zero chooses on the tick the decision begins.
          DeliberationTime = 4<subtick> }

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

    let private noShift: FootballEngine.Domain.TacticalInstructions =
        { Mentality = 0
          DefensiveLine = 0
          PressingIntensity = 0
          Width = 0
          Tempo = 0
          Directness = 0
          PressTriggerZone = 0
          DefensiveShape = 0 }

    let defaultRivalManager: RivalManagerConfig =
        { StudyWindow = 10
          // A coach with PA 180 reacts to a winning recipe after about two matches, one with PA 80 after five.
          StudyRate = 0.3
          // A recipe that earns one point a game or less is not worth answering.
          ThreatPpgFloor = 1.0
          // Squads differ by tens of CA points; a coach who reads poorly can be a fifth of a gap off.
          StrengthNoise = 12.0
          UnderdogGap = 25.0
          CounterThreatMin = 0.3
          CopyImitationMin = 12
          CopySuitabilityMin = 0.9
          RevengePrideMin = 13
          // A three-goal defeat is the line between a bad day and an insult.
          HumiliationMargin = 3
          FormationSuitabilityMin = 0.92
          CounterMaxSteps = 2.5
          CounterFormationIntensity = 0.5
          // A third per corridor is a balanced side; 0.42 in the middle is a clear lean.
          CentreHeavyShare = 0.42
          WideHeavyShare = 0.62
          DirectLongPassShare = 0.22
          PossessionShortPassShare = 0.55
          PressAnswerBoldness = 11
          RevengeShift =
            { noShift with
                Mentality = 1
                PressingIntensity = 1 }
          UnderdogCautiousShift =
            { noShift with
                Mentality = -1
                DefensiveLine = -1
                Tempo = 1
                Directness = 1
                PressTriggerZone = -1 }
          UnderdogBoldShift =
            { noShift with
                DefensiveLine = 1
                PressingIntensity = 2
                PressTriggerZone = 1 }
          UnderdogBoldnessPivot = 12
          // A starter at 55% condition loses about a third of his score to a coach who reads perfectly.
          RotationWeight = 0.8
          PossessionPreset =
            { PreferredFormation = "F433"
              Values =
                { Mentality = 3
                  DefensiveLine = 3
                  PressingIntensity = 2
                  Width = 2
                  Tempo = 1
                  Directness = 0
                  PressTriggerZone = 1
                  DefensiveShape = 2 } }
          PressingPreset =
            { PreferredFormation = "F4231"
              Values =
                { Mentality = 3
                  DefensiveLine = 3
                  PressingIntensity = 4
                  Width = 2
                  Tempo = 3
                  Directness = 2
                  PressTriggerZone = 3
                  DefensiveShape = 2 } }
          CounterPreset =
            { PreferredFormation = "F4141"
              Values =
                { Mentality = 1
                  DefensiveLine = 1
                  PressingIntensity = 1
                  Width = 2
                  Tempo = 3
                  Directness = 3
                  PressTriggerZone = 0
                  DefensiveShape = 2 } }
          DirectPreset =
            { PreferredFormation = "F442"
              Values =
                { Mentality = 2
                  DefensiveLine = 2
                  PressingIntensity = 2
                  Width = 3
                  Tempo = 3
                  Directness = 4
                  PressTriggerZone = 1
                  DefensiveShape = 2 } }
          FortressPreset =
            { PreferredFormation = "F541"
              Values =
                { Mentality = 0
                  DefensiveLine = 0
                  PressingIntensity = 1
                  Width = 1
                  Tempo = 1
                  Directness = 3
                  PressTriggerZone = 0
                  DefensiveShape = 1 } }
          ReviewIntervalMin = 5
          MaxReactionDelayMin = 6.0
          TemperamentHaste = 0.6
          MinMinutesBetweenMoves = 8
          HotheadMinutesBetweenMoves = 3
          ReadNoise = 0.5
          // About a quarter-hour of chances at the league rate: one shot in a quiet spell stays under the storm line.
          MomentumEpsilon = 0.35
          // A spell of pressure, not the last chance: half the memory fades in twelve minutes.
          MomentumHalfLifeMin = 12.0
          ReadSettleMinute = 15
          // Measured on the corpus: at 0.3 over five-minute windows coaches flipped storm/back-to-plan six times a match.
          StormMomentum = 0.45
          InitiativeMomentum = 0.45
          ChaseMinuteBold = 55
          ChaseMinuteCautious = 78
          AllOutMinute = 82
          AllOutBoldness = 0.7
          ProtectMinuteCautious = 65
          ProtectMinuteBold = 80
          ParkBusBoldness = 0.25
          InitiativeBoldness = 0.6
          SmartChangeReadingMin = 0.6
          CardRiskMinute = 60
          // Temperament 15 of 20 is also where the archetype turns Hothead.
          HotheadTemperament = 0.75
          HotheadHalfTimeChanges = 2
          ChaseMentalityShift = 0.25
          AllOutMentalityShift = 0.5
          ProtectMentalityDrop = 0.25
          StormMentalityDrop = 0.2
          InitiativeMentalityShift = 0.2
          RedCardMentalityDrop = 0.25
          AttackLineShift = 6.0
          DefendLineShift = 6.0
          // The best coach against the worst (0.75 of reading apart) gains 3 attribute points: a ~5% scoring edge.
          CoachStrengthPoints = 4.0
          CoachPeakEndAge = 58
          CoachGrowthRate = 0.25
          CoachDeclinePerYear = 2
          CoachSkillFloor = 50
          RivalMemoryYears = 5 }

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

    /// Real-league priors until `Training quick-fit` can be trusted: on 2026-11-12 the engine showed no gain
    /// from stronger lineups and the away side outscoring the home side, so a fit would carry both into every
    /// background league. Refit once the engine rewards quality.
    let defaultQuickMatch: QuickMatchConfig =
        { // The engine's ~3 goals a match (`quick-fit 6`, 2026-11-13), split so the home advantage below moves
          // goals between the sides instead of adding them: 3 / (1 + e^0.25).
          GoalsPerTeam = 1.31
          // The real ~1.3x home scoring rate. The engine fits ~0 because it has no home advantage yet; keeping the
          // real value here is deliberate until the engine has one, so background tables keep the real pattern.
          HomeAdvantage = 0.25
          // Fitted: a starting eleven 16 attribute points better scores ~1.3 times as often.
          StrengthSensitivity = 0.016
          AssistedShare = 0.7
          ScorerByRole =
            { Keeper = 0.0
              CentreBack = 0.08
              Fullback = 0.08
              Holding = 0.16
              Central = 0.33
              Playmaker = 0.49
              Wide = 0.2
              Forward = 1.0 }
          AssistByRole =
            { Keeper = 0.02
              CentreBack = 0.1
              Fullback = 0.3
              Holding = 0.3
              Central = 0.5
              Playmaker = 0.8
              Wide = 0.7
              Forward = 0.5 }
          // Fitted to the engine; reds keep the real prior, the fit's corpus over-counts them against `validate`.
          YellowsPerTeam = 3.35
          RedsPerTeam = 0.06
          FoulsPerTeam = 13.6
          InjuriesPerTeam = 0.15
          SubstitutionsPerTeam = 4.0
          FirstSubstitutionMinute = 55
          // A calm player commits about a sixth of the fouls of the most aggressive one.
          CalmFoulShare = 0.2
          RobustInjuryShare = 0.2 }

    let defaultConfig: BalanceConfig =
        { Ball = defaultBall
          Movement = defaultMovement
          Gait = defaultGait
          StrikeTechnique = defaultStrikeTechnique
          Contact = defaultContact
          ContactAction = defaultContactAction
          Keeper = defaultKeeper
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
          ExpectedValue = defaultExpectedValue
          CollectiveAttack = defaultCollectiveAttack
          Scout = defaultScout
          Counter = defaultCounter
          RivalManager = defaultRivalManager
          Transfer = defaultTransfer
          World = FootballEngine.Domain.WorldConfig.defaults
          TacticalTradeoffs = defaultTacticalTradeoffs
          Training = defaultTraining
          QuickMatch = defaultQuickMatch }
