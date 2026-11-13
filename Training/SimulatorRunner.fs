namespace Training

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Runs headless matches against the Core engine. Deterministic: the seed
/// controls the whole match, so the same config always yields the same metrics.
module SimulatorRunner =

    /// The same session the game and `Simulator.runMatch` play, so calibration measures exactly the match a career
    /// plays, input and config validation included. A tool may stop on an invalid config.
    let private sessionOf (systems: SystemSpec list) (ctx: MatchContext) (seed: uint64) =
        let cfg = ctx.Config

        let effective =
            match EffectiveConfig.fromBalance cfg with
            | Ok effective -> effective
            | Error error -> failwithf "invalid balance config: %A" error

        let input =
            { MatchId = seed
              Context = ctx
              Seed = seed
              EffectiveConfig = effective
              EngineVersion = "football-engine-core-01" }

        match MatchSession.create systems input MatchRunMode.Fast with
        | Ok session -> session
        | Error error -> failwithf "match session rejected: %A" error

    /// The calibration corpus: every fixture of a generated world, with squads, formations and quality gaps
    /// built the way a career builds them. The test lineup crowds every base position into one quadrant, so
    /// numbers measured on it describe a match no career plays.
    let private world =
        lazy
            (let gs = FootballEngine.Core.Tests.World.WorldGenerationTests.generate 0xC02UL []

             let fixtures =
                 gs.Competitions
                 |> Map.toSeq
                 |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq |> Seq.map snd)
                 |> Seq.filter (fun fixture ->
                     FootballEngine.World.Phases.MatchContextBuilder.build gs fixture FootballEngine.Core.Match.BalanceConfig.defaultConfig
                     |> Option.isSome)
                 |> Array.ofSeq

             gs, fixtures)

    /// The corpus fixture a seed plays, under `cfg`.
    let corpusContext (cfg: BalanceConfig) (seed: uint64) : MatchContext =
        let gs, fixtures = world.Value
        let fixture = fixtures[int (seed % uint64 fixtures.Length)]

        match FootballEngine.World.Phases.MatchContextBuilder.build gs fixture cfg with
        | Some ctx -> ctx
        | None -> failwithf "corpus fixture %A has no context" fixture.Id

    let private session (systems: SystemSpec list) (cfg: BalanceConfig) (seed: uint64) =
        sessionOf systems (corpusContext cfg seed) seed

    /// A whole match between the context's lineups, with every event it recorded.
    let runContext (ctx: MatchContext) (seed: uint64) : MatchOutcome =
        match MatchSession.finish (sessionOf Scheduler.systems ctx seed) with
        | Ok outcome -> outcome
        | Error error -> failwithf "match did not finish: %A" error

    let private finish (session: MatchSession) =
        match MatchSession.finish session with
        | Ok outcome -> outcome.Final
        | Error error -> failwithf "match did not finish: %A" error

    /// Ticks played, for per-tick timings.
    let runWith (ownThread: bool) (systems: SystemSpec list) (cfg: BalanceConfig) (seed: uint64) : int =
        let session = session systems cfg seed

        if ownThread then
            MatchSession.runCognitionOnOwnThread session

        int (finish session).State.Tick

    let runOne (cfg: BalanceConfig) (seed: uint64) : World = finish (session Scheduler.systems cfg seed)

    /// Same match as `runOne`, handing every tick's applied events to `observe`.
    let runObservedOn (ownThread: bool) (cfg: BalanceConfig) (seed: uint64) (observe: World -> MatchEvent seq -> unit) : World =
        let session = session Scheduler.systems cfg seed

        if ownThread then
            MatchSession.runCognitionOnOwnThread session

        while session.World.State.Flow <> MatchFlow.MatchEnded do
            match MatchSession.advanceTo (session.World.State.Tick + 1<subtick>) session with
            | Ok advance -> observe session.World advance.Events
            | Error error -> failwithf "match did not advance: %A" error

        finish session

    /// A whole match between the context's lineups, handing every tick's applied events to `observe`.
    let runContextObserved (ctx: MatchContext) (seed: uint64) (observe: World -> MatchEvent seq -> unit) : World =
        let session = sessionOf Scheduler.systems ctx seed

        while session.World.State.Flow <> MatchFlow.MatchEnded do
            match MatchSession.advanceTo (session.World.State.Tick + 1<subtick>) session with
            | Ok advance -> observe session.World advance.Events
            | Error error -> failwithf "match did not advance: %A" error

        finish session

    let runObserved cfg seed observe = runObservedOn false cfg seed observe

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
