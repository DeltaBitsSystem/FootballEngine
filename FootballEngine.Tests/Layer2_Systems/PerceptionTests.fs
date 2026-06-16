module FootballEngine.Tests.Layer2.PerceptionTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Player.Perception

let private defaultConfig =
    { VisionRadiusBase = 20.0<meter>
      VisionRadiusMax = 45.0<meter>
      VisionConeAngle = 2.094
      PeripheralMultiplier = 0.4
      MinimumAwarenessFloor = 20.0<meter>
      AnticipationBonusRadius = 5.0<meter>
      GoalkeeperConeAngle = 3.1416
      CommunicationRange = 15.0<meter>
      SetPieceSimplifiedRadius = 30.0<meter>
      BlindPassVisionThreshold = 15
      BlindPassComposureThreshold = 14
      BlindPassSuccessPenalty = 0.30 }

let visionRadiusTests =
    testList
        "VisionRadius"
        [ test "high vision produces larger radius than low vision" {
              let low = Perception.computeVisionRadius 2 2 defaultConfig
              let high = Perception.computeVisionRadius 18 18 defaultConfig
              Expect.isGreaterThan high low "high vision+anticipation should yield larger radius"
          }

          test "vision radius is at least MinimumAwarenessFloor" {
              let radius = Perception.computeVisionRadius 1 1 defaultConfig
              Expect.isGreaterThanOrEqual radius defaultConfig.MinimumAwarenessFloor "radius must be at least the floor"
          }

          test "max vision with max anticipation does not exceed VisionRadiusMax + AnticipationBonusRadius" {
              let radius = Perception.computeVisionRadius 20 20 defaultConfig

              let maxExpected =
                  defaultConfig.VisionRadiusMax + defaultConfig.AnticipationBonusRadius

              Expect.isLessThanOrEqual radius maxExpected "radius should be bounded"
          }

          test "vision=0 yields base radius" {
              let radius = Perception.computeVisionRadius 0 0 defaultConfig

              Expect.floatClose
                  Accuracy.medium
                  (float radius)
                  (float defaultConfig.VisionRadiusBase)
                  "zero vision should give base radius"
          } ]

let facingDirectionTests =
    testList
        "FacingDirection"
        [ test "facing toward ball when no target and idle" {
              let myPos = defaultSpatial 50.0<meter> 34.0<meter>
              let ballPos = defaultSpatial 60.0<meter> 34.0<meter>

              let angle =
                  Perception.computeFacingDirection
                      myPos
                      IntentKind.Idle
                      0.0f
                      0.0f
                      ballPos
                      0.0<meter / second>
                      0.0<meter / second>

              Expect.floatClose Accuracy.low angle 0.0 "should face right (toward ball at same Y)"
          }

          test "facing toward target when PressBall with nonzero target" {
              let myPos = defaultSpatial 50.0<meter> 34.0<meter>
              let ballPos = defaultSpatial 60.0<meter> 34.0<meter>

              let angle =
                  Perception.computeFacingDirection
                      myPos
                      IntentKind.PressBall
                      60.0f
                      34.0f
                      ballPos
                      0.0<meter / second>
                      0.0<meter / second>

              Expect.floatClose Accuracy.low angle 0.0 "should face right toward target"
          }

          test "facing direction follows velocity when moving fast and no target" {
              let myPos = defaultSpatial 50.0<meter> 34.0<meter>
              let ballPos = defaultSpatial 60.0<meter> 34.0<meter>

              let angle =
                  Perception.computeFacingDirection
                      myPos
                      IntentKind.Idle
                      0.0f
                      0.0f
                      ballPos
                      1.0<meter / second>
                      0.0<meter / second>

              Expect.floatClose Accuracy.low angle 0.0 "should face right (velocity direction)"
          }

          test "facing direction follows velocity when moving diagonally" {
              let myPos = defaultSpatial 50.0<meter> 34.0<meter>
              let ballPos = defaultSpatial 60.0<meter> 34.0<meter>

              let angle =
                  Perception.computeFacingDirection
                      myPos
                      IntentKind.Idle
                      0.0f
                      0.0f
                      ballPos
                      1.0<meter / second>
                      1.0<meter / second>

              Expect.floatClose Accuracy.low angle (System.Math.PI / 4.0<meter> |> float) "should face ~45 degrees"
          } ]

let coneTests =
    testList
        "IsInCone"
        [ test "target directly ahead is in cone" {
              let obs = defaultSpatial 50.0<meter> 34.0<meter>
              let target = defaultSpatial 60.0<meter> 34.0<meter>

              Expect.isTrue
                  (Perception.isInCone obs 0.0 2.094 target)
                  "target at same Y, ahead X, facing 0 should be in cone"
          }

          test "target directly behind is not in cone" {
              let obs = defaultSpatial 50.0<meter> 34.0<meter>
              let target = defaultSpatial 40.0<meter> 34.0<meter>

              Expect.isFalse
                  (Perception.isInCone obs 0.0 2.094 target)
                  "target behind observer should not be in 120 degree cone"
          }

          test "very close target is always in cone" {
              let obs = defaultSpatial 50.0<meter> 34.0<meter>
              let target = defaultSpatial 50.001<meter> 34.0<meter>

              Expect.isTrue
                  (Perception.isInCone obs 0.0 0.1 target)
                  "very close target should be in cone regardless of angle"
          }

          test "target at 90 degrees is not in narrow cone" {
              let obs = defaultSpatial 50.0<meter> 34.0<meter>
              let target = defaultSpatial 50.0<meter> 44.0<meter>
              Expect.isFalse (Perception.isInCone obs 0.0 0.5 target) "90 degree offset outside narrow cone"
          } ]

let perceptionTests =
    testList "Perception" [ visionRadiusTests; facingDirectionTests; coneTests ]
