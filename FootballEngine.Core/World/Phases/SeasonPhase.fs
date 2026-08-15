namespace FootballEngine.World.Phases

open System
open FootballEngine.Domain
open FootballEngine.Stats
open FootballEngine.World

module SeasonPhase =

    let private seasonWeeks (gs: GameState) =
        let seasonStart = DateTime(gs.Season, 7, 1)

        let seasonEnd =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq |> Seq.map (fun (_, f) -> f.ScheduledDate))
            |> Seq.toList
            |> function
                | [] -> DateTime(gs.Season + 1, 5, 1)
                | dates -> List.max dates

        int (seasonEnd - seasonStart).TotalDays / 7

    let private processContracts (gs: GameState) : GameState =
        { gs with
            Players =
                gs.Players
                |> Map.map (fun _ p ->
                    match p.Affiliation with
                    | Contracted(_, c) when c.ExpiryYear <= gs.Season -> { p with Affiliation = FreeAgent }
                    | _ -> p)
            Clubs =
                gs.Clubs
                |> Map.map (fun clubId club ->
                    let active =
                        gs.Players
                        |> Map.toList
                        |> List.choose (fun (_, p) ->
                            match p.Affiliation with
                            | Contracted(cid, c) when cid = clubId && c.ExpiryYear > gs.Season -> Some p.Id
                            | _ -> None)

                    { club with PlayerIds = active }) }

    let private distributeRevenue (gs: GameState) : GameState =
        let revenueFor (standing: LeagueStanding) (total: int) =
            let posBonus = max 0 (total - standing.Played) |> decimal
            10_000_000m + posBonus * 500_000m

        let updatedClubs =
            (gs.Clubs, gs.Competitions)
            ||> Map.fold (fun clubs _ comp ->
                match comp.Type with
                | NationalLeague _ ->
                    let ranked = Competition.rankedStandings comp
                    let total = ranked.Length

                    (clubs, ranked)
                    ||> List.fold (fun acc (clubId, standing) ->
                        acc
                        |> Map.change clubId (Option.map (Club.adjustBudget (revenueFor standing total))))
                | _ -> clubs)

        { gs with Clubs = updatedClubs }

    let private resetConditions (gs: GameState) : GameState =
        { gs with
            Players =
                gs.Players
                |> Map.map (fun _ p ->
                    { p with
                        Condition = 100
                        Morale = clamp 40 100 (p.Morale + 10)
                        Status =
                            match p.Status with
                            | Injured(_, until) when until <= gs.CurrentDate -> Available
                            | other -> other }) }

    let private refreshAiLineups (gs: GameState) : GameState =
        gs.Clubs
        |> Map.toList
        |> List.filter (fun (id, _) -> id <> gs.UserClubId)
        |> List.fold (fun acc (clubId, _) -> LineupOps.ensureForClub clubId acc) gs

    let private applyLeagueConsequences (gs: GameState) : GameState =
        let objective (rank: int) (total: int) =
            if rank = 1 then LeagueObjective WinLeague
            elif rank <= total / 4 then LeagueObjective TopHalf
            elif rank <= total * 3 / 4 then LeagueObjective MidTable
            else LeagueObjective Survival

        { gs with
            Clubs =
                (gs.Clubs, gs.Competitions)
                ||> Map.fold (fun clubs _ comp ->
                    match comp.Type with
                    | NationalLeague _ ->
                        let ranked = Competition.rankedStandings comp
                        let total = ranked.Length

                        ranked
                        |> List.mapi (fun i (clubId, _) -> i + 1, clubId)
                        |> List.fold
                            (fun acc (rank, clubId) ->
                                acc |> Map.change clubId (Option.map (Club.setObjective (objective rank total))))
                            clubs
                    | _ -> clubs) }


    let computeSeasonSummary (gs: GameState) : string list =
        gs.Competitions
        |> Map.tryFindKey (fun _ comp ->
            match comp.Type, comp.Country with
            | NationalLeague(LeagueLevel 0, _), Some c when c = gs.PrimaryCountry -> true
            | _ -> false)
        |> Option.map (fun id ->
            gs.Competitions[id]
            |> Competition.rankedStandings
            |> List.mapi (fun i (clubId, s) ->
                let name =
                    gs.Clubs |> Map.tryFind clubId |> Option.map _.Name |> Option.defaultValue "?"

                $"{i + 1}. {name}  {s.Points}pts  {s.GoalsFor}-{s.GoalsAgainst}"))
        |> Option.defaultValue []

    // ── Pipeline de fin de temporada ───────────────────────────────────────

    let runEndOfSeason (state: GameState) : GameState =
        let totalWeeks = seasonWeeks state

        state
        |> distributeRevenue
        |> TrainingEngine.applyRemainingSeasonTraining state.CurrentDate state.TrainingWeeksApplied totalWeeks
        |> PlayerDevelopment.developAll
        |> processContracts
        |> applyLeagueConsequences
        |> resetConditions
        |> refreshAiLineups
        |> fun s ->
            { s with
                Season = s.Season + 1
                CurrentDate = DateTime(s.Season + 1, 7, 1)
                TrainingWeeksApplied = 0 }
        |> FootballEngine.Generation.SeasonGen.regenerateFixtures

    let make: WorldPhase =
        { Tag = Season
          Frequency = Seasonal
          Run = fun _clock state -> runEndOfSeason state }
