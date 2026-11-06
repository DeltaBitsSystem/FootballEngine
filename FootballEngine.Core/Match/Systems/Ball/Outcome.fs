namespace FootballEngine.Core.Match

/// A shot remains unresolved until its actual path is saved, scored, or leaves play.
module BallOutcome =

    let private emit (world: World) tick (buffer: EventBuffer) data =
        let event = { Tick = tick; Data = data }
        buffer.Events.Add event

        if world.Observation.Enabled then
            world.State.Ball.Impulse
            |> Option.iter (fun impulse -> world.Observation.Associate(event, ActionCause impulse.ActionId))

    let private emitSetPiece world tick buffer kind side =
        emit world tick buffer (EventData.SetPiece(kind, side))

    let private goalLineDefender (x: float<meter>) : ClubSide =
        if x > 0.0<meter> then ClubSide.Away else ClubSide.Home

    let private isInGoal (position: Vec3) : bool = BallPhysics.withinGoalFrame position

    let private isGoalkeeper (ctx: MatchContext) player =
        match MatchWorld.teamOfPlayer ctx player with
        | Some side ->
            MatchWorld.slotOf ctx side player
            |> Option.exists (fun slot -> (MatchWorld.profileOf ctx side slot).IsGK)
        | None -> false

    let private detectOwnGoal (ctx: SystemContext) (world: World) shooter scoringSide =
        let defenderSide = MatchWorld.oppOf scoringSide

        match MatchWorld.lastTouchSide ctx.Ctx world with
        | Some side when side = defenderSide ->
            match world.State.Ball.LastTouch with
            | Some defender -> Some defender, None
            | None -> Some shooter, None
        | _ -> Some shooter, None

    let private shotExitResult (ctx: SystemContext) (world: World) shooter defendingSide =
        match world.State.Ball.LastTouch with
        | Some player when MatchWorld.teamOfPlayer ctx.Ctx player = Some defendingSide && isGoalkeeper ctx.Ctx player ->
            ShotResult.OnTarget true, Some player
        | Some _ when MatchWorld.lastTouchSide ctx.Ctx world = Some defendingSide -> ShotResult.Blocked, None
        | _ -> ShotResult.OffTarget, None

    let private emitShotExit ctx world tick buffer shooter speed xG x y defendingSide =
        let result, keeper = shotExitResult ctx world shooter defendingSide
        emit world tick buffer (EventData.Shot(shooter, result, xG, speed, x, y))
        keeper |> Option.iter (fun gk -> emit world tick buffer (EventData.Save(gk, shooter)))

    let private finishBall (world: World) stopVelocity =
        MatchWorld.updateBall world (fun ball ->
            { ball with
                Control = BallControl.Free
                Trajectory = None
                ArrivalGate = None
                PendingStrike = None
                Impulse = None
                StationarySince = None
                GKHoldSince = None
                Position =
                    if stopVelocity then
                        { ball.Position with
                            Vx = 0.0<meterPerSecond>
                            Vy = 0.0<meterPerSecond>
                            Vz = 0.0<meterPerSecond> }
                    else
                        ball.Position })

    let private resolveShot
        (ctx: SystemContext)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        (shooter: PlayerId)
        (speed: float<meterPerSecond>)
        (xG: float)
        (crossing: GoalCrossing)
        : unit =
        let scoringSide = crossing.Side
        let defenderSide = MatchWorld.oppOf scoringSide
        let x, y = crossing.Position.X, crossing.Position.Y

        if isInGoal crossing.Position then
            emit world tick buffer (EventData.Shot(shooter, ShotResult.OnTarget false, xG, speed, x, y))
            let scorer, assist = detectOwnGoal ctx world shooter scoringSide
            emit world tick buffer (EventData.Goal(scoringSide, scorer, assist, xG))
            emit world tick buffer (EventData.MomentumShift scoringSide)

            world.State <-
                { world.State with
                    Momentum = world.State.Momentum + (if scoringSide = ClubSide.Home then 0.35 else -0.35) }

            finishBall world true
        else
            emitShotExit ctx world tick buffer shooter speed xG x y defenderSide

            if MatchWorld.lastTouchSide ctx.Ctx world = Some defenderSide then
                emitSetPiece world tick buffer SetPieceKind.Corner scoringSide
            else
                emitSetPiece world tick buffer SetPieceKind.GoalKick defenderSide

            finishBall world true

    /// A line crossing ends the live path; the last physical touch owns its restart.
    let private handleOutOfBounds
        (ctx: SystemContext)
        (world: World)
        (tick: int<subtick>)
        (buffer: EventBuffer)
        : bool =
        let ball = world.State.Ball
        let px, py = ball.Position.X, ball.Position.Y
        let outX = abs (float px) > float Pitch.HalfLength
        let outY = abs (float py) > float Pitch.HalfWidth

        if not outX && not outY then
            false
        else
            let lastSide = MatchWorld.lastTouchSide ctx.Ctx world
            let attackingSide = lastSide |> Option.defaultValue ClubSide.Home

            match ball.Trajectory with
            | Some(TrajectoryIntent.Struck(shooter, speed, _, xG)) ->
                let shooterSide = MatchWorld.teamOfPlayer ctx.Ctx shooter |> Option.defaultValue ClubSide.Home
                emitShotExit ctx world tick buffer shooter speed xG px py (MatchWorld.oppOf shooterSide)
            | _ -> ()

            if outX then
                let defender = goalLineDefender px

                if lastSide = Some defender then
                    emitSetPiece world tick buffer SetPieceKind.Corner (MatchWorld.oppOf defender)
                else
                    emitSetPiece world tick buffer SetPieceKind.GoalKick defender
            else
                emitSetPiece world tick buffer SetPieceKind.ThrowIn (MatchWorld.oppOf attackingSide)

            MatchWorld.updateBall world (fun current ->
                { current with
                    Control = BallControl.Free
                    Trajectory = None
                    ArrivalGate = None
                    PendingStrike = None
                    LastTouch = None
                    StationarySince = None
                    Impulse = None
                    Position =
                        { current.Position with
                            Vx = 0.0<meterPerSecond>
                            Vy = 0.0<meterPerSecond>
                            Vz = 0.0<meterPerSecond> } })

            true

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let ball = world.State.Ball
            let firstEvent = buffer.Events.Count
            let mutable shotCrossed = false

            match ball.Trajectory with
            | Some(TrajectoryIntent.Struck(shooter, speed, _, xG)) ->
                let dt = float (SimulationClock.dt ctx.Clock)

                let inferredPrevious =
                    { ball.Position with
                        X = (float ball.Position.X - float ball.Position.Vx * dt) * 1.0<meter>
                        Y = (float ball.Position.Y - float ball.Position.Vy * dt) * 1.0<meter>
                        Z = max 0.0 (float ball.Position.Z - float ball.Position.Vz * dt) * 1.0<meter> }

                let previous = ball.PreviousPosition |> ValueOption.defaultValue inferredPrevious

                match BallPhysics.goalCrossing previous ball.Position with
                | Some crossing ->
                    shotCrossed <- true
                    resolveShot ctx world tick buffer shooter speed xG crossing
                | None -> ()
            | _ -> ()

            let exited = if shotCrossed then true else handleOutOfBounds ctx world tick buffer

            if not shotCrossed && not exited then
                match world.State.Ball.Trajectory, world.State.Ball.StationarySince with
                | Some(TrajectoryIntent.Struck(shooter, speed, _, xG)), Some _ ->
                    let shooterSide = MatchWorld.teamOfPlayer ctx.Ctx shooter |> Option.defaultValue ClubSide.Home
                    emitShotExit ctx world tick buffer shooter speed xG world.State.Ball.Position.X world.State.Ball.Position.Y (MatchWorld.oppOf shooterSide)
                    finishBall world false
                | _ -> ()

            if world.Observation.Enabled then
                match ball.Impulse with
                | Some impulse ->
                    for index = firstEvent to buffer.Events.Count - 1 do
                        world.Observation.AssociateDefault(buffer.Events[index], ActionCause impulse.ActionId)
                | None -> ()

            rng
