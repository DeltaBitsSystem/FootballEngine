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
        let hasUnplayed (comp: Competition) =
            comp.Fixtures |> Map.exists (fun _ f -> not f.Played)

        gs.Competitions
        |> Map.tryFindKey (fun _ comp ->
            match comp.Type, comp.Country with
            | NationalLeague(LeagueLevel 0, _), Some c when c = gs.PrimaryCountry -> true
            | _ -> false)
        |> Option.map (fun id ->
            not (hasUnplayed gs.Competitions[id])
            && gs.Competitions
               |> Map.forall (fun _ comp ->
                   match comp.Type with
                   | NationalLeague _ -> not (hasUnplayed comp)
                   | _ -> true))
        |> Option.defaultValue false

    let advanceDays (days: int) (clock: WorldClock) (gs: GameState) : DayResult =

        let rec loop remaining clock gs accumulated =
            if remaining = 0 then
                { GameState = gs
                  WorldClock = clock
                  PlayedMatches = accumulated
                  Logs = []
                  SeasonComplete = isSeasonComplete gs }
            else
                let gs1 =
                    { gs with
                        CurrentDate = gs.CurrentDate.AddDays(1.0) }

                let gs2, clock1 =
                    if remaining = 1 then
                        WorldScheduler.tickWith (Set.singleton Match) clock gs1
                    else
                        WorldScheduler.tick clock gs1

                let todayPlayed =
                    gs2.Competitions
                    |> Map.toSeq
                    |> Seq.collect (fun (_, comp) ->
                        comp.Fixtures
                        |> Map.toSeq
                        |> Seq.filter (fun (_, f) -> f.Played && f.ScheduledDate.Date = gs1.CurrentDate.Date))
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

    let simulateFullSeasonAndAdvance (clock: WorldClock) (gs: GameState) : Result<SeasonResult, SeasonError> =

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
                            match MatchPhase.simulateFixture id fixture gsReady with
                            | Some o -> Some o
                            | None -> None)
                        |> Array.fold
                            (fun (outs, errs) o ->
                                match o with
                                | Some x -> x :: outs, errs
                                | None -> outs, $"Could not line up fixture for date {date}" :: errs)
                            ([], [])

                    if errs.IsEmpty then
                        currentGs <- applyOutcomes (fixtureToCompMap gsReady) (Array.ofList outcomes) gsReady

                        currentGs <- { currentGs with CurrentDate = date }
                    else
                        errors <- errs

            match errors with
            | _ :: _ -> Error(SimulationErrors errors)
            | [] ->
                let summary = SeasonPhase.computeSeasonSummary currentGs
                let nextGs = SeasonPhase.runEndOfSeason currentGs
                let nextClock = WorldClockOps.init nextGs.Season

                Ok
                    { Summary = summary
                      SeasonFinalGs = currentGs
                      NextSeasonGs = nextGs
                      NextSeasonClock = nextClock }

    let advanceToNextSeason (clock: WorldClock) (gs: GameState) : SeasonResult =
        let summary = SeasonPhase.computeSeasonSummary gs
        let nextGs = SeasonPhase.runEndOfSeason gs
        let nextClock = WorldClockOps.init nextGs.Season

        { Summary = summary
          SeasonFinalGs = gs
          NextSeasonGs = nextGs
          NextSeasonClock = nextClock }

    let simulateUserMatch (clock: WorldClock) (gs: GameState) : Result<UserMatchResult, string> =

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

            match MatchContextBuilder.build gsReady fixture BalanceConfig.defaultConfig with
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

                    let outcome =
                        { FixtureId = fixtureId
                          Fixture =
                            { fixture with
                                Played = true
                                HomeScore = Some h
                                AwayScore = Some a
                                Events = result.Events }
                          HomeScore = h
                          AwayScore = a
                          InjuredPlayers = MatchContextBuilder.injuredOf result.Events
                          HomePlayerStats = [||]
                          AwayPlayerStats = [||] }

                    let finalGs = applyOutcomes (fixtureToCompMap gsReady) [| outcome |] gsReady

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

    let hasUserMatchToday (gs: GameState) : bool =
        gs.Competitions
        |> Map.exists (fun _ comp ->
            comp.Fixtures
            |> Map.exists (fun _ f ->
                f.ScheduledDate.Date = gs.CurrentDate.Date
                && MatchFixture.isPending f
                && MatchFixture.involves gs.UserClubId f))

    let runAiFixtures (clock: WorldClock) (gs: GameState) : DayResult =
        let nextGs =
            match MatchPhase.getAiFixturesToday gs with
            | [] -> gs
            | fixtures -> MatchPhase.runFixtures fixtures gs

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
