namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player
open FootballEngine.Player.Actions
open FootballEngine.Player.Decision
open FootballEngine.Player.Intent
open FootballEngine.Player.Perception
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps

// ── BatchDecision ─────────────────────────────────────────────────────────────
//
// Responsabilidad única: ejecutar el pipeline individual por jugador.
//
// TeamOrchestrator ya escribió en TeamFrame:
//   SlotRoles[]         — qué rol colectivo tiene este slot
//   SupportPositionX/Y  — dónde quiere el equipo que estés
//   DefensiveRole[]     — FirstDefender / Cover / Marker
//
// BatchDecision lee esos arrays y los usa para sesgar el intent individual.
// No resuelve DirectiveKind. No asigna roles. No calcula support positions.
//
// Para mejorar la decisión individual: editar los scorers en Player/Decision/.
// Para mejorar la coordinación colectiva: editar TeamOrchestrator.
//
// ─────────────────────────────────────────────────────────────────────────────
module RoleBiasedScores =
    let apply (role: SlotRole) (scores: MovementScores) : MovementScores =
        { scores with
            PressBall = scores.PressBall * SlotRole.pressBallBias role
            SupportAttack = scores.SupportAttack * SlotRole.supportAttackBias role
            CoverSpace = scores.CoverSpace * SlotRole.coverSpaceBias role
            RecoverBall = scores.RecoverBall * SlotRole.recoverBallBias role }

module BatchDecision =

    let processTeam
        (currentSubTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (clubSide: ClubSide)
        (cFrame: CognitiveFrame)
        : unit =

        let team = SimStateOps.buildTeamPerspective clubSide ctx state
        let frame = team.OwnFrame
        let roster = team.OwnRoster
        let emergent = getEmergentState state clubSide
        let dir = team.AttackDir
        let ballPos = state.Ball.Position
        let ballXSmooth = state.BallXSmooth
        let phase = phaseFromBallZone dir ballXSmooth

        // ShapeEngine corre acá porque necesita la granularidad del cognition rate —
        // las posiciones de forma cambian más seguido que el DirectiveKind.
        let basePositions = getBasePositions state clubSide

        let tacticsCfg =
            tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

        let shapeTargetX = Array.zeroCreate<float32> frame.SlotCount
        let shapeTargetY = Array.zeroCreate<float32> frame.SlotCount
        ShapeEngine.computeShapeTargets basePositions dir phase ballXSmooth tacticsCfg shapeTargetX shapeTargetY

        SimStateOps.setCognitiveFrame state clubSide cFrame

        // ── Set piece override ────────────────────────────────────────────────
        let isSetPiece =
            match state.Flow with
            | RestartDelay _ -> true
            | _ -> false

        let setPiecePositions =
            if isSetPiece then
                SetPiecePositioning.computePositions ctx state clubSide
            else
                Array.empty

        if isSetPiece then
            SetPiecePositioning.assignPositions ctx setPiecePositions clubSide

        let influence =
            if clubSide = HomeClub then
                state.HomeInfluenceFrame
            else
                state.AwayInfluenceFrame

        let possessionHistory = state.PossessionHistory

        // ── Loop individual: intent sesgado por rol colectivo ─────────────────
        //
        // El rol colectivo (SlotRole) viene del TeamOrchestrator — ya está en el frame.
        // Este loop solo decide cómo cada jugador interpreta ese rol dado sus atributos.
        //
        // La tensión individual/colectiva emerge acá:
        //   RoleBiasedScores sesga, pero el softmax del jugador puede ignorarlo
        //   si sus atributos (Aggression alto, TeamWork bajo) producen otro score mayor.

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Sidelined _ -> ()
            | OccupancyKind.Active rosterIdx ->

                if not (IntentPhase.shouldRecalculate frame.Intent i currentSubTick possessionHistory) then
                    ()
                else
                    let player = roster.Players[rosterIdx]
                    let profile = roster.Profiles[rosterIdx]

                    let myX = float frame.Physics.PosX[i] * 1.0<meter>
                    let myY = float frame.Physics.PosY[i] * 1.0<meter>
                    let myVx = float frame.Physics.VelX[i] * 1.0<meter / second>
                    let myVy = float frame.Physics.VelY[i] * 1.0<meter / second>

                    let visibilityMask =
                        if isSetPiece then
                            ValueNone
                        else
                            ValueSome(
                                Perception.computeVisibilityMask
                                    i
                                    { X = myX
                                      Y = myY
                                      Z = 0.0<meter>
                                      Vx = myVx
                                      Vy = myVy
                                      Vz = 0.0<meter / second> }
                                    myVx
                                    myVy
                                    frame.Intent.Kind[i]
                                    frame.Intent.TargetX[i]
                                    frame.Intent.TargetY[i]
                                    player.Mental.Vision
                                    player.Mental.Positioning
                                    (player.Position = GK)
                                    state.Ball.Position
                                    frame
                                    team.OppFrame
                                    ctx.Config.Perception
                            )

                    let actx =
                        AgentContext.build
                            player
                            profile
                            i
                            team
                            ValueNone
                            frame.Intent.Kind[i]
                            frame.Intent.TargetX[i]
                            frame.Intent.TargetY[i]
                            frame.Intent.TargetPid[i]
                            state
                            clock
                            ctx
                            state.Config.Decision
                            state.Config.BuildUp
                            (Some cFrame)
                            visibilityMask
                            influence

                    let rawScores = MovementScorer.computeAll actx emergent

                    // Rol colectivo escrito por TeamOrchestrator — sesga sin forzar
                    let slotRole = frame.SlotRoles[i]
                    let biasedScores = RoleBiasedScores.apply slotRole rawScores

                    let intent = MovementScorer.pickIntent currentSubTick biasedScores actx

                    let myPos = defaultSpatial myX myY

                    let mutable finalIntent =
                        match intent with
                        | MaintainShape _ ->
                            let tx = float shapeTargetX[i] * 1.0<meter>
                            let ty = float shapeTargetY[i] * 1.0<meter>
                            MaintainShape(defaultSpatial tx ty)
                        | other -> other

                    if isSetPiece then
                        let pos =
                            if i < setPiecePositions.Length then
                                setPiecePositions[i]
                            else
                                myPos

                        finalIntent <- MoveToSetPiecePos pos

                    let kind, tx, ty, tpid = IntentFrame.fromMovementIntent finalIntent
                    FrameMutate.setIntent frame.Intent i kind tx ty tpid

                    let dur, trigger = IntentPhase.duration clock finalIntent
                    FrameMutate.commitIntent frame.Intent i currentSubTick dur trigger

                    match finalIntent with
                    | ExecuteRun run ->
                        let teamState = SimStateOps.getTeam state clubSide

                        teamState.ActiveRuns <-
                            run
                            :: (teamState.ActiveRuns |> List.filter (fun r -> r.PlayerId <> run.PlayerId))
                    | _ -> ()
