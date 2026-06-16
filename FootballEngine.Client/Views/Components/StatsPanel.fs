namespace FootballEngine.Components

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open FootballEngine.Domain

module StatsPanel =

    type MatchStats =
        { HomePossession: float
          AwayPossession: float
          HomeShots: int
          AwayShots: int
          HomeShotsOnTarget: int
          AwayShotsOnTarget: int
          HomeCorners: int
          AwayCorners: int
          HomeFouls: int
          AwayFouls: int
          HomeYellows: int
          AwayYellows: int
          HomeReds: int
          AwayReds: int }

    let computeStats (homeClubId: ClubId) (events: MatchEvent list) : MatchStats =
        let mutable homeShots = 0
        let mutable awayShots = 0
        let mutable homeOnTarget = 0
        let mutable awayOnTarget = 0
        let mutable homeCorners = 0
        let mutable awayCorners = 0
        let mutable homeFouls = 0
        let mutable awayFouls = 0
        let mutable homeYellows = 0
        let mutable awayYellows = 0
        let mutable homeReds = 0
        let mutable awayReds = 0

        for e in events do
            let isHome = e.ClubId = homeClubId

            match e.Type with
            | MatchEventType.ShotOnTarget ->
                if isHome then
                    homeOnTarget <- homeOnTarget + 1
                else
                    awayOnTarget <- awayOnTarget + 1

                if isHome then
                    homeShots <- homeShots + 1
                else
                    awayShots <- awayShots + 1
            | MatchEventType.ShotOffTarget
            | MatchEventType.ShotLaunched
            | MatchEventType.ShotBlocked ->
                if isHome then
                    homeShots <- homeShots + 1
                else
                    awayShots <- awayShots + 1
            | MatchEventType.Corner ->
                if isHome then
                    homeCorners <- homeCorners + 1
                else
                    awayCorners <- awayCorners + 1
            | MatchEventType.FoulCommitted ->
                if isHome then
                    homeFouls <- homeFouls + 1
                else
                    awayFouls <- awayFouls + 1
            | MatchEventType.YellowCard ->
                if isHome then
                    homeYellows <- homeYellows + 1
                else
                    awayYellows <- awayYellows + 1
            | MatchEventType.RedCard ->
                if isHome then
                    homeReds <- homeReds + 1
                else
                    awayReds <- awayReds + 1
            | _ -> ()

        { HomePossession = 50.0
          AwayPossession = 50.0
          HomeShots = homeShots
          AwayShots = awayShots
          HomeShotsOnTarget = homeOnTarget
          AwayShotsOnTarget = awayOnTarget
          HomeCorners = homeCorners
          AwayCorners = awayCorners
          HomeFouls = homeFouls
          AwayFouls = awayFouls
          HomeYellows = homeYellows
          AwayYellows = awayYellows
          HomeReds = homeReds
          AwayReds = awayReds }

    let buildStatsPanel (stats: MatchStats) : StackPanel =
        let panel = StackPanel(Orientation = Orientation.Vertical, Margin = Thickness(8.0))

        let addRow label homeVal awayVal =
            let row =
                StackPanel(Orientation = Orientation.Horizontal, Margin = Thickness(0.0, 2.0))

            row.Children.Add(
                TextBlock(
                    Text = $"{homeVal}",
                    Width = 40.0,
                    TextAlignment = TextAlignment.Right,
                    Foreground = SolidColorBrush(Colors.White)
                )
            )
            |> ignore

            row.Children.Add(
                TextBlock(
                    Text = label,
                    Width = 120.0,
                    TextAlignment = TextAlignment.Center,
                    Foreground = SolidColorBrush(Colors.LightGray)
                )
            )
            |> ignore

            row.Children.Add(
                TextBlock(
                    Text = $"{awayVal}",
                    Width = 40.0,
                    TextAlignment = TextAlignment.Left,
                    Foreground = SolidColorBrush(Colors.White)
                )
            )
            |> ignore

            panel.Children.Add(row) |> ignore

        addRow "Shots" stats.HomeShots stats.AwayShots
        addRow "On Target" stats.HomeShotsOnTarget stats.AwayShotsOnTarget
        addRow "Corners" stats.HomeCorners stats.AwayCorners
        addRow "Fouls" stats.HomeFouls stats.AwayFouls
        addRow "Yellows" stats.HomeYellows stats.AwayYellows
        addRow "Reds" stats.HomeReds stats.AwayReds

        panel
