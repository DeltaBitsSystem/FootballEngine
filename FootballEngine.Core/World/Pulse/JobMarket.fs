namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain
open FootballEngine.Domain.LineupOps

/// Offers for the unemployed user. They come from boards that are already losing patience with their own coach,
/// so every offer has a cause in the world: a club in crisis whose standing the user can reach.
module JobMarket =

    let private isActive (date: DateTime) (offer: JobOffer) = offer.Expires.Date >= date.Date

    let activeOffers (date: DateTime) (gs: GameState) : JobOffer list =
        gs.JobOffers |> List.filter (isActive date)

    let private leagueClubs (gs: GameState) : Set<ClubId> =
        gs.Competitions.Values
        |> Seq.collect (fun c ->
            match c.Type with
            | NationalLeague _ -> c.ClubIds
            | _ -> [])
        |> Set.ofSeq

    /// A board that sacked the user this past year will not take him back; afterwards the slate is clean.
    let private recentlyDismissedBy (date: DateTime) (gs: GameState) : Set<ClubId> =
        gs.CareerHistory
        |> List.choose (function
            | ManagerCareerEvent.ManagerDismissed(on, club, _) when on > date.AddYears -1 -> Some club
            | _ -> None)
        |> Set.ofList

    let private salaryAt (clubId: ClubId) (gs: GameState) : decimal =
        GameState.headCoach clubId gs
        |> Option.bind _.Contract
        |> Option.map _.Salary
        |> Option.defaultValue 50_000m

    /// Ordered best-first. Crisis boards come first; with none, the reachable clubs whose boards are least happy;
    /// with none reachable at all, the most modest clubs, so an unemployed career is never a dead end.
    let private candidates (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : Club list =
        let standing =
            GameState.userManager gs |> Option.map _.Reputation |> Option.defaultValue 0

        let excluded =
            recentlyDismissedBy date gs
            |> Set.union (gs.JobOffers |> List.map _.ClubId |> Set.ofList)

        let open' =
            leagueClubs gs
            |> Seq.choose gs.Clubs.TryFind
            |> Seq.filter (fun club -> not (excluded.Contains club.Id) && (GameState.headCoach club.Id gs).IsSome)
            |> List.ofSeq

        let reachable =
            open'
            |> List.filter (fun club -> club.Reputation <= standing + config.JobReputationReach)
            |> List.sortBy (fun club -> club.BoardConfidence, -club.Reputation, club.Id)

        let crisis =
            reachable
            |> List.filter (fun club -> club.BoardConfidence < config.BoardWarningThreshold)

        if not crisis.IsEmpty then
            crisis
        elif not reachable.IsEmpty then
            reachable
        else
            open' |> List.sortBy (fun club -> club.Reputation, club.Id)

    let private offerMessage (date: DateTime) (club: Club) (gs: GameState) =
        GameState.addInboxMessage
            (Inbox.create
                date
                club.Name
                $"Job offer: {club.Name}"
                $"The board of {club.Name} wants you as head coach. The offer is open in Career."
                InboxMessageCategory.BoardUpdate
                false)
            gs

    /// Daily. Expired offers drop; new ones arrive weekly, or at once whenever the user has none to consider.
    let refresh (config: WorldPulseConfig) (weekly: bool) (date: DateTime) (gs: GameState) : GameState =
        match GameState.managedClub gs with
        | Some _ when gs.JobOffers.IsEmpty -> gs
        | Some _ -> { gs with JobOffers = [] }
        | None ->
            let alive =
                activeOffers date gs
                |> List.filter (fun offer -> (GameState.headCoach offer.ClubId gs).IsSome)

            let gs = { gs with JobOffers = alive }

            if not (weekly || alive.IsEmpty) then
                gs
            else
                let room = min config.JobOffersPerWeek (config.MaxJobOffers - alive.Length)

                candidates config date gs
                |> List.truncate (max 0 room)
                |> List.fold
                    (fun (state: GameState) club ->
                        let offer =
                            { Id = state.NextJobOfferId
                              ClubId = club.Id
                              Offered = date
                              Expires = date.AddDays(float config.JobOfferDays)
                              Salary = salaryAt club.Id state
                              ContractYears = config.JobContractYears }

                        { state with
                            JobOffers = state.JobOffers @ [ offer ]
                            NextJobOfferId = state.NextJobOfferId + 1 }
                        |> offerMessage date club)
                    gs

    let decline (offerId: int) (gs: GameState) : GameState =
        { gs with
            JobOffers = gs.JobOffers |> List.filter (fun offer -> offer.Id <> offerId) }

    /// The club's head coaches leave so the user is the only one; his lineup is rebuilt for the new squad.
    let accept (config: WorldPulseConfig) (date: DateTime) (offerId: int) (gs: GameState) : Result<GameState, string> =
        let offer = gs.JobOffers |> List.tryFind (fun o -> o.Id = offerId)

        match GameState.managedClub gs, offer, GameState.userManager gs with
        | Some _, _, _ -> Error "The manager already has a club"
        | None, None, _ -> Error "The job offer does not exist"
        | None, Some offer, _ when not (isActive date offer) -> Error "The job offer has expired"
        | None, _, None -> Error "The user manager does not exist"
        | None, Some offer, Some manager ->
            match Map.tryFind offer.ClubId gs.Clubs with
            | None -> Error "The club does not exist"
            | Some club ->
                let incumbents =
                    GameState.getStaff club.Id gs |> List.filter (fun s -> s.Role = HeadCoach)

                let matches = Pressure.matchesInCharge date club gs

                let cleared =
                    incumbents
                    |> List.fold
                        (fun (state: GameState) coach ->
                            GameState.updateStaff
                                { coach with
                                    Contract = None
                                    Status = StaffStatus.Unemployed }
                                state)
                        gs

                let unemployedManager =
                    { manager with
                        Contract = None
                        Status = StaffStatus.Unemployed }

                let cleared = GameState.updateStaff unemployedManager cleared

                Career.hire date manager.Id club.Id offer.Salary (date.Year + offer.ContractYears) cleared
                |> Result.map (fun hired ->
                    let squad = club.PlayerIds |> List.choose hired.Players.TryFind

                    let formation =
                        manager.Attributes.Coaching.PreferredFormation
                        |> Option.defaultValue (bestFormation squad)

                    let coach =
                        autoLineup
                            { hired.Staff[manager.Id] with
                                Role = HeadCoach }
                            squad
                            formation

                    let incumbentIds = incumbents |> List.map _.Id |> Set.ofList

                    let club =
                        { club with
                            ManagerSince = Some date
                            BoardConfidence = config.HoneymoonConfidence
                            StaffIds = (club.StaffIds |> List.filter (incumbentIds.Contains >> not)) @ [ manager.Id ] }

                    let state =
                        { hired with
                            Staff = hired.Staff |> Map.add coach.Id coach
                            Clubs = hired.Clubs |> Map.add club.Id club
                            UserClubId = club.Id
                            JobOffers = [] }

                    let scope = NewsDesk.classifyScope state [ club.Id ]

                    let news: NewsCandidate list =
                        [ for incumbent in incumbents |> List.truncate 1 do
                              yield
                                  NewsKind.ManagerSacked(club.Id, incumbent.Id, matches),
                                  NewsImportance.Notable,
                                  scope,
                                  [ club.Id ],
                                  []
                          yield
                              NewsKind.ManagerAppointed(club.Id, manager.Id),
                              NewsImportance.Headline,
                              scope,
                              [ club.Id ],
                              [] ]

                    NewsDesk.publish config date news state)
