namespace FootballEngine.Core.Tests.World

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.Core.Tests.Infrastructure

module TransferMarketTests =

    let private player =
        { Id = 1
          Name = "Reserved Player"
          Birthday = DateTime(2000, 1, 1)
          Nationality = "ARG"
          Position = ST
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical =
            { Acceleration = 70
              Pace = 70
              Agility = 70
              Balance = 70
              JumpingReach = 70
              Stamina = 70
              Strength = 70 }
          Technical =
            { Finishing = 70
              LongShots = 60
              Dribbling = 60
              BallControl = 60
              Passing = 60
              Crossing = 50
              Tackling = 30
              Marking = 30
              Heading = 60
              FreeKick = 40
              Penalty = 50 }
          Mental =
            { Aggression = 40
              Composure = 70
              Vision = 60
              Positioning = 60
              Bravery = 50
              WorkRate = 60
              Concentration = 60
              Leadership = 30 }
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
          Morale = 70
          Status = Available
          CurrentSkill = 70
          PotentialSkill = 75
          Reputation = 1000
          Affiliation = Contracted(1, { Salary = 1000m; ExpiryYear = 2028 })
          TrainingPlan = Perform
          ExperienceModifiers = ExperienceModifiers.defaultModifiers
          Unrest = None
          BenchWeeks = 0
          UnrestWeeks = 0
          TransferListed = false
          SeasonStartSkill = 0 }

    let private club id name players finance =
        { Id = id
          Name = name
          Nationality = "ARG"
          Reputation = 1000
          PlayerIds = players
          StaffIds = []
          Finance = finance
          Morale = 70
          BoardObjective = LeagueObjective MidTable
          CoordinationMemory = CoordinationMemory.defaultMemory
          ScoutingMemory = Map.empty
          Kit = None
          BoardConfidence = 60
          FansMood = 60
          RecentResults = []
          ManagerSince = None }

    let private state (operation: TransferOperation) (buyer: Club) (seller: Club) =
        { CurrentDate = DateTime(2026, 7, 1)
          Season = 2026
          TrainingWeeksApplied = 0
          Clubs = Map.ofList [ (1, seller); (2, buyer) ]
          Players = Map.ofList [ (player.Id, player) ]
          Staff = Map.empty
          Competitions = Map.empty
          Countries = Map.empty
          DataLock = TestDataLock.valid
          UserClubId = 2
          UserStaffId = 0
          PrimaryCountry = "ARG"
          Inbox = []
          NextInboxId = 1
          PendingNegotiations = Map.empty
          NextNegotiationId = 1
          Market =
            { MarketState.empty with
                Operations = Map.ofList [ (operation.Id, operation) ] }
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
          PulseQueues = PulseQueues.empty
          JobOffers = []
          NextJobOfferId = 1 }

    let tests =
        testList
            "TransferMarket"
            [ testCase "reserved offer completes once without duplicating"
              <| fun _ ->
                  let now = DateTime(2026, 7, 1)

                  let reservation =
                      { Id = "transfer:TransferOperationId 1"
                        Reason = "Confirmed transfer"
                        Amount = 10000m
                        CreatedOn = now }

                  let operation =
                      { Id = TransferOperationId 1
                        Player = 1
                        Buyer = 2
                        Seller = Some 1
                        Fee = 10000m
                        Salary = 1000m
                        ContractYears = 3
                        Role = Starter
                        MinutesPromised = 900
                        Status = Agreed
                        Reason = None
                        Created = now
                        Updated = now }

                  let buyer =
                      club
                          2
                          "Buyer"
                          []
                          { Finance.initial 100000m with
                              Reservations = [ reservation ] }

                  let seller = club 1 "Seller" [ 1 ] (Finance.initial 100000m)

                  let cfg =
                      { Window =
                          { TransferWindow.summer 2026 with
                              Opens = DateTime(2026, 1, 1)
                              Closes = DateTime(2026, 12, 31) }
                        RegistrationCloses = DateTime(2026, 12, 31)
                        AllowPreContracts = true }

                  let first =
                      TransferMarket.complete now operation.Id cfg (state operation buyer seller)

                  match first with
                  | Error reason -> failtestf "first completion failed: %A" reason
                  | Ok completed ->
                      Expect.equal completed.Market.Operations[operation.Id].Status Completed "operation completes"
                      Expect.isEmpty completed.Clubs[2].Finance.Reservations "reservation is consumed"
                      Expect.equal completed.Clubs[2].PlayerIds [ 1 ] "player is added once"

                      Expect.equal
                          completed.PulseQueues.Transfers
                          [ { Player = 1
                              From = Some 1
                              To = 2
                              Fee = 10000m } ]
                          "the pulse is told exactly once, with the real fee"

                      match TransferMarket.complete now operation.Id cfg completed with
                      | Ok _ -> failtest "completed offer must not complete twice"
                      | Error _ -> () ]
