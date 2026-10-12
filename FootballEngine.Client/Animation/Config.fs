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
      Clearance: float32
      LeanSeconds: float32
      MaxLean: float32
      MaxGazeYaw: float32
      MaxGazePitch: float32
      GazeRate: float32 }

module AnimationConfig =
    let defaultConfig =
        { Version = 1
          TickSeconds =
            1.0f
            / float32 FootballEngine.Core.Match.SimulationClock.defaultClock.SubTicksPerSecond
          WindowTicks = 256
          // Historical playback needs forward headroom, not a second complete live window.
          RecoveryWindowTicks = 32
          MaxActors = 22
          TasksPerActor = 4
          StepWidth = 0.18f
          StepReach = 0.62f
          SwingSeconds = 0.20f
          Clearance = 0.12f
          LeanSeconds = 0.18f
          MaxLean = 0.22f
          MaxGazeYaw = 1.45f
          MaxGazePitch = 0.55f
          GazeRate = 3.5f }

    let validate config =
        if
            config.Version <> 1
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
                && Single.IsFinite config.Clearance
                && Single.IsFinite config.LeanSeconds
                && Single.IsFinite config.MaxLean
                && Single.IsFinite config.MaxGazeYaw
                && Single.IsFinite config.MaxGazePitch
                && Single.IsFinite config.GazeRate
            )
            || config.TickSeconds <= 0.0f
            || config.StepWidth <= 0.0f
            || config.StepReach <= config.StepWidth
            || config.SwingSeconds <= config.TickSeconds
            || config.LeanSeconds <= 0.0f
            || config.Clearance < 0.0f
            || config.Clearance > 1.0f
            || config.MaxLean <= 0.0f
            || config.MaxLean >= MathF.PI * 0.5f
            || config.MaxGazeYaw <= 0.0f
            || config.MaxGazeYaw >= MathF.PI
            || config.MaxGazePitch <= 0.0f
            || config.MaxGazePitch >= MathF.PI * 0.5f
            || config.GazeRate <= 0.0f
        then
            Error "invalid animation timing or geometry"
        else
            Ok()
