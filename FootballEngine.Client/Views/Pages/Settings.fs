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
open FootballEngine.Pages.SettingsWidgets
open FootballEngine.Pages.SettingsCards
open FootballEngine.Pages.SettingsAdvanced

module Settings =

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
                                                                                                            "Acento"
                                                                                                        TextBlock.fontSize
                                                                                                            9.0
                                                                                                        TextBlock.foreground
                                                                                                            Theme.TextDim ]
                                                                                                  TextBlock.create
                                                                                                      [ TextBlock.text (
                                                                                                            g.Appearance.Accent
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
