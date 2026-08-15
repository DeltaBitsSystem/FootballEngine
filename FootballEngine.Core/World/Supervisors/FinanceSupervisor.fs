namespace FootballEngine.World.Supervisors

open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Agents

module FinanceSupervisor =

    let private applyIntent (state: GameState) (intent: Intent<ClubFinanceIntent>) =
        match intent.Payload with
        | BypassAcceptOffer(NegotiationId negId) ->
            match state.PendingNegotiations |> Map.tryFind negId with
            | Some neg ->
                match state.Players |> Map.tryFind neg.PlayerId with
                | Some player ->
                    let newSalary = Player.playerSalary player.CurrentSkill

                    let fee =
                        match neg.Stage with
                        | OfferMade f
                        | CounterReceived(f, _)
                        | AwaitingPlayerResponse(f, _) -> f
                        | _ -> Player.playerValue player.CurrentSkill |> decimal

                    match state.Clubs |> Map.tryFind neg.BuyerClubId, state.Clubs |> Map.tryFind neg.SellerClubId with
                    | Some buyer, Some seller ->
                        Transfer.completeUserTransfer buyer seller player fee newSalary 3 state
                        |> fun gs ->
                            { gs with
                                PendingNegotiations = gs.PendingNegotiations |> Map.remove negId }
                    | _ -> state
                | None -> state
            | None -> state
        | ForwardOfferToManager(NegotiationId _) -> state
        | AssignSeasonBudget(clubId, tb, wb) ->
            state.Clubs
            |> Map.tryFind clubId
            |> Option.map (fun club ->
                { state with
                    Clubs =
                        state.Clubs
                        |> Map.add
                            clubId
                            { club with
                                Budget = club.Budget + tb + wb } })
            |> Option.defaultValue state

    let private detectBudgetConflicts (state: GameState) (intents: Intent<ClubFinanceIntent> list) =
        intents
        |> List.groupBy (fun i ->
            match i.AgentId with
            | ClubAgentId id -> id
            | _ -> -1)
        |> List.choose (fun (clubId, group) ->
            state.Clubs
            |> Map.tryFind clubId
            |> Option.bind (fun club ->
                let totalSpend =
                    group
                    |> List.sumBy (fun i ->
                        match i.Payload with
                        | AssignSeasonBudget(_, tb, _) -> tb
                        | _ -> 0m)

                if totalSpend > club.Budget then
                    Some(BudgetExceeded(clubId, totalSpend, group))
                else
                    None))

    let resolve (state: GameState) (intents: Intent<ClubFinanceIntent> list) : GameState =
        let conflicts = detectBudgetConflicts state intents

        let conflicted =
            conflicts
            |> List.collect (fun c ->
                match c with
                | BudgetExceeded(_, _, group) -> group |> List.sortByDescending (fun i -> i.Priority) |> List.tail
                | _ -> [])
            |> Set.ofList

        intents
        |> List.filter (fun i -> not (Set.contains i conflicted))
        |> List.fold applyIntent state
