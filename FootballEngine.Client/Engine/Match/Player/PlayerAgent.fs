namespace FootballEngine

open FootballEngine.Referee
open FootballEngine.Types

module PlayerAgent =

    let agent (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : ActionResult * RefereeAction list =
        ActionResolver.run (int state.SubTick) ctx state clock
