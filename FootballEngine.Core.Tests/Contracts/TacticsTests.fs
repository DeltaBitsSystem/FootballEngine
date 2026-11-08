namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Domain

/// Spec for the tactics overhaul: engine-scale counter-planning, the three
/// live-decision tradeoff knobs, role suitability/Quick Pick, tactical
/// familiarity validation and press-trigger-zone analysis. Every expectation
/// is hand-computed from the documented semantics, never copied from output.
module TacticsTests =

    let private engineTactics
        (line: float32)
        (press: float32)
        (width: float32)
        (tempo: float32)
        (directness: float32)
        : TacticsConfig =
        // Intensities run 0..1 with the line in meters, matching
        // MatchContextBuilder.tacticsOf output for mid sliders.
        { Formation = "4-3-3"
          DefensiveLine = line
          PressIntensity = press
          Width = width
          Tempo = tempo
          Directness = directness
          Mentality = 0.5f
          PressTriggerZone = 0.5f }

    let private basePhysical =
        { Acceleration = 10
          Pace = 10
          Agility = 10
          Balance = 10
          JumpingReach = 10
          Stamina = 10
          Strength = 10 }

    let private baseTechnical =
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

    let private baseKeeping =
        { Reflexes = 4
          Handling = 4
          Kicking = 4
          OneOnOne = 4
          AerialReach = 4 }

    let private makePlayer
        (id: int)
        (pos: Position)
        (phys: PhysicalStats)
        (tech: TechnicalStats)
        (ment: MentalStats)
        (gk: GoalkeeperStats)
        : Player =
        { Id = id
          Name = sprintf "Player %d" id
          Birthday = System.DateTime(1998, 1, 1)
          Nationality = "ARG"
          Position = pos
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical = phys
          Technical = tech
          Mental = ment
          Goalkeeping = gk
          Condition = 100
          Fatigue = 0
          MatchFitness = 100
          RecentLoad = 0
          Morale = 70
          Status = Available
          CurrentSkill = 120
          PotentialSkill = 150
          Reputation = 50
          Affiliation = FreeAgent
          TrainingPlan = Perform
          ExperienceModifiers = ExperienceModifiers.defaultModifiers
          Unrest = None }

    // A genuine finisher: elite finishing/pace, no defensive craft.
    let private striker =
        makePlayer
            9
            ST
            { basePhysical with
                Pace = 17
                Strength = 14 }
            { baseTechnical with
                Finishing = 18
                Heading = 14
                BallControl = 15
                LongShots = 15
                Marking = 6
                Tackling = 6 }
            { baseMental with
                Composure = 14
                Positioning = 14 }
            baseKeeping

    // A genuine keeper: elite shot-stopping, outfield attributes at base.
    let private keeper =
        makePlayer
            1
            GK
            basePhysical
            baseTechnical
            { baseMental with
                Positioning = 12
                Composure = 12 }
            { Reflexes = 18
              Handling = 18
              Kicking = 16
              OneOnOne = 18
              AerialReach = 18 }

    let private stopper =
        makePlayer
            4
            DC
            { basePhysical with
                Strength = 15
                Pace = 12 }
            { baseTechnical with
                Marking = 16
                Tackling = 16
                Heading = 14
                Finishing = 8 }
            { baseMental with Positioning = 14 }
            baseKeeping

    let private slot (idx: int) (role: Position) : LineupSlot =
        { Index = idx
          Role = role
          X = 0.5
          Y = 0.5
          PlayerId = None }

    let tacticalPlanTests =
        testList
            "FootballEngine.Core.Contracts.TacticalPlan"
            [ test "a lineup resolves one plan for simple and advanced consumers" {
                  let lineup =
                      { Formation = F433
                        Tactics = Pressing
                        Instructions = Some TacticalInstructions.defaultInstructions
                        Slots = [] }

                  let plan = TacticalPlan.fromLineup lineup

                  let changed =
                      TacticalPlan.withInstructions { plan.Instructions with Tempo = 4 } plan

                  Expect.equal
                      (TacticalPlan.signature plan)
                      ("F433", plan.Instructions)
                      "signature is from the shared plan"

                  Expect.equal changed.Formation plan.Formation "editing instructions preserves identity"
                  Expect.equal changed.Instructions.Tempo 4 "advanced edit updates the same plan"
              }

              test "every registered instruction exposes a pure tradeoff and explanation" {
                  Expect.equal
                      TacticalInstructionRegistry.all.Length
                      InstructionSpecs.all.Length
                      "registry covers every instruction"

                  for spec, tradeoff in TacticalInstructionRegistry.all do
                      Expect.isNonEmpty tradeoff.Condition (spec.Id + " condition")
                      Expect.isNonEmpty tradeoff.Advantage (spec.Id + " advantage")
                      Expect.isNonEmpty tradeoff.Cost (spec.Id + " cost")
                      Expect.isTrue (tradeoff.Parameters.Length > 0) (spec.Id + " parameters")

                      Expect.isNonEmpty
                          (TacticalInstructionRegistry.explain TacticalInstructions.defaultInstructions spec.Id)
                          (spec.Id + " explanation")
              }

              test "registry explanation is deterministic" {
                  let a =
                      TacticalInstructionRegistry.explain TacticalInstructions.defaultInstructions "PressingIntensity"

                  let b =
                      TacticalInstructionRegistry.explain TacticalInstructions.defaultInstructions "PressingIntensity"

                  Expect.equal a b "same plan gives same explanation"
              }

              test "contextual playbooks reuse the shared plan and declare commitments" {
                  let basePlan =
                      { Formation = F4231
                        Tactics = Balanced
                        Instructions = TacticalInstructions.defaultInstructions }

                  let playbook = TacticalPlaybooks.tryFind "protect-lead" |> Option.get
                  let applied = playbook.Apply basePlan

                  Expect.equal applied.Formation basePlan.Formation "playbook preserves formation"
                  Expect.equal applied.Tactics basePlan.Tactics "playbook preserves team identity"
                  Expect.notEqual applied.Instructions basePlan.Instructions "playbook changes instructions"
                  Expect.isNonEmpty playbook.Effects "playbook exposes effects"
                  Expect.isNonEmpty playbook.Commitments "playbook exposes commitments"
              }

              test "playbook recommendations require known evidence" {
                  let close = TacticalPlaybooks.tryFind "contain-fast-attack" |> Option.get

                  let unknown =
                      { IsLeading = false
                        IsTrailing = false
                        OpponentHasFastAttack = None
                        OpponentUsesLowBlock = None
                        TeamAverageStamina = None }

                  let known =
                      { unknown with
                          OpponentHasFastAttack = Some true }

                  Expect.isNone (close.Recommendation unknown) "unknown scouting cannot recommend a counter"
                  Expect.isSome (close.Recommendation known) "known scouting can recommend a counter"
              } ]

    let tacticsTests =
        testList
            "FootballEngine.Core.Contracts.Tactics"
            [ test "counter-plan deltas land in engine scales" {
                  let t =
                      CounterPlan.apply
                          BalanceConfig.defaultCounter
                          (engineTactics 40.0f 0.5f 0.5f 0.5f 0.5f)
                          { Active = true
                            SideBias = 0.0
                            PressBoost = 0.4
                            LineDrop = 5.0
                            WidthTighten = 0.2
                            TempoShift = 0.2
                            DirectnessShift = 0.4 }

                  // boosts convert from slider units (/4); the line drop is already meters
                  Expect.floatClose Accuracy.medium (float t.PressIntensity) 0.6 "press boosted by boost/4"
                  Expect.floatClose Accuracy.medium (float t.DefensiveLine) 35.0 "line dropped in meters"
                  Expect.floatClose Accuracy.medium (float t.Width) 0.45 "width tightened by tighten/4"
                  Expect.floatClose Accuracy.medium (float t.Tempo) 0.45 "tempo lowered by shift/4"
                  Expect.floatClose Accuracy.medium (float t.Directness) 0.6 "directness raised by shift/4"
                  Expect.equal t.Formation "4-3-3" "formation unchanged"
                  Expect.equal t.Mentality 0.5f "counter never touches mentality"
                  Expect.equal t.PressTriggerZone 0.5f "counter never touches trigger zone"
              }

              test "counter-plan apply stays in engine range" {
                  let t =
                      CounterPlan.apply
                          BalanceConfig.defaultCounter
                          (engineTactics 16.0f 0.9f 0.05f 0.05f 0.95f)
                          { Active = true
                            SideBias = 0.0
                            PressBoost = 1.0
                            LineDrop = 10.0
                            WidthTighten = 1.0
                            TempoShift = 1.0
                            DirectnessShift = 1.0 }

                  Expect.floatClose Accuracy.medium (float t.PressIntensity) 1.0 "press capped"
                  Expect.floatClose Accuracy.medium (float t.DefensiveLine) 15.0 "line floored at 15m"
                  Expect.floatClose Accuracy.medium (float t.Width) 0.0 "width floored"
                  Expect.floatClose Accuracy.medium (float t.Tempo) 0.0 "tempo floored"
                  Expect.floatClose Accuracy.medium (float t.Directness) 1.0 "directness capped"

                  for name, v in
                      [ "press", t.PressIntensity
                        "width", t.Width
                        "tempo", t.Tempo
                        "directness", t.Directness ] do
                      Expect.isTrue (v >= 0.0f && v <= 1.0f) (sprintf "%s stays in 0..1" name)

                  Expect.isTrue (t.DefensiveLine >= 15.0f) "line stays above the playable floor"
              }

              test "default tradeoffs carry the three live-decision knobs" {
                  let t = BalanceConfig.defaultTacticalTradeoffs
                  Expect.floatClose Accuracy.medium t.TempoCounterUrgencyShift 0.3 "counter urgency shift"
                  Expect.floatClose Accuracy.medium t.DirectnessLongBallUtility 0.12 "long-ball utility"
                  Expect.floatClose Accuracy.medium t.MentalityRiskThresholdShift 0.08 "risk threshold shift"
              }

              test "removed defensive-shape knobs are gone" {
                  let names = typeof<TacticalTradeoffConfig>.GetProperties() |> Array.map _.Name

                  for gone in
                      [ "DefensiveShapeManMarkingTightness"
                        "ManStaminaCost"
                        "ManFoulRisk"
                        "ZonalCompactness" ] do
                      Expect.isFalse
                          (names |> Array.exists (fun n -> n.Contains gone))
                          (sprintf "%s must not exist" gone)

                  for present in
                      [ "TempoCounterUrgencyShift"
                        "DirectnessLongBallUtility"
                        "MentalityRiskThresholdShift" ] do
                      Expect.isTrue (names |> Array.contains present) (sprintf "%s must exist" present)
              }

              test "striker rates higher at ST than at DC" {
                  let atStriker = TacticalSuitability.stars striker ST
                  let atCenterBack = TacticalSuitability.stars striker DC
                  Expect.isTrue (atStriker > atCenterBack) "finishing matters up front, not at the back"
                  Expect.isTrue (atStriker >= 0.0 && atStriker <= 5.0) "stars stay in 0..5"
              }

              test "keeper rates far higher in goal than outfield" {
                  let inGoal = TacticalSuitability.stars keeper GK
                  let atStriker = TacticalSuitability.stars keeper ST
                  let atCenterBack = TacticalSuitability.stars keeper DC
                  Expect.isTrue (inGoal > atStriker) "keeping attributes collapse outfield"
                  Expect.isTrue (inGoal > atCenterBack) "a keeper is no center back"
                  Expect.isTrue (inGoal >= 0.0 && inGoal <= 5.0) "stars stay in 0..5"
              }

              test "quick pick fills the GK slot with the keeper" {
                  let picks =
                      TacticalSuitability.quickPick [ slot 0 GK; slot 1 ST; slot 2 DC ] [ striker; keeper; stopper ]

                  Expect.equal picks.Count 3 "no slot empty when enough players"
                  Expect.equal picks.[0] keeper.Id "the keeper takes the gloves"
                  Expect.equal picks.[1] striker.Id "the finisher leads the line"
                  Expect.equal picks.[2] stopper.Id "the stopper covers the back"
              }

              test "quick pick is deterministic and degrades gracefully" {
                  let slots = [ slot 0 GK; slot 1 ST; slot 2 DC ]
                  let players = [ striker; keeper; stopper ]

                  Expect.equal
                      (TacticalSuitability.quickPick slots players)
                      (TacticalSuitability.quickPick slots players)
                      "same inputs give same map"

                  let short = TacticalSuitability.quickPick slots [ keeper ]
                  Expect.equal short.Count 1 "fewer players than slots fills what it can"
                  Expect.equal short.[0] keeper.Id "the lone keeper still takes the gloves"
              }

              test "familiarity memory starts empty and validates its gain" {
                  Expect.equal
                      CoordinationMemory.defaultMemory.TacticalFamiliarity
                      Map.empty
                      "new clubs start with no drilled shape"

                  Expect.isEmpty
                      (ConfigValidation.validateTeam BalanceConfig.defaultTeam)
                      "default team config validates clean"

                  Expect.isEmpty
                      (ConfigValidation.validateTactics BalanceConfig.defaultTacticalTradeoffs)
                      "default tactics validate clean"

                  Expect.isNonEmpty
                      (ConfigValidation.validateTeam
                          { BalanceConfig.defaultTeam with
                              TacticalFamiliarityGain = -0.1 })
                      "negative familiarity gain rejected"

                  Expect.isNonEmpty
                      (ConfigValidation.validateTeam
                          { BalanceConfig.defaultTeam with
                              TacticalFamiliarityGain = 1.5 })
                      "familiarity gain above one rejected"
              }

              test "press trigger zone analysis labels extremes and middle" {
                  let tradeoffs = BalanceConfig.defaultTacticalTradeoffs

                  let high =
                      TacticalStaffAnalysis.evaluateSingle tradeoffs [] None "PressTriggerZone" 4

                  let mid =
                      TacticalStaffAnalysis.evaluateSingle tradeoffs [] None "PressTriggerZone" 2

                  let low =
                      TacticalStaffAnalysis.evaluateSingle tradeoffs [] None "PressTriggerZone" 0

                  Expect.equal high.Label "High" "4 presses everywhere"
                  Expect.equal mid.Label "Balanced" "2 holds the middle third"
                  Expect.equal low.Label "Low" "0 drops into a block"
                  Expect.equal high.InstructionId "PressTriggerZone" "verdict carries its id"
              } ]
