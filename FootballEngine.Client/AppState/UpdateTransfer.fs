namespace FootballEngine

open System.Threading.Tasks
open Elmish
open FootballEngine.Domain
open FootballEngine.Domain.TransferNegotiation
open FootballEngine.Icons
open AppTypes
open AppMsgs

module UpdateTransfer =

    let private buildClubNameCache (gs: GameState) : Map<PlayerId, string> =
        gs.Players
        |> Map.toSeq
        |> Seq.choose (fun (_, p) ->
            Player.clubOf p
            |> Option.bind (fun clubId -> gs.Clubs |> Map.tryFind clubId |> Option.map (fun c -> p.Id, c.Name)))
        |> Map.ofSeq

    let private isPositionInFilter (filter: TransferFilter) (pos: Position) =
        match filter, pos with
        | AllPositions, _ -> true
        | Goalkeepers, GK -> true
        | Defenders, (DC | DL | DR | WBR | WBL) -> true
        | Midfielders, (DM | MC | ML | MR | AMC | AMR | AML) -> true
        | Attackers, ST -> true
        | _ -> false

    let private matchesFilter (filter: TransferFilter) (p: Player) = isPositionInFilter filter p.Position

    let private applySort (sortBy: SortField) (asc: bool) (players: Player list) =
        match sortBy, asc with
        | ByName, true -> players |> List.sortBy _.Name
        | ByName, false -> players |> List.sortByDescending _.Name
        | ByCA, true -> players |> List.sortBy _.CurrentSkill
        | ByCA, false -> players |> List.sortByDescending _.CurrentSkill
        | ByValue, true -> players |> List.sortBy (fun p -> Player.playerValue p.CurrentSkill)
        | ByValue, false -> players |> List.sortByDescending (fun p -> Player.playerValue p.CurrentSkill)
        | ByAge, true -> players |> List.sortByDescending _.Birthday
        | ByAge, false -> players |> List.sortBy _.Birthday
        | ByPosition, true -> players |> List.sortBy _.Position
        | ByPosition, false -> players |> List.sortByDescending _.Position

    let private applyFilters (t: TransferState) =
        let query = t.SearchQuery.Trim().ToLowerInvariant()

        t.CachedPlayers
        |> List.filter (fun p ->
            matchesFilter t.PositionFilter p
            && (query = "" || p.Name.ToLowerInvariant().Contains(query)))
        |> applySort t.SortBy t.SortAsc

    let private withFilteredPlayers (t: TransferState) =
        { t with
            FilteredPlayers = applyFilters t
            Page = 0 }

    let private buildCacheAsync (gs: GameState) =
        Task.Run(fun () ->
            let clubNameCache = buildClubNameCache gs

            let players =
                gs.Players
                |> Map.toList
                |> List.map snd
                |> List.filter (fun p -> Player.clubOf p |> Option.forall (fun clubId -> clubId <> gs.UserClubId))

            players, clubNameCache)

    let private recordTransfer
        (p: Player)
        (buyer: Club)
        (seller: Club)
        (fee: decimal)
        (season: int)
        (t: TransferState)
        =
        { t with
            TransferHistory =
                { PlayerId = p.Id
                  PlayerName = p.Name
                  FromClubName = seller.Name
                  ToClubName = buyer.Name
                  Fee = fee
                  Season = season }
                :: t.TransferHistory }

    let private pushNotification = AppTypes.pushNotification

    let private completeTransferAndSave
        (p: Player)
        (buyer: Club)
        (seller: Club)
        (fee: decimal)
        (negId: int)
        (gs: GameState)
        (state: State)
        : State * Cmd<Msg> =
        let t = state.Transfer

        let updatedTransfer =
            { t with
                ActiveNegotiationId = None
                PendingOffer = None
                CachedPlayers = t.CachedPlayers |> List.filter (fun x -> x.Id <> p.Id)
                FilteredPlayers = t.FilteredPlayers |> List.filter (fun x -> x.Id <> p.Id) }
            |> recordTransfer p buyer seller fee gs.Season

        { state with
            Mode = InGame(gs, managerEmployment gs)
            Transfer = updatedTransfer }
        |> pushNotification
            NotificationIcons.win
            "Transfer Complete"
            $"{p.Name} signed for {buyer.Name} — fee: ${fee:N0}",
        SimHelpers.saveCmd gs state.WorldClock

    let handle (msg: TransferMsg) (state: State) : State * Cmd<Msg> =
        let t = state.Transfer

        let withGame (f: GameState -> State * Cmd<Msg>) =
            match state.Mode with
            | InGame(gs, _) -> f gs
            | _ -> state, Cmd.none

        match msg with
        | Load ->
            withGame (fun gs ->
                { state with
                    State.Transfer.IsLoading = true },
                Cmd.OfTask.perform (fun () -> buildCacheAsync gs) () (fun (players, cache) ->
                    TransferMsg(Loaded(players, cache))))

        | Loaded(players, cache) ->
            let updated =
                { t with
                    CachedPlayers = players
                    ClubNameCache = cache
                    IsLoading = false }
                |> withFilteredPlayers

            { state with Transfer = updated }, Cmd.none

        | TabChange tab ->
            { state with
                Transfer = { t with ActiveTab = tab; Page = 0 } },
            Cmd.none

        | Search query ->
            let delayed () =
                task {
                    do! Task.Delay 150
                    return query
                }

            { state with
                Transfer = { t with SearchQuery = query } },
            Cmd.OfTask.perform delayed () (fun q -> TransferMsg(ApplySearch q))

        | ApplySearch query ->
            if query <> t.SearchQuery then
                state, Cmd.none
            else
                { state with
                    Transfer =
                        { t with
                            FilteredPlayers = applyFilters t
                            Page = 0 } },
                Cmd.none

        | FilterChange f ->
            { state with
                Transfer = { t with PositionFilter = f } |> withFilteredPlayers },
            Cmd.none

        | SortChange s ->
            let asc = if t.SortBy = s then not t.SortAsc else false

            { state with
                Transfer = { t with SortBy = s; SortAsc = asc } |> withFilteredPlayers },
            Cmd.none

        | PlayerSelect pid ->
            { state with
                State.Transfer.SelectedPlayerId = Some pid },
            Cmd.none

        | WatchToggle pid ->
            let updated =
                if List.contains pid t.WatchlistIds then
                    t.WatchlistIds |> List.filter ((<>) pid)
                else
                    pid :: t.WatchlistIds

            { state with
                State.Transfer.WatchlistIds = updated },
            Cmd.none

        | PageChange p -> { state with State.Transfer.Page = p }, Cmd.none

        | MakeOffer(playerId, fee, salary) ->
            { state with
                Transfer =
                    { t with
                        PendingOffer =
                            Some
                                { PlayerId = playerId
                                  Fee = fee
                                  Salary = salary }
                        ActiveTab = MarketSearch } },
            Cmd.none

        | SubmitOffer ->
            withGame (fun gs ->
                match t.PendingOffer with
                | None -> state, Cmd.none
                | Some draft ->
                    match gs.Players |> Map.tryFind draft.PlayerId with
                    | None -> state, Cmd.none
                    | Some p ->
                        let buyer = gs.Clubs[gs.UserClubId]

                        match Player.clubOf p |> Option.bind (fun id -> gs.Clubs |> Map.tryFind id) with
                        | None -> state, Cmd.none
                        | Some seller ->
                            if not (canAfford buyer draft.Fee (suggestedSalary p)) then
                                { state with
                                    State.Transfer.PendingOffer = Some { draft with Fee = draft.Fee } },
                                Cmd.none
                            else
                                let neg =
                                    { Id = gs.NextNegotiationId
                                      BuyerClubId = buyer.Id
                                      SellerClubId = seller.Id
                                      PlayerId = p.Id
                                      Stage = NegotiationStage.OfferMade draft.Fee
                                      Deadline = gs.CurrentDate.AddDays(30.0) }

                                let newGs =
                                    { gs with
                                        PendingNegotiations = gs.PendingNegotiations |> Map.add neg.Id neg
                                        NextNegotiationId = gs.NextNegotiationId + 1 }

                                { state with
                                    Mode = InGame(newGs, managerEmployment newGs)
                                    Transfer =
                                        { t with
                                            PendingOffer = None
                                            ActiveNegotiationId = Some neg.Id } },
                                SimHelpers.saveCmd newGs state.WorldClock)

        | WithdrawOffer negId ->
            withGame (fun gs ->
                match gs.PendingNegotiations |> Map.tryFind negId with
                | Some neg ->
                    let updatedNeg =
                        { neg with
                            Stage = NegotiationStage.Collapsed "withdrawn" }

                    let newGs =
                        { gs with
                            PendingNegotiations = gs.PendingNegotiations |> Map.add negId updatedNeg }

                    { state with
                        Mode = InGame(newGs, managerEmployment newGs)
                        Transfer = { t with ActiveNegotiationId = None } },
                    SimHelpers.saveCmd newGs state.WorldClock
                | None -> state, Cmd.none)

        | ClearNegotiation negotiationId ->
            withGame (fun gs ->
                match gs.PendingNegotiations |> Map.tryFind negotiationId with
                | Some neg ->
                    match neg.Stage with
                    | NegotiationStage.RejectedByClub _
                    | NegotiationStage.RejectedByPlayer _
                    | NegotiationStage.Collapsed _ ->
                        let newGs =
                            { gs with
                                PendingNegotiations = gs.PendingNegotiations |> Map.remove neg.Id }

                        { state with
                            Mode = InGame(newGs, managerEmployment newGs)
                            Transfer = { t with ActiveNegotiationId = None } },
                        SimHelpers.saveCmd newGs state.WorldClock
                    | _ ->
                        { state with
                            Transfer = { t with ActiveNegotiationId = None } },
                        Cmd.none
                | None ->
                    { state with
                        Transfer = { t with ActiveNegotiationId = None } },
                    Cmd.none)

        | CounterOffer(negId, newFee, newSalary) ->
            withGame (fun gs ->
                match gs.PendingNegotiations |> Map.tryFind negId with
                | Some neg ->
                    let updatedNeg =
                        match neg.Stage with
                        | NegotiationStage.RejectedByPlayer(_, _, _) ->
                            match newSalary with
                            | Some salary ->
                                { neg with
                                    Stage = NegotiationStage.AwaitingPlayerResponse(newFee, salary) }
                            | None ->
                                { neg with
                                    Stage = NegotiationStage.OfferMade newFee }
                        | NegotiationStage.RejectedByClub _
                        | NegotiationStage.CounterReceived _ ->
                            { neg with
                                Stage = NegotiationStage.OfferMade newFee }
                        | NegotiationStage.AwaitingPlayerResponse(fee, _) ->
                            match newSalary with
                            | Some salary ->
                                { neg with
                                    Stage = NegotiationStage.AwaitingPlayerResponse(fee, salary) }
                            | None ->
                                { neg with
                                    Stage = NegotiationStage.OfferMade newFee }
                        | _ ->
                            { neg with
                                Stage = NegotiationStage.OfferMade newFee }

                    let newGs =
                        { gs with
                            PendingNegotiations = gs.PendingNegotiations |> Map.add negId updatedNeg }

                    { state with
                        Mode = InGame(newGs, managerEmployment newGs) },
                    SimHelpers.saveCmd newGs state.WorldClock
                | None -> state, Cmd.none)
