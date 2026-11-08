namespace FootballEngine.Domain

open System

type TransferMarketConfig =
    { Window: TransferWindow
      RegistrationCloses: DateTime
      AllowPreContracts: bool }

module TransferMarket =

    let private fail reason = Error reason

    let private operationReservationId (id: TransferOperationId) = $"transfer:{id}"

    let private playerCommitted (state: MarketState) player =
        state.Operations
        |> Map.exists (fun _ op ->
            op.Player = player
            && (op.Status = TransferOperationStatus.Agreed
                || op.Status = TransferOperationStatus.PendingRegistration
                || op.Status = TransferOperationStatus.Completed))

    let private playerAccepts (player: Player) (role: ContractRole) (minutes: int) =
        // Minutes are a promise, so a player can reject a financially valid offer.
        match role with
        | Starter -> minutes >= 900
        | Rotation -> minutes >= 450
        | Backup -> minutes >= 180
        | Prospect -> minutes >= 90

    let private nextOperation (state: MarketState) =
        TransferOperationId state.NextOperationId,
        { state with
            NextOperationId = state.NextOperationId + 1 }

    let submit
        (cfg: TransferMarketConfig)
        (now: DateTime)
        (buyer: Club)
        (seller: Club option)
        (player: Player)
        (fee: decimal)
        (salary: decimal)
        (years: int)
        (role: ContractRole)
        (minutes: int)
        (state: MarketState)
        : Result<MarketState, TransferReason> =
        if not (TransferWindow.isOpen now cfg.Window) && not cfg.AllowPreContracts then
            fail TransferWindowClosed
        elif fee < 0m || salary <= 0m || years <= 0 || minutes < 0 then
            fail (InvalidOperation "Non-positive transfer terms")
        elif playerCommitted state player.Id then
            fail PlayerAlreadyCommitted
        elif not (playerAccepts player role minutes) then
            fail PlayerRejectedMinutes
        elif Finance.availableForTransfers buyer.Finance < fee then
            fail InsufficientFunds
        elif buyer.Finance.WageBudget - buyer.Finance.WageCommitment < salary * 12m then
            fail WageCapacityExceeded
        else
            let id, next = nextOperation state

            let operation =
                { Id = id
                  Player = player.Id
                  Buyer = buyer.Id
                  Seller = seller |> Option.map _.Id
                  Fee = fee
                  Salary = salary
                  ContractYears = years
                  Role = role
                  MinutesPromised = minutes
                  Status = TransferOperationStatus.Submitted
                  Reason = None
                  Created = now
                  Updated = now }

            Ok
                { next with
                    Operations = next.Operations.Add(id, operation) }

    /// Submit an offer and reserve its fee in the aggregate.  The older
    /// `submit` function remains a pure market transition for callers that
    /// only have a Club projection; game code should use this boundary.
    let submitInGame
        (cfg: TransferMarketConfig)
        (now: DateTime)
        (buyer: Club)
        (seller: Club option)
        (player: Player)
        (fee: decimal)
        (salary: decimal)
        (years: int)
        (role: ContractRole)
        (minutes: int)
        (game: GameState)
        : Result<GameState, TransferReason> =
        match submit cfg now buyer seller player fee salary years role minutes game.Market with
        | Error reason -> Error reason
        | Ok market ->
            let operationId = TransferOperationId(market.NextOperationId - 1)

            match
                Finance.reserveTransfer now (operationReservationId operationId) "transfer offer" fee buyer.Finance
            with
            | Error _ -> Error InsufficientFunds
            | Ok finance ->
                let updatedBuyer = { buyer with Finance = finance }

                Ok
                    { game with
                        Market = market
                        Clubs = game.Clubs.Add(buyer.Id, updatedBuyer) }

    let counter
        (now: DateTime)
        (id: TransferOperationId)
        (fee: decimal)
        (salary: decimal)
        (state: MarketState)
        : Result<MarketState, TransferReason> =
        match state.Operations.TryFind id with
        | Some op when
            op.Status = TransferOperationStatus.Submitted
            || op.Status = TransferOperationStatus.Countered
            ->
            if fee < 0m || salary <= 0m then
                fail (InvalidOperation "Counteroffer has invalid terms")
            else
                Ok
                    { state with
                        Operations =
                            state.Operations.Add(
                                id,
                                { op with
                                    Fee = fee
                                    Salary = salary
                                    Status = Countered
                                    Updated = now }
                            ) }
        | Some _ -> fail (InvalidOperation "Operation is not negotiable")
        | None -> fail (InvalidOperation "Operation not found")

    let agree (now: DateTime) (id: TransferOperationId) (state: MarketState) =
        match state.Operations.TryFind id with
        | Some op when
            op.Status = TransferOperationStatus.Submitted
            || op.Status = TransferOperationStatus.Countered
            ->
            Ok
                { state with
                    Operations =
                        state.Operations.Add(
                            id,
                            { op with
                                Status = TransferOperationStatus.Agreed
                                Updated = now }
                        ) }
        | Some _ -> fail (InvalidOperation "Operation cannot be agreed")
        | None -> fail (InvalidOperation "Operation not found")

    let reject (now: DateTime) (id: TransferOperationId) (reason: TransferReason) (state: MarketState) =
        match state.Operations.TryFind id with
        | Some op ->
            Ok
                { state with
                    Operations =
                        state.Operations.Add(
                            id,
                            { op with
                                Status = Rejected(string reason)
                                Reason = Some reason
                                Updated = now }
                        ) }
        | None -> fail (InvalidOperation "Operation not found")

    let withdraw (now: DateTime) (id: TransferOperationId) (state: MarketState) =
        match state.Operations.TryFind id with
        | Some op when op.Status <> TransferOperationStatus.Completed ->
            Ok
                { state with
                    Operations =
                        state.Operations.Add(
                            id,
                            { op with
                                Status = TransferOperationStatus.Withdrawn
                                Updated = now }
                        ) }
        | _ -> fail (InvalidOperation "Completed operation cannot be withdrawn")

    let complete
        (now: DateTime)
        (id: TransferOperationId)
        (cfg: TransferMarketConfig)
        (game: GameState)
        : Result<GameState, TransferReason> =
        match game.Market.Operations.TryFind id with
        | None -> fail (InvalidOperation "Operation not found")
        | Some op when op.Status <> TransferOperationStatus.Agreed -> fail (InvalidOperation "Operation is not agreed")
        | Some op when not (TransferWindow.isOpen now cfg.Window) -> fail TransferWindowClosed
        | Some op ->
            match
                game.Players.TryFind op.Player, game.Clubs.TryFind op.Buyer, op.Seller |> Option.bind game.Clubs.TryFind
            with
            | Some player, Some buyer, seller ->
                let sellerFinance, sellerClub =
                    match seller with
                    | None -> Ok None, None
                    | Some club ->
                        Finance.recordRevenue
                            now
                            ($"transfer:{id}")
                            "Transfer completed"
                            "Transfer fee"
                            op.Fee
                            (Some buyer.Name)
                            club.Finance
                        |> Result.map (fun f -> Some { club with Finance = f }),
                        Some club

                let buyerFinance =
                    buyer.Finance
                    |> Finance.releaseReservation (operationReservationId id)
                    |> fun released ->
                        if Finance.availableCash released < op.Fee then
                            Error "Cash reserve would be breached"
                        else
                            Ok released
                    |> Result.bind (
                        Finance.commitObligation
                            now
                            ($"transfer:{id}")
                            "Confirmed transfer"
                            { Id = $"transfer:{id}"
                              Counterparty = seller |> Option.map _.Name |> Option.defaultValue "Free agent"
                              Description = "Transfer fee"
                              DueDate = now
                              Amount = op.Fee
                              Currency = EUR
                              Condition = "Confirmed transfer"
                              State = ObligationCommitted
                              AccruedAmount = 0m
                              PaidAmount = 0m }
                    )
                    |> Result.bind (
                        Finance.settleObligation now ($"transfer:{id}:payment") "Transfer completed" ($"transfer:{id}")
                    )

                match buyerFinance, sellerFinance with
                | Ok bf, Ok sf ->
                    let contract =
                        { Salary = op.Salary
                          ExpiryYear = now.Year + op.ContractYears }

                    let contractId = ContractId game.Market.NextContractId

                    let marketContract =
                        { Id = contractId
                          Employer = buyer.Id
                          Player = player.Id
                          Starts = now
                          Ends = now.AddYears(op.ContractYears)
                          Salary = op.Salary
                          Bonuses = 0m
                          Role = op.Role
                          MinutesPromised = op.MinutesPromised
                          Options = []
                          Clauses = [] }

                    let moved =
                        { player with
                            Affiliation = Contracted(buyer.Id, contract) }

                    let buyer' = { buyer with Finance = bf } |> Club.addPlayer player.Id
                    let clubs = game.Clubs.Add(buyer.Id, buyer')

                    let clubs =
                        match seller, sf with
                        | Some old, Some updated -> clubs.Add(old.Id, Club.removePlayer player.Id updated)
                        | _ -> clubs

                    let marketOp =
                        { op with
                            Status = TransferOperationStatus.Completed
                            Updated = now }

                    let market =
                        { game.Market with
                            Operations = game.Market.Operations.Add(id, marketOp)
                            Contracts = game.Market.Contracts.Add(contractId, marketContract)
                            Registrations = game.Market.Registrations.Add(player.Id, Eligible)
                            NextContractId = game.Market.NextContractId + 1
                            Events =
                                TransferCompleted(id, "affiliation, contract and finance confirmed")
                                :: game.Market.Events }

                    Ok
                        { game with
                            Players = game.Players.Add(player.Id, moved)
                            Clubs = clubs
                            Market = market
                            PulseQueues =
                                PulseQueues.recordTransfer
                                    { Player = player.Id
                                      From = seller |> Option.map _.Id
                                      To = buyer.Id
                                      Fee = op.Fee }
                                    game.PulseQueues }
                | Error _, _ -> fail InsufficientFunds
                | _, Error _ -> fail (InvalidOperation "Seller finance rejected receipt")
            | _ -> fail (InvalidOperation "Player or club not found")

    let renew
        (now: DateTime)
        (contractId: ContractId)
        (ends: DateTime)
        (salary: decimal)
        (minutes: int)
        (state: MarketState)
        =
        match state.Contracts.TryFind contractId with
        | Some c when ends > c.Ends && salary > 0m && minutes >= 0 ->
            Ok
                { state with
                    Contracts =
                        state.Contracts.Add(
                            contractId,
                            { c with
                                Ends = ends
                                Salary = salary
                                MinutesPromised = minutes }
                        )
                    Events =
                        ContractRenewed(contractId, "future obligations replaced at agreed date")
                        :: state.Events }
        | Some _ -> fail (InvalidOperation "Renewal terms are invalid")
        | None -> fail (InvalidOperation "Contract not found")

    let promoteYouth
        (now: DateTime)
        (clubId: ClubId)
        (playerId: PlayerId)
        (salary: decimal)
        (years: int)
        (role: ContractRole)
        (minutes: int)
        (game: GameState)
        : Result<GameState, TransferReason> =
        match game.Players.TryFind playerId, game.Clubs.TryFind clubId with
        | Some player, Some club when
            player.Affiliation = YouthProspect clubId
            && salary > 0m
            && years > 0
            && minutes >= 0
            ->
            if club.Finance.WageBudget - club.Finance.WageCommitment < salary * 12m then
                fail WageCapacityExceeded
            else
                let contractId = ContractId game.Market.NextContractId

                let contract =
                    { Id = contractId
                      Employer = clubId
                      Player = playerId
                      Starts = now
                      Ends = now.AddYears years
                      Salary = salary
                      Bonuses = 0m
                      Role = role
                      MinutesPromised = minutes
                      Options = []
                      Clauses = [] }

                let updatedPlayer =
                    { player with
                        Affiliation =
                            Contracted(
                                clubId,
                                { Salary = salary
                                  ExpiryYear = now.Year + years }
                            ) }

                let updatedClub = club |> Club.addPlayer playerId

                let market =
                    { game.Market with
                        Contracts = game.Market.Contracts.Add(contractId, contract)
                        Registrations = game.Market.Registrations.Add(playerId, Eligible)
                        NextContractId = game.Market.NextContractId + 1
                        Events =
                            PlayerPromoted(playerId, "youth prospect promoted with first professional contract")
                            :: game.Market.Events }

                Ok
                    { game with
                        Players = game.Players.Add(playerId, updatedPlayer)
                        Clubs = game.Clubs.Add(clubId, updatedClub)
                        Market = market }
        | Some _, Some _ -> fail (InvalidOperation "Only a youth prospect of the club can be promoted")
        | _ -> fail (InvalidOperation "Player or club not found")

    let register (now: DateTime) (id: TransferOperationId) (registrationCloses: DateTime) (state: GameState) =
        match state.Market.Operations.TryFind id with
        | Some op when op.Status = Completed && now.Date <= registrationCloses.Date ->
            match state.Market.Registrations.TryFind op.Player with
            | Some Eligible
            | Some Unregistered ->
                Ok
                    { state with
                        Market =
                            { state.Market with
                                Registrations = state.Market.Registrations.Add(op.Player, Registered) } }
            | Some(RegistrationBlocked reason) -> Error(RegistrationInvalid reason)
            | Some Registered -> Ok state
            | None -> Error(RegistrationInvalid "player has no registration record")
        | Some _ -> Error(RegistrationInvalid "operation is not completed")
        | None -> Error(InvalidOperation "Operation not found")

    let activateReleaseClause (now: DateTime) (contractId: ContractId) (state: MarketState) =
        match state.Contracts.TryFind contractId with
        | Some contract ->
            match
                contract.Clauses
                |> List.tryPick (function
                    | ReleaseClause fee -> Some fee
                    | _ -> None)
            with
            | Some fee when fee > 0m ->
                Ok
                    { state with
                        Events =
                            ContractRenewed(contractId, $"release clause activated at {now:O}; fee={fee}")
                            :: state.Events }
            | None -> Error(InvalidOperation "contract has no release clause")
            | Some _ -> Error(InvalidOperation "release clause must be positive")
        | None -> Error(InvalidOperation "Contract not found")

    let startLoan (loan: LoanAgreement) (state: MarketState) =
        if
            loan.Origin = loan.Destination
            || loan.Ends <= loan.Starts
            || loan.WageShareOrigin < 0m
            || loan.WageShareDestination < 0m
        then
            Error(InvalidOperation "Invalid loan agreement")
        elif state.Loans.ContainsKey loan.Player then
            Error(InvalidOperation "player already has an active loan")
        else
            Ok
                { state with
                    Loans = state.Loans.Add(loan.Player, loan) }

    let startLoanInGame (loan: LoanAgreement) (game: GameState) : Result<GameState, TransferReason> =
        match game.Players.TryFind loan.Player, game.Clubs.TryFind loan.Origin, game.Clubs.TryFind loan.Destination with
        | Some player, Some origin, Some destination when Player.clubOf player = Some loan.Origin ->
            match startLoan loan game.Market with
            | Error reason -> Error reason
            | Ok market ->
                let origin' = Club.removePlayer loan.Player origin
                let destination' = Club.addPlayer loan.Player destination

                Ok
                    { game with
                        Clubs = game.Clubs.Add(loan.Origin, origin').Add(loan.Destination, destination')
                        Market = market }
        | _ -> Error(InvalidOperation "Loan player and clubs do not match the contract")

    let endLoan (now: DateTime) (player: PlayerId) (state: MarketState) =
        match state.Loans.TryFind player with
        | Some loan when now.Date >= loan.Ends.Date ->
            Ok
                { state with
                    Loans = state.Loans.Remove player
                    Events = LoanEnded(player, "loan term elapsed") :: state.Events }
        | Some _ -> Error(InvalidOperation "loan has not ended")
        | None -> Error(InvalidOperation "Loan not found")

    let endLoanInGame (now: DateTime) (playerId: PlayerId) (game: GameState) : Result<GameState, TransferReason> =
        match game.Market.Loans.TryFind playerId with
        | Some loan ->
            match
                endLoan now playerId game.Market, game.Clubs.TryFind loan.Origin, game.Clubs.TryFind loan.Destination
            with
            | Ok market, Some origin, Some destination ->
                let destination' = Club.removePlayer playerId destination
                let origin' = Club.addPlayer playerId origin

                Ok
                    { game with
                        Clubs = game.Clubs.Add(loan.Origin, origin').Add(loan.Destination, destination')
                        Market = market }
            | Error reason, _, _ -> Error reason
            | _ -> Error(InvalidOperation "Loan club not found")
        | None -> Error(InvalidOperation "Loan not found")

    let renewInGame
        (now: DateTime)
        (contractId: ContractId)
        (ends: DateTime)
        (salary: decimal)
        (minutes: int)
        (game: GameState)
        : Result<GameState, TransferReason> =
        match game.Market.Contracts.TryFind contractId with
        | None -> Error(InvalidOperation "Contract not found")
        | Some contract ->
            match game.Clubs.TryFind contract.Employer with
            | None -> Error(InvalidOperation "Employer club not found")
            | Some club when club.Finance.WageBudget - club.Finance.WageCommitment < salary * 12m ->
                Error WageCapacityExceeded
            | Some _ ->
                renew now contractId ends salary minutes game.Market
                |> Result.map (fun market -> { game with Market = market })

    let assignScouting (assignment: ScoutingAssignment) (clubId: ClubId) (state: MarketState) =
        Ok
            { state with
                Assignments = state.Assignments.Add(assignment.Id, assignment)
                Shortlists =
                    state.Shortlists
                    |> Map.change clubId (fun ids -> Some(ids |> Option.defaultValue [])) }

    let addReport (clubId: ClubId) (report: RecruitmentReport) (state: MarketState) =
        Ok
            { state with
                Knowledge = state.Knowledge.Add(report.Player, report.Knowledge)
                Shortlists =
                    state.Shortlists
                    |> Map.change clubId (fun ids ->
                        Some(
                            report.Player
                            :: (ids |> Option.defaultValue [] |> List.filter ((<>) report.Player))
                        ))
                Events = ReportReady(report.Assignment, report.Knowledge.ReportKey) :: state.Events }

/// Deterministic market policy used by world phases when the caller has not
/// supplied a human negotiation.  Ordering is explicit so parallel world
/// execution cannot make the winner depend on map enumeration.
module MarketManager =

    let private roleAndMinutes (player: Player) =
        match player.Position with
        | GK
        | DC
        | DL
        | DR -> Rotation, 450
        | _ -> Rotation, 450

    let run (cfg: TransferMarketConfig) (now: DateTime) (state: GameState) : GameState =
        if not (TransferWindow.isOpen now cfg.Window) then
            state
        else
            state.Clubs
            |> Map.toList
            |> List.sortBy fst
            |> List.fold
                (fun current (clubId, club) ->
                    let candidates =
                        current.Players
                        |> Map.toList
                        |> List.sortBy fst
                        |> List.choose (fun (_, player) ->
                            match Player.clubOf player with
                            | Some owner when owner = clubId -> None
                            | Some _ when player.Status <> Available -> None
                            | None
                            | Some _ ->
                                let fee = Player.playerValue player.CurrentSkill
                                let role, minutes = roleAndMinutes player

                                if
                                    Finance.availableForTransfers club.Finance >= fee
                                    && Finance.availableCash club.Finance >= fee
                                    && club.Finance.WageBudget - club.Finance.WageCommitment
                                       >= Player.playerSalary player.CurrentSkill * 12m
                                then
                                    Some(player, fee, role, minutes)
                                else
                                    None)

                    match candidates with
                    | [] -> current
                    | (player, fee, role, minutes) :: _ ->
                        match
                            TransferMarket.submitInGame
                                cfg
                                now
                                club
                                (Player.clubOf player |> Option.bind current.Clubs.TryFind)
                                player
                                fee
                                (Player.playerSalary player.CurrentSkill)
                                3
                                role
                                minutes
                                current
                        with
                        | Error _ -> current
                        | Ok submitted ->
                            let operationId = TransferOperationId(submitted.Market.NextOperationId - 1)

                            match TransferMarket.agree now operationId submitted.Market with
                            | Error _ -> submitted
                            | Ok market -> { submitted with Market = market })
                state
