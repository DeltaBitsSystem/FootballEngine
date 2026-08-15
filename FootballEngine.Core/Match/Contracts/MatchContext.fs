namespace FootballEngine.Core.Match

[<Struct>]
type TacticsConfig =
    { Formation: string
      DefensiveLine: float32
      PressIntensity: float32
      Width: float32
      Tempo: float32
      Directness: float32 }

[<Struct>]
type HomeAdvantageConfig =
    { CrowdSupport: float32
      Familiarity: float32 }

[<Struct>]
type ClubRef =
    { Id: uint64
      Name: string
      BaseTactics: TacticsConfig
      Chemistry: float
      HomeAdvantage: HomeAdvantageConfig }

/// Slot-indexed lineup.
type Lineup =
    { Profiles: PlayerProfile[]
      BasePositions: Spatial[]
      SlotCount: int }

type MatchContext =
    { HomeClub: ClubRef
      AwayClub: ClubRef
      HomeLineup: Lineup
      AwayLineup: Lineup
      Config: BalanceConfig
      IsKnockout: bool }
