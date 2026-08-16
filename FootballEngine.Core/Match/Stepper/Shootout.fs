namespace FootballEngine.Core.Match

/// Penalty shootout: deterministic mini-simulation reusing Shot + GK save.
/// R10. The same formula Shot uses (xG → on-target → save) runs here with the
/// same threaded Rng, so no physics is duplicated.
module Shootout =

    /// On-target probability for a penalty at 11 m, mirroring Shot.launch
    /// (openness = 1 for a set piece).
    let private onTargetP (p: PlayerProfile) : float =
        MatchMath.clamp01 (0.5 + (float p.Finishing - 60.0) / 100.0 + 0.2)

    /// The five takers of a side: active outfield players by Finishing desc.
    let private takers (ctx: MatchContext) (world: World) (side: ClubSide) : PlayerProfile list =
        MatchWorld.activeSlots (MatchWorld.team world side).Frame
        |> Array.toList
        |> List.filter (fun s -> s <> 0)
        |> List.sortBy (fun s -> -float (MatchWorld.profileOf ctx side s).Finishing)
        |> List.truncate 5
        |> List.map (MatchWorld.profileOf ctx side)

    /// One kick. Consumes the shared Rng deterministically and returns whether
    /// the ball goes in, mirroring Shot/GK.save resolution.
    let private kick (ctx: SystemContext) (rng: Rng) (side: ClubSide) (taker: PlayerProfile) : bool * Rng =
        let cfg = ctx.Ctx.Config
        let gk = MatchWorld.profileOf ctx.Ctx (MatchWorld.oppOf side) 0
        let onTarget, rng' = Rng.bernoulli (onTargetP taker) rng

        if onTarget then
            let speed = 25.0<meterPerSecond>
            let gkDist = 11.0 * 0.7
            let gkAttrs = float (gk.Positioning + gk.Agility + gk.Anticipation)

            let saveP = Goalkeeping.saveProbability cfg gkAttrs speed gkDist
            let saved, rng'' = Rng.bernoulli saveP rng'
            not saved, rng''
        else
            false, rng'

    /// Runs the whole shootout and returns (homeShootoutGoals, awayShootoutGoals).
    /// Sudden death continues until one side leads after an equal pair.
    let resolve (ctx: SystemContext) (rng: Rng) (world: World) : (int * int) * Rng =
        let cfg = ctx.Ctx.Config.Match
        let homeTakers = takers ctx.Ctx world ClubSide.Home
        let awayTakers = takers ctx.Ctx world ClubSide.Away

        let mutable r = rng
        let mutable home = 0
        let mutable away = 0
        let rounds = cfg.ShootoutRounds

        // Regulated rounds.
        for i in 0 .. rounds - 1 do
            let hp = List.tryItem i homeTakers |> Option.defaultValue (List.head homeTakers)
            let scored, r' = kick ctx r ClubSide.Home hp
            r <- r'

            if scored then
                home <- home + 1

            let ap = List.tryItem i awayTakers |> Option.defaultValue (List.head awayTakers)
            let scoredA, r'' = kick ctx r ClubSide.Away ap
            r <- r''

            if scoredA then
                away <- away + 1

        // Sudden death.
        let mutable doneNow = home <> away
        let mutable round = rounds

        while not doneNow do
            let hp = List.tryItem round homeTakers |> Option.defaultValue (List.head homeTakers)
            let scored, r' = kick ctx r ClubSide.Home hp
            r <- r'

            if scored then
                home <- home + 1

            let ap = List.tryItem round awayTakers |> Option.defaultValue (List.head awayTakers)
            let scoredA, r'' = kick ctx r ClubSide.Away ap
            r <- r''

            if scoredA then
                away <- away + 1

            doneNow <- home <> away
            round <- round + 1

        (home, away), r
