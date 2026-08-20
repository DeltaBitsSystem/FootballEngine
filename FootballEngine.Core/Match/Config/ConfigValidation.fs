namespace FootballEngine.Core.Match

/// Config sanity checks. The engine must never run with a self-contradictory
/// config: a negative budget or TTL would silently corrupt every decision,
/// and an invalid weights file should be loud, not silently ignored.
module ConfigValidation =

    let validatePlanner (p: PlannerConfig) : string list =
        [ if p.MaxChainLength < 1 then
              yield "Planner.MaxChainLength must be >= 1"
          if p.MaxEvaluateLimit < 1 then
              yield "Planner.MaxEvaluateLimit must be >= 1"
          if p.PlanTTL <= 0<subtick> then
              yield "Planner.PlanTTL must be > 0" ]

    let validateScout (c: ScoutConfig) : string list =
        [ if c.MemoryHalfLifeDays <= 0.0 then
              yield "Scout.MemoryHalfLifeDays must be > 0"
          if c.MinConfidenceToReport <= 0.0 || c.MinConfidenceToReport > 1.0 then
              yield "Scout.MinConfidenceToReport must be in (0, 1]"
          if c.AiReportNoise < 0.0 then
              yield "Scout.AiReportNoise must be >= 0"
          if c.NoiseByAbility < 0.0 then
              yield "Scout.NoiseByAbility must be >= 0"
          if c.RivalPruneAfterDays <= 0.0 then
              yield "Scout.RivalPruneAfterDays must be > 0"
          if c.ConfidenceMatches <= 0.0 then
              yield "Scout.ConfidenceMatches must be > 0"
          if c.ShortPassMaxMeters <= 0.0<meter> then
              yield "Scout.ShortPassMaxMeters must be > 0"
          if c.LongPassMinMeters <= 0.0<meter> then
              yield "Scout.LongPassMinMeters must be > 0"
          if c.NearShotMaxMeters <= 0.0<meter> then
              yield "Scout.NearShotMaxMeters must be > 0"
          if c.AreaShotMaxMeters <= 0.0<meter> then
              yield "Scout.AreaShotMaxMeters must be > 0"
          if c.ShortPassMaxMeters >= c.LongPassMinMeters then
              yield "Scout.ShortPassMaxMeters must be < LongPassMinMeters"
          if c.NearShotMaxMeters >= c.AreaShotMaxMeters then
              yield "Scout.NearShotMaxMeters must be < AreaShotMaxMeters" ]

    let validateCounter (c: CounterConfig) : string list =
        [ if c.MaxSideBias < 0.0 then
              yield "Counter.MaxSideBias must be >= 0"
          if c.BiasScale < 0.0 then
              yield "Counter.BiasScale must be >= 0"
          if c.MaxPressBoost < 0.0 then
              yield "Counter.MaxPressBoost must be >= 0"
          if c.MaxLineDrop < 0.0 then
              yield "Counter.MaxLineDrop must be >= 0"
          if c.MaxWidthTighten < 0.0 then
              yield "Counter.MaxWidthTighten must be >= 0"
          if c.MaxTempoShift < 0.0 then
              yield "Counter.MaxTempoShift must be >= 0"
          if c.MaxDirectnessShift < 0.0 then
              yield "Counter.MaxDirectnessShift must be >= 0"
          if c.SignatureChangePenalty <= 0.0 || c.SignatureChangePenalty > 1.0 then
              yield "Counter.SignatureChangePenalty must be in (0, 1]" ]

    let validateTransfer (c: TransferConfig) : string list =
        [ if c.BenchWarmerAgeMin < 0 then
              yield "Transfer.BenchWarmerAgeMin must be >= 0"
          if c.MinMatchesForSquadPlayer < 0 then
              yield "Transfer.MinMatchesForSquadPlayer must be >= 0"
          if c.MaxOver25BenchWarmers < 0 then
              yield "Transfer.MaxOver25BenchWarmers must be >= 0"
          if c.OverpayTolerance < 0.0m then
              yield "Transfer.OverpayTolerance must be >= 0" ]
