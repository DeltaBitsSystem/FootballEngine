namespace FootballEngine.Core.Match

/// Pass reception cannot be a binary "receiver gets it": a ball arriving is
/// contested by every active player near the arrival point, and the first to
/// actually REACH it wins. Arrival ranking is a vectorized time-to-ball run
/// (Pace/Condition/Intent), not raw distance, so a slow player standing close
/// cannot beat a fast one arriving first.
module BallContact =

    type Arrival =
        | CleanReception of side: ClubSide * slot: int * pid: PlayerId
        | Interception of side: ClubSide * slot: int * pid: PlayerId * passer: PlayerId
        | NoContact

    let private intentFactorOf (frame: TeamFrame) (slot: int) : float =
        match frame.Intent.Kind.[slot] with
        | IntentCode.Press
        | IntentCode.Recover
        | IntentCode.BallCarry
        | IntentCode.ExecuteRun -> 1.0
        | IntentCode.Support -> 0.75
        | _ -> 0.55

    /// Time-to-ball must be a pure function: the same race must resolve
    /// identically whether it happens here or in the loose-ball contest, and the
    /// match is deterministic (no Rng, no mutation). Lower is better.
    let timeToBall
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (x: float<meter>)
        (y: float<meter>)
        : float =
        let cfg = ctx.Config
        let p = MatchWorld.profileOf ctx side slot
        let frame = (MatchWorld.team world side).Frame
        let d = MatchMath.dist x y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)
        let vmax = float cfg.Movement.SprintSpeed * (0.5 + 0.006 * float p.Pace)
        let conditionFactor = 0.4 + 0.6 * float frame.Condition.[slot]
        let intentFactor = intentFactorOf frame slot

        let recovering =
            frame.Intent.Kind.[slot] = IntentCode.Recover
            || (let vx = float frame.VelX.[slot]
                let vy = float frame.VelY.[slot]
                let dx = float x - float (MatchWorld.posX frame slot)
                let dy = float y - float (MatchWorld.posY frame slot)
                vx * dx + vy * dy > 0.0)

        let recoveryBoost = if recovering then cfg.Ball.InterceptionIntentBoost else 1.0
        d / (vmax * conditionFactor * intentFactor * recoveryBoost)

    let tryReachableContact
        (ctx: MatchContext)
        (world: World)
        (x: float<meter>)
        (y: float<meter>)
        (excluded: PlayerId option)
        : struct (ClubSide * int * PlayerId * float) voption =
        let radius = float ctx.Config.Ball.FootReach

        let mutable best: struct (ClubSide * int * PlayerId * float * float) voption =
            ValueNone

        for sideIndex = 0 to 1 do
            let side = if sideIndex = 0 then ClubSide.Home else ClubSide.Away
            let team = MatchWorld.team world side
            let frame = team.Frame
            let profiles = (MatchWorld.lineup ctx side).Profiles

            for slot = 0 to frame.PosX.Length - 1 do
                let dx = float x - float frame.PosX[slot]
                let dy = float y - float frame.PosY[slot]
                let squared = dx * dx + dy * dy
                let pid = profiles[slot].Id

                let eligible =
                    match excluded with
                    | Some actor -> actor <> pid
                    | None -> true

                if
                    MatchWorld.isActive frame slot
                    && eligible
                    && squared <= radius * radius
                    && abs (float world.State.Ball.Position.Z - float frame.PosZ[slot])
                       <= float ctx.Config.Ball.FootContactHeight
                then
                    let distance = sqrt squared

                    let vmax =
                        float ctx.Config.Movement.SprintSpeed
                        * (0.5 + 0.006 * float profiles[slot].Pace)

                    let conditionFactor = 0.4 + 0.6 * float frame.Condition[slot]
                    let time = distance / (vmax * conditionFactor * intentFactorOf frame slot)

                    match best with
                    | ValueSome struct (_, _, _, bestTime, bestDistance) when
                        bestTime < time || (bestTime = time && bestDistance <= distance)
                        ->
                        ()
                    | _ -> best <- ValueSome struct (side, slot, pid, time, distance)

        match best with
        | ValueSome struct (side, slot, pid, time, _) -> ValueSome struct (side, slot, pid, time)
        | ValueNone -> ValueNone

    /// Passer's position at resolution time, plus the measured distance to the
    /// arrival point: the scouting layer needs the origin (corridor/directness),
    /// and the fallback keeps the event well-formed even if the passer is gone.
    let private passerOriginAndDist
        (ctx: MatchContext)
        (world: World)
        (passer: PlayerId)
        (rx: float<meter>)
        (ry: float<meter>)
        : float<meter> * float<meter> * float<meter> =
        match MatchWorld.teamOfPlayer ctx passer with
        | Some pSide ->
            match MatchWorld.slotOf ctx pSide passer with
            | Some pSlot ->
                let f = (MatchWorld.team world pSide).Frame
                let px = MatchWorld.posX f pSlot
                let py = MatchWorld.posY f pSlot
                px, py, MatchMath.dist px py rx ry * 1.0<meter>
            | None -> 10.0<meter>, 10.0<meter>, 10.0<meter>
        | None -> 10.0<meter>, 10.0<meter>, 10.0<meter>

    let resolve (ctx: SystemContext) (rng: Rng) (world: World) (tick: int<subtick>) (buffer: EventBuffer) : Rng =
        let ball = world.State.Ball
        let cfg = ctx.Ctx.Config

        let x = ball.Position.X
        let y = ball.Position.Y

        match ball.ArrivalGate, ball.Trajectory with
        | Some gate, Some(TrajectoryIntent.Cleared) when gate <= tick ->
            let contact =
                tryReachableContact ctx.Ctx world x y (ball.Impulse |> Option.map (fun impulse -> impulse.ActorId))

            if contact.IsSome then
                MatchWorld.updateBall world (fun b ->
                    { b with
                        Trajectory = None
                        ArrivalGate = None
                        Impulse = None
                        StationarySince = None })

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.BallLoose(x, y) }

            rng
        | _, Some(TrajectoryIntent.Aimed(passer, receiver, quality)) ->
            let rx = ball.Position.X
            let ry = ball.Position.Y

            let receiverSide =
                MatchWorld.teamOfPlayer ctx.Ctx receiver |> Option.defaultValue ClubSide.Home

            let rSlot = MatchWorld.slotOf ctx.Ctx receiverSide receiver

            match rSlot with
            | None ->
                MatchWorld.updateBall world (fun b ->
                    { b with
                        Trajectory = None
                        ArrivalGate = None })

                rng
            | Some rSlot ->
                let best = tryReachableContact ctx.Ctx world rx ry (Some passer)

                let arrival =
                    match best with
                    | ValueSome struct (side, slot, pid, _) when side = receiverSide && slot = rSlot ->
                        Arrival.CleanReception(side, slot, pid)
                    | ValueSome struct (side, slot, pid, _) -> Arrival.Interception(side, slot, pid, passer)
                    | ValueNone -> Arrival.NoContact

                let restartImpulse = ball.Impulse
                let resolved = arrival <> Arrival.NoContact

                if resolved then
                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Impulse = None })

                let firstResolutionEvent = buffer.Events.Count

                match arrival with
                | Arrival.CleanReception(side, slot, pid) ->
                    let p = MatchWorld.profileOf ctx.Ctx side slot
                    let space = MatchWorld.opponentsNear ctx.Ctx world side rx ry 3.5 |> float

                    let receptionP =
                        MatchMath.clamp01 (
                            cfg.Ball.ReceptionBase
                            + float quality * 0.3
                            + (float (p.Passing + p.Anticipation + p.Composure)) / 900.0
                            - space * cfg.Ball.ReceptionSpacePenalty
                        )

                    let ok, rng' = Rng.bernoulli receptionP rng

                    let resolutionCause =
                        if world.Observation.Enabled then
                            let action = restartImpulse |> Option.map _.ActionId

                            let cause =
                                match action with
                                | Some id -> ValueSome(ActionCause id)
                                | None -> ValueNone

                            world.Observation.Record(
                                tick,
                                cause,
                                ProbabilityResolved(Reception, action, receptionP, ok)
                            )
                            |> ValueOption.map ResolutionCause
                        else
                            ValueNone

                    let px, py, passDist = passerOriginAndDist ctx.Ctx world passer rx ry

                    if ok then
                        BallFootContact.receive ctx world buffer side slot

                        MatchWorld.updateBall world (fun b ->
                            { b with
                                Trajectory = None
                                ArrivalGate = None
                                Impulse = None })

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.Possession(side, pid) }

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.Pass(passer, Some pid, passDist, quality, true, px, py, rx, ry) }
                    else
                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.Pass(passer, Some pid, passDist, quality, false, px, py, rx, ry) }

                        MatchWorld.updateBall world (fun b ->
                            { b with
                                Trajectory = None
                                ArrivalGate = None
                                Impulse = None })

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.BallLoose(rx, ry) }

                    if world.Observation.Enabled then
                        match resolutionCause with
                        | ValueSome cause ->
                            for index = firstResolutionEvent to buffer.Events.Count - 1 do
                                world.Observation.Associate(buffer.Events[index], cause)
                        | ValueNone -> ()

                    rng'
                | Arrival.Interception(side, slot, pid, passer) ->
                    BallFootContact.receive ctx world buffer side slot

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Impulse = None })

                    let px, py, passDist = passerOriginAndDist ctx.Ctx world passer rx ry

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Pass(passer, Some receiver, passDist, quality, false, px, py, rx, ry) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Interception(pid, passer) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Possession(side, pid) }

                    if world.Observation.Enabled then
                        match restartImpulse with
                        | Some impulse ->
                            for index = firstResolutionEvent to buffer.Events.Count - 1 do
                                world.Observation.Associate(buffer.Events[index], ActionCause impulse.ActionId)
                        | None -> ()

                    rng
                | Arrival.NoContact ->
                    if ball.StationarySince.IsSome then
                        MatchWorld.updateBall world (fun b ->
                            { b with
                                Control = BallControl.Free
                                Trajectory = None
                                ArrivalGate = None
                                Impulse = None })

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.BallLoose(ball.Position.X, ball.Position.Y) }

                    rng
        | _ -> rng

    let step: SystemStep =
        fun ctx rng world buffer -> resolve ctx rng world world.State.Tick buffer
