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
    | CommandAccepted of MatchCommandId * ApplicationPoint
    | CommandApplied of MatchCommandId * revision: uint64 * appliedAt: int<subtick>
    | CommandRejected of MatchCommandId * CommandRejection
    | StatusChanged of LiveMatchStatus
    | Finished of MatchJobResult
    | Failed of string

type private MatchRuntime
    (
        runtimeId: MatchRuntimeId,
        matchId: MatchId,
        gs: GameState,
        clock: WorldClock,
        saveOpt: SaveSettings option,
        runMode: MatchRunMode
    ) =
    let cancellation = new CancellationTokenSource()
    let commandQueue = Channel.CreateUnbounded<LiveMatchCommand>()

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

    let enqueue (dispatch: Msg -> unit) publication =
        queue.Writer.WriteAsync(publication, cancellation.Token).AsTask().GetAwaiter().GetResult()
        dispatch (Msg.LiveMsg(LiveMsg.RuntimePublicationAvailable runtimeId))

    let buildJob () : Result<MatchJob, string> =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt

        match runMode with
        | MatchRunMode.Replay _ -> prepareReplayJobWithBalance balance clock gs matchId
        | MatchRunMode.Fast
        | MatchRunMode.Live _ -> prepareUserMatchJobWithBalance balance clock gs matchId

    let compactResult (job: MatchJob) (outcome: MatchOutcome) : MatchJobResult = matchJobResult job outcome

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

            let name =
                gs.Clubs
                |> Map.tryFind clubId
                |> Option.map _.Name
                |> Option.defaultValue club.Name

            { ClubId = clubId
              Name = name
              ShortName = name
              PrimaryColor = 0u
              SecondaryColor = 0u }

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

                    let source =
                        match runMode with
                        | MatchRunMode.Replay _ -> RecordedReplay
                        | MatchRunMode.Fast
                        | MatchRunMode.Live _ -> LiveSession

                    enqueue dispatch (Connected(sessionId, fixtureContext, 0UL, source))

                    let sessionResult = MatchSession.create Scheduler.systems job.Input runMode

                    match sessionResult with
                    | Error error -> enqueue dispatch (Failed(sprintf "%A" error))
                    | Ok session ->
                        let ticksPerSlice =
                            match runMode with
                            | MatchRunMode.Live _ -> int policy.WorkerSlice
                            | MatchRunMode.Replay interval -> int interval
                            | MatchRunMode.Fast -> int LiveExecutionPolicy.defaultPolicy.WorkerSlice

                        let mutable speed = Normal
                        let mutable nextAdvance = Stopwatch.GetTimestamp()
                        let mutable finished = false
                        let mutable paused = false
                        let mutable accepted: LiveMatchCommand list = []
                        let mutable revision = 0UL
                        let mutable lastCommandId = 0UL
                        let mutable lastSemanticTick = 0<subtick>
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
                            let due = frontier - lastSemanticTick >= int policy.SemanticEvery * 1<subtick>
                            let scoreChanged = score <> lastSemanticScore

                            if force || due || scoreChanged then
                                enqueue
                                    dispatch
                                    (Semantic
                                        { RuntimeId = runtimeId
                                          Revision = revision
                                          Frontier = frontier
                                          Events = events |> List.toArray
                                          Clock = liveClock session
                                          Score = score
                                          Stats = session.World.Home.Stats, session.World.Away.Stats })

                                lastSemanticTick <- frontier
                                lastSemanticScore <- score
                                pendingEvents <- []

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
                                enqueue dispatch (StatusChanged(Live speed))
                            | SetLiveSpeed value ->
                                speed <- value
                                nextAdvance <- Stopwatch.GetTimestamp()
                                presentation.SetPlayback(value, paused, Stopwatch.GetTimestamp())
                                acceptImmediate command
                                enqueue dispatch (StatusChanged(if paused then Paused else Live speed))
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

                        while not finished && not cancellation.IsCancellationRequested do
                            drainCommands ()

                            if paused || Stopwatch.GetTimestamp() < nextAdvance then
                                cancellation.Token.WaitHandle.WaitOne 5 |> ignore
                            else
                                let current = session.World.State.Tick
                                let previousFlow = session.World.State.Flow
                                let target = min MatchSession.maxTicks (current + ticksPerSlice * 1<subtick>)

                                match MatchSession.advanceTo target session with
                                | Error error ->
                                    enqueue dispatch (Failed(sprintf "%A" error))
                                    finished <- true
                                | Ok advance ->
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

                                    advance.Poses
                                    |> Array.iter (fun pose ->
                                        presentation.Publish(pose.Tick, pose, poseTimestamp pose) |> ignore)

                                    presentation.PublishEvents advance.Events |> ignore
                                    pendingEvents <- pendingEvents @ (Array.toList advance.Events)
                                    let flowBoundary = previousFlow <> advance.Flow
                                    let critical = advance.Events |> Array.exists criticalEvent

                                    match previousFlow, advance.Flow with
                                    | MatchFlow.HalfTime _, MatchFlow.HalfTime _
                                    | MatchFlow.ExtraTimeBreak _, MatchFlow.ExtraTimeBreak _ -> ()
                                    | _, MatchFlow.HalfTime _
                                    | _, MatchFlow.ExtraTimeBreak _ ->
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
                                        publishSemantic true advance.ReachedTick pendingEvents

                                        match MatchSession.finish session with
                                        | Error error -> enqueue dispatch (Failed(sprintf "%A" error))
                                        | Ok outcome ->
                                            let result = compactResult job outcome
                                            enqueue dispatch (Finished result)

                                        finished <- true
                                    elif advance.ReachedTick >= MatchSession.maxTicks then
                                        enqueue
                                            dispatch
                                            (Failed(sprintf "%A" (TickLimitExceeded MatchSession.maxTicks)))

                                        finished <- true

                                    let multiplier =
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

    member _.RuntimeId = runtimeId
    member _.MatchId = matchId
    member _.PresentationStream = presentation

    member _.Start(dispatch: Msg -> unit) =
        worker <- Some(Task.Run(fun () -> run dispatch))

    member _.TryTake() : MatchRuntimePublication option =
        match queue.Reader.TryRead() with
        | true, publication -> Some publication
        | _ -> None

    member _.Submit(command: LiveMatchCommand) =
        commandQueue.Writer.TryWrite command |> ignore

    member _.Dispose() =
        if not cancellation.IsCancellationRequested then
            cancellation.Cancel()

        queue.Writer.TryComplete() |> ignore
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

    member _.CreateUserMatch
        (matchId: MatchId, gs: GameState, clock: WorldClock, saveOpt: SaveSettings option, runMode: MatchRunMode)
        =
        lock syncRoot (fun () ->
            nextId <- nextId + 1UL
            let runtimeId = MatchRuntimeId nextId
            let runtime = MatchRuntime(runtimeId, matchId, gs, clock, saveOpt, runMode)
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

    let createUserMatch matchId gs clock saveOpt =
        host.CreateUserMatch(matchId, gs, clock, saveOpt, MatchRunMode.Live LiveExecutionPolicy.defaultPolicy)

    let createReplay matchId gs clock saveOpt =
        host.CreateUserMatch(matchId, gs, clock, saveOpt, MatchRunMode.Replay 20<tickDelta>)

    let start runtimeId dispatch = host.Start(runtimeId, dispatch)

    let tryTake runtimeId = host.TryTake runtimeId

    let submit runtimeId command = host.Submit(runtimeId, command)

    let presentationStream runtimeId = host.PresentationStream runtimeId

    let close runtimeId = host.Close runtimeId

    let disposeAll () = host.DisposeAll()
