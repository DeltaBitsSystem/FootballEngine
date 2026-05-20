namespace FootballEngine.Types

open FootballEngine.Domain

[<Struct>]
type TeamPhase = Attacking | Defending | Transition

[<Struct>]
type OpponentShape = HighLine | MidBlock | LowBlock

[<Struct>]
type OpponentPressure = HighPress | MidPress | NoPress

type TeamBlackboard =
    { OurPhase: TeamPhase
      OpponentShape: OpponentShape
      OpponentPressure: OpponentPressure
      ThreatZones: FlankZone[]
      WeaknessZones: FlankZone[]
      MomentumStreak: float
      Urgency: float
      BallZone: PitchZone
      JustLostBall: bool
      PressTriggerZone: PitchZone
      CoordinationMemory: CoordinationMemory }

module TeamBlackboard =
    let empty =
        { OurPhase = TeamPhase.Defending
          OpponentShape = OpponentShape.MidBlock
          OpponentPressure = OpponentPressure.NoPress
          ThreatZones = [||]
          WeaknessZones = [||]
          MomentumStreak = 0.0
          Urgency = 0.0
          BallZone = MidfieldZone
          JustLostBall = false
          PressTriggerZone = MidfieldZone
          CoordinationMemory = CoordinationMemory.defaultMemory }
