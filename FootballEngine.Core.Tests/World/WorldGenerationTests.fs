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
            ModSystem.resolve
                [ { RelativePath = "test-world.femod"
                    Package = package } ]
        with
        | Ok value -> value
        | Error error -> failwithf "test dataset did not resolve: %A" error

    let private startDate = DateTime(2034, 3, 17, 9, 30, 0, DateTimeKind.Utc)

    let private generate seed secondary =
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

        let outcome =
            match MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast with
            | Error error -> failtestf "user match session creation failed: %A" error
            | Ok session ->
                match MatchSession.finish session with
                | Ok value -> value
                | Error error -> failtestf "user match did not finish: %A" error

        state, clock, WorldRunner.matchJobResult job outcome

    let worldGenerationTests =
        testList
            "World generation"
            [ testCase "same resolved snapshot, seed and inputs produce the same state"
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
                  let once = MatchOutcome.applyOutcomes fixtureToComp [| outcome |] state
                  let twice = MatchOutcome.applyOutcomes fixtureToComp [| outcome |] once

                  Expect.equal twice once "standings, morale and fixture state are not applied twice"

              testCase "a completed user job commits exactly once"
              <| fun _ ->
                  let state, clock, result = completedUserJob ()

                  match WorldRunner.commitUserMatchJobWithBalance BalanceConfig.defaultConfig clock state result with
                  | Error error -> failtestf "valid result was rejected: %s" error
                  | Ok committed ->
                      Expect.hasLength committed.DayResult.PlayedMatches 1 "one fixture is committed"

                      match
                          WorldRunner.commitUserMatchJobWithBalance
                              BalanceConfig.defaultConfig
                              committed.DayResult.WorldClock
                              committed.DayResult.GameState
                              result
                      with
                      | Error error -> Expect.equal error "Match result was already committed" "replay is rejected"
                      | Ok _ -> failtest "the same result was committed twice"

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
                      "result facts are validated" ]
