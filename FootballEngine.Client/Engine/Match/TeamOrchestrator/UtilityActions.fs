namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract



module private UtilityEvaluators =

    let private avgCondition (state: SimState) (clubSide: ClubSide) : float =
        let frame = SimStateOps.getFrame state clubSide
        let mutable total = 0
        let mutable count = 0

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                total <- total + int frame.Condition[i]
                count <- count + 1
            | _ -> ()

        if count > 0 then float total / float count else 50.0

    let private scoreDiffFor (state: SimState) (clubSide: ClubSide) : int =
        if clubSide = HomeClub then
            state.HomeScore - state.AwayScore
        else
            state.AwayScore - state.HomeScore

    let private minutesLeft (state: SimState) : float =
        let elapsedSec = float (int state.SubTick) / 40.0
        max 0.0 (90.0 * 60.0 - elapsedSec) / 60.0

    let evaluatePress
        (depth: PressDepth)
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        let uw = EngineWeightDefaults.defaults.Utility
        let ballZoneBonus =
            match depth, bb.BallZone with
            | PressHigh, AttackingZone -> uw.PressZoneBonus_HighAttacking
            | PressHigh, MidfieldZone -> uw.PressZoneBonus_HighMidfield
            | PressHigh, _ -> uw.PressZoneBonus_HighDefensive
            | PressMid, (AttackingZone | MidfieldZone) -> uw.PressZoneBonus_MidAttackingMidfield
            | PressMid, _ -> uw.PressZoneBonus_MidDefensive
            | PressLow, _ -> uw.PressZoneBonus_Low

        let avgCond = avgCondition state clubSide
        let staminaFactor = avgCond / 100.0

        let opponentBonus =
            if bb.OpponentShape = OpponentShape.HighLine && bb.OpponentPressure = NoPress then
                uw.OpponentHighLineNoPressBonus
            else
                0.0

        let successBonus = emergent.PressingIntensity * uw.PressingSuccessBonus

        clamp (ballZoneBonus * staminaFactor + opponentBonus + successBonus) 0.0 1.0

    let evaluateDropDeep
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        let uw = EngineWeightDefaults.defaults.Utility
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let leadBonus =
            if sd > 0 then min (uw.DropDeepLeadBonus * float sd) (uw.DropDeepLeadBonus * 2.5)
            elif sd < 0 then -0.4
            else 0.0

        let timeBonus = if ml < 15.0 && sd >= 0 then uw.DropDeepTimeBonus else 0.0

        let threatPenalty =
            if bb.OpponentShape = OpponentShape.HighLine then
                uw.DropDeepHighLinePenalty
            else
                0.0

        clamp (uw.DropDeepBase + leadBonus + timeBonus + threatPenalty) 0.0 1.0

    let evaluateCounterPress
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        let uw = EngineWeightDefaults.defaults.Utility
        if not bb.JustLostBall then
            0.0
        else
            let staminaFactor = avgCondition state clubSide / 100.0
            let intensityBonus = emergent.PressingIntensity * uw.CounterPressIntensityBonus
            clamp (uw.CounterPressBase * staminaFactor + intensityBonus) 0.0 1.0

    let evaluateBuildFromBack
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        let uw = EngineWeightDefaults.defaults.Utility
        if bb.OurPhase = Defending then
            0.0
        else
            let comfortBonus =
                if bb.OpponentPressure = NoPress then uw.BuildFromBackNoPressBonus
                elif bb.OpponentPressure = MidPress then uw.BuildFromBackMidPressBonus
                else uw.BuildFromBackHighPressPenalty

            let shapeBonus = if bb.OpponentShape = LowBlock then uw.BuildFromBackLowBlockBonus else 0.0

            clamp (uw.BuildFromBackBase + comfortBonus + shapeBonus) 0.0 1.0

    let evaluatePlayWings bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        if bb.OurPhase = Defending then
            0.0
        else
            let wingSpace = if bb.WeaknessZones.Length > 0 then uw.WingSpaceBase else 0.0

            let staminaFactor = avgCondition state clubSide / 100.0
            clamp (uw.DropDeepBase + wingSpace + staminaFactor * uw.StaminaWingMult) 0.0 1.0

    let evaluateOverloadFlank flank bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        if bb.OurPhase = Defending then
            0.0
        else
            let targetFlank =
                match flank with
                | FlankLeft -> FlankZone.LeftFlank
                | FlankRight -> FlankZone.RightFlank

            let hasWeakness =
                bb.WeaknessZones |> Array.exists (fun z -> z = targetFlank)

            let bonus = if hasWeakness then uw.OverloadWeaknessBonus else 0.1
            clamp (uw.OverloadFlankBase + bonus) 0.0 1.0

    let evaluateDirectPlay bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let urgencyBonus =
            if sd < 0 && ml < 20.0 then uw.DirectPlayUrgencyBonus
            elif sd < 0 then uw.DirectPlayUrgencyBonusAny
            else 0.0

        let highLineBonus =
            if bb.OpponentShape = OpponentShape.HighLine then
                uw.DirectPlayHighLineBonus
            else
                0.0

        clamp (uw.DirectPlayBase + urgencyBonus + highLineBonus) 0.0 1.0

    let evaluateSitAndCounter bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        let sd = scoreDiffFor state clubSide

        if sd <= 0 then
            uw.SitAndCounterBase
        else
            let leadBonus = min (uw.SitAndCounterLeadBonus * 2.0) (float sd * uw.SitAndCounterLeadBonus)
            let staminaFactor = avgCondition state clubSide / 100.0
            clamp (uw.SitAndCounterBase + leadBonus + staminaFactor * uw.SitAndCounterStaminaFactor) 0.0 1.0

    let evaluateHoldPossession bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let leadBonus =
            if sd > 0 then uw.HoldPossessionLeadBonus
            elif sd = 0 then uw.HoldPossessionDrawBonus
            else uw.HoldPossessionLosingPenalty

        let timeBonus = if ml < 10.0 && sd >= 0 then uw.HoldPossessionTimeBonus else 0.0

        let pressPenalty = if bb.OpponentPressure = HighPress then uw.HoldPossessionPressPenalty else 0.0

        clamp (uw.HoldPossessionBase + leadBonus + timeBonus + pressPenalty) 0.0 1.0

    let evaluateCompactBlock bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let defensiveBonus =
            if sd < 0 then uw.CompactBlockLosingBonus
            elif sd > 0 then uw.CompactBlockWinningPenalty
            else 0.0

        let opponentBonus =
            if bb.OpponentShape = OpponentShape.HighLine then
                0.1
            else
                uw.CompactBlockOpponentBonus

        let timeBonus = if ml < 15.0 && sd >= 0 then uw.CompactBlockTimeBonus else 0.0

        clamp (uw.CompactBlockBase + defensiveBonus + opponentBonus + timeBonus) 0.0 1.0

    let evaluateHighLine bb emergent state clubSide ctx =
        let uw = EngineWeightDefaults.defaults.Utility
        if bb.OurPhase = Attacking then
            0.0
        else
            let cohesionBonus = emergent.CompactnessLevel * uw.HighLineCohesionBonus
            let staminaFactor = avgCondition state clubSide / 100.0

            let riskPenalty =
                if bb.OpponentShape = OpponentShape.HighLine then
                    uw.HighLineRiskPenalty
                else
                    0.0

            clamp (uw.HighLineBase + cohesionBonus + staminaFactor * uw.HighLineStaminaFactor + riskPenalty) 0.0 1.0

    let evaluateStructured _bb _emergent _state _clubSide _ctx =
        EngineWeightDefaults.defaults.Utility.StructuredBase

module UtilityActions =



    let evaluate
        (action: CollectiveAction)
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        : float =
        match action with
        | Press depth -> UtilityEvaluators.evaluatePress depth bb emergent state clubSide ctx
        | DropDeep -> UtilityEvaluators.evaluateDropDeep bb emergent state clubSide ctx
        | CounterPress -> UtilityEvaluators.evaluateCounterPress bb emergent state clubSide ctx
        | BuildFromBack -> UtilityEvaluators.evaluateBuildFromBack bb emergent state clubSide ctx
        | PlayWings -> UtilityEvaluators.evaluatePlayWings bb emergent state clubSide ctx
        | OverloadFlank flank -> UtilityEvaluators.evaluateOverloadFlank flank bb emergent state clubSide ctx
        | DirectPlay -> UtilityEvaluators.evaluateDirectPlay bb emergent state clubSide ctx
        | SitAndCounter -> UtilityEvaluators.evaluateSitAndCounter bb emergent state clubSide ctx
        | HoldPossession -> UtilityEvaluators.evaluateHoldPossession bb emergent state clubSide ctx
        | CompactBlock -> UtilityEvaluators.evaluateCompactBlock bb emergent state clubSide ctx
        | CollectiveAction.HighLine -> UtilityEvaluators.evaluateHighLine bb emergent state clubSide ctx
        | CollectiveAction.Structured -> UtilityEvaluators.evaluateStructured bb emergent state clubSide ctx

    let private allActions: CollectiveAction[] =
        [| Press PressHigh
           Press PressMid
           Press PressLow
           DropDeep
           CounterPress
           BuildFromBack
           PlayWings
           OverloadFlank FlankLeft
           OverloadFlank FlankRight
           DirectPlay
           SitAndCounter
           HoldPossession
           CompactBlock
           CollectiveAction.HighLine
           CollectiveAction.Structured |]

    let resolve
        (bb: TeamBlackboard)
        (prevAction: CollectiveAction option)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        : CollectiveAction =
        let mutable bestIdx = 0
        let mutable bestScore = -1.0
        let mutable prevScore = -1.0

        for i = 0 to allActions.Length - 1 do
            let score = evaluate allActions[i] bb emergent state clubSide ctx

            if score > bestScore then
                bestScore <- score
                bestIdx <- i

            match prevAction with
            | Some prev when prev = allActions[i] -> prevScore <- score
            | _ -> ()

        let best = allActions[bestIdx]

        match prevAction with
        | Some prev when prev <> best ->
            let uw = EngineWeightDefaults.defaults.Utility
            if bestScore > prevScore + uw.DirectiveChangeThreshold then best else prev
        | _ -> best

    let toDirectiveKind (action: CollectiveAction) : DirectiveKind =
        match action with
        | Press _
        | CounterPress
        | CollectiveAction.HighLine -> PressingBlock
        | DropDeep
        | CompactBlock
        | SitAndCounter -> DefensiveBlock
        | BuildFromBack
        | HoldPossession
        | CollectiveAction.Structured -> DirectiveKind.Structured
        | PlayWings
        | OverloadFlank _
        | DirectPlay -> DirectAttack
