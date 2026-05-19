namespace FootballEngine.Components

open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Icons


module UI =

    let statusBanner (color: string) (icon: Material.Icons.MaterialIconKind) (text: string) =
        Border.create
            [ Border.background (color + "18")
              Border.borderBrush color
              Border.borderThickness 1.0
              Border.cornerRadius 8.0
              Border.padding 15.0
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 12.0
                        StackPanel.children
                            [ Icons.iconSm icon color
                              TextBlock.create
                                  [ TextBlock.text text
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.Medium
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let primaryButton (label: string) (icon: Material.Icons.MaterialIconKind option) onClick =
        Button.create
            [ Button.background Theme.Accent
              Button.foreground Theme.BgSidebar
              Button.fontWeight FontWeight.Bold
              Button.padding (20.0, 10.0)
              Button.cornerRadius 8.0
              Button.cursor Avalonia.Input.Cursor.Default
              Button.onClick onClick
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children
                            [ match icon with
                              | Some k -> Icons.iconSm k Theme.BgSidebar
                              | None -> ()
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.BgSidebar
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let ghostButton (label: string) (icon: Material.Icons.MaterialIconKind option) onClick =
        Button.create
            [ Button.background "Transparent"
              Button.foreground Theme.TextMuted
              Button.borderBrush Theme.Border
              Button.borderThickness 1.0
              Button.padding (16.0, 8.0)
              Button.cornerRadius 8.0
              Button.cursor Avalonia.Input.Cursor.Default
              Button.onClick onClick
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children
                            [ match icon with
                              | Some k -> Icons.iconSm k Theme.TextMuted
                              | None -> ()
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let iconToggleButton (label: string) (iconKind: Material.Icons.MaterialIconKind) (isActive: bool) onClick =
        Button.create
            [ Button.onClick onClick
              Button.padding (10.0, 6.0)
              Button.cornerRadius 6.0
              Button.cursor Avalonia.Input.Cursor.Default
              Button.background (if isActive then Theme.AccentLight else "Transparent")
              Button.borderBrush (if isActive then Theme.Accent else "Transparent")
              Button.borderThickness (if isActive then 1.0 else 0.0)
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children
                            [ Icons.iconSm iconKind (if isActive then Theme.Accent else Theme.TextMuted)
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.fontSize 11.0
                                    TextBlock.fontWeight (if isActive then FontWeight.SemiBold else FontWeight.Normal)
                                    TextBlock.foreground (if isActive then Theme.Accent else Theme.TextMuted)
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let badge (text: string) (color: string) =
        Border.create
            [ Border.background color
              Border.cornerRadius 4.0
              Border.padding (6.0, 2.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text text
                        TextBlock.fontSize 10.0
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground "#ffffff" ]
              ) ]

    let countBadge (count: int) =
        Border.create
            [ Border.background Theme.AccentLight
              Border.cornerRadius 10.0
              Border.padding (8.0, 2.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text (string count)
                        TextBlock.fontSize 11.0
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground Theme.Accent ]
              ) ]

    let positionBadge (pos: Position) =
        let color = Theme.positionColor pos

        Border.create
            [ Border.background (color + "1a")
              Border.borderBrush (color + "55")
              Border.borderThickness 1.0
              Border.cornerRadius 5.0
              Border.padding (7.0, 4.0)
              Border.child (
                  TextBlock.create
                      [ TextBlock.text $"%A{pos}"
                        TextBlock.fontSize 10.0
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground color ]
              ) ]

    let statCard (iconKind: Material.Icons.MaterialIconKind) (label: string) (value: string) (sub: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius 10.0
              Border.padding (16.0, 12.0)
              Border.margin (8.0, 8.0, 8.0, 8.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "auto, *"
                        Grid.rowDefinitions "auto, auto"
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Grid.rowSpan 2
                                    Border.background Theme.AccentLight
                                    Border.cornerRadius 8.0
                                    Border.padding 8.0
                                    Border.margin (0.0, 0.0, 12.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (Icons.iconMd iconKind Theme.Accent) ]
                              TextBlock.create
                                  [ Grid.column 1
                                    Grid.row 0
                                    TextBlock.text label
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.lineSpacing 0.8 ]
                              StackPanel.create
                                  [ Grid.column 1
                                    Grid.row 1
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 5.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text value
                                                TextBlock.fontSize 18.0
                                                TextBlock.fontWeight FontWeight.Black
                                                TextBlock.foreground Theme.TextMain ]
                                          if sub <> "" then
                                              TextBlock.create
                                                  [ TextBlock.text sub
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.verticalAlignment VerticalAlignment.Bottom
                                                    TextBlock.margin (0.0, 0.0, 0.0, 2.0) ] ] ] ] ]
              ) ]

    let iconStatCard (label: string) (value: string) (iconKind: Material.Icons.MaterialIconKind) (sub: string) =
        statCard iconKind label value sub

    let statMiniCard (iconKind: Material.Icons.MaterialIconKind) (label: string) (value: string) (color: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush (color + "40")
              Border.borderThickness 1.0
              Border.cornerRadius 8.0
              Border.padding (12.0, 10.0)
              Border.margin (8.0, 8.0, 8.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 6.0
                        StackPanel.children
                            [ StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 5.0
                                    StackPanel.children
                                        [ Icons.iconSm iconKind color
                                          TextBlock.create
                                              [ TextBlock.text label
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.fontSize 9.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                              TextBlock.create
                                  [ TextBlock.text value
                                    TextBlock.foreground color
                                    TextBlock.fontSize 22.0
                                    TextBlock.fontWeight FontWeight.Black ] ] ]
              ) ]

    let sectionTitle (label: string) (iconKind: Material.Icons.MaterialIconKind) =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 8.0
              StackPanel.margin (0.0, 0.0, 0.0, 10.0)
              StackPanel.children
                  [ Icons.iconSm iconKind Theme.Accent
                    TextBlock.create
                        [ TextBlock.text label
                          TextBlock.fontSize 11.0
                          TextBlock.fontWeight FontWeight.Black
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.lineSpacing 1.2
                          TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

    let sectionHeader (iconKind: Material.Icons.MaterialIconKind) (label: string) : IView =
        Border.create
            [ Border.background Theme.BgSidebar
              Border.padding (16.0, 10.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconMd iconKind Theme.TextMuted
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]
        :> IView

    let sectionHeaderWithBadge (iconKind: Material.Icons.MaterialIconKind) (label: string) (count: int) : IView =
        Border.create
            [ Border.background Theme.BgSidebar
              Border.padding (16.0, 11.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconMd iconKind Theme.TextMuted
                              TextBlock.create
                                  [ TextBlock.text label
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.fontSize 10.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              countBadge count ] ]
              ) ]
        :> IView

    let panelCard (header: IView) (body: IView) : IView =
        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.clipToBounds true
              Border.child (StackPanel.create [ StackPanel.spacing 0.0; StackPanel.children [ header; body ] ]) ]
        :> IView

    let emptyState (iconKind: Material.Icons.MaterialIconKind) (title: string) (sub: string) : IView =
        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing 8.0
              StackPanel.margin (0.0, 40.0)
              StackPanel.children
                  [ Icons.iconXl iconKind Theme.TextMuted
                    TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontSize 14.0
                          TextBlock.fontWeight FontWeight.SemiBold
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                    if sub <> "" then
                        TextBlock.create
                            [ TextBlock.text sub
                              TextBlock.fontSize 11.0
                              TextBlock.foreground Theme.TextMuted
                              TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
        :> IView

    let loadingState () : IView =
        StackPanel.create
            [ StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.spacing 12.0
              StackPanel.margin (0.0, 40.0)
              StackPanel.children
                  [ Icons.iconXl IconName.refresh Theme.TextMuted
                    TextBlock.create
                        [ TextBlock.text "Loading..."
                          TextBlock.fontSize 13.0
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
        :> IView

    let iconRow (iconKind: Material.Icons.MaterialIconKind) (text: string) =
        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius 6.0
              Border.padding (10.0, 8.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Icons.iconSm iconKind Theme.TextMuted
                              TextBlock.create
                                  [ TextBlock.text text
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextSub
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let tabButton (label: string) isActive onClick =
        Button.create
            [ Button.content label
              Button.background (if isActive then Theme.Accent else "transparent")
              Button.foreground (if isActive then Theme.BgSidebar else Theme.TextMuted)
              Button.borderThickness (if isActive then 0.0 else 1.0)
              Button.borderBrush Theme.Border
              Button.padding (15.0, 5.0)
              Button.fontSize 11.0
              Button.fontWeight FontWeight.Bold
              Button.cornerRadius 4.0
              Button.onClick onClick
              Button.margin (0.0, 0.0, 10.0, 0.0) ]

    let sectionContainer (title: string) (content: IView) =
        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text title
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.TextMuted
                          TextBlock.fontSize 12.0 ]
                    Border.create
                        [ Border.background Theme.BgCard
                          Border.borderBrush Theme.Border
                          Border.borderThickness 1.0
                          Border.cornerRadius 8.0
                          Border.clipToBounds true
                          Border.child content ] ] ]

    let menuButton (title: string) (icon: Material.Icons.MaterialIconKind) (description: string) onClick =
        Button.create
            [ Button.onClick onClick
              Button.padding 0.0
              Button.background "transparent"
              Button.content (
                  Border.create
                      [ Border.background Theme.BgCard
                        Border.cornerRadius 12.0
                        Border.padding 30.0
                        Border.borderBrush Theme.Border
                        Border.borderThickness 1.0
                        Border.child (
                            StackPanel.create
                                [ StackPanel.spacing 10.0
                                  StackPanel.children
                                      [ Icons.iconXl icon Theme.TextMain
                                        TextBlock.create
                                            [ TextBlock.text title
                                              TextBlock.fontSize 22.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground Theme.Accent
                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                        TextBlock.create
                                            [ TextBlock.text description
                                              TextBlock.fontSize 12.0
                                              TextBlock.foreground Theme.TextMuted
                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                        ) ]
              ) ]

    let countrySelectionCard name flag isPrimary isSecondary onClickPrimary onClickSecondary =
        let borderColor =
            if isPrimary then Theme.Accent
            elif isSecondary then Theme.AccentAlt
            else "transparent"

        let statusText, statusColor =
            if isPrimary then "PRIMARY LEAGUE", Theme.Accent
            elif isSecondary then "SIMULATED", Theme.AccentAlt
            else "NOT ACTIVE", Theme.TextMuted

        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius 10.0
              Border.borderThickness 2.0
              Border.borderBrush borderColor
              Border.padding 15.0
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.inboxListCols)
                        Grid.children
                            [ TextBlock.create
                                  [ Grid.column 0
                                    TextBlock.text flag
                                    TextBlock.fontSize 32.0
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.margin (15.0, 0.0)
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text name
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.fontSize 16.0 ]
                                          TextBlock.create
                                              [ TextBlock.text statusText
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground statusColor ] ] ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.spacing 5.0
                                    StackPanel.children
                                        [ Button.create
                                              [ Button.content (if isPrimary then "✓ Main" else "Set Main")
                                                Button.fontSize 10.0
                                                Button.onClick onClickPrimary
                                                Button.background (if isPrimary then Theme.Accent else Theme.BgSidebar) ]
                                          Button.create
                                              [ Button.content (if isSecondary then "✕ Remove" else "+ Add")
                                                Button.fontSize 10.0
                                                Button.onClick onClickSecondary
                                                Button.isEnabled (not isPrimary) ] ] ] ] ]
              ) ]

    let roleBadge (role: string) (duty: string) =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 2.0
              StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text role
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.fontSize 10.0
                          TextBlock.foreground "#ffffff" ]
                    TextBlock.create
                        [ TextBlock.text $"- {duty}"
                          TextBlock.fontSize 10.0
                          TextBlock.foreground "#fbbf24" ] ] ]

    let sidebarButton (isActive: bool) =
        [ Button.background (if isActive then Theme.Accent else "transparent")
          Button.foreground (if isActive then Theme.BgSidebar else Theme.TextMain)
          Button.borderThickness 0.0
          Button.cornerRadius 8.0
          Button.margin (10.0, 4.0)
          Button.padding (15.0, 10.0)
          Button.horizontalAlignment HorizontalAlignment.Stretch ]


module PlayerView =

    let tableHeader () =
        let col index label =
            TextBlock.create
                [ Grid.column index
                  TextBlock.text label
                  TextBlock.foreground Theme.TextMuted
                  TextBlock.fontSize 10.0
                  TextBlock.fontWeight FontWeight.Bold ]

        Border.create
            [ Border.padding (15.0, 8.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.playerListCols)
                        Grid.children [ col 0 "PLAYER"; col 1 "POS"; col 2 "AGE"; col 3 "CA / PA"; col 4 "VALUE" ] ]
              ) ]

    let private statusIndicator (p: Player) =
        match p.Status with
        | Injured(severity, _) ->
            let color =
                match severity with
                | Minor
                | Moderate -> Theme.Warning
                | _ -> Theme.Danger

            UI.badge "INJ" color
        | Suspended days -> UI.badge $"SUS {days}" Theme.Warning
        | Available -> Border.create [ Border.width 0.0 ]

    let row (p: Player) (currentDate: DateTime) (isSelected: bool) (isDragged: bool) onSelect =
        let leftBorderColor =
            if isSelected then Theme.Accent
            elif isDragged then Theme.DragBorder
            else "transparent"

        let bg =
            if isSelected then SelectionStyle.rowBg true
            elif isDragged then Theme.DragOverlay
            else "transparent"

        Border.create
            [ Border.background bg
              Border.borderBrush leftBorderColor
              Border.borderThickness (3.0, 0.0, 0.0, 0.0)
              Border.padding (12.0, 10.0)
              Border.onTapped (fun _ -> onSelect ())
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.playerListCols)
                        Grid.children
                            [ Grid.create
                                  [ Grid.column 0
                                    Grid.columnDefinitions "*, Auto"
                                    Grid.children
                                        [ StackPanel.create
                                              [ Grid.column 0
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.spacing 2.0
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text p.Name
                                                            TextBlock.fontWeight FontWeight.SemiBold
                                                            TextBlock.fontSize 13.0 ]
                                                      StackPanel.create
                                                          [ StackPanel.orientation Orientation.Horizontal
                                                            StackPanel.spacing 6.0
                                                            StackPanel.children
                                                                [ TextBlock.create
                                                                      [ TextBlock.text $"Morale {p.Morale}%%"
                                                                        TextBlock.fontSize 10.0
                                                                        TextBlock.foreground (
                                                                            Theme.moraleColor p.Morale
                                                                        ) ]
                                                                  TextBlock.create
                                                                      [ TextBlock.text
                                                                            $"·  {(Player.contractOf p |> Option.map _.ExpiryYear |> Option.defaultValue 0)}"
                                                                        TextBlock.fontSize 10.0
                                                                        TextBlock.foreground Theme.TextMuted ] ] ] ] ]
                                          StackPanel.create
                                              [ Grid.column 1
                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.margin (8.0, 0.0)
                                                StackPanel.children [ statusIndicator p ] ] ] ]

                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text $"%A{p.Position}"
                                    TextBlock.fontSize 11.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.Accent
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]

                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text (string (Player.age currentDate p))
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]

                              StackPanel.create
                                  [ Grid.column 3
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.spacing 3.0
                                    StackPanel.children
                                        [ DockPanel.create
                                              [ DockPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text (string p.CurrentSkill)
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground Theme.Accent
                                                            TextBlock.fontSize 12.0 ]
                                                      TextBlock.create
                                                          [ DockPanel.dock Dock.Right
                                                            TextBlock.text (string p.PotentialSkill)
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.foreground Theme.TextMuted
                                                            TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                          ProgressBar.create
                                              [ ProgressBar.minimum 0.0
                                                ProgressBar.maximum 200.0
                                                ProgressBar.value (float p.CurrentSkill)
                                                ProgressBar.height 3.0
                                                ProgressBar.background Theme.BgSidebar
                                                ProgressBar.foreground Theme.Accent ] ] ]

                              TextBlock.create
                                  [ Grid.column 4
                                    TextBlock.text $"${int (Player.playerValue p.CurrentSkill / 1_000_000m)}M"
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    type private StatGroup =
        { Title: string
          Color: string
          Stats: (string * int) list }

    let private physicalGroup (p: Player) =
        { Title = "PHYSICAL"
          Color = Theme.Accent
          Stats =
            [ "Acceleration", p.Physical.Acceleration
              "Pace", p.Physical.Pace
              "Agility", p.Physical.Agility
              "Balance", p.Physical.Balance
              "Jumping Reach", p.Physical.JumpingReach
              "Stamina", p.Physical.Stamina
              "Strength", p.Physical.Strength ] }

    let private mentalGroup (p: Player) =
        { Title = "MENTAL"
          Color = Theme.Accent
          Stats =
            [ "Aggression", p.Mental.Aggression
              "Composure", p.Mental.Composure
              "Concentration", p.Mental.Concentration
              "Vision", p.Mental.Vision
              "Positioning", p.Mental.Positioning
              "Bravery", p.Mental.Bravery
              "Work Rate", p.Mental.WorkRate
              "Leadership", p.Mental.Leadership ] }

    let private technicalGroup (p: Player) =
        { Title = "TECHNICAL"
          Color = Theme.Accent
          Stats =
            [ "Finishing", p.Technical.Finishing
              "Long Shots", p.Technical.LongShots
              "Dribbling", p.Technical.Dribbling
              "Ball Control", p.Technical.BallControl
              "Passing", p.Technical.Passing
              "Crossing", p.Technical.Crossing
              "Tackling", p.Technical.Tackling
              "Marking", p.Technical.Marking
              "Heading", p.Technical.Heading
              "Free Kick", p.Technical.FreeKick
              "Penalty", p.Technical.Penalty ] }

    let private goalkeepingGroup (p: Player) =
        { Title = "GOALKEEPING"
          Color = Theme.DragBorder
          Stats =
            [ "Reflexes", p.Goalkeeping.Reflexes
              "Handling", p.Goalkeeping.Handling
              "Kicking", p.Goalkeeping.Kicking
              "One On One", p.Goalkeeping.OneOnOne
              "Aerial Reach", p.Goalkeeping.AerialReach ] }

    let private statGroupsFor (p: Player) =
        let base' = [ physicalGroup p; mentalGroup p; technicalGroup p ]

        if p.Position = GK then
            base' @ [ goalkeepingGroup p ]
        else
            base'

    let private statRow label value (color: string) =
        StackPanel.create
            [ StackPanel.margin (0.0, 2.0)
              StackPanel.children
                  [ DockPanel.create
                        [ DockPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text label
                                      TextBlock.foreground Theme.TextMuted
                                      TextBlock.fontSize 11.0 ]
                                TextBlock.create
                                    [ TextBlock.text (string value)
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.fontSize 11.0
                                      DockPanel.dock Dock.Right ] ] ]
                    ProgressBar.create
                        [ ProgressBar.minimum 0.0
                          ProgressBar.maximum 20.0
                          ProgressBar.value (float value)
                          ProgressBar.height 3.0
                          ProgressBar.background Theme.BgMain
                          ProgressBar.foreground color ] ] ]

    let private renderGroup (g: StatGroup) : IView list =
        [ TextBlock.create
              [ TextBlock.text g.Title
                TextBlock.fontSize 10.0
                TextBlock.fontWeight FontWeight.Black
                TextBlock.foreground g.Color
                TextBlock.lineSpacing 1.5
                TextBlock.margin (0, 14, 0, 6) ]
          :> IView
          yield! g.Stats |> List.map (fun (label, value) -> statRow label value g.Color :> IView) ]

    let private miniBar label value (color: string) =
        Grid.create
            [ Grid.rowDefinitions "Auto, 4"
              Grid.children
                  [ DockPanel.create
                        [ Grid.row 0
                          DockPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text label
                                      TextBlock.fontSize 9.0
                                      TextBlock.foreground Theme.TextMuted ]
                                TextBlock.create
                                    [ TextBlock.text (string value)
                                      TextBlock.fontSize 9.0
                                      TextBlock.fontWeight FontWeight.Bold
                                      DockPanel.dock Dock.Right ] ] ]
                    ProgressBar.create
                        [ Grid.row 1
                          ProgressBar.minimum 0.0
                          ProgressBar.maximum 100.0
                          ProgressBar.value (float value)
                          ProgressBar.height 4.0
                          ProgressBar.background Theme.BgMain
                          ProgressBar.foreground color ] ] ]

    let detail (player: Player option) (currentDate: DateTime) =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness (1.0, 0.0, 0.0, 0.0)
              Border.child (
                  match player with
                  | None -> UI.emptyState IconName.squad "Select a player" ""
                  | Some p ->
                      Grid.create
                          [ Grid.rowDefinitions "Auto, *"
                            Grid.children
                                [ Border.create
                                      [ Grid.row 0
                                        Border.padding (20.0, 20.0, 20.0, 16.0)
                                        Border.borderBrush Theme.Border
                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 12.0
                                                  StackPanel.children
                                                      [ DockPanel.create
                                                            [ DockPanel.children
                                                                  [ UI.badge $"%A{p.Position}" Theme.Accent
                                                                    TextBlock.create
                                                                        [ TextBlock.text p.Name
                                                                          TextBlock.fontSize 20.0
                                                                          TextBlock.fontWeight FontWeight.Bold
                                                                          TextBlock.verticalAlignment
                                                                              VerticalAlignment.Center
                                                                          TextBlock.margin (10.0, 0.0, 0.0, 0.0) ] ] ]

                                                        UniformGrid.create
                                                            [ UniformGrid.columns 2
                                                              UniformGrid.rows 2
                                                              UniformGrid.children
                                                                  [ UI.statMiniCard
                                                                        PlayerIcon.age
                                                                        "AGE"
                                                                        (string (Player.age currentDate p))
                                                                        Theme.Accent
                                                                    UI.statMiniCard
                                                                        PlayerIcon.condition
                                                                        "CONDITION"
                                                                        $"{p.Condition}%%"
                                                                        Theme.Accent
                                                                    UI.statMiniCard
                                                                        PlayerIcon.skill
                                                                        "CA / PA"
                                                                        $"{p.CurrentSkill} / {p.PotentialSkill}"
                                                                        Theme.Accent
                                                                    UI.statMiniCard
                                                                        PlayerIcon.contract
                                                                        "CONTRACT"
                                                                        (Player.contractOf p
                                                                         |> Option.map (_.ExpiryYear >> string)
                                                                         |> Option.defaultValue "—")
                                                                        Theme.Accent ] ]

                                                        Grid.create
                                                            [ Grid.columnDefinitions "*, 10, *"
                                                              Grid.children
                                                                  [ miniBar
                                                                        "MORALE"
                                                                        p.Morale
                                                                        (Theme.moraleColor p.Morale)
                                                                    Grid.create
                                                                        [ Grid.column 2
                                                                          Grid.children
                                                                              [ miniBar
                                                                                    "MATCH FIT"
                                                                                    p.MatchFitness
                                                                                    Theme.Accent ] ] ] ] ] ]
                                        ) ]

                                  ScrollViewer.create
                                      [ Grid.row 1
                                        ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Visible
                                        ScrollViewer.padding (20.0, 4.0, 20.0, 20.0)
                                        ScrollViewer.content (
                                            StackPanel.create
                                                [ StackPanel.children
                                                      [ yield! statGroupsFor p |> List.collect renderGroup
                                                        Border.create [ Border.height 40.0 ] ] ]
                                        ) ] ] ]
                      :> IView
              ) ]


module Display =

    module Matches =

        let private teamBlock (name: string) initial align =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 15.0
                  StackPanel.horizontalAlignment align
                  StackPanel.children
                      [ if align = HorizontalAlignment.Right then
                            TextBlock.create
                                [ TextBlock.text (name.ToUpper())
                                  TextBlock.fontSize 24.0
                                  TextBlock.fontWeight FontWeight.Black
                                  TextBlock.foreground "White"
                                  TextBlock.verticalAlignment VerticalAlignment.Center ]

                        Border.create
                            [ Border.width 60.0
                              Border.height 60.0
                              Border.cornerRadius 30.0
                              Border.background (
                                  if align = HorizontalAlignment.Right then
                                      "White"
                                  else
                                      "#334155"
                              )
                              Border.child (
                                  TextBlock.create
                                      [ TextBlock.text initial
                                        TextBlock.fontSize 28.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground (
                                            if align = HorizontalAlignment.Right then
                                                "#7f1d1d"
                                            else
                                                "White"
                                        )
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                              ) ]

                        if align = HorizontalAlignment.Left then
                            TextBlock.create
                                [ TextBlock.text (name.ToUpper())
                                  TextBlock.fontSize 24.0
                                  TextBlock.fontWeight FontWeight.Black
                                  TextBlock.foreground Theme.TextSub
                                  TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

        let nextMatchBanner (homeName: string) (awayName: string) (date: DateTime) (location: string) =
            Border.create
                [ Border.cornerRadius 12.0
                  Border.margin (0.0, 0.0, 0.0, 30.0)
                  Border.padding 30.0
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "*, Auto, *"
                            Grid.children
                                [ StackPanel.create
                                      [ Grid.column 0
                                        StackPanel.children
                                            [ teamBlock homeName (homeName.Substring(0, 1)) HorizontalAlignment.Right ] ]

                                  StackPanel.create
                                      [ Grid.column 1
                                        StackPanel.margin (40.0, 0.0)
                                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.children
                                            [ TextBlock.create
                                                  [ TextBlock.text $"NEXT MATCH ({location})"
                                                    TextBlock.foreground Theme.Danger
                                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                    TextBlock.fontSize 10.0 ]
                                              TextBlock.create
                                                  [ TextBlock.text (date.ToString("HH:mm"))
                                                    TextBlock.fontSize 32.0
                                                    TextBlock.fontWeight FontWeight.Black
                                                    TextBlock.foreground "White"
                                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                              TextBlock.create
                                                  [ TextBlock.text (date.ToString("dd MMMM yyyy"))
                                                    TextBlock.foreground Theme.TextSub
                                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                    TextBlock.fontSize 12.0 ] ] ]

                                  StackPanel.create
                                      [ Grid.column 2
                                        StackPanel.children
                                            [ teamBlock awayName (awayName.Substring(0, 1)) HorizontalAlignment.Left ] ] ] ]
                  ) ]

        let emptyBanner () =
            Border.create
                [ Border.cornerRadius 12.0
                  Border.margin (0.0, 0.0, 0.0, 30.0)
                  Border.padding 40.0
                  Border.background Theme.BgCard
                  Border.child (
                      TextBlock.create
                          [ TextBlock.text "No upcoming matches"
                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                            TextBlock.foreground Theme.TextMuted ]
                  ) ]

    module Tables =

        type RowViewModel =
            { Pos: int
              TeamName: string
              Points: int
              Stats: string
              IsUser: bool
              PosColor: string }

        let leagueTable
            (leagues: (int * string) list)
            (selectedId: int)
            (onSelect: int -> unit)
            (rows: RowViewModel list)
            =
            StackPanel.create
                [ StackPanel.children
                      [ Border.create
                            [ Border.padding 15.0
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.child (
                                  DockPanel.create
                                      [ DockPanel.children
                                            [ StackPanel.create
                                                  [ StackPanel.dock Dock.Right
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                                    StackPanel.children
                                                        [ TextBlock.create
                                                              [ TextBlock.text "W-D-L"
                                                                TextBlock.width 60.0
                                                                TextBlock.textAlignment TextAlignment.Right
                                                                TextBlock.fontSize 10.0
                                                                TextBlock.foreground Theme.TextMuted ]
                                                          TextBlock.create
                                                              [ TextBlock.text "PTS"
                                                                TextBlock.width 40.0
                                                                TextBlock.textAlignment TextAlignment.Right
                                                                TextBlock.fontSize 10.0
                                                                TextBlock.foreground Theme.TextMuted
                                                                TextBlock.fontWeight FontWeight.Bold ] ] ]
                                              StackPanel.create
                                                  [ StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.children
                                                        [ for id, name in leagues do
                                                              UI.tabButton name (id = selectedId) (fun _ -> onSelect id) ] ] ] ]
                              ) ]

                        ScrollViewer.create
                            [ ScrollViewer.maxHeight 450.0
                              ScrollViewer.content (
                                  StackPanel.create
                                      [ StackPanel.children
                                            [ for row in rows do
                                                  Border.create
                                                      [ Border.background (
                                                            if row.IsUser then Theme.AccentAlt + "30" else "transparent"
                                                        )
                                                        Border.padding (15.0, 8.0)
                                                        Border.borderBrush (
                                                            if row.IsUser then "transparent" else Theme.BgCard
                                                        )
                                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                        Border.child (
                                                            DockPanel.create
                                                                [ DockPanel.children
                                                                      [ TextBlock.create
                                                                            [ TextBlock.dock Dock.Right
                                                                              TextBlock.width 40.0
                                                                              TextBlock.textAlignment
                                                                                  TextAlignment.Right
                                                                              TextBlock.text (string row.Points)
                                                                              TextBlock.fontWeight FontWeight.Bold
                                                                              TextBlock.foreground (
                                                                                  if row.IsUser then
                                                                                      "White"
                                                                                  else
                                                                                      Theme.TextMain
                                                                              ) ]
                                                                        TextBlock.create
                                                                            [ TextBlock.dock Dock.Right
                                                                              TextBlock.width 60.0
                                                                              TextBlock.textAlignment
                                                                                  TextAlignment.Right
                                                                              TextBlock.text row.Stats
                                                                              TextBlock.fontSize 11.0
                                                                              TextBlock.foreground (
                                                                                  if row.IsUser then
                                                                                      Theme.TextSub
                                                                                  else
                                                                                      Theme.TextMuted
                                                                              ) ]
                                                                        TextBlock.create
                                                                            [ TextBlock.text (string row.Pos)
                                                                              TextBlock.width 30.0
                                                                              TextBlock.foreground row.PosColor
                                                                              TextBlock.fontWeight FontWeight.Bold ]
                                                                        TextBlock.create
                                                                            [ TextBlock.text row.TeamName
                                                                              TextBlock.fontWeight (
                                                                                  if row.IsUser then
                                                                                      FontWeight.Bold
                                                                                  else
                                                                                      FontWeight.Normal
                                                                              )
                                                                              TextBlock.foreground (
                                                                                  if row.IsUser then
                                                                                      "White"
                                                                                  else
                                                                                      Theme.TextMain
                                                                              ) ] ] ]
                                                        ) ] ] ]
                              ) ] ] ]

