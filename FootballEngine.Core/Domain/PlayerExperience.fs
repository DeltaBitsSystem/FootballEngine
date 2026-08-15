namespace FootballEngine.Domain

type PlayerExperience =
    { TotalMatchesPlayed: int
      GoalsScored: int
      AssistsGiven: int
      HighPressureMatches: int
      RivalRecord: Map<ClubId, {| Wins: int; Losses: int; Draws: int |}> }

module PlayerExperience =
    let empty =
        { TotalMatchesPlayed = 0
          GoalsScored = 0
          AssistsGiven = 0
          HighPressureMatches = 0
          RivalRecord = Map.empty }

    let recordMatch
        (scored: int)
        (assisted: int)
        (wasHighPressure: bool)
        (vsClub: ClubId)
        (won: bool)
        (drew: bool)
        (exp: PlayerExperience)
        : PlayerExperience =
        let existing =
            exp.RivalRecord
            |> Map.tryFind vsClub
            |> Option.defaultValue {| Wins = 0; Losses = 0; Draws = 0 |}

        let updatedRival =
            if won then
                {| existing with
                    Wins = existing.Wins + 1 |}
            elif drew then
                {| existing with
                    Draws = existing.Draws + 1 |}
            else
                {| existing with
                    Losses = existing.Losses + 1 |}

        { exp with
            TotalMatchesPlayed = exp.TotalMatchesPlayed + 1
            GoalsScored = exp.GoalsScored + scored
            AssistsGiven = exp.AssistsGiven + assisted
            HighPressureMatches = exp.HighPressureMatches + (if wasHighPressure then 1 else 0)
            RivalRecord = exp.RivalRecord |> Map.add vsClub updatedRival }

    let pressureExperienceBonus (exp: PlayerExperience) : float =
        let ratio =
            if exp.TotalMatchesPlayed = 0 then
                0.0
            else
                float exp.HighPressureMatches / float exp.TotalMatchesPlayed

        min 0.05 (ratio * 0.1)

    let rivalConfidenceModifier (vsClub: ClubId) (exp: PlayerExperience) : float =
        match exp.RivalRecord |> Map.tryFind vsClub with
        | None -> 0.0
        | Some r ->
            let total = r.Wins + r.Losses + r.Draws

            if total = 0 then
                0.0
            else
                let winRate = float r.Wins / float total
                (winRate - 0.5) * 0.10
