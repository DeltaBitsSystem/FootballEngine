#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"

open System
open System.Diagnostics
open System.Numerics
open FootballEngine.Core.Match

// A motion-kernel diagnostic isolates constant-size physics; it is not a full-match or GPU benchmark.
let cfg = BalanceConfig.defaultBall

let ball: BallState =
  { Position =
      { X = 0.0<meter>
        Y = 0.0<meter>
        Z = 1.0<meter>
        Vx = 15.0<meterPerSecond>
        Vy = 2.0<meterPerSecond>
        Vz = 4.0<meterPerSecond> }
    Radius = cfg.Radius
    Spin =
      { X = 1.0<radianPerSecond>
        Y = 60.0<radianPerSecond>
        Z = 5.0<radianPerSecond> }
    Orientation = Quaternion.Identity
    FootMotion = None
    PendingStrike = None
    Control = BallControl.Airborne
    Trajectory = None
    LastTouch = None
    StationarySince = None
    GKHoldSince = None
    ArrivalGate = None
    PreviousPosition = ValueNone
    Impulse = None }

let run count =
  let mutable current = ball
  let before = GC.GetAllocatedBytesForCurrentThread()
  let start = Stopwatch.GetTimestamp()

  for tick = 1 to count do
    if tick % 200 = 0 then
      current <- ball

    current <- BallDynamics.integrate cfg 0.025 (tick * 1<subtick>) current

  let elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds
  elapsed * 1e6 / float count, float (GC.GetAllocatedBytesForCurrentThread() - before) / float count, current

run 20000 |> ignore

for _ = 1 to 5 do
  let nanoseconds, bytes, last = run 500000

  printfn
    "{\"nanosecondsPerTick\":%.1f,\"allocatedBytesPerTick\":%.1f,\"checksum\":%.6f}"
    nanoseconds
    bytes
    (float last.Position.X)
