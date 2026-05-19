namespace FootballEngine

open FootballEngine.Domain
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
        let ballZoneBonus =
            match depth, bb.BallZone with
            | PressHigh, AttackingZone -> 0.4
            | PressHigh, MidfieldZone -> 0.1
            | PressHigh, _ -> -0.3
            | PressMid, (AttackingZone | MidfieldZone) -> 0.3
            | PressMid, _ -> -0.1
            | PressLow, _ -> 0.1

        let avgCond = avgCondition state clubSide
        let staminaFactor = avgCond / 100.0

        let opponentBonus =
            if bb.OpponentShape = OpponentShape.HighLine && bb.OpponentPressure = NoPress then
                0.3
            else
                0.0

        let successBonus = emergent.PressingIntensity * 0.2

        clamp (ballZoneBonus * staminaFactor + opponentBonus + successBonus) 0.0 1.0

    let evaluateDropDeep
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let leadBonus =
            if sd > 0 then min 0.5 (float sd * 0.2)
            elif sd < 0 then -0.4
            else 0.0

        let timeBonus = if ml < 15.0 && sd >= 0 then 0.3 else 0.0

        let threatPenalty =
            if bb.OpponentShape = OpponentShape.HighLine then
                -0.2
            else
                0.0

        clamp (0.3 + leadBonus + timeBonus + threatPenalty) 0.0 1.0

    let evaluateCounterPress
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        if not bb.JustLostBall then
            0.0
        else
            let staminaFactor = avgCondition state clubSide / 100.0
            let intensityBonus = emergent.PressingIntensity * 0.3
            clamp (0.5 * staminaFactor + intensityBonus) 0.0 1.0

    let evaluateBuildFromBack
        (bb: TeamBlackboard)
        (emergent: EmergentState)
        (state: SimState)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        =
        if bb.OurPhase = Defending then
            0.0
        else
            let comfortBonus =
                if bb.OpponentPressure = NoPress then 0.4
                elif bb.OpponentPressure = MidPress then 0.1
                else -0.2

            let shapeBonus = if bb.OpponentShape = LowBlock then 0.2 else 0.0

            clamp (0.3 + comfortBonus + shapeBonus) 0.0 1.0

    let evaluatePlayWings bb emergent state clubSide ctx =
        if bb.OurPhase = Defending then
            0.0
        else
            let wingSpace = if bb.WeaknessZones.Length > 0 then 0.3 else 0.0

            let staminaFactor = avgCondition state clubSide / 100.0
            clamp (0.3 + wingSpace + staminaFactor * 0.2) 0.0 1.0

    let evaluateOverloadFlank flank bb emergent state clubSide ctx =
        if bb.OurPhase = Defending then
            0.0
        else
            let targetFlank =
                match flank with
                | FlankLeft -> FlankZone.LeftFlank
                | FlankRight -> FlankZone.RightFlank

            let hasWeakness =
                bb.WeaknessZones |> Array.exists (fun z -> z = targetFlank)

            let bonus = if hasWeakness then 0.4 else 0.1
            clamp (0.3 + bonus) 0.0 1.0

    let evaluateDirectPlay bb emergent state clubSide ctx =
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let urgencyBonus =
            if sd < 0 && ml < 20.0 then 0.4
            elif sd < 0 then 0.2
            else 0.0

        let highLineBonus =
            if bb.OpponentShape = OpponentShape.HighLine then
                0.3
            else
                0.0

        clamp (0.2 + urgencyBonus + highLineBonus) 0.0 1.0

    let evaluateSitAndCounter bb emergent state clubSide ctx =
        let sd = scoreDiffFor state clubSide

        if sd <= 0 then
            0.1
        else
            let leadBonus = min 0.3 (float sd * 0.15)
            let staminaFactor = avgCondition state clubSide / 100.0
            clamp (0.3 + leadBonus + staminaFactor * 0.2) 0.0 1.0

    let evaluateHoldPossession bb emergent state clubSide ctx =
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let leadBonus =
            if sd > 0 then 0.3
            elif sd = 0 then 0.1
            else -0.2

        let timeBonus = if ml < 10.0 && sd >= 0 then 0.3 else 0.0

        let pressPenalty = if bb.OpponentPressure = HighPress then -0.2 else 0.0

        clamp (0.3 + leadBonus + timeBonus + pressPenalty) 0.0 1.0

    let evaluateCompactBlock bb emergent state clubSide ctx =
        let sd = scoreDiffFor state clubSide
        let ml = minutesLeft state

        let defensiveBonus =
            if sd < 0 then 0.2
            elif sd > 0 then -0.1
            else 0.0

        let opponentBonus =
            if bb.OpponentShape = OpponentShape.HighLine then
                0.1
            else
                0.2

        let timeBonus = if ml < 15.0 && sd >= 0 then 0.2 else 0.0

        clamp (0.3 + defensiveBonus + opponentBonus + timeBonus) 0.0 1.0

    let evaluateHighLine bb emergent state clubSide ctx =
        if bb.OurPhase = Attacking then
            0.0
        else
            let cohesionBonus = emergent.CompactnessLevel * 0.3
            let staminaFactor = avgCondition state clubSide / 100.0

            let riskPenalty =
                if bb.OpponentShape = OpponentShape.HighLine then
                    -0.2
                else
                    0.0

            clamp (0.2 + cohesionBonus + staminaFactor * 0.2 + riskPenalty) 0.0 1.0

    let evaluateStructured _bb _emergent _state _clubSide _ctx = 0.25

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
        | Some prev when prev <> best -> if bestScore > prevScore + 0.15 then best else prev
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
