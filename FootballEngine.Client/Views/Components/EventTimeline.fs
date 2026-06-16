namespace FootballEngine.Components

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Controls.Shapes
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
        match e.Type with
        | MatchEventType.Goal -> Goal
        | MatchEventType.OwnGoal -> Goal
        | MatchEventType.YellowCard -> YellowCard
        | MatchEventType.RedCard -> RedCard
        | MatchEventType.SubstitutionIn
        | MatchEventType.SubstitutionOut -> Substitution
        | MatchEventType.Corner -> Corner
        | MatchEventType.FreeKick _
        | MatchEventType.IndirectFreeKickAwarded _ -> FreeKick
        | MatchEventType.ShotOnTarget
        | MatchEventType.ShotOffTarget
        | MatchEventType.ShotLaunched
        | MatchEventType.ShotBlocked -> Shot
        | MatchEventType.Save
        | MatchEventType.SaveCaught _
        | MatchEventType.SaveParried _ -> Save
        | MatchEventType.FoulCommitted -> Foul
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

    let buildTimeline (events: MatchEvent list) (totalSubTicks: int) : StackPanel =
        let panel = StackPanel(Orientation = Orientation.Horizontal, Height = 30.0)

        let mutable lastMinute = -1

        for e in events do
            let minute = e.SubTick / 40

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
