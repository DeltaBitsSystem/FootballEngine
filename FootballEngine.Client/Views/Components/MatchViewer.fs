namespace FootballEngine.Components

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Client.Match3D
open FootballEngine.Client.Views.Components
open FootballEngine.Client.Views.MatchPitch
open FootballEngine.LiveMatch

module MatchViewer =
    let private fieldWidth = 1000.0
    let private fieldHeight = 560.0

    let private statusText status =
        match status with
        | Connecting -> "CONNECTING"
        | Preparing -> "PREPARING"
        | Live _ -> "LIVE"
        | Paused -> "PAUSED"
        | AwaitingDecision _ -> "DECISION REQUIRED"
        | HalfTime -> "HALF-TIME"
        | Finished -> "FULL-TIME"
        | Failed message -> "ERROR: " + message

    let private statusColor status =
        match status with
        | Failed _ -> Theme.Negative
        | Preparing
        | Connecting
        | AwaitingDecision _ -> Theme.Warning
        | _ -> Theme.Accent

    let private label text : IView =
        TextBlock.create
            [ TextBlock.text text
              TextBlock.fontSize 10.0
              TextBlock.fontWeight FontWeight.SemiBold
              TextBlock.foreground Theme.TextMuted ]

    let private value text size : IView =
        TextBlock.create
            [ TextBlock.text text
              TextBlock.fontSize size
              TextBlock.fontWeight FontWeight.Bold
              TextBlock.foreground Theme.TextMain ]

    let private panel title children : IView =
        Border.create
            [ Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusMd
              Border.padding 14.0
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 10.0
                        StackPanel.children
                            [ TextBlock.create
                                  [ TextBlock.text title
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.foreground Theme.TextSub ]
                              yield! children ] ]
              ) ]

    let private workspaceButton
        (labelText: string)
        (workspace: MatchDayWorkspace)
        (selected: bool)
        (dispatch: Msg -> unit)
        : IView =
        Button.create
            [ Button.content labelText
              Button.horizontalContentAlignment HorizontalAlignment.Left
              Button.background (if selected then Theme.Surface3 else Theme.BgSidebar)
              Button.foreground (if selected then Theme.Accent else Theme.TextSub)
              Button.borderBrush (if selected then Theme.Accent else Theme.Border)
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusSm
              Button.padding (10.0, 8.0)
              Button.onClick (fun _ -> dispatch (LiveMsg(LiveMsg.SelectManagerWorkspace workspace))) ]
        :> IView

    let private commandButton
        (labelText: string)
        (live: LiveMatchState)
        (payload: LiveMatchCommandPayload)
        (dispatch: Msg -> unit)
        : IView =
        let command =
            { CommandId = MatchCommandId live.NextCommandId
              SessionId = live.SessionId
              ExpectedRevision = live.Revision
              IssuedAt = live.Clock.Elapsed
              Payload = payload }

        Button.create
            [ Button.content labelText
              Button.isEnabled (live.Source = LiveSession && live.Status <> Finished)
              Button.background Theme.Surface3
              Button.foreground Theme.TextMain
              Button.borderBrush Theme.BorderLight
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusSm
              Button.padding (10.0, 7.0)
              Button.onClick (fun _ -> dispatch (LiveMsg(LiveMsg.CommandSubmitted command))) ]
        :> IView

    let private livePitch (runtimeId: MatchRuntimeId) (live: LiveMatchState) : IView =
        Border.create
            [ Border.background Theme.BgMain
              Border.child (MatchPitch.view runtimeId live.Fixture) ]

    let private scoreHeader (live: LiveMatchState) : IView =
        let score = live.Score

        Grid.create
            [ Grid.columnDefinitions "*, 120, *"
              Grid.children
                  [ StackPanel.create
                        [ Grid.column 0
                          StackPanel.verticalAlignment VerticalAlignment.Center
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text live.Fixture.Home.Name
                                      TextBlock.fontSize 18.0
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.foreground Theme.TextMain
                                      TextBlock.margin (0.0, 0.0, 14.0, 0.0)
                                      TextBlock.horizontalAlignment HorizontalAlignment.Right ]
                                TextBlock.create
                                    [ TextBlock.text "HOME"
                                      TextBlock.fontSize 10.0
                                      TextBlock.foreground Theme.Accent
                                      TextBlock.margin (0.0, 0.0, 14.0, 0.0)
                                      TextBlock.horizontalAlignment HorizontalAlignment.Right ] ] ]
                    StackPanel.create
                        [ Grid.column 1
                          StackPanel.spacing 2.0
                          StackPanel.horizontalAlignment HorizontalAlignment.Center
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text $"{fst score}   -   {snd score}"
                                      TextBlock.fontSize 30.0
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.foreground Theme.TextMain
                                      TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                TextBlock.create
                                    [ TextBlock.text $"{live.Clock.DisplayMinute}'  {statusText live.Status}"
                                      TextBlock.fontSize 11.0
                                      TextBlock.fontWeight FontWeight.SemiBold
                                      TextBlock.foreground (statusColor live.Status)
                                      TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                    StackPanel.create
                        [ Grid.column 2
                          StackPanel.verticalAlignment VerticalAlignment.Center
                          StackPanel.children
                              [ TextBlock.create
                                    [ TextBlock.text live.Fixture.Away.Name
                                      TextBlock.fontSize 18.0
                                      TextBlock.fontWeight FontWeight.Bold
                                      TextBlock.foreground Theme.TextMain
                                      TextBlock.margin (14.0, 0.0, 0.0, 0.0) ]
                                TextBlock.create
                                    [ TextBlock.text "AWAY"
                                      TextBlock.fontSize 10.0
                                      TextBlock.foreground Theme.Info ] ] ] ] ]

    let private leftRail (live: LiveMatchState) (dispatch: Msg -> unit) : IView =
        let pending =
            live.PendingCommands
            |> Map.toList
            |> List.map (fun (_, pendingCommand) ->
                match pendingCommand.Lifecycle with
                | Queued -> "Queued"
                | Accepted point -> $"Accepted: %A{point}"
                | Applied(revision, _) -> $"Applied at #{revision}"
                | Rejected _ -> "Rejected")

        let plan =
            if live.Fixture.UserClubId = live.Fixture.Home.ClubId then
                live.Fixture.HomeLineup.InitialTactics
            else
                live.Fixture.AwayLineup.InitialTactics

        let workspace =
            match live.Ui.ManagerWorkspace with
            | Overview ->
                [ panel
                      "CURRENT PLAN"
                      [ label "MENTALITY"
                        value (sprintf "%.0f%%" (plan.Mentality * 100.0f)) 16.0
                        label "FORMATION"
                        value plan.Formation 16.0 ] ]
            | Tactics _ ->
                [ panel
                      "MATCH MENTALITY"
                      [ commandButton "Defensive" live (ChangeMentality Defensive) dispatch
                        commandButton "Balanced" live (ChangeMentality Balanced) dispatch
                        commandButton "Positive" live (ChangeMentality Positive) dispatch
                        commandButton "Attacking" live (ChangeMentality Attacking) dispatch ] ]
            | Players ->
                let team =
                    if live.Fixture.UserClubId = live.Fixture.Home.ClubId then
                        live.Fixture.HomeLineup
                    else
                        live.Fixture.AwayLineup

                [ panel
                      "STARTING XI"
                      (team.Players
                       |> Array.take team.StarterCount
                       |> Array.map (fun player ->
                           TextBlock.create
                               [ TextBlock.text (
                                     (if player.IsGoalkeeper then "GK · " else "") + player.Name
                                 )
                                 TextBlock.foreground Theme.TextSub ]
                           :> IView)
                       |> Array.toList) ]
            | Substitutions ->
                let team =
                    if live.Fixture.UserClubId = live.Fixture.Home.ClubId then
                        live.Fixture.HomeLineup
                    else
                        live.Fixture.AwayLineup

                let change =
                    if team.Players.Length > team.StarterCount then
                        Some
                            { Outgoing = team.Players[team.StarterCount - 1].Id
                              Incoming = team.Players[team.StarterCount].Id
                              Role = StrikerRole
                              Duty = SupportDuty }
                    else
                        None

                [ panel
                      "SUBSTITUTION"
                      [ match change with
                        | Some change ->
                            yield
                                label
                                    $"{live.Fixture.PlayerNames[FootballEngine.Core.Match.PlayerId change.Outgoing]} OUT"

                            yield
                                label
                                    $"{live.Fixture.PlayerNames[FootballEngine.Core.Match.PlayerId change.Incoming]} IN"

                            yield commandButton "Confirm change" live (ConfirmSubstitutions [ change ]) dispatch
                        | None ->
                            yield
                                TextBlock.create
                                    [ TextBlock.text "No eligible substitute"
                                      TextBlock.foreground Theme.TextMuted ] ] ]
            | Touchline ->
                [ panel
                      "TOUCHLINE"
                      [ commandButton "Encourage" live (IssueTouchlineInstruction Encourage) dispatch
                        commandButton "Demand more" live (IssueTouchlineInstruction DemandMore) dispatch
                        commandButton "Calm down" live (IssueTouchlineInstruction CalmDown) dispatch ] ]

        StackPanel.create
            [ StackPanel.spacing 10.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text "MANAGER DESK"
                          TextBlock.fontSize 11.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.Accent ]
                    Grid.create
                        [ Grid.columnDefinitions "*, *"
                          Grid.rowDefinitions "Auto, Auto, Auto"
                          Grid.children
                              [ ContentControl.create
                                    [ Grid.row 0
                                      Grid.column 0
                                      ContentControl.content (
                                          workspaceButton
                                              "Overview"
                                              Overview
                                              (live.Ui.ManagerWorkspace = Overview)
                                              dispatch
                                      ) ]
                                ContentControl.create
                                    [ Grid.row 0
                                      Grid.column 1
                                      ContentControl.content (
                                          workspaceButton
                                              "Tactics"
                                              (Tactics InPossession)
                                              (live.Ui.ManagerWorkspace = Tactics InPossession)
                                              dispatch
                                      ) ]
                                ContentControl.create
                                    [ Grid.row 1
                                      Grid.column 0
                                      ContentControl.content (
                                          workspaceButton
                                              "Players"
                                              Players
                                              (live.Ui.ManagerWorkspace = Players)
                                              dispatch
                                      ) ]
                                ContentControl.create
                                    [ Grid.row 1
                                      Grid.column 1
                                      ContentControl.content (
                                          workspaceButton
                                              "Substitutions"
                                              Substitutions
                                              (live.Ui.ManagerWorkspace = Substitutions)
                                              dispatch
                                      ) ]
                                ContentControl.create
                                    [ Grid.row 2
                                      Grid.column 0
                                      ContentControl.content (
                                          workspaceButton
                                              "Touchline"
                                              Touchline
                                              (live.Ui.ManagerWorkspace = Touchline)
                                              dispatch
                                      ) ] ] ]
                    yield! workspace
                    if pending.IsEmpty then
                        panel
                            "PENDING ACTIONS"
                            [ TextBlock.create
                                  [ TextBlock.text "No queued decisions"; TextBlock.foreground Theme.TextMuted ] ]
                    else
                        panel
                            "PENDING ACTIONS"
                            (pending
                             |> List.map (fun text ->
                                 TextBlock.create [ TextBlock.text text; TextBlock.foreground Theme.Warning ] :> IView)) ] ]

    let private rightRail (live: LiveMatchState) : IView =
        let commentary =
            live.Commentary
            |> List.rev
            |> List.truncate 2
            |> List.map (fun entry ->
                StackPanel.create
                    [ StackPanel.spacing 3.0
                      StackPanel.children
                          [ TextBlock.create
                                [ TextBlock.text entry.Text
                                  TextBlock.fontSize 12.0
                                  TextBlock.textWrapping TextWrapping.Wrap
                                  TextBlock.foreground Theme.TextMain ]
                            TextBlock.create
                                [ TextBlock.text $"{entry.Tick} · %A{entry.Category}"
                                  TextBlock.fontSize 10.0
                                  TextBlock.foreground Theme.TextMuted ] ] ]
                :> IView)

        let events =
            live.Events
            |> List.rev
            |> List.truncate 2
            |> List.map (fun event ->
                TextBlock.create
                    [ TextBlock.text $"{event.Tick}  %A{event.Data}"
                      TextBlock.fontSize 11.0
                      TextBlock.textWrapping TextWrapping.Wrap
                      TextBlock.foreground Theme.TextSub ]
                :> IView)

        StackPanel.create
            [ StackPanel.spacing 12.0
              StackPanel.children
                  [ TextBlock.create
                        [ TextBlock.text "MATCH INTELLIGENCE"
                          TextBlock.fontSize 11.0
                          TextBlock.fontWeight FontWeight.Bold
                          TextBlock.foreground Theme.Info ]
                    panel
                        "COMMENTARY"
                        (if commentary.IsEmpty then
                             [ TextBlock.create
                                   [ TextBlock.text "Waiting for the first meaningful action."
                                     TextBlock.foreground Theme.TextMuted ] ]
                         else
                             commentary)
                    panel
                        "LIVE STATS"
                        [ Grid.create
                              [ Grid.columnDefinitions "*, Auto, *"
                                Grid.children
                                    [ ContentControl.create
                                          [ Grid.column 0
                                            ContentControl.content (value (string live.Metrics.Home.Shots) 20.0) ]
                                      ContentControl.create
                                          [ Grid.column 1; ContentControl.content (value "SHOTS" 10.0) ]
                                      ContentControl.create
                                          [ Grid.column 2
                                            ContentControl.content (value (string live.Metrics.Away.Shots) 20.0) ] ] ]
                          Grid.create
                              [ Grid.columnDefinitions "*, Auto, *"
                                Grid.children
                                    [ ContentControl.create
                                          [ Grid.column 0
                                            ContentControl.content (
                                                value (string live.Metrics.Home.PassesCompleted) 18.0
                                            ) ]
                                      ContentControl.create
                                          [ Grid.column 1; ContentControl.content (value "PASSES" 10.0) ]
                                      ContentControl.create
                                          [ Grid.column 2
                                            ContentControl.content (
                                                value (string live.Metrics.Away.PassesCompleted) 18.0
                                            ) ] ] ] ]
                    panel
                        "EVENT STREAM"
                        (if events.IsEmpty then
                             [ TextBlock.create
                                   [ TextBlock.text "No major events yet."; TextBlock.foreground Theme.TextMuted ] ]
                         else
                             events) ] ]

    let private controlDock (live: LiveMatchState) (dispatch: Msg -> unit) : IView =
        let isPaused =
            match live.Status with
            | Paused
            | HalfTime -> true
            | _ -> false

        let pauseControl =
            Button.create
                [ Button.content (if isPaused then "Resume" else "Pause")
                  Button.isEnabled (live.Source = LiveSession && live.Status <> Finished)
                  Button.background Theme.Surface3
                  Button.foreground Theme.TextMain
                  Button.borderBrush Theme.BorderLight
                  Button.borderThickness 1.0
                  Button.cornerRadius Theme.RadiusSm
                  Button.padding (10.0, 7.0)
                  Button.onClick (fun _ -> dispatch (LiveMsg LiveMsg.TogglePause)) ]

        Border.create
            [ Grid.row 2
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 1.0, 0.0, 0.0)
              Border.padding (12.0, 10.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, Auto, Auto, Auto, Auto, *, Auto"
                        Grid.children
                            [ pauseControl
                              commandButton "1x" live (SetLiveSpeed Normal) dispatch
                              |> fun view -> ContentControl.create [ Grid.column 1; ContentControl.content view ]
                              commandButton "2x" live (SetLiveSpeed Fast) dispatch
                              |> fun view -> ContentControl.create [ Grid.column 2; ContentControl.content view ]
                              commandButton "4x" live (SetLiveSpeed VeryFast) dispatch
                              |> fun view -> ContentControl.create [ Grid.column 3; ContentControl.content view ]
                              commandButton "Positive" live (ChangeMentality Positive) dispatch
                              |> fun view -> ContentControl.create [ Grid.column 4; ContentControl.content view ]
                              TextBlock.create
                                  [ Grid.column 5
                                    TextBlock.text $"{statusText live.Status}  ·  revision {live.Revision}"
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground (statusColor live.Status)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                              Button.create
                                  [ Grid.column 6
                                    Button.content (
                                        if live.Status = Finished then
                                            "Continue to post-match"
                                        else
                                            "Leave Match Day"
                                    )
                                    Button.background Theme.BgCard
                                    Button.foreground Theme.TextSub
                                    Button.borderBrush Theme.Border
                                    Button.borderThickness 1.0
                                    Button.cornerRadius Theme.RadiusSm
                                    Button.padding (12.0, 7.0)
                                    Button.onClick (fun _ ->
                                        dispatch (
                                            LiveMsg(
                                                if live.Status = Finished then
                                                    LiveMsg.ConfirmFinish
                                                else
                                                    LiveMsg.Close
                                            )
                                        )) ] ] ]
              ) ]

    let private noMatch (error: string option) : IView =
        Grid.create
            [ Grid.background Theme.BgMain
              Grid.children
                  [ StackPanel.create
                        [ StackPanel.spacing 12.0
                          StackPanel.horizontalAlignment HorizontalAlignment.Center
                          StackPanel.verticalAlignment VerticalAlignment.Center
                          StackPanel.children
                              [ value
                                    (if error.IsSome then
                                         "Match Day could not start"
                                     else
                                         "Preparing Match Day")
                                    24.0
                                TextBlock.create
                                    [ TextBlock.text (
                                          error |> Option.defaultValue "Connecting to the scheduled fixture..."
                                      )
                                      TextBlock.foreground (if error.IsSome then Theme.Negative else Theme.TextMuted)
                                      TextBlock.textWrapping TextWrapping.Wrap
                                      TextBlock.maxWidth 520.0 ] ] ] ] ]

    let view (state: State) (dispatch: Msg -> unit) : IView =
        match state.Match.Live, state.Match.RuntimeId with
        | Some live, Some runtimeId ->
            let width =
                match Avalonia.Application.Current with
                | null -> 1600.0
                | application ->
                    match application.ApplicationLifetime with
                    | :? IClassicDesktopStyleApplicationLifetime as desktop when not (isNull desktop.MainWindow) ->
                        desktop.MainWindow.ClientSize.Width
                    | _ -> 1600.0

            let pitchAndHud =
                Grid.create
                    [ Grid.row 1
                      Grid.clipToBounds true
                      Grid.children
                          [ livePitch runtimeId live
                            if width >= 1300.0 then
                                Border.create
                                    [ Border.horizontalAlignment HorizontalAlignment.Left
                                      Border.verticalAlignment VerticalAlignment.Top
                                      Border.margin MatchPitchOverlay.margin
                                      Border.width MatchPitchOverlay.managerWidth
                                      Border.height MatchPitchOverlay.managerHeight
                                      Border.background Theme.BgSidebar
                                      Border.borderBrush Theme.Border
                                      Border.borderThickness 1.0
                                      Border.cornerRadius Theme.RadiusMd
                                      Border.clipToBounds true
                                      Border.child (
                                          ScrollViewer.create
                                              [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                                ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
                                                ScrollViewer.content (leftRail live dispatch) ]
                                      ) ]

                                Border.create
                                    [ Border.horizontalAlignment HorizontalAlignment.Right
                                      Border.verticalAlignment VerticalAlignment.Top
                                      Border.margin MatchPitchOverlay.margin
                                      Border.width MatchPitchOverlay.intelligenceWidth
                                      Border.height MatchPitchOverlay.intelligenceHeight
                                      Border.background Theme.BgSidebar
                                      Border.borderBrush Theme.Border
                                      Border.borderThickness 1.0
                                      Border.cornerRadius Theme.RadiusMd
                                      Border.clipToBounds true
                                      Border.padding 12.0
                                      Border.child (
                                          ScrollViewer.create
                                              [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                                ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
                                                ScrollViewer.content (rightRail live) ]
                                      ) ] ] ]

            Grid.create
                [ Grid.background Theme.BgMain
                  Grid.rowDefinitions "Auto, *, Auto"
                  Grid.children
                      [ Border.create
                            [ Grid.row 0
                              Border.background Theme.BgSidebar
                              Border.borderBrush Theme.Border
                              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                              Border.padding (18.0, 12.0)
                              Border.child (
                                  StackPanel.create
                                      [ StackPanel.spacing 4.0
                                        StackPanel.children
                                            [ TextBlock.create
                                                  [ TextBlock.text live.Fixture.CompetitionName
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                              scoreHeader live ] ]
                              ) ]
                        pitchAndHud
                        controlDock live dispatch ] ]
            :> IView
        | _ -> noMatch state.Match.Error

module MatchDayView =
    let view (state: State) (dispatch: Msg -> unit) : IView = MatchViewer.view state dispatch
