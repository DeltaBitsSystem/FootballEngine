namespace FootballEngine.World.Agents

open FootballEngine.Domain
open FootballEngine.World

type PlayerIntent =
    | AcceptOffer of NegotiationId * PlayerId
    | RejectOffer of NegotiationId * PlayerId * reason: string

module PlayerAgent =

    let private projectSalaryOf (neg: Negotiation) (player: Player) =
        match neg.Stage with
        | AwaitingPlayerResponse(_, salary)
        | Agreed(_, salary, _) -> salary
        | _ -> Player.playerSalary player.CurrentSkill

    let private project (playerId: PlayerId) (state: GameState) : AgentView =
        let player = state.Players[playerId]

        let offers =
            state.PendingNegotiations
            |> Map.toList
            |> List.map snd
            |> List.filter (fun n -> n.PlayerId = playerId)
            |> List.map (fun n ->
                { Id = n.Id
                  PlayerId = n.PlayerId
                  SellerClubId = n.SellerClubId
                  Fee =
                    match n.Stage with
                    | OfferMade fee -> fee
                    | CounterReceived(fee, _) -> fee
                    | AwaitingPlayerResponse(fee, _) -> fee
                    | Agreed(fee, _, _) -> fee
                    | _ -> 0m
                  Salary = projectSalaryOf n player
                  Status = OfferStatus.Pending
                  CreatedOnDate = n.Deadline })

        PlayerView(player, offers)

    let private decide (view: AgentView) : Intent<PlayerIntent> list =
        match view with
        | PlayerView(player, offers) ->
            let currentSalary =
                player
                |> Player.contractOf
                |> Option.map (fun c -> c.Salary)
                |> Option.defaultValue 0m

            offers
            |> List.map (fun offer ->
                let payload =
                    if offer.Salary >= currentSalary * 1.1m then
                        AcceptOffer(NegotiationId offer.Id, player.Id)
                    else
                        RejectOffer(NegotiationId offer.Id, player.Id, "salary insufficient")

                { AgentId = PlayerAgentId player.Id
                  Priority = 1
                  Payload = payload })
        | _ -> []

    let make (playerId: PlayerId) : Agent<PlayerIntent> =
        { Id = PlayerAgentId playerId
          Trigger = OnEvent(OfferReceived(playerId, 0))
          Project = project playerId
          Decide = decide }
