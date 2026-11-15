namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Phases
open FootballEngine.World.Pulse
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.World

module SeasonCeremonyTests =

    let private config = WorldPulseConfig.defaultWorldPulse

    // The generated league has 6 matches per club, so the production threshold of 15 would leave every award empty.
    let private lenient = { config with AwardMinAppearances = 3 }

    let private balance = BalanceConfig.defaultConfig

    /// One simulated season shared by every case that needs real fixtures.
    let private finished =
        lazy
            (let gs = WorldGenerationTests.generate 0xC02UL []

             match WorldRunner.simulateFullSeasonAndAdvanceWithBalance balance (WorldClockOps.init gs.Season) gs with
             | Ok season -> season.SeasonFinalGs
             | Error error -> failtestf "season did not simulate: %A" error)

    let private leagueId (gs: GameState) =
        gs.Competitions
        |> Map.toSeq
        |> Seq.find (fun (_, c) -> MatchFacts.isLeague c)
        |> fst

    let private rolled (gs: GameState) =
        { SeasonPhase.runEndOfSeason balance gs with
            PulseProcessed = Set.empty }

    let private tinyDaily =
        { lenient with
            DailyBudgetUserClub = 0
            DailyBudgetUserLeague = 0
            DailyBudgetWorld = 0 }

    let private awardsIn (gs: GameState) =
        gs.News
        |> List.choose (fun n ->
            match n.Kind with
            | NewsKind.Award(kind, player, staff, cid) -> Some(kind, player, staff, cid)
            | _ -> None)

    // ---- synthetic seasons ---------------------------------------------------------------------------------------

    let private event data = { Tick = 0<subtick>; Data = data }

    let private goal side scorer =
        event (EventData.Goal(side, Some(FootballEngine.Core.Match.PlayerId scorer), None, 0.0))

    let private touch side player =
        event (EventData.Possession(side, FootballEngine.Core.Match.PlayerId player))

    /// Replaces the league's fixtures with hand-made played ones; each entry is (home, away, events).
    let private withFixtures (fixtures: (ClubId * ClubId * MatchEvent list) list) (gs: GameState) =
        let cid = leagueId gs

        let made =
            fixtures
            |> List.mapi (fun i (home, away, events) ->
                let id = 800_000 + i

                id,
                { Id = id
                  CompetitionId = cid
                  Round = None
                  HomeClubId = home
                  AwayClubId = away
                  ScheduledDate = gs.CurrentDate.AddDays(float (-100 + i))
                  Played = true
                  HomeScore = Some 0
                  AwayScore = Some 0
                  Events = events })
            |> Map.ofList

        { gs with
            Competitions =
                gs.Competitions
                |> Map.add
                    cid
                    { gs.Competitions[cid] with
                        Fixtures = made } }

    let private squad club (gs: GameState) =
        GameState.getSquad club gs |> List.sortBy _.Id

    let private clubsOf (gs: GameState) =
        gs.Competitions[leagueId gs].ClubIds |> List.sort

    let private setPlayer (id: int) (f: Player -> Player) (gs: GameState) =
        { gs with
            Players = gs.Players |> Map.add id (f gs.Players[id]) }

    let private bornYearsAgo (years: int) (gs: GameState) (p: Player) =
        { p with
            Birthday = gs.CurrentDate.AddYears(-years).AddDays(-10.0) }

    let private winnerOf kind (config: WorldPulseConfig) (gs: GameState) =
        SeasonCeremony.awards config gs |> List.tryFind (fun w -> w.Kind = kind)

    // ---- cases ---------------------------------------------------------------------------------------------------

    let private championTests =
        testList
            "champions and movements"
            [ testCase "each played league yields exactly one champion item: its table leader"
              <| fun _ ->
                  let gs = finished.Value
                  let next = SeasonCeremony.apply lenient gs (rolled gs)

                  for KeyValue(cid, comp) in gs.Competitions do
                      if MatchFacts.isLeague comp then
                          let leader = Competition.rankedStandings comp |> List.head |> fst

                          let champions =
                              next.News
                              |> List.choose (fun n ->
                                  match n.Kind with
                                  | NewsKind.Champion(club, c) when c = cid -> Some club
                                  | _ -> None)

                          Expect.equal champions [ leader ] "one champion, the leader"

              testCase "promotion and relegation items are exactly the clubs the season record moved"
              <| fun _ ->
                  let gs = finished.Value
                  let cid = leagueId gs

                  let withRules =
                      match gs.Competitions[cid].Type with
                      | NationalLeague(level, rules) ->
                          { gs with
                              Competitions =
                                  gs.Competitions
                                  |> Map.add
                                      cid
                                      { gs.Competitions[cid] with
                                          Type =
                                              NationalLeague(
                                                  level,
                                                  { rules with
                                                      Promotion = [ AutomaticPromotion 1 ]
                                                      Relegation = [ AutomaticRelegation 1 ] }
                                              ) } }
                      | _ -> gs

                  let after = rolled withRules
                  let next = SeasonCeremony.apply lenient withRules after
                  let record = after.SeasonHistory.Head
                  Expect.isNonEmpty record.Promoted "scenario promotes someone"
                  Expect.isNonEmpty record.Relegated "scenario relegates someone"

                  let clubsOfKind pick =
                      next.News |> List.choose pick |> List.sort

                  Expect.equal
                      (clubsOfKind (fun n ->
                          match n.Kind with
                          | NewsKind.Promotion(club, _) -> Some club
                          | _ -> None))
                      (List.sort record.Promoted)
                      "promotions"

                  Expect.equal
                      (clubsOfKind (fun n ->
                          match n.Kind with
                          | NewsKind.Relegation(club, _) -> Some club
                          | _ -> None))
                      (List.sort record.Relegated)
                      "relegations" ]

    let private awardTests =
        testList
            "awards"
            [ testCase "top scorer is the league's goal leader; ties go to fewer appearances"
              <| fun _ ->
                  let gs = finished.Value
                  let cid = leagueId gs

                  let goals =
                      gs.Competitions[cid].Fixtures.Values
                      |> Seq.filter _.Played
                      |> Seq.collect MatchFacts.scorers
                      |> Seq.countBy fst
                      |> Seq.toList

                  let most = goals |> List.map snd |> List.max

                  let winner = (winnerOf AwardKind.TopScorer lenient gs).Value

                  Expect.contains
                      (goals |> List.filter (fun (_, n) -> n = most) |> List.map fst)
                      winner.Player.Value
                      "winner has the maximum goal count"

                  let a, b, c =
                      match clubsOf gs |> List.truncate 3 with
                      | [ a; b; c ] -> a, b, c
                      | clubs -> failtestf "the fixture world has at least three clubs, got %d" clubs.Length

                  let pa = (squad a gs).Head.Id
                  let pb = (squad b gs).Head.Id
                  let pc = (squad c gs).Head.Id
                  let home = ClubSide.Home

                  let tied =
                      gs
                      |> withFixtures
                          [ a, b, [ goal home pa; goal home pa ]
                            // pb scores the same two goals but also plays a match without scoring.
                            b, a, [ goal home pb; goal home pb ]
                            b, c, [ touch home pb ]
                            c, a, [ touch home pc ] ]

                  Expect.equal
                      (winnerOf AwardKind.TopScorer lenient tied).Value.Player
                      (Some pa)
                      "equal goals: fewer appearances wins"

                  let leader =
                      gs
                      |> withFixtures [ a, b, [ goal home pa ]; b, c, [ goal home pb; goal home pb; goal home pb ] ]

                  Expect.equal
                      (winnerOf AwardKind.TopScorer lenient leader).Value.Player
                      (Some pb)
                      "more goals beat fewer appearances"

              testCase "player of the season needs the minimum appearances"
              <| fun _ ->
                  let gs = finished.Value

                  let a, b =
                      match clubsOf gs |> List.truncate 2 with
                      | [ a; b ] -> a, b
                      | clubs -> failtestf "the fixture world has at least two clubs, got %d" clubs.Length

                  let star = (squad a gs).Head.Id
                  let regular = (squad b gs).Head.Id
                  let home = ClubSide.Home
                  let min = 4

                  let fixtures =
                      [ // the star scores a lot but plays twice
                        a, b, [ goal home star; goal home star; goal home star; goal home star ]
                        b, a, [ goal home star; goal home star; goal home star; touch home regular ]
                        yield! List.replicate (min - 1) (b, a, [ touch home regular ]) ]

                  let strict =
                      { lenient with
                          AwardMinAppearances = min }

                  let crafted = gs |> withFixtures fixtures

                  // Neutralise the other terms so appearances alone decide eligibility.
                  let crafted =
                      crafted
                      |> setPlayer star (fun p -> { p with CurrentSkill = 200 })
                      |> setPlayer regular (fun p -> { p with CurrentSkill = 1 })

                  let winner = (winnerOf AwardKind.PlayerOfSeason strict crafted).Value

                  Expect.notEqual winner.Player (Some star) "two appearances are below the threshold"

                  let allowed = { strict with AwardMinAppearances = 2 }

                  Expect.equal
                      (winnerOf AwardKind.PlayerOfSeason allowed crafted).Value.Player
                      (Some star)
                      "the same star wins once the threshold admits him"

              testCase "young player never exceeds the age cap and ignores older stars"
              <| fun _ ->
                  let gs = finished.Value

                  let a, b =
                      match clubsOf gs |> List.truncate 2 with
                      | [ a; b ] -> a, b
                      | clubs -> failtestf "the fixture world has at least two clubs, got %d" clubs.Length

                  let old = (squad a gs).Head.Id
                  let youth = (squad b gs).Head.Id
                  let home = ClubSide.Home

                  let crafted =
                      gs
                      |> withFixtures
                          [ yield! List.replicate 4 (a, b, [ goal home old; touch home youth ])
                            yield! List.replicate 4 (b, a, [ touch home youth ]) ]
                      |> setPlayer old (fun p ->
                          { bornYearsAgo 30 gs p with
                              CurrentSkill = 190 })
                      |> setPlayer youth (fun p ->
                          { bornYearsAgo 20 gs p with
                              CurrentSkill = 40 })

                  let young = (winnerOf AwardKind.YoungPlayer lenient crafted).Value
                  Expect.equal young.Player (Some youth) "the only eligible youngster"

                  Expect.equal
                      (winnerOf AwardKind.PlayerOfSeason lenient crafted).Value.Player
                      (Some old)
                      "the star wins overall"

                  for w in SeasonCeremony.awards lenient gs do
                      if w.Kind = AwardKind.YoungPlayer then
                          Expect.isLessThanOrEqual
                              (Player.age gs.CurrentDate gs.Players[w.Player.Value])
                              lenient.YoungPlayerMaxAge
                              "age cap"

              testCase "manager of the season is the coach who beat the reputation order the most"
              <| fun _ ->
                  let gs = finished.Value
                  let cid = leagueId gs
                  let clubs = clubsOf gs
                  let reputations = [ 9000; 8000; 7000; 6000 ]

                  let withRep =
                      (gs, List.zip clubs reputations)
                      ||> List.fold (fun state (club, rep) ->
                          { state with
                              Clubs =
                                  state.Clubs
                                  |> Map.add
                                      club
                                      { state.Clubs[club] with
                                          Reputation = rep } })

                  // Finishing order is the reverse of the reputation order: the weakest club wins the league.
                  let points = [ 1; 2; 3; 4 ]

                  let standings =
                      List.zip clubs points
                      |> List.map (fun (club, pts) ->
                          club,
                          { ClubId = club
                            Played = 6
                            Won = pts
                            Drawn = 0
                            Lost = 6 - pts
                            GoalsFor = pts
                            GoalsAgainst = 0
                            Points = pts * 3 })
                      |> Map.ofList

                  let crafted =
                      { withRep with
                          Competitions =
                              withRep.Competitions
                              |> Map.add
                                  cid
                                  { withRep.Competitions[cid] with
                                      Standings = standings } }

                  let weakest = List.last clubs
                  let winner = (winnerOf AwardKind.ManagerOfSeason lenient crafted).Value
                  Expect.equal winner.Club weakest "largest expected-minus-actual rank"
                  Expect.equal winner.Staff (GameState.headCoach weakest gs |> Option.map _.Id) "his head coach"

              testCase "at most one award per kind per league and the same season gives the same awards"
              <| fun _ ->
                  let gs = finished.Value
                  let first = SeasonCeremony.awards lenient gs

                  Expect.equal
                      first.Length
                      (first |> List.distinctBy (fun w -> w.Kind, w.Competition) |> List.length)
                      "one per kind"

                  Expect.isTrue
                      (first |> List.exists (fun w -> w.Kind = AwardKind.TopScorer))
                      "a played league has a top scorer"

                  Expect.equal (SeasonCeremony.awards lenient gs) first "awards are deterministic"

                  let a = SeasonCeremony.apply lenient gs (rolled gs)
                  let b = SeasonCeremony.apply lenient gs (rolled gs)
                  Expect.equal a.News b.News "the published ceremony is deterministic" ]

    let private budgetTests =
        testList
            "budget and inbox"
            [ testCase "the whole ceremony survives a daily cap of zero"
              <| fun _ ->
                  let gs = finished.Value

                  let expectedChampions =
                      gs.Competitions.Values |> Seq.filter MatchFacts.isLeague |> Seq.length

                  let expectedAwards = (SeasonCeremony.awards tinyDaily gs).Length
                  let next = SeasonCeremony.apply tinyDaily gs (rolled gs)

                  let champions =
                      next.News
                      |> List.filter (fun n ->
                          match n.Kind with
                          | NewsKind.Champion _ -> true
                          | _ -> false)

                  Expect.equal champions.Length expectedChampions "every champion"
                  Expect.isGreaterThan expectedAwards 0 "scenario has awards"
                  Expect.equal (awardsIn next).Length expectedAwards "every award"

              testCase "retirements are not cut by the daily cap"
              <| fun _ ->
                  let gs = WorldGenerationTests.generate 0xC02UL []

                  let nextRolled =
                      { SeasonPhase.runEndOfSeason balance gs with
                          PulseProcessed = Set.empty }

                  let after = PlayerStories.seasonRoll tinyDaily gs nextRolled

                  let retiredNow =
                      after.Players.Values
                      |> Seq.filter (fun p -> p.Affiliation = Retired && gs.Players[p.Id].Affiliation <> Retired)
                      |> Seq.length

                  let announced =
                      after.News
                      |> List.filter (fun n ->
                          match n.Kind with
                          | NewsKind.Retirement _ -> true
                          | _ -> false)
                      |> List.length

                  Expect.isGreaterThan retiredNow 0 "scenario retires someone"
                  Expect.equal announced retiredNow "each retirement reaches the feed despite a zero daily budget"

              testCase "only the user's own club reaches the inbox"
              <| fun _ ->
                  let gs = finished.Value
                  let user = gs.UserClubId
                  let other = gs.Clubs.Keys |> Seq.find (fun c -> c <> user)
                  let cid = leagueId gs

                  let record: SeasonRecord =
                      { Season = gs.Season
                        CompetitionIds = [ cid ]
                        Winners = [ cid, other ]
                        Promoted = [ other ]
                        Relegated = [ other ]
                        CompletedOn = gs.CurrentDate }

                  Expect.isEmpty
                      (WorldNews.deriveSeasonMessages gs record)
                      "other clubs' season items are news, not mail"

                  let own =
                      { record with
                          Winners = [ cid, user ]
                          Promoted = [ user ]
                          Relegated = [ user ] }

                  Expect.equal
                      (WorldNews.deriveSeasonMessages gs own).Length
                      3
                      "title, promotion and relegation of the user's club"

                  let mine = (squad user gs).Head.Id
                  let theirs = (squad other gs).Head.Id


                  let inboxAwards (state: GameState) =
                      state.Inbox |> List.filter (fun m -> m.Subject.StartsWith "Top scorer")

                  let userScorer = (gs |> withFixtures [ user, other, [ goal ClubSide.Home mine ] ])

                  Expect.equal
                      (inboxAwards (SeasonCeremony.apply lenient userScorer (rolled userScorer))).Length
                      1
                      "an award to the user's player is mailed"

                  let rivalScorer =
                      (gs |> withFixtures [ user, other, [ goal ClubSide.Away theirs ] ])

                  Expect.isEmpty
                      (inboxAwards (SeasonCeremony.apply lenient rivalScorer (rolled rivalScorer)))
                      "a rival's award is not" ]

    let tests = testList "SeasonCeremony" [ championTests; awardTests; budgetTests ]
