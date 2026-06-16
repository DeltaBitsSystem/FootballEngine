namespace FootballEngine.Domain

open System

type OfferStatus =
    | Pending
    | AcceptedByClub
    | RejectedByClub
    | ContractOffered of salary: decimal * years: int
    | ContractRejectedByPlayer
    | Completed
    | Withdrawn

type TransferOffer =
    { Id: int
      PlayerId: PlayerId
      SellerClubId: ClubId
      Fee: decimal
      Salary: decimal
      Status: OfferStatus
      CreatedOnDate: DateTime }

type TransferRecord =
    { PlayerId: PlayerId
      PlayerName: string
      FromClubName: string
      ToClubName: string
      Fee: decimal
      Season: int }

module TransferNegotiation =

    let private minAcceptableMultiplier = 0.85m

    let private clubBudgetSafetyFactor = 1.2m

    let private reputationGapThreshold = 2000

    let private playerWillAccept (club: Club) (p: Player) (salary: decimal) =
        let isBetterClub = club.Reputation >= p.Reputation - 500

        let currentSalary =
            match p.Affiliation with
            | Contracted(_, c) -> c.Salary
            | _ -> 0m

        let isFairSalary = salary >= currentSalary * 0.9m
        isBetterClub && isFairSalary

    let clubResponse (buyer: Club) (seller: Club) (p: Player) (fee: decimal) : OfferStatus =
        let isEnoughFee = fee >= Player.playerValue p.CurrentSkill * minAcceptableMultiplier

        let buyerReputationOk =
            buyer.Reputation >= seller.Reputation - reputationGapThreshold

        if isEnoughFee && buyerReputationOk then
            AcceptedByClub
        else
            RejectedByClub

    let playerResponse (buyer: Club) (p: Player) (salary: decimal) : OfferStatus =
        if playerWillAccept buyer p salary then
            ContractOffered(salary, 3)
        else
            ContractRejectedByPlayer

    let suggestedFee (p: Player) = Player.playerValue p.CurrentSkill
    let suggestedSalary (p: Player) = Player.playerSalary p.CurrentSkill

    let canAfford (buyer: Club) (fee: decimal) (salary: decimal) =
        buyer.Budget >= fee * clubBudgetSafetyFactor && buyer.Budget >= salary * 12m

module Transfer =

    let transferPlayer
        (playerId: PlayerId)
        (fromClubId: ClubId)
        (toClubId: ClubId)
        (fee: decimal)
        (gs: GameState)
        : GameState =
        match gs.Clubs |> Map.tryFind toClubId, gs.Clubs |> Map.tryFind fromClubId with
        | Some buyer, Some seller ->
            let updatedBuyer = buyer |> Club.adjustBudget -fee |> Club.addPlayer playerId

            let updatedSeller = seller |> Club.adjustBudget fee |> Club.removePlayer playerId

            { gs with
                Clubs = gs.Clubs |> Map.add toClubId updatedBuyer |> Map.add fromClubId updatedSeller }
        | _ -> gs

    let completeUserTransfer
        (buyer: Club)
        (seller: Club)
        (player: Player)
        (fee: decimal)
        (salary: decimal)
        (years: int)
        (gs: GameState)
        : GameState =
        let updatedPlayer =
            { player with
                Affiliation =
                    Contracted(
                        buyer.Id,
                        { Salary = salary
                          ExpiryYear = gs.Season + years }
                    )
                Morale = min 100 (player.Morale + 10) }

        let gs2 = transferPlayer player.Id seller.Id buyer.Id fee gs

        { gs2 with
            Players = gs2.Players |> Map.add player.Id updatedPlayer }
