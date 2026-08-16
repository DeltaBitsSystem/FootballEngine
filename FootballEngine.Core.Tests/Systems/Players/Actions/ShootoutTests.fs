namespace FootballEngine.Core.Tests.Systems.Players.Actions

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ShootoutTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let shootoutTests =
        testList
            "FootballEngine.Core.Systems.Players.Actions.ShootoutTests"
            [ testCase "resolve is deterministic for a seed" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let rng = Rng.ofSeed 1234UL
                  let (h1, a1), rng' = Shootout.resolve sctx rng world
                  // Re-run from the same seed: same result.
                  let rngB = Rng.ofSeed 1234UL
                  let (h2, a2), _ = Shootout.resolve sctx rngB world

                  Expect.equal (h1, a1) (h2, a2) "same seed -> same shootout")

              testCase "produces a winner" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let (home, away), _ = Shootout.resolve sctx (Rng.ofSeed 7UL) world

                  Expect.isTrue (home <> away) "shootout ends with a winner") ]
