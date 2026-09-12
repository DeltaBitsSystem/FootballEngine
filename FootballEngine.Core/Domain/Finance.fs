namespace FootballEngine.Domain

open System

type Currency = EUR

type FinancialAccount =
    | CashAccount
    | TransferBudgetAccount
    | WageBudgetAccount
    | ExpenseAccount of string
    | RevenueAccount of string
    | PayablesAccount

type LedgerPosting =
    { Account: FinancialAccount
      Amount: decimal }

type LedgerEntry =
    { Id: uint64
      Date: DateTime
      OperationId: string
      Cause: string
      Debits: LedgerPosting list
      Credits: LedgerPosting list
      Amount: decimal
      Currency: Currency
      Counterparty: string option }

type ObligationState =
    | ObligationCommitted
    | ObligationAccrued
    | ObligationPaid
    | ObligationOverdue
    | ObligationCancelled

type FinancialObligation =
    { Id: string
      Counterparty: string
      Description: string
      DueDate: DateTime
      Amount: decimal
      Currency: Currency
      Condition: string
      State: ObligationState
      AccruedAmount: decimal
      PaidAmount: decimal }

type Reservation =
    { Id: string
      Reason: string
      Amount: decimal
      CreatedOn: DateTime }

type Facility =
    { Id: string
      Name: string
      Capacity: int
      MaintenancePerDay: decimal
      ConstructionCost: decimal
      StartDate: DateTime option
      CompletionDate: DateTime option
      Active: bool }

type BoardPolicy =
    { MaximumDebt: decimal
      MinimumCashReserve: decimal
      TransferApprovalLimit: decimal
      WageApprovalLimit: decimal }

type CashForecast =
    { HorizonDays: int
      ExpectedCash: decimal
      ExpectedIncome: decimal
      ExpectedCosts: decimal
      Risks: string list }

type FinancialEvent =
    | BudgetApproved of operationId: string * reason: string
    | ObligationCreated of obligationId: string * operationId: string * reason: string
    | PaymentSettled of obligationId: string * operationId: string * reason: string
    | PaymentOverdue of obligationId: string * operationId: string * reason: string
    | FacilityCompleted of facilityId: string * operationId: string * reason: string

type ClubFinance =
    { Cash: decimal
      TransferBudget: decimal
      WageBudget: decimal
      WageCommitment: decimal
      Debt: decimal
      Reservations: Reservation list
      Obligations: FinancialObligation list
      Ledger: LedgerEntry list
      Events: FinancialEvent list
      Facilities: Facility list
      Forecast: CashForecast
      BoardPolicy: BoardPolicy
      NextLedgerId: uint64 }

module Finance =

    let private roundMoney amount =
        Decimal.Round(amount, 2, MidpointRounding.ToEven)

    let initial (cash: decimal) =
        let transfer = roundMoney (cash * 0.65m)
        let wage = roundMoney (cash - transfer)

        { Cash = roundMoney cash
          TransferBudget = transfer
          WageBudget = wage
          WageCommitment = 0m
          Debt = 0m
          Reservations = []
          Obligations = []
          Ledger = []
          Events = []
          Facilities = []
          Forecast =
            { HorizonDays = 30
              ExpectedCash = roundMoney cash
              ExpectedIncome = 0m
              ExpectedCosts = 0m
              Risks = [] }
          BoardPolicy =
            { MaximumDebt = cash * 0.25m
              MinimumCashReserve = cash * 0.05m
              TransferApprovalLimit = transfer
              WageApprovalLimit = wage }
          NextLedgerId = 1UL }

    let availableForTransfers finance =
        finance.TransferBudget - (finance.Reservations |> List.sumBy _.Amount)

    let availableCash finance =
        finance.Cash - finance.BoardPolicy.MinimumCashReserve

    let private total (postings: LedgerPosting list) =
        postings |> List.sumBy _.Amount |> roundMoney

    let private post
        date
        operationId
        cause
        counterparty
        (debits: LedgerPosting list)
        (credits: LedgerPosting list)
        (finance: ClubFinance)
        =
        let debitTotal, creditTotal = total debits, total credits

        if debitTotal <= 0m || debitTotal <> creditTotal then
            Error "Ledger entry is not balanced"
        else
            let cashDebit =
                debits |> List.filter (fun p -> p.Account = CashAccount) |> List.sumBy _.Amount

            let cashCredit =
                credits |> List.filter (fun p -> p.Account = CashAccount) |> List.sumBy _.Amount

            let entry =
                { Id = finance.NextLedgerId
                  Date = date
                  OperationId = operationId
                  Cause = cause
                  Debits = debits
                  Credits = credits
                  Amount = debitTotal
                  Currency = EUR
                  Counterparty = counterparty }

            Ok
                { finance with
                    Cash = roundMoney (finance.Cash + cashDebit - cashCredit)
                    Ledger = entry :: finance.Ledger
                    NextLedgerId = finance.NextLedgerId + 1UL }

    let approveBudget date operationId reason transferBudget wageBudget finance =
        if transferBudget < 0m || wageBudget < 0m then
            Error "Budget authorization cannot be negative"
        else
            Ok
                { finance with
                    TransferBudget = roundMoney transferBudget
                    WageBudget = roundMoney wageBudget
                    BoardPolicy =
                        { finance.BoardPolicy with
                            TransferApprovalLimit = transferBudget
                            WageApprovalLimit = wageBudget }
                    Events = BudgetApproved(operationId, reason) :: finance.Events }

    let reserveTransfer date reservationId reason amount finance =
        if amount <= 0m then
            Error "Reservation amount must be positive"
        elif availableForTransfers finance < amount then
            Error "Transfer capacity is already committed"
        else
            Ok
                { finance with
                    Reservations =
                        { Id = reservationId
                          Reason = reason
                          Amount = roundMoney amount
                          CreatedOn = date }
                        :: finance.Reservations }

    let releaseReservation reservationId finance =
        { finance with
            Reservations = finance.Reservations |> List.filter (fun r -> r.Id <> reservationId) }

    let commitObligation date operationId reason (obligation: FinancialObligation) (finance: ClubFinance) =
        if
            finance.Obligations
            |> List.exists (fun o -> o.Id = obligation.Id && o.State <> ObligationCancelled)
        then
            Error "Obligation already exists"
        else
            post
                date
                operationId
                reason
                (Some obligation.Counterparty)
                [ { Account = ExpenseAccount obligation.Description
                    Amount = obligation.Amount } ]
                [ { Account = PayablesAccount
                    Amount = obligation.Amount } ]
                finance
            |> Result.map (fun next ->
                { next with
                    Obligations =
                        { obligation with
                            State = ObligationCommitted
                            AccruedAmount = obligation.Amount }
                        :: next.Obligations
                    Events = ObligationCreated(obligation.Id, operationId, reason) :: next.Events })

    let settleObligation date operationId reason obligationId (finance: ClubFinance) =
        match finance.Obligations |> List.tryFind (fun o -> o.Id = obligationId) with
        | None -> Error "Obligation not found"
        | Some obligation when obligation.State = ObligationPaid -> Ok finance
        | Some obligation when obligation.State = ObligationCancelled -> Error "Obligation is cancelled"
        | Some obligation ->
            post
                date
                operationId
                reason
                (Some obligation.Counterparty)
                [ { Account = PayablesAccount
                    Amount = obligation.Amount } ]
                [ { Account = CashAccount
                    Amount = obligation.Amount } ]
                finance
            |> Result.map (fun next ->
                { next with
                    Obligations =
                        next.Obligations
                        |> List.map (fun o ->
                            if o.Id = obligationId then
                                { o with
                                    State = ObligationPaid
                                    PaidAmount = o.Amount }
                            else
                                o)
                    Events = PaymentSettled(obligationId, operationId, reason) :: next.Events })

    let markOverdue (date: DateTime) operationId reason (finance: ClubFinance) =
        let overdue =
            finance.Obligations
            |> List.filter (fun o ->
                o.DueDate.Date < date.Date
                && o.State <> ObligationPaid
                && o.State <> ObligationCancelled
                && o.State <> ObligationOverdue)

        let next =
            overdue
            |> List.fold
                (fun f o ->
                    { f with
                        Obligations =
                            f.Obligations
                            |> List.map (fun x ->
                                if x.Id = o.Id then
                                    { x with State = ObligationOverdue }
                                else
                                    x)
                        Events = PaymentOverdue(o.Id, operationId, reason) :: f.Events })
                finance

        next

    let recordRevenue date operationId reason account amount counterparty finance =
        post
            date
            operationId
            reason
            counterparty
            [ { Account = CashAccount
                Amount = amount } ]
            [ { Account = RevenueAccount account
                Amount = amount } ]
            finance

    let forecast finance =
        let outstanding =
            finance.Obligations
            |> List.filter (fun (o: FinancialObligation) -> o.State <> ObligationPaid && o.State <> ObligationCancelled)
            |> List.sumBy _.Amount

        { finance.Forecast with
            ExpectedCash = roundMoney (finance.Cash - outstanding)
            ExpectedCosts = outstanding }

    let validate finance =
        let invalid =
            finance.Ledger
            |> List.tryFind (fun (e: LedgerEntry) -> total e.Debits <> total e.Credits || e.Amount <> total e.Debits)

        match invalid with
        | Some _ -> Error "Ledger contains an unbalanced entry"
        | None -> Ok finance
