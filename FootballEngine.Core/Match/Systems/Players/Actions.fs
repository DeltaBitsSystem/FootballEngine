namespace FootballEngine.Core.Match

/// Proposals stay separate from movement so choosing an action cannot overwrite a committed run.
module Actions =

    let executeRestart (ctx: SystemContext) (world: World) (buffer: EventBuffer) (plan: RestartPlan) : unit =
        let origin = world.State.Ball.Position

        let target =
            match plan.Action with
            | RestartAction.RestartPass receiver ->
                plan.Targets
                |> Array.tryFind (fun item -> item.Player = receiver)
                |> Option.map _.Position
                |> Option.defaultValue
                    { X = origin.X + 12.0<meter> * MatchMath.goalDirection plan.Side
                      Y = origin.Y }
            | RestartAction.RestartCross(x, y)
            | RestartAction.RestartShot(x, y)
            | RestartAction.RestartClearance(x, y) -> { X = x; Y = y }

        let dx = float target.X - float origin.X
        let dy = float target.Y - float origin.Y
        let distance = max 0.001 (sqrt (dx * dx + dy * dy))

        let speed =
            match plan.Action with
            | RestartAction.RestartShot _ -> 24.0
            | RestartAction.RestartCross _ -> 20.0
            | RestartAction.RestartClearance _ -> 22.0
            | RestartAction.RestartPass _ -> float ctx.Ctx.Config.Actions.PassLaunchSpeed

        let trajectory =
            match plan.Action with
            | RestartAction.RestartPass receiver -> TrajectoryIntent.Aimed(plan.Executor, receiver, 0.8)
            | RestartAction.RestartCross _ -> TrajectoryIntent.Cleared
            | RestartAction.RestartShot _ ->
                TrajectoryIntent.Struck(plan.Executor, speed * 1.0<meterPerSecond>, 0.0, 0.2)
            | RestartAction.RestartClearance _ -> TrajectoryIntent.Cleared

        let impulse =
            BallPhysics.createImpulse
                plan.Executor
                { X = origin.X
                  Y = origin.Y
                  Z = origin.Z }
                { X = dx / distance * speed * 1.0<meterPerSecond>
                  Y = dy / distance * speed * 1.0<meterPerSecond>
                  Z = 0.0<meterPerSecond> }
                { Top = 0.0<radianPerSecond>
                  Side = 0.0<radianPerSecond>
                  Roll = 0.0<radianPerSecond> }
                (Some target)
                (StrikeKind.Restart plan.Kind)
                (SimTick.ofSubTicks world.State.Tick)
                (ExecutionContext.SetPiece plan.Kind)
            |> BallPhysics.withRestartId plan.Id

        let arrival =
            world.State.Tick
            + int (round (distance / speed * float ctx.Clock.SubTicksPerSecond)) * 1<subtick>
            + 1<subtick>

        BallFootContact.request ctx world buffer impulse trajectory (Some arrival)

    let private executeDecision
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (slot: int)
        (choice: PlayerActionChoice)
        : Rng =
        match choice with
        | DeliverPass targetSlot -> Pass.launch ctx rng world buffer side slot targetSlot 0.0
        | TakeShot -> Shot.launch ctx rng world buffer side slot
        | ContestDuel -> Duel.adjudicate ctx rng world buffer side slot
        | ClearBall ->
            let tick = world.State.Tick
            let p = MatchWorld.profileOf ctx.Ctx side slot
            let pid = p.Id
            let frame = (MatchWorld.team world side).Frame
            let x = MatchWorld.posX frame slot
            let y = MatchWorld.posY frame slot
            let dir = MatchMath.goalDirection side

            let targetX = MatchMath.ownGoalX side + 30.0<meter> * dir
            let targetY = 0.0<meter>
            let d = MatchMath.dist x y targetX targetY
            let speed = 20.0
            let direction = max d 0.001

            let linearVelocity: LinearVelocity =
                { X = float (targetX - x) / direction * speed * 1.0<meterPerSecond>
                  Y = float (targetY - y) / direction * speed * 1.0<meterPerSecond>
                  Z = 5.0<meterPerSecond> }

            let angularVelocity =
                { Top = -0.8<radianPerSecond>
                  Side = 0.0<radianPerSecond>
                  Roll = 0.0<radianPerSecond> }

            let impulse =
                BallPhysics.createImpulse
                    pid
                    { X = world.State.Ball.Position.X
                      Y = world.State.Ball.Position.Y
                      Z = world.State.Ball.Position.Z }
                    linearVelocity
                    angularVelocity
                    (Some { X = targetX; Y = targetY })
                    StrikeKind.Clearance
                    (SimTick.ofSubTicks tick)
                    ExecutionContext.OpenPlay

            let arrival =
                tick + int (round (d / speed * float ctx.Clock.SubTicksPerSecond)) * 1<subtick>

            BallFootContact.request ctx world buffer impulse (TrajectoryIntent.Cleared) (Some arrival)

            rng
        | DeliverCross -> Cross.launch ctx rng world buffer side slot

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let mutable r = rng

            match world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
            | None, Some(side, slot, _) ->
                let team = MatchWorld.team world side
                let frame = team.Frame

                if slot = 0 then
                    r <- GK.distribute ctx r world buffer side
                else
                    match team.PendingActions.TryGetValue slot with
                    | true, a when a.ExpiresAt >= tick ->
                        world.Observation.BeginAction (MatchWorld.profileOf ctx.Ctx side slot).Id

                        try
                            r <- executeDecision ctx r world buffer side slot a.Choice
                        finally
                            world.Observation.EndAction()

                        team.PendingActions.Remove slot |> ignore
                    | _ ->
                        match
                            MatchWorld.nearestOpponent
                                ctx.Ctx
                                world
                                side
                                (MatchWorld.posX frame slot)
                                (MatchWorld.posY frame slot)
                                2.5
                        with
                        | Some _ -> r <- PlayerDecision.choose ctx.Ctx world side slot tick r
                        | None -> ()

            | _ -> ()

            r
