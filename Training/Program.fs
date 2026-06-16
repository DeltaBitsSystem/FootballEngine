namespace Training

open System
open System.IO
open System.Text.Json
open FootballEngine
open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Simulation
open FootballEngine.Types

module Program =

    let private makeTestPlayer (id: PlayerId) (pos: Position) (skill: int) : Player =
        let s = Math.Clamp(skill, 1, 20)

        { Id = id
          Name = $"P{id}"
          Birthday = System.DateTime(1995, 1, 1)
          Nationality = "AR"
          Position = pos
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical =
            { Acceleration = s
              Pace = s
              Agility = s
              Balance = s
              JumpingReach = s
              Stamina = s
              Strength = s }
          Technical =
            { Finishing = s
              LongShots = s
              Dribbling = s
              BallControl = s
              Passing = s
              Crossing = s
              Tackling = s
              Marking = s
              Heading = s
              FreeKick = s
              Penalty = s }
          Mental =
            { Aggression = s
              Composure = s
              Vision = s
              Positioning = s
              Bravery = s
              WorkRate = s
              Concentration = s
              Leadership = s }
          Goalkeeping =
            { Reflexes = s
              Handling = s
              Kicking = s
              OneOnOne = s
              AerialReach = s }
          Condition = 100
          MatchFitness = 100
          Morale = 50
          Status = Available
          CurrentSkill = skill
          PotentialSkill = skill
          Reputation = 50
          Affiliation = Contracted(1, { Salary = 1000m; ExpiryYear = 2030 })
          TrainingSchedule =
            { Focus = TrainingAllRound
              Intensity = TrainingLight }
          ExperienceModifiers = ExperienceModifiers.defaultModifiers }

    let private makeTestClub (id: ClubId) (name: string) (playerIds: PlayerId list) : Club =
        { Id = id
          Name = name
          Nationality = "AR"
          Reputation = 1500
          PlayerIds = playerIds
          StaffIds = []
          Budget = 10_000_000m
          Morale = 50
          BoardObjective = LeagueObjective MidTable
          CoordinationMemory = CoordinationMemory.defaultMemory }

    let private makeTestStaff (id: StaffId) (clubId: ClubId) (playerIds: PlayerId list) : Staff =
        let slots =
            playerIds
            |> List.mapi (fun i pid ->
                { Index = i
                  Role =
                    match i with
                    | 0 -> GK
                    | 1 -> DL
                    | 2 -> DC
                    | 3 -> DC
                    | 4 -> DR
                    | 5 -> ML
                    | 6 -> MC
                    | 7 -> MC
                    | 8 -> MR
                    | 9 -> ST
                    | 10 -> ST
                    | _ -> MC
                  X = 0.5
                  Y = 0.5
                  PlayerId = Some pid })
            |> List.take 11

        let lineup =
            { Formation = F442
              Tactics = Balanced
              Instructions = Some TacticalInstructions.defaultInstructions
              Slots = slots }

        { Id = id
          Name = $"Coach{id}"
          Birthday = System.DateTime(1970, 1, 1)
          Nationality = "AR"
          Role = HeadCoach
          Attributes =
            { Coaching =
                { Attacking = 15
                  Defending = 14
                  Fitness = 12
                  Goalkeeping = 10
                  Mental = 14
                  SetPieces = 13
                  Tactical = 15
                  Technical = 14
                  WorkingWithYoungsters = 10
                  PreferredFormation = None
                  Lineup = Some lineup }
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
            { JudgingPlayerAbility = 14
              JudgingPlayerPotential = 13
              JudgingStaffAbility = 12
              Negotiating = 13
              TacticalKnowledge = 15 }
          Mental =
            { Adaptability = 14
              Determination = 15
              LevelOfDiscipline = 14
              PeopleManagement = 14
              Motivating = 15 }
          CurrentSkill = 15
          PotentialSkill = 15
          Badge = ProLicense
          Reputation = 1500
          Contract =
            Some
                { ClubId = clubId
                  Salary = 50000m
                  ExpiryYear = 2030 }
          Status = StaffStatus.Active
          TrophiesWon = 0
          SeasonsManaged = 5 }

    let private createTestData () =
        let homePlayers =
            [| makeTestPlayer 1 GK 15
               makeTestPlayer 2 DL 12
               makeTestPlayer 3 DC 13
               makeTestPlayer 4 DC 13
               makeTestPlayer 5 DR 12
               makeTestPlayer 6 DM 12
               makeTestPlayer 7 MC 13
               makeTestPlayer 8 MC 13
               makeTestPlayer 9 AML 14
               makeTestPlayer 10 AMC 14
               makeTestPlayer 11 ST 15 |]

        let awayPlayers =
            [| makeTestPlayer 12 GK 15
               makeTestPlayer 13 DL 12
               makeTestPlayer 14 DC 13
               makeTestPlayer 15 DC 13
               makeTestPlayer 16 DR 12
               makeTestPlayer 17 DM 12
               makeTestPlayer 18 MC 13
               makeTestPlayer 19 MC 13
               makeTestPlayer 20 AML 14
               makeTestPlayer 21 AMC 14
               makeTestPlayer 22 ST 15 |]

        let homeClub =
            makeTestClub 100 "Home" (homePlayers |> Array.map _.Id |> Array.toList)

        let awayClub =
            makeTestClub 200 "Away" (awayPlayers |> Array.map _.Id |> Array.toList)

        let allPlayers = Array.append homePlayers awayPlayers
        let players = allPlayers |> Array.map (fun p -> p.Id, p) |> Map.ofArray

        let homePlayerIds = homePlayers |> Array.map _.Id |> Array.toList
        let awayPlayerIds = awayPlayers |> Array.map _.Id |> Array.toList
        let homeCoach = makeTestStaff 1000 homeClub.Id homePlayerIds
        let awayCoach = makeTestStaff 1001 awayClub.Id awayPlayerIds
        let staff = [ homeCoach; awayCoach ] |> List.map (fun s -> s.Id, s) |> Map.ofList

        let pw = BalanceConfig.defaultConfig.ProfileWeights
        let profileMap = players |> Map.map (fun _ p -> Player.profile p pw)

        homeClub, awayClub, players, staff, profileMap

    let runCuration
        (weightsPath: string)
        (outputPath: string)
        (iterations: int)
        (matchesPerIteration: int)
        (validationMatches: int)
        (epsilon: float)
        (learningRate: float)
        : unit =

        printfn "=== Curation Tool ==="
        printfn "Input:  %s" weightsPath
        printfn "Output: %s" outputPath

        let initialConfig =
            match WeightsLoader.load weightsPath with
            | Ok w ->
                printfn "Loaded %s" weightsPath
                w
            | Error e ->
                printfn "WARNING: %s — using defaults" e
                BalanceConfig.defaultConfig

        let home, away, players, staff, profileMap = createTestData ()
        let targets = Training.CalibrationTargets.targetsDefault

        let errorFn (config: BalanceConfig) : float =
            let results = SimulatorRunner.runBatch config 5 home away players staff profileMap

            if List.isEmpty results then
                999.0
            else
                let m, _ = MetricsAggregator.aggregate results
                fst (ErrorCalculator.computeError m targets)

        let initialResults =
            SimulatorRunner.runBatch initialConfig matchesPerIteration home away players staff profileMap

        let initialMetrics, _ = MetricsAggregator.aggregate initialResults
        let baseError, baseDeltas = ErrorCalculator.computeError initialMetrics targets

        printfn "\n=== Baseline ==="

        for d in baseDeltas do
            let status =
                if d.IsHigh then "HIGH"
                elif d.IsLow then "LOW"
                else "OK"

            printfn "  %-25s %.3f (target: %.2f-%.2f) %s" d.Metric d.Observed d.TargetMin d.TargetMax status

        printfn "  Error: %.4f" baseError

        let rec loop i config currentError =
            if i >= iterations then
                printfn "\nDone after %d iterations. Error: %.4f" iterations currentError
                config
            else
                printfn "\n--- Iteration %d/%d (error: %.4f) ---" (i + 1) iterations currentError
                let tuned = GradientTuner.tune epsilon learningRate config errorFn

                let valResults =
                    SimulatorRunner.runBatch tuned validationMatches home away players staff profileMap

                let newError, deltas =
                    ErrorCalculator.computeError (fst (MetricsAggregator.aggregate valResults)) targets

                let significant = Calibrator.getSignificant 0.1 deltas

                for d in significant do
                    printfn "  %s: %.3f (target: %.2f-%.2f)" d.Metric d.Observed d.TargetMin d.TargetMax

                if newError < currentError then
                    printfn "  Improved: %.4f -> %.4f" currentError newError
                    loop (i + 1) tuned newError
                else
                    printfn "  No improvement, keeping current"
                    loop (i + 1) config currentError

        let finalConfig = loop 0 initialConfig baseError

        let opts = JsonSerializerOptions(WriteIndented = true)
        let json = JsonSerializer.Serialize(finalConfig, opts)
        File.WriteAllText(outputPath, json)
        printfn "\nSaved suggested weights to %s" outputPath
        printfn "Review and promote to weights.json manually if satisfied."

    [<EntryPoint>]
    let main argv =
        let weightsPath = if argv.Length > 0 then argv.[0] else "weights.json"
        let outputPath = if argv.Length > 1 then argv.[1] else "weights_curated.json"
        let iterations = if argv.Length > 2 then int argv.[2] else 10
        let matchesPerIt = if argv.Length > 3 then int argv.[3] else 20
        let valMatches = if argv.Length > 4 then int argv.[4] else 10
        let epsilon = if argv.Length > 5 then float argv.[5] else 0.01
        let learningRate = if argv.Length > 6 then float argv.[6] else 0.05
        runCuration weightsPath outputPath iterations matchesPerIt valMatches epsilon learningRate
        0
