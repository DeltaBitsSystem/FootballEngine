namespace FootballEngine.Core.Match

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

/// Slot-indexed: the engine addresses players by fixed slot, so frame arrays
/// align 1:1 with the lineup.
type Lineup =
    { Profiles: PlayerProfile[]
      BasePositions: Spatial[]
      SlotCount: int }

/// Everything fixed before kickoff — clubs, lineups, config, stage. With the
/// match seed, it fully determines the simulation.
type MatchContext =
    { HomeClub: ClubRef
      AwayClub: ClubRef
      HomeLineup: Lineup
      AwayLineup: Lineup
      Config: BalanceConfig
      IsKnockout: bool }
