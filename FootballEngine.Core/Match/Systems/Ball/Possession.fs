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

    /// A free ball nobody claims would freeze the game: after a long stop the
    /// first player to reach it simply picks it up — no duel worth modeling remains.
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

            let controlRadius = float ctx.Config.Intercept.ControlRadius

            match MatchWorld.nearestActive ctx world ClubSide.Home x y controlRadius with
            | Some(slot, _) ->
                let pid = (MatchWorld.lineup ctx ClubSide.Home).Profiles.[slot].Id

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(ClubSide.Home, pid) }

                rng
            | None ->
                match MatchWorld.nearestActive ctx world ClubSide.Away x y controlRadius with
                | Some(slot, _) ->
                    let pid = (MatchWorld.lineup ctx ClubSide.Away).Profiles.[slot].Id

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
                    let dt = float (SimulationClock.dt ctx.Clock)

                    let targetX, targetY = MatchMath.pitchClamp (float px + 0.4 * dir) (float py)
                    let currentX = float world.State.Ball.Position.X
                    let currentY = float world.State.Ball.Position.Y
                    let dx = targetX - currentX
                    let dy = targetY - currentY
                    let distance = sqrt (dx * dx + dy * dy)

                    let playerSpeed =
                        sqrt (
                            float frame.VelX.[slot] * float frame.VelX.[slot]
                            + float frame.VelY.[slot] * float frame.VelY.[slot]
                        )
                    // A new controller may be slightly away from the ball (for
                    // example after a restart), so approach the contact point
                    // instead of teleporting the ball onto the player.
                    let maxStep = max 0.05 (playerSpeed * dt)
                    let step = min distance maxStep

                    let nextX =
                        if distance < 0.0001 then
                            targetX
                        else
                            currentX + dx / distance * step

                    let nextY =
                        if distance < 0.0001 then
                            targetY
                        else
                            currentY + dy / distance * step

                    let nextX, nextY = MatchMath.pitchClamp nextX nextY

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Position =
                                { X = nextX * 1.0<meter>
                                  Y = nextY * 1.0<meter>
                                  Z = 0.0<meter>
                                  Vx = (nextX - currentX) / dt * 1.0<meterPerSecond>
                                  Vy = (nextY - currentY) / dt * 1.0<meterPerSecond>
                                  Vz = 0.0<meterPerSecond> }
                            PreviousPosition = None
                            Impulse = None
                            GKHoldSince = (if slot = 0 then Some tick else None) })

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
            | BallControl.Airborne -> pickupFreeBall ctx.Ctx rng world tick buffer
