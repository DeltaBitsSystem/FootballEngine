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
        match frame.SteerIntent.Kind.[slot] with
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
            frame.SteerIntent.Kind.[slot] = IntentCode.Recover
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
        match MatchWorld.sideOfPlayer ctx passer with
        | ValueSome pSide ->
            match MatchWorld.slotOf ctx pSide passer with
            | ValueSome pSlot ->
                let f = (MatchWorld.team world pSide).Frame
                let px = MatchWorld.posX f pSlot
                let py = MatchWorld.posY f pSlot
                px, py, MatchMath.dist px py rx ry * 1.0<meter>
            | ValueNone -> 10.0<meter>, 10.0<meter>, 10.0<meter>
        | ValueNone -> 10.0<meter>, 10.0<meter>, 10.0<meter>

    let resolve (ctx: SystemContext) (rng: Rng) (world: World) (tick: int<subtick>) (buffer: EventBuffer) : Rng =
        let ball = world.State.Ball
        let mutable contact: ContactRecord option = None

        for index = buffer.Events.Count - 1 downto 0 do
            if contact.IsNone then
                match buffer.Events[index].Data with
                | EventData.PhysicalContact record when record.BallTouched -> contact <- Some record
                | _ -> ()

        let emit data =
            let event = { Tick = tick; Data = data }
            buffer.Events.Add event

            if world.Observation.Enabled then
                ball.Impulse
                |> Option.iter (fun impulse -> world.Observation.Associate(event, ActionCause impulse.ActionId))

        let resolveTrajectory () =
            MatchWorld.updateBall world (fun current ->
                { current with
                    Trajectory = None
                    ArrivalGate = None
                    PendingStrike = None
                    Impulse = None })

        match contact, ball.Trajectory with
        | Some record, Some(TrajectoryIntent.Aimed(passer, receiver, quality)) ->
            let origin =
                ball.Impulse
                |> Option.map _.Origin
                |> Option.defaultValue
                    { X = ball.Position.X
                      Y = ball.Position.Y
                      Z = ball.Position.Z }

            let distance =
                MatchMath.dist origin.X origin.Y ball.Position.X ball.Position.Y * 1.0<meter>

            let controlled =
                record.ControlAfter.Kind = ContactControlKind.ReceivingBall
                && record.ControlAfter.Player = Some record.Actor

            // A completed pass is one a teammate controls (the Opta definition the calibration bands use); a
            // teammate who takes a ball meant for his neighbour keeps possession just the same.
            let completed =
                controlled
                && (record.Actor = receiver
                    || (MatchWorld.sideOfPlayer ctx.Ctx record.Actor).IsSome
                       && MatchWorld.sideOfPlayer ctx.Ctx record.Actor = MatchWorld.sideOfPlayer ctx.Ctx passer)

            let firstEvent = buffer.Events.Count

            emit (
                EventData.Pass(
                    passer,
                    Some receiver,
                    distance,
                    quality,
                    completed,
                    origin.X,
                    origin.Y,
                    ball.Position.X,
                    ball.Position.Y
                )
            )

            match MatchWorld.sideOfPlayer ctx.Ctx record.Actor, MatchWorld.sideOfPlayer ctx.Ctx passer with
            | ValueSome actorSide, ValueSome passerSide when actorSide <> passerSide && controlled ->
                emit (EventData.Interception(record.Actor, passer))
            | _ -> ()

            resolveTrajectory ()

            if world.Observation.Enabled then
                match ball.Impulse with
                | Some impulse ->
                    for index = firstEvent to buffer.Events.Count - 1 do
                        world.Observation.Associate(buffer.Events[index], ActionCause impulse.ActionId)
                | None -> ()

            rng
        | Some record, Some(TrajectoryIntent.Struck(shooter, speed, _, xG)) ->
            let controlled =
                record.ControlAfter.Kind = ContactControlKind.ReceivingBall
                && record.ControlAfter.Player = Some record.Actor

            let keeper =
                match MatchWorld.sideOfPlayer ctx.Ctx record.Actor with
                | ValueSome side ->
                    MatchWorld.slotOf ctx.Ctx side record.Actor
                    |> ValueOption.exists (fun slot -> (MatchWorld.profileOf ctx.Ctx side slot).IsGK)
                | ValueNone -> false

            // A parry is a save even though the ball stays loose; what happens to the rebound is a new phase.
            if keeper && not controlled && record.SurfaceA = ContactSurface.ContactHand then
                emit (EventData.Shot(shooter, ShotResult.OnTarget true, xG, speed, ball.Position.X, ball.Position.Y))
                emit (EventData.Save(record.Actor, shooter))
                resolveTrajectory ()
            elif controlled then
                if keeper then
                    emit (
                        EventData.Shot(shooter, ShotResult.OnTarget true, xG, speed, ball.Position.X, ball.Position.Y)
                    )

                    emit (EventData.Save(record.Actor, shooter))
                else
                    emit (EventData.Shot(shooter, ShotResult.Blocked, xG, speed, ball.Position.X, ball.Position.Y))

                resolveTrajectory ()

            // Loose deflections keep the original shot context so the altered path,
            // not the pre-contact label, decides a later goal-line crossing.
            rng
        | Some _, _ ->
            resolveTrajectory ()
            rng
        | None, Some(TrajectoryIntent.Aimed(passer, receiver, quality)) when
            (ball.ArrivalGate |> Option.exists (fun gate -> gate <= tick))
            && (ball.StationarySince.IsSome
                || (MatchMath.speedOf ball.Position.Vx ball.Position.Vy
                    <= float ctx.Ctx.Config.Ball.StopSpeedCutoff
                    && abs (float ball.Position.Vz) <= float ctx.Ctx.Config.Ball.StopSpeedCutoff))
            ->
            // Arrival estimates ignore changing drag and receiver motion; only a stopped flight can expire without a touch.
            let origin =
                ball.Impulse
                |> Option.map _.Origin
                |> Option.defaultValue
                    { X = ball.Position.X
                      Y = ball.Position.Y
                      Z = ball.Position.Z }

            let distance =
                MatchMath.dist origin.X origin.Y ball.Position.X ball.Position.Y * 1.0<meter>

            emit (
                EventData.Pass(
                    passer,
                    Some receiver,
                    distance,
                    quality,
                    false,
                    origin.X,
                    origin.Y,
                    ball.Position.X,
                    ball.Position.Y
                )
            )

            resolveTrajectory ()
            emit (EventData.BallLoose(ball.Position.X, ball.Position.Y))
            rng
        | None, Some TrajectoryIntent.Cleared when ball.ArrivalGate |> Option.exists (fun gate -> gate <= tick) ->
            resolveTrajectory ()
            emit (EventData.BallLoose(ball.Position.X, ball.Position.Y))
            rng
        | _ -> rng

    let step: SystemStep =
        fun ctx rng world buffer -> resolve ctx rng world world.State.Tick buffer
