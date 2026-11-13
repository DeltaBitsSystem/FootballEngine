namespace FootballEngine.World.Phases

open FootballEngine.Core.Match

/// A background fixture played without ticks: scores drawn from the engine's fitted scoring rates, scorers,
/// cards, fouls, injuries and substitutions from its fitted shares. It reads the same MatchContext as the engine,
/// so a stronger lineup is stronger here for the same reason.
module QuickMatch =

    type Played =
        { HomeScore: int
          AwayScore: int
          Events: MatchEvent list
          HomeStats: MatchPlayerStats[]
          AwayStats: MatchPlayerStats[]
          Injured: Set<FootballEngine.Domain.PlayerId> }

    let private attributes (p: PlayerProfile) =
        [| p.Pace; p.Strength; p.Stamina; p.Agility; p.Vision; p.Passing; p.Finishing; p.Crossing; p.Dribbling
           p.Tackling; p.Marking; p.Positioning; p.Anticipation; p.Composure; p.Teamwork; p.DecisionMaking |]

    let private starters (lineup: Lineup) =
        lineup.Profiles |> Array.truncate (min lineup.StarterCount lineup.Profiles.Length)

    /// Mean attribute of the starting eleven, on the engine's 0..100 scale.
    let strength (lineup: Lineup) : float =
        let players = starters lineup

        if players.Length = 0 then
            0.0
        else
            players |> Array.averageBy (fun p -> attributes p |> Array.averageBy float)

    /// Expected goals of the home and the away side.
    let rates (cfg: QuickMatchConfig) (ctx: MatchContext) : float * float =
        let gap = strength ctx.HomeLineup - strength ctx.AwayLineup

        cfg.GoalsPerTeam * exp (cfg.HomeAdvantage + cfg.StrengthSensitivity * gap),
        cfg.GoalsPerTeam * exp (-cfg.StrengthSensitivity * gap)

    let roleWeight (w: QuickRoleWeights) (role: SlotRole) =
        match role with
        | SlotRole.Goalkeeper -> w.Keeper
        | SlotRole.Defender -> w.CentreBack
        | SlotRole.FullBack -> w.Fullback
        | SlotRole.DefensiveMid -> w.Holding
        | SlotRole.CenterMid -> w.Central
        | SlotRole.AttackingMid -> w.Playmaker
        | SlotRole.Winger -> w.Wide
        | SlotRole.Striker -> w.Forward

    /// Knuth's method: exact, and rates here stay far below where its cost would matter.
    let private poisson (lambda: float) (rng: Rng) : int * Rng =
        let limit = exp (-lambda)
        let mutable product, rng = Rng.double rng
        let mutable count = 0

        while product > limit do
            let u, next = Rng.double rng
            product <- product * u
            rng <- next
            count <- count + 1

        count, rng

    let private pick (weights: float[]) (rng: Rng) =
        if Array.sum weights <= 0.0 then
            let index, rng = Rng.intIn 0 (weights.Length - 1) rng
            index, rng
        else
            Rng.weightedPick weights rng

    let private domainId (PlayerId n) = n

    /// One side's match: who is on the pitch when, who scores, and what the referee and the medics record.
    type private Side =
        { Club: ClubSide
          Lineup: Lineup
          Minutes: int[]
          Stats: MatchPlayerStats[] }

    let private sideOf club (lineup: Lineup) =
        { Club = club
          Lineup = lineup
          Minutes = Array.zeroCreate lineup.Profiles.Length
          Stats =
            lineup.Profiles
            |> Array.map (fun p ->
                { PlayerId = domainId p.Id
                  GoalsScored = 0
                  AssistsGiven = 0
                  MinutesPlayed = 0
                  YellowCards = 0
                  RedCards = 0
                  FoulsCommitted = 0 }) }

    let simulate (ctx: MatchContext) (seed: uint64) : Played =
        let cfg = ctx.Config.QuickMatch
        let mutable rng = Rng.ofSeed seed
        let draw f = let value, next = f rng in rng <- next; value
        let events = ResizeArray<MatchEvent>()
        let at minute = minute * SimulationClock.minuteSubticks
        let home = sideOf ClubSide.Home ctx.HomeLineup
        let away = sideOf ClubSide.Away ctx.AwayLineup
        let injured = System.Collections.Generic.HashSet<FootballEngine.Domain.PlayerId>()

        // Starters play the whole match unless replaced; a substitute plays from the minute he comes on.
        let substitute (side: Side) =
            let starterCount = min side.Lineup.StarterCount side.Lineup.Profiles.Length

            for slot in 0 .. starterCount - 1 do
                side.Minutes[slot] <- 90

            let bench = side.Lineup.Profiles.Length - starterCount
            let maximum = min bench ctx.SubstitutionRules.MaxSubstitutions
            let count = min maximum (draw (poisson cfg.SubstitutionsPerTeam))
            let replaced = System.Collections.Generic.HashSet<int>()

            for k in 0 .. count - 1 do
                let incoming = starterCount + k
                let outfield = [| for slot in 0 .. starterCount - 1 -> if side.Lineup.Profiles[slot].IsGK || replaced.Contains slot then 0.0 else 1.0 |]
                let outgoing = draw (pick outfield)
                let minute = draw (Rng.intIn cfg.FirstSubstitutionMinute 89)
                replaced.Add outgoing |> ignore
                side.Minutes[outgoing] <- minute
                side.Minutes[incoming] <- 90 - minute

                events.Add
                    { Tick = at minute
                      Data = EventData.Substitution(side.Lineup.Profiles[outgoing].Id, side.Lineup.Profiles[incoming].Id) }

        let onPitch (side: Side) slot = side.Minutes[slot] > 0

        let weighted (side: Side) (weight: PlayerProfile -> float) =
            side.Lineup.Profiles |> Array.mapi (fun slot p -> if onPitch side slot then weight p * float side.Minutes[slot] else 0.0)

        let score (side: Side) goals =
            let scorers = weighted side (fun p -> roleWeight cfg.ScorerByRole p.Role * float p.Finishing / 100.0)

            for _ in 1 .. goals do
                let scorer = draw (pick scorers)
                let minute = draw (Rng.intIn 1 90)
                side.Stats[scorer] <- { side.Stats[scorer] with GoalsScored = side.Stats[scorer].GoalsScored + 1 }

                let assist =
                    if draw (Rng.bernoulli cfg.AssistedShare) then
                        let assisters =
                            weighted side (fun p -> roleWeight cfg.AssistByRole p.Role * float (p.Passing + p.Vision) / 200.0)

                        assisters[scorer] <- 0.0

                        if Array.sum assisters > 0.0 then
                            let assister = draw (pick assisters)
                            side.Stats[assister] <- { side.Stats[assister] with AssistsGiven = side.Stats[assister].AssistsGiven + 1 }
                            Some side.Lineup.Profiles[assister].Id
                        else
                            None
                    else
                        None

                events.Add
                    { Tick = at minute
                      Data = EventData.Goal(side.Club, Some side.Lineup.Profiles[scorer].Id, assist, 0.0) }

        let discipline (side: Side) =
            let aggression = weighted side (fun p -> cfg.CalmFoulShare + float p.Aggression / 100.0)

            for _ in 1 .. draw (poisson cfg.FoulsPerTeam) do
                let fouler = draw (pick aggression)
                side.Stats[fouler] <- { side.Stats[fouler] with FoulsCommitted = side.Stats[fouler].FoulsCommitted + 1 }

            for _ in 1 .. draw (poisson cfg.YellowsPerTeam) do
                let player = draw (pick aggression)
                side.Stats[player] <- { side.Stats[player] with YellowCards = side.Stats[player].YellowCards + 1 }
                events.Add { Tick = at (draw (Rng.intIn 1 90)); Data = EventData.Card(side.Lineup.Profiles[player].Id, CardColor.Yellow) }

            for _ in 1 .. draw (poisson cfg.RedsPerTeam) do
                let player = draw (pick aggression)
                side.Stats[player] <- { side.Stats[player] with RedCards = side.Stats[player].RedCards + 1 }
                events.Add { Tick = at (draw (Rng.intIn 1 90)); Data = EventData.Card(side.Lineup.Profiles[player].Id, CardColor.Red) }

            let prone = weighted side (fun p -> cfg.RobustInjuryShare + float p.InjuryProne)

            for _ in 1 .. draw (poisson cfg.InjuriesPerTeam) do
                let player = draw (pick prone)
                let id = side.Lineup.Profiles[player].Id
                injured.Add(domainId id) |> ignore
                events.Add { Tick = at (draw (Rng.intIn 1 90)); Data = EventData.Injury(id, 1) }

        substitute home
        substitute away
        let homeRate, awayRate = rates cfg ctx
        let homeGoals = draw (poisson homeRate)
        let awayGoals = draw (poisson awayRate)
        score home homeGoals
        score away awayGoals
        discipline home
        discipline away

        let finish (side: Side) =
            side.Stats
            |> Array.mapi (fun slot stats -> { stats with MinutesPlayed = side.Minutes[slot] })
            |> Array.filter (fun stats -> stats.MinutesPlayed > 0)

        { HomeScore = homeGoals
          AwayScore = awayGoals
          Events = events |> Seq.sortBy (fun e -> e.Tick) |> List.ofSeq
          HomeStats = finish home
          AwayStats = finish away
          Injured = Set.ofSeq injured }
