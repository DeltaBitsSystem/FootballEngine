namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

/// Confirmed match facts shared by the ledger and every match detector.
module MatchFacts =

    type PlayedMatch =
        { Id: MatchId
          Competition: Competition
          Fixture: MatchFixture
          HomeGoals: int
          AwayGoals: int }

    let private played (gs: GameState) : PlayedMatch list =
        gs.Competitions
        |> Map.toList
        |> List.collect (fun (_, competition) ->
            competition.Fixtures
            |> Map.toList
            |> List.choose (fun (id, fixture) ->
                match fixture.Played, fixture.HomeScore, fixture.AwayScore with
                | true, Some home, Some away ->
                    Some
                        { Id = id
                          Competition = competition
                          Fixture = fixture
                          HomeGoals = home
                          AwayGoals = away }
                | _ -> None))
        |> List.sortBy _.Id

    /// Fixtures confirmed but not yet absorbed. Results can be committed after their scheduled day (user matches
    /// pause the scheduler), so "newly played" is keyed by fixture id rather than by date. Oldest first.
    let newlyPlayed (date: DateTime) (gs: GameState) : PlayedMatch list =
        played gs
        |> List.filter (fun m ->
            m.Fixture.ScheduledDate.Date <= date.Date && not (gs.PulseProcessed.Contains m.Id))
        |> List.sortBy (fun m -> m.Fixture.ScheduledDate.Date, m.Id)

    /// Ids still needed to avoid reprocessing: fixtures that no longer exist or are unplayed cannot recur.
    let markProcessed (fresh: PlayedMatch list) (gs: GameState) : GameState =
        let alive = played gs |> List.map _.Id |> Set.ofList

        { gs with
            PulseProcessed =
                Set.intersect gs.PulseProcessed alive
                |> Set.union (fresh |> List.map _.Id |> Set.ofList) }

    /// Newest first; ties on the same day fall back to the fixture id so order never depends on map layout.
    let playedByClubUntil (club: ClubId) (date: DateTime) (gs: GameState) : PlayedMatch list =
        played gs
        |> List.filter (fun m ->
            m.Fixture.ScheduledDate.Date <= date.Date
            && (m.Fixture.HomeClubId = club || m.Fixture.AwayClubId = club))
        |> List.sortBy (fun m -> m.Fixture.ScheduledDate.Date, m.Id)
        |> List.rev

    let goalsOf (m: PlayedMatch) (club: ClubId) =
        if m.Fixture.HomeClubId = club then m.HomeGoals, m.AwayGoals else m.AwayGoals, m.HomeGoals

    let outcomeFor (m: PlayedMatch) (club: ClubId) : MatchOutcomeTag =
        let mine, theirs = goalsOf m club

        if mine > theirs then MatchOutcomeTag.Won
        elif mine = theirs then MatchOutcomeTag.Drawn
        else MatchOutcomeTag.Lost

    /// Scorers with the club they scored for; the engine id equals the domain id by construction of the lineup.
    let scorers (fixture: MatchFixture) : (PlayerId * ClubId) list =
        fixture.Events
        |> List.choose (fun event ->
            match event.Data with
            | FootballEngine.Core.Match.EventData.Goal(side, Some(FootballEngine.Core.Match.PlayerId pid), _, _) ->
                let club =
                    match side with
                    | FootballEngine.Core.Match.ClubSide.Home -> fixture.HomeClubId
                    | FootballEngine.Core.Match.ClubSide.Away -> fixture.AwayClubId

                Some(pid, club)
            | _ -> None)

    let isLeague (competition: Competition) =
        match competition.Type with
        | NationalLeague _ -> true
        | _ -> false

    /// Clubs inside the first three places of a league that has already started; an all-zero table ranks by id only.
    let topThree (gs: GameState) : Set<ClubId> =
        gs.Competitions
        |> Map.toSeq
        |> Seq.filter (fun (_, c) -> isLeague c)
        |> Seq.collect (fun (_, c) ->
            Competition.rankedStandings c
            |> List.truncate 3
            |> List.filter (fun (_, s) -> s.Played > 0)
            |> List.map fst)
        |> Set.ofSeq

module ResultsLedger =

    /// Counts each played fixture once: absorbed ids live in `PulseProcessed`, which `WorldPulse.run` updates
    /// after the detectors have seen the same fresh set.
    let apply (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        let today = MatchFacts.newlyPlayed date gs

        if today.IsEmpty then
            gs
        else
            let add club (m: MatchFacts.PlayedMatch) (clubs: Map<ClubId, Club>) =
                match Map.tryFind club clubs with
                | Some c ->
                    let results =
                        (MatchFacts.outcomeFor m club :: c.RecentResults) |> List.truncate config.RecentResultsKept

                    Map.add club { c with RecentResults = results } clubs
                | None -> clubs

            let clubs =
                today
                |> List.fold (fun acc m -> acc |> add m.Fixture.HomeClubId m |> add m.Fixture.AwayClubId m) gs.Clubs

            { gs with Clubs = clubs }

module MatchStories =

    let private importance (gs: GameState) (top: Set<ClubId>) (clubs: ClubId list) (baseline: NewsImportance) =
        if
            clubs |> List.contains gs.UserClubId
            || clubs |> List.exists top.Contains
        then
            NewsImportance.Headline
        else
            baseline

    let private candidate gs top kind baseline clubs players : NewsCandidate =
        kind, importance gs top clubs baseline, NewsDesk.classifyScope gs clubs, clubs, players

    let private reputation (gs: GameState) club =
        gs.Clubs |> Map.tryFind club |> Option.map _.Reputation

    let private isUpset (config: WorldPulseConfig) gs (m: MatchFacts.PlayedMatch) =
        let winner, loser =
            if m.HomeGoals > m.AwayGoals then
                m.Fixture.HomeClubId, m.Fixture.AwayClubId
            else
                m.Fixture.AwayClubId, m.Fixture.HomeClubId

        m.HomeGoals <> m.AwayGoals
        && (match reputation gs winner, reputation gs loser with
            | Some w, Some l -> l - w >= config.UpsetReputationGap
            | _ -> false)

    let private winnerLoser (m: MatchFacts.PlayedMatch) =
        if m.HomeGoals > m.AwayGoals then
            (m.Fixture.HomeClubId, m.HomeGoals), (m.Fixture.AwayClubId, m.AwayGoals)
        else
            (m.Fixture.AwayClubId, m.AwayGoals), (m.Fixture.HomeClubId, m.HomeGoals)

    let scorelines (config: WorldPulseConfig) : PulseDetector =
        fun gs date ->
            let top = MatchFacts.topThree gs

            MatchFacts.newlyPlayed date gs
            |> List.filter (fun m -> m.HomeGoals <> m.AwayGoals)
            |> List.choose (fun m ->
                let (winner, wg), (loser, lg) = winnerLoser m
                let clubs = [ winner; loser ]

                // An upset already tells the story of a lopsided score, so it replaces the big-win item.
                if isUpset config gs m then
                    Some(candidate gs top (NewsKind.Upset(winner, loser, wg, lg)) NewsImportance.Major clubs [])
                elif wg - lg >= config.BigWinMargin then
                    Some(candidate gs top (NewsKind.BigWin(winner, loser, wg, lg)) NewsImportance.Notable clubs [])
                else
                    None)

    let private runLength predicate (results: MatchOutcomeTag list) =
        results |> List.takeWhile predicate |> List.length

    let private isUnbeaten r = r <> MatchOutcomeTag.Lost
    let private isWinless r = r <> MatchOutcomeTag.Won

    let streaks (config: WorldPulseConfig) : PulseDetector =
        fun gs date ->
            let top = MatchFacts.topThree gs
            let lowest = List.head config.StreakThresholds
            MatchFacts.newlyPlayed date gs
            |> List.collect (fun m -> [ m.Fixture.HomeClubId; m.Fixture.AwayClubId ])
            |> List.distinct
            |> List.sort
            |> List.collect (fun club ->
                // Only clubs with newly absorbed results are inspected: a standing run would otherwise re-trigger daily.
                match Map.tryFind club gs.Clubs with
                | None -> []
                | Some c ->
                    let wins = runLength ((=) MatchOutcomeTag.Won) c.RecentResults
                    let unbeaten = runLength isUnbeaten c.RecentResults
                    let losses = runLength ((=) MatchOutcomeTag.Lost) c.RecentResults
                    let winless = runLength isWinless c.RecentResults

                    [ yield StreakKind.Wins, wins
                      if unbeaten <> wins then
                          yield StreakKind.Unbeaten, unbeaten
                      yield StreakKind.Losses, losses
                      if winless <> losses then
                          yield StreakKind.Winless, winless ]
                    |> List.filter (fun (_, length) -> List.contains length config.StreakThresholds)
                    |> List.map (fun (kind, length) ->
                        let scope = NewsDesk.classifyScope gs [ club ]

                        let baseline =
                            if length >= config.StreakMajorLength then
                                NewsImportance.Major
                            elif
                                length = lowest
                                && scope = NewsScope.World
                                && (kind = StreakKind.Unbeaten || kind = StreakKind.Winless)
                            then
                                NewsImportance.Minor
                            else
                                NewsImportance.Notable

                        candidate gs top (NewsKind.Streak(club, kind, length)) baseline [ club ] []))

    let newLeader (config: WorldPulseConfig) : PulseDetector =
        fun gs date ->
            let playedToday = MatchFacts.newlyPlayed date gs |> List.map _.Competition.Id |> Set.ofList

            gs.Competitions
            |> Map.toList
            |> List.choose (fun (id, competition) ->
                if not (MatchFacts.isLeague competition && playedToday.Contains id) then
                    None
                else
                    match Competition.rankedStandings competition with
                    | (leader, standing) :: _ when standing.Played >= config.NewLeaderMinRound ->
                        // The published history is the memory: a leader already announced is not news again.
                        let announced =
                            gs.News
                            |> List.tryPick (fun item ->
                                match item.Kind with
                                | NewsKind.NewLeader(club, comp) when comp = id -> Some club
                                | _ -> None)

                        if announced = Some leader then
                            None
                        else
                            let topFlight =
                                match competition.Type with
                                | NationalLeague(LeagueLevel 0, _) -> true
                                | _ -> false

                            let baseline = if topFlight then NewsImportance.Major else NewsImportance.Notable

                            let importance =
                                if leader = gs.UserClubId then NewsImportance.Headline else baseline

                            Some(
                                NewsKind.NewLeader(leader, id),
                                importance,
                                NewsDesk.classifyScope gs [ leader ],
                                [ leader ],
                                []
                            )
                    | _ -> None)

    let hatTricks (config: WorldPulseConfig) : PulseDetector =
        fun gs date ->
            let top = MatchFacts.topThree gs

            MatchFacts.newlyPlayed date gs
            |> List.collect (fun m ->
                MatchFacts.scorers m.Fixture
                |> List.countBy id
                |> List.filter (fun (_, goals) -> goals >= config.HatTrickGoals)
                |> List.map (fun ((player, club), goals) ->
                    candidate gs top (NewsKind.HatTrick(player, club, goals)) NewsImportance.Major [ club ] [ player ]))

    let scoringRuns (config: WorldPulseConfig) : PulseDetector =
        fun gs date ->
            let top = MatchFacts.topThree gs

            MatchFacts.newlyPlayed date gs
            |> List.collect (fun m -> MatchFacts.scorers m.Fixture)
            |> List.distinct
            |> List.sort
            |> List.choose (fun (player, club) ->
                let run =
                    MatchFacts.playedByClubUntil club date gs
                    |> List.takeWhile (fun m ->
                        MatchFacts.scorers m.Fixture |> List.exists (fun (p, _) -> p = player))
                    |> List.length
                // Multiples keep a long run alive in the news without reporting it every single match.
                if run >= config.ScoringRunMatches && run % config.ScoringRunMatches = 0 then
                    Some(candidate gs top (NewsKind.ScoringRun(player, run)) NewsImportance.Notable [ club ] [ player ])
                else
                    None)

    let private topByReputation (gs: GameState) (count: int) (competition: Competition) : Set<ClubId> =
        competition.ClubIds
        |> List.choose (fun id -> gs.Clubs |> Map.tryFind id |> Option.map (fun c -> id, c.Reputation))
        // Club id breaks reputation ties so the pair never depends on list order.
        |> List.sortBy (fun (id, rep) -> -rep, id)
        |> List.truncate count
        |> List.map fst
        |> Set.ofList

    let private evenHistory (config: WorldPulseConfig) (gs: GameState) date (home: ClubId) (away: ClubId) =
        let meetings =
            MatchFacts.playedByClubUntil home date gs
            |> List.filter (fun m -> m.Fixture.HomeClubId = away || m.Fixture.AwayClubId = away)

        let balance =
            meetings
            |> List.sumBy (fun m ->
                let mine, theirs = MatchFacts.goalsOf m home
                mine - theirs)

        meetings.Length >= config.ClasicoMinMeetings
        && abs balance <= config.ClasicoMaxGoalDifference

    let clasicoPreviews (config: WorldPulseConfig) : PulseDetector =
        fun gs date ->
            let target = date.Date.AddDays(float config.ClasicoLookaheadDays)
            let top = MatchFacts.topThree gs

            gs.Competitions
            |> Map.toList
            |> List.collect (fun (_, competition) ->
                let elite = topByReputation gs config.ClasicoTopReputationCount competition

                competition.Fixtures
                |> Map.toList
                |> List.filter (fun (_, f) -> not f.Played && f.ScheduledDate.Date = target)
                |> List.choose (fun (_, f) ->
                    let topPair =
                        MatchFacts.isLeague competition
                        && elite.Contains f.HomeClubId
                        && elite.Contains f.AwayClubId

                    if topPair || evenHistory config gs date f.HomeClubId f.AwayClubId then
                        Some(
                            candidate
                                gs
                                top
                                (NewsKind.Clasico(f.HomeClubId, f.AwayClubId))
                                NewsImportance.Notable
                                [ f.HomeClubId; f.AwayClubId ]
                                []
                        )
                    else
                        None))

    let detectors (config: WorldPulseConfig) : PulseDetector list =
        [ scorelines config
          streaks config
          newLeader config
          hatTricks config
          scoringRuns config
          clasicoPreviews config ]
