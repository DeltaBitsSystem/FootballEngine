namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

type NewsCandidate = NewsKind * NewsImportance * NewsScope * ClubId list * PlayerId list

/// A detector reads confirmed facts and proposes stories; it never mutates state or decides visibility.
type PulseDetector = GameState -> DateTime -> NewsCandidate list

module NewsDesk =

    /// The user's top-flight league mirrors how the client shows "your league".
    let private userLeagueClubs (gs: GameState) : Set<ClubId> =
        gs.Competitions
        |> Map.toSeq
        |> Seq.tryPick (fun (_, competition) ->
            match competition.Type with
            | NationalLeague(LeagueLevel 0, _) when List.contains gs.UserClubId competition.ClubIds ->
                Some(Set.ofList competition.ClubIds)
            | _ -> None)
        |> Option.defaultValue Set.empty

    let classifyScope (gs: GameState) (clubs: ClubId list) : NewsScope =
        if clubs |> List.contains gs.UserClubId then
            NewsScope.UserClub
        else
            let league = userLeagueClubs gs

            if clubs |> List.exists league.Contains then
                NewsScope.UserLeague
            else
                NewsScope.World

    let private budgetFor (config: WorldPulseConfig) scope =
        match scope with
        | NewsScope.UserClub -> config.DailyBudgetUserClub
        | NewsScope.UserLeague -> config.DailyBudgetUserLeague
        | NewsScope.World -> config.DailyBudgetWorld

    let private kindCase (kind: NewsKind) =
        Reflection.FSharpValue.GetUnionFields(kind, typeof<NewsKind>)
        |> fun (case, _) -> case.Tag

    let private primary (clubs: ClubId list) (players: PlayerId list) =
        match players, clubs with
        | p :: _, _ -> Some(true, p)
        | [], c :: _ -> Some(false, c)
        | [], [] -> None

    let private isDuplicate window (date: DateTime) (existing: NewsItem list) (kind, clubs, players) =
        let tag = kindCase kind
        let key = primary clubs players

        existing
        |> List.exists (fun item ->
            (date.Date - item.Date.Date).TotalDays <= float window
            && kindCase item.Kind = tag
            && primary item.Clubs item.Players = key)

    let private publishWith
        (budgetOf: NewsScope -> int)
        (config: WorldPulseConfig)
        (date: DateTime)
        (candidates: NewsCandidate list)
        (gs: GameState)
        : GameState =
        // Duplicates are checked against stored news only, so one batch may legitimately hold two similar items
        // that the scope budget then arbitrates.
        let fresh =
            candidates
            |> List.indexed
            |> List.filter (fun (_, (kind, _, _, clubs, players)) ->
                not (isDuplicate config.DuplicateWindowDays date gs.News (kind, clubs, players)))

        let chosen =
            fresh
            |> List.groupBy (fun (_, (_, _, scope, _, _)) -> scope)
            |> List.collect (fun (scope, items) ->
                items
                // Candidate index breaks importance ties so the choice is deterministic.
                |> List.sortWith (fun (i1, (_, imp1, _, _, _)) (i2, (_, imp2, _, _, _)) ->
                    match compare imp2 imp1 with
                    | 0 -> compare i1 i2
                    | c -> c)
                |> List.truncate (budgetOf scope))
            |> List.sortBy fst

        let items, nextId =
            chosen
            |> List.fold
                (fun (acc, id) (_, (kind, importance, scope, clubs, players)) ->
                    { Id = id
                      Date = date
                      Kind = kind
                      Importance = importance
                      Scope = scope
                      Clubs = clubs
                      Players = players }
                    :: acc,
                    id + 1)
                ([], gs.NextNewsId)

        { gs with
            News = (items @ gs.News) |> List.truncate config.NewsRetention
            NextNewsId = nextId }

    let publish (config: WorldPulseConfig) (date: DateTime) (candidates: NewsCandidate list) (gs: GameState) =
        publishWith (budgetFor config) config date candidates gs

    /// The season roll is a one-off burst the daily cap was never meant for: every champion, award and retirement
    /// must reach the feed, so one season-sized budget per scope replaces the daily one (importance still orders).
    let publishSeason (config: WorldPulseConfig) (date: DateTime) (candidates: NewsCandidate list) (gs: GameState) =
        publishWith (fun _ -> config.SeasonDeskBudget) config date candidates gs
