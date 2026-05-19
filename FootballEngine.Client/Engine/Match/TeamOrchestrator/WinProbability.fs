namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Types
open SimulationClock

type MatchSituation =
    | Winning of goalDiff: int * minutesLeft: float
    | Drawing of minutesLeft: float
    | Losing of goalDiff: int * minutesLeft: float

module WinProbability =

    let calculate (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : float =
        let scoreDiff = state.HomeScore - state.AwayScore
        let minutesLeft = subTicksToSeconds clock state.SubTick / 60.0

        // Base según marcador y minuto
        let baseProb =
            if scoreDiff > 0 then
                0.55 + min 0.3 (float scoreDiff * 0.1)
            elif scoreDiff < 0 then
                0.45 - min 0.3 (float -scoreDiff * 0.1)
            else
                0.50

        // Ajustes desde BalanceConfig (TODO: leer pesos de config)
        let xGFactor = (state.HomeXG - state.AwayXG) * 0.15

        let momentumFactor =
            if state.Momentum > 2.0 then 0.08
            elif state.Momentum < -2.0 then -0.08
            else state.Momentum * 0.04

        let prob = baseProb + xGFactor + momentumFactor
        max 0.05 (min 0.95 prob)
