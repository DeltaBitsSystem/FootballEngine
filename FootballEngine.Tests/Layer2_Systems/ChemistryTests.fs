module FootballEngine.Tests.Layer2.ChemistryTests

open Expecto
open FootballEngine
open FootballEngine.TeamOrchestrator

let chemistryTests =
    testList
        "Chemistry"
        [ test "updateFamiliarity increases with successful pass" {
              let fam = ChemistryTracker.updateFamiliarity 0.5 true 0.0
              Expect.isGreaterThan fam 0.5 "familiarity should increase after successful pass"
          }

          test "updateFamiliarity decreases slightly with failed pass" {
              let fam = ChemistryTracker.updateFamiliarity 0.5 false 0.0
              Expect.isLessThan fam 0.5 "familiarity should decrease after failed pass"
          }

          test "updateFamiliarity never exceeds 1.0" {
              let fam = ChemistryTracker.updateFamiliarity 0.99 true 100.0
              Expect.isLessThanOrEqual fam 1.0 "familiarity should be capped at 1.0"
          }

          test "calculateCohesion returns valid TeamCohesion" {
              let fam = Array2D.create 3 3 0.5
              let cohesion = ChemistryTracker.calculateCohesion fam 3
              Expect.isGreaterThan cohesion.OverallCohesion 0.0 "cohesion should be positive"
              Expect.isLessThanOrEqual cohesion.OverallCohesion 1.0 "cohesion should be <= 1.0"
              Expect.isGreaterThan cohesion.PressingCoordination 0.0 "pressing coordination should be positive"
              Expect.isGreaterThan cohesion.TransitionSpeed 0.0 "transition speed should be positive"
          }

          ]
