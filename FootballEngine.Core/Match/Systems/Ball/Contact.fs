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
        | Contested of attackerSide: ClubSide * aSlot: int * aPid: PlayerId * dSlot: int * dPid: PlayerId
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

    /// The race is won by whoever REACHES first, not whoever is nearest: a close
    /// slow player must not beat a faster one farther away. Distance only breaks ties.
    let private bestCandidate
        (ctx: MatchContext)
        (world: World)
        (x: float<meter>)
        (y: float<meter>)
        : (ClubSide * int * PlayerId * float) option =
        let mutable best: (ClubSide * int * PlayerId * float * float) option = None

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world side
            let frame = team.Frame

            for slot in MatchWorld.activeSlots frame do
                let d = MatchMath.dist x y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                if d < 20.0 then
                    let t = timeToBall ctx world side slot x y

                    match best with
                    | Some(_, _, _, bt, bd) when bt < t || (bt = t && bd <= d) -> ()
                    | _ ->
                        let pid = (MatchWorld.lineup ctx side).Profiles.[slot].Id
                        best <- Some(side, slot, pid, t, d)

        best |> Option.map (fun (s, slot, pid, t, _) -> s, slot, pid, t)

    let private bestContactCandidate
        (ctx: MatchContext)
        (world: World)
        (x: float<meter>)
        (y: float<meter>)
        (excluded: PlayerId option)
        : (ClubSide * int * PlayerId * float) option =
        let radius = max 2.5 (float ctx.Config.Intercept.ControlRadius * 2.0)
        let mutable best: (ClubSide * int * PlayerId * float * float) option = None

        let contactTime side slot distance =
            let p = MatchWorld.profileOf ctx side slot
            let frame = (MatchWorld.team world side).Frame
            let vmax = float ctx.Config.Movement.SprintSpeed * (0.5 + 0.006 * float p.Pace)
            let conditionFactor = 0.4 + 0.6 * float frame.Condition.[slot]
            let intentFactor = intentFactorOf frame slot
            distance / (vmax * conditionFactor * intentFactor)

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world side

            for slot in MatchWorld.activeSlots team.Frame do
                let distance =
                    MatchMath.dist x y (MatchWorld.posX team.Frame slot) (MatchWorld.posY team.Frame slot)

                let pid = (MatchWorld.lineup ctx side).Profiles.[slot].Id

                if distance <= radius && Some pid <> excluded then
                    let time = contactTime side slot distance

                    match best with
                    | Some(_, _, _, bestTime, bestDistance) when
                        bestTime < time || (bestTime = time && bestDistance <= distance)
                        ->
                        ()
                    | _ -> best <- Some(side, slot, pid, time, distance)

        best |> Option.map (fun (s, slot, pid, time, _) -> s, slot, pid, time)

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
                bestContactCandidate ctx.Ctx world x y (ball.Impulse |> Option.map (fun impulse -> impulse.ActorId))
            let restartContact =
                match ball.Impulse with
                | Some
                    { RestartId = Some restartId
                      ActorId = actor
                      StrikeKind = StrikeKind.Restart kind
                      ExecutionContext = ExecutionContext.SetPiece _ } ->
                    Some(restartId, actor, kind)
                | _ -> None

            if contact.IsSome || restartContact.IsSome then
                MatchWorld.updateBall world (fun b ->
                    { b with
                        Trajectory = None
                        ArrivalGate = None
                        Impulse = None
                        StationarySince = None })

                match restartContact with
                | Some(restartId, actor, kind) ->
                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.RestartContact(restartId, actor, kind) }
                | None ->
                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.BallLoose(x, y) }

            rng
        | Some gate, Some(TrajectoryIntent.Struck _) when gate <= tick ->
            match ball.Impulse with
            | Some
                { RestartId = Some restartId
                  ActorId = actor
                  StrikeKind = StrikeKind.Restart kind
                  ExecutionContext = ExecutionContext.SetPiece _ } ->
                MatchWorld.updateBall world (fun b ->
                    { b with
                        Trajectory = None
                        ArrivalGate = None
                        Impulse = None })

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.RestartContact(restartId, actor, kind) }
            | _ -> ()

            rng
        | Some gate, Some(TrajectoryIntent.Aimed(passer, receiver, quality)) when gate <= tick ->
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
                let best = bestContactCandidate ctx.Ctx world rx ry (Some passer)

                let receiverWins =
                    match best with
                    | Some(bs, bslot, _, _) when bs = receiverSide && bslot = rSlot -> true
                    | _ -> false

                let interceptingPlayer =
                    match best with
                    | Some(bs, bslot, bpid, _) -> Some(bs, bslot, bpid)
                    | None -> None

                let arrival =
                    match interceptingPlayer with
                    | Some(side, slot, pid) when not (side = receiverSide && slot = rSlot) ->
                        Arrival.Interception(side, slot, pid, passer)
                    | None when receiverWins -> Arrival.CleanReception(receiverSide, rSlot, receiver)
                    | None ->
                        match interceptingPlayer with
                        | Some(bs, bslot, bpid) when not (bs = receiverSide && bslot = rSlot) ->
                            Arrival.Contested(receiverSide, rSlot, receiver, bslot, bpid)
                        | _ -> Arrival.NoContact
                    | Some _ when receiverWins -> Arrival.CleanReception(receiverSide, rSlot, receiver)
                    | Some _ -> Arrival.NoContact

                let restartImpulse = ball.Impulse
                let restartContact =
                    match restartImpulse with
                    | Some
                        { RestartId = Some restartId
                          ActorId = actor
                          StrikeKind = StrikeKind.Restart kind
                          ExecutionContext = ExecutionContext.SetPiece _ } ->
                        Some(restartId, actor, kind)
                    | _ -> None

                let resolved = arrival <> Arrival.NoContact || restartContact.IsSome

                if resolved then
                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Impulse = None })

                    match restartContact with
                    | Some(restartId, actor, kind) ->
                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.RestartContact(restartId, actor, kind) }
                    | _ ->
                        match restartImpulse with
                        | Some impulse ->
                            match impulse.RestartId, impulse.StrikeKind, impulse.ExecutionContext with
                            | Some restartId, StrikeKind.Pass, ExecutionContext.SetPiece kind
                            | Some restartId, StrikeKind.Cross, ExecutionContext.SetPiece kind
                            | Some restartId, StrikeKind.Shot, ExecutionContext.SetPiece kind
                            | Some restartId, StrikeKind.Clearance, ExecutionContext.SetPiece kind
                            | Some restartId, StrikeKind.Restart kind, ExecutionContext.SetPiece _ ->
                                buffer.Events.Add
                                    { Tick = tick
                                      Data = EventData.RestartContact(restartId, impulse.ActorId, kind) }
                            | _ -> ()
                        | None -> ()

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

                    let px, py, passDist = passerOriginAndDist ctx.Ctx world passer rx ry

                    if ok then
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

                    rng'
                | Arrival.Interception(side, slot, pid, passer) ->
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

                    rng
                | Arrival.Contested(aSide, aSlot, aPid, dSlot, dPid) ->
                    let a = MatchWorld.profileOf ctx.Ctx aSide aSlot
                    let d = MatchWorld.profileOf ctx.Ctx (MatchWorld.oppOf aSide) dSlot

                    let px, py, passDist = passerOriginAndDist ctx.Ctx world passer rx ry

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Pass(passer, Some receiver, passDist, quality, false, px, py, rx, ry) }

                    let attackerWin =
                        MatchMath.clamp01 (
                            0.52
                            + (float (a.Anticipation + a.Dribbling) - float (d.Anticipation + d.Tackling))
                              / 600.0
                        )

                    let ok, rng' = Rng.bernoulli attackerWin rng

                    let winSide, winSlot, winPid, losePid =
                        if ok then
                            aSide, aSlot, aPid, dPid
                        else
                            MatchWorld.oppOf aSide, dSlot, dPid, aPid

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Impulse = None })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Possession(winSide, winPid) }

                    rng'
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
