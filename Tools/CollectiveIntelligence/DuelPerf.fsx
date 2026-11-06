#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open System
open System.Diagnostics
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let ctx = TestBuilders.formationCtx ()
let sctx = { Ctx = ctx; Clock = SimulationClock.defaultClock }
let run label distance =
    let world = MatchFactory.create ctx
    for slot in MatchWorld.activeSlots world.Away.Frame do
        world.Away.Frame.PosX[slot] <- 20.0f
        world.Away.Frame.PosY[slot] <- 20.0f
    world.Home.Frame.PosX[5] <- 0.0f
    world.Home.Frame.PosY[5] <- 0.0f
    world.Away.Frame.PosX[1] <- distance
    world.Away.Frame.PosY[1] <- 0.0f
    world.State <- { world.State with Flow = MatchFlow.Live; Ball = { TestBuilders.emptyBall with Control = BallControl.Controlled(ClubSide.Home, world.Home.RosterIds[5]) } }
    world.Observation.Enabled <- false
    let initial = world.State
    let events: EventBuffer = { Events = ResizeArray(); Applied = ResizeArray(); Flags = { Bits = 0UL } }
    let rng = Rng.ofSeed 42UL
    let invoke () =
        world.State <- initial
        events.Events.Clear()
        Duel.adjudicate sctx rng world events ClubSide.Home 5 |> ignore
    for _ in 1 .. 1000 do invoke ()
    let timings = Array.zeroCreate<float> 10000
    let bytes = GC.GetAllocatedBytesForCurrentThread()
    for i in 0 .. timings.Length - 1 do
        let start = Stopwatch.GetTimestamp()
        invoke ()
        timings[i] <- float (Stopwatch.GetTimestamp() - start) * 1000000.0 / float Stopwatch.Frequency
    let allocated = GC.GetAllocatedBytesForCurrentThread() - bytes
    Array.sortInPlace timings
    printfn "%s median=%.3f us p95=%.3f us p99=%.3f us bytes/call=%.1f emitted=%d" label timings[4999] timings[9499] timings[9899] (float allocated / float timings.Length) events.Events.Count

run "near" 0.5f
run "outside-foot-inside-old-radius" 2.0f
