namespace FootballEngine.World.Agents

open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.Stats
open FootballEngine.World

type ClubFinanceIntent =
    | BypassAcceptOffer of NegotiationId
    | ForwardOfferToManager of NegotiationId
    | AssignSeasonBudget of ClubId * transferBudget: decimal * wageBudget: decimal

type ClubYouthIntent = GenerateYouthPlayer of ClubId

module ClubAgent =

    let private project (clubId: ClubId) (state: GameState) : AgentView =
        let club = state.Clubs[clubId]
        let budget = Finance.availableForTransfers club.Finance

        let squad =
            state.Players
            |> Map.toList
            |> List.map snd
            |> List.filter (fun p -> Player.clubOf p = Some clubId)

        let incomingNegotiations =
            state.PendingNegotiations
            |> Map.toList
            |> List.map snd
            |> List.filter (fun n -> n.SellerClubId = clubId)
            |> List.choose (fun n -> squad |> List.tryFind (fun p -> p.Id = n.PlayerId) |> Option.map (fun p -> n, p))

        ClubView(club, budget, squad, incomingNegotiations)

    let private decideFinance (view: AgentView) : Intent<ClubFinanceIntent> list =
        match view with
        | ClubView(club, _budget, _squad, incomingNegotiations) ->
            incomingNegotiations
            |> List.map (fun (neg, player) ->
                let marketValue = Player.playerValue player.CurrentSkill |> decimal

                let fee =
                    match neg.Stage with
                    | OfferMade fee
                    | CounterReceived(fee, _)
                    | AwaitingPlayerResponse(fee, _)
                    | Agreed(fee, _, _) -> fee
                    | _ -> 0m

                let payload =
                    if fee >= marketValue * 2.0m then
                        BypassAcceptOffer(NegotiationId neg.Id)
                    else
                        ForwardOfferToManager(NegotiationId neg.Id)

                { AgentId = ClubAgentId club.Id
                  Priority = 1
                  Payload = payload })
        | _ -> []

    let makeFinance (clubId: ClubId) : Agent<ClubFinanceIntent> =
        { Id = ClubAgentId clubId
          Trigger = Both(Seasonal, OfferExceedsThreshold(0, 0m))
          Project = project clubId
          Decide = decideFinance }

    let private decideYouth (view: AgentView) : Intent<ClubYouthIntent> list =
        match view with
        | ClubView(club, _budget, _squad, _) ->
            [ { AgentId = ClubAgentId club.Id
                Priority = 1
                Payload = GenerateYouthPlayer club.Id } ]
        | _ -> []

    let makeYouth (clubId: ClubId) : Agent<ClubYouthIntent> =
        { Id = ClubAgentId clubId
          Trigger = OnSchedule Seasonal
          Project = project clubId
          Decide = decideYouth }

module YouthGen =

    let private youthPositions = [| GK; DC; MC; ST; AML; AMR |]

    let generateOne (clubId: ClubId) (state: GameState) : GameState =
        match state.Clubs |> Map.tryFind clubId with
        | None -> state
        | Some club ->
            let countryDataOpt = state.Countries |> Map.tryFind club.Nationality

            match countryDataOpt with
            | None -> state
            | Some countryData ->
                let nextId =
                    state.Players
                    |> Map.toList
                    |> List.map fst
                    |> fun ids -> if ids.IsEmpty then 1 else List.max ids + 1

                // A youth cohort must not depend on a process-global RNG after loading a career.
                let seed =
                    (uint64 (uint32 state.Season) <<< 32)
                    ^^^ (uint64 (uint32 clubId) <<< 16)
                    ^^^ uint64 (uint32 nextId)

                let raw =
                    FootballEngine.RNG.withSeed seed (fun () ->
                        let roll = uniformSample 0.0 (float youthPositions.Length)
                        let pos = youthPositions[int roll]
                        PlayerGen.createYouthProspect nextId pos clubId countryData state.Season)

                let youth =
                    { raw with
                        CurrentSkill = clamp 30 70 raw.CurrentSkill }

                { state with
                    Players = state.Players |> Map.add youth.Id youth
                    Clubs = state.Clubs |> Map.add clubId (Club.addPlayer youth.Id state.Clubs[clubId]) }
