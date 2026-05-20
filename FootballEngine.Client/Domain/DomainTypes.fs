namespace FootballEngine.Domain

type PlayerId = int
type ClubId = int
type MatchId = int
type CompetitionId = int
type CountryCode = string
type StaffId = int

type Round =
    | GroupStage of groupIndex: int
    | KnockoutRound of teamsRemaining: int
    | ThirdPlace
    | Final


type Position =
    | GK
    | DR
    | DC
    | DL
    | WBR
    | WBL
    | DM
    | MR
    | MC
    | ML
    | AMR
    | AMC
    | AML
    | ST



type Formation =
    | F442
    | F442Diamond
    | F433
    | F433Flat
    | F451
    | F4141
    | F4231
    | F4312
    | F4321
    | F352
    | F343
    | F3421
    | F532
    | F541
    | F523

type BehavioralProfile =
    { PositionalFreedom: float
      AttackingDepth: float
      LateralTendency: float
      DefensiveHeight: float
      PressingIntensity: float
      RiskAppetite: float
      Directness: float
      CreativityWeight: float
      AerialThreat: float
      HoldUpPlay: float }

module BehavioralProfile =
    let neutral =
        { PositionalFreedom = 0.5
          AttackingDepth = 0.5
          LateralTendency = 0.0
          DefensiveHeight = 0.5
          PressingIntensity = 0.5
          RiskAppetite = 0.5
          Directness = 0.5
          CreativityWeight = 0.5
          AerialThreat = 0.5
          HoldUpPlay = 0.5 }

[<CLIMutable>]
type ProfileWeights = {
    PositionalFreedom_PositioningWeight: float
    PositionalFreedom_VisionWeight: float
    PositionalFreedom_StaminaWeight: float
    PositionalFreedom_ConcentrationWeight: float
    PositionalFreedom_BalanceWeight: float
    PositionalFreedom_AgilityWeight: float
    AttackingDepth_PaceWeight: float
    AttackingDepth_AccelerationWeight: float
    AttackingDepth_FinishingWeight: float
    AttackingDepth_ComposureWeight: float
    AttackingDepth_StaminaWeight: float
    AttackingDepth_PositionBaseMultiplier: float
    LateralTendency_CrossingWeight: float
    LateralTendency_PaceWeight: float
    DefensiveHeight_WorkRateWeight: float
    DefensiveHeight_TacklingWeight: float
    DefensiveHeight_PositioningWeight: float
    DefensiveHeight_StaminaWeight: float
    DefensiveHeight_PositionBaseMultiplier: float
    PressingIntensity_StaminaWeight: float
    PressingIntensity_WorkRateWeight: float
    PressingIntensity_AggressionWeight: float
    PressingIntensity_PaceWeight: float
    PressingIntensity_ConcentrationWeight: float
    RiskAppetite_PassingWeight: float
    RiskAppetite_LongShotsWeight: float
    RiskAppetite_VisionWeight: float
    RiskAppetite_ComposureWeight: float
    RiskAppetite_DribblingWeight: float
    RiskAppetite_BraveryWeight: float
    Directness_FinishingWeight: float
    Directness_PaceWeight: float
    Directness_AccelerationWeight: float
    Directness_AggressionWeight: float
    Directness_DribblingWeight: float
    Directness_StrengthWeight: float
    Directness_InversePassingWeight: float
    CreativityWeight_PassingWeight: float
    CreativityWeight_VisionWeight: float
    CreativityWeight_BallControlWeight: float
    CreativityWeight_DribblingWeight: float
    CreativityWeight_ComposureWeight: float
    CreativityWeight_CrossingWeight: float
    AerialThreat_JumpingReachWeight: float
    AerialThreat_HeadingWeight: float
    AerialThreat_StrengthWeight: float
    AerialThreat_BraveryWeight: float
    HoldUpPlay_StrengthWeight: float
    HoldUpPlay_BallControlWeight: float
    HoldUpPlay_ComposureWeight: float
    HoldUpPlay_PassingWeight: float
    HoldUpPlay_HeadingWeight: float
    HoldUpPlay_BalanceWeight: float
}

module ProfileWeightDefaults =
    let defaults : ProfileWeights = {
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
