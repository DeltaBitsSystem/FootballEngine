namespace FootballEngine.Domain

open System

type PreferredFoot =
    | Left
    | Right
    | Both

type InjurySeverity =
    | Minor
    | Moderate
    | Major
    | Severe

type PlayerStatus =
    | Available
    | Suspended of matchesBanned: int
    | Injured of InjurySeverity * until: DateTime

type ContractInfo = { Salary: decimal; ExpiryYear: int }

type PlayerAffiliation =
    | Contracted of clubId: ClubId * contract: ContractInfo
    | FreeAgent
    | YouthProspect of clubId: ClubId
    | Retired

type PhysicalStats =
    { Acceleration: int
      Pace: int
      Agility: int
      Balance: int
      JumpingReach: int
      Stamina: int
      Strength: int }

type TechnicalStats =
    { Finishing: int
      LongShots: int
      Dribbling: int
      BallControl: int
      Passing: int
      Crossing: int
      Tackling: int
      Marking: int
      Heading: int
      FreeKick: int
      Penalty: int }

type MentalStats =
    { Aggression: int
      Composure: int
      Vision: int
      Positioning: int
      Bravery: int
      WorkRate: int
      Concentration: int
      Leadership: int }

type GoalkeeperStats =
    { Reflexes: int
      Handling: int
      Kicking: int
      OneOnOne: int
      AerialReach: int }

type Player =
    { Id: PlayerId
      Name: string
      Birthday: DateTime
      Nationality: CountryCode
      Position: Position
      PreferredFoot: PreferredFoot
      Height: int
      Weight: int
      Physical: PhysicalStats
      Technical: TechnicalStats
      Mental: MentalStats
      Goalkeeping: GoalkeeperStats
      Condition: int
      MatchFitness: int
      Morale: int
      Status: PlayerStatus
      CurrentSkill: int
      PotentialSkill: int
      Reputation: int
      Affiliation: PlayerAffiliation
      TrainingSchedule: TrainingSchedule
      ExperienceModifiers: ExperienceModifiers }

module Player =
    let age (currentDate: DateTime) (p: Player) =
        let years = currentDate.Year - p.Birthday.Year

        if currentDate < p.Birthday.AddYears(years) then
            years - 1
        else
            years

    let playerValue (skill: int) =
        decimal (max 0 (skill - 30) |> fun x -> x * x * 800)

    let playerSalary (skill: int) =
        decimal (max 0 (skill - 20) |> fun x -> x * x * 5 + 500)

    let clubOf (p: Player) : ClubId option =
        match p.Affiliation with
        | Contracted(clubId, _)
        | YouthProspect clubId -> Some clubId
        | FreeAgent
        | Retired -> None

    let contractOf (p: Player) : ContractInfo option =
        match p.Affiliation with
        | Contracted(_, contract) -> Some contract
        | _ -> None

    let profile (p: Player) (pw: ProfileWeights) : BehavioralProfile =
        let norm x = float x / 20.0
        let clamp01 v = Math.Clamp(v, 0.0, 1.0)

        let tech = p.Technical
        let mental = p.Mental
        let phys = p.Physical

        let positionalFreedom =
            clamp01 (
                norm mental.Positioning * pw.PositionalFreedom_PositioningWeight
                + norm mental.Vision * pw.PositionalFreedom_VisionWeight
                + norm phys.Stamina * pw.PositionalFreedom_StaminaWeight
                + norm mental.Concentration * pw.PositionalFreedom_ConcentrationWeight
                + norm phys.Balance * pw.PositionalFreedom_BalanceWeight
                + norm phys.Agility * pw.PositionalFreedom_AgilityWeight
            )

        let attackingDepth =
            let posBase =
                match p.Position with
                | GK -> 0.0
                | DC | DM -> 0.05
                | DL | DR | WBL | WBR -> 0.25
                | MC -> 0.35
                | ML | MR | AML | AMR -> 0.55
                | AMC -> 0.65
                | ST -> 0.85
            clamp01 (
                posBase * pw.AttackingDepth_PositionBaseMultiplier
                + norm phys.Pace * pw.AttackingDepth_PaceWeight
                + norm phys.Acceleration * pw.AttackingDepth_AccelerationWeight
                + norm tech.Finishing * pw.AttackingDepth_FinishingWeight
                + norm mental.Composure * pw.AttackingDepth_ComposureWeight
                + norm phys.Stamina * pw.AttackingDepth_StaminaWeight
            )

        let lateralTendency =
            let baseFromPosition =
                match p.Position with
                | GK
                | DC
                | DM
                | MC
                | AMC
                | ST -> 0.0
                | DL
                | ML
                | AML -> -0.7
                | DR
                | MR
                | AMR -> 0.7
                | WBL -> -0.85
                | WBR -> 0.85

            Math.Clamp(baseFromPosition + norm tech.Crossing * pw.LateralTendency_CrossingWeight + norm phys.Pace * pw.LateralTendency_PaceWeight, -1.0, 1.0)

        let defensiveHeight =
            let posBase =
                match p.Position with
                | GK -> 1.0
                | DC -> 0.90
                | DL | DR | WBL | WBR -> 0.75
                | DM -> 0.70
                | MC | ML | MR -> 0.45
                | AML | AMR | AMC -> 0.25
                | ST -> 0.10
            clamp01 (
                posBase * pw.DefensiveHeight_PositionBaseMultiplier
                + norm mental.WorkRate * pw.DefensiveHeight_WorkRateWeight
                + norm tech.Tackling * pw.DefensiveHeight_TacklingWeight
                + norm mental.Positioning * pw.DefensiveHeight_PositioningWeight
                + norm phys.Stamina * pw.DefensiveHeight_StaminaWeight
            )

        let pressingIntensity =
            clamp01 (
                norm phys.Stamina * pw.PressingIntensity_StaminaWeight
                + norm mental.WorkRate * pw.PressingIntensity_WorkRateWeight
                + norm mental.Aggression * pw.PressingIntensity_AggressionWeight
                + norm phys.Pace * pw.PressingIntensity_PaceWeight
                + norm mental.Concentration * pw.PressingIntensity_ConcentrationWeight
            )

        let riskAppetite =
            clamp01 (
                norm tech.Passing * pw.RiskAppetite_PassingWeight
                + norm tech.LongShots * pw.RiskAppetite_LongShotsWeight
                + norm mental.Vision * pw.RiskAppetite_VisionWeight
                + norm mental.Composure * pw.RiskAppetite_ComposureWeight
                + norm tech.Dribbling * pw.RiskAppetite_DribblingWeight
                + norm mental.Bravery * pw.RiskAppetite_BraveryWeight
            )

        let directness =
            clamp01 (
                norm tech.Finishing * pw.Directness_FinishingWeight
                + norm phys.Pace * pw.Directness_PaceWeight
                + norm phys.Acceleration * pw.Directness_AccelerationWeight
                + norm mental.Aggression * pw.Directness_AggressionWeight
                + norm tech.Dribbling * pw.Directness_DribblingWeight
                + norm phys.Strength * pw.Directness_StrengthWeight
                + (1.0 - norm tech.Passing) * pw.Directness_InversePassingWeight
            )

        let creativityWeight =
            clamp01 (
                norm tech.Passing * pw.CreativityWeight_PassingWeight
                + norm mental.Vision * pw.CreativityWeight_VisionWeight
                + norm tech.BallControl * pw.CreativityWeight_BallControlWeight
                + norm tech.Dribbling * pw.CreativityWeight_DribblingWeight
                + norm mental.Composure * pw.CreativityWeight_ComposureWeight
                + norm tech.Crossing * pw.CreativityWeight_CrossingWeight
            )

        let aerialThreat =
            clamp01 (
                clamp01 (float phys.JumpingReach / 20.0) * pw.AerialThreat_JumpingReachWeight
                + norm tech.Heading * pw.AerialThreat_HeadingWeight
                + norm phys.Strength * pw.AerialThreat_StrengthWeight
                + norm mental.Bravery * pw.AerialThreat_BraveryWeight
            )

        let holdUpPlay =
            clamp01 (
                norm phys.Strength * pw.HoldUpPlay_StrengthWeight
                + norm tech.BallControl * pw.HoldUpPlay_BallControlWeight
                + norm mental.Composure * pw.HoldUpPlay_ComposureWeight
                + norm tech.Passing * pw.HoldUpPlay_PassingWeight
                + norm tech.Heading * pw.HoldUpPlay_HeadingWeight
                + norm phys.Balance * pw.HoldUpPlay_BalanceWeight
            )

        { PositionalFreedom = positionalFreedom
          AttackingDepth = attackingDepth
          LateralTendency = lateralTendency
          DefensiveHeight = defensiveHeight
          PressingIntensity = pressingIntensity
          RiskAppetite = riskAppetite
          Directness = directness
          CreativityWeight = creativityWeight
          AerialThreat = aerialThreat
          HoldUpPlay = holdUpPlay }
