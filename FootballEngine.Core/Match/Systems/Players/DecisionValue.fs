namespace FootballEngine.Core.Match

/// What an option is worth, in goal probability: the chance it adds to the carrier's side scoring minus the chance
/// it gives the opponent. One scale for every kind of action, so a choice between a shot, a pass and a carry is a
/// comparison of numbers that mean the same thing, and skill enters only as the chance an action works.
module DecisionValue =

    /// Chance a possession at this spot ends in a goal for `side`.
    let threat (e: ExpectedValueConfig) (side: ClubSide) (x: float<meter>) (y: float<meter>) : float =
        let d = MatchMath.distanceToGoal side x y
        e.ThreatFloor + e.ThreatPeak * exp (-d / float e.ThreatDecay)

    /// Chance a ball lost at this spot ends in a goal for the opponent of `side`.
    let concession (e: ExpectedValueConfig) (side: ClubSide) (x: float<meter>) (y: float<meter>) : float =
        threat e (MatchWorld.oppOf side) x y

    /// Chance a pass arrives, from the intercept race margin (steps) and the passer's Passing: a good passer
    /// weights and places the ball so the receiver gets it earlier than the race of a plain ball says.
    let passCompletion (e: ExpectedValueConfig) (passing: float) (margin: int) : float =
        let effective = float margin + e.PassSkillSteps * (passing - 60.0) / 40.0
        1.0 / (1.0 + exp (-effective / e.PassMarginScale))

    /// A pass: the receiver's threat if it arrives, the opponent's threat where the lane is cut if it does not.
    let pass
        (e: ExpectedValueConfig)
        (side: ClubSide)
        (completion: float)
        (fromX: float<meter>)
        (fromY: float<meter>)
        (toX: float<meter>)
        (toY: float<meter>)
        : float =
        completion * threat e side toX toY
        - (1.0 - completion)
          * concession e side ((fromX + toX) / 2.0) ((fromY + toY) / 2.0)

    /// Chance a carry survives the nearest opponent, the others closing in and those standing in its path.
    let carryKeep
        (e: ExpectedValueConfig)
        (dribbling: float)
        (tackling: float)
        (opponents: int)
        (blockers: int)
        : float =
        MatchMath.clamp01 (
            e.CarryKeepBase + e.CarrySkillWeight * (dribbling - tackling)
            - e.CarryPressurePenalty * float opponents
            - e.CarryBlockPenalty * float blockers
        )

    /// Outfield opponents inside the triangle from the ball to the posts, widened by half a metre for their body.
    let shotBlockers (world: World) (side: ClubSide) (x: float<meter>) (y: float<meter>) : int =
        let opponent = MatchWorld.team world (MatchWorld.oppOf side)
        let dir = MatchMath.goalDirection side
        let depth = (MatchMath.goalX side - x) * dir
        let mutable count = 0

        for s in MatchWorld.activeSlots opponent.Frame do
            // A match on the option, not `<> Some s`: the comparison boxed a new option per opponent.
            let isKeeper =
                match opponent.GoalkeeperSlot with
                | Some keeper -> keeper = s
                | None -> false

            if not isKeeper && depth > 0.0<meter> then
                let ahead = (MatchWorld.posX opponent.Frame s - x) * dir
                let t = ahead / depth

                if t > 0.0 && t < 1.0 then
                    let oy = MatchWorld.posY opponent.Frame s
                    let low = y + (-Pitch.GoalHalfWidth - y) * t - 0.5<meter>
                    let high = y + (Pitch.GoalHalfWidth - y) * t + 0.5<meter>

                    if oy > low && oy < high then
                        count <- count + 1

        count

    /// Opponents inside a 45° half-angle cone toward goal, within CarryBlockRange.
    let blockers (e: ExpectedValueConfig) (world: World) (side: ClubSide) (x: float<meter>) (y: float<meter>) : int =
        let opp = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
        let dir = MatchMath.goalDirection side
        let mutable count = 0

        for s in MatchWorld.activeSlots opp do
            let ahead = (MatchWorld.posX opp s - x) * dir
            let lateral = abs (MatchWorld.posY opp s - y)

            if ahead > 0.0<meter> && ahead < e.CarryBlockRange && lateral < ahead then
                count <- count + 1

        count

    let carry (e: ExpectedValueConfig) (side: ClubSide) (keep: float) (x: float<meter>) (y: float<meter>) : float =
        let ahead = x + e.CarryAdvance * MatchMath.goalDirection side
        keep * threat e side ahead y - (1.0 - keep) * concession e side x y

    /// The attacker a cross from `slot` would be aimed at: the one nearest goal and the delivery spot, finishers
    /// first; -1 when nobody is there. The decision and the delivery share it, so a cross is only chosen when the
    /// delivery has someone to find.
    let crossTarget (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : int =
        let frame = (MatchWorld.team world side).Frame
        let targetX = MatchMath.goalX side - 6.0<meter> * MatchMath.goalDirection side
        let mutable bestSlot = -1
        let mutable bestScore = 0.0

        for s2 in MatchWorld.activeSlots frame do
            if s2 <> slot && s2 <> 0 then
                let rx = MatchWorld.posX frame s2
                let ry = MatchWorld.posY frame s2
                let dToSpot = MatchMath.dist targetX 0.0<meter> rx ry
                let dToGoal = MatchMath.distanceToGoal side rx ry

                if dToSpot < float ctx.Config.Actions.CrossTargetRadius + 5.0 && dToGoal < 22.0 then
                    let score =
                        (22.0 - dToGoal) * 0.5 - dToSpot * 0.2
                        + float (MatchWorld.profileOf ctx side s2).Finishing / 50.0

                    if score > bestScore then
                        bestScore <- score
                        bestSlot <- s2

        bestSlot

    /// A cross met by a teammate is a header at goal; one that is not is the opponent's ball in their own box.
    let cross (e: ExpectedValueConfig) (side: ClubSide) (crossing: float) : float =
        let met = MatchMath.clamp01 (e.CrossBaseSuccess * crossing / 60.0)
        let boxX = MatchMath.goalX side - 8.0<meter> * MatchMath.goalDirection side
        met * e.HeaderConversion - (1.0 - met) * concession e side boxX 0.0<meter>

    /// A clearance gives up the ball but moves the danger: it concedes a share of the opponent's threat here.
    let clear (e: ExpectedValueConfig) (side: ClubSide) (x: float<meter>) (y: float<meter>) : float =
        -e.ClearConcession * concession e side x y

    /// A ball held at this spot: kept, it is worth its threat; the nearest opponent may take it, less so with a
    /// teammate in support.
    let held
        (e: ExpectedValueConfig)
        (side: ClubSide)
        (x: float<meter>)
        (y: float<meter>)
        (nearestOpponent: float)
        (supported: bool)
        : float =
        let loss =
            e.LossNear
            * exp (-nearestOpponent / float e.LossRange)
            * (if supported then 1.0 - e.SupportRelief else 1.0)

        (1.0 - loss) * threat e side x y - loss * concession e side x y
