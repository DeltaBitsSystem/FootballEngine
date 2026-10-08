namespace FootballEngine.Core.Match

/// Config sanity checks. The engine must never run with a self-contradictory
/// config: a negative budget or TTL would silently corrupt every decision,
/// and an invalid weights file should be loud, not silently ignored.
module ConfigValidation =

    let validatePlanner (p: PlannerConfig) : string list =
        [ if p.MaxChainLength < 1 then
              yield "Planner.MaxChainLength must be >= 1"
          if p.MaxEvaluateLimit < 1 then
              yield "Planner.MaxEvaluateLimit must be >= 1"
          if p.PlanTTL <= 0<subtick> then
              yield "Planner.PlanTTL must be > 0" ]

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
                "AirDrag", b.AirDrag
                "BounceRestitution", b.BounceRestitution
                "BounceFriction", b.BounceFriction
                "StopSpeedCutoff", float b.StopSpeedCutoff
                "MaxShotSpeed", float b.MaxShotSpeed
                "MagnusFactor", b.MagnusFactor
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
          // AirDrag is per-tick retention — >1 would accelerate, <0 would invert
          if b.AirDrag < 0.0 || b.AirDrag > 1.0 then
              yield "Ball.AirDrag must be in [0, 1]"
          for name, value in
              [ "Radius", float b.Radius
                "InertiaRatio", b.InertiaRatio
                "FootReach", float b.FootReach
                "FootContactHeight", float b.FootContactHeight
                "CarryLeadTime", float b.CarryLeadTime
                "CarryOffset", float b.CarryOffset
                "CarryMaxSpeed", float b.CarryMaxSpeed ] do
              if not (System.Double.IsFinite value) || value <= 0.0 then
                  yield $"Ball.{name} must be finite and > 0"
          for name, value in
              [ "SlipFriction", b.SlipFriction
                "RollingResistance", b.RollingResistance
                "CarryTechniqueError", b.CarryTechniqueError
                "PassRollingSpinRatio", b.PassRollingSpinRatio
                "ReceptionVelocityRetention", b.ReceptionVelocityRetention ] do
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
          // MagnusFactor >1 would curve unrealistically, <0 would invert spin
          if b.MagnusFactor < 0.0 || b.MagnusFactor > 1.0 then
              yield "Ball.MagnusFactor must be in [0, 1]"
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
          // SaveDistancePenalty beyond pitch half (50m) would make keeper useless
          if b.SaveDistancePenalty < 0.0<meter> || b.SaveDistancePenalty > 50.0<meter> then
              yield "Ball.SaveDistancePenalty must be in [0, 50] m"
          if b.ReceptionBase < 0.0 || b.ReceptionBase > 1.0 then
              yield "Ball.ReceptionBase must be in [0, 1]"
          if b.ReceptionSpacePenalty < 0.0 || b.ReceptionSpacePenalty > 1.0 then
              yield "Ball.ReceptionSpacePenalty must be in [0, 1]"
          if b.ShotSaveBaseProbability < 0.0 || b.ShotSaveBaseProbability > 1.0 then
              yield "Ball.ShotSaveBaseProbability must be in [0, 1]"
          // xG multiplier >5 would inflate goals, <0 would nullify shot threat
          if b.ShotGoalXGMultiplier < 0.0 || b.ShotGoalXGMultiplier > 5.0 then
              yield "Ball.ShotGoalXGMultiplier must be in [0, 5]"
          // GK divisor <100 makes GK irrelevant, >2000 makes him unbeatable
          if b.ShotGoalGKDivisor < 100.0 || b.ShotGoalGKDivisor > 2000.0 then
              yield "Ball.ShotGoalGKDivisor must be in [100, 2000]"
          if b.InterceptionPaceWeight < 0.0 || b.InterceptionPaceWeight > 1.0 then
              yield "Ball.InterceptionPaceWeight must be in [0, 1]"
          // Intent boost >5 would make intent dominate over pace
          if b.InterceptionIntentBoost < 0.0 || b.InterceptionIntentBoost > 5.0 then
              yield "Ball.InterceptionIntentBoost must be in [0, 5]" ]

    let validateMovement (m: MovementConfig) : string list =
        [ // Sprint 12 m/s is Bolt, 5 is jogging — outside not football
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
              a.PassLaunchSpeed < 5.0<meterPerSecond>
              || a.PassLaunchSpeed > 35.0<meterPerSecond>
          then
              yield "Actions.PassLaunchSpeed must be in [5, 35] m/s"
          if a.ShotQualityGate < 0.0 || a.ShotQualityGate > 1.0 then
              yield "Actions.ShotQualityGate must be in [0, 1]"
          // AngleSpread >2 rad would wrap around, <0 would invert
          if a.ShotAngleSpread < 0.0 || a.ShotAngleSpread > 2.0 then
              yield "Actions.ShotAngleSpread must be in [0, 2]"
          if a.ShotDistanceToGoalMultiplier < 0.0 || a.ShotDistanceToGoalMultiplier > 0.1 then
              yield "Actions.ShotDistanceToGoalMultiplier must be in [0, 0.1]"
          if a.ShotVzBase < 0.0 || a.ShotVzBase > 2.0 then
              yield "Actions.ShotVzBase must be in [0, 2]"
          // Steepness 0 would flatten sigmoid, >20 would be step function
          if a.DuelSteepness < 0.1 || a.DuelSteepness > 20.0 then
              yield "Actions.DuelSteepness must be in [0.1, 20]"
          if a.DuelMomentumBonus < 0.0 || a.DuelMomentumBonus > 1.0 then
              yield "Actions.DuelMomentumBonus must be in [0, 1]"
          if a.DuelFoulChance < 0.0 || a.DuelFoulChance > 1.0 then
              yield "Actions.DuelFoulChance must be in [0, 1]"
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
          if a.ContestFoulChance < 0.0 || a.ContestFoulChance > 1.0 then
              yield "Actions.ContestFoulChance must be in [0, 1]"
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
              yield "Actions.MemoryMentalDecay must be in [0, 1]" ]

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
          if s.ContactReadyRadius <= 0.0<meter> then
              yield "SetPiece.ContactReadyRadius must be positive"
          if s.TargetTolerance <= 0.0<meter> then
              yield "SetPiece.TargetTolerance must be positive"
          if s.MaxTargetDistance <= 0.0<meter> then
              yield "SetPiece.MaxTargetDistance must be positive" ]

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
          if p.SupportLateralDistance < 2.0<meter> || p.SupportLateralDistance > 20.0<meter> then
              yield "Positioning.SupportLateralDistance must be in [2, 20] m"
          if p.GkMaxAdvance < 0.0<meter> || p.GkMaxAdvance > 20.0<meter> then
              yield "Positioning.GkMaxAdvance must be in [0, 20] m" ]

    let validateIntercept (c: InterceptConfig) : string list =
        [ // Control radius <0.5 would be feet only, >10 would be telekinesis
          if c.ControlRadius < 0.5<meter> || c.ControlRadius > 10.0<meter> then
              yield "Intercept.ControlRadius must be in [0.5, 10] m"
          // MaxSteps 0 would never intercept, >200 hurts perf per RoboCup table
          if c.MaxSteps < 1 || c.MaxSteps > 200 then
              yield "Intercept.MaxSteps must be in [1, 200]" ]

    let validatePress (c: PressConfig) : string list =
        [ if c.BaseAllowance < 1 || c.BaseAllowance > 50 then
              yield "Press.BaseAllowance must be in [1, 50]"
          if c.MidfieldAllowance < 1 || c.MidfieldAllowance > 50 then
              yield "Press.MidfieldAllowance must be in [1, 50]"
          if c.FullBackAllowance < 1 || c.FullBackAllowance > 50 then
              yield "Press.FullBackAllowance must be in [1, 50]"
          if c.MaxPressStep < 1 || c.MaxPressStep > 100 then
              yield "Press.MaxPressStep must be in [1, 100]"
          // Max must dominate allowances — otherwise allowance never used
          if c.MaxPressStep < c.BaseAllowance then
              yield "Press.MaxPressStep must be >= BaseAllowance"
          if c.MaxPressStep < c.MidfieldAllowance then
              yield "Press.MaxPressStep must be >= MidfieldAllowance"
          if c.MaxPressStep < c.FullBackAllowance then
              yield "Press.MaxPressStep must be >= FullBackAllowance" ]

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
          // Foul chances are mutually exclusive distribution — sum >1 would overflow
          if
              r.FoulMinorChance
              + r.FoulTacticalChance
              + r.FoulYellowWorthyChance
              + r.FoulRedWorthyChance > 1.01
          then
              yield "Referee.Foul*Chance sum must be <= 1"
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
        let errs =
            validateBall config.Ball
            @ validateMovement config.Movement
            @ validateActions config.Actions
            @ validateSetPiece config.SetPiece
            @ validateTeam config.Team
            @ validatePositioning config.Positioning
            @ validateIntercept config.Intercept
            @ validatePress config.Press
            @ validateSituation config.Situation
            @ validatePassIntercept config.PassIntercept
            @ validateReferee config.Referee
            @ validateManager config.Manager
            @ validateEnvironment config.Environment
            @ validateMatch config.Match
            @ validatePlanner config.Planner
            @ validateScout config.Scout
            @ validateCounter config.Counter
            @ validateTransfer config.Transfer
            @ FootballEngine.Domain.WorldConfig.validate config.World
            @ validateTactics config.TacticalTradeoffs
            @ validateTraining config.Training

        if List.isEmpty errs then Ok() else Error errs
