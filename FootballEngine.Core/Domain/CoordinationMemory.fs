namespace FootballEngine.Domain

type CoordinationMemory =
    { PressingCoordination: float
      TransitionSpeed: float
      // Familiarity per formation name: switching shapes costs crispness until
      // the new shape is drilled. Keyed by formation so returning to an old
      // shape keeps its drilled level.
      TacticalFamiliarity: Map<string, float> }

module CoordinationMemory =
    let defaultMemory =
        { PressingCoordination = 1.0
          TransitionSpeed = 1.0
          TacticalFamiliarity = Map.empty }
