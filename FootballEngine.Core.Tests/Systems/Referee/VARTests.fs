namespace FootballEngine.Core.Tests.Systems.Referee

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module VARTests =

    let private ctx = TestBuilders.defaultCtx ()

    let varTests =
        testList
            "FootballEngine.Core.Systems.Referee.VARTests"
            [ test "review snapshots cannot overwrite movement or action proposals" {
                  let world = TestBuilders.minimalWorld ()

                  let run =
                      { Slot = 9
                        TargetX = 12.0f
                        TargetY = 4.0f
                        Phase = 1
                        ExpiresAt = 50<subtick> }

                  world.Home.Frame.RunLookup[9] <- run

                  world.Home.PendingActions[9] <-
                      { Choice = TakeShot
                        DecidedAt = -1<subtick>
                        ExpiresAt = 50<subtick> }

                  RefereeVAR.markOffsideSnapshot world ClubSide.Home 9 20.0<meter> 18.0<meter> 0<subtick>
                  RefereeVAR.markOffsideSnapshot world ClubSide.Away 9 -20.0<meter> -18.0<meter> 0<subtick>
                  RefereeVAR.clearSnapshots world ClubSide.Home
                  Expect.equal world.Home.Frame.RunLookup[9] run "movement survives review"
                  Expect.equal world.Home.PendingActions[9].Choice TakeShot "pending action survives review"
                  Expect.isFalse (RefereeVAR.isOffside ctx world ClubSide.Home) "home review was cleared"
                  Expect.isTrue (RefereeVAR.isOffside ctx world ClubSide.Away) "away review is independently retained"
                  Expect.equal world.Home.Frame.RunLookup.Count 1 "no hidden movement keys"
              }
              test "offside marker beyond the margin overturns" {
                  let world = TestBuilders.minimalWorld ()
                  // Home attacks +X. Attacker shoots from 1 m ahead of the line.
                  RefereeVAR.markOffsideSnapshot
                      world
                      ClubSide.Home
                      9
                      (Pitch.awayGoalX - 1.0<meter>)
                      (Pitch.awayGoalX - 3.0<meter>)
                      0<subtick>

                  Expect.isTrue (RefereeVAR.isOffside ctx world ClubSide.Home) "clear offside flags a review"
              }

              test "marker inside the margin stands" {
                  let world = TestBuilders.minimalWorld ()

                  RefereeVAR.markOffsideSnapshot
                      world
                      ClubSide.Home
                      9
                      (Pitch.awayGoalX - 1.0<meter>)
                      (Pitch.awayGoalX - 1.05<meter>)
                      0<subtick>

                  Expect.isFalse (RefereeVAR.isOffside ctx world ClubSide.Home) "marginal position stands"
              }

              test "clearing markers resets review state" {
                  let world = TestBuilders.minimalWorld ()

                  RefereeVAR.markOffsideSnapshot
                      world
                      ClubSide.Home
                      9
                      (Pitch.awayGoalX - 1.0<meter>)
                      (Pitch.awayGoalX - 3.0<meter>)
                      0<subtick>

                  RefereeVAR.clearSnapshots world ClubSide.Home

                  Expect.isFalse (RefereeVAR.isOffside ctx world ClubSide.Home) "cleared markers -> no review"
              } ]
