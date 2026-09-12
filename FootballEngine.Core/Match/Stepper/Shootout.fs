namespace FootballEngine.Core.Match

/// Deterministic penalty shootout proposal. It never writes regulation score;
/// the reducer applies the returned kick facts to MatchState.Shootout.
module Shootout =

    type Kick =
        { Side: ClubSide
          Taker: PlayerId
          Scored: bool }

    let private onTargetP (p: PlayerProfile) : float =
        MatchMath.clamp01 (0.5 + (float p.Finishing - 60.0) / 100.0 + 0.2)

    let private takers (ctx: MatchContext) (world: World) (side: ClubSide) : PlayerProfile list =
        let lineup = MatchWorld.lineup ctx side

        MatchWorld.activeSlots (MatchWorld.team world side).Frame
        |> Array.toList
        |> List.sortBy (fun slot -> -float lineup.Profiles.[slot].Finishing, slot)
        |> List.map (fun slot -> lineup.Profiles.[slot])

    let private takerAt (players: PlayerProfile list) index : PlayerProfile option =
        if players.IsEmpty then
            None
        else
            Some players.[index % players.Length]

    let private kick (ctx: SystemContext) (rng: Rng) (side: ClubSide) (taker: PlayerProfile) : bool * Rng =
        let gk = MatchWorld.profileOf ctx.Ctx (MatchWorld.oppOf side) 0
        let onTarget, rng' = Rng.bernoulli (onTargetP taker) rng

        if onTarget then
            let speed = 25.0<meterPerSecond>
            let gkDist = 11.0 * 0.7
            let gkAttrs = float (gk.Positioning + gk.Agility + gk.Anticipation)
            let saveP = Goalkeeping.saveProbability ctx.Ctx.Config gkAttrs speed gkDist
            let saved, rng'' = Rng.bernoulli saveP rng'
            not saved, rng''
        else
            false, rng'

    let resolveDetailed (ctx: SystemContext) (rng: Rng) (world: World) : Kick list * Rng =
        let cfg = ctx.Ctx.Config.Match
        let homeTakers = takers ctx.Ctx world ClubSide.Home
        let awayTakers = takers ctx.Ctx world ClubSide.Away
        let mutable result = ResizeArray<Kick>()
        let mutable r = rng
        let mutable home = 0
        let mutable away = 0
        let mutable round = 0
        let mutable regulated = true

        while regulated && round < cfg.ShootoutRounds do
            match takerAt homeTakers round, takerAt awayTakers round with
            | Some homeTaker, Some awayTaker ->
                let homeScored, r' = kick ctx r ClubSide.Home homeTaker
                let awayScored, r'' = kick ctx r' ClubSide.Away awayTaker
                r <- r''

                result.Add(
                    { Side = ClubSide.Home
                      Taker = homeTaker.Id
                      Scored = homeScored }
                )

                result.Add(
                    { Side = ClubSide.Away
                      Taker = awayTaker.Id
                      Scored = awayScored }
                )

                if homeScored then
                    home <- home + 1

                if awayScored then
                    away <- away + 1

                let remaining = cfg.ShootoutRounds - round - 1
                regulated <- abs (home - away) <= remaining
                round <- round + 1
            | _ -> regulated <- false

        while home = away && round < 1000 do
            match takerAt homeTakers round, takerAt awayTakers round with
            | Some homeTaker, Some awayTaker ->
                let homeScored, r' = kick ctx r ClubSide.Home homeTaker
                let awayScored, r'' = kick ctx r' ClubSide.Away awayTaker
                r <- r''

                result.Add(
                    { Side = ClubSide.Home
                      Taker = homeTaker.Id
                      Scored = homeScored }
                )

                result.Add(
                    { Side = ClubSide.Away
                      Taker = awayTaker.Id
                      Scored = awayScored }
                )

                if homeScored then
                    home <- home + 1

                if awayScored then
                    away <- away + 1

                round <- round + 1
            | _ -> round <- 1000

        (List.ofSeq result), r

    /// Compatibility helper for callers that only need the shootout result.
    let resolve (ctx: SystemContext) (rng: Rng) (world: World) : (int * int) * Rng =
        let kicks, rng' = resolveDetailed ctx rng world

        let home =
            kicks
            |> List.filter (fun kick -> kick.Side = ClubSide.Home && kick.Scored)
            |> List.length

        let away =
            kicks
            |> List.filter (fun kick -> kick.Side = ClubSide.Away && kick.Scored)
            |> List.length

        (home, away), rng'
