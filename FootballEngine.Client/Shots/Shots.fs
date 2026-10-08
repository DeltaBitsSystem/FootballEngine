namespace FootballEngine

open System
open System.IO
open System.Diagnostics
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
          SettleMs: int
          VideoPath: string option
          VideoFps: int
          VideoStep: int
          VideoFrames: int
          MatchIndex: int
          InspectMatch: bool
          MatchSeed: uint64 option }

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
          Route.Match(1, MatchPhase.Live)
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
        | "match" -> Some(Route.Match(1, MatchPhase.Live))
        | "modeditor" -> Some Route.ModEditor
        | _ -> None

    let tryParse (args: string[]) : Options option =
        let getValue (flag: string) =
            args
            |> Array.tryFindIndex (fun a -> a = flag)
            |> Option.bind (fun i -> if i + 1 < args.Length then Some args.[i + 1] else None)

        match getValue "--shots", getValue "--match-video" with
        | None, None -> None
        | shotsDir, videoPath ->
            let dir =
                shotsDir
                |> Option.defaultValue (Path.Combine(Path.GetTempPath(), "football-engine-video"))

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
                | None when videoPath.IsSome -> [ Route.Match(1, MatchPhase.Live) ]
                | None -> allPages

            let positiveValue flag fallback =
                match getValue flag with
                | Some value ->
                    match Int32.TryParse value with
                    | true, parsed when parsed > 0 -> parsed
                    | _ -> fallback
                | None -> fallback

            let nonNegativeValue flag fallback =
                match getValue flag with
                | Some value ->
                    match Int32.TryParse value with
                    | true, parsed when parsed >= 0 -> parsed
                    | _ -> fallback
                | None -> fallback

            let matchSeed =
                match getValue "--match-seed" with
                | Some value ->
                    match UInt64.TryParse value with
                    | true, parsed -> Some parsed
                    | _ -> None
                | None -> None

            Some
                { Dir = dir
                  Width = width
                  Height = height
                  Pages = pages
                  SettleMs = settle
                  VideoPath = videoPath |> Option.map Path.GetFullPath
                  VideoFps = positiveValue "--video-fps" 30
                  VideoStep = positiveValue "--video-step" 4
                  VideoFrames = positiveValue "--video-frames" 3600
                  MatchIndex = nonNegativeValue "--match-index" 0
                  InspectMatch = args |> Array.contains "--match-evidence"
                  MatchSeed = matchSeed }

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

    let buildDemoState (opts: Options) (globalSettings: GlobalSettings) (resolved: ResolvedDataSet) : State =
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

        let gs =
            let fixtures =
                gs.Competitions
                |> Map.toSeq
                |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                |> Seq.filter (fun (_, fixture) ->
                    MatchFixture.isPending fixture && MatchFixture.involves gs.UserClubId fixture)
                |> Seq.sortBy (fun (_, fixture) -> fixture.ScheduledDate)
                |> Seq.toArray

            fixtures
            |> Array.tryItem opts.MatchIndex
            |> Option.orElseWith (fun () -> fixtures |> Array.tryHead)
            |> Option.map (fun (_, fixture) ->
                { gs with
                    CurrentDate = fixture.ScheduledDate.Date })
            |> Option.defaultValue gs

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

        withNotes

    let private captureTo (window: Window) (path: string) : unit =
        // Window chrome differs between X11/Wayland; reading back the real size keeps
        // the PNG exactly what was on screen.
        let size = window.ClientSize
        let w = max 1 (int size.Width)
        let h = max 1 (int size.Height)
        use bmp = new RenderTargetBitmap(PixelSize(w, h), Vector(96.0, 96.0))
        bmp.Render(window)
        bmp.Save(path)

    let private encodeVideo (opts: Options) framesDir =
        match opts.VideoPath with
        | None -> ()
        | Some output ->
            let parent = Path.GetDirectoryName output

            if not (String.IsNullOrWhiteSpace parent) then
                Directory.CreateDirectory(parent) |> ignore

            let info = ProcessStartInfo("ffmpeg", UseShellExecute = false)

            for argument in
                [ "-y"
                  "-framerate"
                  string opts.VideoFps
                  "-i"
                  Path.Combine(framesDir, "%06d.png")
                  "-c:v"
                  "libx264"
                  "-crf"
                  "20"
                  "-pix_fmt"
                  "yuv420p"
                  output ] do
                info.ArgumentList.Add argument

            use encoder = Process.Start info
            encoder.WaitForExit()

            if encoder.ExitCode <> 0 then
                failwith $"ffmpeg failed with exit code {encoder.ExitCode}"

    let runLoop
        (window: Window)
        (getDispatch: unit -> (Msg -> unit) option)
        (getState: unit -> State option)
        (ctx: Context)
        : Async<unit> =
        async {
            let liveRoute fallback =
                getState ()
                |> Option.bind (fun state ->
                    match state.Mode with
                    | InGame(gs, _) ->
                        gs.Competitions
                        |> Map.toSeq
                        |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                        |> Seq.tryPick (fun (matchId, fixture) ->
                            if
                                fixture.ScheduledDate.Date = gs.CurrentDate.Date
                                && MatchFixture.isPending fixture
                                && MatchFixture.involves gs.UserClubId fixture
                            then
                                Some(Route.Match(matchId, MatchPhase.Live))
                            else
                                None)
                    | _ -> None)
                |> Option.defaultValue fallback

            Directory.CreateDirectory(ctx.Opts.Dir) |> ignore
            // Cold start still compiles shaders and lays out the first view; later pages
            // only need a FuncUI diff + one render.
            do! Async.Sleep 1500

            match ctx.Opts.VideoPath, getDispatch () with
            | Some _, Some dispatch ->
                let framesDir = Path.Combine(ctx.Opts.Dir, "frames")

                if Directory.Exists framesDir then
                    Directory.Delete(framesDir, true)

                Directory.CreateDirectory(framesDir) |> ignore
                Dispatcher.UIThread.Invoke(fun () -> dispatch (Navigate(liveRoute (Route.Match(1, MatchPhase.Live)))))
                do! Async.Sleep ctx.Opts.SettleMs

                for frame = 0 to ctx.Opts.VideoFrames - 1 do

                    do!
                        Dispatcher.UIThread
                            .InvokeAsync((fun () -> window.UpdateLayout()), DispatcherPriority.Render)
                            .GetTask()
                        |> Async.AwaitTask

                    Dispatcher.UIThread.Invoke(fun () -> captureTo window (Path.Combine(framesDir, $"%06d{frame}.png")))
                    do! Async.Sleep ctx.Opts.VideoStep

                encodeVideo ctx.Opts framesDir
                Directory.Delete(framesDir, true)
            | Some _, None -> eprintfn "[video] no dispatch available"
            | None, _ ->
                let mutable idx = 0

                for page in ctx.Opts.Pages do
                    match getDispatch () with
                    | None -> eprintfn $"[shots] no dispatch yet, skipping %A{page}"
                    | Some dispatch ->
                        let route =
                            match page with
                            | Route.Match _ -> liveRoute page
                            | _ -> page

                        Dispatcher.UIThread.Invoke(fun () -> dispatch (Navigate route))

                        do!
                            Async.Sleep(
                                if ctx.Opts.InspectMatch then
                                    min ctx.Opts.SettleMs 4500
                                else
                                    ctx.Opts.SettleMs
                            )

                        if ctx.Opts.InspectMatch then
                            match page with
                            | Route.Match _ ->
                                Dispatcher.UIThread.Invoke(fun () ->
                                    dispatch (
                                        LiveMsg(LiveMsg.SelectIntelligenceWorkspace FootballEngine.LiveMatch.Events)
                                    ))

                                do! Async.Sleep(max 250 (ctx.Opts.SettleMs - 4500))
                            | _ -> ()

                        let file = Path.Combine(ctx.Opts.Dir, $"%02d{idx}_{page}.png")
                        Dispatcher.UIThread.Invoke(fun () -> captureTo window file)
                        printfn $"[shots] %A{page} -> {file}"
                        idx <- idx + 1

            Dispatcher.UIThread.Invoke(fun () -> window.Close())
        }
