namespace FootballEngine

open FootballEngine.Referee
open FootballEngine.Types


module ActionSystem =

    let run
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : SystemOutput[] =

        let actionResult, refActions = ActionResolver.run subTick ctx state clock
        let results = ResizeArray<SystemOutput>(16)

        actionResult.Events |> List.iter (fun e -> results.Add(Emit e))
        results.AddRange(actionResult.Outputs)

        refActions
        |> List.iter (fun a ->
            let events, outputs = RefereeApplicator.apply subTick a ctx state
            events |> List.iter (fun e -> results.Add(Emit e))
            outputs |> List.iter results.Add)

        results.ToArray()
