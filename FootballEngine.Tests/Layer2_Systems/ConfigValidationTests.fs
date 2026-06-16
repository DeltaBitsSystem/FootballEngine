module FootballEngine.Tests.Layer2.ConfigValidationTests

open Expecto
open FootballEngine
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

let private defaultDuelConfig =
    { DuelSteepness = 1.2
      MomentumBonus = 1.0
      JitterWin = 0.1
      JitterRecover = 0.1
      JitterKeep = 0.1
      SpeedKeep = 5.0<meter / second>
      SpeedKeepVz = 3.0<meter / second>
      AttackerDribblingWeight = 0.4
      AttackerAgilityWeight = 0.3
      AttackerBalanceWeight = 0.3
      DefenderTacklingWeight = 0.4
      DefenderStrengthWeight = 0.3
      DefenderPositionWeight = 0.3
      FatigueThreshold = 60
      FatigueDecay = 0.05 }

let private defaultShotConfig =
    { OnTargetBase = 0.40
      OnTargetDistDecayRate = 15.0
      OnTargetDistMaxPenalty = 0.30
      QualityGate = 0.5
      AngleSpreadBase = 0.3
      VzBase = 25.0<meter / second>
      VzVariance = 3.0
      OnTargetMultiplier = 1.0
      OnTargetDistDivisor = 10.0
      NormalisationDistance = 16.5<meter>
      DistanceToGoalMultiplier = 1.0
      FinishingMin = 0.1
      FinishingMax = 0.9
      FinishingBonusST = 0.1
      FinishingBonusAM = 0.05
      FinishingBonusMC = 0.0
      FinishingBonusOther = 0.0
      CondFactorDivisor = 100.0
      ComposureMultiplier = 1.0
      UrgencyMultiplier = 1.0
      BasePowerDivisor = 30.0
      SpinTopMultiplier = 1.0
      PositionDirectnessWeight = 0.3
      PositionDepthWeight = 0.3
      PositionCreativityWeight = 0.4
      DistNormWeight = 0.5
      PositionBonusWeight = 0.5
      HeavyTouchDivisor = 10.0
      HeavyTouchMultiplier = 0.8
      JitterStdDev = 0.05
      GkReflexesStatMult = 1.0
      GkOneOnOneStatMult = 1.0
      SaveDenominatorOffset = 0.5
      ShotWideMargin = 0.5<meter> }

let private defaultMatchVolumeConfig =
    { MaxChainLength = 12
      TargetDuelTicksPerMatch = 200
      TargetShotsPerMatch = 14.0
      TargetDribblesPerMatch = 30.0
      TargetPassesPerMatch = 400.0
      TargetCrossesPerMatch = 40.0
      TargetLongBallsPerMatch = 30.0 }

let configValidationTests =
    testList
        "ConfigValidation"
        [ testList
              "validateDuelConfig"
              [ test "valid config returns empty errors" {
                    let errors = ConfigValidation.validateDuelConfig defaultDuelConfig
                    Expect.isEmpty errors "Default duel config should be valid"
                }

                test "DuelSteepness > 3.0 returns error" {
                    let cfg =
                        { defaultDuelConfig with
                            DuelSteepness = 3.5 }

                    let errors = ConfigValidation.validateDuelConfig cfg
                    Expect.isNonEmpty errors "Should fail with DuelSteepness > 3.0"
                    Expect.stringContains errors.Head "DuelSteepness" "Error should mention DuelSteepness"
                }

                test "DuelSteepness <= 0.0 returns error" {
                    let cfg =
                        { defaultDuelConfig with
                            DuelSteepness = 0.0 }

                    let errors = ConfigValidation.validateDuelConfig cfg
                    Expect.isNonEmpty errors "Should fail with DuelSteepness <= 0.0"
                }

                test "attacker weights sum > 1.5 returns error" {
                    let cfg =
                        { defaultDuelConfig with
                            AttackerDribblingWeight = 0.6
                            AttackerAgilityWeight = 0.5
                            AttackerBalanceWeight = 0.5 }

                    let errors = ConfigValidation.validateDuelConfig cfg
                    Expect.isNonEmpty errors "Should fail when attacker weights > 1.5"
                    Expect.stringContains errors.Head "Attacker weights" "Error should mention attacker weights"
                }

                test "defender weights sum > 1.5 returns error" {
                    let cfg =
                        { defaultDuelConfig with
                            DefenderTacklingWeight = 0.6
                            DefenderStrengthWeight = 0.5
                            DefenderPositionWeight = 0.5 }

                    let errors = ConfigValidation.validateDuelConfig cfg
                    Expect.isNonEmpty errors "Should fail when defender weights > 1.5"
                }

                test "weights at exactly 1.5 are accepted" {
                    let cfg =
                        { defaultDuelConfig with
                            AttackerDribblingWeight = 0.5
                            AttackerAgilityWeight = 0.5
                            AttackerBalanceWeight = 0.5 }

                    let errors = ConfigValidation.validateDuelConfig cfg
                    let attackerErrors = errors |> List.filter (fun e -> e.Contains "Attacker")
                    Expect.isEmpty attackerErrors "Sum exactly 1.5 should pass"
                } ]

          testList
              "validateShotConfig"
              [ test "valid config returns empty errors" {
                    let errors = ConfigValidation.validateShotConfig defaultShotConfig
                    Expect.isEmpty errors "Default shot config should be valid"
                }

                test "OnTargetBase < 0.0 returns error" {
                    let cfg =
                        { defaultShotConfig with
                            OnTargetBase = -0.1 }

                    let errors = ConfigValidation.validateShotConfig cfg
                    Expect.isNonEmpty errors "Should fail with negative OnTargetBase"
                }

                test "OnTargetBase > 1.0 returns error" {
                    let cfg =
                        { defaultShotConfig with
                            OnTargetBase = 1.5 }

                    let errors = ConfigValidation.validateShotConfig cfg
                    Expect.isNonEmpty errors "Should fail with OnTargetBase > 1.0"
                }

                test "OnTargetDistMaxPenalty > OnTargetBase returns error" {
                    let cfg =
                        { defaultShotConfig with
                            OnTargetBase = 0.30
                            OnTargetDistMaxPenalty = 0.40 }

                    let errors = ConfigValidation.validateShotConfig cfg
                    Expect.isNonEmpty errors "Should fail when MaxPenalty > Base"
                }

                test "OnTargetDistDecayRate <= 0 returns error" {
                    let cfg =
                        { defaultShotConfig with
                            OnTargetDistDecayRate = 0.0 }

                    let errors = ConfigValidation.validateShotConfig cfg
                    Expect.isNonEmpty errors "Should fail with non-positive decay rate"
                }

                test "OnTargetDistMaxPenalty equal to Base is accepted" {
                    let cfg =
                        { defaultShotConfig with
                            OnTargetDistMaxPenalty = 0.40 }

                    let errors = ConfigValidation.validateShotConfig cfg
                    let penaltyErrors = errors |> List.filter (fun e -> e.Contains "MaxPenalty")
                    Expect.isEmpty penaltyErrors "MaxPenalty equal to Base should pass"
                } ]

          testList
              "validateMatchVolumeConfig"
              [ test "valid config returns empty errors" {
                    let errors = ConfigValidation.validateMatchVolumeConfig defaultMatchVolumeConfig
                    Expect.isEmpty errors "Default volume config should be valid"
                }

                test "TargetShotsPerMatch < 8 returns error" {
                    let cfg =
                        { defaultMatchVolumeConfig with
                            TargetShotsPerMatch = 5.0 }

                    let errors = ConfigValidation.validateMatchVolumeConfig cfg
                    Expect.isNonEmpty errors "Should fail with shots < 8"
                }

                test "TargetShotsPerMatch > 25 returns error" {
                    let cfg =
                        { defaultMatchVolumeConfig with
                            TargetShotsPerMatch = 30.0 }

                    let errors = ConfigValidation.validateMatchVolumeConfig cfg
                    Expect.isNonEmpty errors "Should fail with shots > 25"
                }

                test "TargetPassesPerMatch < 200 returns error" {
                    let cfg =
                        { defaultMatchVolumeConfig with
                            TargetPassesPerMatch = 100.0 }

                    let errors = ConfigValidation.validateMatchVolumeConfig cfg
                    Expect.isNonEmpty errors "Should fail with passes < 200"
                }

                test "TargetPassesPerMatch > 700 returns error" {
                    let cfg =
                        { defaultMatchVolumeConfig with
                            TargetPassesPerMatch = 800.0 }

                    let errors = ConfigValidation.validateMatchVolumeConfig cfg
                    Expect.isNonEmpty errors "Should fail with passes > 700"
                }

                test "boundary values are accepted" {
                    let cfg =
                        { defaultMatchVolumeConfig with
                            TargetShotsPerMatch = 8.0
                            TargetPassesPerMatch = 700.0 }

                    let errors = ConfigValidation.validateMatchVolumeConfig cfg
                    Expect.isEmpty errors "Boundary values should pass"
                } ]

          testList
              "validatePerformanceConfig"
              [ test "valid config returns empty errors" {
                    let errors =
                        ConfigValidation.validatePerformanceConfig PerformanceDefaults.duelPerformanceConfig

                    Expect.isEmpty errors "Default duel perf config should be valid"
                }

                test "weights not summing to 1.0 returns error" {
                    let cfg =
                        { PerformanceDefaults.duelPerformanceConfig with
                            StatWeight = 0.5
                            ConditionWeight = 0.5
                            MoraleWeight = 0.5 }

                    let errors = ConfigValidation.validatePerformanceConfig cfg
                    Expect.isNonEmpty errors "Should fail when weights sum != 1.0"
                    Expect.stringContains errors.Head "weights must sum to 1.0" "Error should mention weight sum"
                }

                test "CurveSteepness <= 0 returns error" {
                    let cfg =
                        { PerformanceDefaults.duelPerformanceConfig with
                            CurveSteepness = 0.0 }

                    let errors = ConfigValidation.validatePerformanceConfig cfg
                    Expect.isNonEmpty errors "Should fail with non-positive CurveSteepness"
                }

                test "CurveInflection outside [0.3, 0.7] returns error" {
                    let cfg =
                        { PerformanceDefaults.duelPerformanceConfig with
                            CurveInflection = 0.8 }

                    let errors = ConfigValidation.validatePerformanceConfig cfg
                    Expect.isNonEmpty errors "Should fail with CurveInflection > 0.7"
                }

                test "all three default perf configs are valid" {
                    let configs =
                        [ PerformanceDefaults.duelPerformanceConfig
                          PerformanceDefaults.technicalPerformanceConfig
                          PerformanceDefaults.decisionPerformanceConfig ]

                    for cfg in configs do
                        let errors = ConfigValidation.validatePerformanceConfig cfg
                        Expect.isEmpty errors $"%s{cfg.Name} default config should be valid"
                } ]

          testList
              "validateAll"
              [ test "validateAll on default config returns empty errors" {
                    let cfg = BalanceConfig.defaultConfig
                    let errors = ConfigValidation.validateAll cfg
                    Expect.isEmpty errors "Default BalanceConfig should be fully valid"
                } ] ]
