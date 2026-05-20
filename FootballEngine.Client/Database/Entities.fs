namespace FootballEngine.Database

open System
open SQLite

[<CLIMutable>]
type PlayerEntity =
    { [<PrimaryKey>]
      Id: int
      ClubId: int
      Name: string
      Birthday: DateTime
      Nationality: string
      Position: string
      PreferredFoot: string
      Height: int
      Weight: int
      Acceleration: int
      Pace: int
      Agility: int
      Balance: int
      JumpingReach: int
      Stamina: int
      Strength: int
      Finishing: int
      LongShots: int
      Dribbling: int
      BallControl: int
      Passing: int
      Crossing: int
      Tackling: int
      Marking: int
      Heading: int
      FreeKick: int
      Penalty: int
      Aggression: int
      Composure: int
      Vision: int
      Positioning: int
      Bravery: int
      WorkRate: int
      Concentration: int
      Leadership: int
      Reflexes: int
      Handling: int
      Kicking: int
      OneOnOne: int
      AerialReach: int
      Condition: int
      MatchFitness: int
      Morale: int
      StatusType: string
      StatusParamInt: int
      StatusParamDate: DateTime
      CurrentSkill: int
      PotentialSkill: int
      Reputation: int
      Value: decimal
      Salary: decimal
      ContractExpiry: int
      TrainingFocus: string
      TrainingIntensity: string
      BehavioralProfile: string }

[<CLIMutable>]
type ClubEntity =
    { [<PrimaryKey>]
      Id: int
      Name: string
      Nationality: string
      Reputation: int
      Budget: decimal
      Morale: int
      BoardObjective: string
      CoordinationMemory: string }

[<CLIMutable>]
type LineupSlotEntity =
    { [<PrimaryKey; AutoIncrement>]
      Id: int
      ClubId: int
      FormationName: string
      TacticsName: string
      Mentality: int
      DefensiveLine: int
      PressingIntensity: int
      Width: int
      Tempo: int
      Directness: int
      PressTriggerZone: int
      DefensiveShape: int
      SlotIndex: int
      Role: string
      X: float
      Y: float
      PlayerId: int }

[<CLIMutable>]
type StaffEntity =
    { [<PrimaryKey>]
      Id: int
      Name: string
      Nationality: string
      Birthday: DateTime
      Role: string
      Badge: string
      Reputation: int
      Status: string
      TrophiesWon: int
      SeasonsManaged: int
      ContractClubId: int
      ContractSalary: decimal
      ContractExpiry: int
      CoachAttacking: int
      CoachDefending: int
      CoachFitness: int
      CoachGoalkeeping: int
      CoachMental: int
      CoachSetPieces: int
      CoachTactical: int
      CoachTechnical: int
      CoachWorkingWithYoungsters: int
      ScoutJudgingAbility: int
      ScoutJudgingPotential: int
      ScoutAdaptability: int
      MedicalPhysiotherapy: int
      MedicalSportsScience: int
      AnalysisPerformance: int
      AnalysisRecruitment: int
      MentalAdaptability: int
      MentalDetermination: int
      MentalLevelOfDiscipline: int
      MentalManManagement: int
      MentalMotivating: int
      MentalPlayerKnowledge: int
      MentalYoungsterKnowledge: int
      CurrentSkill: int
      PotentialSkill: int }

[<CLIMutable>]
type CompetitionEntity =
    { [<PrimaryKey>]
      Id: int
      Name: string
      TypeTag: string
      TypeParam1: string
      TypeParam2: string
      Country: string
      Season: int }

[<CLIMutable>]
type CompetitionClubEntity =
    { [<PrimaryKey; AutoIncrement>]
      Id: int
      CompetitionId: int
      ClubId: int }

[<CLIMutable>]
type LeagueStandingEntity =
    { [<PrimaryKey; AutoIncrement>]
      Id: int
      CompetitionId: int
      ClubId: int
      Played: int
      Won: int
      Drawn: int
      Lost: int
      GoalsFor: int
      GoalsAgainst: int
      Points: int }

[<CLIMutable>]
type KnockoutTieEntity =
    { [<PrimaryKey>]
      TieId: int
      CompetitionId: int
      Round: string
      HomeClubId: int
      AwayClubId: int
      Leg1FixtureId: int
      Leg2FixtureId: int
      AggHome: int
      AggAway: int
      WinnerId: int }

[<CLIMutable>]
type MatchFixtureEntity =
    { [<PrimaryKey>]
      Id: int
      CompetitionId: int
      Round: string
      HomeClubId: int
      AwayClubId: int
      ScheduledDate: DateTime
      HomeScore: int
      AwayScore: int
      Played: bool }

[<CLIMutable>]
type CountryEntity =
    { [<PrimaryKey>]
      Code: string
      DataJson: string }

[<CLIMutable>]
type GameSaveMeta =
    { [<PrimaryKey>]
      Id: int
      CurrentDate: DateTime
      Season: int
      TrainingWeeksApplied: int
      UserClubId: int
      UserStaffId: int
      PrimaryCountry: string
      NextInboxId: int
      ClockDay: int
      ClockWeek: int
      ClockMonth: int
      ActiveModsJson: string }

[<CLIMutable>]
type InboxMessageEntity =
    { [<PrimaryKey>]
      Id: int
      GameSaveId: int
      Date: DateTime
      From: string
      Subject: string
      Body: string
      Category: string
      IsRead: bool
      RequiresAction: bool
      ActionTaken: System.Nullable<bool> }
