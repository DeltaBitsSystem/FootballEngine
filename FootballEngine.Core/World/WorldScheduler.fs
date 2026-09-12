namespace FootballEngine.World

open System
open FootballEngine.Domain
open FootballEngine.World.Phases
open FootballEngine.World.Supervisors
open FootballEngine.World.Agents

module WorldScheduler =

    type DayExecution =
        { State: GameState
          Clock: WorldClock
          Events: WorldEvent list
          Phases: WorldPhaseTrace list
          PlayedMatches: (MatchId * MatchFixture) list
          PausedMatch: MatchId option }

    type DayFailure =
        { Phase: WorldPhaseTag
          Message: string
          State: GameState
          Clock: WorldClock }

    let private trace phase date clockBefore clockAfter =
        { Phase = phase
          Date = date
          RevisionBefore = clockBefore.Revision
          RevisionAfter = clockAfter.Revision }

    let private runPhase phase date clock state work =
        try
            let nextState, nextClock = work clock state

            let nextClock =
                { nextClock with
                    Revision = nextClock.Revision + 1UL }

            Ok(nextState, nextClock, trace phase date clock nextClock)
        with ex ->
            Error
                { Phase = phase
                  Message = ex.Message
                  State = state
                  Clock = clock }

    let private allFixturesOn (date: DateTime) (state: GameState) =
        state.Competitions
        |> Map.toList
        |> List.collect (fun (_, competition) ->
            competition.Fixtures
            |> Map.toList
            |> List.filter (fun (_, fixture) -> fixture.ScheduledDate.Date = date.Date && not fixture.Played))
        |> List.sortBy fst

    let private disjointBatches fixtures =
        let rec add (remaining: (MatchId * MatchFixture) list) batches =
            match remaining with
            | [] -> batches |> List.map (fun (_, items) -> items |> List.sortBy fst)
            | (matchId, fixture) :: tail ->
                let clubs = Set [ fixture.HomeClubId; fixture.AwayClubId ]

                match
                    batches
                    |> List.tryFindIndex (fun (used, _) -> Set.isEmpty (Set.intersect used clubs))
                with
                | Some index ->
                    let updated =
                        batches
                        |> List.mapi (fun i (used, items) ->
                            if i = index then
                                used + clubs, (matchId, fixture) :: items
                            else
                                used, items)

                    add tail updated
                | None -> add tail ((clubs, [ matchId, fixture ]) :: batches)

        add fixtures []

    let private runAgents (balance: FootballEngine.Core.Match.BalanceConfig) (state: GameState) (clock: WorldClock) =
        let financeAgents =
            state.Clubs |> Map.toList |> List.map (fst >> ClubAgent.makeFinance)

        let youthAgents = state.Clubs |> Map.toList |> List.map (fst >> ClubAgent.makeYouth)

        let finance =
            AgentTypes.mkPhase financeAgents FinanceSupervisor.resolve Finance Daily

        let transfer = TransferPhase.makeWithBalance balance state
        let youth = AgentTypes.mkPhase youthAgents YouthSupervisor.resolve Youth Seasonal

        state |> transfer.Run clock |> finance.Run clock |> youth.Run clock

    let executeDayWithBalance
        (balance: FootballEngine.Core.Match.BalanceConfig, policy: PausePolicy, clock: WorldClock, state: GameState)
        : Result<DayExecution, DayFailure> =
        let date: DateTime = state.CurrentDate.AddDays(1.0)
        let candidate = { state with CurrentDate = date }
        let initial = { clock with PhaseHistory = [] }
        let run phase s c f = runPhase phase date c s f

        let result =
            run ExtractDueWork candidate initial (fun c s ->
                let due = WorldClockOps.dueWork date ExtractDueWork c
                s, WorldClockOps.consumeWork due c)
            |> Result.bind (fun (s, c, t1) ->
                run ApplyEffectiveRecords s c (fun c s ->
                    let due = WorldClockOps.dueWork date ApplyEffectiveRecords c
                    s, WorldClockOps.consumeWork due c)
                |> Result.map (fun (s2, c2, t2) -> s2, c2, [ t1; t2 ]))
            |> Result.bind (fun (s, c, traces) ->
                run PrepareDay s c (fun c s ->
                    if c.Current.Day % 7 = 0 then
                        TrainingPhase.makeWithBalance balance |> fun p -> p.Run c s, c
                    else
                        s, c)
                |> Result.map (fun (s2, c2, t) -> s2, c2, traces @ [ t ]))
            |> Result.bind (fun (s, c, traces) ->
                let fixtures = allFixturesOn date s

                let hasUserMatch =
                    fixtures |> List.exists (fun (_, f) -> MatchFixture.involves s.UserClubId f)

                if policy = PauseBeforeUserMatch && hasUserMatch then
                    Ok(s, c, traces, fixtures)
                else
                    run ResolveFixtures s c (fun c s ->
                        disjointBatches fixtures
                        |> List.fold (fun acc batch -> MatchPhase.runFixturesWithBalance balance batch acc) s
                        |> fun next -> next, c)
                    |> Result.map (fun (s2, c2, t) -> s2, c2, traces @ [ t ], fixtures))
            |> Result.bind (fun (s, c, traces, fixtures) ->
                run ConfirmResults s c (fun c s -> s, c)
                |> Result.bind (fun (s2, c2, t) ->
                    run SettleObligations s2 c2 (fun c s -> FinancePhase.make.Run c s, c)
                    |> Result.bind (fun (s3, c3, t2) ->
                        run ResolveDecisions s3 c3 (fun c s -> runAgents balance s c, c)
                        |> Result.bind (fun (s4, c4, t3) ->
                            run CloseDay s4 c4 (fun c s ->
                                let nextClock = WorldClockOps.advance c
                                s, nextClock)
                            |> Result.map (fun (s5, c5, t4) -> s5, c5, traces @ [ t; t2; t3; t4 ], fixtures)))))

        result
        |> Result.map (fun (nextState, nextClock, traces, fixtures) ->
            let events =
                fixtures
                |> List.choose (fun (id, fixture) ->
                    if fixture.Played then
                        Some
                            { Id = WorldEventId(uint64 id)
                              EffectiveDate = date
                              Sequence = uint64 id
                              Cause = None
                              Entities = [ string fixture.HomeClubId; string fixture.AwayClubId ]
                              Payload =
                                MatchConfirmed(
                                    id,
                                    fixture.HomeScore |> Option.defaultValue 0,
                                    fixture.AwayScore |> Option.defaultValue 0
                                ) }
                    else
                        None)

            let nextClock = { nextClock with PhaseHistory = traces }

            { State = nextState
              Clock = nextClock
              Events = events
              Phases = traces
              PlayedMatches =
                nextState.Competitions
                |> Map.toList
                |> List.collect (fun (_, comp) -> comp.Fixtures |> Map.toList)
                |> List.filter (fun (_, f) -> f.Played && f.ScheduledDate.Date = date.Date)
                |> List.sortBy fst
              PausedMatch =
                if policy = PauseBeforeUserMatch then
                    fixtures
                    |> List.tryFind (fun (_, fixture) ->
                        MatchFixture.involves nextState.UserClubId fixture && not fixture.Played)
                    |> Option.map fst
                else
                    None })

    let tickWithBalance balance exclude clock state =
        let policy =
            if Set.contains Match exclude then
                PauseBeforeUserMatch
            else
                RunThroughMatches

        match executeDayWithBalance (balance, policy, clock, state) with
        | Ok day -> day.State, day.Clock
        | Error failure -> failure.State, failure.Clock

    let tickWith exclude clock state =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave None

        tickWithBalance balance exclude clock state

    let tick clock state = tickWith Set.empty clock state

    let tickWithSave saveOpt exclude clock state =
        let balance =
            FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave saveOpt

        tickWithBalance balance exclude clock state

    let tickSave saveOpt clock state =
        tickWithSave saveOpt Set.empty clock state
