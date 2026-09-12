namespace FootballEngine.Pages.Setup

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Components
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Icons
open FootballEngine.Routing
open FootballEngine.Settings

module Setup =
    let private materializedCountries (state: State) : CountryData list =
        match state.ResolvedData with
        | None -> []
        | Some resolved ->
            match ResolvedWorldData.materialize resolved with
            | Ok world -> world.Countries
            | Error _ -> []

    let private selectedCountryData (state: State) : CountryData option =
        state.Setup.SelectedCountry
        |> Option.bind (fun code ->
            materializedCountries state
            |> List.tryFind (fun country -> country.Country.Code = code))

    let private setupContainer (content: IView) =
        Grid.create
            [ Grid.background Theme.BgMain
              Grid.children
                  [ ScrollViewer.create
                        [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                          ScrollViewer.content (
                              StackPanel.create
                                  [ StackPanel.horizontalAlignment HorizontalAlignment.Center
                                    StackPanel.maxWidth 900.0
                                    StackPanel.margin (32.0, 36.0)
                                    StackPanel.spacing 28.0
                                    StackPanel.children
                                        [ StackPanel.create
                                              [ StackPanel.horizontalAlignment HorizontalAlignment.Center
                                                StackPanel.spacing 3.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text "FOOTBALL ENGINE"
                                                            TextBlock.fontSize 30.0
                                                            TextBlock.fontWeight FontWeight.Black
                                                            TextBlock.foreground Theme.TextMain
                                                            TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                                      TextBlock.create
                                                          [ TextBlock.text "CAREER SETUP"
                                                            TextBlock.fontSize 12.0
                                                            TextBlock.fontWeight FontWeight.SemiBold
                                                            TextBlock.foreground Theme.Accent
                                                            TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                                          content ] ]
                          ) ] ] ]

    let private stepDivider (label: string) =
        DockPanel.create
            [ DockPanel.margin (0.0, 0.0, 0.0, 4.0)
              DockPanel.children
                  [ TextBlock.create
                        [ DockPanel.dock Dock.Right
                          TextBlock.text (label.ToUpperInvariant())
                          TextBlock.fontSize 11.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMuted ]
                    Border.create
                        [ Border.height 1.0
                          Border.background Theme.Border
                          Border.verticalAlignment VerticalAlignment.Center
                          Border.margin (0.0, 0.0, 12.0, 0.0) ] ] ]

    let private heading (title: string) (description: string) =
        StackPanel.create
            [ StackPanel.spacing 6.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontSize 24.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMain ]
                    TextBlock.create
                        [ TextBlock.text description
                          TextBlock.fontSize 13.0
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.textWrapping TextWrapping.Wrap ] ] ]

    let private navRow
        (backStep: SetupStep option)
        (next: (string * Material.Icons.MaterialIconKind * SetupMsg) option)
        (dispatch: Msg -> unit)
        : IView =
        DockPanel.create
            [ DockPanel.margin (0.0, 8.0, 0.0, 0.0)
              DockPanel.children
                  [ Border.create
                        [ DockPanel.dock Dock.Left
                          Border.child (
                              match backStep with
                              | Some step ->
                                  UI.ghostButton "Back" (Some IconName.back) (fun _ ->
                                      dispatch (SetupMsg(GoToStep step)))
                              | None ->
                                  UI.ghostButton "Cancel" (Some IconName.close) (fun _ ->
                                      dispatch (SetupMsg CancelSetup))
                          ) ]
                    match next with
                    | Some(label, icon, message) ->
                        Border.create
                            [ DockPanel.dock Dock.Right
                              Border.child (UI.primaryButton label (Some icon) (fun _ -> dispatch (SetupMsg message))) ]
                    | None -> () ] ]

    let private flagForCode (code: string) =
        match code with
        | "ARG" -> "🇦🇷"
        | "BRA" -> "🇧🇷"
        | "ENG" -> "🏴"
        | "ESP" -> "🇪🇸"
        | _ -> "🌍"

    let private countryCard (state: State) (dispatch: Msg -> unit) (country: CountryData) : IView =
        let code = country.Country.Code

        UI.countrySelectionCard
            country.Country.Name
            (flagForCode code)
            (state.Setup.SelectedCountry = Some code)
            (state.Setup.SecondaryCountries |> List.contains code)
            (fun _ -> dispatch (SetupMsg(SelectPrimaryCountry code)))
            (fun _ -> dispatch (SetupMsg(ToggleSecondaryCountry code)))
        :> IView

    let private leagueChoices (state: State) (dispatch: Msg -> unit) : IView =
        match selectedCountryData state with
        | None -> TextBlock.create [ TextBlock.text "Select a country to see its leagues." ] :> IView
        | Some country ->
            let choices =
                country.LeagueNames
                |> List.mapi (fun level name ->
                    let clubs = country.Clubs |> List.filter (fun club -> club.LeagueLevel = level)

                    level, name, clubs.Length)
                |> List.filter (fun (_, _, count) -> count > 0)

            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.padding 14.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 8.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text "LEAGUE"
                                        TextBlock.fontSize 11.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextMuted ]
                                  for level, name, clubCount in choices do
                                      let isSelected = state.Setup.SelectedLeagueLevel = Some level

                                      Button.create
                                          [ Button.background (
                                                if isSelected then Theme.AccentLight else Theme.BgElevated
                                            )
                                            Button.borderBrush (if isSelected then Theme.Accent else Theme.BorderLight)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius Theme.RadiusMd
                                            Button.padding (12.0, 10.0)
                                            Button.horizontalAlignment HorizontalAlignment.Stretch
                                            Button.horizontalContentAlignment HorizontalAlignment.Left
                                            Button.onClick (fun _ -> dispatch (SetupMsg(SelectLeague level)))
                                            Button.content (
                                                DockPanel.create
                                                    [ DockPanel.lastChildFill false
                                                      DockPanel.children
                                                          [ TextBlock.create
                                                                [ TextBlock.text ($"{level + 1}. {name}")
                                                                  TextBlock.foreground (
                                                                      if isSelected then
                                                                          Theme.Accent
                                                                      else
                                                                          Theme.TextMain
                                                                  )
                                                                  TextBlock.fontWeight FontWeight.SemiBold ]
                                                            TextBlock.create
                                                                [ DockPanel.dock Dock.Right
                                                                  TextBlock.text ($"{clubCount} clubs")
                                                                  TextBlock.foreground Theme.TextMuted ] ] ]
                                            ) ] ] ]
                  ) ]
            :> IView

    let private styleLabel style =
        match style with
        | EmHighPress -> "High press"
        | EmPossession -> "Possession"
        | EmAttack -> "Attack"
        | EmDefense -> "Defensive"
        | EmSetPieces -> "Set pieces"
        | EmRecovery -> "Recovery"

    let private styleChoices (state: State) (dispatch: Msg -> unit) : IView =
        let styles =
            [ EmPossession; EmHighPress; EmAttack; EmDefense; EmSetPieces; EmRecovery ]

        UniformGrid.create
            [ UniformGrid.columns 3
              UniformGrid.children
                  [ for style in styles do
                        let isSelected = state.Setup.ManagementStyle = Some style

                        Button.create
                            [ Button.background (if isSelected then Theme.AccentLight else Theme.BgCard)
                              Button.borderBrush (if isSelected then Theme.Accent else Theme.Border)
                              Button.borderThickness 1.0
                              Button.cornerRadius Theme.RadiusMd
                              Button.padding (12.0, 12.0)
                              Button.onClick (fun _ -> dispatch (SetupMsg(SelectManagementStyle style)))
                              Button.content (
                                  TextBlock.create
                                      [ TextBlock.text (styleLabel style)
                                        TextBlock.fontSize 13.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.foreground (if isSelected then Theme.Accent else Theme.TextMain)
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              ) ] ] ]

    let private difficultyLabel difficulty =
        match difficulty with
        | None -> "Use career defaults"
        | Some DifficultyPreset.Classic -> "Classic"
        | Some DifficultyPreset.Realistic -> "Realistic"
        | Some DifficultyPreset.Arcade -> "Arcade"
        | Some DifficultyPreset.Custom -> "Custom"

    let private difficultyChoices (state: State) (dispatch: Msg -> unit) : IView =
        let choices =
            [ None
              Some DifficultyPreset.Classic
              Some DifficultyPreset.Realistic
              Some DifficultyPreset.Arcade ]

        UniformGrid.create
            [ UniformGrid.columns 2
              UniformGrid.children
                  [ for difficulty in choices do
                        let isSelected = state.Setup.Difficulty = difficulty

                        Button.create
                            [ Button.background (if isSelected then Theme.AccentLight else Theme.BgCard)
                              Button.borderBrush (if isSelected then Theme.Accent else Theme.Border)
                              Button.borderThickness 1.0
                              Button.cornerRadius Theme.RadiusMd
                              Button.padding (12.0, 12.0)
                              Button.onClick (fun _ -> dispatch (SetupMsg(SelectDifficulty difficulty)))
                              Button.content (
                                  TextBlock.create
                                      [ TextBlock.text (difficultyLabel difficulty)
                                        TextBlock.fontSize 13.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.foreground (if isSelected then Theme.Accent else Theme.TextMain)
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              ) ] ] ]

    let private selectedClubChoices (state: State) (dispatch: Msg -> unit) : IView =
        match selectedCountryData state, state.Setup.SelectedLeagueLevel with
        | Some country, Some level ->
            let clubs =
                country.Clubs
                |> List.filter (fun club -> club.LeagueLevel = level)
                |> List.sortByDescending (fun club -> club.Reputation |> Option.defaultValue 0)

            let clubButtons =
                clubs
                |> List.mapi (fun index club ->
                    let selected = state.Setup.SelectedClubName = Some club.Name

                    let button =
                        Button.create
                            [ Button.background (if selected then Theme.AccentLight else "Transparent")
                              Button.borderBrush (if selected then Theme.Accent else Theme.Border)
                              Button.borderThickness (
                                  if selected then
                                      Avalonia.Thickness(3.0, 0.0, 0.0, 0.0)
                                  else
                                      Avalonia.Thickness(0.0)
                              )
                              Button.padding (16.0, 13.0)
                              Button.horizontalAlignment HorizontalAlignment.Stretch
                              Button.horizontalContentAlignment HorizontalAlignment.Left
                              Button.onClick (fun _ -> dispatch (SetupMsg(SelectClub club.Name)))
                              Button.content (
                                  Grid.create
                                      [ Grid.columnDefinitions "Auto, *, Auto"
                                        Grid.children
                                            [ TextBlock.create
                                                  [ Grid.column 0
                                                    TextBlock.text (string (index + 1))
                                                    TextBlock.width 30.0
                                                    TextBlock.foreground Theme.TextMuted ]
                                              TextBlock.create
                                                  [ Grid.column 1
                                                    TextBlock.text club.Name
                                                    TextBlock.fontSize 14.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextMain ]
                                              TextBlock.create
                                                  [ Grid.column 2
                                                    TextBlock.text (
                                                        club.Reputation
                                                        |> Option.map string
                                                        |> Option.defaultValue "Unrated"
                                                    )
                                                    TextBlock.foreground Theme.TextMuted ] ] ]
                              ) ]

                    button :> IView)

            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.clipToBounds true
                  Border.child (
                      ScrollViewer.create
                          [ ScrollViewer.maxHeight 430.0
                            ScrollViewer.content (StackPanel.create [ StackPanel.children clubButtons ]) ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text "Select a league before choosing a club." ] :> IView

    let private summaryRow (label: string) (value: string) =
        Grid.create
            [ Grid.columnDefinitions "160, *"
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text label
                          TextBlock.fontSize 12.0
                          TextBlock.foreground Theme.TextMuted ]
                    TextBlock.create
                        [ Grid.column 1
                          TextBlock.text value
                          TextBlock.fontSize 13.0
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground Theme.TextMain
                          TextBlock.textWrapping TextWrapping.Wrap ] ] ]
        :> IView

    let private loadStatusText status =
        match status with
        | CompatibleSlot -> "Compatible"
        | IncompatibleSlot(expected, actual) -> $"Incompatible ({actual}/{expected})"
        | CorruptSlot _ -> "Corrupt"

    let private loadStatusColor status =
        match status with
        | CompatibleSlot -> Theme.Success
        | IncompatibleSlot _ -> Theme.Warning
        | CorruptSlot _ -> Theme.Danger

    let private loadErrorText error =
        match error with
        | Missing slot -> $"Save '{slot}' does not exist."
        | Incompatible(slot, expected, actual) -> $"Save '{slot}' uses schema {actual}; expected {expected}."
        | Corrupt(slot, details) -> $"Save '{slot}' is corrupt: {details}"

    let private saveSlotButton (slot: SaveSlotInfo) (dispatch: Msg -> unit) : IView =
        let managerName = slot.ManagerName |> Option.defaultValue "Unknown manager"

        let gameDate =
            slot.GameDate
            |> Option.map (fun date -> date.ToString("dd MMM yyyy"))
            |> Option.defaultValue "No game date"

        let clubInfo =
            StackPanel.create
                [ Grid.column 0
                  StackPanel.spacing 3.0
                  StackPanel.children
                      [ TextBlock.create
                            [ TextBlock.text (slot.ClubName |> Option.defaultValue "Unknown club")
                              TextBlock.fontSize 14.0
                              TextBlock.fontWeight FontWeight.SemiBold
                              TextBlock.foreground Theme.TextMain ]
                        TextBlock.create
                            [ TextBlock.text (sprintf "%s  |  %s" managerName gameDate)
                              TextBlock.fontSize 12.0
                              TextBlock.foreground Theme.TextMuted ] ] ]

        let statusInfo =
            StackPanel.create
                [ Grid.column 1
                  StackPanel.spacing 3.0
                  StackPanel.horizontalAlignment HorizontalAlignment.Right
                  StackPanel.children
                      [ TextBlock.create
                            [ TextBlock.text (loadStatusText slot.Status)
                              TextBlock.fontWeight FontWeight.SemiBold
                              TextBlock.foreground (loadStatusColor slot.Status)
                              TextBlock.horizontalAlignment HorizontalAlignment.Right ]
                        TextBlock.create
                            [ TextBlock.text (slot.LastWrittenUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm"))
                              TextBlock.fontSize 11.0
                              TextBlock.foreground Theme.TextDim
                              TextBlock.horizontalAlignment HorizontalAlignment.Right ] ] ]

        Button.create
            [ Button.background Theme.BgCard
              Button.borderBrush Theme.Border
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusMd
              Button.padding (14.0, 12.0)
              Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.horizontalContentAlignment HorizontalAlignment.Left
              Button.onClick (fun _ -> dispatch (PersistenceMsg(PersistenceMsg.LoadSlot slot.SlotId)))
              Button.content (Grid.create [ Grid.columnDefinitions "*, Auto"; Grid.children [ clubInfo; statusInfo ] ]) ]
        :> IView

    let private loadPanel (state: State) (dispatch: Msg -> unit) : IView =
        let body: IView =
            match state.Setup.LoadState with
            | LoadingSlots _ ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ Icons.iconLg IconName.refresh Theme.Accent
                            TextBlock.create
                                [ TextBlock.text "Reading save slots..."
                                  TextBlock.fontSize 14.0
                                  TextBlock.foreground Theme.TextMuted ] ] ]
                :> IView
            | SlotsLoadFailed message ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ TextBlock.create
                                [ TextBlock.text message
                                  TextBlock.foreground Theme.Danger
                                  TextBlock.textWrapping TextWrapping.Wrap ]
                            UI.primaryButton "Retry" (Some IconName.refresh) (fun _ -> dispatch (SetupMsg OpenLoadGame)) ] ]
                :> IView
            | LoadingSlot(slot, _) ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ TextBlock.create
                                [ TextBlock.text $"Loading {slot}..."
                                  TextBlock.fontSize 14.0
                                  TextBlock.foreground Theme.TextMuted ] ] ]
                :> IView
            | LoadFailed(slot, error) ->
                StackPanel.create
                    [ StackPanel.spacing 12.0
                      StackPanel.children
                          [ TextBlock.create
                                [ TextBlock.text "The career could not be loaded"
                                  TextBlock.fontSize 16.0
                                  TextBlock.fontWeight FontWeight.Bold
                                  TextBlock.foreground Theme.Danger ]
                            TextBlock.create
                                [ TextBlock.text (loadErrorText error)
                                  TextBlock.foreground Theme.TextMuted
                                  TextBlock.textWrapping TextWrapping.Wrap ]
                            StackPanel.create
                                [ StackPanel.orientation Orientation.Horizontal
                                  StackPanel.spacing 8.0
                                  StackPanel.children
                                      [ UI.primaryButton "Retry" (Some IconName.refresh) (fun _ ->
                                            dispatch (PersistenceMsg(PersistenceMsg.LoadSlot slot)))
                                        UI.ghostButton "Back to menu" (Some IconName.back) (fun _ ->
                                            dispatch (SetupMsg CloseLoadGame)) ] ] ] ]
                :> IView
            | LoadClosed -> TextBlock.create [ TextBlock.text "" ] :> IView
            | SlotsReady slots ->
                if slots.IsEmpty then
                    StackPanel.create
                        [ StackPanel.spacing 12.0
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text (Localization.resolve TranslationKey.NoSaveSlots [])
                                      TextBlock.fontSize 14.0
                                      TextBlock.foreground Theme.TextMuted ]
                                TextBlock.create
                                    [ TextBlock.text
                                          "Create a career first; empty database files are not shown as slots."
                                      TextBlock.foreground Theme.TextDim
                                      TextBlock.textWrapping TextWrapping.Wrap ] ] ]
                    :> IView
                else
                    StackPanel.create
                        [ StackPanel.spacing 8.0
                          StackPanel.children (slots |> List.map (fun slot -> saveSlotButton slot dispatch)) ]
                    :> IView

        UI.sectionContainer "SAVED CAREERS" body

    let setupView (state: State) (dispatch: Msg -> unit) : IView =
        match state.Setup.Step with
        | MainMenu ->
            let menuItems =
                [ UI.menuButton
                      "NEW CAREER"
                      NotificationIcons.newCareer
                      "Choose country, league, manager and club"
                      (fun _ -> dispatch (SetupMsg(GoToStep CountrySelection)))
                  :> IView
                  UI.menuButton
                      "LOAD GAME"
                      NotificationIcons.loadGame
                      "Inspect compatible and damaged save slots"
                      (fun _ -> dispatch (SetupMsg OpenLoadGame))
                  :> IView
                  UI.menuButton "MOD EDITOR" IconName.fileDocumentEdit "Edit installed football data packages" (fun _ ->
                      dispatch (Navigate Route.ModEditor))
                  :> IView ]

            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 18.0
                      StackPanel.children
                          [ stepDivider "Start"
                            if not state.ModLoadErrors.IsEmpty then
                                Border.create
                                    [ Border.background (Theme.withAlpha "14" Theme.Danger)
                                      Border.borderBrush (Theme.withAlpha "40" Theme.Danger)
                                      Border.borderThickness 1.0
                                      Border.cornerRadius Theme.RadiusMd
                                      Border.padding (12.0, 10.0)
                                      Border.child (
                                          StackPanel.create
                                              [ StackPanel.spacing 4.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text "World data is not ready"
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.Danger ]
                                                      for error in state.ModLoadErrors do
                                                          TextBlock.create
                                                              [ TextBlock.text error
                                                                TextBlock.foreground Theme.Danger
                                                                TextBlock.textWrapping TextWrapping.Wrap ] ] ]
                                      ) ]
                            heading
                                "Start a career"
                                "Build a manager profile first. No world is generated until the final confirmation."
                            UniformGrid.create [ UniformGrid.columns 2; UniformGrid.children menuItems ] ] ]
            )
        | CountrySelection ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 20.0
                      StackPanel.children
                          [ stepDivider "Step 1 of 5 - Country and league"
                            heading
                                "Choose the football world"
                                "The selected country and leagues come from the installed data snapshot."
                            StackPanel.create
                                [ StackPanel.spacing 8.0
                                  StackPanel.children
                                      [ for country in materializedCountries state |> List.sortBy _.Country.Name do
                                            countryCard state dispatch country ] ]
                            leagueChoices state dispatch
                            navRow
                                (Some MainMenu)
                                (if state.Setup.SelectedLeagueLevel.IsSome then
                                     Some("Manager details", IconName.next, GoToStep ManagerNaming)
                                 else
                                     None)
                                dispatch ] ]
            )
        | ManagerNaming ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 20.0
                      StackPanel.children
                          [ stepDivider "Step 2 of 5 - Manager"
                            heading "Define the manager" "These choices remain a draft until you create the career."
                            UI.sectionContainer
                                "MANAGER NAME"
                                (TextBox.create
                                    [ TextBox.text state.Setup.ManagerName
                                      TextBox.onTextChanged (fun name -> dispatch (SetupMsg(UpdateManagerName name)))
                                      TextBox.padding (14.0, 12.0)
                                      TextBox.fontSize 16.0
                                      TextBox.background Theme.BgCard
                                      TextBox.borderThickness 0.0 ])
                            UI.sectionContainer "MANAGEMENT STYLE" (styleChoices state dispatch)
                            navRow
                                (Some CountrySelection)
                                (if state.Setup.ManagerName.Trim().Length >= 2 then
                                     Some("Choose club", IconName.next, GoToStep ClubSelection)
                                 else
                                     None)
                                dispatch ] ]
            )
        | ClubSelection ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 20.0
                      StackPanel.children
                          [ stepDivider "Step 3 of 5 - Club"
                            heading
                                "Choose your club"
                                "Clubs are listed from the selected league; no generated world exists yet."
                            selectedClubChoices state dispatch
                            navRow
                                (Some ManagerNaming)
                                (if state.Setup.SelectedClubName.IsSome then
                                     Some("Optional settings", IconName.next, GoToStep OptionalSettings)
                                 else
                                     None)
                                dispatch ] ]
            )
        | OptionalSettings ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 20.0
                      StackPanel.children
                          [ stepDivider "Step 4 of 5 - Optional settings"
                            heading
                                "Set the starting context"
                                "Defaults keep the engine unchanged. Difficulty is applied only when the career is created."
                            UI.sectionContainer "DIFFICULTY" (difficultyChoices state dispatch)
                            match selectedCountryData state with
                            | Some country ->
                                UI.sectionContainer
                                    "DATA AND RULES"
                                    (StackPanel.create
                                        [ StackPanel.spacing 6.0
                                          StackPanel.children
                                              [ TextBlock.create
                                                    [ TextBlock.text (
                                                          $"{country.Country.Name}: {country.LeagueNames.Length} league levels, "
                                                          + $"{country.Clubs.Length} clubs, {country.LeagueRules.Length} rule sets"
                                                      )
                                                      TextBlock.foreground Theme.TextMain
                                                      TextBlock.textWrapping TextWrapping.Wrap ]
                                                TextBlock.create
                                                    [ TextBlock.text
                                                          "These are read-only facts from the resolved data snapshot."
                                                      TextBlock.foreground Theme.TextMuted ] ] ])
                            | None -> ()
                            navRow
                                (Some ClubSelection)
                                (Some("Review career", IconName.next, GoToStep Summary))
                                dispatch ] ]
            )
        | Summary ->
            let countryName =
                selectedCountryData state
                |> Option.map (fun c -> c.Country.Name)
                |> Option.defaultValue "Not selected"

            let leagueName =
                match selectedCountryData state, state.Setup.SelectedLeagueLevel with
                | Some country, Some level ->
                    country.LeagueNames |> List.tryItem level |> Option.defaultValue "Not selected"
                | _ -> "Not selected"

            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 20.0
                      StackPanel.children
                          [ stepDivider "Step 5 of 5 - Confirmation"
                            heading
                                "Review before creating"
                                "Only Create career generates the world and writes the first save."
                            Border.create
                                [ Border.background Theme.BgCard
                                  Border.borderBrush Theme.Border
                                  Border.borderThickness 1.0
                                  Border.cornerRadius 10.0
                                  Border.padding 18.0
                                  Border.child (
                                      StackPanel.create
                                          [ StackPanel.spacing 12.0
                                            StackPanel.children
                                                [ summaryRow "Country" countryName
                                                  summaryRow "League" leagueName
                                                  summaryRow
                                                      "Manager"
                                                      (if state.Setup.ManagerName.Trim().Length = 0 then
                                                           "Not selected"
                                                       else
                                                           state.Setup.ManagerName.Trim())
                                                  summaryRow
                                                      "Style"
                                                      (state.Setup.ManagementStyle
                                                       |> Option.map styleLabel
                                                       |> Option.defaultValue "Default")
                                                  summaryRow
                                                      "Club"
                                                      (state.Setup.SelectedClubName
                                                       |> Option.defaultValue "Not selected")
                                                  summaryRow "Difficulty" (difficultyLabel state.Setup.Difficulty) ] ]
                                  ) ]
                            navRow
                                (Some OptionalSettings)
                                (Some("Create career", IconName.success, StartNewGame))
                                dispatch ] ]
            )
        | LoadGame ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 20.0
                      StackPanel.children
                          [ stepDivider "Load career"
                            heading
                                "Load a saved career"
                                "Choose a slot to validate and load. Corrupt slots remain visible with their diagnostics."
                            loadPanel state dispatch
                            navRow None None dispatch ] ]
            )
