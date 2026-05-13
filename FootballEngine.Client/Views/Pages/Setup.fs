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
open FootballEngine.Domain
open FootballEngine.Icons

module Setup =

    let setupView (state: State) dispatch =

        let setupContainer (content: IView) =
            Grid.create
                [ Grid.background Theme.BgSidebar
                  Grid.children
                      [ ScrollViewer.create
                            [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                              ScrollViewer.content (
                                  StackPanel.create
                                      [ StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                                        StackPanel.maxWidth 860.0
                                        StackPanel.minWidth 560.0
                                        StackPanel.margin (48.0, 56.0)
                                        StackPanel.spacing 48.0
                                        StackPanel.children
                                            [ Border.create
                                                  [ Border.child (
                                                        StackPanel.create
                                                            [ StackPanel.spacing 4.0
                                                              StackPanel.children
                                                                  [ TextBlock.create
                                                                        [ TextBlock.text "FOOTBALL ENGINE"
                                                                          TextBlock.fontSize 44.0
                                                                          TextBlock.fontWeight FontWeight.Black
                                                                          TextBlock.foreground Theme.TextMain
                                                                          TextBlock.lineSpacing 6.0
                                                                          TextBlock.horizontalAlignment
                                                                              HorizontalAlignment.Center ]
                                                                    TextBlock.create
                                                                        [ TextBlock.text "2026"
                                                                          TextBlock.fontSize 13.0
                                                                          TextBlock.foreground Theme.Accent
                                                                          TextBlock.lineSpacing 8.0
                                                                          TextBlock.fontWeight FontWeight.SemiBold
                                                                          TextBlock.horizontalAlignment
                                                                              HorizontalAlignment.Center ] ] ]
                                                    ) ]
                                              content ] ]
                              ) ] ] ]

        let stepDivider (label: string) =
            DockPanel.create
                [ DockPanel.margin (0.0, 0.0, 0.0, 4.0)
                  DockPanel.children
                      [ TextBlock.create
                            [ DockPanel.dock Dock.Right
                              TextBlock.text (label.ToUpperInvariant())
                              TextBlock.fontSize 10.0
                              TextBlock.fontWeight FontWeight.Bold
                              TextBlock.foreground Theme.TextMuted
                              TextBlock.lineSpacing 2.0
                              TextBlock.verticalAlignment VerticalAlignment.Center ]
                        Border.create
                            [ Border.height 1.0
                              Border.background Theme.Border
                              Border.verticalAlignment VerticalAlignment.Center
                              Border.margin (0.0, 0.0, 12.0, 0.0) ] ] ]

        let navRow (backStep: SetupStep option) (nextView: IView option) =
            DockPanel.create
                [ DockPanel.margin (0.0, 8.0, 0.0, 0.0)
                  DockPanel.children
                      [ match backStep with
                        | Some step -> UI.ghostButton "← Back" None (fun _ -> dispatch (SetupMsg(GoToStep step)))
                        | None -> ()
                        match nextView with
                        | Some v -> Border.create [ DockPanel.dock Dock.Right; Border.child v ]
                        | None -> () ] ]

        match state.Setup.Step with

        | MainMenu ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 16.0
                      StackPanel.children
                          [ stepDivider "Start"
                            UniformGrid.create
                                [ UniformGrid.columns 2
                                  UniformGrid.children
                                      [ UI.menuButton
                                            "NEW CAREER"
                                            NotificationIcons.newCareer
                                            "Start a new journey as a manager"
                                            (fun _ -> dispatch (SetupMsg(GoToStep CountrySelection)))
                                        UI.menuButton
                                            "LOAD GAME"
                                            NotificationIcons.loadGame
                                            "Continue your existing career"
                                            (fun _ -> dispatch (SimMsg SaveGame)) ] ] ] ]
            )

        | CountrySelection ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 24.0
                      StackPanel.children
                          [ stepDivider "Step 1 of 3 — Leagues"

                            StackPanel.create
                                [ StackPanel.spacing 6.0
                                  StackPanel.children
                                      [ TextBlock.create
                                            [ TextBlock.text "Select Countries & Leagues"
                                              TextBlock.fontSize 22.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground Theme.TextMain ]
                                        TextBlock.create
                                            [ TextBlock.text
                                                  "Pick one primary country to manage in. Add others to simulate alongside."
                                              TextBlock.foreground Theme.TextMuted
                                              TextBlock.fontSize 13.0 ] ] ]

                            let countryCount = Data.DataRegistry.allCountries () |> List.length
                            printfn $"[Setup] CountrySelection rendering: %d{countryCount} countries"

                            let flagForCode (code: string) =
                                match code with
                                | "ARG" -> "🇦🇷"
                                | "BRA" -> "🇧🇷"
                                | "ENG" -> "🏴"
                                | "ESP" -> "🇪🇸"
                                | _ -> "🌍"

                            let countryCards: IView list =
                                Data.DataRegistry.allCountries ()
                                |> List.sortBy _.Country.Name
                                |> List.map (fun cd ->
                                    let code = cd.Country.Code
                                    let name = cd.Country.Name
                                    let flag = flagForCode code

                                    UI.countrySelectionCard
                                        name
                                        flag
                                        (state.Setup.SelectedCountry = Some code)
                                        (state.Setup.SecondaryCountries |> List.contains code)
                                        (fun _ -> dispatch (SetupMsg(SelectPrimaryCountry code)))
                                        (fun _ -> dispatch (SetupMsg(ToggleSecondaryCountry code)))
                                    :> IView)

                            StackPanel.create [ StackPanel.spacing 8.0; StackPanel.children countryCards ]

                            navRow
                                (Some MainMenu)
                                (if state.Setup.SelectedCountry.IsSome then
                                     Some(
                                         UI.primaryButton "Manager Details" (Some IconName.next) (fun _ ->
                                             dispatch (SetupMsg(GoToStep ManagerNaming)))
                                         :> IView
                                     )
                                 else
                                     None) ] ]
            )

        | ManagerNaming ->
            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 24.0
                      StackPanel.children
                          [ stepDivider "Step 2 of 3 — Manager"

                            StackPanel.create
                                [ StackPanel.spacing 6.0
                                  StackPanel.children
                                      [ TextBlock.create
                                            [ TextBlock.text "Manager Details"
                                              TextBlock.fontSize 22.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground Theme.TextMain ]
                                        TextBlock.create
                                            [ TextBlock.text "Your name will appear throughout the game."
                                              TextBlock.foreground Theme.TextMuted
                                              TextBlock.fontSize 13.0 ] ] ]

                            UI.sectionContainer
                                "YOUR NAME"
                                (TextBox.create
                                    [ TextBox.text state.Setup.ManagerName
                                      TextBox.onTextChanged (fun n -> dispatch (SetupMsg(UpdateManagerName n)))
                                      TextBox.padding (16.0, 14.0)
                                      TextBox.fontSize 17.0
                                      TextBox.background Theme.BgCard
                                      TextBox.borderThickness 0.0 ])

                            Border.create
                                [ Border.background Theme.BgCard
                                  Border.cornerRadius 10.0
                                  Border.borderBrush Theme.Border
                                  Border.borderThickness 1.0
                                  Border.padding (16.0, 14.0)
                                  Border.child (
                                      Grid.create
                                          [ Grid.columnDefinitions "Auto, *"
                                            Grid.children
                                                [ Border.create
                                                      [ Grid.column 0
                                                        Border.background Theme.AccentLight
                                                        Border.cornerRadius 8.0
                                                        Border.padding 10.0
                                                        Border.margin (0.0, 0.0, 14.0, 0.0)
                                                        Border.verticalAlignment VerticalAlignment.Center
                                                        Border.child (Icons.iconLg IconName.league Theme.Accent) ]
                                                  StackPanel.create
                                                      [ Grid.column 1
                                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                                        StackPanel.spacing 3.0
                                                        StackPanel.children
                                                            [ TextBlock.create
                                                                  [ TextBlock.text "PRIMARY NATION"
                                                                    TextBlock.fontSize 10.0
                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                    TextBlock.foreground Theme.TextMuted
                                                                    TextBlock.lineSpacing 1.5 ]
                                                              TextBlock.create
                                                                  [ TextBlock.text (
                                                                        state.Setup.SelectedCountry
                                                                        |> Option.defaultValue "—"
                                                                    )
                                                                    TextBlock.fontSize 18.0
                                                                    TextBlock.fontWeight FontWeight.Black
                                                                    TextBlock.foreground Theme.TextMain ] ] ] ] ]
                                  ) ]

                            navRow
                                (Some CountrySelection)
                                (if state.Setup.ManagerName.Length > 2 then
                                     Some(
                                         UI.primaryButton "Confirm & Create Game" (Some IconName.success) (fun _ ->
                                             dispatch (SetupMsg StartNewGame))
                                         :> IView
                                     )
                                 else
                                     None) ] ]
            )

        | ClubSelection ->
            let clubs =
                match state.Mode with
                | InGame(gs, _) ->
                    gs.Competitions
                    |> Map.tryPick (fun _ (comp: Competition) ->
                        match comp.Type, comp.Country with
                        | NationalLeague(LeagueLevel 0, _), Some country when
                            country = state.Setup.SelectedCountry.Value
                            ->
                            Some comp
                        | _ -> None)
                    |> Option.map (fun comp -> comp.ClubIds |> List.map (fun id -> gs.Clubs[id]))
                    |> Option.defaultValue []
                    |> List.sortByDescending _.Reputation
                | _ -> []

            setupContainer (
                StackPanel.create
                    [ StackPanel.spacing 24.0
                      StackPanel.children
                          [ stepDivider "Step 3 of 3 — Club"

                            StackPanel.create
                                [ StackPanel.spacing 6.0
                                  StackPanel.children
                                      [ TextBlock.create
                                            [ TextBlock.text "Choose Your Club"
                                              TextBlock.fontSize 22.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground Theme.TextMain ]
                                        TextBlock.create
                                            [ TextBlock.text "Select the club you want to manage this season."
                                              TextBlock.foreground Theme.TextMuted
                                              TextBlock.fontSize 13.0 ] ] ]

                            Border.create
                                [ Border.background Theme.BgCard
                                  Border.cornerRadius 10.0
                                  Border.borderBrush Theme.Border
                                  Border.borderThickness 1.0
                                  Border.clipToBounds true
                                  Border.child (
                                      ScrollViewer.create
                                          [ ScrollViewer.maxHeight 440.0
                                            ScrollViewer.content (
                                                StackPanel.create
                                                    [ StackPanel.children
                                                          [ for i, club in clubs |> List.indexed do
                                                                Button.create
                                                                    [ Button.horizontalAlignment
                                                                          HorizontalAlignment.Stretch
                                                                      Button.padding (18.0, 14.0)
                                                                      Button.background "Transparent"
                                                                      Button.borderThickness (
                                                                          if i < clubs.Length - 1 then
                                                                              Avalonia.Thickness(0.0, 0.0, 0.0, 1.0)
                                                                          else
                                                                              Avalonia.Thickness(0.0)
                                                                      )
                                                                      Button.borderBrush Theme.Border
                                                                      Button.onClick (fun _ ->
                                                                          dispatch (SetupMsg(ConfirmClub club.Id)))
                                                                      Button.content (
                                                                          Grid.create
                                                                              [ Grid.columnDefinitions
                                                                                    "Auto, *, Auto, Auto"
                                                                                Grid.children
                                                                                    [ Border.create
                                                                                          [ Grid.column 0
                                                                                            Border.width 36.0
                                                                                            Border.height 36.0
                                                                                            Border.cornerRadius 18.0
                                                                                            Border.background
                                                                                                Theme.AccentLight
                                                                                            Border.margin (
                                                                                                0.0,
                                                                                                0.0,
                                                                                                14.0,
                                                                                                0.0
                                                                                            )
                                                                                            Border.verticalAlignment
                                                                                                VerticalAlignment.Center
                                                                                            Border.child (
                                                                                                TextBlock.create
                                                                                                    [ TextBlock.text (
                                                                                                          string
                                                                                                              club.Name[0]
                                                                                                      )
                                                                                                      TextBlock.fontSize
                                                                                                          14.0
                                                                                                      TextBlock.fontWeight
                                                                                                          FontWeight.Black
                                                                                                      TextBlock.foreground
                                                                                                          Theme.Accent
                                                                                                      TextBlock.horizontalAlignment
                                                                                                          HorizontalAlignment.Center
                                                                                                      TextBlock.verticalAlignment
                                                                                                          VerticalAlignment.Center ]
                                                                                            ) ]
                                                                                      StackPanel.create
                                                                                          [ Grid.column 1
                                                                                            StackPanel.verticalAlignment
                                                                                                VerticalAlignment.Center
                                                                                            StackPanel.children
                                                                                                [ TextBlock.create
                                                                                                      [ TextBlock.text
                                                                                                            club.Name
                                                                                                        TextBlock.fontWeight
                                                                                                            FontWeight.SemiBold
                                                                                                        TextBlock.fontSize
                                                                                                            14.0
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextMain ] ] ]
                                                                                      StackPanel.create
                                                                                          [ Grid.column 2
                                                                                            StackPanel.orientation
                                                                                                Orientation.Horizontal
                                                                                            StackPanel.spacing 4.0
                                                                                            StackPanel.verticalAlignment
                                                                                                VerticalAlignment.Center
                                                                                            StackPanel.margin (
                                                                                                0.0,
                                                                                                0.0,
                                                                                                16.0,
                                                                                                0.0
                                                                                            )
                                                                                            StackPanel.children
                                                                                                [ Icons.iconSm
                                                                                                      PlayerIcon.skill
                                                                                                      Theme.Warning
                                                                                                  TextBlock.create
                                                                                                      [ TextBlock.text (
                                                                                                            string
                                                                                                                club.Reputation
                                                                                                        )
                                                                                                        TextBlock.fontSize
                                                                                                            12.0
                                                                                                        TextBlock.fontWeight
                                                                                                            FontWeight.Bold
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextMuted
                                                                                                        TextBlock.verticalAlignment
                                                                                                            VerticalAlignment.Center ] ] ]
                                                                                      Border.create
                                                                                          [ Grid.column 3
                                                                                            Border.verticalAlignment
                                                                                                VerticalAlignment.Center
                                                                                            Border.child (
                                                                                                Icons.iconMd
                                                                                                    Nav.next
                                                                                                    Theme.TextMuted
                                                                                            ) ] ] ]
                                                                      ) ] ] ]
                                            ) ]
                                  ) ]

                            navRow None None ] ]
            )
