namespace FootballEngine.Components

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open FootballEngine.Core.Match
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

    let private unwrapPid (FootballEngine.Core.Match.PlayerId n) = n

    let computeStats
        (homeClubId: ClubId)
        (awayClubId: ClubId)
        (events: MatchEvent list)
        (clubOfPlayer: PlayerId -> ClubId option)
        : MatchStats =
        let m = MatchMetrics.extract events homeClubId awayClubId clubOfPlayer

        let homeYellows, homeReds, awayYellows, awayReds =
            events
            |> List.fold
                (fun (hy, hr, ay, ar) ev ->
                    match ev.Data with
                    | EventData.Card(player, CardColor.Yellow) ->
                        if clubOfPlayer (unwrapPid player) = Some homeClubId then
                            (hy + 1, hr, ay, ar)
                        else
                            (hy, hr, ay + 1, ar)
                    | EventData.Card(player, CardColor.Red)
                    | EventData.RedCardIssued player ->
                        if clubOfPlayer (unwrapPid player) = Some homeClubId then
                            (hy, hr + 1, ay, ar)
                        else
                            (hy, hr, ay, ar + 1)
                    | _ -> (hy, hr, ay, ar))
                (0, 0, 0, 0)

        { HomePossession = m.PossessionHome * 100.0
          AwayPossession = m.PossessionAway * 100.0
          HomeShots = m.ShotsHome
          AwayShots = m.ShotsAway
          HomeShotsOnTarget = m.ShotsOnTargetHome
          AwayShotsOnTarget = m.ShotsOnTargetAway
          HomeCorners = m.CornersHome
          AwayCorners = m.CornersAway
          HomeFouls = m.FoulsHome
          AwayFouls = m.FoulsAway
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
