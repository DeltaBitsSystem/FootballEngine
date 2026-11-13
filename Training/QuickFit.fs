namespace Training

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.World.Phases

/// Fits the background model to the full engine and checks the fit on matches it did not see. Strength varies
/// by shifting every attribute of one lineup, so the fit covers mismatches as well as equal sides.
module QuickFit =

    let private shifts = [| -8.0f; 0.0f; 8.0f |]

    let private shift (delta: float32) (lineup: Lineup) =
        let s v = max 5.0f (min 100.0f (v + delta))

        { lineup with
            Profiles =
                lineup.Profiles
                |> Array.map (fun p ->
                    { p with
                        Pace = s p.Pace; Strength = s p.Strength; Stamina = s p.Stamina; Agility = s p.Agility
                        Vision = s p.Vision; Passing = s p.Passing; Finishing = s p.Finishing; Crossing = s p.Crossing
                        Dribbling = s p.Dribbling; Tackling = s p.Tackling; Marking = s p.Marking
                        Positioning = s p.Positioning; Anticipation = s p.Anticipation; Composure = s p.Composure
                        Teamwork = s p.Teamwork; DecisionMaking = s p.DecisionMaking }) }

    /// Real fixtures of a generated world, each played under every shift pair: the generated squads give the
    /// fit real formations and real quality gaps, the shifts widen the gaps beyond one small league's.
    let private contexts (cfg: BalanceConfig) (seedsPerPair: int) (seedBase: uint64) =
        let gs = FootballEngine.Core.Tests.World.WorldGenerationTests.generate 0xC02UL []

        let fixtures =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq |> Seq.map snd)
            |> Seq.choose (fun fixture -> MatchContextBuilder.build gs fixture cfg)
            |> Seq.truncate 12
            |> Array.ofSeq

        [| for f, base' in Array.indexed fixtures do
               for h in shifts do
                   for a in shifts do
                       for k in 0 .. seedsPerPair - 1 do
                           let ctx =
                               { base' with
                                   HomeLineup = shift h base'.HomeLineup
                                   AwayLineup = shift a base'.AwayLineup }

                           (ctx, (h, a)), seedBase + uint64 (f * 1000003 + k * 7919 + int (h * 13.0f) * 31 + int (a * 17.0f)) |]

    type private Sample =
        { Gap: float
          HomeGoals: int
          AwayGoals: int
          /// Per side: yellows, reds, fouls, injuries, substitutions.
          Counts: float[]
          SubMinutes: int list
          ScorerRoles: SlotRole list
          AssistRoles: SlotRole list
          Assisted: int
          GoalEvents: int }

    let private roleOf (ctx: MatchContext) (player: PlayerId) =
        Array.append ctx.HomeLineup.Profiles ctx.AwayLineup.Profiles
        |> Array.tryFind (fun p -> p.Id = player)
        |> Option.map _.Role

    let private sample (ctx: MatchContext) (outcome: MatchOutcome) =
        let counts = Array.zeroCreate<float> 5
        let roles = ResizeArray()
        let assists = ResizeArray()
        let minutes = ResizeArray()
        let mutable assisted = 0
        let mutable goals = 0
        let bump index = counts[index] <- counts[index] + 0.5

        for event in outcome.Events do
            match event.Data with
            | EventData.Goal(side, scorer, assist, _) ->
                goals <- goals + 1
                // An own goal's scorer is a defender of the other side; the quick model leaves own goals out.
                scorer
                |> Option.filter (fun p -> MatchWorld.teamOfPlayer ctx p = Some side)
                |> Option.bind (roleOf ctx)
                |> Option.iter roles.Add

                match assist |> Option.bind (roleOf ctx) with
                | Some role ->
                    assisted <- assisted + 1
                    assists.Add role
                | None -> ()
            | EventData.Card(_, CardColor.Yellow) -> bump 0
            // RefereeCards issues every red as RedCardIssued; a Card Red alongside it is the same dismissal.
            | EventData.RedCardIssued _ -> bump 1
            | EventData.Foul _ -> bump 2
            | EventData.Injury _ -> bump 3
            | EventData.Substitution _ ->
                bump 4
                minutes.Add(int (event.Tick / SimulationClock.minuteSubticks))
            | _ -> ()

        { Gap = QuickMatch.strength ctx.HomeLineup - QuickMatch.strength ctx.AwayLineup
          HomeGoals = outcome.Final.State.HomeScore
          AwayGoals = outcome.Final.State.AwayScore
          Counts = counts
          SubMinutes = List.ofSeq minutes
          ScorerRoles = List.ofSeq roles
          AssistRoles = List.ofSeq assists
          Assisted = assisted
          GoalEvents = goals }

    /// Poisson regression of goals on (1, home, gap) by Newton's method; three parameters converge in a few steps.
    let private fitRates (samples: Sample[]) =
        let rows =
            [| for s in samples do
                   yield float s.HomeGoals, [| 1.0; 1.0; s.Gap |]
                   yield float s.AwayGoals, [| 1.0; 0.0; -s.Gap |] |]

        let mean = rows |> Array.averageBy fst
        let beta = [| log (max mean 0.1); 0.0; 0.0 |]

        for _ in 1 .. 25 do
            let gradient = Array.zeroCreate<float> 3
            let hessian = Array2D.zeroCreate<float> 3 3

            for y, x in rows do
                let mu = exp (Array.fold2 (fun acc b v -> acc + b * v) 0.0 beta x)

                for i in 0..2 do
                    gradient[i] <- gradient[i] + (y - mu) * x[i]

                    for j in 0..2 do
                        hessian[i, j] <- hessian[i, j] + mu * x[i] * x[j]

            // Gaussian elimination on the 3x3 system H·step = g.
            let m = Array2D.init 3 4 (fun i j -> if j < 3 then hessian[i, j] else gradient[i])

            for c in 0..2 do
                for r in c + 1 .. 2 do
                    let f = m[r, c] / m[c, c]

                    for k in c..3 do
                        m[r, k] <- m[r, k] - f * m[c, k]

            let step = Array.zeroCreate<float> 3

            for r in 2 .. -1 .. 0 do
                let tail = Seq.sum (seq { for k in r + 1 .. 2 -> m[r, k] * step[k] })
                step[r] <- (m[r, 3] - tail) / m[r, r]

            for i in 0..2 do
                beta[i] <- beta[i] + step[i]

        exp beta[0], beta[1], beta[2]

    let private roleIndex role =
        match role with
        | SlotRole.Goalkeeper -> 0
        | SlotRole.Defender -> 1
        | SlotRole.FullBack -> 2
        | SlotRole.DefensiveMid -> 3
        | SlotRole.CenterMid -> 4
        | SlotRole.AttackingMid -> 5
        | SlotRole.Winger -> 6
        | SlotRole.Striker -> 7

    /// A role's weight is its goals per unit of the quick model's exposure (attribute × starters in that role),
    /// so the model reproduces the engine's share by role.
    let private roleWeights (ctxs: MatchContext[]) (roles: SlotRole list) (exposure: PlayerProfile -> float) =
        let found = Array.zeroCreate<float> 8
        let exposed = Array.zeroCreate<float> 8

        for role in roles do
            found[roleIndex role] <- found[roleIndex role] + 1.0

        for ctx in ctxs do
            for lineup in [ ctx.HomeLineup; ctx.AwayLineup ] do
                for p in lineup.Profiles |> Array.truncate lineup.StarterCount do
                    exposed[roleIndex p.Role] <- exposed[roleIndex p.Role] + exposure p

        let raw = Array.init 8 (fun i -> if exposed[i] > 0.0 then found[i] / exposed[i] else 0.0)
        let top = max 1e-9 (Array.max raw)
        let w = raw |> Array.map (fun v -> v / top)

        { Keeper = w[0]; CentreBack = w[1]; Fullback = w[2]; Holding = w[3]; Central = w[4]; Playmaker = w[5]
          Wide = w[6]; Forward = w[7] }

    let private engine (pairs: ((MatchContext * (float32 * float32)) * uint64)[]) =
        pairs |> Array.Parallel.map (fun ((ctx, _), seed) -> sample ctx (SimulatorRunner.runContext ctx seed))

    let fit (cfg: BalanceConfig) (seedsPerPair: int) : QuickMatchConfig =
        let pairs = contexts cfg seedsPerPair 1UL
        let samples = engine pairs

        printfn "mean goals by lineup shift (home shift, away shift): home - away"

        for h in shifts do
            for a in shifts do
                let group =
                    Array.zip pairs samples
                    |> Array.filter (fun (((_, shift), _), _) -> shift = (h, a))
                    |> Array.map snd

                if group.Length > 0 then
                    printfn "  %+5.0f %+5.0f: %.2f - %.2f" h a (group |> Array.averageBy (fun s -> float s.HomeGoals)) (group |> Array.averageBy (fun s -> float s.AwayGoals))

        let goals, home, sensitivity = fitRates samples
        let perTeam index = samples |> Array.averageBy (fun s -> s.Counts[index])
        let ctxs = pairs |> Array.map (fst >> fst)
        let allGoals = samples |> Array.sumBy _.GoalEvents
        let subMinutes = samples |> Array.collect (fun s -> Array.ofList s.SubMinutes)

        { cfg.QuickMatch with
            GoalsPerTeam = goals
            HomeAdvantage = home
            StrengthSensitivity = sensitivity
            AssistedShare = float (samples |> Array.sumBy _.Assisted) / float (max 1 allGoals)
            ScorerByRole = roleWeights ctxs (samples |> Seq.collect _.ScorerRoles |> List.ofSeq) (fun p -> float p.Finishing / 100.0)
            AssistByRole =
              roleWeights ctxs (samples |> Seq.collect _.AssistRoles |> List.ofSeq) (fun p -> float (p.Passing + p.Vision) / 200.0)
            YellowsPerTeam = perTeam 0
            RedsPerTeam = perTeam 1
            FoulsPerTeam = perTeam 2
            InjuriesPerTeam = perTeam 3
            SubstitutionsPerTeam = perTeam 4
            // The quick model draws substitution minutes uniformly from here to 89, so the engine's mean minute fixes it.
            FirstSubstitutionMinute =
              if subMinutes.Length = 0 then cfg.QuickMatch.FirstSubstitutionMinute
              else max 1 (min 89 (2 * int (Array.averageBy float subMinutes) - 89)) }

    type private Summary =
        { Goals: float; HomeWins: float; Draws: float; AwayWins: float; Margin: float }

    let private summarize (results: (int * int)[]) =
        let n = float results.Length
        let count f = float (results |> Array.filter f |> Array.length) / n

        { Goals = results |> Array.averageBy (fun (h, a) -> float (h + a))
          HomeWins = count (fun (h, a) -> h > a)
          Draws = count (fun (h, a) -> h = a)
          AwayWins = count (fun (h, a) -> h < a)
          Margin = results |> Array.averageBy (fun (h, a) -> float (h - a)) }

    /// Engine and quick model on held-out seeds, by strength gap: the fit holds if each row agrees within noise.
    let validate (cfg: BalanceConfig) (seedsPerPair: int) =
        let pairs = contexts cfg seedsPerPair 900001UL
        let samples = engine pairs
        let quick = pairs |> Array.map (fun ((ctx, _), seed) -> QuickMatch.simulate ctx (seed ^^^ 0x9E3779B97F4A7C15UL))

        printfn "gap     n   | engine goals  H/D/A        margin | quick goals  H/D/A        margin"

        let rows =
            Array.zip samples quick
            |> Array.groupBy (fun (s, _) -> System.Math.Round(s.Gap / 6.0) * 6.0)
            |> Array.sortBy fst

        for gap, group in rows do
            let e = summarize (group |> Array.map (fun (s, _) -> s.HomeGoals, s.AwayGoals))
            let q = summarize (group |> Array.map (fun (_, p) -> p.HomeScore, p.AwayScore))

            printfn
                "%+5.0f %5d   | %5.2f  %3.0f/%3.0f/%3.0f%%  %+5.2f | %5.2f  %3.0f/%3.0f/%3.0f%%  %+5.2f"
                gap group.Length e.Goals (100.0 * e.HomeWins) (100.0 * e.Draws) (100.0 * e.AwayWins) e.Margin
                q.Goals (100.0 * q.HomeWins) (100.0 * q.Draws) (100.0 * q.AwayWins) q.Margin

        let e = summarize (samples |> Array.map (fun s -> s.HomeGoals, s.AwayGoals))
        let q = summarize (quick |> Array.map (fun p -> p.HomeScore, p.AwayScore))
        printfn "all %7d   | %5.2f  %3.0f/%3.0f/%3.0f%%  %+5.2f | %5.2f  %3.0f/%3.0f/%3.0f%%  %+5.2f" samples.Length e.Goals (100.0 * e.HomeWins) (100.0 * e.Draws) (100.0 * e.AwayWins) e.Margin q.Goals (100.0 * q.HomeWins) (100.0 * q.Draws) (100.0 * q.AwayWins) q.Margin

        let stopwatch = System.Diagnostics.Stopwatch.StartNew()
        let (ctx, _), seed = pairs[0]

        for k in 1 .. 10000 do
            QuickMatch.simulate ctx (seed + uint64 k) |> ignore

        printfn "quick model: %.1f us per match" (stopwatch.Elapsed.TotalMilliseconds * 1000.0 / 10000.0)

    /// Every goal of a few real fixtures with who scored it, from which side and where the ball was.
    let traceGoals (cfg: BalanceConfig) (matches: int) =
        for ((ctx, _), seed) in contexts cfg 1 77UL |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f)) |> Array.truncate matches do
            let outcome = SimulatorRunner.runContext ctx seed
            let sideOf player =
                if ctx.HomeLineup.Profiles |> Array.exists (fun p -> p.Id = player) then "home" else "away"

            printfn "match %d: %d - %d" seed outcome.Final.State.HomeScore outcome.Final.State.AwayScore

            for event in outcome.Events do
                match event.Data with
                | EventData.Goal(side, scorer, _, _) ->
                    let who = scorer |> Option.map (fun p -> $"{roleOf ctx p} ({sideOf p})") |> Option.defaultValue "none"
                    printfn "  t=%d goal for %A by %s" (int event.Tick) side who
                | EventData.RedCardIssued p -> printfn "  t=%d red %s" (int event.Tick) (sideOf p)
                | _ -> ()

    /// Event counts per kind and side over real fixtures: a side bias shows as the first kind that differs.
    let sideCountsWith (transform: MatchContext -> MatchContext) (cfg: BalanceConfig) (matches: int) =
        let pairs =
            contexts cfg ((matches + 11) / 12) 555UL
            |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f))
            |> Array.truncate matches
            |> Array.map (fun ((ctx, s), seed) -> (transform ctx, s), seed)
        let counts = System.Collections.Concurrent.ConcurrentDictionary<string * string, int>()

        pairs
        |> Array.Parallel.iter (fun ((ctx, _), seed) ->
            let outcome = SimulatorRunner.runContext ctx seed
            let home = ctx.HomeLineup.Profiles |> Array.map _.Id |> Set.ofArray

            for event in outcome.Events do
                let case, fields = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(event.Data, typeof<EventData>)

                let side =
                    fields
                    |> Array.tryPick (fun f ->
                        match f with
                        | :? ClubSide as s -> Some(if s = ClubSide.Home then "home" else "away")
                        | :? PlayerId as p -> Some(if home.Contains p then "home" else "away")
                        | _ -> None)
                    |> Option.defaultValue "-"

                counts.AddOrUpdate((case.Name, side), 1, fun _ v -> v + 1) |> ignore)

        let n = float pairs.Length
        printfn "%-28s %9s %9s" "per match" "home" "away"

        for name in counts.Keys |> Seq.map fst |> Seq.distinct |> Seq.sort do
            let get s = match counts.TryGetValue((name, s)) with | true, v -> float v / n | _ -> 0.0
            printfn "%-28s %9.2f %9.2f" name (get "home") (get "away")

    /// Where each side has the ball and shoots from, in its own attacking direction.
    let sideFlowWith (transform: MatchContext -> MatchContext) (cfg: BalanceConfig) (matches: int) =
        let pairs =
            contexts cfg ((matches + 11) / 12) 555UL
            |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f))
            |> Array.truncate matches
            |> Array.map (fun ((ctx, s), seed) -> (transform ctx, s), seed)
        let lockObj = obj ()
        // Per side: ticks controlled in each attacking-depth band (own third, middle, final third), shots, shot distance sum, xG sum.
        let acc = Array2D.zeroCreate<float> 2 6

        pairs
        |> Array.Parallel.iter (fun ((ctx, _), seed) ->
            let local = Array2D.zeroCreate<float> 2 6
            let home = ctx.HomeLineup.Profiles |> Array.map _.Id |> Set.ofArray

            SimulatorRunner.runContextObserved ctx seed (fun world events ->
                match world.State.Ball.Control with
                | BallControl.Controlled(side, _) ->
                    let i = if side = ClubSide.Home then 0 else 1
                    let depth = float world.State.Ball.Position.X * (if side = ClubSide.Home then 1.0 else -1.0)
                    let band = if depth < -17.5 then 0 elif depth < 17.5 then 1 else 2
                    local[i, band] <- local[i, band] + 1.0
                | _ -> ()

                for event in events do
                    match event.Data with
                    | EventData.Shot(shooter, _, xG, _, x, y) ->
                        let i = if home.Contains shooter then 0 else 1
                        let goalX = if i = 0 then 52.5 else -52.5
                        local[i, 3] <- local[i, 3] + 1.0
                        local[i, 4] <- local[i, 4] + sqrt ((float x - goalX) ** 2.0 + float y ** 2.0)
                        local[i, 5] <- local[i, 5] + float xG
                    | _ -> ())
            |> ignore

            lock lockObj (fun () ->
                for i in 0..1 do
                    for j in 0..5 do
                        acc[i, j] <- acc[i, j] + local[i, j]))

        for i, name in [ 0, "home"; 1, "away" ] do
            let controlled = acc[i, 0] + acc[i, 1] + acc[i, 2]
            printfn
                "%s: control %.0f ticks/match, own/mid/final %.0f/%.0f/%.0f%%, shots %.1f, mean distance %.1f m, xG %.2f"
                name (controlled / float pairs.Length) (100.0 * acc[i, 0] / controlled) (100.0 * acc[i, 1] / controlled)
                (100.0 * acc[i, 2] / controlled) (acc[i, 3] / float pairs.Length) (acc[i, 4] / max 1.0 acc[i, 3])
                (acc[i, 5] / float pairs.Length)

    /// One lineup lifted and the other lowered by `delta` on every attribute, then the reverse: a quality-blind
    /// engine shows the same score both ways.
    let contrast (cfg: BalanceConfig) (delta: float32) (matches: int) =
        let fixtures = contexts cfg ((matches + 11) / 12) 4242UL |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f)) |> Array.truncate matches

        let play h a =
            fixtures
            |> Array.Parallel.map (fun ((ctx, _), seed) ->
                let ctx = { ctx with HomeLineup = shift h ctx.HomeLineup; AwayLineup = shift a ctx.AwayLineup }
                let outcome = SimulatorRunner.runContext ctx seed
                let shots side = outcome.Events |> List.filter (fun e -> match e.Data with EventData.Shot(p, _, _, _, _, _) -> MatchWorld.teamOfPlayer ctx p = Some side | _ -> false) |> List.length
                float outcome.Final.State.HomeScore, float outcome.Final.State.AwayScore, float (shots ClubSide.Home), float (shots ClubSide.Away))

        for h, a in [ delta, -delta; 0.0f, 0.0f; -delta, delta ] do
            let r = play h a
            let mean f = r |> Array.averageBy f
            printfn "home %+3.0f away %+3.0f: goals %.2f - %.2f, shots %.1f - %.1f" h a (mean (fun (x, _, _, _) -> x)) (mean (fun (_, x, _, _) -> x)) (mean (fun (_, _, x, _) -> x)) (mean (fun (_, _, _, x) -> x))

    /// The contrast with only one attribute group moved, to find the group whose effect has the wrong sign.
    let contrastGroups (cfg: BalanceConfig) (delta: float32) (matches: int) =
        let fixtures = contexts cfg ((matches + 11) / 12) 4242UL |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f)) |> Array.truncate matches
        let s d v = max 5.0f (min 100.0f (v + d))

        let groups: (string * (float32 -> PlayerProfile -> PlayerProfile)) list =
            [ "physical", fun d p -> { p with Pace = s d p.Pace; Strength = s d p.Strength; Stamina = s d p.Stamina; Agility = s d p.Agility }
              "technical", fun d p -> { p with Passing = s d p.Passing; Finishing = s d p.Finishing; Crossing = s d p.Crossing; Dribbling = s d p.Dribbling; Vision = s d p.Vision }
              "defending", fun d p -> { p with Tackling = s d p.Tackling; Marking = s d p.Marking; Positioning = s d p.Positioning; Anticipation = s d p.Anticipation }
              "mental", fun d p -> { p with Composure = s d p.Composure; Teamwork = s d p.Teamwork; DecisionMaking = s d p.DecisionMaking } ]

        for name, f in groups do
            let r =
                fixtures
                |> Array.Parallel.map (fun ((ctx, _), seed) ->
                    let lift d (l: Lineup) = { l with Profiles = l.Profiles |> Array.map (f d) }
                    let ctx = { ctx with HomeLineup = lift delta ctx.HomeLineup; AwayLineup = lift -delta ctx.AwayLineup }
                    let outcome = SimulatorRunner.runContext ctx seed
                    let isHome p = MatchWorld.teamOfPlayer ctx p = Some ClubSide.Home
                    // Per side: shots, passes, completed passes, passes intercepted, passes from the own third.
                    let c = Array2D.zeroCreate<float> 2 5
                    let bump p k = let i = if isHome p then 0 else 1 in c[i, k] <- c[i, k] + 1.0

                    for e in outcome.Events do
                        match e.Data with
                        | EventData.Shot(p, _, _, _, _, _) -> bump p 0
                        | EventData.Pass(p, _, _, _, completed, fromX, _, _, _) ->
                            bump p 1
                            if completed then bump p 2
                            if float fromX * MatchMath.goalDirection (if isHome p then ClubSide.Home else ClubSide.Away) < -17.5 then bump p 4
                        | EventData.Interception(_, passer) -> bump passer 3
                        | _ -> ()

                    float outcome.Final.State.HomeScore, float outcome.Final.State.AwayScore, c)

            let mean f = r |> Array.averageBy f
            let m i k = mean (fun (_, _, c: float[,]) -> c[i, k])
            printfn "%-10s stronger home: goals %.2f - %.2f, shots %.1f - %.1f, passes %.0f - %.0f, completed %.0f%% - %.0f%%, intercepted %.1f - %.1f, own third %.0f%% - %.0f%%"
                name (mean (fun (x, _, _) -> x)) (mean (fun (_, x, _) -> x)) (m 0 0) (m 1 0) (m 0 1) (m 1 1)
                (100.0 * m 0 2 / m 0 1) (100.0 * m 1 2 / m 1 1) (m 0 3) (m 1 3) (100.0 * m 0 4 / m 0 1) (100.0 * m 1 4 / m 1 1)

    let sideCounts cfg matches = sideCountsWith id cfg matches
    let sideFlow cfg matches = sideFlowWith id cfg matches

    let private technical (delta: float32) (ctx: MatchContext) =
        let s d v = max 5.0f (min 100.0f (v + d))
        let lift d (l: Lineup) =
            { l with
                Profiles =
                    l.Profiles
                    |> Array.map (fun p ->
                        { p with Passing = s d p.Passing; Finishing = s d p.Finishing; Crossing = s d p.Crossing; Dribbling = s d p.Dribbling; Vision = s d p.Vision }) }
        { ctx with HomeLineup = lift delta ctx.HomeLineup; AwayLineup = lift -delta ctx.AwayLineup }

    let technicalFlow cfg delta matches = sideFlowWith (technical delta) cfg matches

    /// Event counts with the home side's technique lifted and the away side's lowered.
    let technicalCounts (cfg: BalanceConfig) (delta: float32) (matches: int) =
        let s d v = max 5.0f (min 100.0f (v + d))
        let lift d (l: Lineup) =
            { l with
                Profiles =
                    l.Profiles
                    |> Array.map (fun p ->
                        { p with Passing = s d p.Passing; Finishing = s d p.Finishing; Crossing = s d p.Crossing; Dribbling = s d p.Dribbling; Vision = s d p.Vision }) }
        sideCountsWith (fun ctx -> { ctx with HomeLineup = lift delta ctx.HomeLineup; AwayLineup = lift -delta ctx.AwayLineup }) cfg matches

    /// The contrast moving one named attribute only.
    let contrastAttributes (cfg: BalanceConfig) (delta: float32) (matches: int) (names: string list) =
        let fixtures = contexts cfg ((matches + 11) / 12) 4242UL |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f)) |> Array.truncate matches
        let fields = Microsoft.FSharp.Reflection.FSharpType.GetRecordFields typeof<PlayerProfile>

        for name in names do
            let index = fields |> Array.findIndex (fun f -> f.Name = name)
            let lift d (l: Lineup) =
                { l with
                    Profiles =
                        l.Profiles
                        |> Array.map (fun p ->
                            let values = Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields p
                            values[index] <- box (max 5.0f (min 100.0f (unbox<float32> values[index] + d)))
                            Microsoft.FSharp.Reflection.FSharpValue.MakeRecord(typeof<PlayerProfile>, values) :?> PlayerProfile) }

            let r =
                fixtures
                |> Array.Parallel.map (fun ((ctx, _), seed) ->
                    let ctx = { ctx with HomeLineup = lift delta ctx.HomeLineup; AwayLineup = lift -delta ctx.AwayLineup }
                    let o = SimulatorRunner.runContext ctx seed
                    let passes side = o.Events |> List.filter (fun e -> match e.Data with EventData.Pass(p, _, _, _, _, _, _, _, _) -> MatchWorld.teamOfPlayer ctx p = Some side | _ -> false) |> List.length
                    float o.Final.State.HomeScore, float o.Final.State.AwayScore, float (passes ClubSide.Home), float (passes ClubSide.Away))

            let mean f = r |> Array.averageBy f
            printfn "%-14s stronger home: goals %.2f - %.2f, passes %.0f - %.0f" name (mean (fun (a, _, _, _) -> a)) (mean (fun (_, b, _, _) -> b)) (mean (fun (_, _, c, _) -> c)) (mean (fun (_, _, _, d) -> d))

    let contrastFlow cfg (delta: float32) matches =
        sideFlowWith (fun ctx -> { ctx with HomeLineup = shift delta ctx.HomeLineup; AwayLineup = shift -delta ctx.AwayLineup }) cfg matches

    /// Where each side wins the ball (its own attacking frame: own, middle, final third) and how many of its shots
    /// come within 10 s of winning it, with one named attribute moved +delta home / -delta away.
    let turnovers (cfg: BalanceConfig) (delta: float32) (matches: int) (name: string) =
        let fixtures = contexts cfg ((matches + 11) / 12) 4242UL |> Array.filter (fun ((_, s), _) -> s = (0.0f, 0.0f)) |> Array.truncate matches
        let fields = Microsoft.FSharp.Reflection.FSharpType.GetRecordFields typeof<PlayerProfile>
        let index = fields |> Array.findIndex (fun f -> f.Name = name)

        let lift d (l: Lineup) =
            { l with
                Profiles =
                    l.Profiles
                    |> Array.map (fun p ->
                        let values = Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields p
                        values[index] <- box (max 5.0f (min 100.0f (unbox<float32> values[index] + d)))
                        Microsoft.FSharp.Reflection.FSharpValue.MakeRecord(typeof<PlayerProfile>, values) :?> PlayerProfile) }

        let acc = Array2D.zeroCreate<float> 2 5
        let lockObj = obj ()

        fixtures
        |> Array.Parallel.iter (fun ((ctx, _), seed) ->
            let ctx = { ctx with HomeLineup = lift delta ctx.HomeLineup; AwayLineup = lift -delta ctx.AwayLineup }
            let local = Array2D.zeroCreate<float> 2 5
            let mutable holder: ClubSide option = None
            let wonAt = [| -1000000; -1000000 |]

            SimulatorRunner.runContextObserved ctx seed (fun world events ->
                for e in events do
                    match e.Data with
                    | EventData.Possession(side, _) when world.State.Flow = MatchFlow.Live && holder <> Some side ->
                        if holder.IsSome then
                            let i = if side = ClubSide.Home then 0 else 1
                            let depth = float world.State.Ball.Position.X * MatchMath.goalDirection side
                            let band = if depth < -17.5 then 0 elif depth < 17.5 then 1 else 2
                            local[i, band] <- local[i, band] + 1.0
                            wonAt[i] <- int e.Tick

                        holder <- Some side
                    | EventData.Possession(side, _) -> holder <- Some side
                    | EventData.Shot(p, _, _, _, _, _) ->
                        let i = if MatchWorld.teamOfPlayer ctx p = Some ClubSide.Home then 0 else 1
                        local[i, 3] <- local[i, 3] + 1.0
                        if int e.Tick - wonAt[i] < 10 * int SimulationClock.minuteSubticks / 60 then local[i, 4] <- local[i, 4] + 1.0
                    | _ -> ())
            |> ignore

            lock lockObj (fun () ->
                for i in 0..1 do
                    for j in 0..4 do
                        acc[i, j] <- acc[i, j] + local[i, j]))

        let n = float fixtures.Length
        for i, label in [ 0, "home (+)"; 1, "away (-)" ] do
            printfn "%s %s: wins ball own/mid/final %.1f/%.1f/%.1f, shots %.1f, of which within 10 s of a win %.1f" name label (acc[i, 0] / n) (acc[i, 1] / n) (acc[i, 2] / n) (acc[i, 3] / n) (acc[i, 4] / n)
