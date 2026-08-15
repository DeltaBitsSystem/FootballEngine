namespace FootballEngine.Generation

open System
open FootballEngine.Domain
open FootballEngine.Data
open FootballEngine.Stats

module PlayerGen =

    let private currentSkillForLevel (leagueLevel: int) : int =
        match leagueLevel with
        | 0 -> betaInt 4.0 3.0 80 180
        | 1 -> betaInt 3.5 4.0 60 150
        | _ -> betaInt 2.5 5.0 40 120

    type private PotentialTier =
        | Limited
        | Decent
        | Good
        | VeryGood
        | Excellent
        | WorldClass

    let private potentialTierWeights (currentSkill: int) =
        let skillFactor = float currentSkill / 180.0

        [ 3.5 + skillFactor * 2.0, Limited
          3.0, Decent
          1.8, Good
          0.8, VeryGood
          0.25, Excellent
          0.05, WorldClass ]

    let private potentialSkillFor (currentSkill: int) (age: int) : int =
        let agePenalty = max 0 (age - 21) * 4
        let tier = pickWeighted (potentialTierWeights currentSkill)

        let rawPotential =
            match tier with
            | Limited -> currentSkill + normalInt 12.0 4.0 5 20
            | Decent -> currentSkill + normalInt 25.0 6.0 15 35
            | Good -> currentSkill + normalInt 40.0 8.0 25 55
            | VeryGood -> currentSkill + normalInt 60.0 8.0 45 75
            | Excellent -> currentSkill + normalInt 82.0 9.0 65 100
            | WorldClass -> currentSkill + normalInt 110.0 12.0 90 130

        min 200 (max currentSkill (rawPotential - agePenalty))

    let private ageForPosition (position: Position) =
        match position with
        | GK -> normalInt 27.0 4.0 18 38
        | DC
        | DM -> normalInt 26.0 4.0 17 36
        | ST
        | AML
        | AMR -> normalInt 24.0 4.0 16 33
        | DL
        | DR
        | WBL
        | WBR -> normalInt 25.0 4.0 17 34
        | ML
        | MR
        | MC
        | AMC -> normalInt 25.0 4.0 16 35

    let private contractExpiryFor (year: int) (age: int) =
        let maxYears = max 1 (min 5 (38 - age))
        year + 1 + normalInt (float maxYears / 2.0) 1.0 1 maxYears

    let private preferredFootFor (position: Position) =
        match position with
        | AML
        | DL
        | WBL -> if bernoulli 0.75 then Left else Right
        | AMR
        | DR
        | WBR -> if bernoulli 0.75 then Right else Left
        | GK
        | DC
        | DM
        | MC
        | ML
        | MR
        | AMC
        | ST -> if bernoulli 0.80 then Right else Left

    let private heightWeightFor (position: Position) =
        match position with
        | GK -> normalInt 185.0 5.0 175 200, normalInt 82.0 5.0 72 95
        | DC -> normalInt 182.0 5.0 172 195, normalInt 78.0 5.0 68 90
        | DL
        | DR -> normalInt 178.0 4.0 168 190, normalInt 74.0 4.0 64 84
        | WBL
        | WBR -> normalInt 177.0 4.0 167 190, normalInt 73.0 4.0 63 83
        | DM -> normalInt 180.0 5.0 170 192, normalInt 76.0 5.0 66 86
        | MC
        | ML
        | MR -> normalInt 178.0 5.0 168 192, normalInt 75.0 5.0 65 85
        | AMC -> normalInt 175.0 5.0 165 188, normalInt 71.0 4.0 61 81
        | AML
        | AMR -> normalInt 174.0 5.0 164 186, normalInt 70.0 4.0 60 80
        | ST -> normalInt 180.0 6.0 168 196, normalInt 76.0 5.0 66 88

    let private attributeBiasFor (position: Position) (currentSkill: int) =
        let baseAbility = float currentSkill / 10.0
        let atMean mean = normalInt mean 3.0 1 20
        let above delta = atMean (baseAbility + delta)
        let below delta = atMean (baseAbility - delta)
        let atBase () = atMean baseAbility

        fun (category: string) (attribute: string) ->
            match category, attribute, position with
            | "tech", "Finishing", ST -> above 4.0
            | "tech", "Finishing", _ -> below 2.0
            | "tech", "Crossing", (AML | AMR) -> above 3.0
            | "tech", "Tackling", (DC | DM) -> above 4.0
            | "tech", "Tackling", _ -> below 3.0
            | "tech", "Marking", DC -> above 4.0
            | "tech", "Marking", _ -> below 3.0
            | _ -> atBase ()

    let private buildPhysical (bias: string -> string -> int) : PhysicalStats =
        { Acceleration = bias "phys" "Acceleration"
          Pace = bias "phys" "Pace"
          Agility = bias "phys" "Agility"
          Balance = bias "phys" "Balance"
          JumpingReach = bias "phys" "JumpingReach"
          Stamina = bias "phys" "Stamina"
          Strength = bias "phys" "Strength" }

    let private buildTechnical (bias: string -> string -> int) : TechnicalStats =
        { Finishing = bias "tech" "Finishing"
          LongShots = bias "tech" "LongShots"
          Dribbling = bias "tech" "Dribbling"
          BallControl = bias "tech" "BallControl"
          Passing = bias "tech" "Passing"
          Crossing = bias "tech" "Crossing"
          Tackling = bias "tech" "Tackling"
          Marking = bias "tech" "Marking"
          Heading = bias "tech" "Heading"
          FreeKick = normalInt 8.0 4.0 1 20
          Penalty = normalInt 10.0 3.0 1 20 }

    let private buildMental (currentSkill: int) : MentalStats =
        let baseAbility = float currentSkill / 10.0

        { Aggression = normalInt 10.0 5.0 1 20
          Composure = normalInt baseAbility 3.0 1 20
          Vision = normalInt baseAbility 4.0 1 20
          Positioning = normalInt baseAbility 3.0 1 20
          Bravery = normalInt 10.0 4.0 1 20
          WorkRate = normalInt 12.0 3.0 1 20
          Concentration = normalInt baseAbility 2.0 1 20
          Leadership = normalInt 5.0 5.0 1 20 }

    let private buildGoalkeeping (position: Position) (currentSkill: int) : GoalkeeperStats =
        let baseAbility = float currentSkill / 10.0
        let mean = if position = GK then baseAbility + 5.0 else 3.0

        { Reflexes = normalInt mean 2.0 1 20
          Handling = normalInt mean 2.0 1 20
          Kicking = normalInt mean 4.0 1 20
          OneOnOne = normalInt mean 3.0 1 20
          AerialReach = normalInt mean 3.0 1 20 }

    let private randomName (namePool: NamePool) =
        let firstName =
            pickWeighted (namePool.FirstNames |> List.map (fun name -> 1.0, name))

        let lastName = pickWeighted (namePool.LastNames |> List.map (fun name -> 1.0, name))
        $"{firstName} {lastName}"

    let create
        (id: PlayerId)
        (position: Position)
        (clubId: ClubId)
        (countryData: CountryData)
        (leagueLevel: int)
        (year: int)
        : Player =
        let currentSkill = currentSkillForLevel leagueLevel
        let age = ageForPosition position
        let height, weight = heightWeightFor position

        { Id = id
          Name = randomName countryData.Names
          Birthday = DateTime(year - age, normalInt 6.0 3.0 1 12, normalInt 15.0 8.0 1 28)
          Nationality = countryData.Country.Code
          Position = position
          PreferredFoot = preferredFootFor position
          Height = height
          Weight = weight
          Physical = buildPhysical (attributeBiasFor position currentSkill)
          Technical = buildTechnical (attributeBiasFor position currentSkill)
          Mental = buildMental currentSkill
          Goalkeeping = buildGoalkeeping position currentSkill
          Condition = normalInt 95.0 5.0 75 100
          MatchFitness = normalInt 90.0 8.0 60 100
          Morale = normalInt 65.0 10.0 40 90
          Status = Available
          CurrentSkill = currentSkill
          PotentialSkill = potentialSkillFor currentSkill age
          Reputation = currentSkill * 5
          Affiliation =
            Contracted(
                clubId,
                { Salary = Player.playerSalary currentSkill
                  ExpiryYear = contractExpiryFor year age }
            )
          TrainingSchedule =
            { Focus = TrainingFocus.TrainingAllRound
              Intensity = TrainingIntensity.TrainingNormal }
          ExperienceModifiers = ExperienceModifiers.defaultModifiers }
