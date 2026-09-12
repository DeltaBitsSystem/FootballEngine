namespace FootballEngine.Components

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Icons
open FootballEngine.Routing

module Header =
    let private clubContext (gameState: GameState) =
        let clubName =
            gameState.Clubs
            |> Map.tryFind gameState.UserClubId
            |> Option.map _.Name
            |> Option.defaultValue "Football Engine"

        let competitionName =
            gameState.Competitions
            |> Map.toSeq
            |> Seq.tryPick (fun (_, competition) ->
                match competition.Type, competition.Country with
                | NationalLeague(LeagueLevel 0, _), Some country when
                    country = gameState.PrimaryCountry
                    && List.contains gameState.UserClubId competition.ClubIds
                    ->
                    Some competition.Name
                | _ -> None)
            |> Option.defaultValue ""

        clubName, competitionName

    let private continueLabel (gameState: GameState) =
        let nextDate = gameState.CurrentDate.AddDays 1.0

        let stopsForMatch =
            GameState.getUserNextFixture gameState
            |> Option.exists (fun (_, fixture) -> fixture.ScheduledDate.Date <= nextDate.Date)

        if stopsForMatch then
            Localization.resolve TranslationKey.GoToMatch []
        else
            Localization.resolve
                TranslationKey.ContinueToDate
                [ "date", nextDate.ToString("ddd dd MMM yyyy").ToUpperInvariant() ]

    let private saveStatus (status: SaveStatus) =
        match status with
        | SaveIdle -> "", Theme.TextMuted
        | Saving _ -> Localization.resolve TranslationKey.SaveStatusSaving [], Theme.Warning
        | Saved _ -> Localization.resolve TranslationKey.SaveStatusSaved [], Theme.Success
        | SaveFailed _ -> Localization.resolve TranslationKey.SaveStatusError [], Theme.Danger

    let private navButton
        (icon: Material.Icons.MaterialIconKind)
        (label: string)
        (enabled: bool)
        (onClick: unit -> unit)
        : IView =
        Button.create
            [ Button.isEnabled enabled
              Button.background (if enabled then Theme.BgCard else Theme.BgElevated)
              Button.borderBrush Theme.BorderLight
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusMd
              Button.padding (8.0, 7.0)
              Button.opacity (if enabled then 1.0 else 0.45)
              Button.onClick (fun event ->
                  event.Handled <- true
                  onClick ())
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 5.0
                        StackPanel.children
                            [ Icons.iconSm icon Theme.TextSub
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let header (state: State) (dispatch: Msg -> unit) : IView =
        let gameState =
            match state.Mode with
            | InGame(gs, _) -> Some gs
            | _ -> None

        let clubName, competitionName =
            gameState
            |> Option.map clubContext
            |> Option.defaultValue ("Football Engine", "")

        let dateText =
            gameState
            |> Option.map (fun gs -> gs.CurrentDate.ToString("ddd dd MMM yyyy").ToUpperInvariant())
            |> Option.defaultValue ""

        let saveText, saveColor = saveStatus state.SaveStatus
        let isProcessing = state.IsProcessing
        let canContinue = gameState.IsSome && not isProcessing

        let saveBadge: IView =
            if saveText = "" then
                TextBlock.create [ TextBlock.text "" ] :> IView
            else
                Border.create
                    [ Border.padding (8.0, 4.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text saveText
                                TextBlock.fontSize 11.0
                                TextBlock.fontWeight FontWeight.SemiBold
                                TextBlock.foreground saveColor ]
                      ) ]
                :> IView

        let clubInitial =
            if clubName.Length > 0 then
                clubName.Substring(0, 1).ToUpperInvariant()
            else
                "F"

        let identityBadge: IView =
            Border.create
                [ Border.width 34.0
                  Border.height 34.0
                  Border.cornerRadius 8.0
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.BorderLight
                  Border.borderThickness 1.0
                  Border.child (
                      TextBlock.create
                          [ TextBlock.text clubInitial
                            TextBlock.fontSize 14.0
                            TextBlock.fontWeight FontWeight.Black
                            TextBlock.foreground Theme.Accent
                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                            TextBlock.verticalAlignment VerticalAlignment.Center ]
                  ) ]

        let identityText: IView =
            StackPanel.create
                [ StackPanel.spacing 1.0
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.children
                      [ TextBlock.create
                            [ TextBlock.text clubName
                              TextBlock.fontSize 13.5
                              TextBlock.fontWeight FontWeight.Bold
                              TextBlock.foreground Theme.TextMain ]
                        TextBlock.create
                            [ TextBlock.text competitionName
                              TextBlock.fontSize 11.0
                              TextBlock.foreground Theme.TextDim ] ] ]

        let identity =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.spacing 12.0
                  StackPanel.children [ identityBadge; identityText ] ]

        let search =
            Border.create
                [ Border.width 190.0
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.BorderLight
                  Border.borderThickness 1.0
                  Border.cornerRadius Theme.RadiusMd
                  Border.child (
                      TextBox.create
                          [ TextBox.text state.GlobalSearchQuery
                            TextBox.watermark "Search players, clubs or messages"
                            TextBox.padding (10.0, 7.0)
                            TextBox.background "Transparent"
                            TextBox.borderThickness 0.0
                            TextBox.onTextChanged (fun query -> dispatch (SearchGlobal query)) ]
                  ) ]

        let left =
            StackPanel.create
                [ Grid.column 0
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.spacing 16.0
                  StackPanel.children [ identity; search ] ]

        let right =
            StackPanel.create
                [ Grid.column 1
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.spacing 8.0
                  StackPanel.children
                      [ navButton
                            Nav.prev
                            (Localization.resolve TranslationKey.Back [])
                            (not state.Navigation.Back.IsEmpty)
                            (fun () -> dispatch GoBack)
                        navButton
                            Nav.next
                            (Localization.resolve TranslationKey.Forward [])
                            (not state.Navigation.Forward.IsEmpty)
                            (fun () -> dispatch GoForward)
                        saveBadge
                        Border.create
                            [ Border.background Theme.BgCard
                              Border.cornerRadius 999.0
                              Border.borderBrush Theme.BorderLight
                              Border.borderThickness 1.0
                              Border.padding (10.0, 7.0)
                              Border.child (
                                  StackPanel.create
                                      [ StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 6.0
                                        StackPanel.children
                                            [ Icons.iconSm IconName.calendar Theme.TextMuted
                                              TextBlock.create
                                                  [ TextBlock.text dateText
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextSub ] ] ]
                              ) ]
                        Button.create
                            [ Button.isEnabled (gameState.IsSome && not isProcessing)
                              Button.background Theme.BgCard
                              Button.borderBrush Theme.BorderLight
                              Button.borderThickness 1.0
                              Button.cornerRadius Theme.RadiusMd
                              Button.padding (10.0, 8.0)
                              Button.onClick (fun event ->
                                  event.Handled <- true
                                  dispatch (PersistenceMsg SaveCurrent))
                              Button.content (
                                  StackPanel.create
                                      [ StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 5.0
                                        StackPanel.children
                                            [ Icons.iconSm IconName.save Theme.TextSub
                                              TextBlock.create
                                                  [ TextBlock.text (Localization.resolve TranslationKey.ManualSave [])
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.foreground Theme.TextSub ] ] ]
                              ) ]
                        Button.create
                            [ Button.isEnabled canContinue
                              Button.background (if canContinue then Theme.Accent else Theme.BgElevated)
                              Button.borderThickness 0.0
                              Button.cornerRadius Theme.RadiusMd
                              Button.padding (16.0, 9.0)
                              Button.onClick (fun event ->
                                  event.Handled <- true
                                  dispatch (SimMsg(Advance 1)))
                              Button.content (
                                  StackPanel.create
                                      [ StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 6.0
                                        StackPanel.children
                                            [ Icons.iconSm
                                                  (if isProcessing then IconName.refresh else Nav.next)
                                                  (if canContinue then Theme.BgSidebar else Theme.TextMuted)
                                              TextBlock.create
                                                  [ TextBlock.text (
                                                        match gameState with
                                                        | Some gs when not isProcessing -> continueLabel gs
                                                        | _ -> Localization.resolve TranslationKey.Continue []
                                                    )
                                                    TextBlock.fontSize 11.5
                                                    TextBlock.fontWeight FontWeight.Black
                                                    TextBlock.foreground (
                                                        if canContinue then Theme.BgSidebar else Theme.TextMuted
                                                    ) ] ] ]
                              ) ] ] ]

        Border.create
            [ Border.height 64.0
              Border.background Theme.BgSidebar
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.borderBrush Theme.Border
              Border.padding (16.0, 0.0, 14.0, 0.0)
              Border.child (Grid.create [ Grid.columnDefinitions "*, Auto"; Grid.children [ left; right ] ]) ]
