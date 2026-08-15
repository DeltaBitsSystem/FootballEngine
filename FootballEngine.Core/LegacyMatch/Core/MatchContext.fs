namespace FootballEngine

open FootballEngine.Types
open FootballEngine.Domain


type MatchContext =
    { Home: Club
      Away: Club
      HomeCoach: Staff
      AwayCoach: Staff
      HomePlayers: Player[]
      AwayPlayers: Player[]
      HomeBasePositions: Spatial[]
      AwayBasePositions: Spatial[]
      HomeChemistry: ChemistryGraph
      AwayChemistry: ChemistryGraph
      IsKnockoutMatch: bool
      Config: BalanceConfig
      HomeRoster: PlayerRoster
      AwayRoster: PlayerRoster }
