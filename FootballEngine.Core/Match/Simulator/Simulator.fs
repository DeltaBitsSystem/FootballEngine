namespace FootballEngine.Core.Match

open System

/// The public simulator facade. The facade deliberately has no independent
/// stepping loop: batch and interactive callers use the same MatchSession.
module Simulator =

    type RunMode = MatchRunMode
    type MatchOutcome = FootballEngine.Core.Match.MatchOutcome

    let eventsHash = MatchOutcomeHash.eventsHash

    let runMatchResult
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (matchSeed: uint64)
        (mode: RunMode)
        (weightsPath: string option)
        : Result<MatchOutcome, MatchContractError> =
        // The caller supplies the resolved config. Reading a mutable file here
        // would make the same MatchInput produce different results.
        let _ = weightsPath

        match EffectiveConfig.fromBalance ctx.Config with
        | Error error ->
            let messages =
                match error with
                | InvalidWorldConfig errors -> errors
                | _ -> [ sprintf "%A" error ]

            Error(InvalidInputConfig messages)
        | Ok effective ->
            let input =
                { MatchId = matchSeed
                  Context = ctx
                  Seed = matchSeed
                  EffectiveConfig = effective
                  EngineVersion = "football-engine-core-01" }

            match MatchSession.create systems input mode with
            | Error error -> Error error
            | Ok session -> MatchSession.finish session

    /// Existing synchronous callers still receive a value, but terminal safety
    /// failures are explicit exceptions rather than a fabricated MatchEnded state.
    let runMatch systems ctx matchSeed mode weightsPath : MatchOutcome =
        match runMatchResult systems ctx matchSeed mode weightsPath with
        | Ok outcome -> outcome
        | Error error -> raise (InvalidOperationException(sprintf "match simulation failed: %A" error))
