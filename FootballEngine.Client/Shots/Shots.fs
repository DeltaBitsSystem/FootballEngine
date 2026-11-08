namespace FootballEngine

open System
open System.IO
open System.Diagnostics
open Avalonia
open Avalonia.Controls
open Avalonia.Media.Imaging
open Avalonia.Threading
open Avalonia.VisualTree
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Data
open FootballEngine.Generation.WorldGen
open FootballEngine.Core.Match
open FootballEngine.World
open FootballEngine.World.Phases
open FootballEngine.Settings
open FootballEngine.Client.Match3D
open FootballEngine.Icons
open FootballEngine.Routing

// Spectacle + manual clicking can't drive a fast autonomous UI loop on KDE/Wayland.
// `--shots` renders every page in-process with a fixed demo career and fixed window
// size, so `dotnet run -- --shots <dir>` + reading the PNGs is the whole review cycle.
// No new packages, no Xvfb required (works on XWayland :0, and under xvfb-run
// unchanged for background runs).
type IPitchPresentationSource =
    abstract FirstPresented: System.Threading.Tasks.Task<unit>

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
          /// Days the demo world runs before capture, so world pages (news, board) show real stories, not empty states.
          WorldDays: int
          /// Capture the career right after a dismissal, so the job hunt is reviewable.
          Unemployed: bool
          InspectMatch: bool
          MatchSeed: uint64 option
          /// Skip through the live match unpaced and write the match second of every goal; captures nothing.
          VideoFindGoals: bool
          /// Find every goal, then record each in a fresh run of the same seeded match.
          VideoGoals: bool
          /// Skip unpaced to just before each of these match seconds, then record the move at normal speed.
          VideoGoalsAt: int list
          VideoMaxGoals: int
          VideoLeadSeconds: int
          VideoTailSeconds: int
          /// Render the fixed validation scenes into this directory, with per-pass timings.
          ViewerScenesDir: string option
          /// Forces the 3D quality tier for this run instead of the saved setting.
          ViewerQualityTier: ViewerQualityTier option
          ViewerSceneFrames: int
          /// Match second of the goal used by the goal scene; it depends on the match seed.
          ViewerGoalSecond: int
          Args: string[] }

    type Context = { Opts: Options; Demo: State }

    // Training/Scouting/Settings collide with PhaseTag and SettingsCategory cases opened
    // for the demo builder; qualification keeps it unambiguous.
    let allPages =
        [ Route.Loading
          Route.Setup
          Route.Home
          Route.Calendar
          Route.News
          Route.Board
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
        | "news" -> Some Route.News
        | "board" -> Some Route.Board
        | "inbox" -> Some(Route.Inbox(InboxRoute None))
        | "squad" -> Some(Route.Squad SquadRoute.All)
        | "tactics" -> Some(Route.Tactics TacticsRoute.Overview)
        | "training" -> Some(Route.Training TrainingRoute.Overview)
        | "scouting" -> Some(Route.Scouting ScoutingRoute.Overview)
        | "transfers" -> Some(Route.Transfers TransferRoute.Market)
        | "finances" -> Some(Route.Finances FinanceRoute.Overview)
        | "settings" -> Some(Route.Settings SettingsRoute.Overview)
        | "match" -> Some(Route.Match(1, MatchPhase.Live))
        | "prematch" -> Some(Route.Match(1, MatchPhase.Prematch))
        | "postmatch" -> Some(Route.Match(1, MatchPhase.Postmatch))
        | "modeditor" -> Some Route.ModEditor
        | _ -> None

    let tryParse (args: string[]) : Options option =
        let getValue (flag: string) =
            args
            |> Array.tryFindIndex (fun a -> a = flag)
            |> Option.bind (fun i -> if i + 1 < args.Length then Some args.[i + 1] else None)

        let findGoals = args |> Array.contains "--video-find-goals"
        let allGoals = args |> Array.contains "--video-goals"
        let findGoal = findGoals || allGoals

        let viewerScenes = getValue "--viewer-scenes"

        match
            (match getValue "--shots" with
             | Some dir -> Some dir
             | None -> viewerScenes),
            getValue "--match-video"
        with
        | None, None when not findGoal && not (args |> Array.contains "--video-goal-at") -> None
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
                | None when videoPath.IsSome || findGoal || viewerScenes.IsSome -> [ Route.Match(1, MatchPhase.Live) ]
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
                  VideoFps = positiveValue "--video-fps" 20
                  VideoStep = positiveValue "--video-step" 4
                  VideoFrames = positiveValue "--video-frames" 3600
                  MatchIndex = nonNegativeValue "--match-index" 0
                  WorldDays = nonNegativeValue "--shots-days" 0
                  Unemployed = args |> Array.contains "--shots-unemployed"
                  InspectMatch = args |> Array.contains "--match-evidence"
                  MatchSeed = matchSeed
                  VideoFindGoals = findGoals
                  VideoGoals = allGoals
                  VideoGoalsAt =
                    getValue "--video-goal-at"
                    |> Option.map (fun value ->
                        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.choose (fun item ->
                            match Int32.TryParse(item.Trim()) with
                            | true, parsed when parsed >= 0 -> Some parsed
                            | _ -> None)
                        |> Array.sort
                        |> List.ofArray)
                    |> Option.defaultValue []
                  VideoMaxGoals = positiveValue "--video-max-goals" Int32.MaxValue
                  VideoLeadSeconds = positiveValue "--video-lead" 12
                  VideoTailSeconds = positiveValue "--video-tail" 5
                  ViewerScenesDir = viewerScenes |> Option.map Path.GetFullPath
                  ViewerQualityTier =
                    getValue "--viewer-quality"
                    |> Option.bind (fun value ->
                        match ViewerQualityTier.tryParse value with
                        | Ok tier -> Some tier
                        | Error _ -> None)
                  ViewerSceneFrames = positiveValue "--viewer-frames" 120
                  ViewerGoalSecond = nonNegativeValue "--viewer-goal-at" 0
                  Args = args }

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
            if opts.WorldDays = 0 then
                gs
            else
                (WorldRunner.advanceDays opts.WorldDays (WorldClockOps.init gs.Season) gs).GameState

        let gs =
            if opts.Unemployed then
                Career.dismiss gs.CurrentDate "Board confidence collapsed" gs.UserStaffId gs
                |> FootballEngine.World.Pulse.JobMarket.refresh
                    WorldPulseConfig.defaultWorldPulse
                    true
                    gs.CurrentDate
            else
                gs

        // An advanced world already sits on its own date; jumping to a fixture would hide the days just simulated.
        let gs =
            let fixtures =
                gs.Competitions
                |> Map.toSeq
                |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                |> Seq.filter (fun (_, fixture) ->
                    MatchFixture.isPending fixture && MatchFixture.involves gs.UserClubId fixture)
                |> Seq.sortBy (fun (_, fixture) -> fixture.ScheduledDate)
                |> Seq.filter (fun _ -> opts.WorldDays = 0)
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
                Tactics =
                    { DraggedPlayer = None
                      SelectedSlot = None
                      History = [] }
                Training = { SelectedPlayer = demoPupil |> Option.map _.Id }
                Settings =
                    AppTypes.initSettingsState
                        (match opts.ViewerQualityTier with
                         | Some tier ->
                             FootballEngine.Client.Match3D.ViewerQualitySelection.select tier

                             { globalSettings with
                                 MatchView = { globalSettings.MatchView with ViewerQuality = tier } }
                         | None -> globalSettings)
                        SaveSettings.defaults
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

        match opts.Pages with
        | [ Route.Match(_, MatchPhase.Postmatch) ] ->
            // A real batch result lets the review screen be checked without waiting through real-time playback.
            match GameState.getUserNextFixture gs with
            | Some(id, _) ->
                let balance =
                    WeightMergeSettings.mergeWithOverridesSave (Some withNotes.SaveSettings)

                let result =
                    WorldRunner.prepareUserMatchJobWithBalance balance withNotes.WorldClock gs id
                    |> Result.bind (fun job ->
                        Simulator.runMatch Scheduler.systems job.Input.Context job.Input.Seed Simulator.RunMode.Fast
                        |> Result.mapError (sprintf "%A")
                        |> Result.map (WorldRunner.matchJobResult job []))

                match result with
                | Ok result ->
                    { withNotes with
                        Match =
                            { withNotes.Match with
                                RequestedMatchId = Some id
                                PendingResult = Some result } }
                | Error error -> failwithf "Cannot prepare postmatch screenshot: %s" error
            | None -> withNotes
        | _ -> withNotes

    let private waitForPitch (window: Window) =
        async {
            let attached =
                System.Threading.Tasks.TaskCompletionSource<System.Threading.Tasks.Task<unit>>(
                    System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously)

            let findPitch () =
                window.GetVisualDescendants()
                |> Seq.map box
                |> Seq.tryPick (function
                    | :? IPitchPresentationSource as pitch -> Some pitch.FirstPresented
                    | _ -> None)
                |> Option.iter (fun ready -> attached.TrySetResult ready |> ignore)

            let handler = EventHandler(fun _ _ -> findPitch ())

            Dispatcher.UIThread.Invoke(fun () ->
                window.LayoutUpdated.AddHandler handler
                findPitch ())

            try
                let deadline = Diagnostics.Stopwatch.StartNew()
                let! ready = attached.Task.WaitAsync(TimeSpan.FromSeconds 15.0) |> Async.AwaitTask
                let remaining = TimeSpan.FromSeconds 15.0 - deadline.Elapsed

                // Attachment and GPU presentation share one deadline, not two fixed settling delays.
                do! ready.WaitAsync(max TimeSpan.Zero remaining) |> Async.AwaitTask
            finally
                Dispatcher.UIThread.Invoke(fun () ->
                    window.LayoutUpdated.RemoveHandler handler)
        }

    /// X11 import of one window id; the native WebGPU child is invisible to Avalonia's own offscreen renderer.
    let private importWindow (id: uint64) (path: string) =
        let info = ProcessStartInfo("import", UseShellExecute = false, RedirectStandardError = true)
        info.ArgumentList.Add("-window")
        info.ArgumentList.Add(string id)
        info.ArgumentList.Add(path)
        use process = Process.Start info
        let errors = process.StandardError.ReadToEndAsync()

        if not (process.WaitForExit 15000) then
            process.Kill(true)
            process.WaitForExit()
            invalidOp "Native match capture timed out."

        if process.ExitCode <> 0 then
            invalidOp $"Native match capture failed: {errors.GetAwaiter().GetResult()}"

    let private captureTo (window: Window) (path: string) native =
        if native then
            // Avalonia's offscreen bitmap omits native WebGPU children and can falsely certify a blank pitch.
            match window.TryGetPlatformHandle() with
            | null -> invalidOp "Native match capture requires a realized window."
            | handle when handle.HandleDescriptor <> "XID" ->
                invalidOp "Native WebGPU match capture currently requires X11/XWayland."
            | handle -> importWindow (uint64 handle.Handle) path
        else
            let size = window.ClientSize
            let w = max 1 (int size.Width)
            let h = max 1 (int size.Height)
            use bmp = new RenderTargetBitmap(PixelSize(w, h), Vector(96.0, 96.0))
            bmp.Render(window)
            bmp.Save(path)

    let private encodeVideo (output: string) (fps: int) framesDir =
        do
            let parent = Path.GetDirectoryName output

            if not (String.IsNullOrWhiteSpace parent) then
                Directory.CreateDirectory(parent) |> ignore

            let info = ProcessStartInfo("ffmpeg", UseShellExecute = false)

            for argument in
                [ "-y"
                  "-framerate"
                  string fps
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

    /// Headless corpus over the demo career's real squads and the game's own context builder, so match
    /// calibration is measured on the data a player sees rather than on test fixtures.
    let runCorpus (count: int) (state: State) =
        match state.Mode with
        | InGame(gs, _) ->
            let fixtures =
                gs.Competitions
                |> Map.toSeq
                |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                |> Seq.filter (fun (_, fixture) -> MatchFixture.isPending fixture)
                |> Seq.sortBy (fun (id, fixture) -> fixture.ScheduledDate, id)
                |> Seq.truncate count
                |> Seq.toList

            let mutable goals = 0
            let mutable shots = 0
            let mutable played = 0

            for id, fixture in fixtures do
                match MatchContextBuilder.build gs fixture BalanceConfig.defaultConfig with
                | None -> printfn $"[corpus] fixture {id}: no context"
                | Some ctx ->
                    let seed = MatchContextBuilder.seedOf gs.ExecutionSeed id

                    match Simulator.runMatch Scheduler.systems ctx seed MatchRunMode.Fast with
                    | Error error -> printfn $"[corpus] fixture {id}: %A{error}"
                    | Ok outcome ->
                        let world = outcome.Final
                        let home, away = world.Home.Stats, world.Away.Stats
                        played <- played + 1
                        goals <- goals + world.State.HomeScore + world.State.AwayScore
                        shots <- shots + home.Shots + away.Shots

                        printfn
                            $"[corpus] {ctx.HomeClub.Name} {world.State.HomeScore}-{world.State.AwayScore} {ctx.AwayClub.Name} | shots {home.Shots}-{away.Shots} on target {home.OnTarget}-{away.OnTarget} xG {home.XgSum:F2}-{away.XgSum:F2} passes {home.PassesCompleted}/{home.Passes} {away.PassesCompleted}/{away.Passes}"

            if played > 0 then
                printfn $"[corpus] {played} matches: {float goals / float played:F2} goals and {float shots / float played:F1} shots per match"
        | _ -> eprintfn "[corpus] demo state is not in game"

    let runLoop
        (window: Window)
        (getDispatch: unit -> (Msg -> unit) option)
        (getState: unit -> State option)
        (ctx: Context)
        : Async<unit> =
        async {
            // Native renderer failures otherwise reach Trace only and can look like a successful blank capture.
            if isNull Trace.Listeners["shots-native"] then
                let listener = new ConsoleTraceListener(true)
                listener.Name <- "shots-native"
                Trace.Listeners.Add listener |> ignore

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

            let live () =
                getState () |> Option.bind (fun state -> state.Match.Live)

            // Same command path as the viewer's speed buttons, so presentation pacing stays authoritative.
            let sendLive dispatch payload =
                match live () with
                | Some state ->
                    let command: FootballEngine.LiveMatch.LiveMatchCommand =
                        { CommandId = FootballEngine.LiveMatch.MatchCommandId state.NextCommandId
                          SessionId = state.SessionId
                          ExpectedRevision = state.Revision
                          IssuedAt = state.Clock.Elapsed
                          Payload = payload }

                    Dispatcher.UIThread.Invoke(fun () -> dispatch (LiveMsg(LiveMsg.CommandSubmitted command)))
                | None -> ()

            let matchSecond () =
                live ()
                |> Option.map (fun state -> float (int state.Clock.Elapsed) / float SimulationClock.defaultClock.SubTicksPerSecond)
                |> Option.defaultValue 0.0

            let goals () =
                live () |> Option.map (fun state -> fst state.Score + snd state.Score) |> Option.defaultValue 0

            let openLiveMatch dispatch =
                async {
                    Dispatcher.UIThread.Invoke(fun () -> dispatch (Navigate(liveRoute (Route.Match(1, MatchPhase.Live)))))
                    do! Async.Sleep ctx.Opts.SettleMs
                    do! waitForPitch window
                }

            let liveTick () =
                live () |> Option.map (fun state -> state.Clock.Elapsed) |> Option.defaultValue 0<subtick>

            let ended () =
                match live () |> Option.map _.Status with
                | Some FootballEngine.LiveMatch.Finished
                | Some(FootballEngine.LiveMatch.Failed _) -> true
                | _ -> false

            let toTick (second: int) =
                second * SimulationClock.defaultClock.SubTicksPerSecond * 1<subtick>

            // The match is deterministic, so skipping the wall-clock wait reaches the same state the
            // paced viewer would; the worker advances unpaced and publishes only the newest pose.
            let skipTo dispatch (second: int) =
                async {
                    let target = toTick second
                    sendLive dispatch (FootballEngine.LiveMatch.SkipAhead target)
                    let deadline = Stopwatch.StartNew()

                    while liveTick () < target && not (ended ()) && deadline.Elapsed < TimeSpan.FromMinutes 5.0 do
                        do! Async.Sleep 20
                }

            let videoBase () =
                ctx.Opts.VideoPath
                |> Option.defaultValue (Path.Combine(ctx.Opts.Dir, "goal.mp4"))

            let findGoals dispatch =
                async {
                    do! openLiveMatch dispatch
                    let goals = ResizeArray<int>()
                    let mutable scored = 0

                    // Live state keeps only the latest 128 events and contacts alone exceed that in a
                    // minute, so the skip moves in short steps; a goal whose event already scrolled out
                    // is still caught by the score and placed at the end of its step.
                    let step = 5
                    let mutable second = step

                    while not (ended ()) && toTick second <= 400000<subtick> do
                        do! skipTo dispatch second
                        let stepStart = second - step

                        live ()
                        |> Option.iter (fun state ->
                            let exact =
                                state.Events
                                |> List.choose (fun event ->
                                    match event.Data with
                                    | EventData.Goal _ ->
                                        let at = int event.Tick / SimulationClock.defaultClock.SubTicksPerSecond
                                        if at >= stepStart && at < second then Some at else None
                                    | _ -> None)

                            let total = fst state.Score + snd state.Score
                            let added = total - scored
                            goals.AddRange(exact |> List.truncate (max 0 added))

                            for _ in 1 .. added - exact.Length do
                                goals.Add second

                            scored <- total)

                        second <- second + step

                    let score = live () |> Option.map _.Score
                    let found = goals |> Seq.sort |> List.ofSeq

                    match score with
                    | Some(home, away) when home + away <> found.Length ->
                        eprintfn $"[video] warning: final score {home}-{away} but {found.Length} goal events were read"
                    | _ -> ()

                    File.WriteAllLines(Path.Combine(ctx.Opts.Dir, "goals.txt"), found |> List.map string)
                    let stats (team: TeamStats) =
                        $"shots {team.Shots} on target {team.OnTarget} xG {team.XgSum:F2} passes {team.PassesCompleted}/{team.Passes}"

                    live ()
                    |> Option.iter (fun state ->
                        printfn $"[video] home {stats state.Metrics.Home} | away {stats state.Metrics.Away}")

                    printfn $"[video] goals at match seconds %A{found} (final score {score})"
                    return found
                }

            // Commands carry the revision they expect, so the next one waits until this one was applied.
            let sendAcked dispatch payload =
                async {
                    let revision () = live () |> Option.map _.Revision |> Option.defaultValue 0UL
                    let before = revision ()
                    sendLive dispatch payload
                    let deadline = Stopwatch.StartNew()

                    while revision () <= before && deadline.ElapsedMilliseconds < 2000L do
                        do! Async.Sleep 2
                }

            let ticksPerFrame = max 1 (SimulationClock.defaultClock.SubTicksPerSecond / ctx.Opts.VideoFps)
            // Margin for the paced worker to publish the frame and the viewer clock to reach it.
            let viewerCatchUpMs = 60

            let recordGoals dispatch (goalSeconds: int list) =
                async {
                    do! openLiveMatch dispatch
                    let stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(videoBase ())), Path.GetFileNameWithoutExtension(videoBase ()))
                    let framesDir = Path.Combine(ctx.Opts.Dir, "frames")

                    for index, goalAt in goalSeconds |> List.truncate ctx.Opts.VideoMaxGoals |> List.indexed do
                        if Directory.Exists framesDir then
                            Directory.Delete(framesDir, true)

                        Directory.CreateDirectory(framesDir) |> ignore
                        let start = max 0 (goalAt - ctx.Opts.VideoLeadSeconds)
                        // Holding before the skip makes the worker stop exactly at the clip start.
                        do! sendAcked dispatch (FootballEngine.LiveMatch.HoldAt(toTick start))

                        if liveTick () < toTick start then
                            do! skipTo dispatch start

                        let scoreBefore = goals ()
                        let stopAt = toTick (goalAt + ctx.Opts.VideoTailSeconds)
                        let wall = Stopwatch.StartNew()
                        let mutable frame = 0
                        let mutable tick = max (toTick start) (liveTick ())

                        // Each frame releases the match by exactly one frame of match time and grabs it once
                        // the viewer has caught up, so the clip runs at match speed however slow a capture is.
                        while frame < ctx.Opts.VideoFrames && tick < stopAt && not (ended ()) do
                            tick <- tick + ticksPerFrame * 1<subtick>
                            do! sendAcked dispatch (FootballEngine.LiveMatch.HoldAt tick)
                            do! Async.Sleep(ticksPerFrame * 1000 / SimulationClock.defaultClock.SubTicksPerSecond + viewerCatchUpMs)

                            do!
                                Dispatcher.UIThread
                                    .InvokeAsync((fun () -> window.UpdateLayout()), DispatcherPriority.Render)
                                    .GetTask()
                                |> Async.AwaitTask

                            Dispatcher.UIThread.Invoke(fun () -> captureTo window (Path.Combine(framesDir, $"%06d{frame}.png")) true)
                            frame <- frame + 1

                        do! sendAcked dispatch (FootballEngine.LiveMatch.HoldAt MatchSession.maxTicks)
                        let fps = SimulationClock.defaultClock.SubTicksPerSecond / ticksPerFrame

                        let output = $"{stem}-%02d{index + 1}.mp4"

                        if goals () = scoreBefore then
                            eprintfn $"[video] warning: no goal was scored while recording around second {goalAt}"

                        printfn $"[video] goal {index + 1} at {goalAt / 60}:%02d{goalAt % 60}: {frame} frames at {fps} fps, captured in {wall.Elapsed.TotalSeconds:F1}s -> {output}"
                        encodeVideo output fps framesDir
                        Directory.Delete(framesDir, true)
                }

            // The finished match cannot be rewound in-process, so recording runs the same seeded match again.
            let recordInFreshRun (goalSeconds: int list) =
                let exe = Environment.ProcessPath

                let prefix =
                    if Path.GetFileNameWithoutExtension exe = "dotnet" then
                        [ Reflection.Assembly.GetEntryAssembly().Location ]
                    else
                        []

                let args =
                    ctx.Opts.Args
                    |> Array.filter (fun arg -> arg <> "--video-goals")
                    |> List.ofArray

                let info = ProcessStartInfo(exe, UseShellExecute = false)

                for argument in prefix @ args @ [ "--video-goal-at"; goalSeconds |> List.map string |> String.concat "," ] do
                    info.ArgumentList.Add argument

                use child = Process.Start info
                child.WaitForExit()

            // Fixed validation scenes: the same seed, match second and camera every run, so a change in the image is
            // a change in the renderer. Seconds ascend because a live match cannot be rewound.
            let viewerScenes dispatch (directory: string) =
                async {
                    Directory.CreateDirectory directory |> ignore
                    ViewerDiagnostics.enableProfilingHud ()
                    do! openLiveMatch dispatch

                    let scenes =
                        [ 1, "kickoff-tv", 1, ViewerCamera.Broadcast
                          3, "closeup-dribble", 95, ViewerCamera.CloseUp
                          2, "corner-behind-goal", 600, ViewerCamera.BehindGoal
                          6, "night-rain-PLACEHOLDER-day", 3000, ViewerCamera.Broadcast
                          4, "goal", ctx.Opts.ViewerGoalSecond, ViewerCamera.Touchline
                          // Minute 30 until pitch wear exists (F6): reaching minute 80 costs minutes per capture run.
                          5, "minute-30-above", 1800, ViewerCamera.Tactical ]
                        |> List.sortBy (fun (_, _, second, _) -> second)

                    let results = ResizeArray<string>()
                    let options = Text.Json.JsonWriterOptions(Indented = true)
                    use stream = new MemoryStream()
                    let writer = new Text.Json.Utf8JsonWriter(stream, options)
                    writer.WriteStartObject()
                    writer.WriteString("seed", string (defaultArg ctx.Opts.MatchSeed 0UL))
                    writer.WriteNumber("framesPerScene", ctx.Opts.ViewerSceneFrames)

                    match ViewerDiagnostics.adapterReport () with
                    | Some adapter ->
                        writer.WriteStartObject "adapter"
                        writer.WriteString("name", adapter.AdapterName)
                        writer.WriteString("backend", adapter.Backend)
                        writer.WriteBoolean("timestampQuery", adapter.TimestampQuery)
                        writer.WriteBoolean("textureCompressionBC", adapter.TextureCompressionBC)
                        writer.WriteBoolean("indirectFirstInstance", adapter.IndirectFirstInstance)
                        writer.WriteBoolean("float32Filterable", adapter.Float32Filterable)

                        match adapter.TimestampPeriodNs with
                        | Some period -> writer.WriteNumber("timestampPeriodNs", period)
                        | None -> writer.WriteNull "timestampPeriodNs"

                        writer.WriteEndObject()
                    | None -> writer.WriteNull "adapter"

                    writer.WriteStartArray "scenes"

                    for number, name, second, camera in scenes do
                        // Holding first makes the worker stop exactly on the scene second instead of overshooting it.
                        do! sendAcked dispatch (FootballEngine.LiveMatch.HoldAt(toTick second))

                        if liveTick () < toTick second then
                            do! skipTo dispatch second

                        Dispatcher.UIThread.Invoke(fun () -> dispatch (LiveMsg(LiveMsg.SelectViewerCamera camera)))
                        do! Async.Sleep 1500
                        ViewerDiagnostics.beginWindow ()
                        let deadline = Stopwatch.StartNew()

                        while ViewerDiagnostics.windowFrames () < ctx.Opts.ViewerSceneFrames && deadline.Elapsed < TimeSpan.FromSeconds 120.0 do
                            do! Async.Sleep 25

                        let average = ViewerDiagnostics.windowAverage ()
                        let file = $"scene{number}-{name}.png"
                        let native = ViewerDiagnostics.nativeWindowId ()

                        if native = 0UL then
                            Dispatcher.UIThread.Invoke(fun () -> captureTo window (Path.Combine(directory, file)) true)
                        else
                            importWindow native (Path.Combine(directory, file))

                        writer.WriteStartObject()
                        writer.WriteNumber("scene", number)
                        writer.WriteString("name", name)
                        writer.WriteString("file", file)
                        writer.WriteNumber("matchSecond", second)
                        writer.WriteString("camera", string camera)

                        match average with
                        | Some stats ->
                            writer.WriteNumber("frames", stats.Frames)
                            writer.WriteNumber("fps", Math.Round(stats.Fps, 1))
                            writer.WriteNumber("frameIntervalMs", Math.Round(stats.FrameIntervalMs, 3))
                            writer.WriteNumber("cpuFrameMs", Math.Round(stats.CpuFrameMs, 3))
                            writer.WriteBoolean("gpuTimingAvailable", stats.GpuTimingAvailable)

                            match stats.GpuTotalMs with
                            | Some total -> writer.WriteNumber("gpuTotalMs", Math.Round(total, 3))
                            | None -> writer.WriteString("gpuTotalMs", "GPU timing unavailable")

                            writer.WriteStartArray "passes"

                            for pass in stats.Passes do
                                writer.WriteStartObject()
                                writer.WriteString("name", pass.Name)

                                match pass.GpuMs with
                                | Some value -> writer.WriteNumber("gpuMs", Math.Round(value, 3))
                                | None -> writer.WriteNull "gpuMs"

                                writer.WriteNumber("cpuMs", Math.Round(pass.CpuMs, 3))
                                writer.WriteEndObject()

                            writer.WriteEndArray()
                        | None -> writer.WriteString("error", "the renderer produced no frames")

                        writer.WriteEndObject()
                        printfn $"[viewer] scene {number} {name} at {second}s -> {file}"

                    writer.WriteEndArray()
                    writer.WriteEndObject()
                    writer.Flush()
                    File.WriteAllBytes(Path.Combine(directory, "stats.json"), stream.ToArray())
                    do! sendAcked dispatch (FootballEngine.LiveMatch.HoldAt MatchSession.maxTicks)
                }

            match ctx.Opts.VideoFindGoals || ctx.Opts.VideoGoals, ctx.Opts.VideoGoalsAt, ctx.Opts.VideoPath, getDispatch () with
            | _, _, _, Some dispatch when ctx.Opts.ViewerScenesDir.IsSome -> do! viewerScenes dispatch ctx.Opts.ViewerScenesDir.Value
            | true, _, _, Some dispatch ->
                let! found = findGoals dispatch

                if ctx.Opts.VideoGoals && not found.IsEmpty then
                    recordInFreshRun found
            | false, (_ :: _ as goalSeconds), _, Some dispatch -> do! recordGoals dispatch goalSeconds
            | false, [], Some output, Some dispatch ->
                let framesDir = Path.Combine(ctx.Opts.Dir, "frames")

                if Directory.Exists framesDir then
                    Directory.Delete(framesDir, true)

                Directory.CreateDirectory(framesDir) |> ignore
                do! openLiveMatch dispatch
                let mutable frame = 0

                while frame < ctx.Opts.VideoFrames do
                    do!
                        Dispatcher.UIThread
                            .InvokeAsync((fun () -> window.UpdateLayout()), DispatcherPriority.Render)
                            .GetTask()
                        |> Async.AwaitTask

                    Dispatcher.UIThread.Invoke(fun () -> captureTo window (Path.Combine(framesDir, $"%06d{frame}.png")) true)
                    frame <- frame + 1
                    do! Async.Sleep ctx.Opts.VideoStep

                printfn $"[video] {frame} frames, encoded at {ctx.Opts.VideoFps} fps"
                encodeVideo output ctx.Opts.VideoFps framesDir
                Directory.Delete(framesDir, true)
            | true, _, _, None
            | false, _ :: _, _, None
            | false, [], Some _, None -> eprintfn "[video] no dispatch available"
            | false, [], None, _ ->
                let mutable idx = 0

                for page in ctx.Opts.Pages do
                    match getDispatch () with
                    | None -> eprintfn $"[shots] no dispatch yet, skipping %A{page}"
                    | Some dispatch ->
                        let route =
                            match page with
                            | Route.Match(_, phase) ->
                                match liveRoute page with
                                | Route.Match(id, _) -> Route.Match(id, phase)
                                | route -> route
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

                        let native =
                            match route with
                            | Route.Match(_, MatchPhase.Live) -> true
                            | _ -> false

                        if native then
                            do! waitForPitch window

                        Dispatcher.UIThread.Invoke(fun () -> captureTo window file native)
                        printfn $"[shots] %A{page} -> {file}"
                        idx <- idx + 1

            Dispatcher.UIThread.Invoke(fun () -> window.Close())
        }
