namespace FootballEngine

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Components
open FootballEngine.Components.Header
open FootballEngine.Components.Sidebar
open FootballEngine.Pages
open FootballEngine.Pages.Home
open FootballEngine.Pages.Inbox
open FootballEngine.Pages.Loading.LoadingPage
open FootballEngine.Pages.Setup.Setup
open FootballEngine.Pages.Squad
open FootballEngine.Pages.Tactics
open FootballEngine.Pages.Training
open FootballEngine.Pages.Finances
open FootballEngine.Pages.Scouting
open FootballEngine.Pages.Transfers
open FootballEngine.Pages.Calendar
open FootballEngine.Routing

open FootballEngine.Settings

open Material.Icons.Avalonia

module Views =
    let private viewportWidth () =
        match Avalonia.Application.Current with
        | null -> 1600.0
        | app ->
            match app.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop when not (isNull desktop.MainWindow) ->
                desktop.MainWindow.ClientSize.Width
            | _ -> 1600.0

    let mainView (state: State) dispatch =
        let unavailableRoute route : IView =
            Border.create
                [ Border.padding 32.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 8.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text "This destination is not available in this build."
                                        TextBlock.fontSize 18.0
                                        TextBlock.foreground Theme.TextMain ]
                                  TextBlock.create
                                      [ TextBlock.text (Route.toPath route)
                                        TextBlock.fontSize 12.0
                                        TextBlock.foreground Theme.TextMuted ]
                                  Button.create
                                      [ Button.background Theme.Accent
                                        Button.borderThickness 0.0
                                        Button.padding (14.0, 8.0)
                                        Button.cornerRadius Theme.RadiusMd
                                        Button.onClick (fun _ -> dispatch GoBack)
                                        Button.content (
                                            TextBlock.create
                                                [ TextBlock.text "Back"; TextBlock.foreground Theme.BgSidebar ]
                                        ) ] ] ]
                  ) ]

        let content: IView =
            match state.Navigation.Current with
            | Route.Loading -> loadingView ()
            | Route.Setup -> setupView state dispatch
            | Route.Home ->
                homeView state state.SelectedLeagueId (fun leagueId -> dispatch (ChangeLeague leagueId)) dispatch
            | Route.Calendar -> Calendar.view state dispatch
            | Route.Inbox _ -> inboxView state dispatch
            | Route.Squad _ -> squadView state dispatch
            | Route.Tactics _ -> tacticView state dispatch
            | Route.Training _ -> trainingView state dispatch
            | Route.Scouting _ -> Scouting.scoutingView state dispatch
            | Route.Transfers _ -> transfersView state dispatch
            | Route.Finances _ -> Finances.view state dispatch
            | Route.Settings _ -> Settings.view state dispatch
            | Route.Match(id, _) when state.Match.PendingResult.IsSome -> CareerJourney.review state id dispatch
            | Route.Match(id, MatchPhase.Prematch) -> CareerJourney.preparation state id dispatch
            | Route.Match(id, MatchPhase.Postmatch) -> CareerJourney.review state id dispatch
            | Route.Match _ -> MatchDayView.view state dispatch
            | Route.ModEditor -> ModEditor.view state dispatch
            | route -> unavailableRoute route

        let shellVisible =
            match state.Mode, state.Navigation.Current with
            | InGame _, (Route.Loading | Route.Setup | Route.ModEditor) -> false
            | _, Route.ModEditor -> false
            | InGame _, _ -> true
            | _ -> false

        let compactHeader: IView =
            let clubName =
                match state.Mode with
                | InGame(gs, _) -> gs.Clubs |> Map.tryFind gs.UserClubId |> Option.map _.Name
                | _ -> None

            Border.create
                [ DockPanel.dock Dock.Top
                  Border.height 56.0
                  Border.background Theme.BgSidebar
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.padding (10.0, 0.0)
                  Border.child (
                      DockPanel.create
                          [ DockPanel.lastChildFill true
                            DockPanel.children
                                [ Button.create
                                      [ DockPanel.dock Dock.Left
                                        Button.background Theme.BgCard
                                        Button.borderBrush Theme.BorderLight
                                        Button.borderThickness 1.0
                                        Button.cornerRadius Theme.RadiusMd
                                        Button.padding (10.0, 6.0)
                                        Button.onClick (fun _ -> dispatch (Navigate Route.Home))
                                        Button.content (
                                            TextBlock.create
                                                [ TextBlock.text (Localization.resolve TranslationKey.Today []) ]
                                        ) ]
                                  Button.create
                                      [ DockPanel.dock Dock.Left
                                        Button.isEnabled (not state.Navigation.Back.IsEmpty)
                                        Button.margin (6.0, 0.0)
                                        Button.onClick (fun _ -> dispatch GoBack)
                                        Button.content (Localization.resolve TranslationKey.Back []) ]
                                  Button.create
                                      [ DockPanel.dock Dock.Right
                                        Button.isEnabled (
                                            not state.IsProcessing
                                            && state.Match.RuntimeId.IsNone
                                            && state.Match.PendingResult.IsNone
                                        )
                                        Button.margin (6.0, 0.0)
                                        Button.onClick (fun _ -> dispatch (PersistenceMsg SaveCurrent))
                                        Button.content (Localization.resolve TranslationKey.ManualSave []) ]
                                  Button.create
                                      [ DockPanel.dock Dock.Right
                                        Button.isEnabled (
                                            state.Mode
                                            |> function
                                                | InGame _ -> not state.IsProcessing
                                                | _ -> false
                                        )
                                        Button.background Theme.Accent
                                        Button.borderThickness 0.0
                                        Button.cornerRadius Theme.RadiusMd
                                        Button.padding (10.0, 6.0)
                                        Button.onClick (fun _ -> dispatch (SimMsg ContinueCareer))
                                        Button.content (
                                            TextBlock.create [ TextBlock.text (CareerJourney.continueLabel state) ]
                                        ) ]
                                  TextBlock.create
                                      [ TextBlock.text (clubName |> Option.defaultValue "Football Engine")
                                        TextBlock.fontSize 14.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextMain
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                  ) ]

        let compactThreshold =
            match state.Navigation.Current with
            | Route.Match _ -> 1100.0
            | _ -> 1280.0

        let isMatchRoute =
            match state.Navigation.Current with
            | Route.Match _ -> true
            | _ -> false

        if not shellVisible then
            content
        elif isMatchRoute then
            if viewportWidth () < compactThreshold then
                content
            else
                DockPanel.create
                    [ DockPanel.background Theme.BgMain
                      DockPanel.lastChildFill true
                      DockPanel.children [ sidebar state dispatch; content ] ]
        elif viewportWidth () < compactThreshold then
            DockPanel.create
                [ DockPanel.background Theme.BgMain
                  DockPanel.lastChildFill true
                  DockPanel.children [ compactHeader; content ] ]
        else
            DockPanel.create
                [ DockPanel.background Theme.BgMain
                  DockPanel.children
                      [ sidebar state dispatch
                        DockPanel.create
                            [ DockPanel.lastChildFill true
                              DockPanel.children
                                  [ header state dispatch
                                    |> fun h -> Border.create [ Border.dock Dock.Top; Border.child h ]
                                    content ] ] ] ]

type MainWindow(initialGlobal: GlobalSettings, resolvedData: Data.ResolvedDataSet option, ?shots: Shots.Context) as this
    =
    inherit HostWindow()

    do
        base.Title <- "Football Engine 2026"

        match shots with
        | Some s ->
            // Fixed Normal window in shots mode: maximized geometry depends on the host
            // screen; fixed size keeps screenshots comparable across machines.
            base.WindowState <- WindowState.Normal
            base.Width <- s.Opts.Width
            base.Height <- s.Opts.Height
        | None -> base.WindowState <- WindowState.Maximized

        Theme.applyToApp initialGlobal.Appearance this

        let mutable dispatchCell: (Msg -> unit) option = None
        let mutable stateCell: State option = None
        let mutable exitApproved = false
        let mutable exitPromptOpen = false

        this.Closing.Add(fun args ->
            if shots.IsNone then
                try
                    // Closing must wait for SQLite writes, not for their UI completion messages.
                    Db.waitForInFlightWrites().GetAwaiter().GetResult()
                with error ->
                    args.Cancel <- true

                    dispatchCell
                    |> Option.iter (fun dispatch ->
                        dispatch (
                            NotificationMsg(
                                PushNotification
                                    { Id = 0
                                      Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                                      Title = "Save failed"
                                      Body = "The window stayed open because a save failed: " + error.Message
                                      IsRead = false }
                            )
                        )))

        let initFn () =
            match shots with
            | Some s ->
                // runWithAvaloniaSyncDispatch doesn't expose the dispatch; the init Cmd runs
                // before the first render, so the shots loop always sees a live dispatch.
                s.Demo, Elmish.Cmd.ofEffect (fun dispatch -> dispatchCell <- Some dispatch)
            | None -> AppState.initWithGlobal initialGlobal resolvedData ()

        let viewFn state dispatch =
            dispatchCell <- Some dispatch
            stateCell <- Some state
            Views.mainView state dispatch

        Elmish.Program.mkProgram initFn AppState.update viewFn
        |> Elmish.Program.withHost this
        |> Elmish.Program.runWithAvaloniaSyncDispatch ()

        this.Closing.Add(fun args ->
            match shots, stateCell with
            | None, Some state when not exitApproved && (state.Match.RuntimeId.IsSome || state.Match.PendingResult.IsSome) ->
                args.Cancel <- true
                if not exitPromptOpen then
                    exitPromptOpen <- true
                    let dialog = Window(Title = Localization.resolve TranslationKey.MatchOverview [], Width = 520.0, Height = 230.0, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner)
                    let body = StackPanel(Margin = Thickness(24.0), Spacing = 16.0)
                    body.Children.Add(TextBlock(Text = Localization.resolve TranslationKey.LeaveMatchWarning [], TextWrapping = TextWrapping.Wrap))
                    let keep = Button(Content = Localization.resolve TranslationKey.KeepPlaying [], IsDefault = true, IsCancel = true)
                    let leave = Button(Content = Localization.resolve TranslationKey.LeaveMatch [])
                    keep.Click.Add(fun _ -> dialog.Close(false))
                    leave.Click.Add(fun _ -> dialog.Close(true))
                    body.Children.Add(keep)
                    body.Children.Add(leave)
                    dialog.Content <- body
                    async {
                        let! leave = dialog.ShowDialog<bool>(this) |> Async.AwaitTask
                        exitPromptOpen <- false
                        if leave then
                            exitApproved <- true
                            this.Close()
                    } |> Async.StartImmediate
            | _ -> ())

        this.KeyDown.Add(fun args ->
            if not args.Handled then
                let command =
                    match args.Key, args.KeyModifiers with
                    | Avalonia.Input.Key.S, Avalonia.Input.KeyModifiers.Control -> Some(PersistenceMsg SaveCurrent)
                    | Avalonia.Input.Key.Left, Avalonia.Input.KeyModifiers.Alt -> Some GoBack
                    | Avalonia.Input.Key.Right, Avalonia.Input.KeyModifiers.Alt -> Some GoForward
                    | _ -> None

                match command, dispatchCell with
                | Some command, Some dispatch ->
                    args.Handled <- true
                    dispatch command
                | _ -> ())

        match shots with
        | Some s ->
            Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                Async.StartImmediate(Shots.runLoop this (fun () -> dispatchCell) (fun () -> stateCell) s))
        | None -> ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.Styles.Add(MaterialIconStyles(null))

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.Exit.Add(fun _ -> MatchRuntime.disposeAll ())

            let startupGlobal =
                match AppState.initialGlobalOverride with
                | Some g -> g
                | None -> GlobalSettings.defaults

            match AppState.shotsContext with
            | Some s -> desktop.MainWindow <- MainWindow(startupGlobal, AppState.initialResolvedDataOverride, s)
            | None -> desktop.MainWindow <- MainWindow(startupGlobal, AppState.initialResolvedDataOverride)
        | _ -> ()

module Program =
    [<EntryPoint>]
    let main args =
        let initialGlobal: GlobalSettings =
            try
                match SettingsStorage.loadGlobalAsync () |> Async.RunSynchronously with
                | Ok g -> g
                | Error _ -> GlobalSettings.defaults
            with _ ->
                GlobalSettings.defaults

        let builtinsDir = Data.ModPaths.builtinsDir
        let modsDir = Data.ModPaths.modsDir

        let resolvedData =
            match
                Data.ModSystem.loadInstalledPackages
                    builtinsDir
                    modsDir
                    initialGlobal.Data.ActiveMods
                    initialGlobal.Data.ModOrder
            with
            | Error error ->
                for diagnostic in error.Diagnostics do
                    eprintfn "[ModSystem] %s %s" diagnostic.Code diagnostic.Message

                None
            | Ok resolved -> Some resolved

        AppState.initialGlobalOverride <- Some initialGlobal
        AppState.initialResolvedDataOverride <- resolvedData

        match Shots.tryParse args, resolvedData with
        | Some opts, Some resolved ->
            // World-gen + a full deterministic match sim take seconds; doing it upfront
            // keeps the window responsive once shown.
            printfn $"[shots] building demo career..."
            ExecutionRandomness.seedOverride <- opts.MatchSeed
            Shots.Enabled <- true

            let demo = Shots.buildDemoState opts initialGlobal resolved

            match args |> Array.tryFindIndex ((=) "--match-corpus") with
            | Some index ->
                let count =
                    args |> Array.tryItem (index + 1) |> Option.bind (fun v -> match System.Int32.TryParse v with | true, n -> Some n | _ -> None) |> Option.defaultValue 5

                Shots.runCorpus count demo
                exit 0
            | None -> ()

            AppState.shotsContext <- Some { Opts = opts; Demo = demo }
            printfn $"[shots] demo ready, capturing {opts.Pages.Length} pages into {opts.Dir}"
        | Some _, None -> eprintfn "[shots] cannot build demo career because the resolved dataset is unavailable"
        | None, _ -> ()

        // Screenshot runs are strictly read-only: never create or touch the user's
        // save database.
        if AppState.shotsContext.IsNone then
            Db.initTables () |> Async.AwaitTask |> Async.RunSynchronously

        AppBuilder.Configure<App>().UsePlatformDetect().UseSkia().StartWithClassicDesktopLifetime(args)
