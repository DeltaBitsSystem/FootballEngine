namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Pulse
open FootballEngine.Core.Tests.World

module PressureTests =

    let private config = WorldPulseConfig.defaultWorldPulse

    let private baseWorld () =
        WorldGenerationTests.generate 0xC02UL []

    let private firstDay = WorldGenerationTests.startDate.Date

    let private aiClubs (gs: GameState) =
        gs.Clubs.Values
        |> Seq.filter (fun c -> c.Id <> gs.UserClubId)
        |> Seq.sortByDescending _.Reputation
        |> List.ofSeq

    let private leagueId (gs: GameState) =
        gs.Competitions |> Map.toSeq |> Seq.head |> fst

    let private setClub id (f: Club -> Club) (gs: GameState) =
        { gs with
            Clubs = gs.Clubs |> Map.add id (f gs.Clubs[id]) }

    /// Appends fixtures between `club` and `rival` on consecutive days after the career start; the club's result is
    /// (own, rival) goals and home/away alternates. Returns the fixture ids so callers can mark them processed.
    let private addFixtures
        (club: ClubId)
        (rival: ClubId)
        (startOffset: int)
        (results: (int * int) list)
        (gs: GameState)
        =
        let cid = leagueId gs

        let made =
            results
            |> List.mapi (fun i (own, other) ->
                let id = 900_000 + startOffset + i
                let isHome = i % 2 = 0
                let home, away = if isHome then club, rival else rival, club
                let hs, aws = if isHome then own, other else other, own

                id,
                { Id = id
                  CompetitionId = cid
                  Round = None
                  HomeClubId = home
                  AwayClubId = away
                  ScheduledDate = firstDay.AddDays(float (1 + startOffset + i))
                  Played = true
                  HomeScore = Some hs
                  AwayScore = Some aws
                  Events = [] })

        let comp = gs.Competitions[cid]

        let comp =
            { comp with
                Fixtures = made |> List.fold (fun m (id, f) -> Map.add id f m) comp.Fixtures }

        { gs with
            Competitions = Map.add cid comp gs.Competitions },
        made |> List.map fst

    let private markProcessed ids (gs: GameState) =
        { gs with
            PulseProcessed = Set.union gs.PulseProcessed (Set.ofList ids) }

    let private dayAfter n = firstDay.AddDays(float n)

    let private run cfg date gs = WorldPulse.runWith [] cfg date gs

    let private newsOf pick (gs: GameState) =
        gs.News |> List.choose (fun n -> pick n.Kind)

    let private sackedNews club (gs: GameState) =
        newsOf
            (function
            | NewsKind.ManagerSacked(c, m, n) when c = club -> Some(m, n)
            | _ -> None)
            gs

    let private appointedNews club (gs: GameState) =
        newsOf
            (function
            | NewsKind.ManagerAppointed(c, m) when c = club -> Some m
            | _ -> None)
            gs

    let private headCoachId club gs =
        GameState.headCoach club gs |> Option.map _.Id

    /// A strong AI club losing at home and away to the weakest club, with equal ConfidenceGain defaults.
    let private strongAndWeak (gs: GameState) =
        let ai = aiClubs gs
        List.head ai, List.last ai

    let private losingSeries n = List.replicate n (0, 3)

    let private expectedTests =
        testList
            "expected points"
            [ testCase "stronger club expects more, home expects more, and bounds hold"
              <| fun _ ->
                  let e = Pressure.expectedPoints config
                  Expect.isGreaterThan (e 8000 3000 true) (e 3000 8000 true) "stronger"
                  Expect.isGreaterThan (e 5000 5000 true) (e 5000 5000 false) "home"

                  Expect.floatClose
                      Accuracy.high
                      (e 5000 5000 true)
                      (config.ExpectedPointsBase + config.HomeAdvantagePoints)
                      "equal sides"

                  Expect.isLessThanOrEqual (e 9999 500 true) config.ExpectedPointsMax "upper bound"
                  Expect.isGreaterThanOrEqual (e 500 9999 false) config.ExpectedPointsMin "lower bound"
                  let tight = { config with ExpectedPointsMax = 1.5 }
                  Expect.equal (Pressure.expectedPoints tight 9999 500 true) 1.5 "clamp follows config"

              testCase "new config fields are validated"
              <| fun _ ->
                  let rejected c =
                      not (WorldPulseConfig.validate c).IsEmpty

                  Expect.isTrue
                      (rejected
                          { config with
                              SackThreshold = config.UltimatumThreshold })
                      "ordering"

                  Expect.isTrue (rejected { config with ReputationScale = 0.0 }) "scale"

                  Expect.isTrue
                      (rejected
                          { config with
                              ExpectedPointsMin = 2.0
                              ExpectedPointsMax = 1.0 })
                      "bounds"

                  Expect.isTrue (rejected { config with MeanReversionRate = 1.5 }) "reversion"
                  Expect.isTrue (rejected { config with MinMatchesBeforeSack = 0 }) "min matches"
                  Expect.isTrue (rejected { config with HoneymoonConfidence = 10 }) "honeymoon below warning"

                  Expect.isTrue
                      (rejected
                          { config with
                              FansRestlessBelow = config.FansFuriousBelow })
                      "fan bands" ]

    let private adjustTests =
        testList
            "confidence and mood"
            [ testCase "result above expectation raises, below lowers, for both measures"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0
                  // Weak club wins at home over the strongest: overperformance for it, failure for the favourite.
                  let gs, _ = addFixtures weak.Id strong.Id 0 [ (2, 0) ] gs0
                  let after = run config (dayAfter 1) gs

                  Expect.isGreaterThan
                      after.Clubs[weak.Id].BoardConfidence
                      gs0.Clubs[weak.Id].BoardConfidence
                      "winner confidence"

                  Expect.isGreaterThan after.Clubs[weak.Id].FansMood gs0.Clubs[weak.Id].FansMood "winner mood"

                  Expect.isLessThan
                      after.Clubs[strong.Id].BoardConfidence
                      gs0.Clubs[strong.Id].BoardConfidence
                      "loser confidence"

                  Expect.isLessThan after.Clubs[strong.Id].FansMood gs0.Clubs[strong.Id].FansMood "loser mood"

              testCase "a fixture moves pressure exactly once"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0
                  let gs, _ = addFixtures weak.Id strong.Id 0 [ (2, 0) ] gs0
                  let once = run config (dayAfter 1) gs
                  let twice = run config (dayAfter 2) once

                  Expect.equal
                      twice.Clubs[weak.Id].BoardConfidence
                      once.Clubs[weak.Id].BoardConfidence
                      "no second absorption"

              testCase "values clamp to 0..100"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0

                  let high =
                      gs0
                      |> setClub weak.Id (fun c ->
                          { c with
                              BoardConfidence = 100
                              FansMood = 100 })
                      |> addFixtures weak.Id strong.Id 0 [ (5, 0) ]
                      |> fst

                  let low =
                      gs0
                      |> setClub strong.Id (fun c ->
                          { c with
                              BoardConfidence = 0
                              FansMood = 0 })
                      |> addFixtures weak.Id strong.Id 0 [ (5, 0) ]
                      |> fst

                  let h = (run config (dayAfter 1) high).Clubs[weak.Id]
                  let l = (run config (dayAfter 1) low).Clubs[strong.Id]
                  Expect.equal (h.BoardConfidence, h.FansMood) (100, 100) "ceiling"
                  Expect.equal (l.BoardConfidence, l.FansMood) (0, 0) "floor"

              testCase "mean reversion pulls towards the initial value and can be switched off"
              <| fun _ ->
                  let adj cfg current =
                      Pressure.adjust cfg 60 current config.ConfidenceGain 0.0

                  Expect.isLessThan (adj config 100) 100 "from above"
                  Expect.isGreaterThan (adj config 20) 20 "from below"
                  Expect.equal (adj config 60) 60 "at the anchor"
                  Expect.equal (adj { config with MeanReversionRate = 0.0 } 100) 100 "disabled"

              testCase "fan bands follow the configured thresholds"
              <| fun _ ->
                  let band = Pressure.fansBand config
                  Expect.equal (band (config.FansFuriousBelow - 1)) FansMoodBand.Furious "furious"
                  Expect.equal (band config.FansFuriousBelow) FansMoodBand.Restless "restless"
                  Expect.equal (band config.FansRestlessBelow) FansMoodBand.Content "content"
                  Expect.equal (band config.FansEuphoricFrom) FansMoodBand.Euphoric "euphoric" ]

    let private withReputations a b (gs: GameState) =
        gs
        |> setClub a (fun c -> { c with Reputation = 5000 })
        |> setClub b (fun c -> { c with Reputation = 5000 })

    let private sackingTests =
        testList
            "AI sackings"
            [ testCase "sacked only after the minimum matches, with a full replacement"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0
                  let old = (GameState.headCoach strong.Id gs0).Value
                  let m = config.MinMatchesBeforeSack
                  let early, _ = addFixtures strong.Id weak.Id 0 (losingSeries (m - 1)) gs0
                  let early = run config (dayAfter m) early

                  Expect.isLessThan
                      early.Clubs[strong.Id].BoardConfidence
                      config.SackThreshold
                      "confidence is already below the line"

                  Expect.equal (headCoachId strong.Id early) (Some old.Id) "kept before the minimum"
                  Expect.isEmpty (sackedNews strong.Id early) "no sacking news yet"

                  let full, _ = addFixtures strong.Id weak.Id 0 (losingSeries m) gs0
                  let day = dayAfter (m + 1)
                  let after = run config day full
                  let hired = (GameState.headCoach strong.Id after).Value
                  Expect.notEqual hired.Id old.Id "a new head coach"
                  Expect.equal after.Staff[old.Id].Status StaffStatus.Unemployed "old coach unemployed"
                  Expect.isNone after.Staff[old.Id].Contract "old coach has no contract"
                  let lineup = hired.Attributes.Coaching.Lineup.Value

                  Expect.equal
                      (lineup.Slots |> List.choose _.PlayerId |> List.distinct |> List.length)
                      11
                      "eleven distinct starters"

                  Expect.equal after.Clubs[strong.Id].ManagerSince (Some day) "ManagerSince"
                  Expect.equal after.Clubs[strong.Id].BoardConfidence config.HoneymoonConfidence "honeymoon"
                  Expect.equal (sackedNews strong.Id after) [ old.Id, m ] "sacked once with matches in charge"
                  Expect.equal (appointedNews strong.Id after) [ hired.Id ] "appointed once"
                  let again = run config (dayAfter (m + 2)) after
                  Expect.equal (sackedNews strong.Id again |> List.length) 1 "not repeated the next day"
                  Expect.equal (headCoachId strong.Id again) (Some hired.Id) "new coach keeps the job"

              testCase "no sacking above the threshold"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0
                  let gs, _ = addFixtures strong.Id weak.Id 0 (List.replicate 12 (3, 0)) gs0
                  let after = run config (dayAfter 13) gs
                  Expect.isGreaterThan after.Clubs[strong.Id].BoardConfidence config.SackThreshold "healthy"
                  Expect.equal (headCoachId strong.Id after) (headCoachId strong.Id gs0) "kept"
                  Expect.isEmpty (sackedNews strong.Id after) "no news"

              testCase "the AI path never touches the user's coach"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let user = gs0.UserClubId
                  let other = (aiClubs gs0).Head.Id
                  let between = config.SackThreshold - 5
                  let gs, ids = addFixtures user other 0 (losingSeries 10) gs0

                  let gs =
                      gs
                      |> markProcessed ids
                      |> setClub user (fun c -> { c with BoardConfidence = between })

                  let after = run config (dayAfter 11) gs
                  Expect.equal after.Staff[after.UserStaffId] gs0.Staff[gs0.UserStaffId] "user manager untouched"
                  Expect.isEmpty (sackedNews user after) "no sacking news for the user"

              testCase "replacement comes from the unemployed head coaches by quality"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0
                  let template = (GameState.headCoach weak.Id gs0).Value

                  let boost (n: int) (s: Staff) =
                      { s with
                          Knowledge =
                              { s.Knowledge with
                                  TacticalKnowledge = n
                                  JudgingPlayerAbility = n
                                  JudgingPlayerPotential = n }
                          Attributes.Coaching.PreferredFormation = Some F343 }

                  let free id n =
                      { boost n template with
                          Id = id
                          Name = $"Free {id}"
                          Contract = None
                          Status = StaffStatus.Unemployed }

                  let good, poor = free 777_001 20, free 777_002 1

                  let gs0 =
                      { gs0 with
                          Staff = gs0.Staff |> Map.add good.Id good |> Map.add poor.Id poor }

                  let gs, _ =
                      addFixtures strong.Id weak.Id 0 (losingSeries config.MinMatchesBeforeSack) gs0

                  let after = run config (dayAfter 20) gs
                  let hired = (GameState.headCoach strong.Id after).Value
                  Expect.equal hired.Id good.Id "best unemployed coach"
                  Expect.equal hired.Attributes.Coaching.Lineup.Value.Formation F343 "his preferred formation"
                  Expect.equal hired.Contract.Value.ClubId strong.Id "contracted by the club"
                  Expect.equal after.Staff[poor.Id].Status StaffStatus.Unemployed "the other stays unemployed"

              testCase "a coach is generated deterministically when no one is available"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0

                  let free =
                      gs0.Staff.Values
                      |> Seq.filter (fun s -> s.Status = StaffStatus.Unemployed && s.Role = HeadCoach)

                  Expect.isEmpty free "premise: nobody unemployed"

                  let gs, _ =
                      addFixtures strong.Id weak.Id 0 (losingSeries config.MinMatchesBeforeSack) gs0

                  let a = run config (dayAfter 20) gs
                  let b = run config (dayAfter 20) gs
                  let hiredA = (GameState.headCoach strong.Id a).Value
                  Expect.isFalse (gs0.Staff.ContainsKey hiredA.Id) "a brand new person"
                  Expect.equal hiredA (GameState.headCoach strong.Id b).Value "same input, same coach"
                  let other = run config (dayAfter 20) { gs with ExecutionSeed = 99UL }
                  Expect.notEqual (GameState.headCoach strong.Id other).Value hiredA "the seed drives generation" ]

    let private unrestTests =
        testList
            "unrest news and user messages"
            [ testCase "board unrest and fan mood news fire on crossings only"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0

                  let gs0 =
                      gs0
                      |> setClub strong.Id (fun c ->
                          { c with
                              BoardConfidence = config.BoardWarningThreshold
                              FansMood = config.FansRestlessBelow })

                  let gs, _ = addFixtures strong.Id weak.Id 0 [ (0, 3) ] gs0
                  let after = run config (dayAfter 1) gs

                  let unrest g =
                      newsOf
                          (function
                          | NewsKind.BoardUnrest(c, v) when c = strong.Id -> Some v
                          | _ -> None)
                          g

                  let fans g =
                      newsOf
                          (function
                          | NewsKind.FansMood(c, b) when c = strong.Id -> Some b
                          | _ -> None)
                          g

                  Expect.hasLength (unrest after) 1 "one unrest item"
                  Expect.equal (fans after) [ FansMoodBand.Restless ] "band dropped"
                  // A second loss while already below both lines is not a new crossing.
                  let gs2, _ = addFixtures strong.Id weak.Id 5 [ (0, 3) ] after
                  let next = run config (dayAfter 6) gs2
                  Expect.hasLength (unrest next) 1 "unrest not repeated"
                  Expect.hasLength (fans next |> List.filter ((=) FansMoodBand.Restless)) 1 "same band not repeated"

              testCase "the user gets one warning and one ultimatum, each at its crossing"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let user = gs0.UserClubId
                  let rival = (aiClubs gs0).Head.Id
                  let gs0 = gs0 |> withReputations user rival
                  let subjects (g: GameState) = g.Inbox |> List.map _.Subject

                  let warn =
                      gs0
                      |> setClub user (fun c ->
                          { c with
                              BoardConfidence = config.BoardWarningThreshold })

                  let warn, _ = addFixtures user rival 0 [ (0, 2) ] warn
                  let warned = run config (dayAfter 1) warn
                  Expect.equal (subjects warned) [ "Board concern" ] "warning once"
                  Expect.equal warned.Inbox.Head.Category InboxMessageCategory.BoardUpdate "board category"
                  Expect.isFalse warned.Inbox.Head.RequiresAction "informational"
                  let again = run config (dayAfter 2) warned
                  Expect.equal (subjects again) [ "Board concern" ] "not repeated"

                  let ult =
                      gs0
                      |> setClub user (fun c ->
                          { c with
                              BoardConfidence = config.UltimatumThreshold + 3 })

                  let ult, _ = addFixtures user rival 0 [ (0, 2) ] ult
                  let ulted = run config (dayAfter 1) ult
                  Expect.equal (subjects ulted) [ "Board ultimatum" ] "ultimatum once, superseding the warning"
                  Expect.equal ulted.Staff[ulted.UserStaffId] gs0.Staff[gs0.UserStaffId] "still employed"

              testCase "the user is never dismissed, even far below every threshold, and gets the ultimatum once"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let user = gs0.UserClubId
                  let rival = (aiClubs gs0).Head.Id
                  let gs0 = gs0 |> withReputations user rival

                  let past, ids =
                      addFixtures user rival 0 (losingSeries (config.MinMatchesBeforeSack + 4)) gs0

                  let past =
                      past
                      |> markProcessed ids
                      |> setClub user (fun c ->
                          { c with
                              BoardConfidence = config.UltimatumThreshold + 1 })

                  let live, _ = addFixtures user rival 20 [ (0, 2) ] past
                  let harsh = { config with ConfidenceGain = 100.0 }
                  let after = run harsh (dayAfter 21) live
                  Expect.equal after.Clubs[user].BoardConfidence 0 "confidence collapsed"
                  Expect.equal after.Staff[after.UserStaffId] gs0.Staff[gs0.UserStaffId] "user manager untouched"
                  Expect.isEmpty after.CareerHistory "no dismissal recorded"
                  Expect.equal (after.Inbox |> List.map _.Subject) [ "Board ultimatum" ] "ultimatum once"
                  let later = run harsh (dayAfter 22) after
                  Expect.equal (later.Inbox |> List.map _.Subject) [ "Board ultimatum" ] "not repeated"
                  Expect.equal later.Staff[later.UserStaffId] gs0.Staff[gs0.UserStaffId] "still employed" ]

    let private budgetTests =
        testList
            "news budget"
            [ testCase "ManagerSacked survives a day with more candidates in its scope than the budget"
              <| fun _ ->
                  let gs0 = baseWorld ()
                  let strong, weak = strongAndWeak gs0

                  let gs, _ =
                      addFixtures strong.Id weak.Id 0 (losingSeries config.MinMatchesBeforeSack) gs0

                  let noise: PulseDetector =
                      fun _ _ ->
                          [ for n in 1 .. 3 * config.DailyBudgetUserLeague ->
                                NewsKind.Streak(weak.Id, StreakKind.Wins, n),
                                NewsImportance.Major,
                                NewsScope.UserLeague,
                                [ weak.Id ],
                                [] ]

                  let after = WorldPulse.runWith [ noise ] config (dayAfter 20) gs

                  let leagueItems =
                      after.News |> List.filter (fun n -> n.Scope = NewsScope.UserLeague)

                  Expect.hasLength
                      leagueItems
                      config.DailyBudgetUserLeague
                      "the scope budget was exceeded by candidates"

                  Expect.hasLength (sackedNews strong.Id after) 1 "sacking still published" ]

    let private seasonTests =
        testList
            "season integration"
            [ testCase "a full generated season keeps one head coach per club and sacks within a sane range"
              <| fun _ ->
                  let world = baseWorld ()
                  let balance = FootballEngine.Core.Match.BalanceConfig.defaultConfig

                  let rec go (clock: WorldClock) (state: GameState) days =
                      let pending =
                          state.Competitions.Values
                          |> Seq.exists (fun c -> c.Fixtures.Values |> Seq.exists MatchFixture.isPending)

                      if not pending || days > 400 then
                          state
                      else
                          match WorldScheduler.executeDayWithBalance (balance, RunThroughMatches, clock, state) with
                          | Error failure -> failtest failure.Message
                          | Ok day -> go day.Clock day.State (days + 1)

                  let finished = go (WorldClockOps.init world.Season) world 0
                  Expect.isEmpty (WorldPulseConfig.validate config) "defaults valid"

                  // The shipped thresholds guard a 30-round league; the 4-club test league has 6 rounds per club, so the
                  // same season is replayed through the ledger with a short qualification to exercise the sack path.
                  let lenient =
                      { config with
                          MinMatchesBeforeSack = 3
                          SackThreshold = 55
                          UltimatumThreshold = 56
                          BoardWarningThreshold = 57
                          HoneymoonConfidence = 58 }

                  Expect.isEmpty (WorldPulseConfig.validate lenient) "lenient config valid"

                  // Match simulation is not bit-stable across runs, so the replay forces one AI club to lose every game:
                  // the sack path is then exercised regardless of how the simulated season went.
                  let victim = (aiClubs finished).Head.Id

                  let rigged =
                      finished.Competitions
                      |> Map.map (fun _ c ->
                          { c with
                              Fixtures =
                                  c.Fixtures
                                  |> Map.map (fun _ f ->
                                      if f.HomeClubId = victim then
                                          { f with
                                              HomeScore = Some 0
                                              AwayScore = Some 3 }
                                      elif f.AwayClubId = victim then
                                          { f with
                                              HomeScore = Some 3
                                              AwayScore = Some 0 }
                                      else
                                          f) })

                  let rewound =
                      { finished with
                          Competitions = rigged
                          PulseProcessed = Set.empty
                          News = []
                          NextNewsId = 1
                          Clubs =
                              finished.Clubs
                              |> Map.map (fun _ c ->
                                  { c with
                                      BoardConfidence = 60
                                      FansMood = 60
                                      RecentResults = []
                                      ManagerSince = Some firstDay }) }

                  let replay = run lenient (finished.CurrentDate) rewound

                  let changedCoaches (a: GameState) (b: GameState) =
                      a.Clubs.Keys
                      |> Seq.filter (fun id -> id <> a.UserClubId && headCoachId id a <> headCoachId id b)
                      |> Seq.length

                  // Coach turnover is counted from state, not news, because the news desk budgets how much is shown.
                  let sacked = changedCoaches rewound replay

                  let defaultSacked =
                      finished.News
                      |> List.filter (fun n ->
                          match n.Kind with
                          | NewsKind.ManagerSacked _ -> true
                          | _ -> false)
                      |> List.length

                  Expect.isGreaterThanOrEqual
                      sacked
                      1
                      $"the lenient replay must exercise the sack path (got {sacked}; confidences {replay.Clubs.Values |> Seq.map (fun c -> c.Id, c.BoardConfidence) |> List.ofSeq})"

                  Expect.isLessThanOrEqual
                      sacked
                      6
                      $"sackings in the replayed season: {sacked} (default config: {defaultSacked})"

                  Expect.isLessThanOrEqual defaultSacked 6 $"sackings with default config: {defaultSacked}"

                  for replayState in [ finished; replay ] do
                      // The user's club also holds the generated coach beside the user's own manager.
                      for club in replayState.Clubs.Values |> Seq.filter (fun c -> c.Id <> replayState.UserClubId) do
                          let coaches =
                              GameState.getStaff club.Id replayState
                              |> List.filter (fun s -> s.Role = HeadCoach)

                          Expect.hasLength coaches 1 $"club {club.Id} has exactly one head coach"

                  let replay2 = run lenient (finished.CurrentDate) rewound
                  Expect.equal replay2.News replay.News "the replay is deterministic" ]

    let tests =
        testList
            "Pressure"
            [ expectedTests
              adjustTests
              sackingTests
              unrestTests
              budgetTests
              seasonTests ]
