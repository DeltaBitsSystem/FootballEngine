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

        let pressingDecay = 1.0 - w.Chemistry.PressingCoordinationBase
        let pressingLearn = w.Chemistry.PressingCoordinationBase
        let newPressing =
            current.PressingCoordination * pressingDecay
            + pressSuccessRate * pressingLearn

        let transitionRate =
            if transitionCount > 0 then float successfulTransitions / float transitionCount else 0.5

        let transitionDecay = 1.0 - w.Chemistry.TransitionSpeedBase
        let transitionLearn = w.Chemistry.TransitionSpeedBase
        let newTransition =
            current.TransitionSpeed * transitionDecay
            + transitionRate * transitionLearn

        let familiarityDecay = 1.0 - w.Chemistry.FamiliarityTimeBonus * 50.0
        let familiarityLearn = w.Chemistry.FamiliarityTimeBonus * 50.0
        let newFamiliarity =
            current.TacticalFamiliarity
            |> Map.map (fun _ v -> v * familiarityDecay + passSuccessRate * familiarityLearn)

        { PressingCoordination = PhysicsContract.clampFloat newPressing 0.0 1.0
          TransitionSpeed = PhysicsContract.clampFloat newTransition 0.0 1.0
          TacticalFamiliarity = newFamiliarity }

    let applySquadChangeDecay (playersChanged: int) (current: CoordinationMemory) : CoordinationMemory =
        let baseDecay = 0.90
        let perPlayerPenalty = 0.05
        let minDecay = 0.50
        let decay = max minDecay (baseDecay - float playersChanged * perPlayerPenalty)

        { PressingCoordination = current.PressingCoordination * decay
          TransitionSpeed = current.TransitionSpeed * decay
          TacticalFamiliarity = current.TacticalFamiliarity |> Map.map (fun _ v -> v * decay) }
