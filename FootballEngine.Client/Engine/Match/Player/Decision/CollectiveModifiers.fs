namespace FootballEngine

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.ML

module CollectiveModifiers =

    let private applyCoordination (mem: CoordinationMemory) (scores: MovementScores) : MovementScores =
        let coord = clampFloat 0.90 1.20 mem.PressingCoordination
        { scores with
            PressBall = scores.PressBall * coord
            CoverSpace = scores.CoverSpace * coord }

    let private getFlankOfPlayer (ctx: AgentContext) : FlankZone =
        PitchZoneOps.toFlank ctx.MyPos.Y

    let private applyTransition
        (bb: TeamBlackboard)
        (ctx: AgentContext)
        (_defRole: DefensiveRole)
        (scores: MovementScores)
        : MovementScores =
        let w = EngineWeightDefaults.defaults.Collective.Modifiers
        if bb.OurPhase = Transition && bb.JustLostBall then
            let distToBall = ctx.MyPos.DistTo2D ctx.BallState.Position
            let multiplier =
                if distToBall < w.TransitionNearDistance * 1.0<meter> then
                    w.TransitionNearMult
                else
                    w.TransitionFarMult
            { scores with PressBall = scores.PressBall * multiplier }
        else
            scores

    let private applyWeakness
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (myFlank: FlankZone)
        (scores: MovementScores)
        : MovementScores =
        let w = EngineWeightDefaults.defaults.Collective.Modifiers
        let flankMatch =
            bb.WeaknessZones
            |> Array.exists (fun z -> z = myFlank)

        if flankMatch then
            { scores with SupportAttack = scores.SupportAttack * w.WeaknessSupportMult }
        else
            scores

    let private applyRestDefense
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (slotRole: SlotRole)
        (scores: MovementScores)
        : MovementScores =
        let w = EngineWeightDefaults.defaults.Collective.Modifiers
        if bb.OurPhase = Attacking && slotRole = AnchorDefense then
            { scores with
                SupportAttack = scores.SupportAttack * w.RestDefenseSupportMult
                CoverSpace = scores.CoverSpace * w.RestDefenseCoverMult }
        else
            scores

    let private applyThreats
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (myFlank: FlankZone)
        (scores: MovementScores)
        : MovementScores =
        let w = EngineWeightDefaults.defaults.Collective.Modifiers
        let threatMatch =
            bb.ThreatZones
            |> Array.exists (fun z -> z = myFlank)

        if threatMatch then
            { scores with CoverSpace = scores.CoverSpace * w.ThreatCoverMult }
        else
            scores

    let private applyOpponentShape
        (bb: TeamBlackboard)
        (ctx: AgentContext)
        (scores: MovementScores)
        : MovementScores =
        let w = EngineWeightDefaults.defaults.Collective.Modifiers
        match bb.OpponentShape with
        | HighLine when ctx.TeamHasBall ->
            { scores with SupportAttack = scores.SupportAttack * w.HighLineSupportMult }
        | LowBlock when not ctx.TeamHasBall ->
            { scores with
                PressBall = scores.PressBall * w.LowBlockPressMult
                CoverSpace = scores.CoverSpace * w.LowBlockCoverMult }
        | _ -> scores

    let private applyUrgency
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (scores: MovementScores)
        : MovementScores =
        let w = EngineWeightDefaults.defaults.Collective.Modifiers
        if bb.Urgency > w.UrgencyThreshold then
            { scores with
                PressBall = scores.PressBall * (1.0 + bb.Urgency * w.UrgencyPressMult)
                SupportAttack = scores.SupportAttack * (1.0 + bb.Urgency * w.UrgencySupportMult) }
        else
            scores

    let applyModifiers
        (blackboard: TeamBlackboard)
        (ctx: AgentContext)
        (scores: MovementScores)
        : MovementScores =

        let frame = ctx.Team.OwnFrame

        let mySlotRole =
            if ctx.MeIdx < frame.SlotCount then
                box frame.SlotRoles[ctx.MeIdx] :?> SlotRole
            else FreeRole

        let myFlank = getFlankOfPlayer ctx

        scores
        |> applyTransition blackboard ctx DefensiveRole.Marker
        |> applyWeakness blackboard ctx myFlank
        |> applyRestDefense blackboard ctx mySlotRole
        |> applyThreats blackboard ctx myFlank
        |> applyOpponentShape blackboard ctx
        |> applyUrgency blackboard ctx
        |> applyCoordination blackboard.CoordinationMemory
