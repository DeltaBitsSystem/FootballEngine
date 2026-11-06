namespace FootballEngine.Core.Match

/// The regulation solver uses physical contact; only shootouts retain this separate abstract kick resolution.
module Goalkeeping =

    /// Shootout save weight; regulation saves are produced by keeper-ball contact.
    let saveProbability (cfg: BalanceConfig) (attrs: float) (speed: float<meterPerSecond>) (gkDist: float) : float =
        MatchMath.clamp01 (
            cfg.Ball.ShootoutSaveBaseProbability
            + (attrs - 195.0) / 400.0
            + 0.35 * (20.0 - MatchMath.speedOf speed 0.0<meterPerSecond>) / 20.0
            - gkDist * float cfg.Ball.ShootoutSaveDistancePenalty / 200.0
        )
