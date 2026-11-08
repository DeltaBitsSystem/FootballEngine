namespace FootballEngine.Core.Tests.World

open System
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.Generation
open FootballEngine.World.Phases

module C07LongRunTests =

    let private rules promotion relegation =
        { PointsForWin = 3
          PointsForDraw = 1
          Tiebreakers = [ GoalDifference; GoalsScored ]
          Promotion = promotion
          Relegation = relegation }

    let private club id =
        { Id = id
          Name = $"Club {id}"
          Nationality = "TST"
          Reputation = 100
          PlayerIds = []
          StaffIds = []
          Finance = Finance.initial 1_000_000m
          Morale = 60
          BoardObjective = LeagueObjective MidTable
          CoordinationMemory = CoordinationMemory.defaultMemory
          ScoutingMemory = Map.empty
          Kit = None
          BoardConfidence = 60
          FansMood = 60
          RecentResults = []
          ManagerSince = None }

    let private competition id name level leagueRules clubs =
        let fixtures, _ = FixtureGen.forLeague id clubs (DateTime(2026, 8, 1)) (id * 100)

        { Id = id
          Name = name
          BroadcastIdentity = BroadcastIdentity.generated ($"competition:{id}")
          Type = NationalLeague(LeagueLevel level, leagueRules)
          Country = Some "TST"
          Season = 2026
          ClubIds = clubs
          Fixtures =
            fixtures
            |> List.map (fun f ->
                f.Id,
                { f with
                    Played = true
                    HomeScore = Some 1
                    AwayScore = Some 0 })
            |> Map.ofList
          Standings =
            clubs
            |> List.mapi (fun index clubId ->
                clubId,
                { ClubId = clubId
                  Played = 3
                  Won = 3
                  Drawn = 0
                  Lost = 0
                  GoalsFor = 12 - index * 3
                  GoalsAgainst = index
                  Points = 12 - index * 3 })
            |> Map.ofList
          KnockoutTies = Map.empty }

    let private state () =
        let topRules = rules [] [ PlayoffRelegation 1 ]
        let lowerRules = rules [ PlayoffPromotion 1 ] []

        { CurrentDate = DateTime(2026, 6, 30)
          Season = 2026
          TrainingWeeksApplied = 0
          Clubs = [ 1..8 ] |> List.map (fun id -> id, club id) |> Map.ofList
          Players = Map.empty
          Staff = Map.empty
          Competitions =
            Map.ofList
                [ 1, competition 1 "Premier" 0 topRules [ 1; 2; 3; 4 ]
                  2, competition 2 "Second" 1 lowerRules [ 5; 6; 7; 8 ] ]
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
          ExecutionSeed = 1UL
          MatchSeeds = Map.empty
          News = []
          NextNewsId = 1
          PulseProcessed = Set.empty
          PulseQueues = PulseQueues.empty }

    let private finishFixtures (gs: GameState) =
        { gs with
            Competitions =
                gs.Competitions
                |> Map.map (fun _ comp ->
                    { comp with
                        Fixtures =
                            comp.Fixtures
                            |> Map.map (fun _ f ->
                                { f with
                                    Played = true
                                    HomeScore = Some 1
                                    AwayScore = Some 0 })
                        Standings =
                            comp.ClubIds
                            |> List.mapi (fun index clubId ->
                                clubId,
                                { ClubId = clubId
                                  Played = max 1 (comp.ClubIds.Length - 1)
                                  Won = max 1 (comp.ClubIds.Length - 1)
                                  Drawn = 0
                                  Lost = 0
                                  GoalsFor = comp.ClubIds.Length * 3 - index
                                  GoalsAgainst = index
                                  Points = comp.ClubIds.Length * 3 - index })
                            |> Map.ofList }) }

    let private testPlayer =
        let physical =
            { Acceleration = 12
              Pace = 12
              Agility = 12
              Balance = 12
              JumpingReach = 12
              Stamina = 12
              Strength = 12 }

        let technical =
            { Finishing = 12
              LongShots = 12
              Dribbling = 12
              BallControl = 12
              Passing = 12
              Crossing = 12
              Tackling = 12
              Marking = 12
              Heading = 12
              FreeKick = 12
              Penalty = 12 }

        let mental =
            { Aggression = 12
              Composure = 12
              Vision = 12
              Positioning = 12
              Bravery = 12
              WorkRate = 12
              Concentration = 12
              Leadership = 12 }

        { Id = 900
          Name = "Convocable"
          Birthday = DateTime(1998, 1, 1)
          Nationality = "ARG"
          Position = ST
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical = physical
          Technical = technical
          Mental = mental
          Goalkeeping =
            { Reflexes = 1
              Handling = 1
              Kicking = 1
              OneOnOne = 1
              AerialReach = 1 }
          Condition = 100
          Fatigue = 0
          MatchFitness = 100
          RecentLoad = 0
          Morale = 80
          Status = Available
          CurrentSkill = 80
          PotentialSkill = 100
          Reputation = 400
          Affiliation = Contracted(1, { Salary = 1000m; ExpiryYear = 2030 })
          TrainingPlan = Perform
          ExperienceModifiers = ExperienceModifiers.defaultModifiers
          Unrest = None
          BenchWeeks = 0
          UnrestWeeks = 0
          TransferListed = false
          SeasonStartSkill = 0 }

    let c07Tests =
        testList
            "C07"
            [ testCase "playoff participants and outcomes follow final classification"
              <| fun _ ->
                  let comp = (state ()).Competitions[2]
                  let resolution = Competition.resolvePlayoffs comp |> Option.get
                  Expect.equal resolution.PromotionCandidates [ 5; 6 ] "classification selects the playoff pair"
                  Expect.equal resolution.Promoted [ 5 ] "higher ranked playoff candidate advances"

              testCase "callup is eligible, deterministic and emits the causal event"
              <| fun _ ->
                  let gs =
                      { state () with
                          Players = Map.ofList [ testPlayer.Id, testPlayer ] }

                  let callUps = Selection.select "ARG" (DateTime(2026, 10, 10)) 23 gs
                  Expect.equal callUps.Length 1 "available contracted player is selected"

                  Expect.equal
                      (Selection.toWorldEvents DateTime.Now callUps)
                      [ PlayerCalledUp(900, "ARG") ]
                      "selection is observable"

              testCase "ten season closures preserve history, places and continuity"
              <| fun _ ->
                  let balance = BalanceConfig.defaultConfig

                  let finalState =
                      [ 1..10 ]
                      |> List.fold
                          (fun current _ ->
                              let finished = finishFixtures current
                              SeasonPhase.runEndOfSeason balance finished |> finishFixtures)
                          (state ())

                  Expect.equal finalState.SeasonHistory.Length 10 "one immutable record per season"
                  Expect.equal finalState.Competitions.Count 2 "competition editions continue"

                  Expect.all
                      finalState.Competitions
                      (fun entry -> entry.Value.ClubIds.Length = 4)
                      "division size remains stable"

                  Expect.equal (finalState.Clubs.Count) 8 "club identities are preserved"
                  Expect.isGreaterThan finalState.Inbox.Length 0 "confirmed competition facts become news"

                  Expect.isTrue
                      (finalState.Competitions
                       |> Map.exists (fun _ c -> List.contains finalState.UserClubId c.ClubIds))
                      "manager club remains valid" ]
