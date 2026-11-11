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
            let gait = ctx.Ctx.Config.Gait
            let vx, vy = float frame.VelX[slot], float frame.VelY[slot]
            let morphology = (MatchWorld.profileOf ctx.Ctx side slot).Morphology

            // Airborne the stride clock holds, so it cannot time a touch.
            let cycles =
                if frame.PosZ[slot] > 0.0f then
                    0.0
                else
                    Gait.cyclesPerSecond gait morphology (sqrt (vx * vx + vy * vy))

            let phase = float frame.GaitPhase[slot]
            let previous = phase - cycles * float (SimulationClock.dt ctx.Clock)

            // The latest occurrence of a touch point at or before this tick, compared with where the clock stood a
            // tick ago.
            let crossed point = point + floor (phase - point) > previous

            let crossing =
                [ gait.CarryTouchPhase; gait.CarryTouchPhase + 0.5 ]
                |> List.tryFind (fun point -> cycles > 0.0 && crossed point)

            let touch =
                match ball.AnnouncedTouch, ball.FootMotion with
                // An announced touch is a commitment, like a strike's wind-up: the boot is already swinging to the
                // tick and face it was sent to, so it lands then, whatever the stride clock has drifted to since.
                | ValueSome announced, _ when announced.Motion.Actor = pid ->
                    if tick + 1<subtick> = announced.Motion.ContactAt then Some announced.Motion.Foot else None
                | _, Some motion when motion.Actor = pid ->
                    if tick + 1<subtick> - motion.ContactAt < cfg.CarryTouchInterval then None
                    elif cycles = 0.0 then Some(BallFootContact.lateralFoot world side slot)
                    else crossing |> Option.map Gait.swingFoot
                // The first touch is a reception: it cannot wait for the stride.
                | _, _ -> Some(BallFootContact.lateralFoot world side slot)

            match touch with
            | None -> ()
            | Some touchFoot ->
                let facing = float frame.BodyFacing[slot]
                let technique = float (MatchWorld.profileOf ctx.Ctx side slot).Dribbling / 100.0

                let error =
                    cfg.CarryTechniqueError * (1.0 - technique * float frame.Condition[slot])

                let lead = float cfg.CarryOffset * (1.0 + error)

                let perSecond = float ctx.Clock.SubTicksPerSecond
                let minimumTicks = int cfg.CarryTouchInterval

                // The next touch is the first crossing of a touch point once the minimum interval has passed, at
                // the current cadence; standing, it is the interval itself. Ticks are counted the way the crossing
                // test above detects them.
                let struct (nextTicks, nextFoot) =
                    if cycles > 0.0 then
                        let mutable best = System.Int32.MaxValue
                        let mutable foot = touchFoot

                        for point in [ gait.CarryTouchPhase; gait.CarryTouchPhase + 0.5 ] do
                            let mutable lap = Gait.wrap (point - phase)

                            while int (ceil (lap / cycles * perSecond)) < minimumTicks do
                                lap <- lap + 1.0

                            let ticks = int (ceil (lap / cycles * perSecond))

                            if ticks < best then
                                best <- ticks
                                foot <- Gait.swingFoot point

                        struct (best, foot)
                    else
                        struct (minimumTicks, touchFoot)

                let interval = float nextTicks / perSecond
                let dt = float (SimulationClock.dt ctx.Clock)

                // The ball under the dynamics the engine will apply until the next touch is detected.
                let rolled launchX launchY =
                    let mutable rolling =
                        { ball with
                            Position =
                                { ball.Position with
                                    Vx = launchX * 1.0<meterPerSecond>
                                    Vy = launchY * 1.0<meterPerSecond> }
                            Impulse = None }

                    for step = 1 to nextTicks do
                        rolling <- BallDynamics.integrate cfg dt (tick + step * 1<subtick>) rolling

                    rolling

                // At the next touch the ball must sit at boot reach ahead of where the carrier will be, as a real
                // touch meets it; a ball aimed further ahead is out of the swinging leg's reach at contact.
                let targetX = float frame.PosX[slot] + vx * interval + cos facing * lead
                let targetY = float frame.PosY[slot] + vy * interval + sin facing * lead
                let mutable desiredX = (targetX - float ball.Position.X) / interval
                let mutable desiredY = (targetY - float ball.Position.Y) / interval

                // Two corrections absorb rolling loss and spin; the residual is well under a centimetre.
                for _ in 1..2 do
                    let arrived = rolled desiredX desiredY
                    desiredX <- desiredX + (targetX - float arrived.Position.X) / interval
                    desiredY <- desiredY + (targetY - float arrived.Position.Y) / interval

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
                    touchFoot
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

                // The next touch is known now: its tick, its foot and where the ball will be. Publishing it lets the
                // viewer swing that boot to the ball from toe-off instead of snapping it there at contact.
                let contact = tick + 1<subtick> + nextTicks * 1<subtick>

                // Standing, no toe-off times the swing: the foot reaches over the whole interval.
                let lead =
                    if cycles > 0.0 then int (round (gait.CarrySwingLead / cycles * perSecond)) * 1<subtick>
                    else nextTicks * 1<subtick>
                let rolled = rolled nextX nextY
                let ballX = float rolled.Position.X
                let ballY = float rolled.Position.Y
                let nextX, nextY = float rolled.Position.Vx, float rolled.Position.Vy

                BallFootContact.announceTouch
                    world
                    { Actor = pid
                      Foot = nextFoot
                      Kind = FootTouchKind.Carry
                      StartedAt = max (tick + 1<subtick>) (contact - lead)
                      ContactAt = contact
                      EndsAt = contact + cfg.CarryTouchInterval
                      Point = BallFootContact.rearPoint cfg ballX ballY nextX nextY
                      BallCenter =
                        { X = ballX * 1.0<meter>
                          Y = ballY * 1.0<meter>
                          Z = cfg.Radius } }

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
