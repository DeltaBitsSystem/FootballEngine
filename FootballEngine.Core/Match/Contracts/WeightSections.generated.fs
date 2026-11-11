namespace FootballEngine.Core.Match

/// ML-tunable weights ONLY — mirrors weights.json section names and field names 1:1.
/// Nested objects and arrays are preserved as nested records and float[].

[<Struct>]
type WeightSectionsDuel =
    { DuelSteepness: float
      MomentumBonus: float
      JitterWin: int
      JitterRecover: int
      JitterKeep: float
      SpeedKeep: int
      SpeedKeepVz: float
      AttackerDribblingWeight: float
      AttackerAgilityWeight: float
      AttackerBalanceWeight: float
      DefenderTacklingWeight: float
      DefenderStrengthWeight: float
      DefenderPositionWeight: float
      FatigueThreshold: int
      FatigueDecay: float }

[<Struct>]
type WeightSectionsShot =
    { QualityGate: float
      AngleSpreadBase: float
      VzBase: int
      VzVariance: float
      NormalisationDistance: int
      DistanceToGoalMultiplier: float
      FinishingMin: float
      FinishingMax: int
      FinishingBonusST: int
      FinishingBonusAM: int
      FinishingBonusMC: float
      FinishingBonusOther: float
      CondFactorDivisor: int
      ComposureMultiplier: float
      UrgencyMultiplier: float
      BasePowerDivisor: int
      SpinTopMultiplier: float
      PositionDirectnessWeight: float
      PositionDepthWeight: float
      PositionCreativityWeight: float
      DistNormWeight: float
      PositionBonusWeight: float
      HeavyTouchDivisor: int
      HeavyTouchMultiplier: float
      JitterStdDev: int
      GkReflexesStatMult: float
      GkOneOnOneStatMult: int
      SaveDenominatorOffset: int
      ShotWideMargin: int }

[<Struct>]
type WeightSectionsPass =
    { BaseMean: float
      DistancePenaltyPerMeter: float
      LongPassPenaltyPerMeter: float
      TechnicalWeight: float
      VisionWeight: float
      SuccessShapeAlpha: int
      SuccessConditionMultiplier: int
      OffsideMomentum: float
      SuccessMomentum: float
      FailMomentum: float
      DeflectBaseRate: float
      MisplacedBaseRate: float
      InterceptBaseRate: float
      InterceptionRadius: int
      PressureDistance: int
      DeflectPressureMultiplier: float
      InterceptPaceWeight: float
      InterceptPositioningWeight: float
      ScrambleJitter: int
      Speed: int
      Vz: float
      InterceptDistFactorWeight: float
      InterceptPositioningContrib: float
      InterceptVisionContrib: float
      CreativityWeight: float
      DirectnessWeight: float
      MeanMin: float
      MeanMax: float
      DefaultNearestDefDist: int
      DefaultTackling: float
      HeavyTouchDivisor: int
      HeavyTouchMultiplier: float
      JitterStdDev: int
      DeflectedSpeedMult: float
      DeflectedVzMult: float
      InterceptProbMax: float
      MisplacedSpeedMult: float
      LongBallBaseMean: float
      LongBallLongShotsWeight: float
      LongBallPassingWeight: float
      LongBallVisionWeight: float
      LongBallSuccessShapeAlpha: int
      LongBallSuccessConditionMultiplier: int
      LongBallOffsideMomentum: float
      LongBallSuccessMomentum: float
      LongBallFailMomentum: float
      LongBallSpeed: int
      LongBallVz: int
      LongBallDeflectMult: float
      LongBallInterceptMult: float
      LongBallPressureContrib: float
      ForwardDepthThreshold: float
      ForwardCreativityThreshold: float
      LongBallScrambleJitterMult: int
      PassLeadFactor: float }

[<Struct>]
type WeightSectionsCross =
    { BaseMean: float
      CrossingWeight: float
      PassingWeight: float
      SuccessShapeAlpha: int
      SuccessConditionMultiplier: int
      HeaderDuelSteepness: float
      HeaderAccuracyBase: float
      HeaderAccuracySkillMult: float
      GkSaveBase: float
      GkReflexesMult: float
      GkAerialReachMult: float
      GkJumpMult: float
      ClaimCrossProbability: float
      FailMomentum: float
      Speed: int
      Vz: int
      AerialThreatThreshold: float
      AttackingDepthThreshold: float
      GkSkillDefault: int
      GkSkillDivisor: int
      SpinTopMult: float
      SpinSideMult: float
      FallbackSpeed: int
      FallbackVz: int }

[<Struct>]
type WeightSectionsDribble =
    { TechnicalWeight: float
      AgilityWeight: float
      BalanceWeight: float
      ForwardDistance: int
      SuccessMomentum: float
      FailMomentum: float
      CrossProbability: float
      PassProbability: float
      ShotProbability: float }

[<Struct>]
type WeightSectionsTackle =
    { TechnicalWeight: float
      PositioningWeight: float
      StrengthWeight: float
      AggressionWeight: float
      PositioningReduction: float
      FoulShapeBeta: int
      FoulMomentum: float
      SuccessMomentum: float
      FailMomentum: float
      TackleSteepness: float }

[<Struct>]
type WeightSectionsSetPiece =
    { FreeKickTargetX: float
      FreeKickSpeed: int
      FreeKickVz: float
      FreeKickSteepness: int
      FreeKickSavePowerThreshold: float
      FreeKickSaveVariance: float
      FreeKickSpinTopMult: float
      FreeKickSpinSideMult: float
      CornerBoxXThreshold: float
      CornerDefenderBoxThreshold: float
      CornerSecondPhaseProbability: float
      CornerKeepPossessionProbability: float
      CornerSpeed: int
      CornerVz: int
      CornerDensityBase: int
      CornerDensityPenalty: float
      CornerLogisticSteepness: float
      CornerDefScoreDefault: float
      ThrowInSpeed: int
      ThrowInVz: float
      ThrowInMomentum: float
      PenaltySkillMultiplier: float
      PenaltyMoraleMultiplier: float
      PenaltyPressureMultiplier: float
      PenaltyComposureNoise: float
      PenaltyLogisticBase: float
      PenaltyGkReflexesMult: float
      PenaltyGkHandlingMult: int
      PenaltySkillDivisor: int
      PenaltyCondDivisor: int
      PenaltyMoraleBase: float
      PenaltyMoraleDivisor: float
      PenaltyGkSkillDivisor: int
      PenaltySpeed: int
      PenaltyVzBase: float
      PenaltyVzVariance: float
      PenaltyAngleSpread: float
      PostShotClearProbability: float
      ClearSpeed: int
      ClearVz: float
      ClearYStdDev: int
      GoalKickFallbackDistHome: int
      GoalKickFallbackDistAway: int
      KickOffPartnerOffsetX: int
      KickOffPartnerOffsetY: int
      FoulBaseRate: float
      CornerOnFailedCross: float }

[<Struct>]
type WeightSectionsGK =
    { CatchHandlingMult: float
      DiveReach: float
      ParrySpeed: int
      ParryDeflectionAngle: float
      AerialReachMult: float
      JumpReachMult: float
      PunchProbability: float
      ClaimCrossProbability: float
      CollectionRadius: int
      CollectionPriority: int
      ThrowSpeed: int
      RollSpeed: int
      GoalKickSpeed: int
      PuntSpeed: int
      DistributionAccuracyMult: int
      DistributionDecisionNoise: float
      HoldTimeSubTicks: int
      MaxHoldSubTicks: int
      BackPassHandlingPenalty: float
      GKDecisionWindowSubTicks: int }

[<Struct>]
type WeightSectionsHomeAdvantage =
    { Strength: int
      DuelAttackBonus: int
      DuelDefenseBonus: int
      ShotComposureBonus: int
      PassAccuracyBonus: float
      DribbleBonus: int
      SetPlayAccuracyBonus: float
      TackleBonus: int
      FreeKickComposure: int
      PenaltyBonus: float
      CardReduction: float
      FatigueReduction: float }

[<Struct>]
type WeightSectionsPhysics =
    { Gravity: float
      AirDrag: float
      GroundRestitution: float
      GroundFriction: float
      PostRestitution: float
      SpinDecay: float
      MagnusCoeff: float
      ContactRadius: float
      PlayerMaxForce: int
      PlayerMassBase: int
      PlayerMassWeightCoeff: float
      PlayerMassStrengthCoeff: float
      SteeringSlowRadius: float
      SteeringFleeRadius: int
      SteeringAlignmentWeight: float
      CohesionWeight: float
      TurnConstraintAgilityCoeff: float
      TurnConstraintBaseLimit: float
      MoveSpeedMax: float
      MoveSpeedMin: float
      SeparationMinDistance: float
      BallContestSeparationRadius: float
      SeparationStrength: float
      SeparationAgilityMultiplier: float
      JitterBase: float
      JitterAgilityMultiplier: float
      BallStopThreshold: float
      AirborneCheckThreshold: float
      AxisStopThreshold: float
      AirborneRestitutionBase: float
      AirborneRestitutionCoeff: float
      AirborneRestitutionFloor: float
      GroundRestitutionBase: float
      GroundRestitutionCoeff: float
      GroundRestitutionFloor: float
      ChaserProximity: int
      AirborneThreshold: float
      ArrivalAnticipationBase: float
      ArrivalAnticipationQuality: float
      ArrivalCompetitionRadius: int
      ArrivalConvergenceThreshold: int
      ArrivalContestThreshold: float
      ReceivingGraceSubTicks: int }

[<Struct>]
type WeightSectionsTimingDuelChainDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingDuelNextDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingShotDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingFoulDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingGoalDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingKickOffDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingCornerDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingFreeKickDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingThrowInDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingGoalKickDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingInjuryDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingManagerReactDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTimingSubsDelay =
    { MeanST: int
      StdST: int
      MinST: int
      MaxST: int }

[<Struct>]
type WeightSectionsTiming =
    { DuelChainDelay: WeightSectionsTimingDuelChainDelay
      DuelNextDelay: WeightSectionsTimingDuelNextDelay
      ShotDelay: WeightSectionsTimingShotDelay
      FoulDelay: WeightSectionsTimingFoulDelay
      GoalDelay: WeightSectionsTimingGoalDelay
      KickOffDelay: WeightSectionsTimingKickOffDelay
      CornerDelay: WeightSectionsTimingCornerDelay
      FreeKickDelay: WeightSectionsTimingFreeKickDelay
      ThrowInDelay: WeightSectionsTimingThrowInDelay
      GoalKickDelay: WeightSectionsTimingGoalKickDelay
      InjuryDelay: WeightSectionsTimingInjuryDelay
      ManagerReactDelay: WeightSectionsTimingManagerReactDelay
      SubsDelay: WeightSectionsTimingSubsDelay
      StuckBallDelay: int
      EventWindowSubTicks: int }

[<Struct>]
type WeightSectionsMatchVolume =
    { MaxChainLength: int
      TargetDuelTicksPerMatch: int
      TargetShotsPerMatch: int
      TargetDribblesPerMatch: int
      TargetPassesPerMatch: int
      TargetCrossesPerMatch: int
      TargetLongBallsPerMatch: int }

[<Struct>]
type WeightSectionsManager =
    { FatigueReactionThreshold: int
      SustainedMomentumSubTicks: int
      MomentumThreshold: int
      FatigueCheckSubTicks: int
      ConditionThresholdLosing: int
      ConditionThresholdDrawing: int
      ConditionThresholdWinning: int
      SubWindowMinutes: float[] }

[<Struct>]
type WeightSectionsBuildUp =
    { PassSuccessBonus: float
      DribblePenalty: float
      LongBallPenalty: float
      GKDistributionBonus: float
      DCPassingBonus: float }

[<Struct>]
type WeightSectionsDecision =
    { ShootFinishingWeight: float
      ShootLongShotsWeight: float
      ShootComposureWeight: float
      ShootDistNormWeight: float
      ShootDistNormDivisor: int
      ShootPosDirectnessWeight: float
      ShootPosDepthWeight: float
      ShootSTBonus: float
      ShootDistPenaltyDivisor: int
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
      CrossCrossingWeight: float
      CrossLateralTendencyWeight: float
      CrossLateralTendencyBase: float
      CrossZoneBonus: float
      CrossWidthBonus: float
      LongBallPassingWeight: float
      LongBallVisionWeight: float
      LongBallPressDistBase: int
      LongBallPressMin: float
      LongBallPressMax: int
      LongBallPressNoOpponent: float
      LongBallAttackPhaseBonus: float
      LongBallDirectnessBonus: float
      CreativityWeight: float
      DirectnessWeight: float
      DecisionTemperature: float
      ShootMinThreshold: float
      ShootDirectnessThresholdMod: float
      PassMinThreshold: float
      PassDirectnessThresholdMod: float
      DribbleMinThreshold: float
      DribbleDefZoneDirectnessMin: float
      DribbleHighTempo: float
      DribbleHighTempoDirectnessMin: float
      LongBallMinThreshold: float
      LongBallDirectnessThresholdMod: float
      SpacePassMinScore: float
      ShootDecisionsWeight: float
      ShootBraveryMod: float
      ShootConcentrationMod: float
      PassDecisionsWeight: float
      PassConcentrationMod: float
      DribbleBraveryMod: float
      ShootXGWeight: float
      PassHighPressPenalty: float
      DribbleAggressivePenalty: float
      LongBallSlowTargetBonus: float
      PassTrajectoryBonus: float
      PersonalityFlairWeight: float
      PersonalityTeamworkWeight: float
      PersonalityConsistencyMod: float
      ShootBraveryPressureMod: float
      PassAnticipationBonus: float
      PassBaseUtility: float
      ShotBaseUtility: float
      DribbleBaseUtility: float
      CrossBaseUtility: float
      ShotXGMultiplier: float
      ShotDepthBonusAttacking: float
      ShotDepthBonusDefending: float
      DribbleZoneAttackingMult: float
      DribblePhaseBonusMult: float
      PassHighPressFactor: float }

[<Struct>]
type WeightSectionsPerception =
    { VisionRadiusBase: int
      VisionRadiusMax: int
      VisionConeAngle: float
      PeripheralMultiplier: float
      MinimumAwarenessFloor: int
      AnticipationBonusRadius: int
      GoalkeeperConeAngle: float
      CommunicationRange: int
      SetPieceSimplifiedRadius: int
      BlindPassVisionThreshold: int
      BlindPassComposureThreshold: int
      BlindPassSuccessPenalty: float }

[<Struct>]
type WeightSectionsPlanner =
    { ProgressionWeight: float
      ShootValueWeight: float
      ShootFinishingAssumption: float
      DangerBase: float[]
      DangerZoneWeights: float[]
      PossessionSecurityWeight: float }

type WeightSections =
    { Duel: WeightSectionsDuel
      Shot: WeightSectionsShot
      Pass: WeightSectionsPass
      Cross: WeightSectionsCross
      Dribble: WeightSectionsDribble
      Tackle: WeightSectionsTackle
      SetPiece: WeightSectionsSetPiece
      GK: WeightSectionsGK
      HomeAdvantage: WeightSectionsHomeAdvantage
      Physics: WeightSectionsPhysics
      Timing: WeightSectionsTiming
      MatchVolume: WeightSectionsMatchVolume
      Manager: WeightSectionsManager
      BuildUp: WeightSectionsBuildUp
      Decision: WeightSectionsDecision
      Perception: WeightSectionsPerception
      Planner: WeightSectionsPlanner }

module WeightSections =

    let defaultWeightsDuel: WeightSectionsDuel =
        { DuelSteepness = 1.2
          MomentumBonus = 0.5
          JitterWin = 8
          JitterRecover = 2
          JitterKeep = 2.5
          SpeedKeep = 3
          SpeedKeepVz = 0.2
          AttackerDribblingWeight = 0.5
          AttackerAgilityWeight = 0.3
          AttackerBalanceWeight = 0.2
          DefenderTacklingWeight = 0.5
          DefenderStrengthWeight = 0.3
          DefenderPositionWeight = 0.2
          FatigueThreshold = 50
          FatigueDecay = 0.04 }

    let defaultWeightsShot: WeightSectionsShot =
        { QualityGate = 0.35
          AngleSpreadBase = 0.15
          VzBase = 5
          VzVariance = 1.5
          NormalisationDistance = 30
          DistanceToGoalMultiplier = 0.15
          FinishingMin = 0.2
          FinishingMax = 1
          FinishingBonusST = 2
          FinishingBonusAM = 2
          FinishingBonusMC = 1.2
          FinishingBonusOther = 0.5
          CondFactorDivisor = 100
          ComposureMultiplier = 0.1
          UrgencyMultiplier = 0.05
          BasePowerDivisor = 20
          SpinTopMultiplier = 0.4
          PositionDirectnessWeight = 0.3
          PositionDepthWeight = 0.2
          PositionCreativityWeight = 0.1
          DistNormWeight = 0.7
          PositionBonusWeight = 0.3
          HeavyTouchDivisor = 20
          HeavyTouchMultiplier = 0.25
          JitterStdDev = 2
          GkReflexesStatMult = 3.5
          GkOneOnOneStatMult = 5
          SaveDenominatorOffset = 3
          ShotWideMargin = 3 }

    let defaultWeightsPass: WeightSectionsPass =
        { BaseMean = 0.65
          DistancePenaltyPerMeter = 0.012
          LongPassPenaltyPerMeter = 0.02
          TechnicalWeight = 0.15
          VisionWeight = 0.15
          SuccessShapeAlpha = 10
          SuccessConditionMultiplier = 6
          OffsideMomentum = 0.3
          SuccessMomentum = 0.3
          FailMomentum = 0.5
          DeflectBaseRate = 0.06
          MisplacedBaseRate = 0.03
          InterceptBaseRate = 0.05
          InterceptionRadius = 5
          PressureDistance = 8
          DeflectPressureMultiplier = 0.12
          InterceptPaceWeight = 0.35
          InterceptPositioningWeight = 0.45
          ScrambleJitter = 3
          Speed = 16
          Vz = 0.5
          InterceptDistFactorWeight = 0.3
          InterceptPositioningContrib = 0.15
          InterceptVisionContrib = 0.1
          CreativityWeight = 0.06
          DirectnessWeight = 0.06
          MeanMin = 0.01
          MeanMax = 0.99
          DefaultNearestDefDist = 10
          DefaultTackling = 0.5
          HeavyTouchDivisor = 20
          HeavyTouchMultiplier = 0.25
          JitterStdDev = 2
          DeflectedSpeedMult = 0.6
          DeflectedVzMult = 0.5
          InterceptProbMax = 0.8
          MisplacedSpeedMult = 0.7
          LongBallBaseMean = 0.4
          LongBallLongShotsWeight = 0.2
          LongBallPassingWeight = 0.2
          LongBallVisionWeight = 0.15
          LongBallSuccessShapeAlpha = 5
          LongBallSuccessConditionMultiplier = 10
          LongBallOffsideMomentum = 0.4
          LongBallSuccessMomentum = 0.5
          LongBallFailMomentum = 0.4
          LongBallSpeed = 26
          LongBallVz = 5
          LongBallDeflectMult = 1.5
          LongBallInterceptMult = 1.5
          LongBallPressureContrib = 0.3
          ForwardDepthThreshold = 0.5
          ForwardCreativityThreshold = 0.4
          LongBallScrambleJitterMult = 2
          PassLeadFactor = 0.3 }

    let defaultWeightsCross: WeightSectionsCross =
        { BaseMean = 0.5
          CrossingWeight = 0.25
          PassingWeight = 0.1
          SuccessShapeAlpha = 6
          SuccessConditionMultiplier = 8
          HeaderDuelSteepness = 1.5
          HeaderAccuracyBase = 0.25
          HeaderAccuracySkillMult = 0.03
          GkSaveBase = 0.25
          GkReflexesMult = 0.3
          GkAerialReachMult = 1.2
          GkJumpMult = 0.8
          ClaimCrossProbability = 0.25
          FailMomentum = 0.3
          Speed = 20
          Vz = 4
          AerialThreatThreshold = 0.4
          AttackingDepthThreshold = 0.5
          GkSkillDefault = 50
          GkSkillDivisor = 150
          SpinTopMult = 0.2
          SpinSideMult = 0.8
          FallbackSpeed = 15
          FallbackVz = 2 }

    let defaultWeightsDribble: WeightSectionsDribble =
        { TechnicalWeight = 0.5
          AgilityWeight = 0.3
          BalanceWeight = 0.2
          ForwardDistance = 5
          SuccessMomentum = 0.5
          FailMomentum = 0.6
          CrossProbability = 0.05
          PassProbability = 0.75
          ShotProbability = 0.2 }

    let defaultWeightsTackle: WeightSectionsTackle =
        { TechnicalWeight = 0.5
          PositioningWeight = 0.3
          StrengthWeight = 0.2
          AggressionWeight = 0.15
          PositioningReduction = 0.1
          FoulShapeBeta = 10
          FoulMomentum = 0.3
          SuccessMomentum = 0.8
          FailMomentum = 0.5
          TackleSteepness = 1.5 }

    let defaultWeightsSetPiece: WeightSectionsSetPiece =
        { FreeKickTargetX = 88.5
          FreeKickSpeed = 16
          FreeKickVz = 0.5
          FreeKickSteepness = 2
          FreeKickSavePowerThreshold = 1.5
          FreeKickSaveVariance = 1.5
          FreeKickSpinTopMult = 0.5
          FreeKickSpinSideMult = 0.9
          CornerBoxXThreshold = 88.5
          CornerDefenderBoxThreshold = 83.5
          CornerSecondPhaseProbability = 0.35
          CornerKeepPossessionProbability = 0.55
          CornerSpeed = 14
          CornerVz = 1
          CornerDensityBase = 3
          CornerDensityPenalty = 0.05
          CornerLogisticSteepness = 2.5
          CornerDefScoreDefault = 0.5
          ThrowInSpeed = 12
          ThrowInVz = 0.5
          ThrowInMomentum = 0.1
          PenaltySkillMultiplier = 0.04
          PenaltyMoraleMultiplier = 0.01
          PenaltyPressureMultiplier = 0.01
          PenaltyComposureNoise = 1.4
          PenaltyLogisticBase = 0.7
          PenaltyGkReflexesMult = 3.5
          PenaltyGkHandlingMult = 3
          PenaltySkillDivisor = 25
          PenaltyCondDivisor = 200
          PenaltyMoraleBase = 0.7
          PenaltyMoraleDivisor = 166.6
          PenaltyGkSkillDivisor = 40
          PenaltySpeed = 28
          PenaltyVzBase = 0.8
          PenaltyVzVariance = 0.3
          PenaltyAngleSpread = 0.12
          PostShotClearProbability = 0.4
          ClearSpeed = 16
          ClearVz = 1.5
          ClearYStdDev = 10
          GoalKickFallbackDistHome = 30
          GoalKickFallbackDistAway = 75
          KickOffPartnerOffsetX = -3
          KickOffPartnerOffsetY = 2
          FoulBaseRate = 0.35
          CornerOnFailedCross = 0.85 }

    let defaultWeightsGK: WeightSectionsGK =
        { CatchHandlingMult = 0.85
          DiveReach = 2.5
          ParrySpeed = 8
          ParryDeflectionAngle = 0.4
          AerialReachMult = 1.2
          JumpReachMult = 0.8
          PunchProbability = 0.35
          ClaimCrossProbability = 0.25
          CollectionRadius = 3
          CollectionPriority = 2
          ThrowSpeed = 12
          RollSpeed = 8
          GoalKickSpeed = 30
          PuntSpeed = 25
          DistributionAccuracyMult = 1
          DistributionDecisionNoise = 0.08
          HoldTimeSubTicks = 20
          MaxHoldSubTicks = 240
          BackPassHandlingPenalty = 0.15
          GKDecisionWindowSubTicks = 16 }

    let defaultWeightsHomeAdvantage: WeightSectionsHomeAdvantage =
        { Strength = 1
          DuelAttackBonus = 4
          DuelDefenseBonus = 2
          ShotComposureBonus = 4
          PassAccuracyBonus = 0.05
          DribbleBonus = 2
          SetPlayAccuracyBonus = 0.04
          TackleBonus = 6
          FreeKickComposure = 5
          PenaltyBonus = 0.04
          CardReduction = 0.2
          FatigueReduction = 0.1 }

    let defaultWeightsPhysics: WeightSectionsPhysics =
        { Gravity = -9.80665
          AirDrag = 0.985
          GroundRestitution = 0.55
          GroundFriction = 0.92
          PostRestitution = 0.65
          SpinDecay = 0.98
          MagnusCoeff = 0.0035
          ContactRadius = 0.35
          PlayerMaxForce = 25
          PlayerMassBase = 70
          PlayerMassWeightCoeff = 0.3
          PlayerMassStrengthCoeff = 0.5
          SteeringSlowRadius = 2.5
          SteeringFleeRadius = 8
          SteeringAlignmentWeight = 0.3
          CohesionWeight = 0.08
          TurnConstraintAgilityCoeff = 0.018
          TurnConstraintBaseLimit = 0.65
          MoveSpeedMax = 9.5
          MoveSpeedMin = 1.2
          SeparationMinDistance = 2.5
          BallContestSeparationRadius = 0.8
          SeparationStrength = 0.1
          SeparationAgilityMultiplier = 0.15
          JitterBase = 0.3
          JitterAgilityMultiplier = 0.5
          BallStopThreshold = 0.15
          AirborneCheckThreshold = 0.05
          AxisStopThreshold = 0.5
          AirborneRestitutionBase = 0.35
          AirborneRestitutionCoeff = 0.25
          AirborneRestitutionFloor = 0.008
          GroundRestitutionBase = 0.5
          GroundRestitutionCoeff = 0.2
          GroundRestitutionFloor = 0.005
          ChaserProximity = 1
          AirborneThreshold = 0.3
          ArrivalAnticipationBase = 0.4
          ArrivalAnticipationQuality = 0.8
          ArrivalCompetitionRadius = 2
          ArrivalConvergenceThreshold = -2
          ArrivalContestThreshold = 0.1
          ReceivingGraceSubTicks = 6 }

    let defaultWeightsTimingDuelChainDelay: WeightSectionsTimingDuelChainDelay =
        { MeanST = 160
          StdST = 40
          MinST = 80
          MaxST = 280 }

    let defaultWeightsTimingDuelNextDelay: WeightSectionsTimingDuelNextDelay =
        { MeanST = 960
          StdST = 200
          MinST = 480
          MaxST = 1520 }

    let defaultWeightsTimingShotDelay: WeightSectionsTimingShotDelay =
        { MeanST = 80
          StdST = 20
          MinST = 40
          MaxST = 160 }

    let defaultWeightsTimingFoulDelay: WeightSectionsTimingFoulDelay =
        { MeanST = 200
          StdST = 60
          MinST = 120
          MaxST = 360 }

    let defaultWeightsTimingGoalDelay: WeightSectionsTimingGoalDelay =
        { MeanST = 1120
          StdST = 160
          MinST = 800
          MaxST = 1520 }

    let defaultWeightsTimingKickOffDelay: WeightSectionsTimingKickOffDelay =
        { MeanST = 40
          StdST = 0
          MinST = 40
          MaxST = 40 }

    let defaultWeightsTimingCornerDelay: WeightSectionsTimingCornerDelay =
        { MeanST = 440
          StdST = 80
          MinST = 280
          MaxST = 640 }

    let defaultWeightsTimingFreeKickDelay: WeightSectionsTimingFreeKickDelay =
        { MeanST = 400
          StdST = 80
          MinST = 240
          MaxST = 600 }

    let defaultWeightsTimingThrowInDelay: WeightSectionsTimingThrowInDelay =
        { MeanST = 200
          StdST = 40
          MinST = 120
          MaxST = 320 }

    let defaultWeightsTimingGoalKickDelay: WeightSectionsTimingGoalKickDelay =
        { MeanST = 240
          StdST = 40
          MinST = 160
          MaxST = 360 }

    let defaultWeightsTimingInjuryDelay: WeightSectionsTimingInjuryDelay =
        { MeanST = 1200
          StdST = 240
          MinST = 800
          MaxST = 1800 }

    let defaultWeightsTimingManagerReactDelay: WeightSectionsTimingManagerReactDelay =
        { MeanST = 320
          StdST = 100
          MinST = 200
          MaxST = 600 }

    let defaultWeightsTimingSubsDelay: WeightSectionsTimingSubsDelay =
        { MeanST = 880
          StdST = 120
          MinST = 560
          MaxST = 1200 }

    let defaultWeightsTiming: WeightSectionsTiming =
        { DuelChainDelay = defaultWeightsTimingDuelChainDelay
          DuelNextDelay = defaultWeightsTimingDuelNextDelay
          ShotDelay = defaultWeightsTimingShotDelay
          FoulDelay = defaultWeightsTimingFoulDelay
          GoalDelay = defaultWeightsTimingGoalDelay
          KickOffDelay = defaultWeightsTimingKickOffDelay
          CornerDelay = defaultWeightsTimingCornerDelay
          FreeKickDelay = defaultWeightsTimingFreeKickDelay
          ThrowInDelay = defaultWeightsTimingThrowInDelay
          GoalKickDelay = defaultWeightsTimingGoalKickDelay
          InjuryDelay = defaultWeightsTimingInjuryDelay
          ManagerReactDelay = defaultWeightsTimingManagerReactDelay
          SubsDelay = defaultWeightsTimingSubsDelay
          StuckBallDelay = 200
          EventWindowSubTicks = 1800 }

    let defaultWeightsMatchVolume: WeightSectionsMatchVolume =
        { MaxChainLength = 6
          TargetDuelTicksPerMatch = 228
          TargetShotsPerMatch = 14
          TargetDribblesPerMatch = 25
          TargetPassesPerMatch = 400
          TargetCrossesPerMatch = 20
          TargetLongBallsPerMatch = 40 }

    let defaultWeightsManager: WeightSectionsManager =
        { FatigueReactionThreshold = 60
          SustainedMomentumSubTicks = 24000
          MomentumThreshold = -2
          FatigueCheckSubTicks = 4800
          ConditionThresholdLosing = 75
          ConditionThresholdDrawing = 65
          ConditionThresholdWinning = 55
          SubWindowMinutes = [| 60.0; 75.0; 85.0 |] }

    let defaultWeightsBuildUp: WeightSectionsBuildUp =
        { PassSuccessBonus = 0.06
          DribblePenalty = 0.12
          LongBallPenalty = 0.08
          GKDistributionBonus = 0.08
          DCPassingBonus = 0.05 }

    let defaultWeightsDecision: WeightSectionsDecision =
        { ShootFinishingWeight = 0.35
          ShootLongShotsWeight = 0.15
          ShootComposureWeight = 0.2
          ShootDistNormWeight = 0.2
          ShootDistNormDivisor = 30
          ShootPosDirectnessWeight = 0.1
          ShootPosDepthWeight = 0.08
          ShootSTBonus = 0.2
          ShootDistPenaltyDivisor = 50
          ShootDistPenaltyMax = 0.5
          ShootDirectnessBonus = 0.15
          PassPassingWeight = 0.4
          PassVisionWeight = 0.3
          PassTargetBonus = 0.1
          PassAttackPhasePenalty = -0.03
          DribbleZoneBonusAttacking = 0.1
          DribbleZoneBonusMidfield = 0.05
          DribbleAttackPhaseBonus = 0.05
          DribbleTempoPenalty = 0.2
          DribblePressurePenalty = 0.15
          CrossCrossingWeight = 0.6
          CrossLateralTendencyWeight = 0.6
          CrossLateralTendencyBase = 0.1
          CrossZoneBonus = 0.15
          CrossWidthBonus = 0.25
          LongBallPassingWeight = 0.3
          LongBallVisionWeight = 0.2
          LongBallPressDistBase = 10
          LongBallPressMin = 0.3
          LongBallPressMax = 1
          LongBallPressNoOpponent = 0.7
          LongBallAttackPhaseBonus = 0.05
          LongBallDirectnessBonus = 0.2
          CreativityWeight = 0.1
          DirectnessWeight = 0.06
          DecisionTemperature = 0.15
          ShootMinThreshold = 0.35
          ShootDirectnessThresholdMod = 0.1
          PassMinThreshold = 0.25
          PassDirectnessThresholdMod = 0.08
          DribbleMinThreshold = 0.15
          DribbleDefZoneDirectnessMin = 0.2
          DribbleHighTempo = 0.7
          DribbleHighTempoDirectnessMin = 0.4
          LongBallMinThreshold = 0.15
          LongBallDirectnessThresholdMod = 0.06
          SpacePassMinScore = 0.25
          ShootDecisionsWeight = 0.15
          ShootBraveryMod = 0.12
          ShootConcentrationMod = 0.08
          PassDecisionsWeight = 0.12
          PassConcentrationMod = 0.08
          DribbleBraveryMod = 0.15
          ShootXGWeight = 0.2
          PassHighPressPenalty = 0.07
          DribbleAggressivePenalty = 0.08
          LongBallSlowTargetBonus = 0.12
          PassTrajectoryBonus = 0.15
          PersonalityFlairWeight = 0.6
          PersonalityTeamworkWeight = 0.45
          PersonalityConsistencyMod = 0.3
          ShootBraveryPressureMod = 0.2
          PassAnticipationBonus = 0.12
          PassBaseUtility = 0.72
          ShotBaseUtility = 0.05
          DribbleBaseUtility = 0.185
          CrossBaseUtility = 0.095
          ShotXGMultiplier = 0.525
          ShotDepthBonusAttacking = 0.10
          ShotDepthBonusDefending = 0.2
          DribbleZoneAttackingMult = 0.75
          DribblePhaseBonusMult = 0.15
          PassHighPressFactor = 0.5 }

    let defaultWeightsPerception: WeightSectionsPerception =
        { VisionRadiusBase = 20
          VisionRadiusMax = 45
          VisionConeAngle = 2.094
          PeripheralMultiplier = 0.4
          MinimumAwarenessFloor = 20
          AnticipationBonusRadius = 5
          GoalkeeperConeAngle = 3.1416
          CommunicationRange = 15
          SetPieceSimplifiedRadius = 30
          BlindPassVisionThreshold = 15
          BlindPassComposureThreshold = 14
          BlindPassSuccessPenalty = 0.3 }

    let defaultWeightsPlanner: WeightSectionsPlanner =
        { ProgressionWeight = 0.6
          ShootValueWeight = 0.35
          ShootFinishingAssumption = 75.0
          DangerBase = [| 20.0; 10.0; 5.0; 3.0; 2.0; 1.0; 0.0 |]
          DangerZoneWeights = [| 1.0; 0.5; 0.25 |]
          PossessionSecurityWeight = 0.8 }

    let defaultWeights: WeightSections =
        { Duel = defaultWeightsDuel
          Shot = defaultWeightsShot
          Pass = defaultWeightsPass
          Cross = defaultWeightsCross
          Dribble = defaultWeightsDribble
          Tackle = defaultWeightsTackle
          SetPiece = defaultWeightsSetPiece
          GK = defaultWeightsGK
          HomeAdvantage = defaultWeightsHomeAdvantage
          Physics = defaultWeightsPhysics
          Timing = defaultWeightsTiming
          MatchVolume = defaultWeightsMatchVolume
          Manager = defaultWeightsManager
          BuildUp = defaultWeightsBuildUp
          Decision = defaultWeightsDecision
          Perception = defaultWeightsPerception
          Planner = defaultWeightsPlanner }
