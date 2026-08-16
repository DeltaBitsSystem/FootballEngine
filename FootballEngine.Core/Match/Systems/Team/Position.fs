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

    /// R7 — linear role assignment. The team needs exactly one designated
    /// presser and one anchor on every ball, and position alone decides it:
    /// the slot closest to the ball presses, the most retarded anchors, two
    /// intermediates cover, everyone else supports. Reassigned only when the
    /// directive changes so the roles do not thrash. Deterministic: no Rng.
    let private assignRoles (ctx: MatchContext) (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let cfg = ctx.Config.Team
        let ball = world.State.Ball.Position
        let dir = MatchMath.goalDirection side

        let active = MatchWorld.activeSlots frame

        if active.Length > 0 then
            let nearest =
                active
                |> Array.minBy (fun slot ->
                    if slot = 0 then
                        System.Double.MaxValue
                    else
                        MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot))

            let anchor =
                active
                |> Array.maxBy (fun slot ->
                    if slot = 0 then
                        -System.Double.MaxValue
                    else
                        -MatchMath.dist
                            (MatchMath.ownGoalX side)
                            0.0<meter>
                            (MatchWorld.posX frame slot)
                            (MatchWorld.posY frame slot))

            let ranked =
                active
                |> Array.filter (fun slot -> slot <> nearest && slot <> anchor && slot <> 0)
                |> Array.sortBy (fun slot ->
                    MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot))

            let covers = ranked |> Array.truncate 2 |> Set.ofArray

            for slot in active do
                let bias =
                    if slot = anchor then
                        1.0 + cfg.RoleBiasStrength * 0.5
                    elif covers.Contains slot then
                        1.0 + cfg.RoleBiasStrength * 0.3
                    elif slot = nearest then
                        1.0 + cfg.RoleBiasStrength
                    else
                        1.0

                frame.RoleBias.[slot] <- float32 bias

    /// Active positions of a team converted to the given team's attacking frame —
    /// the evaluator needs every side in the same frame to compare distances.
    let private activePositions (world: World) (side: ClubSide) (dir: float) : (float * float)[] =
        let frame = (MatchWorld.team world side).Frame

        MatchWorld.activeSlots frame
        |> Array.map (fun i -> float frame.PosX.[i] * dir, float frame.PosY.[i])

    let private positionTeam (ctx: MatchContext) (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let lineup = MatchWorld.lineup ctx side
        let posCfg = ctx.Config.Positioning
        let dir = MatchMath.goalDirection side
        let ball = world.State.Ball.Position
        let dt = 1.0 / 40.0
        let cfg = ctx.Config

        let interceptParams: InterceptTable.InterceptParams =
            { BallDecayPerTick = float cfg.Ball.AirDrag ** dt * float cfg.Ball.GroundFriction ** dt
              PlayerSpeedPerSecond = cfg.Movement.SprintSpeed
              ControlRadius = cfg.Intercept.ControlRadius
              MaxSteps = cfg.Intercept.MaxSteps }

        let intercept = InterceptTable.compute interceptParams world side
        team.Intercept <- intercept

        team.Emergent.DefenseLine <- float32 (DefenseLine.secondDeepest ctx world side)

        let ballStep = max 1 intercept.BallStep
        let pred = InterceptTable.ballInertiaPoint interceptParams ball ballStep

        // Work in the team's attacking frame (attack = +X).
        let ballFx = pred.X * dir
        let ballFy = pred.Y

        let roles = lineup.Profiles |> Array.map (fun p -> p.Role)
        let formation = Formation.fromLineup roles lineup.BasePositions dir anchors

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

        // The directive intent must reach the shape before local refinements apply,
        // so the nudge happens on the raw formation targets (team frame).
        let baseTargets =
            Formation.query formation ballFx ballFy
            |> Array.map (fun (x, y) -> float x + nudge, float y)

        // Pair swaps compare travel from where the slots are now, so the current
        // positions must be in the same frame as the targets.
        let ownPositions =
            let frame = (MatchWorld.team world side).Frame
            Array.init frame.SlotCount (fun i -> float frame.PosX.[i] * dir, float frame.PosY.[i])

        // The evaluator must see the opponents where they really stand, so their
        // live positions (not the shape) feed the Voronoi term.
        let oppPositions = activePositions world (MatchWorld.oppOf side) dir

        let pairedTargets =
            LateralPairs.solve pairParams ownPositions baseTargets (lateralPairs ctx side)

        // The formation cannot know the local spacing, so each slot's target is
        // refined against live positions before being written to world frame.
        let active = MatchWorld.activeSlots frame

        let gkSlot =
            lineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Goalkeeper)

        let defending =
            team.Blackboard.OurPhase = PhaseKind.Defending
            || team.Blackboard.OurPhase = PhaseKind.TransitionDefend

        for i in active do
            let tx, ty = pairedTargets.[i]

            let fx, fy =
                if i = gkSlot then
                    tx, ty
                else
                    // Exclude the slot's own current position from the spread term.
                    let teams =
                        active
                        |> Array.filter (fun j -> j <> i)
                        |> Array.map (fun j -> ownPositions.[j])

                    FieldEvaluator.refine evalParams teams oppPositions tx ty

            let fx' =
                if defending && i <> gkSlot then
                    min fx (float offLine * dir - 1.0)
                else
                    fx

            let wx = fx' * dir
            let wy = fy
            let cx, cy = MatchMath.pitchClamp wx wy
            team.Shape.TargetsX.[i] <- float32 cx
            team.Shape.TargetsY.[i] <- float32 cy

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            for side in [ ClubSide.Home; ClubSide.Away ] do
                let team = MatchWorld.team world side

                // R7 — roles follow the directive; reassign only right after it changed,
                // so the linear assignment does not fight the current plan.
                if tick - team.Directive.SinceTick <= 240<subtick> then
                    assignRoles ctx.Ctx world side

                positionTeam ctx.Ctx world side

            rng
