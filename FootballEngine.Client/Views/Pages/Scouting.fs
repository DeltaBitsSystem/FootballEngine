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
            [ Border.background (Theme.AccentAlt + "15")
              Border.borderBrush (Theme.AccentAlt + "35")
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

    let scoutingView (state: State) (_dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) ->
            match ScoutingSlice.view gs with
            | { NextOpponent = None } ->
                emptyState "No upcoming fixture" "The next match involving your club has not been scheduled yet."
            | { Report = None } ->
                emptyState
                    "No scouting report"
                    "The staff has not yet observed the next opponent — play a match against them first."
            | { Report = Some report } ->
                ScrollViewer.create
                    [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                      ScrollViewer.content (
                          StackPanel.create
                              [ StackPanel.maxWidth 760.0
                                StackPanel.horizontalAlignment HorizontalAlignment.Center
                                StackPanel.margin (24.0, 20.0)
                                StackPanel.children [ reportPanel report gs ] ]
                      ) ]
                :> IView
        | _ -> Border.create [] :> IView
