namespace FootballEngine.Core.Match

/// Requests prepare a reachable effector; only its contact tick can change the ball's momentum.
module BallFootContact =
    let private vector (point: Vec3) =
        System.Numerics.Vector3(float32 point.X, float32 point.Y, float32 point.Z)

    let private ballVelocity (ball: BallState) =
        System.Numerics.Vector3(float32 ball.Position.Vx, float32 ball.Position.Vy, float32 ball.Position.Vz)

    let private generationOf (book: ParticipationBook) actor =
        let mutable generation = 0

        for index = 0 to book.Intervals.Count - 1 do
            if book.Intervals[index].PlayerId = actor then
                generation <- generation + 1

        generation

    let private publish (world: World) taskId phase (motion: FootMotion) predicted incoming outgoing =
        let generation =
            generationOf world.Home.Participation motion.Actor
            + generationOf world.Away.Participation motion.Actor

        let family =
            match motion.Kind with
            | FootTouchKind.Kick _ -> MotionFamily.FootStrike
            | FootTouchKind.Receive -> MotionFamily.Reception
            | FootTouchKind.Carry -> MotionFamily.Dribble

        world.Motion.Append
            { Sequence = 0UL
              TaskId = taskId
              Actor = motion.Actor
              Generation = generation
              Family = family
              Effector =
                if motion.Foot = ContactFoot.Left then
                    MotionEffector.LeftFoot
                else
                    MotionEffector.RightFoot
              Phase = phase
              OccurredAt = world.State.Tick
              AvailableAt = world.State.Tick + 1<subtick>
              StartedAt = motion.StartedAt
              CommitAt = motion.ContactAt
              EndsAt = motion.EndsAt
              PredictedPoint = predicted
              ConfirmedPoint =
                if phase = MotionEvidencePhase.Confirmed then
                    ValueSome(vector motion.Point)
                else
                    ValueNone
              BallCenter = vector motion.BallCenter
              IncomingVelocity = incoming
              OutgoingVelocity = outgoing }
        |> ignore

    let private centre (ball: BallState) : Vec3 =
        { X = ball.Position.X
          Y = ball.Position.Y
          Z = ball.Position.Z + ball.Radius }

    let private point (cfg: BallConfig) (ball: BallState) vx vy : Vec3 =
        let speed = max 1e-10 (sqrt (vx * vx + vy * vy))

        { X = ball.Position.X - vx / speed * cfg.Radius
          Y = ball.Position.Y - vy / speed * cfg.Radius
          Z = ball.Position.Z + cfg.Radius }

    let reachable (ctx: MatchContext) (world: World) side slot =
        let frame = (MatchWorld.team world side).Frame
        let ball = world.State.Ball.Position

        let dx, dy =
            float ball.X - float frame.PosX[slot], float ball.Y - float frame.PosY[slot]

        MatchWorld.isActiveSlot frame.Occupancy[slot]
        && dx * dx + dy * dy <= float ctx.Config.Ball.FootReach ** 2.0
        && abs (float ball.Z - float frame.PosZ[slot])
           <= float ctx.Config.Ball.FootContactHeight

    let private ownsBall (world: World) side actor =
        match world.State.Ball.Control with
        | BallControl.Controlled(s, p)
        | BallControl.Receiving(s, p, _) -> s = side && p = actor
        | _ -> false

    /// The foot on the ball's side of the body, for touches that are not timed by the stride.
    let lateralFoot (world: World) side slot =
        let frame = (MatchWorld.team world side).Frame
        let facing = float frame.BodyFacing[slot]
        let ball = world.State.Ball.Position

        let lateral =
            -sin facing * (float ball.X - float frame.PosX[slot])
            + cos facing * (float ball.Y - float frame.PosY[slot])

        if lateral > 0.0 then
            ContactFoot.Left
        else
            ContactFoot.Right

    /// Retracts an announced carry touch that will not happen, so the viewer does not swing at a ball now elsewhere.
    let withdrawTouch (world: World) =
        match world.State.Ball.AnnouncedTouch with
        | ValueSome announced ->
            publish
                world
                announced.TaskId
                MotionEvidencePhase.Cancelled
                announced.Motion
                (vector announced.Motion.Point)
                ValueNone
                ValueNone

            MatchWorld.updateBall world (fun ball -> { ball with AnnouncedTouch = ValueNone })
        | ValueNone -> ()

    /// Publishes the next carry touch ahead of time; `point` is where the boot meets the ball's rear surface.
    let announceTouch (world: World) (motion: FootMotion) =
        withdrawTouch world
        let taskId = world.Motion.NextSequence
        publish world taskId MotionEvidencePhase.Prepared motion (vector motion.Point) ValueNone ValueNone

        MatchWorld.updateBall world (fun ball ->
            { ball with
                AnnouncedTouch = ValueSome { TaskId = taskId; Motion = motion } })

    /// Contact point on the ball's rear surface for a ball centred at (x, y) leaving along (vx, vy).
    let rearPoint (cfg: BallConfig) (x: float) (y: float) (vx: float) (vy: float) : Vec3 =
        let speed = max 1e-10 (sqrt (vx * vx + vy * vy))

        { X = (x - vx / speed * float cfg.Radius) * 1.0<meter>
          Y = (y - vy / speed * float cfg.Radius) * 1.0<meter>
          Z = cfg.Radius }

    let emit tick (buffer: EventBuffer) (motion: FootMotion) =
        buffer.Events.Add
            { Tick = tick
              Data =
                EventData.FootContact(
                    motion.Actor,
                    motion.Foot,
                    motion.Kind,
                    motion.Point.X,
                    motion.Point.Y,
                    motion.Point.Z
                ) }

    let request (ctx: SystemContext) (world: World) (buffer: EventBuffer) (impulse: BallImpulse) trajectory arrival =
        match impulse.StrikeKind, MatchWorld.sideOfPlayer ctx.Ctx impulse.ActorId with
        | StrikeKind.Restart SetPieceKind.ThrowIn, _ ->
            BallPhysics.applyImpulse world impulse trajectory arrival

            impulse.RestartId
            |> Option.iter (fun id ->
                buffer.Events.Add
                    { Tick = world.State.Tick
                      Data = EventData.RestartContact(id, impulse.ActorId, SetPieceKind.ThrowIn) })
        | _, ValueSome side ->
            match MatchWorld.slotOf ctx.Ctx side impulse.ActorId with
            | ValueSome slot when world.State.Ball.PendingStrike.IsNone && reachable ctx.Ctx world side slot ->
                withdrawTouch world
                let cfg = ctx.Ctx.Config.Ball
                let contact = world.State.Tick + cfg.KickWindup
                let foot = lateralFoot world side slot

                let motion =
                    { Actor = impulse.ActorId
                      Foot = foot
                      Kind = FootTouchKind.Kick impulse.StrikeKind
                      Surface =
                        StrikeTechnique.choose
                            ctx.Ctx.Config.StrikeTechnique
                            impulse
                            foot
                            (float (MatchWorld.team world side).Frame.BodyFacing[slot])
                      StartedAt = world.State.Tick + 1<subtick>
                      ContactAt = contact + 1<subtick>
                      EndsAt = contact + cfg.KickRecovery + 1<subtick>
                      Point =
                        point cfg world.State.Ball (float impulse.LinearVelocity.X) (float impulse.LinearVelocity.Y)
                      BallCenter = centre world.State.Ball }

                let pending =
                    { MotionTaskId = world.Motion.NextSequence
                      Impulse =
                        { impulse with
                            ContactTick = SimTick.ofSubTicks contact }
                      Trajectory = trajectory
                      ArrivalGate = arrival |> Option.map (fun gate -> gate + cfg.KickWindup)
                      Side = side
                      Slot = slot }

                publish
                    world
                    pending.MotionTaskId
                    MotionEvidencePhase.Prepared
                    motion
                    (vector motion.Point)
                    ValueNone
                    ValueNone

                MatchWorld.updateBall world (fun ball ->
                    { ball with
                        PendingStrike = Some pending
                        FootMotion = Some motion })

                world.Observation.PrepareImpulse impulse.ActionId
            | _ ->
                buffer.Events.Add
                    { Tick = world.State.Tick
                      Data = EventData.FootStrikeCancelled impulse.ActorId }
        | _ ->
            buffer.Events.Add
                { Tick = world.State.Tick
                  Data = EventData.FootStrikeCancelled impulse.ActorId }

    let recordTouch
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        side
        slot
        touchFoot
        kind
        (vx: float)
        (vy: float)
        (outgoingVz: float)
        =
        let actor = (MatchWorld.profileOf ctx.Ctx side slot).Id
        let cfg = ctx.Ctx.Config.Ball

        let motion =
            { Actor = actor
              Foot = touchFoot
              Kind = kind
              // Carrying and cushioning are side-foot touches.
              Surface = StrikeSurface.Inside
              StartedAt = world.State.Tick
              ContactAt = world.State.Tick + 1<subtick>
              EndsAt = world.State.Tick + cfg.CarryTouchInterval + 1<subtick>
              // A carry pushes the ball on from behind, so the boot meets the rear of its new path; a reception
              // absorbs momentum and meets the face the impulse opposes.
              Point =
                match kind with
                | FootTouchKind.Carry ->
                    point
                        cfg
                        world.State.Ball
                        (float world.State.Ball.Position.Vx + vx)
                        (float world.State.Ball.Position.Vy + vy)
                | _ -> point cfg world.State.Ball vx vy
              BallCenter = centre world.State.Ball }

        // The touch that was announced confirms that task, meeting the ball on the face the swing was aimed at:
        // a boot already on its way strikes where it was sent, and the new direction comes from the push, not
        // from the boot circling the ball. Any other touch first retracts the announcement.
        let struct (taskId, motion) =
            match world.State.Ball.AnnouncedTouch with
            | ValueSome announced when
                announced.Motion.Actor = actor
                && announced.Motion.Foot = touchFoot
                && kind = FootTouchKind.Carry
                ->
                MatchWorld.updateBall world (fun ball -> { ball with AnnouncedTouch = ValueNone })
                let aimed = announced.Motion

                let face: Vec3 =
                    { X = motion.BallCenter.X + (aimed.Point.X - aimed.BallCenter.X)
                      Y = motion.BallCenter.Y + (aimed.Point.Y - aimed.BallCenter.Y)
                      Z = motion.BallCenter.Z + (aimed.Point.Z - aimed.BallCenter.Z) }

                struct (announced.TaskId, { motion with Point = face })
            | _ ->
                withdrawTouch world
                struct (world.Motion.NextSequence, motion)

        MatchWorld.updateBall world (fun ball ->
            { ball with
                FootMotion = Some motion
                LastTouch = Some actor })

        emit world.State.Tick buffer motion

        let incoming = ballVelocity world.State.Ball

        let outgoing =
            System.Numerics.Vector3(
                float32 (float world.State.Ball.Position.Vx + vx),
                float32 (float world.State.Ball.Position.Vy + vy),
                float32 outgoingVz
            )

        publish
            world
            taskId
            MotionEvidencePhase.Confirmed
            motion
            (vector motion.Point)
            (ValueSome incoming)
            (ValueSome outgoing)

    /// Re-leads the receiver at the moment of contact, since he kept moving during the wind-up. A ground pass is
    /// re-solved with the roll model for its new length, so its speed, arrival gate and target stay one solution;
    /// other aimed deliveries keep their solved speed and only turn toward the new lead.
    let private liveAimedTarget
        (ctx: SystemContext)
        (world: World)
        (pending: PendingFootStrike)
        (origin: Spatial)
        (impulse: BallImpulse)
        : Vec2 option * int<subtick> option * float option =
        let unchanged = impulse.IntendedTarget, pending.ArrivalGate, None

        match pending.Trajectory, impulse.ExecutionContext with
        | TrajectoryIntent.Aimed(_, receiver, _), ExecutionContext.OpenPlay ->
            match MatchWorld.sideOfPlayer ctx.Ctx receiver with
            | ValueSome side ->
                match MatchWorld.slotOf ctx.Ctx side receiver with
                | ValueSome slot ->
                    let frame = (MatchWorld.team world side).Frame
                    let cfg = ctx.Ctx.Config

                    let speed =
                        sqrt (float impulse.LinearVelocity.X ** 2.0 + float impulse.LinearVelocity.Y ** 2.0)

                    if MatchWorld.isActiveSlot frame.Occupancy[slot] && speed > 1e-8 then
                        let receiverX = float frame.PosX[slot]
                        let receiverY = float frame.PosY[slot]
                        let receiverVx = float frame.VelX[slot]
                        let receiverVy = float frame.VelY[slot]
                        // The lead must stay collectable on the pitch, as when the pass was planned.
                        let inset = float cfg.Positioning.TargetInset
                        let limitX = float Pitch.HalfLength - inset
                        let limitY = float Pitch.HalfWidth - inset
                        let groundPass = impulse.StrikeKind = StrikeKind.Pass

                        let flight distance =
                            if groundPass then
                                let solved =
                                    BallAero.pass
                                        cfg.Ball
                                        cfg.Actions.PassArrivalSpeed
                                        cfg.Actions.PassArrivalSpeedGain
                                        distance

                                solved.Seconds, solved.LaunchSpeed
                            else
                                distance / speed, speed

                        let distanceTo x y =
                            sqrt ((x - float origin.X) ** 2.0 + (y - float origin.Y) ** 2.0)

                        let mutable flightSeconds, launchSpeed = flight (distanceTo receiverX receiverY)
                        let mutable targetX = receiverX
                        let mutable targetY = receiverY

                        for _ in 1..6 do
                            targetX <- MatchMath.clamp -limitX limitX (receiverX + receiverVx * flightSeconds)
                            targetY <- MatchMath.clamp -limitY limitY (receiverY + receiverVy * flightSeconds)
                            let seconds, solvedSpeed = flight (distanceTo targetX targetY)
                            flightSeconds <- seconds
                            launchSpeed <- solvedSpeed

                        let target: Vec2 =
                            { X = targetX * 1.0<meter>
                              Y = targetY * 1.0<meter> }

                        let travelTicks =
                            max 1 (int (ceil (flightSeconds * float ctx.Clock.SubTicksPerSecond)))
                            * 1<subtick>

                        Some target,
                        Some(world.State.Tick + travelTicks),
                        (if groundPass then Some launchSpeed else None)
                    else
                        unchanged
                | ValueNone -> unchanged
            | ValueNone -> unchanged
        | _ -> unchanged

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let mutable rng = rng

            // An announced touch is stale once its carrier no longer controls the ball or its moment has passed
            // without a touch; the carry system re-announces after the next real touch.
            match world.State.Ball.AnnouncedTouch with
            | ValueSome announced ->
                let controlled =
                    match world.State.Ball.Control with
                    | BallControl.Controlled(_, actor) -> actor = announced.Motion.Actor
                    | _ -> false

                if
                    not controlled
                    || tick > announced.Motion.ContactAt + ctx.Ctx.Config.Ball.CarryTouchInterval
                then
                    withdrawTouch world
            | ValueNone -> ()

            match world.State.Ball.PendingStrike, world.State.Ball.FootMotion with
            | Some pending, Some motion ->
                let flowAllows =
                    match world.State.Flow with
                    | MatchFlow.Live -> ownsBall world pending.Side motion.Actor
                    | MatchFlow.Restart restart -> pending.Impulse.RestartId = Some restart.Id
                    | _ -> false

                if not flowAllows || not (reachable ctx.Ctx world pending.Side pending.Slot) then
                    publish
                        world
                        pending.MotionTaskId
                        MotionEvidencePhase.Cancelled
                        motion
                        (vector motion.Point)
                        ValueNone
                        ValueNone

                    world.Observation.CancelImpulse()

                    MatchWorld.updateBall world (fun ball ->
                        { ball with
                            PendingStrike = None
                            FootMotion = None })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.FootStrikeCancelled motion.Actor }
                elif tick >= SimTick.value pending.Impulse.ContactTick then
                    let incoming = ballVelocity world.State.Ball
                    let predicted = vector motion.Point
                    let old = pending.Impulse
                    let origin = world.State.Ball.Position

                    let intendedTarget, arrivalGate, solvedSpeed =
                        liveAimedTarget ctx world pending origin old

                    let speed =
                        solvedSpeed
                        |> Option.defaultValue (MatchMath.speedOf old.LinearVelocity.X old.LinearVelocity.Y)

                    let old =
                        { old with
                            IntendedTarget = intendedTarget }

                    let velocity =
                        match old.IntendedTarget with
                        | Some target ->
                            let dx, dy = float (target.X - origin.X), float (target.Y - origin.Y)
                            let distance = max 1e-10 (sqrt (dx * dx + dy * dy))

                            { old.LinearVelocity with
                                X = dx / distance * speed * 1.0<meterPerSecond>
                                Y = dy / distance * speed * 1.0<meterPerSecond> }
                        | None -> old.LinearVelocity

                    let impulse =
                        { old with
                            Origin =
                                { X = origin.X
                                  Y = origin.Y
                                  Z = origin.Z }
                            LinearVelocity = velocity }

                    // Technique, pressure and balance are read at contact, where the boot actually meets the ball.
                    let struct (impulse, shaped) =
                        StrikeTechnique.shape
                            ctx.Ctx
                            world
                            pending.Side
                            pending.Slot
                            motion.Foot
                            motion.Surface
                            impulse
                            rng

                    rng <- shaped

                    let contactPoint, angular =
                        BallPhysics.strikeContact ctx.Ctx.Config.Ball world.State.Ball impulse

                    let impulse =
                        { impulse with
                            AngularVelocity = angular }

                    let motion =
                        { motion with
                            Point = contactPoint
                            BallCenter = centre world.State.Ball }

                    BallPhysics.applyImpulse world impulse pending.Trajectory arrivalGate

                    publish
                        world
                        pending.MotionTaskId
                        MotionEvidencePhase.Confirmed
                        motion
                        predicted
                        (ValueSome incoming)
                        (ValueSome(ballVelocity world.State.Ball))

                    MatchWorld.updateBall world (fun ball ->
                        { ball with
                            PendingStrike = None
                            FootMotion = Some motion })

                    emit tick buffer motion
                    world.Observation.Associate(buffer.Events[buffer.Events.Count - 1], ActionCause impulse.ActionId)

                    if impulse.StrikeKind = StrikeKind.Cross then
                        let attempt =
                            { Tick = tick
                              Data = EventData.CrossAttempt(motion.Actor, origin.X, origin.Y) }

                        buffer.Events.Add attempt
                        world.Observation.Associate(attempt, ActionCause impulse.ActionId)
                    // A receiver can move onside during preparation; the legal snapshot belongs to contact.
                    match pending.Trajectory, impulse.ExecutionContext with
                    | TrajectoryIntent.Aimed(_, receiver, _), ExecutionContext.OpenPlay ->
                        match MatchWorld.slotOf ctx.Ctx pending.Side receiver with
                        | ValueSome receiverSlot ->
                            let frame = (MatchWorld.team world pending.Side).Frame
                            let receiverX = MatchWorld.posX frame receiverSlot
                            let line = MatchWorld.offsideLine ctx.Ctx world pending.Side
                            let direction = MatchMath.goalDirection pending.Side

                            if
                                float receiverX * direction > max (float line * direction) (float origin.X * direction)
                            then
                                RefereeVAR.markOffsideSnapshot world pending.Side receiverSlot receiverX line tick

                                buffer.Events.Add
                                    { Tick = tick
                                      Data = EventData.Offside receiver }
                        | ValueNone -> ()
                    | _ -> ()

                    match impulse.RestartId, impulse.StrikeKind with
                    | Some id, StrikeKind.Restart kind ->
                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.RestartContact(id, motion.Actor, kind) }
                    | _ -> ()
            | _ -> ()

            rng
