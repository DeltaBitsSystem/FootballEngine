namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.World.Phases
open FootballEngine.World
open FootballEngine.World.Agents
open FootballEngine.World.Supervisors

/// Honest contract tests for the weekly training triangle (develop / freshness /
/// risk). Randomness comes from a shared global stream with no seed control, so
/// every roll-dependent test pins chances to 0.0 or 1.0 and asserts outcomes
/// that hold for any draw; pure helpers are asserted directly.
module TrainingTests =

    let private date = DateTime(2026, 8, 19)

    let private baseTraining =
        FootballEngine.Core.Match.BalanceConfig.defaultConfig.Training

    // All chances zeroed: the only deterministic motion left is batteries.
    let private quiet: FootballEngine.Core.Match.TrainingConfig =
        { baseTraining with
            DevelopChance = 0.0
            TrickleChance = 0.0
            MaintainChance = 0.0
            DeclineChance = 0.0
            DevelopInjuryAdd = 0.0
            BaseInjuryRisk = 0.0
            TiredInjuryScale = 0.0 }

    // Age multiplier above 1 at 18 turns DevelopChance 1.0 into a certain gain
    // whatever the shared stream draws.
    let private forcedGrowth: FootballEngine.Core.Match.TrainingConfig =
        { quiet with DevelopChance = 1.0 }

    let private basePhys =
        { Acceleration = 10
          Pace = 10
          Agility = 10
          Balance = 10
          JumpingReach = 10
          Stamina = 10
          Strength = 10 }

    let private baseTech =
        { Finishing = 10
          LongShots = 10
          Dribbling = 10
          BallControl = 10
          Passing = 10
          Crossing = 10
          Tackling = 10
          Marking = 10
          Heading = 10
          FreeKick = 10
          Penalty = 10 }

    let private baseMental =
        { Aggression = 10
          Composure = 10
          Vision = 10
          Positioning = 10
          Bravery = 10
          WorkRate = 10
          Concentration = 10
          Leadership = 10 }

    let private baseGk =
        { Reflexes = 5
          Handling = 5
          Kicking = 5
          OneOnOne = 5
          AerialReach = 5 }

    let private makePlayer
        (id: PlayerId)
        (pos: Position)
        (age: int)
        (plan: IndividualPlan)
        (condition: int)
        (fitness: int)
        (skill: int)
        (potential: int)
        (status: PlayerStatus)
        (affiliation: PlayerAffiliation)
        : Player =
        { Id = id
          Name = sprintf "Player %d" id
          Birthday = date.AddYears(-age)
          Nationality = "ES"
          Position = pos
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical = basePhys
          Technical = baseTech
          Mental = baseMental
          Goalkeeping = baseGk
          Condition = condition
          Fatigue = 0
          MatchFitness = fitness
          RecentLoad = 0
          Morale = 70
          Status = status
          CurrentSkill = skill
          PotentialSkill = potential
          Reputation = 40
          Affiliation = affiliation
          TrainingPlan = plan
          ExperienceModifiers = ExperienceModifiers.defaultModifiers
          Unrest = None
          BenchWeeks = 0
          UnrestWeeks = 0
          TransferListed = false
          SeasonStartSkill = 0 }

    let private contracted id pos age plan condition fitness skill potential =
        makePlayer
            id
            pos
            age
            plan
            condition
            fitness
            skill
            potential
            Available
            (Contracted(id * 100, { Salary = 10000m; ExpiryYear = 2030 }))

    let private makeCoach (id: StaffId) (clubId: ClubId) (lineup: Lineup option) : Staff =
        { Id = id
          Name = sprintf "Coach %d" id
          Nationality = "ES"
          Birthday = DateTime(1980, 1, 1)
          Role = HeadCoach
          Attributes =
            { Coaching =
                { Attacking = 10
                  Defending = 10
                  Fitness = 10
                  Goalkeeping = 10
                  Mental = 10
                  SetPieces = 10
                  Tactical = 10
                  Technical = 10
                  WorkingWithYoungsters = 10
                  PreferredFormation = None
                  Lineup = lineup }
              Scouting =
                { NetworkReach = 10
                  DataAnalysis = 10
                  MarketKnowledge = 10 }
              Medical =
                { Physiotherapy = 10
                  SportsScience = 10 }
              Analysis =
                { PerformanceAnalysis = 10
                  RecruitmentAnalysis = 10 } }
          Knowledge =
            { JudgingPlayerAbility = 10
              JudgingPlayerPotential = 10
              JudgingStaffAbility = 10
              Negotiating = 10
              TacticalKnowledge = 10 }
          Mental =
            { Adaptability = 10
              Determination = 10
              LevelOfDiscipline = 10
              PeopleManagement = 10
              Motivating = 10 }
          CurrentSkill = 60
          PotentialSkill = 60
          Badge = NoneBadge
          Reputation = 100
          Contract =
            Some
                { ClubId = clubId
                  Salary = 10000m
                  ExpiryYear = 2030 }
          Status = Active
          TrophiesWon = 0
          SeasonsManaged = 0 }

    let private makeClub (id: ClubId) (playerIds: PlayerId list) : Club =
        { Id = id
          Name = sprintf "Club %d" id
          Nationality = "ES"
          Reputation = 50
          PlayerIds = playerIds
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

    let private makeLineup (playerIds: PlayerId list) : Lineup =
        { Formation = F442
          Tactics = Balanced
          Instructions = None
          Slots =
            playerIds
            |> List.mapi (fun i pid ->
                { Index = i
                  Role = ST
                  X = 0.0
                  Y = 0.0
                  PlayerId = Some pid }) }

    let private playerStats (playerId: PlayerId) (minutes: int) : MatchOutcome.PlayerMatchStats =
        { PlayerId = playerId
          GoalsScored = 0
          AssistsGiven = 0
          MinutesPlayed = minutes
          YellowCards = 0
          RedCards = 0
          FoulsCommitted = 0 }

    let private makeGameState (clubs: Club list) (players: Player list) (staff: Staff list) : GameState =
        { CurrentDate = date
          Season = 2026
          TrainingWeeksApplied = 0
          Clubs = clubs |> List.map (fun c -> c.Id, c) |> Map.ofList
          Players = players |> List.map (fun p -> p.Id, p) |> Map.ofList
          Staff = staff |> List.map (fun s -> s.Id, s) |> Map.ofList
          Competitions = Map.empty
          Countries = Map.empty
          DataLock = TestDataLock.valid
          UserClubId = 1
          UserStaffId = 101
          PrimaryCountry = "ES"
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
          PulseQueues = PulseQueues.empty
          JobOffers = []
          NextJobOfferId = 1 }

    let private makeOutcome (homeId: ClubId) (awayId: ClubId) : MatchOutcome.FixtureOutcome =
        let fixture: MatchFixture =
            { Id = 1
              CompetitionId = 1
              Round = None
              HomeClubId = homeId
              AwayClubId = awayId
              ScheduledDate = date
              Played = true
              HomeScore = Some 1
              AwayScore = Some 0
              Events = [] }

        { FixtureId = 1
          Fixture = fixture
          HomeScore = 1
          AwayScore = 0
          InjuredPlayers = Set.empty
          HomePlayerStats =
            [| { PlayerId = homeId
                 GoalsScored = 0
                 AssistsGiven = 0
                 MinutesPlayed = 90
                 YellowCards = 0
                 RedCards = 0
                 FoulsCommitted = 0 } |]
          AwayPlayerStats =
            [| { PlayerId = awayId
                 GoalsScored = 0
                 AssistsGiven = 0
                 MinutesPlayed = 90
                 YellowCards = 0
                 RedCards = 0
                 FoulsCommitted = 0 } |] }

    let trainingTests =
        testList
            "FootballEngine.Core.Contracts.Training"
            [ test "growthChance is zero when the potential gap is closed" {
                  let chance =
                      TrainingPhaseEngine.growthChance baseTraining 18 None (Some(Develop Finishing)) 0 100

                  Expect.equal chance 0.0 "a finished player has nothing to learn"
              }

              test "growthChance is positive for a youngster with a gap on Develop" {
                  let chance =
                      TrainingPhaseEngine.growthChance baseTraining 18 None (Some(Develop Finishing)) 10 100

                  Expect.isTrue (chance > 0.0) "an 18-year-old with headroom must be able to grow"
              }

              test "tilt bonus applies only to the matching group" {
                  let cfg = baseTraining
                  let age = 20
                  let gap = 10
                  let cond = 100

                  let matching =
                      TrainingPhaseEngine.growthChance cfg age (Some Finishing) (Some(Develop Finishing)) gap cond

                  let otherGroup =
                      TrainingPhaseEngine.growthChance cfg age (Some Creation) (Some(Develop Finishing)) gap cond

                  let noTilt =
                      TrainingPhaseEngine.growthChance cfg age None (Some(Develop Finishing)) gap cond

                  Expect.floatClose
                      Accuracy.medium
                      (matching - noTilt)
                      cfg.TiltBonus
                      "the emphasis tilt is an additive bonus on its own group"

                  Expect.floatClose Accuracy.medium otherGroup noTilt "an unrelated emphasis changes nothing"
              }

              test "tired condition scales growth down" {
                  let cfg = baseTraining

                  let fresh =
                      TrainingPhaseEngine.growthChance
                          cfg
                          20
                          None
                          (Some(Develop Finishing))
                          10
                          cfg.TiredConditionThreshold

                  let tired =
                      TrainingPhaseEngine.growthChance
                          cfg
                          20
                          None
                          (Some(Develop Finishing))
                          10
                          (cfg.TiredConditionThreshold - 1)

                  Expect.isTrue (tired < fresh) "training exhausted legs teaches less"

                  Expect.floatClose
                      Accuracy.medium
                      (tired / fresh)
                      cfg.TiredGrowthPenalty
                      "the penalty below the threshold is exactly the configured factor"
              }

              test "rehab players grow nothing via trainPlayer" {
                  let injured =
                      { contracted 1 ST 18 (Develop Finishing) 100 90 70 80 with
                          Status = Injured(Minor, date.AddDays(7.0)) }

                  Expect.isTrue (TrainingPlans.isRehab injured) "precondition: the player counts as rehab"
                  Expect.equal (TrainingPlans.effectivePlan injured) None "precondition: rehab exposes no plan"

                  let trained = TrainingPhaseEngine.trainPlayer forcedGrowth date None false injured

                  Expect.equal trained.Technical.Finishing injured.Technical.Finishing "no finishing growth in rehab"
                  Expect.equal trained.Technical.LongShots injured.Technical.LongShots "no trickle growth in rehab"
                  Expect.equal trained.CurrentSkill injured.CurrentSkill "no CA movement in rehab"
                  Expect.equal trained.Status injured.Status "a quiet rehab week keeps the original injury"
              }

              test "trainPlayer grows the weakest attribute of the target group and adds one CA" {
                  let prospect =
                      { contracted 1 ST 18 (Develop Finishing) 100 90 70 80 with
                          Technical =
                              { baseTech with
                                  Finishing = 10
                                  LongShots = 14 } }

                  let trained = TrainingPhaseEngine.trainPlayer forcedGrowth date None false prospect

                  Expect.equal trained.Technical.Finishing 11 "the lagging attribute moves first"
                  Expect.equal trained.Technical.LongShots 14 "the stronger attribute waits its turn"
                  Expect.equal trained.CurrentSkill 71 "visible growth is priced into CA"
              }

              test "CA growth caps at potential" {
                  let capped =
                      { contracted 1 ST 18 (Develop Finishing) 100 90 79 80 with
                          Technical =
                              { baseTech with
                                  Finishing = 10
                                  LongShots = 14 } }

                  let trained = TrainingPhaseEngine.trainPlayer forcedGrowth date None false capped

                  Expect.equal trained.CurrentSkill 80 "CA stops at PA instead of overshooting to 81"
                  Expect.equal trained.Technical.Finishing 11 "the attribute itself still moves that week"
              }

              test "a player at full potential grows nothing further" {
                  // Consistent with growthChance returning 0 on a closed gap:
                  // the preview promises 0%, so the week must deliver 0.
                  let maxed =
                      { contracted 1 ST 18 (Develop Finishing) 100 90 80 80 with
                          Technical =
                              { baseTech with
                                  Finishing = 10
                                  LongShots = 14 } }

                  let trained = TrainingPhaseEngine.trainPlayer forcedGrowth date None false maxed

                  Expect.equal trained.Technical maxed.Technical "no attribute growth without headroom"
                  Expect.equal trained.CurrentSkill 80 "and CA stays parked at PA"
              }

              test "outfielders on Develop Goalkeeping grow no keeping attributes" {
                  let striker = contracted 1 ST 18 (Develop Goalkeeping) 100 90 70 80
                  let trained = TrainingPhaseEngine.trainPlayer forcedGrowth date None false striker

                  Expect.equal trained.Goalkeeping striker.Goalkeeping "keeping drills do not take on outfielders"
                  Expect.equal trained.CurrentSkill striker.CurrentSkill "no phantom CA without growth"
              }

              test "batteryDeltas signs follow the plan" {
                  let cfg = baseTraining

                  let devCond, devFit =
                      TrainingPhaseEngine.batteryDeltas cfg (Some(Develop Finishing))

                  Expect.equal
                      (devCond, devFit)
                      (-cfg.DevelopConditionCost, cfg.DevelopFitnessGain)
                      "develop spends legs, builds sharpness"

                  let restCond, restFit = TrainingPhaseEngine.batteryDeltas cfg (Some Rest)

                  Expect.equal
                      (restCond, restFit)
                      (cfg.RestConditionGain, -cfg.RestFitnessLoss)
                      "rest restores legs, dulls sharpness"

                  let rehabCond, rehabFit = TrainingPhaseEngine.batteryDeltas cfg None
                  Expect.equal (rehabCond, rehabFit) (cfg.RehabConditionGain, 0) "rehab is gentle recovery only"
              }

              test "batteries clamp to [0,100] via trainPlayer" {
                  let drained = contracted 1 ST 25 (Develop Finishing) 3 99 70 70
                  let trainedDrained = TrainingPhaseEngine.trainPlayer quiet date None false drained

                  Expect.equal trainedDrained.Condition 0 "condition floors at zero instead of going negative"
                  Expect.equal trainedDrained.MatchFitness 100 "fitness ceilings at a hundred"

                  let rested = contracted 2 ST 25 Rest 95 1 70 70
                  let trainedRested = TrainingPhaseEngine.trainPlayer quiet date None false rested

                  Expect.equal trainedRested.Condition 100 "a nearly fresh player cannot over-recover"
                  Expect.equal trainedRested.MatchFitness 0 "idleness cannot push fitness below zero"
              }

              test "injuryChance rises as condition falls" {
                  let cfg = baseTraining
                  let fresh = TrainingPhaseEngine.injuryChance cfg 0.0 100
                  let mid = TrainingPhaseEngine.injuryChance cfg 0.0 50
                  let shot = TrainingPhaseEngine.injuryChance cfg 0.0 0

                  Expect.isTrue (fresh < mid && mid < shot) "exhaustion must monotonically raise training risk"
              }

              test "trainPlayer with certain risk injures Minor for seven days" {
                  let cfg = { quiet with BaseInjuryRisk = 1.0 }
                  let player = contracted 1 ST 24 Perform 90 90 70 70
                  let trained = TrainingPhaseEngine.trainPlayer cfg date None false player

                  Expect.equal
                      trained.Status
                      (Injured(Minor, date.AddDays(7.0)))
                      "a certain training injury is Minor, one week"
              }

              test "trainPlayer with zero risk and full condition does not injure" {
                  let player = contracted 1 ST 24 (Develop Finishing) 100 90 70 70
                  let trained = TrainingPhaseEngine.trainPlayer quiet date None false player

                  Expect.equal trained.Status Available "a fresh player on a safe week stays available"
              }

              test "veterans decline Pace or Stamina by id parity at certain chance" {
                  let cfg = { quiet with DeclineChance = 1.0 }

                  let even =
                      { contracted 2 ST 35 Perform 90 90 70 70 with
                          Physical =
                              { basePhys with
                                  Pace = 10
                                  Stamina = 10 } }

                  let trainedEven = TrainingPhaseEngine.trainPlayer cfg date None false even

                  Expect.equal trainedEven.Physical.Pace 9 "even ids wear at the legs first"
                  Expect.equal trainedEven.Physical.Stamina 10 "the other attribute is spared that week"
                  Expect.equal trainedEven.CurrentSkill 69 "physical decay is priced into CA"

                  let odd =
                      { contracted 3 ST 35 Perform 90 90 70 70 with
                          Physical =
                              { basePhys with
                                  Pace = 10
                                  Stamina = 10 } }

                  let trainedOdd = TrainingPhaseEngine.trainPlayer cfg date None false odd

                  Expect.equal trainedOdd.Physical.Stamina 9 "odd ids wear at the lungs first"
                  Expect.equal trainedOdd.Physical.Pace 10 "the other attribute is spared that week"
              }

              test "decline never drops attributes below 1" {
                  let cfg = { quiet with DeclineChance = 1.0 }

                  let floored =
                      { contracted 2 ST 35 Perform 90 90 70 70 with
                          Physical = { basePhys with Pace = 1; Stamina = 1 } }

                  let trained = TrainingPhaseEngine.trainPlayer cfg date None false floored

                  Expect.equal trained.Physical.Pace 1 "attributes floor at one, never zero"
                  Expect.isTrue (trained.Physical.Stamina >= 1) "the spared attribute stays in range"
              }

              test "young players never decline" {
                  let cfg = { quiet with DeclineChance = 1.0 }

                  let young =
                      { contracted 2 ST 20 Perform 90 90 70 70 with
                          Physical =
                              { basePhys with
                                  Pace = 10
                                  Stamina = 10 } }

                  let trained = TrainingPhaseEngine.trainPlayer cfg date None false young

                  Expect.equal trained.Physical young.Physical "a 20-year-old keeps every physical point"
                  Expect.equal trained.CurrentSkill young.CurrentSkill "and keeps every CA point"
              }

              test "withReadiness scales only physicals" {
                  let profile = TestBuilders.makeProfile 1 FootballEngine.Core.Match.SlotRole.Striker
                  let halved = FootballEngine.Core.Match.Projection.withReadiness 0.5f profile

                  Expect.floatClose Accuracy.medium (float halved.Pace) (float profile.Pace * 0.5) "pace halves"

                  Expect.floatClose
                      Accuracy.medium
                      (float halved.Strength)
                      (float profile.Strength * 0.5)
                      "strength halves"

                  Expect.floatClose
                      Accuracy.medium
                      (float halved.Stamina)
                      (float profile.Stamina * 0.5)
                      "stamina halves"

                  Expect.floatClose
                      Accuracy.medium
                      (float halved.Agility)
                      (float profile.Agility * 0.5)
                      "agility halves"

                  Expect.equal halved.Vision profile.Vision "a tired genius still sees the pass"
                  Expect.equal halved.Finishing profile.Finishing "and still finishes"
                  Expect.equal halved.Personality profile.Personality "character is untouched by fatigue"
              }

              test "withReadiness clamps its input to [0,1]" {
                  let profile = TestBuilders.makeProfile 1 FootballEngine.Core.Match.SlotRole.Striker

                  Expect.equal
                      (FootballEngine.Core.Match.Projection.withReadiness 2.0f profile)
                      (FootballEngine.Core.Match.Projection.withReadiness 1.0f profile)
                      "readiness above one is full readiness"

                  Expect.equal
                      (FootballEngine.Core.Match.Projection.withReadiness 1.0f profile)
                      profile
                      "full readiness is the identity"

                  let emptied = FootballEngine.Core.Match.Projection.withReadiness -0.5f profile
                  Expect.equal emptied.Pace 0.0f "negative readiness pins pace at zero"
                  Expect.equal emptied.Stamina 0.0f "and stamina with it"
                  Expect.equal emptied.Vision profile.Vision "while vision still ignores it"
              }

              test "applyPostMatchLoad taxes lineups and spares the uninvolved" {
                  let cfg = baseTraining

                  let starter =
                      { contracted 1 ST 24 Perform 80 70 70 70 with
                          Affiliation = Contracted(1, { Salary = 10000m; ExpiryYear = 2030 }) }

                  let bench =
                      { contracted 2 MC 24 Perform 80 70 70 70 with
                          Affiliation = Contracted(1, { Salary = 10000m; ExpiryYear = 2030 }) }

                  let rival =
                      { contracted 3 ST 24 Perform 80 70 70 70 with
                          Affiliation = Contracted(2, { Salary = 10000m; ExpiryYear = 2030 }) }

                  let bystander =
                      { contracted 4 ST 24 Perform 80 70 70 70 with
                          Affiliation = Contracted(3, { Salary = 10000m; ExpiryYear = 2030 }) }

                  let gs =
                      makeGameState
                          [ makeClub 1 [ 1; 2 ]; makeClub 2 [ 3 ]; makeClub 3 [ 4 ] ]
                          [ starter; bench; rival; bystander ]
                          [ makeCoach 101 1 (Some(makeLineup [ 1 ]))
                            makeCoach 102 2 (Some(makeLineup [ 3 ])) ]

                  let outcome =
                      { makeOutcome 1 2 with
                          AwayPlayerStats = [| playerStats 3 90 |] }

                  let after = MatchOutcome.applyPostMatchLoad cfg [| outcome |] gs
                  let played = after.Players[1]
                  let idle = after.Players[2]
                  let awayPlayed = after.Players[3]

                  Expect.equal played.Condition (80 - cfg.PostMatchConditionCost) "ninety minutes cost legs"
                  Expect.equal played.MatchFitness (70 + cfg.PostMatchFitnessGain) "and build sharpness"
                  Expect.equal idle.Condition 80 "the bench keeps its legs"
                  Expect.equal idle.MatchFitness (70 - cfg.IdleFitnessLoss) "but loses rhythm"
                  Expect.equal awayPlayed.Condition (80 - cfg.PostMatchConditionCost) "away starters pay the same price"
                  Expect.equal after.Players[4] bystander "clubs outside the fixture are untouched"
              }

              test "applyPostMatchLoad with no outcomes changes nothing" {
                  let player = contracted 1 ST 24 Perform 80 70 70 70

                  let gs =
                      makeGameState [ makeClub 1 [ 1 ] ] [ player ] [ makeCoach 101 1 (Some(makeLineup [ 1 ])) ]

                  Expect.equal
                      (MatchOutcome.applyPostMatchLoad baseTraining [||] gs)
                      gs
                      "an empty weekend costs nobody anything"
              }

              test "applyPostMatchLoad charges actual participants once" {
                  let starter =
                      { contracted 1 ST 24 Perform 80 70 70 70 with
                          Affiliation = Contracted(1, { Salary = 10000m; ExpiryYear = 2030 }) }

                  let substitute =
                      { contracted 2 ST 24 Perform 80 70 70 70 with
                          Affiliation = Contracted(1, { Salary = 10000m; ExpiryYear = 2030 }) }

                  let gs =
                      makeGameState
                          [ makeClub 1 [ 1; 2 ] ]
                          [ starter; substitute ]
                          [ makeCoach 101 1 (Some(makeLineup [ 1 ])) ]

                  let baseOutcome = makeOutcome 1 2

                  let outcome =
                      { baseOutcome with
                          HomePlayerStats = [| playerStats 1 0; playerStats 2 35 |]
                          AwayPlayerStats = [||] }

                  let after = MatchOutcome.applyPostMatchLoad baseTraining [| outcome |] gs

                  Expect.equal after.Players[1].Condition 80 "an unused saved starter is not charged"

                  Expect.equal
                      after.Players[1].MatchFitness
                      (70 - baseTraining.IdleFitnessLoss)
                      "the unused player loses rhythm"

                  Expect.equal
                      after.Players[2].Condition
                      (80 - baseTraining.PostMatchConditionCost)
                      "the actual substitute is charged"

                  Expect.equal
                      after.Players[2].MatchFitness
                      (70 + baseTraining.PostMatchFitnessGain)
                      "the actual substitute gains sharpness"

                  let twice = MatchOutcome.applyPostMatchLoad baseTraining [| outcome; outcome |] gs
                  Expect.equal twice after "duplicate outcome is applied once"
              }

              test "applyWeekly counts the week and touches only contracted players" {
                  let pro =
                      { contracted 1 ST 24 Rest 50 90 70 70 with
                          TrainingPlan = Rest }

                  let drifter = makePlayer 2 ST 24 Rest 50 90 70 70 Available FreeAgent

                  let gs =
                      makeGameState [ makeClub 1 [ 1 ] ] [ pro; drifter ] [ makeCoach 101 1 None ]

                  let after = TrainingPhaseEngine.applyWeekly quiet date gs

                  Expect.equal after.TrainingWeeksApplied 1 "exactly one week is booked"
                  Expect.equal after.Players[2] drifter "a free agent answers to no training ground"
                  Expect.equal after.Players[1].Condition (50 + quiet.RestConditionGain) "the pro recovers on schedule"
                  Expect.equal after.Players[1].MatchFitness (90 - quiet.RestFitnessLoss) "while losing a little edge"
              }

              test "training rolls depend on the career, not on the thread or on earlier draws" {
                  // Coin-flip odds make every player's growth and injury rolls visible in the result.
                  let coinFlip =
                      { baseTraining with
                          DevelopChance = 0.5
                          DeclineChance = 0.5
                          BaseInjuryRisk = 0.3 }

                  let squad =
                      [ for id in 1..24 -> contracted id ST (18 + id) (Develop Physical) 70 80 60 90 ]

                  let gs =
                      { makeGameState [ makeClub 1 [ 1..24 ] ] squad [ makeCoach 101 1 None ] with
                          ExecutionSeed = 0xC0FFEEUL }

                  let weekly () =
                      TrainingPhaseEngine.applyWeekly coinFlip date gs

                  let bulk () =
                      TrainingPhaseEngine.applyBulk coinFlip date 6 gs

                  let first = weekly (), bulk ()

                  // Draws made elsewhere advance the unseeded fallback generator; they must not leak into training.
                  for _ in 1..17 do
                      FootballEngine.RNG.roll () |> ignore

                  let again = weekly (), bulk ()

                  let otherThread =
                      System.Threading.Tasks.Task.Run(fun () -> weekly (), bulk ()).Result

                  Expect.equal again first "the same career trains the same way after unrelated draws"
                  Expect.equal otherThread first "and on another thread"

                  let reseeded =
                      TrainingPhaseEngine.applyWeekly coinFlip date { gs with ExecutionSeed = 0xBEEFUL }

                  Expect.notEqual reseeded.Players (fst first).Players "a different career seed rolls differently"
              }

              test "validateTraining accepts defaults and rejects bad chances and thresholds" {
                  Expect.isEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining baseTraining)
                      "the shipped defaults must be valid"

                  let negative =
                      { baseTraining with
                          DevelopChance = -0.1 }

                  Expect.isNonEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining negative)
                      "a negative chance must be rejected"

                  let overOne =
                      { baseTraining with
                          BaseInjuryRisk = 1.5 }

                  Expect.isNonEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining overOne)
                      "a risk above one must be rejected"

                  let earlyDecline =
                      { baseTraining with
                          DeclineStartAge = 10 }

                  Expect.isNonEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining earlyDecline)
                      "decline starting in childhood must be rejected"

                  let badThreshold =
                      { baseTraining with
                          TiredConditionThreshold = 101 }

                  Expect.isNonEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining badThreshold)
                      "a tiredness threshold outside [0,100] must be rejected"
              }

              test "preview battery deltas match trainPlayer" {
                  let cfg = baseTraining
                  let player = contracted 1 ST 19 (Develop Finishing) 80 80 70 80

                  let preview = TrainingPhaseEngine.preview cfg date None false player
                  let trained = TrainingPhaseEngine.trainPlayer cfg date None false player

                  Expect.equal
                      preview.ConditionDelta
                      (trained.Condition - player.Condition)
                      "the UI preview and the week agree on legs"

                  Expect.equal preview.FitnessDelta (trained.MatchFitness - player.MatchFitness) "and on sharpness"
                  Expect.equal preview.Target "Finishing" "the preview names the targeted group"

                  let previewRec = TrainingPhaseEngine.preview cfg date None true player
                  let trainedRec = TrainingPhaseEngine.trainPlayer cfg date None true player

                  Expect.equal
                      previewRec.ConditionDelta
                      (trainedRec.Condition - player.Condition)
                      "the recovery-week bonus is previewed exactly"

                  Expect.equal
                      previewRec.FitnessDelta
                      (trainedRec.MatchFitness - player.MatchFitness)
                      "recovery never touches sharpness"
              }

              test "validateTraining rejects bad rehab and develop-risk values" {
                  let badRehab =
                      { baseTraining with
                          RehabConditionGain = -5 }

                  Expect.isNonEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining badRehab)
                      "a negative rehab gain would drain recovering players with no diagnostic"

                  let badRisk =
                      { baseTraining with
                          DevelopInjuryAdd = -0.5 }

                  Expect.isNonEmpty
                      (FootballEngine.Core.Match.ConfigValidation.validateTraining badRisk)
                      "a negative develop-risk add would silently discount injuries"
              }

              test "coachingOf takes the best rating per area and neutral defaults" {
                  let low =
                      { makeCoach 101 1 None with
                          Attributes =
                              { (makeCoach 101 1 None).Attributes with
                                  Coaching =
                                      { (makeCoach 101 1 None).Attributes.Coaching with
                                          Attacking = 8 } } }

                  let high =
                      { makeCoach 102 1 None with
                          Attributes =
                              { (makeCoach 102 1 None).Attributes with
                                  Coaching =
                                      { (makeCoach 102 1 None).Attributes.Coaching with
                                          Attacking = 16 } } }

                  let player = contracted 1 ST 20 (Develop Finishing) 90 90 70 90
                  let gs = makeGameState [ makeClub 1 [ 1 ] ] [ player ] [ low; high ]
                  let ratings = TrainingPhaseEngine.coachingOf gs 1

                  Expect.equal ratings.Attacking 16 "the best coach sets the standard"

                  let empty = makeGameState [ makeClub 1 [ 1 ] ] [ player ] []
                  let neutral = TrainingPhaseEngine.coachingOf empty 1

                  Expect.equal neutral.Attacking 10 "no staff means league-average coaching"
                  Expect.equal neutral.Physio 0 "no medical staff means no injury reduction"
              }

              test "coachGrowthMult is neutral at rating 10" {
                  let mult =
                      TrainingPhaseEngine.coachGrowthMult
                          baseTraining
                          19
                          (Some Finishing)
                          TrainingPhaseEngine.neutralCoaching

                  Expect.equal mult 1.0 "average staff neither helps nor hinders"
              }

              test "elite attacking coach raises finishing growth only" {
                  let elite =
                      { TrainingPhaseEngine.neutralCoaching with
                          Attacking = 20 }

                  let plain =
                      TrainingPhaseEngine.growthChance baseTraining 19 None (Some(Develop Finishing)) 10 100

                  let coached =
                      TrainingPhaseEngine.growthChanceWith baseTraining 19 None (Some(Develop Finishing)) 10 100 elite

                  Expect.floatClose
                      Accuracy.high
                      coached
                      (plain * (1.0 + baseTraining.CoachGrowthInfluence))
                      "a 20-rated coach adds the full influence"

                  let offTarget =
                      TrainingPhaseEngine.growthChanceWith baseTraining 19 None (Some(Develop Defending)) 10 100 elite

                  let offPlain =
                      TrainingPhaseEngine.growthChance baseTraining 19 None (Some(Develop Defending)) 10 100

                  Expect.floatClose Accuracy.high offTarget offPlain "attacking expertise does not teach tackling"
              }

              test "youth coach lifts teenagers but not veterans" {
                  let youth =
                      { TrainingPhaseEngine.neutralCoaching with
                          Youngsters = 20 }

                  let kid = TrainingPhaseEngine.coachGrowthMult baseTraining 18 (Some Physical) youth

                  let vet = TrainingPhaseEngine.coachGrowthMult baseTraining 30 (Some Physical) youth

                  Expect.isTrue (kid > 1.0) "good youth coaches accelerate development"
                  Expect.equal vet 1.0 "veterans are past coaching influence"
              }

              test "physio staff reduces but never negates injury chance" {
                  let exposed = TrainingPhaseEngine.injuryChanceWith baseTraining 0.0 40 20
                  let alone = TrainingPhaseEngine.injuryChanceWith baseTraining 0.0 40 0

                  Expect.isTrue (exposed < alone) "medical staff earns its wage bill"
                  Expect.isTrue (exposed >= 0.0) "risk reduction can never go negative"
              }

              test "previewWith neutral coaching matches plain preview" {
                  let player = contracted 1 ST 20 (Develop Finishing) 80 80 70 90
                  let a = TrainingPhaseEngine.preview baseTraining date None false player

                  let b =
                      TrainingPhaseEngine.previewWith
                          baseTraining
                          date
                          None
                          false
                          TrainingPhaseEngine.neutralCoaching
                          player

                  Expect.equal a b "neutral staff must not move the numbers the manager sees"
              }

              test "health transition separates training load from recovery" {
                  let trained = Training.healthTransition (Some(Develop Physical))
                  let rested = Training.healthTransition (Some Rest)

                  Expect.isGreaterThan trained.FatigueDelta 0 "development adds fatigue"
                  Expect.isGreaterThan trained.LoadDelta 0 "development adds recent load"
                  Expect.isLessThan rested.FatigueDelta 0 "rest recovers fatigue"
                  Expect.isLessThan rested.LoadDelta 0 "rest clears recent load"
              }

              test "youth supervisor does not duplicate a club cohort" {
                  let youth =
                      { contracted 1 ST 16 Rest 100 0 40 70 with
                          Birthday = DateTime(2010, 7, 1)
                          Affiliation = YouthProspect 1 }

                  let state = makeGameState [ makeClub 1 [ 1 ] ] [ youth ] []

                  let intent =
                      { AgentId = ClubAgentId 1
                        Priority = 1
                        Payload = GenerateYouthPlayer 1 }

                  let after = YouthSupervisor.resolve state [ intent ]
                  Expect.equal after.Players.Count state.Players.Count "one annual cohort is idempotent"
              } ]
