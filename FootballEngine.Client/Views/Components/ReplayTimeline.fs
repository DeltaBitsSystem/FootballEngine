namespace FootballEngine.Components

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.Core.Match
open FootballEngine.LiveMatch

/// Recording bar: the playhead, one marker per moment, and seeks by bar, marker or moment step.
module ReplayTimeline =
    // One column per slice of the recording: markers and the playhead sit on the same grid the bar is clicked on.
    let private buckets = 360

    let private kindColor (kind: ReplayMomentKind) =
        let eventType =
            match kind with
            | ReplayMomentKind.Goal -> EventTimeline.Goal
            | ReplayMomentKind.Save -> EventTimeline.Save
            | ReplayMomentKind.Woodwork
            | ReplayMomentKind.Penalty -> EventTimeline.Other
            | ReplayMomentKind.FreeKick -> EventTimeline.FreeKick
            | ReplayMomentKind.Corner -> EventTimeline.Corner
            | ReplayMomentKind.YellowCard -> EventTimeline.YellowCard
            | ReplayMomentKind.RedCard -> EventTimeline.RedCard
            | ReplayMomentKind.Substitution -> EventTimeline.Substitution

        EventTimeline.eventColor eventType

    let private bucketOf (total: int) (tick: int) =
        if total <= 0 then
            0
        else
            min (buckets - 1) (max 0 (int (int64 tick * int64 buckets / int64 total)))

    let view (live: LiveMatchState) (moments: ReplayMoment list) (total: int<subtick>) (dispatch: Msg -> unit) : IView =
        let tokens = Theme.currentTokens.Value
        let colors = tokens.Colors
        let localized key = Localization.resolve key []
        let totalTicks = max 1 (int total)
        let playhead = int live.Clock.Elapsed

        let leadTicks =
            ReplayMoments.leadSeconds * SimulationClock.defaultClock.SubTicksPerSecond

        let seek (tick: int) =
            dispatch (LiveMsg(LiveMsg.SeekReplay(max 0 tick * 1<subtick>)))

        let momentStarts =
            moments |> List.map (fun moment -> max 0 (int moment.Tick - leadTicks))

        let previousStart =
            momentStarts |> List.filter (fun start -> start < playhead) |> List.tryLast

        let nextStart = momentStarts |> List.tryFind (fun start -> start > playhead)

        // The overlay takes every click on the bar that no marker claims, so it reads the position on the bar.
        let overlay: IView =
            Border.create
                [ Grid.columnSpan buckets
                  Border.background "Transparent"
                  Border.onPointerPressed (fun e ->
                      let bar = e.Source :?> Control
                      let x = e.GetCurrentPoint(bar).Position.X
                      let width = max 1.0 bar.Bounds.Width
                      seek (int (x / width * float totalTicks))) ]
            :> IView

        let markers: IView list =
            moments
            |> List.map (fun moment ->
                Border.create
                    [ Grid.column (bucketOf totalTicks (int moment.Tick))
                      Border.width 4.0
                      Border.height 22.0
                      Border.cornerRadius 2.0
                      Border.horizontalAlignment HorizontalAlignment.Center
                      Border.verticalAlignment VerticalAlignment.Center
                      Border.background (kindColor moment.Kind)
                      Border.onPointerPressed (fun e ->
                          e.Handled <- true
                          seek (int moment.Tick - leadTicks)) ]
                :> IView)

        let playheadMark: IView =
            Border.create
                [ Grid.column (bucketOf totalTicks playhead)
                  Border.width 2.0
                  Border.background colors.Accent
                  Border.horizontalAlignment HorizontalAlignment.Center ]
            :> IView

        let bar: IView =
            Border.create
                [ Border.height 30.0
                  Border.background colors.Surface2
                  Border.borderBrush colors.BorderSubtle
                  Border.borderThickness 1.0
                  Border.cornerRadius tokens.Radii.Control
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions (String.Join(",", Array.create buckets "*"))
                            Grid.children (overlay :: markers @ [ playheadMark ]) ]
                  ) ]
            :> IView

        let stepButton (column: int) (label: string) (target: int option) : IView =
            Button.create
                [ Grid.column column
                  Button.content (
                      TextBlock.create
                          [ TextBlock.text label
                            TextBlock.fontSize 12.0
                            TextBlock.fontWeight FontWeight.SemiBold
                            TextBlock.foreground colors.TextSecondary ]
                  )
                  Button.isEnabled target.IsSome
                  Button.background colors.Surface2
                  Button.borderBrush colors.BorderSubtle
                  Button.borderThickness 1.0
                  Button.cornerRadius tokens.Radii.Control
                  Button.padding (10.0, 4.0)
                  Button.minHeight tokens.Controls.StandardHeight
                  Button.onClick (fun _ -> target |> Option.iter seek) ]
            :> IView

        Grid.create
            [ Grid.columnDefinitions "Auto, *, Auto"
              Grid.margin (12.0, 6.0)
              Grid.children
                  [ stepButton 0 (localized TranslationKey.ReplayPreviousMoment) previousStart
                    Grid.create [ Grid.column 1; Grid.margin (8.0, 0.0); Grid.children [ bar ] ]
                    stepButton 2 (localized TranslationKey.ReplayNextMoment) nextStart ] ]
        :> IView
