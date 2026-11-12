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

module Display =

    module Matches =

        let private teamBlock (name: string) initial align =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 14.0
                  StackPanel.horizontalAlignment align
                  StackPanel.verticalAlignment VerticalAlignment.Center
                  StackPanel.children
                      [ if align = HorizontalAlignment.Right then
                            TextBlock.create
                                [ TextBlock.text (name.ToUpper())
                                  TextBlock.fontSize 20.0
                                  TextBlock.fontWeight FontWeight.Black
                                  TextBlock.foreground Theme.TextMain
                                  TextBlock.verticalAlignment VerticalAlignment.Center ]

                        Border.create
                            [ Border.width 56.0
                              Border.height 56.0
                              Border.cornerRadius 28.0
                              Border.background (
                                  if align = HorizontalAlignment.Right then
                                      Theme.TextMain
                                  else
                                      Theme.BgElevated
                              )
                              Border.borderBrush (
                                  if align = HorizontalAlignment.Right then
                                      Theme.TextMain
                                  else
                                      Theme.BorderLight
                              )
                              Border.borderThickness 1.0
                              Border.child (
                                  TextBlock.create
                                      [ TextBlock.text initial
                                        TextBlock.fontSize 24.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground (
                                            if align = HorizontalAlignment.Right then
                                                "#0f172a"
                                            else
                                                Theme.TextSub
                                        )
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                              ) ]

                        if align = HorizontalAlignment.Left then
                            TextBlock.create
                                [ TextBlock.text (name.ToUpper())
                                  TextBlock.fontSize 20.0
                                  TextBlock.fontWeight FontWeight.Black
                                  TextBlock.foreground Theme.TextSub
                                  TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

        let nextMatchBanner (homeName: string) (awayName: string) (date: DateTime) (location: string) =
            Border.create
                [ Border.cornerRadius Theme.RadiusXl
                  Border.margin (0.0, 0.0, 0.0, 20.0)
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.clipToBounds true
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "*, Auto, *"
                            Grid.children
                                [ Border.create
                                      [ Grid.columnSpan 3
                                        Border.height 3.0
                                        Border.background Theme.Accent
                                        Border.verticalAlignment VerticalAlignment.Top ]

                                  StackPanel.create
                                      [ Grid.column 0
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.margin (28.0, 20.0, 0.0, 20.0)
                                        StackPanel.children
                                            [ teamBlock homeName (homeName.Substring(0, 1)) HorizontalAlignment.Right ] ]

                                  Border.create
                                      [ Grid.column 1
                                        Border.background Theme.BgElevated
                                        Border.cornerRadius Theme.RadiusLg
                                        Border.borderBrush Theme.BorderLight
                                        Border.borderThickness 1.0
                                        Border.padding (24.0, 14.0)
                                        Border.margin (28.0, 0.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 2.0
                                                  StackPanel.horizontalAlignment HorizontalAlignment.Center
                                                  StackPanel.children
                                                      [ Border.create
                                                            [ Border.background (Theme.withAlpha "18" Theme.Accent)
                                                              Border.cornerRadius Theme.RadiusPill
                                                              Border.padding (8.0, 3.0)
                                                              Border.horizontalAlignment HorizontalAlignment.Center
                                                              Border.child (
                                                                  TextBlock.create
                                                                      [ TextBlock.text $"NEXT MATCH  •  {location}"
                                                                        TextBlock.foreground Theme.Accent
                                                                        TextBlock.fontSize
                                                                            Theme.currentTokens.Value.Typography.Detail
                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                        TextBlock.horizontalAlignment
                                                                            HorizontalAlignment.Center ]
                                                              ) ]
                                                        TextBlock.create
                                                            [ TextBlock.text "VS"
                                                              TextBlock.fontSize
                                                                  Theme.currentTokens.Value.Typography.Detail
                                                              TextBlock.fontWeight FontWeight.Black
                                                              TextBlock.foreground Theme.TextDim
                                                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                              TextBlock.margin (0.0, 6.0, 0.0, 2.0) ]
                                                        TextBlock.create
                                                            [ TextBlock.text (date.ToString("dddd").ToUpper())
                                                              TextBlock.fontSize 28.0
                                                              TextBlock.fontWeight FontWeight.Black
                                                              TextBlock.foreground Theme.TextMain
                                                              TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                                        TextBlock.create
                                                            [ TextBlock.text (date.ToString("dd MMMM yyyy").ToUpper())
                                                              TextBlock.foreground Theme.TextMuted
                                                              TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                              TextBlock.fontSize
                                                                  Theme.currentTokens.Value.Typography.Detail ] ] ]
                                        ) ]

                                  StackPanel.create
                                      [ Grid.column 2
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.margin (0.0, 20.0, 28.0, 20.0)
                                        StackPanel.children
                                            [ teamBlock awayName (awayName.Substring(0, 1)) HorizontalAlignment.Left ] ] ] ]
                  ) ]

        let nextMatchBannerCompact (homeName: string) (awayName: string) (date: DateTime) (location: string) =
            Border.create
                [ Border.cornerRadius Theme.RadiusXl
                  Border.margin (0.0, 0.0, 0.0, 20.0)
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.clipToBounds true
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 6.0
                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.margin (16.0, 18.0)
                            StackPanel.children
                                [ Border.create
                                      [ Border.background (Theme.withAlpha "18" Theme.Accent)
                                        Border.cornerRadius Theme.RadiusPill
                                        Border.padding (8.0, 3.0)
                                        Border.horizontalAlignment HorizontalAlignment.Center
                                        Border.child (
                                            TextBlock.create
                                                [ TextBlock.text $"NEXT MATCH  |  {location}"
                                                  TextBlock.foreground Theme.Accent
                                                  TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                                  TextBlock.fontWeight FontWeight.Bold ]
                                        ) ]
                                  TextBlock.create
                                      [ TextBlock.text $"{homeName.ToUpper()}  VS  {awayName.ToUpper()}"
                                        TextBlock.fontSize 18.0
                                        TextBlock.fontWeight FontWeight.Black
                                        TextBlock.foreground Theme.TextMain
                                        TextBlock.textAlignment TextAlignment.Center
                                        TextBlock.textWrapping TextWrapping.Wrap ]
                                  TextBlock.create
                                      [ TextBlock.text (date.ToString("dddd, dd MMMM yyyy").ToUpper())
                                        TextBlock.fontSize Theme.currentTokens.Value.Typography.Detail
                                        TextBlock.foreground Theme.TextMuted
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                  ) ]

        let emptyBanner () =
            Border.create
                [ Border.cornerRadius Theme.RadiusLg
                  Border.margin (0.0, 0.0, 0.0, 20.0)
                  Border.padding 32.0
                  Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.spacing 8.0
                            StackPanel.children
                                [ Border.create
                                      [ Border.background (Theme.withAlpha "14" Theme.TextMuted)
                                        Border.cornerRadius 999.0
                                        Border.padding 12.0
                                        Border.horizontalAlignment HorizontalAlignment.Center
                                        Border.child (Icons.iconSm IconName.calendar Theme.TextDim) ]
                                  TextBlock.create
                                      [ TextBlock.text "No upcoming matches"
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.fontSize 13.0 ] ] ]
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
                                                                TextBlock.fontSize
                                                                    Theme.currentTokens.Value.Typography.Detail
                                                                TextBlock.foreground Theme.TextMuted ]
                                                          TextBlock.create
                                                              [ TextBlock.text "PTS"
                                                                TextBlock.width 40.0
                                                                TextBlock.textAlignment TextAlignment.Right
                                                                TextBlock.fontSize
                                                                    Theme.currentTokens.Value.Typography.Detail
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
                                                            (if row.IsUser then
                                                                 Theme.withAlpha "30" Theme.AccentAlt
                                                             else
                                                                 "transparent")
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
                                                                              TextBlock.fontSize
                                                                                  Theme.currentTokens.Value.Typography.Detail
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
