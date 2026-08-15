namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.SimStateOps
open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module OutcomeResolver =

    type BallOutcome =
        | PossessionGained of ClubSide * Player * MatchEvent list
        | BallLoose of MatchEvent list
        | BallContested of ClubSide
        | GoalScored of ClubSide * PlayerId option
        | SetPieceAwarded of MatchFlow
        | BallInFlight
        | NoChange

    let private isGoalLineCrossed (x: float<meter>) (dir: AttackDir) : bool =
        match dir with
        | LeftToRight -> x >= PitchLength
        | RightToLeft -> x <= 0.0<meter>

    let private isInGoal (x: float<meter>) (y: float<meter>) (z: float<meter>) (dir: AttackDir) : bool =
        isGoalLineCrossed x dir
        && y >= PostNearY
        && y <= PostFarY
        && z >= 0.0<meter>
        && z <= CrossbarHeight



    let private clamp (v: float) (min: float) (max: float) =
        System.Math.Max(min, System.Math.Min(max, v))

    let private goalCenterX (dir: AttackDir) : float<meter> =
        match dir with
        | LeftToRight -> PitchLength
        | RightToLeft -> 0.0<meter>

    let resolve
        (state: SimState)
        (contact: ContactResolver.Contact)
        (ball: BallPhysicsState)
        (subTick: int)
        (attDir: AttackDir)
        (defDir: AttackDir)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        (ctx: MatchContext)
        (clock: SimulationClock)
        : BallOutcome * BallPhysicsState =
        match contact with
        | ContactResolver.OutOfBounds side ->
            let lastTouchClub =
                ball.LastTouchBy
                |> Option.bind (fun pid ->
                    if homeRoster.Players |> Array.exists (fun p -> p.Id = pid) then
                        Some HomeClub
                    elif awayRoster.Players |> Array.exists (fun p -> p.Id = pid) then
                        Some AwayClub
                    else
                        None)

            match side with
            | ContactResolver.Top
            | ContactResolver.Bottom ->
                match lastTouchClub with
                | Some club ->
                    SetPieceAwarded(
                        MatchFlow.RestartDelay
                            { Kind = SetPieceKind.ThrowIn
                              Team = ClubSide.flip club
                              Cause = AfterBallOut
                              RemainingTicks = TickDelay.delayFrom clock ctx.Config.Timing.ThrowInDelay }
                    ),
                    ball
                | None -> BallLoose [], ball
            | ContactResolver.Left
            | ContactResolver.Right ->
                let inGoal =
                    isInGoal
                        ball.Position.X
                        ball.Position.Y
                        ball.Position.Z
                        (if side = ContactResolver.Left then
                             RightToLeft
                         else
                             LeftToRight)

                if inGoal then
                    match lastTouchClub with
                    | Some sc -> GoalScored(sc, ball.LastTouchBy), { ball with Control = Free }
                    | None -> BallLoose [], ball
                else
                    let (attackingSide, defendingSide) =
                        if side = ContactResolver.Left then
                            HomeClub, AwayClub
                        else
                            AwayClub, HomeClub

                    match lastTouchClub with
                    | Some club when club = attackingSide ->
                        SetPieceAwarded(
                            MatchFlow.RestartDelay
                                { Kind = SetPieceKind.Corner
                                  Team = defendingSide
                                  Cause = AfterBallOut
                                  RemainingTicks = TickDelay.delayFrom clock ctx.Config.Timing.CornerDelay }
                        ),
                        { ball with Control = Free }
                    | _ ->
                        SetPieceAwarded(
                            MatchFlow.RestartDelay
                                { Kind = SetPieceKind.GoalKick
                                  Team = attackingSide
                                  Cause = AfterBallOut
                                  RemainingTicks = TickDelay.delayFrom clock ctx.Config.Timing.GoalKickDelay }
                        ),
                        { ball with Control = Free }

        | ContactResolver.IntendedReceiver(player, club) ->
            match ball.Trajectory with
            | Some traj ->
                match traj.Intent with
                | Aimed(passerId, targetId, quality, RegularPass)
                | Aimed(passerId, targetId, quality, AimedKind.LongBall)
                | Aimed(passerId, targetId, quality, AimedKind.Cross) ->
                    let isLongBall =
                        match traj.Intent with
                        | Aimed(_, _, _, AimedKind.LongBall) -> true
                        | _ -> false

                    let isCross =
                        match traj.Intent with
                        | Aimed(_, _, _, AimedKind.Cross) -> true
                        | _ -> false

                    let attClubId =
                        if homeRoster.Players |> Array.exists (fun p -> p.Id = passerId) then
                            ctx.Home.Id
                        else
                            ctx.Away.Id

                    let controlMult = if isLongBall then 0.8 else 1.0
                    let interceptCap = if isLongBall then 0.9 else 0.95

                    let passEvent success =
                        if isLongBall then
                            [ createEvent (subTick * 1<subtick>) passerId attClubId (MatchEventType.LongBall success) ]
                        elif isCross then
                            [ createEvent (subTick * 1<subtick>) passerId attClubId (PassCompleted(passerId, targetId)) ]
                        else
                            [ createEvent (subTick * 1<subtick>) passerId attClubId (PassCompleted(passerId, targetId)) ]

                    let orCfg = ctx.Config.OutfieldReception

                    let controlProb =
                        orCfg.ReceptionBaseProbability
                        + orCfg.BallControlWeight * normaliseAttr player.Technical.BallControl
                        + orCfg.PassQualityWeight * quality
                        - orCfg.BallSpeedPenalty
                          * float (sqrt (ball.Position.Vx * ball.Position.Vx + ball.Position.Vy * ball.Position.Vy))

                    let controlProbClamped = clamp controlProb 0.0 orCfg.ReceptionMaxProbability

                    let withCtrl (e: MatchEvent) =
                        { e with
                            Context =
                                { e.Context with
                                    ControlProb = Some controlProbClamped } }

                    if bernoulli controlProbClamped then
                        PossessionGained(club, player, passEvent true |> List.map withCtrl),
                        { ball with
                            Control = Controlled(club, player.Id)
                            LastTouchBy = Some player.Id
                            Trajectory = None }
                    else
                        BallLoose(passEvent false |> List.map withCtrl),
                        { ball with
                            Control = Free
                            LastTouchBy = Some player.Id
                            Trajectory = None }
                | Struck(shooterId, quality, shotSpeed, shotAngle, xg) ->
                    let shootingDir = attDir

                    let shooterClub =
                        if homeRoster.Players |> Array.exists (fun p -> p.Id = shooterId) then
                            HomeClub
                        else
                            AwayClub

                    if shooterClub = HomeClub then
                        state.HomeXG <- state.HomeXG + xg
                    else
                        state.AwayXG <- state.AwayXG + xg

                    if isInGoal ball.Position.X ball.Position.Y ball.Position.Z shootingDir then
                        GoalScored(shooterClub, Some shooterId),
                        { ball with
                            Control = Free
                            Trajectory = None }
                    else
                        let onTargetEvt =
                            createEvent
                                (subTick * 1<subtick>)
                                shooterId
                                (if shooterClub = HomeClub then ctx.Home.Id else ctx.Away.Id)
                                ShotOnTarget

                        let saveEvt =
                            createEvent
                                (subTick * 1<subtick>)
                                shooterId
                                (if shooterClub = HomeClub then ctx.Home.Id else ctx.Away.Id)
                                (SaveCaught(shooterId, player.Id))

                        PossessionGained(
                            club,
                            player,
                            [ onTargetEvt
                              { saveEvt with
                                  Context =
                                      { saveEvt.Context with
                                          GKSaveAttempt = Some true
                                          ShotSpeed = Some shotSpeed } } ]
                        ),
                        { ball with
                            Control = Controlled(club, player.Id)
                            LastTouchBy = Some player.Id
                            Trajectory = None
                            GKHoldSinceSubTick = Some(subTick * 1<subtick>) }
                | _ -> NoChange, ball
            | None ->
                PossessionGained(club, player, []),
                { ball with
                    Control = Controlled(club, player.Id) }

        | ContactResolver.Interceptor(player, club) ->
            match ball.Trajectory with
            | Some traj ->
                match traj.Intent with
                | Aimed(passerId, _, _, _) ->
                    let passerClub =
                        if homeRoster.Players |> Array.exists (fun p -> p.Id = passerId) then
                            HomeClub
                        else
                            AwayClub

                    let passerClubId = if passerClub = HomeClub then ctx.Home.Id else ctx.Away.Id

                    let interceptorEvt =
                        createEvent (subTick * 1<subtick>) passerId passerClubId (PassIntercepted(passerId, player.Id))

                    PossessionGained(
                        club,
                        player,
                        [ { interceptorEvt with
                              Context =
                                  { interceptorEvt.Context with
                                      InterceptorId = Some player.Id } } ]
                    ),
                    { ball with
                        Control = Controlled(club, player.Id)
                        LastTouchBy = Some player.Id
                        Trajectory = None }
                | _ ->
                    PossessionGained(club, player, []),
                    { ball with
                        Control = Controlled(club, player.Id)
                        Trajectory = None }
            | None ->
                PossessionGained(club, player, []),
                { ball with
                    Control = Controlled(club, player.Id)
                    Trajectory = None }

        | ContactResolver.Contested club ->
            BallContested club,
            { ball with
                Control = Contesting club
                Trajectory = None }

        | ContactResolver.NoContact ->
            match ball.Trajectory with
            | Some traj ->
                match traj.Intent with
                | Struck(shooterId, quality, shotSpeed, shotAngle, _xg) ->
                    let shooterClub =
                        if homeRoster.Players |> Array.exists (fun p -> p.Id = shooterId) then
                            HomeClub
                        else
                            AwayClub

                    if isInGoal ball.Position.X ball.Position.Y ball.Position.Z attDir then
                        GoalScored(shooterClub, Some shooterId),
                        { ball with
                            Control = Free
                            Trajectory = None }
                    else
                        BallInFlight, ball
                | _ ->
                    let vSq =
                        ball.Position.Vx * ball.Position.Vx
                        + ball.Position.Vy * ball.Position.Vy
                        + ball.Position.Vz * ball.Position.Vz

                    if vSq < 1.0<meter / second> * 1.0<meter / second> then
                        BallLoose [], { ball with Control = Free }
                    else
                        NoChange, ball
            | None ->
                let vSq =
                    ball.Position.Vx * ball.Position.Vx
                    + ball.Position.Vy * ball.Position.Vy
                    + ball.Position.Vz * ball.Position.Vz

                if vSq < 1.0<meter / second> * 1.0<meter / second> then
                    BallLoose [], { ball with Control = Free }
                else
                    NoChange, ball

        | _ -> NoChange, ball
