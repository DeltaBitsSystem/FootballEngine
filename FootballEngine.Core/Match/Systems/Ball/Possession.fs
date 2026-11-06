namespace FootballEngine.Core.Match

/// Control of the ball must keep behaving like football in every state: a
/// carried ball follows the dribbler, a received ball gets a grace period
/// before it is fully under control, and a free ball cannot stay dead —
/// someone eventually claims it.
module BallPossession =

    /// A player can claim an uncontrolled ball only after ContactDynamics records an attainable touch.

    let private carry (ctx: SystemContext) (world: World) (buffer: EventBuffer) side slot pid =
        let ball = world.State.Ball
        let tick = world.State.Tick
        let frame = (MatchWorld.team world side).Frame
        let cfg = ctx.Ctx.Config.Ball

        if (MatchWorld.profileOf ctx.Ctx side slot).IsGK && ball.GKHoldSince.IsSome then
            // A hand-held ball is an explicit attachment, unlike an outfield player's intermittent touches.
            MatchWorld.updateBall world (fun ball ->
                { ball with
                    Position =
                        { ball.Position with
                            X = MatchWorld.posX frame slot
                            Y = MatchWorld.posY frame slot
                            Vx = 0.0<meterPerSecond>
                            Vy = 0.0<meterPerSecond>
                            Vz = 0.0<meterPerSecond> } })
        elif not (BallFootContact.reachable ctx.Ctx world side slot) then
            buffer.Events.Add
                { Tick = tick
                  Data = EventData.BallLoose(ball.Position.X, ball.Position.Y) }
        elif ball.PendingStrike.IsNone then
            let due =
                match ball.FootMotion with
                | Some motion when motion.Actor = pid -> tick + 1<subtick> - motion.ContactAt >= cfg.CarryTouchInterval
                | _ -> true

            if due then
                let facing = float frame.BodyFacing[slot]
                let vx, vy = float frame.VelX[slot], float frame.VelY[slot]
                let technique = float (MatchWorld.profileOf ctx.Ctx side slot).Dribbling / 100.0

                let error =
                    cfg.CarryTechniqueError * (1.0 - technique * float frame.Condition[slot])

                let lead = float cfg.CarryOffset * (1.0 + error)

                let targetX =
                    float frame.PosX[slot] + cos facing * lead + vx * float cfg.CarryLeadTime

                let targetY =
                    float frame.PosY[slot] + sin facing * lead + vy * float cfg.CarryLeadTime

                let interval = float cfg.CarryTouchInterval / float ctx.Clock.SubTicksPerSecond
                let desiredX = vx + (targetX - float ball.Position.X) / interval
                let desiredY = vy + (targetY - float ball.Position.Y) / interval
                let speed = sqrt (desiredX * desiredX + desiredY * desiredY)

                let scale =
                    if speed > float cfg.CarryMaxSpeed then
                        float cfg.CarryMaxSpeed / speed
                    else
                        1.0

                let nextX, nextY = desiredX * scale, desiredY * scale

                let firstTouch =
                    ball.FootMotion |> Option.forall (fun motion -> motion.Actor <> pid)

                let kind =
                    if firstTouch then
                        FootTouchKind.Receive
                    else
                        FootTouchKind.Carry

                BallFootContact.recordTouch
                    ctx
                    world
                    buffer
                    side
                    slot
                    kind
                    (nextX - float ball.Position.Vx)
                    (nextY - float ball.Position.Vy)
                    (float ball.Position.Vz)
                // A finite touch changes momentum once; the position and spin subsequently evolve in BallDynamics.
                MatchWorld.updateBall world (fun ball ->
                    { ball with
                        Position =
                            { ball.Position with
                                Vx = nextX * 1.0<meterPerSecond>
                                Vy = nextY * 1.0<meterPerSecond> }
                        GKHoldSince = None
                        StationarySince = None
                        Impulse = None })

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let cfg = ctx.Ctx.Config

            match world.State.Ball.Control with
            | BallControl.Controlled(side, pid) ->
                match MatchWorld.slotOf ctx.Ctx side pid with
                | Some slot ->
                    carry ctx world buffer side slot pid
                    rng
                | None -> rng
            | BallControl.Receiving(side, pid, since) ->
                if tick - since >= cfg.Ball.ReceptionGrace then
                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Possession(side, pid) }

                rng
            | BallControl.Free
            | BallControl.Airborne
            | BallControl.Contesting _ -> rng
