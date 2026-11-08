namespace FootballEngine.World


open FootballEngine.Core.Match
open FootballEngine.World.Phases
open MatchOutcome
open FootballEngine.Domain

module WorldRunner =

    type DayResult =
        { GameState: GameState
          WorldClock: WorldClock
          PlayedMatches: (MatchId * MatchFixture) list
          Logs: string list
          SeasonComplete: bool }

    type SeasonResult =
        { Summary: string list
          SeasonFinalGs: GameState
          NextSeasonGs: GameState
          NextSeasonClock: WorldClock }

    type SeasonError =
        | NoFixturesToPlay
        | SimulationErrors of string list

    type UserMatchResult =
        { DayResult: DayResult
          MatchReplay: Replay.ReplayData option }

    let private parallelMapLimited (f: 'T -> 'U) (arr: 'T[]) : 'U[] =
        let maxThreads = max 1 (System.Environment.ProcessorCount - 1)

        let options =
            System.Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = maxThreads)

        let results = Array.zeroCreate arr.Length

        System.Threading.Tasks.Parallel.For(0, arr.Length, options, fun i -> results[i] <- f arr[i])
        |> ignore

        results

    let private isSeasonComplete (gs: GameState) =
        let relevant =
            gs.Competitions
            |> Map.values
            |> Seq.filter (fun comp -> comp.Season = gs.Season)
            |> Seq.toList

        not relevant.IsEmpty && relevant |> List.forall Competition.isComplete

    let advanceDaysWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (days: int)
        (clock: WorldClock)
        (gs: GameState)
        : DayResult =

        let rec loop remaining clock gs accumulated =
            if remaining = 0 then
                { GameState = gs
                  WorldClock = clock
                  PlayedMatches = accumulated
                  Logs = []
                  SeasonComplete = isSeasonComplete gs }
            else
                let gs2, clock1 =
                    if remaining = 1 then
                        WorldScheduler.tickWithBalance balance (Set.singleton Match) clock gs
                    else
                        WorldScheduler.tickWithBalance balance Set.empty clock gs

                let todayPlayed =
                    gs2.Competitions
                    |> Map.toSeq
                    |> Seq.collect (fun (_, comp) ->
                        comp.Fixtures
                        |> Map.toSeq
                        |> Seq.filter (fun (_, f) -> f.Played && f.ScheduledDate.Date = gs2.CurrentDate.Date))
                    |> List.ofSeq

                if isSeasonComplete gs2 then
                    { GameState = gs2
                      WorldClock = clock1
                      PlayedMatches = accumulated @ todayPlayed
                      Logs = []
                      SeasonComplete = true }
                else
                    loop (remaining - 1) clock1 gs2 (accumulated @ todayPlayed)

        loop days clock gs []

    let advanceDays (days: int) (clock: WorldClock) (gs: GameState) : DayResult =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave None

        advanceDaysWithBalance balance days clock gs

    let advanceDaysWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (days: int)
        (clock: WorldClock)
        (gs: GameState)
        : DayResult =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        advanceDaysWithBalance balance days clock gs

    /// Contract adapter used by callers that advance by a logical date range.
    /// It delegates to the established daily runner so all phases keep one
    /// ordering and one source of truth.
    let advanceWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (request: AdvanceRequest)
        (clock: WorldClock)
        (state: WorldState)
        : AdvanceResult =
        if request.ExpectedRevision <> clock.Revision then
            { State = state
              ConfirmedRevision = clock.Revision
              Events = []
              StoppedBecause = InvalidRequest "World revision is stale" }
        elif request.UntilDate < request.FromDate then
            { State = state
              ConfirmedRevision = clock.Revision
              Events = []
              StoppedBecause = InvalidRequest "AdvanceRequest.UntilDate must be >= FromDate" }
        elif request.CancellationRequested then
            { State = state
              ConfirmedRevision = clock.Revision
              Events = []
              StoppedBecause = Cancelled }
        else
            let rec loop
                (currentState: GameState)
                (currentClock: WorldClock)
                (events: WorldEvent list)
                (remaining: int)
                =
                if request.CancellationRequested then
                    { State = currentState
                      ConfirmedRevision = currentClock.Revision
                      Events = events
                      StoppedBecause = Cancelled }
                elif remaining <= 0 then
                    { State = currentState
                      ConfirmedRevision = currentClock.Revision
                      Events = events
                      StoppedBecause = ReachedLimit }
                else
                    match
                        WorldScheduler.executeDayWithBalance (balance, request.PausePolicy, currentClock, currentState)
                    with
                    | Error failure ->
                        { State = failure.State
                          ConfirmedRevision = failure.Clock.Revision
                          Events = events
                          StoppedBecause = Failed(string failure.Phase, failure.Message) }
                    | Ok day ->
                        let dayEvent =
                            { Id = WorldEventId(day.Clock.Revision)
                              EffectiveDate = day.State.CurrentDate
                              Sequence = day.Clock.Revision
                              Cause = None
                              Entities = []
                              Payload = WorldAdvanced(currentState.CurrentDate, day.State.CurrentDate) }

                        let allEvents = events @ day.Events @ [ dayEvent ]

                        match request.PausePolicy, day.PausedMatch with
                        | PauseBeforeUserMatch, Some matchId ->
                            { State = day.State
                              ConfirmedRevision = day.Clock.Revision
                              Events = allEvents
                              StoppedBecause = PausedForMatch matchId }
                        | _ -> loop day.State day.Clock allEvents (remaining - 1)

            let days = int (request.UntilDate - request.FromDate).TotalDays
            loop state clock [] days

    let applyCommand
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (command: WorldCommand)
        (clock: WorldClock)
        (state: WorldState)
        : Result<AdvanceResult, string> =
        match WorldCommand.validate command with
        | Error error -> Error error
        | Ok() ->
            match command.Action with
            | AdvanceDays days ->
                let request =
                    { FromDate = state.CurrentDate
                      UntilDate = state.CurrentDate.AddDays(float days)
                      PausePolicy = RunThroughMatches
                      ExpectedRevision = command.ExpectedRevision
                      CancellationRequested = false }

                Ok(advanceWithBalance balance request clock state)
            | ConfirmMatch _
            | ChangeBalance _ -> Error "WorldCommand action requires its owning phase"

    let simulateFullSeasonAndAdvanceWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        : Result<SeasonResult, SeasonError> =

        let fixturesByDate =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq |> Seq.filter (fun (_, f) -> not f.Played))
            |> Seq.groupBy (fun (_, f) -> f.ScheduledDate.Date)
            |> Seq.map (fun (date, fixtures) -> date, List.ofSeq fixtures)
            |> Map.ofSeq

        if fixturesByDate.IsEmpty then
            Error NoFixturesToPlay
        else
            let sortedDates = fixturesByDate |> Map.keys |> Seq.sort |> Array.ofSeq
            let mutable currentGs = gs
            let mutable executions = clock.MatchExecutions
            let mutable errors: string list = []

            for date in sortedDates do
                if errors.IsEmpty then
                    let dayFixtures = fixturesByDate[date]
                    let gsReady = LineupOps.ensureForFixtures dayFixtures currentGs

                    let outcomes, errs =
                        dayFixtures
                        |> Array.ofList
                        |> parallelMapLimited (fun (id, fixture) ->
                            match MatchPhase.simulateFixtureExecutionWithBalance balance id fixture gsReady with
                            | Some(o, input) -> Some(o, input)
                            | None -> None)
                        |> Array.fold
                            (fun (outs, errs) o ->
                                match o with
                                | Some x -> x :: outs, errs
                                | None -> outs, $"Could not line up fixture for date {date}" :: errs)
                            ([], [])

                    if errs.IsEmpty then
                        for outcome, input in outcomes do
                            if MatchFixture.involves gs.UserClubId outcome.Fixture then
                                executions <- executions |> Map.add outcome.FixtureId (MatchInput.snapshot input, [])

                        let outcomes = outcomes |> List.map fst
                        currentGs <-
                            applyPostMatchLoad balance.Training (Array.ofList outcomes) gsReady
                            |> applyOutcomes balance.CollectiveAttack (fixtureToCompMap gsReady) (Array.ofList outcomes)

                        currentGs <- { currentGs with CurrentDate = date }
                    else
                        errors <- errs

            match errors with
            | _ :: _ -> Error(SimulationErrors errors)
            | [] ->
                let summary = SeasonPhase.computeSeasonSummary currentGs
                let nextGs = SeasonPhase.runEndOfSeason balance currentGs
                let nextClock =
                    { WorldClockOps.init nextGs.Season with MatchExecutions = executions }

                Ok
                    { Summary = summary
                      SeasonFinalGs = currentGs
                      NextSeasonGs = nextGs
                      NextSeasonClock = nextClock }

    let simulateFullSeasonAndAdvance (clock: WorldClock) (gs: GameState) : Result<SeasonResult, SeasonError> =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave None

        simulateFullSeasonAndAdvanceWithBalance balance clock gs

    let simulateFullSeasonAndAdvanceWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (clock: WorldClock)
        (gs: GameState)
        : Result<SeasonResult, SeasonError> =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        simulateFullSeasonAndAdvanceWithBalance balance clock gs

    let advanceToNextSeasonWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        : SeasonResult =
        let summary = SeasonPhase.computeSeasonSummary gs
        // A new season issues new fixtures; carried-over ids could make the pulse skip a result.
        let rolled = { SeasonPhase.runEndOfSeason balance gs with PulseProcessed = Set.empty }

        // The ceremony reads the finished season, so it needs `gs`; it runs after the roll only to publish onto the
        // new state, whose retirements it must not displace.
        let nextGs =
            FootballEngine.World.Pulse.PlayerStories.seasonRoll WorldPulseConfig.defaultWorldPulse gs rolled
            |> FootballEngine.World.Pulse.SeasonCeremony.apply WorldPulseConfig.defaultWorldPulse gs

        let nextClock =
            { WorldClockOps.init nextGs.Season with MatchExecutions = clock.MatchExecutions }

        { Summary = summary
          SeasonFinalGs = gs
          NextSeasonGs = nextGs
          NextSeasonClock = nextClock }

    let advanceToNextSeason (clock: WorldClock) (gs: GameState) : SeasonResult =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave None

        advanceToNextSeasonWithBalance balance clock gs

    let advanceToNextSeasonWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (clock: WorldClock)
        (gs: GameState)
        : SeasonResult =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        advanceToNextSeasonWithBalance balance clock gs

    let prepareUserMatchJobWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        (matchId: MatchId)
        : Result<MatchJob, string> =
        let fixtureOpt =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
            |> Seq.tryFind (fun (fixtureId, fixture) ->
                fixtureId = matchId
                && fixture.ScheduledDate.Date = gs.CurrentDate.Date
                && MatchFixture.isPending fixture
                && MatchFixture.involves gs.UserClubId fixture)

        match fixtureOpt with
        | None -> Error "No pending user match today"
        | Some(fixtureId, fixture) ->
            let gsReady =
                gs
                |> LineupOps.ensureForClub fixture.HomeClubId
                |> LineupOps.ensureForClub fixture.AwayClubId

            match EffectiveConfig.fromBalance balance, MatchContextBuilder.build gsReady fixture balance with
            | Error error, _ -> Error(sprintf "Invalid match configuration: %A" error)
            | _, None -> Error "Match simulation failed: missing lineup"
            | Ok effective, Some context ->
                let input =
                    { MatchId = uint64 fixtureId
                      Context = context
                      Seed = MatchContextBuilder.seedOf gs.ExecutionSeed fixtureId
                      EffectiveConfig = effective
                      EngineVersion = "football-engine-core-01" }

                Ok(MatchJob.create fixtureId fixture.ScheduledDate clock.Revision input)

    let prepareReplayJob
        (clock: WorldClock)
        (gs: GameState)
        (matchId: MatchId)
        : Result<MatchJob, string> =
        match Map.tryFind matchId clock.MatchExecutions with
        | None -> Error "The requested match has no recorded execution"
        | Some(input, _) ->
            let fixtureOpt =
                gs.Competitions
                |> Map.toSeq
                |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
                |> Seq.tryFind (fun (fixtureId, fixture) ->
                    fixtureId = matchId && MatchFixture.isPlayed fixture && MatchFixture.involves gs.UserClubId fixture)

            match fixtureOpt with
            | None -> Error "The requested replay fixture could not be found"
            | Some(_, fixture) ->
                Ok(MatchJob.create matchId fixture.ScheduledDate clock.Revision input)

    let matchJobResult (job: MatchJob) (commands: MatchCommand list) (outcome: MatchOutcome) : MatchJobResult =
        let homePlayerStats, awayPlayerStats =
            MatchOutcome.extractPlayerStatsFromWorld job.Input.Context outcome.Final outcome.Events

        { Input = job.Input
          Commands = commands
          MatchId = job.MatchId
          SourceRevision = job.SourceRevision
          InputHash = job.InputHash
          ResultHash = MatchJob.resultHash job outcome
          HomeScore = outcome.Final.State.HomeScore
          AwayScore = outcome.Final.State.AwayScore
          EventsHash = outcome.EventsHash
          Events = outcome.Events
          InjuredPlayers = MatchContextBuilder.injuredOf outcome.Events |> Set.toList
          HomePlayerStats = homePlayerStats
          AwayPlayerStats = awayPlayerStats
          Metrics = outcome.Final.Home.Stats, outcome.Final.Away.Stats
          Replay = outcome.Replay }

    /// Applies a completed match job without re-running the match. The job
    /// result is deliberately World-free; every world-side consequence is
    /// committed once, after revision, fixture and input-hash validation.
    let commitUserMatchJobWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        (result: MatchJobResult)
        : Result<UserMatchResult, string> =
        if clock.Revision <> result.SourceRevision then
            Error "Match result is stale for the current world revision"
        else
            let fixtureOpt =
                gs.Competitions
                |> Map.toSeq
                |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
                |> Seq.tryFind (fun (fixtureId, fixture) -> fixtureId = result.MatchId)

            match fixtureOpt with
            | None -> Error "Match result references an unknown fixture"
            | Some(fixtureId, fixture) when MatchFixture.isPlayed fixture -> Error "Match result was already committed"
            | Some(fixtureId, fixture) ->
                let gsReady =
                    gs
                    |> LineupOps.ensureForClub fixture.HomeClubId
                    |> LineupOps.ensureForClub fixture.AwayClubId

                match EffectiveConfig.fromBalance balance, MatchContextBuilder.build gsReady fixture balance with
                | Error error, _ -> Error(sprintf "Invalid match configuration: %A" error)
                | _, None -> Error "Match result cannot be committed because the lineup is unavailable"
                | Ok effective, Some ctx ->
                    let input =
                        { MatchId = uint64 fixtureId
                          Context = ctx
                          Seed = MatchContextBuilder.seedOf gs.ExecutionSeed fixtureId
                          EffectiveConfig = effective
                          EngineVersion = "football-engine-core-01" }

                    let job =
                        MatchJob.create fixtureId fixture.ScheduledDate result.SourceRevision input

                    let recordedJob = MatchJob.create fixtureId fixture.ScheduledDate result.SourceRevision result.Input

                    if recordedJob.InputHash <> result.InputHash || job.InputHash <> result.InputHash then
                        Error "Match result input hash does not match the current fixture input"
                    elif
                        MatchJob.resultHashFromFacts job result.HomeScore result.AwayScore result.EventsHash
                        <> result.ResultHash
                    then
                        Error "Match result hash is invalid"
                    elif MatchOutcomeHash.eventsHash result.Events <> result.EventsHash then
                        Error "Match result event hash is invalid"
                    else
                        let playedFixture =
                            { fixture with
                                Played = true
                                HomeScore = Some result.HomeScore
                                AwayScore = Some result.AwayScore
                                Events = result.Events }

                        let outcome =
                            { FixtureId = fixtureId
                              Fixture = playedFixture
                              HomeScore = result.HomeScore
                              AwayScore = result.AwayScore
                              InjuredPlayers = result.InjuredPlayers |> Set.ofList
                              HomePlayerStats = result.HomePlayerStats
                              AwayPlayerStats = result.AwayPlayerStats }

                        let finalGs =
                            applyPostMatchLoad balance.Training [| outcome |] gsReady
                            |> applyOutcomes balance.CollectiveAttack (fixtureToCompMap gsReady) [| outcome |]

                        let clubOfPlayer =
                            fun pid -> gsReady.Players |> Map.tryFind pid |> Option.bind Player.clubOf

                        let metrics =
                            MatchMetrics.extract result.Events fixture.HomeClubId fixture.AwayClubId clubOfPlayer

                        let updateCoord (clubId: ClubId) (isHome: bool) (state: GameState) : GameState =
                            let passRate =
                                if isHome then
                                    metrics.PassAccuracyHome
                                else
                                    metrics.PassAccuracyAway

                            let club = state.Clubs[clubId]
                            let injuredCount = result.InjuredPlayers |> Set.ofList |> Set.count
                            let decay = System.Math.Clamp(1.0 - 0.05 * float injuredCount, 0.5, 1.0)

                            let updatedCoordination =
                                { club.CoordinationMemory with
                                    PressingCoordination =
                                        System.Math.Clamp(
                                            (club.CoordinationMemory.PressingCoordination + (passRate - 0.5) * 0.1)
                                            * decay,
                                            0.5,
                                            1.5
                                        ) }

                            { state with
                                Clubs =
                                    state.Clubs
                                    |> Map.add
                                        clubId
                                        { club with
                                            CoordinationMemory = updatedCoordination } }

                        let clubOfEnginePlayer (PlayerId n) =
                            gsReady.Players |> Map.tryFind n |> Option.bind Player.clubOf

                        let updateScouting (clubId: ClubId) (isHome: bool) (state: GameState) : GameState =
                            let rivalId = if isHome then fixture.AwayClubId else fixture.HomeClubId

                            let rivalSignature =
                                match GameState.headCoach rivalId gsReady with
                                | Some coach ->
                                    match coach.Attributes.Coaching.Lineup with
                                    | Some lu ->
                                        (string lu.Formation,
                                         lu.Instructions |> Option.defaultValue TacticalInstructions.defaultInstructions)
                                    | None -> "", TacticalInstructions.defaultInstructions
                                | None -> "", TacticalInstructions.defaultInstructions

                            let club = state.Clubs[clubId]

                            let updatedMemory =
                                MatchPhase.recordPattern
                                    ctx.Config.Scout
                                    gsReady.CurrentDate
                                    fixture.HomeClubId
                                    fixture.AwayClubId
                                    rivalId
                                    rivalSignature
                                    clubOfEnginePlayer
                                    result.Events
                                    club.ScoutingMemory
                                |> ScoutingMemory.prune ctx.Config.Scout gsReady.CurrentDate

                            { state with
                                Clubs =
                                    state.Clubs
                                    |> Map.add
                                        clubId
                                        { club with
                                            ScoutingMemory = updatedMemory } }

                        let committedGs =
                            finalGs
                            |> updateCoord fixture.HomeClubId true
                            |> updateCoord fixture.AwayClubId false
                            |> updateScouting fixture.HomeClubId true
                            |> updateScouting fixture.AwayClubId false

                        let clock1 =
                            { WorldClockOps.advance clock with
                                MatchExecutions = clock.MatchExecutions |> Map.add fixtureId (MatchInput.snapshot result.Input, result.Commands) }

                        Ok
                            { DayResult =
                                { GameState = committedGs
                                  WorldClock = clock1
                                  PlayedMatches = [ fixtureId, playedFixture ]
                                  Logs =
                                    [ $"{gsReady.Clubs[fixture.HomeClubId].Name} {result.HomeScore}-{result.AwayScore} {gsReady.Clubs[fixture.AwayClubId].Name}" ]
                                  SeasonComplete = isSeasonComplete committedGs }
                              MatchReplay = result.Replay }

    let simulateUserMatchWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        : Result<UserMatchResult, string> =
        let matchIdOpt =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
            |> Seq.tryPick (fun (fixtureId, fixture) ->
                if
                    fixture.ScheduledDate.Date = gs.CurrentDate.Date
                    && MatchFixture.isPending fixture
                    && MatchFixture.involves gs.UserClubId fixture
                then
                    Some fixtureId
                else
                    None)

        match matchIdOpt with
        | None -> Error "No user match today"
        | Some matchId ->
            match prepareUserMatchJobWithBalance balance clock gs matchId with
            | Error error -> Error error
            | Ok job ->
                match MatchSession.create Scheduler.systems job.Input (MatchRunMode.Replay 20<tickDelta>) with
                | Error error -> Error(sprintf "%A" error)
                | Ok session ->
                    match MatchSession.finish session with
                    | Error error -> Error(sprintf "%A" error)
                    | Ok outcome ->
                        match commitUserMatchJobWithBalance balance clock gs (matchJobResult job session.Commands outcome) with
                        | Error error -> Error error
                        | Ok result -> Ok result

    let simulateUserMatch (clock: WorldClock) (gs: GameState) : Result<UserMatchResult, string> =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave None

        simulateUserMatchWithBalance balance clock gs

    let simulateUserMatchWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (clock: WorldClock)
        (gs: GameState)
        : Result<UserMatchResult, string> =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        simulateUserMatchWithBalance balance clock gs

    let hasUserMatchToday (gs: GameState) : bool =
        gs.Competitions
        |> Map.exists (fun _ comp ->
            comp.Fixtures
            |> Map.exists (fun _ f ->
                f.ScheduledDate.Date = gs.CurrentDate.Date
                && MatchFixture.isPending f
                && MatchFixture.involves gs.UserClubId f))

    let runAiFixturesWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        : DayResult =
        let nextGs =
            match MatchPhase.getAiFixturesToday gs with
            | [] -> gs
            | fixtures -> MatchPhase.runFixturesWithBalance balance fixtures gs

        let todayPlayed =
            nextGs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, comp) ->
                comp.Fixtures
                |> Map.toSeq
                |> Seq.filter (fun (_, f) -> f.Played && f.ScheduledDate.Date = gs.CurrentDate.Date))
            |> List.ofSeq

        { GameState = nextGs
          WorldClock = WorldClockOps.advance clock
          PlayedMatches = todayPlayed
          Logs = []
          SeasonComplete = isSeasonComplete nextGs }

    let runAiFixtures (clock: WorldClock) (gs: GameState) : DayResult =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave None

        runAiFixturesWithBalance balance clock gs

    let runAiFixturesWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (clock: WorldClock)
        (gs: GameState)
        : DayResult =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        runAiFixturesWithBalance balance clock gs
