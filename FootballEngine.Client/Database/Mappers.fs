namespace FootballEngine.Database

open System
open FootballEngine.Domain
open FootballEngine.Database.Serializers

module Mappers =

    let toInboxMessageEntity (gameSaveId: int) (msg: InboxMessage) : InboxMessageEntity =
        { Id = msg.Id
          GameSaveId = gameSaveId
          Date = msg.Date
          From = msg.From
          Subject = msg.Subject
          Body = msg.Body
          Category = inboxCategoryToString msg.Category
          IsRead = msg.IsRead
          RequiresAction = msg.RequiresAction
          ActionTaken =
            match msg.ActionTaken with
            | Some true -> System.Nullable<bool>(true)
            | Some false -> System.Nullable<bool>(false)
            | None -> System.Nullable<bool>() }

    let fromInboxMessageEntity (e: InboxMessageEntity) : InboxMessage =
        { Id = e.Id
          Date = e.Date
          From = e.From
          Subject = e.Subject
          Body = e.Body
          Category = parseInboxCategory e.Category
          IsRead = e.IsRead
          RequiresAction = e.RequiresAction
          ActionTaken =
            if e.ActionTaken.HasValue then
                Some e.ActionTaken.Value
            else
                None }

    let private affiliationFromEntity (e: PlayerEntity) : PlayerAffiliation =
        if e.ClubId <= 0 then
            FreeAgent
        else
            Contracted(
                e.ClubId,
                { Salary = e.Salary
                  ExpiryYear = e.ContractExpiry }
            )

    let private affiliationToEntity (affiliation: PlayerAffiliation) : int * decimal * int =
        match affiliation with
        | Contracted(clubId, contract) -> clubId, contract.Salary, contract.ExpiryYear
        | FreeAgent -> 0, 0m, 0
        | YouthProspect clubId -> clubId, 0m, 0
        | Retired -> 0, 0m, 0

    let toPlayerEntity (player: Player) : PlayerEntity =
        let statusType, statusInt, statusDate =
            match player.Status with
            | Available -> "Available", 0, DateTime.MinValue
            | Suspended matchesBanned -> "Suspended", matchesBanned, DateTime.MinValue
            | Injured(severity, until) ->
                let severityInt =
                    match severity with
                    | Minor -> 0
                    | Moderate -> 1
                    | Major -> 2
                    | Severe -> 3

                "Injured", severityInt, until

        let clubId, salary, contractExpiry = affiliationToEntity player.Affiliation

        { Id = player.Id
          ClubId = clubId
          Name = player.Name
          Birthday = player.Birthday
          Nationality = player.Nationality
          Position = $"%A{player.Position}"
          PreferredFoot = $"%A{player.PreferredFoot}"
          Height = player.Height
          Weight = player.Weight
          Acceleration = player.Physical.Acceleration
          Pace = player.Physical.Pace
          Agility = player.Physical.Agility
          Balance = player.Physical.Balance
          JumpingReach = player.Physical.JumpingReach
          Stamina = player.Physical.Stamina
          Strength = player.Physical.Strength
          Finishing = player.Technical.Finishing
          LongShots = player.Technical.LongShots
          Dribbling = player.Technical.Dribbling
          BallControl = player.Technical.BallControl
          Passing = player.Technical.Passing
          Crossing = player.Technical.Crossing
          Tackling = player.Technical.Tackling
          Marking = player.Technical.Marking
          Heading = player.Technical.Heading
          FreeKick = player.Technical.FreeKick
          Penalty = player.Technical.Penalty
          Aggression = player.Mental.Aggression
          Composure = player.Mental.Composure
          Vision = player.Mental.Vision
          Positioning = player.Mental.Positioning
          Bravery = player.Mental.Bravery
          WorkRate = player.Mental.WorkRate
          Concentration = player.Mental.Concentration
          Leadership = player.Mental.Leadership
          Reflexes = player.Goalkeeping.Reflexes
          Handling = player.Goalkeeping.Handling
          Kicking = player.Goalkeeping.Kicking
          OneOnOne = player.Goalkeeping.OneOnOne
          AerialReach = player.Goalkeeping.AerialReach
          Condition = player.Condition
          MatchFitness = player.MatchFitness
          Morale = player.Morale
          StatusType = statusType
          StatusParamInt = statusInt
          StatusParamDate = statusDate
          CurrentSkill = player.CurrentSkill
          PotentialSkill = player.PotentialSkill
          Reputation = player.Reputation
          Value = Player.playerValue player.CurrentSkill
          Salary = salary
          ContractExpiry = contractExpiry
          TrainingFocus = trainingFocusToString player.TrainingSchedule.Focus
          TrainingIntensity = trainingIntensityToString player.TrainingSchedule.Intensity
          BehavioralProfile = behavioralProfileToString (Player.profile player) }

    let toPlayerDomain (e: PlayerEntity) : Player =
        let status =
            match e.StatusType with
            | "Suspended" -> Suspended e.StatusParamInt
            | "Injured" ->
                let severity =
                    match e.StatusParamInt with
                    | 0 -> Minor
                    | 1 -> Moderate
                    | 2 -> Major
                    | _ -> Severe

                Injured(severity, e.StatusParamDate)
            | _ -> Available

        { Id = e.Id
          Name = e.Name
          Birthday = e.Birthday
          Nationality = e.Nationality
          Position = parsePosition e.Position
          PreferredFoot = parseFoot e.PreferredFoot
          Height = e.Height
          Weight = e.Weight
          Physical =
            { Acceleration = e.Acceleration
              Pace = e.Pace
              Agility = e.Agility
              Balance = e.Balance
              JumpingReach = e.JumpingReach
              Stamina = e.Stamina
              Strength = e.Strength }
          Technical =
            { Finishing = e.Finishing
              LongShots = e.LongShots
              Dribbling = e.Dribbling
              BallControl = e.BallControl
              Passing = e.Passing
              Crossing = e.Crossing
              Tackling = e.Tackling
              Marking = e.Marking
              Heading = e.Heading
              FreeKick = e.FreeKick
              Penalty = e.Penalty }
          Mental =
            { Aggression = e.Aggression
              Composure = e.Composure
              Vision = e.Vision
              Positioning = e.Positioning
              Bravery = e.Bravery
              WorkRate = e.WorkRate
              Concentration = e.Concentration
              Leadership = e.Leadership }
          Goalkeeping =
            { Reflexes = e.Reflexes
              Handling = e.Handling
              Kicking = e.Kicking
              OneOnOne = e.OneOnOne
              AerialReach = e.AerialReach }
          Condition = e.Condition
          MatchFitness = e.MatchFitness
          Morale = e.Morale
          Status = status
          CurrentSkill = e.CurrentSkill
          PotentialSkill = e.PotentialSkill
          Reputation = e.Reputation
          Affiliation = affiliationFromEntity e
          TrainingSchedule =
            { Focus = parseTrainingFocus e.TrainingFocus
              Intensity = parseTrainingIntensity e.TrainingIntensity }
          ExperienceModifiers = ExperienceModifiers.defaultModifiers }

    let toClubEntity (club: Club) : ClubEntity =
        { Id = club.Id
          Name = club.Name
          Nationality = club.Nationality
          Reputation = club.Reputation
          Budget = club.Budget
          Morale = club.Morale
          BoardObjective = boardObjectiveToString club.BoardObjective }

    let toClubDomain (players: Map<PlayerId, Player>) (staff: Map<StaffId, Staff>) (ce: ClubEntity) : ClubId * Club =
        let playerIds =
            players
            |> Map.values
            |> Seq.choose (fun p ->
                match p.Affiliation with
                | Contracted(clubId, _) when clubId = ce.Id -> Some p.Id
                | YouthProspect clubId when clubId = ce.Id -> Some p.Id
                | _ -> None)
            |> List.ofSeq

        let staffIds =
            staff
            |> Map.values
            |> Seq.choose (fun s ->
                match s.Contract with
                | Some c when c.ClubId = ce.Id -> Some s.Id
                | _ -> None)
            |> List.ofSeq

        ce.Id,
        { Id = ce.Id
          Name = ce.Name
          Nationality = ce.Nationality
          Reputation = ce.Reputation
          PlayerIds = playerIds
          StaffIds = staffIds
          Budget = ce.Budget
          Morale = ce.Morale
          BoardObjective = parseBoardObjective ce.BoardObjective }

    let toStaffEntity (staff: Staff) : StaffEntity =
        let contractClubId, contractSalary, contractExpiry =
            match staff.Contract with
            | Some contract -> contract.ClubId, contract.Salary, contract.ExpiryYear
            | None -> 0, 0m, 0

        { Id = staff.Id
          Name = staff.Name
          Nationality = staff.Nationality
          Birthday = staff.Birthday
          Role = staffRoleToString staff.Role
          Badge = coachingBadgeToString staff.Badge
          Reputation = staff.Reputation
          Status = staffStatusToString staff.Status
          TrophiesWon = staff.TrophiesWon
          SeasonsManaged = staff.SeasonsManaged
          ContractClubId = contractClubId
          ContractSalary = contractSalary
          ContractExpiry = contractExpiry
          CoachAttacking = staff.Attributes.Coaching.Attacking
          CoachDefending = staff.Attributes.Coaching.Defending
          CoachFitness = staff.Attributes.Coaching.Fitness
          CoachGoalkeeping = staff.Attributes.Coaching.Goalkeeping
          CoachMental = staff.Attributes.Coaching.Mental
          CoachSetPieces = staff.Attributes.Coaching.SetPieces
          CoachTactical = staff.Attributes.Coaching.Tactical
          CoachTechnical = staff.Attributes.Coaching.Technical
          CoachWorkingWithYoungsters = staff.Attributes.Coaching.WorkingWithYoungsters
          ScoutJudgingAbility = staff.Knowledge.JudgingPlayerAbility
          ScoutJudgingPotential = staff.Knowledge.JudgingPlayerPotential
          ScoutAdaptability = staff.Mental.Adaptability
          MedicalPhysiotherapy = staff.Attributes.Medical.Physiotherapy
          MedicalSportsScience = staff.Attributes.Medical.SportsScience
          AnalysisPerformance = staff.Attributes.Analysis.PerformanceAnalysis
          AnalysisRecruitment = staff.Attributes.Analysis.RecruitmentAnalysis
          MentalAdaptability = staff.Mental.Adaptability
          MentalDetermination = staff.Mental.Determination
          MentalLevelOfDiscipline = staff.Mental.LevelOfDiscipline
          MentalManManagement = staff.Mental.PeopleManagement
          MentalMotivating = staff.Mental.Motivating
          MentalPlayerKnowledge = 0
          MentalYoungsterKnowledge = 0
          CurrentSkill = staff.CurrentSkill
          PotentialSkill = staff.PotentialSkill }

    let toStaffDomain (e: StaffEntity) : StaffId * Staff =
        let contract =
            if e.ContractClubId <= 0 then
                None
            else
                Some
                    { ClubId = e.ContractClubId
                      Salary = e.ContractSalary
                      ExpiryYear = e.ContractExpiry }

        e.Id,
        { Id = e.Id
          Name = e.Name
          Nationality = e.Nationality
          Birthday = e.Birthday
          Role = parseStaffRole e.Role
          Badge = parseCoachingBadge e.Badge
          Reputation = e.Reputation
          Status = parseStaffStatus e.Status
          TrophiesWon = e.TrophiesWon
          SeasonsManaged = e.SeasonsManaged
          Contract = contract
          Attributes =
            { Coaching =
                { Attacking = e.CoachAttacking
                  Defending = e.CoachDefending
                  Fitness = e.CoachFitness
                  Goalkeeping = e.CoachGoalkeeping
                  Mental = e.CoachMental
                  SetPieces = e.CoachSetPieces
                  Tactical = e.CoachTactical
                  Technical = e.CoachTechnical
                  WorkingWithYoungsters = e.CoachWorkingWithYoungsters
                  PreferredFormation = None
                  Lineup = None }
              Scouting =
                { NetworkReach = 0
                  DataAnalysis = 0
                  MarketKnowledge = 0 }
              Medical =
                { Physiotherapy = e.MedicalPhysiotherapy
                  SportsScience = e.MedicalSportsScience }
              Analysis =
                { PerformanceAnalysis = e.AnalysisPerformance
                  RecruitmentAnalysis = e.AnalysisRecruitment } }
          Knowledge =
            { JudgingPlayerAbility = e.ScoutJudgingAbility
              JudgingPlayerPotential = e.ScoutJudgingPotential
              JudgingStaffAbility = 0
              Negotiating = 0
              TacticalKnowledge = 0 }
          Mental =
            { Adaptability = e.MentalAdaptability
              Determination = e.MentalDetermination
              LevelOfDiscipline = e.MentalLevelOfDiscipline
              PeopleManagement = e.MentalManManagement
              Motivating = e.MentalMotivating }
          CurrentSkill = e.CurrentSkill
          PotentialSkill = e.PotentialSkill }

    let toCompetitionEntity (competition: Competition) : CompetitionEntity =
        let tag, param1, param2 = competitionTypeToStrings competition.Type

        { Id = competition.Id
          Name = competition.Name
          TypeTag = tag
          TypeParam1 = param1
          TypeParam2 = param2
          Country = competition.Country |> Option.defaultValue ""
          Season = competition.Season }

    let toFixtureEntity (fixture: MatchFixture) : MatchFixtureEntity =
        { Id = fixture.Id
          CompetitionId = fixture.CompetitionId
          Round = fixture.Round |> Option.map roundToString |> Option.defaultValue ""
          HomeClubId = fixture.HomeClubId
          AwayClubId = fixture.AwayClubId
          ScheduledDate = fixture.ScheduledDate
          HomeScore = fixture.HomeScore |> Option.defaultValue -1
          AwayScore = fixture.AwayScore |> Option.defaultValue -1
          Played = fixture.Played }

    let toFixtureDomain (e: MatchFixtureEntity) : MatchFixture =
        { Id = e.Id
          CompetitionId = e.CompetitionId
          Round =
            if String.IsNullOrEmpty(e.Round) then
                None
            else
                Some(parseRound e.Round)
          HomeClubId = e.HomeClubId
          AwayClubId = e.AwayClubId
          ScheduledDate = e.ScheduledDate
          HomeScore = if e.HomeScore = -1 then None else Some e.HomeScore
          AwayScore = if e.AwayScore = -1 then None else Some e.AwayScore
          Played = e.Played
          Events = [] }

    let toKnockoutTieEntity (compId: CompetitionId) (tie: KnockoutTie) : KnockoutTieEntity =
        let aggHome, aggAway = tie.AggregateScore |> Option.defaultValue (-1, -1)

        { TieId = tie.TieId
          CompetitionId = compId
          Round = roundToString tie.Round
          HomeClubId = tie.HomeClubId
          AwayClubId = tie.AwayClubId
          Leg1FixtureId = tie.Leg1FixtureId |> Option.defaultValue -1
          Leg2FixtureId = tie.Leg2FixtureId |> Option.defaultValue -1
          AggHome = aggHome
          AggAway = aggAway
          WinnerId = tie.WinnerId |> Option.defaultValue -1 }

    let toKnockoutTieDomain (e: KnockoutTieEntity) : KnockoutTie =
        { TieId = e.TieId
          Round = parseRound e.Round
          HomeClubId = e.HomeClubId
          AwayClubId = e.AwayClubId
          Leg1FixtureId = if e.Leg1FixtureId = -1 then None else Some e.Leg1FixtureId
          Leg2FixtureId = if e.Leg2FixtureId = -1 then None else Some e.Leg2FixtureId
          AggregateScore = if e.AggHome = -1 then None else Some(e.AggHome, e.AggAway)
          WinnerId = if e.WinnerId = -1 then None else Some e.WinnerId }

    let toStandingEntity (compId: CompetitionId) (standing: LeagueStanding) : LeagueStandingEntity =
        { Id = 0
          CompetitionId = compId
          ClubId = standing.ClubId
          Played = standing.Played
          Won = standing.Won
          Drawn = standing.Drawn
          Lost = standing.Lost
          GoalsFor = standing.GoalsFor
          GoalsAgainst = standing.GoalsAgainst
          Points = standing.Points }

    let toStandingDomain (e: LeagueStandingEntity) : LeagueStanding =
        { ClubId = e.ClubId
          Played = e.Played
          Won = e.Won
          Drawn = e.Drawn
          Lost = e.Lost
          GoalsFor = e.GoalsFor
          GoalsAgainst = e.GoalsAgainst
          Points = e.Points }

    let toCompetitionDomain
        (compClubs: CompetitionClubEntity list)
        (allFixtures: MatchFixtureEntity list)
        (allStandings: LeagueStandingEntity list)
        (allTies: KnockoutTieEntity list)
        (ce: CompetitionEntity)
        : CompetitionId * Competition =
        let clubIds =
            compClubs
            |> List.filter (fun cc -> cc.CompetitionId = ce.Id)
            |> List.map _.ClubId

        let fixtures =
            allFixtures
            |> List.filter (fun f -> f.CompetitionId = ce.Id)
            |> List.map (fun f -> f.Id, toFixtureDomain f)
            |> Map.ofList

        let standings =
            allStandings
            |> List.filter (fun s -> s.CompetitionId = ce.Id)
            |> List.map (fun s -> s.ClubId, toStandingDomain s)
            |> Map.ofList

        let knockoutTies =
            allTies
            |> List.filter (fun t -> t.CompetitionId = ce.Id)
            |> List.map (fun t -> t.TieId, toKnockoutTieDomain t)
            |> Map.ofList

        ce.Id,
        { Id = ce.Id
          Name = ce.Name
          Type = parseCompetitionType ce.TypeTag ce.TypeParam1 ce.TypeParam2
          Country =
            if String.IsNullOrEmpty(ce.Country) then
                None
            else
                Some ce.Country
          Season = ce.Season
          ClubIds = clubIds
          Fixtures = fixtures
          Standings = standings
          KnockoutTies = knockoutTies }

    let toCountryEntity (cd: CountryData) : CountryEntity =
        { Code = cd.Country.Code
          DataJson = serializeCountryData cd }

    let toCountryDomain (e: CountryEntity) : CountryData =
        deserializeCountryData e.DataJson
