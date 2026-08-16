namespace FootballEngine.Core.Match

/// The team needs one shared read of the situation before anyone decides, so
/// this system derives the phase, opponent pressure, threat/weakness zones,
/// urgency and momentum streak and writes them to the blackboard — every
/// other system reads that single view instead of re-interpreting events.
module TeamPerceive =

    let private perceiveTeam (ctx: MatchContext) (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let ball = world.State.Ball.Position

        let ours =
            match MatchWorld.ballCarrier ctx world with
            | Some(s, _, _) -> s = side
            | None ->
                match world.State.Possession.LastGainer with
                | Some s -> s = side
                | None -> world.State.LastAttackingClub = Some side

        let opp = MatchWorld.team world (MatchWorld.oppOf side)

        let pressure = MatchWorld.opponentsNear ctx world side ball.X ball.Y 12.0

        let lastTransition = team.Blackboard.TransitionSince

        let situationPhase = Situation.resolve ctx.Config.Situation team.Intercept

        let phase =
            if ours && tick - lastTransition < 240<subtick> then
                PhaseKind.TransitionAttack
            elif not ours && tick - lastTransition < 240<subtick> then
                PhaseKind.TransitionDefend
            else
                match situationPhase with
                | Some p -> p
                | None -> team.Blackboard.OurPhase

        let dir = MatchMath.goalDirection side

        let threatZone =
            if not ours then
                let goalX = float (MatchMath.ownGoalX side)
                MatchMath.clamp01 (0.5 + (goalX - float ball.X) * dir / 60.0)
            else
                float team.Blackboard.ThreatZone

        let weaknessZone =
            if ours then
                let oppGoalX = float (MatchMath.goalX side)
                MatchMath.clamp01 (0.5 + (float ball.X - oppGoalX * 0.3) * dir / 70.0)
            else
                float team.Blackboard.WeaknessZone

        let minute = int (SimulationClock.toMatchMinute tick)
        let diff = world.State.HomeScore - world.State.AwayScore
        let myDiff = if side = ClubSide.Home then diff else -diff

        let urgency =
            let scorePart =
                (if myDiff < 0 then
                     -0.25 * float myDiff
                 else
                     -0.1 * float myDiff)
                |> MatchMath.clamp -0.5 0.5

            let timePart = if minute > 70 then 0.2 else 0.0
            MatchMath.clamp01 (scorePart + timePart)

        let streak =
            let window =
                EventWindow.countInWindow team.EventWindow tick 4000<subtick> EventKind.Goal

            if window > 0 then
                min 3 (window + (if world.State.Momentum > 0.3 then 1 else 0))
            else
                0

        if phase <> team.Blackboard.OurPhase && tick - lastTransition > 60<subtick> then
            team.Blackboard.TransitionSince <- tick

        team.Blackboard.OurPhase <- phase
        team.Blackboard.OpponentPressure <- pressure
        team.Blackboard.OpponentShape <- opp.Shape
        team.Blackboard.ThreatZone <- float32 threatZone
        team.Blackboard.WeaknessZone <- float32 weaknessZone
        team.Blackboard.Urgency <- float32 urgency
        team.Blackboard.MomentumStreak <- streak

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick
            perceiveTeam ctx.Ctx world ClubSide.Home tick
            perceiveTeam ctx.Ctx world ClubSide.Away tick
            rng
