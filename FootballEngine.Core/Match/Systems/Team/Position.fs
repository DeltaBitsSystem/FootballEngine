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

        let active = MatchWorld.activeSlots frame

        if active.Length > 0 then
            let nearest = MatchWorld.looseBallChaser world side |> Option.defaultValue -1

            let rest =
                active
                |> Array.filter (fun slot -> frame.SlotRoles.[slot] <> SlotRole.Goalkeeper && slot <> nearest)
                |> Array.sortBy (fun slot ->
                    MatchMath.dist
                        (MatchMath.ownGoalX side)
                        0.0<meter>
                        (MatchWorld.posX frame slot)
                        (MatchWorld.posY frame slot),
                    slot)
                |> Array.truncate 2

            let cover =
                active
                |> Array.filter (fun slot -> frame.SlotRoles.[slot] <> SlotRole.Goalkeeper && slot <> nearest && not (Array.contains slot rest))
                |> Array.sortBy (fun slot ->
                    MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot),
                    slot)
                |> Array.tryHead

            for slot in active do
                frame.DefensiveRoles.[slot] <-
                    if slot = nearest then DefensiveRole.FirstDefender
                    elif Some slot = cover then DefensiveRole.Cover
                    elif Array.contains slot rest then DefensiveRole.Marker
                    else DefensiveRole.Unassigned

                let bias =
                    if slot = nearest then
                        1.0 + cfg.RoleBiasStrength
                    elif Some slot = cover then
                        1.0 + cfg.RoleBiasStrength * 0.3
                    elif Array.contains slot rest then
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

    let private positionTeam (ctx: MatchContext) (world: World) (side: ClubSide) : unit =
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
                let value = Formation.fromLineup roles lineup.BasePositions dir anchors
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
            | None ->
                let value =
                    lineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Goalkeeper)

                team.GoalkeeperSlot <- Some value
                value

        let defending =
            team.Blackboard.OurPhase = PhaseKind.Defending
            || team.Blackboard.OurPhase = PhaseKind.TransitionDefend

        if team.EvaluatorScratch.Length < posCfg.EvalCandidateCount + 1 then
            team.EvaluatorScratch <- Array.zeroCreate (max 1 (posCfg.EvalCandidateCount + 1))

        let opponentActive = MatchWorld.activeSlots opponent.Frame

        for activeIndex in 0 .. active.Length - 1 do
            let i = active.[activeIndex]
            let tx = team.FormationTargets.[i].X
            let ty = team.FormationTargets.[i].Y

            let refinedTarget =
                if i = gkSlot then
                    let ownGoalX = float (MatchMath.ownGoalX side)
                    let ballDistance = abs (float ball.X - ownGoalX)
                    let opponentDistance =
                        opponentActive
                        |> Array.map (fun opponentSlot ->
                            MatchMath.dist
                                (MatchMath.ownGoalX side)
                                0.0<meter>
                                (MatchWorld.posX opponent.Frame opponentSlot)
                                (MatchWorld.posY opponent.Frame opponentSlot))
                        |> fun distances ->
                            if Array.isEmpty distances then
                                System.Double.MaxValue
                            else
                                Array.min distances

                    let threat =
                        ballDistance < float posCfg.GkThreatDistance
                        || opponentDistance < float posCfg.GkThreatDistance

                    let targetX =
                        if threat then
                            ownGoalX * dir
                        elif float ballFx > 0.0 then
                            tx + float posCfg.GkMaxAdvance
                        else
                            tx

                    FPoint.ofXY targetX 0.0
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

            team.Shape.TargetsY.[i] <-
                float32 (MatchMath.clamp (-safeHalfWidth) safeHalfWidth refinedTarget.Y)

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

                assignRoles ctx.Ctx world side
                team.LastRoleAssignmentTick <- tick

                positionTeam ctx.Ctx world side

            position ClubSide.Home
            position ClubSide.Away

            rng
