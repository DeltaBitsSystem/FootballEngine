namespace FootballEngine.World


open FootballEngine.Domain

open FootballEngine.Simulation
open FootballEngine.Simulation.MatchSimulator
open FootballEngine.Types
open FootballEngine.World.Phases
open MatchOutcome

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
          MatchReplay: MatchReplay }

    let private parallelMapLimited (f: 'T -> 'U) (arr: 'T[]) : 'U[] =
        let maxThreads = max 1 (System.Environment.ProcessorCount - 1)
        let options = System.Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = maxThreads)
        let results = Array.zeroCreate arr.Length
        System.Threading.Tasks.Parallel.For(0, arr.Length, options, fun i ->
            results[i] <- f arr[i]) |> ignore
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
                            let home = gsReady.Clubs[fixture.HomeClubId]
                            let away = gsReady.Clubs[fixture.AwayClubId]
                            id, fixture, trySimulateMatch home away gsReady.Players gsReady.Staff gsReady.ProfileCache)
                        |> Array.fold
                            (fun (outs, errs) (id, fixture, result) ->
                                match result with
                                | Ok(h, a, _, finalState) ->
                                    let injured =
                                        finalState.Home.Sidelined
                                        |> Map.toSeq
                                        |> Seq.append (finalState.Away.Sidelined |> Map.toSeq)
                                        |> Seq.choose (fun (pid, s) ->
                                            if s = SidelinedByInjury then Some pid else None)
                                        |> Set.ofSeq

                                    { FixtureId = id
                                      Fixture =
                                        { fixture with
                                            Played = true
                                            HomeScore = Some h
                                            AwayScore = Some a
                                            Events = [] }
                                      HomeScore = h
                                      AwayScore = a
                                      InjuredPlayers = injured
                                      HomePlayerStats = [||]
                                      AwayPlayerStats = [||] }
                                    :: outs,
                                    errs
                                | Error e -> outs, string e :: errs)
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

            let home = gsReady.Clubs[fixture.HomeClubId]
            let away = gsReady.Clubs[fixture.AwayClubId]

            match trySimulateMatchFull home away gsReady.Players gsReady.Staff gsReady.ProfileCache with
            | Error e -> Error $"Match simulation failed: {e}"
            | Ok replay ->
                let h = replay.Final.HomeScore
                let a = replay.Final.AwayScore

                let injured =
                    replay.Final.Home.Sidelined
                    |> Map.toSeq
                    |> Seq.append (replay.Final.Away.Sidelined |> Map.toSeq)
                    |> Seq.choose (fun (pid, s) -> if s = SidelinedByInjury then Some pid else None)
                    |> Set.ofSeq

                let outcome =
                    { FixtureId = fixtureId
                      Fixture =
                        { fixture with
                            Played = true
                            HomeScore = Some h
                            AwayScore = Some a
                            Events = replay.Events }
                      HomeScore = h
                      AwayScore = a
                      InjuredPlayers = injured
                      HomePlayerStats = [||]
                      AwayPlayerStats = [||] }

                let finalGs = applyOutcomes (fixtureToCompMap gsReady) [| outcome |] gsReady

                let metrics =
                    MatchMetrics.extract replay.Events fixture.HomeClubId fixture.AwayClubId

                let clock1 = WorldClockOps.advance clock

                Ok
                    { DayResult =
                        { GameState = finalGs
                          WorldClock = clock1
                          PlayedMatches = [ fixtureId, outcome.Fixture ]
                          Logs = [ $"{home.Name} {h}-{a} {away.Name}" ]
                          SeasonComplete = isSeasonComplete finalGs }
                      MatchReplay = replay }

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
