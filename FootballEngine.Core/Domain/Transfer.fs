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

        // A player who asked to leave is on the market for anyone: the seller no longer shields him from smaller clubs.
        let buyerReputationOk =
            p.TransferListed
            || buyer.Reputation >= seller.Reputation - reputationGapThreshold

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
        Finance.availableForTransfers buyer.Finance >= fee * clubBudgetSafetyFactor
        && buyer.Finance.WageBudget - buyer.Finance.WageCommitment >= salary * 12m

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
            let updatedBuyer = buyer |> Club.addPlayer playerId
            let updatedSeller = seller |> Club.removePlayer playerId

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

        let reservationId = $"transfer:{player.Id}:{buyer.Id}:{fee}"

        let obligation =
            { Id = reservationId
              Counterparty = seller.Name
              Description = "Transfer fee"
              DueDate = gs.CurrentDate
              Amount = fee
              Currency = EUR
              Condition = "Confirmed transfer"
              State = ObligationCommitted
              AccruedAmount = 0m
              PaidAmount = 0m }

        let buyerFinance =
            buyer.Finance
            |> Finance.releaseReservation reservationId
            |> Finance.commitObligation
                gs.CurrentDate
                ("transfer-commit:" + reservationId)
                "Confirmed transfer"
                obligation
            |> Result.bind (
                Finance.settleObligation
                    gs.CurrentDate
                    ("transfer-payment:" + reservationId)
                    "Transfer completed"
                    reservationId
            )

        let sellerFinance =
            Finance.recordRevenue
                gs.CurrentDate
                ("transfer-receipt:" + reservationId)
                "Transfer completed"
                "Transfer fee"
                fee
                (Some buyer.Name)
                seller.Finance

        let gs2 =
            match buyerFinance, sellerFinance with
            | Ok bf, Ok sf ->
                let updatedBuyer = { buyer with Finance = bf } |> Club.addPlayer player.Id
                let updatedSeller = { seller with Finance = sf } |> Club.removePlayer player.Id

                { gs with
                    Clubs = gs.Clubs |> Map.add buyer.Id updatedBuyer |> Map.add seller.Id updatedSeller
                    PulseQueues =
                        PulseQueues.recordTransfer
                            { Player = player.Id
                              From = Some seller.Id
                              To = buyer.Id
                              Fee = fee }
                            gs.PulseQueues }
            | _ -> gs

        { gs2 with
            Players = gs2.Players |> Map.add player.Id updatedPlayer }
