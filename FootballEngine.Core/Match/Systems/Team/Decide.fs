namespace FootballEngine.Core.Match

/// The team commits to one directive (press, park the bus, ...) and holds it
/// through hysteresis — reacting to every tick would make the shape and the
/// pressing thrash instead of playing one coherent plan.
module TeamDecide =

    let pressGate (tactics: TacticsConfig) =
        17.5 - 70.0 * float tactics.PressTriggerZone

    let private decideTeam (ctx: MatchContext) (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        let tactics = team.Tactics

        let minute = int (PlayClock.toMatchMinute (MatchClock.playClock world.State.Clock))
        let diff = world.State.HomeScore - world.State.AwayScore
        let myDiff = if side = ClubSide.Home then diff else -diff

        // R9 — win-probability model drives the late-game directive, so the
        // team doesn't just read the scoreboard.
        let winProb = WinProbability.estimate ctx world side
        let riskLate = minute >= int ctx.Config.Team.RiskLateMinute

        // Tempo sets how eagerly transitions become counters: fast teams break
        // on half-chances, slow teams wait for control.
        let counterThreshold =
            0.5f
            - float32 ctx.Config.TacticalTradeoffs.TempoCounterUrgencyShift * tactics.Tempo

        let ballFx =
            float world.State.Ball.Position.X * float (MatchMath.goalDirection side)

        // The trigger zone draws the engagement line in the attacking frame:
        // low values only press in the attacking third, high values press anywhere.
        let pressGate = pressGate tactics

        let candidate: DirectiveKind =
            if riskLate && winProb < 0.35 && minute >= 60 then
                if myDiff < -1 then
                    DirectiveKind.Attack
                else
                    DirectiveKind.Counter
            elif riskLate && winProb > 0.7 && minute >= 75 then
                if myDiff >= 2 then
                    DirectiveKind.ParkTheBus
                else
                    DirectiveKind.Defend
            elif myDiff <= -2 && minute >= 60 then
                DirectiveKind.Attack
            elif myDiff <= -1 && minute >= 75 then
                DirectiveKind.Attack
            elif myDiff >= 3 && minute >= 75 then
                DirectiveKind.ParkTheBus
            elif myDiff >= 1 && minute >= 80 then
                DirectiveKind.Defend
            elif myDiff >= 2 && minute >= 70 then
                DirectiveKind.Defend
            elif
                team.Emergent.PressingIntensity > 0.6f
                && team.Blackboard.OurPhase = PhaseKind.Defending
            then
                DirectiveKind.Press
            elif
                team.Blackboard.OurPhase = PhaseKind.TransitionAttack
                && team.Blackboard.Urgency > counterThreshold
            then
                DirectiveKind.Counter
            elif tactics.PressIntensity > 0.7f && ballFx > pressGate then
                DirectiveKind.Press
            else
                DirectiveKind.Balanced

        let hysteresis =
            float ctx.Config.Team.DirectiveHysteresis * float SimulationClock.minuteSubticks

        if
            candidate <> team.Directive.Kind
            && tick - team.Directive.SinceTick > int (hysteresis * 1.0<subtick>) * 1<subtick>
        then
            team.Directive.Kind <- candidate
            team.Directive.SinceTick <- tick

        let pressBase = float tactics.PressIntensity
        let lineBase = float tactics.DefensiveLine

        let lineHeight, press, width =
            match team.Directive.Kind with
            | DirectiveKind.Attack -> lineBase + 10.0, pressBase + 0.2, min 1.0 (float tactics.Width + 0.15)
            | DirectiveKind.Defend ->
                max 20.0 (lineBase - 6.0), min 1.0 (pressBase + 0.1), max 0.4 (float tactics.Width - 0.1)
            | DirectiveKind.ParkTheBus -> 15.0, 0.6, 0.35
            | DirectiveKind.Press -> lineBase + 6.0, min 1.0 (pressBase + 0.35), float tactics.Width
            | DirectiveKind.Counter -> lineBase + 4.0, min 1.0 (pressBase + 0.15), float tactics.Width + 0.1
            | DirectiveKind.Balanced -> lineBase, pressBase, float tactics.Width

        team.Directive.Params.LineHeight <- float32 lineHeight
        team.Directive.Params.PressIntensity <- float32 press
        team.Directive.Params.Width <- float32 width

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick
            decideTeam ctx.Ctx world ClubSide.Home tick
            decideTeam ctx.Ctx world ClubSide.Away tick
            rng
