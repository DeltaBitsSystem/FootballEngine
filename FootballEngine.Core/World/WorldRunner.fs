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
            let mutable errors: string list = []

            for date in sortedDates do
                if errors.IsEmpty then
                    let dayFixtures = fixturesByDate[date]
                    let gsReady = LineupOps.ensureForFixtures dayFixtures currentGs

                    let outcomes, errs =
                        dayFixtures
                        |> Array.ofList
                        |> parallelMapLimited (fun (id, fixture) ->
                            match MatchPhase.simulateFixtureWithBalance balance id fixture gsReady with
                            | Some o -> Some o
                            | None -> None)
                        |> Array.fold
                            (fun (outs, errs) o ->
                                match o with
                                | Some x -> x :: outs, errs
                                | None -> outs, $"Could not line up fixture for date {date}" :: errs)
                            ([], [])

                    if errs.IsEmpty then
                        currentGs <-
                            applyPostMatchLoad balance.Training (Array.ofList outcomes) gsReady
                            |> applyOutcomes (fixtureToCompMap gsReady) (Array.ofList outcomes)

                        currentGs <- { currentGs with CurrentDate = date }
                    else
                        errors <- errs

            match errors with
            | _ :: _ -> Error(SimulationErrors errors)
            | [] ->
                let summary = SeasonPhase.computeSeasonSummary currentGs
                let nextGs = SeasonPhase.runEndOfSeason balance currentGs
                let nextClock = WorldClockOps.init nextGs.Season

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
        let nextGs = SeasonPhase.runEndOfSeason balance gs
        let nextClock = WorldClockOps.init nextGs.Season

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

    let simulateUserMatchWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig)
        (clock: WorldClock)
        (gs: GameState)
        : Result<UserMatchResult, string> =

        let userFixture =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
            |> Seq.tryFind (fun (_, f) ->
                f.ScheduledDate.Date = gs.CurrentDate.Date
                && MatchFixture.isPending f
                && MatchFixture.involves gs.UserClubId f)

        match userFixture with
        | None -> Error "No user match today"
        | Some(fixtureId, fixture) ->
            let gsReady =
                gs
                |> LineupOps.ensureForClub fixture.HomeClubId
                |> LineupOps.ensureForClub fixture.AwayClubId

            match MatchContextBuilder.build gsReady fixture balance with
            | None -> Error "Match simulation failed: missing lineup"
            | Some ctx ->
                let result =
                    Simulator.runMatch
                        Scheduler.systems
                        ctx
                        (MatchContextBuilder.seedOf fixtureId)
                        (Simulator.RunMode.Replay 20<tickDelta>)
                        None

                match result.Replay with
                | None -> Error "Match simulation failed: replay unavailable"
                | Some replay ->
                    let h = result.Final.State.HomeScore
                    let a = result.Final.State.AwayScore

                    let homePlayerStats, awayPlayerStats =
                        MatchOutcome.extractPlayerStatsFromWorld ctx result.Final result.Events

                    let playedFixture =
                        { fixture with
                            Played = true
                            HomeScore = Some h
                            AwayScore = Some a
                            Events = result.Events }

                    let outcome =
                        { FixtureId = fixtureId
                          Fixture = playedFixture
                          HomeScore = h
                          AwayScore = a
                          InjuredPlayers = MatchContextBuilder.injuredOf result.Events
                          HomePlayerStats = homePlayerStats
                          AwayPlayerStats = awayPlayerStats }

                    let finalGs =
                        applyPostMatchLoad balance.Training [| outcome |] gsReady
                        |> applyOutcomes (fixtureToCompMap gsReady) [| outcome |]

                    let clubOfPlayer =
                        fun pid -> gsReady.Players |> Map.tryFind pid |> Option.bind Player.clubOf

                    let metrics =
                        MatchMetrics.extract result.Events fixture.HomeClubId fixture.AwayClubId clubOfPlayer

                    let updateCoord (clubId: ClubId) (isHome: bool) (gs: GameState) : GameState =
                        let passRate =
                            if isHome then
                                metrics.PassAccuracyHome
                            else
                                metrics.PassAccuracyAway

                        let club = gs.Clubs[clubId]
                        let injuredCount = outcome.InjuredPlayers |> Set.count
                        let decay = System.Math.Clamp(1.0 - 0.05 * float injuredCount, 0.5, 1.0)

                        let updatedCoord =
                            { club.CoordinationMemory with
                                PressingCoordination =
                                    System.Math.Clamp(
                                        (club.CoordinationMemory.PressingCoordination + (passRate - 0.5) * 0.1) * decay,
                                        0.5,
                                        1.5
                                    ) }

                        { gs with
                            Clubs =
                                gs.Clubs
                                |> Map.add
                                    clubId
                                    { club with
                                        CoordinationMemory = updatedCoord } }

                    let gsWithCoord =
                        finalGs
                        |> updateCoord fixture.HomeClubId true
                        |> updateCoord fixture.AwayClubId false

                    // Engine player ids are the domain ids (Projection.toProfile),
                    // so the scouting path unwraps them to index the domain map.
                    let clubOfEnginePlayer (PlayerId n) =
                        gsReady.Players |> Map.tryFind n |> Option.bind Player.clubOf

                    let updateScouting (clubId: ClubId) (isHome: bool) (gs: GameState) : GameState =
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

                        let club = gs.Clubs[clubId]

                        let updatedMem =
                            MatchPhase.recordPattern
                                ctx.Config.Scout
                                gsReady.CurrentDate
                                fixture.HomeClubId
                                fixture.AwayClubId
                                rivalId
                                rivalSignature
                                clubOfEnginePlayer
                                outcome.Fixture.Events
                                club.ScoutingMemory
                            // Rivals unseen for months stop costing save space.
                            |> ScoutingMemory.prune ctx.Config.Scout gsReady.CurrentDate

                        { gs with
                            Clubs =
                                gs.Clubs
                                |> Map.add
                                    clubId
                                    { club with
                                        ScoutingMemory = updatedMem } }

                    let gsWithScouting =
                        gsWithCoord
                        |> updateScouting fixture.HomeClubId true
                        |> updateScouting fixture.AwayClubId false

                    let clock1 = WorldClockOps.advance clock

                    Ok
                        { DayResult =
                            { GameState = gsWithScouting
                              WorldClock = clock1
                              PlayedMatches = [ fixtureId, outcome.Fixture ]
                              Logs =
                                [ $"{gsReady.Clubs[fixture.HomeClubId].Name} {h}-{a} {gsReady.Clubs[fixture.AwayClubId].Name}" ]
                              SeasonComplete = isSeasonComplete gsWithScouting }
                          MatchReplay = Some replay }

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
