namespace FootballEngine.Core.Match

/// Slider tactics as the engine consumes them, in ENGINE scales (never slider
/// units): intensities 0..1, line height in meters, trigger zone 0..1.
/// tacticsOf is the only producer; CounterPlan deltas land here pre-match.
[<Struct>]
type TacticsConfig =
    { Formation: string
      DefensiveLine: float32
      PressIntensity: float32
      Width: float32
      Tempo: float32
      Directness: float32
      Mentality: float32
      PressTriggerZone: float32 }
