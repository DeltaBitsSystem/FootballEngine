namespace FootballEngine.Types

open FootballEngine.Domain

type CoordinationMemory = {
    PressingCoordination: float
    TransitionSpeed: float
    TacticalFamiliarity: Map<TeamTactics, float>
}

module CoordinationMemory =
    let defaultMemory = {
        PressingCoordination = 1.0
        TransitionSpeed = 1.0
        TacticalFamiliarity = Map.empty
    }
