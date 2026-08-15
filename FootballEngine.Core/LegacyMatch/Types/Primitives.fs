namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract


[<Struct>]
type Spatial =
    { X: float<meter>
      Y: float<meter>
      Z: float<meter>
      Vx: float<meter / second>
      Vy: float<meter / second>
      Vz: float<meter / second> }

    member this.XY = this.X, this.Y
    member this.XYZ = this.X, this.Y, this.Z
    member this.Vel = this.Vx, this.Vy, this.Vz

    member this.DistSqTo(other: Spatial) =
        let dx = this.X - other.X
        let dy = this.Y - other.Y
        let dz = this.Z - other.Z
        dx * dx + dy * dy + dz * dz

    member this.DistTo(other: Spatial) = sqrt (this.DistSqTo(other))

    member this.DistSqTo2D(other: Spatial) =
        let dx = this.X - other.X
        let dy = this.Y - other.Y
        dx * dx + dy * dy

    member this.DistTo2D(other: Spatial) = sqrt (this.DistSqTo2D(other))
    member this.VelMagSq = this.Vx * this.Vx + this.Vy * this.Vy + this.Vz * this.Vz
    member this.VelMag = sqrt this.VelMagSq

[<Struct>]
type Spin =
    { Top: float<radianPerSecond>
      Side: float<radianPerSecond> }

module Spin =
    let zero =
        { Top = 0.0<radianPerSecond>
          Side = 0.0<radianPerSecond> }

[<Struct>]
type PossessionHistory =
    { LastChangeTick: int
      LastBallInFlightTick: int
      LastSetPieceTick: int
      LastBallReceivedTick: int
      ChangedToSide: ClubSide option }

module PossessionHistory =
    let empty =
        { LastChangeTick = -1
          LastBallInFlightTick = -1
          LastSetPieceTick = -1
          LastBallReceivedTick = -1
          ChangedToSide = None }
