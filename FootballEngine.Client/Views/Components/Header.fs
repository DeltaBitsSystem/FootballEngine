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

    let private saveStatus (status: SaveStatus) =
        match status with
        | SaveIdle -> Localization.resolve TranslationKey.DraftNotSaved [], Theme.Warning
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
              Button.background Theme.BgCard
              Button.borderBrush Theme.BorderLight
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusMd
              Button.width 44.0
              Button.height 44.0
              Button.opacity (if enabled then 1.0 else 0.4)
              ToolTip.tip label
              Button.onClick (fun event ->
                  event.Handled <- true
                  onClick ())
              Button.content (
                  Icons.icon icon 18.0 Theme.TextSub
                  |> fun view ->
                      Border.create
                          [ Border.horizontalAlignment HorizontalAlignment.Center
                            Border.verticalAlignment VerticalAlignment.Center
                            Border.child view ]
              ) ]

    let private culture () =
        match Localization.active () with
        | Locale.Es -> System.Globalization.CultureInfo.GetCultureInfo "es-AR"
        | Locale.En -> System.Globalization.CultureInfo.GetCultureInfo "en-GB"

    let header (state: State) (dispatch: Msg -> unit) : IView =
        let gameState =
            match state.Mode with
            | InGame(gs, _) -> Some gs
            | _ -> None

        // The day is the headline of a career: everything the manager does is "before the next match".
        let dayText, seasonText =
            match gameState with
            | Some gs ->
                let culture = culture ()
                let day = gs.CurrentDate.ToString("dddd d MMMM", culture)
                let competition = snd (clubContext gs)

                let season =
                    [ string gs.CurrentDate.Year; competition ]
                    |> List.filter (fun part -> part <> "")
                    |> String.concat " · "

                day, season
            | None -> "", ""

        let saveText, saveColor = saveStatus state.SaveStatus
        let isProcessing = state.IsProcessing
        let canContinue = CareerJourney.canContinue state

        let saveBadge: IView list =
            if saveText = "" then
                []
            else
                [ TextBlock.create
                      [ TextBlock.text saveText
                        TextBlock.fontSize 12.0
                        TextBlock.fontWeight FontWeight.SemiBold
                        TextBlock.foreground saveColor
                        TextBlock.verticalAlignment VerticalAlignment.Center ] ]

        let today =
            StackPanel.create
                [ StackPanel.spacing 2.0
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.children
                      [ UI.display dayText 30.0 Theme.TextMain
                        TextBlock.create
                            [ TextBlock.text seasonText
                              TextBlock.fontSize 13.0
                              TextBlock.foreground Theme.TextMuted ] ] ]

        let search =
            Border.create
                [ Border.width 260.0
                  Border.height 44.0
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.BorderLight
                  Border.borderThickness 1.0
                  Border.cornerRadius Theme.RadiusPill
                  Border.verticalAlignment VerticalAlignment.Center
                  Border.child (
                      DockPanel.create
                          [ DockPanel.children
                                [ Border.create
                                      [ DockPanel.dock Dock.Left
                                        Border.padding (14.0, 0.0, 0.0, 0.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (Icons.icon IconName.search 16.0 Theme.TextMuted) ]
                                  TextBox.create
                                      [ TextBox.text state.GlobalSearchQuery
                                        TextBox.placeholderText (
                                            Localization.resolve TranslationKey.SearchPlaceholder []
                                        )
                                        TextBox.padding (8.0, 10.0)
                                        TextBox.background "Transparent"
                                        TextBox.borderThickness 0.0
                                        TextBox.verticalContentAlignment VerticalAlignment.Center
                                        TextBox.onTextChanged (fun query -> dispatch (SearchGlobal query)) ] ] ]
                  ) ]

        let left =
            StackPanel.create
                [ Grid.column 0
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.spacing 24.0
                  StackPanel.children [ today; search ] ]

        let continueButton =
            Button.create
                [ Button.isEnabled canContinue
                  Button.background (if canContinue then Theme.Accent else Theme.BgElevated)
                  Button.borderThickness 0.0
                  Button.cornerRadius Theme.RadiusMd
                  Button.padding (24.0, 0.0)
                  Button.height 52.0
                  Button.onClick (fun event ->
                      event.Handled <- true
                      dispatch (SimMsg ContinueCareer))
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 10.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.children
                                [ UI.display
                                      (CareerJourney.continueLabel state)
                                      22.0
                                      (if canContinue then Theme.OnAccent else Theme.TextMuted)
                                  Icons.icon
                                      (if isProcessing then IconName.refresh else Nav.next)
                                      20.0
                                      (if canContinue then Theme.OnAccent else Theme.TextMuted) ] ]
                  ) ]
            |> fun view ->
                // The one action that moves the career forward glows like the mockup's "Continuar".
                Border.create
                    [ Border.cornerRadius Theme.RadiusMd
                      Border.verticalAlignment VerticalAlignment.Center
                      if canContinue then
                          Border.boxShadow (Theme.accentGlow ())
                      Border.child view ]

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
                        yield! saveBadge
                        navButton
                            IconName.save
                            (if state.Match.RuntimeId.IsSome || state.Match.PendingResult.IsSome then
                                 Localization.resolve TranslationKey.SaveAfterMatch []
                             else
                                 Localization.resolve TranslationKey.ManualSave [])
                            (gameState.IsSome
                             && not isProcessing
                             && state.Match.RuntimeId.IsNone
                             && state.Match.PendingResult.IsNone)
                            (fun () -> dispatch (PersistenceMsg SaveCurrent))
                        Border.create [ Border.width 8.0 ]
                        continueButton ] ]

        Border.create
            [ Border.height 88.0
              Border.background "Transparent"
              Border.padding (32.0, 0.0, 28.0, 0.0)
              Border.child (Grid.create [ Grid.columnDefinitions "*, Auto"; Grid.children [ left; right ] ]) ]
