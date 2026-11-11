namespace FootballEngine.Client.Animation

open System

[<Struct>]
type AnimationConfig =
    { Version: int
      TickSeconds: float32
      WindowTicks: int
      RecoveryWindowTicks: int
      MaxActors: int
      TasksPerActor: int
      StepWidth: float32
      StepReach: float32
      SwingSeconds: float32
      MinSwingSeconds: float32
      SwingSpeedFactor: float32
      StepTrigger: float32
      StepSpeedTrigger: float32
      StepLeadSeconds: float32
      RunFlightSpeed: float32
      RunFlightOverlapSeconds: float32
      MaxLegLeadFraction: float32
      RetargetDistance: float32
      ArmSwing: float32
      ArmBendSpeed: float32
      ArmIdleFlex: float32
      ArmRunFlex: float32
      ArmSwingFlexLimit: float32
      FootSwingPitch: float32
      PivotSensitivity: float32
      PivotTriggerMinimumRatio: float32
      PelvisMaxDrop: float32
      Clearance: float32
      LeanSeconds: float32
      MaxLean: float32
      MaxGazeYaw: float32
      MaxGazePitch: float32
      GazeNearDistance: float32
      NeckShare: float32
      MaxNeckYaw: float32
      MaxNeckPitch: float32
      GazeRate: float32 }

module AnimationConfig =
    let defaultConfig =
        { Version = 3
          TickSeconds =
            1.0f
            / float32 FootballEngine.Core.Match.SimulationClock.defaultClock.SubTicksPerSecond
          WindowTicks = 256
          // Historical playback needs forward headroom, not a second complete live window. Motion matching inside a
          // recovered window extrapolates its trajectory past this headroom rather than doubling the state budget.
          RecoveryWindowTicks = 32
          MaxActors = 22
          TasksPerActor = 4
          StepWidth = 0.12f
          StepReach = 0.62f
          SwingSeconds = 0.38f
          MinSwingSeconds = 0.14f
          SwingSpeedFactor = 0.35f
          StepTrigger = 0.12f
          StepSpeedTrigger = 0.015f
          StepLeadSeconds = 0.10f
          RunFlightSpeed = 3.6f
          RunFlightOverlapSeconds = 0.035f
          MaxLegLeadFraction = 0.50f
          RetargetDistance = 0.08f
          ArmSwing = 0.90f
          ArmBendSpeed = 0.20f
          ArmIdleFlex = 0.24f
          ArmRunFlex = 1.50f
          ArmSwingFlexLimit = 0.45f
          FootSwingPitch = 0.40f
          PivotSensitivity = 0.045f
          PivotTriggerMinimumRatio = 0.4f
          PelvisMaxDrop = 0.08f
          Clearance = 0.12f
          LeanSeconds = 0.18f
          MaxLean = 0.22f
          MaxGazeYaw = 0.85f
          MaxGazePitch = 0.30f
          GazeNearDistance = 3.0f
          NeckShare = 0.45f
          MaxNeckYaw = 0.38f
          MaxNeckPitch = 0.18f
          GazeRate = 3.5f }

    let validate config =
        if
            config.Version <> 3
            || config.WindowTicks < 128
            || config.WindowTicks > 256
            || config.RecoveryWindowTicks < 16
            || config.RecoveryWindowTicks > 128
            || config.MaxActors < 22
            || config.MaxActors > 64
            || config.TasksPerActor < 2
            || config.TasksPerActor > 4
        then
            Error "unsupported animation capacity or version"
        elif
            not (Single.IsFinite config.TickSeconds)
            || not (
                Single.IsFinite config.StepWidth
                && Single.IsFinite config.StepReach
                && Single.IsFinite config.SwingSeconds
                && Single.IsFinite config.MinSwingSeconds
                && Single.IsFinite config.SwingSpeedFactor
                && Single.IsFinite config.StepTrigger
                && Single.IsFinite config.StepSpeedTrigger
                && Single.IsFinite config.StepLeadSeconds
                && Single.IsFinite config.RunFlightSpeed
                && Single.IsFinite config.RunFlightOverlapSeconds
                && Single.IsFinite config.MaxLegLeadFraction
                && Single.IsFinite config.RetargetDistance
                && Single.IsFinite config.ArmSwing
                && Single.IsFinite config.ArmBendSpeed
                && Single.IsFinite config.ArmIdleFlex
                && Single.IsFinite config.ArmRunFlex
                && Single.IsFinite config.ArmSwingFlexLimit
                && Single.IsFinite config.FootSwingPitch
                && Single.IsFinite config.PivotSensitivity
                && Single.IsFinite config.PivotTriggerMinimumRatio
                && Single.IsFinite config.PelvisMaxDrop
                && Single.IsFinite config.Clearance
                && Single.IsFinite config.LeanSeconds
                && Single.IsFinite config.MaxLean
                && Single.IsFinite config.MaxGazeYaw
                && Single.IsFinite config.MaxGazePitch
                && Single.IsFinite config.GazeNearDistance
                && Single.IsFinite config.NeckShare
                && Single.IsFinite config.MaxNeckYaw
                && Single.IsFinite config.MaxNeckPitch
                && Single.IsFinite config.GazeRate
            )
            || config.TickSeconds <= 0.0f
            || config.StepWidth <= 0.0f
            || config.StepReach <= config.StepWidth
            || config.SwingSeconds <= config.TickSeconds
            || config.MinSwingSeconds < config.TickSeconds
            || config.MinSwingSeconds > config.SwingSeconds
            || config.SwingSpeedFactor < 0.0f
            || config.StepTrigger <= 0.0f
            || config.StepSpeedTrigger < 0.0f
            || config.StepLeadSeconds < 0.0f
            || config.RunFlightSpeed <= 0.0f
            || config.RunFlightOverlapSeconds < 0.0f
            || config.RunFlightOverlapSeconds >= config.MinSwingSeconds
            || config.MaxLegLeadFraction <= 0.0f
            || config.MaxLegLeadFraction > 1.0f
            || config.RetargetDistance <= 0.0f
            || config.ArmSwing < 0.0f
            || config.ArmBendSpeed <= 0.0f
            || config.ArmIdleFlex <= 0.0f
            || config.ArmRunFlex <= config.ArmIdleFlex
            || config.ArmRunFlex >= MathF.PI
            || config.ArmSwingFlexLimit <= 0.0f
            || config.ArmSwingFlexLimit > 0.5f
            || config.FootSwingPitch < 0.0f
            || config.FootSwingPitch > 0.5f
            || config.PivotSensitivity < 0.0f
            || config.PivotTriggerMinimumRatio <= 0.0f
            || config.PivotTriggerMinimumRatio > 1.0f
            || config.PelvisMaxDrop < 0.0f
            || config.PelvisMaxDrop > 0.2f
            || config.LeanSeconds <= 0.0f
            || config.Clearance < 0.0f
            || config.Clearance > 1.0f
            || config.MaxLean <= 0.0f
            || config.MaxLean >= MathF.PI * 0.5f
            || config.MaxGazeYaw <= 0.0f
            || config.MaxGazeYaw >= MathF.PI
            || config.MaxGazePitch <= 0.0f
            || config.MaxGazePitch >= MathF.PI * 0.5f
            || config.GazeNearDistance <= 0.0f
            || config.NeckShare <= 0.0f
            || config.NeckShare >= 1.0f
            || config.MaxNeckYaw <= 0.0f
            || config.MaxNeckYaw > config.MaxGazeYaw
            || config.MaxNeckPitch <= 0.0f
            || config.MaxNeckPitch > config.MaxGazePitch
            || config.GazeRate <= 0.0f
        then
            Error "invalid animation timing or geometry"
        else
            Ok()
