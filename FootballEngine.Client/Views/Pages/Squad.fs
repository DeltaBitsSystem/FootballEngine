namespace FootballEngine.Pages

open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Components

open FootballEngine.Icons


module SquadPresenter =

    type TeamStatsVM =
        { TotalValue: string
          AvgSkill: string
          TotalWages: string
          SquadSize: string }

    let getTeamStats (players: Player list) =
        let totalValue = players |> List.sumBy (fun p -> Player.playerValue p.CurrentSkill)

        let totalWages =
            players
            |> List.sumBy (fun p -> Player.contractOf p |> Option.map _.Salary |> Option.defaultValue 0m)

        let avgSkill =
            if players.IsEmpty then
                0.0
            else
                players |> List.averageBy (fun p -> float p.CurrentSkill)

        { TotalValue = $"€{int (totalValue / 1_000_000m)}M"
          AvgSkill = $"%.0f{avgSkill}"
          TotalWages = $"€{totalWages / 1_000_000m:N1}M"
          SquadSize = string players.Length }

    let private positionLine (pos: Position) =
        match pos with
        | GK -> 0, TranslationKey.LineGoalkeepers
        | DR
        | DC
        | DL
        | WBR
        | WBL -> 1, TranslationKey.LineDefenders
        | DM
        | MC
        | MR
        | ML
        | AMR
        | AMC
        | AML -> 2, TranslationKey.LineMidfielders
        | ST -> 3, TranslationKey.LineAttackers

    let getSortedPlayers (players: Player list) (currentDate: DateTime) (sortBy: SortField) =
        match sortBy with
        | ByName -> players |> List.sortBy _.Name
        | ByCA -> players |> List.sortByDescending _.CurrentSkill
        | ByAge -> players |> List.sortBy (fun p -> Player.age currentDate p)
        | ByValue -> players |> List.sortByDescending (fun p -> Player.playerValue p.CurrentSkill)
        | ByPosition -> players |> List.sortBy (fun p -> fst (positionLine p.Position), -p.CurrentSkill)

    let getGroupedPlayers (players: Player list) (currentDate: DateTime) (sortBy: SortField) =
        match sortBy with
        | ByName
        | ByCA
        | ByAge
        | ByValue -> [ None, getSortedPlayers players currentDate sortBy ]
        | ByPosition ->
            players
            |> List.groupBy (fun p -> positionLine p.Position)
            |> List.sortBy fst
            |> List.map (fun ((_, label), ps) ->
                Some(Localization.resolve label []), ps |> List.sortByDescending _.CurrentSkill)


module Squad =

    let private tr key = Localization.resolve key []

    let private sortPill (label: string) (isActive: bool) onClick : IView =
        Border.create
            [ Border.margin (0.0, 0.0, 8.0, 8.0)
              Border.child (UI.tabButton label isActive onClick) ]
        :> IView

    let private lineHeader (label: string) : IView =
        Border.create
            [ Border.margin (4.0, 14.0, 0.0, 8.0)
              Border.child (UI.eyebrow label Theme.TextDim) ]
        :> IView

    let squadView (state: State) dispatch =
        match state.Mode with
        | InGame(gs, _) ->
            let squad = GameState.getSquad gs.UserClubId gs
            let stats = SquadPresenter.getTeamStats squad

            let groups =
                SquadPresenter.getGroupedPlayers squad gs.CurrentDate state.Squad.SortBy

            let clubKit = ClubIdentity.kitOf gs gs.UserClubId

            let sort = state.Squad.SortBy

            let setSort field =
                fun _ -> dispatch (SquadMsg(SquadMsg.SetSort field))

            let header =
                DockPanel.create
                    [ DockPanel.lastChildFill false
                      DockPanel.margin (0.0, 0.0, 0.0, 12.0)
                      DockPanel.children
                          [ StackPanel.create
                                [ DockPanel.dock Dock.Left
                                  StackPanel.spacing 6.0
                                  StackPanel.children
                                      [ UI.display (tr TranslationKey.SquadTitle) 44.0 Theme.TextMain
                                        TextBlock.create
                                            [ TextBlock.text (
                                                  Localization.resolve
                                                      TranslationKey.SquadSummary
                                                      [ "count", stats.SquadSize
                                                        "value", stats.TotalValue
                                                        "wages", stats.TotalWages ]
                                              )
                                              TextBlock.fontSize 14.0
                                              TextBlock.foreground Theme.TextMuted ] ] ]
                            WrapPanel.create
                                [ DockPanel.dock Dock.Right
                                  WrapPanel.verticalAlignment VerticalAlignment.Bottom
                                  WrapPanel.children
                                      [ sortPill (tr TranslationKey.SortByLine) (sort = ByPosition) (setSort ByPosition)
                                        sortPill (tr TranslationKey.SortBySkill) (sort = ByCA) (setSort ByCA)
                                        sortPill (tr TranslationKey.SortByAge) (sort = ByAge) (setSort ByAge)
                                        sortPill (tr TranslationKey.SortByValue) (sort = ByValue) (setSort ByValue)
                                        sortPill (tr TranslationKey.SortByName) (sort = ByName) (setSort ByName) ] ] ] ]

            let list =
                ScrollViewer.create
                    [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                      ScrollViewer.padding (0.0, 0.0, 12.0, 0.0)
                      ScrollViewer.content (
                          StackPanel.create
                              [ StackPanel.children
                                    [ for label, players in groups do
                                          match label with
                                          | Some text -> lineHeader text
                                          | None -> ()

                                          for player in players do
                                              let isSelected =
                                                  state.Squad.SelectedPlayer |> Option.exists (fun id -> id = player.Id)

                                              PlayerView.row
                                                  player
                                                  gs.CurrentDate
                                                  isSelected
                                                  (state.Squad.DraggedPlayer = Some player.Id)
                                                  (fun () -> dispatch (SquadMsg(SquadMsg.SelectPlayer player.Id)))
                                              |> View.withKey (string player.Id) ] ]
                      ) ]

            let selected =
                state.Squad.SelectedPlayer
                |> Option.bind (fun id -> gs.Players |> Map.tryFind id)

            Grid.create
                [ Grid.columnDefinitions "*, 24, 420"
                  Grid.rowDefinitions "Auto, *"
                  Grid.margin (32.0, 8.0, 32.0, 24.0)
                  Grid.children
                      [ Border.create [ Grid.row 0; Grid.columnSpan 3; Border.child header ]
                        Border.create [ Grid.row 1; Grid.column 0; Border.child list ]
                        Border.create
                            [ Grid.row 1
                              Grid.column 2
                              Border.child (PlayerView.detail selected gs.CurrentDate clubKit) ] ] ]
            :> IView
        | _ -> Border.create [] :> IView
