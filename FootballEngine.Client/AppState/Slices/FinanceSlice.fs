namespace FootballEngine

open FootballEngine.AppTypes
open FootballEngine.AppMsgs

module FinanceSlice =
    let init: FinanceState =
        { SelectedOperationId = None
          ShowObligations = true
          LedgerFilter = AllLedgerEntries
          LedgerSort = NewestLedgerFirst }

    let update (msg: FinanceMsg) (state: FinanceState) : FinanceState =
        match msg with
        | FinanceMsg.SelectOperation operationId ->
            { state with
                SelectedOperationId = operationId }
        | FinanceMsg.SetLedgerFilter filter -> { state with LedgerFilter = filter }
        | FinanceMsg.SetLedgerSort sort -> { state with LedgerSort = sort }
        | FinanceMsg.ToggleObligations ->
            { state with
                ShowObligations = not state.ShowObligations }
