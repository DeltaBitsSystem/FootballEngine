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
open FootballEngine.Icons
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

    let private livePitch (runtimeId: MatchRuntimeId) (live: LiveMatchState) camera : IView =
        Border.create
            [ Border.background Theme.BgMain
              Border.child (MatchPitch.view runtimeId live camera) ]

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
                               [ TextBlock.text ((if player.IsGoalkeeper then "GK · " else "") + player.Name)
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

    let private controlDock (live: LiveMatchState) camera (dispatch: Msg -> unit) : IView =
        let tokens = Theme.currentTokens.Value
        let colors = tokens.Colors
        let canControl = live.Source = LiveSession && live.Status <> Finished
        let localized key = Localization.resolve key []

        let cameraChoices =
            [| ViewerCamera.Broadcast, TranslationKey.MatchCameraBroadcast
               ViewerCamera.Tactical, TranslationKey.MatchCameraTactical
               ViewerCamera.BehindGoal, TranslationKey.MatchCameraBehindGoal
               ViewerCamera.Touchline, TranslationKey.MatchCameraTouchline
               ViewerCamera.CloseUp, TranslationKey.MatchCameraCloseUp |]
            |> Array.map (fun (mode, key) -> mode, localized key)

        let cameraSelector : IView =
            ComboBox.create
                [ ComboBox.dataItems (cameraChoices |> Array.map snd)
                  ComboBox.selectedItem (cameraChoices |> Array.find (fun (mode, _) -> mode = camera) |> snd)
                  ComboBox.minWidth 150.0
                  ComboBox.minHeight tokens.Controls.StandardHeight
                  ToolTip.tip (localized TranslationKey.MatchCamera)
                  ComboBox.onSelectedItemChanged (fun value ->
                      if not (isNull value) then
                          cameraChoices
                          |> Array.tryFind (fun (_, label) -> label = string value)
                          |> Option.iter (fun (mode, _) ->
                              if mode <> camera then
                                  dispatch (LiveMsg(LiveMsg.SelectViewerCamera mode)))) ]

        let dockButton (label: string) icon selected enabled onClick : IView =
            let foreground =
                if not enabled then colors.DisabledText
                elif selected then colors.Accent
                else colors.TextSecondary

            Button.create
                [ Button.background (if selected then colors.Surface3 else colors.Surface2)
                  Button.foreground foreground
                  Button.borderBrush (if selected then colors.Accent else colors.BorderSubtle)
                  Button.borderThickness 1.0
                  Button.cornerRadius tokens.Radii.Control
                  Button.padding (10.0, 6.0)
                  Button.minHeight tokens.Controls.StandardHeight
                  Button.isEnabled enabled
                  Button.onClick (fun _ -> onClick ())
                  ToolTip.tip label
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 5.0
                            StackPanel.children
                                [ match icon with
                                  | Some kind -> Icons.iconSm kind foreground
                                  | None -> ()
                                  TextBlock.create
                                      [ TextBlock.text label
                                        TextBlock.fontSize 12.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.foreground foreground
                                        TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                  ) ]
            :> IView

        let workspace label icon target =
            dockButton label (Some icon) (live.Ui.ManagerWorkspace = target) true (fun () ->
                dispatch (LiveMsg(LiveMsg.SelectManagerWorkspace target)))

        let submit payload =
            let matchCommand =
                { CommandId = MatchCommandId live.NextCommandId
                  SessionId = live.SessionId
                  ExpectedRevision = live.Revision
                  IssuedAt = live.Clock.Elapsed
                  Payload = payload }

            dispatch (LiveMsg(LiveMsg.CommandSubmitted matchCommand))

        let command label payload =
            dockButton label None false canControl (fun () -> submit payload)

        let isPaused =
            match live.Status with
            | Paused
            | HalfTime -> true
            | _ -> false

        let team =
            if live.Fixture.UserClubId = live.Fixture.Home.ClubId then
                live.Fixture.HomeLineup
            else
                live.Fixture.AwayLineup

        let activeTools =
            match live.Ui.ManagerWorkspace with
            | Overview ->
                [ TextBlock.create
                      [ TextBlock.text $"{team.InitialTactics.Formation}  ·  {live.Fixture.CompetitionName}"
                        TextBlock.fontSize 12.0
                        TextBlock.foreground colors.TextSecondary
                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                  :> IView ]
            | Tactics _ ->
                [ command (localized TranslationKey.MatchDefensive) (ChangeMentality Defensive)
                  command (localized TranslationKey.MatchBalanced) (ChangeMentality Balanced)
                  command (localized TranslationKey.MatchPositive) (ChangeMentality Positive)
                  command (localized TranslationKey.MatchAttacking) (ChangeMentality Attacking)
                  TextBlock.create
                      [ TextBlock.text (localized TranslationKey.MatchMentalityHint)
                        TextBlock.maxWidth 230.0
                        TextBlock.textWrapping TextWrapping.Wrap
                        TextBlock.fontSize 11.0
                        TextBlock.foreground colors.TextSecondary ] :> IView ]
            | Players ->
                [ TextBlock.create
                      [ TextBlock.text (
                            Localization.resolve
                                TranslationKey.MatchStartersAndSubstitutes
                                [ "starters", string team.StarterCount
                                  "substitutes", string (team.Players.Length - team.StarterCount) ]
                        )
                        TextBlock.fontSize 12.0
                        TextBlock.foreground colors.TextSecondary
                        TextBlock.verticalAlignment VerticalAlignment.Center ]
                  :> IView ]
            | Substitutions ->
                if team.Players.Length > team.StarterCount then
                    let change =
                        { Outgoing = team.Players[team.StarterCount - 1].Id
                          Incoming = team.Players[team.StarterCount].Id
                          Role = StrikerRole
                          Duty = SupportDuty }

                    [ command (localized TranslationKey.MatchConfirmSubstitution) (ConfirmSubstitutions [ change ]) ]
                else
                    [ TextBlock.create
                          [ TextBlock.text (localized TranslationKey.MatchNoEligibleSubstitute)
                            TextBlock.foreground colors.TextMuted
                            TextBlock.verticalAlignment VerticalAlignment.Center ]
                      :> IView ]
            | Touchline ->
                [ command (localized TranslationKey.MatchEncourage) (IssueTouchlineInstruction Encourage)
                  command (localized TranslationKey.MatchDemandMore) (IssueTouchlineInstruction DemandMore)
                  command (localized TranslationKey.MatchCalmDown) (IssueTouchlineInstruction CalmDown) ]

        Border.create
            [ Grid.row 1
              Border.background colors.Surface1
              Border.borderBrush colors.BorderSubtle
              Border.borderThickness (0.0, 1.0, 0.0, 0.0)
              Border.padding (12.0, 8.0)
              Border.child (
                  ScrollViewer.create
                      [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                        ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Disabled
                        ScrollViewer.content (
                            Grid.create
                                [ Grid.columnDefinitions "Auto, *, Auto"
                                  Grid.minWidth 980.0
                                  Grid.children
                                      [ StackPanel.create
                                            [ Grid.column 0
                                              StackPanel.orientation Orientation.Horizontal
                                              StackPanel.spacing 5.0
                                              StackPanel.children
                                                  [ workspace
                                                        (localized TranslationKey.MatchOverview)
                                                        IconName.home
                                                        Overview
                                                    workspace
                                                        (localized TranslationKey.Tactics)
                                                        IconName.tactics
                                                        (Tactics InPossession)
                                                    workspace
                                                        (localized TranslationKey.MatchPlayers)
                                                        IconName.squad
                                                        Players
                                                    workspace
                                                        (localized TranslationKey.MatchSubstitutions)
                                                        ClubIcon.transfer
                                                        Substitutions
                                                    workspace
                                                        (localized TranslationKey.MatchTouchline)
                                                        IconName.bullhorn
                                                        Touchline ] ]
                                        ScrollViewer.create
                                            [ Grid.column 1
                                              ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Auto
                                              ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Disabled
                                              ScrollViewer.margin (14.0, 0.0)
                                              ScrollViewer.content (
                                                  StackPanel.create
                                                      [ StackPanel.orientation Orientation.Horizontal
                                                        StackPanel.spacing 5.0
                                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                                        StackPanel.children (cameraSelector :: activeTools) ]
                                              ) ]
                                        StackPanel.create
                                            [ Grid.column 2
                                              StackPanel.orientation Orientation.Horizontal
                                              StackPanel.spacing 5.0
                                              StackPanel.children
                                                  [ TextBlock.create
                                                        [ TextBlock.text (statusText live.Status)
                                                          TextBlock.fontSize 11.0
                                                          TextBlock.fontWeight FontWeight.SemiBold
                                                          TextBlock.foreground (statusColor live.Status)
                                                          TextBlock.verticalAlignment VerticalAlignment.Center
                                                          TextBlock.margin (0.0, 0.0, 9.0, 0.0) ]
                                                    dockButton
                                                        (localized (
                                                            if isPaused then
                                                                TranslationKey.MatchResume
                                                            else
                                                                TranslationKey.MatchPause
                                                        ))
                                                        (Some(if isPaused then Nav.play else Nav.pause))
                                                        isPaused
                                                        canControl
                                                        (fun () -> dispatch (LiveMsg LiveMsg.TogglePause))
                                                    dockButton "1x" None (live.Speed = Normal) canControl (fun () ->
                                                        submit (SetLiveSpeed Normal))
                                                    dockButton "2x" None (live.Speed = Fast) canControl (fun () ->
                                                        submit (SetLiveSpeed Fast))
                                                    dockButton "4x" None (live.Speed = VeryFast) canControl (fun () ->
                                                        submit (SetLiveSpeed VeryFast))
                                                    dockButton
                                                        (localized TranslationKey.MatchEvidence)
                                                        None
                                                        (live.Ui.IntelligenceWorkspace = Events)
                                                        true
                                                        (fun () ->
                                                            dispatch (
                                                                LiveMsg(
                                                                    LiveMsg.SelectIntelligenceWorkspace(
                                                                        if live.Ui.IntelligenceWorkspace = Events then
                                                                            Commentary
                                                                        else
                                                                            Events
                                                                    )
                                                                )
                                                            ))
                                                    dockButton
                                                        (localized (
                                                            if live.Status = Finished then
                                                                TranslationKey.MatchPostMatch
                                                            else
                                                                TranslationKey.ReturnHome
                                                        ))
                                                        (Some IconName.next)
                                                        false
                                                        true
                                                        (fun () ->
                                                            if live.Status = Finished then
                                                                dispatch (LiveMsg LiveMsg.ConfirmFinish)
                                                            else
                                                                dispatch (Navigate FootballEngine.Routing.Route.Home)) ] ] ] ]
                        ) ]
              ) ]

    let private commandFeedback (live: LiveMatchState) : IView =
        let latest =
            live.PendingCommands
            |> Map.toList
            |> List.rev
            |> List.tryFind (fun (_, pending) ->
                match pending.Command.Payload with
                | SetLiveSpeed _ | SkipAhead _ | PauseMatch | ResumeMatch -> false
                | _ -> true)

        match latest with
        | None -> Border.create [] :> IView
        | Some(_, pending) ->
            let tr key = Localization.resolve key []
            let action =
                match pending.Command.Payload with
                | ChangeMentality Defensive -> tr TranslationKey.MatchDefensive
                | ChangeMentality Balanced -> tr TranslationKey.MatchBalanced
                | ChangeMentality Positive -> tr TranslationKey.MatchPositive
                | ChangeMentality Attacking -> tr TranslationKey.MatchAttacking
                | ConfirmSubstitutions _ | PrepareSubstitutions _ -> tr TranslationKey.MatchSubstitutions
                | IssueTouchlineInstruction _ -> tr TranslationKey.MatchTouchline
                | _ -> tr TranslationKey.Tactics
            let status =
                match pending.Lifecycle with
                | Queued -> tr TranslationKey.MatchChangeQueued
                | Accepted _ -> tr TranslationKey.MatchChangeAccepted
                | Applied(revision, _) -> Localization.resolve TranslationKey.MatchChangeApplied [ "revision", string revision ]
                | Rejected reason -> Localization.resolve TranslationKey.MatchChangeRejected [ "reason", sprintf "%A" reason ]

            Border.create
                [ Border.padding (12.0, 6.0)
                  Border.background Theme.BgSidebar
                  ToolTip.tip (tr TranslationKey.MatchChangeHint)
                  Border.child (
                      TextBlock.create
                          [ TextBlock.text (action + " · " + status)
                            TextBlock.fontSize 12.0
                            TextBlock.foreground Theme.TextMain
                            TextBlock.textWrapping TextWrapping.Wrap ]
                  ) ] :> IView

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
            let inspect = live.Ui.IntelligenceWorkspace = Events

            Grid.create
                [ Grid.background Theme.BgMain
                  Grid.rowDefinitions "*, Auto"
                  Grid.children
                      [ Grid.create
                            [ Grid.columnDefinitions (if inspect then "*, 380" else "*")
                              Grid.children
                                  [ livePitch runtimeId live state.Match.Camera
                                    if inspect then
                                        MatchEvidenceInspector.view runtimeId live dispatch ] ]
                        StackPanel.create
                            [ Grid.row 1
                              StackPanel.children [ commandFeedback live; controlDock live state.Match.Camera dispatch ] ] ] ]
            :> IView
        | _ -> noMatch state.Match.Error

module MatchDayView =
    let view (state: State) (dispatch: Msg -> unit) : IView = MatchViewer.view state dispatch
