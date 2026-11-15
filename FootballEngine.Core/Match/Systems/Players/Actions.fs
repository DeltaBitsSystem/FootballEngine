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
            | RestartAction.RestartPass _ ->
                (BallAero.pass
                    ctx.Ctx.Config.Ball
                    ctx.Ctx.Config.Actions.PassArrivalSpeed
                    ctx.Ctx.Config.Actions.PassArrivalSpeedGain
                    distance)
                    .LaunchSpeed

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

    let private prepareChallenges (ctx: SystemContext) (world: World) =
        match world.State.Ball.Control with
        | BallControl.Controlled(ownerSide, ownerId)
        | BallControl.Receiving(ownerSide, ownerId, _) when world.State.Flow = MatchFlow.Live ->
            let ownerFrame = (MatchWorld.team world ownerSide).Frame
            // -1 when the holder is in neither lineup.
            let ownerSlot = MatchWorld.slotIndex ctx.Ctx ownerSide ownerId
            let ball = world.State.Ball.Position
            let config = ctx.Ctx.Config.ContactAction
            let movement = ctx.Ctx.Config.Movement

            let preparation =
                float config.StandingTacklePreparation / float ctx.Clock.SubTicksPerSecond

            let slidePreparation =
                float config.SlidingTacklePreparation / float ctx.Clock.SubTicksPerSecond

            for side in MatchWorld.sides do
                if side <> ownerSide then
                    let team = MatchWorld.team world side

                    for slot in MatchWorld.activeSlots team.Frame do
                        let pid = team.RosterIds[slot]
                        let intent = team.Frame.SteerIntent.Kind[slot]

                        let profile = MatchWorld.profileOf ctx.Ctx side slot
                        let engaging = intent = IntentCode.Press || intent = IntentCode.Recover

                        // Shape duties do not stop an outfielder from playing a ball that is already at his boot; only
                        // the players sent to engage also close distance to make a challenge.
                        if pid <> ownerId && not profile.IsGK then
                            let dx = float ball.X - float team.Frame.PosX[slot]
                            let dy = float ball.Y - float team.Frame.PosY[slot]

                            let closing =
                                if engaging then
                                    float movement.SprintSpeed * (0.5 + 0.006 * float profile.Pace)
                                else
                                    0.0

                            let reach = float config.FootReach + closing * preparation
                            let slideReach = float config.SlidingReach + closing * slidePreparation
                            let distanceSquared = dx * dx + dy * dy
                            // A ball moving away from him can only be reached by going to ground; a recovering
                            // defender takes that risk, a presser facing the carrier stays on his feet.
                            let escaping = dx * float ball.Vx + dy * float ball.Vy > 0.0

                            // Where the carrier and the ball will be when the boot commits: a carrier standing between
                            // the defender and the ball shields it, and a goal-side defender reads that and holds.
                            // One the carrier has already got past has nothing left to hold for and goes through the
                            // man: the tactical foul that stops an attack.
                            let ownGoal = float (MatchMath.ownGoalX side)

                            let shielded seconds =
                                match ownerSlot with
                                | carrier when
                                    carrier >= 0
                                    && abs (float ownerFrame.PosX[carrier] - ownGoal)
                                       >= abs (float team.Frame.PosX[slot] - ownGoal)
                                    ->
                                    let px, py = float team.Frame.PosX[slot], float team.Frame.PosY[slot]

                                    let bx, by =
                                        float ball.X + float ball.Vx * seconds, float ball.Y + float ball.Vy * seconds

                                    let cx = float ownerFrame.PosX[carrier] + float ownerFrame.VelX[carrier] * seconds
                                    let cy = float ownerFrame.PosY[carrier] + float ownerFrame.VelY[carrier] * seconds
                                    let sx, sy = bx - px, by - py
                                    let length = sx * sx + sy * sy

                                    let along =
                                        if length > 1e-9 then
                                            ((cx - px) * sx + (cy - py) * sy) / length
                                        else
                                            0.0

                                    let ox, oy = px + sx * along - cx, py + sy * along - cy

                                    along > 0.0
                                    && along < 1.0
                                    && sqrt (ox * ox + oy * oy) < float config.TackleBodyClearance
                                | _ -> false

                            // Only a player sent to engage goes in while the ball is at the carrier's boot; a containing or
                            // retreating one waits for the touch that leaves it nearer him, since diving in is a trip.
                            let exposed =
                                engaging
                                || match ownerSlot with
                                   | carrier when carrier >= 0 ->
                                       let cx = float ball.X - float ownerFrame.PosX[carrier]
                                       let cy = float ball.Y - float ownerFrame.PosY[carrier]
                                       distanceSquared < cx * cx + cy * cy
                                   | _ -> true

                            let kind =
                                if not exposed then
                                    ValueNone
                                elif distanceSquared <= reach * reach && not (shielded preparation) then
                                    ValueSome ContactActionKind.ContactStandingTackle
                                elif
                                    intent = IntentCode.Recover
                                    && escaping
                                    && distanceSquared <= slideReach * slideReach
                                    && not (shielded slidePreparation)
                                then
                                    ValueSome ContactActionKind.ContactSlidingTackle
                                else
                                    ValueNone

                            match kind with
                            | ValueSome kind ->
                                ContactActionPlanner.prepare ctx world side slot kind (Some ownerId) |> ignore
                            | ValueNone -> ()
        | _ -> ()

    let private prepareReceptions (ctx: SystemContext) (world: World) =
        let ball = world.State.Ball
        let cfg = ctx.Ctx.Config.ContactAction

        let horizon =
            float (cfg.TrapPreparation + cfg.CommittedDuration)
            / float ctx.Clock.SubTicksPerSecond

        let intendedReceiver =
            match ball.Trajectory with
            | Some(TrajectoryIntent.Aimed(_, receiver, _)) -> ValueSome receiver
            | _ -> ValueNone

        match ball.Control with
        | BallControl.Free
        | BallControl.Airborne
        | BallControl.Contesting _ when world.State.Flow = MatchFlow.Live ->
            for side in MatchWorld.sides do
                let team = MatchWorld.team world side
                let frame = team.Frame

                for slot in MatchWorld.activeSlots frame do
                    let intent = frame.SteerIntent.Kind[slot]
                    // The player a pass is aimed at expects it whatever his current run, so he sets to receive it too.
                    let pursuing =
                        intent = IntentCode.Recover
                        || intent = IntentCode.Press
                        || intent = IntentCode.ExecuteRun
                        || (match intendedReceiver with
                            | ValueSome receiver -> receiver = team.RosterIds[slot]
                            | ValueNone -> false)

                    if pursuing && frame.ContactState[slot].Status <> BodyContactStatus.BodyFalling then
                        let dx = float ball.Position.X - float frame.PosX[slot]
                        let dy = float ball.Position.Y - float frame.PosY[slot]
                        let vx = float ball.Position.Vx - float frame.VelX[slot]
                        let vy = float ball.Position.Vy - float frame.VelY[slot]
                        let speedSquared = vx * vx + vy * vy

                        let approach =
                            if speedSquared > 1e-9 then
                                -(dx * vx + dy * vy) / speedSquared
                            else
                                0.0

                        let closestAt = MatchMath.clamp 0.0 horizon approach
                        let closestX, closestY = dx + vx * closestAt, dy + vy * closestAt
                        let height = float ball.Position.Z + float ball.Position.Vz * closestAt

                        // A ball that comes closest after the horizon is set for on a later pass: clamping it to the
                        // horizon armed the trap early, and the boot was recovering when the ball rolled by.
                        if
                            approach <= horizon
                            && closestX * closestX + closestY * closestY <= float cfg.FootReach ** 2.0
                            && abs (height - float frame.PosZ[slot])
                               <= float ctx.Ctx.Config.Ball.FootContactHeight
                        then
                            ContactActionPlanner.prepare ctx world side slot ContactActionKind.ContactTrap None
                            |> ignore
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let mutable r = rng

            prepareChallenges ctx world
            prepareReceptions ctx world

            match world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
            | None, ValueSome(struct (side, slot, _)) ->
                let team = MatchWorld.team world side
                let frame = team.Frame

                if slot = 0 then
                    r <- GK.distribute ctx r world buffer side
                else
                    match team.PendingActions.TryGetValue slot with
                    // A decision reaches the body the tick after it is taken, as it would from a parallel mind.
                    | true, a when a.DecidedAt >= tick -> ()
                    | true, a when a.ExpiresAt >= tick ->
                        world.Observation.BeginAction (MatchWorld.profileOf ctx.Ctx side slot).Id

                        try
                            r <- executeDecision ctx r world buffer side slot a.Choice
                        finally
                            world.Observation.EndAction()

                        team.PendingActions.Remove slot |> ignore
                    | _ -> ()

            | _ -> ()

            r
