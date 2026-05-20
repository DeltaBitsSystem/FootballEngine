namespace FootballEngine.TeamOrchestrator

open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types

module CoordinationLoop =

    let updateFromMatch
        (passSuccessRate: float)
        (pressSuccessRate: float)
        (transitionCount: int)
        (successfulTransitions: int)
        (current: CoordinationMemory)
        (w: CollectiveWeights)
        : CoordinationMemory =

        let newPressing =
            current.PressingCoordination * 0.90
            + pressSuccessRate * w.Chemistry.PressingCoordinationBase * 0.10

        let transitionRate =
            if transitionCount > 0 then float successfulTransitions / float transitionCount else 0.5

        let newTransition =
            current.TransitionSpeed * 0.92
            + transitionRate * w.Chemistry.TransitionSpeedBase * 0.08

        let newFamiliarity =
            current.TacticalFamiliarity
            |> Map.map (fun _ v -> v * 0.95 + passSuccessRate * 0.05)

        { PressingCoordination = PhysicsContract.clampFloat newPressing 0.0 1.0
          TransitionSpeed = PhysicsContract.clampFloat newTransition 0.0 1.0
          TacticalFamiliarity = newFamiliarity }

    let applySquadChangeDecay (playersChanged: int) (current: CoordinationMemory) : CoordinationMemory =
        let decay = max 0.50 (0.90 - float playersChanged * 0.05)

        { PressingCoordination = current.PressingCoordination * decay
          TransitionSpeed = current.TransitionSpeed * decay
          TacticalFamiliarity = current.TacticalFamiliarity |> Map.map (fun _ v -> v * decay) }
