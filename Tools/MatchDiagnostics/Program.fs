module FootballEngine.Tools.MatchDiagnostics

open System
open System.IO
open FootballEngine
open FootballEngine.Core.Match

type Inspection =
    { Query: ObservationQuery
      Chain: ObservationId option
      Audit: bool
      Json: bool
      Summary: bool
      Verify: bool
      Limit: int }

let parseOptions arguments =
    let initial =
        { Query =
            { Actor = None
              Action = None
              Event = None
              FromTick = 0<subtick>
              UntilTick = Int32.MaxValue * 1<subtick> }
          Chain = None
          Audit = false
          Json = false
          Summary = false
          Verify = List.isEmpty arguments
          Limit = 100 }

    let rec parse seen options remaining =
        match remaining with
        | [] ->
            if options.Query.UntilTick < options.Query.FromTick then
                Error "--until must not precede --from."
            elif options.Chain.IsSome && options.Query <> initial.Query then
                Error "--chain traverses its explicit root; query selectors must be used separately."
            elif options.Audit && (options.Chain.IsSome || options.Query <> initial.Query) then
                Error "--audit inspects the entire retained window; chain/query selectors must be used separately."
            else
                Ok options
        | flag :: _ when Set.contains flag seen -> Error(sprintf "Repeated option: %s" flag)
        | "--summary" :: rest -> parse (Set.add "--summary" seen) { options with Summary = true } rest
        | "--verify" :: rest -> parse (Set.add "--verify" seen) { options with Verify = true } rest
        | "--json" :: rest -> parse (Set.add "--json" seen) { options with Json = true } rest
        | "--audit" :: rest -> parse (Set.add "--audit" seen) { options with Audit = true } rest
        | flag :: value :: rest ->
            let next =
                match flag with
                | "--actor" ->
                    match Int32.TryParse value with
                    | true, id ->
                        Ok
                            { options with
                                Query =
                                    { options.Query with
                                        Actor = Some(PlayerId id) } }
                    | _ -> Error "--actor requires an int32 player identity."
                | "--action"
                | "--event"
                | "--chain" ->
                    match UInt64.TryParse value with
                    | true, id ->
                        match flag with
                        | "--action" ->
                            Ok
                                { options with
                                    Query =
                                        { options.Query with
                                            Action = Some(ActionId id) } }
                        | "--event" ->
                            Ok
                                { options with
                                    Query =
                                        { options.Query with
                                            Event = Some(EventId id) } }
                        | _ ->
                            Ok
                                { options with
                                    Chain = Some(ObservationId id) }
                    | _ -> Error(sprintf "%s requires a uint64 identity." flag)
                | "--from"
                | "--until" ->
                    match Int32.TryParse value with
                    | true, tick when tick >= 0 ->
                        let tick = tick * 1<subtick>

                        if flag = "--from" then
                            Ok
                                { options with
                                    Query = { options.Query with FromTick = tick } }
                        else
                            Ok
                                { options with
                                    Query = { options.Query with UntilTick = tick } }
                    | _ -> Error(sprintf "%s requires a non-negative int32 tick." flag)
                | "--limit" ->
                    match Int32.TryParse value with
                    | true, limit when limit > 0 && limit <= 10000 -> Ok { options with Limit = limit }
                    | _ -> Error "--limit must be between 1 and 10000."
                | _ -> Error(sprintf "Unknown option: %s" flag)

            next |> Result.bind (fun updated -> parse (Set.add flag seen) updated rest)
        | [ flag ] -> Error(sprintf "Unknown option or missing value: %s" flag)

    parse Set.empty initial arguments

let private category (entry: ObservationEntry) =
    match entry.Data with
    | AttackEvaluated _ -> "collective opportunity evaluation"
    | AttackResolved _ -> "realized collective learning sample"
    | AttackCooperation _ -> "individual cooperation"
    | TransitionEvaluated _ -> "defensive transition evaluation"
    | TransitionEnded _ -> "defensive transition outcome"
    | DecisionChosen _ -> "chosen decision"
    | DecisionAlternatives _ -> "selective alternatives"
    | ImpulseApplied _ -> "applied impulse"
    | ActionRejected _ -> "rejected execution"
    | ProbabilityResolved _ -> "stochastic resolution"
    | RefereeAssessed _ -> "referee assessment"
    | RestartCreated _ -> "restart award"
    | EventCommitted _ -> "committed event"
    | CommandEffective _ -> "effective command"
    | TacticsEffective _ -> "effective tactics"
    | DuelResolved _ -> "stochastic duel resolution"

let private printSummary (archive: MatchObservationArchive) (entries: ObservationEntry[]) =
    printfn "Match %d seed %d build %s" archive.Input.MatchId archive.Input.Seed archive.Input.EngineVersion

    match MatchSession.deserializeCheckpoint archive.Checkpoint with
    | Ok checkpoint ->
        printfn
            "Checkpoint frontier %d events-hash %d (metadata only unless --verify)."
            (int checkpoint.Frontier)
            checkpoint.EventsHash
    | Error error -> eprintfn "INVALID CHECKPOINT METADATA: %A" error

    printfn
        "Evidence enabled=%b captured=%d retained=%d evicted=%d unlinked=%d matched=%d"
        archive.Evidence.Enabled
        archive.Evidence.Total
        archive.Evidence.Entries.Length
        archive.Evidence.Evicted
        archive.Evidence.Unlinked
        entries.Length

    printfn
        "Only explicit retained causes are available. Missing history is not reconstructed; probability resolutions are not physical contacts."

    entries
    |> Array.countBy category
    |> Array.sortBy fst
    |> Array.iter (fun (kind, count) -> printfn "  %s: %d" kind count)

let private printEntries limit (entries: ObservationEntry[]) =
    for entry in entries |> Array.truncate limit do
        let (ObservationId id) = entry.Id
        printfn "#%d tick=%d cause=%A" id (int entry.Tick) entry.Cause
        printfn "  %s" (Observation.explain entry)

    if entries.Length > limit then
        printfn "Output truncated: %d of %d occurrences shown (--limit, maximum 10000)." limit entries.Length

let private inspect path options =
    try
        match File.ReadAllText path |> MatchObservationArchive.decode with
        | Error error -> Error(sprintf "Cannot decode archive: %A" error)
        | Ok archive ->
            let verified =
                if options.Verify then
                    MatchObservationArchive.restore archive
                    |> Result.map (fun session ->
                        let checkpoint = MatchSession.checkpoint session

                        let message =
                            sprintf
                                "Verified frontier %d events-hash %d RNG and pending commands."
                                (int checkpoint.Frontier)
                                checkpoint.EventsHash

                        if options.Json then
                            eprintfn "%s" message
                        else
                            printfn "%s" message)
                    |> Result.mapError (sprintf "Cannot restore archive: %A")
                else
                    Ok()

            verified
            |> Result.bind (fun () ->
                match options.Audit, options.Chain with
                | true, _ ->
                    if options.Json then
                        printfn "%s" (Observation.exportAuditJson archive.Evidence)
                    else
                        printSummary archive archive.Evidence.Entries
                        let audit = Observation.audit archive.Evidence

                        printfn
                            "Whole-window audit: duplicates=%d missing=%d ambiguous=%d future=%d uncaused=%d"
                            audit.DuplicateOccurrences.Length
                            audit.MissingCauses.Length
                            audit.AmbiguousCauses.Length
                            audit.FutureCauses.Length
                            audit.Uncaused.Length

                        printfn
                            "Uncaused is not automatically invalid; roots may be legitimate. This audit does not certify sporting coverage or detect all cycles."

                        printfn "Use --audit --json for explicit defect identities."

                    Ok()
                | false, Some id ->
                    if options.Json then
                        Observation.exportChainJson id archive.Evidence |> Result.map (printfn "%s")
                    else
                        Observation.chain id archive.Evidence
                        |> Result.map (fun chain ->
                            printSummary archive chain.Entries
                            printEntries options.Limit chain.Entries

                            for cause in chain.MissingCauses do
                                printfn "MISSING CAUSE: %A (outside captured context; no reconstruction)." cause

                            for cycle in chain.Cycles do
                                printfn "INVALID CYCLE: %A." cycle

                            for child, source in chain.FutureCauses do
                                printfn "INVALID FUTURE CAUSE: %A -> %A (excluded)." child source)
                | false, None ->
                    Observation.query options.Query archive.Evidence
                    |> Result.map (fun entries ->
                        if options.Json then
                            printfn
                                "%s"
                                (Observation.exportJson
                                    { archive.Evidence with
                                        Entries = entries })
                        else
                            printSummary archive entries

                            if
                                not options.Summary
                                && not (options.Verify && entries.Length = archive.Evidence.Entries.Length)
                            then
                                printEntries options.Limit entries))
    with
    | :? IOException as error -> Error error.Message
    | :? UnauthorizedAccessException as error -> Error error.Message

[<EntryPoint>]
let main args =
    match Array.toList args with
    | path :: arguments ->
        match parseOptions arguments |> Result.bind (inspect path) with
        | Ok() -> 0
        | Error message ->
            eprintfn "%s" message
            1
    | [] ->
        eprintfn
            "Usage: MatchDiagnostics <archive.json> [--summary | --verify | --audit | --chain <occurrence>] [--actor <player>] [--action <id>] [--event <id>] [--from <tick>] [--until <tick>] [--limit <count>] [--json]"

        1
