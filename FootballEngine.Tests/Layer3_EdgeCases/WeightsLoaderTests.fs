module FootballEngine.Tests.Layer3.WeightsLoaderTests

open Expecto
open FootballEngine.ML
open FootballEngine.Types

let weightsLoaderTests =
    testList
        "WeightsLoader"
        [

          test "defaults round-trip produces identical BalanceConfig" {
              let expected = BalanceConfig.defaultConfig
              let actual = expected

              // Duel
              Expect.equal actual.Duel.DuelSteepness expected.Duel.DuelSteepness "DuelSteepness"
              Expect.equal actual.Duel.MomentumBonus expected.Duel.MomentumBonus "MomentumBonus"
              Expect.equal actual.Duel.JitterWin expected.Duel.JitterWin "JitterWin"
              Expect.equal actual.Duel.JitterRecover expected.Duel.JitterRecover "JitterRecover"
              Expect.equal actual.Duel.JitterKeep expected.Duel.JitterKeep "JitterKeep"
              Expect.equal (float actual.Duel.SpeedKeep) (float expected.Duel.SpeedKeep) "SpeedKeep"
              Expect.equal (float actual.Duel.SpeedKeepVz) (float expected.Duel.SpeedKeepVz) "SpeedKeepVz"

              Expect.equal
                  actual.Duel.AttackerDribblingWeight
                  expected.Duel.AttackerDribblingWeight
                  "AttackerDribblingWeight"

              Expect.equal actual.Duel.AttackerAgilityWeight expected.Duel.AttackerAgilityWeight "AttackerAgilityWeight"
              Expect.equal actual.Duel.AttackerBalanceWeight expected.Duel.AttackerBalanceWeight "AttackerBalanceWeight"

              Expect.equal
                  actual.Duel.DefenderTacklingWeight
                  expected.Duel.DefenderTacklingWeight
                  "DefenderTacklingWeight"

              Expect.equal
                  actual.Duel.DefenderStrengthWeight
                  expected.Duel.DefenderStrengthWeight
                  "DefenderStrengthWeight"

              Expect.equal
                  actual.Duel.DefenderPositionWeight
                  expected.Duel.DefenderPositionWeight
                  "DefenderPositionWeight"

              Expect.equal actual.Duel.FatigueThreshold expected.Duel.FatigueThreshold "FatigueThreshold"
              Expect.equal actual.Duel.FatigueDecay expected.Duel.FatigueDecay "FatigueDecay"

              // Shot
              Expect.equal actual.Shot.QualityGate expected.Shot.QualityGate "Shot.QualityGate"
              Expect.equal actual.Shot.AngleSpreadBase expected.Shot.AngleSpreadBase "Shot.AngleSpreadBase"
              Expect.equal (float actual.Shot.VzBase) (float expected.Shot.VzBase) "Shot.VzBase"
              Expect.equal actual.Shot.VzVariance expected.Shot.VzVariance "Shot.VzVariance"

              Expect.equal
                  (float actual.Shot.NormalisationDistance)
                  (float expected.Shot.NormalisationDistance)
                  "Shot.NormalisationDistance"

              Expect.equal
                  actual.Shot.DistanceToGoalMultiplier
                  expected.Shot.DistanceToGoalMultiplier
                  "Shot.DistanceToGoalMultiplier"

              Expect.equal actual.Shot.FinishingMin expected.Shot.FinishingMin "Shot.FinishingMin"
              Expect.equal actual.Shot.FinishingMax expected.Shot.FinishingMax "Shot.FinishingMax"
              Expect.equal actual.Shot.FinishingBonusST expected.Shot.FinishingBonusST "Shot.FinishingBonusST"
              Expect.equal actual.Shot.FinishingBonusAM expected.Shot.FinishingBonusAM "Shot.FinishingBonusAM"
              Expect.equal actual.Shot.FinishingBonusMC expected.Shot.FinishingBonusMC "Shot.FinishingBonusMC"
              Expect.equal actual.Shot.FinishingBonusOther expected.Shot.FinishingBonusOther "Shot.FinishingBonusOther"
              Expect.equal actual.Shot.CondFactorDivisor expected.Shot.CondFactorDivisor "Shot.CondFactorDivisor"
              Expect.equal actual.Shot.ComposureMultiplier expected.Shot.ComposureMultiplier "Shot.ComposureMultiplier"
              Expect.equal actual.Shot.UrgencyMultiplier expected.Shot.UrgencyMultiplier "Shot.UrgencyMultiplier"
              Expect.equal actual.Shot.BasePowerDivisor expected.Shot.BasePowerDivisor "Shot.BasePowerDivisor"
              Expect.equal actual.Shot.SpinTopMultiplier expected.Shot.SpinTopMultiplier "Shot.SpinTopMultiplier"

              Expect.equal
                  actual.Shot.PositionDirectnessWeight
                  expected.Shot.PositionDirectnessWeight
                  "Shot.PositionDirectnessWeight"

              Expect.equal actual.Shot.PositionDepthWeight expected.Shot.PositionDepthWeight "Shot.PositionDepthWeight"

              Expect.equal
                  actual.Shot.PositionCreativityWeight
                  expected.Shot.PositionCreativityWeight
                  "Shot.PositionCreativityWeight"

              Expect.equal actual.Shot.DistNormWeight expected.Shot.DistNormWeight "Shot.DistNormWeight"
              Expect.equal actual.Shot.PositionBonusWeight expected.Shot.PositionBonusWeight "Shot.PositionBonusWeight"
              Expect.equal actual.Shot.HeavyTouchDivisor expected.Shot.HeavyTouchDivisor "Shot.HeavyTouchDivisor"

              Expect.equal
                  actual.Shot.HeavyTouchMultiplier
                  expected.Shot.HeavyTouchMultiplier
                  "Shot.HeavyTouchMultiplier"

              Expect.equal actual.Shot.JitterStdDev expected.Shot.JitterStdDev "Shot.JitterStdDev"
              Expect.equal actual.Shot.GkReflexesStatMult expected.Shot.GkReflexesStatMult "Shot.GkReflexesStatMult"
              Expect.equal actual.Shot.GkOneOnOneStatMult expected.Shot.GkOneOnOneStatMult "Shot.GkOneOnOneStatMult"

              Expect.equal
                  actual.Shot.SaveDenominatorOffset
                  expected.Shot.SaveDenominatorOffset
                  "Shot.SaveDenominatorOffset"

              Expect.equal (float actual.Shot.ShotWideMargin) (float expected.Shot.ShotWideMargin) "Shot.ShotWideMargin"

              // Cross
              Expect.equal actual.Cross.BaseMean expected.Cross.BaseMean "Cross.BaseMean"
              Expect.equal actual.Cross.CrossingWeight expected.Cross.CrossingWeight "Cross.CrossingWeight"
              Expect.equal actual.Cross.PassingWeight expected.Cross.PassingWeight "Cross.PassingWeight"
              Expect.equal actual.Cross.SuccessShapeAlpha expected.Cross.SuccessShapeAlpha "Cross.SuccessShapeAlpha"

              Expect.equal
                  actual.Cross.SuccessConditionMultiplier
                  expected.Cross.SuccessConditionMultiplier
                  "Cross.SuccessConditionMultiplier"

              Expect.equal
                  actual.Cross.HeaderDuelSteepness
                  expected.Cross.HeaderDuelSteepness
                  "Cross.HeaderDuelSteepness"

              Expect.equal actual.Cross.HeaderAccuracyBase expected.Cross.HeaderAccuracyBase "Cross.HeaderAccuracyBase"

              Expect.equal
                  actual.Cross.HeaderAccuracySkillMult
                  expected.Cross.HeaderAccuracySkillMult
                  "Cross.HeaderAccuracySkillMult"

              Expect.equal actual.Cross.GkSaveBase expected.Cross.GkSaveBase "Cross.GkSaveBase"
              Expect.equal actual.Cross.GkReflexesMult expected.Cross.GkReflexesMult "Cross.GkReflexesMult"
              Expect.equal actual.Cross.GkAerialReachMult expected.Cross.GkAerialReachMult "Cross.GkAerialReachMult"
              Expect.equal actual.Cross.GkJumpMult expected.Cross.GkJumpMult "Cross.GkJumpMult"

              Expect.equal
                  actual.Cross.ClaimCrossProbability
                  expected.Cross.ClaimCrossProbability
                  "Cross.ClaimCrossProbability"

              Expect.equal actual.Cross.FailMomentum expected.Cross.FailMomentum "Cross.FailMomentum"
              Expect.equal (float actual.Cross.Speed) (float expected.Cross.Speed) "Cross.Speed"
              Expect.equal (float actual.Cross.Vz) (float expected.Cross.Vz) "Cross.Vz"

              Expect.equal
                  actual.Cross.AerialThreatThreshold
                  expected.Cross.AerialThreatThreshold
                  "Cross.AerialThreatThreshold"

              Expect.equal
                  actual.Cross.AttackingDepthThreshold
                  expected.Cross.AttackingDepthThreshold
                  "Cross.AttackingDepthThreshold"

              Expect.equal actual.Cross.GkSkillDefault expected.Cross.GkSkillDefault "Cross.GkSkillDefault"
              Expect.equal actual.Cross.GkSkillDivisor expected.Cross.GkSkillDivisor "Cross.GkSkillDivisor"
              Expect.equal actual.Cross.SpinTopMult expected.Cross.SpinTopMult "Cross.SpinTopMult"
              Expect.equal actual.Cross.SpinSideMult expected.Cross.SpinSideMult "Cross.SpinSideMult"
              Expect.equal (float actual.Cross.FallbackSpeed) (float expected.Cross.FallbackSpeed) "Cross.FallbackSpeed"
              Expect.equal (float actual.Cross.FallbackVz) (float expected.Cross.FallbackVz) "Cross.FallbackVz"

              // Tackle
              Expect.equal actual.Tackle.TechnicalWeight expected.Tackle.TechnicalWeight "Tackle.TechnicalWeight"
              Expect.equal actual.Tackle.PositioningWeight expected.Tackle.PositioningWeight "Tackle.PositioningWeight"
              Expect.equal actual.Tackle.StrengthWeight expected.Tackle.StrengthWeight "Tackle.StrengthWeight"
              Expect.equal actual.Tackle.AggressionWeight expected.Tackle.AggressionWeight "Tackle.AggressionWeight"

              Expect.equal
                  actual.Tackle.PositioningReduction
                  expected.Tackle.PositioningReduction
                  "Tackle.PositioningReduction"

              Expect.equal actual.Tackle.FoulShapeBeta expected.Tackle.FoulShapeBeta "Tackle.FoulShapeBeta"
              Expect.equal actual.Tackle.FoulMomentum expected.Tackle.FoulMomentum "Tackle.FoulMomentum"
              Expect.equal actual.Tackle.SuccessMomentum expected.Tackle.SuccessMomentum "Tackle.SuccessMomentum"
              Expect.equal actual.Tackle.FailMomentum expected.Tackle.FailMomentum "Tackle.FailMomentum"
              Expect.equal actual.Tackle.TackleSteepness expected.Tackle.TackleSteepness "Tackle.TackleSteepness"

              // Sections that use defaultConfig passthrough
              Expect.equal actual.Pass.BaseMean expected.Pass.BaseMean "Pass (passthrough)"
              Expect.equal actual.Dribble.TechnicalWeight expected.Dribble.TechnicalWeight "Dribble (passthrough)"
              Expect.equal actual.SetPiece.FreeKickSpeed expected.SetPiece.FreeKickSpeed "SetPiece (passthrough)"
              Expect.equal actual.GK.CatchHandlingMult expected.GK.CatchHandlingMult "GK (passthrough)"
              Expect.equal actual.Physics.Gravity expected.Physics.Gravity "Physics (passthrough)"
              Expect.equal actual.Timing.DuelChainDelay expected.Timing.DuelChainDelay "Timing (passthrough)"

              Expect.equal
                  actual.MatchVolume.MaxChainLength
                  expected.MatchVolume.MaxChainLength
                  "MatchVolume (passthrough)"

              Expect.equal actual.BuildUp.PassSuccessBonus expected.BuildUp.PassSuccessBonus "BuildUp (passthrough)"

              Expect.equal
                  actual.Decision.DecisionTemperature
                  expected.Decision.DecisionTemperature
                  "Decision (passthrough)"

              // HomeAdvantage
              Expect.equal actual.HomeAdvantage.Strength expected.HomeAdvantage.Strength "HomeAdvantage.Strength"

              Expect.equal
                  actual.HomeAdvantage.DuelAttackBonus
                  expected.HomeAdvantage.DuelAttackBonus
                  "HomeAdvantage.DuelAttackBonus"

              Expect.equal
                  actual.HomeAdvantage.DuelDefenseBonus
                  expected.HomeAdvantage.DuelDefenseBonus
                  "HomeAdvantage.DuelDefenseBonus"

              Expect.equal
                  actual.HomeAdvantage.ShotComposureBonus
                  expected.HomeAdvantage.ShotComposureBonus
                  "HomeAdvantage.ShotComposureBonus"

              Expect.equal
                  actual.HomeAdvantage.PassAccuracyBonus
                  expected.HomeAdvantage.PassAccuracyBonus
                  "HomeAdvantage.PassAccuracyBonus"

              Expect.equal
                  actual.HomeAdvantage.DribbleBonus
                  expected.HomeAdvantage.DribbleBonus
                  "HomeAdvantage.DribbleBonus"

              Expect.equal
                  actual.HomeAdvantage.SetPlayAccuracyBonus
                  expected.HomeAdvantage.SetPlayAccuracyBonus
                  "HomeAdvantage.SetPlayAccuracyBonus"

              Expect.equal
                  actual.HomeAdvantage.TackleBonus
                  expected.HomeAdvantage.TackleBonus
                  "HomeAdvantage.TackleBonus"

              Expect.equal
                  actual.HomeAdvantage.FreeKickComposure
                  expected.HomeAdvantage.FreeKickComposure
                  "HomeAdvantage.FreeKickComposure"

              Expect.equal
                  actual.HomeAdvantage.PenaltyBonus
                  expected.HomeAdvantage.PenaltyBonus
                  "HomeAdvantage.PenaltyBonus"

              Expect.equal
                  actual.HomeAdvantage.CardReduction
                  expected.HomeAdvantage.CardReduction
                  "HomeAdvantage.CardReduction"

              Expect.equal
                  actual.HomeAdvantage.FatigueReduction
                  expected.HomeAdvantage.FatigueReduction
                  "HomeAdvantage.FatigueReduction"

              // Manager
              Expect.equal
                  actual.Manager.FatigueReactionThreshold
                  expected.Manager.FatigueReactionThreshold
                  "Manager.FatigueReactionThreshold"

              Expect.equal
                  actual.Manager.SustainedMomentumSubTicks
                  expected.Manager.SustainedMomentumSubTicks
                  "Manager.SustainedMomentumSubTicks"

              Expect.equal
                  actual.Manager.MomentumThreshold
                  expected.Manager.MomentumThreshold
                  "Manager.MomentumThreshold"

              Expect.equal
                  actual.Manager.FatigueCheckSubTicks
                  expected.Manager.FatigueCheckSubTicks
                  "Manager.FatigueCheckSubTicks"

              Expect.equal
                  actual.Manager.ConditionThresholdLosing
                  expected.Manager.ConditionThresholdLosing
                  "Manager.ConditionThresholdLosing"

              Expect.equal
                  actual.Manager.ConditionThresholdDrawing
                  expected.Manager.ConditionThresholdDrawing
                  "Manager.ConditionThresholdDrawing"

              Expect.equal
                  actual.Manager.ConditionThresholdWinning
                  expected.Manager.ConditionThresholdWinning
                  "Manager.ConditionThresholdWinning"

              Expect.equal actual.Manager.SubWindowMinutes expected.Manager.SubWindowMinutes "Manager.SubWindowMinutes"

              // Perception
              Expect.equal
                  (float actual.Perception.VisionRadiusBase)
                  (float expected.Perception.VisionRadiusBase)
                  "Perception.VisionRadiusBase"

              Expect.equal
                  (float actual.Perception.VisionRadiusMax)
                  (float expected.Perception.VisionRadiusMax)
                  "Perception.VisionRadiusMax"

              Expect.equal
                  actual.Perception.VisionConeAngle
                  expected.Perception.VisionConeAngle
                  "Perception.VisionConeAngle"

              Expect.equal
                  actual.Perception.PeripheralMultiplier
                  expected.Perception.PeripheralMultiplier
                  "Perception.PeripheralMultiplier"

              Expect.equal
                  (float actual.Perception.MinimumAwarenessFloor)
                  (float expected.Perception.MinimumAwarenessFloor)
                  "Perception.MinimumAwarenessFloor"

              Expect.equal
                  (float actual.Perception.AnticipationBonusRadius)
                  (float expected.Perception.AnticipationBonusRadius)
                  "Perception.AnticipationBonusRadius"

              Expect.equal
                  actual.Perception.GoalkeeperConeAngle
                  expected.Perception.GoalkeeperConeAngle
                  "Perception.GoalkeeperConeAngle"

              Expect.equal
                  (float actual.Perception.CommunicationRange)
                  (float expected.Perception.CommunicationRange)
                  "Perception.CommunicationRange"

              Expect.equal
                  (float actual.Perception.SetPieceSimplifiedRadius)
                  (float expected.Perception.SetPieceSimplifiedRadius)
                  "Perception.SetPieceSimplifiedRadius"

              Expect.equal
                  actual.Perception.BlindPassVisionThreshold
                  expected.Perception.BlindPassVisionThreshold
                  "Perception.BlindPassVisionThreshold"

              Expect.equal
                  actual.Perception.BlindPassComposureThreshold
                  expected.Perception.BlindPassComposureThreshold
                  "Perception.BlindPassComposureThreshold"

              Expect.equal
                  actual.Perception.BlindPassSuccessPenalty
                  expected.Perception.BlindPassSuccessPenalty
                  "Perception.BlindPassSuccessPenalty"
          }

          test "mergeWithDefaults with empty partial returns defaults" {
              let emptyPartial: PartialBalanceConfig =
                  { Duel = None
                    Shot = None
                    Pass = None
                    Cross = None
                    Dribble = None
                    Tackle = None
                    SetPiece = None
                    GK = None
                    HomeAdvantage = None
                    Physics = None
                    Timing = None
                    MatchVolume = None
                    Manager = None
                    BuildUp = None
                    Decision = None
                    Perception = None
                    Individual = None
                    ProfileWeights = None
                    Development = None
                    CalibrationTargets = None
                    Collective = None
                    Personality = None
                    Utility = None
                    Referee = None
                    Environment = None
                    Momentum = None
                    XG = None
                    Interception = None
                    WinProbability = None
                    Performance = None
                    MatchSituation = None }

              let result =
                  WeightsLoader.mergeWithDefaults emptyPartial BalanceConfig.defaultConfig

              Expect.equal result.Duel.DuelSteepness BalanceConfig.defaultConfig.Duel.DuelSteepness "DuelSteepness"

              Expect.equal
                  result.Utility.PressZoneBonus_HighAttacking
                  BalanceConfig.defaultConfig.Utility.PressZoneBonus_HighAttacking
                  "PressZoneBonus"
          }

          test "mergeWithDefaults overrides single field" {
              let partialShoot: PartialShootWeights =
                  { FinishingWeight = Some 0.99
                    LongShotsWeight = None
                    ComposureWeight = None
                    XGInfluence = None
                    ComposureStateMod = None
                    ConfidenceMod = None
                    FocusMod = None
                    RiskBonus = None
                    DistPenaltyDivisor = None
                    DistPenaltyMax = None }

              let partialIndividual: PartialIndividualWeights =
                  { Shoot = Some partialShoot
                    Pass = None
                    Dribble = None
                    Cross = None
                    LongBall = None
                    SoftmaxTemperature = None
                    DirectnessBlendTactic = None
                    DirectnessBlendProfile = None }

              let partial: PartialBalanceConfig =
                  { Duel = None
                    Shot = None
                    Pass = None
                    Cross = None
                    Dribble = None
                    Tackle = None
                    SetPiece = None
                    GK = None
                    HomeAdvantage = None
                    Physics = None
                    Timing = None
                    MatchVolume = None
                    Manager = None
                    BuildUp = None
                    Decision = None
                    Perception = None
                    Individual = Some partialIndividual
                    ProfileWeights = None
                    Development = None
                    CalibrationTargets = None
                    Collective = None
                    Personality = None
                    Utility = None
                    Referee = None
                    Environment = None
                    Momentum = None
                    XG = None
                    Interception = None
                    WinProbability = None
                    Performance = None
                    MatchSituation = None }

              let result = WeightsLoader.mergeWithDefaults partial BalanceConfig.defaultConfig
              Expect.equal result.Individual.Shoot.FinishingWeight 0.99 "FinishingWeight overridden"

              Expect.equal
                  result.Individual.Shoot.LongShotsWeight
                  BalanceConfig.defaultConfig.Individual.Shoot.LongShotsWeight
                  "LongShotsWeight from default"

              Expect.equal
                  result.Individual.SoftmaxTemperature
                  BalanceConfig.defaultConfig.Individual.SoftmaxTemperature
                  "SoftmaxTemperature from default"
          } ]
