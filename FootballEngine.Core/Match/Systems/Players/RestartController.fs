namespace FootballEngine.Core.Match

/// World half of a restart and the owner of its phase transitions. Awarding and planning arrive as events
/// reduced by EventApply; this controller owns authorization, command issue, contact observation and the
/// single return to live play.
module RestartController =

    let private setRestart world state =
        world.State <-
            { world.State with
                Flow = MatchFlow.Restart state }

    let private authorize
        (ctx: SystemContext)
        (world: World)
        (restart: RestartState)
        (plan: RestartPlan)
        (buffer: EventBuffer)
        =
        let validation = RestartRules.validate ctx.Ctx world plan

        let executorGone =
            validation.Violations
            |> List.exists (fun violation -> violation.Rule = RestartRules.Rule.ExecutorActive)

        // An executor sent off or injured after planning never becomes ready; only a new plan picks a taker on the pitch.
        if executorGone then
            setRestart
                world
                { restart with
                    Plan = None
                    Phase = RestartPhase.Awarded }

            buffer.Events.Add
                { Tick = world.State.Tick
                  Data = EventData.RestartPlanChanged(plan.Id, RestartPlanChangeReason.ExecutorInvalidated) }
        elif validation.Valid then
            setRestart
                world
                { restart with
                    Phase = RestartPhase.Authorized }

            buffer.Events.Add
                { Tick = world.State.Tick
                  Data = EventData.RestartAuthorized plan.Id }

    let private issue
        (ctx: SystemContext)
        (world: World)
        (restart: RestartState)
        (plan: RestartPlan)
        (buffer: EventBuffer)
        =
        Actions.executeRestart ctx world buffer plan

        setRestart
            world
            { restart with
                Phase = RestartPhase.AwaitingContact
                CommandIssued = true }

        buffer.Events.Add
            { Tick = world.State.Tick
              Data = EventData.RestartCommandIssued(plan.Id, plan.Executor) }

    let step: SystemStep =
        fun ctx rng world buffer ->
            match world.State.Flow with
            | MatchFlow.Restart restart ->
                match restart.Phase, restart.Plan with
                | RestartPhase.Positioning, Some plan -> authorize ctx world restart plan buffer
                | RestartPhase.Authorized, Some plan -> issue ctx world restart plan buffer
                | RestartPhase.AwaitingContact, Some plan ->
                    for ev in buffer.Applied do
                        match ev.Data with
                        | EventData.RestartContact(id, actor, kind) when
                            id = plan.Id && actor = plan.Executor && kind = plan.Kind
                            ->
                            setRestart
                                world
                                { restart with
                                    Phase = RestartPhase.Completed
                                    ContactObserved = true }

                            buffer.Events.Add
                                { Tick = world.State.Tick
                                  Data = EventData.RestartCompleted plan.Id }
                        | EventData.FootStrikeCancelled actor when actor = plan.Executor ->
                            MatchWorld.placeDeadBall world restart.Spot

                            setRestart
                                world
                                { restart with
                                    Phase = RestartPhase.Positioning
                                    CommandIssued = false }
                        | _ -> ()
                | RestartPhase.Completed, _ ->
                    world.State <-
                        { world.State with
                            Flow = MatchFlow.Live }
                | _, None -> ()
            | _ -> ()

            rng
