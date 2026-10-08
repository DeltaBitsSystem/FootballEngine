namespace FootballEngine.Core.Match

/// Requests prepare a reachable effector; only its contact tick can change the ball's momentum.
module BallFootContact =
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

    let private foot (world: World) side slot =
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
        match impulse.StrikeKind, MatchWorld.teamOfPlayer ctx.Ctx impulse.ActorId with
        | StrikeKind.Restart SetPieceKind.ThrowIn, _ ->
            BallPhysics.applyImpulse world impulse trajectory arrival

            impulse.RestartId
            |> Option.iter (fun id ->
                buffer.Events.Add
                    { Tick = world.State.Tick
                      Data = EventData.RestartContact(id, impulse.ActorId, SetPieceKind.ThrowIn) })
        | _, Some side ->
            match MatchWorld.slotOf ctx.Ctx side impulse.ActorId with
            | Some slot when world.State.Ball.PendingStrike.IsNone && reachable ctx.Ctx world side slot ->
                let cfg = ctx.Ctx.Config.Ball
                let contact = world.State.Tick + cfg.KickWindup

                let motion =
                    { Actor = impulse.ActorId
                      Foot = foot world side slot
                      Kind = FootTouchKind.Kick impulse.StrikeKind
                      StartedAt = world.State.Tick + 1<subtick>
                      ContactAt = contact + 1<subtick>
                      EndsAt = contact + cfg.KickRecovery + 1<subtick>
                      Point =
                        point cfg world.State.Ball (float impulse.LinearVelocity.X) (float impulse.LinearVelocity.Y)
                      BallCenter = centre world.State.Ball }

                let pending =
                    { Impulse =
                        { impulse with
                            ContactTick = SimTick.ofSubTicks contact }
                      Trajectory = trajectory
                      ArrivalGate = arrival |> Option.map (fun gate -> gate + cfg.KickWindup)
                      Side = side
                      Slot = slot }

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

    let recordTouch (ctx: SystemContext) (world: World) (buffer: EventBuffer) side slot kind vx vy =
        let actor = (MatchWorld.profileOf ctx.Ctx side slot).Id
        let cfg = ctx.Ctx.Config.Ball

        let motion =
            { Actor = actor
              Foot = foot world side slot
              Kind = kind
              StartedAt = world.State.Tick
              ContactAt = world.State.Tick + 1<subtick>
              EndsAt = world.State.Tick + cfg.CarryTouchInterval + 1<subtick>
              Point = point cfg world.State.Ball vx vy
              BallCenter = centre world.State.Ball }

        MatchWorld.updateBall world (fun ball ->
            { ball with
                FootMotion = Some motion
                LastTouch = Some actor })

        emit world.State.Tick buffer motion

    let receive (ctx: SystemContext) (world: World) buffer side slot =
        let profile = MatchWorld.profileOf ctx.Ctx side slot

        if not profile.IsGK then
            let frame = (MatchWorld.team world side).Frame
            let ball = world.State.Ball
            let retention = ctx.Ctx.Config.Ball.ReceptionVelocityRetention

            let vx =
                float frame.VelX[slot]
                + (float ball.Position.Vx - float frame.VelX[slot]) * retention

            let vy =
                float frame.VelY[slot]
                + (float ball.Position.Vy - float frame.VelY[slot]) * retention

            recordTouch
                ctx
                world
                buffer
                side
                slot
                FootTouchKind.Receive
                (vx - float ball.Position.Vx)
                (vy - float ball.Position.Vy)
            // The boot absorbs relative momentum at first touch, not on a later possession timer.
            MatchWorld.updateBall world (fun ball ->
                { ball with
                    Position =
                        { ball.Position with
                            Vx = vx * 1.0<meterPerSecond>
                            Vy = vy * 1.0<meterPerSecond>
                            Vz = ball.Position.Vz * retention }
                    Spin =
                        { X = ball.Spin.X * retention
                          Y = ball.Spin.Y * retention
                          Z = ball.Spin.Z * retention }
                    GKHoldSince = None })

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick

            match world.State.Ball.PendingStrike, world.State.Ball.FootMotion with
            | Some pending, Some motion ->
                let flowAllows =
                    match world.State.Flow with
                    | MatchFlow.Live -> ownsBall world pending.Side motion.Actor
                    | MatchFlow.Restart restart -> pending.Impulse.RestartId = Some restart.Id
                    | _ -> false

                if not flowAllows || not (reachable ctx.Ctx world pending.Side pending.Slot) then
                    world.Observation.CancelImpulse()

                    MatchWorld.updateBall world (fun ball ->
                        { ball with
                            PendingStrike = None
                            FootMotion = None })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.FootStrikeCancelled motion.Actor }
                elif tick >= SimTick.value pending.Impulse.ContactTick then
                    let old = pending.Impulse
                    let origin = world.State.Ball.Position
                    let speed = MatchMath.speedOf old.LinearVelocity.X old.LinearVelocity.Y

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

                    let contactPoint, angular =
                        BallPhysics.strikeContact ctx.Ctx.Config.Ball world.State.Ball impulse

                    let impulse =
                        { impulse with
                            AngularVelocity = angular }

                    let motion =
                        { motion with
                            Point = contactPoint
                            BallCenter = centre world.State.Ball }

                    BallPhysics.applyImpulse world impulse pending.Trajectory pending.ArrivalGate

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
                        | Some receiverSlot ->
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
                        | None -> ()
                    | _ -> ()

                    match impulse.RestartId, impulse.StrikeKind with
                    | Some id, StrikeKind.Restart kind ->
                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.RestartContact(id, motion.Actor, kind) }
                    | _ -> ()
            | _ -> ()

            rng
