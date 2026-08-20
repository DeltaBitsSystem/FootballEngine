namespace FootballEngine.Core.Match

/// Scouting: how long a rival's pattern is remembered, how many
/// sightings a report needs, and the noise that makes human reports biased
/// while AI reports stay exact.
[<Struct>]
type ScoutConfig =
    { MemoryHalfLifeDays: float
      MinConfidenceToReport: float
      AiReportNoise: float
      NoiseByAbility: float
      RivalPruneAfterDays: float
      ConfidenceMatches: float
      ShortPassMaxMeters: float<meter>
      LongPassMinMeters: float<meter>
      NearShotMaxMeters: float<meter>
      AreaShotMaxMeters: float<meter> }

/// Counter-planning: the ceiling on each tactical delta the pre-match
/// plan may add, and the penalty when the rival changed its signature.
[<Struct>]
type CounterConfig =
    { MaxSideBias: float
      BiasScale: float
      MaxPressBoost: float
      MaxLineDrop: float
      MaxWidthTighten: float
      MaxTempoShift: float
      MaxDirectnessShift: float
      SignatureChangePenalty: float }
