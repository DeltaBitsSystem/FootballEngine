namespace FootballEngine.Domain

open System



type CoachingBadge =
    | NoneBadge
    | NationalC
    | NationalB
    | NationalA
    | ProLicense
    | ContinentalPro

type StaffRole =
    | HeadCoach
    | AssistantManager
    | FirstTeamCoach
    | GoalkeeperCoach
    | FitnessCoach
    | HeadOfYouthDevelopment
    | Scout
    | Physio
    | SportsScientist
    | PerformanceAnalyst
    | RecruitmentAnalyst
    | LoanManager
    | TechnicalDirector

type CoachingAttributes =
    { Attacking: int
      Defending: int
      Fitness: int
      Goalkeeping: int
      Mental: int
      SetPieces: int
      Tactical: int
      Technical: int
      WorkingWithYoungsters: int
      PreferredFormation: Formation option
      Lineup: Lineup option }

type KnowledgeAttributes =
    { JudgingPlayerAbility: int
      JudgingPlayerPotential: int
      JudgingStaffAbility: int
      Negotiating: int
      TacticalKnowledge: int }

type ScoutingAttributes =
    { NetworkReach: int
      DataAnalysis: int
      MarketKnowledge: int }

type MedicalAttributes =
    { Physiotherapy: int
      SportsScience: int }

type AnalysisAttributes =
    { PerformanceAnalysis: int
      RecruitmentAnalysis: int }

type StaffAttributes =
    { Coaching: CoachingAttributes
      Scouting: ScoutingAttributes
      Medical: MedicalAttributes
      Analysis: AnalysisAttributes }

type StaffMentalAttributes =
    { Adaptability: int
      Determination: int
      LevelOfDiscipline: int
      PeopleManagement: int
      Motivating: int }

type StaffStatus =
    | Active
    | UnderPressure
    | Sacked
    | Resigned
    | ContractExpired
    | Unemployed
    | StaffRetired

type StaffContract =
    { ClubId: ClubId
      Salary: decimal
      ExpiryYear: int }

type Staff =
    { Id: StaffId
      Name: string
      Nationality: CountryCode
      Birthday: DateTime
      Role: StaffRole
      Attributes: StaffAttributes
      Knowledge: KnowledgeAttributes
      Mental: StaffMentalAttributes
      CurrentSkill: int
      PotentialSkill: int
      Badge: CoachingBadge
      Reputation: int
      Contract: StaffContract option
      Status: StaffStatus
      TrophiesWon: int
      SeasonsManaged: int }

module Staff =

    let age (currentDate: DateTime) (s: Staff) =
        let years = currentDate.Year - s.Birthday.Year

        if currentDate < s.Birthday.AddYears(years) then
            years - 1
        else
            years

    let clubId (s: Staff) = s.Contract |> Option.map _.ClubId

    let isEmployed (s: Staff) =
        s.Status = Active || s.Status = UnderPressure

    let isCoachingRole (s: Staff) =
        match s.Role with
        | HeadCoach
        | AssistantManager
        | FirstTeamCoach
        | GoalkeeperCoach
        | FitnessCoach
        | HeadOfYouthDevelopment -> true
        | Scout
        | Physio
        | SportsScientist
        | PerformanceAnalyst
        | RecruitmentAnalyst
        | LoanManager
        | TechnicalDirector -> false

    let effectiveAbility (s: Staff) =
        let c = s.Attributes.Coaching
        let sc = s.Attributes.Scouting
        let md = s.Attributes.Medical
        let an = s.Attributes.Analysis
        let kn = s.Knowledge
        let mn = s.Mental

        let disciplineMultiplier =
            let avg = float (mn.Determination + mn.LevelOfDiscipline + mn.Motivating) / 3.0
            0.79 + (avg / 20.0) * 0.21

        let applyDiscipline (baseScore: float) = baseScore * disciplineMultiplier |> int

        match s.Role with
        | HeadCoach ->
            [ kn.TacticalKnowledge
              kn.JudgingPlayerAbility
              kn.JudgingPlayerPotential
              mn.PeopleManagement
              c.Attacking
              c.Defending ]
            |> List.averageBy float
            |> applyDiscipline

        | AssistantManager ->
            [ kn.TacticalKnowledge
              kn.JudgingPlayerAbility
              mn.PeopleManagement
              c.Attacking
              c.Defending ]
            |> List.averageBy float
            |> applyDiscipline

        | FirstTeamCoach ->
            [ c.Attacking; c.Defending; c.Technical; c.Mental; kn.TacticalKnowledge ]
            |> List.averageBy float
            |> applyDiscipline

        | GoalkeeperCoach ->
            [ c.Goalkeeping; c.Technical; c.Mental ]
            |> List.averageBy float
            |> applyDiscipline

        | FitnessCoach -> [ c.Fitness; md.SportsScience ] |> List.averageBy float |> applyDiscipline

        | HeadOfYouthDevelopment ->
            [ c.WorkingWithYoungsters
              kn.JudgingPlayerPotential
              kn.JudgingPlayerAbility
              mn.PeopleManagement ]
            |> List.averageBy float
            |> applyDiscipline

        | Scout ->

            [ kn.JudgingPlayerAbility
              kn.JudgingPlayerPotential
              mn.Adaptability
              sc.NetworkReach
              sc.DataAnalysis ]
            |> List.averageBy float
            |> applyDiscipline

        | Physio ->
            int (
                float md.Physiotherapy * 0.8
                + float md.SportsScience * 0.1
                + float mn.Determination * 0.1
            )

        | SportsScientist -> [ md.SportsScience; c.Fitness ] |> List.averageBy float |> applyDiscipline

        | PerformanceAnalyst ->
            [ an.PerformanceAnalysis; kn.TacticalKnowledge; kn.JudgingPlayerAbility ]
            |> List.averageBy float
            |> applyDiscipline

        | RecruitmentAnalyst ->
            [ an.RecruitmentAnalysis
              kn.JudgingPlayerAbility
              kn.JudgingPlayerPotential
              sc.MarketKnowledge ]
            |> List.averageBy float
            |> int

        | LoanManager ->
            [ kn.JudgingPlayerPotential
              mn.PeopleManagement
              kn.JudgingPlayerAbility
              mn.Adaptability ]
            |> List.averageBy float
            |> int

        | TechnicalDirector -> [ kn.JudgingStaffAbility; kn.Negotiating ] |> List.averageBy float |> int

    let canBecomeHeadCoach (s: Staff) =
        match s.Role with
        | AssistantManager
        | FirstTeamCoach -> s.Badge >= NationalA
        | _ -> false

    let promoteToHeadCoach (s: Staff) : Staff = { s with Role = HeadCoach }

    let applySeasonOutcome (objective: BoardObjective) (achieved: bool) (s: Staff) : Staff =
        match s.Role with
        | HeadCoach ->
            let repDelta =
                match objective, achieved with
                | LeagueObjective WinLeague, true -> 500
                | LeagueObjective TopFour, true -> 300
                | LeagueObjective TopHalf, true -> 100
                | LeagueObjective MidTable, true -> 50
                | LeagueObjective Survival, true -> 30
                | Promotion, true -> 400
                | CupObjective WinChampionsLeague, true -> 600
                | CupObjective WinContinentalCup, true -> 400
                | CupObjective WinDomesticCup, true -> 200
                | Relegation, true -> 0
                | LeagueObjective WinLeague, false -> -300
                | LeagueObjective TopFour, false -> -200
                | LeagueObjective TopHalf, false -> -80
                | LeagueObjective MidTable, false -> -40
                | LeagueObjective Survival, false -> -200
                | Promotion, false -> -50
                | CupObjective _, false -> 0
                | Relegation, false -> -100

            let newStatus =
                match s.Status, achieved with
                | Active, false -> UnderPressure
                | UnderPressure, false -> Sacked
                | UnderPressure, true -> Active
                | other, _ -> other

            let isTrophy =
                match objective, achieved with
                | LeagueObjective WinLeague, true -> true
                | CupObjective _, true -> true
                | Promotion, true -> true
                | _ -> false

            { s with
                Reputation = max 0 (min 10000 (s.Reputation + repDelta))
                Status = newStatus
                SeasonsManaged = s.SeasonsManaged + 1
                TrophiesWon = if isTrophy then s.TrophiesWon + 1 else s.TrophiesWon }

        | _ ->
            { s with
                SeasonsManaged = s.SeasonsManaged + 1 }

    let setLineup (lineup: Lineup option) (s: Staff) : Staff =
        { s with
            Attributes =
                { s.Attributes with
                    Coaching =
                        { s.Attributes.Coaching with
                            Lineup = lineup } } }

    let updateLineup (updater: Lineup -> Lineup) (s: Staff) : Staff =
        match s.Attributes.Coaching.Lineup with
        | None -> s
        | Some lineup -> setLineup (Some(updater lineup)) s
