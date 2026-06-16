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

        let actionResult, refActions = ActionResolver.run subTick ctx state clock

        outputs.AddRange(actionResult.Events)

        for action in refActions do
            let refEvents = RefereeApplicator.apply subTick action ctx state
            outputs.AddRange(refEvents)
