namespace FootballEngine.World.Phases

open System
open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.AgentTypes

module TransferPhase =

    let makeWithBalance (balance: FootballEngine.Core.Match.BalanceConfig) (state: GameState) : WorldPhase =
        let marketConfig (now: DateTime) : TransferMarketConfig =
            { Window =
                { TransferWindow.summer now.Year with
                    Opens = DateTime(now.Year, 1, 1)
                    Closes = DateTime(now.Year, 12, 31) }
              RegistrationCloses = DateTime(now.Year, 12, 31)
              AllowPreContracts = true }

        let completeAgreedOperations state =
            let completed =
                state.Market.Operations
                |> Map.toList
                |> List.fold
                    (fun current (id, operation) ->
                        match operation.Status with
                        | TransferOperationStatus.Agreed ->
                            match
                                TransferMarket.complete
                                    current.CurrentDate
                                    id
                                    (marketConfig current.CurrentDate)
                                    current
                            with
                            | Ok completed -> completed
                            | Error _ -> current
                        | _ -> current)
                    state

            completed.Market.Loans
            |> Map.toList
            |> List.fold
                (fun current (playerId, loan) ->
                    if current.CurrentDate.Date >= loan.Ends.Date then
                        match TransferMarket.endLoanInGame current.CurrentDate playerId current with
                        | Ok ended -> ended
                        | Error _ -> current
                    else
                        current)
                completed

        { Tag = Transfer
          Frequency = Weekly
          Run = fun _ state -> completeAgreedOperations state }

    let makeFromSave (saveOpt: FootballEngine.Settings.SaveSettings option) (state: GameState) : WorldPhase =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        makeWithBalance balance state

    let make (state: GameState) : WorldPhase = makeFromSave None state
