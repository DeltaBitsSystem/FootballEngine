module FootballEngine.Tests.Layer2.GoalDetectorTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

let private makeBallAt (x: float<meter>) (y: float<meter>) (z: float<meter>) : BallPhysicsState =
    { Position =
        { X = x
          Y = y
          Z = z
          Vx = 0.0<meter / second>
          Vy = 0.0<meter / second>
          Vz = 0.0<meter / second> }
      Spin = Spin.zero
      Control = Free
      LastTouchBy = None
      PendingOffsideSnapshot = None
      StationarySinceSubTick = None
      GKHoldSinceSubTick = None
      PlayerHoldSinceSubTick = None
      Trajectory = None }

let goalDetectorTests =
    testList
        "GoalDetector"
        [ test "ball between posts at ground level detects home goal (LeftToRight)" {
              let ball = makeBallAt GoalLineHome PostNearY 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.equal result (Some HomeClub) "Ball on home goal line should detect HomeClub goal"
          }

          test "ball between posts at ground level detects away goal (LeftToRight)" {
              let ball = makeBallAt GoalLineAway PostNearY 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.equal result (Some AwayClub) "Ball on away goal line should detect AwayClub goal"
          }

          test "ball between posts at ground level detects home goal (RightToLeft)" {
              let ball = makeBallAt GoalLineAway PostNearY 0.0<meter>
              let result = GoalDetector.detect ball RightToLeft
              Expect.equal result (Some HomeClub) "Ball on away goal line (RTL) should detect HomeClub goal"
          }

          test "ball between posts at ground level detects away goal (RightToLeft)" {
              let ball = makeBallAt GoalLineHome PostNearY 0.0<meter>
              let result = GoalDetector.detect ball RightToLeft
              Expect.equal result (Some AwayClub) "Ball on home goal line (RTL) should detect AwayClub goal"
          }

          test "ball wide left returns None" {
              let ball = makeBallAt GoalLineHome 10.0<meter> 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.isNone result "Ball outside posts should return None"
          }

          test "ball wide right returns None" {
              let ball = makeBallAt GoalLineHome 50.0<meter> 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.isNone result "Ball outside posts should return None"
          }

          test "ball above crossbar returns None" {
              let ball = makeBallAt GoalLineHome PostNearY (CrossbarHeight + 0.1<meter>)
              let result = GoalDetector.detect ball LeftToRight
              Expect.isNone result "Ball above crossbar should return None"
          }

          test "ball at crossbar height is still a goal" {
              let ball = makeBallAt GoalLineHome PostNearY CrossbarHeight
              let result = GoalDetector.detect ball LeftToRight
              Expect.equal result (Some HomeClub) "Ball exactly at crossbar height should be a goal"
          }

          test "ball at ground level within goal is a goal" {
              let ball = makeBallAt GoalLineHome ((PostNearY + PostFarY) / 2.0) 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.equal result (Some HomeClub) "Ball centered between posts at ground level should be a goal"
          }

          test "ball between posts but off the goal line returns None" {
              let ball = makeBallAt (GoalLineHome - 1.0<meter>) PostNearY 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.isNone result "Ball one meter before goal line should return None"
          }

          test "ball on goal line with z=0 is a goal" {
              let ball = makeBallAt GoalLineHome PostNearY 0.0<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.equal result (Some HomeClub) "Ball on goal line at z=0 should be a goal"
          }

          test "ball below ground returns None" {
              let ball = makeBallAt GoalLineHome PostNearY -0.1<meter>
              let result = GoalDetector.detect ball LeftToRight
              Expect.isNone result "Ball below ground (z<0) should return None"
          } ]
