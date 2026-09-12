namespace FootballEngine.Core.Match

open FootballEngine.Domain
open FootballEngine.Core.Match

/// Staff grade — deterministic, derived from TacticalTradeoffConfig thresholds.
type StaffGrade =
    | A
    | B
    | C
    | D
    | F

module StaffGrade =

    let toString =
        function
        | A -> "A"
        | B -> "B"
        | C -> "C"
        | D -> "D"
        | F -> "F"

    let color =
        function
        | A -> "#00d492"
        | B -> "#3b82f6"
        | C -> "#f59e0b"
        | D -> "#f97316"
        | F -> "#ef4444"

/// One verdict per instruction value — fully deterministic from squad + scout + config.
type InstructionVerdict =
    { InstructionId: string
      Value: int
      Label: string
      Grade: StaffGrade
      Pros: string list
      Cons: string list
      Metrics: (string * float) list
      Confidence: float
      // Raw numeric score before thresholding, for testing
      Score: float }

/// Long-horizon season impact — shown as 2 bullets + mini-bars.
type SeasonProjection =
    { WeeklyConditionCost: float
      SeasonInjuryRiskDelta: float
      YouthSuitability: float // 0..1, how well youth fit this style
      TransferNeed: string option
      ChemistryDelta: float }

module TacticalStaffAnalysis =

    // -----------------------------------------------------------------------
    // Squad aggregates — pure, deterministic
    // -----------------------------------------------------------------------

    let private avg (players: Player list) (f: Player -> float) =
        if players.IsEmpty then 0.0 else players |> List.averageBy f

    let private squadMetrics (squad: Player list) =
        // Only outfield for most metrics; GK filtered where relevant
        let outfield = squad |> List.filter (fun p -> p.Position <> GK)
        let all = squad

        {| AvgPace = avg outfield (fun p -> float p.Physical.Pace)
           AvgStamina = avg all (fun p -> float p.Physical.Stamina)
           AvgStrength = avg all (fun p -> float p.Physical.Strength)
           AvgPositioning = avg outfield (fun p -> float p.Mental.Positioning)
           AvgWorkRate = avg all (fun p -> float p.Mental.WorkRate)
           AvgComposure = avg all (fun p -> float p.Mental.Composure)
           AvgConcentration = avg all (fun p -> float p.Mental.Concentration)
           AvgBravery = avg all (fun p -> float p.Mental.Bravery)
           AvgVision = avg all (fun p -> float p.Mental.Vision)
           AvgPassing = avg outfield (fun p -> float p.Technical.Passing)
           AvgMarking = avg outfield (fun p -> float p.Technical.Marking)
           AvgTackling = avg outfield (fun p -> float p.Technical.Tackling)
           SquadSize = float squad.Length |}

    let private gradeOf (config: TacticalTradeoffConfig) (score: float) =
        if score >= config.GradeAThreshold then A
        elif score >= config.GradeBThreshold then B
        elif score >= config.GradeCThreshold then C
        elif score >= config.GradeDThreshold then D
        else F

    // -----------------------------------------------------------------------
    // Per-instruction deterministic evaluation
    // -----------------------------------------------------------------------

    let private evaluateMentality
        (config: TacticalTradeoffConfig)
        (m:
            {| AvgComposure: float
               AvgPace: float |})
        (value: int)
        =
        // 0=Defensive 2=Balanced 4=Attacking
        match value with
        | 0 ->
            let score = (m.AvgComposure / 20.0) * 0.6 + 0.2 // defensive suits low composure
            let grade = gradeOf config score

            { InstructionId = "Mentality"
              Value = value
              Label = "Defensive"
              Grade = grade
              Confidence = 0.9
              Score = score
              Pros =
                [ sprintf "Concedes %.2f xGC less (compact block)" config.MentalityDefensiveXgConcededDelta
                  sprintf "Suits low composure (%.1f avg) — fewer risky passes" m.AvgComposure ]
              Cons =
                [ sprintf "-%.2f xG for vs balanced" config.MentalityAttackingXgDelta
                  "Requires patience; crowd may demand more" ]
              Metrics =
                [ "xGC", -config.MentalityDefensiveXgConcededDelta
                  "xG", -config.MentalityAttackingXgDelta ] }
        | 4 ->
            let score = (m.AvgPace / 20.0) * 0.5 + (m.AvgComposure / 20.0) * 0.3
            let grade = gradeOf config score

            { InstructionId = "Mentality"
              Value = value
              Label = "Attacking"
              Grade = grade
              Confidence = 0.9
              Score = score
              Pros =
                [ sprintf "+%.2f xG from transitions (pace %.1f)" config.MentalityAttackingXgDelta m.AvgPace
                  "Extra runner in box" ]
              Cons =
                [ sprintf "+%.2f xGC conceded (exposed on loss)" config.MentalityAttackingXgConcededDelta
                  sprintf "Demands composure %.1f — low composure = turnovers" m.AvgComposure ]
              Metrics =
                [ "xG", config.MentalityAttackingXgDelta
                  "xGC", config.MentalityAttackingXgConcededDelta ] }
        | _ ->
            { InstructionId = "Mentality"
              Value = value
              Label = "Balanced"
              Grade = B
              Confidence = 0.9
              Score = 0.55
              Pros = [ "No bias — staff trusts the base plan" ]
              Cons = [ "No specialist edge vs extremes" ]
              Metrics = [ "xG", 0.0; "xGC", 0.0 ] }

    let private evaluateDefensiveLine
        (config: TacticalTradeoffConfig)
        (m:
            {| AvgPace: float
               AvgPositioning: float
               AvgStamina: float |})
        (value: int)
        (opponentPace: float option)
        =
        match value with
        | 4 ->
            let paceScore = m.AvgPace / 20.0
            let posScore = m.AvgPositioning / 20.0
            let baseScore = paceScore * 0.5 + posScore * 0.4
            // Opponent-aware penalty: if rival has fast strikers, high line is punished
            let oppPenalty =
                match opponentPace with
                | Some p when p >= 15.0 -> 0.25
                | Some p when p >= 13.5 -> 0.12
                | _ -> 0.0

            let score = max 0.0 (baseScore - oppPenalty)
            let grade = gradeOf config score

            let oppText =
                match opponentPace with
                | Some p when p >= 15.0 -> sprintf "Rival pace %.1f — exposed on balls in behind" p
                | Some p -> sprintf "Rival pace %.1f — moderate risk" p
                | None -> "No scout data — staff unsure about balls in behind"

            { InstructionId = "DefensiveLine"
              Value = value
              Label = "High"
              Grade = grade
              Confidence = (if opponentPace.IsSome then 0.85 else 0.55)
              Score = score
              Pros =
                [ sprintf
                      "Compresses space, -%.2f xGC vs midfield build-up (positioning %.1f)"
                      config.DefensiveLineHighSprintCost
                      m.AvgPositioning
                  "Forces offsides" ]
              Cons =
                [ sprintf "+%.2f xGC on balls in behind" config.DefensiveLineHighXgBehind
                  oppText
                  sprintf
                      "Sprint distance +%.0f%% (stamina %.1f test)"
                      (config.DefensiveLineHighSprintCost * 100.0)
                      m.AvgStamina ]
              Metrics =
                [ "xGC behind", config.DefensiveLineHighXgBehind
                  "Sprint", config.DefensiveLineHighSprintCost
                  "Compactness", 0.15 ] }
        | 0 ->
            let score = (m.AvgStamina / 20.0) * 0.3 + 0.4 // deep suits low stamina
            let grade = gradeOf config score

            { InstructionId = "DefensiveLine"
              Value = value
              Label = "Deep"
              Grade = grade
              Confidence = 0.8
              Score = score
              Pros =
                [ sprintf "Protects vs pace, +%.2f compactness" config.DefensiveLineDeepCompactness
                  sprintf "Saves legs (stamina %.1f)" m.AvgStamina ]
              Cons =
                [ sprintf "+%.2f xGC from central overload" config.DefensiveLineDeepXgCentral
                  "Concedes territory" ]
              Metrics =
                [ "xGC central", config.DefensiveLineDeepXgCentral
                  "Compactness", config.DefensiveLineDeepCompactness ] }
        | _ ->
            { InstructionId = "DefensiveLine"
              Value = value
              Label = "Mid"
              Grade = B
              Confidence = 0.9
              Score = 0.55
              Pros = [ "Balanced risk" ]
              Cons = [ "No specialist edge" ]
              Metrics = [ "xGC", 0.0; "Sprint", 0.0 ] }

    let private evaluatePressing
        (config: TacticalTradeoffConfig)
        (m:
            {| AvgStamina: float
               AvgWorkRate: float
               AvgBravery: float |})
        (value: int)
        =
        match value with
        | 4 ->
            let score =
                (m.AvgStamina / 20.0) * 0.4
                + (m.AvgWorkRate / 20.0) * 0.4
                + (m.AvgBravery / 20.0) * 0.2

            let grade = gradeOf config score

            { InstructionId = "PressingIntensity"
              Value = value
              Label = "High"
              Grade = grade
              Confidence = 0.9
              Score = score
              Pros =
                [ sprintf "PPDA %.1f harder to play through" config.PressHighPpdaDelta
                  sprintf "Forces turnovers high (work rate %.1f)" m.AvgWorkRate ]
              Cons =
                [ sprintf "Sprint distance +%.0f%%" (config.PressHighSprintDistance * 100.0)
                  sprintf "Condition -%.0f per week (stamina %.1f)" config.PressHighConditionCost m.AvgStamina
                  sprintf "Injury risk +%.1f%% season" (config.PressHighInjuryRisk * 100.0) ]
              Metrics =
                [ "PPDA", config.PressHighPpdaDelta
                  "Sprint", config.PressHighSprintDistance
                  "Condition", -config.PressHighConditionCost ] }
        | 0 ->
            let score = 0.6 // low press always feasible
            let grade = gradeOf config score

            { InstructionId = "PressingIntensity"
              Value = value
              Label = "Low"
              Grade = grade
              Confidence = 0.9
              Score = score
              Pros =
                [ "Saves legs, protects lead"
                  sprintf "Condition +%.0f vs high" config.PressHighConditionCost ]
              Cons =
                [ sprintf "Concedes possession +%.0f%%" (config.PressLowPossessionLoss * 100.0)
                  "Allows opponent rhythm" ]
              Metrics =
                [ "Possession", -config.PressLowPossessionLoss
                  "Sprint", -config.PressHighSprintDistance ] }
        | _ ->
            { InstructionId = "PressingIntensity"
              Value = value
              Label = "Mid"
              Grade = B
              Confidence = 0.9
              Score = 0.55
              Pros = [ "Balanced press" ]
              Cons = [ "No extreme edge" ]
              Metrics = [ "PPDA", -0.8; "Sprint", 0.06 ] }

    let private evaluateWidth
        (config: TacticalTradeoffConfig)
        (m:
            {| AvgVision: float
               AvgPassing: float |})
        (value: int)
        =
        match value with
        | 4 ->
            let score = (m.AvgPassing / 20.0) * 0.5 + (m.AvgVision / 20.0) * 0.3
            let grade = gradeOf config score

            { InstructionId = "Width"
              Value = value
              Label = "Wide"
              Grade = grade
              Confidence = 0.85
              Score = score
              Pros =
                [ "Stretches opponent, isolates wingers"
                  sprintf "Wing overload +%.0f%%" (config.WidthWideWingExposure * 100.0) ]
              Cons =
                [ sprintf
                      "Central overload -%.0f%% (vision %.1f matters)"
                      (config.WidthWideCentralOverloadLoss * 100.0)
                      m.AvgVision
                  "Exposed on quick central transition" ]
              Metrics =
                [ "Wing exposure", config.WidthWideWingExposure
                  "Central", -config.WidthWideCentralOverloadLoss ] }
        | 0 ->
            let score = 0.5 + (m.AvgVision / 20.0) * 0.2
            let grade = gradeOf config score

            { InstructionId = "Width"
              Value = value
              Label = "Narrow"
              Grade = grade
              Confidence = 0.85
              Score = score
              Pros =
                [ sprintf "Central overload +%.0f%%" (config.WidthNarrowWingOverload * 100.0)
                  sprintf "Compact +%.0f%%" (config.WidthNarrowCentralCompactness * 100.0) ]
              Cons = [ "Congested, fewer switches"; "Needs vision to unlock" ]
              Metrics =
                [ "Central", config.WidthNarrowWingOverload
                  "Compactness", config.WidthNarrowCentralCompactness ] }
        | _ ->
            { InstructionId = "Width"
              Value = value
              Label = "Balanced"
              Grade = B
              Confidence = 0.9
              Score = 0.55
              Pros = [ "No structural bias" ]
              Cons = [ "No specialist overload" ]
              Metrics = [ "Width", 0.0 ] }

    let private evaluateTempo
        (config: TacticalTradeoffConfig)
        (m:
            {| AvgVision: float
               AvgPassing: float
               AvgStamina: float |})
        (value: int)
        =
        match value with
        | 4 ->
            let score = (m.AvgVision / 20.0) * 0.3 + (m.AvgStamina / 20.0) * 0.4
            let grade = gradeOf config score

            { InstructionId = "Tempo"
              Value = value
              Label = "Fast"
              Grade = grade
              Confidence = 0.85
              Score = score
              Pros =
                [ sprintf "+%.2f xG in transition" config.TempoFastTransitionXg
                  "Forces opponent into chaos" ]
              Cons =
                [ sprintf "Possession -%.0f%%" (config.TempoFastPossessionLoss * 100.0)
                  sprintf
                      "Pass completion -%.0f%% (passing %.1f)"
                      (config.TempoFastPassCompletionLoss * 100.0)
                      m.AvgPassing ]
              Metrics =
                [ "xG trans", config.TempoFastTransitionXg
                  "Possession", -config.TempoFastPossessionLoss ] }
        | 0 ->
            let score = (m.AvgPassing / 20.0) * 0.4 + (m.AvgVision / 20.0) * 0.3
            let grade = gradeOf config score

            { InstructionId = "Tempo"
              Value = value
              Label = "Slow"
              Grade = grade
              Confidence = 0.85
              Score = score
              Pros =
                [ sprintf "Possession +%.0f%%" (config.TempoSlowPossessionGain * 100.0)
                  sprintf
                      "Pass completion +%.0f%% (passing %.1f)"
                      (config.TempoSlowPassCompletionGain * 100.0)
                      m.AvgPassing ]
              Cons = [ "Few transitions, predictable"; "Needs patience" ]
              Metrics =
                [ "Possession", config.TempoSlowPossessionGain
                  "PassComp", config.TempoSlowPassCompletionGain ] }
        | _ ->
            { InstructionId = "Tempo"
              Value = value
              Label = "Mixed"
              Grade = B
              Confidence = 0.9
              Score = 0.55
              Pros = [ "Balanced rhythm" ]
              Cons = [ "No tempo edge" ]
              Metrics = [ "Tempo", 0.0 ] }

    let private evaluateDirectness
        (config: TacticalTradeoffConfig)
        (m:
            {| AvgPassing: float
               AvgVision: float |})
        (value: int)
        =
        match value with
        | 4 ->
            let score = (m.AvgVision / 20.0) * 0.3 + 0.3
            let grade = gradeOf config score

            { InstructionId = "Directness"
              Value = value
              Label = "Direct"
              Grade = grade
              Confidence = 0.8
              Score = score
              Pros =
                [ sprintf "+%.2f xG second balls" config.DirectnessDirectSecondBallXg
                  "Bypasses press" ]
              Cons =
                [ sprintf
                      "Pass completion -%.0f%% (passing %.1f)"
                      (config.DirectnessDirectPassCompletionLoss * 100.0)
                      m.AvgPassing
                  "Concedes possession quickly" ]
              Metrics =
                [ "SecondB", config.DirectnessDirectSecondBallXg
                  "PassComp", -config.DirectnessDirectPassCompletionLoss ] }
        | 0 ->
            let score = (m.AvgPassing / 20.0) * 0.4 + 0.3
            let grade = gradeOf config score

            { InstructionId = "Directness"
              Value = value
              Label = "Short"
              Grade = grade
              Confidence = 0.85
              Score = score
              Pros =
                [ sprintf "Pass completion +%.0f%%" (config.DirectnessShortPassCompletionGain * 100.0)
                  "Builds through thirds" ]
              Cons = [ "Vulnerable to high press"; "Needs passing quality" ]
              Metrics = [ "PassComp", config.DirectnessShortPassCompletionGain ] }
        | _ ->
            { InstructionId = "Directness"
              Value = value
              Label = "Mixed"
              Grade = B
              Confidence = 0.9
              Score = 0.55
              Pros = [ "Balanced" ]
              Cons = [ "No direct edge" ]
              Metrics = [ "Direct", 0.0 ] }

    let private evaluatePressTriggerZone (value: int) =
        match value with
        | v when v >= 3 ->
            { InstructionId = "PressTriggerZone"
              Value = value
              Label = "High"
              Grade = B
              Confidence = 0.85
              Score = 0.65
              Pros = [ "Engages everywhere — starves buildup"; "Forces errors high up" ]
              Cons = [ "Space behind when beaten"; "Costs legs late on" ]
              Metrics = [ "Engage", 1.0 ] }
        | v when v <= 1 ->
            { InstructionId = "PressTriggerZone"
              Value = value
              Label = "Low"
              Grade = B
              Confidence = 0.85
              Score = 0.6
              Pros = [ "Compact block — hard to play through"; "Saves legs" ]
              Cons = [ "Invites pressure"; "Cedes territory" ]
              Metrics = [ "Engage", 0.0 ] }
        | _ ->
            { InstructionId = "PressTriggerZone"
              Value = value
              Label = "Balanced"
              Grade = B
              Confidence = 0.9
              Score = 0.6
              Pros = [ "Presses in the middle third"; "Shape stays connected" ]
              Cons = [ "No specialist edge" ]
              Metrics = [ "Engage", 0.5 ] }

    // -----------------------------------------------------------------------
    // Public API — deterministic, pure, fully testable
    // -----------------------------------------------------------------------

    let evaluateSingle
        (config: TacticalTradeoffConfig)
        (squad: Player list)
        (opponentReport: ScoutReport option)
        (instructionId: string)
        (value: int)
        : InstructionVerdict =

        let m = squadMetrics squad

        let opponentPace =
            opponentReport
            |> Option.bind (fun r ->
                // Try to infer rival pace from findings; if scout saw pace-related pattern, use it
                // For now, use a deterministic proxy: if report exists and has high directness finding, assume pace threat
                let directnessFinding =
                    r.Findings |> List.tryFind (fun f -> f.Label.Contains("Direct"))

                match directnessFinding with
                | Some f when f.Strength > 0.6 -> Some(15.0 + f.Strength * 2.0)
                | Some f when f.Strength > 0.4 -> Some(13.5 + f.Strength)
                | _ -> Some 12.0)

        match instructionId with
        | "Mentality" ->
            evaluateMentality
                config
                {| AvgComposure = m.AvgComposure
                   AvgPace = m.AvgPace |}
                value
        | "DefensiveLine" ->
            evaluateDefensiveLine
                config
                {| AvgPace = m.AvgPace
                   AvgPositioning = m.AvgPositioning
                   AvgStamina = m.AvgStamina |}
                value
                opponentPace
        | "PressingIntensity" ->
            evaluatePressing
                config
                {| AvgStamina = m.AvgStamina
                   AvgWorkRate = m.AvgWorkRate
                   AvgBravery = m.AvgBravery |}
                value
        | "Width" ->
            evaluateWidth
                config
                {| AvgVision = m.AvgVision
                   AvgPassing = m.AvgPassing |}
                value
        | "Tempo" ->
            evaluateTempo
                config
                {| AvgVision = m.AvgVision
                   AvgPassing = m.AvgPassing
                   AvgStamina = m.AvgStamina |}
                value
        | "Directness" ->
            evaluateDirectness
                config
                {| AvgPassing = m.AvgPassing
                   AvgVision = m.AvgVision |}
                value
        | "PressTriggerZone" -> evaluatePressTriggerZone value
        | _ -> // unknown — degrade gracefully
            { InstructionId = instructionId
              Value = value
              Label = string value
              Grade = C
              Confidence = 0.5
              Score = 0.5
              Pros = [ "No strong effect" ]
              Cons = [ "Marginal trade-off" ]
              Metrics = [] }

    let evaluateAll
        (config: BalanceConfig)
        (squad: Player list)
        (opponentReport: ScoutReport option)
        (instructions: TacticalInstructions)
        : Map<string, InstructionVerdict> =

        let t = config.TacticalTradeoffs

        [ "Mentality", instructions.Mentality
          "DefensiveLine", instructions.DefensiveLine
          "PressingIntensity", instructions.PressingIntensity
          "Width", instructions.Width
          "Tempo", instructions.Tempo
          "Directness", instructions.Directness
          "PressTriggerZone", instructions.PressTriggerZone ]
        |> List.map (fun (id, v) -> id, evaluateSingle t squad opponentReport id v)
        |> Map.ofList

    // -----------------------------------------------------------------------
    // Long horizon — season projection from tactics
    // -----------------------------------------------------------------------

    let projectSeason
        (config: TacticalTradeoffConfig)
        (squad: Player list)
        (instructions: TacticalInstructions)
        : SeasonProjection =

        let m = squadMetrics squad
        // Pressing + high line drive fatigue
        let pressCost =
            if instructions.PressingIntensity >= 3 then
                config.PressHighConditionCost
            else
                0.0

        let lineCost =
            if instructions.DefensiveLine >= 3 then
                config.DefensiveLineHighSprintCost * 10.0
            else
                0.0

        let weeklyCost = pressCost + lineCost

        let injuryDelta =
            let baseRisk =
                if instructions.PressingIntensity >= 3 then
                    config.PressHighInjuryRisk
                else
                    0.0

            let lineRisk = if instructions.DefensiveLine >= 3 then 0.008 else 0.0
            baseRisk + lineRisk

        // Youth suitability: high press needs stamina/work rate; direct needs strength
        let youthFit =
            let pressFit = m.AvgStamina / 20.0 * 0.5 + m.AvgWorkRate / 20.0 * 0.5
            let directFit = m.AvgStrength / 20.0
            (pressFit + directFit) / 2.0 |> max 0.0 |> min 1.0

        let transferNeed =
            if instructions.DefensiveLine >= 3 && m.AvgPace < 13.0 then
                Some "Needs fast CB for high line"
            elif instructions.PressingIntensity >= 3 && m.AvgStamina < 12.0 then
                Some "Needs legs for high press"
            elif instructions.Directness >= 3 && m.AvgStrength < 12.0 then
                Some "Needs target man for direct play"
            else
                None

        let chemistryDelta =
            // High press without work rate hurts morale/cohesion
            if instructions.PressingIntensity >= 3 && m.AvgWorkRate < 12.0 then
                -0.05
            elif instructions.PressingIntensity = 0 && m.AvgWorkRate > 15.0 then
                -0.02
            else
                0.0

        { WeeklyConditionCost = weeklyCost
          SeasonInjuryRiskDelta = injuryDelta
          YouthSuitability = youthFit
          TransferNeed = transferNeed
          ChemistryDelta = chemistryDelta }
