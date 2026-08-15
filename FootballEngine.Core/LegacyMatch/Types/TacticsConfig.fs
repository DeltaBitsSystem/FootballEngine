namespace FootballEngine.Types

open FootballEngine.Domain

[<Struct>]
type EmergentState =
    { CompactnessLevel: float
      PressingIntensity: float
      WingPlayPreference: float
      TempoLevel: float
      RiskAppetite: float }

module EmergentState =
    let initial =
        { CompactnessLevel = 0.5
          PressingIntensity = 0.5
          WingPlayPreference = 0.5
          TempoLevel = 0.5
          RiskAppetite = 0.5 }

[<Struct>]
type TacticsConfig =
    { PressureDistance: float
      UrgencyMultiplier: float
      ForwardPush: float
      DefensiveDrop: float
      PressingIntensity: float
      Width: float
      Tempo: float
      Directness: float
      PressTriggerZone: PitchZone
      DefensiveShape: float }

module TacticsConfig =
    let baseTacticsConfig =
        function
        | TeamTactics.Balanced ->
            { PressureDistance = 0.0
              UrgencyMultiplier = 1.0
              ForwardPush = 0.0
              DefensiveDrop = 0.0
              PressingIntensity = 1.0
              Width = 0.5
              Tempo = 0.5
              Directness = 0.5
              PressTriggerZone = MidfieldZone
              DefensiveShape = 0.5 }
        | TeamTactics.Attacking ->
            { PressureDistance = 8.0
              UrgencyMultiplier = 1.15
              ForwardPush = 10.0
              DefensiveDrop = -5.0
              PressingIntensity = 1.2
              Width = 0.6
              Tempo = 0.6
              Directness = 0.4
              PressTriggerZone = AttackingZone
              DefensiveShape = 0.4 }
        | TeamTactics.Defensive ->
            { PressureDistance = -10.0
              UrgencyMultiplier = 0.9
              ForwardPush = -5.0
              DefensiveDrop = 8.0
              PressingIntensity = 0.7
              Width = 0.4
              Tempo = 0.3
              Directness = 0.6
              PressTriggerZone = DefensiveZone
              DefensiveShape = 0.6 }
        | TeamTactics.Pressing ->
            { PressureDistance = 12.0
              UrgencyMultiplier = 1.1
              ForwardPush = 8.0
              DefensiveDrop = -3.0
              PressingIntensity = 1.5
              Width = 0.5
              Tempo = 0.7
              Directness = 0.5
              PressTriggerZone = AttackingZone
              DefensiveShape = 0.5 }
        | TeamTactics.Counter ->
            { PressureDistance = -6.0
              UrgencyMultiplier = 1.2
              ForwardPush = -8.0
              DefensiveDrop = 6.0
              PressingIntensity = 0.8
              Width = 0.3
              Tempo = 0.8
              Directness = 0.8
              PressTriggerZone = MidfieldZone
              DefensiveShape = 0.4 }

    let defaultPressParams (tactics: TacticsConfig) (emergent: EmergentState) =
        { Intensity = tactics.PressingIntensity * emergent.PressingIntensity
          TriggerZone = tactics.PressTriggerZone
          MinPresserCount = int (3.0 + tactics.PressingIntensity * 3.0) }

    let defaultShapeParams (tactics: TacticsConfig) (emergent: EmergentState) =
        { Width = tactics.Width * 0.5 + emergent.WingPlayPreference * 0.5
          DefensiveLineHeight = tactics.DefensiveDrop
          Compactness = emergent.CompactnessLevel }

    let defaultTransitionParams (tactics: TacticsConfig) (emergent: EmergentState) =
        { Tempo = tactics.Tempo * 0.6 + emergent.TempoLevel * 0.4
          DirectnessThreshold = tactics.Directness
          CounterTrigger = false
          WingBias = 0.0
          DirectnessBias = 0.0 }

    let defaultParams (tactics: TacticsConfig) (emergent: EmergentState) : DirectiveParams =
        { Press = defaultPressParams tactics emergent
          Shape = defaultShapeParams tactics emergent
          Transition = defaultTransitionParams tactics emergent }
