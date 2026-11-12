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

/// One card per everyday settings category.
module internal SettingsCards =

    let internal appearanceCard (state: State) (dispatch: Msg -> unit) : IView =
        let q = state.Settings.SearchQuery
        let g = state.Settings.Global.Appearance

        let rowsData: (string * string * string * IView * string) list =
            [ let label2 = "Color de acento"
              let desc2 = "Hex como #C8F031 — usado en botones y enlaces, validado en vivo"

              let ctl2 =
                  TextBox.create
                      [ TextBox.text g.Accent
                        TextBox.placeholderText "#00d492"
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

    let internal matchViewCard (state: State) (dispatch: Msg -> unit) : IView =
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
              let labelQ = "Calidad 3D"
              let descQ = "Low / Medium / High / Ultra — se aplica al abrir el próximo partido"

              let ctlQ =
                  ComboBox.create
                      [ ComboBox.dataItems ViewerQualityTier.all
                        ComboBox.selectedItem m.ViewerQuality
                        ComboBox.onSelectedItemChanged (fun obj ->
                            if obj <> null then
                                let v = obj :?> ViewerQualityTier

                                dispatch (
                                    SettingsMsg(
                                        UpdateGlobal(fun gs ->
                                            { gs with
                                                MatchView = { gs.MatchView with ViewerQuality = v } })
                                    )
                                )) ]
                  :> IView

              yield (labelQ, descQ, "🎮", ctlQ, "ViewerQuality")
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

    let internal careerCard (state: State) (dispatch: Msg -> unit) : IView =
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

    let internal simulationCard (state: State) (dispatch: Msg -> unit) : IView =
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
                  "Activa viento/lluvia — afecta la resistencia del aire y la visibilidad si WeatherEffect>0"

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
                                        ] ]
                        ) ]
                  :> IView ]
            else
                []

        // why StackPanel wrapper: banner + rows must share same card container, keeps layout consistent with other cards
        let allRows = banner @ rows @ effectivePanel
        cardContainerWithReset "Simulación" IconName.chart AppTypes.SettingsCategory.Simulation allRows dispatch

    let internal dataModsCard (state: State) (dispatch: Msg -> unit) : IView =
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

    let internal scoutingCard (state: State) (dispatch: Msg -> unit) : IView =
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
