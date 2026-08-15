namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Types
open SimulationClock

type OrchestrationMode =
    | ExecutingPlan of winProb: float
    | Recovering of PlanDeviation * recoveryTarget: DirectiveKind

module StrategicLoop =

    let private recoveryTarget (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : DirectiveKind =
        let scoreDiff = state.HomeScore - state.AwayScore
        let minutesLeft = 90.0 - (subTicksToSeconds clock state.SubTick / 60.0)

        match scoreDiff with
        | d when d < 0 && minutesLeft < 20.0 -> DirectiveKind.DirectAttack // perdiendo, poco tiempo
        | d when d < 0 -> DirectiveKind.PressingBlock // perdiendo, tiempo disponible
        | d when d > 0 && minutesLeft < 15.0 -> DirectiveKind.DefensiveBlock // ganando, proteger
        | _ -> DirectiveKind.Structured // empte o sin urgencia

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : OrchestrationMode =
        let winProb = WinProbability.calculate ctx state clock

        if winProb > 0.55 then
            ExecutingPlan winProb
        else
            let deviation = ReactiveLoop.run ctx state clock
            let target = recoveryTarget ctx state clock
            Recovering(deviation, target)
