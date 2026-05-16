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

    let resolve
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
                | Aimed(passerId, targetId, quality, AimedKind.LongBall) ->
                    let isLongBall =
                        match traj.Intent with
                        | Aimed(_, _, _, AimedKind.LongBall) -> true
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
                        else
                            [ createEvent (subTick * 1<subtick>) passerId attClubId (PassCompleted(passerId, targetId)) ]

                    let controlProb =
                        normaliseAttr player.Technical.BallControl
                        * ctx.Config.GK.CatchHandlingMult
                        * controlMult

                    if bernoulli (clamp controlProb 0.0 interceptCap) then
                        PossessionGained(club, player, passEvent true),
                        { ball with
                            Control = Controlled(club, player.Id)
                            LastTouchBy = Some player.Id }
                    else
                        BallLoose(passEvent false),
                        { ball with
                            Control = Free
                            LastTouchBy = Some player.Id }
                | Struck(shooterId, quality) ->
                    let shootingDir = attDir

                    if isInGoal ball.Position.X ball.Position.Y ball.Position.Z shootingDir then
                        GoalScored(
                            (if homeRoster.Players |> Array.exists (fun p -> p.Id = shooterId) then
                                 HomeClub
                             else
                                 AwayClub),
                            Some shooterId
                        ),
                        { ball with
                            Control = Free
                            Trajectory = None }
                    else
                        NoChange, ball
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

                    PossessionGained(
                        club,
                        player,
                        [ createEvent (subTick * 1<subtick>) passerId passerClubId (PassIntercepted(passerId, player.Id)) ]
                    ),
                    { ball with
                        Control = Controlled(club, player.Id)
                        LastTouchBy = Some player.Id }
                | _ ->
                    PossessionGained(club, player, []),
                    { ball with
                        Control = Controlled(club, player.Id) }
            | None ->
                PossessionGained(club, player, []),
                { ball with
                    Control = Controlled(club, player.Id) }

        | ContactResolver.Contested club ->
            BallContested club,
            { ball with
                Control = Contesting club
                Trajectory = None }

        | ContactResolver.NoContact ->
            let vSq =
                ball.Position.Vx * ball.Position.Vx
                + ball.Position.Vy * ball.Position.Vy
                + ball.Position.Vz * ball.Position.Vz

            if vSq < 1.0<meter / second> * 1.0<meter / second> then
                BallLoose [], { ball with Control = Free }
            else
                NoChange, ball

        | _ -> NoChange, ball
