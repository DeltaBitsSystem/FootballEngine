namespace FootballEngine

open System
open System.Collections.Generic
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player.Actions
open FootballEngine.Player.Decision
open FootballEngine.Player.Intent
open FootballEngine.Player.Perception
open FootballEngine.Player
open FootballEngine.Types
open FootballEngine.Types.IntentPhaseTypes
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

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) (time: MatchTime) : DomainEvent[] =

        let domainEvents = ResizeArray<DomainEvent>(4)

        let possessionHistory = state.PossessionHistory

        for clubSide in [| HomeClub; AwayClub |] do
            let team = buildTeamPerspective clubSide ctx state
            let frame = team.OwnFrame
            let roster = team.OwnRoster
            let cFrame =
                if clubSide = HomeClub then
                    state.HomeCognitiveFrame
                else
                    state.AwayCognitiveFrame
            let influence =
                if clubSide = HomeClub then
                    state.HomeInfluenceFrame
                else
                    state.AwayInfluenceFrame
            let dir = team.AttackDir
            let ballXSmooth = state.BallXSmooth
            let phase = phaseFromBallZone dir ballXSmooth

            let basePositions = getBasePositions state clubSide

            let tacticsCfg =
                tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

            let isSetPiece =
                match state.Flow with
                | RestartDelay _ -> true
                | _ -> false

            let setPiecePositions =
                match (getTeam state clubSide).SetPiecePositions with
                | Some positions -> positions
                | None -> Array.empty

            let teamState = SimStateOps.getTeam state clubSide
            let shapeTargetX = teamState.ShapeTargetX
            let shapeTargetY = teamState.ShapeTargetY

            let activeRunByPlayer = teamState.ActiveRunLookup
            activeRunByPlayer.Clear()
            for run in getActiveRuns state clubSide do
                if RunAssignment.isActive time.Subtick run && not (activeRunByPlayer.ContainsKey run.PlayerId) then
                    activeRunByPlayer.Add(run.PlayerId, run)

            let ownCount = frame.SlotCount
            let oppCount = team.OppFrame.SlotCount
            let visCanSeeTM =
                if teamState.VisCanSeeTM.Length >= ownCount then
                    teamState.VisCanSeeTM
                else
                    teamState.VisCanSeeTM <- Array.zeroCreate ownCount
                    teamState.VisCanSeeTM
            let visCanSeeOpp =
                if teamState.VisCanSeeOpp.Length >= oppCount then
                    teamState.VisCanSeeOpp
                else
                    teamState.VisCanSeeOpp <- Array.zeroCreate oppCount
                    teamState.VisCanSeeOpp

            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Sidelined _ -> ()
                | OccupancyKind.Active rosterIdx ->
                    if not (IntentPhase.shouldRecalculate frame.Intent i time.Subtick possessionHistory) then
                        ()
                    else
                        let player = roster.Players[rosterIdx]
                        let profile = roster.Profiles[rosterIdx]

                        let myX = float frame.Physics.PosX[i] * 1.0<meter>
                        let myY = float frame.Physics.PosY[i] * 1.0<meter>
                        let myVx = float frame.Physics.VelX[i] * 1.0<meter / second>
                        let myVy = float frame.Physics.VelY[i] * 1.0<meter / second>

                        let hasBall =
                            match state.Ball.Control with
                            | Controlled(_, pid) when pid = player.Id -> true
                            | Receiving(_, pid, _) when pid = player.Id -> true
                            | _ -> false

                        if hasBall && not isSetPiece then
                            Perception.computeVisibilityMaskInto
                                visCanSeeTM visCanSeeOpp
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
                            |> ignore

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
                                ValueNone
                                influence

                        let individual = individualPipeline actx
                        let blackboard = (getTeam state clubSide).Blackboard
                        let contextual = CollectiveModifiers.applyModifiers blackboard actx individual

                        let collectiveIntent = frame.CollectiveIntents[i]
                        let withCollective =
                            { contextual with
                                PressBall = contextual.PressBall * collectiveIntent.PressBall
                                SupportAttack = contextual.SupportAttack * collectiveIntent.SupportAttack
                                CoverSpace = contextual.CoverSpace * collectiveIntent.CoverSpace
                                MarkMan = contextual.MarkMan * collectiveIntent.MarkMan
                                RecoverBall = contextual.RecoverBall * collectiveIntent.RecoverBall }

                        let slotRole = box frame.SlotRoles[i] :?> SlotRole
                        let withSlotRole =
                            { withCollective with
                                PressBall = withCollective.PressBall * SlotRole.pressBallBias slotRole
                                SupportAttack = withCollective.SupportAttack * SlotRole.supportAttackBias slotRole
                                CoverSpace = withCollective.CoverSpace * SlotRole.coverSpaceBias slotRole
                                RecoverBall = withCollective.RecoverBall * SlotRole.recoverBallBias slotRole }

                        let defRole = LanguagePrimitives.EnumOfValue<byte, DefensiveRole> frame.DefensiveRole[i]
                        let withDefRole = MovementScorer.applyRoleModifiersForRole defRole actx.TeamHasBall withSlotRole

                        let emergent = getEmergentState state clubSide
                        let withEmergent = MovementScorer.applyEmergentModifiers emergent actx withDefRole

                        let rawIntent = MovementScorer.pickIntent (int time.Subtick) withEmergent actx

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

                        let finalFinalIntent =
                            match activeRunByPlayer.TryGetValue player.Id with
                            | true, run -> ExecuteRun run
                            | false, _ -> finalIntent

                        let kind, tx, ty, pid = IntentFrame.fromMovementIntent finalFinalIntent
                        FrameMutate.setIntent frame.Intent i kind tx ty pid

                        let dur, trigger = IntentPhase.duration clock finalFinalIntent
                        FrameMutate.commitIntent frame.Intent i (time.Subtick + SimulationClock.deltaToSubtick dur) (LanguagePrimitives.EnumOfValue<byte, ExitTrigger> (byte trigger))
                        frame.Intent.CommittedAt[i] <- int time.Subtick

                        match finalFinalIntent with
                        | ExecuteRun run -> domainEvents.Add(RegisterRun(clubSide, run))
                        | _ -> ()

        domainEvents.ToArray()
