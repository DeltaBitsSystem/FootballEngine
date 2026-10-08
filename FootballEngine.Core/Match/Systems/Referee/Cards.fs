namespace FootballEngine.Core.Match

/// A card changes nothing by itself: this system makes it matter — two yellows
/// become a red when the rule is enabled, and totals are tracked so discipline
/// has real consequences.
module RefereeCards =

    let private decideDismissal
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (tick: int<subtick>)
        (ev: MatchEvent)
        : unit =
        match ev.Data with
        | EventData.Card(pid, color) ->
            match MatchWorld.teamOfPlayer ctx.Ctx pid with
            | None -> ()
            | Some side ->
                match MatchWorld.slotOf ctx.Ctx side pid with
                | None -> ()
                | Some slot ->
                    let team = MatchWorld.team world side

                    match color with
                    | CardColor.Yellow ->
                        let secondYellow = team.Memory.[slot].Yellows >= 2

                        let alreadyQueued =
                            buffer.Events
                            |> Seq.exists (fun queued -> queued.Data = EventData.RedCardIssued pid)

                        if
                            secondYellow
                            && ctx.Ctx.Config.Referee.SecondYellowSuspension
                            && not alreadyQueued
                        then
                            buffer.Events.Add
                                { Tick = tick
                                  Data = EventData.RedCardIssued(pid) }
                    | CardColor.Red ->
                        let alreadyQueued =
                            buffer.Events
                            |> Seq.exists (fun queued -> queued.Data = EventData.RedCardIssued pid)

                        if not alreadyQueued then
                            buffer.Events.Add
                                { Tick = tick
                                  Data = EventData.RedCardIssued(pid) }
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick

            for ev in buffer.Applied do
                if MatchEvent.kind ev = EventKind.Card then
                    let firstEvent = buffer.Events.Count
                    decideDismissal ctx world buffer tick ev

                    if world.Observation.Enabled then
                        for index = firstEvent to buffer.Events.Count - 1 do
                            world.Observation.Associate(buffer.Events[index], EventCause(MatchEvent.id ev))

            rng
