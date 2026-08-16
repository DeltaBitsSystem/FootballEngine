namespace Training

open FootballEngine.Core.Match

/// A per-match target band. Error is zero inside [Lo; Hi]; outside it grows
/// quadratically relative to the band width.
type MetricTarget =
    { Name: string
      Target: float
      Lo: float
      Hi: float }

/// Aggregated stats of one simulated match.
type MatchMetrics =
    { Goals: float
      Shots: float
      OnTarget: float
      PassPct: float
      Fouls: float
      Yellows: float
      Corners: float
      Offsides: float
      PossessionHome: float }

/// A tunable knob: read/write into BalanceConfig plus hard bounds.
type Param =
    { Name: string
      Get: BalanceConfig -> float
      Set: BalanceConfig -> float -> BalanceConfig
      Min: float
      Max: float }

type EvalResult =
    { Error: float
      Metrics: MatchMetrics list }

/// Result of a tuning run: best config found, its error, and the per-param deltas
/// from the starting config.
type TuneResult =
    { Best: BalanceConfig
      Error: float
      StartError: float
      Evaluations: int
      History: float list }
