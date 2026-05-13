module FootballEngine.Tests.Layer2.EmergentLoopsTests

open Expecto
open FootballEngine
open FootballEngine.TeamOrchestrator
open FootballEngine.Types

let emergentLoopsTests =
    testList
        "EmergentLoops"
        [ test "updateCompactness increases with high pass success" {
              let state = { CompactnessLevel = 0.5; PressingIntensity = 0.5; WingPlayPreference = 0.5; TempoLevel = 0.5; RiskAppetite = 0.5 }
              let updated = EmergentLoops.updateCompactness 0.8 state
              Expect.isGreaterThan updated.CompactnessLevel 0.5 "compactness should increase with pass success > 0.7"
          }

          test "updateCompactness decreases with low pass success" {
              let state = { CompactnessLevel = 0.5; PressingIntensity = 0.5; WingPlayPreference = 0.5; TempoLevel = 0.5; RiskAppetite = 0.5 }
              let updated = EmergentLoops.updateCompactness 0.4 state
              Expect.isLessThan updated.CompactnessLevel 0.5 "compactness should decrease with pass success < 0.5"
          }

          test "updatePressing increases with high press success" {
              let state = { CompactnessLevel = 0.5; PressingIntensity = 0.5; WingPlayPreference = 0.5; TempoLevel = 0.5; RiskAppetite = 0.5 }
              let updated = EmergentLoops.updatePressing 0.7 state
              Expect.isGreaterThan updated.PressingIntensity 0.5 "pressing should increase with press success > 0.6"
          }

          test "updateWingPlay increases with high flank success" {
              let state = { CompactnessLevel = 0.5; PressingIntensity = 0.5; WingPlayPreference = 0.5; TempoLevel = 0.5; RiskAppetite = 0.5 }
              let updated = EmergentLoops.updateWingPlay 0.6 state
              Expect.isGreaterThan updated.WingPlayPreference 0.5 "wing play should increase with flank success > 0.55"
          }

          test "updateFatigueSpiral reduces all levels with low condition" {
              let state = { CompactnessLevel = 0.8; PressingIntensity = 0.8; WingPlayPreference = 0.8; TempoLevel = 0.8; RiskAppetite = 0.8 }
              let updated = EmergentLoops.updateFatigueSpiral 30.0 3 state
              Expect.isLessThan updated.CompactnessLevel 0.8 "compactness should decrease"
              Expect.isLessThan updated.PressingIntensity 0.8 "pressing should decrease"
              Expect.isLessThan updated.TempoLevel 0.8 "tempo should decrease"
              Expect.isLessThan updated.RiskAppetite 0.8 "risk appetite should decrease"
          }

          test "all values stay within [0.1, 1.0]" {
              let state = { CompactnessLevel = 0.1; PressingIntensity = 0.1; WingPlayPreference = 0.1; TempoLevel = 0.1; RiskAppetite = 0.1 }
              let updated = EmergentLoops.updateFatigueSpiral 10.0 5 state
              Expect.isGreaterThanOrEqual updated.CompactnessLevel 0.1 "compactness >= 0.1"
              Expect.isLessThanOrEqual updated.CompactnessLevel 1.0 "compactness <= 1.0"
              Expect.isGreaterThanOrEqual updated.PressingIntensity 0.1 "pressing >= 0.1"
              Expect.isLessThanOrEqual updated.PressingIntensity 1.0 "pressing <= 1.0"
          } ]
