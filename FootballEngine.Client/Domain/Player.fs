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
      TrainingSchedule: TrainingSchedule }

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

    let profile (p: Player) : BehavioralProfile =
        let norm x = float x / 20.0
        let clamp01 v = Math.Clamp(v, 0.0, 1.0)

        let tech = p.Technical
        let mental = p.Mental
        let phys = p.Physical

        let positionalFreedom =
            clamp01 (
                norm mental.Positioning * 0.15
                + norm mental.Vision * 0.15
                + norm phys.Stamina * 0.20
                + norm mental.Concentration * 0.10
                + norm phys.Balance * 0.15
                + norm phys.Agility * 0.25
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
                posBase * 0.50
                + norm phys.Pace * 0.15
                + norm phys.Acceleration * 0.12
                + norm tech.Finishing * 0.08
                + norm mental.Composure * 0.05
                + norm phys.Stamina * 0.10
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

            Math.Clamp(baseFromPosition + norm tech.Crossing * 0.15 + norm phys.Pace * 0.10, -1.0, 1.0)

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
                posBase * 0.55
                + norm mental.WorkRate * 0.15
                + norm tech.Tackling * 0.10
                + norm mental.Positioning * 0.12
                + norm phys.Stamina * 0.08
            )

        let pressingIntensity =
            clamp01 (
                norm phys.Stamina * 0.30
                + norm mental.WorkRate * 0.25
                + norm mental.Aggression * 0.20
                + norm phys.Pace * 0.15
                + norm mental.Concentration * 0.10
            )

        let riskAppetite =
            clamp01 (
                norm tech.Passing * 0.15
                + norm tech.LongShots * 0.15
                + norm mental.Vision * 0.20
                + norm mental.Composure * 0.20
                + norm tech.Dribbling * 0.15
                + norm mental.Bravery * 0.15
            )

        let directness =
            clamp01 (
                norm tech.Finishing * 0.20
                + norm phys.Pace * 0.20
                + norm phys.Acceleration * 0.15
                + norm mental.Aggression * 0.15
                + norm tech.Dribbling * 0.10
                + norm phys.Strength * 0.10
                + (1.0 - norm tech.Passing) * 0.10
            )

        let creativityWeight =
            clamp01 (
                norm tech.Passing * 0.25
                + norm mental.Vision * 0.30
                + norm tech.BallControl * 0.15
                + norm tech.Dribbling * 0.10
                + norm mental.Composure * 0.10
                + norm tech.Crossing * 0.10
            )

        let aerialThreat =
            clamp01 (
                clamp01 (float phys.JumpingReach / 20.0) * 0.35
                + norm tech.Heading * 0.30
                + norm phys.Strength * 0.20
                + norm mental.Bravery * 0.15
            )

        let holdUpPlay =
            clamp01 (
                norm phys.Strength * 0.25
                + norm tech.BallControl * 0.20
                + norm mental.Composure * 0.15
                + norm tech.Passing * 0.15
                + norm tech.Heading * 0.10
                + norm phys.Balance * 0.15
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
