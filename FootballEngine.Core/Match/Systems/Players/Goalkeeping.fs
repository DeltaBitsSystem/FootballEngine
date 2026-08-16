namespace FootballEngine.Core.Match

/// Pure goalkeeper math shared by BallOutcome (open play) and Shootout
/// (penalties), so the save physics is never duplicated.
module Goalkeeping =

    /// Pure save probability for a shot with `speed` crossing near the GK at
    /// `gkDist`. Range [0,1]. Same formula BallOutcome uses.
    let saveProbability (cfg: BalanceConfig) (attrs: float) (speed: float<meterPerSecond>) (gkDist: float) : float =
        MatchMath.clamp01 (
            cfg.Ball.ShotSaveBaseProbability
            + (attrs - 195.0) / 400.0
            + 0.35 * (20.0 - MatchMath.speedOf speed 0.0<meterPerSecond>) / 20.0
            - gkDist * float cfg.Ball.SaveDistancePenalty / 200.0
        )
