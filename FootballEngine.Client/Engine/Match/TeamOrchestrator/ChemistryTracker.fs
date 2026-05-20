namespace FootballEngine.TeamOrchestrator

open FootballEngine.Domain
open FootballEngine.ML


type FamiliarityPair =
    { PlayerA: PlayerId
      PlayerB: PlayerId
      Familiarity: float }

type TeamCohesion =
    { OverallCohesion: float
      PressingCoordination: float
      TransitionSpeed: float }

module ChemistryTracker =

    let updateFamiliarity (current: float) (successfulPass: bool) (minutesPlayed: float) : float =
        let w = EngineWeightDefaults.defaults.Collective.Chemistry
        let passBonus = if successfulPass then w.FamiliarityPassBonus else w.FamiliarityFailPenalty
        let timeBonus = minutesPlayed * w.FamiliarityTimeBonus
        min 1.0 (current + passBonus + timeBonus)

    let calculateCohesion (familiarity: float[,]) (playerCount: int) : TeamCohesion =
        let w = EngineWeightDefaults.defaults.Collective.Chemistry

        let mutable totalFam = 0.0
        let mutable count = 0

        for i = 0 to playerCount - 1 do
            for j = i + 1 to playerCount - 1 do
                totalFam <- totalFam + familiarity[i, j]
                count <- count + 1

        let avgFam = if count > 0 then totalFam / float count else 0.5

        { OverallCohesion = avgFam
          PressingCoordination = avgFam * w.PressingCoordinationFamiliarityMult + w.PressingCoordinationBase
          TransitionSpeed = avgFam * w.TransitionSpeedFamiliarityMult + w.TransitionSpeedBase }
