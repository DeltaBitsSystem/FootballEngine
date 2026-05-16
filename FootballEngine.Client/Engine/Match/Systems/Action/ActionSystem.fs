namespace FootballEngine

open FootballEngine.Referee
open FootballEngine.Types


module ActionSystem =

    let run
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : DomainEvent[] =

        let actionResult, refActions = ActionResolver.run subTick ctx state clock
        let events = ResizeArray<DomainEvent>(16)

        events.AddRange(actionResult.Events)

        for action in refActions do
            let refEvents = RefereeApplicator.apply subTick action ctx state
            events.AddRange(refEvents)

        events.ToArray()
