namespace FootballEngine

open System
open System.Security.Cryptography
open FootballEngine.Domain

module ExecutionRandomness =
    /// Set only by the capture tool's --match-seed, so a recording run replays the match it searched.
    let mutable seedOverride: uint64 option = None

    // OS entropy belongs to the application boundary, never to deterministic ticks or workers.
    let newSeed () : uint64 =
        match seedOverride with
        | Some seed -> seed
        | None ->
            RandomNumberGenerator.GetBytes(sizeof<uint64>)
            |> fun bytes -> BitConverter.ToUInt64(bytes, 0)

    let beginAttempt (state: GameState) : GameState =
        { state with
            ExecutionSeed = newSeed () }
