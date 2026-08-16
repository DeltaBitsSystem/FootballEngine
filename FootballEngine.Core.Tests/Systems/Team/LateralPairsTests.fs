namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Behaviour of the lateral pair swap: it reduces total travel when players
/// have crossed, leaves symmetric pairs untouched when there is no gain, and
/// is deterministic.
module LateralPairsTests =

    let private p: LateralPairs.PairParams = { SwapThreshold = 0.5 }

    let lateralPairsTests =
        testList
            "FootballEngine.Core.Systems.Team.LateralPairsTests"
            [ testCase "swaps when players have crossed" (fun _ ->
                  // Left slot is now at the right target and vice-versa.
                  let current = [| (5.0, 5.0); (-5.0, -5.0) |]
                  let targets = [| (-5.0, -5.0); (5.0, 5.0) |]
                  let pairs = [| 0, 1 |]
                  let result = LateralPairs.solve p current targets pairs
                  Expect.equal result.[0] targets.[1] "left slot takes the right target"
                  Expect.equal result.[1] targets.[0] "right slot takes the left target")

              testCase "keeps symmetry when already settled" (fun _ ->
                  let current = [| (-5.0, -5.0); (5.0, 5.0) |]
                  let targets = [| (-5.0, -5.0); (5.0, 5.0) |]
                  let pairs = [| 0, 1 |]
                  let result = LateralPairs.solve p current targets pairs
                  Expect.equal result.[0] targets.[0] "left target unchanged"
                  Expect.equal result.[1] targets.[1] "right target unchanged")

              testCase "does not swap a marginal gain below the threshold" (fun _ ->
                  // Crossed by less than the threshold: keep the assignment.
                  let current = [| (-4.0, 0.0); (6.0, 0.0) |]
                  let targets = [| (-5.0, 0.0); (5.0, 0.0) |]
                  let pairs = [| 0, 1 |]
                  let result = LateralPairs.solve p current targets pairs
                  Expect.equal result.[0] targets.[0] "left target kept"
                  Expect.equal result.[1] targets.[1] "right target kept")

              testCase "is deterministic" (fun _ ->
                  let current = [| (5.0, 5.0); (-5.0, -5.0); (0.0, 3.0) |]
                  let targets = [| (-5.0, -5.0); (5.0, 5.0); (0.0, -3.0) |]
                  let pairs = [| 0, 1; 2, 1 |]
                  let a = LateralPairs.solve p current targets pairs
                  let b = LateralPairs.solve p current targets pairs
                  Expect.equal a b "same inputs yield the same assignment") ]
