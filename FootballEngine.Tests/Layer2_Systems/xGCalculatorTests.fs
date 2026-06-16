module FootballEngine.Tests.Layer2.xGCalculatorTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Player.Actions

let private w = BalanceConfig.defaultConfig.XG

let private makeModel dist angle shotType pressure =
    { DistanceToGoal = dist
      AngleToGoal = angle
      ShotType = shotType
      BodyPart = "RightFoot"
      AssistType = "None"
      PressureLevel = pressure
      IsOneOnOne = false
      IsSetPiece = false }

let xGCalculatorTests =
    testList
        "xGCalculator"
        [ test "baseXG returns value between 0 and 1" {
              let xg = xGCalculator.baseXG 10.0<meter> 90.0 w
              Expect.isGreaterThanOrEqual xg 0.0 "xG should be >= 0"
              Expect.isLessThanOrEqual xg 1.0 "xG should be <= 1"
          }

          test "closer distance yields higher xG" {
              let far = xGCalculator.baseXG 30.0<meter> 90.0 w
              let close = xGCalculator.baseXG 6.0<meter> 90.0 w
              Expect.isGreaterThan close far "close shot should have higher xG than far shot"
          }

          test "wider angle yields lower xG" {
              let central = xGCalculator.baseXG 12.0<meter> 90.0 w
              let wide = xGCalculator.baseXG 12.0<meter> 30.0 w
              Expect.isGreaterThan central wide "central angle should have higher xG than wide angle"
          }

          test "adjustForShotType preserves bounds for DrivenShot" {
              let baseXg = 0.4
              let xg = xGCalculator.adjustForShotType baseXg ShotType.DrivenShot w
              Expect.isGreaterThanOrEqual xg 0.0 "xG should be >= 0"
              Expect.isLessThanOrEqual xg 1.0 "xG should be <= 1"
          }

          test "adjustForShotType preserves bounds for Header" {
              let baseXg = 0.4
              let xg = xGCalculator.adjustForShotType baseXg ShotType.Header w
              Expect.isGreaterThanOrEqual xg 0.0 "xG should be >= 0"
              Expect.isLessThanOrEqual xg 1.0 "xG should be <= 1"
          }

          test "Header multiplier reduces xG relative to PlacedShot" {
              let baseXg = 0.5
              let header = xGCalculator.adjustForShotType baseXg ShotType.Header w
              let placed = xGCalculator.adjustForShotType baseXg ShotType.PlacedShot w
              Expect.isLessThan header placed "header should have lower xG than placed shot"
          }

          test "DrivenShot multiplier increases xG relative to PlacedShot" {
              let baseXg = 0.5
              let driven = xGCalculator.adjustForShotType baseXg ShotType.DrivenShot w
              let placed = xGCalculator.adjustForShotType baseXg ShotType.PlacedShot w
              Expect.isGreaterThan driven placed "driven shot should have higher xG than placed shot"
          }

          test "adjustForPressure reduces xG" {
              let baseXg = 0.5
              let noPressure = xGCalculator.adjustForPressure baseXg 0.0 w
              let highPressure = xGCalculator.adjustForPressure baseXg 1.0 w
              Expect.isGreaterThan noPressure highPressure "no pressure should yield higher xG than high pressure"
          }

          test "adjustForPressure returns non-negative" {
              let xg = xGCalculator.adjustForPressure 0.1 1.0 w
              Expect.isGreaterThanOrEqual xg 0.0 "xG should be >= 0 after pressure"
          }

          test "calculate returns value between 0 and 1 for typical shot" {
              let model = makeModel 12.0<meter> 90.0 ShotType.PlacedShot 0.0
              let xg = xGCalculator.calculate model w
              Expect.isGreaterThanOrEqual xg 0.0 "xG should be >= 0"
              Expect.isLessThanOrEqual xg 1.0 "xG should be <= 1"
          }

          test "calculate close central shot has higher xG than far wide shot" {
              let close = makeModel 6.0<meter> 90.0 ShotType.PlacedShot 0.0
              let far = makeModel 30.0<meter> 20.0 ShotType.Header 0.5
              let closeXg = xGCalculator.calculate close w
              let farXg = xGCalculator.calculate far w
              Expect.isGreaterThan closeXg farXg "close central shot should have higher xG"
          }

          test "calculate with high pressure yields lower xG" {
              let noPressure = makeModel 12.0<meter> 90.0 ShotType.PlacedShot 0.0
              let highPressure = makeModel 12.0<meter> 90.0 ShotType.PlacedShot 1.0
              let xgLow = xGCalculator.calculate noPressure w
              let xgHigh = xGCalculator.calculate highPressure w
              Expect.isGreaterThan xgLow xgHigh "no pressure should yield higher xG"
          }

          test "calculate returns value between 0 and 1" {
              let model = makeModel 10.0<meter> 90.0 ShotType.PlacedShot 0.0
              let xg = xGCalculator.calculate model w
              Expect.isGreaterThanOrEqual xg 0.0 "xG should be >= 0"
              Expect.isLessThanOrEqual xg 1.0 "xG should be <= 1"
          }

          test "one-on-one increases xG" {
              let baseModel = makeModel 10.0<meter> 90.0 ShotType.PlacedShot 0.0
              let oooModel = { baseModel with IsOneOnOne = true }
              let baseXg = xGCalculator.calculate baseModel w
              let oooXg = xGCalculator.calculate oooModel w
              Expect.isGreaterThan oooXg baseXg "one-on-one should increase xG"
          } ]
