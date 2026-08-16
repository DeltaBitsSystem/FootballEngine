namespace FootballEngine.Core.Match

/// Possession mechanics: dribble carry, reception grace, loose-ball contests, GK hold.
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
                MatchWorld.nearestActive ctx world s x y 9.0
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

                MatchWorld.updateBall world (fun b ->
                    { b with
                        Control = BallControl.Controlled(winSide, pid)
                        LastTouch = Some pid })

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(winSide, pid) }

                rng
        | Some(s1, slot1, _), None ->
            let pid = (MatchWorld.lineup ctx s1).Profiles.[slot1].Id

            MatchWorld.updateBall world (fun b ->
                { b with
                    Control = BallControl.Controlled(s1, pid)
                    LastTouch = Some pid })

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.Possession(s1, pid) }

            rng
        | None, Some(s2, slot2, _) ->
            let pid = (MatchWorld.lineup ctx s2).Profiles.[slot2].Id

            MatchWorld.updateBall world (fun b ->
                { b with
                    Control = BallControl.Controlled(s2, pid)
                    LastTouch = Some pid })

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.Possession(s2, pid) }

            rng
        | None, None -> rng

    /// A long-stopped free ball is picked up by whoever reaches it.
    let private pickupFreeBall
        (ctx: MatchContext)
        (rng: Rng)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        : Rng =
        let ball = world.State.Ball

        match ball.StationarySince with
        | Some since when tick - since >= 20<subtick> ->
            let x = ball.Position.X
            let y = ball.Position.Y

            match MatchWorld.nearestActive ctx world ClubSide.Home x y 3.5 with
            | Some(slot, _) ->
                let pid = (MatchWorld.lineup ctx ClubSide.Home).Profiles.[slot].Id

                MatchWorld.updateBall world (fun b ->
                    { b with
                        Control = BallControl.Controlled(ClubSide.Home, pid)
                        LastTouch = Some pid })

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(ClubSide.Home, pid) }

                rng
            | None ->
                match MatchWorld.nearestActive ctx world ClubSide.Away x y 3.5 with
                | Some(slot, _) ->
                    let pid = (MatchWorld.lineup ctx ClubSide.Away).Profiles.[slot].Id

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Control = BallControl.Controlled(ClubSide.Away, pid)
                            LastTouch = Some pid })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Possession(ClubSide.Away, pid) }

                    rng
                | None -> rng
        | _ -> rng

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let cfg = ctx.Ctx.Config

            match world.State.Ball.Control with
            | BallControl.Controlled(side, pid) ->
                match MatchWorld.slotOf ctx.Ctx side pid with
                | Some slot ->
                    let frame = (MatchWorld.team world side).Frame
                    let px = MatchWorld.posX frame slot
                    let py = MatchWorld.posY frame slot
                    let dir = MatchMath.goalDirection side

                    let carryX =
                        MatchMath.clamp
                            (-(float Pitch.HalfLength + 5.0))
                            (float Pitch.HalfLength + 5.0)
                            (float px + 0.4 * dir)

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Position =
                                { X = carryX * 1.0<meter>
                                  Y = py
                                  Z = 0.0<meter>
                                  Vx = float frame.VelX.[slot] * 1.0<meterPerSecond>
                                  Vy = float frame.VelY.[slot] * 1.0<meterPerSecond>
                                  Vz = 0.0<meterPerSecond> }
                            GKHoldSince = (if slot = 0 then Some tick else None) })

                    rng
                | None -> rng
            | BallControl.Receiving(side, pid, since) ->
                if tick - since >= cfg.Ball.ReceptionGrace then
                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Control = BallControl.Controlled(side, pid) })

                rng
            | BallControl.Contesting _ -> contest ctx.Ctx rng world tick buffer
            | BallControl.Free
            | BallControl.Airborne -> pickupFreeBall ctx.Ctx rng world tick buffer
