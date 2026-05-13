module FootballEngine.Tests.Layer2.HandballDetectorTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract

let handballDetectorTests =
    testList
        "HandballDetector"
        [ test "GK never gets handball" {
              let ctx =
                  { BallPos =
                      { X = 52.5<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerPos =
                      { X = 52.5<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerId = 1
                    IsGK = true
                    ArmPosition = 0.8
                    DeliberateMovement = true
                    DistanceFromShot = 5.0<meter>
                    InPenaltyArea = true }

              let result = HandballDetector.assess ctx HomeClub
              Expect.equal result HandballResult.NoHandball "GK should never get handball"
          }

          test "deliberate handball in penalty area can produce penalty" {
              let ctx =
                  { BallPos =
                      { X = 10.0<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerPos =
                      { X = 10.0<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerId = 1
                    IsGK = false
                    ArmPosition = 0.9
                    DeliberateMovement = true
                    DistanceFromShot = 3.0<meter>
                    InPenaltyArea = true }

              let mutable gotPenalty = false
              let mutable gotFreeKick = false

              for _ in 1..50 do
                  match HandballDetector.assess ctx HomeClub with
                  | HandballResult.HandballPenalty _ -> gotPenalty <- true
                  | HandballResult.HandballFreeKick _ -> gotFreeKick <- true
                  | _ -> ()

              Expect.isTrue (gotPenalty || gotFreeKick) "deliberate handball in box should produce penalty or free kick"
          }

          test "handball outside area produces free kick" {
              let ctx =
                  { BallPos =
                      { X = 52.5<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerPos =
                      { X = 52.5<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerId = 1
                    IsGK = false
                    ArmPosition = 0.9
                    DeliberateMovement = true
                    DistanceFromShot = 3.0<meter>
                    InPenaltyArea = false }

              let mutable gotFreeKick = false

              for _ in 1..50 do
                  match HandballDetector.assess ctx HomeClub with
                  | HandballResult.HandballFreeKick _ -> gotFreeKick <- true
                  | _ -> ()

              Expect.isTrue gotFreeKick "handball outside area should produce free kick"
          }

          test "natural arm position far from shot rarely triggers handball" {
              let ctx =
                  { BallPos =
                      { X = 52.5<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerPos =
                      { X = 52.5<meter>
                        Y = 34.0<meter>
                        Z = 0.0<meter>
                        Vx = 0.0<meter / second>
                        Vy = 0.0<meter / second>
                        Vz = 0.0<meter / second> }
                    PlayerId = 1
                    IsGK = false
                    ArmPosition = 0.1
                    DeliberateMovement = false
                    DistanceFromShot = 20.0<meter>
                    InPenaltyArea = false }

              let mutable noHandballCount = 0

              for _ in 1..50 do
                  match HandballDetector.assess ctx HomeClub with
                  | HandballResult.NoHandball -> noHandballCount <- noHandballCount + 1
                  | _ -> ()

              Expect.isGreaterThanOrEqual
                  noHandballCount
                  40
                  "natural arm position far from shot should mostly be NoHandball"
          } ]
