namespace FootballEngine.Core.Match

/// Config sanity checks. The engine must never run with a self-contradictory
/// config: an invalid search budget would silently corrupt every decision,
/// and an invalid weights file should be loud, not silently ignored.
module ConfigValidation =

    let validatePlanner (p: PlannerConfig) : string list =
        [ if p.MaxChainLength < 1 || p.MaxChainLength > 16 then
              yield "Planner.MaxChainLength must be in [1, 16]"
          if p.MaxEvaluateLimit < 1 || p.MaxEvaluateLimit > 4096 then
              yield "Planner.MaxEvaluateLimit must be in [1, 4096]"
          if p.DeliberationTime < 0<subtick> || p.DeliberationTime > 40<subtick> then
              yield "Planner.DeliberationTime must be in [0, 40] subticks" ]

    let validateExpectedValue (e: ExpectedValueConfig) : string list =
        [ for name, value, low, high in
              [ "ThreatPeak", e.ThreatPeak, 0.0, 1.0
                "ThreatDecay", float e.ThreatDecay, 1.0, 100.0
                "ThreatFloor", e.ThreatFloor, 0.0, 0.1
                "PassMarginScale", e.PassMarginScale, 0.5, 40.0
                "PassSkillSteps", e.PassSkillSteps, 0.0, 40.0
                "CarryAdvance", float e.CarryAdvance, 0.5, 20.0
                "CarryKeepBase", e.CarryKeepBase, 0.0, 1.0
                "CarrySkillWeight", e.CarrySkillWeight, 0.0, 0.05
                "CarryPressurePenalty", e.CarryPressurePenalty, 0.0, 1.0
                "PressureRadius", float e.PressureRadius, 0.5, 15.0
                "ShotBlockShare", e.ShotBlockShare, 0.0, 1.0
                "ShotTurnLimit", e.ShotTurnLimit, 0.5, System.Math.PI
                "CarryBlockPenalty", e.CarryBlockPenalty, 0.0, 1.0
                "CarryBlockRange", float e.CarryBlockRange, 1.0, 40.0
                "CrossBaseSuccess", e.CrossBaseSuccess, 0.0, 1.0
                "HeaderConversion", e.HeaderConversion, 0.0, 1.0
                "ClearConcession", e.ClearConcession, 0.0, 1.0
                "LossNear", e.LossNear, 0.0, 1.0
                "LossRange", float e.LossRange, 0.5, 30.0
                "SupportRelief", e.SupportRelief, 0.0, 1.0
                "SupportDistance", float e.SupportDistance, 0.0, 40.0
                "StyleValue", e.StyleValue, 0.0, 1.0
                "Temperature", e.Temperature, 0.0001, 1.0
                "ChainFinishing", e.ChainFinishing, 0.0, 100.0
                "StylePhasePenalty", e.StylePhasePenalty, -5.0, 5.0
                "StyleTeamwork", e.StyleTeamwork, -5.0, 5.0
                "StyleConcentration", e.StyleConcentration, -5.0, 5.0
                "StyleConsistency", e.StyleConsistency, -5.0, 5.0
                "StyleShotBravery", e.StyleShotBravery, -5.0, 5.0
                "StyleFlair", e.StyleFlair, -5.0, 5.0
                "StyleCrossWidth", e.StyleCrossWidth, -5.0, 5.0
                "StyleCarryBravery", e.StyleCarryBravery, -5.0, 5.0
                "StyleCarryTempo", e.StyleCarryTempo, -5.0, 5.0
                "OffsideReadMargin", float e.OffsideReadMargin, 0.0, 5.0 ] do
              if not (System.Double.IsFinite value) || value < low || value > high then
                  yield $"ExpectedValue.{name} must be in [{low}, {high}]"
          if e.PassMinimumMargin < -40 || e.PassMinimumMargin > 0 then
              yield "ExpectedValue.PassMinimumMargin must be in [-40, 0]" ]

    let validateQuickMatch (q: QuickMatchConfig) : string list =
        let roles (name: string) (w: QuickRoleWeights) =
            let all =
                [ w.Keeper
                  w.CentreBack
                  w.Fullback
                  w.Holding
                  w.Central
                  w.Playmaker
                  w.Wide
                  w.Forward ]

            [ if all |> List.exists (fun v -> not (System.Double.IsFinite v) || v < 0.0) then
                  yield $"QuickMatch.{name} weights must be finite and non-negative"
              if List.sum all <= 0.0 then
                  yield $"QuickMatch.{name} needs a positive weight" ]

        [ if not (q.GoalsPerTeam > 0.0 && q.GoalsPerTeam < 10.0) then
              yield "QuickMatch.GoalsPerTeam must be in (0, 10)"
          if not (abs q.HomeAdvantage <= 1.0) then
              yield "QuickMatch.HomeAdvantage must be in [-1, 1]"
          if not (q.StrengthSensitivity >= 0.0 && q.StrengthSensitivity <= 0.5) then
              yield "QuickMatch.StrengthSensitivity must be in [0, 0.5]"
          if not (q.AssistedShare >= 0.0 && q.AssistedShare <= 1.0) then
              yield "QuickMatch.AssistedShare must be in [0, 1]"
          for name, rate in
              [ "YellowsPerTeam", q.YellowsPerTeam
                "RedsPerTeam", q.RedsPerTeam
                "FoulsPerTeam", q.FoulsPerTeam
                "InjuriesPerTeam", q.InjuriesPerTeam
                "SubstitutionsPerTeam", q.SubstitutionsPerTeam
                "CalmFoulShare", q.CalmFoulShare
                "RobustInjuryShare", q.RobustInjuryShare ] do
              if not (rate >= 0.0 && rate < 100.0) then
                  yield $"QuickMatch.{name} must be in [0, 100)"
          if q.FirstSubstitutionMinute < 1 || q.FirstSubstitutionMinute > 89 then
              yield "QuickMatch.FirstSubstitutionMinute must be in [1, 89]"
          yield! roles "ScorerByRole" q.ScorerByRole
          yield! roles "AssistByRole" q.AssistByRole ]

    let validateScout (c: ScoutConfig) : string list =
        [ if c.MemoryHalfLifeDays <= 0.0 then
              yield "Scout.MemoryHalfLifeDays must be > 0"
          if c.MinConfidenceToReport <= 0.0 || c.MinConfidenceToReport > 1.0 then
              yield "Scout.MinConfidenceToReport must be in (0, 1]"
          if c.AiReportNoise < 0.0 then
              yield "Scout.AiReportNoise must be >= 0"
          if c.NoiseByAbility < 0.0 then
              yield "Scout.NoiseByAbility must be >= 0"
          if c.RivalPruneAfterDays <= 0.0 then
              yield "Scout.RivalPruneAfterDays must be > 0"
          if c.ConfidenceMatches <= 0.0 then
              yield "Scout.ConfidenceMatches must be > 0"
          if c.ShortPassMaxMeters <= 0.0<meter> then
              yield "Scout.ShortPassMaxMeters must be > 0"
          if c.LongPassMinMeters <= 0.0<meter> then
              yield "Scout.LongPassMinMeters must be > 0"
          if c.NearShotMaxMeters <= 0.0<meter> then
              yield "Scout.NearShotMaxMeters must be > 0"
          if c.AreaShotMaxMeters <= 0.0<meter> then
              yield "Scout.AreaShotMaxMeters must be > 0"
          if c.ShortPassMaxMeters >= c.LongPassMinMeters then
              yield "Scout.ShortPassMaxMeters must be < LongPassMinMeters"
          if c.NearShotMaxMeters >= c.AreaShotMaxMeters then
              yield "Scout.NearShotMaxMeters must be < AreaShotMaxMeters" ]

    let validateCounter (c: CounterConfig) : string list =
        [ if c.MaxSideBias < 0.0 then
              yield "Counter.MaxSideBias must be >= 0"
          if c.BiasScale < 0.0 then
              yield "Counter.BiasScale must be >= 0"
          if c.MaxPressBoost < 0.0 then
              yield "Counter.MaxPressBoost must be >= 0"
          if c.MaxLineDrop < 0.0 then
              yield "Counter.MaxLineDrop must be >= 0"
          if c.MaxWidthTighten < 0.0 then
              yield "Counter.MaxWidthTighten must be >= 0"
          if c.MaxTempoShift < 0.0 then
              yield "Counter.MaxTempoShift must be >= 0"
          if c.MaxDirectnessShift < 0.0 then
              yield "Counter.MaxDirectnessShift must be >= 0"
          if c.SignatureChangePenalty <= 0.0 || c.SignatureChangePenalty > 1.0 then
              yield "Counter.SignatureChangePenalty must be in (0, 1]" ]

    let validateRivalManager (c: RivalManagerConfig) : string list =
        let ranged (name: string) (value: float) (low: float) (high: float) =
            if not (System.Double.IsFinite value) || value < low || value > high then
                [ $"RivalManager.{name} must be in [{low}, {high}]" ]
            else
                []

        let ranged' (name: string) (value: int) (low: int) (high: int) =
            ranged name (float value) (float low) (float high)

        let formation (name: string) (value: string) =
            if
                FootballEngine.Domain.FormationLineups.all
                |> List.exists (fun f -> string f = value)
            then
                []
            else
                [ $"RivalManager.{name}.PreferredFormation '{value}' is not a known formation" ]

        let values (name: string) (low: int) (high: int) (v: FootballEngine.Domain.TacticalInstructions) =
            [ for field, value in
                  [ "Mentality", v.Mentality
                    "DefensiveLine", v.DefensiveLine
                    "PressingIntensity", v.PressingIntensity
                    "Width", v.Width
                    "Tempo", v.Tempo
                    "Directness", v.Directness
                    "PressTriggerZone", v.PressTriggerZone
                    "DefensiveShape", v.DefensiveShape ] do
                  yield! ranged' $"{name}.{field}" value low high ]

        let preset (name: string) (p: PhilosophyPreset) =
            formation name p.PreferredFormation @ values name 0 4 p.Values

        List.concat
            [ ranged' "StudyWindow" c.StudyWindow 1 100
              ranged "StudyRate" c.StudyRate 0.01 5.0
              ranged "ThreatPpgFloor" c.ThreatPpgFloor 0.0 2.9
              ranged "StrengthNoise" c.StrengthNoise 0.0 100.0
              ranged "UnderdogGap" c.UnderdogGap 0.0 200.0
              ranged "CounterThreatMin" c.CounterThreatMin 0.0 1.0
              ranged' "CopyImitationMin" c.CopyImitationMin 1 20
              ranged "CopySuitabilityMin" c.CopySuitabilityMin 0.0 1.0
              ranged' "RevengePrideMin" c.RevengePrideMin 1 20
              ranged' "HumiliationMargin" c.HumiliationMargin 1 20
              ranged "FormationSuitabilityMin" c.FormationSuitabilityMin 0.0 1.0
              ranged "CounterMaxSteps" c.CounterMaxSteps 0.0 4.0
              ranged "CounterFormationIntensity" c.CounterFormationIntensity 0.0 1.0
              ranged "CentreHeavyShare" c.CentreHeavyShare 0.0 1.0
              ranged "WideHeavyShare" c.WideHeavyShare 0.0 1.0
              ranged "DirectLongPassShare" c.DirectLongPassShare 0.0 1.0
              ranged "PossessionShortPassShare" c.PossessionShortPassShare 0.0 1.0
              ranged' "PressAnswerBoldness" c.PressAnswerBoldness 1 20
              values "RevengeShift" -4 4 c.RevengeShift
              values "UnderdogCautiousShift" -4 4 c.UnderdogCautiousShift
              values "UnderdogBoldShift" -4 4 c.UnderdogBoldShift
              ranged' "UnderdogBoldnessPivot" c.UnderdogBoldnessPivot 1 20
              ranged "RotationWeight" c.RotationWeight 0.0 2.0
              preset "PossessionPreset" c.PossessionPreset
              preset "PressingPreset" c.PressingPreset
              preset "CounterPreset" c.CounterPreset
              preset "DirectPreset" c.DirectPreset
              preset "FortressPreset" c.FortressPreset
              ranged' "ReviewIntervalMin" c.ReviewIntervalMin 1 30
              ranged "MaxReactionDelayMin" c.MaxReactionDelayMin 0.0 30.0
              ranged "TemperamentHaste" c.TemperamentHaste 0.0 1.0
              ranged' "MinMinutesBetweenMoves" c.MinMinutesBetweenMoves 0 45
              ranged' "HotheadMinutesBetweenMoves" c.HotheadMinutesBetweenMoves 0 c.MinMinutesBetweenMoves
              ranged "ReadNoise" c.ReadNoise 0.0 5.0
              ranged "MomentumEpsilon" c.MomentumEpsilon 0.001 5.0
              ranged "MomentumHalfLifeMin" c.MomentumHalfLifeMin 1.0 90.0
              ranged' "ReadSettleMinute" c.ReadSettleMinute 0 90
              ranged "StormMomentum" c.StormMomentum 0.0 1.0
              ranged "InitiativeMomentum" c.InitiativeMomentum 0.0 1.0
              ranged' "ChaseMinuteBold" c.ChaseMinuteBold 1 120
              ranged' "ChaseMinuteCautious" c.ChaseMinuteCautious c.ChaseMinuteBold 120
              ranged' "AllOutMinute" c.AllOutMinute 1 120
              ranged "AllOutBoldness" c.AllOutBoldness 0.0 1.0
              ranged' "ProtectMinuteCautious" c.ProtectMinuteCautious 1 c.ProtectMinuteBold
              ranged' "ProtectMinuteBold" c.ProtectMinuteBold 1 120
              ranged "ParkBusBoldness" c.ParkBusBoldness 0.0 1.0
              ranged "InitiativeBoldness" c.InitiativeBoldness 0.0 1.0
              ranged "SmartChangeReadingMin" c.SmartChangeReadingMin 0.0 1.0
              ranged' "CardRiskMinute" c.CardRiskMinute 1 120
              ranged "HotheadTemperament" c.HotheadTemperament 0.0 1.0
              ranged' "HotheadHalfTimeChanges" c.HotheadHalfTimeChanges 0 5
              ranged "ChaseMentalityShift" c.ChaseMentalityShift 0.0 1.0
              ranged "AllOutMentalityShift" c.AllOutMentalityShift 0.0 1.0
              ranged "ProtectMentalityDrop" c.ProtectMentalityDrop 0.0 1.0
              ranged "StormMentalityDrop" c.StormMentalityDrop 0.0 1.0
              ranged "InitiativeMentalityShift" c.InitiativeMentalityShift 0.0 1.0
              ranged "RedCardMentalityDrop" c.RedCardMentalityDrop 0.0 1.0
              ranged "AttackLineShift" c.AttackLineShift 0.0 20.0
              ranged "DefendLineShift" c.DefendLineShift 0.0 20.0
              ranged "CoachStrengthPoints" c.CoachStrengthPoints 0.0 20.0
              ranged' "CoachPeakEndAge" c.CoachPeakEndAge 30 80
              ranged "CoachGrowthRate" c.CoachGrowthRate 0.0 1.0
              ranged' "CoachDeclinePerYear" c.CoachDeclinePerYear 0 20
              ranged' "CoachSkillFloor" c.CoachSkillFloor 1 200
              ranged' "RivalMemoryYears" c.RivalMemoryYears 1 50 ]

    let validateTransfer (c: TransferConfig) : string list =
        [ if c.BenchWarmerAgeMin < 0 then
              yield "Transfer.BenchWarmerAgeMin must be >= 0"
          if c.MinMatchesForSquadPlayer < 0 then
              yield "Transfer.MinMatchesForSquadPlayer must be >= 0"
          if c.MaxOver25BenchWarmers < 0 then
              yield "Transfer.MaxOver25BenchWarmers must be >= 0"
          if c.OverpayTolerance < 0.0m then
              yield "Transfer.OverpayTolerance must be >= 0" ]

    let validateTraining (c: TrainingConfig) : string list =
        [ if
              c.AgeMultU18 < 0.0
              || c.AgeMultU21 < 0.0
              || c.AgeMultU25 < 0.0
              || c.AgeMultU28 < 0.0
          then
              yield "Training.AgeMult* must be >= 0"
          if c.DeclineStartAge < 25 || c.DeclineStartAge > 40 then
              yield "Training.DeclineStartAge must be in [25, 40]"
          if c.DeclineChance < 0.0 || c.DeclineChance > 1.0 then
              yield "Training.DeclineChance must be in [0, 1]"
          if c.DevelopChance < 0.0 || c.DevelopChance > 1.0 then
              yield "Training.DevelopChance must be in [0, 1]"
          if c.TrickleChance < 0.0 || c.TrickleChance > 1.0 then
              yield "Training.TrickleChance must be in [0, 1]"
          if c.MaintainChance < 0.0 || c.MaintainChance > 1.0 then
              yield "Training.MaintainChance must be in [0, 1]"
          if c.TiltBonus < 0.0 || c.TiltBonus > 1.0 then
              yield "Training.TiltBonus must be in [0, 1]"
          if c.TiredConditionThreshold < 0 || c.TiredConditionThreshold > 100 then
              yield "Training.TiredConditionThreshold must be in [0, 100]"
          if c.TiredGrowthPenalty < 0.0 || c.TiredGrowthPenalty > 1.0 then
              yield "Training.TiredGrowthPenalty must be in [0, 1]"
          if
              c.DevelopConditionCost < 0
              || c.PerformConditionGain < 0
              || c.RestConditionGain < 0
          then
              yield "Training plan condition deltas must be >= 0 (costs are subtracted)"
          if c.RehabConditionGain < 0 then
              yield "Training.RehabConditionGain must be >= 0"
          if c.DevelopInjuryAdd < 0.0 then
              yield "Training.DevelopInjuryAdd must be >= 0"
          if c.DevelopFitnessGain < 0 || c.PerformFitnessGain < 0 || c.RestFitnessLoss < 0 then
              yield "Training plan fitness deltas must be >= 0 (losses are subtracted)"
          if c.BaseInjuryRisk < 0.0 || c.BaseInjuryRisk > 1.0 then
              yield "Training.BaseInjuryRisk must be in [0, 1]"
          if c.TiredInjuryScale < 0.0 || c.TiredInjuryScale > 1.0 then
              yield "Training.TiredInjuryScale must be in [0, 1]"
          if c.ReadinessConditionWeight < 0.0 || c.ReadinessConditionWeight > 1.0 then
              yield "Training.ReadinessConditionWeight must be in [0, 1]"
          if c.ReadinessFloor < 0.0 || c.ReadinessFloor > 1.0 then
              yield "Training.ReadinessFloor must be in [0, 1]"
          if
              c.PostMatchConditionCost < 0
              || c.PostMatchFitnessGain < 0
              || c.IdleFitnessLoss < 0
          then
              yield "Training post-match deltas must be >= 0"
          if c.RecoveryConditionGain < 0 then
              yield "Training.RecoveryConditionGain must be >= 0"
          if c.CoachGrowthInfluence < 0.0 || c.CoachGrowthInfluence > 1.0 then
              yield "Training.CoachGrowthInfluence must be in [0, 1]"
          if c.YouthCoachBonus < 0.0 || c.YouthCoachBonus > 1.0 then
              yield "Training.YouthCoachBonus must be in [0, 1]"
          if c.PhysioInjuryReduction < 0.0 || c.PhysioInjuryReduction > 0.05 then
              yield "Training.PhysioInjuryReduction must be in [0, 0.05]" ]

    let validateTactics (c: TacticalTradeoffConfig) : string list =
        [ if c.GradeAThreshold <= c.GradeBThreshold then
              yield "Tactics.GradeAThreshold must be > GradeBThreshold"
          if c.GradeBThreshold <= c.GradeCThreshold then
              yield "Tactics.GradeBThreshold must be > GradeCThreshold"
          if c.GradeCThreshold <= c.GradeDThreshold then
              yield "Tactics.GradeCThreshold must be > GradeDThreshold"
          if c.PressHighConditionCost < 0.0 then
              yield "Tactics.PressHighConditionCost must be >= 0"
          if c.PressHighInjuryRisk < 0.0 then
              yield "Tactics.PressHighInjuryRisk must be >= 0"
          if c.DefensiveLineHighSprintCost < 0.0 then
              yield "Tactics.DefensiveLineHighSprintCost must be >= 0" ]

    let validateBall (b: BallConfig) : string list =
        [ // Gravity 9.81 is earth g — 0 would float forever, >20 is 2g and drops like stone
          for name, value in
              [ "Gravity", float b.Gravity
                "BounceRestitution", b.BounceRestitution
                "BounceFriction", b.BounceFriction
                "StopSpeedCutoff", float b.StopSpeedCutoff
                "MaxShotSpeed", float b.MaxShotSpeed
                "SpinDecay", b.SpinDecay ] do
              if not (System.Double.IsFinite value) then
                  yield $"Ball.{name} must be finite"
          if b.InertiaRatio > 1.0 then
              yield "Ball.InertiaRatio must be <= 1"
          if
              b.Gravity < 0.0<meterPerSecondSquared>
              || b.Gravity > 20.0<meterPerSecondSquared>
          then
              yield "Ball.Gravity must be in [0, 20] m/s²"
          for name, value in
              [ "AirDensity", b.AirDensity
                "Mass", b.Mass
                "DragSupercritical", b.DragSupercritical
                "DragCrisisSpeed", float b.DragCrisisSpeed
                "DragCrisisWidth", float b.DragCrisisWidth
                "LiftSlope", b.LiftSlope
                "MaxLift", b.MaxLift ] do
              if not (System.Double.IsFinite value) || value <= 0.0 then
                  yield $"Ball.{name} must be finite and > 0"
          // The crisis lowers drag; a supercritical Cd above the subcritical one would invert the physics.
          if
              not (System.Double.IsFinite b.DragSubcritical)
              || b.DragSubcritical < b.DragSupercritical
              || b.DragSubcritical > 1.5
          then
              yield "Ball.DragSubcritical must be finite, >= DragSupercritical and <= 1.5"
          if b.MaxLift > 1.0 then
              yield "Ball.MaxLift must be <= 1"
          for name, value in
              [ "WoodworkRestitution", b.WoodworkRestitution
                "WoodworkFriction", b.WoodworkFriction
                "NetRestitution", b.NetRestitution
                "NetFriction", b.NetFriction ] do
              if not (System.Double.IsFinite value) || value < 0.0 || value > 1.0 then
                  yield $"Ball.{name} must be finite and in [0, 1]"
          for name, value in
              [ "Radius", float b.Radius
                "InertiaRatio", b.InertiaRatio
                "FootReach", float b.FootReach
                "FootContactHeight", float b.FootContactHeight
                "CarryOffset", float b.CarryOffset
                "CarryMaxSpeed", float b.CarryMaxSpeed ] do
              if not (System.Double.IsFinite value) || value <= 0.0 then
                  yield $"Ball.{name} must be finite and > 0"
          for name, value in
              [ "SlipFriction", b.SlipFriction
                "RollingResistance", b.RollingResistance
                "CarryTechniqueError", b.CarryTechniqueError
                "PassRollingSpinRatio", b.PassRollingSpinRatio ] do
              if not (System.Double.IsFinite value) || value < 0.0 || value > 1.0 then
                  yield $"Ball.{name} must be finite and in [0, 1]"
          if
              b.KickWindup <= 0<subtick>
              || b.KickRecovery <= 0<subtick>
              || b.CarryTouchInterval <= 0<subtick>
          then
              yield "Ball contact durations must be > 0"
          if b.FootReach <= b.Radius || b.CarryOffset >= b.FootReach then
              yield "Ball contact geometry must keep the carry offset inside foot reach"
          // BounceRestitution >1 would create energy on bounce, <0 would invert
          if b.BounceRestitution < 0.0 || b.BounceRestitution > 1.0 then
              yield "Ball.BounceRestitution must be in [0, 1]"
          if b.BounceFriction < 0.0 || b.BounceFriction > 1.0 then
              yield "Ball.BounceFriction must be in [0, 1]"
          // StopSpeedCutoff >5 would snap ball while visibly moving, <0 never stops
          if
              b.StopSpeedCutoff < 0.0<meterPerSecond>
              || b.StopSpeedCutoff > 5.0<meterPerSecond>
          then
              yield "Ball.StopSpeedCutoff must be in [0, 5] m/s"
          // MaxShotSpeed 10 is weak, 40 is 144 km/h world-record — outside breaks xG
          if b.MaxShotSpeed < 10.0<meterPerSecond> || b.MaxShotSpeed > 40.0<meterPerSecond> then
              yield "Ball.MaxShotSpeed must be in [10, 40] m/s"
          if b.SpinDecay < 0.0 || b.SpinDecay > 1.0 then
              yield "Ball.SpinDecay must be in [0, 1]"
          // ReceptionGrace 20 subticks is 0.5s at 40Hz — beyond that too forgiving
          if b.ReceptionGrace < 0<subtick> || b.ReceptionGrace > 20<subtick> then
              yield "Ball.ReceptionGrace must be in [0, 20] subticks"
          if b.InterceptionBonus < 0.0 || b.InterceptionBonus > 1.0 then
              yield "Ball.InterceptionBonus must be in [0, 1]"
          if b.SaveReflexWeight < 0.0 || b.SaveReflexWeight > 1.0 then
              yield "Ball.SaveReflexWeight must be in [0, 1]"
          if b.SavePositioningWeight < 0.0 || b.SavePositioningWeight > 1.0 then
              yield "Ball.SavePositioningWeight must be in [0, 1]"
          // Beyond the penalty spot distance, the shootout keeper's positional penalty would dominate every kick.
          if
              b.ShootoutSaveDistancePenalty < 0.0<meter>
              || b.ShootoutSaveDistancePenalty > 50.0<meter>
          then
              yield "Ball.ShootoutSaveDistancePenalty must be in [0, 50] m"
          if b.ReceptionBase < 0.0 || b.ReceptionBase > 1.0 then
              yield "Ball.ReceptionBase must be in [0, 1]"
          if b.ReceptionSpacePenalty < 0.0 || b.ReceptionSpacePenalty > 1.0 then
              yield "Ball.ReceptionSpacePenalty must be in [0, 1]"
          if b.ShootoutSaveBaseProbability < 0.0 || b.ShootoutSaveBaseProbability > 1.0 then
              yield "Ball.ShootoutSaveBaseProbability must be in [0, 1]"
          if b.InterceptionPaceWeight < 0.0 || b.InterceptionPaceWeight > 1.0 then
              yield "Ball.InterceptionPaceWeight must be in [0, 1]"
          // Intent boost >5 would make intent dominate over pace
          if b.InterceptionIntentBoost < 0.0 || b.InterceptionIntentBoost > 5.0 then
              yield "Ball.InterceptionIntentBoost must be in [0, 5]" ]

    let validateGait (g: GaitConfig) : string list =
        [ // Human cadence spans roughly 1-5 steps per second from walk to sprint.
          if g.StepFrequencyBase < 0.5 || g.StepFrequencyBase > 3.0 then
              yield "Gait.StepFrequencyBase must be in [0.5, 3] Hz"
          if g.StepFrequencyPerSpeed < 0.0 || g.StepFrequencyPerSpeed > 1.0 then
              yield "Gait.StepFrequencyPerSpeed must be in [0, 1] Hz per m/s"
          if g.StepFrequencyMax < g.StepFrequencyBase || g.StepFrequencyMax > 6.0 then
              yield "Gait.StepFrequencyMax must be in [StepFrequencyBase, 6] Hz"
          if g.ReferenceLegLength < 0.6<meter> || g.ReferenceLegLength > 1.2<meter> then
              yield "Gait.ReferenceLegLength must be in [0.6, 1.2] m"
          if g.StandingSpeed < 0.0<meterPerSecond> || g.StandingSpeed > 1.0<meterPerSecond> then
              yield "Gait.StandingSpeed must be in [0, 1] m/s"
          // A half cycle is one step: the touch must fall inside the support foot's own step.
          if g.CarryTouchPhase < 0.0 || g.CarryTouchPhase >= 0.5 then
              yield "Gait.CarryTouchPhase must be in [0, 0.5)"
          // The swing cannot start before the touching foot's own stance has ended, nor after the contact.
          if g.CarrySwingLead <= 0.0 || g.CarrySwingLead > 0.5 then
              yield "Gait.CarrySwingLead must be in (0, 0.5]" ]

    let validateStrikeTechnique (t: StrikeTechniqueConfig) : string list =
        [ for name, s in [ "Inside", t.Inside; "Instep", t.Instep; "Outside", t.Outside ] do
              // More than ~0.2 rad (11 degrees) at no technique is a miskick, not a surface.
              if s.Spread < 0.0 || s.Spread > 0.2 then
                  yield $"StrikeTechnique.{name}.Spread must be in [0, 0.2] rad"
              // 2 rad/s per m/s is ~5 rev/s on a 15 m/s ball, the top of a curled free kick.
              if s.SideSpinPerSpeed < 0.0 || s.SideSpinPerSpeed > 2.0 then
                  yield $"StrikeTechnique.{name}.SideSpinPerSpeed must be in [0, 2] rad/s per m/s"
          if t.OutsideAngle <= 0.0 || t.OutsideAngle >= System.Math.PI / 2.0 then
              yield "StrikeTechnique.OutsideAngle must be in (0, pi/2) rad"
          if t.InstepSpeed <= 0.0<meterPerSecond> || t.InstepSpeed > 40.0<meterPerSecond> then
              yield "StrikeTechnique.InstepSpeed must be in (0, 40] m/s"
          if t.TechniqueShare < 0.0 || t.TechniqueShare > 1.0 then
              yield "StrikeTechnique.TechniqueShare must be in [0, 1]"
          if t.PressureRadius < 0.0<meter> || t.PressureRadius > 10.0<meter> then
              yield "StrikeTechnique.PressureRadius must be in [0, 10] m"
          if t.PressureSpreadPerOpponent < 0.0 || t.PressureSpreadPerOpponent > 2.0 then
              yield "StrikeTechnique.PressureSpreadPerOpponent must be in [0, 2]"
          if t.ComposureShield < 0.0 || t.ComposureShield > 1.0 then
              yield "StrikeTechnique.ComposureShield must be in [0, 1]"
          if t.OffBalanceSpread < 1.0 || t.OffBalanceSpread > 5.0 then
              yield "StrikeTechnique.OffBalanceSpread must be in [1, 5]" ]

    let validateMovement (m: MovementConfig) : string list =
        [ if m.CarryEffort < 0.0 || m.CarryEffort > 1.0 then
              yield "Movement.CarryEffort must be in [0, 1]"
          // Sprint 12 m/s is Bolt, 5 is jogging — outside not football
          if m.SprintSpeed < 5.0<meterPerSecond> || m.SprintSpeed > 12.0<meterPerSecond> then
              yield "Movement.SprintSpeed must be in [5, 12] m/s"
          if m.JogSpeed < 2.0<meterPerSecond> || m.JogSpeed > 8.0<meterPerSecond> then
              yield "Movement.JogSpeed must be in [2, 8] m/s"
          if m.WalkSpeed < 0.5<meterPerSecond> || m.WalkSpeed > 3.0<meterPerSecond> then
              yield "Movement.WalkSpeed must be in [0.5, 3] m/s"
          // Walk < Jog < Sprint is the speed invariant — otherwise gait collapses
          if m.JogSpeed >= m.SprintSpeed then
              yield "Movement.JogSpeed must be < SprintSpeed"
          if m.WalkSpeed >= m.JogSpeed then
              yield "Movement.WalkSpeed must be < JogSpeed"
          // Acceleration <0.5 sluggish, >10 instant teleport
          if
              m.Acceleration < 0.5<meterPerSecondSquared>
              || m.Acceleration > 10.0<meterPerSecondSquared>
          then
              yield "Movement.Acceleration must be in [0.5, 10] m/s²"
          if m.SeparationRadius < 0.5<meter> || m.SeparationRadius > 5.0<meter> then
              yield "Movement.SeparationRadius must be in [0.5, 5] m"
          // SeparationStrength >2 would explode crowding, <0 would invert
          if m.SeparationStrength < 0.0 || m.SeparationStrength > 2.0 then
              yield "Movement.SeparationStrength must be in [0, 2]"
          if m.TurningRate < 0.5<radianPerSecond> || m.TurningRate > 10.0<radianPerSecond> then
              yield "Movement.TurningRate must be in [0.5, 10] rad/s"
          if
              not (System.Double.IsFinite(float m.TurningAcceleration))
              || m.TurningAcceleration < 1.0<radianPerSecondSquared>
              || m.TurningAcceleration > 100.0<radianPerSecondSquared>
          then
              yield "Movement.TurningAcceleration must be in [1, 100] rad/s²"
          if
              not (System.Double.IsFinite(float m.FacingIntentDistance))
              || m.FacingIntentDistance < 0.05<meter>
              || m.FacingIntentDistance > 2.0<meter>
          then
              yield "Movement.FacingIntentDistance must be finite and in [0.05, 2] metres"
          if
              not (System.Double.IsFinite(float m.StandingHeight))
              || m.StandingHeight < 1.0<meter>
              || m.StandingHeight > 2.5<meter>
          then
              yield "Movement.StandingHeight must be finite and in [1, 2.5] metres"
          if
              not (System.Double.IsFinite(float m.JumpSpeed))
              || m.JumpSpeed < 1.0<meterPerSecond>
              || m.JumpSpeed > 5.0<meterPerSecond>
          then
              yield "Movement.JumpSpeed must be finite and in [1, 5] m/s"
          if
              not (System.Double.IsFinite(float m.AerialIntentRadius))
              || m.AerialIntentRadius < 0.1<meter>
              || m.AerialIntentRadius > 5.0<meter>
          then
              yield "Movement.AerialIntentRadius must be finite and in [0.1, 5] metres"
          // Smoothing outside [0,1] overshoots or inverts interpolation
          if m.Smoothing < 0.0 || m.Smoothing > 1.0 then
              yield "Movement.Smoothing must be in [0, 1]"
          if m.FatigueSpeedDecay < 0.0 || m.FatigueSpeedDecay > 1.0 then
              yield "Movement.FatigueSpeedDecay must be in [0, 1]"
          // ConditionDecay >0.01 would drain fully in 100s, <0 would heal while sprinting
          if m.ConditionDecayPerSecond < 0.0 || m.ConditionDecayPerSecond > 0.01 then
              yield "Movement.ConditionDecayPerSecond must be in [0, 0.01]"
          if m.RecoveryPerSecond < 0.0 || m.RecoveryPerSecond > 0.1 then
              yield "Movement.RecoveryPerSecond must be in [0, 0.1]"
          if m.ReactiveRadius < 0.5<meter> || m.ReactiveRadius > 10.0<meter> then
              yield "Movement.ReactiveRadius must be in [0.5, 10] m"
          if m.SetPieceAvoidRadius < 0.5<meter> || m.SetPieceAvoidRadius > 5.0<meter> then
              yield "Movement.SetPieceAvoidRadius must be in [0.5, 5] m"
          if m.ConditionImpactThreshold < 0.0 || m.ConditionImpactThreshold > 1.0 then
              yield "Movement.ConditionImpactThreshold must be in [0, 1]"
          if m.IntentFloor < 0.0 || m.IntentFloor > 1.0 then
              yield "Movement.IntentFloor must be in [0, 1]"
          if m.IntentHysteresis < 0.0 || m.IntentHysteresis > 0.5 then
              yield "Movement.IntentHysteresis must be in [0, 0.5]"
          // Floor + hysteresis >1 would push intent above 1 and break clamp
          if m.IntentFloor + m.IntentHysteresis > 1.0 then
              yield "Movement.IntentFloor + IntentHysteresis must be <= 1"
          if m.CommittedRunDuration < 1<subtick> || m.CommittedRunDuration > 2000<subtick> then
              yield "Movement.CommittedRunDuration must be in [1, 2000] subticks" ]

    let validateContact (c: ContactConfig) : string list =
        [ if c.BodyRadiusFallback < 0.2<meter> || c.BodyRadiusFallback > 0.6<meter> then
              yield "Contact.BodyRadiusFallback must be in [0.2, 0.6] m"
          if
              not (System.Double.IsFinite c.BodyRadiusScale)
              || c.BodyRadiusScale < 0.2
              || c.BodyRadiusScale > 0.8
          then
              yield "Contact.BodyRadiusScale must be finite and in [0.2, 0.8]"
          if
              not (System.Double.IsFinite c.CapsuleLowerHeightRatio)
              || not (System.Double.IsFinite c.CapsuleUpperHeightRatio)
              || c.CapsuleLowerHeightRatio < 0.0
              || c.CapsuleUpperHeightRatio > 1.0
              || c.CapsuleLowerHeightRatio >= c.CapsuleUpperHeightRatio
          then
              yield "Contact capsule height ratios must be finite, ordered and within [0, 1]"
          if c.HeadRadius <= 0.0<meter> || c.HeadRadius > 0.3<meter> then
              yield "Contact.HeadRadius must be in (0, 0.3] m"
          if c.LegRadius <= 0.0<meter> || c.LegRadius > 0.3<meter> then
              yield "Contact.LegRadius must be in (0, 0.3] m"
          if c.FootRadius <= 0.0<meter> || c.FootRadius > 0.3<meter> then
              yield "Contact.FootRadius must be in (0, 0.3] m"
          if c.StrikeSeparation < 0<subtick> || c.StrikeSeparation > 20<subtick> then
              yield "Contact.StrikeSeparation must be in [0, 20] subticks"
          if c.HandRadius <= 0.0<meter> || c.HandRadius > 0.25<meter> then
              yield "Contact.HandRadius must be in (0, 0.25] m"
          if
              not (System.Double.IsFinite(float c.FallbackMass))
              || c.FallbackMass < 40.0<kilogram>
              || c.FallbackMass > 150.0<kilogram>
          then
              yield "Contact.FallbackMass must be finite and in [40, 150] kg"
          if
              not (System.Double.IsFinite(float c.BallMass))
              || c.BallMass < 0.2<kilogram>
              || c.BallMass > 0.6<kilogram>
          then
              yield "Contact.BallMass must be finite and in [0.2, 0.6] kg"
          if
              not (System.Double.IsFinite c.BodyRestitution)
              || c.BodyRestitution < 0.0
              || c.BodyRestitution > 0.2
          then
              yield "Contact.BodyRestitution must be finite and in [0, 0.2]"
          if
              not (System.Double.IsFinite c.BodyFriction)
              || c.BodyFriction < 0.0
              || c.BodyFriction > 1.0
          then
              yield "Contact.BodyFriction must be finite and in [0, 1]"
          if
              not (System.Double.IsFinite c.BallBodyRestitution)
              || c.BallBodyRestitution < 0.0
              || c.BallBodyRestitution > 1.0
          then
              yield "Contact.BallBodyRestitution must be finite and in [0, 1]"
          if
              not (System.Double.IsFinite c.BallFootRestitution)
              || c.BallFootRestitution < 0.0
              || c.BallFootRestitution > 1.0
          then
              yield "Contact.BallFootRestitution must be finite and in [0, 1]"
          if
              not (System.Double.IsFinite c.BallBodyFriction)
              || c.BallBodyFriction < 0.0
              || c.BallBodyFriction > 1.0
          then
              yield "Contact.BallBodyFriction must be finite and in [0, 1]"
          if
              not (System.Double.IsFinite(float c.BallControlSpeed))
              || c.BallControlSpeed < 0.0<meterPerSecond>
              || c.BallControlSpeed > 8.0<meterPerSecond>
          then
              yield "Contact.BallControlSpeed must be finite and in [0, 8] m/s"
          if
              not (System.Double.IsFinite(float c.MaximumTrapSpeed))
              || c.MaximumTrapSpeed <= c.BallControlSpeed
          then
              yield "Contact.MaximumTrapSpeed must be finite and greater than BallControlSpeed"
          // Absorption is a share of the sliding speed: the best trap cannot absorb more than all of it.
          if
              c.TrapAbsorptionBase < 0.0
              || c.TrapAbsorptionSkill < 0.0
              || c.TrapAbsorptionBase + c.TrapAbsorptionSkill > 1.0
          then
              yield "Contact.TrapAbsorptionBase and TrapAbsorptionSkill must be non-negative and sum to at most 1"
          if
              c.TrapPoorRestitution < 0.0
              || c.BallFootRestitution + c.TrapPoorRestitution > 1.0
          then
              yield "Contact.TrapPoorRestitution must be non-negative and keep the trap's restitution within 1"
          if c.OffBalanceTrapPenalty < 1.0 || c.OffBalanceTrapPenalty > 4.0 then
              yield "Contact.OffBalanceTrapPenalty must be in [1, 4]"
          if
              not (System.Double.IsFinite c.SupportedInverseMassScale)
              || c.SupportedInverseMassScale < 0.05
              || c.SupportedInverseMassScale > 1.0
          then
              yield "Contact.SupportedInverseMassScale must be finite and in [0.05, 1]"
          if
              not (System.Double.IsFinite c.AirborneInverseMassScale)
              || c.AirborneInverseMassScale < 0.5
              || c.AirborneInverseMassScale > 2.0
          then
              yield "Contact.AirborneInverseMassScale must be finite and in [0.5, 2]"
          if
              not (System.Double.IsFinite c.StabilityRecoveryPerSecond)
              || c.StabilityRecoveryPerSecond < 0.0
              || c.StabilityRecoveryPerSecond > 5.0
          then
              yield "Contact.StabilityRecoveryPerSecond must be finite and in [0, 5]"
          if
              not (System.Double.IsFinite c.StaggerImpulseThreshold)
              || not (System.Double.IsFinite c.FallImpulseThreshold)
              || c.StaggerImpulseThreshold <= 0.0
              || c.FallImpulseThreshold <= c.StaggerImpulseThreshold
          then
              yield "Contact impulse thresholds must be finite, positive and ordered"
          if
              not (System.Double.IsFinite c.RecoverStabilityThreshold)
              || c.RecoverStabilityThreshold <= 0.0
              || c.RecoverStabilityThreshold > 1.0
          then
              yield "Contact.RecoverStabilityThreshold must be finite and in (0, 1]"
          if
              not (System.Double.IsFinite c.StaggerAccelerationScale)
              || c.StaggerAccelerationScale < 0.0
              || c.StaggerAccelerationScale > 1.0
          then
              yield "Contact.StaggerAccelerationScale must be finite and in [0, 1]"
          if
              not (System.Double.IsFinite c.FallingAccelerationScale)
              || c.FallingAccelerationScale < 0.0
              || c.FallingAccelerationScale > 1.0
          then
              yield "Contact.FallingAccelerationScale must be finite and in [0, 1]"
          if
              not (System.Double.IsFinite c.PositionCorrectionPercent)
              || c.PositionCorrectionPercent < 0.0
              || c.PositionCorrectionPercent > 0.8
          then
              yield "Contact.PositionCorrectionPercent must be finite and in [0, 0.8]"
          if
              c.MaximumPositionCorrection <= 0.0<meter>
              || c.MaximumPositionCorrection > 0.05<meter>
          then
              yield "Contact.MaximumPositionCorrection must be in (0, 0.05] m"
          if c.VelocitySlop < 0.0<meterPerSecond> || c.VelocitySlop > 0.5<meterPerSecond> then
              yield "Contact.VelocitySlop must be in [0, 0.5] m/s"
          if
              not (System.Double.IsFinite c.ToiTolerance)
              || c.ToiTolerance <= 0.0
              || c.ToiTolerance > 0.001
          then
              yield "Contact.ToiTolerance must be finite and in (0, 0.001]"
          if
              not (System.Double.IsFinite c.SimultaneousTolerance)
              || c.SimultaneousTolerance <= 0.0
              || c.SimultaneousTolerance > 0.01
          then
              yield "Contact.SimultaneousTolerance must be finite and in (0, 0.01]"
          if c.SolverIterations < 1 || c.SolverIterations > 32 then
              yield "Contact.SolverIterations must be in [1, 32]"
          if c.MaximumImpactIterations < 1 || c.MaximumImpactIterations > 64 then
              yield "Contact.MaximumImpactIterations must be in [1, 64]"
          if c.CurvedPathSegments < 1 || c.CurvedPathSegments > 16 then
              yield "Contact.CurvedPathSegments must be in [1, 16]"
          if c.MaximumPathError <= 0.0<meter> || c.MaximumPathError > 0.05<meter> then
              yield "Contact.MaximumPathError must be in (0, 0.05] m" ]

    let validateKeeper (k: KeeperConfig) : string list =
        [ for name, value, minimum, maximum in
              [ "ReactionBase", float k.ReactionBase, 0.05, 1.0
                "ReactionSkill", float k.ReactionSkill, 0.0, float k.ReactionBase
                "ArmReach", float k.ArmReach, 0.3, 1.2
                "ShoulderHeightRatio", k.ShoulderHeightRatio, 0.6, 0.95
                "DiveSetup", float k.DiveSetup, 0.0, 0.5
                "DiveSpeed", float k.DiveSpeed, 0.5, 8.0
                "MaxDive", float k.MaxDive, 0.0, 2.5
                "HandRadius", float k.HandRadius, 0.05, 0.3
                "HandError", float k.HandError, 0.0, 1.0
                "CatchSpeed", float k.CatchSpeed, 0.0, 45.0
                "ParryRestitution", k.ParryRestitution, 0.0, 1.0
                "HoldSeconds", float k.HoldSeconds, 0.0, 6.0 ] do
              if not (System.Double.IsFinite value) || value < minimum || value > maximum then
                  yield $"Keeper.{name} must be finite and in [{minimum}, {maximum}]"
          if k.HandWindow < 0<subtick> || k.HandWindow > 10<subtick> then
              yield "Keeper.HandWindow must be in [0, 10] subticks" ]

    let validateContactAction (c: ContactActionConfig) : string list =
        let durations =
            [ c.ContainPreparation
              c.ShieldPreparation
              c.ShoulderPreparation
              c.StandingTacklePreparation
              c.SlidingTacklePreparation
              c.TrapPreparation
              c.StrikePreparation
              c.HeaderPreparation ]

        [ if
              durations
              |> List.exists (fun duration -> duration < 1<subtick> || duration > 80<subtick>)
          then
              yield "ContactAction preparation durations must be in [1, 80] subticks"
          if c.RecoveryDuration < 1<subtick> || c.RecoveryDuration > 400<subtick> then
              yield "ContactAction.RecoveryDuration must be in [1, 400] subticks"
          if c.CommittedDuration < 1<subtick> || c.CommittedDuration > 80<subtick> then
              yield "ContactAction.CommittedDuration must be in [1, 80] subticks"
          if c.FootReach <= 0.0<meter> || c.FootReach > 1.2<meter> then
              yield "ContactAction.FootReach must be in (0, 1.2] m"
          if c.SlidingReach <= c.FootReach || c.SlidingReach > 2.5<meter> then
              yield "ContactAction.SlidingReach must exceed FootReach and be at most 2.5 m"
          if
              c.ReceptionTargetTolerance < 0.0<meter>
              || c.ReceptionTargetTolerance > 5.0<meter>
          then
              yield "ContactAction.ReceptionTargetTolerance must be in [0, 5] m"
          if c.TackleBodyClearance < 0.0<meter> || c.TackleBodyClearance > 1.0<meter> then
              yield "ContactAction.TackleBodyClearance must be in [0, 1] m"
          if c.HeadReach <= 0.0<meter> || c.HeadReach > 1.0<meter> then
              yield "ContactAction.HeadReach must be in (0, 1] m"
          if
              not (System.Double.IsFinite(float c.MaximumReorientation))
              || c.MaximumReorientation < 0.0<radian>
              || c.MaximumReorientation > 3.1416<radian>
          then
              yield "ContactAction.MaximumReorientation must be finite and in [0, pi]"
          if c.ExecutionErrorScale < 0.0<meter> || c.ExecutionErrorScale > 0.5<meter> then
              yield "ContactAction.ExecutionErrorScale must be in [0, 0.5] m"
          if c.MaximumExecutionError < 0.0<meter> || c.MaximumExecutionError > 1.0<meter> then
              yield "ContactAction.MaximumExecutionError must be in [0, 1] m"
          if c.ShieldOffset <= 0.0<meter> || c.ShieldOffset > 1.0<meter> then
              yield "ContactAction.ShieldOffset must be in (0, 1] m"
          if
              c.NewImpactThreshold < 0.0<meterPerSecond>
              || c.NewImpactThreshold > 5.0<meterPerSecond>
          then
              yield "ContactAction.NewImpactThreshold must be in [0, 5] m/s"
          if
              c.CarelessRelativeSpeed <= 0.0<meterPerSecond>
              || c.RecklessRelativeSpeed <= c.CarelessRelativeSpeed
              || c.ExcessiveRelativeSpeed <= c.RecklessRelativeSpeed
          then
              yield "ContactAction foul speed thresholds must be positive and strictly ordered"
          if
              c.DangerousBallFirstSpeed < c.RecklessRelativeSpeed
              || c.DangerousBallFirstSpeed > c.ExcessiveRelativeSpeed
          then
              yield "ContactAction.DangerousBallFirstSpeed must be between reckless and excessive thresholds" ]

    let validateActions (a: ActionsConfig) : string list =
        [ if a.PassBaseQuality < 0.0 || a.PassBaseQuality > 1.0 then
              yield "Actions.PassBaseQuality must be in [0, 1]"
          // >0.1 per meter would make 10m pass impossible
          if a.PassDistancePenaltyPerMeter < 0.0 || a.PassDistancePenaltyPerMeter > 0.1 then
              yield "Actions.PassDistancePenaltyPerMeter must be in [0, 0.1]"
          if a.PassLongThreshold < 5.0<meter> || a.PassLongThreshold > 50.0<meter> then
              yield "Actions.PassLongThreshold must be in [5, 50] m"
          if
              not (System.Double.IsFinite(float a.MaxPassExecutionDistance))
              || a.MaxPassExecutionDistance <= 0.0<meter>
          then
              yield "Actions.MaxPassExecutionDistance must be finite and positive"
          if a.PassVisionWeight < 0.0 || a.PassVisionWeight > 1.0 then
              yield "Actions.PassVisionWeight must be in [0, 1]"
          if a.PassTechnicalWeight < 0.0 || a.PassTechnicalWeight > 1.0 then
              yield "Actions.PassTechnicalWeight must be in [0, 1]"
          if a.PassJitterBase < 0.0 || a.PassJitterBase > 1.0 then
              yield "Actions.PassJitterBase must be in [0, 1]"
          if a.PassHeavyTouchChance < 0.0 || a.PassHeavyTouchChance > 1.0 then
              yield "Actions.PassHeavyTouchChance must be in [0, 1]"
          if
              a.PassArrivalSpeed < 1.0<meterPerSecond>
              || a.PassArrivalSpeed > 20.0<meterPerSecond>
          then
              yield "Actions.PassArrivalSpeed must be in [1, 20] m/s"
          if a.ShotQualityGate < 0.0 || a.ShotQualityGate > 1.0 then
              yield "Actions.ShotQualityGate must be in [0, 1]"
          // AngleSpread >2 rad would wrap around, <0 would invert
          for name, value, maximum in
              [ "ShotErrorBase", a.ShotErrorBase, 0.5
                "ShotErrorSkill", a.ShotErrorSkill, 0.5
                "ShotVerticalErrorRatio", a.ShotVerticalErrorRatio, 2.0
                "ShotPostMargin", float a.ShotPostMargin, float Pitch.GoalHalfWidth ] do
              if not (System.Double.IsFinite value) || value < 0.0 || value > maximum then
                  yield $"Actions.{name} must be finite and in [0, {maximum}]"
          if a.ShotDistanceToGoalMultiplier < 0.0 || a.ShotDistanceToGoalMultiplier > 0.1 then
              yield "Actions.ShotDistanceToGoalMultiplier must be in [0, 0.1]"
          // Beyond ~12 rev/s no foot can spin a ball; above that the curl would be fiction.
          if
              not (System.Double.IsFinite(float a.ShotCurlSpin))
              || a.ShotCurlSpin < 0.0<radianPerSecond>
              || a.ShotCurlSpin > 75.0<radianPerSecond>
          then
              yield "Actions.ShotCurlSpin must be in [0, 75] rad/s"
          // The fastest strikes on record are ~38 m/s; a slower base than a jog could not reach goal from range.
          if
              not (System.Double.IsFinite(float a.ShotSpeedBase))
              || a.ShotSpeedBase < 8.0<meterPerSecond>
              || a.ShotSpeedBase > 38.0<meterPerSecond>
          then
              yield "Actions.ShotSpeedBase must be in [8, 38] m/s"
          if
              not (System.Double.IsFinite(float a.ShotSpeedStrength))
              || a.ShotSpeedStrength < 0.0<meterPerSecond>
              || a.ShotSpeedStrength > 15.0<meterPerSecond>
          then
              yield "Actions.ShotSpeedStrength must be in [0, 15] m/s"
          if
              not (System.Double.IsFinite a.ShotPlacementSpeedCost)
              || a.ShotPlacementSpeedCost < 0.0
              || a.ShotPlacementSpeedCost > 0.8
          then
              yield "Actions.ShotPlacementSpeedCost must be in [0, 0.8]"
          if
              not (System.Double.IsFinite(float a.CrossArrivalHeight))
              || a.CrossArrivalHeight < 0.0<meter>
              || a.CrossArrivalHeight > 2.2<meter>
          then
              yield "Actions.CrossArrivalHeight must be in [0, 2.2] m"
          // Steepness 0 would flatten sigmoid, >20 would be step function
          if a.DuelSteepness < 0.1 || a.DuelSteepness > 20.0 then
              yield "Actions.DuelSteepness must be in [0.1, 20]"
          if a.DuelMomentumBonus < 0.0 || a.DuelMomentumBonus > 1.0 then
              yield "Actions.DuelMomentumBonus must be in [0, 1]"
          if a.DuelFoulSeverityBias < 0.0 || a.DuelFoulSeverityBias > 1.0 then
              yield "Actions.DuelFoulSeverityBias must be in [0, 1]"
          if a.CrossLateralnessMin < 0.0 || a.CrossLateralnessMin > 1.0 then
              yield "Actions.CrossLateralnessMin must be in [0, 1]"
          if a.CrossTargetRadius < 1.0<meter> || a.CrossTargetRadius > 30.0<meter> then
              yield "Actions.CrossTargetRadius must be in [1, 30] m"
          if a.GkPunchChance < 0.0 || a.GkPunchChance > 1.0 then
              yield "Actions.GkPunchChance must be in [0, 1]"
          if a.GkParryChance < 0.0 || a.GkParryChance > 1.0 then
              yield "Actions.GkParryChance must be in [0, 1]"
          // Punch + parry >1 would be two outcomes at once
          if a.GkPunchChance + a.GkParryChance > 1.0 then
              yield "Actions.GkPunchChance + GkParryChance must be <= 1"
          if
              a.GkDistributionPressureRadius < 1.0<meter>
              || a.GkDistributionPressureRadius > 30.0<meter>
          then
              yield "Actions.GkDistributionPressureRadius must be in [1, 30] m"
          if a.DribbleBaseSuccess < 0.0 || a.DribbleBaseSuccess > 1.0 then
              yield "Actions.DribbleBaseSuccess must be in [0, 1]"
          if a.DribbleRiskWeight < 0.0 || a.DribbleRiskWeight > 1.0 then
              yield "Actions.DribbleRiskWeight must be in [0, 1]"
          if a.TackleBaseSuccess < 0.0 || a.TackleBaseSuccess > 1.0 then
              yield "Actions.TackleBaseSuccess must be in [0, 1]"
          // Temperature 0 would div/0, >5 flattens decisions to random
          if a.DecisionSoftmaxTemperature < 0.1 || a.DecisionSoftmaxTemperature > 5.0 then
              yield "Actions.DecisionSoftmaxTemperature must be in [0.1, 5]"
          if a.PerceptionConeAngle < 0.0 || a.PerceptionConeAngle > 3.1416 then
              yield "Actions.PerceptionConeAngle must be in [0, pi]"
          if a.PerceptionConeRange < 5.0<meter> || a.PerceptionConeRange > 100.0<meter> then
              yield "Actions.PerceptionConeRange must be in [5, 100] m"
          if a.LaneClearBlockRadius < 0.5<meter> || a.LaneClearBlockRadius > 10.0<meter> then
              yield "Actions.LaneClearBlockRadius must be in [0.5, 10] m"
          if a.CognitiveFatigueSteepness < 0.1 || a.CognitiveFatigueSteepness > 10.0 then
              yield "Actions.CognitiveFatigueSteepness must be in [0.1, 10]"
          if a.CognitiveFatigueFloor < 0.0 || a.CognitiveFatigueFloor > 1.0 then
              yield "Actions.CognitiveFatigueFloor must be in [0, 1]"
          if a.MemoryPassFailWeight < 0.0 || a.MemoryPassFailWeight > 1.0 then
              yield "Actions.MemoryPassFailWeight must be in [0, 1]"
          if a.MemoryDuelWeight < 0.0 || a.MemoryDuelWeight > 1.0 then
              yield "Actions.MemoryDuelWeight must be in [0, 1]"
          // Decay >1 would amplify memory, <0 would invert
          if a.MemoryMentalDecay < 0.0 || a.MemoryMentalDecay > 1.0 then
              yield "Actions.MemoryMentalDecay must be in [0, 1]"
          if a.ReflexDecisionRadius <= 0.0<meter> then
              yield "Actions.ReflexDecisionRadius must be positive" ]

    let validateSetPiece (s: SetPieceConfig) : string list =
        [ if s.KickoffDelay < 0.0<second> || s.KickoffDelay > 10.0<second> then
              yield "SetPiece.KickoffDelay must be in [0, 10] s"
          if s.ThrowInDelay < 0.0<second> || s.ThrowInDelay > 10.0<second> then
              yield "SetPiece.ThrowInDelay must be in [0, 10] s"
          if s.CornerDelay < 0.0<second> || s.CornerDelay > 10.0<second> then
              yield "SetPiece.CornerDelay must be in [0, 10] s"
          if s.GoalKickDelay < 0.0<second> || s.GoalKickDelay > 10.0<second> then
              yield "SetPiece.GoalKickDelay must be in [0, 10] s"
          if s.FreeKickDelay < 0.0<second> || s.FreeKickDelay > 10.0<second> then
              yield "SetPiece.FreeKickDelay must be in [0, 10] s"
          if s.PenaltyDelay < 0.0<second> || s.PenaltyDelay > 15.0<second> then
              yield "SetPiece.PenaltyDelay must be in [0, 15] s"
          if s.WallDistance < 5.0<meter> || s.WallDistance > 15.0<meter> then
              yield "SetPiece.WallDistance must be in [5, 15] m"
          if s.WallPlayers < 0 || s.WallPlayers > 10 then
              yield "SetPiece.WallPlayers must be in [0, 10]"
          if s.PenaltyQualityBonus < 0.0 || s.PenaltyQualityBonus > 1.0 then
              yield "SetPiece.PenaltyQualityBonus must be in [0, 1]"
          if s.FreeKickShotChance < 0.0 || s.FreeKickShotChance > 1.0 then
              yield "SetPiece.FreeKickShotChance must be in [0, 1]"
          if s.CornerTargetRadius < 1.0<meter> || s.CornerTargetRadius > 30.0<meter> then
              yield "SetPiece.CornerTargetRadius must be in [1, 30] m"
          if s.DirectFreeKickRange < 10.0<meter> || s.DirectFreeKickRange > 50.0<meter> then
              yield "SetPiece.DirectFreeKickRange must be in [10, 50] m"
          if s.WallArcSpread < 0.0<meter> || s.WallArcSpread > 5.0<meter> then
              yield "SetPiece.WallArcSpread must be in [0, 5] m"
          if s.CornerNearZoneDist < 1.0<meter> || s.CornerNearZoneDist > 20.0<meter> then
              yield "SetPiece.CornerNearZoneDist must be in [1, 20] m"
          if s.CornerFarZoneDist < 5.0<meter> || s.CornerFarZoneDist > 30.0<meter> then
              yield "SetPiece.CornerFarZoneDist must be in [5, 30] m"
          if s.CornerNearZoneDist >= s.CornerFarZoneDist then
              yield "SetPiece.CornerNearZoneDist must be < CornerFarZoneDist"
          if s.CornerTakerOffset <= 0.0<meter> || s.CornerTakerOffset > 1.0<meter> then
              yield "SetPiece.CornerTakerOffset must be in (0, 1] m"
          if s.BallClearance < 1.0<meter> || s.BallClearance > s.WallDistance then
              yield "SetPiece.BallClearance must be in [1, WallDistance] m"
          if s.TargetTolerance <= 0.0<meter> then
              yield "SetPiece.TargetTolerance must be positive"
          if s.MaxTargetDistance <= 0.0<meter> then
              yield "SetPiece.MaxTargetDistance must be positive"
          if s.ThrowInReceiverInset <= 0.0<meter> || s.ThrowInReceiverInset > 15.0<meter> then
              yield "SetPiece.ThrowInReceiverInset must be in (0, 15] m" ]

    let validateTeam (t: TeamConfig) : string list =
        [ if t.UtilityAttackingWeight < 0.0 || t.UtilityAttackingWeight > 5.0 then
              yield "Team.UtilityAttackingWeight must be in [0, 5]"
          if t.UtilityDefendingWeight < 0.0 || t.UtilityDefendingWeight > 5.0 then
              yield "Team.UtilityDefendingWeight must be in [0, 5]"
          if t.UtilityPressWeight < 0.0 || t.UtilityPressWeight > 5.0 then
              yield "Team.UtilityPressWeight must be in [0, 5]"
          if t.UtilityCounterWeight < 0.0 || t.UtilityCounterWeight > 5.0 then
              yield "Team.UtilityCounterWeight must be in [0, 5]"
          if t.UtilityBalancedWeight < 0.0 || t.UtilityBalancedWeight > 5.0 then
              yield "Team.UtilityBalancedWeight must be in [0, 5]"
          // Hysteresis >1 would never switch directive, <0 would flicker
          if t.DirectiveHysteresis < 0.0 || t.DirectiveHysteresis > 1.0 then
              yield "Team.DirectiveHysteresis must be in [0, 1]"
          if t.OpponentShapeWindow < 1<subtick> || t.OpponentShapeWindow > 2000<subtick> then
              yield "Team.OpponentShapeWindow must be in [1, 2000] subticks"
          if t.OpponentPressureRadius < 1.0<meter> || t.OpponentPressureRadius > 30.0<meter> then
              yield "Team.OpponentPressureRadius must be in [1, 30] m"
          if t.UrgencyMaxDeficit < 0 || t.UrgencyMaxDeficit > 5 then
              yield "Team.UrgencyMaxDeficit must be in [0, 5]"
          if
              t.MomentumStreakHalfLife < 1<subtick>
              || t.MomentumStreakHalfLife > 5000<subtick>
          then
              yield "Team.MomentumStreakHalfLife must be in [1, 5000] subticks"
          if t.TransitionWindow < 1<subtick> || t.TransitionWindow > 2000<subtick> then
              yield "Team.TransitionWindow must be in [1, 2000] subticks"
          // Shape baseline 42m is mid-pitch — <10 would be own goal, >80 offside trap impossible
          if t.ShapeBaseLine < 10.0<meter> || t.ShapeBaseLine > 80.0<meter> then
              yield "Team.ShapeBaseLine must be in [10, 80] m"
          if t.ShapeBallPull < 0.0 || t.ShapeBallPull > 1.0 then
              yield "Team.ShapeBallPull must be in [0, 1]"
          if t.ShapeCompactShift < 0.0<meter> || t.ShapeCompactShift > 20.0<meter> then
              yield "Team.ShapeCompactShift must be in [0, 20] m"
          if t.ShapeWidthSpread < 0.0<meter> || t.ShapeWidthSpread > 20.0<meter> then
              yield "Team.ShapeWidthSpread must be in [0, 20] m"
          // Emergent steps >0.5 would oscillate team shape each tick
          if t.EmergentCompactnessStep < 0.0 || t.EmergentCompactnessStep > 0.5 then
              yield "Team.EmergentCompactnessStep must be in [0, 0.5]"
          if t.EmergentPressingStep < 0.0 || t.EmergentPressingStep > 0.5 then
              yield "Team.EmergentPressingStep must be in [0, 0.5]"
          if t.EmergentWingplayStep < 0.0 || t.EmergentWingplayStep > 0.5 then
              yield "Team.EmergentWingplayStep must be in [0, 0.5]"
          if t.ChemistryCohesionWeight < 0.0 || t.ChemistryCohesionWeight > 1.0 then
              yield "Team.ChemistryCohesionWeight must be in [0, 1]"
          if t.RoleBiasStrength < 0.0 || t.RoleBiasStrength > 1.0 then
              yield "Team.RoleBiasStrength must be in [0, 1]"
          if t.RolePressRadius < 1.0<meter> || t.RolePressRadius > 30.0<meter> then
              yield "Team.RolePressRadius must be in [1, 30] m"
          if t.RoleAnchorRadius < 5.0<meter> || t.RoleAnchorRadius > 100.0<meter> then
              yield "Team.RoleAnchorRadius must be in [5, 100] m"
          if t.WinProbGoalDiffWeight < 0.0 || t.WinProbGoalDiffWeight > 2.0 then
              yield "Team.WinProbGoalDiffWeight must be in [0, 2]"
          if t.WinProbXgDiffWeight < 0.0 || t.WinProbXgDiffWeight > 2.0 then
              yield "Team.WinProbXgDiffWeight must be in [0, 2]"
          if t.WinProbMomentumWeight < 0.0 || t.WinProbMomentumWeight > 2.0 then
              yield "Team.WinProbMomentumWeight must be in [0, 2]"
          // Minute weight >0.1 would make minute dominate over goals
          if t.WinProbMinuteWeight < 0.0 || t.WinProbMinuteWeight > 0.1 then
              yield "Team.WinProbMinuteWeight must be in [0, 0.1]"
          if t.RiskLateMinute < 0<matchMin> || t.RiskLateMinute > 120<matchMin> then
              yield "Team.RiskLateMinute must be in [0, 120] min"
          if t.PatternWindow < 1<subtick> || t.PatternWindow > 5000<subtick> then
              yield "Team.PatternWindow must be in [1, 5000] subticks"
          if t.PatternCenterBias < 0.0 || t.PatternCenterBias > 1.0 then
              yield "Team.PatternCenterBias must be in [0, 1]"
          if t.PatternWingBias < 0.0 || t.PatternWingBias > 1.0 then
              yield "Team.PatternWingBias must be in [0, 1]"
          if t.TacticalFamiliarityGain < 0.0 || t.TacticalFamiliarityGain > 1.0 then
              yield "Team.TacticalFamiliarityGain must be in [0, 1]"
          if t.LooseBallCoverDistance < 2.0<meter> || t.LooseBallCoverDistance > 20.0<meter> then
              yield "Team.LooseBallCoverDistance must be in [2, 20] m"
          if t.RestDefenseDistance < 5.0<meter> || t.RestDefenseDistance > 40.0<meter> then
              yield "Team.RestDefenseDistance must be in [5, 40] m"
          if t.WeakSideSeparation < 2.0<meter> || t.WeakSideSeparation > 30.0<meter> then
              yield "Team.WeakSideSeparation must be in [2, 30] m" ]

    let validatePositioning (p: PositioningConfig) : string list =
        [ if p.EvalTeammateWeight < 0.0 || p.EvalTeammateWeight > 1.0 then
              yield "Positioning.EvalTeammateWeight must be in [0, 1]"
          if p.EvalOpponentWeight < 0.0 || p.EvalOpponentWeight > 1.0 then
              yield "Positioning.EvalOpponentWeight must be in [0, 1]"
          if p.EvalGoalWeight < 0.0 || p.EvalGoalWeight > 1.0 then
              yield "Positioning.EvalGoalWeight must be in [0, 1]"
          if p.EvalSearchRadius < 0.5<meter> || p.EvalSearchRadius > 20.0<meter> then
              yield "Positioning.EvalSearchRadius must be in [0.5, 20] m"
          // Candidate 0 would evaluate none, >20 hurts perf
          if p.EvalCandidateCount < 1 || p.EvalCandidateCount > 20 then
              yield "Positioning.EvalCandidateCount must be in [1, 20]"
          if p.RefineGroups < 1 || p.RefineGroups > 11 then
              yield "Positioning.RefineGroups must be in [1, 11]"
          if p.EvalMinTeammateDist < 0.5<meter> || p.EvalMinTeammateDist > 20.0<meter> then
              yield "Positioning.EvalMinTeammateDist must be in [0.5, 20] m"
          if p.EvalOppOurWeight < 0.0 || p.EvalOppOurWeight > 1.0 then
              yield "Positioning.EvalOppOurWeight must be in [0, 1]"
          if p.LateralSwapThreshold < 0.1<meter> || p.LateralSwapThreshold > 10.0<meter> then
              yield "Positioning.LateralSwapThreshold must be in [0.1, 10] m"
          if p.TargetInset < 0.5<meter> || p.TargetInset > 8.0<meter> then
              yield "Positioning.TargetInset must be in [0.5, 8] m"
          if p.CarrySearchRadius < 1.0<meter> || p.CarrySearchRadius > 15.0<meter> then
              yield "Positioning.CarrySearchRadius must be in [1, 15] m"
          if p.SupportForwardDistance < 1.0<meter> || p.SupportForwardDistance > 12.0<meter> then
              yield "Positioning.SupportForwardDistance must be in [1, 12] m"
          if
              not (System.Double.IsFinite(float p.CarryCutInsideDepth))
              || p.CarryCutInsideDepth < 0.0<meter>
              || p.CarryCutInsideDepth > 20.0<meter>
          then
              yield "Positioning.CarryCutInsideDepth must be finite and in [0, 20] m"
          if p.SupportLateralDistance < 2.0<meter> || p.SupportLateralDistance > 20.0<meter> then
              yield "Positioning.SupportLateralDistance must be in [2, 20] m"
          if p.GkMaxAdvance < 0.0<meter> || p.GkMaxAdvance > 20.0<meter> then
              yield "Positioning.GkMaxAdvance must be in [0, 20] m"
          if
              not (System.Double.IsFinite p.GkDepthRatio)
              || p.GkDepthRatio < 0.0
              || p.GkDepthRatio > 1.0
          then
              yield "Positioning.GkDepthRatio must be finite and in [0, 1]" ]

    let validateIntercept (c: InterceptConfig) : string list =
        [ // Control radius <0.5 would be feet only, >10 would be telekinesis
          if c.ControlRadius < 0.5<meter> || c.ControlRadius > 10.0<meter> then
              yield "Intercept.ControlRadius must be in [0.5, 10] m"
          // MaxSteps 0 would never intercept, >200 hurts perf per RoboCup table
          if c.MaxSteps < 1 || c.MaxSteps > 200 then
              yield "Intercept.MaxSteps must be in [1, 200]"
          // Finer than 1 mm/s is memory for no accuracy; coarser than 0.1 m/s makes interpolation visible
          if
              c.RollTableResolution < 0.001<meterPerSecond>
              || c.RollTableResolution > 0.1<meterPerSecond>
          then
              yield "Intercept.RollTableResolution must be in [0.001, 0.1] m/s"
          // Zero would silently disable the table; the cap and the node budget bound its memory
          if
              c.RollTableMaxSpeed <= 0.0<meterPerSecond>
              || c.RollTableMaxSpeed > 100.0<meterPerSecond>
          then
              yield "Intercept.RollTableMaxSpeed must be in (0, 100] m/s"
          elif
              c.RollTableResolution >= 0.001<meterPerSecond>
              && (float c.RollTableMaxSpeed / float c.RollTableResolution + 1.0)
                 * float (c.MaxSteps + 1) > 1e7
          then
              yield
                  "Intercept roll table (RollTableMaxSpeed / RollTableResolution nodes x MaxSteps) must stay under 1e7 values" ]

    let validateSituation (c: SituationConfig) : string list =
        [ // LeadSteps negative would invert who leads, >10 would delay phase forever
          if c.LeadSteps < 0 || c.LeadSteps > 10 then
              yield "Situation.LeadSteps must be in [0, 10]" ]

    let validatePassIntercept (c: PassInterceptConfig) : string list =
        [ // Risk is step margin ball needs over interceptor — <-5 always safe, >10 impossible
          if c.BaseRisk < -5 || c.BaseRisk > 10 then
              yield "PassIntercept.BaseRisk must be in [-5, 10]"
          if c.OffsideCrossRisk < -5 || c.OffsideCrossRisk > 10 then
              yield "PassIntercept.OffsideCrossRisk must be in [-5, 10]" ]

    let validateReferee (r: RefereeConfig) : string list =
        [ if r.FoulMinorChance < 0.0 || r.FoulMinorChance > 1.0 then
              yield "Referee.FoulMinorChance must be in [0, 1]"
          if r.FoulTacticalChance < 0.0 || r.FoulTacticalChance > 1.0 then
              yield "Referee.FoulTacticalChance must be in [0, 1]"
          if r.FoulYellowWorthyChance < 0.0 || r.FoulYellowWorthyChance > 1.0 then
              yield "Referee.FoulYellowWorthyChance must be in [0, 1]"
          if r.FoulRedWorthyChance < 0.0 || r.FoulRedWorthyChance > 1.0 then
              yield "Referee.FoulRedWorthyChance must be in [0, 1]"
          // A harsher severity can never be sanctioned less often than a milder one.
          if
              r.FoulMinorChance > r.FoulTacticalChance
              || r.FoulTacticalChance > r.FoulYellowWorthyChance
          then
              yield "Referee foul sanction chances must not decrease with severity"
          if r.VarGoalReviewDelay < 0.0<second> || r.VarGoalReviewDelay > 60.0<second> then
              yield "Referee.VarGoalReviewDelay must be in [0, 60] s"
          // Offside margin >1m would forgive obvious offsides
          if r.VarOffsideMargin < 0.0<meter> || r.VarOffsideMargin > 1.0<meter> then
              yield "Referee.VarOffsideMargin must be in [0, 1] m"
          if r.AdvantageWindow < 1<subtick> || r.AdvantageWindow > 500<subtick> then
              yield "Referee.AdvantageWindow must be in [1, 500] subticks"
          if r.HomeCardBonus < 0.0 || r.HomeCardBonus > 1.0 then
              yield "Referee.HomeCardBonus must be in [0, 1]"
          if r.CardAccumulationThreshold < 1 || r.CardAccumulationThreshold > 10 then
              yield "Referee.CardAccumulationThreshold must be in [1, 10]"
          if r.BookedLeniency < 0.0 || r.BookedLeniency > 1.0 then
              yield "Referee.BookedLeniency must be in [0, 1]"
          if r.BookedRecklessLeniency < 0.0 || r.BookedRecklessLeniency > 1.0 then
              yield "Referee.BookedRecklessLeniency must be in [0, 1]"
          if r.LastManFactor < 0.0 || r.LastManFactor > 1.0 then
              yield "Referee.LastManFactor must be in [0, 1]"
          if r.PromisingAttackFactor < 0.0 || r.PromisingAttackFactor > 1.0 then
              yield "Referee.PromisingAttackFactor must be in [0, 1]"
          if r.ZoneFactor < 0.0 || r.ZoneFactor > 1.0 then
              yield "Referee.ZoneFactor must be in [0, 1]" ]

    let validateManager (m: ManagerConfig) : string list =
        [ if m.MaxSubs < 0 || m.MaxSubs > 7 then
              yield "Manager.MaxSubs must be in [0, 7]"
          if m.FirstSubWindowMin < 0<matchMin> || m.FirstSubWindowMin > 120<matchMin> then
              yield "Manager.FirstSubWindowMin must be in [0, 120] min"
          if m.SecondSubWindowMin < 0<matchMin> || m.SecondSubWindowMin > 120<matchMin> then
              yield "Manager.SecondSubWindowMin must be in [0, 120] min"
          if m.ThirdSubWindowMin < 0<matchMin> || m.ThirdSubWindowMin > 120<matchMin> then
              yield "Manager.ThirdSubWindowMin must be in [0, 120] min"
          // Windows must be ordered — otherwise sub logic picks wrong window
          if m.FirstSubWindowMin >= m.SecondSubWindowMin then
              yield "Manager.FirstSubWindowMin must be < SecondSubWindowMin"
          if m.SecondSubWindowMin >= m.ThirdSubWindowMin then
              yield "Manager.SecondSubWindowMin must be < ThirdSubWindowMin"
          if m.ConditionSubThreshold < 0.0 || m.ConditionSubThreshold > 1.0 then
              yield "Manager.ConditionSubThreshold must be in [0, 1]"
          if m.AttackingSubGoalDeficit < 0 || m.AttackingSubGoalDeficit > 5 then
              yield "Manager.AttackingSubGoalDeficit must be in [0, 5]"
          if m.InjurySubCondition < 0.0 || m.InjurySubCondition > 1.0 then
              yield "Manager.InjurySubCondition must be in [0, 1]"
          if m.DrawAttackingMinute < 0<matchMin> || m.DrawAttackingMinute > 120<matchMin> then
              yield "Manager.DrawAttackingMinute must be in [0, 120] min"
          if
              m.WinningDefensiveMinute < 0<matchMin>
              || m.WinningDefensiveMinute > 120<matchMin>
          then
              yield "Manager.WinningDefensiveMinute must be in [0, 120] min" ]

    let validateEnvironment (e: EnvironmentConfig) : string list =
        [ // Home advantage >1 would be superhuman, <0 would be away advantage
          if e.HomeAdvantageStrength < 0.0 || e.HomeAdvantageStrength > 1.0 then
              yield "Environment.HomeAdvantageStrength must be in [0, 1]"
          if e.CrowdInfluence < 0.0 || e.CrowdInfluence > 1.0 then
              yield "Environment.CrowdInfluence must be in [0, 1]"
          if e.WeatherEffect < 0.0 || e.WeatherEffect > 1.0 then
              yield "Environment.WeatherEffect must be in [0, 1]"
          // Pitch quality 0 is mud, 2 is perfect — >2 would be frictionless
          if e.PitchQuality < 0.0 || e.PitchQuality > 2.0 then
              yield "Environment.PitchQuality must be in [0, 2]" ]

    let validateMatch (m: MatchConfig) : string list =
        [ if m.MatchImportanceWeight < 0.0 || m.MatchImportanceWeight > 5.0 then
              yield "Match.MatchImportanceWeight must be in [0, 5]"
          if m.StoppageBasePerHalf < 0.0<second> || m.StoppageBasePerHalf > 300.0<second> then
              yield "Match.StoppageBasePerHalf must be in [0, 300] s"
          if m.StoppagePerGoal < 0.0<second> || m.StoppagePerGoal > 120.0<second> then
              yield "Match.StoppagePerGoal must be in [0, 120] s"
          if m.StoppagePerCard < 0.0<second> || m.StoppagePerCard > 120.0<second> then
              yield "Match.StoppagePerCard must be in [0, 120] s"
          if m.StoppagePerSub < 0.0<second> || m.StoppagePerSub > 120.0<second> then
              yield "Match.StoppagePerSub must be in [0, 120] s"
          if m.StoppagePerFoul < 0.0<second> || m.StoppagePerFoul > 30.0<second> then
              yield "Match.StoppagePerFoul must be in [0, 30] s"
          if m.StoppageMax < 0.0<second> || m.StoppageMax > 600.0<second> then
              yield "Match.StoppageMax must be in [0, 600] s"
          // Max must cover at least the base — otherwise stoppage is immediately capped
          if m.StoppageMax < m.StoppageBasePerHalf then
              yield "Match.StoppageMax must be >= StoppageBasePerHalf"
          if m.HalfTimeDuration < 60.0<second> || m.HalfTimeDuration > 1800.0<second> then
              yield "Match.HalfTimeDuration must be in [60, 1800] s"
          if m.GoalPauseDuration < 0.0<second> || m.GoalPauseDuration > 120.0<second> then
              yield "Match.GoalPauseDuration must be in [0, 120] s"
          if m.VarReviewDuration < 0.0<second> || m.VarReviewDuration > 60.0<second> then
              yield "Match.VarReviewDuration must be in [0, 60] s"
          if m.InjuryPauseDuration < 0.0<second> || m.InjuryPauseDuration > 300.0<second> then
              yield "Match.InjuryPauseDuration must be in [0, 300] s"
          if
              m.FullTimeReviewDuration < 0.0<second>
              || m.FullTimeReviewDuration > 30.0<second>
          then
              yield "Match.FullTimeReviewDuration must be in [0, 30] s"
          if
              m.ExtraTimePeriodDuration < 60.0<second>
              || m.ExtraTimePeriodDuration > 3600.0<second>
          then
              yield "Match.ExtraTimePeriodDuration must be in [60, 3600] s"
          if m.ShootoutRounds < 3 || m.ShootoutRounds > 10 then
              yield "Match.ShootoutRounds must be in [3, 10]" ]

    let validateAll (config: BalanceConfig) : Result<unit, string list> =
        let attack = config.CollectiveAttack

        let attackErrors =
            [ if
                  not (System.Double.IsFinite config.Actions.PassArrivalSpeedGain)
                  || config.Actions.PassArrivalSpeedGain < 0.0
                  || config.Actions.PassArrivalSpeedGain > 0.5
              then
                  yield "Actions.PassArrivalSpeedGain must be finite and in [0, 0.5]"
              for name, value, maximum in
                  [ "SupportDepth", float attack.SupportDepth, 25.0
                    "SupportWidth", float attack.SupportWidth, 25.0
                    "RunDepth", float attack.RunDepth, 30.0
                    "SwitchWidth", float attack.SwitchWidth, float Pitch.HalfWidth
                    "SecurityDepth", float attack.SecurityDepth, 25.0
                    "OffsideMargin", float attack.OffsideMargin, 5.0
                    "MaxArrival", float attack.MaxArrival, 10.0
                    "LaneRadius", float attack.LaneRadius, 5.0
                    "SpaceScale", float attack.SpaceScale, 30.0
                    "PriorStrength", attack.PriorStrength, 100.0
                    "EvidenceCap", attack.EvidenceCap, 1000.0
                    "TerritoryBoundary", float attack.TerritoryBoundary, float Pitch.HalfLength
                    "AnticipationHorizon", float attack.AnticipationHorizon, 10.0
                    "MinimumDutySeparation", float attack.MinimumDutySeparation, 10.0
                    "PressureRadius", float attack.PressureRadius, 30.0 ] do
                  if not (System.Double.IsFinite value) || value <= 0.0 || value > maximum then
                      yield sprintf "CollectiveAttack.%s must be finite and in (0, %.2f]" name maximum
              for name, value in
                  [ "MinimumCondition", attack.MinimumCondition
                    "MinimumScore", attack.MinimumScore
                    "LearningWeight", attack.LearningWeight
                    "Exploration", attack.Exploration
                    "SeasonalRetention", attack.SeasonalRetention
                    "CooperationWeight", attack.CooperationWeight
                    "ProgressReward", attack.ProgressReward
                    "SpaceReward", attack.SpaceReward
                    "ConnectionReward", attack.ConnectionReward
                    "ThreatReward", attack.ThreatReward
                    "LossPenalty", attack.LossPenalty
                    "IdlePenalty", attack.IdlePenalty
                    "ExposurePenalty", attack.ExposurePenalty
                    "AnticipationConfidence", attack.AnticipationConfidence ] do
                  if not (System.Double.IsFinite value) || value < 0.0 || value > 1.0 then
                      yield sprintf "CollectiveAttack.%s must be finite and in [0, 1]" name
              for name, value in
                  [ "Duration", attack.Duration
                    "Recovery", attack.Recovery
                    "CounterHold", attack.CounterHold ] do
                  if value < 1<subtick> || value > 2400<subtick> then
                      yield sprintf "CollectiveAttack.%s must be in [1, 2400]" name
              if attack.CounterHold >= attack.Duration then
                  yield "CollectiveAttack.CounterHold must be shorter than Duration"
              if attack.PressureCount < 1 || attack.PressureCount > 10 then
                  yield "CollectiveAttack.PressureCount must be in [1, 10]"
              if config.Planner.BeamWidth < 1 || config.Planner.BeamWidth > 8 then
                  yield "Planner.BeamWidth must be in [1, 8]"
              for name, value, maximum in
                  [ "DribbleDuration", float config.Planner.DribbleDuration, 3.0
                    "MinimumPassDistance", float config.Planner.MinimumPassDistance, 5.0
                    "ShootingRange", float config.Planner.ShootingRange, float Pitch.HalfLength
                    "PassTableResolution", float config.Planner.PassTableResolution, 1.0 ] do
                  if not (System.Double.IsFinite value) || value <= 0.0 || value > maximum then
                      yield sprintf "Planner.%s must be finite and in (0, %.2f]" name maximum ]

        let transition = config.DefensiveTransition

        let transitionErrors =
            [ for name, value, maximum in
                  [ "BaseArrivalWindow", float transition.BaseArrivalWindow, 10.0
                    "PressArrivalExtension", float transition.PressArrivalExtension, 10.0
                    "LocalArrivalWindow", float transition.LocalArrivalWindow, 10.0
                    "CoverArrivalWindow", float transition.CoverArrivalWindow, 10.0
                    "HandoffAdvantage", float transition.HandoffAdvantage, 5.0
                    "ApproachOffset", float transition.ApproachOffset, float config.Ball.FootReach
                    "CarrierPrediction", float transition.CarrierPrediction, 2.0
                    "CoverDepth", float transition.CoverDepth, 20.0
                    "BeatenMargin", float transition.BeatenMargin, 10.0
                    "EscapeProgress", float transition.EscapeProgress, 30.0
                    "ThreatGoalDistance", float transition.ThreatGoalDistance, 60.0
                    "EngageGoalDistance", float transition.EngageGoalDistance, float Pitch.HalfLength
                    "CentralHalfWidth", float transition.CentralHalfWidth, float Pitch.HalfWidth
                    "DepthSafety", float transition.DepthSafety, 10.0
                    "RetreatBallGap", float transition.RetreatBallGap, 25.0
                    "SettledTolerance", float transition.SettledTolerance, 10.0 ] do
                  if not (System.Double.IsFinite value) || value <= 0.0 || value > maximum then
                      yield sprintf "DefensiveTransition.%s must be finite and in (0, %.2f]" name maximum
              for name, value in
                  [ "MinimumPressIntensity", transition.MinimumPressIntensity
                    "MinimumPressCondition", transition.MinimumPressCondition
                    "ScreenFraction", transition.ScreenFraction
                    "RetreatWidthScale", transition.RetreatWidthScale ] do
                  if not (System.Double.IsFinite value) || value <= 0.0 || value > 1.0 then
                      yield sprintf "DefensiveTransition.%s must be finite and in (0, 1]" name
              for name, value in
                  [ "CoverEffort", transition.CoverEffort
                    "ScreenEffort", transition.ScreenEffort
                    "RecoveryEffort", transition.RecoveryEffort ] do
                  if not (System.Double.IsFinite value) || value <= 0.0 || value > 1.0 then
                      yield sprintf "DefensiveTransition.%s must be finite and in (0, 1]" name
              if transition.MaximumScreens < 0 || transition.MaximumScreens > 3 then
                  yield "DefensiveTransition.MaximumScreens must be in [0, 3]"
              if
                  transition.ReevaluateTicks < 1<tickDelta>
                  || transition.ReevaluateTicks > 40<tickDelta>
              then
                  yield "DefensiveTransition.ReevaluateTicks must be in [1, 40]"
              if
                  transition.Commitment < 1<subtick>
                  || transition.Commitment > transition.MaxPressDuration
              then
                  yield "DefensiveTransition.Commitment must be positive and <= MaxPressDuration"
              if
                  transition.MaxPressDuration < 1<subtick>
                  || transition.MaxPressDuration > 1200<subtick>
              then
                  yield "DefensiveTransition.MaxPressDuration must be in [1, 1200]"
              if
                  transition.StabilizationHold < 1<subtick>
                  || transition.StabilizationHold > 400<subtick>
              then
                  yield "DefensiveTransition.StabilizationHold must be in [1, 400]"
              if transition.MinimumLocalPlayers < 3 || transition.MinimumLocalPlayers > 10 then
                  yield "DefensiveTransition.MinimumLocalPlayers must be in [3, 10]"
              if transition.LocalSurplus < 0 || transition.LocalSurplus > 5 then
                  yield "DefensiveTransition.LocalSurplus must be in [0, 5]" ]

        let errs =
            attackErrors
            @ transitionErrors
            @ validateBall config.Ball
            @ validateMovement config.Movement
            @ validateGait config.Gait
            @ validateStrikeTechnique config.StrikeTechnique
            @ validateContact config.Contact
            @ validateContactAction config.ContactAction
            @ validateKeeper config.Keeper
            @ validateActions config.Actions
            @ validateSetPiece config.SetPiece
            @ validateTeam config.Team
            @ validatePositioning config.Positioning
            @ validateIntercept config.Intercept
            @ validateSituation config.Situation
            @ validatePassIntercept config.PassIntercept
            @ validateReferee config.Referee
            @ validateManager config.Manager
            @ validateEnvironment config.Environment
            @ validateMatch config.Match
            @ validatePlanner config.Planner
            @ validateQuickMatch config.QuickMatch
            @ validateExpectedValue config.ExpectedValue
            @ [ let m = config.Manager

                for name, value in
                    [ "Winning", m.ConditionThresholdWinning
                      "Drawing", m.ConditionThresholdDrawing
                      "Losing", m.ConditionThresholdLosing ] do
                    if not (System.Double.IsFinite value) || value < 0.0 || value > 1.0 then
                        yield $"Manager.ConditionThreshold{name} must be in [0, 1]"

                if
                    config.Match.EventWindowCapacity < 1
                    || config.Match.EventWindowCapacity > 100000
                then
                    yield "Match.EventWindowCapacity must be in [1, 100000]"

                if
                    config.Actions.PerceptionRadius <= 0.0<meter>
                    || config.Actions.PerceptionRadius > 105.0<meter>
                then
                    yield "Actions.PerceptionRadius must be in (0, 105] m" ]
            @ validateScout config.Scout
            @ validateCounter config.Counter
            @ validateRivalManager config.RivalManager
            @ validateTransfer config.Transfer
            @ FootballEngine.Domain.WorldConfig.validate config.World
            @ validateTactics config.TacticalTradeoffs
            @ validateTraining config.Training

        if List.isEmpty errs then Ok() else Error errs
