module FootballEngine.Tests.Layer2.MatchMemoryTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

let recordTests =
    testList
        "Record"
        [ test "recordDuel Won increments wins and streak" {
              let mem = MatchMemory.create ()
              MatchMemory.recordDuel HomeClub 0 3 Won mem
              Expect.equal mem.Home[0].DuelWins 1 "wins should be 1"
              Expect.equal mem.Home[0].SuccessStreak 1 "streak should be 1"
              Expect.equal mem.Home[0].LastOpponentId 3 "opponent id should be 3"
          }

          test "recordDuel Lost increments losses" {
              let mem = MatchMemory.create ()
              MatchMemory.recordDuel HomeClub 0 5 Lost mem
              Expect.equal mem.Home[0].DuelLosses 1 "losses should be 1"
              Expect.equal mem.Home[0].SuccessStreak 0 "streak should remain 0"
          }

          test "wins and losses cap at 5" {
              let mem = MatchMemory.create ()

              for _ in 1..10 do
                  MatchMemory.recordDuel HomeClub 0 1 Won mem

              Expect.equal mem.Home[0].DuelWins 5 "wins should cap at 5"
          }

          test "recordPassFailure increments failures and resets streak" {
              let mem = MatchMemory.create ()
              mem.Home[0].SuccessStreak <- 3
              MatchMemory.recordPassFailure HomeClub 0 mem
              Expect.equal mem.Home[0].RecentPassFailures 1 "failures should be 1"
              Expect.equal mem.Home[0].SuccessStreak 0 "streak should reset to 0"
          }

          test "pass failures cap at 5" {
              let mem = MatchMemory.create ()

              for _ in 1..10 do
                  MatchMemory.recordPassFailure HomeClub 0 mem

              Expect.equal mem.Home[0].RecentPassFailures 5 "failures should cap at 5"
          }

          test "recordSuccess increments streak and decrements failures" {
              let mem = MatchMemory.create ()
              mem.Home[0].RecentPassFailures <- 2
              MatchMemory.recordSuccess HomeClub 0 mem
              Expect.equal mem.Home[0].SuccessStreak 1 "streak should be 1"
              Expect.equal mem.Home[0].RecentPassFailures 1 "failures should decrement to 1"
          }

          test "success does not decrement failures below 0" {
              let mem = MatchMemory.create ()
              MatchMemory.recordSuccess HomeClub 0 mem
              Expect.equal mem.Home[0].RecentPassFailures 0 "failures should stay at 0"
          } ]

let modifierTests =
    testList
        "Modifiers"
        [ test "duelHistoryModifier returns positive for wins" {
              let mem = MatchMemory.create ()
              mem.Home[0].DuelWins <- 3
              mem.Home[0].DuelLosses <- 1
              let modif = MatchMemory.duelHistoryModifier HomeClub 0 mem
              Expect.floatClose Accuracy.medium modif (2.0 * 0.033) "3 wins - 1 loss = 2 * 0.033"
          }

          test "passFailureModifier is negative" {
              let mem = MatchMemory.create ()
              mem.Home[0].RecentPassFailures <- 2
              let modif = MatchMemory.passFailureModifier HomeClub 0 mem
              Expect.isLessThan modif 0.0 "pass failure modifier should be negative"
          }

          test "successStreakModifier is positive for positive streak" {
              let mem = MatchMemory.create ()
              mem.Home[0].SuccessStreak <- 3
              let modif = MatchMemory.successStreakModifier HomeClub 0 mem
              Expect.floatClose Accuracy.medium modif 0.03 "3 * 0.01 = 0.03"
          } ]

let decayTests =
    testList
        "Decay"
        [ test "decay reduces wins and losses by 20%" {
              let mem = MatchMemory.create ()
              mem.Home[0].DuelWins <- 5
              mem.Home[0].DuelLosses <- 5
              mem.Home[0].RecentPassFailures <- 5
              MatchMemory.decay mem
              Expect.equal mem.Home[0].DuelWins 4 "wins should decay 5 * 0.8 = 4"
              Expect.equal mem.Home[0].DuelLosses 4 "losses should decay 5 * 0.8 = 4"
              Expect.equal mem.Home[0].RecentPassFailures 4 "failures should decay 5 * 0.8 = 4"
          }

          test "decay does not go below 0" {
              let mem = MatchMemory.create ()
              mem.Home[0].DuelWins <- 1
              mem.Home[0].DuelLosses <- 1
              mem.Home[0].RecentPassFailures <- 1
              MatchMemory.decay mem
              Expect.equal mem.Home[0].DuelWins 0 "wins should floor at 0"
              Expect.equal mem.Home[0].DuelLosses 0 "losses should floor at 0"
              Expect.equal mem.Home[0].RecentPassFailures 0 "failures should floor at 0"
          }

          test "decay affects both sides" {
              let mem = MatchMemory.create ()
              mem.Away[3].DuelWins <- 5
              MatchMemory.decay mem
              Expect.equal mem.Away[3].DuelWins 4 "away wins should also decay"
          } ]

let matchMemoryTests =
    testList "MatchMemory" [ recordTests; modifierTests; decayTests ]
