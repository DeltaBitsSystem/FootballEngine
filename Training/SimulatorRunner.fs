namespace Training

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Runs headless matches against the Core engine. Deterministic: the seed
/// controls the whole match, so the same config always yields the same metrics.
module SimulatorRunner =

    let private maxTicks = 600000

    let runOne (cfg: BalanceConfig) (seed: uint64) : World =
        let ctx =
            { TestBuilders.defaultCtx () with
                Config = cfg }

        let world = MatchFactory.create ctx

        let sctx =
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }

        let buf =
            { Events = ResizeArray()
              Flags = { EventFlags.Bits = 0UL }
              Applied = ResizeArray() }

        let mutable rng = Rng.ofSeed seed
        let mutable safety = 0

        while safety < maxTicks && world.State.Flow <> MatchFlow.MatchEnded do
            let rng', _, _ = Stepper.step Scheduler.systems sctx rng world buf
            rng <- rng'
            safety <- safety + 1

        world

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
