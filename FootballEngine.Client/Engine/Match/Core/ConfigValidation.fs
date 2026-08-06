namespace FootballEngine

open FootballEngine.Types

module ConfigValidation =

    type ValidationError = string

    let private errors (conditions: (bool * string) list) : ValidationError list =
        conditions |> List.choose (fun (ok, msg) -> if not ok then Some msg else None)

    let validateDuelConfig (cfg: DuelConfig) : ValidationError list =
        errors
            [ cfg.DuelSteepness > 0.0 && cfg.DuelSteepness <= 3.0,
              "DuelSteepness must be in (0, 3]. Above 3 produces unrealistic win rates."
              cfg.AttackerDribblingWeight
              + cfg.AttackerAgilityWeight
              + cfg.AttackerBalanceWeight
              <= 1.5,
              "Attacker weights sum exceeds 1.5 — scores will be out of normalization range."
              cfg.DefenderTacklingWeight
              + cfg.DefenderStrengthWeight
              + cfg.DefenderPositionWeight
              <= 1.5,
              "Defender weights sum exceeds 1.5 — scores will be out of normalization range." ]

    let validateShotConfig (_cfg: ShotConfig) : ValidationError list = []

    let validateMatchVolumeConfig (cfg: MatchVolumeConfig) : ValidationError list =
        errors
            [ cfg.TargetShotsPerMatch >= 8.0 && cfg.TargetShotsPerMatch <= 25.0,
              "TargetShotsPerMatch outside realistic range [8, 25]. Real football: ~13-14."
              cfg.TargetPassesPerMatch >= 200.0 && cfg.TargetPassesPerMatch <= 700.0,
              "TargetPassesPerMatch outside realistic range [200, 700]. Real football: ~400." ]

    let validatePerformanceConfig (cfg: PerformanceConfig) : ValidationError list =
        errors
            [ cfg.StatWeight + cfg.ConditionWeight + cfg.MoraleWeight > 0.99
              && cfg.StatWeight + cfg.ConditionWeight + cfg.MoraleWeight < 1.01,
              $"PerformanceConfig '%s{cfg.Name}' weights must sum to 1.0 (got %.3f{cfg.StatWeight + cfg.ConditionWeight + cfg.MoraleWeight})."
              cfg.CurveSteepness > 0.0, $"PerformanceConfig '%s{cfg.Name}' CurveSteepness must be positive."
              cfg.CurveInflection >= 0.3 && cfg.CurveInflection <= 0.7,
              $"PerformanceConfig '%s{cfg.Name}' CurveInflection should be in [0.3, 0.7] for realistic curves." ]

    let validateMatchSituationConfig (cfg: MatchSituationConfig) : ValidationError list =
        errors
            [ cfg.ShootLosingLateMod >= 1.0 && cfg.ShootLosingLateMod <= 2.0,
              "ShootLosingLateMod should be in [1.0, 2.0]. Below 1.0 reduces urgency when losing."
              cfg.ShootDrawLateMod >= 1.0 && cfg.ShootDrawLateMod <= 1.5, "ShootDrawLateMod should be in [1.0, 1.5]."
              cfg.PassWinningMod >= 1.0 && cfg.PassWinningMod <= 1.5, "PassWinningMod should be in [1.0, 1.5]."
              cfg.PassTransitionPenalty >= 0.5 && cfg.PassTransitionPenalty <= 1.0,
              "PassTransitionPenalty should be in [0.5, 1.0]."
              cfg.LongBallPressureThreshold >= 0.3 && cfg.LongBallPressureThreshold <= 0.9,
              "LongBallPressureThreshold should be in [0.3, 0.9]." ]

    let validateDiagnosticsConfig (cfg: DiagnosticsConfig) : ValidationError list =
        errors
            [ cfg.MaxTracesPerMatch >= 100 && cfg.MaxTracesPerMatch <= 10000,
              "MaxTracesPerMatch should be in [100, 10000]." ]

    let validatePassConfig (cfg: PassConfig) : ValidationError list =
        errors
            [ cfg.FlightTimePerMeter > 0.0, "FlightTimePerMeter must be positive."
              int cfg.FlightTimeMin > 0, "FlightTimeMin must be positive."
              int cfg.FlightTimeMax > int cfg.FlightTimeMin, "FlightTimeMax must be greater than FlightTimeMin."
              cfg.LongBallFlightTimePerMeter > 0.0, "LongBallFlightTimePerMeter must be positive."
              int cfg.LongBallFlightTimeMin > 0, "LongBallFlightTimeMin must be positive."
              int cfg.LongBallFlightTimeMax > int cfg.LongBallFlightTimeMin,
              "LongBallFlightTimeMax must be greater than LongBallFlightTimeMin."
              int cfg.ArrivalWindow > 0, "ArrivalWindow must be positive." ]

    let validateGKPassConfig (cfg: GKConfig) : ValidationError list =
        errors
            [ int cfg.DistributionDecisionDelay >= 0, "DistributionDecisionDelay must be non-negative."
              cfg.ShortDistributionBias >= 0.0 && cfg.ShortDistributionBias <= 1.0,
              "ShortDistributionBias must be in [0, 1]."
              float cfg.GKShortPassMaxDist > 0.0, "GKShortPassMaxDist must be positive." ]

    let validateAll (cfg: BalanceConfig) : ValidationError list =
        [ yield! validateDuelConfig cfg.Duel
          yield! validateShotConfig cfg.Shot
          yield! validateMatchVolumeConfig cfg.MatchVolume
          yield! validateMatchSituationConfig cfg.MatchSituation
          yield! validateDiagnosticsConfig cfg.Diagnostics
          yield! validatePassConfig cfg.Pass
          yield! validateGKPassConfig cfg.GK
          yield! validatePerformanceConfig PerformanceDefaults.duelPerformanceConfig
          yield! validatePerformanceConfig PerformanceDefaults.technicalPerformanceConfig
          yield! validatePerformanceConfig PerformanceDefaults.decisionPerformanceConfig ]
