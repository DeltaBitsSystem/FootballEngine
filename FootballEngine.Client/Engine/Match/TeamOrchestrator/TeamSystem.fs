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

    let run (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : SystemOutput[] =
        let outputs = ResizeArray<SystemOutput>()
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
            let phase = phaseFromBallZone (attackDirFor clubSide state) state.Ball.Position.X
            let team1 = buildTeamPerspective clubSide ctx state
            let supportArr = BatchDecisionSupport.computeSupportPositions team1 state.Ball.Position state.Ball.Control phase tacticsCfg tacticsCfg.Width basePositions
            let supportPx = Array.map (fun (s: Spatial) -> float32 s.X) supportArr
            let supportPy = Array.map (fun (s: Spatial) -> float32 s.Y) supportArr
            let cFrame = if clubSide = HomeClub then state.HomeCognitiveFrame else state.AwayCognitiveFrame
            let team2 = buildTeamPerspective clubSide ctx state
            let defRoles = BatchDecisionSupport.computeDefensiveShape team2 state.Ball.Position cFrame subTick (getTeam state clubSide).TransitionPressExpiry
            let side = if clubSide = HomeClub then HomeFrame else AwayFrame
            for i = 0 to frame.SlotCount - 1 do
                outputs.Add(side (SetSlotRole(i, slotRoles[i])))
                outputs.Add(side (SetSupportPos(i, supportPx[i], supportPy[i])))
                outputs.Add(side (SetDefensiveRole(i, defRoles[i])))
                outputs.Add(side (SetCollectiveIntent(i, buildCollectiveIntent kind emergent slotRoles[i] defRoles[i])))
            let currentDir =
                match TeamDirectiveOps.currentDirective directive with
                | Some d -> d
                | None -> TeamDirectiveOps.empty subTick
            let newDirective = { currentDir with Kind = kind; ActiveSince = if kind <> currentDir.Kind then subTick else currentDir.ActiveSince }
            outputs.Add(DirectiveUpdate(clubSide, TeamDirectiveState.Active newDirective))
        outputs.ToArray()
