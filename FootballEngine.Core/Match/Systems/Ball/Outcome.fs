namespace FootballEngine.Core.Match

/// A struck ball does not resolve at the moment of the shot: its outcome is
/// decided when it reaches the target, so defenders and the GK can still react
/// during flight. Also awards out-of-bounds restarts, so a ball leaving the
/// pitch never just vanishes.
module BallOutcome =

    let private emitSetPiece
        (ctx: SystemContext)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        (kind: SetPieceKind)
        (side: ClubSide)
        : unit =
        buffer.Events.Add
            { Tick = tick
              Data = EventData.SetPiece(kind, side) }

    /// Restart ownership follows who defends the crossed goal line: the defenders
    /// of that line concede a corner or win a goal kick.
    let private goalLineDefender (x: float<meter>) : ClubSide =
        if x > 0.0<meter> then ClubSide.Away else ClubSide.Home

    /// A goal is the real goal frame: inside the 7.32 m width and below the 2.44 m
    /// crossbar at the line — anything outside is a miss, even inches away.
    let private isInGoal (ball: BallState) : bool =
        let absY = abs (float ball.Position.Y)
        let zOk = float ball.Position.Z < 2.44
        absY < float Pitch.GoalHalfWidth && zOk

    /// Attribution must follow the real rule: if the last touch before the line was
    /// a defender's, the goal counts to that defender, not to the shooter.
    let private detectOwnGoal
        (ctx: SystemContext)
        (world: World)
        (shooterSide: ClubSide)
        (shooter: PlayerId)
        : PlayerId option * PlayerId option =
        let defSide = MatchWorld.oppOf shooterSide

        match MatchWorld.lastTouchSide ctx.Ctx world with
        | Some side when side = defSide ->
            match world.State.Ball.LastTouch with
            | Some defPid -> Some defPid, None
            | None -> Some shooter, None
        | _ -> Some shooter, None

    let private resolveShot
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        (shooter: PlayerId)
        (speed: float<meterPerSecond>)
        (xG: float)
        : Rng =
        let ball = world.State.Ball

        let shooterSide =
            MatchWorld.teamOfPlayer ctx.Ctx shooter |> Option.defaultValue ClubSide.Home

        let defSide = MatchWorld.oppOf shooterSide
        let frame = (MatchWorld.team world defSide).Frame
        let bx = ball.Position.X
        let by = ball.Position.Y

        // A defender within reach of the flight path gets one last chance to stop
        // the ball before arrival — resolving at arrival is what makes this possible.
        let blocker = MatchWorld.nearestOpponent ctx.Ctx world shooterSide bx by 3.0

        match blocker with
        | Some _ ->
            let blocked, rng' = Rng.bernoulli 0.5 rng

            if blocked then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Shot(shooter, ShotResult.Blocked, xG, speed) }

                let outForCorner, rng'' = Rng.bernoulli 0.25 rng'

                if outForCorner then
                    emitSetPiece ctx world tick buffer SetPieceKind.Corner (MatchWorld.oppOf shooterSide)
                    rng''
                else
                    let lastSide =
                        MatchWorld.lastTouchSide ctx.Ctx world |> Option.defaultValue shooterSide

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Control = BallControl.Contesting lastSide
                            Position =
                                { b.Position with
                                    Vx = -b.Position.Vx * 0.3
                                    Vy = -b.Position.Vy * 0.3
                                    Vz = 1.0<meterPerSecond> } })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.BallLoose(bx, by) }

                    rng''
            else
                MatchWorld.updateBall world (fun b ->
                    { b with
                        Trajectory = None
                        ArrivalGate = None })

                rng'
        | None ->
            let absY = abs (float by)

            if absY < float Pitch.GoalHalfWidth then
                // Inside the frame only the keeper can stop it — the shot must beat the save roll.
                let gkSlot = 0
                let gk = MatchWorld.profileOf ctx.Ctx defSide gkSlot
                let gkX = MatchWorld.posX frame gkSlot
                let gkY = MatchWorld.posY frame gkSlot
                let gkDist = MatchMath.dist gkX gkY bx by
                let attrs = float (gk.Positioning + gk.Agility + gk.Anticipation)

                let saveP = Goalkeeping.saveProbability ctx.Ctx.Config attrs speed gkDist

                let goalP =
                    MatchMath.clamp01 (
                        xG * ctx.Ctx.Config.Ball.ShotGoalXGMultiplier
                        + (attrs - 195.0) / ctx.Ctx.Config.Ball.ShotGoalGKDivisor
                    )

                let saved, rng' = Rng.bernoulli (max saveP (1.0 - goalP)) rng
                let parriedOver, rng'' = Rng.bernoulli 0.15 rng'
                let gkPid = (MatchWorld.lineup ctx.Ctx defSide).Profiles.[0].Id

                if saved && parriedOver then
                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Shot(shooter, ShotResult.OnTarget true, xG, speed) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Save(gkPid, shooter) }

                    emitSetPiece ctx world tick buffer SetPieceKind.Corner (MatchWorld.oppOf defSide)
                    rng''
                elif saved then
                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Control = BallControl.Controlled(defSide, gkPid)
                            LastTouch = Some gkPid
                            GKHoldSince = Some tick
                            Position =
                                { X = gkX
                                  Y = gkY
                                  Z = 0.0<meter>
                                  Vx = 0.0<meterPerSecond>
                                  Vy = 0.0<meterPerSecond>
                                  Vz = 0.0<meterPerSecond> } })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Shot(shooter, ShotResult.OnTarget true, xG, speed) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Save(gkPid, shooter) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Possession(defSide, gkPid) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.MomentumShift defSide }

                    world.State <-
                        { world.State with
                            Momentum = world.State.Momentum + (if defSide = ClubSide.Home then 0.12 else -0.12) }

                    rng'
                else
                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None
                            Control = BallControl.Free
                            Position =
                                { b.Position with
                                    Vx = 0.0<meterPerSecond>
                                    Vy = 0.0<meterPerSecond>
                                    Vz = 0.0<meterPerSecond> } })

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Shot(shooter, ShotResult.OnTarget false, xG, speed) }

                    let scorer, assist = detectOwnGoal ctx world shooterSide shooter

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Goal(shooterSide, scorer, assist, xG) }

                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.MomentumShift shooterSide }

                    world.State <-
                        { world.State with
                            Momentum = world.State.Momentum + (if shooterSide = ClubSide.Home then 0.35 else -0.35) }

                    rng'
            else
                // A miss near the frame edges is not dead yet: the post can still keep it in play.
                let postZone = absY < float Pitch.GoalHalfWidth + 0.5

                if postZone then
                    let post, rng' = Rng.bernoulli 0.35 rng

                    if post then
                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.Shot(shooter, ShotResult.Post, xG, speed) }

                        let lastSide = MatchWorld.lastTouchSide ctx.Ctx world |> Option.defaultValue defSide

                        MatchWorld.updateBall world (fun b ->
                            { b with
                                Trajectory = None
                                ArrivalGate = None
                                Control = BallControl.Contesting lastSide
                                Position =
                                    { b.Position with
                                        Vx = -b.Position.Vx * 0.5
                                        Vy = -b.Position.Vy * 0.5
                                        Vz = 1.5<meterPerSecond> } })

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.BallLoose(bx, by) }

                        rng'
                    else
                        MatchWorld.updateBall world (fun b ->
                            { b with
                                Trajectory = None
                                ArrivalGate = None })

                        rng'
                else
                    buffer.Events.Add
                        { Tick = tick
                          Data = EventData.Shot(shooter, ShotResult.OffTarget, xG, speed) }

                    MatchWorld.updateBall world (fun b ->
                        { b with
                            Trajectory = None
                            ArrivalGate = None })

                    rng

    /// A ball crossing a line must produce its restart immediately: without this
    /// the ball would leave the pitch and possession would silently disappear.
    let private handleOutOfBounds
        (ctx: SystemContext)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        : bool =
        let ball = world.State.Ball
        let px = ball.Position.X
        let py = ball.Position.Y

        let outX = abs (float px) > float Pitch.HalfLength
        let outY = abs (float py) > float Pitch.HalfWidth

        if not outX && not outY then
            false
        else
            let lastSide = MatchWorld.lastTouchSide ctx.Ctx world
            let attackingSide = lastSide |> Option.defaultValue ClubSide.Home

            if outX then
                let defender = goalLineDefender px

                if lastSide = Some defender then
                    // The defender of the crossed line concedes the corner — attackers get the restart.
                    emitSetPiece ctx world tick buffer SetPieceKind.Corner (MatchWorld.oppOf defender)
                else
                    emitSetPiece ctx world tick buffer SetPieceKind.GoalKick defender
            else
                emitSetPiece ctx world tick buffer SetPieceKind.ThrowIn (MatchWorld.oppOf attackingSide)

            MatchWorld.updateBall world (fun b ->
                { b with
                    Trajectory = None
                    ArrivalGate = None
                    LastTouch = None
                    StationarySince = None })

            true

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let ball = world.State.Ball
            let mutable rng = rng

            match ball.ArrivalGate, ball.Trajectory with
            | Some gate, Some(TrajectoryIntent.Struck(shooter, speed, _, xG)) when gate <= tick ->
                rng <- resolveShot ctx rng world tick buffer shooter speed xG
            | _ -> ()

            if handleOutOfBounds ctx world tick buffer then rng else rng
