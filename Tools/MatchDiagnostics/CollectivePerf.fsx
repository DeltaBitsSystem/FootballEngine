#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open System
open System.Diagnostics
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let unwrap =
  function
  | Ok value -> value
  | Error error -> failwithf "%A" error

let percentile p (items: float[]) =
  let sorted = Array.sort items
  sorted[int (Math.Ceiling(p * float sorted.Length)) - 1]

let report label (samples: ResizeArray<float>) (allocated: int64) iterations =
  let values = samples.ToArray()

  printfn
    "%s n=%d median=%.3f us p95=%.3f us p99=%.3f us bytes/iteration=%.1f"
    label
    values.Length
    (percentile 0.5 values)
    (percentile 0.95 values)
    (percentile 0.99 values)
    (float allocated / float iterations)

let ctx = TestBuilders.formationCtx ()

let system =
  { Ctx = ctx
    Clock = SimulationClock.defaultClock }

let buffer =
  { Events = ResizeArray()
    Flags = { Bits = 0UL }
    Applied = ResizeArray() }

let world = MatchFactory.create ctx

world.State <-
  { world.State with
      Flow = MatchFlow.Live
      Ball =
        { world.State.Ball with
            Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[9]) } }

for _ in 1..100 do
  TeamPosition.step system (Rng.ofSeed 1UL) world buffer |> ignore

let kernel = ResizeArray<float>()
let beforeKernel = GC.GetAllocatedBytesForCurrentThread()

for _ in 1..1000 do
  let start = Stopwatch.GetTimestamp()
  TeamPosition.step system (Rng.ofSeed 1UL) world buffer |> ignore
  kernel.Add(float (Stopwatch.GetTimestamp() - start) * 1000000.0 / float Stopwatch.Frequency)

let kernelBytes = GC.GetAllocatedBytesForCurrentThread() - beforeKernel
report "TeamPosition/tick" kernel kernelBytes kernel.Count

let samples = ResizeArray<float>()
let mutable bytes = 0L
let mutable ticks = 0

let batchSize =
  if Environment.GetEnvironmentVariable("COLLECTIVE_PERF_TICK") = "1" then
    1
  else
    200

for seed in [ 1UL; 2UL; 3UL ] do
  let input =
    { MatchId = seed
      Seed = seed
      Context = ctx
      EffectiveConfig = EffectiveConfig.defaultConfig
      EngineVersion = "collective-perf" }

  let session =
    MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

  session.World.Observation.Enabled <- false

  while session.World.State.Tick < 20000<subtick>
        && session.World.State.Flow <> MatchFlow.MatchEnded do
    let count = min batchSize (20000 - int session.World.State.Tick)
    let before = GC.GetAllocatedBytesForCurrentThread()
    let start = Stopwatch.GetTimestamp()

    MatchSession.advanceTo (session.World.State.Tick + count * 1<subtick>) session
    |> unwrap
    |> ignore

    samples.Add(
      float (Stopwatch.GetTimestamp() - start) * 1000000.0
      / float Stopwatch.Frequency
      / float count
    )

    bytes <- bytes + GC.GetAllocatedBytesForCurrentThread() - before
    ticks <- ticks + count

report
  (if batchSize = 1 then
     "session/tick (single advanceTo)"
   else
     "session/tick (200-tick batch averages)")
  samples
  bytes
  ticks
