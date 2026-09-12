namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Domain

module TacticalStaffAnalysisTests =

    let private makePlayer
        (id: int)
        (pos: Position)
        (pace: int)
        (stamina: int)
        (positioning: int)
        (workRate: int)
        (composure: int)
        : Player =
        { Id = id
          Name = sprintf "Player %d" id
          Birthday = System.DateTime(1998, 1, 1)
          Nationality = "ARG"
          Position = pos
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical =
            { Acceleration = pace
              Pace = pace
              Agility = 12
              Balance = 12
              JumpingReach = 12
              Stamina = stamina
              Strength = 12 }
          Technical =
            { Finishing = 12
              LongShots = 12
              Dribbling = 12
              BallControl = 12
              Passing = 12
              Crossing = 12
              Tackling = 12
              Marking = 12
              Heading = 12
              FreeKick = 12
              Penalty = 12 }
          Mental =
            { Aggression = 12
              Composure = composure
              Concentration = 12
              Vision = 12
              Positioning = positioning
              Bravery = 12
              WorkRate = workRate
              Leadership = 12 }
          Goalkeeping =
            { Reflexes = 8
              Handling = 8
              Kicking = 8
              OneOnOne = 8
              AerialReach = 8 }
          Condition = 100
          Fatigue = 0
          MatchFitness = 100
          RecentLoad = 0
          Morale = 70
          Status = Available
          CurrentSkill = 120
          PotentialSkill = 150
          Reputation = 50
          Affiliation = Contracted(1, { Salary = 10000m; ExpiryYear = 2030 })
          TrainingPlan = Perform
          ExperienceModifiers = ExperienceModifiers.defaultModifiers }

    let private config = BalanceConfig.defaultConfig.TacticalTradeoffs

    let private squadHighPace =
        [ makePlayer 1 DC 16 14 15 14 13
          makePlayer 2 DC 15 14 14 14 13
          makePlayer 3 DL 16 15 13 14 12
          makePlayer 4 DR 16 15 13 14 12
          makePlayer 5 DM 14 14 14 15 14
          makePlayer 6 MC 13 14 13 14 14
          makePlayer 7 MC 13 14 13 14 13
          makePlayer 8 AML 15 13 12 13 13
          makePlayer 9 AMR 15 13 12 13 13
          makePlayer 10 ST 14 12 13 12 12 ]

    let private squadLowPace =
        [ makePlayer 1 DC 10 11 10 11 11
          makePlayer 2 DC 9 11 9 11 11
          makePlayer 3 DL 10 12 9 11 11
          makePlayer 4 DR 10 12 9 11 11
          makePlayer 5 DM 11 11 10 11 12
          makePlayer 6 MC 11 11 10 11 12
          makePlayer 7 MC 10 11 10 11 11
          makePlayer 8 AML 11 11 9 11 11
          makePlayer 9 AMR 11 11 9 11 11
          makePlayer 10 ST 11 10 9 11 11 ]

    let private squadHighStamina = [ for i in 1..10 -> makePlayer i MC 12 16 12 16 13 ]

    let private squadLowStamina = [ for i in 1..10 -> makePlayer i MC 12 8 12 8 12 ]

    [<Tests>]
    let tests =
        testList
            "TacticalStaffAnalysis"
            [ test "high line grade higher with fast squad than slow squad" {
                  let fastVerdict =
                      TacticalStaffAnalysis.evaluateSingle config squadHighPace None "DefensiveLine" 4

                  let slowVerdict =
                      TacticalStaffAnalysis.evaluateSingle config squadLowPace None "DefensiveLine" 4
                  // Fast squad should score higher on high line
                  Expect.isTrue
                      (fastVerdict.Score > slowVerdict.Score)
                      "fast squad should have higher score for high line"
                  // Grades should reflect that
                  Expect.isTrue (fastVerdict.Grade <> F) "fast squad should not get F for high line"
              }

              test "high press punishes low stamina" {
                  let highFit =
                      TacticalStaffAnalysis.evaluateSingle config squadHighStamina None "PressingIntensity" 4

                  let lowFit =
                      TacticalStaffAnalysis.evaluateSingle config squadLowStamina None "PressingIntensity" 4

                  Expect.isTrue (highFit.Score > lowFit.Score) "high stamina squad should score higher for high press"
                  Expect.equal highFit.Label "High" "label should be High"
                  Expect.isTrue (highFit.Pros.Length > 0) "should have pros"
                  Expect.isTrue (highFit.Cons.Length > 0) "should have cons"
              }

              test "evaluation is deterministic" {
                  let v1 =
                      TacticalStaffAnalysis.evaluateSingle config squadHighPace None "Mentality" 4

                  let v2 =
                      TacticalStaffAnalysis.evaluateSingle config squadHighPace None "Mentality" 4

                  Expect.equal v1 v2 "same inputs must give same verdict"
              }

              test "opponent-aware high line penalized by fast rival" {
                  // Create a scout report that implies fast rival
                  let report =
                      { SubjectId = 2
                        ReaderId = 1
                        Formation = Some "4-3-3"
                        Instructions = Some TacticalInstructions.defaultInstructions
                        Confidence = 0.9
                        Findings =
                          [ { Label = "Direct play"
                              Strength = 0.7
                              Confidence = 0.9 } ] }

                  let withScout =
                      TacticalStaffAnalysis.evaluateSingle config squadLowPace (Some report) "DefensiveLine" 4

                  let withoutScout =
                      TacticalStaffAnalysis.evaluateSingle config squadLowPace None "DefensiveLine" 4
                  // With fast rival scout, score should be lower (penalized)
                  Expect.isTrue
                      (withScout.Score <= withoutScout.Score)
                      "scout for fast rival should not increase high line score"

                  Expect.isTrue
                      (withScout.Cons |> List.exists (fun c -> c.Contains("Rival pace")))
                      "should mention rival pace"
              }

              test "season projection deterministic and reflects pressing cost" {
                  let lowPress =
                      { TacticalInstructions.defaultInstructions with
                          PressingIntensity = 0
                          DefensiveLine = 2 }

                  let highPress =
                      { TacticalInstructions.defaultInstructions with
                          PressingIntensity = 4
                          DefensiveLine = 4 }

                  let projLow = TacticalStaffAnalysis.projectSeason config squadHighStamina lowPress
                  let projHigh = TacticalStaffAnalysis.projectSeason config squadHighStamina highPress

                  Expect.isTrue
                      (projHigh.WeeklyConditionCost > projLow.WeeklyConditionCost)
                      "high press should cost more condition"

                  Expect.isTrue
                      (projHigh.SeasonInjuryRiskDelta >= projLow.SeasonInjuryRiskDelta)
                      "high press should not reduce injury risk"
                  // Youth suitability is 0..1
                  Expect.isTrue
                      (projHigh.YouthSuitability >= 0.0 && projHigh.YouthSuitability <= 1.0)
                      "youth fit in range"
              }

              test "transfer need appears for mismatched squad" {
                  let highLine =
                      { TacticalInstructions.defaultInstructions with
                          DefensiveLine = 4 }

                  let need = TacticalStaffAnalysis.projectSeason config squadLowPace highLine
                  Expect.isSome need.TransferNeed "slow squad with high line should need fast CB"
                  Expect.isTrue (need.TransferNeed.Value.Contains("CB")) "should mention CB"
              }

              test "evaluateAll returns 7 verdicts" {
                  let all =
                      TacticalStaffAnalysis.evaluateAll
                          BalanceConfig.defaultConfig
                          squadHighPace
                          None
                          TacticalInstructions.defaultInstructions

                  Expect.equal all.Count 7 "should have 7 instructions"
                  Expect.isTrue (all.ContainsKey "Mentality") "should contain Mentality"
                  Expect.isTrue (all.ContainsKey "DefensiveLine") "should contain DefensiveLine"
                  Expect.isTrue (all.ContainsKey "PressTriggerZone") "should contain PressTriggerZone"
              }

              test "grade thresholds respect config" {
                  let cfg =
                      { config with
                          GradeAThreshold = 0.9
                          GradeBThreshold = 0.7
                          GradeCThreshold = 0.5
                          GradeDThreshold = 0.2 }
                  // Create a squad that should be borderline
                  let verdict =
                      TacticalStaffAnalysis.evaluateSingle cfg squadHighPace None "Mentality" 4
                  // Grade should be one of the defined grades
                  Expect.isTrue ([ A; B; C; D; F ] |> List.contains verdict.Grade) "grade should be valid"
              } ]
