namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Domain
open FootballEngine.Components
open FootballEngine.Icons

module Finances =
    let private money value = Formatters.money value

    let private stat label value detail color =
        UI.iconStatCard label (money value) ClubIcon.finances detail
        |> fun view ->
            Border.create
                [ Border.child view
                  Border.margin 6.0
                  Border.borderBrush (Theme.withAlpha "45" color)
                  Border.borderThickness 1.0 ]

    let private ledgerRow (entry: LedgerEntry) =
        let direction =
            if entry.Debits |> List.exists (fun p -> p.Account = CashAccount) then
                "Ingreso"
            elif entry.Credits |> List.exists (fun p -> p.Account = CashAccount) then
                "Egreso"
            else
                "Compromiso"

        Border.create
            [ Border.padding (12.0, 8.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "120, *, 130, 120"
                        Grid.children
                            [ TextBlock.create
                                  [ Grid.column 0
                                    TextBlock.text (entry.Date.ToString("dd/MM/yyyy"))
                                    TextBlock.foreground Theme.TextMuted ]
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text $"{direction} · {entry.Cause}"
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text (money entry.Amount)
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ Grid.column 3
                                    TextBlock.text entry.OperationId
                                    TextBlock.foreground Theme.TextMuted ] ] ]
              ) ]
        :> IView

    let view (state: State) (_dispatch: Msg -> unit) =
        match state.Mode with
        | InGame(gs, _) ->
            let club = gs.Clubs[gs.UserClubId]
            let finance = club.Finance
            let forecast = Finance.forecast finance

            let obligations =
                finance.Obligations
                |> List.filter (fun o -> o.State <> ObligationPaid && o.State <> ObligationCancelled)

            let rows = finance.Ledger |> List.sortByDescending _.Id |> List.truncate 12

            ScrollViewer.create
                [ ScrollViewer.padding 28.0
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.spacing 16.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text "FINANZAS"
                                        TextBlock.fontSize 24.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextMain ]
                                  TextBlock.create
                                      [ TextBlock.text "Caja, autorizaciones y compromisos confirmados"
                                        TextBlock.foreground Theme.TextMuted ]
                                  Grid.create
                                      [ Grid.columnDefinitions "*, *, *, *"
                                        Grid.children
                                            [ stat "CAJA" finance.Cash "Disponible hoy" Theme.Success
                                              |> fun v -> v :> IView
                                              stat
                                                  "FICHAJES"
                                                  (Finance.availableForTransfers finance)
                                                  "Capacidad libre"
                                                  Theme.Accent
                                              |> fun v -> v :> IView
                                              stat "SALARIOS" finance.WageCommitment "Compromiso" Theme.Warning
                                              |> fun v -> v :> IView
                                              stat "PREVISIÓN" forecast.ExpectedCash "30 días" Theme.AccentAlt
                                              |> fun v -> v :> IView ] ]
                                  TextBlock.create
                                      [ TextBlock.text
                                            $"Reservas activas: {finance.Reservations.Length} · Obligaciones pendientes: {obligations.Length} · Deuda: {money finance.Debt}"
                                        TextBlock.foreground Theme.TextSub ]
                                  TextBlock.create
                                      [ TextBlock.text "DESGLOSE DE OPERACIONES"
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextMain ]
                                  Border.create
                                      [ Border.background Theme.BgSidebar
                                        Border.child (
                                            StackPanel.create [ StackPanel.children (rows |> List.map ledgerRow) ]
                                        ) ] ] ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text "No hay una carrera cargada" ] :> IView
