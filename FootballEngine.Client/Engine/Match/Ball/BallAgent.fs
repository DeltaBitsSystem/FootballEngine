namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps

module BallAgent =

    let agent_DEPRECATED (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : DomainEvent[] =
        [||]
