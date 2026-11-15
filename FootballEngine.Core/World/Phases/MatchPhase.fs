namespace FootballEngine.World.Phases

open System
open FootballEngine.Core.Match.Simulator
open FootballEngine.Core.Match
open FootballEngine.Stats
open FootballEngine.World
open FootballEngine.Domain

module MatchOutcome =

    type PlayerMatchStats = MatchPlayerStats

    type FixtureOutcome =
        { FixtureId: MatchId
          Fixture: MatchFixture
          HomeScore: int
          AwayScore: int
          InjuredPlayers: Set<PlayerId>
          HomePlayerStats: PlayerMatchStats[]
          AwayPlayerStats: PlayerMatchStats[] }

    let private addStat playerId selector (stats: Map<PlayerId, PlayerMatchStats>) =
        let current =
            stats
            |> Map.tryFind playerId
            |> Option.defaultValue
                { PlayerId = playerId
                  GoalsScored = 0
                  AssistsGiven = 0
                  MinutesPlayed = 0
                  YellowCards = 0
                  RedCards = 0
                  FoulsCommitted = 0 }

        stats |> Map.add playerId (selector current)

    let private eventStats (events: MatchEvent list) : Map<PlayerId, PlayerMatchStats> =
        let addGoal pid stats =
            addStat
                pid
                (fun s ->
                    { s with
                        GoalsScored = s.GoalsScored + 1 })
                stats

        let addAssist pid stats =
            addStat
                pid
                (fun s ->
                    { s with
                        AssistsGiven = s.AssistsGiven + 1 })
                stats

        let addYellow pid stats =
            addStat
                pid
                (fun s ->
                    { s with
                        YellowCards = s.YellowCards + 1 })
                stats

        let addRed pid stats =
            addStat pid (fun s -> { s with RedCards = s.RedCards + 1 }) stats

        let addFoul pid stats =
            addStat
                pid
                (fun s ->
                    { s with
                        FoulsCommitted = s.FoulsCommitted + 1 })
                stats

        (Map.empty, events)
        ||> List.fold (fun stats event ->
            match event.Data with
            | EventData.Goal(_, scorer, assist, _) ->
                let withGoal =
                    scorer
                    |> Option.map MatchContextBuilder.unwrapPid
                    |> Option.fold (fun acc pid -> addGoal pid acc) stats

                assist
                |> Option.map MatchContextBuilder.unwrapPid
                |> Option.fold (fun acc pid -> addAssist pid acc) withGoal
            | EventData.Card(player, CardColor.Yellow) -> addYellow (MatchContextBuilder.unwrapPid player) stats
            | EventData.Card(player, CardColor.Red) -> addRed (MatchContextBuilder.unwrapPid player) stats
            | EventData.RedCardIssued player -> addRed (MatchContextBuilder.unwrapPid player) stats
            | EventData.Foul(fouler, _, _, _, _) -> addFoul (MatchContextBuilder.unwrapPid fouler) stats
            | _ -> stats)

    /// Event-only extraction deliberately reports no inferred playing time. The
    /// complete extraction below supplies minutes from the match-owned intervals.
    let extractPlayerStatsFromEvents (events: MatchEvent list) =
        eventStats events |> Map.values |> Array.ofSeq

    let private minutesPlayed
        (endAt: int<subtick>)
        (intervals: FootballEngine.Core.Match.ParticipationInterval[])
        : int =
        let ticks =
            intervals
            |> Array.sumBy (fun interval ->
                let stop = interval.EndAt |> Option.defaultValue endAt
                max 0 (int stop - int interval.StartAt))

        max 0 (ticks / int SimulationClock.minuteSubticks)

    let extractPlayerStats
        (lineup: FootballEngine.Core.Match.Lineup)
        (participation: FootballEngine.Core.Match.ParticipationBook)
        (endAt: int<subtick>)
        (events: MatchEvent list)
        : PlayerMatchStats[] =
        let byPlayer =
            ParticipationBook.snapshot participation endAt
            |> Array.groupBy _.PlayerId
            |> Map.ofArray

        let eventStats = eventStats events

        lineup.Profiles
        |> Array.map (fun profile ->
            let playerId = MatchContextBuilder.unwrapPid profile.Id
            let stats = eventStats |> Map.tryFind playerId
            let intervals = byPlayer |> Map.tryFind profile.Id |> Option.defaultValue [||]

            { PlayerId = playerId
              GoalsScored = stats |> Option.map _.GoalsScored |> Option.defaultValue 0
              AssistsGiven = stats |> Option.map _.AssistsGiven |> Option.defaultValue 0
              MinutesPlayed = minutesPlayed endAt intervals
              YellowCards = stats |> Option.map _.YellowCards |> Option.defaultValue 0
              RedCards = stats |> Option.map _.RedCards |> Option.defaultValue 0
              FoulsCommitted = stats |> Option.map _.FoulsCommitted |> Option.defaultValue 0 })

    let extractPlayerStatsFromWorld
        (ctx: MatchContext)
        (world: World)
        (events: MatchEvent list)
        : PlayerMatchStats[] * PlayerMatchStats[] =
        let endAt = world.State.Clock.Tick

        extractPlayerStats ctx.HomeLineup world.Home.Participation endAt events,
        extractPlayerStats ctx.AwayLineup world.Away.Participation endAt events

    let private emptyStanding (clubId: ClubId) =
        { ClubId = clubId
          Played = 0
          Won = 0
          Drawn = 0
          Lost = 0
          GoalsFor = 0
          GoalsAgainst = 0
          Points = 0 }

    let updateStanding (pointsForWin: int) (pointsForDraw: int) (standing: LeagueStanding) myScore oppScore =
        let base' =
            { standing with
                Played = standing.Played + 1
                GoalsFor = standing.GoalsFor + myScore
                GoalsAgainst = standing.GoalsAgainst + oppScore }

        if myScore > oppScore then
            { base' with
                Won = standing.Won + 1
                Points = standing.Points + pointsForWin }
        elif myScore < oppScore then
            { base' with Lost = standing.Lost + 1 }
        else
            { base' with
                Drawn = standing.Drawn + 1
                Points = standing.Points + pointsForDraw }

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

    /// The played flag is the persisted transaction boundary for the current
    /// World model. Deduplicating here keeps every downstream consequence on the
    /// same once-only set, including repeated outcomes in one batch.
    let freshOutcomes
        (fixtureToComp: Map<MatchId, CompetitionId>)
        (outcomes: FixtureOutcome[])
        (gs: GameState)
        : FixtureOutcome[] =
        let seen = System.Collections.Generic.HashSet<MatchId>()

        outcomes
        |> Array.choose (fun outcome ->
            if not (seen.Add outcome.FixtureId) then
                None
            else
                fixtureToComp
                |> Map.tryFind outcome.FixtureId
                |> Option.bind (fun compId ->
                    gs.Competitions[compId].Fixtures
                    |> Map.tryFind outcome.FixtureId
                    |> Option.bind (fun fixture -> if fixture.Played then None else Some outcome)))

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

        let processSide (clubId: ClubId) (rivalId: ClubId) (stats: PlayerMatchStats[]) (gs: GameState) : GameState =
            let mutable acc = gs

            for playerStats in stats do
                if playerStats.MinutesPlayed > 0 then
                    let exp =
                        match acc.PlayerExperiences |> Map.tryFind playerStats.PlayerId with
                        | Some e -> e
                        | None -> PlayerExperience.empty

                    let newExp =
                        PlayerExperience.recordMatch
                            playerStats.GoalsScored
                            playerStats.AssistsGiven
                            wasHighPressure
                            rivalId
                            won
                            drew
                            exp

                    acc <-
                        { acc with
                            PlayerExperiences = acc.PlayerExperiences |> Map.add playerStats.PlayerId newExp
                            PulseQueues =
                                PulseQueues.recordAppearance
                                    { Player = playerStats.PlayerId
                                      Matches = newExp.TotalMatchesPlayed
                                      GoalsAfter = newExp.GoalsScored
                                      GoalsInMatch = playerStats.GoalsScored }
                                    acc.PulseQueues }

            acc

        let gs1 =
            processSide fixture.HomeClubId fixture.AwayClubId outcome.HomePlayerStats gs

        processSide fixture.AwayClubId fixture.HomeClubId outcome.AwayPlayerStats gs1

    let private injurySeverityOfCode severity =
        match severity with
        | value when value >= 4 -> InjurySeverity.Severe
        | 3 -> InjurySeverity.Major
        | 2 -> InjurySeverity.Moderate
        | _ -> InjurySeverity.Minor

    let private injurySeverities (events: MatchEvent list) : Map<PlayerId, InjurySeverity> =
        events
        |> List.choose (fun event ->
            match event.Data with
            | EventData.Injury(player, severity) ->
                Some(MatchContextBuilder.unwrapPid player, injurySeverityOfCode severity)
            | _ -> None)
        |> List.groupBy fst
        |> List.map (fun (playerId, reports) ->
            let severity =
                reports
                |> List.map snd
                |> List.maxBy (function
                    | InjurySeverity.Minor -> 0
                    | InjurySeverity.Moderate -> 1
                    | InjurySeverity.Major -> 2
                    | InjurySeverity.Severe -> 3)

            playerId, severity)
        |> Map.ofList

    let applyOutcomes
        (attackConfig: CollectiveAttackConfig)
        (fixtureToComp: Map<MatchId, CompetitionId>)
        (outcomes: FixtureOutcome[])
        (gs: GameState)
        : GameState =
        let outcomes = freshOutcomes fixtureToComp outcomes gs

        let updatedComps =
            (gs.Competitions, outcomes)
            ||> Array.fold (fun comps o ->
                match Map.tryFind o.FixtureId fixtureToComp with
                | None -> comps
                | Some compId ->
                    let comp = comps[compId]

                    if comp.Fixtures |> Map.tryFind o.FixtureId |> Option.exists MatchFixture.isPlayed then
                        comps
                    else
                        let pointsForWin, pointsForDraw =
                            match comp.Type with
                            | NationalLeague(_, rules) -> rules.PointsForWin, rules.PointsForDraw
                            | NationalCup(GroupThenKnockout(group, _), _)
                            | InternationalCup(_, GroupThenKnockout(group, _), _) ->
                                group.PointsForWin, group.PointsForDraw
                            | _ -> 3, 1

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
                                    match comp.Type with
                                    | NationalLeague _
                                    | NationalCup(GroupThenKnockout _, _)
                                    | InternationalCup(_, GroupThenKnockout _, _) ->
                                        comp.Standings
                                        |> Map.add
                                            o.Fixture.HomeClubId
                                            (updateStanding pointsForWin pointsForDraw hs o.HomeScore o.AwayScore)
                                        |> Map.add
                                            o.Fixture.AwayClubId
                                            (updateStanding pointsForWin pointsForDraw as' o.AwayScore o.HomeScore)
                                    | _ -> comp.Standings })

        let withUpdatedMorale =
            ({ gs with
                Competitions = updatedComps
                MatchSeeds =
                    outcomes
                    |> Array.fold
                        (fun seeds outcome ->
                            seeds |> Map.add outcome.FixtureId (MatchContextBuilder.seedOf gs.ExecutionSeed outcome.FixtureId))
                        gs.MatchSeeds }, outcomes)
            ||> Array.fold (fun acc o ->
                updateAfterMatch o.HomeScore o.AwayScore o.Fixture.HomeClubId o.Fixture.AwayClubId acc)

        let withUpdatedExperiences =
            outcomes
            |> Array.fold (fun acc outcome ->
                let updated = updatePlayerExperiences acc outcome
                let merge clubId side (state: GameState) =
                    { state with Clubs = state.Clubs |> Map.change clubId (Option.map (fun club ->
                                { club with CoordinationMemory =
                                                { club.CoordinationMemory with AttackLearning = AttackLearning.applyEvents attackConfig side club.CoordinationMemory.AttackLearning outcome.Fixture.Events } })) }
                updated |> merge outcome.Fixture.HomeClubId ClubSide.Home |> merge outcome.Fixture.AwayClubId ClubSide.Away) withUpdatedMorale

        // Every fixture is a meeting between two coaches, engine or background alike; each remembers it from his side.
        let withRivalries =
            (withUpdatedExperiences, outcomes)
            ||> Array.fold (fun acc o ->
                FootballEngine.World.Agents.GamePlanner.recordMeeting o.Fixture o.HomeScore o.AwayScore acc)

        let injuredPlayers =
            outcomes
            |> Array.collect (fun o -> o.InjuredPlayers |> Set.toArray)
            |> Set.ofSeq

        if injuredPlayers.IsEmpty then
            withRivalries
        else
            let currentDate = gs.CurrentDate

            let applyInjury
                (players: Map<PlayerId, Player>)
                (severityByPlayer: Map<PlayerId, InjurySeverity>)
                (pid: PlayerId)
                =
                players
                |> Map.change pid (fun playerOpt ->
                    playerOpt
                    |> Option.map (fun p ->
                        // Severity is authored by the match event. A manually
                        // assembled outcome has no severity, so it gets the
                        // deterministic minimum rather than a World-side roll.
                        let severity =
                            severityByPlayer |> Map.tryFind pid |> Option.defaultValue InjurySeverity.Minor

                        let until =
                            match severity with
                            | InjurySeverity.Minor -> currentDate.AddDays 7.0
                            | InjurySeverity.Moderate -> currentDate.AddDays 21.0
                            | InjurySeverity.Major -> currentDate.AddDays 60.0
                            | InjurySeverity.Severe -> currentDate.AddDays 120.0

                        { p with
                            Status = Injured(severity, until) }))

            let players =
                outcomes
                |> Array.fold
                    (fun players outcome ->
                        let severityByPlayer = injurySeverities outcome.Fixture.Events

                        outcome.InjuredPlayers
                        |> Seq.fold (fun acc pid -> applyInjury acc severityByPlayer pid) players)
                    withRivalries.Players

            { withRivalries with
                Players = players }

    let private freshLoadOutcomes (outcomes: FixtureOutcome[]) (gs: GameState) : FixtureOutcome[] =
        let seen = System.Collections.Generic.HashSet<MatchId>()

        outcomes
        |> Array.choose (fun outcome ->
            if not (seen.Add outcome.FixtureId) then
                None
            else
                let alreadyPlayed =
                    gs.Competitions
                    |> Map.exists (fun _ comp ->
                        comp.Fixtures
                        |> Map.tryFind outcome.FixtureId
                        |> Option.exists MatchFixture.isPlayed)

                if alreadyPlayed then None else Some outcome)

    /// Playing costs condition and builds sharpness; squad members who don't play
    /// lose rhythm. Participation stats are the authority, so substitutes and
    /// players sent off are charged from their actual minutes, not the saved lineup.
    let applyPostMatchLoad (cfg: TrainingConfig) (outcomes: FixtureOutcome[]) (gs: GameState) : GameState =
        let outcomes = freshLoadOutcomes outcomes gs

        if outcomes.Length = 0 then
            gs
        else
            let played =
                outcomes
                |> Array.collect (fun o ->
                    Array.append
                        (o.HomePlayerStats
                         |> Array.choose (fun stats ->
                             if stats.MinutesPlayed > 0 then
                                 Some stats.PlayerId
                             else
                                 None))
                        (o.AwayPlayerStats
                         |> Array.choose (fun stats ->
                             if stats.MinutesPlayed > 0 then
                                 Some stats.PlayerId
                             else
                                 None)))
                |> Set.ofArray

            let involved =
                outcomes
                |> Array.collect (fun o -> [| o.Fixture.HomeClubId; o.Fixture.AwayClubId |])
                |> Set.ofArray

            { gs with
                Players =
                    gs.Players
                    |> Map.map (fun pid p ->
                        if Set.contains pid played then
                            { p with
                                Condition = max 0 (p.Condition - cfg.PostMatchConditionCost)
                                MatchFitness = min 100 (p.MatchFitness + cfg.PostMatchFitnessGain) }
                        else
                            match Player.clubOf p with
                            | Some cid when Set.contains cid involved ->
                                { p with
                                    MatchFitness = max 0 (p.MatchFitness - cfg.IdleFitnessLoss) }
                            | _ -> p) }

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
                && not (GameState.isUserFixture state f)))
        |> List.ofSeq

    /// Per-match memory seam: one club watches the other. The watcher is the
    /// side facing the opponent, and its own events are dropped before
    /// extraction because PatternExtractor reads the pattern's owner from the
    /// first action event — and the first kick is almost always the watcher's.
    let patternOf
        (cfg: ScoutConfig)
        (homeId: ClubId)
        (awayId: ClubId)
        (subject: ClubId)
        (signature: string * TacticalInstructions)
        (clubOfPlayer: FootballEngine.Core.Match.PlayerId -> ClubId option)
        (events: MatchEvent list)
        : PatternVector =
        let watcher = if subject = homeId then awayId else homeId

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

        PatternExtractor.extract cfg homeId awayId clubOfPlayer rivalEvents
        |> PatternVector.withSignature (fst signature) (snd signature)

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
        ScoutingMemory.update cfg now opponent (patternOf cfg homeId awayId opponent signature clubOfPlayer events) mem

    /// The user's match as a rival can read it: the shape and instructions he took in, the pattern he played and
    /// the result. None when the user's lineup is not on record.
    let userMatchRecord
        (cfg: ScoutConfig)
        (gs: GameState)
        (fixture: MatchFixture)
        (homeGoals: int)
        (awayGoals: int)
        (events: MatchEvent list)
        : UserMatchRecord option =
        match GameState.managedClub gs with
        | Some userClub when MatchFixture.involves userClub fixture ->
            GameState.getLineup userClub gs
            |> Option.map (fun lineup ->
                let instructions =
                    lineup.Instructions |> Option.defaultValue TacticalInstructions.defaultInstructions

                let clubOfEnginePlayer (FootballEngine.Core.Match.PlayerId n) =
                    gs.Players |> Map.tryFind n |> Option.bind Player.clubOf

                let isHome = fixture.HomeClubId = userClub

                { Date = fixture.ScheduledDate
                  OpponentClubId = if isHome then fixture.AwayClubId else fixture.HomeClubId
                  Formation = lineup.Formation
                  Instructions = instructions
                  Pattern =
                    patternOf
                        cfg
                        fixture.HomeClubId
                        fixture.AwayClubId
                        userClub
                        (string lineup.Formation, instructions)
                        clubOfEnginePlayer
                        events
                  GoalsFor = if isHome then homeGoals else awayGoals
                  GoalsAgainst = if isHome then awayGoals else homeGoals })
        | _ -> None

    /// Simulates a fixture with the Core engine. Returns the outcome or None
    /// when the fixture cannot be lined up (missing coach/lineup).
    let simulateFixtureExecutionWithBalance
        (balance: BalanceConfig)
        (fixtureId: MatchId)
        (fixture: MatchFixture)
        (gs: GameState)
        : (FixtureOutcome * MatchInput) option =
        match EffectiveConfig.fromBalance balance, MatchContextBuilder.build gs fixture balance with
        | Error _, _
        | _, None -> None
        | Ok effective, Some ctx ->
            let input =
                MatchInput.create (uint64 fixtureId) (MatchContextBuilder.seedOf gs.ExecutionSeed fixtureId) effective ctx

            match MatchAdapter.runOutcome Scheduler.systems input [] with
            | Error _ -> None
            | Ok result ->
                let finalWorld = result.Final

                let homePlayerStats, awayPlayerStats =
                    extractPlayerStatsFromWorld ctx finalWorld result.Events

                let playedFixture =
                    { fixture with
                        Played = true
                        HomeScore = Some finalWorld.State.HomeScore
                        AwayScore = Some finalWorld.State.AwayScore
                        Events = MatchEvent.record result.Events }

                let outcome =
                    { FixtureId = fixtureId
                      Fixture = playedFixture
                      HomeScore = finalWorld.State.HomeScore
                      AwayScore = finalWorld.State.AwayScore
                      InjuredPlayers = MatchContextBuilder.injuredOf result.Events
                      HomePlayerStats = homePlayerStats
                      AwayPlayerStats = awayPlayerStats }

                Some(outcome, input)

    /// A fixture played by the background model: the same lineups and seed the engine would get, no ticks.
    let simulateFixtureQuickWithBalance
        (balance: BalanceConfig)
        (fixtureId: MatchId)
        (fixture: MatchFixture)
        (gs: GameState)
        : (FixtureOutcome * MatchInput) option =
        match EffectiveConfig.fromBalance balance, MatchContextBuilder.build gs fixture balance with
        | Error _, _
        | _, None -> None
        | Ok effective, Some ctx ->
            let seed = MatchContextBuilder.seedOf gs.ExecutionSeed fixtureId
            let played = QuickMatch.simulate ctx seed

            let input =
                { MatchId = uint64 fixtureId
                  Context = ctx
                  Seed = seed
                  EffectiveConfig = effective
                  EngineVersion = "football-engine-quick-01" }

            let outcome =
                { FixtureId = fixtureId
                  Fixture =
                    { fixture with
                        Played = true
                        HomeScore = Some played.HomeScore
                        AwayScore = Some played.AwayScore
                        Events = played.Events }
                  HomeScore = played.HomeScore
                  AwayScore = played.AwayScore
                  InjuredPlayers = played.Injured
                  HomePlayerStats = played.HomeStats
                  AwayPlayerStats = played.AwayStats }

            Some(outcome, input)

    /// Only the user's own fixtures get the engine: the user asked for every other match, rivals in the same
    /// league included, to be fast. The background model is fitted to the engine (`Training quick-fit`), so a
    /// rival's result still answers to the same strengths.
    let private playsOnEngine (gs: GameState) : MatchFixture -> bool =
        match GameState.managedClub gs with
        | None -> fun _ -> false
        | Some club -> MatchFixture.involves club

    let simulateFixtureWithBalance balance fixtureId fixture gs : FixtureOutcome option =
        simulateFixtureExecutionWithBalance balance fixtureId fixture gs |> Option.map fst

    let simulateFixture (fixtureId: MatchId) (fixture: MatchFixture) (gs: GameState) : FixtureOutcome option =
        let balance = WeightMergeSettings.mergeWithOverridesSave None
        simulateFixtureWithBalance balance fixtureId fixture gs

    /// Save-aware variant: derives effective BalanceConfig from SaveSettings.
    let simulateFixtureWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (fixtureId: MatchId)
        (fixture: MatchFixture)
        (gs: GameState)
        : FixtureOutcome option =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        simulateFixtureWithBalance balance fixtureId fixture gs

    /// The engine for the user's own fixtures, the background model for every other one.
    let simulateFixtureForWorldWithBalance (balance: BalanceConfig) (fixtureId: MatchId) (fixture: MatchFixture) (gs: GameState) =
        if playsOnEngine gs fixture then
            simulateFixtureExecutionWithBalance balance fixtureId fixture gs
        else
            simulateFixtureQuickWithBalance balance fixtureId fixture gs

    let runFixtureExecutionsWithBalance (balance: BalanceConfig) (fixtures: (MatchId * MatchFixture) list) (state: GameState) =
        let config = balance
        let gsReady =
            state
            |> FootballEngine.World.Agents.GamePlanner.prepareForFixtures balance.RivalManager fixtures
            |> LineupOps.ensureForFixtures fixtures

        let executions =
            fixtures
            |> Array.ofList
            |> parallelMapLimited (fun (id, fixture) -> simulateFixtureForWorldWithBalance balance id fixture gsReady)
            |> Array.choose id

        // A background result carries no passes or patterns: pressing coordination and scouting learn nothing from it.
        let hasEvidence (o: FixtureOutcome) =
            o.Fixture.Events |> List.exists (fun e -> match e.Data with EventData.Pass _ -> true | _ -> false)

        let outcomes = freshOutcomes (fixtureToCompMap gsReady) (executions |> Array.map fst) gsReady

        let afterOutcomes =
            applyPostMatchLoad balance.Training outcomes gsReady
            |> applyOutcomes balance.CollectiveAttack (fixtureToCompMap gsReady) outcomes

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
            | Some o ->
                let isHome = o.Fixture.HomeClubId = clubId

                let m =
                    MatchMetrics.extract o.Fixture.Events o.Fixture.HomeClubId o.Fixture.AwayClubId clubOfPlayer

                // Without passes the neutral rate leaves pressing coordination where it was.
                let passRate =
                    if not (hasEvidence o) then 0.5
                    elif isHome then m.PassAccuracyHome
                    else m.PassAccuracyAway

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

                let withFamiliarity =
                    match
                        GameState.headCoach clubId gs
                        |> Option.bind (fun c -> c.Attributes.Coaching.Lineup)
                    with
                    | None -> updatedCoord
                    | Some lu ->
                        let key = string lu.Formation

                        let cur =
                            updatedCoord.TacticalFamiliarity |> Map.tryFind key |> Option.defaultValue 0.5

                        { updatedCoord with
                            TacticalFamiliarity =
                                updatedCoord.TacticalFamiliarity
                                |> Map.add key (min 1.0 (cur + balance.Team.TacticalFamiliarityGain)) }

                { gs with
                    Clubs =
                        gs.Clubs
                        |> Map.add
                            clubId
                            { club with
                                CoordinationMemory = withFamiliarity } }
            | _ -> gs

        let updateScouting (clubId: ClubId) (gs: GameState) : GameState =
            let outcome =
                outcomes
                |> Array.tryFind (fun o -> o.Fixture.HomeClubId = clubId || o.Fixture.AwayClubId = clubId)

            match outcome with
            | Some o when hasEvidence o ->
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
            | _ -> gs

        let affectedClubs =
            outcomes
            |> Array.collect (fun o -> [| o.Fixture.HomeClubId; o.Fixture.AwayClubId |])
            |> Set.ofArray

        let withScouting =
            affectedClubs
            |> Seq.fold (fun gs clubId -> gs |> updateClubCoord clubId |> updateScouting clubId) afterOutcomes

        // What rivals may study is what reached the screen: the user's engine matches only.
        let nextState =
            (withScouting, outcomes)
            ||> Array.fold (fun gs o ->
                if hasEvidence o then
                    match userMatchRecord config.Scout gsReady o.Fixture o.HomeScore o.AwayScore o.Fixture.Events with
                    | Some record -> FootballEngine.World.Agents.GamePlanner.recordUserMatch config.RivalManager record gs
                    | None -> gs
                else
                    gs)

        let acceptedIds = outcomes |> Array.map _.FixtureId |> Set.ofArray
        let acceptedInputs =
            executions
            |> Array.choose (fun (outcome, input) ->
                if Set.contains outcome.FixtureId acceptedIds && GameState.isUserFixture state outcome.Fixture then
                    Some(outcome.FixtureId, (MatchInput.snapshot input, []))
                else
                    None)

        nextState, acceptedInputs

    let runFixturesWithBalance balance fixtures state =
        runFixtureExecutionsWithBalance balance fixtures state |> fst

    let runFixtures (fixtures: (MatchId * MatchFixture) list) (state: GameState) =
        let balance = WeightMergeSettings.mergeWithOverridesSave None
        runFixturesWithBalance balance fixtures state

    let runFixturesWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (fixtures: (MatchId * MatchFixture) list)
        (state: GameState)
        =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        runFixturesWithBalance balance fixtures state

    let makeWithBalance (balance: BalanceConfig) : WorldPhase =
        { Tag = Match
          Frequency = OnDemand
          Run =
            fun _clock state ->
                match getAiFixturesToday state with
                | [] -> state
                | fixtures -> runFixturesWithBalance balance fixtures state }

    let makeWithSave (saveOpt: FootballEngine.Settings.SaveSettings option) : WorldPhase =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        makeWithBalance balance

    let make: WorldPhase =
        { Tag = Match
          Frequency = OnDemand
          Run =
            fun _clock state ->
                match getAiFixturesToday state with
                | [] -> state
                | fixtures -> runFixtures fixtures state }
