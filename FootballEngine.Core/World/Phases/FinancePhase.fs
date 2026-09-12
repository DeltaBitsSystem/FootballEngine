namespace FootballEngine.World.Phases

open FootballEngine.Domain
open FootballEngine.World

module FinancePhase =

    let private settleDailyWages (gs: GameState) : GameState =
        let marked =
            gs.Clubs
            |> Map.map (fun _ club ->
                { club with
                    Finance =
                        Finance.markOverdue
                            gs.CurrentDate
                            $"overdue:{gs.CurrentDate:yyyyMMdd}"
                            "Due date reached"
                            club.Finance })

        { gs with Clubs = marked }
        |> fun state -> state.Players
        |> Map.toSeq
        |> Seq.choose (fun (_, p) ->
            match p.Affiliation with
            | Contracted(clubId, c) -> Some(clubId, p.Id, c.Salary / 7m)
            | _ -> None)
        |> Seq.fold
            (fun state (clubId, playerId, amount) ->
                match state.Clubs |> Map.tryFind clubId with
                | None -> state
                | Some club ->
                    let obligationId = $"wage:{playerId}:{state.CurrentDate.Date:yyyyMMdd}"

                    let obligation =
                        { Id = obligationId
                          Counterparty = $"player:{playerId}"
                          Description = "Player wage"
                          DueDate = state.CurrentDate
                          Amount = amount
                          Currency = EUR
                          Condition = "Active player contract"
                          State = ObligationCommitted
                          AccruedAmount = 0m
                          PaidAmount = 0m }

                    let finance =
                        club.Finance
                        |> Finance.commitObligation
                            state.CurrentDate
                            ($"wage-accrual:{obligationId}")
                            "Daily wage accrued"
                            obligation
                        |> Result.bind (
                            Finance.settleObligation
                                state.CurrentDate
                                ($"wage-payment:{obligationId}")
                                "Daily wage paid"
                                obligationId
                        )
                        |> Result.defaultValue club.Finance

                    { state with
                        Clubs = state.Clubs |> Map.add clubId { club with Finance = finance } })
            { gs with Clubs = marked }

    let make: WorldPhase =
        { Tag = Finance
          Frequency = Daily
          Run = fun _clock state -> settleDailyWages state }
