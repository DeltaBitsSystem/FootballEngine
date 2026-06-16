namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps

module MatchSituationBuilder =

    let private clampFloat (lo: float) (hi: float) (v: float) =
        if v < lo then lo
        elif v > hi then hi
        else v

    let private minuteNorm (state: SimState) (clock: SimulationClock) : float =
        let totalSubTicks = 90 * 40 * 60
        min 1.0 (float state.SubTick / float totalSubTicks)

    let private goalDiffFor (side: ClubSide) (state: SimState) : int =
        if side = HomeClub then
            state.HomeScore - state.AwayScore
        else
            state.AwayScore - state.HomeScore

    let private possessionPressure (ctx: MatchContext) (state: SimState) (side: ClubSide) : float =
        let events = state.MatchEvents
        let n = events.Count

        if n = 0 then
            0.0
        else
            let cutoff = events[n - 1].SubTick - 1200
            let ourClubId = if side = HomeClub then ctx.Home.Id else ctx.Away.Id
            let mutable total = 0
            let mutable against = 0

            for i = n - 1 downto 0 do
                if events[i].SubTick < cutoff then
                    ()
                else
                    total <- total + 1

                    if events[i].ClubId <> ourClubId then
                        against <- against + 1

            if total = 0 then 0.0 else float against / float total

    let compute (side: ClubSide) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : MatchSituation =
        let ms = state.Config.MatchSituation
        let minute = minuteNorm state clock
        let minuteReal = minute * 90.0
        let gd = goalDiffFor side state
        let isDrawLate = gd = 0 && minuteReal > 70.0
        let isLosingLate = gd < 0 && minuteReal > 60.0
        let momNorm = (state.Momentum + 10.0) / 20.0
        let posPress = possessionPressure ctx state side
        let teamState = getTeam state side
        let bb = teamState.Blackboard
        let inTransition = bb.OurPhase = Transition && bb.JustLostBall

        let teamPressingHigh =
            teamState.Tactics = TeamTactics.Pressing
            || teamState.EmergentState.PressingIntensity > 0.7

        let shootMod =
            let urgency =
                if isLosingLate then ms.ShootLosingLateMod
                elif isDrawLate then ms.ShootDrawLateMod
                else 1.0

            let momentum = 1.0 + (momNorm - 0.5) * ms.ShootMomentumScale
            clampFloat 0.7 1.5 (urgency * momentum)

        let passMod =
            clampFloat
                0.7
                1.5
                (if gd > 0 then ms.PassWinningMod
                 elif inTransition then ms.PassTransitionPenalty
                 else 1.0)

        let dribbleMod =
            clampFloat 0.7 1.3 (if inTransition then ms.DribbleTransitionMod else 1.0)

        let longBallMod =
            clampFloat
                0.7
                1.4
                (if isLosingLate then
                     ms.LongBallLosingLateMod
                 elif posPress > ms.LongBallPressureThreshold then
                     ms.LongBallPressureMod
                 else
                     1.0)

        { MinuteNorm = minute
          GoalDiff = gd
          IsDrawLate = isDrawLate
          IsLosingLate = isLosingLate
          MomentumNorm = momNorm
          PossessionPressure = posPress
          TeamInTransition = inTransition
          TeamPressingHigh = teamPressingHigh
          ShootMod = shootMod
          PassMod = passMod
          DribbleMod = dribbleMod
          LongBallMod = longBallMod }
