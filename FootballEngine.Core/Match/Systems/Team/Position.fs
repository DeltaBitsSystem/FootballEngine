namespace FootballEngine.Core.Match

/// Team position (ex TeamShape): where the team stands. Each pipeline stage
/// fixes a failure the previous one cannot see: the HELIOS formation gives a
/// coherent ball-tracking shape, the directive nudge carries the manager's
/// intent, lateral pairs balance symmetric coverage, and the Voronoi field
/// evaluator fixes the local spacing against real opponent positions. Writes
/// team.Shape; everything else reads it, so positioning is decided once per
/// scheduler pass — the Formation / FieldEvaluator / LateralPairs helpers are
/// pure modules.
module TeamPosition =

    /// Ball-zone anchors in the team's attacking frame (attack = +X): the ball
    /// lives in one of five corridors — center, own third, attacking third,
    /// and both wings — and each corridor needs its own sample for the
    /// interpolation to be well-conditioned everywhere.
    let private anchors: (float<meter> * float<meter>)[] =
        [| 0.0<meter>, 0.0<meter>
           -30.0<meter>, 0.0<meter>
           30.0<meter>, 0.0<meter>
           12.0<meter>, -22.0<meter>
           12.0<meter>, 22.0<meter> |]

    /// The directive carries the manager's intent, so the whole shape is nudged
    /// forward/back accordingly (replaces the old phaseShift/LineHeight/
    /// ballPull heuristic). Positive pushes toward the rival goal.
    let private directiveNudge (kind: DirectiveKind) : float =
        match kind with
        | DirectiveKind.Attack -> 8.0
        | DirectiveKind.Counter -> 6.0
        | DirectiveKind.Press -> 4.0
        | DirectiveKind.Defend -> -6.0
        | DirectiveKind.ParkTheBus -> -12.0
        | DirectiveKind.Balanced -> 0.0

    /// Symmetric lateral pairs (LB<->RB, LW<->RW) built from the lineup roles —
    /// only these roles are mirrored across the pitch axis, so only they can
    /// swap targets without breaking the shape.
    let private lateralPairs (ctx: MatchContext) (side: ClubSide) : (int * int)[] =
        let lineup = MatchWorld.lineup ctx side

        let byRole (role: SlotRole) =
            lineup.Profiles
            |> Array.indexed
            |> Array.filter (fun (_, p) -> p.Role = role)
            |> Array.map fst

        [| for role in [ SlotRole.FullBack; SlotRole.Winger ] do
               let slots = byRole role

               if slots.Length = 2 then
                   yield slots.[0], slots.[1] |]

    /// The loose-ball response is a small team plan, not eleven independent
    /// presses. It is recomputed with the intercept table because possession,
    /// ball flight and player movement can all change the best chaser.
    let private assignRoles (ctx: MatchContext) (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let cfg = ctx.Config.Team
        let ball = world.State.Ball.Position
        let dir = MatchMath.goalDirection side

        DefensiveTransitionState.clearTasks frame team.Defense

        let active = MatchWorld.activeSlots frame

        if active.Length > 0 then
            let nearest = MatchWorld.looseBallChaser world side |> Option.defaultValue -1

            let mutable rest1 = -1
            let mutable rest2 = -1
            let mutable restDistance1 = 0.0
            let mutable restDistance2 = 0.0

            // Only the first two sorted keys are needed; preserve the distance/slot ordering without materializing a sort.
            let precedes (distance: float) (slot: int) (bestDistance: float) (bestSlot: int) =
                let order = compare distance bestDistance
                bestSlot < 0 || order < 0 || (order = 0 && slot < bestSlot)

            for slot in active do
                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper && slot <> nearest then
                    let distance =
                        MatchMath.dist
                            (MatchMath.ownGoalX side)
                            0.0<meter>
                            (MatchWorld.posX frame slot)
                            (MatchWorld.posY frame slot)

                    if precedes distance slot restDistance1 rest1 then
                        rest2 <- rest1
                        restDistance2 <- restDistance1
                        rest1 <- slot
                        restDistance1 <- distance
                    elif precedes distance slot restDistance2 rest2 then
                        rest2 <- slot
                        restDistance2 <- distance

            let mutable cover = -1
            let mutable coverDistance = 0.0

            for slot in active do
                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper && slot <> nearest && slot <> rest1 && slot <> rest2 then
                    let distance = MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                    if precedes distance slot coverDistance cover then
                        cover <- slot
                        coverDistance <- distance

            for slot in active do
                frame.DefensiveRoles.[slot] <-
                    if slot = nearest then DefensiveRole.FirstDefender
                    elif slot = cover then DefensiveRole.Cover
                    elif slot = rest1 || slot = rest2 then DefensiveRole.Marker
                    else DefensiveRole.Unassigned

                let bias =
                    if slot = nearest then
                        1.0 + cfg.RoleBiasStrength
                    elif slot = cover then
                        1.0 + cfg.RoleBiasStrength * 0.3
                    elif slot = rest1 || slot = rest2 then
                        1.0 + cfg.RoleBiasStrength * 0.5
                    else
                        1.0

                frame.RoleBias.[slot] <- float32 bias

    let private refreshPositionCaches (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let dir = MatchMath.goalDirection side

        if team.OwnPositions.Length <> frame.SlotCount then
            team.OwnPositions <- Array.zeroCreate frame.SlotCount

        if team.MirroredPositions.Length <> frame.SlotCount then
            team.MirroredPositions <- Array.zeroCreate frame.SlotCount

        for i in 0 .. frame.SlotCount - 1 do
            let x = float frame.PosX.[i]
            let y = float frame.PosY.[i]
            team.OwnPositions.[i] <- FPoint.ofXY (x * dir) y
            team.MirroredPositions.[i] <- FPoint.ofXY (x * -dir) y

    let private positionTeam (ctx: MatchContext) (world: World) (side: ClubSide) (refineGroup: int voption) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let lineup = MatchWorld.lineup ctx side
        let posCfg = ctx.Config.Positioning
        let dir = MatchMath.goalDirection side
        let ball = world.State.Ball.Position
        let dt = 1.0 / 40.0
        let cfg = ctx.Config
        let interceptParams = InterceptTable.parameters cfg.Intercept cfg.Movement cfg.Ball
        let intercept = team.Intercept

        team.Emergent.DefenseLine <- float32 (DefenseLine.secondDeepest ctx world side)

        let ballStep = max 1 intercept.BallStep
        let pred = InterceptTable.ballInertiaPoint interceptParams ball ballStep

        // Work in the team's attacking frame (attack = +X).
        let ballFx = pred.X * dir
        let ballFy = pred.Y

        let formation =
            match team.FormationCache with
            | Some value -> value
            | None ->
                let roles = lineup.Profiles |> Array.map (fun p -> p.Role)
                // Lineup bases are already team-local; MatchFactory mirrors them into world space once.
                let value = Formation.fromLineup roles lineup.BasePositions 1.0 anchors
                team.FormationCache <- Some value
                value

        let offLine = MatchWorld.offsideLine ctx world side

        let evalParams: FieldEvaluator.EvaluatorParams =
            { TeammateWeight = posCfg.EvalTeammateWeight
              OpponentWeight = posCfg.EvalOpponentWeight
              ProgressWeight = posCfg.EvalGoalWeight
              OppOurWeight = float posCfg.EvalOppOurWeight
              SearchRadius = float posCfg.EvalSearchRadius
              CandidateCount = posCfg.EvalCandidateCount
              MinTeammateDist = float posCfg.EvalMinTeammateDist
              OffsideX = Some(float offLine * dir) }

        let pairParams: LateralPairs.PairParams =
            { SwapThreshold = float posCfg.LateralSwapThreshold }

        let nudge = directiveNudge team.Directive.Kind

        // Familiarity decides how crisply the plan lands: a drilled shape snaps
        // to the directive, a new one only half-follows it.
        let fam =
            float (if side = ClubSide.Home then ctx.HomeClub else ctx.AwayClub).Familiarity

        let crispNudge = nudge * (0.5 + 0.5 * fam)

        // The directive intent must reach the shape before local refinements apply,
        // so the nudge happens on the raw formation targets (team frame).
        if team.FormationTargets.Length <> frame.SlotCount then
            team.FormationTargets <- Array.zeroCreate frame.SlotCount

        Formation.queryInto formation ballFx ballFy team.FormationTargets

        for i in 0 .. frame.SlotCount - 1 do
            let target = team.FormationTargets.[i]
            team.FormationTargets.[i] <- FPoint.ofXY (target.X + crispNudge) target.Y

        // Pair swaps compare travel from where the slots are now, so the current
        // positions must be in the same frame as the targets.
        let ownPositions = team.OwnPositions

        // The evaluator must see the opponents where they really stand, so their
        // live positions (not the shape) feed the Voronoi term.
        let opponent = MatchWorld.team world (MatchWorld.oppOf side)
        let oppPositions = opponent.MirroredPositions

        let pairs =
            match team.LateralPairs with
            | Some value -> value
            | None ->
                let value = lateralPairs ctx side
                team.LateralPairs <- Some value
                value

        LateralPairs.solveInPlace pairParams ownPositions team.FormationTargets pairs

        // The formation cannot know the local spacing, so each slot's target is
        // refined against live positions before being written to world frame.
        let active = MatchWorld.activeSlots frame

        let gkSlot =
            match team.GoalkeeperSlot with
            | Some value -> value
            | None -> lineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Goalkeeper)

        let defending =
            team.Blackboard.OurPhase = PhaseKind.Defending
            || team.Blackboard.OurPhase = PhaseKind.TransitionDefend

        if team.EvaluatorScratch.Length < posCfg.EvalCandidateCount + 1 then
            team.EvaluatorScratch <- Array.zeroCreate (max 1 (posCfg.EvalCandidateCount + 1))

        let opponentActive = MatchWorld.activeSlots opponent.Frame

        for activeIndex in 0 .. active.Length - 1 do
            let i = active.[activeIndex]
            // Off a full update only the group whose turn it is re-reads the spacing; the others keep the target
            // they last chose. The keeper's angle follows the ball every update.
            let due =
                match refineGroup with
                | ValueSome group -> i = gkSlot || activeIndex % posCfg.RefineGroups = group
                | ValueNone -> true

            if due then
                let tx = team.FormationTargets.[i].X
                let ty = team.FormationTargets.[i].Y

                let refinedTarget =
                    if i = gkSlot then
                        // The keeper stands on the line from his goal centre to the predicted ball, so he covers the
                        // shooting angle and is already on the path of a ball played toward his goal.
                        let goalFx = -float Pitch.HalfLength
                        let towardX = float ballFx - goalFx
                        let towardY = float ballFy
                        let ballDistance = max 1.0 (sqrt (towardX * towardX + towardY * towardY))
                        let depth = min (float posCfg.GkMaxAdvance) (posCfg.GkDepthRatio * ballDistance)
                        let targetY = MatchMath.clamp (-float Pitch.GoalHalfWidth) (float Pitch.GoalHalfWidth) (depth * towardY / ballDistance)
                        FPoint.ofXY (goalFx + depth * towardX / ballDistance) targetY
                    else
                        FieldEvaluator.refineInto
                            evalParams
                            ownPositions
                            active
                            i
                            oppPositions
                            opponentActive
                            team.EvaluatorScratch
                            tx
                            ty

                let fx' =
                    if defending && i <> gkSlot then
                        min refinedTarget.X (float offLine * dir - 1.0)
                    else
                        refinedTarget.X

                let wx = fx' * dir
                let inset = float posCfg.TargetInset
                let safeHalfLength = float Pitch.HalfLength - inset
                let safeHalfWidth = float Pitch.HalfWidth - inset
                team.Shape.TargetsX.[i] <- float32 (MatchMath.clamp (-safeHalfLength) safeHalfLength wx)

                team.Shape.TargetsY.[i] <- float32 (MatchMath.clamp (-safeHalfWidth) safeHalfWidth refinedTarget.Y)

    /// The pass-lane owners are picked in distance order, and a plain sort would allocate its key array, closure
    /// and result on every reassessment. Thread-static because the away half of a tick runs on the lane thread.
    [<AbstractClass; Sealed>]
    type private OutletScratch =
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private order: int[]

        [<System.ThreadStatic; DefaultValue>]
        static val mutable private distance: float[]

        [<System.ThreadStatic; DefaultValue>]
        static val mutable private depth: float[]

        static member Ensure(count: int) =
            if isNull OutletScratch.order || OutletScratch.order.Length < count then
                OutletScratch.order <- Array.zeroCreate count
                OutletScratch.distance <- Array.zeroCreate count
                OutletScratch.depth <- Array.zeroCreate count

        static member Order = OutletScratch.order
        static member Distance = OutletScratch.distance
        static member Depth = OutletScratch.depth

    let private sameOpponent (previous: PlayerId voption) (next: PlayerId voption) =
        match previous, next with
        | ValueNone, ValueNone -> true
        | ValueSome a, ValueSome b -> a = b
        | _ -> false

    let private assignDefensiveTasks
        (ctx: MatchContext)
        (world: World)
        side
        mode
        (primary: int)
        (cover: int)
        rest1
        rest2
        =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let opponent = MatchWorld.team world (MatchWorld.oppOf side)
        let cfg = ctx.Config.DefensiveTransition
        let dir = MatchMath.goalDirection side
        let ball = world.State.Ball.Position
        let previousTasks = team.Defense.Tasks
        Array.fill frame.DefensiveRoles 0 frame.SlotCount DefensiveRole.Unassigned
        Array.fill frame.RoleBias 0 frame.SlotCount 1.0f

        // A task identical to the standing one is left in place, so a steady plan allocates nothing.
        let assign slot role (target: Vec2) (opponentId: PlayerId voption) =
            let inset = if role = DefensiveRole.FirstDefender then 0.0 else float ctx.Config.Positioning.TargetInset
            let x = MatchMath.clamp (-float Pitch.HalfLength + inset) (float Pitch.HalfLength - inset) (float target.X)
            let y = MatchMath.clamp (-float Pitch.HalfWidth + inset) (float Pitch.HalfWidth - inset) (float target.Y)
            let targetX = x * 1.0<meter>
            let targetY = y * 1.0<meter>

            match team.Defense.Tasks[slot] with
            | ValueSome previous when
                previous.Role = role
                && previous.Target.X = targetX
                && previous.Target.Y = targetY
                && sameOpponent previous.Opponent opponentId
                -> ()
            | _ ->
                team.Defense.Tasks[slot] <-
                    ValueSome
                        { Role = role
                          Target = Vec2.create targetX targetY
                          Opponent = opponentId }

            team.Frame.DefensiveRoles[slot] <- role

        for slot in 0 .. frame.SlotCount - 1 do
            if not (TransitionGeometry.outfielder frame slot) then team.Defense.Tasks[slot] <- ValueNone

        let protect slot =
            if slot >= 0 then
                let mutable runner = -1
                let mutable deepest = float ball.X * dir
                for candidate in MatchWorld.activeSlots opponent.Frame do
                    let sameCorridor =
                        if slot = rest1 then float opponent.Frame.PosY[candidate] <= float ball.Y
                        else float opponent.Frame.PosY[candidate] >= float ball.Y
                    if TransitionGeometry.outfielder opponent.Frame candidate && sameCorridor then
                        let x = float opponent.Frame.PosX[candidate] * dir
                        if x < deepest then runner <- candidate; deepest <- x
                let ownSide = if team.Frame.PosY[slot] < 0.0f then -1.0 else 1.0
                let y =
                    if runner >= 0 then (float ball.Y + float opponent.Frame.PosY[runner]) / 2.0
                    else float ball.Y + ownSide * float cfg.DepthSafety
                let opponentId = if runner >= 0 then ValueSome opponent.RosterIds[runner] else ValueNone
                let x = min (float team.Shape.TargetsX[slot] * dir) (deepest - float cfg.DepthSafety - float cfg.CoverDepth)
                assign slot DefensiveRole.ProtectDepth (Vec2.create (x * dir * 1.0<meter>) (MatchMath.clamp (-float cfg.CentralHalfWidth) (float cfg.CentralHalfWidth) y * 1.0<meter>)) opponentId

        protect rest1
        protect rest2

        let available slot =
            TransitionGeometry.outfielder team.Frame slot && slot <> rest1 && slot <> rest2
            && slot <> primary && slot <> cover

        if mode = Counterpress then
            if primary >= 0 then
                assign primary DefensiveRole.FirstDefender (TransitionGeometry.pressPoint ctx world side) ValueNone
                frame.RoleBias[primary] <- float32 (1.0 + ctx.Config.Team.RoleBiasStrength)

            if cover >= 0 then
                assign cover DefensiveRole.Cover (TransitionGeometry.coverPoint ctx world side) ValueNone

            let mutable screens = 0
            // Greedy one-to-one lane ownership is bounded by slots, not by combinations of player assignments.
            let opponentSlots = MatchWorld.activeSlots opponent.Frame
            OutletScratch.Ensure opponent.Frame.SlotCount
            let order = OutletScratch.Order
            let distances = OutletScratch.Distance
            let depths = OutletScratch.Depth

            for index in 0 .. opponentSlots.Length - 1 do
                let slot = opponentSlots[index]
                distances[slot] <-
                    MatchMath.dist ball.X ball.Y (MatchWorld.posX opponent.Frame slot) (MatchWorld.posY opponent.Frame slot)
                depths[slot] <- float opponent.Frame.PosX[slot] * dir

                // Insertion by (distance, depth, slot): a total order, so it matches the stable sort it replaces.
                let mutable position = index

                while position > 0
                      && (let other = order[position - 1]
                          distances[slot] < distances[other]
                          || (distances[slot] = distances[other]
                              && (depths[slot] < depths[other] || (depths[slot] = depths[other] && slot < other)))) do
                    order[position] <- order[position - 1]
                    position <- position - 1

                order[position] <- slot

            for index in 0 .. opponentSlots.Length - 1 do
                let outlet = order[index]
                let ox, oy = float opponent.Frame.PosX[outlet], float opponent.Frame.PosY[outlet]
                let distance = MatchMath.dist ball.X ball.Y (MatchWorld.posX opponent.Frame outlet) (MatchWorld.posY opponent.Frame outlet)
                let isCarrier =
                    match world.State.Ball.Control with
                    | BallControl.Controlled(_, pid) | BallControl.Receiving(_, pid, _) -> opponent.RosterIds[outlet] = pid
                    | _ -> false
                if screens < cfg.MaximumScreens && TransitionGeometry.outfielder opponent.Frame outlet && not isCarrier
                   && distance <= float ctx.Config.Actions.MaxPassExecutionDistance then
                    let target = Vec2.create
                                     ((float ball.X + (ox - float ball.X) * cfg.ScreenFraction - dir * float cfg.ApproachOffset) * 1.0<meter>)
                                     ((float ball.Y + (oy - float ball.Y) * cfg.ScreenFraction) * 1.0<meter>)
                    let outletId = opponent.RosterIds[outlet]
                    let mutable previous = -1
                    let slots = MatchWorld.activeSlots frame
                    let mutable scan = 0

                    while previous < 0 && scan < slots.Length do
                        let slot = slots[scan]

                        if available slot then
                            match previousTasks[slot] with
                            | ValueSome task when task.Role = DefensiveRole.Screen && task.Opponent = ValueSome outletId ->
                                previous <- slot
                            | _ -> ()

                        scan <- scan + 1

                    let struct (chosen, time) =
                        TransitionGeometry.chooseSlotFor cfg.ScreenEffort ctx frame target
                            (fun slot -> not (available slot) || team.Frame.DefensiveRoles[slot] = DefensiveRole.Screen)
                            previous cfg.HandoffAdvantage
                    if chosen >= 0 && time <= cfg.LocalArrivalWindow then
                        assign chosen DefensiveRole.Screen target (ValueSome outletId)
                        screens <- screens + 1

        let nearGoal =
            MatchMath.dist ball.X ball.Y (MatchMath.ownGoalX side) 0.0<meter> < float cfg.EngageGoalDistance

        if mode <> Counterpress && nearGoal then
            let mutable previous = -1

            for slot in MatchWorld.activeSlots frame do
                match previousTasks[slot] with
                | ValueSome task when previous < 0 && task.Role = DefensiveRole.Contain -> previous <- slot
                | _ -> ()

            let target = TransitionGeometry.pressPoint ctx world side

            // Only a defender still goal-side can stand in the carrier's way; one he has passed would only chase
            // him from behind into a foul.
            let goalSide slot = float frame.PosX[slot] * dir <= float ball.X * dir + float cfg.BeatenMargin

            let struct (contain, _) =
                TransitionGeometry.chooseSlotFor 1.0 ctx frame target (fun slot -> not (available slot && goalSide slot)) previous cfg.HandoffAdvantage

            if contain >= 0 then assign contain DefensiveRole.Contain target ValueNone

        for slot in MatchWorld.activeSlots frame do
            if TransitionGeometry.outfielder frame slot && frame.DefensiveRoles[slot] = DefensiveRole.Unassigned then
                let x = min (float team.Shape.TargetsX[slot] * dir) (float ball.X * dir - float cfg.RetreatBallGap)
                let width = min 1.0 (cfg.RetreatWidthScale * (1.0 + float team.Tactics.Width))
                assign slot DefensiveRole.RecoverShape (Vec2.create (x * dir * 1.0<meter>) (float team.Shape.TargetsY[slot] * width * 1.0<meter>)) ValueNone

    let private assignOrganizedDefense (ctx: MatchContext) (world: World) side =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let cfg = ctx.Config.DefensiveTransition
        let struct (rest1, rest2) = TransitionGeometry.restDefendersV world side
        let mutable hadTasks = false
        let mutable previousPrimary = -1
        let mutable previousCover = -1
        for slot in MatchWorld.activeSlots frame do
            match team.Defense.Tasks[slot] with
            | ValueSome task ->
                hadTasks <- true
                // The last slot holding the role wins, as the per-role scans it replaces resolved duplicates.
                if task.Role = DefensiveRole.FirstDefender then previousPrimary <- slot
                if task.Role = DefensiveRole.Cover then previousCover <- slot
            | ValueNone -> ()

        let ball = world.State.Ball.Position
        let dir = MatchMath.goalDirection side
        let pressGate = TeamDecide.pressGate team.Tactics
        let eligible slot =
            slot <> rest1 && slot <> rest2
            && float frame.Condition[slot] >= cfg.MinimumPressCondition
            && float frame.PosX[slot] * dir <= float ball.X * dir + float cfg.BeatenMargin

        let mutable depthThreat = rest2 < 0
        if rest2 >= 0 then
            let depthLine = float frame.PosX[rest2] * dir
            let opponents = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
            for slot in MatchWorld.activeSlots opponents do
                if TransitionGeometry.outfielder opponents slot
                   && float opponents.PosX[slot] * dir < depthLine - float cfg.DepthSafety
                   && abs (float opponents.PosY[slot]) <= float cfg.CentralHalfWidth then
                    depthThreat <- true

        let mutable reason = OrganizedUnavailable
        let mutable nextPrimary = -1
        let mutable nextCover = -1

        if float team.Tactics.PressIntensity < cfg.MinimumPressIntensity then reason <- OrganizedTacticalRestraint
        elif float ball.X * dir <= pressGate then reason <- OrganizedOutsidePressZone
        elif depthThreat then reason <- OrganizedDepthThreat
        else
            let struct (primary, arrival) =
                TransitionGeometry.chooseSlotFor 1.0 ctx frame (TransitionGeometry.pressPoint ctx world side)
                    (fun slot -> not (eligible slot)) previousPrimary cfg.HandoffAdvantage
            if primary < 0 || arrival > cfg.LocalArrivalWindow then reason <- OrganizedUnavailable
            else
                let struct (cover, coverArrival) =
                    TransitionGeometry.chooseSlotFor cfg.CoverEffort ctx frame (TransitionGeometry.coverPoint ctx world side)
                        (fun slot -> not (eligible slot) || slot = primary)
                        previousCover cfg.HandoffAdvantage
                if cover < 0 || coverArrival > cfg.CoverArrivalWindow then reason <- OrganizedUnavailable
                else
                    reason <- OrganizedPressure
                    nextPrimary <- primary
                    nextCover <- cover

        if world.Observation.Enabled && (not hadTasks || nextPrimary <> previousPrimary || nextCover <> previousCover) then
            let player slot = if slot >= 0 then Some team.RosterIds[slot] else None
            world.Observation.Record(world.State.Tick, ValueNone,
                OrganizedDefenseEvaluated { Side = side; Reason = reason; Primary = player nextPrimary; Cover = player nextCover }) |> ignore

        assignDefensiveTasks ctx world side (if reason = OrganizedPressure then Counterpress else Retreat)
            nextPrimary nextCover rest1 rest2

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            refreshPositionCaches world ClubSide.Home
            refreshPositionCaches world ClubSide.Away

            let cfg = ctx.Ctx.Config
            let interceptParams = InterceptTable.parameters cfg.Intercept cfg.Movement cfg.Ball
            InterceptTable.computeBothInto interceptParams world

            let position side =
                let team = MatchWorld.team world side

                // A team that did not position on the previous update (kick-off, a dead ball) refines everyone.
                let cadence = int ctx.Ctx.Config.DefensiveTransition.ReevaluateTicks
                let groups = ctx.Ctx.Config.Positioning.RefineGroups

                let refineGroup =
                    if groups <= 1 || team.LastRoleAssignmentTick < 0<subtick> || int (tick - team.LastRoleAssignmentTick) > cadence then
                        ValueNone
                    else
                        ValueSome((int tick / max 1 cadence) % groups)

                positionTeam ctx.Ctx world side refineGroup
                match team.Defense.Episode with
                | Some episode when episode.EndedAt.IsNone ->
                    let struct (rest1, rest2) = TransitionGeometry.restDefendersV world side
                    assignDefensiveTasks ctx.Ctx world side episode.Mode (Option.defaultValue -1 episode.Primary) (Option.defaultValue -1 episode.Cover) rest1 rest2
                | _ ->
                    match world.State.Flow, MatchWorld.ballCarrier ctx.Ctx world with
                    | MatchFlow.Live, ValueSome(struct (owner, _, _)) when owner <> side -> assignOrganizedDefense ctx.Ctx world side
                    | _ -> assignRoles ctx.Ctx world side
                team.LastRoleAssignmentTick <- tick

            MatchWorld.bothTeams world position

            rng
