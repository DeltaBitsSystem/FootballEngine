namespace FootballEngine.Core.Match

/// Linear win-probability model feeding the directive choice. R9.
/// p = 0.5 + wGD·goalDiff + wXG·xGdiff + wMomentum·momentum + wMinute·(min/90 − 0.5)
module WinProbability =

    /// Win probability from the `side`'s perspective, computed from live data.
    /// Pure and deterministic — no Rng, no mutation.
    let estimate (ctx: MatchContext) (world: World) (side: ClubSide) : float =
        let cfg = ctx.Config.Team
        let team = MatchWorld.team world side
        let opp = MatchWorld.team world (MatchWorld.oppOf side)

        let myGoalDiff =
            if side = ClubSide.Home then
                world.State.HomeScore - world.State.AwayScore
            else
                world.State.AwayScore - world.State.HomeScore

        let myXgDiff = MatchMath.clamp -3.0 3.0 (team.Stats.XgSum - opp.Stats.XgSum)

        let myMomentum =
            match side with
            | ClubSide.Home -> max 0.0 world.State.Momentum
            | ClubSide.Away -> max 0.0 (-world.State.Momentum)

        let minute = int (SimulationClock.toMatchMinute world.State.Tick)

        let p =
            0.5
            + cfg.WinProbGoalDiffWeight * float myGoalDiff
            + cfg.WinProbXgDiffWeight * myXgDiff
            + cfg.WinProbMomentumWeight * myMomentum
            + cfg.WinProbMinuteWeight * (float minute / 90.0 - 0.5)

        MatchMath.clamp 0.05 0.95 p
