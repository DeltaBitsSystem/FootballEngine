namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.World.Pulse
open FootballEngine.Core.Tests.Infrastructure

module WorldPulseTests =

    let private date = DateTime(2030, 5, 10)
    let private config = WorldPulseConfig.defaultWorldPulse

    let private league =
        { Id = 1
          Name = "League"
          BroadcastIdentity = BroadcastIdentity.generated "competition:pulse"
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
          ClubIds = [ 1; 2; 3 ]
          Fixtures = Map.empty
          Standings = Map.empty
          KnockoutTies = Map.empty }

    let private state: GameState =
        { CurrentDate = date
          Season = 2030
          TrainingWeeksApplied = 0
          Clubs = Map.empty
          Players = Map.empty
          Staff = Map.empty
          Competitions = Map.ofList [ league.Id, league ]
          Countries = Map.empty
          DataLock = TestDataLock.valid
          UserClubId = 1
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
          PulseQueues = PulseQueues.empty }

    let private streak club length : NewsCandidate =
        NewsKind.Streak(club, StreakKind.Wins, length), NewsImportance.Minor, NewsScope.World, [ club ], []

    let private world club importance : NewsCandidate =
        NewsKind.Streak(club, StreakKind.Wins, 3), importance, NewsScope.World, [ club ], []

    let private deskTests =
        testList
            "NewsDesk"
            [ testCase "budget per scope keeps the most important and breaks ties by order"
              <| fun _ ->
                  let cfg =
                      { config with
                          DailyBudgetWorld = 2
                          DailyBudgetUserClub = 1 }

                  let candidates =
                      [ world 10 NewsImportance.Minor
                        world 11 NewsImportance.Headline
                        world 12 NewsImportance.Major
                        world 13 NewsImportance.Major
                        NewsKind.Streak(1, StreakKind.Losses, 3), NewsImportance.Notable, NewsScope.UserClub, [ 1 ], []
                        NewsKind.Streak(1, StreakKind.Wins, 5), NewsImportance.Headline, NewsScope.UserClub, [ 1 ], [] ]

                  let result = NewsDesk.publish cfg date candidates state

                  let worldClubs =
                      result.News |> List.filter (fun n -> n.Scope = NewsScope.World) |> List.map _.Clubs

                  Expect.sequenceEqual (worldClubs |> List.sort) [ [ 11 ]; [ 12 ] ] "headline and first major win"

                  let user = result.News |> List.filter (fun n -> n.Scope = NewsScope.UserClub)
                  Expect.equal user.Length 1 "user budget is one"
                  Expect.equal user.Head.Importance NewsImportance.Headline "the more important user item survives"

              testCase "importance orders Minor below Headline"
              <| fun _ ->
                  Expect.isTrue (NewsImportance.Minor < NewsImportance.Notable) "minor < notable"
                  Expect.isTrue (NewsImportance.Notable < NewsImportance.Major) "notable < major"
                  Expect.isTrue (NewsImportance.Major < NewsImportance.Headline) "major < headline"

              testCase "a duplicate within the window is dropped and one outside it is kept"
              <| fun _ ->
                  let first = NewsDesk.publish config date [ streak 10 3 ] state
                  Expect.equal first.News.Length 1 "first publication stored"

                  let inside =
                      NewsDesk.publish config (date.AddDays(float config.DuplicateWindowDays)) [ streak 10 5 ] first

                  Expect.equal inside.News.Length 1 "same kind and club inside the window is dropped"

                  let otherClub = NewsDesk.publish config (date.AddDays 1.0) [ streak 11 3 ] first
                  Expect.equal otherClub.News.Length 2 "a different primary entity is not a duplicate"

                  let outside =
                      NewsDesk.publish config (date.AddDays(float config.DuplicateWindowDays + 1.0)) [ streak 10 5 ] first

                  Expect.equal outside.News.Length 2 "outside the window it is published again"

              testCase "ids increase across publications and newest items come first"
              <| fun _ ->
                  let a = NewsDesk.publish config date [ streak 10 3; streak 11 3 ] state
                  let b = NewsDesk.publish config (date.AddDays 1.0) [ streak 12 3 ] a
                  Expect.equal (b.News |> List.map _.Id) [ 3; 2; 1 ] "newest first, ids never reused"
                  Expect.equal b.NextNewsId 4 "counter advanced past the last id"

              testCase "retention trims the oldest items"
              <| fun _ ->
                  let cfg =
                      { config with
                          NewsRetention = 3
                          DailyBudgetWorld = 10 }

                  let final =
                      [ 1..5 ]
                      |> List.fold (fun gs i -> NewsDesk.publish cfg (date.AddDays(float i)) [ streak (100 + i) 3 ] gs) state

                  Expect.equal (final.News |> List.map _.Id) [ 5; 4; 3 ] "only the three newest remain"
                  Expect.equal final.NextNewsId 6 "ids keep counting after trimming" ]

    let private scopeTests =
        testList
            "NewsDesk scope"
            [ testCase "user club, same league and foreign clubs classify differently"
              <| fun _ ->
                  Expect.equal (NewsDesk.classifyScope state [ 2; 1 ]) NewsScope.UserClub "user club involved"
                  Expect.equal (NewsDesk.classifyScope state [ 2; 3 ]) NewsScope.UserLeague "league rival"
                  Expect.equal (NewsDesk.classifyScope state [ 99 ]) NewsScope.World "foreign club"
                  Expect.equal (NewsDesk.classifyScope state []) NewsScope.World "no club" ]

    let private randomTests =
        testList
            "PulseRandom"
            [ testCase "seed is stable and sensitive to every input"
              <| fun _ ->
                  let baseSeed = PulseRandom.seedFor 7UL date "detector" 5
                  Expect.equal (PulseRandom.seedFor 7UL date "detector" 5) baseSeed "same inputs"
                  Expect.notEqual (PulseRandom.seedFor 8UL date "detector" 5) baseSeed "execution seed"
                  Expect.notEqual (PulseRandom.seedFor 7UL (date.AddDays 1.0) "detector" 5) baseSeed "date"
                  Expect.notEqual (PulseRandom.seedFor 7UL date "other" 5) baseSeed "detector"
                  Expect.notEqual (PulseRandom.seedFor 7UL date "detector" 6) baseSeed "entity"

              testCase "seed matches a pinned FNV-1a value so it cannot depend on the process"
              <| fun _ ->
                  // Value computed independently in Python FNV-1a: String.GetHashCode-based seeds would differ between runs.
                  Expect.equal (PulseRandom.seedFor 7UL (DateTime(2030, 5, 10)) "detector" 5) 15532670867235901127UL "pinned"

              testCase "roll stays in [0,1) and spreads"
              <| fun _ ->
                  let rolls = [ 0..999 ] |> List.map (fun i -> PulseRandom.roll (PulseRandom.seedFor 1UL date "r" i))
                  Expect.all rolls (fun r -> r >= 0.0 && r < 1.0) "range"
                  let mean = List.average rolls
                  Expect.isTrue (mean > 0.4 && mean < 0.6) $"mean near 0.5, got {mean}" ]

    let private configTests =
        testList
            "WorldPulseConfig"
            [ testCase "defaults validate"
              <| fun _ -> Expect.isEmpty (WorldPulseConfig.validate config) "defaults are valid"

              testCase "invalid values are rejected one by one"
              <| fun _ ->
                  let rejected c = not (WorldPulseConfig.validate c).IsEmpty
                  Expect.isTrue (rejected { config with NewsRetention = 0 }) "retention"
                  Expect.isTrue (rejected { config with DailyBudgetUserClub = -1 }) "club budget"
                  Expect.isTrue (rejected { config with DailyBudgetUserLeague = -1 }) "league budget"
                  Expect.isTrue (rejected { config with DailyBudgetWorld = -1 }) "world budget"
                  Expect.isTrue (rejected { config with DuplicateWindowDays = -1 }) "window"
                  Expect.isTrue (rejected { config with InitialBoardConfidence = 101 }) "confidence high"
                  Expect.isTrue (rejected { config with InitialBoardConfidence = -1 }) "confidence low"
                  Expect.isTrue (rejected { config with InitialFansMood = 101 }) "mood high"
                  Expect.isTrue (rejected { config with RecentResultsKept = 0 }) "results kept" ]

    let private pipelineTests =
        testList
            "WorldPulse"
            [ testCase "detectors feed the desk and the run is a no-op without them"
              <| fun _ ->
                  let detector: PulseDetector = fun _ d -> [ streak 10 3 ]
                  let published = WorldPulse.runWith [ detector ] config date state
                  Expect.equal published.News.Length 1 "detector candidate published"
                  Expect.equal (WorldPulse.run config date state) state "phase 1 registers no detectors" ]

    let tests =
        testList "World pulse" [ deskTests; scopeTests; randomTests; configTests; pipelineTests ]
