namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.Controls.Primitives
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

    let private tr key = Localization.resolve key []

    let private stat (label: TranslationKey) value (detail: TranslationKey) =
        Border.create
            [ Border.margin (0.0, 0.0, 12.0, 12.0)
              Border.child (UI.iconStatCard (tr label) (money value) ClubIcon.finances (tr detail)) ]
        :> IView

    let private ledgerRow (entry: LedgerEntry) =
        let direction, color =
            if entry.Debits |> List.exists (fun p -> p.Account = CashAccount) then
                TranslationKey.LedgerIncome, Theme.Success
            elif entry.Credits |> List.exists (fun p -> p.Account = CashAccount) then
                TranslationKey.LedgerExpense, Theme.Hot
            else
                TranslationKey.LedgerCommitment, Theme.AccentAlt

        Border.create
            [ Border.padding (20.0, 12.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 1.0, 0.0, 0.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "110, 120, *, 140"
                        Grid.children
                            [ TextBlock.create
                                  [ Grid.column 0
                                    TextBlock.text (entry.Date.ToString("dd/MM/yyyy"))
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.foreground Theme.TextMuted ]
                              Border.create
                                  [ Grid.column 1
                                    Border.horizontalAlignment HorizontalAlignment.Left
                                    Border.child (UI.badge ((tr direction).ToUpperInvariant()) color) ]
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text entry.Cause
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                    TextBlock.foreground Theme.TextMain ]
                              Border.create
                                  [ Grid.column 3
                                    Border.horizontalAlignment HorizontalAlignment.Right
                                    Border.child (UI.display (money entry.Amount) 22.0 Theme.TextMain) ] ] ]
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

            let fact (label: TranslationKey) (value: string) =
                Border.create
                    [ Border.background Theme.BgCard
                      Border.borderBrush Theme.Border
                      Border.borderThickness 1.0
                      Border.cornerRadius Theme.RadiusPill
                      Border.padding (14.0, 8.0)
                      Border.margin (0.0, 0.0, 10.0, 10.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text $"{tr label}: {value}"
                                TextBlock.fontSize 14.0
                                TextBlock.foreground Theme.TextSub ]
                      ) ]
                :> IView

            ScrollViewer.create
                [ ScrollViewer.horizontalScrollBarVisibility Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin (32.0, 8.0, 32.0, 32.0)
                            StackPanel.spacing 14.0
                            StackPanel.children
                                [ StackPanel.create
                                      [ StackPanel.spacing 6.0
                                        StackPanel.children
                                            [ UI.display (tr TranslationKey.FinanceTitle) 44.0 Theme.TextMain
                                              TextBlock.create
                                                  [ TextBlock.text (tr TranslationKey.FinanceSubtitle)
                                                    TextBlock.fontSize 14.0
                                                    TextBlock.foreground Theme.TextMuted ] ] ]
                                  UniformGrid.create
                                      [ UniformGrid.columns 4
                                        UniformGrid.children
                                            [ stat TranslationKey.FinanceCash finance.Cash TranslationKey.FinanceCashSub
                                              stat
                                                  TranslationKey.FinanceTransfers
                                                  (Finance.availableForTransfers finance)
                                                  TranslationKey.FinanceTransfersSub
                                              stat
                                                  TranslationKey.FinanceWages
                                                  finance.WageCommitment
                                                  TranslationKey.FinanceWagesSub
                                              stat
                                                  TranslationKey.FinanceForecast
                                                  forecast.ExpectedCash
                                                  TranslationKey.FinanceForecastSub ] ]
                                  WrapPanel.create
                                      [ WrapPanel.children
                                            [ fact TranslationKey.FinanceReservations (string finance.Reservations.Length)
                                              fact TranslationKey.FinanceObligations (string obligations.Length)
                                              fact TranslationKey.FinanceDebt (money finance.Debt) ] ]
                                  UI.panelCard
                                      (UI.sectionHeader ClubIcon.finances (tr TranslationKey.FinanceLedger))
                                      (if rows.IsEmpty then
                                           Border.create
                                               [ Border.padding (20.0, 4.0, 20.0, 20.0)
                                                 Border.child (
                                                     TextBlock.create
                                                         [ TextBlock.text (tr TranslationKey.FinanceLedgerEmpty)
                                                           TextBlock.fontSize 14.0
                                                           TextBlock.foreground Theme.TextMuted ]
                                                 ) ]
                                           :> IView
                                       else
                                           StackPanel.create [ StackPanel.children (rows |> List.map ledgerRow) ] :> IView) ] ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text (tr TranslationKey.NoCareerLoaded) ] :> IView
