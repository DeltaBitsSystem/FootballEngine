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

module Settings =

    // why import draft as mutable module-level: simplest textbox backing without adding field to SettingsState (0 users, no persist)
    let mutable private importJsonDraft = ""

    // why helpers as private: keeps Settings.view focused on layout, helpers are pure view math with no engine deps

    let private categoryLabel (cat: AppTypes.SettingsCategory) =
        match cat with
        | AppTypes.SettingsCategory.Appearance -> "Apariencia"
        | AppTypes.SettingsCategory.MatchView -> "Partido"
        | AppTypes.SettingsCategory.Simulation -> "Simulación"
        | AppTypes.SettingsCategory.Career -> "Carrera"
        | AppTypes.SettingsCategory.Data -> "Datos"
        | AppTypes.SettingsCategory.Scouting -> "Scouting"
        | AppTypes.SettingsCategory.Advanced -> "Avanzado"
        | AppTypes.SettingsCategory.All -> "Todos"

    let private categoryIcon (cat: AppTypes.SettingsCategory) =
        match cat with
        | AppTypes.SettingsCategory.Appearance -> IconName.settings
        | AppTypes.SettingsCategory.MatchView -> IconName.soccer
        | AppTypes.SettingsCategory.Simulation -> IconName.chart
        | AppTypes.SettingsCategory.Career -> ClubIcon.staff
        | AppTypes.SettingsCategory.Data -> IconName.fileDocumentEdit
        | AppTypes.SettingsCategory.Scouting -> ClubIcon.scouting
        | AppTypes.SettingsCategory.Advanced -> IconName.training
        | AppTypes.SettingsCategory.All -> IconName.filter

    let private impactColor (impact: string) =
        match impact with
        | "🎨" -> Theme.Accent
        | "⚡" -> Theme.Warning
        | "🎯" -> Theme.AccentAlt
        | _ -> Theme.TextMuted

    let private impactIcon (impact: string) : Material.Icons.MaterialIconKind =
        match impact with
        | "🎨" -> Material.Icons.MaterialIconKind.Palette
        | "⚡" -> Material.Icons.MaterialIconKind.LightningBolt
        | "🎯" -> Material.Icons.MaterialIconKind.Target
        | _ -> Material.Icons.MaterialIconKind.InformationOutline

    let private errorForKey (prefix: string) (state: State) =
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

    let private matchesSearch (query: string) (label: string) (desc: string) =
        if System.String.IsNullOrWhiteSpace query then
            true
        else
            let q = query.ToLowerInvariant()
            label.ToLowerInvariant().Contains(q) || desc.ToLowerInvariant().Contains(q)

    let private isCategoryDirty (cat: AppTypes.SettingsCategory) (state: State) =
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

    let private settingRow
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

    let private toggleControl (isOn: bool) (onToggle: bool -> unit) : IView =
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

    let private sliderWithLabel
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

    let private intSliderWithLabel (min: int) (max: int) (value: int) (onChange: int -> unit) : IView =
        sliderWithLabel (float min) (float max) (float value) (fun v -> string (int (System.Math.Round(v)))) (fun v ->
            onChange (int (System.Math.Round(v))))

    let private navButton (cat: AppTypes.SettingsCategory) (state: State) (dispatch: Msg -> unit) : IView =
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

    let private headerView (state: State) (dispatch: Msg -> unit) : IView =
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
                                              [ TextBox.watermark "Buscar ajustes... (ej: tema, sonido)"
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
                                                                  "● Cambios sin guardar"
                                                              else
                                                                  "✓ Sincronizado"
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
                                                        [ TextBlock.text "Global · Carrera"
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
                                                        let json = SettingsSlice.exportState state.Settings
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

    let private navView (state: State) (dispatch: Msg -> unit) : IView =
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
                                              [ TextBlock.text "CATEGORÍAS"
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

    let private cardContainer (title: string) (icon: Material.Icons.MaterialIconKind) (rows: IView list) : IView =
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

    let private cardContainerWithReset
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

    let private placeholderCard (title: string) (desc: string) : IView =
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
                                  [ TextBlock.text "Próximamente"
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

    let private appearanceCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let g = state.Settings.Global.Appearance

        let rowsData: (string * string * string * IView * string) list =
            [ let label = "Tema"
              let desc = "Modo oscuro / claro / automático — aplica sin reinicio"

              let ctl =
                  ComboBox.create
                      [ ComboBox.dataItems [ ThemeMode.Dark; ThemeMode.Light; ThemeMode.Auto ]
                        ComboBox.selectedItem g.ThemeMode
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> ThemeMode

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                Appearance = { gs.Appearance with ThemeMode = v } })
                                    )
                                )) ]
                  :> IView

              yield (label, desc, "🎨", ctl, "ThemeMode")
              let label2 = "Color de acento"
              let desc2 = "Hex como #00d492 — usado en botones y enlaces, validado en vivo"

              let ctl2 =
                  TextBox.create
                      [ TextBox.text g.Accent
                        TextBox.watermark "#00d492"
                        TextBox.width 140.0
                        TextBox.onTextChanged (fun t ->
                            dispatch (
                                SettingsMsg(
                                    UpdateGlobal(fun gs ->
                                        { gs with
                                            Appearance = { gs.Appearance with Accent = t } })
                                )
                            )) ]
                  :> IView

              yield (label2, desc2, "🎨", ctl2, "Accent")
              let label3 = "Densidad"
              let desc3 = "Comfortable deja aire, Compact aprieta listas para más filas visibles"
              let isCompact = g.Density = Density.Compact

              let ctl3 =
                  toggleControl isCompact (fun on ->
                      let v = if on then Density.Compact else Density.Comfortable

                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Appearance = { gs.Appearance with Density = v } })
                          )
                      ))

              yield (label3, desc3, "🎨", ctl3, "Density")
              let label4 = "Escala de fuente"
              let desc4 = "0.7x a 1.5x — escala tipográfica global, no solo zoom"

              let ctl4 =
                  sliderWithLabel 0.7 1.5 g.FontScale (fun v -> sprintf "%.2fx" v) (fun v ->
                      let clamped = System.Math.Clamp(v, 0.7, 1.5)

                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Appearance =
                                          { gs.Appearance with
                                              FontScale = clamped } })
                          )
                      ))

              yield (label4, desc4, "🎨", ctl4, "FontScale")
              let label5 = "Idioma"
              let desc5 = "Español / English — i18n futuro, hoy persiste preferencia"

              let ctl5 =
                  ComboBox.create
                      [ ComboBox.dataItems [ "es"; "en" ]
                        ComboBox.selectedItem g.Language
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> string

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                Appearance = { gs.Appearance with Language = v } })
                                    )
                                )) ]
                  :> IView

              yield (label5, desc5, "🎨", ctl5, "Language")
              let label6 = "Animaciones"
              let desc6 = "Activa transiciones — desactivar mejora batería y reduce mareo"

              let ctl6 =
                  toggleControl g.Animations (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Appearance = { gs.Appearance with Animations = on } })
                          )
                      ))

              yield (label6, desc6, "⚡", ctl6, "Animations")
              let label7 = "Alto contraste"
              let desc7 = "Refuerza bordes y texto para accesibilidad WCAG"

              let ctl7 =
                  toggleControl g.HighContrast (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Appearance = { gs.Appearance with HighContrast = on } })
                          )
                      ))

              yield (label7, desc7, "🎨", ctl7, "HighContrast")
              let label8 = "Paleta del campo"
              let desc8 = "Esquema de colores del césped en el visor táctico"

              let ctl8 =
                  ComboBox.create
                      [ ComboBox.dataItems [ "default"; "dark"; "light"; "vivid" ]
                        ComboBox.selectedItem g.PitchPalette
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> string

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                Appearance = { gs.Appearance with PitchPalette = v } })
                                    )
                                )) ]
                  :> IView

              yield (label8, desc8, "🎨", ctl8, "PitchPalette") ]

        let filtered =
            rowsData
            |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

        let rows =
            filtered
            |> List.map (fun (label, desc, impact, ctl, key) ->
                let err = errorForKey key state
                settingRow label desc impact ctl err)

        cardContainerWithReset "Apariencia" IconName.settings AppTypes.SettingsCategory.Appearance rows dispatch

    let private matchViewCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let m = state.Settings.Global.MatchView

        let rowsData: (string * string * string * IView * string) list =
            [ let label = "Velocidad de reproducción"

              let desc =
                  "5 a 60 — ticks por segundo en el visor, mayor es más fluido pero consume CPU"

              let ctl =
                  intSliderWithLabel 5 60 m.PlaybackSpeed (fun v ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with PlaybackSpeed = v } })
                          )
                      ))

              yield (label, desc, "⚡", ctl, "PlaybackSpeed")
              let label2 = "Auto-play"
              let desc2 = "Inicia al abrir el visor — útil para kiosco, molesto para análisis"

              let ctl2 =
                  toggleControl m.AutoPlay (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with AutoPlay = on } })
                          )
                      ))

              yield (label2, desc2, "⚡", ctl2, "AutoPlay")
              let label3 = "Filtro de highlights"
              let desc3 = "All / GoalsOnly / KeyMoments — reduce ruido en la timeline"

              let ctl3 =
                  ComboBox.create
                      [ ComboBox.dataItems
                            [ HighlightFilter.All; HighlightFilter.GoalsOnly; HighlightFilter.KeyMoments ]
                        ComboBox.selectedItem m.HighlightFilter
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> HighlightFilter

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                MatchView =
                                                    { gs.MatchView with
                                                        HighlightFilter = v } })
                                    )
                                )) ]
                  :> IView

              yield (label3, desc3, "🎯", ctl3, "HighlightFilter")
              let label4 = "Tick rate"
              let desc4 = "1 a 100 ms — intervalo del motor, menor es más suave pero más batería"

              let ctl4 =
                  intSliderWithLabel 1 100 m.TickRate (fun v ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with TickRate = v } })
                          )
                      ))

              yield (label4, desc4, "⚡", ctl4, "TickRate")
              let label5 = "Mostrar xG"
              let desc5 = "Superpone expected goals en el marcador"

              let ctl5 =
                  toggleControl m.ShowXG (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with ShowXG = on } })
                          )
                      ))

              yield (label5, desc5, "🎯", ctl5, "ShowXG")
              let label6 = "Mostrar posesión"
              let desc6 = "Barra de posesión por equipo en el header"

              let ctl6 =
                  toggleControl m.ShowPossession (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView =
                                          { gs.MatchView with
                                              ShowPossession = on } })
                          )
                      ))

              yield (label6, desc6, "🎯", ctl6, "ShowPossession")
              let label7 = "Mostrar overlays"
              let desc7 = "Capas tácticas y flechas — desactivar limpia la vista"

              let ctl7 =
                  toggleControl m.ShowOverlays (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with ShowOverlays = on } })
                          )
                      ))

              yield (label7, desc7, "🎨", ctl7, "ShowOverlays")
              let label8 = "Sonido"
              let desc8 = "Efectos de silbato y ambiente — requiere audio"

              let ctl8 =
                  toggleControl m.SoundEnabled (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with SoundEnabled = on } })
                          )
                      ))

              yield (label8, desc8, "🎨", ctl8, "SoundEnabled")
              let label9 = "Pausa en gol"
              let desc9 = "Detiene la reproducción al marcar — ideal para celebrar"

              let ctl9 =
                  toggleControl m.PauseOnGoal (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with PauseOnGoal = on } })
                          )
                      ))

              yield (label9, desc9, "🎯", ctl9, "PauseOnGoal")
              let label10 = "Pausa en roja"
              let desc10 = "Detiene al ver tarjeta roja — no te pierdas la polémica"

              let ctl10 =
                  toggleControl m.PauseOnRed (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      MatchView = { gs.MatchView with PauseOnRed = on } })
                          )
                      ))

              yield (label10, desc10, "🎯", ctl10, "PauseOnRed") ]

        let filtered =
            rowsData
            |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

        let rows =
            filtered
            |> List.map (fun (label, desc, impact, ctl, key) ->
                let err = errorForKey key state
                settingRow label desc impact ctl err)

        cardContainerWithReset
            "Partido — Visualización"
            IconName.soccer
            AppTypes.SettingsCategory.MatchView
            rows
            dispatch

    let private careerCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let c = state.Settings.Global.Career

        let rowsData: (string * string * string * IView * string) list =
            [ let label = "Días entre autoguardado"
              let desc = "1 a 30 — cada cuántos días de mundo se guarda la carrera"

              let ctl =
                  intSliderWithLabel 1 30 c.AutoSaveDays (fun v ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Career = { gs.Career with AutoSaveDays = v } })
                          )
                      ))

              yield (label, desc, "⚡", ctl, "AutoSaveDays")
              let label2 = "Frecuencia de noticias"
              let desc2 = "Low / Normal / High — controla ruido en el inbox"

              let ctl2 =
                  ComboBox.create
                      [ ComboBox.dataItems [ NewsFreq.Low; NewsFreq.Normal; NewsFreq.High ]
                        ComboBox.selectedItem c.NewsFreq
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> NewsFreq

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                Career = { gs.Career with NewsFreq = v } })
                                    )
                                )) ]
                  :> IView

              yield (label2, desc2, "🎯", ctl2, "NewsFreq")
              let label3 = "Agrupar inbox"
              let desc3 = "Agrupa mensajes por tema para no saturar"

              let ctl3 =
                  toggleControl c.InboxGrouping (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Career = { gs.Career with InboxGrouping = on } })
                          )
                      ))

              yield (label3, desc3, "🎨", ctl3, "InboxGrouping")
              let label4 = "Tutorial"
              let desc4 = "Muestra ayudas contextuales hasta que domines la UI"

              let ctl4 =
                  toggleControl c.Tutorial (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Career = { gs.Career with Tutorial = on } })
                          )
                      ))

              yield (label4, desc4, "🎨", ctl4, "Tutorial") ]

        let filtered =
            rowsData
            |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

        let rows =
            filtered
            |> List.map (fun (label, desc, impact, ctl, key) ->
                let err = errorForKey key state
                settingRow label desc impact ctl err)

        cardContainerWithReset "Carrera" ClubIcon.staff AppTypes.SettingsCategory.Career rows dispatch

    let private toggleControlEnabled (isOn: bool) (isEnabled: bool) (onToggle: bool -> unit) : IView =
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

    let private sliderWithLabelEnabled
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

    let private infoBanner (msg: string) : IView =
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

    let private readOnlyValue (label: string) (value: string) : IView =
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

    let private presetLabel (p: DifficultyPreset) =
        match p with
        | DifficultyPreset.Classic -> "Clásico"
        | DifficultyPreset.Realistic -> "Realista"
        | DifficultyPreset.Arcade -> "Arcade"
        | DifficultyPreset.Custom -> "Custom"

    let private labelToPreset (s: string) =
        match s with
        | "Clásico" -> DifficultyPreset.Classic
        | "Realista" -> DifficultyPreset.Realistic
        | "Arcade" -> DifficultyPreset.Arcade
        | _ -> DifficultyPreset.Custom

    let private simulationCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let sim = state.Settings.Save.Simulation
        let isCustom = sim.DifficultyPreset = DifficultyPreset.Custom
        // why effective via BalanceResolver: preset sets BalanceOverrides, so display must reflect merged config, not just defaults
        let effective = BalanceResolver.effectiveBalance (Some state.Settings.Save)

        let updateSim (f: SimulationSettings -> SimulationSettings) =
            dispatch (SettingsMsg(UpdateSave(fun s -> { s with Simulation = f s.Simulation })))

        let updateBalance (upd: BalanceConfig -> BalanceConfig) =
            dispatch (
                SettingsMsg(
                    UpdateSave(fun save ->
                        let cur =
                            save.Simulation.BalanceOverrides
                            |> Option.defaultValue BalanceConfig.defaultConfig

                        let next = upd cur

                        { save with
                            Simulation =
                                { save.Simulation with
                                    BalanceOverrides = Some next } })
                )
            )

        let presetCombo: IView =
            ComboBox.create
                [ ComboBox.dataItems [ "Clásico"; "Realista"; "Arcade"; "Custom" ]
                  ComboBox.selectedItem (presetLabel sim.DifficultyPreset)
                  ComboBox.onSelectedItemChanged (fun obj ->
                      if obj <> null then
                          let s = obj :?> string
                          let p = labelToPreset s
                          dispatch (SettingsMsg(ApplyPreset p))) ]
            :> IView

        // sliders — disabled when preset locks values
        let refCtl =
            sliderWithLabelEnabled 0.0 3.0 sim.RefStrictness isCustom (fun v -> sprintf "%.2f" v) (fun v ->
                let c = System.Math.Clamp(v, 0.0, 3.0)
                updateSim (fun s -> { s with RefStrictness = c }))

        let injuryCtl =
            sliderWithLabelEnabled 0.0 3.0 sim.InjuryFreq isCustom (fun v -> sprintf "%.2f" v) (fun v ->
                let c = System.Math.Clamp(v, 0.0, 3.0)
                updateSim (fun s -> { s with InjuryFreq = c }))

        let fatigueCtl =
            sliderWithLabelEnabled 0.0 3.0 sim.FatigueFactor isCustom (fun v -> sprintf "%.2f" v) (fun v ->
                let c = System.Math.Clamp(v, 0.0, 3.0)
                updateSim (fun s -> { s with FatigueFactor = c }))

        let weatherCtl =
            toggleControlEnabled sim.WeatherEnabled isCustom (fun on ->
                updateSim (fun s -> { s with WeatherEnabled = on }))

        let homeCtl =
            sliderWithLabelEnabled 0.0 2.0 sim.HomeAdvantage isCustom (fun v -> sprintf "%.2f" v) (fun v ->
                let c = System.Math.Clamp(v, 0.0, 2.0)
                updateSim (fun s -> { s with HomeAdvantage = c }))

        // raw Balance fields — only when Custom, otherwise read-only effective
        let maxShotCtl =
            if isCustom then
                let cur = sim.BalanceOverrides |> Option.defaultValue BalanceConfig.defaultConfig
                let v = float cur.Ball.MaxShotSpeed

                sliderWithLabelEnabled 20.0 40.0 v true (fun x -> sprintf "%.1f m/s" x) (fun nv ->
                    let c = System.Math.Clamp(nv, 20.0, 40.0) * 1.0<meterPerSecond>

                    updateBalance (fun b ->
                        { b with
                            Ball = { b.Ball with MaxShotSpeed = c } }))
            else
                Border.create
                    [ Border.background Theme.BgElevated
                      Border.cornerRadius 6.0
                      Border.padding (10.0, 4.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text (sprintf "%.1f m/s" (float effective.Ball.MaxShotSpeed))
                                TextBlock.fontSize 11.0
                                TextBlock.fontWeight FontWeight.Bold
                                TextBlock.foreground Theme.TextDim ]
                      ) ]
                :> IView

        let sprintCtl =
            if isCustom then
                let cur = sim.BalanceOverrides |> Option.defaultValue BalanceConfig.defaultConfig
                let v = float cur.Movement.SprintSpeed

                sliderWithLabelEnabled 6.0 11.0 v true (fun x -> sprintf "%.1f m/s" x) (fun nv ->
                    let c = System.Math.Clamp(nv, 6.0, 11.0) * 1.0<meterPerSecond>

                    updateBalance (fun b ->
                        { b with
                            Movement = { b.Movement with SprintSpeed = c } }))
            else
                Border.create
                    [ Border.background Theme.BgElevated
                      Border.cornerRadius 6.0
                      Border.padding (10.0, 4.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text (sprintf "%.1f m/s" (float effective.Movement.SprintSpeed))
                                TextBlock.fontSize 11.0
                                TextBlock.fontWeight FontWeight.Bold
                                TextBlock.foreground Theme.TextDim ]
                      ) ]
                :> IView

        let rowsData: (string * string * string * IView * string) list =
            [ let label = "Preset dificultad"

              let desc =
                  "Clásico = defaults, Realista baja tiro, Arcade sube velocidad — Custom desbloquea sliders"

              yield (label, desc, "🎯", presetCombo, "DifficultyPreset")
              let label2 = "Rigor arbitral"

              let desc2 =
                  "0 laxo a 3 estricto — escala probabilidad de falta y severidad de tarjeta"

              yield (label2, desc2, "🎯", refCtl, "RefStrictness")
              let label3 = "Frecuencia lesiones"
              let desc3 = "0 nunca a 3 frecuente — multiplica InjuryFreq en motor"
              yield (label3, desc3, "🎯", injuryCtl, "InjuryFreq")
              let label4 = "Factor fatiga"

              let desc4 =
                  "0 sin fatiga a 3 extremo — afecta percepción y condición además de velocidad"

              yield (label4, desc4, "🎯", fatigueCtl, "FatigueFactor")
              let label5 = "Clima activo"

              let desc5 =
                  "Activa viento/lluvia — afecta Ball.AirDrag y visibilidad si WeatherEffect>0"

              yield (label5, desc5, "🎯", weatherCtl, "WeatherEnabled")
              let label6 = "Ventaja local"
              let desc6 = "0 nula a 2 doblada — escala HomeAdvantageStrength y HomeCardBonus"
              yield (label6, desc6, "🎯", homeCtl, "HomeAdvantage")
              let label7 = "Vel. máx. tiro (Balance)"

              let desc7 =
                  "20-40 m/s — toca Ball.MaxShotSpeed en BalanceOverrides, requiere Custom"

              yield (label7, desc7, "🎯", maxShotCtl, "MaxShotSpeed")
              let label8 = "Vel. sprint (Balance)"

              let desc8 =
                  "6-11 m/s — toca Movement.SprintSpeed, visible siempre pero editable solo en Custom"

              yield (label8, desc8, "🎯", sprintCtl, "SprintSpeed") ]

        let filtered =
            rowsData
            |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

        let rows =
            filtered
            |> List.map (fun (label, desc, impact, ctl, key) ->
                let err = errorForKey key state
                settingRow label desc impact ctl err)

        let banner =
            if not isCustom then
                [ infoBanner
                      "Preset controla estos valores — cambia a Custom para editar. Balance efectivo mostrado como lectura." ]
            else
                []

        let effectivePanel =
            if not isCustom then
                [ Border.create
                      [ Border.background Theme.BgElevated
                        Border.cornerRadius 8.0
                        Border.padding (12.0, 10.0)
                        Border.margin (0.0, 6.0)
                        Border.child (
                            StackPanel.create
                                [ StackPanel.spacing 4.0
                                  StackPanel.children
                                      [ TextBlock.create
                                            [ TextBlock.text "Balance efectivo (solo lectura)"
                                              TextBlock.fontSize 10.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground Theme.TextMuted ]
                                        readOnlyValue
                                            "Tiro máx."
                                            (sprintf "%.1f m/s" (float effective.Ball.MaxShotSpeed))
                                        readOnlyValue
                                            "Sprint"
                                            (sprintf "%.1f m/s" (float effective.Movement.SprintSpeed))
                                        readOnlyValue
                                            "Falta en disputa"
                                            (sprintf "%.3f" effective.Actions.ContestFoulChance) ] ]
                        ) ]
                  :> IView ]
            else
                []

        // why StackPanel wrapper: banner + rows must share same card container, keeps layout consistent with other cards
        let allRows = banner @ rows @ effectivePanel
        cardContainerWithReset "Simulación" IconName.chart AppTypes.SettingsCategory.Simulation allRows dispatch

    let private dataModsCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let data = state.Settings.Global.Data
        let order = data.ModOrder
        let active = data.ActiveMods

        let resolvedIds =
            match state.Mode with
            | InGame(gs, _) -> gs.DataLock.Packages |> List.map _.ModId
            | _ ->
                state.ResolvedData
                |> Option.map (fun resolved -> resolved.Packages |> List.map (fun p -> p.Manifest.ModId))
                |> Option.defaultValue []

        let displayIds =
            if not order.IsEmpty then order
            elif not active.IsEmpty then active
            elif not resolvedIds.IsEmpty then resolvedIds
            else []

        let filteredIds =
            displayIds |> List.filter (fun id -> matchesSearch q id ("mod " + id))

        let swap i j lst =
            lst
            |> List.mapi (fun k v ->
                if k = i then lst[j]
                elif k = j then lst[i]
                else v)

        let modRows: IView list =
            filteredIds
            |> List.mapi (fun idx modId ->
                let isOn = List.contains modId active
                let isFirst = idx = 0
                let isLast = idx = filteredIds.Length - 1

                let toggle =
                    CheckBox.create
                        [ CheckBox.isChecked isOn
                          CheckBox.onIsCheckedChanged (fun args ->
                               match args.Source with
                               | :? CheckBox as checkBox ->
                                   dispatch (
                                       SettingsMsg(
                                           UpdateGlobal(fun gs ->
                                               let d = gs.Data

                                               if checkBox.IsChecked = System.Nullable true then
                                                   let nextActive = (modId :: d.ActiveMods) |> List.distinct

                                                   let nextOrder =
                                                       if List.contains modId d.ModOrder then
                                                           d.ModOrder
                                                       else
                                                           d.ModOrder @ [ modId ]

                                                   { gs with
                                                       Data =
                                                           { d with
                                                               ActiveMods = nextActive
                                                               ModOrder = nextOrder } }
                                               else
                                                   let nextActive = d.ActiveMods |> List.filter ((<>) modId)

                                                   { gs with
                                                       Data = { d with ActiveMods = nextActive } })
                                       )
                                   )
                               | _ -> ())
                          CheckBox.content (if isOn then "Activo" else "Inactivo")
                          CheckBox.foreground (if isOn then Theme.Success else Theme.TextMuted) ]
                    :> IView

                let upBtn =
                    Button.create
                        [ Button.content "↑"
                          Button.padding (6.0, 4.0)
                          Button.fontSize 12.0
                          Button.isEnabled (not isFirst)
                          Button.background "Transparent"
                          Button.borderBrush Theme.Border
                          Button.borderThickness 1.0
                          Button.cornerRadius 4.0
                          Button.onClick (fun _ ->
                              let curOrder = if order.IsEmpty then filteredIds else order
                              let nextOrder = swap idx (idx - 1) curOrder

                              dispatch (
                                  SettingsMsg(
                                      UpdateGlobal(fun gs ->
                                          { gs with
                                              Data = { gs.Data with ModOrder = nextOrder } })
                                  )
                              )) ]
                    :> IView

                let downBtn =
                    Button.create
                        [ Button.content "↓"
                          Button.padding (6.0, 4.0)
                          Button.fontSize 12.0
                          Button.isEnabled (not isLast)
                          Button.background "Transparent"
                          Button.borderBrush Theme.Border
                          Button.borderThickness 1.0
                          Button.cornerRadius 4.0
                          Button.onClick (fun _ ->
                              let curOrder = if order.IsEmpty then filteredIds else order
                              let nextOrder = swap idx (idx + 1) curOrder

                              dispatch (
                                  SettingsMsg(
                                      UpdateGlobal(fun gs ->
                                          { gs with
                                              Data = { gs.Data with ModOrder = nextOrder } })
                                  )
                              )) ]
                    :> IView

                let rowControl =
                    StackPanel.create
                        [ StackPanel.orientation Orientation.Horizontal
                          StackPanel.spacing 8.0
                          StackPanel.verticalAlignment VerticalAlignment.Center
                          StackPanel.children [ toggle; upBtn; downBtn ] ]
                    :> IView

                settingRow modId ("Mod " + modId + " — orden " + string (idx + 1)) "🎯" rowControl None)

        let headerActions: IView =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 8.0
                  StackPanel.children
                      [ Button.create
                            [ Button.content "Recargar Mods"
                              Button.background Theme.Accent
                              Button.foreground Theme.BgSidebar
                              Button.padding (12.0, 6.0)
                              Button.cornerRadius 6.0
                              Button.onClick (fun _ -> dispatch ReloadMods) ]
                        :> IView
                        Button.create
                            [ Button.content "Abrir carpeta Mods"
                              Button.background "Transparent"
                              Button.borderBrush Theme.Border
                              Button.borderThickness 1.0
                              Button.padding (12.0, 6.0)
                              Button.cornerRadius 6.0
                              Button.onClick (fun _ ->
                                  dispatch (
                                      NotificationMsg(
                                          PushNotification
                                              { Id = 0
                                                Icon = Material.Icons.MaterialIconKind.FolderOutline
                                                Title = "Carpeta Mods"
                                                Body = ModPaths.modsDir
                                                IsRead = false }
                                      )
                                  )) ]
                        :> IView ] ]
            :> IView

        let errorPanel: IView list =
            if state.ModLoadErrors.IsEmpty then
                []
            else
                [ Border.create
                      [ Border.background (Theme.withAlpha "14" Theme.Danger)
                        Border.borderBrush (Theme.withAlpha "40" Theme.Danger)
                        Border.borderThickness 1.0
                        Border.cornerRadius 8.0
                        Border.padding (12.0, 10.0)
                        Border.child (
                            StackPanel.create
                                [ StackPanel.spacing 6.0
                                  StackPanel.children
                                      [ TextBlock.create
                                            [ TextBlock.text "Errores de carga"
                                              TextBlock.fontSize 11.0
                                              TextBlock.fontWeight FontWeight.Bold
                                              TextBlock.foreground Theme.Danger ]
                                        for err in state.ModLoadErrors do
                                            Border.create
                                                [ Border.background (Theme.withAlpha "10" Theme.Danger)
                                                  Border.cornerRadius 6.0
                                                  Border.padding (8.0, 6.0)
                                                  Border.child (
                                                      TextBlock.create
                                                          [ TextBlock.text err
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.foreground Theme.Danger
                                                            TextBlock.textWrapping TextWrapping.Wrap ]
                                                  ) ] ] ]
                        ) ]
                  :> IView ]

        let emptyInfo: IView list =
            if modRows.IsEmpty then
                [ Border.create
                      [ Border.background Theme.BgElevated
                        Border.cornerRadius 8.0
                        Border.padding (14.0, 12.0)
                        Border.child (
                            TextBlock.create
                                [ TextBlock.text
                                      "No hay mods configurados — añade carpetas en Data/Mods o usa el Editor de Mods."
                                  TextBlock.fontSize 11.0
                                  TextBlock.foreground Theme.TextMuted
                                  TextBlock.textWrapping TextWrapping.Wrap ]
                        ) ]
                  :> IView ]
            else
                []

        let searchMatch =
            matchesSearch q "Datos" "Mods" || matchesSearch q "mods" "" || q = ""

        if not searchMatch && filteredIds.IsEmpty && q <> "" then
            Border.create [ Border.height 0.0 ] :> IView
        else
            let contentRows =
                [ yield headerActions; yield! errorPanel; yield! emptyInfo; yield! modRows ]

            // why wrapper StackPanel: first row is headerActions without settingRow chrome, rest are settingRow items
            let cardRows =
                if modRows.IsEmpty && errorPanel.IsEmpty then
                    contentRows
                else
                    contentRows

            // Use custom container to allow headerActions at top + reset per category
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
                                                                  [ Icons.iconSm IconName.fileDocumentEdit Theme.Accent
                                                                    TextBlock.create
                                                                        [ TextBlock.text "DATOS Y MODS"
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
                                                              Button.onClick (fun _ ->
                                                                  dispatch (
                                                                      SettingsMsg(
                                                                          ResetCategory AppTypes.SettingsCategory.Data
                                                                      )
                                                                  )) ]
                                                        :> IView ] ]
                                        ) ]
                                  StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children cardRows ] ] ]
                  ) ]
            :> IView

    let private scoutingCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let scout = state.Settings.Save.Scouting

        let updateScout (f: ScoutingSettings -> ScoutingSettings) =
            dispatch (SettingsMsg(UpdateSave(fun s -> { s with Scouting = f s.Scouting })))

        let aiNoiseCtl =
            sliderWithLabel 0.0 1.0 scout.AiNoise (fun v -> sprintf "%.2f" v) (fun v ->
                let c = System.Math.Clamp(v, 0.0, 1.0)
                updateScout (fun s -> { s with AiNoise = c }))

        let halfLifeCtl =
            intSliderWithLabel 1 30 (int (System.Math.Round scout.HalfLifeDays)) (fun v ->
                let c = System.Math.Clamp(v, 1, 30)
                updateScout (fun s -> { s with HalfLifeDays = float c }))

        let minConfCtl =
            sliderWithLabel 0.0 1.0 scout.MinConfidence (fun v -> sprintf "%.2f" v) (fun v ->
                let c = System.Math.Clamp(v, 0.0, 1.0)
                updateScout (fun s -> { s with MinConfidence = c }))

        let rowsData: (string * string * string * IView * string) list =
            [ let label = "Ruido IA"
              // why AiNoise 0-1: feeds ScoutConfig.AiReportNoise — 0 exact, 1 full bias, human reports inherit NoiseByAbility scaling
              let desc =
                  "0 exacto a 1 sesgo total — alimenta ScoutConfig.AiReportNoise y NoiseByAbility"

              yield (label, desc, "🎯", aiNoiseCtl, "AiNoise")
              let label2 = "Half-life memoria"
              // why HalfLifeDays 1-30: maps to ScoutConfig.MemoryHalfLifeDays — rival patterns decay, prune after 200d
              let desc2 =
                  "1-30 días — media vida del recuerdo rival, antes de RivalPruneAfterDays"

              yield (label2, desc2, "🎯", halfLifeCtl, "HalfLifeDays")
              let label3 = "Confianza mínima"
              // why MinConfidence 0-1: gates CounterConfig — under threshold report is withheld, SignatureChangePenalty applies
              let desc3 =
                  "0-1 umbral — bajo esto no se reporta, afecta CounterConfig.SignatureChangePenalty"

              yield (label3, desc3, "🎯", minConfCtl, "MinConfidence") ]

        let filtered =
            rowsData
            |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

        let rows =
            filtered
            |> List.map (fun (label, desc, impact, ctl, key) ->
                let err = errorForKey key state
                settingRow label desc impact ctl err)

        let info =
            [ infoBanner
                  "Estos valores alimentan ScoutConfig (MemoryHalfLifeDays, MinConfidenceToReport, AiReportNoise) y CounterConfig — afectan cuánto recuerda el rival y cuánto se ajusta el plan previo." ]

        let allRows = info @ rows
        cardContainerWithReset "Scouting" ClubIcon.scouting AppTypes.SettingsCategory.Scouting allRows dispatch

    let private warningBanner (msg: string) : IView =
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

    let private motorSection (title: string) (rows: IView list) : IView =
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

    let private advancedCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let adv = state.Settings.Global.Advanced
        // why effective via BalanceResolver: shows merged config, respects preset overrides and fallback to defaults
        let effective = BalanceResolver.effectiveBalance (Some state.Settings.Save)

        let curBalance =
            state.Settings.Save.Simulation.BalanceOverrides
            |> Option.defaultValue BalanceConfig.defaultConfig

        let updateBalance (upd: BalanceConfig -> BalanceConfig) =
            dispatch (
                SettingsMsg(
                    UpdateSave(fun save ->
                        let cur =
                            save.Simulation.BalanceOverrides
                            |> Option.defaultValue BalanceConfig.defaultConfig

                        let next = upd cur

                        { save with
                            Simulation =
                                { save.Simulation with
                                    BalanceOverrides = Some next } })
                )
            )

        let rowsData: (string * string * string * IView * string) list =
            [ let label = "Desbloquear editor motor"

              let desc =
                  "Permite abrir el editor crudo de BalanceConfig — solo depuración, sin validación extra"

              let ctl =
                  toggleControl adv.UnlockMotorEditor (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Advanced =
                                          { gs.Advanced with
                                              UnlockMotorEditor = on } })
                          )
                      ))

              yield (label, desc, "⚡", ctl, "UnlockMotorEditor")
              let label2 = "Mostrar debug"
              let desc2 = "Superpone IDs y ticks — útil para QA, ruidoso para juego normal"

              let ctl2 =
                  toggleControl adv.ShowDebug (fun on ->
                      dispatch (
                          SettingsMsg(
                              UpdateGlobal(fun gs ->
                                  { gs with
                                      Advanced = { gs.Advanced with ShowDebug = on } })
                          )
                      ))

              yield (label2, desc2, "⚡", ctl2, "ShowDebug")
              let label3 = "Nivel de log"

              let desc3 =
                  "Silent/Normal/Verbose — controla LogLevel y verbosidad del pipeline de mods"

              let ctl3 =
                  ComboBox.create
                      [ ComboBox.dataItems [ LogLevel.Silent; LogLevel.Normal; LogLevel.Verbose ]
                        ComboBox.selectedItem adv.LogLevel
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> LogLevel

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                Advanced = { gs.Advanced with LogLevel = v } })
                                    )
                                )) ]
                  :> IView

              yield (label3, desc3, "⚡", ctl3, "LogLevel") ]

        let filtered =
            rowsData
            |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

        let baseRows =
            filtered
            |> List.map (fun (label, desc, impact, ctl, key) ->
                let err = errorForKey key state in settingRow label desc impact ctl err)

        // why early return when locked: task says when UnlockMotorEditor=false just show Toggle and info, hides motor sliders
        if not adv.UnlockMotorEditor then
            let lockedInfo =
                if baseRows.IsEmpty then
                    []
                else
                    [ infoBanner
                          "Activa para editar motor — desbloquea los deslizadores de BalanceConfig. Cambios requieren Custom preset." ]

            let allRows = lockedInfo @ baseRows
            cardContainerWithReset "Avanzado" IconName.training AppTypes.SettingsCategory.Advanced allRows dispatch
        else
            let warning =
                warningBanner "Editor motor desbloqueado — cambios afectan toda la simulación"

            // why validation banner inline: ConfigValidation.validateAll is single source, surfaced here so user sees broken ranges immediately
            let validationErrors =
                match ConfigValidation.validateAll effective with
                | Ok _ -> []
                | Error errs ->
                    [ Border.create
                          [ Border.background (Theme.withAlpha "14" Theme.Danger)
                            Border.borderBrush (Theme.withAlpha "40" Theme.Danger)
                            Border.borderThickness 1.0
                            Border.cornerRadius 8.0
                            Border.padding (12.0, 10.0)
                            Border.child (
                                StackPanel.create
                                    [ StackPanel.spacing 6.0
                                      StackPanel.children
                                          [ TextBlock.create
                                                [ TextBlock.text "Errores de validación BalanceConfig"
                                                  TextBlock.fontSize 11.0
                                                  TextBlock.fontWeight FontWeight.Bold
                                                  TextBlock.foreground Theme.Danger ]
                                            for e in errs do
                                                Border.create
                                                    [ Border.background (Theme.withAlpha "10" Theme.Danger)
                                                      Border.cornerRadius 6.0
                                                      Border.padding (8.0, 6.0)
                                                      Border.child (
                                                          TextBlock.create
                                                              [ TextBlock.text e
                                                                TextBlock.fontSize 10.0
                                                                TextBlock.foreground Theme.Danger
                                                                TextBlock.textWrapping TextWrapping.Wrap ]
                                                      ) ] ] ]
                            ) ]
                      :> IView ]

            // helpers for motor sliders — each dispatches UpdateSave with BalanceOverrides = Some {effective with ...}
            let ballGravityCtl =
                sliderWithLabel 0.0 20.0 (float curBalance.Ball.Gravity) (fun v -> sprintf "%.2f m/s²" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 20.0) * 1.0<meterPerSecondSquared>

                    updateBalance (fun b ->
                        { b with
                            Ball = { b.Ball with Gravity = c } }))

            let ballAirDragCtl =
                sliderWithLabel 0.0 1.0 curBalance.Ball.AirDrag (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Ball = { b.Ball with AirDrag = c } }))

            let ballMaxShotCtl =
                sliderWithLabel 10.0 40.0 (float curBalance.Ball.MaxShotSpeed) (fun v -> sprintf "%.1f m/s" v) (fun v ->
                    let c = System.Math.Clamp(v, 10.0, 40.0) * 1.0<meterPerSecond>

                    updateBalance (fun b ->
                        { b with
                            Ball = { b.Ball with MaxShotSpeed = c } }))

            let ballBounceCtl =
                sliderWithLabel 0.0 1.0 curBalance.Ball.BounceRestitution (fun v -> sprintf "%.2f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Ball = { b.Ball with BounceRestitution = c } }))

            let sprintCtl =
                sliderWithLabel
                    5.0
                    12.0
                    (float curBalance.Movement.SprintSpeed)
                    (fun v -> sprintf "%.1f m/s" v)
                    (fun v ->
                        let c = System.Math.Clamp(v, 5.0, 12.0) * 1.0<meterPerSecond>

                        updateBalance (fun b ->
                            { b with
                                Movement = { b.Movement with SprintSpeed = c } }))

            let accelCtl =
                sliderWithLabel
                    0.5
                    10.0
                    (float curBalance.Movement.Acceleration)
                    (fun v -> sprintf "%.2f m/s²" v)
                    (fun v ->
                        let c = System.Math.Clamp(v, 0.5, 10.0) * 1.0<meterPerSecondSquared>

                        updateBalance (fun b ->
                            { b with
                                Movement = { b.Movement with Acceleration = c } }))

            let decisionTempCtl =
                sliderWithLabel
                    0.1
                    5.0
                    curBalance.Actions.DecisionSoftmaxTemperature
                    (fun v -> sprintf "%.2f" v)
                    (fun v ->
                        let c = System.Math.Clamp(v, 0.1, 5.0)

                        updateBalance (fun b ->
                            { b with
                                Actions =
                                    { b.Actions with
                                        DecisionSoftmaxTemperature = c } }))

            let passQualityCtl =
                sliderWithLabel 0.0 1.0 curBalance.Actions.PassBaseQuality (fun v -> sprintf "%.2f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Actions = { b.Actions with PassBaseQuality = c } }))

            let contestFoulCtl =
                sliderWithLabel 0.0 1.0 curBalance.Actions.ContestFoulChance (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Actions = { b.Actions with ContestFoulChance = c } }))

            let duelFoulCtl =
                sliderWithLabel 0.0 1.0 curBalance.Actions.DuelFoulChance (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Actions = { b.Actions with DuelFoulChance = c } }))

            let urgencyDeficitCtl =
                intSliderWithLabel 0 5 curBalance.Team.UrgencyMaxDeficit (fun v ->
                    let c = System.Math.Clamp(v, 0, 5)

                    updateBalance (fun b ->
                        { b with
                            Team = { b.Team with UrgencyMaxDeficit = c } }))

            let directiveHystCtl =
                sliderWithLabel 0.0 1.0 curBalance.Team.DirectiveHysteresis (fun v -> sprintf "%.2f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Team = { b.Team with DirectiveHysteresis = c } }))

            let foulYellowCtl =
                sliderWithLabel 0.0 1.0 curBalance.Referee.FoulYellowWorthyChance (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Referee =
                                { b.Referee with
                                    FoulYellowWorthyChance = c } }))

            let foulRedCtl =
                sliderWithLabel 0.0 1.0 curBalance.Referee.FoulRedWorthyChance (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Referee =
                                { b.Referee with
                                    FoulRedWorthyChance = c } }))

            let homeCardCtl =
                sliderWithLabel 0.0 1.0 curBalance.Referee.HomeCardBonus (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.0, 1.0)

                    updateBalance (fun b ->
                        { b with
                            Referee = { b.Referee with HomeCardBonus = c } }))

            let homeAdvCtl =
                sliderWithLabel
                    0.0
                    1.0
                    curBalance.Environment.HomeAdvantageStrength
                    (fun v -> sprintf "%.2f" v)
                    (fun v ->
                        let c = System.Math.Clamp(v, 0.0, 1.0)

                        updateBalance (fun b ->
                            { b with
                                Environment =
                                    { b.Environment with
                                        HomeAdvantageStrength = c } }))

            let motorRowsData: (string * string * string * IView * string) list =
                [ yield
                      ("Ball.Gravity",
                       "0-20 m/s² — 9.81 real, 0 flota, >20 cae como piedra",
                       "🎯",
                       ballGravityCtl,
                       "Gravity")
                  yield
                      ("Ball.AirDrag",
                       "0-1 retención por tick — >1 acelera, <0 invierte",
                       "⚡",
                       ballAirDragCtl,
                       "AirDrag")
                  yield
                      ("Ball.MaxShotSpeed",
                       "10-40 m/s — 32 default, 40 es récord mundial",
                       "🎯",
                       ballMaxShotCtl,
                       "MaxShotSpeed")
                  yield
                      ("Ball.BounceRestitution",
                       "0-1 rebote — >1 crea energía, 0 no rebota",
                       "⚡",
                       ballBounceCtl,
                       "BounceRestitution")
                  yield
                      ("Movement.SprintSpeed", "5-12 m/s — Velocidad punta, 8.5 default", "⚡", sprintCtl, "SprintSpeed")
                  yield
                      ("Movement.Acceleration", "0.5-10 m/s² — 4 default, >10 teleport", "⚡", accelCtl, "Acceleration")
                  yield
                      ("Actions.DecisionSoftmaxTemperature",
                       "0.1-5 — 1 default, 0.1 rígido, 5 aleatorio",
                       "🎯",
                       decisionTempCtl,
                       "DecisionSoftmaxTemperature")
                  yield
                      ("Actions.PassBaseQuality",
                       "0-1 calidad base de pase — 0.7 default",
                       "🎯",
                       passQualityCtl,
                       "PassBaseQuality")
                  yield
                      ("Actions.ContestFoulChance",
                       "0-1 falta en disputa — 0.05 default",
                       "🎯",
                       contestFoulCtl,
                       "ContestFoulChance")
                  yield
                      ("Actions.DuelFoulChance",
                       "0-1 falta en duelo — 0.15 default",
                       "🎯",
                       duelFoulCtl,
                       "DuelFoulChance")
                  yield
                      ("Team.UrgencyMaxDeficit",
                       "0-5 déficit máx para urgencia — 2 default",
                       "🎯",
                       urgencyDeficitCtl,
                       "UrgencyMaxDeficit")
                  yield
                      ("Team.DirectiveHysteresis",
                       "0-1 histéresis directiva — 0.08 default, >1 nunca cambia",
                       "🎯",
                       directiveHystCtl,
                       "DirectiveHysteresis")
                  yield
                      ("Referee.FoulYellowWorthyChance",
                       "0-1 prob. amarilla — 0.15 default",
                       "🎯",
                       foulYellowCtl,
                       "FoulYellowWorthyChance")
                  yield
                      ("Referee.FoulRedWorthyChance",
                       "0-1 prob. roja — 0.05 default",
                       "🎯",
                       foulRedCtl,
                       "FoulRedWorthyChance")
                  yield
                      ("Referee.HomeCardBonus",
                       "0-1 sesgo local tarjeta — 0.05 default",
                       "🎯",
                       homeCardCtl,
                       "HomeCardBonus")
                  yield
                      ("Environment.HomeAdvantageStrength",
                       "0-1 fuerza ventaja local — 0.1 default",
                       "🎯",
                       homeAdvCtl,
                       "HomeAdvantageStrength") ]

            let motorFiltered =
                motorRowsData
                |> List.filter (fun (label, desc, _, _, _) -> matchesSearch q label desc)

            let motorRows =
                motorFiltered
                |> List.map (fun (label, desc, impact, ctl, key) ->
                    let err = errorForKey key state
                    settingRow label desc impact ctl err)

            let ballSection =
                let ballRows =
                    motorRows
                    |> List.indexed
                    |> List.choose (fun (i, r) -> if i < 4 then Some r else None)

                if ballRows.IsEmpty then
                    []
                else
                    [ motorSection "Ball — física y tiro" ballRows ]

            let movementSection =
                let mvRows = motorRows |> List.skip 4 |> List.take 2

                if mvRows.IsEmpty then
                    []
                else
                    [ motorSection "Movement — velocidad y aceleración" mvRows ]

            let actionsSection =
                let actRows = motorRows |> List.skip 6 |> List.take 4

                if actRows.IsEmpty then
                    []
                else
                    [ motorSection "Actions — decisiones y faltas" actRows ]

            let teamSection =
                let tRows = motorRows |> List.skip 10 |> List.take 2

                if tRows.IsEmpty then
                    []
                else
                    [ motorSection "Team — urgencia e histéresis" tRows ]

            let refereeSection =
                let refRows = motorRows |> List.skip 12 |> List.take 3

                if refRows.IsEmpty then
                    []
                else
                    [ motorSection "Referee — tarjetas y sesgo local" refRows ]

            let envSection =
                let eRows = motorRows |> List.skip 15

                if eRows.IsEmpty then
                    []
                else
                    [ motorSection "Environment — ventaja local" eRows ]

            let restoreBtn: IView =
                Button.create
                    [ Button.content "Restaurar Balance por defecto"
                      Button.background Theme.Accent
                      Button.foreground Theme.BgSidebar
                      Button.padding (14.0, 8.0)
                      Button.cornerRadius 6.0
                      Button.horizontalAlignment HorizontalAlignment.Stretch
                      Button.onClick (fun _ ->
                          dispatch (
                              SettingsMsg(
                                  UpdateSave(fun save ->
                                      { save with
                                          Simulation =
                                              { save.Simulation with
                                                  BalanceOverrides = None } })
                              )
                          )) ]
                :> IView

            let importExportPanel: IView =
                Border.create
                    [ Border.background Theme.BgElevated
                      Border.cornerRadius 8.0
                      Border.padding (12.0, 10.0)
                      Border.margin (0.0, 6.0)
                      Border.child (
                          StackPanel.create
                              [ StackPanel.spacing 8.0
                                StackPanel.children
                                    [ TextBlock.create
                                          [ TextBlock.text "Import / Export JSON"
                                            TextBlock.fontSize 11.0
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.foreground Theme.TextMain ]
                                      TextBox.create
                                          [ TextBox.watermark "Pega JSON exportado aquí para importar..."
                                            TextBox.text importJsonDraft
                                            TextBox.height 80.0
                                            TextBox.acceptsReturn true
                                            TextBox.textWrapping TextWrapping.Wrap
                                            TextBox.onTextChanged (fun t -> importJsonDraft <- t) ]
                                      StackPanel.create
                                          [ StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 8.0
                                            StackPanel.children
                                                [ Button.create
                                                      [ Button.content "Exportar"
                                                        Button.background Theme.Accent
                                                        Button.foreground Theme.BgSidebar
                                                        Button.padding (12.0, 6.0)
                                                        Button.cornerRadius 6.0
                                                        Button.onClick (fun _ ->
                                                            try
                                                                let json = SettingsSlice.exportState state.Settings
                                                                dispatch (SettingsMsg Export)
                                                            with _ ->
                                                                ()) ]
                                                  :> IView
                                                  Button.create
                                                      [ Button.content "Importar"
                                                        Button.background Theme.BgCard
                                                        Button.foreground Theme.TextMain
                                                        Button.borderBrush Theme.Border
                                                        Button.borderThickness 1.0
                                                        Button.padding (12.0, 6.0)
                                                        Button.cornerRadius 6.0
                                                        Button.onClick (fun _ ->
                                                            dispatch (SettingsMsg(Import importJsonDraft))) ]
                                                  :> IView ] ]
                                      TextBlock.create
                                          [ TextBlock.text
                                                "Export copia JSON al portapapeles y al log; Import valida y notifica error si falla."
                                            TextBlock.fontSize 10.0
                                            TextBlock.foreground Theme.TextDim
                                            TextBlock.textWrapping TextWrapping.Wrap ] ] ]
                      ) ]
                :> IView

            let motorContent =
                ballSection
                @ movementSection
                @ actionsSection
                @ teamSection
                @ refereeSection
                @ envSection

            let allRows =
                [ yield warning
                  yield! validationErrors
                  yield! baseRows
                  yield! motorContent
                  yield restoreBtn
                  yield importExportPanel ]

            cardContainerWithReset "Avanzado" IconName.training AppTypes.SettingsCategory.Advanced allRows dispatch


    let private diffItem (label: string) (value: string) : IView =
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

    let private previewPanel (state: State) (dispatch: Msg -> unit) : IView =
        let g = state.Settings.Global
        let m = g.MatchView
        let c = g.Career
        let sim = state.Settings.Save.Simulation
        let scout = state.Settings.Save.Scouting
        let data = g.Data
        let adv = g.Advanced

        let diffs: (string * string) list =
            [ if g.Appearance <> AppearanceSettings.defaults then
                  if g.Appearance.ThemeMode <> AppearanceSettings.defaults.ThemeMode then
                      yield ("Tema", $"{AppearanceSettings.defaults.ThemeMode} → {g.Appearance.ThemeMode}")

                  if g.Appearance.Accent <> AppearanceSettings.defaults.Accent then
                      yield ("Acento", $"{AppearanceSettings.defaults.Accent} → {g.Appearance.Accent}")

                  if g.Appearance.FontScale <> AppearanceSettings.defaults.FontScale then
                      yield ("Fuente", $"{AppearanceSettings.defaults.FontScale:F2} → {g.Appearance.FontScale:F2}")

                  if g.Appearance.Density <> AppearanceSettings.defaults.Density then
                      yield ("Densidad", $"{AppearanceSettings.defaults.Density} → {g.Appearance.Density}")

                  if g.Appearance.Language <> AppearanceSettings.defaults.Language then
                      yield ("Idioma", $"{AppearanceSettings.defaults.Language} → {g.Appearance.Language}")
              if m <> MatchViewSettings.defaults then
                  if m.PlaybackSpeed <> MatchViewSettings.defaults.PlaybackSpeed then
                      yield ("Vel. reprod.", $"{MatchViewSettings.defaults.PlaybackSpeed} → {m.PlaybackSpeed}")

                  if m.TickRate <> MatchViewSettings.defaults.TickRate then
                      yield ("Tick rate", $"{MatchViewSettings.defaults.TickRate} → {m.TickRate}")

                  if m.HighlightFilter <> MatchViewSettings.defaults.HighlightFilter then
                      yield ("Highlights", $"{MatchViewSettings.defaults.HighlightFilter} → {m.HighlightFilter}")
              if c <> CareerSettings.defaults then
                  if c.AutoSaveDays <> CareerSettings.defaults.AutoSaveDays then
                      yield ("Autoguardado", $"{CareerSettings.defaults.AutoSaveDays}d → {c.AutoSaveDays}d")

                  if c.NewsFreq <> CareerSettings.defaults.NewsFreq then
                      yield ("Noticias", $"{CareerSettings.defaults.NewsFreq} → {c.NewsFreq}")
              if sim <> SimulationSettings.defaults then
                  if sim.DifficultyPreset <> SimulationSettings.defaults.DifficultyPreset then
                      yield ("Preset", $"{SimulationSettings.defaults.DifficultyPreset} → {sim.DifficultyPreset}")

                  if sim.RefStrictness <> SimulationSettings.defaults.RefStrictness then
                      yield ("Árbitro", $"{SimulationSettings.defaults.RefStrictness:F2} → {sim.RefStrictness:F2}")

                  if sim.InjuryFreq <> SimulationSettings.defaults.InjuryFreq then
                      yield ("Lesiones", $"{SimulationSettings.defaults.InjuryFreq:F2} → {sim.InjuryFreq:F2}")

                  if sim.FatigueFactor <> SimulationSettings.defaults.FatigueFactor then
                      yield ("Fatiga", $"{SimulationSettings.defaults.FatigueFactor:F2} → {sim.FatigueFactor:F2}")

                  if sim.WeatherEnabled <> SimulationSettings.defaults.WeatherEnabled then
                      yield ("Clima", $"{SimulationSettings.defaults.WeatherEnabled} → {sim.WeatherEnabled}")

                  if sim.HomeAdvantage <> SimulationSettings.defaults.HomeAdvantage then
                      yield ("Vent. local", $"{SimulationSettings.defaults.HomeAdvantage:F2} → {sim.HomeAdvantage:F2}")

                  if
                      sim.BalanceOverrides.IsSome
                      <> SimulationSettings.defaults.BalanceOverrides.IsSome
                  then
                      yield
                          ("Balance",
                           if sim.BalanceOverrides.IsSome then
                               "override activo"
                           else
                               "sin override")
              if scout <> ScoutingSettings.defaults then
                  if scout.AiNoise <> ScoutingSettings.defaults.AiNoise then
                      yield ("Ruido IA", $"{ScoutingSettings.defaults.AiNoise:F2} → {scout.AiNoise:F2}")

                  if scout.HalfLifeDays <> ScoutingSettings.defaults.HalfLifeDays then
                      yield ("Half-life", $"{ScoutingSettings.defaults.HalfLifeDays:F1}d → {scout.HalfLifeDays:F1}d")

                  if scout.MinConfidence <> ScoutingSettings.defaults.MinConfidence then
                      yield
                          ("Confianza mín.", $"{ScoutingSettings.defaults.MinConfidence:F2} → {scout.MinConfidence:F2}")
              if data <> DataSettings.defaults then
                  if data.ActiveMods <> DataSettings.defaults.ActiveMods then
                      yield ("Mods activos", $"{data.ActiveMods.Length} activos")

                  if data.ModOrder <> DataSettings.defaults.ModOrder then
                      yield ("Orden Mods", String.concat ", " data.ModOrder)
              if adv <> AdvancedSettings.defaults then
                  if adv.UnlockMotorEditor <> AdvancedSettings.defaults.UnlockMotorEditor then
                      yield ("Motor", $"{AdvancedSettings.defaults.UnlockMotorEditor} → {adv.UnlockMotorEditor}")

                  if adv.ShowDebug <> AdvancedSettings.defaults.ShowDebug then
                      yield ("Debug", $"{AdvancedSettings.defaults.ShowDebug} → {adv.ShowDebug}")

                  if adv.LogLevel <> AdvancedSettings.defaults.LogLevel then
                      yield ("Log", $"{AdvancedSettings.defaults.LogLevel} → {adv.LogLevel}") ]

        Border.create
            [ Border.width 250.0
              Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness (1.0, 0.0, 0.0, 0.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ Border.create
                                  [ DockPanel.dock Dock.Top
                                    Border.background Theme.BgCard
                                    Border.padding (16.0, 11.0)
                                    Border.borderBrush Theme.Border
                                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                    Border.child (
                                        StackPanel.create
                                            [ StackPanel.orientation Orientation.Horizontal
                                              StackPanel.spacing 8.0
                                              StackPanel.children
                                                  [ Icons.iconSm IconName.info Theme.Accent
                                                    TextBlock.create
                                                        [ TextBlock.text "PREVIEW"
                                                          TextBlock.fontSize 10.0
                                                          TextBlock.fontWeight FontWeight.Black
                                                          TextBlock.foreground Theme.TextMuted ] ] ]
                                    ) ]
                              ScrollViewer.create
                                  [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                    ScrollViewer.content (
                                        StackPanel.create
                                            [ StackPanel.margin (16.0, 14.0)
                                              StackPanel.spacing 12.0
                                              StackPanel.children
                                                  [ // Live swatch for accent
                                                    Border.create
                                                        [ Border.background Theme.BgElevated
                                                          Border.cornerRadius 8.0
                                                          Border.padding (12.0, 10.0)
                                                          Border.child (
                                                              StackPanel.create
                                                                  [ StackPanel.spacing 8.0
                                                                    StackPanel.children
                                                                        [ TextBlock.create
                                                                              [ TextBlock.text "Swatch en vivo"
                                                                                TextBlock.fontSize 10.0
                                                                                TextBlock.fontWeight FontWeight.Bold
                                                                                TextBlock.foreground Theme.TextMuted ]
                                                                          Border.create
                                                                              [ Border.background g.Appearance.Accent
                                                                                Border.cornerRadius 6.0
                                                                                Border.height 36.0
                                                                                Border.child (
                                                                                    TextBlock.create
                                                                                        [ TextBlock.text
                                                                                              g.Appearance.Accent
                                                                                          TextBlock.fontSize 11.0
                                                                                          TextBlock.fontWeight
                                                                                              FontWeight.Bold
                                                                                          TextBlock.foreground "#ffffff"
                                                                                          TextBlock.horizontalAlignment
                                                                                              HorizontalAlignment.Center
                                                                                          TextBlock.verticalAlignment
                                                                                              VerticalAlignment.Center ]
                                                                                ) ]
                                                                          Grid.create
                                                                              [ Grid.columnDefinitions "*, *"
                                                                                Grid.children
                                                                                    [ StackPanel.create
                                                                                          [ Grid.column 0
                                                                                            StackPanel.spacing 2.0
                                                                                            StackPanel.children
                                                                                                [ TextBlock.create
                                                                                                      [ TextBlock.text
                                                                                                            "Fuente"
                                                                                                        TextBlock.fontSize
                                                                                                            9.0
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextDim ]
                                                                                                  TextBlock.create
                                                                                                      [ TextBlock.text (
                                                                                                            sprintf
                                                                                                                "%.2fx"
                                                                                                                g.Appearance.FontScale
                                                                                                        )
                                                                                                        TextBlock.fontSize
                                                                                                            11.0
                                                                                                        TextBlock.fontWeight
                                                                                                            FontWeight.Bold
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextMain ] ] ]
                                                                                      StackPanel.create
                                                                                          [ Grid.column 1
                                                                                            StackPanel.spacing 2.0
                                                                                            StackPanel.children
                                                                                                [ TextBlock.create
                                                                                                      [ TextBlock.text
                                                                                                            "Tema"
                                                                                                        TextBlock.fontSize
                                                                                                            9.0
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextDim ]
                                                                                                  TextBlock.create
                                                                                                      [ TextBlock.text (
                                                                                                            string
                                                                                                                g.Appearance.ThemeMode
                                                                                                        )
                                                                                                        TextBlock.fontSize
                                                                                                            11.0
                                                                                                        TextBlock.fontWeight
                                                                                                            FontWeight.Bold
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextMain ] ] ] ] ] ] ]
                                                          ) ]
                                                    Border.create [ Border.height 1.0; Border.background Theme.Border ]
                                                    StackPanel.create
                                                        [ StackPanel.spacing 6.0
                                                          StackPanel.children
                                                              [ TextBlock.create
                                                                    [ TextBlock.text "Cambios vs Default"
                                                                      TextBlock.fontSize 11.0
                                                                      TextBlock.fontWeight FontWeight.Bold
                                                                      TextBlock.foreground Theme.TextMain ]
                                                                if diffs.IsEmpty then
                                                                    TextBlock.create
                                                                        [ TextBlock.text
                                                                              "Sin cambios — todo en valores por defecto"
                                                                          TextBlock.fontSize 11.0
                                                                          TextBlock.foreground Theme.TextMuted
                                                                          TextBlock.textWrapping TextWrapping.Wrap ]
                                                                else
                                                                    StackPanel.create
                                                                        [ StackPanel.spacing 2.0
                                                                          StackPanel.children (
                                                                              diffs
                                                                              |> List.map (fun (l, v) -> diffItem l v)
                                                                          ) ] ] ]
                                                    if not state.Settings.ValidationErrors.IsEmpty then
                                                        StackPanel.create
                                                            [ StackPanel.spacing 6.0
                                                              StackPanel.children
                                                                  [ Border.create
                                                                        [ Border.height 1.0
                                                                          Border.background Theme.Border ]
                                                                    TextBlock.create
                                                                        [ TextBlock.text "Validación"
                                                                          TextBlock.fontSize 11.0
                                                                          TextBlock.fontWeight FontWeight.Bold
                                                                          TextBlock.foreground Theme.Danger ]
                                                                    for KeyValue(k, v) in
                                                                        state.Settings.ValidationErrors do
                                                                        Border.create
                                                                            [ Border.background (
                                                                                  Theme.withAlpha "12" Theme.Danger
                                                                              )
                                                                              Border.cornerRadius 6.0
                                                                              Border.padding (8.0, 6.0)
                                                                              Border.margin (0.0, 2.0)
                                                                              Border.child (
                                                                                  TextBlock.create
                                                                                      [ TextBlock.text (
                                                                                            if k = v then
                                                                                                k
                                                                                            else
                                                                                                $"{k}: {v}"
                                                                                        )
                                                                                        TextBlock.fontSize 10.0
                                                                                        TextBlock.foreground
                                                                                            Theme.Danger
                                                                                        TextBlock.textWrapping
                                                                                            TextWrapping.Wrap ]
                                                                              ) ] ] ]
                                                    Border.create [ Border.height 1.0; Border.background Theme.Border ]
                                                    StackPanel.create
                                                        [ StackPanel.spacing 8.0
                                                          StackPanel.children
                                                              [ TextBlock.create
                                                                    [ TextBlock.text "Acciones rápidas"
                                                                      TextBlock.fontSize 11.0
                                                                      TextBlock.fontWeight FontWeight.Bold
                                                                      TextBlock.foreground Theme.TextMain ]
                                                                Button.create
                                                                    [ Button.content "Guardar ahora"
                                                                      Button.background Theme.Accent
                                                                      Button.foreground Theme.BgSidebar
                                                                      Button.padding (14.0, 8.0)
                                                                      Button.cornerRadius 6.0
                                                                      Button.horizontalAlignment
                                                                          HorizontalAlignment.Stretch
                                                                      Button.onClick (fun _ ->
                                                                          dispatch (SettingsMsg SaveSettings)) ]
                                                                Button.create
                                                                    [ Button.content "Restaurar todo"
                                                                      Button.background "Transparent"
                                                                      Button.borderBrush Theme.Border
                                                                      Button.borderThickness 1.0
                                                                      Button.padding (14.0, 8.0)
                                                                      Button.cornerRadius 6.0
                                                                      Button.horizontalAlignment
                                                                          HorizontalAlignment.Stretch
                                                                      Button.onClick (fun _ ->
                                                                          dispatch (SettingsMsg ResetAll)) ] ] ] ] ]
                                    ) ] ] ]
              ) ]
        :> IView

    let private centerContent (state: State) (dispatch: Msg -> unit) : IView =
        let selected = state.Settings.SelectedCategory

        let contentChildren: IView list =
            match selected with
            | AppTypes.SettingsCategory.Appearance -> [ appearanceCard state dispatch ]
            | AppTypes.SettingsCategory.MatchView -> [ matchViewCard state dispatch ]
            | AppTypes.SettingsCategory.Career -> [ careerCard state dispatch ]
            | AppTypes.SettingsCategory.Simulation -> [ simulationCard state dispatch ]
            | AppTypes.SettingsCategory.Data -> [ dataModsCard state dispatch ]
            | AppTypes.SettingsCategory.Scouting -> [ scoutingCard state dispatch ]
            | AppTypes.SettingsCategory.Advanced -> [ advancedCard state dispatch ]
            | AppTypes.SettingsCategory.All ->
                [ appearanceCard state dispatch
                  matchViewCard state dispatch
                  careerCard state dispatch
                  simulationCard state dispatch
                  dataModsCard state dispatch
                  scoutingCard state dispatch
                  advancedCard state dispatch ]

        // why ScrollViewer as center: keeps header + nav fixed while content scrolls, avoids nested scroll sync issues
        ScrollViewer.create
            [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
              ScrollViewer.padding (16.0, 16.0)
              ScrollViewer.background Theme.BgMain
              ScrollViewer.content (
                  StackPanel.create
                      [ StackPanel.maxWidth 780.0
                        StackPanel.horizontalAlignment HorizontalAlignment.Stretch
                        StackPanel.spacing 0.0
                        StackPanel.children contentChildren ]
              ) ]
        :> IView

    let view (state: State) (dispatch: Msg -> unit) : IView =
        DockPanel.create
            [ DockPanel.background Theme.BgMain
              DockPanel.children
                  [ headerView state dispatch
                    |> fun h -> Border.create [ Border.dock Dock.Top; Border.child h ]
                    Grid.create
                        [ Grid.columnDefinitions "200, *, 250"
                          Grid.children
                              [ Border.create [ Grid.column 0; Border.child (navView state dispatch) ]
                                Border.create [ Grid.column 1; Border.child (centerContent state dispatch) ]
                                Border.create [ Grid.column 2; Border.child (previewPanel state dispatch) ] ] ] ] ]
        :> IView
