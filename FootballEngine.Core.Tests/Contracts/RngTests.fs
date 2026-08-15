namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module RngTests =

    let private draws (seed: uint64) (n: int) : uint64 list =
        let mutable rng = Rng.ofSeed seed
        let acc = ResizeArray<uint64>()

        for _ in 1..n do
            let v, rng' = Rng.nextU64 rng
            acc.Add v
            rng <- rng'

        List.ofSeq acc

    let rngTests =
        testList
            "FootballEngine.Core.Contracts.Rng"
            [ test "determinism: same seed -> identical 1000-draw stream" {
                  let a = draws 42UL 1000
                  let b = draws 42UL 1000
                  Expect.equal a b "same seed must produce the same stream"
              }

              test "determinism: different seeds -> different streams" {
                  let a = draws 42UL 200
                  let b = draws 43UL 200
                  Expect.notEqual a b "different seeds must produce different streams"
              }

              test "double in [0,1) over 10k draws" {
                  let rng = TestRng.rngFor "double-range"
                  let mutable r = rng
                  let mutable ok = true

                  for _ in 1..10000 do
                      let v, r' = Rng.double r

                      if v < 0.0 || v >= 1.0 then
                          ok <- false

                      r <- r'

                  Expect.isTrue ok "all doubles must be in [0,1)"
              }

              test "intIn: inclusive bounds and uniformity (chi-square over 10 buckets)" {
                  let rng = TestRng.rngFor "intIn-uniform"
                  let mutable r = rng
                  let counts = Array.zeroCreate 10
                  let mutable inBounds = true

                  for _ in 1..10000 do
                      let v, r' = Rng.intIn 0 9 r

                      if v < 0 || v > 9 then
                          inBounds <- false

                      counts.[v] <- counts.[v] + 1
                      r <- r'

                  Expect.isTrue inBounds "intIn must respect inclusive bounds"

                  let expected = 1000.0

                  let chiSquare =
                      counts |> Array.sumBy (fun c -> ((float c - expected) ** 2.0) / expected)

                  Expect.isLessThan chiSquare 33.0 "chi-square (9 df, p=0.0001) must pass"
              }

              test "intIn respects inclusive bounds (both ends hit)" {
                  let rng = TestRng.rngFor "intIn-bounds"
                  let mutable r = rng
                  let mutable sawLo = false
                  let mutable sawHi = false

                  for _ in 1..20000 do
                      let v, r' = Rng.intIn 5 7 r
                      sawLo <- sawLo || v = 5
                      sawHi <- sawHi || v = 7
                      r <- r'

                  Expect.isTrue (sawLo && sawHi) "both ends must be reachable"
              }

              test "normal: mean and std over 50k draws within tolerance" {
                  let rng = TestRng.rngFor "normal-stats"
                  let mutable r = rng
                  let mutable sum = 0.0
                  let mutable sumSq = 0.0
                  let n = 50000

                  for _ in 1..n do
                      let v, r' = Rng.normal 10.0 2.0 r
                      sum <- sum + v
                      sumSq <- sumSq + (v * v)
                      r <- r'

                  let mean = sum / float n
                  let variance = (sumSq / float n) - (mean * mean)
                  let std = sqrt variance
                  Expect.isLessThan (abs (mean - 10.0)) 0.1 "mean within tolerance"
                  Expect.isLessThan (abs (std - 2.0)) 0.1 "std within tolerance"
              }

              test "bernoulli: rate approximates p" {
                  let rng = TestRng.rngFor "bernoulli-rate"
                  let mutable r = rng
                  let mutable hits = 0
                  let n = 20000

                  for _ in 1..n do
                      let b, r' = Rng.bernoulli 0.3 r

                      if b then
                          hits <- hits + 1

                      r <- r'

                  let rate = float hits / float n
                  Expect.isLessThan (abs (rate - 0.3)) 0.02 "rate must approximate p"
              }

              test "beta: mean approximates a/(a+b)" {
                  let rng = TestRng.rngFor "beta-mean"
                  let mutable r = rng
                  let mutable sum = 0.0
                  let n = 50000
                  let a = 8.0
                  let b = 4.0

                  for _ in 1..n do
                      let v, r' = Rng.beta a b r
                      sum <- sum + v
                      r <- r'

                  let mean = sum / float n
                  Expect.isLessThan (abs (mean - (a / (a + b)))) 0.02 "beta mean must approximate a/(a+b)"
              }

              test "beta: draws are in (0,1)" {
                  let rng = TestRng.rngFor "beta-range"
                  let mutable r = rng
                  let mutable ok = true

                  for _ in 1..10000 do
                      let v, r' = Rng.beta 0.5 2.0 r

                      if v <= 0.0 || v >= 1.0 then
                          ok <- false

                      r <- r'

                  Expect.isTrue ok "beta draws must be in (0,1)"
              }

              test "weightedPick: distribution approximates weights" {
                  let rng = TestRng.rngFor "weightedPick-dist"
                  let mutable r = rng
                  let weights = [| 1.0; 3.0; 6.0 |]
                  let counts = Array.zeroCreate 3
                  let n = 20000

                  for _ in 1..n do
                      let idx, r' = Rng.weightedPick weights r
                      counts.[idx] <- counts.[idx] + 1
                      r <- r'

                  let expected = [| n / 10; 3 * n / 10; 6 * n / 10 |]

                  for i in 0..2 do
                      let rel = abs (float counts.[i] - float expected.[i]) / float expected.[i]
                      Expect.isLessThan rel 0.1 (sprintf "bucket %d within 10%%" i)
              }

              test "weightedPick: empty weights -> 0" {
                  let rng = TestRng.rngFor "weightedPick-empty"
                  let idx, _ = Rng.weightedPick [||] rng
                  Expect.equal idx 0 "empty weights must return 0"
              }

              test "MatchSeed.ofMatch: stable and distinct" {
                  let a1 = MatchSeed.ofMatch 100UL 5UL 7UL
                  let a2 = MatchSeed.ofMatch 100UL 5UL 7UL
                  Expect.equal a1 a2 "same inputs must give the same seed"

                  let b = MatchSeed.ofMatch 101UL 5UL 7UL
                  Expect.notEqual a1 b "different matchId must differ"

                  let c = MatchSeed.ofMatch 100UL 5UL 8UL
                  Expect.notEqual a1 c "different userSeed must differ"
              }

              test "ofSeed: zero seed produces a working stream" {
                  let rng = Rng.ofSeed 0UL
                  let v, _ = Rng.double rng
                  Expect.isTrue (v >= 0.0 && v < 1.0) "zero seed must still produce a valid draw"
              } ]
