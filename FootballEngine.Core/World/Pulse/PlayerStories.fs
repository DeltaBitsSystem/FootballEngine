namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

/// The private lives of players: grievances that build up week by week, milestones, breakouts and retirement.
module PlayerStories =

    /// `importance` is (user club, anyone else): the user cares more about his own squad.
    let private candidate
        (gs: GameState)
        (kind: NewsKind)
        (importance: NewsImportance * NewsImportance)
        (clubs: ClubId list)
        (players: PlayerId list)
        : NewsCandidate =
        let level =
            if List.contains gs.UserClubId clubs then
                fst importance
            else
                snd importance

        kind, level, NewsDesk.classifyScope gs clubs, clubs, players

    let private squadOf (clubId: ClubId) (gs: GameState) : Player list =
        gs.Clubs
        |> Map.tryFind clubId
        |> Option.map (fun club -> club.PlayerIds |> List.choose gs.Players.TryFind)
        |> Option.defaultValue []

    /// Skill then id, so equal-skill players rank the same on every run.
    let bySkill (players: Player list) : Player list =
        players |> List.sortBy (fun p -> -p.CurrentSkill, p.Id)

    /// Whole months until the contract lapses. Contracts end at the season roll that follows `ExpiryYear`
    /// (SeasonPhase frees players with ExpiryYear <= Season on 1 July of the next year).
    let monthsLeft (date: DateTime) (contract: ContractInfo) : int =
        let expiry = DateTime(contract.ExpiryYear + 1, 7, 1)
        (expiry.Year - date.Year) * 12 + (expiry.Month - date.Month)

    /// Rank is taken over the whole contracted squad so an injured star is still the club's star.
    let private regularIds (config: WorldPulseConfig) (squad: Player list) : Set<PlayerId> =
        squad
        |> List.filter (fun p -> (Player.contractOf p).IsSome)
        |> bySkill
        |> List.truncate config.RegularStarterRank
        |> List.map _.Id
        |> Set.ofList

    let isRegular (config: WorldPulseConfig) (gs: GameState) (player: Player) : bool =
        match Player.clubOf player with
        | Some clubId -> regularIds config (squadOf clubId gs) |> Set.contains player.Id
        | None -> false

    // ---- weekly unrest -----------------------------------------------------------------------------------------

    /// A negotiation counts as interest only while it can still end in a move.
    let private liveInterest (date: DateTime) (gs: GameState) : Set<PlayerId> =
        gs.PendingNegotiations
        |> Map.values
        |> Seq.filter (fun n ->
            n.Deadline.Date >= date.Date
            && match n.Stage with
               | NegotiationStage.RejectedByClub _
               | NegotiationStage.RejectedByPlayer _
               | NegotiationStage.Collapsed _ -> false
               | _ -> true)
        |> Seq.map _.PlayerId
        |> Set.ofSeq

    let private notify date subject body category (gs: GameState) =
        GameState.addInboxMessage (Inbox.create date "Squad management" subject body category false) gs

    /// Contract stand-offs outrank interest because the player can walk for nothing; interest outranks minutes
    /// because it is the more urgent of the two for the user.
    let private causeOf
        (config: WorldPulseConfig)
        (date: DateTime)
        (interest: Set<PlayerId>)
        (regular: bool)
        (benchWeeks: int)
        (p: Player)
        : UnrestReason option =
        let contractStalled =
            regular
            && Player.contractOf p
               |> Option.exists (fun c -> monthsLeft date c <= config.ContractWarningMonths)

        let interested =
            regular
            && p.Unrest = Some UnrestReason.BiggerClubInterest
            && interest.Contains p.Id

        let benched =
            regular && p.Status = Available && benchWeeks >= config.UnrestBenchWeeks

        if contractStalled then Some UnrestReason.ContractStalled
        elif interested then Some UnrestReason.BiggerClubInterest
        elif benched then Some UnrestReason.LackOfMinutes
        else None

    type private Review =
        { Player: Player
          News: (NewsKind * (NewsImportance * NewsImportance)) list
          Mails: (string * string * InboxMessageCategory) list }

    let private reviewPlayer
        (config: WorldPulseConfig)
        (date: DateTime)
        (interest: Set<PlayerId>)
        (clubId: ClubId)
        (regulars: Set<PlayerId>)
        (eleven: Set<PlayerId> option)
        (p: Player)
        : Review =
        let regular = regulars.Contains p.Id

        let benchWeeks =
            match eleven with
            // Without a lineup there is no evidence either way, so the count is left alone.
            | None -> p.BenchWeeks
            | Some starters when starters.Contains p.Id -> 0
            | Some _ ->
                if regular && p.Status = Available then
                    p.BenchWeeks + 1
                else
                    0

        let cause = causeOf config date interest regular benchWeeks p
        let weeks = if cause.IsSome then p.UnrestWeeks + 1 else 0
        let request = cause.IsSome && weeks = config.TransferRequestWeeks

        let updated =
            { p with
                BenchWeeks = benchWeeks
                Unrest = cause
                UnrestWeeks = weeks
                Morale =
                    if cause.IsSome then
                        PlayerHealth.clamp (p.Morale - config.UnrestMoralePerWeek)
                    else
                        p.Morale
                TransferListed =
                    match cause with
                    | None -> false
                    | Some _ -> p.TransferListed || request }

        // Announced when the contract grievance begins, not every week it lasts.
        let contractMonths =
            match cause, Player.contractOf p with
            | Some UnrestReason.ContractStalled, Some contract when p.Unrest <> cause ->
                Some(max 0 (monthsLeft date contract))
            | _ -> None

        { Player = updated
          News =
            [ match contractMonths with
              | Some months ->
                  yield
                      NewsKind.ContractRunningOut(p.Id, clubId, months), (NewsImportance.Notable, NewsImportance.Minor)
              | None -> ()
              match cause with
              | Some reason when request ->
                  yield NewsKind.TransferRequest(p.Id, clubId, reason), (NewsImportance.Major, NewsImportance.Notable)
              | _ -> () ]
          Mails =
            [ if contractMonths.IsSome then
                  yield
                      ($"Contract running out: {p.Name}",
                       $"{p.Name}'s contract expires soon and no renewal has been agreed.",
                       InboxMessageCategory.Contract)
              if request then
                  yield
                      ($"Transfer request: {p.Name}",
                       $"{p.Name} is unhappy and has asked to be allowed to leave the club.",
                       InboxMessageCategory.Transfer) ] }

    let private reviewClub
        (config: WorldPulseConfig)
        (date: DateTime)
        (interest: Set<PlayerId>)
        (clubId: ClubId)
        (state: GameState, news: NewsCandidate list)
        : GameState * NewsCandidate list =
        let squad =
            squadOf clubId state |> List.filter (fun p -> (Player.contractOf p).IsSome)

        let regulars = regularIds config squad

        let eleven =
            GameState.getLineup clubId state
            |> Option.map (fun lineup -> lineup.Slots |> List.choose _.PlayerId |> Set.ofList)

        ((state, news), squad)
        ||> List.fold (fun (st, acc) p ->
            let review = reviewPlayer config date interest clubId regulars eleven p

            let st =
                if review.Player <> p then
                    { st with
                        Players = Map.add p.Id review.Player st.Players }
                else
                    st

            let st =
                if clubId = st.UserClubId then
                    (st, review.Mails)
                    ||> List.fold (fun s (subject, body, category) -> notify date subject body category s)
                else
                    st

            st,
            acc
            @ (review.News
               |> List.map (fun (kind, importance) -> candidate st kind importance [ clubId ] [ p.Id ])))

    /// Whoever has left their club (free agent, retired, promoted to a prospect) carries no grievance with them.
    let private clearStale (gs: GameState) : GameState =
        let stale =
            gs.Players
            |> Map.filter (fun _ p ->
                (Player.contractOf p).IsNone
                && (p.Unrest.IsSome || p.UnrestWeeks > 0 || p.BenchWeeks > 0 || p.TransferListed))

        { gs with
            Players =
                (gs.Players, stale)
                ||> Map.fold (fun players id p ->
                    Map.add
                        id
                        { p with
                            Unrest = None
                            UnrestWeeks = 0
                            BenchWeeks = 0
                            TransferListed = false }
                        players) }

    // ---- breakout ----------------------------------------------------------------------------------------------

    /// After a breakout the baseline moves up, so the same youngster needs another full `BreakoutGain` to be
    /// announced again: once per jump instead of every week the gain stays above the line.
    let breakouts (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState * NewsCandidate list =
        ((gs, []), gs.Players |> Map.toList)
        ||> List.fold (fun (state, news) (_, p) ->
            match Player.clubOf p with
            | Some clubId when
                Player.age date p < config.BreakoutMaxAge
                && p.CurrentSkill - p.SeasonStartSkill >= config.BreakoutGain
                ->
                { state with
                    Players =
                        Map.add
                            p.Id
                            { p with
                                SeasonStartSkill = p.CurrentSkill }
                            state.Players },
                news
                @ [ candidate
                        state
                        (NewsKind.Breakout(p.Id, clubId))
                        (NewsImportance.Major, NewsImportance.Notable)
                        [ clubId ]
                        [ p.Id ] ]
            | _ -> state, news)

    /// Weekly pass: unrest accrues, requests are made, breakouts are noticed.
    let weekly (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState * NewsCandidate list =
        let interest = liveInterest date gs

        let reviewed, unrestNews =
            ((gs, []), gs.Clubs |> Map.toList |> List.map fst)
            ||> List.fold (fun acc clubId -> reviewClub config date interest clubId acc)

        let reviewed, breakoutNews = reviewed |> clearStale |> breakouts config date
        reviewed, unrestNews @ breakoutNews

    // ---- milestones --------------------------------------------------------------------------------------------

    /// Drains the appearance queue written where the experience counters change, so every appearance is seen
    /// exactly once and milestones never depend on when the pulse happens to run.
    let milestones (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState * NewsCandidate list =
        match gs.PulseQueues.Appearances with
        | [] -> gs, []
        | queue ->
            let news =
                queue
                |> List.rev
                |> List.collect (fun a ->
                    match gs.Players |> Map.tryFind a.Player with
                    | None -> []
                    | Some p ->
                        let clubs = Player.clubOf p |> Option.toList

                        let make kind =
                            candidate gs kind (NewsImportance.Notable, NewsImportance.Minor) clubs [ p.Id ]

                        [ if a.Matches = 1 && Player.age date p <= config.DebutMaxAge then
                              yield make (NewsKind.Milestone(p.Id, MilestoneKind.Debut, 1))
                          if List.contains a.Matches config.AppearanceMilestones then
                              yield make (NewsKind.Milestone(p.Id, MilestoneKind.Appearances, a.Matches))
                          // Crossing, not equality: a brace taking a player from 49 to 51 still reaches 50.
                          for goals in config.GoalMilestones do
                              if a.GoalsAfter - a.GoalsInMatch < goals && goals <= a.GoalsAfter then
                                  yield make (NewsKind.Milestone(p.Id, MilestoneKind.Goals, goals)) ])

            { gs with
                PulseQueues = { gs.PulseQueues with Appearances = [] } },
            news

    // ---- season roll -------------------------------------------------------------------------------------------

    let private median (skills: int list) : int =
        match List.sort skills with
        | [] -> 0
        | sorted -> sorted[sorted.Length / 2]

    /// Runs on the freshly rolled state: breakouts first (they need last season's baseline, and the season's
    /// bulk training has just landed), then retirements, then the baseline moves to the new season.
    /// `previous` is the state before the roll, which still knows each player's club before contracts lapsed.
    let seasonRoll (config: WorldPulseConfig) (previous: GameState) (next: GameState) : GameState =
        let date = next.CurrentDate
        let gs, breakoutNews = breakouts config date next

        let clubMedians =
            previous.Clubs
            |> Map.map (fun _ club ->
                club.PlayerIds
                |> List.choose gs.Players.TryFind
                |> List.map _.CurrentSkill
                |> median)

        // Free agents have no club to be measured against, so the whole employed world is their yardstick.
        let worldMedian =
            previous.Clubs
            |> Map.values
            |> Seq.collect _.PlayerIds
            |> Seq.choose gs.Players.TryFind
            |> Seq.map _.CurrentSkill
            |> List.ofSeq
            |> median

        let topOfClub clubId =
            previous.Clubs
            |> Map.tryFind clubId
            |> Option.map (fun club ->
                club.PlayerIds
                |> List.choose gs.Players.TryFind
                |> bySkill
                |> List.truncate config.StarSaleTopRank
                |> List.map _.Id
                |> Set.ofList)
            |> Option.defaultValue Set.empty

        let retirees =
            gs.Players
            |> Map.values
            |> Seq.filter (fun p -> p.Affiliation <> Retired)
            |> Seq.choose (fun p ->
                let age = Player.age date p

                let formerClub =
                    previous.Players
                    |> Map.tryFind p.Id
                    |> Option.bind Player.clubOf
                    |> Option.orElse (Player.clubOf p)

                let reference =
                    formerClub |> Option.bind clubMedians.TryFind |> Option.defaultValue worldMedian

                if
                    age >= config.RetireHardAge
                    || (age >= config.RetireMinAge && p.CurrentSkill < reference)
                then
                    Some(p, age, formerClub)
                else
                    None)
            |> List.ofSeq

        let retiredIds = retirees |> List.map (fun (p, _, _) -> p.Id) |> Set.ofList

        let retiredPlayers =
            (gs.Players, retirees)
            ||> List.fold (fun players (p, _, _) ->
                Map.add
                    p.Id
                    { p with
                        Affiliation = Retired
                        Unrest = None
                        UnrestWeeks = 0
                        BenchWeeks = 0
                        TransferListed = false }
                    players)

        let affectedClubs =
            retirees
            |> List.choose (fun (_, _, club) -> club)
            |> List.append (
                gs.Clubs
                |> Map.toList
                |> List.filter (fun (_, club) -> club.PlayerIds |> List.exists retiredIds.Contains)
                |> List.map fst
            )
            |> List.distinct

        let withoutRetirees =
            { gs with
                Players = retiredPlayers
                Clubs =
                    gs.Clubs
                    |> Map.map (fun _ club ->
                        { club with
                            PlayerIds = club.PlayerIds |> List.filter (retiredIds.Contains >> not) })
                // A retired player must not be signed through a negotiation that was already open.
                PendingNegotiations =
                    gs.PendingNegotiations
                    |> Map.filter (fun _ n -> not (retiredIds.Contains n.PlayerId)) }

        // A lineup with a hole is unplayable (and the user's tactics must survive), so each vacated slot takes the
        // best remaining squad player not already in it; only a squad with no one left leaves the slot empty.
        let relined =
            (withoutRetirees, affectedClubs)
            ||> List.fold (fun state clubId ->
                let squad =
                    squadOf clubId state
                    |> List.filter (fun p -> p.Affiliation <> Retired && p.Status = Available)

                GameState.updateLineup
                    clubId
                    (Option.map (fun lineup ->
                        let slots =
                            lineup.Slots
                            |> List.map (fun slot ->
                                if slot.PlayerId |> Option.exists retiredIds.Contains then
                                    { slot with PlayerId = None }
                                else
                                    slot)

                        let filled =
                            slots
                            |> List.fold
                                (fun (acc: LineupSlot list) (slot: LineupSlot) ->
                                    match slot.PlayerId with
                                    | Some _ -> acc
                                    | None ->
                                        let used =
                                            acc |> List.choose (fun (s: LineupSlot) -> s.PlayerId) |> Set.ofList

                                        match
                                            LineupOps.bestForPosition
                                                (squad |> List.filter (fun p -> not (used.Contains p.Id)))
                                                slot.Role
                                        with
                                        | Some replacement ->
                                            acc
                                            |> List.map (fun (s: LineupSlot) ->
                                                if s.Index = slot.Index then
                                                    { s with
                                                        PlayerId = Some replacement.Id }
                                                else
                                                    s)
                                        | None -> acc)
                                slots

                        { lineup with Slots = filled }))
                    state)

        let announced =
            (relined, retirees)
            ||> List.fold (fun state (p, age, club) ->
                if club = Some state.UserClubId then
                    GameState.addInboxMessage
                        (Inbox.create
                            date
                            "Squad management"
                            $"Retirement: {p.Name}"
                            $"{p.Name} has retired from football at {age}."
                            InboxMessageCategory.Contract
                            false)
                        state
                else
                    state)

        let retirementNews =
            retirees
            |> List.map (fun (p, age, club) ->
                let importance =
                    match club with
                    | Some c when (topOfClub c).Contains p.Id -> NewsImportance.Notable
                    | _ -> NewsImportance.Minor

                candidate
                    announced
                    (NewsKind.Retirement(p.Id, age))
                    (NewsImportance.Major, importance)
                    (club |> Option.toList)
                    [ p.Id ])

        // New season, new baseline: gains are measured within a season.
        let rebased =
            { announced with
                Players =
                    announced.Players
                    |> Map.map (fun _ p ->
                        if p.SeasonStartSkill = p.CurrentSkill then
                            p
                        else
                            { p with
                                SeasonStartSkill = p.CurrentSkill }) }

        NewsDesk.publishSeason config date (breakoutNews @ retirementNews) rebased
