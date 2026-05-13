namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player.Actions
open FootballEngine.Player.Decision
open FootballEngine.Player.Intent
open FootballEngine.Player.Perception
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps


module CognitionSystem =

    let private compliance (me: Player) (condition: float32) (urgency: float) : float =
        let baseObedience = float me.Mental.WorkRate / 20.0
        let conditionFactor = float condition / 100.0
        let urgencyBoost = urgency * 0.15
        System.Math.Clamp(baseObedience * conditionFactor + urgencyBoost, 0.05, 1.0)

    let private individualPipeline (ctx: AgentContext) : MovementScores =
        { MaintainShape = MovementScorer.maintainShapeScore ctx
          MarkMan = MovementScorer.markManScore ctx + MovementScorer.interceptPassScore ctx
          PressBall = MovementScorer.pressBallScore ctx
          CoverSpace = MovementScorer.coverSpaceScore ctx
          SupportAttack = MovementScorer.supportAttackScore ctx
          RecoverBall = MovementScorer.recoverBallScore ctx
          MoveToSetPiecePos = 0.0 }

    let run (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : SystemOutput[] =

        let outputs = ResizeArray<SystemOutput>()

        let homeInf =
            InfluenceFrame.compute (getFrame state HomeClub) (getFrame state AwayClub)

        let awayInf =
            InfluenceFrame.compute (getFrame state AwayClub) (getFrame state HomeClub)

        let homeCF = CognitiveFrameModule.build ctx state HomeClub
        let awayCF = CognitiveFrameModule.build ctx state AwayClub
        outputs.Add(InfluenceFrameUpdate(HomeClub, homeInf))
        outputs.Add(InfluenceFrameUpdate(AwayClub, awayInf))
        outputs.Add(CognitiveFrameUpdate(HomeClub, homeCF))
        outputs.Add(CognitiveFrameUpdate(AwayClub, awayCF))

        let possessionHistory = state.PossessionHistory

        for clubSide in [| HomeClub; AwayClub |] do
            let team = buildTeamPerspective clubSide ctx state
            let frame = team.OwnFrame
            let roster = team.OwnRoster
            let cFrame = if clubSide = HomeClub then homeCF else awayCF
            let influence = if clubSide = HomeClub then homeInf else awayInf
            let side = if clubSide = HomeClub then HomeFrame else AwayFrame
            let dir = team.AttackDir
            let ballXSmooth = state.BallXSmooth
            let phase = phaseFromBallZone dir ballXSmooth

            let basePositions = getBasePositions state clubSide

            let tacticsCfg =
                tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

            let shapeTargetX = Array.zeroCreate<float32> frame.SlotCount
            let shapeTargetY = Array.zeroCreate<float32> frame.SlotCount
            ShapeEngine.computeShapeTargets basePositions dir phase ballXSmooth tacticsCfg shapeTargetX shapeTargetY

            let isSetPiece =
                match state.Flow with
                | RestartDelay _ -> true
                | _ -> false

            let setPiecePositions =
                if isSetPiece then
                    SetPiecePositioning.computePositions ctx state clubSide
                else
                    Array.empty

            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Sidelined _ -> ()
                | OccupancyKind.Active rosterIdx ->
                    if not (IntentPhase.shouldRecalculate frame.Intent i subTick possessionHistory) then
                        ()
                    else
                        let player = roster.Players[rosterIdx]
                        let profile = roster.Profiles[rosterIdx]

                        let previousIntent =
                            match frame.Intent.Kind[i] with
                            | IntentKind.Idle -> ValueNone
                            | kind ->
                                ValueSome(
                                    IntentFrame.toMovementIntent
                                        kind
                                        frame.Intent.TargetX[i]
                                        frame.Intent.TargetY[i]
                                        frame.Intent.TargetPid[i]
                                        (defaultSpatial
                                            (float frame.Intent.TargetX[i] * 1.0<meter>)
                                            (float frame.Intent.TargetY[i] * 1.0<meter>))
                                )

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
                                previousIntent
                                state
                                clock
                                ctx
                                state.Config.Decision
                                state.Config.BuildUp
                                (Some cFrame)
                                visibilityMask
                                influence

                        let individual = individualPipeline actx
                        let collective = frame.CollectiveIntents[i]
                        let w = compliance player actx.MyCondition actx.Urgency
                        let merged = CollectiveIntent.merge individual collective w

                        let rawIntent = MovementScorer.pickIntent subTick merged actx

                        let mutable finalIntent =
                            match rawIntent with
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
                                    { X = myX
                                      Y = myY
                                      Z = 0.0<meter>
                                      Vx = 0.0<meter / second>
                                      Vy = 0.0<meter / second>
                                      Vz = 0.0<meter / second> }

                            finalIntent <- MoveToSetPiecePos pos

                        let activeRun =
                            getActiveRuns state clubSide
                            |> List.tryFind (fun r -> r.PlayerId = player.Id && RunAssignment.isActive subTick r)

                        let finalFinalIntent =
                            match activeRun with
                            | Some run -> ExecuteRun run
                            | None -> finalIntent

                        let kind, tx, ty, pid = IntentFrame.fromMovementIntent finalFinalIntent
                        outputs.Add(side (SetIntent(i, kind, tx, ty, pid)))

                        let dur, trigger = IntentPhase.duration clock finalFinalIntent
                        outputs.Add(side (CommitIntent(i, subTick + dur, byte trigger)))

                        match finalFinalIntent with
                        | ExecuteRun run -> outputs.Add(RegisterRun(clubSide, run))
                        | _ -> ()

        outputs.ToArray()
