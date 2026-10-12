namespace FootballEngine

open System
open System.Security.Cryptography
open FootballEngine.Domain

module ExecutionRandomness =
    // OS entropy belongs to the application boundary, never to deterministic ticks or workers.
    let newSeed () : uint64 =
        RandomNumberGenerator.GetBytes(sizeof<uint64>)
        |> fun bytes -> BitConverter.ToUInt64(bytes, 0)

    let beginAttempt (state: GameState) : GameState =
        { state with
            ExecutionSeed = newSeed () }
