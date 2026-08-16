namespace FootballEngine.Core.Match

/// Team position (ex TeamShape): where the team stands. Orchestrates the
/// collective positioning pipeline — HELIOS formation -> directive nudge ->
/// lateral pairs -> Voronoi field evaluator — and writes team.Shape;
/// everything else reads it. Single system in the scheduler; the Formation /
/// FieldEvaluator / LateralPairs helpers are pure modules.
module TeamPosition =

    /// Ball-zone anchors in the team's attacking frame (attack = +X): center,
    /// own third, attacking third, and both wings.
    let private anchors: (float<meter> * float<meter>)[] =
        [| 0.0<meter>, 0.0<meter>
           -30.0<meter>, 0.0<meter>
           30.0<meter>, 0.0<meter>
           12.0<meter>, -22.0<meter>
           12.0<meter>, 22.0<meter> |]

    /// Forward/back nudge applied to the whole shape by the current directive
    /// (replaces the old phaseShift/LineHeight/ballPull heuristic). Positive
    /// pushes toward the rival goal.
    let private directiveNudge (kind: DirectiveKind) : float =
        match kind with
        | DirectiveKind.Attack -> 8.0
        | DirectiveKind.Counter -> 6.0
        | DirectiveKind.Press -> 4.0
        | DirectiveKind.Defend -> -6.0
        | DirectiveKind.ParkTheBus -> -12.0
        | DirectiveKind.Balanced -> 0.0

    /// Symmetric lateral pairs (LB<->RB, LW<->RW) built from the lineup roles.
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

    /// R7 — linear role assignment. Written only when the directive changes:
    /// the slot closest to the ball presses, the most retarded anchors, two
    /// intermediates cover, and everyone else supports. Deterministic: no Rng.
    let private assignRoles (ctx: MatchContext) (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let cfg = ctx.Config.Team
        let ball = world.State.Ball.Position
        let dir = MatchMath.goalDirection side

        let active = MatchWorld.activeSlots frame

        if active.Length > 0 then
            // Nearest to the ball presses.
            let nearest =
                active
                |> Array.minBy (fun slot ->
                    if slot = 0 then
                        System.Double.MaxValue
                    else
                        MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot))

            // Most retarded (farthest toward own goal) anchors a defensive spot.
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

            // Two intermediate candidates get the cover bias.
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

    /// Active positions of a team converted to the given team's attacking frame.
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

        // Base targets from the formation, nudged by the directive (team frame).
        let baseTargets =
            Formation.query formation ballFx ballFy
            |> Array.map (fun (x, y) -> float x + nudge, float y)

        // Slot-indexed current positions in this team's frame, for pair swaps.
        let ownPositions =
            let frame = (MatchWorld.team world side).Frame
            Array.init frame.SlotCount (fun i -> float frame.PosX.[i] * dir, float frame.PosY.[i])

        // Active opponent positions for the Voronoi evaluator (compact).
        let oppPositions = activePositions world (MatchWorld.oppOf side) dir

        // Apply lateral pair swaps to the base targets.
        let pairedTargets =
            LateralPairs.solve pairParams ownPositions baseTargets (lateralPairs ctx side)

        // Refine each slot with the Voronoi evaluator, then write to world frame.
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

            // Back to world frame, then clamp to the pitch.
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

                // R7 — reassign roles only when the directive just changed.
                if tick - team.Directive.SinceTick <= 240<subtick> then
                    assignRoles ctx.Ctx world side

                positionTeam ctx.Ctx world side

            rng
