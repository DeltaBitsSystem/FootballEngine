namespace FootballEngine

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
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
open FootballEngine.Pages.Transfers

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

        DockPanel.create
            [ DockPanel.background Theme.BgMain
              DockPanel.children
                  [ sidebar state dispatch
                    DockPanel.create
                        [ DockPanel.lastChildFill true
                          DockPanel.children
                              [ header state dispatch
                                |> fun h -> Border.create [ Border.dock Dock.Top; Border.child h ]
                                match state.CurrentPage with
                                | Loading -> loadingView ()
                                | Setup -> setupView state dispatch
                                | HomePage ->
                                    homeView
                                        state
                                        state.SelectedLeagueId
                                        (fun leagueId -> dispatch (ChangeLeague leagueId))
                                        dispatch
                                | Inbox -> inboxView state dispatch
                                | Squad -> squadView state dispatch
                                | Tactics -> tacticView state dispatch
                                | Training -> trainingView state dispatch
                                | Scouting -> failwith "todo"
                                | Transfers -> transfersView state dispatch
                                | Finances -> failwith "todo"
                                | Settings -> failwith "todo"
                                | ModEditor -> ModEditor.view state dispatch
                                | Match -> MatchDayView.view state dispatch ] ] ] ]

type MainWindow() as this =
    inherit HostWindow()

    do
        base.Title <- "Football Engine 2026"
        base.WindowState <- WindowState.Maximized

        Elmish.Program.mkProgram AppState.init AppState.update Views.mainView
        |> Elmish.Program.withHost this
        |> Elmish.Program.runWithAvaloniaSyncDispatch ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.Styles.Add(MaterialIconStyles(null))

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop -> desktop.MainWindow <- MainWindow()
        | _ -> ()

module Program =
    [<EntryPoint>]
    let main args =
        // Load game data from JSON builtins + mods
        let builtinsDir = Data.ModPaths.builtinsDir
        let modsDir = Data.ModPaths.modsDir
        printfn $"[ModLoader] builtinsDir: %s{builtinsDir}"
        printfn $"[ModLoader] modsDir: %s{modsDir}"
        printfn $"[ModLoader] builtins exists: %b{System.IO.Directory.Exists builtinsDir}"

        match Data.ModLoader.loadAll builtinsDir modsDir with
        | Ok data ->
            Data.DataRegistry.setLoadedData data
            printfn $"[ModLoader] OK: %d{data.Countries.Count} countries, %d{data.InternationalComps.Length} intl comps"
        | Error errors ->
            for e in errors do
                printfn $"[ModLoader] Error: %A{e}"

        Db.initTables () |> Async.AwaitTask |> Async.RunSynchronously
        AppBuilder.Configure<App>().UsePlatformDetect().UseSkia().StartWithClassicDesktopLifetime(args)
