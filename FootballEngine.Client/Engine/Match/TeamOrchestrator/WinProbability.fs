namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Types
open FootballEngine.ML
open SimulationClock

type MatchSituation =
    | Winning of goalDiff: int * minutesLeft: float
    | Drawing of minutesLeft: float
    | Losing of goalDiff: int * minutesLeft: float

module WinProbability =

    let calculate (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : float =
        let w = EngineWeightDefaults.defaults.WinProbability
        let scoreDiff = state.HomeScore - state.AwayScore
        let minutesLeft = subTicksToSeconds clock state.SubTick / 60.0

        let baseProb =
            if scoreDiff > 0 then
                w.GoalLeadBase + min w.GoalDiffSteepness (float scoreDiff * w.GoalDiffFactor)
            elif scoreDiff < 0 then
                w.DrawBase - min w.GoalDiffSteepness (float -scoreDiff * w.GoalDiffFactor)
            else
                w.DrawBase

        let xGFactor = (state.HomeXG - state.AwayXG) * w.XGFactor

        let momentumFactor =
            if state.Momentum > w.MomentumPositiveThreshold then w.MomentumPositiveBonus
            elif state.Momentum < w.MomentumNegativeThreshold then w.MomentumNegativePenalty
            else state.Momentum * w.MomentumLinearFactor

        let prob = baseProb + xGFactor + momentumFactor
        max 0.05 (min 0.95 prob)
