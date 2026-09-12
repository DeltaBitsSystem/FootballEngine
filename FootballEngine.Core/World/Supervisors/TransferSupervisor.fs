namespace FootballEngine.World.Supervisors

open FootballEngine.Domain
open FootballEngine.Domain.TransferNegotiation
open FootballEngine.World
open FootballEngine.World.Agents

module TransferSupervisor =

    let startLoan (loan: LoanAgreement) (state: GameState) =
        TransferMarket.startLoanInGame loan state

    let endLoan (now: System.DateTime) (playerId: PlayerId) (state: GameState) =
        TransferMarket.endLoanInGame now playerId state

    let private advanceNegotiation (state: GameState) (neg: Negotiation) : GameState * Negotiation option =
        match neg.Stage with
        | Approaching ->
            match
                state.Clubs |> Map.tryFind neg.BuyerClubId,
                state.Clubs |> Map.tryFind neg.SellerClubId,
                state.Players |> Map.tryFind neg.PlayerId
            with
            | Some buyer, Some seller, Some player ->
                let fee = Player.playerValue player.CurrentSkill

                match clubResponse buyer seller player fee with
                | AcceptedByClub ->
                    state,
                    Some
                        { neg with
                            Stage = NegotiationStage.OfferMade fee }
                | RejectedByClub ->
                    state,
                    Some
                        { neg with
                            Stage = NegotiationStage.RejectedByClub(fee, "fee below market value") }
                | _ -> state, Some neg
            | _ -> state, Some neg

        | OfferMade fee ->
            match
                state.Clubs |> Map.tryFind neg.BuyerClubId,
                state.Clubs |> Map.tryFind neg.SellerClubId,
                state.Players |> Map.tryFind neg.PlayerId
            with
            | Some buyer, Some seller, Some player ->
                match clubResponse buyer seller player fee with
                | AcceptedByClub ->
                    let salary = suggestedSalary player

                    state,
                    Some
                        { neg with
                            Stage = NegotiationStage.AwaitingPlayerResponse(fee, salary) }
                | RejectedByClub ->
                    state,
                    Some
                        { neg with
                            Stage = NegotiationStage.RejectedByClub(fee, "fee insufficient") }
                | _ -> state, Some neg
            | _ -> state, Some neg

        | AwaitingPlayerResponse(fee, salary) ->
            match state.Clubs |> Map.tryFind neg.BuyerClubId, state.Players |> Map.tryFind neg.PlayerId with
            | Some buyer, Some player ->
                match playerResponse buyer player salary with
                | ContractOffered(salary', years) ->
                    state,
                    Some
                        { neg with
                            Stage = NegotiationStage.Agreed(fee, salary', years) }
                | ContractRejectedByPlayer ->
                    state,
                    Some
                        { neg with
                            Stage = NegotiationStage.RejectedByPlayer(fee, salary, "salary insufficient") }
                | _ -> state, Some neg
            | _ -> state, Some neg

        | Agreed(fee, salary, years) ->
            match
                state.Clubs |> Map.tryFind neg.BuyerClubId,
                state.Clubs |> Map.tryFind neg.SellerClubId,
                state.Players |> Map.tryFind neg.PlayerId
            with
            | Some buyer, Some seller, Some player ->
                let gs' = Transfer.completeUserTransfer buyer seller player fee salary years state
                gs', None
            | _ -> state, Some neg

        | NegotiationStage.RejectedByClub _
        | NegotiationStage.RejectedByPlayer _
        | CounterReceived _
        | Collapsed _ -> state, Some neg

    let private detectConflicts (intents: Intent<ManagerIntent> list) =
        intents
        |> List.groupBy (fun i ->
            match i.Payload with
            | InitiateTransfer(_, pid, _) -> Some pid
            | _ -> None)
        |> List.choose (fun (pidOpt, group) ->
            match pidOpt with
            | Some _ when group.Length > 1 -> Some(SameTarget(group[0], group[1]))
            | _ -> None)

    let private winnerOf (a: Intent<ManagerIntent>) (b: Intent<ManagerIntent>) =
        let feeOf i =
            match i.Payload with
            | InitiateTransfer(_, _, fee) -> fee
            | _ -> 0m

        if feeOf a >= feeOf b then
            a, (b, "outbid")
        else
            b, (a, "outbid")

    let private applyIntent (state: GameState) (intent: Intent<ManagerIntent>) =
        match intent.Payload with
        | InitiateTransfer(buyerId, pid, _) ->
            match state.Clubs |> Map.tryFind buyerId, state.Players |> Map.tryFind pid with
            | Some _, Some player ->
                match Player.clubOf player with
                | Some sellerId when sellerId <> buyerId ->
                    let neg =
                        { Id = state.NextNegotiationId
                          BuyerClubId = buyerId
                          SellerClubId = sellerId
                          PlayerId = pid
                          Stage = Approaching
                          Deadline = state.CurrentDate.AddDays(30.0) }

                    { state with
                        PendingNegotiations = state.PendingNegotiations |> Map.add neg.Id neg
                        NextNegotiationId = state.NextNegotiationId + 1 }
                | _ -> state
            | _ -> state
        | _ -> state

    let resolve (state: GameState) (intents: Intent<ManagerIntent> list) : GameState =
        let conflicts = detectConflicts intents

        let rejected =
            conflicts
            |> List.choose (fun c ->
                match c with
                | SameTarget(a, b) -> Some(winnerOf a b |> snd |> fst)
                | _ -> None)
            |> Set.ofList

        let filteredIntents =
            intents
            |> List.filter (fun i -> not (Set.contains i rejected))
            |> List.fold applyIntent state

        filteredIntents.PendingNegotiations
        |> Map.toList
        |> List.map snd
        |> List.fold
            (fun gs neg ->
                match advanceNegotiation gs neg with
                | gs', Some updated ->
                    { gs' with
                        PendingNegotiations = gs'.PendingNegotiations |> Map.add neg.Id updated }
                | gs', None ->
                    { gs' with
                        PendingNegotiations = gs'.PendingNegotiations |> Map.remove neg.Id })
            filteredIntents
