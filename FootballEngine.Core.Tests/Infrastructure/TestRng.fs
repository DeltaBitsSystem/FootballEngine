namespace FootballEngine.Core.Tests.Infrastructure

open FootballEngine.Core.Match

/// Stable seed per test name.
module TestRng =

    let rngFor (testName: string) : Rng =
        let hash =
            testName.ToCharArray()
            |> Array.fold (fun acc c -> (acc * 131UL) + uint64 (int c)) 0x9E3779B97F4A7C15UL

        Rng.ofSeed hash

    let runTicks (n: int) (seed: uint64) (step: Rng -> (Rng * 'T)) : 'T list =
        let mutable rng = Rng.ofSeed seed
        let results = ResizeArray<'T>()

        for _ in 1..n do
            let rng', result = step rng
            rng <- rng'
            results.Add result

        List.ofSeq results
