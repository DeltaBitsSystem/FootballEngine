namespace FootballEngine.Manager

open FootballEngine.Domain
open FootballEngine.Types

type SubstitutionSuggestion =
    { PlayerOut: PlayerId
      PlayerIn: PlayerId
      Reason: string
      Urgency: float }

module SubstitutionIntelligence =

    let suggestSubs
        (subTick: int)
        (clock: SimulationClock)
        (players: Player[])
        (frame: TeamFrame)
        (score: int)
        (opponentScore: int)
        : SubstitutionSuggestion list =

        let elapsed = float subTick / float clock.SubTicksPerSecond / 60.0
        let suggestions = ResizeArray()

        if elapsed > 60.0 then
            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active rosterIdx ->
                    let p = players[rosterIdx]

                    if p.Condition < 50 && p.Position <> GK then
                        suggestions.Add(
                            { PlayerOut = p.Id
                              PlayerIn = 0
                              Reason = "Fatigue"
                              Urgency = float (100 - p.Condition) / 100.0 }
                        )
                | _ -> ()

        if score < opponentScore && elapsed > 70.0 then
            suggestions.Add(
                { PlayerOut = 0
                  PlayerIn = 0
                  Reason = "Tactical - chasing goal"
                  Urgency = 0.7 }
            )

        suggestions |> Seq.toList
