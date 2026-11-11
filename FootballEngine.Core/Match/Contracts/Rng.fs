namespace FootballEngine.Core.Match

/// xoshiro256** — pure, seeded, no global state. Same seed -> same stream, always.
[<Struct>]
type Rng =
    { mutable S0: uint64
      mutable S1: uint64
      mutable S2: uint64
      mutable S3: uint64 }

module Rng =

    let private splitMix64 (state: uint64) : uint64 =
        let z = state + 0x9E3779B97F4A7C15UL
        let z = (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        let z = (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        z ^^^ (z >>> 31)

    /// SplitMix64, the standard xoshiro seeding pass: stretches one 64-bit seed
    /// into four well-mixed state words.
    let ofSeed (seed: uint64) : Rng =
        let a = splitMix64 seed
        let b = splitMix64 a
        let c = splitMix64 b
        let d = splitMix64 c
        { S0 = a; S1 = b; S2 = c; S3 = d }

    let nextU64 (rng: Rng) : uint64 * Rng =
        let mutable rng = rng
        let rotl (x: uint64) (k: int) = (x <<< k) ||| (x >>> (64 - k))
        let result = rotl (rng.S1 * 5UL) 7 * 9UL
        let t = rng.S1 <<< 17
        rng.S2 <- rng.S2 ^^^ rng.S0
        rng.S3 <- rng.S3 ^^^ rng.S1
        rng.S1 <- rng.S1 ^^^ rng.S2
        rng.S0 <- rng.S0 ^^^ rng.S3
        rng.S2 <- rng.S2 ^^^ t
        rng.S3 <- (rng.S3 <<< 45) ||| (rng.S3 >>> 19)
        result, rng

    /// FNV-1a over the UTF-16 code units: a stable 64-bit key for a named stream.
    let keyOf (name: string) : uint64 =
        let mutable hash = 0xcbf29ce484222325UL

        for c in name do
            hash <- (hash ^^^ uint64 c) * 0x100000001b3UL

        hash

    /// An independent stream for one consumer within one step. Each system draws from its own stream, so what
    /// one system consumes never shifts the numbers another receives: an A/B change to one system leaves the
    /// rest of the match's randomness common to both runs.
    let stream (stepKey: uint64) (consumerKey: uint64) : Rng =
        ofSeed (splitMix64 (stepKey ^^^ splitMix64 consumerKey))

    /// [0, 1)
    let double (rng: Rng) : float * Rng =
        let u, rng' = nextU64 rng
        (float (u >>> 11) * (1.0 / 9007199254740992.0)), rng'

    let uniform (lo: float) (hi: float) (rng: Rng) : float * Rng =
        let d, rng' = double rng
        (lo + (hi - lo) * d), rng'

    /// Inclusive both ends.
    let intIn (lo: int) (hi: int) (rng: Rng) : int * Rng =
        let width = int64 hi - int64 lo + 1L
        let d, rng' = double rng
        (lo + int (int64 (floor (d * float width)))), rng'

    /// Box-Muller: two uniform draws per sample, drawn one at a time (no cached
    /// spare) so the RNG stream stays positionally pure and deterministic.
    let normal (mean: float) (std: float) (rng: Rng) : float * Rng =
        let u1, rng1 = double rng
        let u2, rng2 = double rng1
        let u1 = max u1 1.0e-12
        let z = sqrt (-2.0 * log u1) * cos (2.0 * System.Math.PI * u2)
        (mean + std * z), rng2

    let bernoulli (p: float) (rng: Rng) : bool * Rng =
        let d, rng' = double rng
        (d < p), rng'

    /// Cheng (1978) BB for a, b > 1 (R's rbeta); Johnk's exact sampler otherwise.
    let beta (alpha: float) (beta: float) (rng: Rng) : float * Rng =
        let a = max alpha 1.0e-9
        let b = max beta 1.0e-9
        let mutable rngState = rng

        if min a b > 1.0 then
            let aa = min a b
            let bb = max a b
            let alpha = aa + bb
            let betaC = sqrt ((alpha - 2.0) / (2.0 * aa * bb - alpha))
            let gamma = aa + 1.0 / betaC
            let log4 = 1.3862943611198906
            let mutable accepted = false
            let mutable w = 0.0

            while not accepted do
                let u1, r1 = double rngState
                let u2, r2 = double r1
                rngState <- r2

                let v = betaC * log (u1 / (1.0 - u1))
                w <- aa * exp v
                let z = u1 * u1 * u2
                let r = gamma * v - log4
                let s = aa + r - w

                if s + 2.609438 >= 5.0 * z then
                    accepted <- true
                else
                    let t = log z

                    if s > t then
                        accepted <- true
                    elif r + alpha * log (alpha / (bb + w)) >= t then
                        accepted <- true

            (if a > b then bb / (bb + w) else w / (bb + w)), rngState
        else
            // Johnk — exact for all a, b > 0.
            let mutable accepted = false
            let mutable x = 0.0

            while not accepted do
                let u1, r1 = double rngState
                let u2, r2 = double r1
                rngState <- r2

                let x1 = u1 ** (1.0 / a)
                let x2 = u2 ** (1.0 / b)

                if x1 + x2 <= 1.0 then
                    x <- x1 / (x1 + x2)
                    accepted <- true

            x, rngState

    /// 0 if empty.
    let weightedPick (weights: float[]) (rng: Rng) : int * Rng =
        if Array.isEmpty weights then
            0, rng
        else
            let total = Array.sum weights

            if total <= 0.0 then
                0, rng
            else
                let r, rng' = double rng
                let target = r * total
                let mutable acc = 0.0
                let mutable idx = 0

                while idx < weights.Length - 1 && acc + weights.[idx] < target do
                    acc <- acc + weights.[idx]
                    idx <- idx + 1

                idx, rng'

/// Deterministic match seed: hash(matchId, configFingerprint, userSeed).
module MatchSeed =

    let ofMatch (matchId: uint64) (configFingerprint: uint64) (userSeed: uint64) : uint64 =
        let mix (v: uint64) (state: uint64) =
            let z = state ^^^ v
            let z = (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
            let z = (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
            z ^^^ (z >>> 31)

        mix userSeed (mix configFingerprint (mix matchId 0x9E3779B97F4A7C15UL))
