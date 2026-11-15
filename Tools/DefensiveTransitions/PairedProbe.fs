module FootballEngine.Tools.DefensiveTransitionProbe

// Binary references keep the same Release probe executable usable against both sides of the change.
open System
open System.Diagnostics
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Systems.Team

let json value =
    JsonSerializer.Serialize(value, JsonSerializerOptions(WriteIndented = true))

let micros start =
    float (Stopwatch.GetTimestamp() - start) * 1e6 / float Stopwatch.Frequency

let roleCount role (frame: TeamFrame) =
    frame.DefensiveRoles
    |> Array.filter (fun value -> string value = role)
    |> Array.length

let prepare side opened condition =
    let ctx, world = DefensiveTransitionFixtures.scenario side opened condition
    let team = MatchWorld.team world side
    let opponent = MatchWorld.team world (MatchWorld.oppOf side)

    let loss =
        { Tick = world.State.Tick
          Data = EventData.Possession(MatchWorld.oppOf side, opponent.RosterIds[9]) }
    // Queue the actual loss on an off-cadence entry, so response timing includes reducer-to-consumer visibility.
    world.AppliedEventIds.Remove(MatchEvent.id loss) |> ignore

    world.State <-
        { world.State with
            Possession =
                { world.State.Possession with
                    LastGainer = Some side }
            Ball =
                { world.State.Ball with
                    Control = BallControl.Controlled(side, team.RosterIds[9]) } }

    let buffer = DefensiveTransitionFixtures.buffer ()
    buffer.Events.Add loss
    ctx, world, buffer

let corpus () =
    [| for side in [ ClubSide.Home; ClubSide.Away ] do
           for opened in [ false; true ] do
               for condition in [ 1.0f; 0.3f ] do
                   for seed in [ 42UL; 112UL; 941UL ] do
                       let ctx, world, buffer = prepare side opened condition
                       let team = MatchWorld.team world side
                       let context = DefensiveTransitionFixtures.systemContext ctx
                       let schedule = ScheduleCompiler.compile Scheduler.systems
                       let output = Array.zeroCreate<MatchEvent> 256
                       let start = world.State.Tick
                       let mutable rng = Rng.ofSeed seed
                       let mutable response, pressers, covers, depth, screens = -1, 0, 0, 0, 0
                       let mutable approach = 0.0
                       let mutable recovered = false
                       let mutable progression, threat = 0.0, 0.0

                       for _ in 1..320 do
                           let struct (next, _, count) =
                               Stepper.stepInto schedule context rng world buffer output

                           rng <- next

                           if
                               response < 0
                               && (MatchWorld.activeSlots team.Frame
                                   |> Array.exists (fun slot ->
                                       slot <> 0 && team.Frame.Intent.Kind[slot] <> IntentCode.None))
                           then
                               response <- int (world.State.Tick - start) - 1
                               pressers <- team.Frame.Intent.Kind |> Array.filter ((=) IntentCode.Press) |> Array.length
                               covers <- roleCount "Cover" team.Frame
                               depth <- roleCount "ProtectDepth" team.Frame + roleCount "Marker" team.Frame
                               screens <- roleCount "Screen" team.Frame

                               let first =
                                   team.Frame.DefensiveRoles
                                   |> Array.tryFindIndex (fun role -> string role = "FirstDefender")

                               approach <-
                                   first
                                   |> Option.map (fun slot ->
                                       (float world.State.Ball.Position.X - float team.Frame.Intent.TargetX[slot])
                                       * MatchMath.goalDirection side)
                                   |> Option.defaultValue 0.0

                           for i in 0 .. count - 1 do
                               match output[i].Data with
                               | EventData.Possession(s, _) when s = side -> recovered <- true
                               | _ -> ()

                           if not recovered && world.State.Flow = MatchFlow.Live then
                               progression <-
                                   max progression (-float world.State.Ball.Position.X * MatchMath.goalDirection side)

                               let distance =
                                   MatchMath.distanceToGoal
                                       (MatchWorld.oppOf side)
                                       world.State.Ball.Position.X
                                       world.State.Ball.Position.Y

                               threat <- max threat (MatchMath.xGFrom distance 65.0 1.0)

                       yield
                           {| Side = string side
                              Open = opened
                              Condition = condition
                              Seed = seed
                              ResponseTicks = response
                              FirstPressers = pressers
                              Covers = covers
                              Depth = depth
                              Screens = screens
                              ApproachGoalSideMetres = approach
                              Recovered = recovered
                              Progression = progression
                              PeakThreatProxy = threat
                              ForwardPlayers =
                               MatchWorld.activeSlots team.Frame
                               |> Array.filter (fun slot ->
                                   float team.Frame.PosX[slot] * MatchMath.goalDirection side > 0.0)
                               |> Array.length |} |]

let summarize (samples: float[]) allocations retained calls =
    Array.sortInPlace samples

    {| Calls = calls
       BytesPerCall = float allocations / float calls
       RetainedManagedDelta = retained
       P50 = samples[samples.Length / 2]
       P95 = samples[samples.Length * 95 / 100]
       P99 = samples[samples.Length * 99 / 100]
       Max = samples[samples.Length - 1] |}

let fullTicks () =
    let ctx, world, buffer = prepare ClubSide.Home false 1.0f
    let context = DefensiveTransitionFixtures.systemContext ctx
    let mutable evaluations = 0

    let systems =
        Scheduler.systems
        |> List.map (fun system ->
            if system.Name <> "TeamTransition" then
                system
            else
                { system with
                    Step =
                        fun ctx rng world buffer ->
                            evaluations <- evaluations + 1
                            system.Step ctx rng world buffer })

    let schedule = ScheduleCompiler.compile systems
    let output = Array.zeroCreate<MatchEvent> 256
    let samples = Array.zeroCreate<float> 20000
    let mutable rng = Rng.ofSeed 42UL

    let step () =
        let struct (next, _, _) = Stepper.stepInto schedule context rng world buffer output
        rng <- next

    for _ in 1..8000 do
        step ()

    evaluations <- 0
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    let retainedBefore = GC.GetTotalMemory true
    let before = GC.GetAllocatedBytesForCurrentThread()

    for i in 0 .. samples.Length - 1 do
        let timestamp = Stopwatch.GetTimestamp()
        step ()
        samples[i] <- micros timestamp

    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
    let retained = GC.GetTotalMemory true - retainedBefore
    GC.KeepAlive world
    let summary = summarize samples allocated retained samples.Length

    {| Ticks = summary
       TacticalEvaluations = evaluations
       EvaluationFrequency = float evaluations / float samples.Length
       RetainedObservations = world.Observation.Count
       TotalObservations = world.Observation.Total |}

let frozen (name: string) =
    let ctx, world, buffer = prepare ClubSide.Home false 1.0f
    let context = DefensiveTransitionFixtures.systemContext ctx
    Stepper.step Scheduler.systems context (Rng.ofSeed 42UL) world buffer |> ignore

    match Scheduler.systems |> List.tryFind (fun system -> system.Name = name) with
    | None -> None
    | Some system ->
        let step () =
            system.Step context (Rng.ofSeed 42UL) world buffer |> ignore

        for _ in 1..8000 do
            step ()

        let samples = Array.zeroCreate<float> 20000
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()
        let retainedBefore = GC.GetTotalMemory true
        let before = GC.GetAllocatedBytesForCurrentThread()

        for i in 0 .. samples.Length - 1 do
            let timestamp = Stopwatch.GetTimestamp()
            step ()
            samples[i] <- micros timestamp

        let allocated = GC.GetAllocatedBytesForCurrentThread() - before
        let retained = GC.GetTotalMemory true - retainedBefore
        GC.KeepAlive world

        Some
            {| Costs = summarize samples allocated retained samples.Length
               RetainedObservations = world.Observation.Count
               TotalObservations = world.Observation.Total
               BufferedEvents = buffer.Events.Count |}

[<EntryPoint>]
let main _ =
    let result =
        {| Runtime = Environment.Version.ToString()
           Corpus = corpus ()
           FullTicks = fullTicks ()
           TacticalOwner = frozen "TeamTransition"
           ShapeAndCoordination = frozen "TeamPosition" |}

    printfn "%s" (json result)
    0
