namespace FootballEngine

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Media.Imaging
open Avalonia.Threading
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Data
open FootballEngine.Generation.WorldGen
open FootballEngine.Core.Match
open FootballEngine.World
open FootballEngine.World.Phases
open FootballEngine.Settings
open FootballEngine.Icons
open FootballEngine.Routing

// Spectacle + manual clicking can't drive a fast autonomous UI loop on KDE/Wayland.
// `--shots` renders every page in-process with a fixed demo career and fixed window
// size, so `dotnet run -- --shots <dir>` + reading the PNGs is the whole review cycle.
// No new packages, no Xvfb required (works on XWayland :0, and under xvfb-run
// unchanged for background runs).
module Shots =

    // Navigation fires Db saves when leaving Tactics/Training; threading a flag through
    // every Msg would touch the whole update pipeline for a dev-only tool.
    let mutable Enabled = false

    type Options =
        { Dir: string
          Width: float
          Height: float
          Pages: Route list
          SettleMs: int }

    type Context = { Opts: Options; Demo: State }

    // Training/Scouting/Settings collide with PhaseTag and SettingsCategory cases opened
    // for the demo builder; qualification keeps it unambiguous.
    let allPages =
        [ Route.Loading
          Route.Setup
          Route.Home
          Route.Calendar
          Route.Inbox(InboxRoute None)
          Route.Squad SquadRoute.All
          Route.Tactics TacticsRoute.Overview
          Route.Training TrainingRoute.Overview
          Route.Scouting ScoutingRoute.Overview
          Route.Transfers TransferRoute.Market
          Route.Finances FinanceRoute.Overview
          Route.Settings SettingsRoute.Overview
          Route.Match(1, MatchPhase.Replay)
          Route.ModEditor ]

    let private parsePage (s: string) : Route option =
        match s.Trim().ToLowerInvariant() with
        | "loading" -> Some Route.Loading
        | "setup" -> Some Route.Setup
        | "home"
        | "homepage" -> Some Route.Home
        | "calendar" -> Some Route.Calendar
        | "inbox" -> Some(Route.Inbox(InboxRoute None))
        | "squad" -> Some(Route.Squad SquadRoute.All)
        | "tactics" -> Some(Route.Tactics TacticsRoute.Overview)
        | "training" -> Some(Route.Training TrainingRoute.Overview)
        | "scouting" -> Some(Route.Scouting ScoutingRoute.Overview)
        | "transfers" -> Some(Route.Transfers TransferRoute.Market)
        | "finances" -> Some(Route.Finances FinanceRoute.Overview)
        | "settings" -> Some(Route.Settings SettingsRoute.Overview)
        | "match" -> Some(Route.Match(1, MatchPhase.Replay))
        | "modeditor" -> Some Route.ModEditor
        | _ -> None

    let tryParse (args: string[]) : Options option =
        let getValue (flag: string) =
            args
            |> Array.tryFindIndex (fun a -> a = flag)
            |> Option.bind (fun i -> if i + 1 < args.Length then Some args.[i + 1] else None)

        match getValue "--shots" with
        | None -> None
        | Some dir ->
            let width, height =
                match getValue "--shots-size" with
                | Some s ->
                    match s.Split('x') with
                    | [| w; h |] ->
                        match Double.TryParse w, Double.TryParse h with
                        | (true, wv), (true, hv) -> wv, hv
                        | _ -> 1600.0, 900.0
                    | _ -> 1600.0, 900.0
                | None -> 1600.0, 900.0

            let settle =
                match getValue "--shots-settle" with
                | Some s ->
                    match Int32.TryParse s with
                    | true, v -> v
                    | _ -> 700
                | None -> 700

            let pages =
                match getValue "--shots-pages" with
                | Some s ->
                    let ps = s.Split(',') |> Array.choose parsePage |> List.ofArray
                    if ps.IsEmpty then allPages else ps
                | None -> allPages

            Some
                { Dir = dir
                  Width = width
                  Height = height
                  Pages = pages
                  SettleMs = settle }

    // Sync replica of UpdateTransfer.buildCacheAsync: the async Load races the screenshot
    // timer; pre-populating keeps Transfers deterministic and spinner-free.
    let private transferCacheOf (gs: GameState) =
        let clubNames =
            gs.Players
            |> Map.toSeq
            |> Seq.choose (fun (_, p) ->
                Player.clubOf p
                |> Option.bind (fun clubId -> gs.Clubs |> Map.tryFind clubId |> Option.map (fun c -> p.Id, c.Name)))
            |> Map.ofSeq

        let players =
            gs.Players
            |> Map.toList
            |> List.map snd
            |> List.filter (fun p -> Player.clubOf p |> Option.forall (fun clubId -> clubId <> gs.UserClubId))
            |> List.sortByDescending _.CurrentSkill

        players, clubNames

    // Mirrors WorldRunner.simulateUserMatchWithBalance: the Match screenshot must show the
    // exact replay pipeline a real user match produces (same systems, same snapshot cadence).
    let private demoReplayOf (gs: GameState) =
        let fixturePick =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
            |> Seq.filter (fun (_, f) -> MatchFixture.isPending f && MatchFixture.involves gs.UserClubId f)
            |> Seq.sortBy (fun (_, f) -> f.ScheduledDate)
            |> Seq.tryHead

        match fixturePick with
        | None -> None
        | Some(fixtureId, fixture) ->
            let gsReady =
                gs
                |> LineupOps.ensureForClub fixture.HomeClubId
                |> LineupOps.ensureForClub fixture.AwayClubId

            match MatchContextBuilder.build gsReady fixture BalanceConfig.defaultConfig with
            | None -> None
            | Some ctx ->
                let outcome =
                    Simulator.runMatch
                        Scheduler.systems
                        ctx
                        (MatchContextBuilder.seedOf fixtureId)
                        (Simulator.RunMode.Replay 20<tickDelta>)
                        None

                match outcome.Replay with
                | None -> None
                | Some replay ->
                    let total = max 0 (replay.Count - 1)
                    Some(gsReady, fixture, replay, total / 2)

    let buildDemoState (globalSettings: GlobalSettings) (resolved: ResolvedDataSet) : State =
        // generateNewGame needs a primary country with a level-0 league; ARG is the
        // flagship builtin dataset.
        let gs0 =
            match generateNewGame resolved 0x53484F5453UL (DateTime(2026, 7, 1)) "ARG" "Demo Manager" [] with
            | Ok gameState -> gameState
            | Error error ->
                let details =
                    error.Diagnostics
                    |> List.map (fun d -> d.Code + ": " + d.Message)
                    |> String.concat " | "

                failwith ("Cannot build screenshot demo career: " + details)

        // Two inbox messages so the Inbox list/detail split renders with real content;
        // the empty state is covered by simply clearing them in a scratch run.
        let gs =
            gs0
            |> GameState.addInboxMessage (
                Inbox.create
                    gs0.CurrentDate
                    "Board"
                    "Season expectations"
                    "The board expects a top-4 finish. Attacking football is preferred, but results come first."
                    InboxMessageCategory.BoardUpdate
                    false
            )
            |> GameState.addInboxMessage (
                Inbox.create
                    gs0.CurrentDate
                    "Chief Scout"
                    "Transfer target: left back"
                    "We flagged a promising left back. Open Transfers to make an offer before rivals move."
                    InboxMessageCategory.Transfer
                    true
            )

        let cachedPlayers, clubNames = transferCacheOf gs

        // Showcase the training rewrite: pick a youngster with headroom, put him on a
        // Develop plan and select him so the detail panel renders with real content.
        let demoPupil =
            GameState.getSquad gs.UserClubId gs
            |> List.filter (fun p -> Player.age gs.CurrentDate p <= 21 && p.PotentialSkill > p.CurrentSkill)
            |> List.sortBy (fun p -> Player.age gs.CurrentDate p)
            |> List.tryHead

        let gs =
            match demoPupil with
            | None -> gs
            | Some pupil ->
                { gs with
                    Players =
                        gs.Players
                        |> Map.add
                            pupil.Id
                            { pupil with
                                TrainingPlan = Develop Physical } }

        let transfer =
            { initTransferState with
                CachedPlayers = cachedPlayers
                FilteredPlayers = cachedPlayers
                ClubNameCache = clubNames }

        let baseState = AppTypes.initialState ()

        let inbox =
            { AppTypes.initInboxState with
                SelectedMessageId = Some(gs.NextInboxId - 1) }

        let inGame =
            { baseState with
                Mode = InGame(gs, AppTypes.managerEmployment gs)
                Navigation =
                    { Current = Route.Loading
                      Back = []
                      Forward = [] }
                IsProcessing = false
                SelectedLeagueId = SimHelpers.primaryLeagueId gs
                WorldClock = WorldClockOps.init gs.Season
                Transfer = transfer
                Inbox = inbox
                Tactics = { DraggedPlayer = None; History = [] }
                Training = { SelectedPlayer = demoPupil |> Option.map _.Id }
                Settings = AppTypes.initSettingsState globalSettings SaveSettings.defaults
                SaveSettings = SaveSettings.defaults
                ResolvedData = Some resolved }

        let withNotes =
            inGame
            |> AppTypes.pushNotification
                NotificationIcons.win
                "River Plate 2-0 Boca Juniors"
                "Demo Manager · Liga Profesional"
            |> AppTypes.pushNotification
                NotificationIcons.swap
                "Transfer window opens soon"
                "Scouts flagged 3 targets in your inbox"
            |> AppTypes.pushNotification
                NotificationIcons.seasonComplete
                "Season preview"
                "Board expects a top-4 finish"

        match demoReplayOf gs with
        | None -> withNotes
        | Some(gsReady, fixture, replay, mid) ->
            { withNotes with
                Mode = InGame(gsReady, AppTypes.managerEmployment gsReady)
                Match =
                    { ActiveReplay = Some replay
                      HomeClubId = Some fixture.HomeClubId
                      AwayClubId = Some fixture.AwayClubId
                      Snapshot = mid
                      IsPlaying = false
                      PlaybackSpeed = 20
                      Accumulator = 0.0
                      InterpolationT = 0.0 } }

    let private captureTo (window: Window) (path: string) : unit =
        // Window chrome differs between X11/Wayland; reading back the real size keeps
        // the PNG exactly what was on screen.
        let size = window.ClientSize
        let w = max 1 (int size.Width)
        let h = max 1 (int size.Height)
        use bmp = new RenderTargetBitmap(PixelSize(w, h), Vector(96.0, 96.0))
        bmp.Render(window)
        bmp.Save(path)

    let runLoop (window: Window) (getDispatch: unit -> (Msg -> unit) option) (ctx: Context) : Async<unit> =
        async {
            Directory.CreateDirectory(ctx.Opts.Dir) |> ignore
            // Cold start still compiles shaders and lays out the first view; later pages
            // only need a FuncUI diff + one render.
            do! Async.Sleep 1500

            let mutable idx = 0

            for page in ctx.Opts.Pages do
                match getDispatch () with
                | None -> eprintfn $"[shots] no dispatch yet, skipping %A{page}"
                | Some dispatch ->
                    // Sync Invoke blocks the pool thread until the UI thread ran the
                    // dispatch; no DispatcherOperation/Task interop needed.
                    Dispatcher.UIThread.Invoke(fun () -> dispatch (Navigate page))

                    do! Async.Sleep ctx.Opts.SettleMs
                    let file = Path.Combine(ctx.Opts.Dir, $"%02d{idx}_{page}.png")

                    Dispatcher.UIThread.Invoke(fun () -> captureTo window file)

                    printfn $"[shots] %A{page} -> {file}"
                    idx <- idx + 1

            Dispatcher.UIThread.Invoke(fun () -> window.Close())
        }
