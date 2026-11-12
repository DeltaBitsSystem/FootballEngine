namespace FootballEngine.Components

open System
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Icons

module PlayerView =

    let private tr key = Localization.resolve key []

    let private statusBadge (p: Player) : IView list =
        match p.Status with
        | Injured(severity, _) ->
            let color =
                match severity with
                | Minor
                | Moderate -> Theme.Warning
                | _ -> Theme.Danger

            [ UI.badge (tr TranslationKey.StatusInjured) color ]
        | Suspended days ->
            [ UI.badge (Localization.resolve TranslationKey.StatusSuspended [ "days", string days ]) Theme.Warning ]
        | Available -> []

    let private bar (value: float) (maximum: float) (color: string) (height: float) : IView =
        ProgressBar.create
            [ ProgressBar.minimum 0.0
              ProgressBar.maximum maximum
              ProgressBar.value value
              ProgressBar.height height
              ProgressBar.minHeight height
              ProgressBar.cornerRadius (height / 2.0)
              ProgressBar.background Theme.Border
              ProgressBar.foreground color ]
        :> IView

    let private conditionColor (value: int) =
        if value >= 75 then Theme.Accent
        elif value >= 55 then Theme.Warning
        else Theme.Hot

    /// A squad row is a card: position chip, who, how fit, and the level a manager scans first.
    let row (p: Player) (currentDate: DateTime) (isSelected: bool) (isDragged: bool) onSelect =
        Border.create
            [ Border.background (if isSelected then Theme.BgCardHover else Theme.BgCard)
              Border.borderBrush (
                  if isSelected then Theme.Accent
                  elif isDragged then Theme.DragBorder
                  else Theme.Border
              )
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (16.0, 12.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.cursor UI.handCursor
              Border.onTapped (fun _ -> onSelect ())
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, 16, *, 16, 120, 16, 64, 12, 64"
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (UI.positionBadge p.Position) ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.spacing 3.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ StackPanel.create
                                              [ StackPanel.orientation Orientation.Horizontal
                                                StackPanel.spacing 8.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text p.Name
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.fontSize 15.0
                                                            TextBlock.foreground Theme.TextMain
                                                            TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                                      yield! statusBadge p ] ]
                                          TextBlock.create
                                              [ TextBlock.text (
                                                    Localization.resolve
                                                        TranslationKey.PlayerAgeMorale
                                                        [ "age", string (Player.age currentDate p); "morale", string p.Morale ]
                                                )
                                                TextBlock.fontSize 13.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              StackPanel.create
                                  [ Grid.column 4
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text (tr TranslationKey.PlayerCondition)
                                                TextBlock.fontSize 12.0
                                                TextBlock.foreground Theme.TextMuted ]
                                          bar (float p.Condition) 100.0 (conditionColor p.Condition) 6.0 ] ]
                              TextBlock.create
                                  [ Grid.column 6
                                    TextBlock.text $"€{int (Player.playerValue p.CurrentSkill / 1_000_000m)}M"
                                    TextBlock.fontSize 13.0
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.horizontalAlignment HorizontalAlignment.Right
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              Border.create
                                  [ Grid.column 8
                                    Border.horizontalAlignment HorizontalAlignment.Right
                                    Border.child (UI.display (string p.CurrentSkill) 32.0 Theme.TextMain) ] ] ]
              ) ]

    type private StatGroup =
        { Title: string
          Color: string
          Stats: (TranslationKey * int) list }

    let private statGroupsFor (p: Player) =
        let physical =
            { Title = tr TranslationKey.AttrGroupPhysical
              Color = Theme.AccentAlt
              Stats =
                [ TranslationKey.AttrAcceleration, p.Physical.Acceleration
                  TranslationKey.AttrPace, p.Physical.Pace
                  TranslationKey.AttrAgility, p.Physical.Agility
                  TranslationKey.AttrBalance, p.Physical.Balance
                  TranslationKey.AttrJumpingReach, p.Physical.JumpingReach
                  TranslationKey.AttrStamina, p.Physical.Stamina
                  TranslationKey.AttrStrength, p.Physical.Strength ] }

        let mental =
            { Title = tr TranslationKey.AttrGroupMental
              Color = Theme.Success
              Stats =
                [ TranslationKey.AttrAggression, p.Mental.Aggression
                  TranslationKey.AttrComposure, p.Mental.Composure
                  TranslationKey.AttrConcentration, p.Mental.Concentration
                  TranslationKey.AttrVision, p.Mental.Vision
                  TranslationKey.AttrPositioning, p.Mental.Positioning
                  TranslationKey.AttrBravery, p.Mental.Bravery
                  TranslationKey.AttrWorkRate, p.Mental.WorkRate
                  TranslationKey.AttrLeadership, p.Mental.Leadership ] }

        let technical =
            { Title = tr TranslationKey.AttrGroupTechnical
              Color = Theme.Hot
              Stats =
                [ TranslationKey.AttrFinishing, p.Technical.Finishing
                  TranslationKey.AttrLongShots, p.Technical.LongShots
                  TranslationKey.AttrDribbling, p.Technical.Dribbling
                  TranslationKey.AttrBallControl, p.Technical.BallControl
                  TranslationKey.AttrPassing, p.Technical.Passing
                  TranslationKey.AttrCrossing, p.Technical.Crossing
                  TranslationKey.AttrTackling, p.Technical.Tackling
                  TranslationKey.AttrMarking, p.Technical.Marking
                  TranslationKey.AttrHeading, p.Technical.Heading
                  TranslationKey.AttrFreeKick, p.Technical.FreeKick
                  TranslationKey.AttrPenalty, p.Technical.Penalty ] }

        let goalkeeping =
            { Title = tr TranslationKey.AttrGroupGoalkeeping
              Color = Theme.Warning
              Stats =
                [ TranslationKey.AttrReflexes, p.Goalkeeping.Reflexes
                  TranslationKey.AttrHandling, p.Goalkeeping.Handling
                  TranslationKey.AttrKicking, p.Goalkeeping.Kicking
                  TranslationKey.AttrOneOnOne, p.Goalkeeping.OneOnOne
                  TranslationKey.AttrAerialReach, p.Goalkeeping.AerialReach ] }

        if p.Position = GK then
            [ goalkeeping; physical; mental ]
        else
            [ technical; physical; mental ]

    let private statRow (label: TranslationKey) (value: int) (color: string) : IView =
        Grid.create
            [ Grid.columnDefinitions "120, 12, *, 12, 28"
              Grid.margin (0.0, 4.0)
              Grid.children
                  [ TextBlock.create
                        [ Grid.column 0
                          TextBlock.text (tr label)
                          TextBlock.fontSize 14.0
                          TextBlock.foreground Theme.TextSub
                          TextBlock.verticalAlignment VerticalAlignment.Center ]
                    Border.create
                        [ Grid.column 2
                          Border.verticalAlignment VerticalAlignment.Center
                          Border.child (bar (float value) 20.0 (if value >= 16 then Theme.Accent else color) 8.0) ]
                    Border.create
                        [ Grid.column 4
                          Border.horizontalAlignment HorizontalAlignment.Right
                          Border.child (UI.display (string value) 20.0 Theme.TextMain) ] ] ]
        :> IView

    let private figure (label: TranslationKey) (value: string) (color: string) : IView =
        Border.create
            [ Border.background Theme.BgElevated
              Border.cornerRadius Theme.RadiusMd
              Border.padding (12.0, 10.0)
              Border.margin (0.0, 0.0, 8.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 2.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text (tr label)
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Theme.TextMuted ]
                              UI.display value 28.0 color ] ]
              ) ]
        :> IView

    let detail (player: Player option) (currentDate: DateTime) (clubKit: ClubIdentity.Kit) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius (Theme.RadiusXl + 2.0)
              Border.clipToBounds true
              Border.child (
                  match player with
                  | None -> UI.emptyState IconName.squad (tr TranslationKey.SelectPlayer) (tr TranslationKey.SelectPlayerHint)
                  | Some p ->
                      let kit = if p.Position = GK then ClubIdentity.keeperKit else clubKit

                      let tint = Theme.positionColor p.Position

                      Grid.create
                          [ Grid.rowDefinitions "Auto, *"
                            Grid.children
                                [ Border.create
                                      [ Grid.row 0
                                        Border.background (
                                            Theme.spotlightBrush (Theme.withAlpha "66" tint) (Theme.withAlpha "22" tint) Theme.BgCard
                                        )
                                        Border.padding (24.0, 24.0, 24.0, 18.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 16.0
                                                  StackPanel.children
                                                      [ StackPanel.create
                                                            [ StackPanel.orientation Orientation.Horizontal
                                                              StackPanel.spacing 18.0
                                                              StackPanel.children
                                                                  [ UI.shirt kit "" 84.0 "#0A1020" 1.5
                                                                    StackPanel.create
                                                                        [ StackPanel.spacing 6.0
                                                                          StackPanel.verticalAlignment VerticalAlignment.Center
                                                                          StackPanel.children
                                                                              [ UI.display p.Name 30.0 Theme.TextMain
                                                                                StackPanel.create
                                                                                    [ StackPanel.orientation Orientation.Horizontal
                                                                                      StackPanel.spacing 8.0
                                                                                      StackPanel.children
                                                                                          [ UI.positionBadge p.Position
                                                                                            TextBlock.create
                                                                                                [ TextBlock.text (
                                                                                                      Localization.resolve
                                                                                                          TranslationKey.PlayerAgeMorale
                                                                                                          [ "age", string (Player.age currentDate p)
                                                                                                            "morale", string p.Morale ]
                                                                                                  )
                                                                                                  TextBlock.fontSize 14.0
                                                                                                  TextBlock.foreground Theme.TextSub
                                                                                                  TextBlock.verticalAlignment
                                                                                                      VerticalAlignment.Center ] ] ] ] ] ] ]
                                                        WrapPanel.create
                                                            [ WrapPanel.children
                                                                  [ figure TranslationKey.PlayerLevel (string p.CurrentSkill) Theme.TextMain
                                                                    figure TranslationKey.PlayerPotential (string p.PotentialSkill) Theme.Accent
                                                                    figure TranslationKey.PlayerCondition $"{p.Condition}%%" (conditionColor p.Condition)
                                                                    figure TranslationKey.PlayerMatchFitness $"{p.MatchFitness}%%" Theme.TextMain
                                                                    figure
                                                                        TranslationKey.PlayerContract
                                                                        (Player.contractOf p
                                                                         |> Option.map (_.ExpiryYear >> string)
                                                                         |> Option.defaultValue "—")
                                                                        Theme.TextMain ] ] ] ]
                                        ) ]
                                  ScrollViewer.create
                                      [ Grid.row 1
                                        ScrollViewer.padding (24.0, 8.0, 24.0, 24.0)
                                        ScrollViewer.content (
                                            StackPanel.create
                                                [ StackPanel.spacing 4.0
                                                  StackPanel.children
                                                      [ for group in statGroupsFor p do
                                                            Border.create
                                                                [ Border.margin (0.0, 14.0, 0.0, 4.0)
                                                                  Border.child (UI.eyebrow group.Title group.Color) ]
                                                            :> IView

                                                            for label, value in group.Stats do
                                                                statRow label value group.Color ] ]
                                        ) ] ] ]
                      :> IView
              ) ]
