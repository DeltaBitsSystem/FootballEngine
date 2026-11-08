namespace FootballEngine.World.Pulse

open System
open System.Text

module PulseRandom =

    let private fnvOffset = 14695981039346656037UL
    let private fnvPrime = 1099511628211UL

    let private mix (hash: uint64) (bytes: byte[]) =
        bytes |> Array.fold (fun h b -> (h ^^^ uint64 b) * fnvPrime) hash

    /// FNV-1a over fixed-width fields: String.GetHashCode is randomized per process and would break replays.
    let seedFor (executionSeed: uint64) (date: DateTime) (detector: string) (entityId: int) : uint64 =
        fnvOffset
        |> fun h -> mix h (BitConverter.GetBytes executionSeed)
        |> fun h -> mix h (BitConverter.GetBytes date.Date.Ticks)
        |> fun h -> mix h (Encoding.UTF8.GetBytes detector)
        // Separator so ("ab", 1) and ("a", ...) cannot collide by concatenation.
        |> fun h -> mix h [| 0uy |]
        |> fun h -> mix h (BitConverter.GetBytes entityId)

    /// Top 53 bits fill a double mantissa exactly, giving a uniform value in [0, 1).
    let roll (seed: uint64) : float =
        // FNV low bits are weak; a splitmix finalizer spreads them before taking the top bits.
        let z = seed + 0x9E3779B97F4A7C15UL
        let z = (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        let z = (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        let z = z ^^^ (z >>> 31)
        float (z >>> 11) / 9007199254740992.0
