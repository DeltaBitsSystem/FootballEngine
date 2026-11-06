#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open System
open System.Diagnostics
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let ctx = TestBuilders.formationCtx ()
let system = { Ctx = ctx; Clock = SimulationClock.defaultClock }
let wanted = set [ "PlayerPerception"; "PlayerIntent"; "PlayerSteering"; "PlayerDecision"; "Actions" ]
for phase in 0..7 do
    let trace = ResizeArray<string * int>()
    let systems =
        Scheduler.systems
        |> List.filter (fun spec -> wanted.Contains spec.Name)
        |> List.map (fun spec ->
            { spec with
                Step =
                    fun ctx rng world buffer ->
                        trace.Add(spec.Name, int world.State.Tick)
                        spec.Step ctx rng world buffer })
    let world = MatchFactory.create ctx
    world.State <- { world.State with Tick = phase * 1<subtick>; Flow = MatchFlow.Live }
    let buffer = { Events = ResizeArray(); Flags = { Bits = 0UL }; Applied = ResizeArray() }
    buffer.Events.Add { Tick = world.State.Tick; Data = EventData.Possession(ClubSide.Home, world.Home.RosterIds[5]) }
    let mutable rng = Rng.ofSeed 1UL
    for _ in 0..7 do
        let next, _, _ = Stepper.step systems system rng world buffer
        rng <- next
    let first name = trace |> Seq.tryPick (fun (n, tick) -> if n = name then Some (tick - phase) else None)
    printfn "phase=%d first-execution offsets perception=%A intent=%A steering=%A decision=%A actions=%A" phase (first "PlayerPerception") (first "PlayerIntent") (first "PlayerSteering") (first "PlayerDecision") (first "Actions")

let unwrap = function Ok value -> value | Error error -> failwithf "%A" error
let baselineSchedule =
    Scheduler.systems
    |> List.map (fun spec ->
        let triggers =
            match spec.Name with
            | "PlayerPerception" -> [ Trigger.WhenLive(Trigger.EveryN 8<tickDelta>) ]
            | "PlayerSteering" -> [ Trigger.WhenMovementAllowed(Trigger.EveryN 2<tickDelta>) ]
            | "PlayerDecision" -> [ Trigger.WhenLive(Trigger.EveryN 4<tickDelta>) ]
            | _ -> spec.Triggers
        { spec with Triggers = triggers })

// This comparison isolates scheduling. Both versions use the current reducers and decision guard.
for label, schedule in [ "baseline-schedule", baselineSchedule; "reactive", Scheduler.systems ] do
    for phase in 0..7 do
        let world = MatchFactory.create ctx
        world.Home.Frame.PosX[5] <- 0.0f
        world.Home.Frame.PosY[5] <- 0.0f
        world.Home.Frame.Cognitive[5] <- -1.0f
        world.State <- { world.State with Tick = phase * 1<subtick>; Flow = MatchFlow.Live; Ball = TestBuilders.emptyBall }
        let trace = ResizeArray<string * int>()
        let mutable proposal = None
        let mutable execution = None
        let mutable strikeRequest = None
        let wrapped =
            schedule |> List.map (fun spec ->
                { spec with
                    Step = fun ctx rng world buffer ->
                        let tick = int world.State.Tick - phase
                        if wanted.Contains spec.Name then trace.Add(spec.Name, tick)
                        let before = world.Home.PendingActions.ContainsKey 5
                        if spec.Name = "Actions" && before && proposal.IsNone then proposal <- Some tick
                        let next = spec.Step ctx rng world buffer
                        if spec.Name = "Actions" && before && not (world.Home.PendingActions.ContainsKey 5) && execution.IsNone then execution <- Some tick
                        if spec.Name = "Actions" && world.State.Ball.PendingStrike.IsSome && strikeRequest.IsNone then strikeRequest <- Some tick
                        next })
        let buffer = { Events = ResizeArray(); Flags = { Bits = 0UL }; Applied = ResizeArray() }
        buffer.Events.Add { Tick = world.State.Tick; Data = EventData.Possession(ClubSide.Home, world.Home.RosterIds[5]) }
        let mutable rng = Rng.ofSeed 1UL
        for _ in 0..7 do
            let next, _, _ = Stepper.step wrapped system rng world buffer
            rng <- next
        let first name = trace |> Seq.tryPick (fun (n, tick) -> if n = name then Some tick else None)
        printfn "%s phase=%d first-perception=%A first-intent=%A first-decision=%A choice-at-actions=%A choice-executed=%A physical-strike-request=%A" label phase (first "PlayerPerception") (first "PlayerIntent") (first "PlayerDecision") proposal execution strikeRequest

let runSession label (schedule: SystemSpec list) =
    let mutable ticks, events, calls, bytes = 0, 0, 0, 0L
    let mutable restartTicks, restartPerceptions = 0, 0
    let wrapped =
        schedule |> List.map (fun spec ->
            if not (wanted.Contains spec.Name) && spec.Name <> "PlayerBodyPhysics" then spec
            else
                { spec with
                    Step = fun ctx rng world buffer ->
                        if wanted.Contains spec.Name then calls <- calls + 1
                        match world.State.Flow with
                        | MatchFlow.Restart _ when spec.Name = "PlayerBodyPhysics" -> restartTicks <- restartTicks + 1
                        | MatchFlow.Restart _ when spec.Name = "PlayerPerception" -> restartPerceptions <- restartPerceptions + 1
                        | _ -> ()
                        spec.Step ctx rng world buffer })
    let stopwatch = Stopwatch.StartNew()
    for seed in [ 1UL; 2UL; 3UL ] do
        let input = { MatchId = seed; Seed = seed; Context = ctx; EffectiveConfig = EffectiveConfig.defaultConfig; EngineVersion = "reaction-probe" }
        let session = MatchSession.create wrapped input MatchRunMode.Fast |> unwrap
        session.World.Observation.Enabled <- false
        let before = GC.GetAllocatedBytesForCurrentThread()
        MatchSession.advanceTo 10000<subtick> session |> unwrap |> ignore
        bytes <- bytes + GC.GetAllocatedBytesForCurrentThread() - before
        ticks <- ticks + int session.World.State.Tick
        events <- events + session.Events.Count
    stopwatch.Stop()
    printfn "%s session seeds=1,2,3 horizon=10000 ticks=%d events=%d player-system-calls=%d restart-ticks=%d restart-perception-calls=%d us/tick=%.3f bytes/tick=%.1f" label ticks events calls restartTicks restartPerceptions (stopwatch.Elapsed.TotalMilliseconds * 1000.0 / float ticks) (float bytes / float ticks)

let runKernel label (schedule: SystemSpec list) =
    let mutable calls = 0
    let wrapped =
        schedule |> List.map (fun spec ->
            if not (wanted.Contains spec.Name) then spec
            else { spec with Step = fun ctx rng world buffer -> calls <- calls + 1; spec.Step ctx rng world buffer })
    let compiled = ScheduleCompiler.compile wrapped
    let mutable elapsed, allocations = 0L, 0L
    for seed in [ 1UL; 2UL; 3UL ] do
        let world = MatchFactory.create ctx
        world.State <- { world.State with Flow = MatchFlow.Live; Ball = TestBuilders.emptyBall }
        world.Observation.Enabled <- false
        let buffer = { Events = ResizeArray(); Flags = { Bits = 0UL }; Applied = ResizeArray() }
        let output = Array.zeroCreate<MatchEvent> 256
        let mutable rng = Rng.ofSeed seed
        let before = GC.GetAllocatedBytesForCurrentThread()
        let start = Stopwatch.GetTimestamp()
        for tick in 0..3999 do
            // Fixed committed-event workload; generated events are intentionally not fed back.
            buffer.Events.Clear()
            if tick % 8 = 1 then
                buffer.Events.Add { Tick = world.State.Tick; Data = EventData.Possession(ClubSide.Home, world.Home.RosterIds[5]) }
            let next, _, _ = Stepper.stepInto compiled system rng world buffer output
            rng <- next
        elapsed <- elapsed + Stopwatch.GetTimestamp() - start
        allocations <- allocations + GC.GetAllocatedBytesForCurrentThread() - before
    printfn "%s kernel ticks=12000 injected-possessions=1500 calls=%d us/tick=%.3f bytes/tick=%.1f" label calls (float elapsed * 1000000.0 / float Stopwatch.Frequency / 12000.0) (float allocations / 12000.0)

let runFixedSession label (schedule: SystemSpec list) =
    let mutable calls, allocations, elapsed = 0, 0L, 0L
    let wrapped =
        schedule |> List.map (fun spec ->
            if not (wanted.Contains spec.Name) then spec
            else { spec with Step = fun ctx rng world buffer -> calls <- calls + 1; spec.Step ctx rng world buffer })
    for seed in [ 1UL; 2UL; 3UL ] do
        let input = { MatchId = seed; Seed = seed; Context = ctx; EffectiveConfig = EffectiveConfig.defaultConfig; EngineVersion = "reaction-fixed" }
        let session = MatchSession.create wrapped input MatchRunMode.Fast |> unwrap
        session.World.State <- { session.World.State with Flow = MatchFlow.Live; Ball = TestBuilders.emptyBall }
        session.World.Observation.Enabled <- false
        let before = GC.GetAllocatedBytesForCurrentThread()
        let start = Stopwatch.GetTimestamp()
        for tick in 0..3999 do
            session.Buffer.Events.Clear()
            if tick % 8 = 1 then
                session.Buffer.Events.Add { Tick = session.World.State.Tick; Data = EventData.Possession(ClubSide.Home, session.World.Home.RosterIds[5]) }
            MatchSession.advanceTo (session.World.State.Tick + 1<subtick>) session |> unwrap |> ignore
        elapsed <- elapsed + Stopwatch.GetTimestamp() - start
        allocations <- allocations + GC.GetAllocatedBytesForCurrentThread() - before
    printfn "%s fixed-session ticks=12000 injected-possessions=1500 calls=%d us/tick=%.3f bytes/tick=%.1f" label calls (float elapsed * 1000000.0 / float Stopwatch.Frequency / 12000.0) (float allocations / 12000.0)

// Warm both schedules before collecting, then reverse their order on the second pass.
runKernel "warmup-baseline" baselineSchedule
runKernel "warmup-reactive" Scheduler.systems
runSession "warmup-baseline" baselineSchedule
runSession "warmup-reactive" Scheduler.systems
runFixedSession "warmup-baseline" baselineSchedule
runFixedSession "warmup-reactive" Scheduler.systems
runKernel "baseline-schedule" baselineSchedule
runKernel "reactive" Scheduler.systems
runKernel "reactive-repeat" Scheduler.systems
runKernel "baseline-schedule-repeat" baselineSchedule
runFixedSession "baseline-schedule" baselineSchedule
runFixedSession "reactive" Scheduler.systems
runFixedSession "reactive-repeat" Scheduler.systems
runFixedSession "baseline-schedule-repeat" baselineSchedule
runSession "baseline-schedule" baselineSchedule
runSession "reactive" Scheduler.systems
runSession "reactive-repeat" Scheduler.systems
runSession "baseline-schedule-repeat" baselineSchedule
