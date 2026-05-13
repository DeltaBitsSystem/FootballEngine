module FootballEngine.Tests.Layer2.AdaptiveTacticsTests

open Expecto
open FootballEngine
open FootballEngine.TeamOrchestrator
open FootballEngine.Types

let adaptiveTacticsTests =
    testList
        "AdaptiveTactics"
        [ test "recordAttempt with SuccessfulXG increments attempts, successes, and totalXG" {
              let state = { Records = [| for _ in 0..4 -> { Pattern = AttackPattern.Central; Attempts = 0; Successes = 0; TotalXG = 0.0 } |] }
              let updated = AdaptiveTactics.recordAttempt AttackPattern.Central (SuccessfulXG 0.3) state
              let record = updated.Records[0]
              Expect.isGreaterThanOrEqual record.Attempts 1 "attempts should increment"
              Expect.isGreaterThanOrEqual record.Successes 1 "successes should increment"
              Expect.isGreaterThanOrEqual record.TotalXG 0.3 "totalXG should include the xG"
          }

          test "recordAttempt with LostPossession increments attempts only" {
              let state = { Records = [| for _ in 0..4 -> { Pattern = AttackPattern.Central; Attempts = 0; Successes = 0; TotalXG = 0.0 } |] }
              let updated = AdaptiveTactics.recordAttempt AttackPattern.Central LostPossession state
              let record = updated.Records[0]
              Expect.isGreaterThanOrEqual record.Attempts 1 "attempts should increment"
              Expect.equal record.Successes 0 "successes should not increment"
          }

          test "recordAttempt with StillInProgress does not modify counters" {
              let state = { Records = [| for _ in 0..4 -> { Pattern = AttackPattern.Central; Attempts = 0; Successes = 0; TotalXG = 0.0 } |] }
              let updated = AdaptiveTactics.recordAttempt AttackPattern.Central StillInProgress state
              let record = updated.Records[0]
              Expect.equal record.Attempts 0 "attempts should not change"
              Expect.equal record.Successes 0 "successes should not change"
          } ]
