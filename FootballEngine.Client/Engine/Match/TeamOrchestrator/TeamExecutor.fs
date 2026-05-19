namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.TeamOrchestrator
open FootballEngine.Types
open FootballEngine.Types.TacticsConfig
open SimStateOps
open SlotRoleAssigner


module TeamExecutor =

    let private buildCollectiveIntent (kind: DirectiveKind) : CollectiveIntent =
        match kind with
        | PressingBlock ->
            { MaintainShape = 0.3
              MarkMan = 0.7
              PressBall = 1.0
              CoverSpace = 0.5
              SupportAttack = 0.2
              RecoverBall = 0.4 }
        | DefensiveBlock ->
            { MaintainShape = 0.8
              MarkMan = 0.8
              PressBall = 0.2
              CoverSpace = 0.9
              SupportAttack = 0.1
              RecoverBall = 0.3 }
        | DirectAttack ->
            { MaintainShape = 0.4
              MarkMan = 0.3
              PressBall = 0.3
              CoverSpace = 0.3
              SupportAttack = 1.0
              RecoverBall = 0.2 }
        | CounterReady ->
            { MaintainShape = 0.8
              MarkMan = 0.5
              PressBall = 0.2
              CoverSpace = 0.7
              SupportAttack = 0.6
              RecoverBall = 0.3 }
        | ContestBall ->
            { MaintainShape = 0.3
              MarkMan = 0.5
              PressBall = 0.7
              CoverSpace = 0.5
              SupportAttack = 0.3
              RecoverBall = 0.8 }
        | Structured ->
            { MaintainShape = 0.6
              MarkMan = 0.5
              PressBall = 0.4
              CoverSpace = 0.5
              SupportAttack = 0.5
              RecoverBall = 0.3 }

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) (time: MatchTime) : DomainEvent[] =
        for clubSide in [| HomeClub; AwayClub |] do
            let teamState = getTeam state clubSide

            let frame   = getFrame state clubSide
            let roster  = getRoster ctx clubSide
            let directive = getDirective state clubSide
            let kind =
                match TeamDirectiveOps.currentDirective directive with
                | Some d -> d.Kind
                | None   -> Structured
            let tacticsCfg = tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)
            let bx = float32 state.Ball.Position.X
            let by = float32 state.Ball.Position.Y
            let slotRoles = SlotRoleAssigner.assign frame roster kind tacticsCfg bx by
            let basePositions = getBasePositions state clubSide
            let dir = attackDirFor clubSide state
            let phase = phaseFromBallZone dir state.Ball.Position.X
            let team = buildTeamPerspective clubSide ctx state
            BatchDecisionSupport.computeSupportPositionsInto
                team
                state.Ball.Position
                state.Ball.Control
                phase
                tacticsCfg
                tacticsCfg.Width
                basePositions
                frame.SupportPositionX
                frame.SupportPositionY

            if teamState.ShapeTargetX.Length <> frame.SlotCount then
                teamState.ShapeTargetX <- Array.zeroCreate frame.SlotCount
                teamState.ShapeTargetY <- Array.zeroCreate frame.SlotCount
            else
                System.Array.Clear(teamState.ShapeTargetX, 0, frame.SlotCount)
                System.Array.Clear(teamState.ShapeTargetY, 0, frame.SlotCount)
            let shapeTargetX = teamState.ShapeTargetX
            let shapeTargetY = teamState.ShapeTargetY
            ShapeEngine.computeShapeTargets basePositions roster.Profiles dir phase state.Ball.Position.X tacticsCfg shapeTargetX shapeTargetY

            let cFrame = if clubSide = HomeClub then state.HomeCognitiveFrame else state.AwayCognitiveFrame
            let defRoles = BatchDecisionSupport.computeDefensiveShape team state.Ball.Position cFrame (int time.Subtick) (int teamState.TransitionPressExpiry)
            for i = 0 to frame.SlotCount - 1 do
                frame.SlotRoles[i] <- slotRoles[i]
                frame.DefensiveRole[i] <- byte defRoles[i]
                frame.CollectiveIntents[i] <- buildCollectiveIntent kind
        Array.empty<DomainEvent>
