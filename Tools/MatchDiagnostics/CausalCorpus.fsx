// A bounded offline consumer of the real session; no GUI, alternative simulator or inferred causal links.
#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open System
open System.IO
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let unwrap =
  function
  | Ok value -> value
  | Error error -> failwithf "causal corpus failed: %A" error

let directory =
  fsi.CommandLineArgs
  |> Array.tryItem 1
  |> Option.defaultValue "/tmp/opencode/causal-corpus"

Directory.CreateDirectory directory |> ignore
// The current contact-dense fixtures retain 221134 occurrences in a Release count pass; this leaves over 2x headroom without allowing silent eviction.
let retainedBudget = 524288

let engineAssemblySha256 =
  typeof<World>.Assembly.Location
  |> File.ReadAllBytes
  |> System.Security.Cryptography.SHA256.HashData
  |> Convert.ToHexString

let run seed capture =
  let input =
    { MatchId = seed
      Context = TestBuilders.defaultCtx ()
      Seed = seed
      EffectiveConfig = EffectiveConfig.defaultConfig
      EngineVersion = "causal-contact-2026-11-04" }

  let session =
    MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

  session.World.Observation.Enabled <- capture

  MatchSession.receive
    { EffectiveTick = 6000<subtick>
      Sequence = 1UL
      Revision = input.EffectiveConfig.Revision
      Manager = input.Context.HomeClub.Id
      Action =
        ChangeTactics
          { input.Context.HomeClub.BaseTactics with
              DefensiveLine = 44.0f
              Width = 0.7f
              PressIntensity = 0.7f } }
    session
  |> unwrap

  let entries = ResizeArray<ObservationEntry>()
  let mutable cursor = 0UL

  while session.World.State.Flow <> MatchFlow.MatchEnded
        && session.World.State.Tick < MatchSession.maxTicks do
    let target = min MatchSession.maxTicks (session.World.State.Tick + 200<subtick>)
    MatchSession.advanceTo target session |> unwrap |> ignore

    if capture then
      let publication = session.World.Observation.SnapshotSince cursor

      for entry in publication.Entries do
        let (ObservationId id) = entry.Id

        if id <> cursor + 1UL then
          failwithf "seed %d lost occurrences after %d before %d" seed cursor id

        if entries.Count = retainedBudget then
          failwithf "seed %d exceeded the explicit offline retention budget" seed

        entries.Add entry
        cursor <- id

      if cursor <> publication.Total then
        failwithf "seed %d publication frontier differs from captured occurrences" seed

  let outcome = MatchSession.finish session |> unwrap

  let snapshot =
    { MatchSession.observations session with
        Entries = entries.ToArray()
        Capacity = retainedBudget
        Evicted = 0UL }

  outcome, session.Rng, snapshot

let eventMatches predicate (entry: ObservationEntry) =
  match entry.Data with
  | EventCommitted event -> predicate event.Data
  | _ -> false

let episodePredicates: (string * (ObservationEntry -> bool))[] =
  [| ("pass-result",
      eventMatches (function
        | EventData.Pass _ -> true
        | _ -> false));
     ("possession-loss",
      eventMatches (function
        | EventData.Interception _
        | EventData.Tackle _ -> true
        | _ -> false));
     ("shot-result",
      eventMatches (function
        | EventData.Shot _ -> true
        | _ -> false));
     ("foul-restart",
      (fun entry ->
        match entry.Data with
        | RestartCreated { Cause = RestartCause.Foul _ } -> true
        | _ -> false));
     ("recalled-advantage",
      (fun entry ->
        match entry.Data, entry.Cause with
        | RestartCreated _, ValueSome(PairedEvents _) -> true
        | _ -> false));
     ("physical-ball-contact",
      (fun entry ->
        match entry.Data with
        | EventCommitted { Data = EventData.PhysicalContact contact } -> contact.BallTouched
        | _ -> false));
     ("rejected-cross",
      (fun entry ->
        match entry.Data with
        | ActionRejected(_, NoCrossTarget) -> true
        | _ -> false));
     ("effective-tactics",
      (fun entry ->
        match entry.Data with
        | TacticsEffective _ -> true
        | _ -> false)) |]

let reports =
  [| for seed in [ 11UL; 12UL; 13UL ] do
       let outcome, rng, snapshot = run seed true

       File.WriteAllText(
         Path.Combine(directory, sprintf "seed-%d-audit.json" seed),
         Observation.exportAuditJson snapshot
       )

       let plain, plainRng, _ = run seed false

       if
         outcome.EventsHash <> plain.EventsHash
         || rng <> plainRng
         || outcome.Final.State <> plain.Final.State
       then
         failwithf "observation changed the authoritative match for seed %d" seed

       use text = new StreamWriter(Path.Combine(directory, sprintf "seed-%d.txt" seed))

       text.WriteLine(
         sprintf
           "Seed %d score %d-%d frontier %d hash %d occurrences %d unlinked %d; capture on/off identical."
           seed
           outcome.Final.State.HomeScore
           outcome.Final.State.AwayScore
           (int outcome.Final.State.Tick)
           outcome.EventsHash
           snapshot.Total
           snapshot.Unlinked
       )

       let episodes =
         episodePredicates
         |> Array.map (fun (name, predicate) ->
           let candidates = snapshot.Entries |> Array.filter predicate
           // A recalled advantage can reference a possession trigger without retaining its original incident.
           let selected =
             candidates
             |> Array.tryFind (fun entry ->
               let chain = Observation.chain entry.Id snapshot |> unwrap

               chain.Entries
               |> Array.exists (fun cause ->
                 match cause.Data with
                 | RefereeAssessed _ when name = "foul-restart" -> true
                 | DecisionChosen _ when name <> "foul-restart" -> true
                 | _ -> false))
             |> Option.orElseWith (fun () -> candidates |> Array.tryHead)

           match selected with
           | None ->
             text.WriteLine(sprintf "\n%s: NOT CAPTURED in this seed; absence is not coverage." name)

             {| Name = name
                Candidates = candidates.Length
                Root = 0UL
                Nodes = 0
                Missing = 0
                Cycles = 0
                Future = 0 |}
           | Some entry ->
             let chain = Observation.chain entry.Id snapshot |> unwrap
             let (ObservationId root) = entry.Id
             let json = Observation.exportChainJson entry.Id snapshot |> unwrap
             File.WriteAllText(Path.Combine(directory, sprintf "seed-%d-%s.json" seed name), json)
             text.WriteLine(sprintf "\n%s root #%d (%d candidates):" name root candidates.Length)

             for cause in chain.Entries do
               let (ObservationId id) = cause.Id

               text.WriteLine(
                 sprintf "  #%d tick %d cause %A: %s" id (int cause.Tick) cause.Cause (Observation.explain cause)
               )

             text.WriteLine(
               sprintf "  Missing=%A Cycles=%A Future=%A" chain.MissingCauses chain.Cycles chain.FutureCauses
             )

             {| Name = name
                Candidates = candidates.Length
                Root = root
                Nodes = chain.Entries.Length
                Missing = chain.MissingCauses.Length
                Cycles = chain.Cycles.Length
                Future = chain.FutureCauses.Length |})

       yield
         {| Seed = seed
            EngineAssemblySha256 = engineAssemblySha256
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            Architecture = string System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            HomeScore = outcome.Final.State.HomeScore
            AwayScore = outcome.Final.State.AwayScore
            Frontier = int outcome.Final.State.Tick
            EventsHash = outcome.EventsHash
            Occurrences = snapshot.Total
            Unlinked = snapshot.Unlinked
            RetentionBudget = retainedBudget
            CaptureEquivalent = true
            Episodes = episodes |} |]

File.WriteAllText(
  Path.Combine(directory, "summary.json"),
  JsonSerializer.Serialize(reports, JsonSerializerOptions(WriteIndented = true))
)

printfn
  "Causal corpus written to %s; six complete matches, explicit text chains and capture on/off equivalence."
  directory
