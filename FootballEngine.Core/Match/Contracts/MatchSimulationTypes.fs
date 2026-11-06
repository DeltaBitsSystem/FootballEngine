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
          PresentationDelay = TimeSpan.FromMilliseconds 250.0 }

/// Execution policy is part of the immutable match input, never of the worker
/// schedule. A worker may advance the same policy in any sized deterministic slice.
type MatchRunMode =
    | Fast
    | Live of policy: LiveExecutionPolicy
    | Replay of snapshotEvery: int<tickDelta>

type MatchOutcome =
    { Events: MatchEvent list
      Final: World
      Replay: Replay.ReplayData option
      EventsHash: uint64 }

module MatchOutcomeHash =
    let private fnv (h: uint64) (v: uint64) : uint64 = (h ^^^ v) * 1099511628211UL

    let eventsHash (events: MatchEvent list) : uint64 =
        let mutable hash = 1469598103934665603UL

        for ev in events do
            hash <- fnv hash (MatchEvent.id ev |> EventId.value)

        hash
