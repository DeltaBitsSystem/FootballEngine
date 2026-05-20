namespace FootballEngine.World.Phases

open FootballEngine.Domain
open FootballEngine.Simulation.MatchSimulator
open FootballEngine.Stats
open FootballEngine.TeamOrchestrator
open FootballEngine.Types
open FootballEngine.World

module MatchOutcome =

    type PlayerMatchStats =
        { PlayerId: PlayerId
          GoalsScored: int
          AssistsGiven: int
          MinutesPlayed: int }

    type FixtureOutcome =
        { FixtureId: MatchId
          Fixture: MatchFixture
          HomeScore: int
          AwayScore: int
          InjuredPlayers: Set<PlayerId>
          HomePlayerStats: PlayerMatchStats[]
          AwayPlayerStats: PlayerMatchStats[] }

    let extractPlayerStatsFromEvents (events: MatchEvent list) =
        let goals = System.Collections.Generic.Dictionary<PlayerId, int>()
        let assists = System.Collections.Generic.Dictionary<PlayerId, int>()

        for e in events do
            match e.Type with
            | MatchEventType.Goal ->
                goals[e.PlayerId] <-
                    match goals.TryGetValue(e.PlayerId) with
                    | true, v -> v + 1
                    | _ -> 1
            | MatchEventType.Assist ->
                let scorer = e.PlayerId

                assists[scorer] <-
                    match assists.TryGetValue(scorer) with
                    | true, v -> v + 1
                    | _ -> 1
            | _ -> ()

        goals.Keys
        |> Seq.map (fun pid ->
            { PlayerId = pid
              GoalsScored =
                match goals.TryGetValue(pid) with
                | true, v -> v
                | _ -> 0
              AssistsGiven =
                match assists.TryGetValue(pid) with
                | true, v -> v
                | _ -> 0
              MinutesPlayed = 90 })
        |> Array.ofSeq

    let private emptyStanding (clubId: ClubId) =
        { ClubId = clubId
          Played = 0
          Won = 0
          Drawn = 0
          Lost = 0
          GoalsFor = 0
          GoalsAgainst = 0
          Points = 0 }

    let updateStanding (standing: LeagueStanding) myScore oppScore =
        let base' =
            { standing with
                Played = standing.Played + 1
                GoalsFor = standing.GoalsFor + myScore
                GoalsAgainst = standing.GoalsAgainst + oppScore }

        if myScore > oppScore then
            { base' with
                Won = standing.Won + 1
                Points = standing.Points + 3 }
        elif myScore < oppScore then
            { base' with Lost = standing.Lost + 1 }
        else
            { base' with
                Drawn = standing.Drawn + 1
                Points = standing.Points + 1 }

    let updateAfterMatch
        (homeScore: int)
        (awayScore: int)
        (homeId: ClubId)
        (awayId: ClubId)
        (gs: GameState)
        : GameState =
        let winnerId =
            if homeScore > awayScore then Some homeId
            elif awayScore > homeScore then Some awayId
            else None

        let clubDelta id =
            if winnerId = Some id then 3
            elif winnerId.IsNone then 0
            else -2

        let playerDelta id =
            if winnerId = Some id then 2
            elif winnerId.IsNone then 0
            else -1

        { gs with
            Clubs =
                gs.Clubs
                |> Map.map (fun id c ->
                    if id = homeId || id = awayId then
                        { c with
                            Morale = clamp 0 100 (c.Morale + clubDelta id) }
                    else
                        c)
            Players =
                gs.Players
                |> Map.map (fun _ p ->
                    match Player.clubOf p with
                    | Some cid when cid = homeId || cid = awayId ->
                        { p with
                            Morale = clamp 0 100 (p.Morale + playerDelta cid) }
                    | _ -> p) }

    let fixtureToCompMap (gs: GameState) =
        gs.Competitions
        |> Map.toList
        |> List.collect (fun (compId, comp) -> comp.Fixtures |> Map.toList |> List.map (fun (fid, _) -> fid, compId))
        |> Map.ofList

    let private isHighPressureMatch (events: MatchEvent list) : bool =
        let hasGoal =
            events
            |> List.exists (fun e ->
                match e.Type with
                | MatchEventType.Goal -> true
                | _ -> false)

        let hasRedCard =
            events
            |> List.exists (fun e ->
                match e.Type with
                | MatchEventType.RedCard -> true
                | _ -> false)

        hasGoal || hasRedCard

    let private updatePlayerExperiences (gs: GameState) (outcome: FixtureOutcome) : GameState =
        let fixture = outcome.Fixture
        let won = outcome.HomeScore > outcome.AwayScore
        let drew = outcome.HomeScore = outcome.AwayScore
        let wasHighPressure = isHighPressureMatch fixture.Events

        let homeStatsMap =
            outcome.HomePlayerStats |> Array.map (fun st -> st.PlayerId, st) |> Map.ofArray

        let awayStatsMap =
            outcome.AwayPlayerStats |> Array.map (fun st -> st.PlayerId, st) |> Map.ofArray

        let processSide (clubId: ClubId) (rivalId: ClubId) (gs: GameState) : GameState =
            let club = gs.Clubs[clubId]
            let mutable acc = gs

            for playerId in club.PlayerIds do
                let goals =
                    homeStatsMap
                    |> Map.tryFind playerId
                    |> Option.map _.GoalsScored
                    |> Option.defaultValue 0

                let assists =
                    homeStatsMap
                    |> Map.tryFind playerId
                    |> Option.map _.AssistsGiven
                    |> Option.defaultValue 0

                let exp =
                    match acc.PlayerExperiences |> Map.tryFind playerId with
                    | Some e -> e
                    | None -> PlayerExperience.empty

                let newExp =
                    PlayerExperience.recordMatch goals assists wasHighPressure rivalId won drew exp

                acc <-
                    { acc with
                        PlayerExperiences = acc.PlayerExperiences |> Map.add playerId newExp }

            acc

        let gs1 = processSide fixture.HomeClubId fixture.AwayClubId gs
        processSide fixture.AwayClubId fixture.HomeClubId gs1

    let private injurySeverityWeights =
        [ 0.30, Minor; 0.40, Moderate; 0.20, Major; 0.10, Severe ]

    let applyOutcomes
        (fixtureToComp: Map<MatchId, CompetitionId>)
        (outcomes: FixtureOutcome[])
        (gs: GameState)
        : GameState =
        let updatedComps =
            (gs.Competitions, outcomes)
            ||> Array.fold (fun comps o ->
                match Map.tryFind o.FixtureId fixtureToComp with
                | None -> comps
                | Some compId ->
                    let comp = comps[compId]

                    let hs =
                        comp.Standings
                        |> Map.tryFind o.Fixture.HomeClubId
                        |> Option.defaultWith (fun () -> emptyStanding o.Fixture.HomeClubId)

                    let as' =
                        comp.Standings
                        |> Map.tryFind o.Fixture.AwayClubId
                        |> Option.defaultWith (fun () -> emptyStanding o.Fixture.AwayClubId)

                    comps
                    |> Map.add
                        compId
                        { comp with
                            Fixtures = comp.Fixtures |> Map.add o.FixtureId o.Fixture
                            Standings =
                                comp.Standings
                                |> Map.add o.Fixture.HomeClubId (updateStanding hs o.HomeScore o.AwayScore)
                                |> Map.add o.Fixture.AwayClubId (updateStanding as' o.AwayScore o.HomeScore) })

        let withUpdatedMorale =
            ({ gs with Competitions = updatedComps }, outcomes)
            ||> Array.fold (fun acc o ->
                updateAfterMatch o.HomeScore o.AwayScore o.Fixture.HomeClubId o.Fixture.AwayClubId acc)

        let injuredPlayers =
            outcomes
            |> Array.collect (fun o -> o.InjuredPlayers |> Set.toArray)
            |> Set.ofSeq

        if injuredPlayers.IsEmpty then
            withUpdatedMorale
        else
            let currentDate = gs.CurrentDate

            let applyInjury (players: Map<PlayerId, Player>) (pid: PlayerId) =
                players
                |> Map.change pid (fun playerOpt ->
                    playerOpt
                    |> Option.map (fun p ->
                        let severity = pickWeighted injurySeverityWeights

                        let until =
                            match severity with
                            | Minor -> currentDate.AddDays 7.0
                            | Moderate -> currentDate.AddDays 21.0
                            | Major -> currentDate.AddDays 60.0
                            | Severe -> currentDate.AddDays 120.0

                        { p with
                            Status = Injured(severity, until) }))

            { withUpdatedMorale with
                Players = injuredPlayers |> Seq.fold applyInjury withUpdatedMorale.Players }

module MatchPhase =
    open MatchOutcome

    let private parallelMapLimited (f: 'T -> 'U) (arr: 'T[]) : 'U[] =
        let maxThreads = max 1 (System.Environment.ProcessorCount - 1)
        let options = System.Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = maxThreads)
        let results = Array.zeroCreate arr.Length
        System.Threading.Tasks.Parallel.For(0, arr.Length, options, fun i ->
            results[i] <- f arr[i]) |> ignore
        results

    let getAiFixturesToday (state: GameState) =
        state.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, comp) ->
            comp.Fixtures
            |> Map.toSeq
            |> Seq.filter (fun (_, f) ->
                f.ScheduledDate.Date = state.CurrentDate.Date
                && not f.Played
                && f.HomeClubId <> state.UserClubId
                && f.AwayClubId <> state.UserClubId))
        |> List.ofSeq

    let runFixtures (fixtures: (MatchId * MatchFixture) list) (state: GameState) =
        let gsReady = LineupOps.ensureForFixtures fixtures state

        let outcomes =
            fixtures
            |> Array.ofList
            |> parallelMapLimited (fun (id, fixture) ->
                let home = gsReady.Clubs[fixture.HomeClubId]
                let away = gsReady.Clubs[fixture.AwayClubId]
                id, fixture, trySimulateMatch home away gsReady.Players gsReady.Staff gsReady.ProfileCache)
            |> Array.choose (fun (id, fixture, result) ->
                match result with
                | Ok(h, a, events, finalState) ->
                    let injured =
                        finalState.Home.Sidelined
                        |> Map.toSeq
                        |> Seq.append (finalState.Away.Sidelined |> Map.toSeq)
                        |> Seq.choose (fun (pid, s) -> if s = SidelinedByInjury then Some pid else None)
                        |> Set.ofSeq

                    Some
                        { FixtureId = id
                          Fixture =
                            { fixture with
                                Played = true
                                HomeScore = Some h
                                AwayScore = Some a
                                Events = events }
                          HomeScore = h
                          AwayScore = a
                          InjuredPlayers = injured
                          HomePlayerStats = [||]
                          AwayPlayerStats = [||] }
                | Error _ -> None)

        let afterOutcomes = applyOutcomes (fixtureToCompMap gsReady) outcomes gsReady

        let w = FootballEngine.ML.EngineWeightDefaults.defaults.Collective

        let updateClubCoord (clubId: ClubId) (gs: GameState) : GameState =
            let outcome = outcomes |> Array.tryFind (fun o -> o.Fixture.HomeClubId = clubId || o.Fixture.AwayClubId = clubId)

            match outcome with
            | None -> gs
            | Some o ->
                let isHome = o.Fixture.HomeClubId = clubId
                let m = MatchMetrics.extract o.Fixture.Events o.Fixture.HomeClubId o.Fixture.AwayClubId
                let passRate = if isHome then m.PassAccuracyHome else m.PassAccuracyAway

                let club = gs.Clubs[clubId]
                let updatedCoord =
                    CoordinationLoop.updateFromMatch
                        passRate
                        0.5
                        0
                        0
                        club.CoordinationMemory
                        w

                let playersChanged = o.InjuredPlayers |> Set.count
                let finalCoord =
                    if playersChanged > 0 then CoordinationLoop.applySquadChangeDecay playersChanged updatedCoord
                    else updatedCoord

                { gs with
                    Clubs = gs.Clubs |> Map.add clubId { club with CoordinationMemory = finalCoord } }

        let affectedClubs =
            outcomes
            |> Array.collect (fun o -> [| o.Fixture.HomeClubId; o.Fixture.AwayClubId |])
            |> Set.ofArray

        affectedClubs |> Seq.fold (fun gs clubId -> updateClubCoord clubId gs) afterOutcomes

    let make: WorldPhase =
        { Tag = Match
          Frequency = OnDemand
          Run =
            fun _clock state ->
                match getAiFixturesToday state with
                | [] -> state
                | fixtures -> runFixtures fixtures state }
