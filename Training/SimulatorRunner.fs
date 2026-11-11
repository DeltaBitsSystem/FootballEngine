namespace Training

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Runs headless matches against the Core engine. Deterministic: the seed
/// controls the whole match, so the same config always yields the same metrics.
module SimulatorRunner =

    let private maxTicks = 600000

    /// The match loop shared by every runner. It drives the runtime `stepInto` with a schedule compiled once,
    /// as a live session does; the `Stepper.step` test adapter recompiles the table and copies events every tick,
    /// which cost a tenth of a balance run.
    let private play (systems: SystemSpec list) (cfg: BalanceConfig) (seed: uint64) (observe: World -> MatchEvent[] -> int -> unit) =
        let ctx = { TestBuilders.defaultCtx () with Config = cfg }
        let world = MatchFactory.create ctx
        let sctx = { Ctx = ctx; Clock = SimulationClock.defaultClock }
        let buf = { Events = ResizeArray(); Flags = { EventFlags.Bits = 0UL }; Applied = ResizeArray() }
        let schedule = ScheduleCompiler.compile systems
        let output = Array.zeroCreate<MatchEvent> 256
        let mutable rng = Rng.ofSeed seed
        let mutable ticks = 0

        while ticks < maxTicks && world.State.Flow <> MatchFlow.MatchEnded do
            let rng', _, count = Stepper.stepInto schedule sctx rng world buf output
            rng <- rng'
            observe world output count
            ticks <- ticks + 1

        world, ticks

    let runWith (systems: SystemSpec list) (cfg: BalanceConfig) (seed: uint64) : int =
        play systems cfg seed (fun _ _ _ -> ()) |> snd

    let runOne (cfg: BalanceConfig) (seed: uint64) : World =
        play Scheduler.systems cfg seed (fun _ _ _ -> ()) |> fst

    /// Same match as `runOne`, handing every tick's applied events to `observe`.
    let runObserved (cfg: BalanceConfig) (seed: uint64) (observe: World -> MatchEvent seq -> unit) : World =
        play Scheduler.systems cfg seed (fun world output count -> observe world (Seq.take count output)) |> fst

    /// Runs `n` matches with different seeds and returns their metrics.
    /// Matches are independent (own world/buffer/rng), so they run in parallel.
    let runMatches (cfg: BalanceConfig) (seedBase: uint64) (n: int) : MatchMetrics list =
        let seeds = [| for i in 0 .. n - 1 -> seedBase + uint64 i * 1013UL |]
        let worlds = seeds |> Array.Parallel.map (fun s -> runOne cfg s)
        worlds |> Array.map Metrics.ofWorld |> Array.toList

    /// Quick evaluation: 1 match (deterministic seed) for fast iterations.
    let evalQuick (cfg: BalanceConfig) (seedBase: uint64) : float =
        let m = runMatches cfg seedBase 1 |> List.head
        ErrorCalculator.error [ m ]

    /// Stable evaluation: `n` matches averaged.
    let evalStable (cfg: BalanceConfig) (seedBase: uint64) (n: int) : float =
        let ms = runMatches cfg seedBase n
        ErrorCalculator.error ms
