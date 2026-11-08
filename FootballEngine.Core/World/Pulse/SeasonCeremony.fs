namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

/// Closes a season: champions, promotions, relegations and the yearly awards, read from the finished season
/// before the roll replaces its fixtures, standings and skill baselines.
module SeasonCeremony =

    /// Match events name players by engine id, which equals the domain id by construction of the lineup.
    module private Participation =
        open FootballEngine.Core.Match

        let actors (data: EventData) : int list =
            match data with
            | Possession(_, PlayerId p) -> [ p ]
            | FootContact(PlayerId p, _, _, _, _, _) -> [ p ]
            | Pass(PlayerId a, receiver, _, _, _, _, _, _, _) ->
                a :: (receiver |> Option.map (fun (PlayerId r) -> r) |> Option.toList)
            | Shot(PlayerId p, _, _, _, _, _) -> [ p ]
            | Goal(_, scorer, assist, _) ->
                [ yield! scorer |> Option.map (fun (PlayerId p) -> p) |> Option.toList
                  yield! assist |> Option.map (fun (PlayerId p) -> p) |> Option.toList ]
            | Foul(PlayerId a, PlayerId b, _, _, _) -> [ a; b ]
            | Card(PlayerId p, _) -> [ p ]
            | Interception(PlayerId a, PlayerId b)
            | Tackle(PlayerId a, PlayerId b)
            | Save(PlayerId a, PlayerId b) -> [ a; b ]
            | CrossAttempt(PlayerId p, _, _) -> [ p ]
            | Dribble(PlayerId p, _, _, _) -> [ p ]
            | EventData.Injury(PlayerId p, _) -> [ p ]
            | SubstitutionApplied(PlayerId o, PlayerId i) -> [ o; i ]
            | Offside(PlayerId p) -> [ p ]
            | RedCardIssued(PlayerId p) -> [ p ]
            | _ -> []

        let scorerAndAssist (data: EventData) : (ClubSide * int * int option) option =
            match data with
            | Goal(side, Some(PlayerId s), assist, _) -> Some(side, s, assist |> Option.map (fun (PlayerId a) -> a))
            | _ -> None

    type Winner =
        { Kind: AwardKind
          Competition: CompetitionId
          Club: ClubId
          Player: PlayerId option
          Staff: StaffId option }

    type private Tally =
        {
            Goals: int
            Assists: int
            Appearances: int
            /// Club of the latest goal, so a player sold mid-season is still credited to a club he scored for.
            ScoredFor: ClubId option
        }

    let private emptyTally =
        { Goals = 0
          Assists = 0
          Appearances = 0
          ScoredFor = None }

    let private leagues (gs: GameState) : (CompetitionId * Competition) list =
        gs.Competitions
        |> Map.toList
        |> List.filter (fun (_, c) -> MatchFacts.isLeague c)

    /// Appearances are the fixtures in which the player acts in some recorded event: fixtures keep events but no
    /// lineups, and a starter who never touches the ball in 90 minutes is indistinguishable from an absentee anyway.
    let private tallies (competition: Competition) : Map<PlayerId, Tally> =
        (Map.empty, competition.Fixtures |> Map.toList |> List.sortBy fst)
        ||> List.fold (fun acc (_, fixture) ->
            if not fixture.Played then
                acc
            else
                let present =
                    fixture.Events
                    |> List.collect (fun e -> Participation.actors e.Data)
                    |> List.distinct

                let withPresence =
                    (acc, present)
                    ||> List.fold (fun m p ->
                        let t = m |> Map.tryFind p |> Option.defaultValue emptyTally

                        Map.add
                            p
                            { t with
                                Appearances = t.Appearances + 1 }
                            m)

                (withPresence, fixture.Events)
                ||> List.fold (fun m e ->
                    match Participation.scorerAndAssist e.Data with
                    | Some(side, scorer, assist) ->
                        let club =
                            match side with
                            | FootballEngine.Core.Match.ClubSide.Home -> fixture.HomeClubId
                            | FootballEngine.Core.Match.ClubSide.Away -> fixture.AwayClubId

                        let m =
                            let t = m |> Map.tryFind scorer |> Option.defaultValue emptyTally

                            Map.add
                                scorer
                                { t with
                                    Goals = t.Goals + 1
                                    ScoredFor = Some club }
                                m

                        match assist with
                        | Some a ->
                            let t = m |> Map.tryFind a |> Option.defaultValue emptyTally
                            Map.add a { t with Assists = t.Assists + 1 } m
                        | None -> m
                    | None -> m))

    let private skillOf (gs: GameState) id =
        gs.Players
        |> Map.tryFind id
        |> Option.map _.CurrentSkill
        |> Option.defaultValue 0

    let private topScorer (gs: GameState) (cid: CompetitionId) (tally: Map<PlayerId, Tally>) : Winner option =
        tally
        |> Map.toList
        |> List.filter (fun (_, t) -> t.Goals > 0 && t.ScoredFor.IsSome)
        // Fewer appearances for the same goals means more efficient; skill and id make the order total.
        |> List.sortBy (fun (id, t) -> -t.Goals, t.Appearances, -(skillOf gs id), id)
        |> List.tryHead
        |> Option.map (fun (id, t) ->
            { Kind = AwardKind.TopScorer
              Competition = cid
              Club = t.ScoredFor.Value
              Player = Some id
              Staff = None })

    let private bestPlayer
        (config: WorldPulseConfig)
        (gs: GameState)
        (kind: AwardKind)
        (cid: CompetitionId)
        (competition: Competition)
        (tally: Map<PlayerId, Tally>)
        (eligible: Player -> bool)
        : Winner option =
        let ranked = Competition.rankedStandings competition |> List.map fst
        let clubs = ranked.Length

        let rankOf = ranked |> List.mapi (fun i club -> club, i) |> Map.ofList

        let positionBonus club =
            match Map.tryFind club rankOf with
            | Some i when clubs > 1 -> config.AwardPositionWeight * float (clubs - 1 - i) / float (clubs - 1)
            | _ -> 0.0

        competition.ClubIds
        |> List.collect (fun club ->
            GameState.getSquad club gs
            |> List.choose (fun p ->
                let t = tally |> Map.tryFind p.Id |> Option.defaultValue emptyTally

                if
                    p.Affiliation <> Retired
                    && t.Appearances >= config.AwardMinAppearances
                    && eligible p
                then
                    let score =
                        float t.Goals * config.AwardGoalWeight
                        + float t.Assists * config.AwardAssistWeight
                        + float p.CurrentSkill * config.AwardSkillWeight
                        + positionBonus club

                    Some(p, club, score)
                else
                    None))
        |> List.sortBy (fun (p, _, score) -> -score, p.Id)
        |> List.tryHead
        |> Option.map (fun (p, club, _) ->
            { Kind = kind
              Competition = cid
              Club = club
              Player = Some p.Id
              Staff = None })

    /// Over-performance is expected rank minus actual rank; reputation is fixed within a season (nothing but
    /// generation writes it), so the order at the finish equals the order at the start and no snapshot is needed.
    let private managerOfSeason (gs: GameState) (cid: CompetitionId) (competition: Competition) : Winner option =
        let actual =
            Competition.rankedStandings competition
            |> List.mapi (fun i (club, s) -> club, (i + 1, s.Points))
            |> Map.ofList

        let expected =
            competition.ClubIds
            |> List.choose (fun id -> gs.Clubs |> Map.tryFind id)
            |> List.sortBy (fun club -> -club.Reputation, club.Id)
            |> List.mapi (fun i club -> club.Id, i + 1)

        expected
        |> List.choose (fun (club, expectedRank) ->
            match Map.tryFind club actual, GameState.headCoach club gs with
            | Some(rank, points), Some coach when expectedRank - rank > 0 ->
                Some(expectedRank - rank, points, club, coach)
            | _ -> None)
        |> List.sortBy (fun (delta, points, club, _) -> -delta, -points, club)
        |> List.tryHead
        |> Option.map (fun (_, _, club, coach) ->
            { Kind = AwardKind.ManagerOfSeason
              Competition = cid
              Club = club
              Player = None
              Staff = Some coach.Id })

    /// Pure: at most one winner per kind per league, read from the finished season.
    let awards (config: WorldPulseConfig) (finished: GameState) : Winner list =
        leagues finished
        |> List.collect (fun (cid, competition) ->
            let tally = tallies competition

            let isYoung p =
                Player.age finished.CurrentDate p <= config.YoungPlayerMaxAge

            [ topScorer finished cid tally
              bestPlayer config finished AwardKind.PlayerOfSeason cid competition tally (fun _ -> true)
              bestPlayer config finished AwardKind.YoungPlayer cid competition tally isYoung
              managerOfSeason finished cid competition ]
            |> List.choose id)

    let private candidate
        (gs: GameState)
        (kind: NewsKind)
        (user: NewsImportance, other: NewsImportance)
        (clubs: ClubId list)
        (players: PlayerId list)
        : NewsCandidate =
        let importance = if List.contains gs.UserClubId clubs then user else other
        kind, importance, NewsDesk.classifyScope gs clubs, clubs, players

    let private champions (finished: GameState) : NewsCandidate list =
        finished.Competitions
        |> Map.toList
        |> List.choose (fun (cid, competition) ->
            let winner =
                if MatchFacts.isLeague competition then
                    // An empty table ranks by id; only a table that was actually played has a champion.
                    Competition.rankedStandings competition
                    |> List.tryHead
                    |> Option.filter (fun (_, s) -> s.Played > 0)
                    |> Option.map fst
                else
                    competition.KnockoutTies.Values
                    |> Seq.tryFind (fun tie -> tie.Round = Final)
                    |> Option.bind _.WinnerId

            winner
            |> Option.map (fun club ->
                candidate
                    finished
                    (NewsKind.Champion(club, cid))
                    (NewsImportance.Headline, NewsImportance.Major)
                    [ club ]
                    []))

    let private movements (finished: GameState) (record: SeasonRecord) : NewsCandidate list =
        let leagueOf club =
            leagues finished
            |> List.tryFind (fun (_, c) -> List.contains club c.ClubIds)
            |> Option.map fst

        let make kind clubs =
            clubs
            |> List.choose (fun club ->
                leagueOf club
                |> Option.map (fun cid ->
                    candidate
                        finished
                        (kind (club, cid))
                        (NewsImportance.Headline, NewsImportance.Notable)
                        [ club ]
                        []))

        make NewsKind.Promotion record.Promoted
        @ make NewsKind.Relegation record.Relegated

    let private awardCandidates (finished: GameState) (winners: Winner list) : NewsCandidate list =
        winners
        |> List.map (fun w ->
            candidate
                finished
                (NewsKind.Award(w.Kind, w.Player, w.Staff, w.Competition))
                (NewsImportance.Headline, NewsImportance.Major)
                [ w.Club ]
                (w.Player |> Option.toList))

    let private ownAwardMessage (finished: GameState) (date: DateTime) (w: Winner) : InboxMessage option =
        if w.Club <> finished.UserClubId then
            None
        else
            let who =
                match w.Player, w.Staff with
                | Some id, _ -> finished.Players |> Map.tryFind id |> Option.map _.Name
                | None, Some id -> finished.Staff |> Map.tryFind id |> Option.map _.Name
                | None, None -> None

            let competition =
                finished.Competitions
                |> Map.tryFind w.Competition
                |> Option.map _.Name
                |> Option.defaultValue ""

            let title =
                match w.Kind with
                | AwardKind.TopScorer -> "Top scorer"
                | AwardKind.PlayerOfSeason -> "Player of the season"
                | AwardKind.YoungPlayer -> "Young player of the season"
                | AwardKind.ManagerOfSeason -> "Manager of the season"

            let winner = who |> Option.defaultValue "Your club"

            Some(
                Inbox.create
                    date
                    "Competition desk"
                    $"{title}: {competition}"
                    $"{winner} won the {title.ToLower()} award in {competition}."
                    InboxMessageCategory.BoardUpdate
                    false
            )

    /// `finished` is the state before the roll (competitions, squads and staff as the season ended); `rolled` is the
    /// state after it. The record archived by the roll supplies the promoted and relegated clubs.
    let apply (config: WorldPulseConfig) (finished: GameState) (rolled: GameState) : GameState =
        let winners = awards config finished

        let movementNews =
            rolled.SeasonHistory
            |> List.tryHead
            |> Option.filter (fun r -> r.Season = finished.Season)
            |> Option.map (movements finished)
            |> Option.defaultValue []

        let candidates =
            champions finished @ movementNews @ awardCandidates finished winners

        let withInbox =
            (rolled, winners)
            ||> List.fold (fun state w ->
                match ownAwardMessage finished rolled.CurrentDate w with
                | Some message -> GameState.addInboxMessage message state
                | None -> state)

        NewsDesk.publishSeason config rolled.CurrentDate candidates withInbox
