namespace FootballEngine.World.Phases

open System
open FootballEngine.Core.Match.Simulator
open FootballEngine.Core.Match
open FootballEngine.Stats
open FootballEngine.World
open FootballEngine.Domain

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

        for e in events do
            match e.Data with
            | EventData.Goal(_, Some scorer, _, _) ->
                let n = MatchContextBuilder.unwrapPid scorer

                goals[n] <-
                    match goals.TryGetValue n with
                    | true, v -> v + 1
                    | _ -> 1
            | _ -> ()

        goals.Keys
        |> Seq.map (fun pid ->
            { PlayerId = pid
              GoalsScored = goals[pid]
              AssistsGiven = 0
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
        events
        |> List.exists (fun e ->
            match e.Data with
            | EventData.Goal _
            | EventData.RedCardIssued _ -> true
            | _ -> false)

    let private updatePlayerExperiences (gs: GameState) (outcome: FixtureOutcome) : GameState =
        let fixture = outcome.Fixture
        let won = outcome.HomeScore > outcome.AwayScore
        let drew = outcome.HomeScore = outcome.AwayScore
        let wasHighPressure = isHighPressureMatch fixture.Events

        let processSide (clubId: ClubId) (rivalId: ClubId) (gs: GameState) : GameState =
            let club = gs.Clubs[clubId]
            let mutable acc = gs

            for playerId in club.PlayerIds do
                let exp =
                    match acc.PlayerExperiences |> Map.tryFind playerId with
                    | Some e -> e
                    | None -> PlayerExperience.empty

                let newExp = PlayerExperience.recordMatch 0 0 wasHighPressure rivalId won drew exp

                acc <-
                    { acc with
                        PlayerExperiences = acc.PlayerExperiences |> Map.add playerId newExp }

            acc

        let gs1 = processSide fixture.HomeClubId fixture.AwayClubId gs
        processSide fixture.AwayClubId fixture.HomeClubId gs1

    let private injurySeverityWeights =
        [ 0.30, InjurySeverity.Minor
          0.40, InjurySeverity.Moderate
          0.20, InjurySeverity.Major
          0.10, InjurySeverity.Severe ]

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
                            | InjurySeverity.Minor -> currentDate.AddDays 7.0
                            | InjurySeverity.Moderate -> currentDate.AddDays 21.0
                            | InjurySeverity.Major -> currentDate.AddDays 60.0
                            | InjurySeverity.Severe -> currentDate.AddDays 120.0

                        { p with
                            Status = Injured(severity, until) }))

            { withUpdatedMorale with
                Players = injuredPlayers |> Seq.fold applyInjury withUpdatedMorale.Players }

module MatchPhase =
    open MatchOutcome

    let private parallelMapLimited (f: 'T -> 'U) (arr: 'T[]) : 'U[] =
        let maxThreads = max 1 (System.Environment.ProcessorCount - 1)

        let options =
            System.Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = maxThreads)

        let results = Array.zeroCreate arr.Length

        System.Threading.Tasks.Parallel.For(0, arr.Length, options, fun i -> results[i] <- f arr[i])
        |> ignore

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

    /// Per-match memory seam: one club watches the other. The watcher is the
    /// side facing the opponent, and its own events are dropped before
    /// extraction because PatternExtractor reads the pattern's owner from the
    /// first action event — and the first kick is almost always the watcher's.
    let recordPattern
        (cfg: ScoutConfig)
        (now: System.DateTime)
        (homeId: ClubId)
        (awayId: ClubId)
        (opponent: ClubId)
        (signature: string * TacticalInstructions)
        (clubOfPlayer: FootballEngine.Core.Match.PlayerId -> ClubId option)
        (events: MatchEvent list)
        (mem: Map<ClubId, OpponentMemory>)
        : Map<ClubId, OpponentMemory> =
        let watcher = if opponent = homeId then awayId else homeId

        let clubOfSide side =
            match side with
            | ClubSide.Home -> homeId
            | ClubSide.Away -> awayId

        let rivalEvents =
            events
            |> List.filter (fun ev ->
                match ev.Data with
                | EventData.Pass(p, _, _, _, _, _, _, _, _)
                | EventData.Shot(p, _, _, _, _, _)
                | EventData.Dribble(p, _, _, _)
                | EventData.CrossAttempt(p, _, _) -> clubOfPlayer p <> Some watcher
                | EventData.Possession(side, _) -> clubOfSide side <> watcher
                | _ -> false)

        let vector =
            PatternExtractor.extract cfg homeId awayId clubOfPlayer rivalEvents
            |> PatternVector.withSignature (fst signature) (snd signature)

        ScoutingMemory.update cfg now opponent vector mem

    /// Simulates a fixture with the Core engine. Returns the outcome or None
    /// when the fixture cannot be lined up (missing coach/lineup).
    let simulateFixture (fixtureId: MatchId) (fixture: MatchFixture) (gs: GameState) : FixtureOutcome option =
        // R11 — production calibration: if weights.json exists next to the
        // executable it is merged over the defaults once per match.
        let weightsPath =
            let dir = System.AppContext.BaseDirectory
            let candidate = System.IO.Path.Combine(dir, "weights.json")

            if System.IO.File.Exists candidate then
                Some candidate
            else
                None

        match MatchContextBuilder.build gs fixture BalanceConfig.defaultConfig with
        | None -> None
        | Some ctx ->
            let result =
                Simulator.runMatch Scheduler.systems ctx (MatchContextBuilder.seedOf fixtureId) RunMode.Fast weightsPath

            let finalWorld = result.Final

            let outcome =
                { FixtureId = fixtureId
                  Fixture =
                    { fixture with
                        Played = true
                        HomeScore = Some finalWorld.State.HomeScore
                        AwayScore = Some finalWorld.State.AwayScore
                        Events = result.Events }
                  HomeScore = finalWorld.State.HomeScore
                  AwayScore = finalWorld.State.AwayScore
                  InjuredPlayers = MatchContextBuilder.injuredOf result.Events
                  HomePlayerStats = [||]
                  AwayPlayerStats = [||] }

            Some outcome

    let runFixtures (fixtures: (MatchId * MatchFixture) list) (state: GameState) =
        let config = BalanceConfig.defaultConfig
        let gsReady = LineupOps.ensureForFixtures fixtures state

        let outcomes =
            fixtures
            |> Array.ofList
            |> parallelMapLimited (fun (id, fixture) -> simulateFixture id fixture gsReady)
            |> Array.choose id

        let afterOutcomes = applyOutcomes (fixtureToCompMap gsReady) outcomes gsReady

        let clubOfPlayer =
            fun pid -> gsReady.Players |> Map.tryFind pid |> Option.bind Player.clubOf

        // Engine player ids are the domain ids (Projection.toProfile), so the
        // scouting path unwraps them to index the domain player map.
        let clubOfEnginePlayer (FootballEngine.Core.Match.PlayerId n) =
            gsReady.Players |> Map.tryFind n |> Option.bind Player.clubOf

        let updateClubCoord (clubId: ClubId) (gs: GameState) : GameState =
            let outcome =
                outcomes
                |> Array.tryFind (fun o -> o.Fixture.HomeClubId = clubId || o.Fixture.AwayClubId = clubId)

            match outcome with
            | None -> gs
            | Some o ->
                let isHome = o.Fixture.HomeClubId = clubId

                let m =
                    MatchMetrics.extract o.Fixture.Events o.Fixture.HomeClubId o.Fixture.AwayClubId clubOfPlayer

                let passRate = if isHome then m.PassAccuracyHome else m.PassAccuracyAway

                let club = gs.Clubs[clubId]

                let injuredCount = o.InjuredPlayers |> Set.count
                let decay = Math.Clamp(1.0 - 0.05 * float injuredCount, 0.5, 1.0)

                let updatedCoord =
                    { club.CoordinationMemory with
                        PressingCoordination =
                            Math.Clamp(
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

        let updateScouting (clubId: ClubId) (gs: GameState) : GameState =
            let outcome =
                outcomes
                |> Array.tryFind (fun o -> o.Fixture.HomeClubId = clubId || o.Fixture.AwayClubId = clubId)

            match outcome with
            | None -> gs
            | Some o ->
                let isHome = o.Fixture.HomeClubId = clubId

                let rivalId =
                    if isHome then
                        o.Fixture.AwayClubId
                    else
                        o.Fixture.HomeClubId

                // The signature recorded is the rival coach's current lineup
                // shape; a rival that later changes it gets a dampened counter.
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
                    recordPattern
                        config.Scout
                        gsReady.CurrentDate
                        o.Fixture.HomeClubId
                        o.Fixture.AwayClubId
                        rivalId
                        rivalSignature
                        clubOfEnginePlayer
                        o.Fixture.Events
                        club.ScoutingMemory
                    // Rivals unseen for months stop costing save space.
                    |> ScoutingMemory.prune config.Scout gsReady.CurrentDate

                { gs with
                    Clubs =
                        gs.Clubs
                        |> Map.add
                            clubId
                            { club with
                                ScoutingMemory = updatedMem } }

        let affectedClubs =
            outcomes
            |> Array.collect (fun o -> [| o.Fixture.HomeClubId; o.Fixture.AwayClubId |])
            |> Set.ofArray

        affectedClubs
        |> Seq.fold (fun gs clubId -> gs |> updateClubCoord clubId |> updateScouting clubId) afterOutcomes

    let make: WorldPhase =
        { Tag = Match
          Frequency = OnDemand
          Run =
            fun _clock state ->
                match getAiFixturesToday state with
                | [] -> state
                | fixtures -> runFixtures fixtures state }
