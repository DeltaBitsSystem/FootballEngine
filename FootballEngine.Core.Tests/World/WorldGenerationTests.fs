namespace FootballEngine.Core.Tests.World

open System
open Expecto
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.Core.Match
open FootballEngine.World
open FootballEngine.World.Phases

module WorldGenerationTests =
    let private countryJson =
        """{"id":"test.world:country/tst","kind":"country","schema":1,"code":"TST","confederation":"UEFA","nameKey":"test.world:country/tst.name","domain":{"Country":{"Code":"TST","Name":"Testland","Confederation":"UEFA"},"Names":{"FirstNames":["Alex","Sam"],"LastNames":["Stone","River"]},"LeagueNames":["Test League"],"LeagueRules":[{"PointsForWin":3,"PointsForDraw":1,"Tiebreakers":["GD","GS"],"Promotion":[],"Relegation":[]}],"Clubs":[{"Name":"North FC","LeagueLevel":0,"Reputation":7000},{"Name":"South FC","LeagueLevel":0,"Reputation":6500},{"Name":"East FC","LeagueLevel":0,"Reputation":null},{"Name":"West FC","LeagueLevel":0,"Reputation":null}],"Cups":[{"Name":"Test Cup","Format":"SK:SingleLeg:ExtraTimeThenPenalties"}]}}"""

    let private competitionJson =
        """{"id":"test.world:competition/test-cup","kind":"competition","schema":1,"nameKey":"test.world:competition/test-cup.name","domain":{"Name":"Test International","Confederation":"UEFA","Format":"GTK:2;2;1;3;1;GD,GS,H2H~TwoLegs:ExtraTimeThenPenalties","Qualification":[{"Kind":"LeaguePosition","Level":0,"FromPos":1,"ToPos":2,"CompetitionName":null,"Slots":null},{"Kind":"CupWinner","Level":null,"FromPos":null,"ToPos":null,"CompetitionName":"Test Cup","Slots":null}]}}"""

    let private package: ModPackage =
        { Manifest =
            { Id = ModId "test.world"
              Version = "1.0.0"
              Dependencies = []
              Priority = 0
              Before = []
              After = [] }
          PackageSha256 = "sha-test-world"
          SportsContent =
            Map.ofList
                [ ContentId "test.world:country/tst", countryJson
                  ContentId "test.world:competition/test-cup", competitionJson ]
          Translations = Map.empty
          Assets = Map.empty
          Patches = [] }

    let private resolved: ResolvedDataSet =
        match
            ModResolution.resolve
                [ { RelativePath = "test-world.femod"
                    Package = package } ]
        with
        | Ok value -> value
        | Error error -> failwithf "test dataset did not resolve: %A" error

    let startDate = DateTime(2034, 3, 17, 9, 30, 0, DateTimeKind.Utc)

    let generate seed secondary =
        match WorldGen.generateNewGame resolved seed startDate "TST" "Test Manager" secondary with
        | Ok value -> value
        | Error error -> failtestf "generation failed: %A" error

    let private completedUserJob () =
        let generated = generate 0xC02UL []

        let matchId, fixture =
            generated.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
            |> Seq.find (fun (_, fixture) ->
                MatchFixture.isPending fixture
                && MatchFixture.involves generated.UserClubId fixture)

        let state =
            { generated with
                CurrentDate = fixture.ScheduledDate.Date }

        let clock = WorldClockOps.init state.Season

        let job =
            match WorldRunner.prepareUserMatchJobWithBalance BalanceConfig.defaultConfig clock state matchId with
            | Ok value -> value
            | Error error -> failtestf "user match preparation failed: %s" error

        let outcome, commands =
            match MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast with
            | Error error -> failtestf "user match session creation failed: %A" error
            | Ok session ->
                let command =
                    { EffectiveTick = 400<subtick>
                      Sequence = 0UL
                      Revision = job.Input.EffectiveConfig.Revision
                      Manager = job.Input.Context.HomeClub.Id
                      Action = ChangeTactics { job.Input.Context.HomeClub.BaseTactics with Tempo = 0.8f } }

                MatchSession.receive command session
                |> function
                    | Ok() -> ()
                    | Error error -> failtestf "managerial command was rejected: %A" error

                match MatchSession.finish session with
                | Ok value -> value, session.Commands
                | Error error -> failtestf "user match did not finish: %A" error

        state, clock, WorldRunner.matchJobResult job commands outcome

    let worldGenerationTests =
        testList
            "World generation"
            [ testCase "saturated club memory follows actual accepted chronology and the next kickoff"
              <| fun _ ->
                  let cfg = BalanceConfig.defaultConfig
                  let generated = generate 0xC02UL []
                  let prior = Map.ofList [ for key in 0 .. 23 -> key, ({ Samples = cfg.CollectiveAttack.EvidenceCap; RewardSum = cfg.CollectiveAttack.EvidenceCap * 0.75 }: AttackEstimate) ]
                  let populated = { generated with Clubs = generated.Clubs |> Map.map (fun _ club -> { club with CoordinationMemory = { club.CoordinationMemory with AttackLearning = prior } }) }
                  let fixtures = populated.Competitions |> Map.toSeq |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq) |> Seq.filter (fun (_, f) -> MatchFixture.isPending f && MatchFixture.involves populated.UserClubId f) |> Seq.sortBy (fun (id, f) -> f.ScheduledDate, id) |> Seq.truncate 6 |> Seq.toArray
                  Expect.hasLength fixtures 6 "six consecutive pending fixtures, never selected by their outcomes"
                  let mutable state = populated
                  let mutable clock = WorldClockOps.init state.Season
                  let mutable samples = Map.empty
                  let unwrap = function Ok value -> value | Error e -> failtestf "%A" e
                  for index in 0 .. fixtures.Length - 1 do
                      let fixtureId, fixture = fixtures[index]
                      state <- {state with CurrentDate = fixture.ScheduledDate.Date}
                      let job = WorldRunner.prepareUserMatchJobWithBalance cfg clock state fixtureId |> unwrap
                      let session = MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast |> unwrap
                      let outcome = MatchSession.finish session |> unwrap
                      let learnedEvents = outcome.Events |> List.filter (fun e -> match e.Data with EventData.CollectiveAttack(_, _, Ended(_, _, Some _)) -> true | _ -> false)
                      if index = 0 then Expect.isGreaterThan learnedEvents.Length (int cfg.CollectiveAttack.EvidenceCap) "real execution exceeds the cap, not fabricated sample rewards"
                      for event in learnedEvents do
                          match event.Data with
                          | EventData.CollectiveAttack(side, _, Ended(_, _, Some key)) ->
                              let club = if side = ClubSide.Home then job.Input.Context.HomeClub.Id else job.Input.Context.AwayClub.Id
                              samples <- samples |> Map.change (club, key) (fun count -> Some(1 + Option.defaultValue 0 count))
                          | _ -> ()
                      let result = WorldRunner.matchJobResult job [] outcome
                      let committed = WorldRunner.commitUserMatchJobWithBalance cfg clock state result |> unwrap
                      for side, final in [ClubSide.Home, outcome.Final.Home; ClubSide.Away, outcome.Final.Away] do
                          let club = if side = ClubSide.Home then job.Input.Context.HomeClub else job.Input.Context.AwayClub
                          let saved = committed.DayResult.GameState.Clubs[int club.Id].CoordinationMemory.AttackLearning
                          Expect.equal saved final.Attack.Estimates "every accepted policy equals chronological online updates, including saturated cells"
                      Expect.isError (WorldRunner.commitUserMatchJobWithBalance cfg committed.DayResult.WorldClock committed.DayResult.GameState result) "duplicate result cannot multiply learning"
                      let nextCtx = MatchContextBuilder.build committed.DayResult.GameState fixture cfg |> Option.get
                      Expect.equal nextCtx.HomeClub.AttackPrior outcome.Final.Home.Attack.Estimates "next kickoff uses exactly accepted memory"
                      if index = 0 then Expect.notEqual (MatchJob.inputHash job.Input) (MatchJob.inputHash {job.Input with Context = nextCtx}) "learned kickoff identity differs, while original archive remains immutable"
                      state <- committed.DayResult.GameState
                      clock <- committed.DayResult.WorldClock
                  let largestCell = samples |> Map.values |> Seq.max
                  Expect.isGreaterThan largestCell (int cfg.CollectiveAttack.EvidenceCap) "the sequence exceeds cap in an actual club/context cell; no football reward is fabricated to reach saturation"

              testCase "same resolved snapshot, seed and inputs produce the same state"
              <| fun _ ->
                  let first = generate 0xC02UL []
                  let second = generate 0xC02UL []
                  Expect.equal first second "generation is structurally deterministic"
                  Expect.equal first.DataLock resolved.DataLock "the resolved lock is frozen in the state"

              testCase "a different seed changes generated world data"
              <| fun _ ->
                  let first = generate 0xC02UL []
                  let second = generate 0xC03UL []
                  Expect.isTrue (first <> second) "different seeds must not collapse to one world"

              testCase "execution seeds split fixtures independently of scheduling order"
              <| fun _ ->
                  let fixtures = [ 3; 14; 7; 100 ]
                  let seedMap order = order |> List.map (fun id -> id, MatchContextBuilder.seedOf 92UL id) |> Map.ofList
                  Expect.equal (seedMap fixtures) (seedMap (List.rev fixtures)) "parallel order has no sporting effect"
                  let attempts = [ 1UL .. 10000UL ] |> List.map (fun seed -> MatchContextBuilder.seedOf seed 14)
                  Expect.equal (Set.ofList attempts |> Set.count) attempts.Length "repeated attempts have independent streams"

              testCase "rerolling a pending fixture changes its accepted input without changing the world"
              <| fun _ ->
                  let generated = generate 0xC02UL []
                  let matchId, fixture =
                      generated.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                      |> Seq.find (fun (_, fixture) -> MatchFixture.involves generated.UserClubId fixture)
                  let state = { generated with CurrentDate = fixture.ScheduledDate.Date }
                  let clock = WorldClockOps.init state.Season
                  let prepare state =
                      WorldRunner.prepareUserMatchJobWithBalance BalanceConfig.defaultConfig clock state matchId
                      |> function
                          | Ok job -> job
                          | Error error -> failtest error
                  let first = prepare state
                  let repeated = prepare state
                  let rerolled = prepare { state with ExecutionSeed = state.ExecutionSeed + 1UL }
                  Expect.equal first repeated "worker slices reuse the same execution"
                  Expect.notEqual first.Input.Seed rerolled.Input.Seed "new attempts reroll the stream"
                  Expect.notEqual first.InputHash rerolled.InputHash "old results cannot masquerade as the new attempt"
                  Expect.equal first.Input.Context rerolled.Input.Context "rerolling cannot alter football inputs"

              testCase "the explicit start date is preserved"
              <| fun _ ->
                  let state = generate 0xC02UL []
                  Expect.equal state.CurrentDate startDate "generation uses the supplied date"
                  Expect.equal state.Season startDate.Year "season comes from the supplied date year"

              testCase "a missing primary country returns diagnostics without a state"
              <| fun _ ->
                  match WorldGen.generateNewGame resolved 0xC02UL startDate "MISSING" "Test Manager" [] with
                  | Ok state -> failtestf "generation unexpectedly produced a state: %A" state
                  | Error error ->
                      Expect.isTrue
                          (error.Diagnostics
                           |> List.exists (fun diagnostic -> diagnostic.Code = "WORLD-COUNTRY-MISSING"))
                          "the missing primary country is explicit"

              testCase "a missing secondary country is an explicit error"
              <| fun _ ->
                  match WorldGen.generateNewGame resolved 0xC02UL startDate "TST" "Test Manager" [ "MISSING" ] with
                  | Ok state -> failtestf "generation unexpectedly produced a state: %A" state
                  | Error error ->
                      Expect.isTrue
                          (error.Diagnostics
                           |> List.exists (fun diagnostic -> diagnostic.Code = "WORLD-SECONDARY-COUNTRY-MISSING"))
                          "the missing secondary country is not silently discarded"

              testCase "generated match context contains eligible starters and a real bench"
              <| fun _ ->
                  let state = generate 0xC02UL []

                  let fixtureId, fixture =
                      state.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                      |> Seq.head

                  let ready = LineupOps.ensureForFixtures [ fixtureId, fixture ] state

                  match MatchContextBuilder.build ready fixture BalanceConfig.defaultConfig with
                  | None -> failtest "generated fixture could not cross the match boundary"
                  | Some context ->
                      Expect.equal context.HomeLineup.StarterCount 11 "home has eleven starters"
                      Expect.equal context.AwayLineup.StarterCount 11 "away has eleven starters"
                      Expect.isGreaterThan context.HomeLineup.SlotCount 11 "home has a real bench"
                      Expect.isGreaterThan context.AwayLineup.SlotCount 11 "away has a real bench"
                      Expect.hasLength context.HomeParticipants.Starters 11 "home boundary exposes eleven starters"
                      Expect.hasLength context.AwayParticipants.Starters 11 "away boundary exposes eleven starters"

                      Expect.isTrue
                          (context.HomeParticipants.Substitutes |> Array.forall _.Eligible)
                          "home substitutes are eligible"

                      Expect.isTrue
                          (context.AwayParticipants.Substitutes |> Array.forall _.Eligible)
                          "away substitutes are eligible"

              // Generated ratings are 1..20 while every engine formula reads 0..100; a raw copy made
              // generated squads play as if every rating were near zero and real matches had no shots.
              testCase "generated players reach the match engine on its 0..100 rating scale"
              <| fun _ ->
                  let state = generate 0xC02UL []

                  let fixtureId, fixture =
                      state.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                      |> Seq.head

                  let ready = LineupOps.ensureForFixtures [ fixtureId, fixture ] state

                  match MatchContextBuilder.build ready fixture BalanceConfig.defaultConfig with
                  | None -> failtest "generated fixture could not cross the match boundary"
                  | Some context ->
                      let starters (lineup: Lineup) = lineup.Profiles |> Array.take lineup.StarterCount
                      let profiles = Array.append (starters context.HomeLineup) (starters context.AwayLineup)

                      let ratings (profile: PlayerProfile) =
                          [ profile.Passing; profile.Finishing; profile.Dribbling; profile.Vision; profile.Composure ]

                      for profile in profiles do
                          for rating in ratings profile do
                              Expect.isTrue (rating >= 0.0f && rating <= 100.0f) $"rating {rating} stays on the engine scale"

                      let meanPassing = profiles |> Array.averageBy (fun p -> float p.Passing)
                      Expect.isGreaterThan meanPassing 25.0 "an average professional is not rated near zero"

                      let generatedPlayer = ready.Players |> Map.toSeq |> Seq.head |> snd
                      let projected = Projection.toProfile generatedPlayer

                      Expect.equal
                          projected.Passing
                          (float32 generatedPlayer.Technical.Passing * 5.0f)
                          "a 1..20 rating maps linearly onto 0..100"

              // The tactics board is drawn attacking up the screen; reading its width as pitch length put
              // goalkeepers on the halfway line and full-backs near opposite goals, unevenly per side.
              testCase "generated lineups enter the engine in the team's attacking frame"
              <| fun _ ->
                  let state = generate 0xC02UL []

                  let fixtureId, fixture =
                      state.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                      |> Seq.head

                  let ready = LineupOps.ensureForFixtures [ fixtureId, fixture ] state

                  match MatchContextBuilder.build ready fixture BalanceConfig.defaultConfig with
                  | None -> failtest "generated fixture could not cross the match boundary"
                  | Some context ->
                      for lineup in [ context.HomeLineup; context.AwayLineup ] do
                          let starters =
                              Array.init lineup.StarterCount (fun i -> lineup.Profiles[i].Role, lineup.BasePositions[i])

                          let keeperX =
                              starters |> Array.find (fun (role, _) -> role = SlotRole.Goalkeeper) |> snd |> _.X

                          let outfield = starters |> Array.filter (fun (role, _) -> role <> SlotRole.Goalkeeper)

                          Expect.isLessThan keeperX -35.0<meter> "the goalkeeper starts in front of his own goal"

                          Expect.isTrue
                              (outfield |> Array.forall (fun (_, p) -> p.X > keeperX))
                              "every outfield player starts ahead of the goalkeeper"

                          let mostAdvanced = outfield |> Array.maxBy (fun (_, p) -> p.X) |> fst

                          Expect.contains
                              [ SlotRole.Striker; SlotRole.AttackingMid; SlotRole.Winger ]
                              mostAdvanced
                              "an attacker leads the shape"

                          let meanDefenderX =
                              outfield
                              |> Array.filter (fun (role, _) -> role = SlotRole.Defender || role = SlotRole.FullBack)
                              |> Array.averageBy (fun (_, p) -> float p.X)

                          let meanAttackerX =
                              outfield
                              |> Array.filter (fun (role, _) -> role = SlotRole.Striker)
                              |> Array.averageBy (fun (_, p) -> float p.X)

                          Expect.isGreaterThan (meanAttackerX - meanDefenderX) 25.0 "the back line sits well behind the strikers"

                          Expect.isTrue
                              (starters |> Array.forall (fun (_, p) -> abs (float p.Y) <= float Pitch.HalfWidth))
                              "the shape spans the width, inside the touchlines"

              // Kits come from data mods; a club without one is drawn with a generated kit in the UI.
              testCase "a data mod kit reaches the generated club and a malformed one is rejected"
              <| fun _ ->
                  let withKit (kitJson: string) =
                      let json =
                          countryJson.Replace(
                              "{\"Name\":\"North FC\",\"LeagueLevel\":0,\"Reputation\":7000}",
                              "{\"Name\":\"North FC\",\"LeagueLevel\":0,\"Reputation\":7000,\"Kit\":" + kitJson + "}"
                          )

                      Expect.notEqual json countryJson "the fixture club was found in the country document"

                      ModResolution.resolve
                          [ { RelativePath = "test-world.femod"
                              Package =
                                { package with
                                    SportsContent = Map.add (ContentId "test.world:country/tst") json package.SportsContent } } ]

                  match withKit "{\"Primary\":\"#FFFFFF\",\"Secondary\":\"#E2353B\",\"Pattern\":\"Sash\"}" with
                  | Error error -> failtestf "a valid kit was rejected: %A" error
                  | Ok dataset ->
                      match WorldGen.generateNewGame dataset 0xC02UL startDate "TST" "Test Manager" [] with
                      | Error error -> failtestf "generation failed: %A" error
                      | Ok state ->
                          let north = state.Clubs |> Map.toSeq |> Seq.map snd |> Seq.find (fun club -> club.Name = "North FC")
                          let south = state.Clubs |> Map.toSeq |> Seq.map snd |> Seq.find (fun club -> club.Name = "South FC")

                          Expect.equal
                              north.Kit
                              (Some
                                  { Primary = "#FFFFFF"
                                    Secondary = "#E2353B"
                                    Pattern = Sash })
                              "the mod's kit is the club's kit"

                          Expect.isNone south.Kit "a club without a kit stays without one"

                  // Content is validated when the world is materialized from the resolved mods.
                  match withKit "{\"Primary\":\"red\",\"Secondary\":\"#E2353B\",\"Pattern\":\"Zigzag\"}" with
                  | Error error -> Expect.stringContains (sprintf "%A" error) "MOD-CONTENT-VALUE" "colour and pattern are validated"
                  | Ok dataset ->
                      match WorldGen.generateNewGame dataset 0xC02UL startDate "TST" "Test Manager" [] with
                      | Ok _ -> failtest "a malformed kit must not reach a career"
                      | Error error ->
                          let text = sprintf "%A" error
                          Expect.stringContains text "MOD-CONTENT-VALUE" "colour and pattern are validated"
                          Expect.stringContains text "/Kit/Primary" "the diagnostic points at the bad colour"

              testCase "applying one fixture outcome twice is idempotent"
              <| fun _ ->
                  let state = generate 0xC02UL []

                  let fixtureId, fixture =
                      state.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
                      |> Seq.head

                  let outcome: MatchOutcome.FixtureOutcome =
                      { FixtureId = fixtureId
                        Fixture =
                          { fixture with
                              Played = true
                              HomeScore = Some 1
                              AwayScore = Some 0 }
                        HomeScore = 1
                        AwayScore = 0
                        InjuredPlayers = Set.empty
                        HomePlayerStats = [||]
                        AwayPlayerStats = [||] }

                  let fixtureToComp = MatchOutcome.fixtureToCompMap state
                  let once = MatchOutcome.applyOutcomes BalanceConfig.defaultConfig.CollectiveAttack fixtureToComp [| outcome |] state
                  let twice = MatchOutcome.applyOutcomes BalanceConfig.defaultConfig.CollectiveAttack fixtureToComp [| outcome |] once

                  Expect.equal twice once "standings, morale and fixture state are not applied twice"

              testCase "a completed user job commits exactly once"
              <| fun _ ->
                  let state, clock, result = completedUserJob ()

                  match WorldRunner.commitUserMatchJobWithBalance BalanceConfig.defaultConfig clock state result with
                  | Error error -> failtestf "valid result was rejected: %s" error
                  | Ok committed ->
                      Expect.hasLength committed.DayResult.PlayedMatches 1 "one fixture is committed"
                      let cfg = BalanceConfig.defaultConfig.CollectiveAttack
                      for side, club in [ ClubSide.Home, result.Input.Context.HomeClub; ClubSide.Away, result.Input.Context.AwayClub ] do
                          let expected = AttackLearning.applyEvents cfg side club.AttackPrior result.Events
                          Expect.equal committed.DayResult.GameState.Clubs[int club.Id].CoordinationMemory.AttackLearning expected "accepted actual match evidence is merged without multiplying kickoff priors"
                      Expect.isNonEmpty (AttackLearning.fromEvents cfg ClubSide.Home result.Events) "actual simulation produces functional adaptive evidence"

                      match
                          WorldRunner.commitUserMatchJobWithBalance
                              BalanceConfig.defaultConfig
                              committed.DayResult.WorldClock
                              committed.DayResult.GameState
                              result
                      with
                      | Error error -> Expect.equal error "Match result was already committed" "replay is rejected"
                       | Ok _ -> failtest "the same result was committed twice"

              testCase "recorded replay owns the original seed football input and managerial commands"
              <| fun _ ->
                  let state, clock, result = completedUserJob ()
                  let committed =
                      WorldRunner.commitUserMatchJobWithBalance BalanceConfig.defaultConfig clock state result
                      |> function
                          | Ok committed -> committed.DayResult
                          | Error error -> failtest error
                  Expect.equal committed.GameState.MatchSeeds[result.MatchId] result.Input.Seed "the actual seed is retained"
                  let changed =
                      { committed.GameState with
                          ExecutionSeed = 999UL
                          Players = Map.empty
                          Staff = Map.empty
                          CurrentDate = committed.GameState.CurrentDate.AddDays(20.0) }
                  let job =
                      WorldRunner.prepareReplayJob committed.WorldClock changed result.MatchId
                      |> function
                          | Ok job -> job
                          | Error error -> failtest error
                  Expect.equal job.Input result.Input "replay never rebuilds from today's career"
                  let session =
                      MatchSession.create Scheduler.systems job.Input (MatchRunMode.Replay 40<tickDelta>)
                      |> function
                          | Ok session -> session
                          | Error error -> failtestf "%A" error
                  let _, commands = committed.WorldClock.MatchExecutions[result.MatchId]
                  Expect.equal commands result.Commands "accepted managerial actions are retained"
                  for command in commands do
                      MatchSession.receive command session
                      |> function
                          | Ok() -> ()
                          | Error error -> failtestf "%A" error
                  let replay =
                      MatchSession.finish session
                      |> function
                          | Ok outcome -> outcome
                          | Error error -> failtestf "%A" error
                  Expect.equal replay.EventsHash result.EventsHash "the replay reproduces every sporting event"
                  Expect.equal (replay.Final.State.HomeScore, replay.Final.State.AwayScore) (result.HomeScore, result.AwayScore) "the score follows that execution"
                  let nextSeason =
                      WorldRunner.advanceToNextSeasonWithBalance BalanceConfig.defaultConfig committed.WorldClock committed.GameState
                  Expect.equal nextSeason.NextSeasonClock.MatchExecutions committed.WorldClock.MatchExecutions "season rollover cannot forget recorded executions"
                  Expect.equal nextSeason.NextSeasonGs.MatchSeeds committed.GameState.MatchSeeds "completed seeds survive a new fixture schedule"
                  for KeyValue(id, club) in committed.GameState.Clubs do
                      let retained = AttackLearning.retain BalanceConfig.defaultConfig.CollectiveAttack club.CoordinationMemory.AttackLearning
                      Expect.equal nextSeason.NextSeasonGs.Clubs[id].CoordinationMemory.AttackLearning retained "season rollover decays actual bounded football memory"

              testCase "an unrecorded fixture cannot be presented as a recorded replay"
              <| fun _ ->
                  let state = generate 0xC02UL []
                  Expect.isError
                      (WorldRunner.prepareReplayJob (WorldClockOps.init state.Season) state 1)
                      "replays must not invent a new simulation"

              testCase "a user job rejects stale revisions, unknown fixtures and invalid hashes"
              <| fun _ ->
                  let state, clock, result = completedUserJob ()

                  let staleClock =
                      { clock with
                          Revision = clock.Revision + 1UL }

                  Expect.equal
                      (WorldRunner.commitUserMatchJobWithBalance BalanceConfig.defaultConfig staleClock state result)
                      (Error "Match result is stale for the current world revision")
                      "stale revision is rejected"

                  Expect.equal
                      (WorldRunner.commitUserMatchJobWithBalance
                          BalanceConfig.defaultConfig
                          clock
                          state
                          { result with MatchId = 999999 })
                      (Error "Match result references an unknown fixture")
                      "unknown fixture is rejected"

                  Expect.equal
                      (WorldRunner.commitUserMatchJobWithBalance
                          BalanceConfig.defaultConfig
                          clock
                          state
                          { result with
                              InputHash = result.InputHash + 1UL })
                      (Error "Match result input hash does not match the current fixture input")
                      "input hash is validated"

                  Expect.equal
                      (WorldRunner.commitUserMatchJobWithBalance
                          BalanceConfig.defaultConfig
                          clock
                          state
                          { result with
                              HomeScore = result.HomeScore + 1 })
                      (Error "Match result hash is invalid")
                      "result facts are validated"

              testCase "a generated world starts with pulse state and a quiet day publishes no news yet"
              <| fun _ ->
                  let world = generate 0xC02UL []
                  Expect.isEmpty world.News "new careers start without news"
                  Expect.equal world.NextNewsId 1 "ids start at one"

                  Expect.all
                      world.Clubs.Values
                      (fun club ->
                          club.BoardConfidence = WorldPulseConfig.defaultWorldPulse.InitialBoardConfidence
                          && club.FansMood = WorldPulseConfig.defaultWorldPulse.InitialFansMood
                          && club.RecentResults.IsEmpty
                          && club.ManagerSince = Some startDate)
                      "every club starts with configured pressure state"

                  Expect.all world.Players.Values (fun p -> p.Unrest.IsNone) "nobody starts unhappy"

                  match
                      WorldScheduler.executeDayWithBalance (
                          BalanceConfig.defaultConfig,
                          RunThroughMatches,
                          WorldClockOps.init world.Season,
                          world
                      )
                  with
                  | Error failure -> failtest failure.Message
                  | Ok day ->
                      Expect.isEmpty day.State.News "no detectors are registered in phase 1"
                      Expect.equal day.State.NextNewsId 1 "no ids are consumed" ]
