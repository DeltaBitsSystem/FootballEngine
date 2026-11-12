namespace FootballEngine.Pages

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Data
open FootballEngine.ModEditorTypes
open FootballEngine.Icons
open FootballEngine.Components


module ModEditor =

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// Label + input stacked with consistent spacing — replaces the scattered
    /// TextBlock / TextBox pairs throughout the original.
    let private field (label: string) (input: IView) : IView =
        StackPanel.create
            [ StackPanel.spacing 4.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text (label.ToUpperInvariant())
                          TextBlock.fontSize 10.0
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground Theme.TextSub ]
                    input ] ]

    /// Two-column field row using a Grid.
    let private fieldRow (left: IView) (right: IView) : IView =
        Grid.create
            [ Grid.columnDefinitions "*, 16, *"
              Grid.children
                  [ Border.create [ Grid.column 0; Border.child left ]
                    Border.create [ Grid.column 2; Border.child right ] ] ]

    /// Destructive action — outlined red button, smaller than primary.
    let private dangerButton (label: string) onClick =
        Button.create
            [ Button.content label
              Button.foreground Theme.Danger
              Button.background "Transparent"
              Button.borderBrush (Theme.withAlpha "55" Theme.Danger)
              Button.borderThickness 1.0
              Button.padding (12.0, 6.0)
              Button.cornerRadius 6.0
              Button.fontSize 12.0
              Button.onClick onClick ]

    let private pageIntro
        (iconKind: Material.Icons.MaterialIconKind)
        (eyebrow: string)
        (title: string)
        (description: string)
        : IView =
        let eyebrowText = eyebrow.ToUpperInvariant()

        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.BorderLight
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (20.0, 18.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 7.0
                        StackPanel.children
                            [ StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children
                                        [ Border.create
                                              [ Border.background Theme.AccentLight
                                                Border.cornerRadius Theme.RadiusSm
                                                Border.padding 7.0
                                                Border.child (Icons.iconMd iconKind Theme.Accent) ]
                                          TextBlock.create
                                              [ (TextBlock.text eyebrowText)
                                                (TextBlock.fontSize 10.0)
                                                (TextBlock.fontWeight FontWeight.Black)
                                                (TextBlock.foreground Theme.Accent)
                                                (TextBlock.verticalAlignment VerticalAlignment.Center) ] ] ]
                              TextBlock.create
                                  [ TextBlock.text title
                                    TextBlock.fontSize 24.0
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ TextBlock.text description
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.textWrapping TextWrapping.Wrap ] ] ]
              ) ]

    /// Stat card shown in the Overview header.
    let private statPill
        (iconKind: Material.Icons.MaterialIconKind)
        (label: string)
        (value: string)
        (description: string)
        =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (14.0, 12.0)
              Border.width 190.0
              Border.margin (0.0, 0.0, 10.0, 0.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, *"
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Border.background Theme.AccentLight
                                    Border.cornerRadius Theme.RadiusSm
                                    Border.padding 8.0
                                    Border.margin (0.0, 0.0, 12.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (Icons.iconMd iconKind Theme.Accent) ]
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.spacing 2.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text value
                                                TextBlock.fontSize 21.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMain ]
                                          TextBlock.create
                                              [ TextBlock.text (label.ToUpperInvariant())
                                                TextBlock.fontSize 9.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground Theme.TextSub ]
                                          TextBlock.create
                                              [ TextBlock.text description
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground Theme.TextDim
                                                TextBlock.textWrapping TextWrapping.Wrap ] ] ] ] ]
              ) ]

    let private newCountryCode (countries: Map<string, CountryDataDto>) =
        let candidates =
            [ "NEW" ]
            @ [ for first in 'A' .. 'Z' do
                    for second in 'A' .. 'Z' do
                        yield $"N{first}{second}" ]
            @ [ "ZZZ" ]

        candidates
        |> List.tryFind (fun code -> not (countries |> Map.containsKey code))
        |> Option.defaultValue "ZZZ"

    let private editorStatus (state: ModEditorState) =
        if state.IsDirty then
            "UNSAVED CHANGES", Theme.Warning
        else
            match state.ExportStatus with
            | Exported -> "EXPORTED", Theme.Success
            | ExportFailed _ -> "EXPORT FAILED", Theme.Danger
            | NotExported -> "NOT EXPORTED", Theme.TextMuted

    let private statusChip (label: string) (color: string) =
        Border.create
            [ Border.background (Theme.withAlpha "18" color)
              Border.borderBrush (Theme.withAlpha "55" color)
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusPill
              Border.padding (9.0, 4.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text label
                        TextBlock.fontSize 9.0
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground color ]
              ) ]

    // ─── Tab bar ──────────────────────────────────────────────────────────────

    let private topTab activeTab tab iconKind label dispatch =
        let isActive = activeTab = tab

        Button.create
            [ Button.onClick (fun _ -> dispatch (ModEditorMsg(SetTab tab)))
              Button.background "Transparent"
              Button.foreground (if isActive then Theme.Accent else Theme.TextSub)
              Button.borderBrush (if isActive then Theme.Accent else "Transparent")
              Button.borderThickness (0.0, 0.0, 0.0, 2.0)
              Button.padding (16.0, 10.0)
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 7.0
                        StackPanel.children
                            [ Icons.iconSm iconKind (if isActive then Theme.Accent else Theme.TextSub)
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight (if isActive then FontWeight.SemiBold else FontWeight.Normal) ] ] ]
              ) ]

    let private editorHeader (state: ModEditorState) dispatch =
        let statusText, statusColor = editorStatus state

        let modName =
            if String.IsNullOrWhiteSpace state.Manifest.Name then
                "Untitled mod"
            else
                state.Manifest.Name

        Border.create
            [ DockPanel.dock Dock.Top
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (20.0, 14.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ StackPanel.create
                                  [ DockPanel.dock Dock.Left
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.children
                                        [ Border.create
                                              [ Border.background Theme.AccentLight
                                                Border.borderBrush (Theme.withAlpha "55" Theme.Accent)
                                                Border.borderThickness 1.0
                                                Border.cornerRadius Theme.RadiusMd
                                                Border.padding 9.0
                                                Border.child (Icons.iconLg IconName.fileDocumentEdit Theme.Accent) ]
                                          StackPanel.create
                                              [ StackPanel.spacing 2.0
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text "MOD WORKSHOP"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Black
                                                            TextBlock.foreground Theme.Accent ]
                                                      TextBlock.create
                                                          [ TextBlock.text modName
                                                            TextBlock.fontSize 18.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMain ]
                                                      TextBlock.create
                                                          [ TextBlock.text
                                                                $"{state.Manifest.Id}  ·  v{state.Manifest.Version}"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.foreground Theme.TextDim ] ] ] ] ]
                              StackPanel.create
                                  [ DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ UI.ghostButton "Back to menu" (Some IconName.back) (fun _ -> dispatch GoBack)
                                          statusChip statusText statusColor
                                          UI.ghostButton "Validate" (Some IconName.success) (fun _ ->
                                              dispatch (ModEditorMsg Validate))
                                          UI.primaryButton "Export" (Some IconName.save) (fun _ ->
                                              dispatch (ModEditorMsg ModEditorMsg.Export)) ] ] ] ]
              ) ]

    // ─── Overview tab ─────────────────────────────────────────────────────────

    let private overviewTab (state: ModEditorState) dispatch =
        let totalClubs =
            state.Countries |> Map.toSeq |> Seq.sumBy (fun (_, c) -> c.Clubs |> List.length)

        let totalLeagues =
            state.Countries
            |> Map.toSeq
            |> Seq.sumBy (fun (_, c) -> c.LeagueNames |> List.length)

        let splitLines (text: string) =
            text.Split([| '\n'; '\r'; ',' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun value -> value.Trim())
            |> Array.filter (fun value -> value <> "")
            |> Array.distinct
            |> Array.toList

        StackPanel.create
            [ StackPanel.spacing 18.0
              StackPanel.children
                  [ pageIntro
                        IconName.fileDocumentEdit
                        "MOD PROFILE"
                        "Shape the world you want to play"
                        "Define the package identity first, then move through the content tabs to build countries and competitions."

                    ScrollViewer.create
                        [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                          ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Disabled
                          ScrollViewer.content (
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.children
                                        [ statPill
                                              IconName.home
                                              "Countries"
                                              (string (state.Countries |> Map.count))
                                              "editable datasets"
                                          statPill IconName.league "Leagues" (string totalLeagues) "competition rules"
                                          statPill IconName.squad "Clubs" (string totalClubs) "teams in the world"
                                          statPill
                                              IconName.league
                                              "International"
                                              (string state.InternationalComps.Length)
                                              "global competitions" ] ]
                          ) ]

                    UI.sectionContainer
                        "Manifest identity"
                        (StackPanel.create
                            [ StackPanel.spacing 14.0
                              StackPanel.margin (Thickness 16.0)
                              StackPanel.children
                                  [ fieldRow
                                        (field
                                            "Name"
                                            (TextBox.create
                                                [ TextBox.text state.Manifest.Name
                                                  TextBox.onTextChanged (fun t ->
                                                      dispatch (
                                                          ModEditorMsg(UpdateManifest(fun m -> { m with Name = t }))
                                                      )) ]))
                                        (field
                                            "Author"
                                            (TextBox.create
                                                [ TextBox.text state.Manifest.Author
                                                  TextBox.onTextChanged (fun t ->
                                                      dispatch (
                                                          ModEditorMsg(UpdateManifest(fun m -> { m with Author = t }))
                                                      )) ]))
                                    fieldRow
                                        (field
                                            "Package ID"
                                            (TextBox.create
                                                [ TextBox.text state.Manifest.Id
                                                  TextBox.placeholderText "community.my-mod"
                                                  TextBox.onTextChanged (fun t ->
                                                      dispatch (
                                                          ModEditorMsg(UpdateManifest(fun m -> { m with Id = t }))
                                                      )) ]))
                                        (field
                                            "Version"
                                            (TextBox.create
                                                [ TextBox.text state.Manifest.Version
                                                  TextBox.placeholderText "1.0.0"
                                                  TextBox.onTextChanged (fun t ->
                                                      dispatch (
                                                          ModEditorMsg(UpdateManifest(fun m -> { m with Version = t }))
                                                      )) ]))
                                    fieldRow
                                        (field
                                            "Priority"
                                            (NumericUpDown.create
                                                [ NumericUpDown.value (decimal state.Manifest.Priority)
                                                  NumericUpDown.onValueChanged (fun value ->
                                                      let priority =
                                                          value
                                                          |> Option.ofNullable
                                                          |> Option.map int
                                                          |> Option.defaultValue 0

                                                      dispatch (
                                                          ModEditorMsg(
                                                              UpdateManifest(fun m -> { m with Priority = priority })
                                                          )
                                                      )) ]))
                                        (field
                                            "Merge Strategy"
                                            (ComboBox.create
                                                [ ComboBox.dataItems [ "Replace"; "Append"; "Patch" ]
                                                  ComboBox.selectedItem (
                                                      state.Manifest.MergeStrategy |> Option.defaultValue "Append"
                                                  )
                                                  ComboBox.onSelectedItemChanged (fun obj ->
                                                      match obj with
                                                      | :? string as s ->
                                                          dispatch (
                                                              ModEditorMsg(
                                                                  UpdateManifest(fun m ->
                                                                      { m with MergeStrategy = Some s })
                                                              )
                                                          )
                                                      | _ -> ()) ]))
                                    field
                                        "Dependencies (one per line)"
                                        (TextBox.create
                                            [ TextBox.text (String.concat "\n" state.Manifest.Dependencies)
                                              TextBox.acceptsReturn true
                                              TextBox.height 62.0
                                              TextBox.placeholderText "Optional package IDs"
                                              TextBox.onTextChanged (fun text ->
                                                  dispatch (
                                                      ModEditorMsg(
                                                          UpdateManifest(fun m ->
                                                              { m with
                                                                  Dependencies = splitLines text })
                                                      )
                                                  )) ])
                                    field
                                        "Description"
                                        (TextBox.create
                                            [ TextBox.text state.Manifest.Description
                                              TextBox.acceptsReturn true
                                              TextBox.height 80.0
                                              TextBox.onTextChanged (fun t ->
                                                  dispatch (
                                                      ModEditorMsg(UpdateManifest(fun m -> { m with Description = t }))
                                                  )) ]) ] ]) ] ]

    // ─── Country sidebar ──────────────────────────────────────────────────────

    let private countrySidebar (state: ModEditorState) dispatch =
        let filtered =
            state.Countries
            |> Map.toList
            |> List.map snd
            |> List.filter (fun c ->
                state.SearchQuery = ""
                || c.Country.Name.ToLower().Contains(state.SearchQuery.ToLower())
                || c.Country.Code.ToLower().Contains(state.SearchQuery.ToLower()))

        let listView: IView =
            if filtered.IsEmpty then
                UI.emptyState IconName.search "No countries found" "Try another search or add a new country."
            else
                ListBox.create
                    [ ListBox.background "Transparent"
                      ListBox.dataItems filtered
                      ListBox.selectedItem (
                          state.SelectedCountryCode
                          |> Option.bind (fun code -> state.Countries |> Map.tryFind code)
                      )
                      ListBox.onSelectedItemChanged (fun item ->
                          match item with
                          | :? CountryDataDto as c -> dispatch (ModEditorMsg(SelectCountry(Some c.Country.Code)))
                          | _ -> ())
                      ListBox.itemTemplate (
                          DataTemplateView<CountryDataDto>.create (fun c ->
                              Border.create
                                  [ Border.background Theme.BgSubtle
                                    Border.cornerRadius Theme.RadiusSm
                                    Border.padding (8.0, 7.0)
                                    Border.margin (2.0, 2.0)
                                    Border.child (
                                        DockPanel.create
                                            [ DockPanel.lastChildFill true
                                              DockPanel.children
                                                  [ StackPanel.create
                                                        [ DockPanel.dock Dock.Right
                                                          StackPanel.spacing 1.0
                                                          StackPanel.children
                                                              [ TextBlock.create
                                                                    [ TextBlock.text $"{c.Clubs.Length} clubs"
                                                                      TextBlock.fontSize 9.0
                                                                      TextBlock.foreground Theme.TextDim
                                                                      TextBlock.horizontalAlignment
                                                                          HorizontalAlignment.Right ]
                                                                TextBlock.create
                                                                    [ TextBlock.text $"{c.LeagueNames.Length} leagues"
                                                                      TextBlock.fontSize 9.0
                                                                      TextBlock.foreground Theme.TextDim
                                                                      TextBlock.horizontalAlignment
                                                                          HorizontalAlignment.Right ] ] ]
                                                    StackPanel.create
                                                        [ StackPanel.orientation Orientation.Horizontal
                                                          StackPanel.spacing 9.0
                                                          StackPanel.verticalAlignment VerticalAlignment.Center
                                                          StackPanel.children
                                                              [ Border.create
                                                                    [ Border.background Theme.AccentLight
                                                                      Border.cornerRadius Theme.RadiusXs
                                                                      Border.padding (5.0, 2.0)
                                                                      Border.child (
                                                                          TextBlock.create
                                                                              [ TextBlock.text c.Country.Code
                                                                                TextBlock.fontSize 10.0
                                                                                TextBlock.fontWeight FontWeight.Bold
                                                                                TextBlock.foreground Theme.Accent ]
                                                                      ) ]
                                                                TextBlock.create
                                                                    [ TextBlock.text c.Country.Name
                                                                      TextBlock.fontSize 13.0
                                                                      TextBlock.foreground Theme.TextMain
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center ] ] ] ] ]
                                    ) ])
                      ) ]
                :> IView

        let countText =
            if filtered.Length = state.Countries.Count then
                $"%d{state.Countries.Count} countries"
            else
                $"%d{filtered.Length} of %d{state.Countries.Count} countries"

        let searchPanel =
            StackPanel.create
                [ DockPanel.dock Dock.Top
                  StackPanel.margin (16.0, 16.0, 16.0, 10.0)
                  StackPanel.spacing 9.0
                  StackPanel.children
                      [ StackPanel.create
                            [ StackPanel.spacing 2.0
                              StackPanel.children
                                  [ TextBlock.create
                                        [ TextBlock.text "COUNTRY CATALOG"
                                          TextBlock.fontSize 10.0
                                          TextBlock.fontWeight FontWeight.Black
                                          TextBlock.foreground Theme.Accent ]
                                    TextBlock.create
                                        [ TextBlock.text "Build the playable world"
                                          TextBlock.fontSize 12.0
                                          TextBlock.foreground Theme.TextSub ] ] ]
                        TextBox.create
                            [ TextBox.placeholderText "Search countries..."
                              TextBox.text state.SearchQuery
                              TextBox.onTextChanged (fun text -> dispatch (ModEditorMsg(SearchMod text))) ]
                        UI.primaryButton "Add country" (Some IconName.add) (fun _ ->
                            let code = newCountryCode state.Countries
                            let country = UpdateModEditor.createEmptyCountry code "New country"
                            dispatch (ModEditorMsg(AddCountry country))) ] ]

        let countPanel =
            Border.create
                [ DockPanel.dock Dock.Top
                  Border.padding (16.0, 4.0, 16.0, 8.0)
                  Border.child (
                      TextBlock.create
                          [ TextBlock.text countText
                            TextBlock.fontSize 10.0
                            TextBlock.foreground Theme.TextMuted ]
                  ) ]

        Border.create
            [ Border.width 260.0
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
              Border.child (DockPanel.create [ DockPanel.children [ searchPanel; countPanel; listView ] ]) ]

    // ─── Country detail sub-tabs ──────────────────────────────────────────────

    let private generalSubTab (_state: ModEditorState) (country: CountryDataDto) dispatch =
        UI.sectionContainer
            "Country Metadata"
            (StackPanel.create
                [ StackPanel.spacing 16.0
                  StackPanel.margin (Thickness 16.0)
                  StackPanel.children
                      [ fieldRow
                            (field
                                "Name"
                                (TextBox.create
                                    [ TextBox.text country.Country.Name
                                      TextBox.onTextChanged (fun t ->
                                          dispatch (
                                              ModEditorMsg(
                                                  UpdateCountry(
                                                      country.Country.Code,
                                                      fun c ->
                                                          { c with
                                                              Country = { c.Country with Name = t } }
                                                  )
                                              )
                                          )) ]))
                            (field
                                "Code (3 letters)"
                                (TextBox.create
                                    [ TextBox.text country.Country.Code
                                      TextBox.maxLength 3
                                      TextBox.onTextChanged (fun t ->
                                          if t.Length <= 3 then
                                              dispatch (
                                                  ModEditorMsg(
                                                      UpdateCountry(
                                                          country.Country.Code,
                                                          fun c ->
                                                              { c with
                                                                  Country = { c.Country with Code = t } }
                                                      )
                                                  )
                                              )) ]))
                        field
                            "Confederation"
                            (TextBox.create
                                [ TextBox.text country.Country.Confederation
                                  TextBox.maxWidth 240.0
                                  TextBox.horizontalAlignment HorizontalAlignment.Left
                                  TextBox.onTextChanged (fun t ->
                                      dispatch (
                                          ModEditorMsg(
                                              UpdateCountry(
                                                  country.Country.Code,
                                                  fun c ->
                                                      { c with
                                                          Country = { c.Country with Confederation = t } }
                                              )
                                          )
                                      )) ])
                        Border.create
                            [ Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                              Border.padding (0.0, 12.0, 0.0, 0.0)
                              Border.child (
                                  dangerButton "Delete Country" (fun _ ->
                                      dispatch (ModEditorMsg(RemoveCountry country.Country.Code)))
                              ) ] ] ])

    let private leaguesSubTab (_state: ModEditorState) (country: CountryDataDto) dispatch =
        let updateCountry f =
            dispatch (ModEditorMsg(UpdateCountry(country.Country.Code, f)))

        UI.sectionContainer
            "Leagues"
            (StackPanel.create
                [ StackPanel.spacing 0.0
                  StackPanel.children
                      [ for i in 0 .. country.LeagueNames.Length - 1 do
                            let name = country.LeagueNames.[i]
                            let rules = country.LeagueRules.[i]

                            Border.create
                                [ Border.borderBrush Theme.Border
                                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                  Border.padding (16.0, 14.0)
                                  Border.child (
                                      StackPanel.create
                                          [ StackPanel.spacing 12.0
                                            StackPanel.children
                                                [

                                                  // ── Name row ──
                                                  DockPanel.create
                                                      [ DockPanel.children
                                                            [ dangerButton "Remove" (fun _ ->
                                                                  updateCountry (fun c ->
                                                                      { c with
                                                                          LeagueNames =
                                                                              c.LeagueNames
                                                                              |> List.indexed
                                                                              |> List.filter (fun (j, _) -> j <> i)
                                                                              |> List.map snd
                                                                          LeagueRules =
                                                                              c.LeagueRules
                                                                              |> List.indexed
                                                                              |> List.filter (fun (j, _) -> j <> i)
                                                                              |> List.map snd }))
                                                              |> fun v ->
                                                                  DockPanel.dock Dock.Right |> ignore
                                                                  v

                                                              TextBox.create
                                                                  [ TextBox.text name
                                                                    TextBox.fontWeight FontWeight.SemiBold
                                                                    TextBox.fontSize 14.0
                                                                    TextBox.onTextChanged (fun t ->
                                                                        updateCountry (fun c ->
                                                                            { c with
                                                                                LeagueNames =
                                                                                    c.LeagueNames
                                                                                    |> List.mapi (fun j n ->
                                                                                        if i = j then t else n) })) ] ] ]

                                                  // ── Points row ──
                                                  StackPanel.create
                                                      [ StackPanel.orientation Orientation.Horizontal
                                                        StackPanel.spacing 16.0
                                                        StackPanel.children
                                                            [ field
                                                                  "Points for Win"
                                                                  (NumericUpDown.create
                                                                      [ NumericUpDown.value (decimal rules.PointsForWin)
                                                                        NumericUpDown.onValueChanged (fun v ->
                                                                            let pts =
                                                                                v
                                                                                |> Option.ofNullable
                                                                                |> Option.map int
                                                                                |> Option.defaultValue 0

                                                                            updateCountry (fun c ->
                                                                                { c with
                                                                                    LeagueRules =
                                                                                        c.LeagueRules
                                                                                        |> List.mapi (fun j r ->
                                                                                            if i = j then
                                                                                                { r with
                                                                                                    PointsForWin =
                                                                                                        pts }
                                                                                            else
                                                                                                r) })) ])
                                                              field
                                                                  "Points for Draw"
                                                                  (NumericUpDown.create
                                                                      [ NumericUpDown.value (
                                                                            decimal rules.PointsForDraw
                                                                        )
                                                                        NumericUpDown.onValueChanged (fun v ->
                                                                            let pts =
                                                                                v
                                                                                |> Option.ofNullable
                                                                                |> Option.map int
                                                                                |> Option.defaultValue 0

                                                                            updateCountry (fun c ->
                                                                                { c with
                                                                                    LeagueRules =
                                                                                        c.LeagueRules
                                                                                        |> List.mapi (fun j r ->
                                                                                            if i = j then
                                                                                                { r with
                                                                                                    PointsForDraw =
                                                                                                        pts }
                                                                                            else
                                                                                                r) })) ]) ] ] ] ]
                                  ) ]

                        Border.create
                            [ Border.padding (16.0, 12.0)
                              Border.child (
                                  UI.primaryButton "Add League" (Some IconName.add) (fun _ ->
                                      updateCountry (fun c ->
                                          { c with
                                              LeagueNames = c.LeagueNames @ [ "New League" ]
                                              LeagueRules =
                                                  c.LeagueRules @ [ UpdateModEditor.createEmptyLeague "New League" ] }))
                              ) ] ] ])

    let private clubsSubTab (_state: ModEditorState) (country: CountryDataDto) dispatch =
        let updateCountry f =
            dispatch (ModEditorMsg(UpdateCountry(country.Country.Code, f)))

        UI.sectionContainer
            "Clubs"
            (StackPanel.create
                [ StackPanel.spacing 0.0
                  StackPanel.children
                      [

                        // ── Column headers ──
                        Border.create
                            [ Border.background Theme.BgMain
                              Border.padding (12.0, 8.0)
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.child (
                                  Grid.create
                                      [ Grid.columnDefinitions "*, 80, 120, 72"
                                        Grid.children
                                            [ TextBlock.create
                                                  [ Grid.column 0
                                                    TextBlock.text "CLUB NAME"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextSub ]
                                              TextBlock.create
                                                  [ Grid.column 1
                                                    TextBlock.text "DIVISION"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextSub ]
                                              TextBlock.create
                                                  [ Grid.column 2
                                                    TextBlock.text "REPUTATION"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextSub ] ] ]
                              ) ]

                        // ── Rows ──
                        for i in 0 .. country.Clubs.Length - 1 do
                            let club = country.Clubs.[i]

                            Border.create
                                [ Border.borderBrush Theme.Border
                                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                  Border.padding (12.0, 6.0)
                                  Border.child (
                                      Grid.create
                                          [ Grid.columnDefinitions "*, 80, 120, 72"
                                            Grid.children
                                                [ TextBox.create
                                                      [ Grid.column 0
                                                        TextBox.text club.Name
                                                        TextBox.margin (0.0, 0.0, 8.0, 0.0)
                                                        TextBox.onTextChanged (fun t ->
                                                            updateCountry (fun c ->
                                                                { c with
                                                                    Clubs =
                                                                        c.Clubs
                                                                        |> List.mapi (fun j cl ->
                                                                            if i = j then
                                                                                { cl with Name = t }
                                                                            else
                                                                                cl) })) ]
                                                  NumericUpDown.create
                                                      [ Grid.column 1
                                                        NumericUpDown.value (decimal club.LeagueLevel)
                                                        NumericUpDown.margin (0.0, 0.0, 8.0, 0.0)
                                                        NumericUpDown.onValueChanged (fun v ->
                                                            let level =
                                                                v
                                                                |> Option.ofNullable
                                                                |> Option.map int
                                                                |> Option.defaultValue 0

                                                            updateCountry (fun c ->
                                                                { c with
                                                                    Clubs =
                                                                        c.Clubs
                                                                        |> List.mapi (fun j cl ->
                                                                            if i = j then
                                                                                { cl with LeagueLevel = level }
                                                                            else
                                                                                cl) })) ]
                                                  NumericUpDown.create
                                                      [ Grid.column 2
                                                        NumericUpDown.value (
                                                            decimal (club.Reputation |> Option.defaultValue 5000)
                                                        )
                                                        NumericUpDown.margin (0.0, 0.0, 8.0, 0.0)
                                                        NumericUpDown.onValueChanged (fun v ->
                                                            let rep =
                                                                v
                                                                |> Option.ofNullable
                                                                |> Option.map int
                                                                |> Option.defaultValue 5000

                                                            updateCountry (fun c ->
                                                                { c with
                                                                    Clubs =
                                                                        c.Clubs
                                                                        |> List.mapi (fun j cl ->
                                                                            if i = j then
                                                                                { cl with Reputation = Some rep }
                                                                            else
                                                                                cl) })) ]
                                                  Border.create
                                                      [ Grid.column 3
                                                        Border.child (
                                                            dangerButton "✕" (fun _ ->
                                                                updateCountry (fun c ->
                                                                    { c with
                                                                        Clubs =
                                                                            c.Clubs
                                                                            |> List.indexed
                                                                            |> List.filter (fun (j, _) -> j <> i)
                                                                            |> List.map snd }))
                                                        ) ] ] ]
                                  ) ]

                        Border.create
                            [ Border.padding (12.0, 12.0)
                              Border.child (
                                  UI.primaryButton "Add Club" (Some IconName.add) (fun _ ->
                                      updateCountry (fun c ->
                                          { c with
                                              Clubs = c.Clubs @ [ UpdateModEditor.createEmptyClub "New Club" 0 ] }))
                              ) ] ] ])

    let private cupsSubTab (_state: ModEditorState) (country: CountryDataDto) dispatch =
        let updateCup i f =
            dispatch (ModEditorMsg(UpdateCup(country.Country.Code, i, f)))

        UI.sectionContainer
            "Cups"
            (StackPanel.create
                [ StackPanel.spacing 0.0
                  StackPanel.children
                      [ // Column headers
                        Border.create
                            [ Border.background Theme.BgMain
                              Border.padding (12.0, 8.0)
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.child (
                                  Grid.create
                                      [ Grid.columnDefinitions "*, 120, 72"
                                        Grid.children
                                            [ TextBlock.create
                                                  [ Grid.column 0
                                                    TextBlock.text "CUP NAME"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextSub ]
                                              TextBlock.create
                                                  [ Grid.column 1
                                                    TextBlock.text "FORMAT"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                    TextBlock.foreground Theme.TextSub ] ] ]
                              ) ]

                        for i in 0 .. country.Cups.Length - 1 do
                            let cup = country.Cups.[i]

                            Border.create
                                [ Border.borderBrush Theme.Border
                                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                  Border.padding (12.0, 6.0)
                                  Border.child (
                                      Grid.create
                                          [ Grid.columnDefinitions "*, 120, 72"
                                            Grid.children
                                                [ TextBox.create
                                                      [ Grid.column 0
                                                        TextBox.text cup.Name
                                                        TextBox.margin (0.0, 0.0, 8.0, 0.0)
                                                        TextBox.onTextChanged (fun t ->
                                                            updateCup i (fun c -> { c with Name = t })) ]
                                                  TextBox.create
                                                      [ Grid.column 1
                                                        TextBox.text cup.Format
                                                        TextBox.margin (0.0, 0.0, 8.0, 0.0)
                                                        TextBox.placeholderText "e.g. SK:SingleLeg:NoExtraTime"
                                                        TextBox.onTextChanged (fun t ->
                                                            updateCup i (fun c -> { c with Format = t })) ]
                                                  Border.create
                                                      [ Grid.column 2
                                                        Border.child (
                                                            dangerButton "✕" (fun _ ->
                                                                dispatch (
                                                                    ModEditorMsg(RemoveCup(country.Country.Code, i))
                                                                ))
                                                        ) ] ] ]
                                  ) ]

                        Border.create
                            [ Border.padding (12.0, 12.0)
                              Border.child (
                                  UI.primaryButton "Add Cup" (Some IconName.add) (fun _ ->
                                      dispatch (
                                          ModEditorMsg(
                                              AddCup(
                                                  country.Country.Code,
                                                  { Name = "New Cup"
                                                    Format = "SK:SingleLeg:NoExtraTime" }
                                              )
                                          )
                                      ))
                              ) ] ] ])

    let private namePoolSubTab (country: CountryDataDto) dispatch =
        UI.sectionContainer
            "Name Pool"
            (Grid.create
                [ Grid.columnDefinitions "*, 1, *"
                  Grid.children
                      [ StackPanel.create
                            [ Grid.column 0
                              StackPanel.spacing 8.0
                              StackPanel.margin (Thickness 16.0)
                              StackPanel.children
                                  [ StackPanel.create
                                        [ StackPanel.orientation Orientation.Horizontal
                                          StackPanel.spacing 8.0
                                          StackPanel.children
                                              [ TextBlock.create
                                                    [ TextBlock.text "FIRST NAMES"
                                                      TextBlock.fontSize 10.0
                                                      TextBlock.fontWeight FontWeight.SemiBold
                                                      TextBlock.foreground Theme.TextSub
                                                      TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                UI.countBadge country.Names.FirstNames.Length ] ]
                                    TextBox.create
                                        [ TextBox.acceptsReturn true
                                          TextBox.height 400.0
                                          TextBox.text (String.concat "\n" country.Names.FirstNames)
                                          TextBox.onTextChanged (fun text ->
                                              let names =
                                                  text.Split(
                                                      [| '\n'; '\r' |],
                                                      System.StringSplitOptions.RemoveEmptyEntries
                                                  )
                                                  |> Array.toList

                                              dispatch (
                                                  ModEditorMsg(
                                                      UpdateCountry(
                                                          country.Country.Code,
                                                          fun c ->
                                                              { c with
                                                                  Names = { c.Names with FirstNames = names } }
                                                      )
                                                  )
                                              )) ] ] ]

                        Border.create [ Grid.column 1; Border.background Theme.Border; Border.width 1.0 ]

                        StackPanel.create
                            [ Grid.column 2
                              StackPanel.spacing 8.0
                              StackPanel.margin (Thickness 16.0)
                              StackPanel.children
                                  [ StackPanel.create
                                        [ StackPanel.orientation Orientation.Horizontal
                                          StackPanel.spacing 8.0
                                          StackPanel.children
                                              [ TextBlock.create
                                                    [ TextBlock.text "LAST NAMES"
                                                      TextBlock.fontSize 10.0
                                                      TextBlock.fontWeight FontWeight.SemiBold
                                                      TextBlock.foreground Theme.TextSub
                                                      TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                UI.countBadge country.Names.LastNames.Length ] ]
                                    TextBox.create
                                        [ TextBox.acceptsReturn true
                                          TextBox.height 400.0
                                          TextBox.text (String.concat "\n" country.Names.LastNames)
                                          TextBox.onTextChanged (fun text ->
                                              let names =
                                                  text.Split(
                                                      [| '\n'; '\r' |],
                                                      System.StringSplitOptions.RemoveEmptyEntries
                                                  )
                                                  |> Array.toList

                                              dispatch (
                                                  ModEditorMsg(
                                                      UpdateCountry(
                                                          country.Country.Code,
                                                          fun c ->
                                                              { c with
                                                                  Names = { c.Names with LastNames = names } }
                                                      )
                                                  )
                                              )) ] ] ] ] ])

    // ─── Country detail ───────────────────────────────────────────────────────

    let private countryDetail (state: ModEditorState) (country: CountryDataDto) dispatch =
        let entitySummary =
            $"{country.Clubs.Length} clubs  |  {country.LeagueNames.Length} leagues  |  {country.Cups.Length} cups  |  {country.Names.FirstNames.Length + country.Names.LastNames.Length} names"

        StackPanel.create
            [ StackPanel.spacing 16.0
              StackPanel.children
                  [

                    // ── Country heading ──
                    Border.create
                        [ Border.background Theme.BgCard
                          Border.borderBrush Theme.Border
                          Border.borderThickness 1.0
                          Border.cornerRadius Theme.RadiusLg
                          Border.padding (16.0, 14.0)
                          Border.child (
                              DockPanel.create
                                  [ DockPanel.lastChildFill false
                                    DockPanel.children
                                        [ StackPanel.create
                                              [ DockPanel.dock Dock.Left
                                                StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 12.0
                                                StackPanel.children
                                                    [ Border.create
                                                          [ Border.background Theme.AccentLight
                                                            Border.cornerRadius Theme.RadiusMd
                                                            Border.padding (9.0, 6.0)
                                                            Border.child (
                                                                TextBlock.create
                                                                    [ TextBlock.text country.Country.Code
                                                                      TextBlock.fontWeight FontWeight.Black
                                                                      TextBlock.fontSize 16.0
                                                                      TextBlock.foreground Theme.Accent ]
                                                            ) ]
                                                      StackPanel.create
                                                          [ StackPanel.spacing 2.0
                                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                                            StackPanel.children
                                                                [ TextBlock.create
                                                                      [ TextBlock.text country.Country.Name
                                                                        TextBlock.fontSize 20.0
                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                        TextBlock.foreground Theme.TextMain ]
                                                                  TextBlock.create
                                                                      [ TextBlock.text "COUNTRY DATA"
                                                                        TextBlock.fontSize 9.0
                                                                        TextBlock.fontWeight FontWeight.Black
                                                                        TextBlock.foreground Theme.TextDim ] ] ] ] ]
                                          TextBlock.create
                                              [ DockPanel.dock Dock.Right
                                                TextBlock.text entitySummary
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                          ) ]

                    // ── Sub-tab bar ──
                    ScrollViewer.create
                        [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                          ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Disabled
                          ScrollViewer.content (
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.children
                                        [ UI.tabButton "General" (state.ActiveSubTab = General) (fun _ ->
                                              dispatch (ModEditorMsg(SetSubTab General)))
                                          UI.tabButton "Leagues" (state.ActiveSubTab = Leagues) (fun _ ->
                                              dispatch (ModEditorMsg(SetSubTab Leagues)))
                                          UI.tabButton "Clubs" (state.ActiveSubTab = Clubs) (fun _ ->
                                              dispatch (ModEditorMsg(SetSubTab Clubs)))
                                          UI.tabButton "Cups" (state.ActiveSubTab = Cups) (fun _ ->
                                              dispatch (ModEditorMsg(SetSubTab Cups)))
                                          UI.tabButton "Names" (state.ActiveSubTab = NamePool) (fun _ ->
                                              dispatch (ModEditorMsg(SetSubTab NamePool))) ] ]
                          ) ]

                    // ── Sub-tab content ──
                    match state.ActiveSubTab with
                    | General -> generalSubTab state country dispatch
                    | Leagues -> leaguesSubTab state country dispatch
                    | Clubs -> clubsSubTab state country dispatch
                    | NamePool -> namePoolSubTab country dispatch
                    | Cups -> cupsSubTab state country dispatch ] ]

    // ─── Countries tab ────────────────────────────────────────────────────────

    let private countriesTab (state: ModEditorState) dispatch =
        let emptyCountry =
            StackPanel.create
                [ StackPanel.horizontalAlignment HorizontalAlignment.Center
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.spacing 14.0
                  StackPanel.children
                      [ UI.emptyState
                            IconName.home
                            "Start with a country"
                            "Countries contain leagues, clubs, cups and name pools."
                        UI.primaryButton "Add country" (Some IconName.add) (fun _ ->
                            let code = newCountryCode state.Countries
                            let country = UpdateModEditor.createEmptyCountry code "New country"
                            dispatch (ModEditorMsg(AddCountry country))) ] ]

        Grid.create
            [ Grid.columnDefinitions "260, *"
              Grid.children
                  [ Border.create [ Grid.column 0; Border.child (countrySidebar state dispatch) ]

                    match
                        state.SelectedCountryCode
                        |> Option.bind (fun code -> state.Countries |> Map.tryFind code)
                    with
                    | Some country ->
                        ScrollViewer.create
                            [ Grid.column 1
                              ScrollViewer.padding (24.0, 20.0)
                              ScrollViewer.content (countryDetail state country dispatch) ]
                    | None -> Border.create [ Grid.column 1; Border.child emptyCountry ] ] ]

    let private internationalTab (state: ModEditorState) dispatch =
        let updateIntComp i f =
            dispatch (ModEditorMsg(UpdateInternationalComp(i, f)))

        StackPanel.create
            [ StackPanel.spacing 18.0
              StackPanel.children
                  [ pageIntro
                        IconName.league
                        "COMPETITION BUILDER"
                        "Shape the international calendar"
                        "Keep global competitions readable and deterministic with explicit confederations and formats."
                    UI.sectionContainer
                        $"International competitions  ·  %d{state.InternationalComps.Length}"
                        (StackPanel.create
                            [ StackPanel.spacing 0.0
                              StackPanel.children
                                  [ if state.InternationalComps.IsEmpty then
                                        UI.emptyState
                                            IconName.league
                                            "No international competitions"
                                            "Add one to make the global calendar come alive."
                                    else
                                        Border.create
                                            [ Border.background Theme.BgMain
                                              Border.padding (12.0, 8.0)
                                              Border.borderBrush Theme.Border
                                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                              Border.child (
                                                  Grid.create
                                                      [ Grid.columnDefinitions "*, 120, 100, 72"
                                                        Grid.children
                                                            [ TextBlock.create
                                                                  [ Grid.column 0
                                                                    TextBlock.text "COMPETITION NAME"
                                                                    TextBlock.fontSize 10.0
                                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                                    TextBlock.foreground Theme.TextSub ]
                                                              TextBlock.create
                                                                  [ Grid.column 1
                                                                    TextBlock.text "CONFEDERATION"
                                                                    TextBlock.fontSize 10.0
                                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                                    TextBlock.foreground Theme.TextSub ]
                                                              TextBlock.create
                                                                  [ Grid.column 2
                                                                    TextBlock.text "FORMAT"
                                                                    TextBlock.fontSize 10.0
                                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                                    TextBlock.foreground Theme.TextSub ] ] ]
                                              ) ]

                                    for i in 0 .. state.InternationalComps.Length - 1 do
                                        let comp = state.InternationalComps.[i]

                                        Border.create
                                            [ Border.borderBrush Theme.Border
                                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                              Border.padding (12.0, 6.0)
                                              Border.child (
                                                  Grid.create
                                                      [ Grid.columnDefinitions "*, 120, 100, 72"
                                                        Grid.children
                                                            [ TextBox.create
                                                                  [ Grid.column 0
                                                                    TextBox.text comp.Name
                                                                    TextBox.margin (0.0, 0.0, 8.0, 0.0)
                                                                    TextBox.onTextChanged (fun t ->
                                                                        updateIntComp i (fun c -> { c with Name = t })) ]
                                                              ComboBox.create
                                                                  [ Grid.column 1
                                                                    ComboBox.dataItems
                                                                        [ "UEFA"
                                                                          "CONMEBOL"
                                                                          "CONCACAF"
                                                                          "CAF"
                                                                          "AFC"
                                                                          "OFC" ]
                                                                    ComboBox.selectedItem (
                                                                        comp.Confederation |> Option.defaultValue "UEFA"
                                                                    )
                                                                    ComboBox.margin (0.0, 0.0, 8.0, 0.0)
                                                                    ComboBox.onSelectedItemChanged (fun obj ->
                                                                        match obj with
                                                                        | :? string as s ->
                                                                            updateIntComp i (fun c ->
                                                                                { c with Confederation = Some s })
                                                                        | _ -> ()) ]
                                                              TextBox.create
                                                                  [ Grid.column 2
                                                                    TextBox.text comp.Format
                                                                    TextBox.margin (0.0, 0.0, 8.0, 0.0)
                                                                    TextBox.onTextChanged (fun t ->
                                                                        updateIntComp i (fun c ->
                                                                            { c with Format = t })) ]
                                                              Border.create
                                                                  [ Grid.column 3
                                                                    Border.child (
                                                                        dangerButton "Remove" (fun _ ->
                                                                            dispatch (
                                                                                ModEditorMsg(RemoveInternationalComp i)
                                                                            ))
                                                                    ) ] ] ]
                                              ) ]

                                    Border.create
                                        [ Border.padding (12.0, 12.0)
                                          Border.child (
                                              UI.primaryButton "Add competition" (Some IconName.add) (fun _ ->
                                                  dispatch (
                                                      ModEditorMsg(
                                                          AddInternationalComp
                                                              { Name = "New Competition"
                                                                Confederation = Some "UEFA"
                                                                Format = "G32:SK:SingleLeg:NoExtraTime"
                                                                Qualification = [] }
                                                      )
                                                  ))
                                          ) ] ] ]) ] ]

    let private globalNamesTab (state: ModEditorState) dispatch =
        let firstNames =
            state.Countries
            |> Map.toSeq
            |> Seq.sumBy (fun (_, country) -> country.Names.FirstNames.Length)

        let lastNames =
            state.Countries
            |> Map.toSeq
            |> Seq.sumBy (fun (_, country) -> country.Names.LastNames.Length)

        let firstCountry = state.Countries |> Map.tryPick (fun code _ -> Some code)

        StackPanel.create
            [ StackPanel.spacing 18.0
              StackPanel.children
                  [ pageIntro
                        IconName.fileDocumentEdit
                        "NAME POOLS"
                        "Keep generated names believable"
                        "Names are stored per country so each football culture can have its own first and last name pool."
                    UI.sectionContainer
                        "Current name coverage"
                        (StackPanel.create
                            [ StackPanel.spacing 14.0
                              StackPanel.margin (Thickness 16.0)
                              StackPanel.children
                                  [ UI.statusBanner
                                        Theme.Info
                                        IconName.info
                                        "There is no separate global pool in the package format yet. Edit each country's pool from Countries > Names."
                                    StackPanel.create
                                        [ StackPanel.orientation Orientation.Horizontal
                                          StackPanel.spacing 10.0
                                          StackPanel.children
                                              [ UI.countBadge firstNames
                                                TextBlock.create
                                                    [ TextBlock.text "first names"
                                                      TextBlock.foreground Theme.TextSub
                                                      TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                UI.countBadge lastNames
                                                TextBlock.create
                                                    [ TextBlock.text "last names"
                                                      TextBlock.foreground Theme.TextSub
                                                      TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                    UI.primaryButton "Open country name pools" (Some IconName.next) (fun _ ->
                                        dispatch (ModEditorMsg(SetTab Countries))
                                        dispatch (ModEditorMsg(SelectCountry firstCountry))
                                        dispatch (ModEditorMsg(SetSubTab NamePool))) ] ]) ] ]

    // ─── Validation tab ───────────────────────────────────────────────────────

    let private validationTab (state: ModEditorState) dispatch =
        let diagnosticContext (error: ModDiagnostic) =
            [ error.RelativePath |> Option.map (fun value -> "File " + value)
              error.JsonPath |> Option.map (fun value -> "Path " + value)
              error.EntityId |> Option.map (fun value -> "Entity " + value) ]
            |> List.choose id
            |> String.concat " | "

        StackPanel.create
            [ StackPanel.spacing 18.0
              StackPanel.children
                  [ pageIntro
                        IconName.success
                        "QUALITY CHECK"
                        "Validate before you ship"
                        "The same package boundary used during export runs here, so errors are actionable instead of surprising at load time."
                    Border.create
                        [ Border.background Theme.BgCard
                          Border.borderBrush Theme.Border
                          Border.borderThickness 1.0
                          Border.cornerRadius Theme.RadiusLg
                          Border.padding (16.0, 12.0)
                          Border.child (
                              DockPanel.create
                                  [ DockPanel.lastChildFill false
                                    DockPanel.children
                                        [ StackPanel.create
                                              [ DockPanel.dock Dock.Left
                                                StackPanel.spacing 2.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text "VALIDATION REPORT"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Black
                                                            TextBlock.foreground Theme.TextDim ]
                                                      TextBlock.create
                                                          [ TextBlock.text (
                                                                if state.Errors.IsEmpty then
                                                                    "Package is ready to export"
                                                                else
                                                                    $"%d{state.Errors.Length} issue(s) need attention"
                                                            )
                                                            TextBlock.fontSize 15.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.TextMain ] ] ]
                                          UI.ghostButton "Run validation" (Some IconName.refresh) (fun _ ->
                                              dispatch (ModEditorMsg Validate)) ] ]
                          ) ]

                    if state.Errors.IsEmpty then
                        UI.statusBanner
                            Theme.Success
                            IconName.success
                            "No errors found. Your mod is valid and can be exported."
                    else
                        for error in state.Errors do
                            let context = diagnosticContext error

                            Border.create
                                [ Border.background (Theme.withAlpha "18" Theme.Danger)
                                  Border.borderBrush Theme.Danger
                                  Border.borderThickness 1.0
                                  Border.padding (14.0, 12.0)
                                  Border.cornerRadius Theme.RadiusMd
                                  Border.margin (0.0, 0.0, 0.0, 4.0)
                                  Border.child (
                                      DockPanel.create
                                          [ DockPanel.children
                                                [ Icons.iconSm IconName.error Theme.Danger
                                                  StackPanel.create
                                                      [ StackPanel.spacing 4.0
                                                        StackPanel.margin (10.0, 0.0, 0.0, 0.0)
                                                        StackPanel.children
                                                            [ TextBlock.create
                                                                  [ TextBlock.text $"{error.Code}: {error.Message}"
                                                                    TextBlock.foreground Theme.TextMain
                                                                    TextBlock.fontWeight FontWeight.SemiBold
                                                                    TextBlock.textWrapping TextWrapping.Wrap ]
                                                              if context <> "" then
                                                                  TextBlock.create
                                                                      [ TextBlock.text context
                                                                        TextBlock.foreground Theme.TextMuted
                                                                        TextBlock.fontSize 10.0 ]
                                                              if error.SuggestedAction <> "" then
                                                                  TextBlock.create
                                                                      [ TextBlock.text (
                                                                            "Action: " + error.SuggestedAction
                                                                        )
                                                                        TextBlock.foreground Theme.Warning
                                                                        TextBlock.fontSize 11.0
                                                                        TextBlock.textWrapping TextWrapping.Wrap ] ] ] ] ]
                                  ) ] ] ]

    // ─── Export tab ───────────────────────────────────────────────────────────

    let private exportTab (state: ModEditorState) dispatch =
        let outputName = $"{state.Manifest.Id}-{state.Manifest.Version}.femod"

        let exportStatusView: IView =
            if not state.Errors.IsEmpty then
                UI.statusBanner
                    Theme.Warning
                    IconName.warning
                    "Resolve validation issues before exporting this package."
            elif state.IsDirty then
                UI.statusBanner Theme.Warning IconName.warning "You have changes that are not in the package on disk."
            else
                match state.ExportStatus with
                | Exported ->
                    UI.statusBanner Theme.Success IconName.success "Package exported successfully and is ready to load."
                | ExportFailed error -> UI.statusBanner Theme.Danger IconName.error $"Export failed: {error}"
                | NotExported -> UI.statusBanner Theme.Info IconName.info "This mod has not been exported yet."

        let exportButton =
            Button.create
                [ Button.isEnabled state.Errors.IsEmpty
                  Button.background (
                      if state.Errors.IsEmpty then
                          Theme.Accent
                      else
                          Theme.BgElevated
                  )
                  Button.foreground (
                      if state.Errors.IsEmpty then
                          Theme.BgSidebar
                      else
                          Theme.TextDim
                  )
                  Button.fontWeight FontWeight.Bold
                  Button.padding (20.0, 10.0)
                  Button.cornerRadius Theme.RadiusMd
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 7.0
                            StackPanel.children
                                [ Icons.iconSm
                                      IconName.save
                                      (if state.Errors.IsEmpty then
                                           Theme.BgSidebar
                                       else
                                           Theme.TextDim)
                                  TextBlock.create
                                      [ TextBlock.text "Export package"
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground (
                                            if state.Errors.IsEmpty then
                                                Theme.BgSidebar
                                            else
                                                Theme.TextDim
                                        ) ] ] ]
                  )
                  Button.onClick (fun _ -> dispatch (ModEditorMsg ModEditorMsg.Export)) ]

        StackPanel.create
            [ StackPanel.spacing 18.0
              StackPanel.children
                  [ pageIntro
                        IconName.save
                        "RELEASE GATE"
                        "Package the world you built"
                        "Review the destination and validation state, then create one atomic .femod package for the Mods folder."

                    UI.sectionContainer
                        "Package destination"
                        (StackPanel.create
                            [ StackPanel.spacing 14.0
                              StackPanel.margin (Thickness 16.0)
                              StackPanel.children
                                  [ TextBlock.create
                                        [ TextBlock.text "Mods/"
                                          TextBlock.foreground Theme.TextDim
                                          TextBlock.fontSize 10.0 ]
                                    TextBlock.create
                                        [ TextBlock.text outputName
                                          TextBlock.foreground Theme.TextMain
                                          TextBlock.fontSize 18.0
                                          TextBlock.fontWeight FontWeight.Bold ]
                                    TextBlock.create
                                        [ TextBlock.text
                                              $"{state.Countries.Count} countries  |  {state.InternationalComps.Length} international competitions"
                                          TextBlock.foreground Theme.TextMuted
                                          TextBlock.fontSize 11.0 ]
                                    exportStatusView
                                    exportButton ] ]) ] ]

    // ─── Root view ────────────────────────────────────────────────────────────

    let view (state: State) dispatch =
        let modState = state.ModEditor

        let contentPadding =
            match modState.ActiveTab with
            | Countries -> Thickness 0.0
            | _ -> Thickness 24.0

        let buildErrorBanner () =
            let errorItems =
                state.ModLoadErrors
                |> List.take (min 5 state.ModLoadErrors.Length)
                |> List.map (fun err ->
                    TextBlock.create
                        [ TextBlock.text $"- {err}"
                          TextBlock.fontSize 12.0
                          TextBlock.foreground Theme.TextSub ]
                    :> IView)
                |> List.append [ UI.ghostButton "Reload Mods" (Some IconName.refresh) (fun _ -> dispatch ReloadMods) ]

            Border.create
                [ Border.background (Theme.withAlpha "22" Theme.Warning)
                  Border.borderBrush Theme.Warning
                  Border.borderThickness 1.0
                  Border.padding 12.0
                  Border.cornerRadius 6.0
                  Border.margin (0.0, 0.0, 0.0, 12.0)
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 8.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text $"{state.ModLoadErrors.Length} mod load warning(s)"
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.foreground Theme.Warning ]
                                  StackPanel.create [ StackPanel.spacing 4.0; StackPanel.children errorItems ] ] ]
                  ) ]

        let contentView: IView =
            match modState.ActiveTab with
            | Overview -> overviewTab modState dispatch :> IView
            | Countries -> countriesTab modState dispatch :> IView
            | Validation -> validationTab modState dispatch :> IView
            | Export -> exportTab modState dispatch :> IView
            | International -> internationalTab modState dispatch :> IView
            | GlobalNames -> globalNamesTab modState dispatch

        let scrollContent =
            if state.ModLoadErrors.IsEmpty then
                contentView
            else
                StackPanel.create [ StackPanel.children [ buildErrorBanner (); contentView ] ]

        let historyButton label iconKind isEnabled onClick =
            Button.create
                [ Button.isEnabled isEnabled
                  Button.background "Transparent"
                  Button.foreground Theme.TextMuted
                  Button.borderBrush Theme.Border
                  Button.borderThickness 1.0
                  Button.padding (12.0, 7.0)
                  Button.cornerRadius Theme.RadiusSm
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 6.0
                            StackPanel.children
                                [ Icons.iconSm iconKind Theme.TextMuted
                                  TextBlock.create
                                      [ TextBlock.text label
                                        TextBlock.fontSize 11.0
                                        TextBlock.foreground Theme.TextMuted ] ] ]
                  )
                  Button.onClick onClick ]

        let historyPanel =
            StackPanel.create
                [ DockPanel.dock Dock.Right
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 4.0
                  StackPanel.margin (8.0, 0.0)
                  StackPanel.children
                      [ historyButton "Undo" Nav.rewind (not modState.Past.IsEmpty) (fun _ ->
                            dispatch (ModEditorMsg UndoMod))
                        historyButton "Redo" Nav.fastForward (not modState.Future.IsEmpty) (fun _ ->
                            dispatch (ModEditorMsg RedoMod)) ] ]

        DockPanel.create
            [ DockPanel.children
                  [ editorHeader modState dispatch

                    Border.create
                        [ DockPanel.dock Dock.Top
                          Border.background Theme.BgCard
                          Border.borderBrush Theme.Border
                          Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                          Border.child (
                              DockPanel.create
                                  [ DockPanel.lastChildFill true
                                    DockPanel.children
                                        [

                                          historyPanel

                                          // Tabs
                                          ScrollViewer.create
                                              [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                                                ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Disabled
                                                ScrollViewer.content (
                                                    StackPanel.create
                                                        [ StackPanel.orientation Orientation.Horizontal
                                                          StackPanel.margin (12.0, 0.0)
                                                          StackPanel.children
                                                              [ topTab
                                                                    modState.ActiveTab
                                                                    Overview
                                                                    IconName.home
                                                                    "Overview"
                                                                    dispatch
                                                                topTab
                                                                    modState.ActiveTab
                                                                    Countries
                                                                    IconName.squad
                                                                    "Countries"
                                                                    dispatch
                                                                topTab
                                                                    modState.ActiveTab
                                                                    International
                                                                    IconName.league
                                                                    "International"
                                                                    dispatch
                                                                topTab
                                                                    modState.ActiveTab
                                                                    GlobalNames
                                                                    IconName.fileDocumentEdit
                                                                    "Global Names"
                                                                    dispatch
                                                                topTab
                                                                    modState.ActiveTab
                                                                    Validation
                                                                    IconName.success
                                                                    "Validation"
                                                                    dispatch
                                                                topTab
                                                                    modState.ActiveTab
                                                                    Export
                                                                    IconName.save
                                                                    "Export"
                                                                    dispatch ] ]
                                                ) ] ] ]
                          ) ]

                    // ── Content area ──
                    ScrollViewer.create [ ScrollViewer.padding contentPadding; ScrollViewer.content scrollContent ] ] ]
