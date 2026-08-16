namespace FootballEngine.Core.Match

/// A card changes nothing by itself: this system makes it matter — two yellows
/// become a red when the rule is enabled, and totals are tracked so discipline
/// has real consequences.
module RefereeCards =

    let private applyCard
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
                    let mem = team.Memory

                    match color with
                    | CardColor.Yellow ->
                        mem.[slot].Yellows <- mem.[slot].Yellows + 1
                        team.Cards.Yellows <- team.Cards.Yellows + 1

                        if mem.[slot].Yellows >= 2 && ctx.Ctx.Config.Referee.SecondYellowSuspension then
                            buffer.Events.Add
                                { Tick = tick
                                  Data = EventData.RedCardIssued(pid) }
                    | CardColor.Red ->
                        team.Cards.Reds <- team.Cards.Reds + 1

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.RedCardIssued(pid) }
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick

            for ev in buffer.Applied do
                if MatchEvent.kind ev = EventKind.Card then
                    applyCard ctx world buffer tick ev

            rng
