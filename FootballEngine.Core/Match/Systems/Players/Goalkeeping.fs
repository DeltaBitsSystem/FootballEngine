namespace FootballEngine.Core.Match

/// One save formula for every shot on goal: shared by BallOutcome (open play)
/// and Shootout (penalties) so the save physics is judged identically
/// everywhere and never duplicated.
module Goalkeeping =

    /// Save probability for a shot with `speed` crossing near the GK at
    /// `gkDist`. Clamped to [0,1] so every caller can use it directly as a
    /// Bernoulli weight; the same formula BallOutcome uses, by design.
    let saveProbability (cfg: BalanceConfig) (attrs: float) (speed: float<meterPerSecond>) (gkDist: float) : float =
        MatchMath.clamp01 (
            cfg.Ball.ShotSaveBaseProbability
            + (attrs - 195.0) / 400.0
            + 0.35 * (20.0 - MatchMath.speedOf speed 0.0<meterPerSecond>) / 20.0
            - gkDist * float cfg.Ball.SaveDistancePenalty / 200.0
        )
