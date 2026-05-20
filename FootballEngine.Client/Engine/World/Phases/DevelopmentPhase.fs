namespace FootballEngine.World.Phases

open System
open FootballEngine.Domain
open FootballEngine.Stats
open FootballEngine.World
open FootballEngine.ML

module TrainingEngine =

    let private dev = EngineWeightDefaults.defaults.Development

    let focusMultiplier (focus: TrainingFocus) (profile: BehavioralProfile) : float =
        match focus with
        | TrainingFocus.TrainingAllRound -> 1.0
        | TrainingFocus.TrainingGoalkeeping -> dev.FocusMultiplier_Goalkeeping
        | TrainingFocus.TrainingPhysical ->
            profile.PressingIntensity * dev.FocusMultiplier_Physical_Pressing + profile.PositionalFreedom * dev.FocusMultiplier_Physical_Positional + dev.FocusMultiplier_PhysicalBase
        | TrainingFocus.TrainingTechnical ->
            profile.CreativityWeight * dev.FocusMultiplier_Technical_Creativity + profile.Directness * dev.FocusMultiplier_Technical_Directness + dev.FocusMultiplier_TechnicalBase
        | TrainingFocus.TrainingMental -> dev.FocusMultiplier_Mental

    let intensityEffect (intensity: TrainingIntensity) : TrainingIntensityData = TrainingIntensityData.get intensity

    let private statFocusForTech (profile: BehavioralProfile) =
        if profile.Directness > 0.5 && profile.AttackingDepth > 0.5 then "attack"
        elif profile.DefensiveHeight > 0.5 then "defense"
        elif profile.CreativityWeight > 0.5 then "playmaking"
        else "general"

    let private applyWeeklySkillDevelopment
        (schedule: TrainingSchedule)
        (age: int)
        (currentSkill: int)
        (potential: int)
        (profile: BehavioralProfile)
        (p: Player)
        : Player =
        let gap = potential - currentSkill

        let maxDeltaU21 = dev.AgeBracket_MaxDelta_U21
        let maxDeltaU25 = dev.AgeBracket_MaxDelta_U25
        let maxDeltaU28 = dev.AgeBracket_MaxDelta_U28
        let maxDeltaU31 = dev.AgeBracket_MaxDelta_U31

        let baseDelta =
            match age with
            | a when a <= 20 -> min gap maxDeltaU21
            | a when a <= 24 -> min gap maxDeltaU25
            | a when a <= 27 -> min gap maxDeltaU28
            | a when a <= 30 -> maxDeltaU31
            | _ -> 0

        if baseDelta <= 0 then
            p
        else
            let focusMult = focusMultiplier schedule.Focus profile
            let intensityMult = intensityEffect schedule.Intensity |> fun e -> e.DeltaMultiplier
            let weeklyDelta = float baseDelta * focusMult * intensityMult / dev.WeeklyDeltaDivisor

            let roll = rollProbability ()
            let shouldIncrease = roll < weeklyDelta

            if shouldIncrease then
                { p with
                    CurrentSkill = min potential (p.CurrentSkill + 1)
                    Physical =
                        { p.Physical with
                            Stamina = min 20 (p.Physical.Stamina + 1) }
                    Technical =
                        match statFocusForTech profile with
                        | "attack" ->
                            { p.Technical with
                                Finishing = min 20 (p.Technical.Finishing + 1)
                                Dribbling = min 20 (p.Technical.Dribbling + 1) }
                        | "defense" ->
                            { p.Technical with
                                Tackling = min 20 (p.Technical.Tackling + 1)
                                Marking = min 20 (p.Technical.Marking + 1) }
                        | "playmaking" ->
                            { p.Technical with
                                Passing = min 20 (p.Technical.Passing + 1)
                                BallControl = min 20 (p.Technical.BallControl + 1) }
                        | _ ->
                            { p.Technical with
                                Passing = min 20 (p.Technical.Passing + 1) }
                    Mental =
                        { p.Mental with
                            Vision = min 20 (p.Mental.Vision + 1)
                            Positioning = min 20 (p.Mental.Positioning + 1) }
                    Goalkeeping =
                        if p.Position = GK then
                            { p.Goalkeeping with
                                Reflexes = min 20 (p.Goalkeeping.Reflexes + 1)
                                OneOnOne = min 20 (p.Goalkeeping.OneOnOne + 1)
                                Handling = min 20 (p.Goalkeeping.Handling + 1) }
                        else
                            p.Goalkeeping }
            else
                p

    let applyWeeklyTraining (currentDate: DateTime) (schedule: TrainingSchedule) (player: Player) : Player =
        let effect = intensityEffect schedule.Intensity

        let newCondition = Math.Clamp(player.Condition + effect.ConditionCost, 0, 100)

        let newMorale = Math.Clamp(player.Morale + effect.MoraleChange, 0, 100)

        let injuryRoll = rollProbability ()

        let playerWithCondition =
            if injuryRoll < effect.InjuryRisk then
                { player with
                    Status = Injured(Minor, currentDate.AddDays(7.0))
                    Condition = newCondition
                    Morale = newMorale }
            else
                { player with
                    Condition = newCondition
                    Morale = newMorale }

        let age = Player.age currentDate playerWithCondition

        applyWeeklySkillDevelopment
            schedule
            age
            playerWithCondition.CurrentSkill
            playerWithCondition.PotentialSkill
            (Player.profile playerWithCondition EngineWeightDefaults.defaults.ProfileWeights)
            playerWithCondition

    let applyRemainingSeasonTraining
        (currentDate: DateTime)
        (weeksApplied: int)
        (totalWeeks: int)
        (gs: GameState)
        : GameState =
        let remaining = max 0 (totalWeeks - weeksApplied)

        if remaining = 0 then
            gs
        else
            { gs with
                Players =
                    gs.Players
                    |> Map.map (fun _ player ->
                        match player.Affiliation with
                        | Contracted _ ->
                            let effect = intensityEffect player.TrainingSchedule.Intensity
                            let totalCost = effect.ConditionCost * remaining
                            let newCondition = Math.Clamp(player.Condition + totalCost, 0, 100)
                            let cumulativeRisk = 1.0 - (1.0 - effect.InjuryRisk) ** float remaining
                            let injuryRoll = rollProbability ()
                            let injured = injuryRoll < cumulativeRisk

                            { player with
                                Condition = newCondition
                                Status =
                                    if injured then
                                        Injured(Minor, currentDate.AddDays(14.0))
                                    else
                                        player.Status }
                        | _ -> player) }

module PlayerDevelopment =

    let private dev = EngineWeightDefaults.defaults.Development

    let private skillDelta
        (age: int)
        (skill: int)
        (potential: int)
        (schedule: TrainingSchedule option)
        (profile: BehavioralProfile)
        : int =
        let gap = potential - skill

        let maxDeltaU21 = dev.AgeBracket_MaxDelta_U21
        let maxDeltaU25 = dev.AgeBracket_MaxDelta_U25
        let maxDeltaU28 = dev.AgeBracket_MaxDelta_U28
        let maxDeltaU31 = dev.AgeBracket_MaxDelta_U31
        let maxDeltaU34 = dev.AgeBracket_MaxDelta_U34

        match schedule with
        | None ->
            match age with
            | a when a <= 20 -> normalInt (float (min gap maxDeltaU21)) 1.5 0 (min gap (maxDeltaU21 + 1))
            | a when a <= 24 -> normalInt (float (min gap maxDeltaU25)) 1.5 -1 (min gap (maxDeltaU25 + 1))
            | a when a <= 27 -> normalInt 0.5 1.0 -1 maxDeltaU28
            | a when a <= 30 -> normalInt -0.5 1.0 -2 maxDeltaU31
            | a when a <= 33 -> normalInt -1.5 1.0 -3 maxDeltaU34
            | _ -> normalInt -2.5 1.0 -4 -1
        | Some sched ->
            let focusMult = TrainingEngine.focusMultiplier sched.Focus profile

            let intensityMult =
                TrainingEngine.intensityEffect sched.Intensity |> fun e -> e.DeltaMultiplier

            let combinedMult = focusMult * intensityMult

            match age with
            | a when a <= 20 ->
                normalInt
                    (float (min gap maxDeltaU21) * combinedMult)
                    (1.5 * combinedMult)
                    0
                    (int (float (min gap (maxDeltaU21 + 1)) * combinedMult))
            | a when a <= 24 ->
                normalInt
                    (float (min gap maxDeltaU25) * combinedMult)
                    (1.5 * combinedMult)
                    -1
                    (int (float (min gap (maxDeltaU25 + 1)) * combinedMult))
            | a when a <= 27 -> normalInt (0.5 * combinedMult) (1.0 * combinedMult) -1 (int (float maxDeltaU28 * combinedMult))
            | a when a <= 30 -> normalInt (-0.5 * combinedMult) (1.0 * combinedMult) -2 (int (float maxDeltaU31 * combinedMult))
            | a when a <= 33 -> normalInt (-1.5 * combinedMult) (1.0 * combinedMult) -3 maxDeltaU34
            | _ -> normalInt (-2.5 * combinedMult) (1.0 * combinedMult) -4 -1

    let private maybeStat (delta: int) (stat: int) : int =
        let threshold =
            if delta > 0 then
                dev.MaybeStat_PositiveThreshold * float delta
            else
                dev.MaybeStat_NegativeThreshold * float (abs delta)

        let roll = rollProbability ()

        if roll < threshold then
            clamp 1 20 (stat + (if delta > 0 then 1 else -1))
        else
            stat

    let private physFocus (profile: BehavioralProfile) =
        if profile.DefensiveHeight > dev.StatFocus_DefensiveHeightThreshold then "defensive"
        elif profile.AttackingDepth > dev.StatFocus_AttackingDepthThreshold && profile.Directness > dev.StatFocus_DirectnessThreshold then "attacking"
        else "general"

    let private techFocus (profile: BehavioralProfile) =
        if profile.Directness > dev.StatFocus_DirectnessThreshold && profile.AttackingDepth > dev.StatFocus_AttackingDepthThreshold then "attack"
        elif profile.DefensiveHeight > dev.StatFocus_DefensiveHeightThreshold then "defense"
        elif profile.CreativityWeight > dev.StatFocus_CreativityThreshold then "playmaking"
        else "general"

    let applyPostMatchExperience (goals: int) (shots: int) (passSuccessRate: float) (duelWinRate: float) (current: ExperienceModifiers) : ExperienceModifiers =
        let shootingPerf = if shots > 0 then float goals / float shots else 0.5
        let shootingAdj = if shootingPerf > 0.3 then 1.01 elif shootingPerf > 0.15 then 1.0 else 0.99
        let passingAdj = if passSuccessRate > 0.8 then 1.01 elif passSuccessRate > 0.6 then 1.0 else 0.99
        let duelAdj = if duelWinRate > 0.55 then 1.01 elif duelWinRate > 0.4 then 1.0 else 0.99
        let pressureAdj = if goals > 0 && shootingPerf > 0.3 then 1.01 else 1.0
        { ShootingConfidence = Math.Clamp(0.85, 1.15, current.ShootingConfidence * shootingAdj)
          PassingRhythm = Math.Clamp(0.85, 1.15, current.PassingRhythm * passingAdj)
          DuelMentality = Math.Clamp(0.85, 1.15, current.DuelMentality * duelAdj)
          HighPressureBonus = Math.Clamp(0.85, 1.15, current.HighPressureBonus * pressureAdj) }

    let applyWeeklyExperienceDecay (current: ExperienceModifiers) : ExperienceModifiers =
        let decay = 0.95
        { ShootingConfidence = 1.0 + (current.ShootingConfidence - 1.0) * decay
          PassingRhythm = 1.0 + (current.PassingRhythm - 1.0) * decay
          DuelMentality = 1.0 + (current.DuelMentality - 1.0) * decay
          HighPressureBonus = 1.0 + (current.HighPressureBonus - 1.0) * decay }

    let private developStats (delta: int) (profile: BehavioralProfile) (currentDate: DateTime) (p: Player) =
        let phys =
            if Player.age currentDate p > 28 then
                maybeStat (min delta 0)
            else
                maybeStat delta

        let tech = maybeStat delta
        let mental = maybeStat delta
        let gkOnly = if p.Position = GK then maybeStat delta else id

        let physical =
            match physFocus profile with
            | "defensive" ->
                { p.Physical with
                    Strength = phys p.Physical.Strength
                    Stamina = phys p.Physical.Stamina }
            | "attacking" ->
                { p.Physical with
                    Pace = phys p.Physical.Pace
                    Agility = phys p.Physical.Agility }
            | _ ->
                { p.Physical with
                    Stamina = phys p.Physical.Stamina }

        let technical =
            match techFocus profile with
            | "attack" ->
                { p.Technical with
                    Finishing = tech p.Technical.Finishing
                    Dribbling = tech p.Technical.Dribbling }
            | "defense" ->
                { p.Technical with
                    Tackling = tech p.Technical.Tackling
                    Marking = tech p.Technical.Marking }
            | "playmaking" ->
                { p.Technical with
                    Passing = tech p.Technical.Passing
                    BallControl = tech p.Technical.BallControl }
            | _ ->
                { p.Technical with
                    Passing = tech p.Technical.Passing }

        { p with
            Physical = physical
            Technical = technical
            Mental =
                { p.Mental with
                    Vision = mental p.Mental.Vision
                    Positioning = mental p.Mental.Positioning }
            Goalkeeping =
                { p.Goalkeeping with
                    Reflexes = gkOnly p.Goalkeeping.Reflexes
                    OneOnOne = gkOnly p.Goalkeeping.OneOnOne
                    Handling = gkOnly p.Goalkeeping.Handling } }

    let developPlayer (currentDate: DateTime) (schedule: TrainingSchedule option) (p: Player) : Player =
        let a = Player.age currentDate p
        let prof = Player.profile p EngineWeightDefaults.defaults.ProfileWeights
        let delta = skillDelta a p.CurrentSkill p.PotentialSkill schedule prof
        let newCA = clamp 1 p.PotentialSkill (p.CurrentSkill + delta)

        let updatedAffiliation =
            match p.Affiliation with
            | Contracted(clubId, c) when newCA > p.CurrentSkill ->
                Contracted(
                    clubId,
                    { c with
                        Salary = Player.playerSalary newCA }
                )
            | other -> other

        { p with
            CurrentSkill = newCA
            Affiliation = updatedAffiliation }
        |> developStats delta prof currentDate

    let developAll (gs: GameState) : GameState =
        { gs with
            Players =
                gs.Players
                |> Map.map (fun _ p ->
                    let schedule = Some p.TrainingSchedule
                    let developed = developPlayer gs.CurrentDate schedule p
                    let decayedExp =
                        { developed with
                            ExperienceModifiers =
                                applyWeeklyExperienceDecay developed.ExperienceModifiers }
                    decayedExp) }

module DevelopmentPhase =

    let make: WorldPhase =
        { Tag = Development
          Frequency = Monthly
          Run = fun _clock state -> PlayerDevelopment.developAll state }
