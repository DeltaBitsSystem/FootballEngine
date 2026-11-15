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

/// The engine tuning card: every advanced match setting with its bounds.
module internal SettingsAdvanced =

    let internal advancedCard (state: State) (dispatch: Msg -> unit) : IView =
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
            [ let label = (tr "Desbloquear editor motor" "Unlock engine editor")

              let desc =
                  (tr
                      "Permite abrir el editor crudo de BalanceConfig — solo depuración, sin validación extra"
                      "Opens the raw BalanceConfig editor. For debugging only, no extra validation")

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
              let label2 = (tr "Mostrar debug" "Show debug")

              let desc2 =
                  (tr
                      "Superpone IDs y ticks — útil para QA, ruidoso para juego normal"
                      "Overlays IDs and ticks. Useful for QA, noisy for normal play")

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
              let label3 = (tr "Nivel de log" "Log level")

              let desc3 =
                  (tr
                      "Silent/Normal/Verbose — controla LogLevel y verbosidad del pipeline de mods"
                      "Silent/Normal/Verbose. Controls LogLevel and mod pipeline verbosity")

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
                    [ infoBanner (
                          tr
                              "Activa para editar motor — desbloquea los deslizadores de BalanceConfig. Cambios requieren Custom preset."
                              "Turn on to edit the engine. Unlocks the BalanceConfig sliders. Changes need the Custom preset."
                      ) ]

            let allRows = lockedInfo @ baseRows

            cardContainerWithReset
                (tr "Avanzado" "Advanced")
                IconName.training
                AppTypes.SettingsCategory.Advanced
                allRows
                dispatch
        else
            let warning =
                warningBanner (
                    tr
                        "Editor motor desbloqueado — cambios afectan toda la simulación"
                        "Engine editor unlocked. Changes affect the whole simulation"
                )

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
                                                [ TextBlock.text (
                                                      tr
                                                          "Errores de validación BalanceConfig"
                                                          "BalanceConfig validation errors"
                                                  )
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

            let ballDragCtl =
                sliderWithLabel 0.1 0.45 curBalance.Ball.DragSupercritical (fun v -> sprintf "%.3f" v) (fun v ->
                    let c = System.Math.Clamp(v, 0.1, min 0.45 curBalance.Ball.DragSubcritical)

                    updateBalance (fun b ->
                        { b with
                            Ball = { b.Ball with DragSupercritical = c } }))

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
                       (tr
                           "0-20 m/s² — 9.81 real, 0 flota, >20 cae como piedra"
                           "0-20 m/s². 9.81 is real, 0 floats, above 20 drops like a stone"),
                       "🎯",
                       ballGravityCtl,
                       "Gravity")
                  yield
                      ("Ball.DragSupercritical",
                       (tr
                           "Resistencia del aire a alta velocidad (≈0,2) — menor = tiros que conservan más velocidad"
                           "Air drag at high speed (about 0.2). Lower means shots keep more speed"),
                       "⚡",
                       ballDragCtl,
                       "DragSupercritical")
                  yield
                      ("Ball.MaxShotSpeed",
                       (tr "10-40 m/s — 32 default, 40 es récord mundial" "10-40 m/s. 32 default, 40 is a world record"),
                       "🎯",
                       ballMaxShotCtl,
                       "MaxShotSpeed")
                  yield
                      ("Ball.BounceRestitution",
                       (tr
                           "0-1 rebote — >1 crea energía, 0 no rebota"
                           "0-1 bounce. Above 1 creates energy, 0 does not bounce"),
                       "⚡",
                       ballBounceCtl,
                       "BounceRestitution")
                  yield
                      ("Movement.SprintSpeed",
                       (tr "5-12 m/s — Velocidad punta, 8.5 default" "5-12 m/s. Top speed, 8.5 default"),
                       "⚡",
                       sprintCtl,
                       "SprintSpeed")
                  yield
                      ("Movement.Acceleration",
                       (tr "0.5-10 m/s² — 4 default, >10 teleport" "0.5-10 m/s². 4 default, above 10 is teleporting"),
                       "⚡",
                       accelCtl,
                       "Acceleration")
                  yield
                      ("Actions.DecisionSoftmaxTemperature",
                       (tr "0.1-5 — 1 default, 0.1 rígido, 5 aleatorio" "0.1-5. 1 default, 0.1 rigid, 5 random"),
                       "🎯",
                       decisionTempCtl,
                       "DecisionSoftmaxTemperature")
                  yield
                      ("Actions.PassBaseQuality",
                       (tr "0-1 calidad base de pase — 0.7 default" "0-1 base pass quality. 0.7 default"),
                       "🎯",
                       passQualityCtl,
                       "PassBaseQuality")
                  yield
                      ("Team.UrgencyMaxDeficit",
                       (tr "0-5 déficit máx para urgencia — 2 default" "0-5 max goal deficit for urgency. 2 default"),
                       "🎯",
                       urgencyDeficitCtl,
                       "UrgencyMaxDeficit")
                  yield
                      ("Team.DirectiveHysteresis",
                       (tr
                           "0-1 histéresis directiva — 0.08 default, >1 nunca cambia"
                           "0-1 directive hysteresis. 0.08 default, above 1 never changes"),
                       "🎯",
                       directiveHystCtl,
                       "DirectiveHysteresis")
                  yield
                      ("Referee.FoulYellowWorthyChance",
                       (tr
                           "0-1 prob. amarilla por falta temeraria — 0.85 default"
                           "0-1 yellow card chance for a reckless foul. 0.85 default"),
                       "🎯",
                       foulYellowCtl,
                       "FoulYellowWorthyChance")
                  yield
                      ("Referee.FoulRedWorthyChance",
                       (tr
                           "0-1 prob. roja por fuerza excesiva — 0.7 default"
                           "0-1 red card chance for excessive force. 0.7 default"),
                       "🎯",
                       foulRedCtl,
                       "FoulRedWorthyChance")
                  yield
                      ("Referee.HomeCardBonus",
                       (tr "0-1 sesgo local tarjeta — 0.05 default" "0-1 home card bias. 0.05 default"),
                       "🎯",
                       homeCardCtl,
                       "HomeCardBonus")
                  yield
                      ("Environment.HomeAdvantageStrength",
                       (tr "0-1 fuerza ventaja local — 0.1 default" "0-1 home advantage strength. 0.1 default"),
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
                    [ motorSection (tr "Ball — física y tiro" "Ball: physics and shooting") ballRows ]

            let movementSection =
                let mvRows = motorRows |> List.skip 4 |> List.take 2

                if mvRows.IsEmpty then
                    []
                else
                    [ motorSection (tr "Movement — velocidad y aceleración" "Movement: speed and acceleration") mvRows ]

            let actionsSection =
                let actRows = motorRows |> List.skip 6 |> List.take 4

                if actRows.IsEmpty then
                    []
                else
                    [ motorSection (tr "Actions — decisiones y faltas" "Actions: decisions and fouls") actRows ]

            let teamSection =
                let tRows = motorRows |> List.skip 10 |> List.take 2

                if tRows.IsEmpty then
                    []
                else
                    [ motorSection (tr "Team — urgencia e histéresis" "Team: urgency and hysteresis") tRows ]

            let refereeSection =
                let refRows = motorRows |> List.skip 12 |> List.take 3

                if refRows.IsEmpty then
                    []
                else
                    [ motorSection (tr "Referee — tarjetas y sesgo local" "Referee: cards and home bias") refRows ]

            let envSection =
                let eRows = motorRows |> List.skip 15

                if eRows.IsEmpty then
                    []
                else
                    [ motorSection (tr "Environment — ventaja local" "Environment: home advantage") eRows ]

            let restoreBtn: IView =
                Button.create
                    [ Button.content (tr "Restaurar Balance por defecto" "Restore default Balance")
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
                                          [ TextBox.placeholderText (
                                                tr
                                                    "Pega JSON exportado aquí para importar..."
                                                    "Paste exported JSON here to import..."
                                            )
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
                                                      [ Button.content (tr "Exportar" "Export")
                                                        Button.background Theme.Accent
                                                        Button.foreground Theme.BgSidebar
                                                        Button.padding (12.0, 6.0)
                                                        Button.cornerRadius 6.0
                                                        Button.onClick (fun _ ->
                                                            try
                                                                dispatch (SettingsMsg Export)
                                                            with _ ->
                                                                ()) ]
                                                  :> IView
                                                  Button.create
                                                      [ Button.content (tr "Importar" "Import")
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
                                          [ TextBlock.text (
                                                tr
                                                    "Export copia JSON al portapapeles y al log; Import valida y notifica error si falla."
                                                    "Export copies JSON to the clipboard and the log. Import validates and reports an error if it fails."
                                            )
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

            cardContainerWithReset
                (tr "Avanzado" "Advanced")
                IconName.training
                AppTypes.SettingsCategory.Advanced
                allRows
                dispatch
