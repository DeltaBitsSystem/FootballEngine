namespace FootballEngine.Core.Match

/// Slider tactics as the engine consumes them: every field is the 0..4
/// slider value, deltas from CounterPlan land here pre-match.
[<Struct>]
type TacticsConfig =
    { Formation: string
      DefensiveLine: float32
      PressIntensity: float32
      Width: float32
      Tempo: float32
      Directness: float32 }
