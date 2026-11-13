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

    /// The one way a whole match starts from a context: the config is validated, the input stamped and a session
    /// opened. Careers, calibration and benchmarks all play the session this returns.
    let start
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (matchSeed: uint64)
        (mode: RunMode)
        : Result<MatchSession, MatchContractError> =
        match EffectiveConfig.fromBalance ctx.Config with
        | Error(InvalidWorldConfig errors) -> Error(InvalidInputConfig errors)
        | Error error -> Error(InvalidInputConfig [ sprintf "%A" error ])
        | Ok effective -> MatchSession.create systems (MatchInput.create matchSeed matchSeed effective ctx) mode

    let runMatchPlaced
        (placement: CognitionPlacement)
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (matchSeed: uint64)
        (mode: RunMode)
        : Result<MatchOutcome, MatchContractError> =
        match start systems ctx matchSeed mode with
        | Error error -> Error error
        | Ok session ->
            if placement = CognitionPlacement.OwnThread then
                MatchSession.runCognitionOnOwnThread session

            MatchSession.finish session

    let runMatch systems ctx matchSeed mode =
        runMatchPlaced CognitionPlacement.WithBody systems ctx matchSeed mode
