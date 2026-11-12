namespace FootballEngine.Core.Match

/// Cognition half of a restart: it chooses the plan and sends players to their marks. It never writes
/// match flow; the plan reaches the state through RestartPlanned so the world side owns every transition.
module RestartPlanning =

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

    let private plan (ctx: SystemContext) (world: World) (restart: RestartState) (buffer: EventBuffer) =
        let data =
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
            | Ok plan -> EventData.RestartPlanned plan
            | Error _ -> EventData.RestartPlanChanged(restart.Id, RestartPlanChangeReason.PlanningFailed)

        buffer.Events.Add { Tick = world.State.Tick; Data = data }

    let step: SystemStep =
        fun ctx rng world buffer ->
            match world.State.Flow with
            | MatchFlow.Restart restart ->
                match restart.Phase, restart.Plan with
                | RestartPhase.Awarded, _ -> plan ctx world restart buffer
                // Targets are re-sent every positioning tick because open play may have overwritten the intent.
                | RestartPhase.Positioning, Some plan -> projectTargets ctx.Ctx world plan
                | _ -> ()
            | _ -> ()

            rng
