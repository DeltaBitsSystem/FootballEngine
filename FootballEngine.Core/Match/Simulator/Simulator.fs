namespace FootballEngine.Core.Match

/// The public simulator facade. The facade deliberately has no independent
/// stepping loop: batch and interactive callers use the same MatchSession.
module Simulator =

    type RunMode = MatchRunMode
    type MatchOutcome = FootballEngine.Core.Match.MatchOutcome

    let eventsHash = MatchOutcomeHash.eventsHash

    /// Where a match's cognition runs; the match is the same either way, only its wall time differs.
    [<RequireQualifiedAccess>]
    type CognitionPlacement =
        | WithBody
        | OwnThread

    let runMatchPlaced
        (placement: CognitionPlacement)
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (matchSeed: uint64)
        (mode: RunMode)
        : Result<MatchOutcome, MatchContractError> =
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
            | Ok session ->
                if placement = CognitionPlacement.OwnThread then
                    MatchSession.runCognitionOnOwnThread session

                MatchSession.finish session

    let runMatch systems ctx matchSeed mode =
        runMatchPlaced CognitionPlacement.WithBody systems ctx matchSeed mode
