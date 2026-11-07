namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Domain
open FootballEngine.Components
open FootballEngine.Icons

module Scouting =

    /// The rival's trait reads as a threat: the stronger the habit, the redder
    /// the marker, so the page answers "what hurts us" at a glance.
    let private strengthColor (strength: float) =
        if strength >= 0.66 then Theme.Danger
        elif strength >= 0.4 then Theme.Accent
        else Theme.TextMuted

    let private findingBar (f: ScoutFinding) =
        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius 8.0
              Border.padding (16.0, 12.0)
              Border.margin (0.0, 6.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 6.0
                        StackPanel.children
                            [ DockPanel.create
                                  [ DockPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text f.Label
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.TextMain ]
                                          TextBlock.create
                                              [ TextBlock.text $"{f.Strength * 100.0:F0}%%"
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground (strengthColor f.Strength)
                                                DockPanel.dock Dock.Right ] ] ]
                              ProgressBar.create
                                  [ ProgressBar.minimum 0.0
                                    ProgressBar.maximum 100.0
                                    ProgressBar.value (f.Strength * 100.0)
                                    ProgressBar.height 6.0
                                    ProgressBar.background Theme.BgMain
                                    ProgressBar.foreground (strengthColor f.Strength) ]
                              TextBlock.create
                                  [ TextBlock.text
                                        $"Confidence %d{int (f.Confidence * 100.0)}%% — share of sightings backing this pattern"
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground Theme.TextMuted ] ] ]
              ) ]
        :> IView

    let private instructionChip (label: string) (value: int) (maxValue: int) =
        Border.create
            [ Border.background (Theme.withAlpha "15" Theme.AccentAlt)
              Border.borderBrush (Theme.withAlpha "35" Theme.AccentAlt)
              Border.borderThickness 1.0
              Border.cornerRadius 4.0
              Border.padding (8.0, 4.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text $"{label} {value}/{maxValue}"
                        TextBlock.fontSize 10.0
                        TextBlock.fontWeight FontWeight.SemiBold
                        TextBlock.foreground Theme.TextMain ]
              ) ]

    let private instructionsPanel (instructions: TacticalInstructions) =
        WrapPanel.create
            [ WrapPanel.margin (16.0, 0.0, 16.0, 12.0)
              WrapPanel.children (
                  [ "Mentality", instructions.Mentality, 4
                    "Defensive Line", instructions.DefensiveLine, 4
                    "Pressing", instructions.PressingIntensity, 4
                    "Width", instructions.Width, 4
                    "Tempo", instructions.Tempo, 4
                    "Directness", instructions.Directness, 4
                    "Press Trigger", instructions.PressTriggerZone, 2
                    "Defensive Shape", instructions.DefensiveShape, 4 ]
                  |> List.map (fun (label, v, maxV) -> instructionChip label v maxV)
              ) ]
        :> IView

    let private confidenceBar (confidence: float) =
        StackPanel.create
            [ StackPanel.margin (16.0, 4.0, 16.0, 0.0)
              StackPanel.spacing 6.0
              StackPanel.children
                  [ DockPanel.create
                        [ DockPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text "REPORT CONFIDENCE"
                                      TextBlock.fontSize 10.0
                                      TextBlock.fontWeight FontWeight.Black
                                      TextBlock.foreground Theme.TextMuted ]
                                TextBlock.create
                                    [ TextBlock.text $"%d{int (confidence * 100.0)}%%"
                                      TextBlock.fontSize 10.0
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.foreground Theme.TextMuted
                                      DockPanel.dock Dock.Right ] ] ]
                    ProgressBar.create
                        [ ProgressBar.minimum 0.0
                          ProgressBar.maximum 100.0
                          ProgressBar.value (confidence * 100.0)
                          ProgressBar.height 5.0
                          ProgressBar.background Theme.BgMain
                          ProgressBar.foreground Theme.Accent ] ] ]
        :> IView

    let private reportPanel (report: ScoutReport) (gs: GameState) =
        let rivalName =
            gs.Clubs
            |> Map.tryFind report.SubjectId
            |> Option.map _.Name
            |> Option.defaultValue "Next rival"

        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius 10.0
              Border.clipToBounds true
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 0.0
                        StackPanel.children
                            [ UI.sectionHeader ClubIcon.scouting "SCOUTING REPORT"
                              Border.create
                                  [ Border.padding (16.0, 14.0)
                                    Border.borderBrush Theme.Border
                                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                    Border.child (
                                        StackPanel.create
                                            [ StackPanel.spacing 8.0
                                              StackPanel.children
                                                  [ DockPanel.create
                                                        [ DockPanel.children
                                                              [ TextBlock.create
                                                                    [ TextBlock.text rivalName
                                                                      TextBlock.fontSize 22.0
                                                                      TextBlock.fontWeight FontWeight.Black
                                                                      TextBlock.foreground Theme.TextMain ]
                                                                UI.badge "SCOUTED" Theme.Accent
                                                                |> fun b ->
                                                                    Border.create
                                                                        [ Border.dock Dock.Right; Border.child b ] ] ]
                                                    TextBlock.create
                                                        [ TextBlock.text
                                                              "Assessed by the scouting department — strengths are biased by the staff's judgement, not ground truth."
                                                          TextBlock.fontSize 10.0
                                                          TextBlock.foreground Theme.TextMuted ] ] ]
                                    ) ]
                              match report.Formation with
                              | Some formation ->
                                  Border.create
                                      [ Border.padding (16.0, 0.0, 16.0, 12.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.orientation Orientation.Horizontal
                                                  StackPanel.spacing 8.0
                                                  StackPanel.children
                                                      [ TextBlock.create
                                                            [ TextBlock.text "FORMATION"
                                                              TextBlock.fontSize 10.0
                                                              TextBlock.fontWeight FontWeight.Black
                                                              TextBlock.foreground Theme.TextMuted
                                                              TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                        UI.badge (string formation) Theme.AccentAlt ] ]
                                        ) ]
                              | None -> ()
                              match report.Instructions with
                              | Some instructions -> instructionsPanel instructions
                              | None -> ()
                              confidenceBar report.Confidence
                              Border.create
                                  [ Border.height 1.0
                                    Border.background Theme.Border
                                    Border.margin (16.0, 10.0) ]
                              match report.Findings with
                              | [] ->
                                  UI.emptyState
                                      IconName.warning
                                      "Not enough observations"
                                      "The staff needs more sightings before trusting any pattern."
                              | findings ->
                                  StackPanel.create
                                      [ StackPanel.margin (16.0, 0.0, 16.0, 14.0)
                                        StackPanel.children (findings |> List.map findingBar) ] ] ]
              ) ]
        :> IView

    let private emptyState (title: string) (sub: string) =
        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius 10.0
              Border.padding (16.0, 24.0)
              Border.child (UI.emptyState ClubIcon.scouting title sub) ]
        :> IView

    let private actionButton (label: string) (onClick: unit -> unit) =
        Button.create
            [ Button.padding (10.0, 7.0)
              Button.background Theme.AccentLight
              Button.borderBrush Theme.Accent
              Button.borderThickness 1.0
              Button.cornerRadius 6.0
              Button.onClick (fun _ -> onClick ())
              Button.content (
                  TextBlock.create
                      [ TextBlock.text label
                        TextBlock.fontSize 11.0
                        TextBlock.fontWeight FontWeight.SemiBold
                        TextBlock.foreground Theme.Accent ]
              ) ]

    let private assignmentCard (row: ScoutingSlice.AssignmentRow) (gs: GameState) =
        let playerName =
            row.Player |> Option.map _.Name |> Option.defaultValue "Objetivo no disponible"

        let scoutName =
            gs.Staff
            |> Map.tryFind row.Assignment.Scout
            |> Option.map _.Name
            |> Option.defaultValue "Scout no disponible"

        let reportText =
            row.Knowledge
            |> Option.map (fun k ->
                let low, high = k.CurrentSkillRange
                let confidence = int (k.Confidence * 100.0)
                $"Rango actual {low}–{high} · confianza {confidence}%%")
            |> Option.defaultValue "Todavía no hay conocimiento persistido para este encargo."

        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius 8.0
              Border.padding (14.0, 12.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 5.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text playerName
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMain ]
                              TextBlock.create
                                  [ TextBlock.text (ScoutingSlice.status row)
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground Theme.Accent ]
                              TextBlock.create
                                  [ TextBlock.text
                                        $"{scoutName} · vence {row.Assignment.Due.ToShortDateString()} · costo {Formatters.money row.Assignment.Cost}"
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextMuted ]
                              TextBlock.create
                                  [ TextBlock.text reportText
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextSub ] ] ]
              ) ]

    let private scoutingViewContent (state: State) (gs: GameState) (dispatch: Msg -> unit) =
        let t = state.Transfer

        let staff =
            GameState.getStaff gs.UserClubId gs
            |> List.filter (fun s -> s.Role = Scout || s.Role = RecruitmentAnalyst)
            |> List.sortByDescending (fun s -> s.Knowledge.JudgingPlayerAbility)

        let candidates =
            gs.Players
            |> Map.toList
            |> List.map snd
            |> List.filter (fun p -> Player.clubOf p <> Some gs.UserClubId)
            |> List.sortBy _.Name

        let shortlistIds = ScoutingSlice.shortlist gs

        let shortlistPlayers =
            shortlistIds |> List.choose (fun id -> gs.Players |> Map.tryFind id)

        let pickButton (text: string) (selected: bool) (onClick: unit -> unit) =
            Button.create
                [ Button.padding (8.0, 6.0)
                  Button.margin (0.0, 0.0, 5.0, 5.0)
                  Button.background (if selected then Theme.AccentLight else Theme.BgCard)
                  Button.borderBrush (if selected then Theme.Accent else Theme.Border)
                  Button.borderThickness 1.0
                  Button.cornerRadius 5.0
                  Button.onClick (fun _ -> onClick ())
                  Button.content (
                      TextBlock.create
                          [ TextBlock.text text
                            TextBlock.fontSize 11.0
                            TextBlock.foreground Theme.TextMain ]
                  ) ]

        let createPanel =
            Border.create
                [ Border.background Theme.BgSidebar
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.padding 14.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 7.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text "NUEVO ENCARGO"
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextMain ]
                                  TextBlock.create
                                      [ TextBlock.text
                                            "Elegí un jugador, un miembro del staff y confirmá costo y plazo."
                                        TextBlock.fontSize 11.0
                                        TextBlock.foreground Theme.TextMuted ]
                                  WrapPanel.create
                                      [ WrapPanel.children
                                            [ for p in candidates |> List.truncate 24 do
                                                  yield
                                                      pickButton p.Name (t.ScoutingPlayerId = Some p.Id) (fun () ->
                                                          dispatch (TransferMsg(SelectScoutingPlayer(Some p.Id)))) ] ]
                                  if staff.IsEmpty then
                                      UI.emptyState
                                          ClubIcon.scouting
                                          "No hay scout disponible"
                                          "Contratá un scout o analista de reclutamiento para crear encargos."
                                  else
                                      WrapPanel.create
                                          [ WrapPanel.children
                                                [ for s in staff do
                                                      yield
                                                          pickButton
                                                              $"{s.Name} · {s.Knowledge.JudgingPlayerAbility}"
                                                              (t.ScoutingScoutId = Some s.Id)
                                                              (fun () ->
                                                                  dispatch (
                                                                      TransferMsg(SelectScoutingScout(Some s.Id))
                                                                  )) ] ]
                                  Grid.create
                                      [ Grid.columnDefinitions "*, * , Auto"
                                        Grid.children
                                            [ TextBox.create
                                                  [ Grid.column 0
                                                    TextBox.text t.ScoutingCostText
                                                    TextBox.watermark "Costo"
                                                    TextBox.margin (0.0, 0.0, 6.0, 0.0)
                                                    TextBox.onTextChanged (fun v ->
                                                        dispatch (TransferMsg(SetScoutingCost v))) ]
                                              TextBox.create
                                                  [ Grid.column 1
                                                    TextBox.text t.ScoutingDaysText
                                                    TextBox.watermark "Días"
                                                    TextBox.margin (0.0, 0.0, 6.0, 0.0)
                                                    TextBox.onTextChanged (fun v ->
                                                        dispatch (TransferMsg(SetScoutingDays v))) ]
                                              actionButton "Crear encargo" (fun () ->
                                                  dispatch (TransferMsg CreateScoutingAssignment))
                                              |> fun b -> Border.create [ Grid.column 2; Border.child b ] ] ] ] ]
                  ) ]

        let shortlistRows =
            shortlistPlayers
            |> List.map (fun p ->
                let knowledge = gs.Market.Knowledge |> Map.tryFind p.Id

                Border.create
                    [ Border.padding (0.0, 5.0)
                      Border.child (
                          StackPanel.create
                              [ StackPanel.spacing 4.0
                                StackPanel.children
                                    [ TextBlock.create [ TextBlock.text p.Name; TextBlock.foreground Theme.TextMain ]
                                      TextBlock.create
                                          [ TextBlock.text (
                                                knowledge
                                                |> Option.map (fun k -> $"Confianza {int (k.Confidence * 100.0)}%%")
                                                |> Option.defaultValue "Sin informe"
                                            )
                                            TextBlock.fontSize 10.0
                                            TextBlock.foreground Theme.TextMuted ]
                                      StackPanel.create
                                          [ StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 5.0
                                            StackPanel.children
                                                [ actionButton "Abrir oferta" (fun () ->
                                                      dispatch (TransferMsg(OpenScoutingOffer p.Id)))
                                                  actionButton "Quitar" (fun () ->
                                                      dispatch (TransferMsg(ToggleShortlist p.Id))) ] ] ] ]
                      ) ])

        let shortlistPanel =
            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.padding 14.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 7.0
                            StackPanel.children (
                                TextBlock.create
                                    [ TextBlock.text "SHORTLIST"
                                      TextBlock.fontWeight FontWeight.Black
                                      TextBlock.foreground Theme.TextMain ]
                                :> IView
                                :: (if shortlistRows.IsEmpty then
                                        [ UI.emptyState
                                              PlayerIcon.skill
                                              "Shortlist vacía"
                                              "Agregá jugadores desde el mercado o desde un informe listo." ]
                                    else
                                        shortlistRows |> List.map (fun row -> row :> IView))
                            ) ]
                  ) ]

        let assignments = ScoutingSlice.assignmentRows gs

        // Horizontal scrolling would hand the centred column infinite width and push it off-screen.
        ScrollViewer.create
            [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
              ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
              ScrollViewer.content (
                  StackPanel.create
                      [ StackPanel.maxWidth 980.0
                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                        StackPanel.margin (24.0, 20.0)
                        StackPanel.spacing 14.0
                        StackPanel.children
                            [ UI.display "Scouting" 44.0 Theme.TextMain
                              createPanel
                              shortlistPanel
                              TextBlock.create
                                  [ TextBlock.text "ENCARGOS E INFORMES"
                                    TextBlock.fontWeight FontWeight.Black
                                    TextBlock.foreground Theme.TextMain ]
                              if assignments.IsEmpty then
                                  UI.emptyState
                                      ClubIcon.scouting
                                      "Sin encargos"
                                      "Elegí un objetivo y confirmá el primer encargo de scouting."
                              else
                                  // No explicit yield: one would silently drop every implicit element above it.
                                  for row in assignments do
                                      assignmentCard row gs ] ]
              ) ]
        :> IView

    let scoutingView (state: State) (dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) -> scoutingViewContent state gs dispatch
        | _ -> Border.create [] :> IView
