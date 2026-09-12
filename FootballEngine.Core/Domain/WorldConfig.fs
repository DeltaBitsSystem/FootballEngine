namespace FootballEngine.Domain

/// World rules are domain data so the domain never depends on the match engine.
[<Struct>]
type EconomyConfig =
    { BaseCurrency: string
      WeeklyWageReviewDays: int
      MinimumReserve: decimal }

[<Struct>]
type MarketConfig =
    { NegotiationWindowDays: int
      OfferExpiryDays: int
      ScoutRefreshDays: int }

[<Struct>]
type DevelopmentConfig =
    { WeeklyTrainingDays: int
      MaximumPotential: int
      DeclineStartAge: int }

[<Struct>]
type RelationshipsConfig =
    { MoraleDecayPerWeek: float
      ChemistryGainPerMatch: float
      ChemistryLossPerConflict: float }

[<Struct>]
type AiConfig =
    { DecisionCadenceDays: int
      MaxParallelAgents: int
      KnowledgeHorizonDays: int }

[<Struct>]
type MemoryRetentionConfig =
    { ScoutingDays: int
      RivalTacticsDays: int
      MatchTraceDays: int }

[<Struct>]
type WorldConfig =
    { Economy: EconomyConfig
      Market: MarketConfig
      Development: DevelopmentConfig
      Relationships: RelationshipsConfig
      Ai: AiConfig
      MemoryRetention: MemoryRetentionConfig }

module WorldConfig =

    let defaults =
        { Economy =
            { BaseCurrency = "EUR"
              WeeklyWageReviewDays = 7
              MinimumReserve = 0m }
          Market =
            { NegotiationWindowDays = 14
              OfferExpiryDays = 7
              ScoutRefreshDays = 30 }
          Development =
            { WeeklyTrainingDays = 7
              MaximumPotential = 200
              DeclineStartAge = 31 }
          Relationships =
            { MoraleDecayPerWeek = 0.02
              ChemistryGainPerMatch = 0.01
              ChemistryLossPerConflict = 0.03 }
          Ai =
            { DecisionCadenceDays = 1
              MaxParallelAgents = 1
              KnowledgeHorizonDays = 30 }
          MemoryRetention =
            { ScoutingDays = 365
              RivalTacticsDays = 180
              MatchTraceDays = 90 } }

    let validate (config: WorldConfig) : string list =
        [ if System.String.IsNullOrWhiteSpace config.Economy.BaseCurrency then
              yield "World.Economy.BaseCurrency must be non-empty"
          if config.Economy.WeeklyWageReviewDays < 1 then
              yield "World.Economy.WeeklyWageReviewDays must be >= 1"
          if config.Economy.MinimumReserve < 0m then
              yield "World.Economy.MinimumReserve must be >= 0"
          if config.Market.NegotiationWindowDays < 1 then
              yield "World.Market.NegotiationWindowDays must be >= 1"
          if config.Market.OfferExpiryDays < 1 then
              yield "World.Market.OfferExpiryDays must be >= 1"
          if config.Market.ScoutRefreshDays < 1 then
              yield "World.Market.ScoutRefreshDays must be >= 1"
          if
              config.Development.WeeklyTrainingDays < 1
              || config.Development.WeeklyTrainingDays > 7
          then
              yield "World.Development.WeeklyTrainingDays must be in [1, 7]"
          if config.Development.MaximumPotential < 1 then
              yield "World.Development.MaximumPotential must be >= 1"
          if config.Development.DeclineStartAge < 25 then
              yield "World.Development.DeclineStartAge must be >= 25"
          if config.Relationships.MoraleDecayPerWeek < 0.0 then
              yield "World.Relationships.MoraleDecayPerWeek must be >= 0"
          if config.Relationships.ChemistryGainPerMatch < 0.0 then
              yield "World.Relationships.ChemistryGainPerMatch must be >= 0"
          if config.Relationships.ChemistryLossPerConflict < 0.0 then
              yield "World.Relationships.ChemistryLossPerConflict must be >= 0"
          if config.Ai.DecisionCadenceDays < 1 then
              yield "World.Ai.DecisionCadenceDays must be >= 1"
          if config.Ai.MaxParallelAgents < 1 then
              yield "World.Ai.MaxParallelAgents must be >= 1"
          if config.Ai.KnowledgeHorizonDays < 0 then
              yield "World.Ai.KnowledgeHorizonDays must be >= 0"
          if config.MemoryRetention.ScoutingDays < 1 then
              yield "World.MemoryRetention.ScoutingDays must be >= 1"
          if config.MemoryRetention.RivalTacticsDays < 1 then
              yield "World.MemoryRetention.RivalTacticsDays must be >= 1"
          if config.MemoryRetention.MatchTraceDays < 1 then
              yield "World.MemoryRetention.MatchTraceDays must be >= 1" ]
