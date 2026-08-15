namespace FootballEngine.Generation

open System
open FootballEngine.Domain
open FootballEngine.Data
open FootballEngine.Stats

module StaffGen =
    let private currentSkillForLevel (leagueLevel: int) : int =
        match leagueLevel with
        | 0 -> betaInt 4.0 3.0 100 190
        | 1 -> betaInt 3.5 4.0 80 160
        | _ -> betaInt 2.5 5.0 50 130

    let private potentialSkillFor (currentSkill: int) (age: int) : int =
        let agePenalty = max 0 (age - 50) * 2
        let rawPotential = currentSkill + normalInt 15.0 5.0 5 40
        min 200 (max currentSkill (rawPotential - agePenalty))

    let private coachingFor (role: StaffRole) =
        let high = normalInt 14.0 2.0 10 20
        let mid = normalInt 10.0 2.0 5 16
        let low = normalInt 6.0 2.0 1 10

        match role with
        | HeadCoach ->
            { Attacking = high
              Defending = high
              Fitness = mid
              Goalkeeping = mid
              Mental = high
              SetPieces = mid
              Tactical = high
              Technical = high
              WorkingWithYoungsters = mid
              PreferredFormation = None
              Lineup = None }
        | AssistantManager ->
            { Attacking = high
              Defending = high
              Fitness = mid
              Goalkeeping = mid
              Mental = high
              SetPieces = mid
              Tactical = high
              Technical = mid
              WorkingWithYoungsters = mid
              PreferredFormation = None
              Lineup = None }
        | FirstTeamCoach ->
            { Attacking = high
              Defending = high
              Fitness = mid
              Goalkeeping = low
              Mental = mid
              SetPieces = mid
              Tactical = mid
              Technical = high
              WorkingWithYoungsters = low
              PreferredFormation = None
              Lineup = None }
        | GoalkeeperCoach ->
            { Attacking = low
              Defending = mid
              Fitness = mid
              Goalkeeping = high
              Mental = mid
              SetPieces = low
              Tactical = low
              Technical = mid
              WorkingWithYoungsters = low
              PreferredFormation = None
              Lineup = None }
        | FitnessCoach ->
            { Attacking = low
              Defending = low
              Fitness = high
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = low
              Technical = low
              WorkingWithYoungsters = mid
              PreferredFormation = None
              Lineup = None }
        | HeadOfYouthDevelopment ->
            { Attacking = mid
              Defending = mid
              Fitness = mid
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = mid
              Technical = mid
              WorkingWithYoungsters = high
              PreferredFormation = None
              Lineup = None }
        | Scout ->
            { Attacking = low
              Defending = low
              Fitness = low
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = mid
              Technical = low
              WorkingWithYoungsters = mid
              PreferredFormation = None
              Lineup = None }
        | Physio ->
            { Attacking = low
              Defending = low
              Fitness = high
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = low
              Technical = low
              WorkingWithYoungsters = low
              PreferredFormation = None
              Lineup = None }
        | SportsScientist ->
            { Attacking = low
              Defending = low
              Fitness = high
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = low
              Technical = low
              WorkingWithYoungsters = low
              PreferredFormation = None
              Lineup = None }
        | PerformanceAnalyst ->
            { Attacking = mid
              Defending = mid
              Fitness = low
              Goalkeeping = low
              Mental = mid
              SetPieces = mid
              Tactical = high
              Technical = low
              WorkingWithYoungsters = low
              PreferredFormation = None
              Lineup = None }
        | RecruitmentAnalyst ->
            { Attacking = low
              Defending = low
              Fitness = low
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = mid
              Technical = low
              WorkingWithYoungsters = mid
              PreferredFormation = None
              Lineup = None }
        | LoanManager ->
            { Attacking = low
              Defending = low
              Fitness = low
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = mid
              Technical = low
              WorkingWithYoungsters = high
              PreferredFormation = None
              Lineup = None }
        | TechnicalDirector ->
            { Attacking = low
              Defending = low
              Fitness = low
              Goalkeeping = low
              Mental = mid
              SetPieces = low
              Tactical = mid
              Technical = low
              WorkingWithYoungsters = low
              PreferredFormation = None
              Lineup = None }

    let private knowledgeFor (role: StaffRole) =
        let high = normalInt 14.0 2.0 10 20
        let mid = normalInt 10.0 2.0 5 16
        let low = normalInt 6.0 2.0 1 10

        match role with
        | Scout
        | RecruitmentAnalyst ->
            { JudgingPlayerAbility = high
              JudgingPlayerPotential = high
              JudgingStaffAbility = mid
              Negotiating = mid
              TacticalKnowledge = mid }
        | HeadCoach
        | AssistantManager ->
            { JudgingPlayerAbility = high
              JudgingPlayerPotential = high
              JudgingStaffAbility = mid
              Negotiating = high
              TacticalKnowledge = high }
        | HeadOfYouthDevelopment
        | LoanManager ->
            { JudgingPlayerAbility = mid
              JudgingPlayerPotential = high
              JudgingStaffAbility = low
              Negotiating = mid
              TacticalKnowledge = mid }
        | PerformanceAnalyst ->
            { JudgingPlayerAbility = high
              JudgingPlayerPotential = mid
              JudgingStaffAbility = low
              Negotiating = low
              TacticalKnowledge = high }
        | TechnicalDirector ->
            { JudgingPlayerAbility = high
              JudgingPlayerPotential = high
              JudgingStaffAbility = high
              Negotiating = high
              TacticalKnowledge = mid }
        | FirstTeamCoach
        | GoalkeeperCoach
        | FitnessCoach
        | Physio
        | SportsScientist ->
            { JudgingPlayerAbility = low
              JudgingPlayerPotential = low
              JudgingStaffAbility = low
              Negotiating = low
              TacticalKnowledge = low }

    let private scoutingFor (role: StaffRole) =
        let high = normalInt 14.0 2.0 10 20
        let mid = normalInt 10.0 2.0 5 16
        let low = normalInt 6.0 2.0 1 10

        match role with
        | Scout
        | RecruitmentAnalyst ->
            { NetworkReach = high
              DataAnalysis = high
              MarketKnowledge = high }
        | HeadCoach
        | AssistantManager
        | TechnicalDirector ->
            { NetworkReach = mid
              DataAnalysis = mid
              MarketKnowledge = high }
        | HeadOfYouthDevelopment
        | LoanManager ->
            { NetworkReach = mid
              DataAnalysis = mid
              MarketKnowledge = mid }
        | PerformanceAnalyst ->
            { NetworkReach = low
              DataAnalysis = high
              MarketKnowledge = mid }
        | FirstTeamCoach
        | GoalkeeperCoach
        | FitnessCoach
        | Physio
        | SportsScientist ->
            { NetworkReach = low
              DataAnalysis = low
              MarketKnowledge = low }

    let private medicalFor (role: StaffRole) =
        let high = normalInt 14.0 2.0 10 20
        let mid = normalInt 10.0 2.0 5 16
        let low = normalInt 6.0 2.0 1 10

        match role with
        | Physio ->
            { Physiotherapy = high
              SportsScience = mid }
        | SportsScientist ->
            { Physiotherapy = mid
              SportsScience = high }
        | FitnessCoach ->
            { Physiotherapy = mid
              SportsScience = mid }
        | HeadCoach
        | AssistantManager
        | FirstTeamCoach
        | GoalkeeperCoach
        | HeadOfYouthDevelopment
        | Scout
        | PerformanceAnalyst
        | RecruitmentAnalyst
        | LoanManager
        | TechnicalDirector ->
            { Physiotherapy = low
              SportsScience = low }

    let private analysisFor (role: StaffRole) =
        let high = normalInt 14.0 2.0 10 20
        let mid = normalInt 10.0 2.0 5 16
        let low = normalInt 6.0 2.0 1 10

        match role with
        | PerformanceAnalyst ->
            { PerformanceAnalysis = high
              RecruitmentAnalysis = mid }
        | RecruitmentAnalyst ->
            { PerformanceAnalysis = mid
              RecruitmentAnalysis = high }
        | HeadCoach
        | AssistantManager
        | TechnicalDirector ->
            { PerformanceAnalysis = mid
              RecruitmentAnalysis = mid }
        | FirstTeamCoach
        | GoalkeeperCoach
        | FitnessCoach
        | HeadOfYouthDevelopment
        | Scout
        | Physio
        | SportsScientist
        | LoanManager ->
            { PerformanceAnalysis = low
              RecruitmentAnalysis = low }

    let private mentalFor (role: StaffRole) =
        let high = normalInt 14.0 2.0 10 20
        let mid = normalInt 10.0 2.0 5 16
        let low = normalInt 6.0 2.0 1 10

        match role with
        | HeadCoach ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = high
              PeopleManagement = high
              Motivating = high }
        | AssistantManager ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = mid
              PeopleManagement = high
              Motivating = high }
        | FirstTeamCoach ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = mid
              PeopleManagement = mid
              Motivating = mid }
        | GoalkeeperCoach ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = mid
              PeopleManagement = low
              Motivating = mid }
        | FitnessCoach ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = high
              PeopleManagement = low
              Motivating = mid }
        | HeadOfYouthDevelopment ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = mid
              PeopleManagement = mid
              Motivating = mid }
        | Scout ->
            { Adaptability = high
              Determination = high
              LevelOfDiscipline = low
              PeopleManagement = low
              Motivating = low }
        | Physio
        | SportsScientist ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = mid
              PeopleManagement = low
              Motivating = low }
        | PerformanceAnalyst ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = low
              PeopleManagement = low
              Motivating = low }
        | RecruitmentAnalyst ->
            { Adaptability = high
              Determination = high
              LevelOfDiscipline = low
              PeopleManagement = low
              Motivating = low }
        | LoanManager ->
            { Adaptability = high
              Determination = mid
              LevelOfDiscipline = low
              PeopleManagement = high
              Motivating = mid }
        | TechnicalDirector ->
            { Adaptability = mid
              Determination = high
              LevelOfDiscipline = mid
              PeopleManagement = high
              Motivating = mid }

    let private badgeForRole (role: StaffRole) (reputation: int) =
        match role with
        | HeadCoach
        | AssistantManager ->
            match reputation with
            | r when r >= 7000 -> ContinentalPro
            | r when r >= 5000 -> ProLicense
            | r when r >= 3000 -> NationalA
            | r when r >= 1500 -> NationalB
            | _ -> NationalC
        | FirstTeamCoach
        | GoalkeeperCoach
        | FitnessCoach
        | HeadOfYouthDevelopment -> if bernoulli 0.4 then NationalB else NationalC
        | Scout
        | Physio
        | SportsScientist
        | PerformanceAnalyst
        | RecruitmentAnalyst
        | LoanManager
        | TechnicalDirector -> NoneBadge

    let private reputationFor (leagueLevel: int) (role: StaffRole) =
        match role with
        | HeadCoach ->
            match leagueLevel with
            | 0 -> normalInt 6000.0 1000.0 2000 9999
            | 1 -> normalInt 3500.0 800.0 500 6000
            | _ -> normalInt 1500.0 600.0 100 4000
        | AssistantManager ->
            match leagueLevel with
            | 0 -> normalInt 4000.0 800.0 1000 7000
            | _ -> normalInt 2000.0 600.0 200 5000
        | TechnicalDirector ->
            match leagueLevel with
            | 0 -> normalInt 4000.0 800.0 1000 7000
            | _ -> normalInt 2000.0 600.0 200 5000
        | FirstTeamCoach
        | GoalkeeperCoach
        | FitnessCoach
        | HeadOfYouthDevelopment -> normalInt 1500.0 600.0 100 4000
        | Scout
        | RecruitmentAnalyst
        | LoanManager -> normalInt 1000.0 400.0 100 3000
        | Physio
        | SportsScientist
        | PerformanceAnalyst -> normalInt 800.0 300.0 100 2500

    let create
        (staffId: StaffId)
        (role: StaffRole)
        (clubId: ClubId)
        (countryData: CountryData)
        (leagueLevel: int)
        (year: int)
        : Staff =
        let reputation = reputationFor leagueLevel role
        let age = normalInt 40.0 7.0 28 65
        let currentSkill = currentSkillForLevel leagueLevel

        { Id = staffId
          Name =
            pickWeighted (countryData.Names.FirstNames |> List.map (fun name -> 1.0, name))
            + " "
            + pickWeighted (countryData.Names.LastNames |> List.map (fun name -> 1.0, name))
          Nationality = countryData.Country.Code
          Birthday = DateTime(year - age, normalInt 6.0 3.0 1 12, normalInt 15.0 8.0 1 28)
          Role = role
          Attributes =
            { Coaching = coachingFor role
              Scouting = scoutingFor role
              Medical = medicalFor role
              Analysis = analysisFor role }
          Knowledge = knowledgeFor role
          Mental = mentalFor role
          CurrentSkill = currentSkill
          PotentialSkill = potentialSkillFor currentSkill age
          Badge = badgeForRole role reputation
          Reputation = reputation
          Contract =
            Some
                { ClubId = clubId
                  Salary = normalFloat 20_000.0 8_000.0 5_000.0 150_000.0 |> decimal
                  ExpiryYear = year + normalInt 2.0 1.0 1 4 }
          Status = Active
          TrophiesWon = 0
          SeasonsManaged = normalInt 5.0 4.0 0 20 }

    let rolesForClub =
        [ HeadCoach
          AssistantManager
          FirstTeamCoach
          GoalkeeperCoach
          FitnessCoach
          HeadOfYouthDevelopment
          Scout
          Scout
          Scout
          Physio
          SportsScientist
          PerformanceAnalyst
          RecruitmentAnalyst
          LoanManager ]
