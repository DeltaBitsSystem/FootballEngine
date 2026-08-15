namespace FootballEngine

open FootballEngine.Referee
open FootballEngine.Types


module ActionSystem =

    let run
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (outputs: ResizeArray<DomainEvent>)
        : unit =

        let actionResult, _refActions = ActionResolver.run subTick ctx state clock

        outputs.AddRange(actionResult.Events)
