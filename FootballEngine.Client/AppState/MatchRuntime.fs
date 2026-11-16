namespace FootballEngine

open System
open System.Diagnostics
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.Settings
open FootballEngine.World
open FootballEngine.World.Phases
open FootballEngine.World.WorldRunner
open FootballEngine.LiveMatch

/// Publications remain outside Elmish until a small wake-up message drains them.
type MatchRuntimePublication =
    | Connected of LiveMatchSessionId * FixtureContext * revision: uint64 * LiveMatchSource
    | Semantic of LiveRuntimeSemantic
    | SemanticReset of LiveRuntimeSemantic
    | CommandAccepted of MatchCommandId * ApplicationPoint
    | CommandApplied of MatchCommandId * revision: uint64 * appliedAt: int<subtick>
    | CommandRejected of MatchCommandId * CommandRejection
    | StatusChanged of LiveMatchStatus
    | Finished of MatchJobResult
    | Failed of string

type private MatchObservationRequest =
    | SelectDetail of ObservationSelection option
    | SelectUpcoming of FootballEngine.Core.Match.PlayerId * int<tickDelta>
    | CaptureArchive of TaskCompletionSource<Result<MatchObservationCapture, string>>

type private MatchRuntime
    (
        runtimeId: MatchRuntimeId,
        matchId: MatchId,
        gs: GameState,
        clock: WorldClock,
        saveOpt: SaveSettings option,
        runMode: MatchRunMode,
        source: LiveMatchSource,
        startAt: int<subtick>,
        initialHold: int<subtick>,
        highlight: HighlightFilter
    ) =
    let cancellation = new CancellationTokenSource()
    let commandQueue = Channel.CreateUnbounded<LiveMatchCommand>()
    let observationRequests = Channel.CreateBounded<MatchObservationRequest>(4)

    let policy =
        match runMode with
        | MatchRunMode.Live value -> value
        | _ -> LiveExecutionPolicy.defaultPolicy

    let presentation = new MatchPresentationStream(runtimeId, policy)

    let queue =
        let options = BoundedChannelOptions(32)
        options.FullMode <- BoundedChannelFullMode.Wait
        options.SingleReader <- true
        options.SingleWriter <- true
        Channel.CreateBounded<MatchRuntimePublication>(options)

    let mutable worker: Task option = None

    // 1 while a wake is in flight to the UI. Every wake costs a full update and view pass, so the UI is woken
    // once per batch it has not drained yet rather than once per publication; 4x playback had flooded it.
    let mutable wakePending = 0

    let enqueue (dispatch: Msg -> unit) publication =
        queue.Writer.WriteAsync(publication, cancellation.Token).AsTask().GetAwaiter().GetResult()

        if Interlocked.Exchange(&wakePending, 1) = 0 then
            dispatch (Msg.LiveMsg(LiveMsg.RuntimePublicationAvailable runtimeId))

    let buildJob () : Result<MatchJob, string> =
        match source with
        | RecordedReplay -> prepareReplayJob clock gs matchId
        | LiveSession ->
            let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
            prepareUserMatchJobWithBalance balance clock gs matchId

    let compactResult (job: MatchJob) (session: MatchSession) (outcome: MatchOutcome) : MatchJobResult =
        let replay =
            outcome.Replay
            |> Option.map (fun replay ->
                { replay with
                    Sections = Array.append replay.Sections [| presentation.MotionArchiveSection() |] })

        matchJobResult job session.Commands { outcome with Replay = replay }

    let liveClock (session: MatchSession) : LiveClock =
        let clock = session.World.State.Clock

        let period =
            match clock.Phase with
            | ClockPhase.FirstHalf -> MatchPeriod.FirstHalf
            | ClockPhase.HalfTime -> HalfTimeInterval
            | ClockPhase.SecondHalf -> MatchPeriod.SecondHalf
            | ClockPhase.ExtraTimeFirstHalf -> MatchPeriod.ExtraTimeFirstHalf
            | ClockPhase.ExtraTimeSecondHalf -> MatchPeriod.ExtraTimeSecondHalf
            | ClockPhase.FullTime -> FullTime

        let minute = int (SimulationClock.toMatchMinute clock.Tick)

        let boundary =
            match clock.Phase with
            | ClockPhase.FirstHalf
            | ClockPhase.HalfTime -> 45
            | ClockPhase.SecondHalf -> 90
            | ClockPhase.ExtraTimeFirstHalf -> 105
            | ClockPhase.ExtraTimeSecondHalf
            | ClockPhase.FullTime -> 120

        { Elapsed = session.World.State.Tick
          DisplayMinute = min minute boundary
          StoppageMinute = if minute > boundary then Some(minute - boundary) else None
          Period =
            if session.World.State.Flow = MatchFlow.Shootout then
                PenaltyShootout
            else
                period }

    let coreCommand (session: MatchSession) (command: LiveMatchCommand) : MatchCommand option =
        let tactics =
            if uint64 gs.UserClubId = session.Input.Context.HomeClub.Id then
                session.Input.Context.HomeClub.BaseTactics
            else
                session.Input.Context.AwayClub.BaseTactics

        let action =
            match command.Payload with
            | ChangeFormation formation ->
                Some(
                    ChangeTactics
                        { tactics with
                            Formation = string formation }
                )
            | ChangeMentality mentality ->
                let value =
                    match mentality with
                    | Defensive -> 0.1f
                    | Cautious -> 0.3f
                    | Balanced -> 0.5f
                    | Positive -> 0.7f
                    | Attacking -> 0.9f

                Some(ChangeTactics { tactics with Mentality = value })
            | ConfirmSubstitutions changes
            | PrepareSubstitutions changes ->
                changes
                |> List.tryHead
                |> Option.map (fun change ->
                    RequestSubstitution(
                        FootballEngine.Core.Match.PlayerId change.Outgoing,
                        FootballEngine.Core.Match.PlayerId change.Incoming
                    ))
            | _ -> None

        action
        |> Option.map (fun action ->
            { EffectiveTick = max session.World.State.Tick command.IssuedAt
              Sequence = LiveMatchCommand.idValue command.CommandId
              Revision = session.Input.EffectiveConfig.Revision
              Manager = uint64 gs.UserClubId
              Action = action })

    let buildFixtureContext (job: MatchJob) : FixtureContext option =
        let context = job.Input.Context

        let competitionFixture =
            gs.Competitions
            |> Map.toSeq
            |> Seq.tryPick (fun (competitionId, competition) ->
                competition.Fixtures
                |> Map.tryFind matchId
                |> Option.map (fun fixture -> competitionId, competition, fixture))

        let clubIdentity (club: ClubRef) =
            let clubId = int club.Id
            let domainClub = gs.Clubs |> Map.tryFind clubId
            let name = domainClub |> Option.map _.Name |> Option.defaultValue club.Name

            // Kit hex strings are validated as #RRGGBB when content loads; anything else means "no kit colour".
            let packed (hex: string) =
                match UInt32.TryParse(hex.TrimStart('#'), Globalization.NumberStyles.HexNumber, null) with
                | true, rgb when hex.Length = 7 -> 0xFF000000u ||| rgb
                | _ -> 0u

            let kit = domainClub |> Option.bind _.Kit

            let leagueLevel =
                SimHelpers.leagueOf gs clubId
                |> Option.bind (fun competitionId -> gs.Competitions |> Map.tryFind competitionId)
                |> Option.bind (fun competition ->
                    match competition.Type with
                    | NationalLeague(LeagueLevel level, _) -> Some level
                    | _ -> None)

            { ClubId = clubId
              Name = name
              ShortName = name
              PrimaryColor = kit |> Option.map (fun kit -> packed kit.Primary) |> Option.defaultValue 0u
              SecondaryColor = kit |> Option.map (fun kit -> packed kit.Secondary) |> Option.defaultValue 0u
              Reputation = domainClub |> Option.map _.Reputation |> Option.defaultValue 5000
              LeagueLevel = leagueLevel }

        let playerNames (lineups: FootballEngine.Core.Match.Lineup array) =
            lineups
            |> Array.collect _.Profiles
            |> Array.map (fun profile ->
                let (FootballEngine.Core.Match.PlayerId playerId) = profile.Id

                let name =
                    gs.Players
                    |> Map.tryFind playerId
                    |> Option.map _.Name
                    |> Option.defaultValue "Unknown player"

                profile.Id, name)
            |> Map.ofArray

        let teamIdentity (lineup: FootballEngine.Core.Match.Lineup) tactics =
            { Players =
                lineup.Profiles
                |> Array.map (fun profile ->
                    let (FootballEngine.Core.Match.PlayerId id) = profile.Id

                    { Id = id
                      Name = gs.Players[id].Name
                      IsGoalkeeper = profile.IsGK })
              StarterCount = lineup.StarterCount
              InitialTactics = tactics }

        competitionFixture
        |> Option.map (fun (competitionId, competition, fixture) ->
            { MatchId = matchId
              CompetitionId = competitionId
              CompetitionName = competition.Name
              BroadcastIdentityKey = competition.BroadcastIdentity.Key
              Home = clubIdentity context.HomeClub
              Away = clubIdentity context.AwayClub
              HomeLineup = teamIdentity context.HomeLineup context.HomeClub.BaseTactics
              AwayLineup = teamIdentity context.AwayLineup context.AwayClub.BaseTactics
              UserClubId = gs.UserClubId
              BroadcastIdentity = competition.BroadcastIdentity
              PlayerNames = playerNames [| context.HomeLineup; context.AwayLineup |]
              Round = fixture.Round
              Leg = None
              AggregateScore = None
              SubstitutionRules = context.SubstitutionRules })

    let run (dispatch: Msg -> unit) =
        try
            match buildJob () with
            | Error error -> enqueue dispatch (Failed error)
            | Ok job ->
                match buildFixtureContext job with
                | None -> enqueue dispatch (Failed "The scheduled fixture could not be resolved.")
                | Some fixtureContext ->
                    let (MatchRuntimeId runtimeValue) = runtimeId
                    let sessionId = LiveMatchSessionId runtimeValue

                    enqueue dispatch (Connected(sessionId, fixtureContext, 0UL, source))

                    let sessionResult = MatchSession.create Scheduler.systems job.Input runMode

                    match sessionResult with
                    | Error error -> enqueue dispatch (Failed(sprintf "%A" error))
                    | Ok session ->
                        let mutable replayCommands =
                            match source with
                            | RecordedReplay -> clock.MatchExecutions[matchId] |> snd
                            | LiveSession -> []

                        // A later outgoing player may have entered through an earlier substitution.
                        // Validate recorded actions at their original frontier, not against kickoff occupancy.
                        let rec receiveRecordedCommands () =
                            match replayCommands with
                            | command :: rest when command.EffectiveTick <= session.World.State.Tick ->
                                MatchSession.receive command session
                                |> Result.bind (fun () ->
                                    replayCommands <- rest
                                    receiveRecordedCommands ())
                            | _ -> Ok()

                        let rigPath =
                            IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

                        let motionConfiguration =
                            match
                                FootballEngine.Client.Animation.MotionIdentity.forMatch
                                    FootballEngine.Client.Animation.AnimationConfig.defaultConfig
                                    rigPath
                                    job.Input
                            with
                            | Error message -> Error message
                            | Ok identity ->
                                match presentation.ConfigureMotion identity with
                                | Error error -> Error(sprintf "%A" error)
                                | Ok() ->
                                    match MatchSession.initialMotion session with
                                    | Error error -> Error(sprintf "%A" error)
                                    | Ok initial ->
                                        presentation.PublishMotion [| initial |] |> Result.mapError (sprintf "%A")

                        match motionConfiguration with
                        | Ok() -> ()
                        | Error message -> enqueue dispatch (Failed($"Motion identity configuration failed: {message}"))

                        // A match held before its first advance has no pose to present; publishing the starting
                        // snapshot gives the viewer one. Live play never starts held, so it is unaffected.
                        if startAt = 0<subtick> && initialHold < MatchSession.maxTicks then
                            let starting = Replay.capture session.World

                            presentation.Publish(starting.Tick, starting, Stopwatch.GetTimestamp())
                            |> ignore

                        let ticksPerSlice =
                            match runMode with
                            | MatchRunMode.Live _ -> int policy.WorkerSlice
                            | MatchRunMode.Archive interval -> int interval
                            | MatchRunMode.Fast -> int LiveExecutionPolicy.defaultPolicy.WorkerSlice

                        let mutable speed = Normal
                        // Capture workflows hold the match at a tick; they must see every frame, so pacing stays off.
                        let pacing =
                            (match runMode with
                             | MatchRunMode.Live _ -> true
                             | _ -> false)
                            && source = LiveSession
                            && initialHold >= MatchSession.maxTicks

                        let mutable director = KeyMoments.init
                        let mutable cruising = false

                        let nameOf (id: FootballEngine.Core.Match.PlayerId) =
                            fixtureContext.PlayerNames |> Map.tryFind id |> Option.defaultValue ""

                        let userSide =
                            if uint64 gs.UserClubId = session.Input.Context.HomeClub.Id then
                                ClubSide.Home
                            else
                                ClubSide.Away

                        let mutable nextAdvance = Stopwatch.GetTimestamp()
                        let mutable finished = Result.isError motionConfiguration
                        let mutable paused = false
                        let mutable holdAt = initialHold
                        // Until this tick the worker advances unpaced, even paused: a seek is still publishing its way in.
                        let mutable warmUntil = 0<subtick>

                        // Motion matching reads a second of future trajectory and the viewer runs a second behind it,
                        // so a seek lands this far before its target; landing on it left nothing to animate.
                        let seekWarmupTicks =
                            2
                            * int (
                                policy.PresentationDelay.TotalSeconds
                                * float SimulationClock.defaultClock.SubTicksPerSecond
                            )
                            * 1<subtick>

                        let mutable accepted: LiveMatchCommand list = []
                        let mutable revision = 0UL
                        let mutable lastCommandId = 0UL
                        let mutable lastSemanticTick = 0<subtick>
                        let mutable lastEvidence = 0UL
                        let mutable lastSemanticScore = 0, 0
                        let mutable pendingEvents: MatchEvent list = []

                        let criticalEvent event =
                            match MatchEvent.kind event with
                            | EventKind.Goal
                            | EventKind.Card
                            | EventKind.RedCard
                            | EventKind.Injury
                            | EventKind.SubstitutionApplied
                            | EventKind.VARDecision
                            | EventKind.HalfTime
                            | EventKind.FullTime -> true
                            | _ -> false

                        let publishSemantic force frontier events =
                            let score = session.World.State.HomeScore, session.World.State.AwayScore
                            // The cadence is per second watched, not per simulated second: at 4x a fixed tick
                            // interval quadrupled the HUD updates the UI thread had to render.
                            let multiplier =
                                match speed with
                                | Normal -> 1
                                | Fast -> 2
                                | VeryFast -> 4

                            let due =
                                frontier - lastSemanticTick
                                >= int policy.SemanticEvery * multiplier * 1<subtick>

                            let scoreChanged = score <> lastSemanticScore

                            if force || due || scoreChanged then
                                enqueue
                                    dispatch
                                    (Semantic
                                        { RuntimeId = runtimeId
                                          Revision = revision
                                          Frontier = frontier
                                          Evidence =
                                            if lastEvidence < session.World.Observation.Total then
                                                Some(session.World.Observation.SnapshotSince lastEvidence)
                                            else
                                                None
                                          Events = events |> List.toArray
                                          Clock = liveClock session
                                          Score = score
                                          Stats = session.World.Home.Stats, session.World.Away.Stats })

                                lastSemanticTick <- frontier
                                lastEvidence <- session.World.Observation.Total
                                lastSemanticScore <- score
                                pendingEvents <- []

                        let concludeMatch (frontier: int<subtick>) =
                            publishSemantic true frontier pendingEvents

                            match MatchSession.finish session with
                            | Error error -> enqueue dispatch (Failed(sprintf "%A" error))
                            | Ok outcome ->
                                let recordedEvents =
                                    gs.Competitions
                                    |> Map.toSeq
                                    |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                                    |> Seq.tryFind (fun (id, _) -> id = matchId)
                                    |> Option.map (fun (_, fixture) -> MatchOutcomeHash.eventsHash fixture.Events)

                                match source with
                                | RecordedReplay when recordedEvents <> Some outcome.EventsHash ->
                                    enqueue
                                        dispatch
                                        (Failed "Recorded execution differs from the committed sporting events.")
                                | RecordedReplay ->
                                    // A recording has nothing to commit; the events hash above is its only check.
                                    enqueue dispatch (StatusChanged LiveMatchStatus.Finished)
                                | LiveSession -> enqueue dispatch (Finished(compactResult job session outcome))

                            finished <- true

                        // A seek re-simulates unpublished, restarts the presentation where it lands and publishes
                        // the last stretch up to the target, so the viewer has motion to animate when it arrives.
                        let seekTo (target: int<subtick>) =
                            if
                                target > session.World.State.Tick
                                && session.World.State.Flow <> MatchFlow.MatchEnded
                            then
                                warmUntil <- max warmUntil target

                            let landing = target - seekWarmupTicks

                            if
                                landing > session.World.State.Tick
                                && session.World.State.Flow <> MatchFlow.MatchEnded
                            then
                                enqueue dispatch (StatusChanged Seeking)
                                let mutable failure: string option = None

                                while session.World.State.Tick < landing
                                      && session.World.State.Flow <> MatchFlow.MatchEnded
                                      && failure.IsNone
                                      && not cancellation.IsCancellationRequested do
                                    // The cap keeps a long seek responsive to cancellation and closing the viewer.
                                    let nextCommand =
                                        match replayCommands with
                                        | command :: _ -> command.EffectiveTick
                                        | [] -> landing

                                    let stop = min (min landing nextCommand) (session.World.State.Tick + 4000<subtick>)

                                    match
                                        receiveRecordedCommands ()
                                        |> Result.bind (fun () -> MatchSession.advanceSilentlyTo stop session)
                                    with
                                    | Ok _ -> ()
                                    | Error error -> failure <- Some(sprintf "%A" error)

                                match failure with
                                | Some message ->
                                    enqueue dispatch (Failed message)
                                    finished <- true
                                | None when cancellation.IsCancellationRequested -> ()
                                | None ->
                                    let tick = session.World.State.Tick
                                    presentation.Reset(tick + 1<subtick>, MatchSession.motionCursor session)

                                    presentation.Publish(tick, Replay.capture session.World, Stopwatch.GetTimestamp())
                                    |> ignore

                                    let score = session.World.State.HomeScore, session.World.State.AwayScore

                                    let evidence =
                                        if lastEvidence < session.World.Observation.Total then
                                            Some(session.World.Observation.SnapshotSince lastEvidence)
                                        else
                                            None

                                    let recent =
                                        session.Events |> Seq.skip (max 0 (session.Events.Count - 128)) |> Seq.toArray

                                    enqueue
                                        dispatch
                                        (SemanticReset
                                            { RuntimeId = runtimeId
                                              Revision = revision
                                              Frontier = tick
                                              Evidence = evidence
                                              Events = recent
                                              Clock = liveClock session
                                              Score = score
                                              Stats = session.World.Home.Stats, session.World.Away.Stats })

                                    lastSemanticTick <- tick
                                    lastSemanticScore <- score
                                    lastEvidence <- session.World.Observation.Total
                                    pendingEvents <- []

                                    nextAdvance <- Stopwatch.GetTimestamp()

                                    enqueue
                                        dispatch
                                        (StatusChanged(
                                            if paused then Paused
                                            elif cruising then Cruising
                                            else Live speed
                                        ))

                                    if session.World.State.Flow = MatchFlow.MatchEnded then
                                        concludeMatch tick

                        let applied command =
                            revision <- revision + 1UL
                            enqueue dispatch (CommandApplied(command.CommandId, revision, session.World.State.Tick))

                        let acceptImmediate command =
                            enqueue dispatch (CommandAccepted(command.CommandId, Immediate))
                            applied command

                        let handleCommand (command: LiveMatchCommand) =
                            match command.Payload with
                            | PauseMatch ->
                                paused <- true
                                presentation.SetPlayback(speed, true, Stopwatch.GetTimestamp())
                                acceptImmediate command
                                enqueue dispatch (StatusChanged Paused)
                            | ResumeMatch ->
                                paused <- false
                                nextAdvance <- Stopwatch.GetTimestamp()
                                presentation.SetPlayback(speed, false, Stopwatch.GetTimestamp())
                                acceptImmediate command
                                enqueue dispatch (StatusChanged(if cruising then Cruising else Live speed))
                            | SetLiveSpeed value ->
                                speed <- value
                                nextAdvance <- Stopwatch.GetTimestamp()
                                presentation.SetPlayback(value, paused, Stopwatch.GetTimestamp())
                                acceptImmediate command

                                enqueue
                                    dispatch
                                    (StatusChanged(
                                        if paused then Paused
                                        elif cruising then Cruising
                                        else Live speed
                                    ))
                            | SkipAhead untilTick ->
                                acceptImmediate command
                                seekTo (min untilTick MatchSession.maxTicks)
                            | HoldAt untilTick ->
                                holdAt <- untilTick
                                nextAdvance <- Stopwatch.GetTimestamp()
                                acceptImmediate command
                            | _ when source = RecordedReplay ->
                                enqueue
                                    dispatch
                                    (CommandRejected(
                                        command.CommandId,
                                        CommandNotPermitted "A recorded match cannot accept managerial changes."
                                    ))
                            | _ ->
                                match coreCommand session command with
                                | None ->
                                    enqueue
                                        dispatch
                                        (CommandRejected(
                                            command.CommandId,
                                            CommandNotPermitted "The selected command has no engine action."
                                        ))
                                | Some engineCommand ->
                                    match MatchSession.receive engineCommand session with
                                    | Ok() ->
                                        accepted <- accepted @ [ command ]

                                        enqueue
                                            dispatch
                                            (CommandAccepted(
                                                command.CommandId,
                                                LiveMatchCommand.applicationPoint command.Payload
                                            ))
                                    | Error error ->
                                        enqueue
                                            dispatch
                                            (CommandRejected(command.CommandId, CommandNotPermitted(sprintf "%A" error)))

                        let rec drainCommands () =
                            match commandQueue.Reader.TryRead() with
                            | true, command ->
                                if command.SessionId <> sessionId then
                                    enqueue
                                        dispatch
                                        (CommandRejected(
                                            command.CommandId,
                                            CommandNotPermitted "Command belongs to another session."
                                        ))
                                elif LiveMatchCommand.idValue command.CommandId <= lastCommandId then
                                    ()
                                else
                                    lastCommandId <- LiveMatchCommand.idValue command.CommandId

                                    if command.ExpectedRevision <> revision then
                                        enqueue
                                            dispatch
                                            (CommandRejected(
                                                command.CommandId,
                                                StaleRevision(command.ExpectedRevision, revision)
                                            ))
                                    else
                                        handleCommand command

                                drainCommands ()
                            | _ -> ()

                        // A recording opened at a tick is re-simulated to it before the viewer is shown anything.
                        if startAt > 0<subtick> && not finished then
                            seekTo startAt

                        while not finished && not cancellation.IsCancellationRequested do
                            let mutable takeObservation = true

                            while takeObservation do
                                match observationRequests.Reader.TryRead() with
                                | true, SelectDetail selection ->
                                    session.World.Observation.SelectDetail selection |> ignore
                                | true, SelectUpcoming(actor, duration) ->
                                    session.World.Observation.SelectDetail(
                                        Some
                                            { Actor = actor
                                              FromTick = session.World.State.Tick
                                              UntilTick = session.World.State.Tick + int duration * 1<subtick> }
                                    )
                                    |> ignore
                                | true, CaptureArchive completion ->
                                    try
                                        completion.TrySetResult(Ok(MatchObservationArchive.snapshot session)) |> ignore
                                    with error ->
                                        completion.TrySetResult(Error error.Message) |> ignore
                                | _ -> takeObservation <- false

                            drainCommands ()

                            match
                                presentation.RecoverMotion(
                                    (fun tick -> MatchSession.motionBlock tick session),
                                    cancellation.Token
                                )
                            with
                            | Error message when not cancellation.IsCancellationRequested ->
                                Diagnostics.Trace.WriteLine($"Motion recovery held presentation: {message}")
                            | _ -> ()

                            let held = session.World.State.Tick >= holdAt
                            let fastForward = cruising

                            // A seek drained above may have ended the match; one more advance would fail it.
                            if finished then
                                ()
                            elif
                                held
                                || (session.World.State.Tick >= warmUntil
                                    && (paused || Stopwatch.GetTimestamp() < nextAdvance))
                            then
                                cancellation.Token.WaitHandle.WaitOne 5 |> ignore
                            else
                                let current = session.World.State.Tick
                                let previousFlow = session.World.State.Flow

                                let advanceResult =
                                    receiveRecordedCommands ()
                                    |> Result.bind (fun () ->
                                        // Live slices are tiny to keep the viewer smooth.
                                        let slice =
                                            if fastForward then
                                                KeyMoments.cruiseSliceTicks
                                            else
                                                ticksPerSlice

                                        let target = min MatchSession.maxTicks (current + slice * 1<subtick>)

                                        let target =
                                            match replayCommands with
                                            | command :: _ -> min target command.EffectiveTick
                                            | [] -> target

                                        let target = min target holdAt
                                        MatchSession.advanceTo target session)

                                match advanceResult with
                                | Error error ->
                                    enqueue dispatch (Failed(sprintf "%A" error))
                                    finished <- true
                                | Ok advance ->
                                    match presentation.PublishMotion advance.Motion with
                                    | Ok() -> ()
                                    | Error error ->
                                        enqueue dispatch (Failed(sprintf "Motion continuity failed: %A" error))
                                        finished <- true

                                    let pending = ResizeArray<LiveMatchCommand>()

                                    for command in accepted do
                                        let hasApplied =
                                            match command.Payload with
                                            | ConfirmSubstitutions changes
                                            | PrepareSubstitutions changes ->
                                                changes
                                                |> List.forall (fun change ->
                                                    advance.Events
                                                    |> Array.exists (fun event ->
                                                        match event.Data with
                                                        | EventData.SubstitutionApplied(PlayerId outgoing,
                                                                                        PlayerId incoming) ->
                                                            outgoing = change.Outgoing && incoming = change.Incoming
                                                        | _ -> false))
                                            | _ ->
                                                session.Pending
                                                |> List.exists (fun item ->
                                                    item.Sequence = LiveMatchCommand.idValue command.CommandId)
                                                |> not

                                        if hasApplied then applied command else pending.Add command

                                    accepted <- List.ofSeq pending
                                    let capturedAt = Stopwatch.GetTimestamp()

                                    let poseTimestamp (pose: Replay.ReplaySnapshot) =
                                        let multiplier =
                                            match speed with
                                            | Normal -> 1.0
                                            | Fast -> 2.0
                                            | VeryFast -> 4.0

                                        let secondsBehind =
                                            float (advance.ReachedTick - pose.Tick)
                                            / float SimulationClock.defaultClock.SubTicksPerSecond
                                            / multiplier

                                        capturedAt - int64 (secondsBehind * float Stopwatch.Frequency)

                                    // Skipped poses would only be drawn as a burst; the newest keeps the viewer current.
                                    let published =
                                        if fastForward then
                                            advance.Poses |> Array.tryLast |> Option.toArray
                                        else
                                            advance.Poses

                                    published
                                    |> Array.iter (fun pose ->
                                        let timestamp = if fastForward then capturedAt else poseTimestamp pose

                                        presentation.Publish(pose.Tick, pose, timestamp) |> ignore)

                                    presentation.PublishEvents advance.Events |> ignore
                                    pendingEvents <- pendingEvents @ (Array.toList advance.Events)

                                    if pacing && not finished then
                                        let input =
                                            KeyMoments.inputOf
                                                session.World
                                                userSide
                                                nameOf
                                                advance.Events
                                                advance.ReachedTick

                                        let next, stop = KeyMoments.step highlight input director
                                        director <- next

                                        let wantsCruise =
                                            highlight <> HighlightFilter.All && next.Pace = KeyMoments.Cruise

                                        if wantsCruise <> cruising then
                                            cruising <- wantsCruise

                                            presentation.SetPace(
                                                (if cruising then KeyMoments.cruiseSpeedMultiplier else 1.0),
                                                Stopwatch.GetTimestamp()
                                            )

                                            if not paused then
                                                enqueue
                                                    dispatch
                                                    (StatusChanged(if cruising then Cruising else Live speed))

                                        match stop with
                                        | Some reason when not paused ->
                                            paused <- true
                                            presentation.SetPlayback(speed, true, Stopwatch.GetTimestamp())
                                            enqueue dispatch (StatusChanged(KeyMomentStop reason))
                                        | _ -> ()

                                    let flowBoundary = previousFlow <> advance.Flow
                                    let critical = advance.Events |> Array.exists criticalEvent

                                    match previousFlow, advance.Flow with
                                    | MatchFlow.HalfTime _, MatchFlow.HalfTime _
                                    | MatchFlow.ExtraTimeBreak _, MatchFlow.ExtraTimeBreak _ -> ()
                                    | _, MatchFlow.HalfTime _
                                    | _, MatchFlow.ExtraTimeBreak _ when source = LiveSession ->
                                        paused <- true
                                        presentation.SetPlayback(speed, true, Stopwatch.GetTimestamp())
                                        enqueue dispatch (StatusChanged HalfTime)
                                    | _ -> ()

                                    publishSemantic (flowBoundary || critical) advance.ReachedTick pendingEvents

                                    if
                                        flowBoundary
                                        && advance.Poses
                                           |> Array.tryLast
                                           |> Option.forall (fun pose -> pose.Tick <> advance.ReachedTick)
                                    then
                                        let boundary = Replay.capture session.World

                                        presentation.Publish(boundary.Tick, boundary, Stopwatch.GetTimestamp())
                                        |> ignore

                                    if session.World.State.Flow = MatchFlow.MatchEnded then
                                        concludeMatch advance.ReachedTick
                                    elif advance.ReachedTick >= MatchSession.maxTicks then
                                        enqueue
                                            dispatch
                                            (Failed(sprintf "%A" (TickLimitExceeded MatchSession.maxTicks)))

                                        finished <- true

                                    let multiplier =
                                        if fastForward then
                                            KeyMoments.cruiseSpeedMultiplier
                                        else
                                            match speed with
                                            | Normal -> 1.0
                                            | Fast -> 2.0
                                            | VeryFast -> 4.0

                                    // Rebase after backpressure: a stalled UI must not
                                    // trigger an unbounded catch-up burst on recovery.
                                    let seconds =
                                        float (advance.ReachedTick - current)
                                        / float SimulationClock.defaultClock.SubTicksPerSecond
                                        / multiplier

                                    nextAdvance <-
                                        Stopwatch.GetTimestamp() + int64 (seconds * float Stopwatch.Frequency)
        with
        | :? OperationCanceledException -> ()
        | _ when cancellation.IsCancellationRequested -> ()
        | error ->
            try
                enqueue dispatch (Failed error.Message)
            with :? OperationCanceledException ->
                ()

        observationRequests.Writer.TryComplete() |> ignore
        let mutable drainObservation = true

        while drainObservation do
            match observationRequests.Reader.TryRead() with
            | true, CaptureArchive completion -> completion.TrySetResult(Error "Match worker stopped.") |> ignore
            | true, SelectDetail _ -> ()
            | true, SelectUpcoming _ -> ()
            | _ -> drainObservation <- false

    member _.RuntimeId = runtimeId
    member _.MatchId = matchId
    member _.PresentationStream = presentation

    member _.Start(dispatch: Msg -> unit) =
        worker <- Some(Task.Run(fun () -> run dispatch))

    /// A drained queue re-arms the wake; the second read covers a publication written between the empty read and
    /// the re-arm, whose writer saw a wake still pending and so did not send one.
    member _.TryTake() : MatchRuntimePublication option =
        match queue.Reader.TryRead() with
        | true, publication -> Some publication
        | _ ->
            Volatile.Write(&wakePending, 0)

            match queue.Reader.TryRead() with
            | true, publication -> Some publication
            | _ -> None

    member _.Submit(command: LiveMatchCommand) =
        commandQueue.Writer.TryWrite command |> ignore

    member _.SelectObservation(selection: ObservationSelection option) =
        match selection with
        | Some value when value.FromTick < 0<subtick> || value.UntilTick < value.FromTick ->
            Error "Capture interval must be non-negative and ordered."
        | _ when worker |> Option.exists (fun task -> task.IsCompleted) -> Error "Match worker has finished."
        | _ ->
            if observationRequests.Writer.TryWrite(SelectDetail selection) then
                Ok()
            else
                Error "Diagnostic request queue is full."

    member _.CaptureObservation() =
        let completion =
            TaskCompletionSource<Result<MatchObservationCapture, string>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            )

        if worker |> Option.exists (fun task -> task.IsCompleted) then
            completion.SetResult(Error "Match worker has finished.")
        elif not (observationRequests.Writer.TryWrite(CaptureArchive completion)) then
            completion.SetResult(Error "Diagnostic request queue is full.")

        task {
            let! captured = completion.Task

            match captured with
            | Error error -> return Error error
            | Ok snapshot ->
                return!
                    Task.Run(fun () ->
                        try
                            Ok(MatchObservationArchive.materialize snapshot)
                        with error ->
                            Error error.Message)
        }

    member _.SelectUpcoming(actor: FootballEngine.Core.Match.PlayerId, duration: int<tickDelta>) =
        if duration <= 0<tickDelta> then
            Error "Capture duration must be positive."
        elif observationRequests.Writer.TryWrite(SelectUpcoming(actor, duration)) then
            Ok()
        else
            Error "Diagnostic request queue is unavailable or full."

    member _.Dispose() =
        if not cancellation.IsCancellationRequested then
            cancellation.Cancel()

        queue.Writer.TryComplete() |> ignore
        observationRequests.Writer.TryComplete() |> ignore
        let mutable drainObservation = true

        while drainObservation do
            match observationRequests.Reader.TryRead() with
            | true, CaptureArchive completion -> completion.TrySetResult(Error "Match runtime closed.") |> ignore
            | true, SelectDetail _ -> ()
            | true, SelectUpcoming _ -> ()
            | _ -> drainObservation <- false

        presentation.Close()

        match worker with
        | Some task when not task.IsCompleted ->
            try
                task.Wait(1000) |> ignore
            with _ ->
                ()
        | _ -> ()

        cancellation.Dispose()

type MatchRuntimeHost() =
    let syncRoot = obj ()
    let runtimes = System.Collections.Generic.Dictionary<MatchRuntimeId, MatchRuntime>()
    let mutable nextId = 0UL

    member this.CreateUserMatch
        (matchId: MatchId, gs: GameState, clock: WorldClock, saveOpt: SaveSettings option, runMode: MatchRunMode)
        =
        // Tests and tools that name no pacing want every minute, as before the key-moment pacing existed.
        this.CreateRuntime(
            matchId,
            gs,
            clock,
            saveOpt,
            runMode,
            LiveSession,
            0<subtick>,
            MatchSession.maxTicks,
            HighlightFilter.All
        )

    member _.CreateRuntime
        (
            matchId: MatchId,
            gs: GameState,
            clock: WorldClock,
            saveOpt: SaveSettings option,
            runMode: MatchRunMode,
            source: LiveMatchSource,
            startAt: int<subtick>,
            initialHold: int<subtick>,
            highlight: HighlightFilter
        ) =
        lock syncRoot (fun () ->
            nextId <- nextId + 1UL
            let runtimeId = MatchRuntimeId nextId

            let runtime =
                MatchRuntime(runtimeId, matchId, gs, clock, saveOpt, runMode, source, startAt, initialHold, highlight)

            runtimes.Add(runtimeId, runtime)
            runtimeId)

    member _.Start(runtimeId: MatchRuntimeId, dispatch: Msg -> unit) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> runtime.Start dispatch
            | _ -> ())

    member _.TryTake(runtimeId: MatchRuntimeId) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> runtime.TryTake()
            | _ -> None)

    member _.Submit(runtimeId: MatchRuntimeId, command: LiveMatchCommand) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> runtime.Submit command
            | _ -> ())

    member _.PresentationStream(runtimeId: MatchRuntimeId) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> Some runtime.PresentationStream
            | _ -> None)

    member _.SelectObservation(runtimeId: MatchRuntimeId, selection: ObservationSelection option) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> runtime.SelectObservation selection
            | _ -> Error "Match runtime is unavailable.")

    member _.CaptureObservation(runtimeId: MatchRuntimeId) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> runtime.CaptureObservation()
            | _ -> Task.FromResult(Error "Match runtime is unavailable."))

    member _.SelectUpcoming
        (runtimeId: MatchRuntimeId, actor: FootballEngine.Core.Match.PlayerId, duration: int<tickDelta>)
        =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime -> runtime.SelectUpcoming(actor, duration)
            | _ -> Error "Match runtime is unavailable.")

    member _.Close(runtimeId: MatchRuntimeId) =
        lock syncRoot (fun () ->
            match runtimes.TryGetValue runtimeId with
            | true, runtime ->
                runtimes.Remove runtimeId |> ignore
                runtime.Dispose()
            | _ -> ())

    member _.DisposeAll() =
        lock syncRoot (fun () ->
            let current = runtimes.Values |> Seq.toArray
            runtimes.Clear()

            for runtime in current do
                runtime.Dispose())

module MatchRuntime =
    let private host = MatchRuntimeHost()

    // Harness-only: the viewer-scenes capture starts the match held at a tick before any simulation runs. The
    // match is created by a navigation message the harness cannot pass arguments through, so the request is
    // armed here and consumed by the next live match; live play always starts at MatchSession.maxTicks.
    let mutable private pendingInitialHold: int<subtick> option = None

    let armInitialHold (tick: int<subtick>) =
        Volatile.Write(&pendingInitialHold, Some tick)

    let private takeInitialHold () =
        match Volatile.Read(&pendingInitialHold) with
        | Some tick ->
            Volatile.Write(&pendingInitialHold, None)
            tick
        | None -> MatchSession.maxTicks

    let createUserMatch matchId gs clock saveOpt highlight =
        host.CreateRuntime(
            matchId,
            gs,
            clock,
            saveOpt,
            MatchRunMode.Live LiveExecutionPolicy.defaultPolicy,
            LiveSession,
            0<subtick>,
            takeInitialHold (),
            highlight
        )

    /// A recording is re-simulated from kickoff; `startAt` is where the viewer lands first.
    let createReplay matchId gs clock saveOpt (startAt: int<subtick>) =
        host.CreateRuntime(
            matchId,
            gs,
            clock,
            saveOpt,
            MatchRunMode.Live LiveExecutionPolicy.defaultPolicy,
            RecordedReplay,
            startAt,
            takeInitialHold (),
            HighlightFilter.All
        )

    let start runtimeId dispatch = host.Start(runtimeId, dispatch)

    let tryTake runtimeId = host.TryTake runtimeId

    let submit runtimeId command = host.Submit(runtimeId, command)

    let selectObservation runtimeId selection =
        host.SelectObservation(runtimeId, selection)

    let captureObservation runtimeId = host.CaptureObservation runtimeId

    let selectUpcoming runtimeId actor duration =
        host.SelectUpcoming(runtimeId, actor, duration)

    let presentationStream runtimeId = host.PresentationStream runtimeId

    let close runtimeId = host.Close runtimeId

    let disposeAll () = host.DisposeAll()
