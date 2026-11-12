namespace FootballEngine.Core.Match

/// The only owner of restart phase transitions. Awarding is reduced into state
/// by EventApply; this controller owns planning, authorization, command issue,
/// contact observation and the single return to live play.
module RestartController =

    let private setRestart world state =
        world.State <-
            { world.State with
                Flow = MatchFlow.Restart state }

    let private projectTargets (ctx: MatchContext) (world: World) (plan: RestartPlan) =
        for target in plan.Targets do
            match MatchWorld.slotIndex ctx target.Side target.Player with
            | -1 -> ()
            | slot ->
                let frame = (MatchWorld.team world target.Side).Frame
                frame.Intent.Kind.[slot] <- IntentCode.SetPieceRun
                frame.Intent.TargetX.[slot] <- float32 target.Position.X
                frame.Intent.TargetY.[slot] <- float32 target.Position.Y
                frame.Intent.Phase.[slot] <- IntentPhase.Executing
                frame.Intent.ExitTrigger.[slot] <- ExitTrigger.None

    let private planOnce (ctx: SystemContext) (world: World) (restart: RestartState) (buffer: EventBuffer) =
        match
            RestartPlanner.plan
                ctx.Ctx
                world
                restart.Id
                restart.Cause
                restart.Kind
                restart.Side
                restart.Spot.X
                restart.Spot.Y
                restart.AwardTick
        with
        | Error _ ->
            setRestart
                world
                { restart with
                    Phase = RestartPhase.Completed }
        | Ok plan ->
            setRestart
                world
                { restart with
                    Plan = Some plan
                    Phase = RestartPhase.Positioning }

            buffer.Events.Add
                { Tick = world.State.Tick
                  Data = EventData.RestartPlanned(plan.Id, plan.Executor) }

    let private authorize
        (ctx: SystemContext)
        (world: World)
        (restart: RestartState)
        (plan: RestartPlan)
        (buffer: EventBuffer)
        =
        let validation = RestartRules.validate ctx.Ctx world plan

        if validation.Valid then
            setRestart
                world
                { restart with
                    Phase = RestartPhase.Authorized }

            buffer.Events.Add
                { Tick = world.State.Tick
                  Data = EventData.RestartAuthorized plan.Id }
        else
            projectTargets ctx.Ctx world plan

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
                | RestartPhase.Awarded, _ -> planOnce ctx world restart buffer
                | RestartPhase.Positioning, Some plan ->
                    projectTargets ctx.Ctx world plan
                    authorize ctx world restart plan buffer
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
