namespace FootballEngine.Pages

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Settings
open FootballEngine.Icons
open FootballEngine.Core.Match
open FootballEngine.Data

/// Rows, controls, cards and navigation shared by every settings category.
module internal SettingsWidgets =

    let internal tr (es: string) (en: string) =
        if Localization.active () = Locale.Es then es else en

    // why import draft as mutable module-level: simplest textbox backing without adding field to SettingsState (0 users, no persist)
    let mutable internal importJsonDraft = ""

    // why helpers as private: keeps Settings.view focused on layout, helpers are pure view math with no engine deps

    let internal categoryLabel (cat: AppTypes.SettingsCategory) =
        match cat with
        | AppTypes.SettingsCategory.Appearance -> (tr "Apariencia" "Appearance")
        | AppTypes.SettingsCategory.MatchView -> (tr "Partido" "Match")
        | AppTypes.SettingsCategory.Simulation -> (tr "Simulación" "Simulation")
        | AppTypes.SettingsCategory.Career -> (tr "Carrera" "Career")
        | AppTypes.SettingsCategory.Data -> (tr "Datos" "Data")
        | AppTypes.SettingsCategory.Scouting -> "Scouting"
        | AppTypes.SettingsCategory.Advanced -> (tr "Avanzado" "Advanced")
        | AppTypes.SettingsCategory.All -> (tr "Todos" "All")

    let internal categoryIcon (cat: AppTypes.SettingsCategory) =
        match cat with
        | AppTypes.SettingsCategory.Appearance -> IconName.settings
        | AppTypes.SettingsCategory.MatchView -> IconName.soccer
        | AppTypes.SettingsCategory.Simulation -> IconName.chart
        | AppTypes.SettingsCategory.Career -> ClubIcon.staff
        | AppTypes.SettingsCategory.Data -> IconName.fileDocumentEdit
        | AppTypes.SettingsCategory.Scouting -> ClubIcon.scouting
        | AppTypes.SettingsCategory.Advanced -> IconName.training
        | AppTypes.SettingsCategory.All -> IconName.filter

    let internal impactColor (impact: string) =
        match impact with
        | "🎨" -> Theme.Accent
        | "⚡" -> Theme.Warning
        | "🎯" -> Theme.AccentAlt
        | _ -> Theme.TextMuted

    let internal impactIcon (impact: string) : Material.Icons.MaterialIconKind =
        match impact with
        | "🎨" -> Material.Icons.MaterialIconKind.Palette
        | "⚡" -> Material.Icons.MaterialIconKind.LightningBolt
        | "🎯" -> Material.Icons.MaterialIconKind.Target
        | _ -> Material.Icons.MaterialIconKind.InformationOutline

    let internal errorForKey (prefix: string) (state: State) =
        state.Settings.ValidationErrors
        |> Map.tryFind prefix
        |> Option.orElseWith (fun () ->
            state.Settings.ValidationErrors
            |> Map.toSeq
            |> Seq.tryFind (fun (k, _) -> k.Contains(prefix))
            |> Option.map snd)

        |> Option.orElseWith (fun () ->
            state.Settings.ValidationErrors
            |> Map.toSeq
            |> Seq.tryFind (fun (k, _) -> k.ToLower().Contains(prefix.ToLower()))
            |> Option.map snd)

    let internal matchesSearch (query: string) (label: string) (desc: string) =
        if System.String.IsNullOrWhiteSpace query then
            true
        else
            let q = query.ToLowerInvariant()
            label.ToLowerInvariant().Contains(q) || desc.ToLowerInvariant().Contains(q)

    let internal isCategoryDirty (cat: AppTypes.SettingsCategory) (state: State) =
        // why per-category: badge · should only light for modified section, avoids global false positives
        match cat with
        | AppTypes.SettingsCategory.Appearance -> state.Settings.Global.Appearance <> AppearanceSettings.defaults
        | AppTypes.SettingsCategory.MatchView -> state.Settings.Global.MatchView <> MatchViewSettings.defaults
        | AppTypes.SettingsCategory.Career -> state.Settings.Global.Career <> CareerSettings.defaults
        | AppTypes.SettingsCategory.Data -> state.Settings.Global.Data <> DataSettings.defaults
        | AppTypes.SettingsCategory.Advanced -> state.Settings.Global.Advanced <> AdvancedSettings.defaults
        | AppTypes.SettingsCategory.Simulation -> state.Settings.Save.Simulation <> SimulationSettings.defaults
        | AppTypes.SettingsCategory.Scouting -> state.Settings.Save.Scouting <> ScoutingSettings.defaults
        | AppTypes.SettingsCategory.All -> state.Settings.Dirty

    let internal settingRow
        (label: string)
        (desc: string)
        (impact: string)
        (control: IView)
        (errorOpt: string option)
        : IView =
        // why Border with bottom divider: groups rows without extra card chrome, keeps scan cheap
        Border.create
            [ Border.padding (16.0, 14.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 8.0
                        StackPanel.children
                            [ Grid.create
                                  [ Grid.columnDefinitions "*, Auto"
                                    Grid.children
                                        [ StackPanel.create
                                              [ Grid.column 0
                                                StackPanel.spacing 4.0
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.children
                                                    [ StackPanel.create
                                                          [ StackPanel.orientation Orientation.Horizontal
                                                            StackPanel.spacing 8.0
                                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                                            StackPanel.children
                                                                [ TextBlock.create
                                                                      [ TextBlock.text label
                                                                        TextBlock.fontSize 13.0
                                                                        TextBlock.fontWeight FontWeight.SemiBold
                                                                        TextBlock.foreground Theme.TextMain ]
                                                                  Border.create
                                                                      [ Border.background (
                                                                            Theme.withAlpha "18" (impactColor impact)
                                                                        )
                                                                        Border.borderBrush (
                                                                            Theme.withAlpha "40" (impactColor impact)
                                                                        )
                                                                        Border.borderThickness 1.0
                                                                        Border.cornerRadius 4.0
                                                                        Border.padding (6.0, 2.0)
                                                                        Border.child (
                                                                            // Emoji at 11px renders as a monochrome blob without a
                                                                            // color-emoji font; vector icons stay crisp at any size.
                                                                            Icons.icon
                                                                                (impactIcon impact)
                                                                                12.0
                                                                                (impactColor impact)
                                                                        ) ] ] ]
                                                      TextBlock.create
                                                          [ TextBlock.text desc
                                                            TextBlock.fontSize 11.0
                                                            TextBlock.foreground Theme.TextMuted
                                                            TextBlock.textWrapping TextWrapping.Wrap ] ] ]
                                          Border.create
                                              [ Grid.column 1
                                                Border.verticalAlignment VerticalAlignment.Center
                                                Border.margin (16.0, 0.0, 0.0, 0.0)
                                                Border.child control ] ] ]
                              match errorOpt with
                              | None -> ()
                              | Some msg when System.String.IsNullOrWhiteSpace msg -> ()
                              | Some msg ->
                                  Border.create
                                      [ Border.background (Theme.withAlpha "12" Theme.Danger)
                                        Border.borderBrush (Theme.withAlpha "40" Theme.Danger)
                                        Border.borderThickness 1.0
                                        Border.cornerRadius 6.0
                                        Border.padding (8.0, 6.0)
                                        Border.child (
                                            TextBlock.create
                                                [ TextBlock.text msg
                                                  TextBlock.fontSize 11.0
                                                  TextBlock.foreground Theme.Danger
                                                  TextBlock.textWrapping TextWrapping.Wrap ]
                                        ) ] ] ]
              ) ]
        :> IView

    let internal toggleControl (isOn: bool) (onToggle: bool -> unit) : IView =
        CheckBox.create
            [ CheckBox.isChecked isOn
              CheckBox.onIsCheckedChanged (fun args ->
                  match args.Source with
                  | :? CheckBox as checkBox -> onToggle (checkBox.IsChecked = System.Nullable true)
                  | _ -> ())
              CheckBox.content (if isOn then "On" else "Off")
              CheckBox.foreground (if isOn then Theme.Accent else Theme.TextMuted)
              CheckBox.verticalAlignment VerticalAlignment.Center ]
        :> IView

    let internal sliderWithLabel
        (min: float)
        (max: float)
        (value: float)
        (format: float -> string)
        (onChange: float -> unit)
        : IView =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 12.0
              StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.children
                  [ Slider.create
                        [ Slider.minimum min
                          Slider.maximum max
                          Slider.value value
                          Slider.onValueChanged (fun v -> onChange v)
                          Slider.width 160.0
                          Slider.verticalAlignment VerticalAlignment.Center ]
                    Border.create
                        [ Border.background Theme.BgElevated
                          Border.cornerRadius 6.0
                          Border.padding (10.0, 4.0)
                          Border.minWidth 64.0
                          Border.child (
                              TextBlock.create
                                  [ TextBlock.text (format value)
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                          ) ] ] ]
        :> IView

    let internal intSliderWithLabel (min: int) (max: int) (value: int) (onChange: int -> unit) : IView =
        sliderWithLabel (float min) (float max) (float value) (fun v -> string (int (System.Math.Round(v)))) (fun v ->
            onChange (int (System.Math.Round(v))))

    let internal navButton (cat: AppTypes.SettingsCategory) (state: State) (dispatch: Msg -> unit) : IView =
        let isActive = state.Settings.SelectedCategory = cat
        let dirty = isCategoryDirty cat state
        let bg = if isActive then Theme.AccentLight else "Transparent"
        let fg = if isActive then Theme.Accent else Theme.TextSub
        let border = if isActive then Theme.Accent else "Transparent"

        Button.create
            [ Button.background bg
              Button.borderBrush border
              Button.borderThickness (if isActive then 1.0 else 0.0)
              Button.cornerRadius 8.0
              Button.margin (8.0, 3.0)
              Button.padding (12.0, 9.0)
              Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.horizontalContentAlignment HorizontalAlignment.Left
              Button.cursor Avalonia.Input.Cursor.Default
              Button.onClick (fun _ -> dispatch (SettingsMsg(SelectCategory cat)))
              Button.content (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ if dirty then
                                  TextBlock.create
                                      [ DockPanel.dock Dock.Right
                                        TextBlock.text "·"
                                        TextBlock.fontSize 18.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.Warning
                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                        TextBlock.margin (6.0, 0.0, 0.0, 0.0) ]
                              else
                                  Border.create [ Border.width 0.0 ]
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 10.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ Icons.iconSm (categoryIcon cat) fg
                                          TextBlock.create
                                              [ TextBlock.text (categoryLabel cat)
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight (
                                                    if isActive then FontWeight.SemiBold else FontWeight.Normal
                                                )
                                                TextBlock.foreground fg
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ]
              ) ]
        :> IView

    let internal headerView (state: State) (dispatch: Msg -> unit) : IView =
        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.padding (16.0, 10.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ StackPanel.create
                                  [ DockPanel.dock Dock.Left
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBox.create
                                              [ TextBox.placeholderText (
                                                    tr
                                                        "Buscar ajustes... (ej: tema, sonido)"
                                                        "Search settings... (e.g. theme, sound)"
                                                )
                                                TextBox.text state.Settings.SearchQuery
                                                TextBox.width 280.0
                                                TextBox.onTextChanged (fun t -> dispatch (SettingsMsg(SetSearch t))) ]
                                          Border.create
                                              [ Border.background (
                                                    if state.Settings.Dirty then
                                                        Theme.WarningLight
                                                    else
                                                        Theme.AccentLight
                                                )
                                                Border.borderBrush (
                                                    if state.Settings.Dirty then Theme.Warning else Theme.Accent
                                                )
                                                Border.borderThickness 1.0
                                                Border.cornerRadius 999.0
                                                Border.padding (10.0, 4.0)
                                                Border.verticalAlignment VerticalAlignment.Center
                                                Border.child (
                                                    TextBlock.create
                                                        [ TextBlock.text (
                                                              if state.Settings.Dirty then
                                                                  (tr "● Cambios sin guardar" "● Unsaved changes")
                                                              else
                                                                  (tr "✓ Sincronizado" "✓ Synced")
                                                          )
                                                          TextBlock.fontSize 10.0
                                                          TextBlock.fontWeight FontWeight.Bold
                                                          TextBlock.foreground (
                                                              if state.Settings.Dirty then
                                                                  Theme.Warning
                                                              else
                                                                  Theme.Accent
                                                          ) ]
                                                ) ]
                                          Border.create
                                              [ Border.background Theme.BgElevated
                                                Border.cornerRadius 999.0
                                                Border.padding (8.0, 4.0)
                                                Border.child (
                                                    TextBlock.create
                                                        [ TextBlock.text (tr "Global · Carrera" "Global · Career")
                                                          TextBlock.fontSize 10.0
                                                          TextBlock.foreground Theme.TextMuted ]
                                                ) ] ] ]
                              StackPanel.create
                                  [ DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ Button.create
                                              [ Button.content "Import"
                                                Button.padding (12.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.background "Transparent"
                                                Button.borderBrush Theme.Border
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 6.0
                                                Button.onClick (fun _ -> dispatch (SettingsMsg(Import importJsonDraft))) ]
                                          Button.create
                                              [ Button.content "Export"
                                                Button.padding (12.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.background "Transparent"
                                                Button.borderBrush Theme.Border
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 6.0
                                                Button.onClick (fun _ ->
                                                    // why try clipboard: best effort — if Avalonia clipboard unavailable (headless test) we still dispatch Export which logs and notifies
                                                    try
                                                        dispatch (SettingsMsg Export)
                                                    with _ ->
                                                        ()) ]
                                          Button.create
                                              [ Button.content "Reset"
                                                Button.padding (12.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.background "Transparent"
                                                Button.borderBrush Theme.Border
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 6.0
                                                Button.onClick (fun _ ->
                                                    if
                                                        state.Settings.SelectedCategory = AppTypes.SettingsCategory.All
                                                    then
                                                        dispatch (SettingsMsg ResetAll)
                                                    else
                                                        dispatch (
                                                            SettingsMsg(ResetCategory state.Settings.SelectedCategory)
                                                        )) ]
                                          Button.create
                                              [ Button.content "Undo"
                                                Button.padding (12.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.background "Transparent"
                                                Button.borderBrush Theme.Border
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 6.0
                                                Button.isEnabled (not state.Settings.History.IsEmpty)
                                                Button.onClick (fun _ -> dispatch (SettingsMsg Undo)) ]
                                          Button.create
                                              [ Button.content "Redo"
                                                Button.padding (12.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.background "Transparent"
                                                Button.borderBrush Theme.Border
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 6.0
                                                Button.isEnabled (not state.Settings.Future.IsEmpty)
                                                Button.onClick (fun _ -> dispatch (SettingsMsg Redo)) ] ] ] ] ]
              ) ]
        :> IView

    let internal navView (state: State) (dispatch: Msg -> unit) : IView =
        Border.create
            [ Border.width 200.0
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ StackPanel.create
                                  [ DockPanel.dock Dock.Top
                                    StackPanel.margin (12.0, 16.0, 12.0, 8.0)
                                    StackPanel.spacing 4.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (tr "CATEGORÍAS" "CATEGORIES")
                                                TextBlock.fontSize 10.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              ScrollViewer.create
                                  [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                    ScrollViewer.content (
                                        StackPanel.create
                                            [ StackPanel.spacing 2.0
                                              StackPanel.children
                                                  [ navButton AppTypes.SettingsCategory.All state dispatch
                                                    navButton AppTypes.SettingsCategory.Appearance state dispatch
                                                    navButton AppTypes.SettingsCategory.MatchView state dispatch
                                                    navButton AppTypes.SettingsCategory.Career state dispatch
                                                    Border.create [ Border.height 8.0; Border.background "Transparent" ]
                                                    navButton AppTypes.SettingsCategory.Simulation state dispatch
                                                    navButton AppTypes.SettingsCategory.Data state dispatch
                                                    navButton AppTypes.SettingsCategory.Scouting state dispatch
                                                    navButton AppTypes.SettingsCategory.Advanced state dispatch ] ]
                                    ) ] ] ]
              ) ]
        :> IView

    let internal cardContainer (title: string) (icon: Material.Icons.MaterialIconKind) (rows: IView list) : IView =
        if rows.IsEmpty then
            Border.create [ Border.height 0.0 ] :> IView
        else
            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.clipToBounds true
                  Border.margin (0.0, 0.0, 0.0, 16.0)
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 0.0
                            StackPanel.children
                                [ Border.create
                                      [ Border.background Theme.BgCard
                                        Border.padding (16.0, 11.0)
                                        Border.borderBrush Theme.Border
                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.orientation Orientation.Horizontal
                                                  StackPanel.spacing 8.0
                                                  StackPanel.children
                                                      [ Icons.iconSm icon Theme.Accent
                                                        TextBlock.create
                                                            [ TextBlock.text (title.ToUpperInvariant())
                                                              TextBlock.fontSize 10.0
                                                              TextBlock.fontWeight FontWeight.Black
                                                              TextBlock.foreground Theme.TextMuted
                                                              TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                        ) ]
                                  StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children rows ] ] ]
                  ) ]
            :> IView

    let internal cardContainerWithReset
        (title: string)
        (icon: Material.Icons.MaterialIconKind)
        (category: AppTypes.SettingsCategory)
        (rows: IView list)
        (dispatch: Msg -> unit)
        : IView =
        if rows.IsEmpty then
            Border.create [ Border.height 0.0 ] :> IView
        else
            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.clipToBounds true
                  Border.margin (0.0, 0.0, 0.0, 16.0)
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 0.0
                            StackPanel.children
                                [ Border.create
                                      [ Border.background Theme.BgCard
                                        Border.padding (16.0, 11.0)
                                        Border.borderBrush Theme.Border
                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                        Border.child (
                                            Grid.create
                                                [ Grid.columnDefinitions "*, Auto"
                                                  Grid.children
                                                      [ StackPanel.create
                                                            [ Grid.column 0
                                                              StackPanel.orientation Orientation.Horizontal
                                                              StackPanel.spacing 8.0
                                                              StackPanel.verticalAlignment VerticalAlignment.Center
                                                              StackPanel.children
                                                                  [ Icons.iconSm icon Theme.Accent
                                                                    TextBlock.create
                                                                        [ TextBlock.text (title.ToUpperInvariant())
                                                                          TextBlock.fontSize 10.0
                                                                          TextBlock.fontWeight FontWeight.Black
                                                                          TextBlock.foreground Theme.TextMuted
                                                                          TextBlock.verticalAlignment
                                                                              VerticalAlignment.Center ] ] ]
                                                        Button.create
                                                            [ Grid.column 1
                                                              Button.content "↺"
                                                              Button.fontSize 12.0
                                                              Button.background "Transparent"
                                                              Button.borderBrush Theme.Border
                                                              Button.borderThickness 1.0
                                                              Button.cornerRadius 6.0
                                                              Button.padding (8.0, 4.0)
                                                              Button.verticalAlignment VerticalAlignment.Center
                                                              // why ResetCategory per card: lets user revert one section without losing other changes
                                                              Button.onClick (fun _ ->
                                                                  dispatch (SettingsMsg(ResetCategory category))) ]
                                                        :> IView ] ]
                                        ) ]
                                  StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children rows ] ] ]
                  ) ]
            :> IView

    let internal placeholderCard (title: string) (desc: string) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius 10.0
              Border.padding (20.0, 18.0)
              Border.margin (0.0, 0.0, 0.0, 16.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 8.0
                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                        StackPanel.children
                            [ Border.create
                                  [ Border.background (Theme.withAlpha "12" Theme.TextMuted)
                                    Border.cornerRadius 999.0
                                    Border.padding 12.0
                                    Border.horizontalAlignment HorizontalAlignment.Center
                                    Border.child (Icons.iconMd IconName.info Theme.TextDim) ]
                              TextBlock.create
                                  [ TextBlock.text (tr "Próximamente" "Coming soon")
                                    TextBlock.fontSize 14.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              TextBlock.create
                                  [ TextBlock.text title
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              TextBlock.create
                                  [ TextBlock.text desc
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextDim
                                    TextBlock.textAlignment TextAlignment.Center
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
              ) ]
        :> IView

    let internal toggleControlEnabled (isOn: bool) (isEnabled: bool) (onToggle: bool -> unit) : IView =
        CheckBox.create
            [ CheckBox.isChecked isOn
              CheckBox.isEnabled isEnabled
              CheckBox.onIsCheckedChanged (fun args ->
                  match args.Source with
                  | :? CheckBox as checkBox when isEnabled -> onToggle (checkBox.IsChecked = System.Nullable true)
                  | _ -> ())
              CheckBox.content (if isOn then "On" else "Off")
              CheckBox.foreground (if isOn then Theme.Accent else Theme.TextMuted)
              CheckBox.verticalAlignment VerticalAlignment.Center ]
        :> IView

    let internal sliderWithLabelEnabled
        (min: float)
        (max: float)
        (value: float)
        (isEnabled: bool)
        (format: float -> string)
        (onChange: float -> unit)
        : IView =
        // why isEnabled guard: preset locks values — disabled slider must not fire UpdateSave
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 12.0
              StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.children
                  [ Slider.create
                        [ Slider.minimum min
                          Slider.maximum max
                          Slider.value value
                          Slider.isEnabled isEnabled
                          Slider.onValueChanged (fun v ->
                              if isEnabled then
                                  onChange v)
                          Slider.width 160.0
                          Slider.verticalAlignment VerticalAlignment.Center ]
                    Border.create
                        [ Border.background Theme.BgElevated
                          Border.cornerRadius 6.0
                          Border.padding (10.0, 4.0)
                          Border.minWidth 64.0
                          Border.opacity (if isEnabled then 1.0 else 0.5)
                          Border.child (
                              TextBlock.create
                                  [ TextBlock.text (format value)
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                          ) ] ] ]
        :> IView

    let internal infoBanner (msg: string) : IView =
        Border.create
            [ Border.background (Theme.withAlpha "14" Theme.Info)
              Border.borderBrush (Theme.withAlpha "40" Theme.Info)
              Border.borderThickness 1.0
              Border.cornerRadius 8.0
              Border.padding (12.0, 8.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm IconName.info Theme.Info
                              TextBlock.create
                                  [ TextBlock.text msg
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]
        :> IView

    let internal readOnlyValue (label: string) (value: string) : IView =
        Grid.create
            [ Grid.columnDefinitions "*, Auto"
              Grid.margin (0.0, 4.0)
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text label
                          TextBlock.fontSize 11.0
                          TextBlock.foreground Theme.TextMuted ]
                    Border.create
                        [ Grid.column 1
                          Border.background Theme.BgElevated
                          Border.cornerRadius 6.0
                          Border.padding (10.0, 4.0)
                          Border.child (
                              TextBlock.create
                                  [ TextBlock.text value
                                    TextBlock.fontSize 11.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextDim
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                          ) ] ] ]
        :> IView

    let internal presetLabel (p: DifficultyPreset) =
        match p with
        | DifficultyPreset.Classic -> tr "Clásico" "Classic"
        | DifficultyPreset.Realistic -> tr "Realista" "Realistic"
        | DifficultyPreset.Arcade -> "Arcade"
        | DifficultyPreset.Custom -> "Custom"

    let internal labelToPreset (s: string) =
        match s with
        | "Clásico"
        | "Classic" -> DifficultyPreset.Classic
        | "Realista"
        | "Realistic" -> DifficultyPreset.Realistic
        | "Arcade" -> DifficultyPreset.Arcade
        | _ -> DifficultyPreset.Custom

    let internal warningBanner (msg: string) : IView =
        Border.create
            [ Border.background (Theme.withAlpha "18" Theme.Warning)
              Border.borderBrush Theme.Warning
              Border.borderThickness 1.0
              Border.cornerRadius 8.0
              Border.padding (12.0, 8.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm IconName.warning Theme.Warning
                              TextBlock.create
                                  [ TextBlock.text msg
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.Warning
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]
        :> IView

    let internal motorSection (title: string) (rows: IView list) : IView =
        // why section as Border: visual grouping without Expander dep, keeps Fantomas stable and avoids extra state for collapsed
        Border.create
            [ Border.background Theme.BgElevated
              Border.cornerRadius 8.0
              Border.padding (12.0, 10.0)
              Border.margin (0.0, 6.0, 0.0, 6.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 6.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text (title.ToUpperInvariant())
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.foreground Theme.TextMuted ]
                              StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children rows ] ] ]
              ) ]
        :> IView

    let internal diffItem (label: string) (value: string) : IView =
        Grid.create
            [ Grid.columnDefinitions "*, Auto"
              Grid.margin (0.0, 4.0)
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text label
                          TextBlock.fontSize 11.0
                          TextBlock.foreground Theme.TextMuted ]
                    TextBlock.create
                        [ Grid.column 1
                          TextBlock.text value
                          TextBlock.fontSize 11.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMain ] ] ]
        :> IView
