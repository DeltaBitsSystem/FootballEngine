namespace FootballEngine.Simulation

open FootballEngine
open FootballEngine.Domain

open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

type SimSnapshot =
    { SubTick: int
      HomePositions: Spatial[]
      AwayPositions: Spatial[]
      BallX: float<meter>
      BallY: float<meter>
      BallVx: float<meter / second>
      BallVy: float<meter / second>
      BallZ: float<meter>
      BallVz: float<meter / second>
      BallSpinTop: float<radianPerSecond>
      BallSpinSide: float<radianPerSecond>
      Control: BallControl
      BallTrajectory: BallTrajectory option
      HomeScore: int
      AwayScore: int
      HomeConditions: int[]
      AwayConditions: int[]
      HomeSidelined: Map<PlayerId, PlayerOut>
      AwaySidelined: Map<PlayerId, PlayerOut>
      Momentum: float
      Flow: MatchFlow }

type MatchReplay =
    { Context: MatchContext
      Final: SimState
      Events: MatchEvent list
      Snapshots: SimSnapshot[] }

module SnapshotBuilder =

    let private positionsFromFrame (frame: TeamFrame) : Spatial[] =
        Array.init frame.SlotCount (fun i ->
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                { X = float frame.Physics.PosX[i] * 1.0<meter>
                  Y = float frame.Physics.PosY[i] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = float frame.Physics.VelX[i] * 1.0<meter / second>
                  Vy = float frame.Physics.VelY[i] * 1.0<meter / second>
                  Vz = 0.0<meter / second> }
            | _ -> kickOffSpatial)

    let private conditionsFromFrame (frame: TeamFrame) : int[] =
        Array.init frame.SlotCount (fun i -> int frame.Condition[i])

    /// Takes a snapshot of the current sim state.
    /// Only called from runLoopFull — never from runLoopFast.
    let take (state: SimState) : SimSnapshot =
        { SubTick = int state.SubTick
          HomePositions = positionsFromFrame state.Home.Frame
          AwayPositions = positionsFromFrame state.Away.Frame
          BallX = state.Ball.Position.X
          BallY = state.Ball.Position.Y
          BallVx = state.Ball.Position.Vx
          BallVy = state.Ball.Position.Vy
          BallZ = state.Ball.Position.Z
          BallVz = state.Ball.Position.Vz
          BallSpinTop = state.Ball.Spin.Top
          BallSpinSide = state.Ball.Spin.Side
          Control = state.Ball.Control
          BallTrajectory = state.Ball.Trajectory
          HomeScore = state.HomeScore
          AwayScore = state.AwayScore
          HomeConditions = conditionsFromFrame state.Home.Frame
          AwayConditions = conditionsFromFrame state.Away.Frame
          HomeSidelined = state.Home.Sidelined
          AwaySidelined = state.Away.Sidelined
          Momentum = state.Momentum
          Flow = state.Flow }
