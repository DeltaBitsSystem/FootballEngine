namespace FootballEngine.Core.Match

/// Control of the ball must keep behaving like football in every state: a
/// carried ball follows the dribbler, a received ball gets a grace period
/// before it is fully under control, and a free ball cannot stay dead —
/// someone eventually claims it.
module BallPossession =

    /// 50/50 duel for a loose ball. A small foul chance keeps free-ball
    /// scrambles physical without making every contest a foul.
    let private contest (ctx: MatchContext) (rng: Rng) (world: World) (tick: int<subtick>) (buffer: EventBuffer) : Rng =
        let ball = world.State.Ball
        let x = ball.Position.X
        let y = ball.Position.Y

        let side =
            match ball.Control with
            | BallControl.Contesting s -> s
            | _ -> ClubSide.Home

        let nearest =
            (fun (s: ClubSide) ->
                MatchWorld.nearestActive ctx world s x y (float ctx.Config.Ball.FootReach)
                |> Option.filter (fun (slot, _) ->
                    abs (float ball.Position.Z - float (MatchWorld.team world s).Frame.PosZ[slot])
                    <= float ctx.Config.Ball.FootContactHeight)
                |> Option.map (fun (slot, d) -> s, slot, d))

        match nearest side, nearest (MatchWorld.oppOf side) with
        | Some(s1, slot1, d1), Some(s2, slot2, d2) ->
            let t1 = BallContact.timeToBall ctx world s1 slot1 x y
            let t2 = BallContact.timeToBall ctx world s2 slot2 x y

            let winSide, winSlot =
                if t1 < t2 then s1, slot1
                elif t2 < t1 then s2, slot2
                elif d1 <= d2 then s1, slot1
                else s2, slot2

            let foul, rng' = Rng.bernoulli ctx.Config.Actions.ContestFoulChance rng

            if foul then
                let winPid = (MatchWorld.lineup ctx winSide).Profiles.[winSlot].Id
                let loseSide = MatchWorld.oppOf winSide
                let loseSlot = if winSide = s1 then slot2 else slot1
                let losePid = (MatchWorld.lineup ctx loseSide).Profiles.[loseSlot].Id

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Foul(losePid, winPid, FoulSeverity.Minor, CardDecision.NoCard, (x, y)) }

                rng'
            else
                let pid = (MatchWorld.lineup ctx winSide).Profiles.[winSlot].Id

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(winSide, pid) }

                rng
        | Some(s1, slot1, _), None ->
            let pid = (MatchWorld.lineup ctx s1).Profiles.[slot1].Id

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.Possession(s1, pid) }

            rng
        | None, Some(s2, slot2, _) ->
            let pid = (MatchWorld.lineup ctx s2).Profiles.[slot2].Id

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.Possession(s2, pid) }

            rng
        | None, None -> rng

    /// A reachable loose ball can be trapped while moving; waiting for an idle timer would invent dead play.
    let private pickupFreeBall
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        : Rng =
        let ball = world.State.Ball

        let touchedThisTick =
            ball.FootMotion
            |> Option.exists (fun motion -> motion.ContactAt = tick + 1<subtick> && motion.Kind = FootTouchKind.Receive)

        if ball.Trajectory.IsNone && ball.PendingStrike.IsNone && not touchedThisTick then
            let excluded =
                match ball.FootMotion with
                | Some motion when tick <= motion.EndsAt -> Some motion.Actor
                | _ -> None

            match BallContact.tryReachableContact ctx.Ctx world ball.Position.X ball.Position.Y excluded with
            | ValueSome struct (side, slot, pid, _) ->
                BallFootContact.receive ctx world buffer side slot

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(side, pid) }
            | ValueNone -> ()

        rng

    let private carry (ctx: SystemContext) (world: World) (buffer: EventBuffer) side slot pid =
        let ball = world.State.Ball
        let tick = world.State.Tick
        let frame = (MatchWorld.team world side).Frame
        let cfg = ctx.Ctx.Config.Ball

        if (MatchWorld.profileOf ctx.Ctx side slot).IsGK then
            // A hand-held ball is an explicit attachment, unlike an outfield player's intermittent touches.
            MatchWorld.updateBall world (fun ball ->
                { ball with
                    GKHoldSince = ball.GKHoldSince |> Option.orElse (Some tick)
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
            | BallControl.Contesting _ -> contest ctx.Ctx rng world tick buffer
            | BallControl.Free
            | BallControl.Airborne -> pickupFreeBall ctx rng world tick buffer
