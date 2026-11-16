namespace FootballEngine.Core.Match

open System

/// The live worker, visual capture and semantic projection schedules are
/// independent so changing presentation pressure cannot change the match.
type LiveExecutionPolicy =
    { WorkerSlice: int<tickDelta>
      PoseEvery: int<tickDelta>
      SemanticEvery: int<tickDelta>
      BufferCapacity: int
      PresentationDelay: TimeSpan }

module LiveExecutionPolicy =
    let defaultPolicy =
        { WorkerSlice = 2<tickDelta>
          PoseEvery = 1<tickDelta>
          SemanticEvery = 10<tickDelta>
          BufferCapacity = 64
          // Motion matching reads a second of future root trajectory; showing play one second late makes it real.
          PresentationDelay = TimeSpan.FromSeconds 1.0 }

/// Execution policy is part of the immutable match input, never of the worker
/// schedule. A worker may advance the same policy in any sized deterministic slice.
type MatchRunMode =
    | Fast
    /// Records presentation snapshots every N ticks into `MatchOutcome.Replay` for tests and tools;
    /// never used to watch a match, which re-simulates from its recorded execution instead.
    | Live of policy: LiveExecutionPolicy
    | Archive of snapshotEvery: int<tickDelta>

type MatchOutcome =
    { Events: MatchEvent list
      Final: World
      Replay: Replay.ReplayData option
      EventsHash: uint64 }

module MatchOutcomeHash =
    let private fnv (h: uint64) (v: uint64) : uint64 = (h ^^^ v) * 1099511628211UL

    /// Hashes the sporting record only, so a fixture that stores the record and a run that kept every event
    /// agree on the same result.
    let eventsHash (events: MatchEvent list) : uint64 =
        let mutable hash = 1469598103934665603UL

        for ev in events do
            if MatchEvent.isRecord ev then
                hash <- fnv hash (MatchEvent.id ev |> EventId.value)

        hash
