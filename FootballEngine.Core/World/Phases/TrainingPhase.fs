namespace FootballEngine.World.Phases

open System
open FootballEngine.Domain
open FootballEngine.Stats
open FootballEngine.Core.Match
open FootballEngine.World

/// Weekly training: the develop / freshness / risk triangle.
///
/// Every contracted player trains every week under the plan on file (user club:
/// manager-set; others: role-based defaults from generation). One phase owns all of
/// it — growth, batteries, injuries, form decay — so there is exactly one place
/// where training behavior lives. All numbers come from BalanceConfig.Training;
/// rolls use the shared deterministic stream in Map key order.
module TrainingPhaseEngine =

    let private clamp01 (v: int) = max 0 (min 100 v)

    let private ageMult (cfg: TrainingConfig) (age: int) : float =
        if age <= 18 then cfg.AgeMultU18
        elif age <= 21 then cfg.AgeMultU21
        elif age <= 25 then cfg.AgeMultU25
        elif age <= 28 then cfg.AgeMultU28
        else 0.0

    // Attribute lenses per group. Growth always feeds the WEAKEST attribute of the
    // group first: training visibly rounds players instead of stacking one stat.
    let private groupAttrs (g: AttributeGroup) : ((Player -> int) * (int -> Player -> Player)) list =
        match g with
        | AttributeGroup.Finishing ->
            [ (fun p -> p.Technical.Finishing),
              (fun v p ->
                  { p with
                      Technical =
                          { p.Technical with
                              Finishing = min 20 v } })
              (fun p -> p.Technical.LongShots),
              (fun v p ->
                  { p with
                      Technical =
                          { p.Technical with
                              LongShots = min 20 v } }) ]
        | AttributeGroup.Creation ->
            [ (fun p -> p.Technical.Passing),
              (fun v p ->
                  { p with
                      Technical = { p.Technical with Passing = min 20 v } })
              (fun p -> p.Mental.Vision),
              (fun v p ->
                  { p with
                      Mental = { p.Mental with Vision = min 20 v } }) ]
        | AttributeGroup.Technique ->
            [ (fun p -> p.Technical.Dribbling),
              (fun v p ->
                  { p with
                      Technical =
                          { p.Technical with
                              Dribbling = min 20 v } })
              (fun p -> p.Technical.BallControl),
              (fun v p ->
                  { p with
                      Technical =
                          { p.Technical with
                              BallControl = min 20 v } })
              (fun p -> p.Technical.Crossing),
              (fun v p ->
                  { p with
                      Technical = { p.Technical with Crossing = min 20 v } }) ]
        | AttributeGroup.Defending ->
            [ (fun p -> p.Technical.Tackling),
              (fun v p ->
                  { p with
                      Technical = { p.Technical with Tackling = min 20 v } })
              (fun p -> p.Technical.Marking),
              (fun v p ->
                  { p with
                      Technical = { p.Technical with Marking = min 20 v } })
              (fun p -> p.Mental.Positioning),
              (fun v p ->
                  { p with
                      Mental = { p.Mental with Positioning = min 20 v } }) ]
        | AttributeGroup.Physical ->
            [ (fun p -> p.Physical.Pace),
              (fun v p ->
                  { p with
                      Physical = { p.Physical with Pace = min 20 v } })
              (fun p -> p.Physical.Stamina),
              (fun v p ->
                  { p with
                      Physical = { p.Physical with Stamina = min 20 v } })
              (fun p -> p.Physical.Strength),
              (fun v p ->
                  { p with
                      Physical = { p.Physical with Strength = min 20 v } }) ]
        | AttributeGroup.Mental ->
            [ (fun p -> p.Mental.Composure),
              (fun v p ->
                  { p with
                      Mental = { p.Mental with Composure = min 20 v } })
              (fun p -> p.Mental.Concentration),
              (fun v p ->
                  { p with
                      Mental =
                          { p.Mental with
                              Concentration = min 20 v } })
              (fun p -> p.Mental.Leadership),
              (fun v p ->
                  { p with
                      Mental = { p.Mental with Leadership = min 20 v } }) ]
        | AttributeGroup.Goalkeeping ->
            [ (fun p -> p.Goalkeeping.Reflexes),
              (fun v p ->
                  { p with
                      Goalkeeping =
                          { p.Goalkeeping with
                              Reflexes = min 20 v } })
              (fun p -> p.Goalkeeping.Handling),
              (fun v p ->
                  { p with
                      Goalkeeping =
                          { p.Goalkeeping with
                              Handling = min 20 v } })
              (fun p -> p.Goalkeeping.OneOnOne),
              (fun v p ->
                  { p with
                      Goalkeeping =
                          { p.Goalkeeping with
                              OneOnOne = min 20 v } }) ]

    let private allAttrs: ((Player -> int) * (int -> Player -> Player)) list =
        Training.allGroups |> List.collect groupAttrs

    let private growWeakest (lenses: ((Player -> int) * (int -> Player -> Player)) list) (p: Player) : Player =
        match lenses with
        | [] -> p
        | _ ->
            let get, set = lenses |> List.minBy (fun (get, _) -> get p)
            set (get p + 1) p

    let private bumpSalary (p: Player) : Player =
        match p.Affiliation with
        | Contracted(clubId, c) ->
            { p with
                Affiliation =
                    Contracted(
                        clubId,
                        { c with
                            Salary = Player.playerSalary p.CurrentSkill }
                    ) }
        | other -> { p with Affiliation = other }

    let private tiredFactor (cfg: TrainingConfig) (condition: int) : float =
        if condition < cfg.TiredConditionThreshold then
            cfg.TiredGrowthPenalty
        else
            1.0

    /// Best rating per area across the club's staff. Missing areas default to 10
    /// (league average = no effect); no medical staff means no injury reduction.
    type CoachRatings =
        { Attacking: int
          Defending: int
          Technical: int
          Fitness: int
          Mental: int
          Goalkeeping: int
          Youngsters: int
          Physio: int }

    let neutralCoaching: CoachRatings =
        { Attacking = 10
          Defending = 10
          Technical = 10
          Fitness = 10
          Mental = 10
          Goalkeeping = 10
          Youngsters = 10
          Physio = 0 }

    let coachingOf (gs: GameState) (clubId: ClubId) : CoachRatings =
        let staff = GameState.getStaff clubId gs

        let best (f: CoachingAttributes -> int) =
            staff
            |> List.filter Staff.isCoachingRole
            |> List.map (fun s -> f s.Attributes.Coaching)
            |> List.append [ 10 ]
            |> List.max

        let physio =
            staff
            |> List.filter (fun s ->
                match s.Role with
                | Physio
                | SportsScientist -> true
                | _ -> false)
            |> List.map (fun s -> max s.Attributes.Medical.Physiotherapy s.Attributes.Medical.SportsScience)
            |> List.append [ 0 ]
            |> List.max

        { Attacking = best _.Attacking
          Defending = best _.Defending
          Technical = best _.Technical
          Fitness = best _.Fitness
          Mental = best _.Mental
          Goalkeeping = best _.Goalkeeping
          Youngsters = best _.WorkingWithYoungsters
          Physio = physio }

    let private groupRating (g: AttributeGroup) (r: CoachRatings) : int =
        match g with
        | AttributeGroup.Finishing -> r.Attacking
        | AttributeGroup.Creation -> r.Technical
        | AttributeGroup.Technique -> r.Technical
        | AttributeGroup.Defending -> r.Defending
        | AttributeGroup.Physical -> r.Fitness
        | AttributeGroup.Mental -> r.Mental
        | AttributeGroup.Goalkeeping -> r.Goalkeeping

    let coachGrowthMult (cfg: TrainingConfig) (age: int) (group: AttributeGroup option) (r: CoachRatings) : float =
        let area =
            match group with
            | Some g -> 1.0 + cfg.CoachGrowthInfluence * float (groupRating g r - 10) / 10.0
            | None -> 1.0

        let youth =
            if age <= 21 then
                1.0 + cfg.YouthCoachBonus * float (r.Youngsters - 10) / 10.0
            else
                1.0

        max 0.0 (area * youth)

    let growthChanceWith
        (cfg: TrainingConfig)
        (age: int)
        (tilt: AttributeGroup option)
        (plan: IndividualPlan option)
        (gap: int)
        (condition: int)
        (ratings: CoachRatings)
        : float =
        let groupOf =
            match plan with
            | Some(Develop g) -> Some g
            | _ -> None

        let coach = coachGrowthMult cfg age groupOf ratings

        match plan with
        | Some(Develop g) when gap > 0 ->
            let tiltBonus =
                match tilt with
                | Some t when t = g -> cfg.TiltBonus
                | _ -> 0.0

            (cfg.DevelopChance * ageMult cfg age * tiredFactor cfg condition + tiltBonus)
            * coach
        | Some Perform when gap > 0 -> cfg.MaintainChance * ageMult cfg age * tiredFactor cfg condition * coach
        | _ -> 0.0

    let growthChance
        (cfg: TrainingConfig)
        (age: int)
        (tilt: AttributeGroup option)
        (plan: IndividualPlan option)
        (gap: int)
        (condition: int)
        : float =
        growthChanceWith cfg age tilt plan gap condition neutralCoaching

    let injuryChanceWith (cfg: TrainingConfig) (planAdd: float) (condition: int) (physio: int) : float =
        let tiredness = 1.0 - float condition / 100.0
        let risk = cfg.BaseInjuryRisk + tiredness * cfg.TiredInjuryScale + planAdd
        max 0.0 (risk - cfg.PhysioInjuryReduction * float physio / 20.0 * risk)

    let injuryChance (cfg: TrainingConfig) (planAdd: float) (condition: int) : float =
        injuryChanceWith cfg planAdd condition 0

    let batteryDeltas (cfg: TrainingConfig) (plan: IndividualPlan option) : int * int =
        match plan with
        | None -> cfg.RehabConditionGain, 0
        | Some(Develop _) -> -cfg.DevelopConditionCost, cfg.DevelopFitnessGain
        | Some Perform -> cfg.PerformConditionGain, cfg.PerformFitnessGain
        | Some Rest -> cfg.RestConditionGain, -cfg.RestFitnessLoss

    type WeeklyPreview =
        { GrowthPct: float
          Target: string
          ConditionDelta: int
          FitnessDelta: int
          InjuryPct: float }

    /// What the UI shows before the week runs: same formulas, no rolls.
    /// previewWith threads real staff ratings; preview assumes neutral coaching.
    let previewWith
        (cfg: TrainingConfig)
        (date: DateTime)
        (tilt: AttributeGroup option)
        (recoveryWeek: bool)
        (ratings: CoachRatings)
        (p: Player)
        : WeeklyPreview =
        let r = ratings
        let plan = TrainingPlans.effectivePlan p
        let age = Player.age date p
        let gap = p.PotentialSkill - p.CurrentSkill

        let planAdd =
            match plan with
            | Some(Develop _) -> cfg.DevelopInjuryAdd
            | _ -> 0.0

        let condDelta, fitDelta = batteryDeltas cfg plan

        let target =
            match plan with
            | None -> "Recovery"
            | Some(Develop g) -> Training.groupLabel g
            | Some Perform -> "Maintenance"
            | Some Rest -> "Recovery"

        { GrowthPct = growthChanceWith cfg age tilt plan gap p.Condition r * 100.0
          Target = target
          ConditionDelta = condDelta + (if recoveryWeek then cfg.RecoveryConditionGain else 0)
          FitnessDelta = fitDelta
          InjuryPct = injuryChanceWith cfg planAdd p.Condition r.Physio * 100.0 }

    let preview
        (cfg: TrainingConfig)
        (date: DateTime)
        (tilt: AttributeGroup option)
        (recoveryWeek: bool)
        (p: Player)
        : WeeklyPreview =
        previewWith cfg date tilt recoveryWeek neutralCoaching p

    let private applyGrowth
        (cfg: TrainingConfig)
        (age: int)
        (tilt: AttributeGroup option)
        (plan: IndividualPlan option)
        (ratings: CoachRatings)
        (p: Player)
        : Player =
        let gap = p.PotentialSkill - p.CurrentSkill
        let chance = growthChanceWith cfg age tilt plan gap p.Condition ratings
        let roll = rollProbability ()

        match plan with
        | Some(Develop g) when gap > 0 ->
            if g = AttributeGroup.Goalkeeping && p.Position <> GK then
                p
            elif roll < chance then
                let grown = growWeakest (groupAttrs g) p

                { grown with
                    CurrentSkill = min p.PotentialSkill (grown.CurrentSkill + 1) }
                |> bumpSalary
            elif roll < chance + cfg.TrickleChance then
                growWeakest allAttrs p
            else
                p
        | Some Perform when gap > 0 ->
            if roll < chance then
                { p with
                    CurrentSkill = min p.PotentialSkill (p.CurrentSkill + 1) }
                |> bumpSalary
            else
                p
        | _ -> p

    let private applyDecline (cfg: TrainingConfig) (age: int) (p: Player) : Player =
        if age < cfg.DeclineStartAge then
            p
        elif rollProbability () < cfg.DeclineChance then
            let physical =
                if p.Id % 2 = 0 then
                    { p.Physical with
                        Pace = max 1 (p.Physical.Pace - 1) }
                else
                    { p.Physical with
                        Stamina = max 1 (p.Physical.Stamina - 1) }

            { p with
                Physical = physical
                CurrentSkill = max 1 (p.CurrentSkill - 1) }
        else
            p

    let private applyBatteries (cfg: TrainingConfig) (plan: IndividualPlan option) (p: Player) : Player =
        let condDelta, fitDelta = batteryDeltas cfg plan

        { p with
            Condition = clamp01 (p.Condition + condDelta)
            MatchFitness = clamp01 (p.MatchFitness + fitDelta) }

    let private applyHealthTransition (plan: IndividualPlan option) (p: Player) : Player =
        let transition = Training.healthTransition plan

        { p with
            Fatigue = clamp01 (p.Fatigue + transition.FatigueDelta)
            RecentLoad = clamp01 (p.RecentLoad + transition.LoadDelta) }

    let private applyRecoveryEmphasis (cfg: TrainingConfig) (p: Player) : Player =
        { p with
            Condition = clamp01 (p.Condition + cfg.RecoveryConditionGain) }

    let private maybeInjure (cfg: TrainingConfig) (planAdd: float) (physio: int) (date: DateTime) (p: Player) : Player =
        if rollProbability () < injuryChanceWith cfg planAdd p.Condition physio then
            { p with
                Status = Injured(InjurySeverity.Minor, date.AddDays(7.0)) }
        else
            p

    let private decayForm (mods: ExperienceModifiers) : ExperienceModifiers =
        let decay = 0.95

        { ShootingConfidence = 1.0 + (mods.ShootingConfidence - 1.0) * decay
          PassingRhythm = 1.0 + (mods.PassingRhythm - 1.0) * decay
          DuelMentality = 1.0 + (mods.DuelMentality - 1.0) * decay
          HighPressureBonus = 1.0 + (mods.HighPressureBonus - 1.0) * decay }

    let trainPlayerWithRatings
        (cfg: TrainingConfig)
        (date: DateTime)
        (tilt: AttributeGroup option)
        (recoveryWeek: bool)
        (ratings: CoachRatings)
        (p: Player)
        : Player =
        let age = Player.age date p
        let plan = TrainingPlans.effectivePlan p

        let planAdd =
            match plan with
            | Some(Develop _) -> cfg.DevelopInjuryAdd
            | _ -> 0.0

        p
        |> applyBatteries cfg plan
        |> applyHealthTransition plan
        |> (fun q -> if recoveryWeek then applyRecoveryEmphasis cfg q else q)
        |> applyGrowth cfg age tilt plan ratings
        |> applyDecline cfg age
        |> maybeInjure cfg planAdd ratings.Physio date
        |> fun q ->
            { q with
                ExperienceModifiers = decayForm q.ExperienceModifiers }

    let trainPlayer
        (cfg: TrainingConfig)
        (date: DateTime)
        (tilt: AttributeGroup option)
        (recoveryWeek: bool)
        (p: Player)
        : Player =
        trainPlayerWithRatings cfg date tilt recoveryWeek neutralCoaching p

    let applyWeekly (cfg: TrainingConfig) (date: DateTime) (gs: GameState) : GameState =
        let tilt = Training.tiltOf gs.TeamEmphasis
        let recoveryWeek = gs.TeamEmphasis = EmRecovery
        let mutable coaches: Map<ClubId, CoachRatings> = Map.empty

        let ratingsFor clubId =
            match coaches.TryFind clubId with
            | Some r -> r
            | None ->
                let r = coachingOf gs clubId
                coaches <- coaches |> Map.add clubId r
                r

        let mutable nextGs = gs

        for KeyValue(_, p) in gs.Players do
            match p.Affiliation with
            | Contracted(clubId, _) ->
                let playerTilt = if clubId = gs.UserClubId then tilt else None
                let recovery = recoveryWeek && clubId = gs.UserClubId

                let trained =
                    trainPlayerWithRatings cfg date playerTilt recovery (ratingsFor clubId) p

                nextGs <- GameState.updatePlayer trained nextGs
            | _ -> ()

        { nextGs with
            TrainingWeeksApplied = nextGs.TrainingWeeksApplied + 1 }

    /// Season-sim fast path: condition costs and cumulative injury risk only, no growth
    /// rolls (matches the old bulk semantics for long simulations).
    let applyBulk (cfg: TrainingConfig) (date: DateTime) (weeks: int) (gs: GameState) : GameState =
        if weeks <= 0 then
            gs
        else
            let costOf (plan: IndividualPlan option) =
                match plan with
                | None -> -cfg.RehabConditionGain
                | Some(Develop _) -> cfg.DevelopConditionCost
                | Some Perform -> -cfg.PerformConditionGain
                | Some Rest -> -cfg.RestConditionGain

            { gs with
                Players =
                    gs.Players
                    |> Map.map (fun _ p ->
                        match p.Affiliation with
                        | Contracted _ ->
                            let plan = TrainingPlans.effectivePlan p

                            let planAdd =
                                match plan with
                                | Some(Develop _) -> cfg.DevelopInjuryAdd
                                | _ -> 0.0

                            let tiredness = 1.0 - float p.Condition / 100.0
                            let weeklyRisk = cfg.BaseInjuryRisk + tiredness * cfg.TiredInjuryScale + planAdd
                            let cumulative = 1.0 - (1.0 - weeklyRisk) ** float weeks

                            { p with
                                Condition = clamp01 (p.Condition - costOf plan * weeks)
                                Status =
                                    if rollProbability () < cumulative then
                                        Injured(InjurySeverity.Minor, date.AddDays(14.0))
                                    else
                                        p.Status }
                        | _ -> p) }

module TrainingPhase =

    let makeWithBalance (balance: BalanceConfig) : WorldPhase =
        { Tag = Development
          Frequency = Weekly
          Run = fun _clock state -> TrainingPhaseEngine.applyWeekly balance.Training state.CurrentDate state }
