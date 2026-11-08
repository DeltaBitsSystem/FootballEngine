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

/// Tuning for the World Pulse story engine; kept apart from WorldConfig so saves and mods are unaffected.
[<Struct>]
type WorldPulseConfig =
    { NewsRetention: int
      DailyBudgetUserClub: int
      DailyBudgetUserLeague: int
      DailyBudgetWorld: int
      DuplicateWindowDays: int
      InitialBoardConfidence: int
      InitialFansMood: int
      RecentResultsKept: int
      BigWinMargin: int
      UpsetReputationGap: int
      StreakThresholds: int list
      StreakMajorLength: int
      NewLeaderMinRound: int
      HatTrickGoals: int
      ScoringRunMatches: int
      ClasicoLookaheadDays: int
      ClasicoTopReputationCount: int
      ClasicoMinMeetings: int
      ClasicoMaxGoalDifference: int
      ExpectedPointsBase: float
      ExpectedPointsSpread: float
      ExpectedPointsMin: float
      ExpectedPointsMax: float
      ReputationScale: float
      HomeAdvantagePoints: float
      ConfidenceGain: float
      FansMoodGain: float
      MeanReversionRate: float
      BoardWarningThreshold: int
      UltimatumThreshold: int
      SackThreshold: int
      MinMatchesBeforeSack: int
      HoneymoonConfidence: int
      ReplacementContractYears: int
      FansFuriousBelow: int
      FansRestlessBelow: int
      FansEuphoricFrom: int }

module WorldPulseConfig =

    let defaultWorldPulse =
        { NewsRetention = 500
          DailyBudgetUserClub = 4
          DailyBudgetUserLeague = 4
          DailyBudgetWorld = 3
          DuplicateWindowDays = 14
          InitialBoardConfidence = 60
          InitialFansMood = 60
          RecentResultsKept = 10
          BigWinMargin = 3
          // Club reputation spans 500..9999 (ClubGen); 1500 is roughly the gap between a mid-table side and a
          // top club, so a win across it is surprising without making upsets daily events.
          UpsetReputationGap = 1500
          // Capped by RecentResultsKept: a streak longer than the retained history can never be observed.
          StreakThresholds = [ 3; 5; 8; 10 ]
          StreakMajorLength = 8
          // Two or three rounds of a table say nothing about who really leads.
          NewLeaderMinRound = 3
          HatTrickGoals = 3
          ScoringRunMatches = 3
          ClasicoLookaheadDays = 2
          ClasicoTopReputationCount = 2
          ClasicoMinMeetings = 4
          ClasicoMaxGoalDifference = 2
          // A league averages 1.35 points per club per match (3 split between winner/loser, 2 on a draw).
          ExpectedPointsBase = 1.35
          // tanh saturates, so even a 9999 vs 500 gap adds at most this much: a giant is expected to win, not to
          // collect 3 points every week.
          ExpectedPointsSpread = 0.8
          ExpectedPointsMin = 0.2
          ExpectedPointsMax = 2.6
          // ClubGen reputation spans 500..9999; a 2500 gap (a quarter of the range) already gives ~76% of the
          // spread, which matches mid-table versus top-club perception.
          ReputationScale = 2500.0
          HomeAdvantagePoints = 0.2
          // A defeat as an even side (expected ~1.35) costs ~7 confidence, so a run of ~6 defeats reaches the
          // sack line from the initial value, while a draw barely moves it.
          ConfidenceGain = 5.0
          FansMoodGain = 6.0
          // Slow pull to the initial value so a past run does not define a club forever.
          MeanReversionRate = 0.03
          BoardWarningThreshold = 40
          UltimatumThreshold = 25
          SackThreshold = 20
          // A league has 30+ rounds; a coach is not judged on a bad start of a few matches.
          MinMatchesBeforeSack = 8
          // Above the warning line so a fresh coach is not immediately in unrest, below the initial value
          // because the club just failed.
          HoneymoonConfidence = 55
          ReplacementContractYears = 2
          FansFuriousBelow = 20
          FansRestlessBelow = 45
          FansEuphoricFrom = 75 }

    let validate (config: WorldPulseConfig) : string list =
        [ if config.NewsRetention < 1 then
              yield "WorldPulse.NewsRetention must be >= 1"
          if config.DailyBudgetUserClub < 0 then
              yield "WorldPulse.DailyBudgetUserClub must be >= 0"
          if config.DailyBudgetUserLeague < 0 then
              yield "WorldPulse.DailyBudgetUserLeague must be >= 0"
          if config.DailyBudgetWorld < 0 then
              yield "WorldPulse.DailyBudgetWorld must be >= 0"
          if config.DuplicateWindowDays < 0 then
              yield "WorldPulse.DuplicateWindowDays must be >= 0"
          if config.InitialBoardConfidence < 0 || config.InitialBoardConfidence > 100 then
              yield "WorldPulse.InitialBoardConfidence must be in [0, 100]"
          if config.InitialFansMood < 0 || config.InitialFansMood > 100 then
              yield "WorldPulse.InitialFansMood must be in [0, 100]"
          if config.RecentResultsKept < 1 then
              yield "WorldPulse.RecentResultsKept must be >= 1"
          if config.BigWinMargin < 2 then
              yield "WorldPulse.BigWinMargin must be >= 2"
          if config.UpsetReputationGap < 1 then
              yield "WorldPulse.UpsetReputationGap must be >= 1"
          if
              config.StreakThresholds.IsEmpty
              || config.StreakThresholds
                 |> List.exists (fun n -> n < 2 || n > config.RecentResultsKept)
              || config.StreakThresholds
                 <> (config.StreakThresholds |> List.distinct |> List.sort)
          then
              yield "WorldPulse.StreakThresholds must be a non-empty ascending list within [2, RecentResultsKept]"
          if config.StreakMajorLength < 2 then
              yield "WorldPulse.StreakMajorLength must be >= 2"
          if config.NewLeaderMinRound < 1 then
              yield "WorldPulse.NewLeaderMinRound must be >= 1"
          if config.HatTrickGoals < 2 then
              yield "WorldPulse.HatTrickGoals must be >= 2"
          if config.ScoringRunMatches < 2 then
              yield "WorldPulse.ScoringRunMatches must be >= 2"
          if config.ClasicoLookaheadDays < 1 then
              yield "WorldPulse.ClasicoLookaheadDays must be >= 1"
          if config.ClasicoTopReputationCount < 2 then
              yield "WorldPulse.ClasicoTopReputationCount must be >= 2"
          if config.ClasicoMinMeetings < 1 then
              yield "WorldPulse.ClasicoMinMeetings must be >= 1"
          if config.ClasicoMaxGoalDifference < 0 then
              yield "WorldPulse.ClasicoMaxGoalDifference must be >= 0"
          if
              config.ExpectedPointsMin < 0.0
              || config.ExpectedPointsMax > 3.0
              || config.ExpectedPointsMin >= config.ExpectedPointsMax
          then
              yield "WorldPulse.ExpectedPoints bounds must satisfy 0 <= Min < Max <= 3"
          if
              config.ExpectedPointsBase < config.ExpectedPointsMin
              || config.ExpectedPointsBase > config.ExpectedPointsMax
          then
              yield "WorldPulse.ExpectedPointsBase must lie within [Min, Max]"
          if config.ExpectedPointsSpread < 0.0 then
              yield "WorldPulse.ExpectedPointsSpread must be >= 0"
          if config.ReputationScale <= 0.0 then
              yield "WorldPulse.ReputationScale must be > 0"
          if config.HomeAdvantagePoints < 0.0 then
              yield "WorldPulse.HomeAdvantagePoints must be >= 0"
          if config.ConfidenceGain < 0.0 then
              yield "WorldPulse.ConfidenceGain must be >= 0"
          if config.FansMoodGain < 0.0 then
              yield "WorldPulse.FansMoodGain must be >= 0"
          if config.MeanReversionRate < 0.0 || config.MeanReversionRate > 1.0 then
              yield "WorldPulse.MeanReversionRate must be in [0, 1]"
          if
              not (
                  0 <= config.SackThreshold
                  && config.SackThreshold < config.UltimatumThreshold
                  && config.UltimatumThreshold < config.BoardWarningThreshold
                  && config.BoardWarningThreshold <= 100
              )
          then
              yield "WorldPulse thresholds must satisfy 0 <= Sack < Ultimatum < BoardWarning <= 100"
          if config.MinMatchesBeforeSack < 1 then
              yield "WorldPulse.MinMatchesBeforeSack must be >= 1"
          if
              config.HoneymoonConfidence < config.BoardWarningThreshold
              || config.HoneymoonConfidence > 100
          then
              yield "WorldPulse.HoneymoonConfidence must be in [BoardWarningThreshold, 100]"
          if config.ReplacementContractYears < 1 then
              yield "WorldPulse.ReplacementContractYears must be >= 1"
          if
              not (
                  0 < config.FansFuriousBelow
                  && config.FansFuriousBelow < config.FansRestlessBelow
                  && config.FansRestlessBelow < config.FansEuphoricFrom
                  && config.FansEuphoricFrom <= 100
              )
          then
              yield "WorldPulse fans bands must satisfy 0 < Furious < Restless < Euphoric <= 100" ]
