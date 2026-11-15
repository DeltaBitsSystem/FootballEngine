namespace FootballEngine.Domain

type AttackEstimate = { Samples: float; RewardSum: float }

module AttackEstimate =
    let empty = { Samples = 0.0; RewardSum = 0.0 }

    let mean estimate =
        if estimate.Samples > 0.0 then
            estimate.RewardSum / estimate.Samples
        else
            0.0

    let merge cap (prior: AttackEstimate) (evidence: AttackEstimate) =
        let count = prior.Samples + evidence.Samples
        let scale = if count > cap then cap / count else 1.0
        // Multiplying the saturated count back by its scale can round above cap and invalidate the next kickoff.
        let samples = min cap count
        let reward = (prior.RewardSum + evidence.RewardSum) * scale

        { Samples = samples
          RewardSum = max (-samples) (min samples reward) }

type CoordinationMemory =
    { PressingCoordination: float
      TransitionSpeed: float
      AttackLearning: Map<int, AttackEstimate>
      // Familiarity per formation name: switching shapes costs crispness until
      // the new shape is drilled. Keyed by formation so returning to an old
      // shape keeps its drilled level.
      TacticalFamiliarity: Map<string, float> }

module CoordinationMemory =
    let defaultMemory =
        { PressingCoordination = 1.0
          TransitionSpeed = 1.0
          AttackLearning = Map.empty
          TacticalFamiliarity = Map.empty }
