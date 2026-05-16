namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.TeamOrchestrator
open FootballEngine.Types
open FootballEngine.Types.TacticsConfig
open SimStateOps
open SlotRoleAssigner


module TeamSystem =

    let private buildCollectiveIntent
        (kind: DirectiveKind)
        (emergent: EmergentState)
        (role: SlotRole)
        (defRole: DefensiveRole)
        : CollectiveIntent =
        let baseRecord =
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
        { MaintainShape = baseRecord.MaintainShape
          MarkMan = baseRecord.MarkMan * emergent.CompactnessLevel
          PressBall = baseRecord.PressBall * emergent.PressingIntensity * SlotRole.pressBallBias role
          CoverSpace = baseRecord.CoverSpace * emergent.CompactnessLevel * SlotRole.coverSpaceBias role
          SupportAttack = baseRecord.SupportAttack * emergent.RiskAppetite * SlotRole.supportAttackBias role
          RecoverBall = baseRecord.RecoverBall * SlotRole.recoverBallBias role }

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) (time: MatchTime) : DomainEvent[] =
        let events = ResizeArray<DomainEvent>(4)
        for clubSide in [| HomeClub; AwayClub |] do
            let frame   = getFrame state clubSide
            let roster  = getRoster ctx clubSide
            let emergent = getEmergentState state clubSide
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
            let team1 = buildTeamPerspective clubSide ctx state
            BatchDecisionSupport.computeSupportPositionsInto
                team1
                state.Ball.Position
                state.Ball.Control
                phase
                tacticsCfg
                tacticsCfg.Width
                basePositions
                frame.SupportPositionX
                frame.SupportPositionY

            let teamState = getTeam state clubSide
            if teamState.ShapeTargetX.Length <> frame.SlotCount then
                teamState.ShapeTargetX <- Array.zeroCreate frame.SlotCount
                teamState.ShapeTargetY <- Array.zeroCreate frame.SlotCount
            else
                System.Array.Clear(teamState.ShapeTargetX, 0, frame.SlotCount)
                System.Array.Clear(teamState.ShapeTargetY, 0, frame.SlotCount)
            let shapeTargetX = teamState.ShapeTargetX
            let shapeTargetY = teamState.ShapeTargetY
            ShapeEngine.computeShapeTargets basePositions dir phase state.Ball.Position.X tacticsCfg shapeTargetX shapeTargetY

            let cFrame = if clubSide = HomeClub then state.HomeCognitiveFrame else state.AwayCognitiveFrame
            let team2 = buildTeamPerspective clubSide ctx state
            let defRoles = BatchDecisionSupport.computeDefensiveShape team2 state.Ball.Position cFrame (int time.Subtick) (int (getTeam state clubSide).TransitionPressExpiry)
            for i = 0 to frame.SlotCount - 1 do
                frame.SlotRoles[i] <- slotRoles[i]
                frame.DefensiveRole[i] <- byte defRoles[i]
                frame.CollectiveIntents[i] <- buildCollectiveIntent kind emergent slotRoles[i] defRoles[i]
            let currentDir =
                match TeamDirectiveOps.currentDirective directive with
                | Some d -> d
                | None -> TeamDirectiveOps.empty time.Subtick
            let newDirective = { currentDir with Kind = kind; ActiveSince = if kind <> currentDir.Kind then time.Subtick else currentDir.ActiveSince }
            events.Add(DirectiveUpdate(clubSide, TeamDirectiveState.Active newDirective))
        events.ToArray()
