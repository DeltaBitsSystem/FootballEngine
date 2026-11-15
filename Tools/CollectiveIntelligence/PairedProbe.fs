module FootballEngine.Tools.CollectiveIntelligence

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.Core.Tests.Systems.Team.CollectiveAttackFixtures

let unwrap = function Ok value -> value | Error error -> failwithf "%A" error

let measuredMatch seed treatment enabled learning anticipation rivalPress directness =
    let baseCtx = TestBuilders.formationCtx ()
    let cfg = { baseCtx.Config with CollectiveAttack = { baseCtx.Config.CollectiveAttack with Enabled = enabled; LearningEnabled = learning }; Planner = {baseCtx.Config.Planner with Enabled = anticipation} }
    let ctx = { baseCtx with Config = cfg; HomeClub = { baseCtx.HomeClub with BaseTactics = { baseCtx.HomeClub.BaseTactics with Directness = directness } }; AwayClub = { baseCtx.AwayClub with BaseTactics = { baseCtx.AwayClub.BaseTactics with PressIntensity = rivalPress } } }
    let input = { MatchId = seed; Context = ctx; Seed = seed; EffectiveConfig = EffectiveConfig.fromBalance cfg |> unwrap; EngineVersion = "collective-probe-v1" }
    let clock = Stopwatch.StartNew()
    let session = MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap
    let result = MatchSession.finish session
    clock.Stop()
    let events = session.Events |> Seq.toArray
    let count predicate = events |> Array.filter predicate |> Array.length
    let episodeEvents = events |> Array.choose (fun e -> match e.Data with EventData.CollectiveAttack(side, id, change) -> Some(side, id, change) | _ -> None)
    let starts = episodeEvents |> Array.choose (fun (side, id, change) -> match change with Started pattern -> Some(side, id, string pattern) | _ -> None)
    let finishes = episodeEvents |> Array.choose (fun (_, _, change) -> match change with Ended(reason, reward, key) -> Some(string reason, reward, key.IsSome) | _ -> None)
    let learned = finishes |> Array.filter (fun (_, _, sample) -> sample)
    let home, away = session.World.Home.Stats, session.World.Away.Stats
    let exposures = session.World.Observation.Snapshot().Entries |> Array.choose (fun entry -> match entry.Data with AttackResolved(_, _, _, _, _, _, exposure) -> Some exposure | _ -> None)
    {| Seed = seed; Treatment = treatment; Enabled = enabled; Learning = learning; Anticipation = anticipation; RivalPress = rivalPress; Directness = directness
       Terminal = session.World.State.Flow = MatchFlow.MatchEnded; Failure = (match result with Ok _ -> "" | Error error -> string error)
       ElapsedMs = clock.Elapsed.TotalMilliseconds; Ticks = int session.World.State.Tick; Hash = MatchOutcomeHash.eventsHash (Array.toList events)
       Score = [| session.World.State.HomeScore; session.World.State.AwayScore |]
       Passes = home.Passes + away.Passes; CompletedPasses = home.PassesCompleted + away.PassesCompleted
       Shots = home.Shots + away.Shots; Xg = home.XgSum + away.XgSum
       Episodes = starts.Length
       Patterns = starts |> Array.countBy (fun (_, _, pattern) -> pattern) |> Array.map (fun (pattern, count) -> {| Pattern = pattern; Count = count |})
       Milestones = episodeEvents |> Array.filter (fun (_, _, c) -> match c with Advanced _ -> true | _ -> false) |> Array.length
       Ends = finishes |> Array.countBy (fun (reason, _, _) -> reason) |> Array.map (fun (reason, count) -> {| Reason = reason; Count = count |})
       LearningSamples = learned.Length
       MeanReward = if learned.Length = 0 then 0.0 else learned |> Array.averageBy (fun (_, reward, _) -> reward)
       LiveRestartContacts = count (fun e -> match e.Data with EventData.RestartCompleted _ -> true | _ -> false)
       LearningCells = session.World.Home.Attack.Estimates.Count + session.World.Away.Attack.Estimates.Count
       RetainedExposureSamples = exposures.Length; RetainedExposureSum = Array.sum exposures |}

let scenario seed enabled closed tired =
    let baseCtx = TestBuilders.formationCtx ()
    let ctx = { baseCtx with Config = { baseCtx.Config with CollectiveAttack = { baseCtx.Config.CollectiveAttack with Enabled = enabled } } }
    let world = scenarioWithContext ctx ClubSide.Home
    if closed then
        world.Away.Frame.PosX[1] <- 3.0f
        world.Away.Frame.PosY[1] <- -4.0f
        world.Away.Frame.PosX[2] <- 6.0f
        world.Away.Frame.PosY[2] <- 4.0f
    if tired then Array.fill world.Home.Frame.Condition 0 world.Home.Frame.SlotCount 0.35f
    let buffer = buffer ()
    let schedule = ScheduleCompiler.compile Scheduler.systems
    let mutable rng = Rng.ofSeed seed
    let initialX = world.Home.Frame.PosX |> Array.copy
    let initialY = world.Home.Frame.PosY |> Array.copy
    let events = ResizeArray<MatchEvent>()
    let watch = Stopwatch.StartNew()
    let output = Array.zeroCreate<MatchEvent> 256
    let mutable collapsed, exposedTicks, activeTicks = 0, 0, 0
    for _ in 1 .. 1600 do
        let struct (next, _, count) = Stepper.stepInto schedule (systemContext ctx) rng world buffer output
        rng <- next
        for index in 0 .. count - 1 do events.Add output[index]
        world.Home.Attack.Episode |> Option.iter (fun e ->
            activeTicks <- activeTicks + 1
            if e.OwnedExposure > 0.0 then exposedTicks <- exposedTicks + 1
            if not (AttackGeometry.complementary ctx.Config.CollectiveAttack [e.Link; e.Runner; e.Security]) then collapsed <- collapsed + 1)
    watch.Stop()
    let starts = events |> Seq.choose (fun e -> match e.Data with EventData.CollectiveAttack(_, _, Started pattern) -> Some(string pattern) | _ -> None) |> Seq.toArray
    let connected = events |> Seq.filter (fun e -> match e.Data with EventData.CollectiveAttack(_, _, Advanced _) -> true | _ -> false) |> Seq.length
    let contacts = events |> Seq.filter (fun e -> match e.Data with EventData.FootContact(_, _, FootTouchKind.Kick StrikeKind.Pass, _, _, _) -> true | _ -> false) |> Seq.length
    let movement = [| for slot in MatchWorld.activeSlots world.Home.Frame do yield MatchMath.distF32 initialX[slot] initialY[slot] world.Home.Frame.PosX[slot] world.Home.Frame.PosY[slot] |] |> Array.sum
    {| Seed = seed; Enabled = enabled; Closed = closed; Tired = tired; ElapsedMs = watch.Elapsed.TotalMilliseconds
       Starts = starts; Milestones = connected; PassFootContacts = contacts; CompletedPasses = world.Home.Stats.PassesCompleted
       TeamDisplacement = movement; Samples = world.Home.Attack.Evidence |> Map.toSeq |> Seq.sumBy (fun (_, value) -> value.Samples)
       Hash = MatchOutcomeHash.eventsHash (List.ofSeq events); CollapsedDutyTicks = collapsed; ExposedOwnedTicks = exposedTicks; ActiveEpisodeTicks = activeTicks |}

type PolicyCell = { Key: int; Samples: float; RewardSum: float }
type PolicyFile = { Format: int; Home: PolicyCell[]; Away: PolicyCell[] }

let repeated output =
    let mutable home, away = Map.empty, Map.empty
    [| for index in 1 .. 6 do
           let baseCtx = TestBuilders.formationCtx ()
           let cfg = baseCtx.Config
           let press = if index <= 3 then 0.2f else 0.9f
           let ctx = {baseCtx with HomeClub = {baseCtx.HomeClub with AttackPrior = home; BaseTactics = {baseCtx.HomeClub.BaseTactics with Directness = 0.2f}}; AwayClub = {baseCtx.AwayClub with AttackPrior = away; BaseTactics = {baseCtx.AwayClub.BaseTactics with PressIntensity = press}}}
           let input = {MatchId = uint64 index; Seed = uint64 index; Context = ctx; EffectiveConfig = EffectiveConfig.fromBalance cfg |> unwrap; EngineVersion = "collective-sequence-v2"}
           let outcome = MatchAdapter.runOutcome Scheduler.systems input [] |> unwrap
           let acceptedHome = AttackLearning.applyEvents cfg.CollectiveAttack ClubSide.Home home outcome.Events
           let acceptedAway = AttackLearning.applyEvents cfg.CollectiveAttack ClubSide.Away away outcome.Events
           if acceptedHome <> outcome.Final.Home.Attack.Estimates || acceptedAway <> outcome.Final.Away.Attack.Estimates then failwith "chronological commit diverged from actual online learning"
           home <- if index = 3 then AttackLearning.retain cfg.CollectiveAttack acceptedHome else acceptedHome
           away <- if index = 3 then AttackLearning.retain cfg.CollectiveAttack acceptedAway else acceptedAway
           let cells memory = memory |> Map.toArray |> Array.map (fun (key, value: AttackEstimate) -> {Key = key; Samples = value.Samples; RewardSum = value.RewardSum})
           let saved = {Format = 10; Home = cells home; Away = cells away}
           let path = output + ".policy.json"
           File.WriteAllText(path, JsonSerializer.Serialize saved)
           let restored = JsonSerializer.Deserialize<PolicyFile>(File.ReadAllText path)
           if restored.Format <> 10 then failwith "obsolete policy format"
           let memory rows = rows |> Array.map (fun row -> row.Key, ({Samples = row.Samples; RewardSum = row.RewardSum}: AttackEstimate)) |> Map.ofArray
           if memory restored.Home <> home || memory restored.Away <> away then failwith "policy disk roundtrip changed learning"
           home <- memory restored.Home
           away <- memory restored.Away
           let replay = MatchAdapter.runOutcome Scheduler.systems input [] |> unwrap
           if replay.EventsHash <> outcome.EventsHash then failwith "original kickoff replay drifted after learning"
           let patterns = outcome.Events |> List.choose (fun e -> match e.Data with EventData.CollectiveAttack(ClubSide.Home, _, Started p) -> Some(string p) | _ -> None) |> List.countBy id
           let rows = patterns |> List.map (fun (name, count) -> {|Pattern = name; Count = count|}) |> List.toArray
           printfn "sequence match %d pressure %.1f: cells %d replay exact" index press home.Count
           yield {|Match = index; RivalPress = press; SeasonRetentionApplied = index = 3; OriginalKickoffHash = MatchJob.inputHash input; ReplayHash = replay.EventsHash; Patterns = rows; Home = saved.Home; Away = saved.Away; DiskRoundtripExact = true; ChronologicalCommitExact = true; ReplayExact = true|} |]

let private historicalMain (argv: string[]) =
    let output = if argv.Length > 0 then argv[0] else "docs/evidence/collective-intelligence/paired-v2.json"
    // Warm both treatments before timing; seeds below are a consecutive declared corpus, not selected successes.
    let treatments = ["collective-off", false, true, true; "full", true, true, true; "learning-off", true, false, true; "anticipation-off", true, true, false]
    for name, enabled, learning, anticipation in treatments do measuredMatch 99UL name enabled learning anticipation 0.5f 0.5f |> ignore
    let matches =
        [| for seed in 1UL .. 12UL do
               let press = if seed % 2UL = 0UL then 0.9f else 0.2f
               let directness = if seed % 3UL = 0UL then 0.8f else 0.2f
               for name, enabled, learning, anticipation in treatments do
                   let row = measuredMatch seed name enabled learning anticipation press directness
                   printfn "seed %d %s: episodes %d milestones %d reward %.3f, %.0fms" seed name row.Episodes row.Milestones row.MeanReward row.ElapsedMs
                   yield row |]
    let scenarios = [| for closed in [ false; true ] do for tired in [ false; true ] do for enabled in [ false; true ] do yield scenario 42UL enabled closed tired |]
    let directory = Path.GetDirectoryName output
    if not (String.IsNullOrEmpty directory) then Directory.CreateDirectory directory |> ignore
    File.WriteAllText(output, JsonSerializer.Serialize({| Format = 2; Complete = false; Matches = matches; IllustrativeScenarios = scenarios |}, JsonSerializerOptions(WriteIndented = true)))
    let sequence = repeated output
    File.WriteAllText(output, JsonSerializer.Serialize({| Format = 2; Provenance = "Rebuilt Release; full production scheduler/actions/physics; consecutive seeds 1..12; same original opponents/scenarios/limits; independent collective, learning and anticipation ablations. Six sequential matches carry exact chronological memory; retention after match3; memory-only disk roundtrip and original-input replay. Actual career database/season persistence separately tested in Client suite. Illustrative40second scenarios unchanged. No injected passes/rewards/goals."; Matches = matches; IllustrativeScenarios = scenarios; RepeatedMatches = sequence |}, JsonSerializerOptions(WriteIndented = true)))
    printfn "Evidence written to %s" output
    if matches |> Array.exists (fun row -> not row.Terminal || row.Failure <> "") then 1 else 0

[<EntryPoint>]
let main argv =
    if argv.Length > 0 && argv[0] = "organized" then
        OrganizedProbe.run (argv |> Array.skip 1)
    elif argv.Length > 0 && argv[0] = "control" then
        OrganizedProbe.runControl (argv |> Array.skip 1)
    else
        historicalMain argv
