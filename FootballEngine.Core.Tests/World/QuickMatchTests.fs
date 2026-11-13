namespace FootballEngine.Core.Tests.World

open Expecto
open FootballEngine.Core.Match
open FootballEngine.World.Phases
open FootballEngine.Core.Tests.Infrastructure

module QuickMatchTests =

    let private ctx = TestBuilders.formationCtx ()

    let private raise (delta: float32) (lineup: Lineup) =
        let s v = min 100.0f (v + delta)

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

    let private matches (ctx: MatchContext) n =
        [| for k in 1 .. n -> QuickMatch.simulate ctx (uint64 k * 0x9E3779B97F4A7C15UL) |]

    let tests =
        testList
            "QuickMatch"
            [ testCase "the same fixture and seed play the same match" (fun _ ->
                  Expect.equal (QuickMatch.simulate ctx 42UL) (QuickMatch.simulate ctx 42UL) "a background result must be reproducible from its seed")

              testCase "every goal has one scorer of the scoring side, never assisted by himself" (fun _ ->
                  for played in matches ctx 400 do
                      let ids (stats: MatchPlayerStats[]) = stats |> Array.map _.PlayerId |> Set.ofArray
                      Expect.equal (played.HomeStats |> Array.sumBy _.GoalsScored) played.HomeScore "home scorers add up to the home score"
                      Expect.equal (played.AwayStats |> Array.sumBy _.GoalsScored) played.AwayScore "away scorers add up to the away score"

                      for event in played.Events do
                          match event.Data with
                          | EventData.Goal(side, Some scorer, assist, _) ->
                              let own = if side = ClubSide.Home then played.HomeStats else played.AwayStats
                              Expect.isTrue ((ids own).Contains(MatchContextBuilder.unwrapPid scorer)) "the scorer played for the scoring side"
                              Expect.notEqual assist (Some scorer) "nobody assists his own goal"
                          | EventData.Goal(_, None, _, _) -> failtest "a quick goal always has a scorer"
                          | _ -> ())

              testCase "nobody plays more than the match, and substitutes only replace starters" (fun _ ->
                  for played in matches ctx 400 do
                      for stats in Array.append played.HomeStats played.AwayStats do
                          Expect.isTrue (stats.MinutesPlayed > 0 && stats.MinutesPlayed <= 90) "minutes within one match"

                      let subs = played.Events |> List.filter (fun e -> match e.Data with EventData.Substitution _ -> true | _ -> false) |> List.length
                      let starters = ctx.HomeLineup.StarterCount + ctx.AwayLineup.StarterCount
                      Expect.equal (played.HomeStats.Length + played.AwayStats.Length) (starters + subs) "each substitution adds one player")

              testCase "mean goals match the model's rates" (fun _ ->
                  let n = 6000
                  let homeRate, awayRate = QuickMatch.rates ctx.Config.QuickMatch ctx
                  let played = matches ctx n
                  let tolerance rate = 4.0 * sqrt (rate / float n)
                  let home = played |> Array.averageBy (fun p -> float p.HomeScore)
                  let away = played |> Array.averageBy (fun p -> float p.AwayScore)
                  Expect.isLessThan (abs (home - homeRate)) (tolerance homeRate) $"home mean {home} vs rate {homeRate}"
                  Expect.isLessThan (abs (away - awayRate)) (tolerance awayRate) $"away mean {away} vs rate {awayRate}")

              testCase "a stronger eleven outscores the same eleven unchanged" (fun _ ->
                  let stronger = { ctx with HomeLineup = raise 12.0f ctx.HomeLineup }
                  let goals (c: MatchContext) = matches c 3000 |> Array.averageBy (fun p -> float (p.HomeScore - p.AwayScore))
                  Expect.isGreaterThan (goals stronger) (goals ctx + 0.3) "twelve attribute points must show in the margin")

              testCase "a forward scores more often than a centre back" (fun _ ->
                  let byRole = System.Collections.Generic.Dictionary<SlotRole, int>()
                  let roleOf id = ctx.HomeLineup.Profiles |> Array.find (fun p -> MatchContextBuilder.unwrapPid p.Id = id) |> _.Role

                  for played in matches ctx 2000 do
                      for stats in played.HomeStats do
                          if stats.GoalsScored > 0 && stats.PlayerId <= 11 then
                              let role = roleOf stats.PlayerId
                              byRole[role] <- (match byRole.TryGetValue role with | true, v -> v | _ -> 0) + stats.GoalsScored

                  let count role = match byRole.TryGetValue role with | true, v -> v | _ -> 0
                  Expect.isGreaterThan (count SlotRole.Striker) (2 * count SlotRole.Defender) "role weights shape who scores") ]
