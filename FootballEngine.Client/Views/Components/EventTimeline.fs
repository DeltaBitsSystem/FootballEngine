namespace FootballEngine.Components

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Controls.Shapes
open FootballEngine.Core.Match
open FootballEngine.Domain

module EventTimeline =

    type EventType =
        | Goal
        | YellowCard
        | RedCard
        | Substitution
        | Corner
        | FreeKick
        | Shot
        | Save
        | Foul
        | Other

    let eventTypeFromMatchEvent (e: MatchEvent) : EventType =
        match e.Data with
        | EventData.Goal _ -> Goal
        | EventData.Card(_, CardColor.Yellow) -> YellowCard
        | EventData.Card(_, CardColor.Red)
        | EventData.RedCardIssued _ -> RedCard
        | EventData.Substitution _ -> Substitution
        | EventData.SetPiece(SetPieceKind.Corner, _) -> Corner
        | EventData.SetPiece(SetPieceKind.FreeKick, _) -> FreeKick
        | EventData.Shot _ -> Shot
        | EventData.Save _ -> Save
        | EventData.Foul _ -> Foul
        | _ -> Other

    let eventColor (t: EventType) : IBrush =
        match t with
        | Goal -> SolidColorBrush(Colors.Green)
        | YellowCard -> SolidColorBrush(Colors.Yellow)
        | RedCard -> SolidColorBrush(Colors.Red)
        | Substitution -> SolidColorBrush(Colors.Blue)
        | Corner -> SolidColorBrush(Colors.Orange)
        | FreeKick -> SolidColorBrush(Colors.LightBlue)
        | Shot -> SolidColorBrush(Colors.Purple)
        | Save -> SolidColorBrush(Colors.Cyan)
        | Foul -> SolidColorBrush(Colors.Gray)
        | Other -> SolidColorBrush(Colors.DarkGray)

    let buildTimeline (events: MatchEvent list) (_totalSubTicks: int) : StackPanel =
        let panel = StackPanel(Orientation = Orientation.Horizontal, Height = 30.0)

        let mutable lastMinute = -1

        for e in events do
            let minute = int e.Tick / 40

            if minute <> lastMinute then
                let marker =
                    Ellipse(Width = 8.0, Height = 8.0, Fill = eventColor (eventTypeFromMatchEvent e))

                let tip = TextBlock(Text = $"{minute}'", FontSize = 8.0, Margin = Thickness(2.0))

                let wrapper =
                    StackPanel(Orientation = Orientation.Vertical, Margin = Thickness(1.0))

                wrapper.Children.Add(marker) |> ignore
                wrapper.Children.Add(tip) |> ignore
                panel.Children.Add(wrapper) |> ignore
                lastMinute <- minute

        panel
