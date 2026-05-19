namespace FootballEngine

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module CollectiveModifiers =

    let private getFlankOfPlayer (ctx: AgentContext) : FlankZone =
        PitchZoneOps.toFlank ctx.MyPos.Y

    let private applyTransition
        (bb: TeamBlackboard)
        (ctx: AgentContext)
        (_defRole: DefensiveRole)
        (scores: MovementScores)
        : MovementScores =
        if bb.OurPhase = Transition && bb.JustLostBall then
            let distToBall = ctx.MyPos.DistTo2D ctx.BallState.Position
            let multiplier = if distToBall < 15.0<meter> then 2.5 else 0.7
            { scores with PressBall = scores.PressBall * multiplier }
        else
            scores

    let private applyWeakness
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (myFlank: FlankZone)
        (scores: MovementScores)
        : MovementScores =
        let flankMatch =
            bb.WeaknessZones
            |> Array.exists (fun z -> z = myFlank)

        if flankMatch then
            { scores with SupportAttack = scores.SupportAttack * 1.4 }
        else
            scores

    let private applyRestDefense
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (slotRole: SlotRole)
        (scores: MovementScores)
        : MovementScores =
        if bb.OurPhase = Attacking && slotRole = AnchorDefense then
            { scores with
                SupportAttack = scores.SupportAttack * 0.3
                CoverSpace = scores.CoverSpace * 1.5 }
        else
            scores

    let private applyThreats
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (myFlank: FlankZone)
        (scores: MovementScores)
        : MovementScores =
        let threatMatch =
            bb.ThreatZones
            |> Array.exists (fun z -> z = myFlank)

        if threatMatch then
            { scores with CoverSpace = scores.CoverSpace * 1.5 }
        else
            scores

    let private applyOpponentShape
        (bb: TeamBlackboard)
        (ctx: AgentContext)
        (scores: MovementScores)
        : MovementScores =
        match bb.OpponentShape with
        | HighLine when ctx.TeamHasBall ->
            { scores with SupportAttack = scores.SupportAttack * 1.3 }
        | LowBlock when not ctx.TeamHasBall ->
            { scores with
                PressBall = scores.PressBall * 0.7
                CoverSpace = scores.CoverSpace * 1.2 }
        | _ -> scores

    let private applyUrgency
        (bb: TeamBlackboard)
        (_ctx: AgentContext)
        (scores: MovementScores)
        : MovementScores =
        if bb.Urgency > 0.7 then
            { scores with
                PressBall = scores.PressBall * (1.0 + bb.Urgency * 0.5)
                SupportAttack = scores.SupportAttack * (1.0 + bb.Urgency * 0.3) }
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
