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
    let private playbackTimer: Avalonia.Threading.DispatcherTimer option ref = ref None

    let mainView (state: State) dispatch =
        match playbackTimer.Value, state.Match.IsPlaying with
        | Some timer, true when timer.IsEnabled -> ()
        | Some timer, false ->
            timer.Stop()
            playbackTimer.Value <- None
        | None, true ->
            let timer =
                new Avalonia.Threading.DispatcherTimer(Interval = System.TimeSpan.FromMilliseconds 16.67)

            timer.Tick.Add(fun _ -> dispatch (MatchMsg MatchMsg.Tick))
            timer.Start()
            playbackTimer.Value <- Some timer
        | _ -> ()

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
            | Route.Match _ -> MatchDayView.view state dispatch
            | Route.ModEditor -> ModEditor.view state dispatch
            | route -> unavailableRoute route

        let shellVisible =
            match state.Mode, state.Navigation.Current with
            | InGame _, (Route.Loading | Route.Setup | Route.ModEditor) -> false
            | _, Route.ModEditor -> false
            | InGame _, _ -> true
            | _ -> false

        if not shellVisible then
            content
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

        let initFn () =
            match shots with
            | Some s ->
                // runWithAvaloniaSyncDispatch doesn't expose the dispatch; the init Cmd runs
                // before the first render, so the shots loop always sees a live dispatch.
                s.Demo, Elmish.Cmd.ofEffect (fun dispatch -> dispatchCell <- Some dispatch)
            | None -> AppState.initWithGlobal initialGlobal resolvedData ()

        Elmish.Program.mkProgram initFn AppState.update Views.mainView
        |> Elmish.Program.withHost this
        |> Elmish.Program.runWithAvaloniaSyncDispatch ()

        match shots with
        | Some s ->
            Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                Async.StartImmediate(Shots.runLoop this (fun () -> dispatchCell) s))
        | None -> ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.Styles.Add(MaterialIconStyles(null))

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
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
            Shots.Enabled <- true
            let demo = Shots.buildDemoState initialGlobal resolved
            AppState.shotsContext <- Some { Opts = opts; Demo = demo }
            printfn $"[shots] demo ready, capturing {opts.Pages.Length} pages into {opts.Dir}"
        | Some _, None -> eprintfn "[shots] cannot build demo career because the resolved dataset is unavailable"
        | None, _ -> ()

        // Screenshot runs are strictly read-only: never create or touch the user's
        // save database.
        if AppState.shotsContext.IsNone then
            Db.initTables () |> Async.AwaitTask |> Async.RunSynchronously

        AppBuilder.Configure<App>().UsePlatformDetect().UseSkia().StartWithClassicDesktopLifetime(args)
