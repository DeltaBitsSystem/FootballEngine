namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain
open FootballEngine.Domain.LineupOps
open FootballEngine.Generation

/// Board confidence and fan mood: what the results mean relative to what the club's standing promised.
module Pressure =

    let expectedPoints (config: WorldPulseConfig) (ownReputation: int) (rivalReputation: int) (isHome: bool) : float =
        let edge =
            config.ExpectedPointsSpread
            * Math.Tanh(float (ownReputation - rivalReputation) / config.ReputationScale)

        let venue =
            if isHome then
                config.HomeAdvantagePoints
            else
                -config.HomeAdvantagePoints

        Math.Clamp(config.ExpectedPointsBase + edge + venue, config.ExpectedPointsMin, config.ExpectedPointsMax)

    let pointsOf (outcome: MatchOutcomeTag) : int =
        match outcome with
        | MatchOutcomeTag.Won -> 3
        | MatchOutcomeTag.Drawn -> 1
        | MatchOutcomeTag.Lost -> 0

    /// Reversion and the result delta are summed before rounding so a small pull is not lost to rounding twice.
    let adjust (config: WorldPulseConfig) (initial: int) (current: int) (gain: float) (surplus: float) : int =
        let reverted = float current + float (initial - current) * config.MeanReversionRate

        Math.Clamp(int (Math.Round(reverted + gain * surplus, MidpointRounding.AwayFromZero)), 0, 100)

    let fansBand (config: WorldPulseConfig) (mood: int) : FansMoodBand =
        if mood < config.FansFuriousBelow then
            FansMoodBand.Furious
        elif mood < config.FansRestlessBelow then
            FansMoodBand.Restless
        elif mood < config.FansEuphoricFrom then
            FansMoodBand.Content
        else
            FansMoodBand.Euphoric

    let private applyFixture (config: WorldPulseConfig) (m: MatchFacts.PlayedMatch) (clubs: Map<ClubId, Club>) =
        let side (club: ClubId) (rival: ClubId) (isHome: bool) (clubs: Map<ClubId, Club>) =
            match Map.tryFind club clubs, Map.tryFind rival clubs with
            | Some own, Some other ->
                let surplus =
                    float (pointsOf (MatchFacts.outcomeFor m club))
                    - expectedPoints config own.Reputation other.Reputation isHome

                Map.add
                    club
                    { own with
                        BoardConfidence =
                            adjust
                                config
                                config.InitialBoardConfidence
                                own.BoardConfidence
                                config.ConfidenceGain
                                surplus
                        FansMood = adjust config config.InitialFansMood own.FansMood config.FansMoodGain surplus }
                    clubs
            | _ -> clubs

        clubs
        |> side m.Fixture.HomeClubId m.Fixture.AwayClubId true
        |> side m.Fixture.AwayClubId m.Fixture.HomeClubId false

    /// Fixtures of the club under the current manager. Strictly after `ManagerSince`: the day a coach is replaced
    /// the match already belongs to his predecessor.
    let matchesInCharge (date: DateTime) (club: Club) (gs: GameState) : int =
        MatchFacts.playedByClubUntil club.Id date gs
        |> List.filter (fun m ->
            club.ManagerSince
            |> Option.forall (fun since -> m.Fixture.ScheduledDate.Date > since.Date))
        |> List.length

    // ---- replacement coach -------------------------------------------------------------------------------

    let private leagueLevelOf (gs: GameState) (club: ClubId) : int =
        gs.Competitions.Values
        |> Seq.tryPick (fun c ->
            match c.Type with
            | NationalLeague(LeagueLevel level, _) when List.contains club c.ClubIds -> Some level
            | _ -> None)
        |> Option.defaultValue 1

    let private bestUnemployed (excluded: Set<StaffId>) (gs: GameState) : Staff option =
        gs.Staff.Values
        |> Seq.filter (fun s ->
            s.Status = StaffStatus.Unemployed
            && s.Id <> gs.UserStaffId
            && not (excluded.Contains s.Id)
            && (s.Role = HeadCoach || Staff.canBecomeHeadCoach s))
        // Judged as a head coach whatever the current role, and reputation/id make the order total.
        |> Seq.sortBy (fun s -> -(Staff.effectiveAbility { s with Role = HeadCoach }), -s.Reputation, s.Id)
        |> Seq.tryHead

    let private generateCoach (date: DateTime) (clubId: ClubId) (gs: GameState) : Staff option =
        gs.Clubs
        |> Map.tryFind clubId
        |> Option.bind (fun club -> gs.Countries |> Map.tryFind club.Nationality)
        |> Option.map (fun countryData ->
            let nextId =
                if gs.Staff.IsEmpty then
                    1
                else
                    (gs.Staff |> Map.toSeq |> Seq.map fst |> Seq.max) + 1

            let seed = PulseRandom.seedFor gs.ExecutionSeed date "replacement-coach" clubId

            FootballEngine.RNG.withSeed seed (fun () ->
                StaffGen.create nextId HeadCoach clubId countryData (leagueLevelOf gs clubId) date.Year))

    /// Pure: contracts the best available coach at `clubId`, or yields nothing when none can be found or made.
    let private appoint
        (config: WorldPulseConfig)
        (date: DateTime)
        (clubId: ClubId)
        (excluded: Set<StaffId>)
        (previousSalary: decimal option)
        (gs: GameState)
        : (GameState * Staff) option =
        let candidate =
            bestUnemployed excluded gs |> Option.orElse (generateCoach date clubId gs)

        match candidate, Map.tryFind clubId gs.Clubs with
        | Some coach, Some club ->
            let squad = club.PlayerIds |> List.choose gs.Players.TryFind

            let formation =
                coach.Attributes.Coaching.PreferredFormation
                |> Option.defaultValue (bestFormation squad)

            let salary =
                previousSalary
                |> Option.orElse (coach.Contract |> Option.map _.Salary)
                |> Option.defaultValue 0m

            // The previous club's lineup names that club's players, so it must be rebuilt for this squad.
            let hired =
                { coach with
                    Role = HeadCoach
                    Status = Active
                    Contract =
                        Some
                            { ClubId = clubId
                              Salary = salary
                              ExpiryYear = date.Year + config.ReplacementContractYears }
                    Attributes.Coaching.Lineup = None }
                |> fun c -> autoLineup c squad formation

            let club =
                { club with
                    ManagerSince = Some date
                    BoardConfidence = config.HoneymoonConfidence
                    StaffIds =
                        if List.contains hired.Id club.StaffIds then
                            club.StaffIds
                        else
                            club.StaffIds @ [ hired.Id ] }

            Some(
                { gs with
                    Staff = gs.Staff |> Map.add hired.Id hired
                    Clubs = gs.Clubs |> Map.add clubId club },
                hired
            )
        | _ -> None

    let private candidate gs kind baseline (clubId: ClubId) : NewsCandidate =
        let importance =
            if clubId = gs.UserClubId then
                NewsImportance.Headline
            else
                baseline

        kind, importance, NewsDesk.classifyScope gs [ clubId ], [ clubId ], []

    /// Replaces the head coach of `clubId`. The old coach is removed first so he cannot be re-hired by the club.
    let replaceCoach
        (config: WorldPulseConfig)
        (date: DateTime)
        (clubId: ClubId)
        (coach: Staff)
        (excluded: Set<StaffId>)
        (gs: GameState)
        : (GameState * NewsCandidate list * Staff) option =
        let matches =
            gs.Clubs
            |> Map.tryFind clubId
            |> Option.map (fun c -> matchesInCharge date c gs)

        let fired =
            { gs with
                Staff =
                    gs.Staff
                    |> Map.add
                        coach.Id
                        { coach with
                            Contract = None
                            Status = StaffStatus.Unemployed }
                Clubs =
                    gs.Clubs
                    |> Map.change
                        clubId
                        (Option.map (fun c ->
                            { c with
                                StaffIds = c.StaffIds |> List.filter ((<>) coach.Id) })) }

        // New games seed the user's club with its generated head coach beside the user; in that case the existing
        // coach already runs the team and a third one must not be created.
        let successor =
            match GameState.headCoach clubId fired with
            | Some existing ->
                fired.Clubs
                |> Map.tryFind clubId
                |> Option.map (fun club ->
                    { fired with
                        Clubs =
                            fired.Clubs
                            |> Map.add
                                clubId
                                { club with
                                    ManagerSince = Some date
                                    BoardConfidence = config.HoneymoonConfidence } },
                    existing)
            | None ->
                appoint config date clubId (Set.add coach.Id excluded) (coach.Contract |> Option.map _.Salary) fired

        match matches, successor with
        | Some played, Some(next, hired) ->
            Some(
                next,
                [ candidate next (NewsKind.ManagerSacked(clubId, coach.Id, played)) NewsImportance.Headline clubId
                  candidate next (NewsKind.ManagerAppointed(clubId, hired.Id)) NewsImportance.Notable clubId ],
                hired
            )
        | _ -> None

    // ---- daily pressure ------------------------------------------------------------------------------------

    let private crossedDown threshold (before: int) (after: int) =
        before >= threshold && after < threshold

    let private userEmployed (gs: GameState) =
        GameState.userManager gs
        |> Option.exists (fun s -> Staff.isEmployed s && Staff.clubId s = Some gs.UserClubId)

    let private boardMessage date subject body (gs: GameState) =
        GameState.addInboxMessage
            (Inbox.create date "Board of Directors" subject body InboxMessageCategory.BoardUpdate false)
            gs

    /// The club keeps playing under a successor; `UserClubId` stays on it as the user's last club until a new job.
    let private dismissUser (config: WorldPulseConfig) (date: DateTime) (taken: Set<StaffId>) (gs: GameState) =
        let clubId = gs.UserClubId

        match GameState.userManager gs with
        | Some manager ->
            match replaceCoach config date clubId manager taken gs with
            | Some(next, news, _) ->
                // The lineup names the old club's players; the next club builds its own on appointment.
                let unemployed =
                    { next.Staff[manager.Id] with
                        Attributes.Coaching.Lineup = None
                        Reputation = int (float manager.Reputation * config.SackReputationFactor) }

                { next with
                    Staff = next.Staff |> Map.add manager.Id unemployed
                    CareerHistory =
                        ManagerCareerEvent.ManagerDismissed(date, clubId, "Board confidence collapsed")
                        :: next.CareerHistory }
                |> boardMessage
                    date
                    "Dismissed"
                    "The board has terminated your contract. Clubs looking for a manager will contact you.",
                news
            | None -> gs, []
        | None -> gs, []

    let apply
        (config: WorldPulseConfig)
        (date: DateTime)
        (fresh: MatchFacts.PlayedMatch list)
        (gs: GameState)
        : GameState * NewsCandidate list =
        let before = gs.Clubs
        let updated = fresh |> List.fold (fun clubs m -> applyFixture config m clubs) before
        let gs = { gs with Clubs = updated }

        let touched =
            updated
            |> Map.toList
            |> List.filter (fun (id, club) -> before[id] <> club)
            |> List.map fst

        // Sacking comes first so a club that just changed coach does not also announce unrest it no longer has.
        let userClub = before |> Map.tryFind gs.UserClubId
        let userWasEmployed = userEmployed gs

        let (sackedAi, taken), gs, sackNews =
            gs.Clubs
            |> Map.toList
            |> List.filter (fun (id, club) ->
                id <> gs.UserClubId
                && club.BoardConfidence < config.SackThreshold
                && matchesInCharge date club gs >= config.MinMatchesBeforeSack)
            |> List.fold
                (fun ((clubs, taken), state, news) (id, _) ->
                    // Coaches hired or fired earlier in this loop are excluded so one man is not hired twice.
                    match GameState.headCoach id state with
                    | Some coach ->
                        match replaceCoach config date id coach taken state with
                        | Some(next, items, hired) ->
                            (Set.add id clubs, taken |> Set.add coach.Id |> Set.add hired.Id), next, news @ items
                        | None -> (clubs, taken), state, news
                    | None -> (clubs, taken), state, news)
                ((Set.empty, Set.empty), gs, [])

        // User: dismissal supersedes the ultimatum, which supersedes a same-day warning.
        let gs, userNews =
            match userClub, userWasEmployed with
            | Some previous, true ->
                let now = gs.Clubs[gs.UserClubId]

                // Confidence already below the ultimatum line before today's matches means the ultimatum was
                // delivered on an earlier matchday: the user is never fired without having been warned.
                // Boards decide after a match, never on a quiet Tuesday.
                let playedToday =
                    fresh |> List.exists (fun m -> MatchFixture.involves gs.UserClubId m.Fixture)

                if
                    playedToday
                    && previous.BoardConfidence < config.UltimatumThreshold
                    && now.BoardConfidence < config.SackThreshold
                    && matchesInCharge date now gs >= config.MinMatchesBeforeSack
                then
                    dismissUser config date taken gs
                elif crossedDown config.UltimatumThreshold previous.BoardConfidence now.BoardConfidence then
                    boardMessage
                        date
                        "Board ultimatum"
                        "Results must improve immediately; the board is considering a change of manager."
                        gs,
                    []
                elif crossedDown config.BoardWarningThreshold previous.BoardConfidence now.BoardConfidence then
                    boardMessage
                        date
                        "Board concern"
                        "The board is worried about recent results and expects a response."
                        gs,
                    []
                else
                    gs, []
            | _ -> gs, []

        let unrest =
            touched
            |> List.collect (fun id ->
                let was, now = before[id], gs.Clubs[id]
                let sackedHere = sackedAi.Contains id

                [ if
                      not sackedHere
                      && crossedDown config.BoardWarningThreshold was.BoardConfidence now.BoardConfidence
                  then
                      yield candidate gs (NewsKind.BoardUnrest(id, now.BoardConfidence)) NewsImportance.Notable id
                  let bandBefore, bandNow = fansBand config was.FansMood, fansBand config now.FansMood

                  if bandBefore <> bandNow then
                      yield candidate gs (NewsKind.FansMood(id, bandNow)) NewsImportance.Minor id ])

        gs, sackNews @ userNews @ unrest
