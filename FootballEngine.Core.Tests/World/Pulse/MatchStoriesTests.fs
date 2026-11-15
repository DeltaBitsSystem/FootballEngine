namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Pulse
open FootballEngine.Core.Tests.Infrastructure

module MatchStoriesTests =

    let private day = DateTime(2030, 5, 10)
    let private config = WorldPulseConfig.defaultWorldPulse
    let private clubIds = [ 1; 2; 3; 4; 5; 6 ]

    // Reputation descends with the id so "top two by reputation" is clubs 1 and 2 and the user (6) is the weakest.
    let private club id : Club =
        { Id = id
          Name = $"Club {id}"
          Nationality = "TST"
          Reputation = 10000 - id * 1500
          PlayerIds = []
          StaffIds = []
          Finance = Finance.initial 1_000_000m
          Morale = 50
          BoardObjective = LeagueObjective MidTable
          CoordinationMemory = CoordinationMemory.defaultMemory
          ScoutingMemory = Map.empty
          Kit = None
          BoardConfidence = 60
          FansMood = 60
          RecentResults = []
          ManagerSince = None }

    let private goal side scorer : FootballEngine.Core.Match.MatchEvent =
        { Tick = 0<FootballEngine.Core.Match.subtick>
          Data =
            FootballEngine.Core.Match.EventData.Goal(side, Some(FootballEngine.Core.Match.PlayerId scorer), None, 0.1) }

    let private home = FootballEngine.Core.Match.ClubSide.Home
    let private away = FootballEngine.Core.Match.ClubSide.Away

    let private fixture id homeClub awayClub (date: DateTime) score events : MatchFixture =
        { Id = id
          CompetitionId = 1
          Round = None
          HomeClubId = homeClub
          AwayClubId = awayClub
          ScheduledDate = date
          Played = Option.isSome score
          HomeScore = score |> Option.map fst
          AwayScore = score |> Option.map snd
          Events = events }

    let private standing id played won drawn lost gf ga : LeagueStanding =
        { ClubId = id
          Played = played
          Won = won
          Drawn = drawn
          Lost = lost
          GoalsFor = gf
          GoalsAgainst = ga
          Points = won * 3 + drawn }

    let private competition fixtures standings : Competition =
        { Id = 1
          Name = "League"
          BroadcastIdentity = BroadcastIdentity.generated "competition:stories"
          Type =
            NationalLeague(
                LeagueLevel 0,
                { PointsForWin = 3
                  PointsForDraw = 1
                  Tiebreakers = [ GoalDifference ]
                  Promotion = []
                  Relegation = [] }
            )
          Country = Some "TST"
          Season = 2030
          ClubIds = clubIds
          Fixtures = fixtures |> List.map (fun (f: MatchFixture) -> f.Id, f) |> Map.ofList
          Standings = standings |> List.map (fun (s: LeagueStanding) -> s.ClubId, s) |> Map.ofList
          KnockoutTies = Map.empty }

    let private worldWith fixtures standings : GameState =
        { CurrentDate = day
          Season = 2030
          TrainingWeeksApplied = 0
          Clubs = clubIds |> List.map (fun id -> id, club id) |> Map.ofList
          Players = Map.empty
          Staff = Map.empty
          Competitions = Map.ofList [ 1, competition fixtures standings ]
          Countries = Map.empty
          DataLock = TestDataLock.valid
          UserClubId = 6
          UserStaffId = 0
          PrimaryCountry = "TST"
          Inbox = []
          NextInboxId = 1
          PendingNegotiations = Map.empty
          NextNegotiationId = 1
          Market = MarketState.empty
          ProfileCache = Map.empty
          PlayerExperiences = Map.empty
          TeamEmphasis = EmPossession
          SeasonHistory = []
          CareerHistory = []
          ExecutionSeed = 7UL
          MatchSeeds = Map.empty
          News = []
          NextNewsId = 1
          PulseProcessed = Set.empty
          PulseQueues = PulseQueues.empty
          JobOffers = []
          NextJobOfferId = 1
          UserStudy = [] }

    let private withResults id results (gs: GameState) =
        { gs with
            Clubs =
                gs.Clubs
                |> Map.add
                    id
                    { gs.Clubs[id] with
                        RecentResults = results } }

    let private fixturesOf (gs: GameState) = gs.Competitions[1].Fixtures

    // Pressure news (mood, unrest) rides the same run; these tests pin the story detectors only.
    let private kinds (gs: GameState) =
        gs.News
        |> List.map _.Kind
        |> List.filter (function
            | NewsKind.FansMood _
            | NewsKind.BoardUnrest _ -> false
            | _ -> true)

    let private pulse (gs: GameState) = WorldPulse.run config day gs

    let private playedDay id h a score events = fixture id h a day (Some score) events

    // Clubs 3 and 4 are mid-reputation and not in the top three of an empty table, so scores alone drive the story.
    let private match34 score =
        worldWith [ playedDay 1 3 4 score [] ] []

    let private ledgerTests =
        testList
            "Results ledger"
            [ testCase "a played fixture prepends one result to each club and a quiet day changes nothing"
              <| fun _ ->
                  let gs = pulse (match34 (2, 0))
                  Expect.equal gs.Clubs[3].RecentResults [ MatchOutcomeTag.Won ] "winner"
                  Expect.equal gs.Clubs[4].RecentResults [ MatchOutcomeTag.Lost ] "loser"
                  Expect.equal gs.Clubs[1].RecentResults [] "bystander"

                  let quiet = worldWith [ fixture 1 3 4 (day.AddDays 1.0) (Some(2, 0)) [] ] []
                  Expect.equal (pulse quiet).Clubs quiet.Clubs "fixtures of other days are not counted"

              testCase "draws are recorded and history is truncated to the configured length"
              <| fun _ ->
                  let gs =
                      match34 (1, 1)
                      |> withResults 3 [ MatchOutcomeTag.Won; MatchOutcomeTag.Lost; MatchOutcomeTag.Won ]

                  let result = WorldPulse.run { config with RecentResultsKept = 3 } day gs

                  Expect.equal
                      result.Clubs[3].RecentResults
                      [ MatchOutcomeTag.Drawn; MatchOutcomeTag.Won; MatchOutcomeTag.Lost ]
                      "newest first, oldest dropped"

                  Expect.equal result.Clubs[4].RecentResults [ MatchOutcomeTag.Drawn ] "both sides draw" ]

    let private scorelineTests =
        testList
            "Scoreline stories"
            [ testCase "a win by the configured margin is a big win and a smaller one is not"
              <| fun _ ->
                  Expect.equal (kinds (pulse (match34 (3, 0)))) [ NewsKind.BigWin(3, 4, 3, 0) ] "margin 3"
                  Expect.isEmpty (kinds (pulse (match34 (2, 0)))) "margin 2"
                  Expect.isEmpty (kinds (pulse (match34 (3, 3)))) "no draws"

              testCase "the weaker club winning across the reputation gap is an upset that replaces the big win"
              <| fun _ ->
                  // Club 5 (2500) beats club 3 (5500): gap 3000 >= 1500, margin 3.
                  let gs = worldWith [ playedDay 1 3 5 (0, 3) [] ] []
                  Expect.equal (kinds (pulse gs)) [ NewsKind.Upset(5, 3, 3, 0) ] "only the upset"

              testCase "no upset when the favourite wins or the gap is too small"
              <| fun _ ->
                  let favourite = worldWith [ playedDay 1 3 5 (1, 0) [] ] []
                  Expect.isEmpty (kinds (pulse favourite)) "favourite wins narrowly"
                  // Club 4 (4000) beats club 3 (5500): gap 1500 exactly counts; club 3 beating club 4 does not.
                  Expect.equal
                      (kinds (pulse (worldWith [ playedDay 1 3 4 (0, 1) [] ] [])))
                      [ NewsKind.Upset(4, 3, 1, 0) ]
                      "gap at the threshold"

                  Expect.isEmpty (kinds (pulse (worldWith [ playedDay 1 4 3 (0, 1) [] ] []))) "favourite away win" ]

    let private streakTests =
        let streakKinds gs =
            kinds (pulse gs)
            |> List.choose (function
                | NewsKind.Streak(c, k, n) -> Some(c, k, n)
                | _ -> None)

        let W = MatchOutcomeTag.Won
        let D = MatchOutcomeTag.Drawn
        let L = MatchOutcomeTag.Lost

        testList
            "Streak stories"
            [ testCase "a win streak fires exactly at the threshold"
              <| fun _ ->
                  let at n =
                      match34 (1, 0) |> withResults 3 (List.replicate (n - 1) W) |> streakKinds

                  Expect.isEmpty (at 2) "two wins"
                  Expect.contains (at 3) (3, StreakKind.Wins, 3) "three wins"
                  Expect.isEmpty (at 4) "four wins"
                  Expect.contains (at 5) (3, StreakKind.Wins, 5) "five wins"

              testCase "losses fire at three and unbeaten is not duplicated beside the same wins"
              <| fun _ ->
                  let losing = match34 (1, 0) |> withResults 4 [ L; L ] |> streakKinds
                  Expect.contains losing (4, StreakKind.Losses, 3) "third loss in a row"

                  Expect.isFalse
                      (losing |> List.exists (fun (_, k, _) -> k = StreakKind.Winless))
                      "winless equals losses"

                  let winning = match34 (1, 0) |> withResults 3 [ W; W ] |> streakKinds

                  Expect.isFalse
                      (winning |> List.exists (fun (c, k, _) -> c = 3 && k = StreakKind.Unbeaten))
                      "unbeaten equals wins"

              testCase "unbeaten and winless fire when they differ from the wins and losses runs"
              <| fun _ ->
                  let unbeaten = match34 (1, 1) |> withResults 3 [ W; D ] |> streakKinds
                  Expect.contains unbeaten (3, StreakKind.Unbeaten, 3) "draw extends an unbeaten run"

                  Expect.isFalse
                      (unbeaten |> List.exists (fun (c, k, _) -> c = 3 && k = StreakKind.Wins))
                      "draw breaks wins"

                  let winless = match34 (1, 1) |> withResults 4 [ L; D ] |> streakKinds
                  Expect.contains winless (4, StreakKind.Winless, 3) "winless run"

              testCase "clubs that did not play are never reported"
              <| fun _ ->
                  let gs = match34 (1, 0) |> withResults 1 [ W; W; W ]
                  Expect.isFalse (streakKinds gs |> List.exists (fun (c, _, _) -> c = 1)) "idle club" ]

    let private leaderTests =
        // After the day's table is fixed up front: club 2 leads with `played` matches.
        let table played =
            [ standing 2 played played 0 0 played 0
              standing 1 played (played - 1) 1 0 played 1
              standing 3 played 0 0 played 0 played
              standing 4 played 0 0 played 0 played
              standing 5 played 0 0 played 0 played
              standing 6 played 0 0 played 0 played ]

        let leaders gs =
            kinds (pulse gs)
            |> List.choose (function
                | NewsKind.NewLeader(c, _) -> Some c
                | _ -> None)

        testList
            "New leader"
            [ testCase "a leader is announced after the minimum round and not before"
              <| fun _ ->
                  let fx = [ playedDay 1 3 4 (0, 0) [] ]
                  Expect.equal (leaders (worldWith fx (table 3))) [ 2 ] "round 3"
                  Expect.isEmpty (leaders (worldWith fx (table 2))) "round 2 is too early"

              testCase "the same leader is not announced twice but a change is"
              <| fun _ ->
                  let nextDay = day.AddDays 1.0
                  let first = pulse (worldWith [ playedDay 1 3 4 (0, 0) [] ] (table 3))

                  let replay (standings: LeagueStanding list) =
                      let competition =
                          { first.Competitions[1] with
                              Fixtures = Map.ofList [ 2, fixture 2 3 4 nextDay (Some(0, 0)) [] ]
                              Standings = standings |> List.map (fun s -> s.ClubId, s) |> Map.ofList }

                      WorldPulse.run
                          config
                          nextDay
                          { first with
                              Competitions = Map.ofList [ 1, competition ] }

                  Expect.equal (leaders first) [ 2 ] "first announcement"
                  Expect.equal (replay (table 3)).News.Length first.News.Length "same leader stays quiet"

                  let changed = replay [ standing 1 4 4 0 0 9 0; standing 2 4 3 0 1 5 1 ]

                  Expect.equal
                      (changed.News
                       |> List.choose (fun n ->
                           match n.Kind with
                           | NewsKind.NewLeader(c, _) -> Some c
                           | _ -> None))
                      [ 1; 2 ]
                      "club 1 takes over, newest first" ]

    let private playerTests =
        let scorerKinds gs = kinds (pulse gs)

        testList
            "Player stories"
            [ testCase "three goals by one scorer is a hat-trick for his club"
              <| fun _ ->
                  let events = [ goal away 77; goal away 77; goal home 11; goal away 77 ]
                  let gs = worldWith [ playedDay 1 3 4 (1, 3) events ] []
                  Expect.contains (scorerKinds gs) (NewsKind.HatTrick(77, 4, 3)) "hat-trick for the away side"

              testCase "two goals each by different players is not a hat-trick"
              <| fun _ ->
                  let events = [ goal away 77; goal away 77; goal away 78; goal away 78 ]
                  let gs = worldWith [ playedDay 1 3 4 (0, 4) events ] []

                  Expect.isFalse
                      (scorerKinds gs
                       |> List.exists (function
                           | NewsKind.HatTrick _ -> true
                           | _ -> false))
                      "no hat-trick"

              testCase "scoring in three consecutive club matches is a run and a blank match breaks it"
              <| fun _ ->
                  let prior n date events = fixture n 3 4 date (Some(1, 0)) events

                  let scored n d =
                      prior n (day.AddDays d) [ goal home 55 ]

                  let run =
                      worldWith
                          [ scored 1 -14.0
                            scored 2 -7.0
                            fixture 3 4 3 day (Some(0, 1)) [ goal away 55 ] ]
                          []

                  Expect.contains (scorerKinds run) (NewsKind.ScoringRun(55, 3)) "three in a row"

                  let broken =
                      worldWith
                          [ scored 1 -14.0
                            prior 2 (day.AddDays -7.0) []
                            fixture 3 4 3 day (Some(0, 1)) [ goal away 55 ] ]
                          []

                  Expect.isFalse
                      (scorerKinds broken
                       |> List.exists (function
                           | NewsKind.ScoringRun _ -> true
                           | _ -> false))
                      "blank match in between"

                  let twoOnly =
                      worldWith [ scored 2 -7.0; fixture 3 4 3 day (Some(0, 1)) [ goal away 55 ] ] []

                  Expect.isFalse
                      (scorerKinds twoOnly
                       |> List.exists (function
                           | NewsKind.ScoringRun _ -> true
                           | _ -> false))
                      "only two" ]

    let private clasicoTests =
        let previews gs =
            kinds (WorldPulse.run config day gs)
            |> List.choose (function
                | NewsKind.Clasico(h, a) -> Some(h, a)
                | _ -> None)

        let ahead d = day.AddDays(float d)

        testList
            "Clasico preview"
            [ testCase "the two highest reputations meeting in two days are previewed"
              <| fun _ ->
                  Expect.equal
                      (previews (worldWith [ fixture 1 1 2 (ahead 2) None []; fixture 2 3 4 (ahead 2) None [] ] []))
                      [ 1, 2 ]
                      "only the top pair"

              testCase "wrong lead time or an already played fixture is ignored"
              <| fun _ ->
                  Expect.isEmpty (previews (worldWith [ fixture 1 1 2 (ahead 1) None [] ] [])) "one day ahead"
                  Expect.isEmpty (previews (worldWith [ fixture 1 1 2 (ahead 3) None [] ] [])) "three days ahead"
                  Expect.isEmpty (previews (worldWith [ fixture 1 1 2 (ahead 2) (Some(1, 0)) [] ] [])) "already played"

              testCase "a level head-to-head history makes any pair a derby"
              <| fun _ ->
                  let meetings =
                      [ 1; 2; 3; 4 ]
                      |> List.map (fun i ->
                          fixture i 3 4 (day.AddDays(float -i * 10.0)) (Some(if i % 2 = 0 then (1, 0) else (0, 1))) [])

                  Expect.equal
                      (previews (worldWith (fixture 9 3 4 (ahead 2) None [] :: meetings) []))
                      [ 3, 4 ]
                      "four even meetings"

                  Expect.isEmpty
                      (previews (worldWith (fixture 9 3 4 (ahead 2) None [] :: List.take 3 meetings) []))
                      "too few meetings"

                  let lopsided =
                      [ 1; 2; 3; 4 ]
                      |> List.map (fun i -> fixture i 3 4 (day.AddDays(float -i * 10.0)) (Some(4, 0)) [])

                  Expect.isEmpty
                      (previews (worldWith (fixture 9 3 4 (ahead 2) None [] :: lopsided) []))
                      "one-sided history" ]

    let private absorptionTests =
        testList
            "Fixture absorption"
            [ testCase "a result committed after its day is absorbed once on the next run"
              <| fun _ ->
                  let nextDay = day.AddDays 1.0
                  let pending = fixture 1 3 5 day None []
                  let before = worldWith [ pending ] []
                  let afterPulse = pulse before
                  Expect.isEmpty afterPulse.News "nothing is played when the pulse runs"
                  Expect.equal afterPulse.Clubs[3].RecentResults [] "no ledger entry yet"

                  // The user-match commit flips the fixture after the day's pulse, exactly like WorldRunner does.
                  let committed =
                      { afterPulse with
                          Competitions =
                              afterPulse.Competitions
                              |> Map.map (fun _ c ->
                                  { c with
                                      Fixtures = Map.ofList [ 1, playedDay 1 3 5 (0, 3) [] ] }) }

                  let absorbed = WorldPulse.run config nextDay committed
                  Expect.equal absorbed.Clubs[3].RecentResults [ MatchOutcomeTag.Lost ] "one ledger entry"
                  Expect.equal (kinds absorbed) [ NewsKind.Upset(5, 3, 3, 0) ] "story published once"
                  Expect.equal absorbed.News.Head.Date nextDay "dated by the run that publishes it"
                  Expect.equal absorbed.PulseProcessed (Set.ofList [ 1 ]) "fixture marked"

                  let again = WorldPulse.run config (nextDay.AddDays 1.0) absorbed
                  Expect.equal again.Clubs absorbed.Clubs "running again never double-applies"
                  Expect.equal again.News absorbed.News "and publishes nothing new"

              testCase "processed ids that no longer match a played fixture are pruned"
              <| fun _ ->
                  let gs =
                      { worldWith [ fixture 2 3 4 (day.AddDays 5.0) None [] ] [] with
                          PulseProcessed = Set.ofList [ 1; 2 ] }

                  Expect.isEmpty
                      (pulse gs).PulseProcessed
                      "missing and unplayed fixtures drop out so ids can be reused by a new season" ]

    let private pipelineTests =
        testList
            "Match pulse pipeline"
            [ testCase "a day without played fixtures publishes nothing"
              <| fun _ ->
                  let gs =
                      worldWith [ fixture 1 3 4 (day.AddDays 1.0) None [] ] [ standing 3 5 5 0 0 9 0 ]

                  Expect.isEmpty (pulse gs).News "quiet day"

              testCase "running the same day twice from the same state is identical"
              <| fun _ ->
                  let gs =
                      worldWith [ playedDay 1 3 5 (0, 3) [ goal away 9; goal away 9; goal away 9 ] ] []

                  Expect.equal (pulse gs) (pulse gs) "deterministic"
                  Expect.isNonEmpty (pulse gs).News "and not vacuous"

              testCase "importance escalates for the user club and for hat-tricks"
              <| fun _ ->
                  let user = worldWith [ playedDay 1 6 4 (3, 0) [] ] []
                  Expect.equal (pulse user).News.Head.Importance NewsImportance.Headline "user club involved"
                  Expect.equal (pulse user).News.Head.Scope NewsScope.UserClub "scope"

                  let hat =
                      worldWith [ playedDay 1 3 4 (0, 3) [ goal away 9; goal away 9; goal away 9 ] ] []

                  let hatItem =
                      (pulse hat).News
                      |> List.find (fun n ->
                          match n.Kind with
                          | NewsKind.HatTrick _ -> true
                          | _ -> false)

                  Expect.equal hatItem.Importance NewsImportance.Major "hat-trick"

              testCase "a generated world run through the scheduler keeps ledger, news and budgets consistent"
              <| fun _ ->
                  let world = FootballEngine.Core.Tests.World.WorldGenerationTests.generate 0xC02UL []

                  let firstFixture =
                      world.Competitions.Values
                      |> Seq.collect (fun c -> c.Fixtures.Values)
                      |> Seq.map _.ScheduledDate.Date
                      |> Seq.min

                  let start =
                      { world with
                          CurrentDate = firstFixture.AddDays -1.0 }

                  let budget =
                      function
                      | NewsScope.UserClub -> config.DailyBudgetUserClub
                      | NewsScope.UserLeague -> config.DailyBudgetUserLeague
                      | NewsScope.World -> config.DailyBudgetWorld

                  let rec loop (state: GameState) clock remaining =
                      if remaining = 0 then
                          state
                      else
                          match
                              WorldScheduler.executeDayWithBalance (
                                  FootballEngine.Core.Match.BalanceConfig.defaultConfig,
                                  RunThroughMatches,
                                  clock,
                                  state
                              )
                          with
                          | Error failure -> failtest failure.Message
                          | Ok exec ->
                              let today = exec.State.CurrentDate.Date

                              let playedToday =
                                  exec.State.Competitions.Values
                                  |> Seq.collect (fun c -> c.Fixtures.Values)
                                  |> Seq.filter (fun f -> f.Played && f.ScheduledDate.Date = today)
                                  |> List.ofSeq

                              for club in exec.State.Clubs.Values do
                                  let before = state.Clubs[club.Id].RecentResults

                                  let plays =
                                      playedToday
                                      |> List.filter (fun f -> f.HomeClubId = club.Id || f.AwayClubId = club.Id)

                                  let expected = min config.RecentResultsKept (before.Length + plays.Length)

                                  Expect.equal
                                      club.RecentResults.Length
                                      expected
                                      $"club {club.Id} gains exactly one result per match"

                              exec.State.News
                              |> List.filter (fun n -> n.Date.Date = today)
                              |> List.groupBy _.Scope
                              |> List.iter (fun (scope, items) ->
                                  Expect.isLessThanOrEqual items.Length (budget scope) "daily budget")

                              loop exec.State exec.Clock (remaining - 1)

                  let final = loop start (WorldClockOps.init start.Season) 35
                  Expect.isNonEmpty final.News "five weeks of league football produce news" ]

    let tests =
        testList
            "Match stories"
            [ ledgerTests
              scorelineTests
              streakTests
              leaderTests
              playerTests
              clasicoTests
              absorptionTests
              pipelineTests ]
