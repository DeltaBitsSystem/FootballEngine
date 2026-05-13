namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Stats
open SchedulingTypes
open SimStateOps

module BallAgent =

    // -------------------------------------------------------------------------
    // Spatial helpers
    // -------------------------------------------------------------------------

    let private isBallArrivingToPlayer
        (ballPos: Spatial)
        (playerPos: Spatial)
        (competitionRadius: float<meter>)
        (convergenceThreshold: float<meter / second>)
        : bool =
        let dist = ballPos.DistTo2D playerPos

        if dist > competitionRadius then
            false
        else
            let toPlayerX = playerPos.X - ballPos.X
            let toPlayerY = playerPos.Y - ballPos.Y
            let toPlayerLen = sqrt (toPlayerX * toPlayerX + toPlayerY * toPlayerY)

            if toPlayerLen < 0.001<meter> then
                true
            else
                let ballSpeed = sqrt (ballPos.Vx * ballPos.Vx + ballPos.Vy * ballPos.Vy)

                if ballSpeed < 0.5<meter / second> then
                    true
                else
                    let normX = toPlayerX / toPlayerLen
                    let normY = toPlayerY / toPlayerLen
                    let convergence = float (ballPos.Vx * normX + ballPos.Vy * normY)
                    convergence < float convergenceThreshold

    let private isInPenaltyArea (x: float<meter>) (y: float<meter>) (dir: AttackDir) : bool =
        let penX = PitchLength - PenaltyAreaDepth

        match dir with
        | LeftToRight -> x > penX
        | RightToLeft -> x < PenaltyAreaDepth

    let private isGoalLineCrossed (x: float<meter>) (dir: AttackDir) : bool =
        match dir with
        | LeftToRight -> x >= PitchLength
        | RightToLeft -> x <= 0.0<meter>

    let private isInGoal (x: float<meter>) (y: float<meter>) (z: float<meter>) (dir: AttackDir) : bool =
        isGoalLineCrossed x dir
        && y >= PostNearY && y <= PostFarY
        && z >= 0.0<meter> && z <= CrossbarHeight

    // -------------------------------------------------------------------------
    // Ball state helpers
    // -------------------------------------------------------------------------

    let private zeroVel (ball: BallPhysicsState) : BallPhysicsState =
        { ball with
            Position =
                { ball.Position with
                    Vx = 0.0<meter / second>
                    Vy = 0.0<meter / second>
                    Vz = 0.0<meter / second> } }

    let private ballNearlyStopped (ball: BallPhysicsState) : bool =
        let vSq =
            ball.Position.Vx * ball.Position.Vx
            + ball.Position.Vy * ball.Position.Vy
            + ball.Position.Vz * ball.Position.Vz

        vSq < 1.0<meter / second> * 1.0<meter / second>

    let private setFree (ball: BallPhysicsState) : BallPhysicsState =
        { zeroVel ball with Control = Free; Trajectory = None }

    let private setFreeWithTouch (pid: PlayerId) (ball: BallPhysicsState) : BallPhysicsState =
        { setFree ball with LastTouchBy = Some pid }

    // -------------------------------------------------------------------------
    // BallResult constructors
    // -------------------------------------------------------------------------

    let private liveResult events playerId =
        { Events = events
          Transition = Some Live
          PossessionChanged = true
          BallInFlight = false
          SetPieceAwarded = false
          ReceivedByPlayer = playerId
          GoalScored = None }

    let private looseResult events =
        { Events = events
          Transition = Some Live
          PossessionChanged = false
          BallInFlight = false
          SetPieceAwarded = false
          ReceivedByPlayer = None
          GoalScored = None }

    let private contestResult events =
        { Events = events
          Transition = Some Live
          PossessionChanged = true
          BallInFlight = false
          SetPieceAwarded = false
          ReceivedByPlayer = None
          GoalScored = None }

    let private inFlightResult events =
        { Events = events
          Transition = None
          PossessionChanged = false
          BallInFlight = true
          SetPieceAwarded = false
          ReceivedByPlayer = None
          GoalScored = None }

    let private setPieceResult transition =
        { Events = []
          Transition = Some transition
          PossessionChanged = false
          BallInFlight = false
          SetPieceAwarded = true
          ReceivedByPlayer = None
          GoalScored = None }

    let private noOpResult =
        { Events = []
          Transition = None
          PossessionChanged = false
          BallInFlight = false
          SetPieceAwarded = false
          ReceivedByPlayer = None
          GoalScored = None }

    // -------------------------------------------------------------------------
    // Player search helpers
    // -------------------------------------------------------------------------

    let private shouldTriggerArrival
        (ball: BallPhysicsState)
        (cfg: PhysicsConfig)
        (state: SimState)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        : bool =
        let ballPos = ball.Position
        let compRadius = cfg.ArrivalCompetitionRadius
        let convThreshold = cfg.ArrivalConvergenceThreshold

        let checkFrame (frame: TeamFrame) (roster: PlayerRoster) =
            let mutable result = false

            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let player = roster.Players[i]

                    let isKicker =
                        match ball.Trajectory with
                        | Some t -> t.KickerId = player.Id && (state.SubTick - t.LaunchSubTick < 10)
                        | None -> false

                    if not isKicker then
                        let px = float frame.Physics.PosX[i] * 1.0<meter>
                        let py = float frame.Physics.PosY[i] * 1.0<meter>

                        let pPos =
                            { X = px; Y = py; Z = 0.0<meter>
                              Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> }

                        if isBallArrivingToPlayer ballPos pPos compRadius convThreshold then
                            result <- true
                | _ -> ()

            result

        checkFrame homeFrame homeRoster || checkFrame awayFrame awayRoster

    let private findNearestChaserSoA
        (config: PhysicsConfig)
        (ballPos: Spatial)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        : PlayerId option =
        let mutable bestTime = System.Double.PositiveInfinity
        let mutable bestPlayerId: PlayerId option = None

        for i = 0 to homeFrame.SlotCount - 1 do
            match homeFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let player = homeRoster.Players[i]
                let px = float homeFrame.Physics.PosX[i] * 1.0<meter>
                let py = float homeFrame.Physics.PosY[i] * 1.0<meter>

                let pPos =
                    { X = px; Y = py; Z = 0.0<meter>
                      Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> }

                let t = Interception.estimateTimeToBall config player pPos ballPos

                if t < bestTime then
                    bestTime <- t
                    bestPlayerId <- Some player.Id
            | _ -> ()

        for i = 0 to awayFrame.SlotCount - 1 do
            match awayFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let player = awayRoster.Players[i]
                let px = float awayFrame.Physics.PosX[i] * 1.0<meter>
                let py = float awayFrame.Physics.PosY[i] * 1.0<meter>

                let pPos =
                    { X = px; Y = py; Z = 0.0<meter>
                      Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> }

                let t = Interception.estimateTimeToBall config player pPos ballPos

                if t < bestTime then
                    bestTime <- t
                    bestPlayerId <- Some player.Id
            | _ -> ()

        bestPlayerId

    let private findPlayerByPidSoA
        (pid: PlayerId)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        =
        let rec searchHome i =
            if i >= homeFrame.SlotCount then None
            else
                match homeFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ when homeRoster.Players[i].Id = pid ->
                    Some(homeRoster.Players[i],
                         float homeFrame.Physics.PosX[i] * 1.0<meter>,
                         float homeFrame.Physics.PosY[i] * 1.0<meter>)
                | _ -> searchHome (i + 1)

        let rec searchAway i =
            if i >= awayFrame.SlotCount then None
            else
                match awayFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ when awayRoster.Players[i].Id = pid ->
                    Some(awayRoster.Players[i],
                         float awayFrame.Physics.PosX[i] * 1.0<meter>,
                         float awayFrame.Physics.PosY[i] * 1.0<meter>)
                | _ -> searchAway (i + 1)

        searchHome 0 |> Option.orElse (searchAway 0)

    // -------------------------------------------------------------------------
    // GK helper — parry used by both Struck and Cross
    // -------------------------------------------------------------------------

    let private applyParry
        (gkc: GKConfig)
        (shootingDir: AttackDir)
        (gkId: PlayerId)
        (ball: BallPhysicsState)
        : BallPhysicsState =
        let ballPos = ball.Position
        let dirSign = forwardX shootingDir
        let parryAngle = normalSample 0.0 gkc.ParryDeflectionAngle
        let parryVx = -dirSign * gkc.ParrySpeed * System.Math.Cos(parryAngle)
        let parryVy = gkc.ParrySpeed * System.Math.Sin(parryAngle)

        { ball with
            Position =
                { ballPos with
                    Vx = parryVx
                    Vy = parryVy
                    Vz = 2.0<meter / second> }
            Control = Free
            LastTouchBy = Some gkId
            GKHoldSinceSubTick = None
            Trajectory = None }

    // -------------------------------------------------------------------------
    // resolveInFlightOutcome — one function per intent
    // -------------------------------------------------------------------------

    let private resolveNoTrajectory
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (player: Player)
        (playerClub: ClubSide)
        : BallResult =
        clearOffsideSnapshot state
        givePossessionTo playerClub player.Id (player.Position = GK) state.SubTick ball state
        liveResult [] (Some player.Id)

    let private resolvePass
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (player: Player)
        (playerClub: ClubSide)
        (passerId: PlayerId)
        (targetId: PlayerId)
        (isLongBall: bool)
        : BallResult =

        let gkc = ctx.Config.GK
        let ballPos = ball.Position
        let attClubId = if playerOnSide ctx state HomeClub passerId then ctx.Home.Id else ctx.Away.Id
        let controlMult = if isLongBall then 0.8 else 1.0
        let interceptCap = if isLongBall then 0.9 else 0.95

        let passEvent success =
            if isLongBall then createEvent state.SubTick passerId attClubId (MatchEventType.LongBall success)
            else createEvent state.SubTick passerId attClubId (PassCompleted(passerId, targetId))

        clearOffsideSnapshot state

        if player.Id = targetId then
            let controlProb = normaliseAttr player.Technical.BallControl * gkc.CatchHandlingMult * controlMult

            if bernoulli (System.Math.Clamp(controlProb, 0.0, interceptCap)) then
                givePossessionTo playerClub player.Id (player.Position = GK) state.SubTick ball state
                liveResult [ passEvent true ] (Some player.Id)
            else
                state.Ball <- setFreeWithTouch player.Id ball
                emitSemantic BallLoose state
                looseResult [ passEvent false ]

        elif
            player.Position = GK
            && player.Id <> passerId
            && isInPenaltyArea ballPos.X ballPos.Y (attackDirFor playerClub state)
        then
            let interceptProb = normaliseAttr player.Mental.Positioning * gkc.CatchHandlingMult

            if bernoulli (System.Math.Clamp(interceptProb, 0.0, interceptCap)) then
                givePossessionTo playerClub player.Id (player.Position = GK) state.SubTick ball state
                liveResult [ createEvent state.SubTick passerId attClubId (PassIntercepted(passerId, player.Id)) ] (Some player.Id)
            else
                if isLongBall then
                    givePossessionTo playerClub player.Id (player.Position = GK) state.SubTick ball state
                    liveResult [ passEvent false ] (Some player.Id)
                else
                    state.Ball <- setFreeWithTouch player.Id ball
                    emitSemantic BallLoose state
                    looseResult []

        else
            let interceptProb = normaliseAttr player.Mental.Positioning * 0.5

            if bernoulli (System.Math.Clamp(interceptProb, 0.0, 0.8)) then
                givePossessionTo playerClub player.Id (player.Position = GK) state.SubTick ball state
                liveResult [ createEvent state.SubTick passerId attClubId (PassIntercepted(passerId, player.Id)) ] (Some player.Id)
            else
                if isLongBall then
                    state.Ball <- { ball with Control = Contesting playerClub; Trajectory = None }
                    contestResult []
                else
                    state.Ball <- { ball with Control = Contesting playerClub; Trajectory = None }
                    contestResult [ createEvent state.SubTick passerId attClubId (PassMisplaced(passerId, player.Id)) ]

    let private resolveStruck
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (player: Player)
        (playerClub: ClubSide)
        (shooterId: PlayerId)
        (traj: BallTrajectory)
        : BallResult =

        let gkc = ctx.Config.GK
        let ballPos = ball.Position
        let shooterClub = if playerOnSide ctx state HomeClub shooterId then HomeClub else AwayClub
        let shooterClubId = if shooterClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let gkClubId = if playerClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let shootingDir = attackDirFor shooterClub state
        let gkDir = attackDirFor playerClub state
        let tick = state.SubTick

        if isInGoal ballPos.X ballPos.Y ballPos.Z shootingDir then
            clearOffsideSnapshot state
            state.Ball <- { ball with Control = Free; Trajectory = None }

            { Events = [ createEvent tick shooterId shooterClubId ShotOnTarget ]
              Transition =
                Some(MatchFlow.GoalPause
                    { ScoringTeam = shooterClub
                      ScorerId = Some shooterId
                      IsOwnGoal = false
                      RemainingTicks = 120
                      VARRequested = false })
              PossessionChanged = true
              BallInFlight = false
              SetPieceAwarded = false
              ReceivedByPlayer = None
              GoalScored = None }

        elif player.Position = GK && isInPenaltyArea ballPos.X ballPos.Y gkDir then
            let distToGoal =
                if shootingDir = LeftToRight then PitchLength - ballPos.X else ballPos.X

            let gkReactionBonus = 1.0 - float distToGoal / float PenaltyAreaDepth
            let saveProb = normaliseAttr player.Goalkeeping.Reflexes * gkc.CatchHandlingMult + gkReactionBonus * 0.3

            if bernoulli (System.Math.Clamp(saveProb, 0.0, 0.95)) then
                let catchProb = normaliseAttr player.Goalkeeping.Handling * gkc.CatchHandlingMult

                if bernoulli (System.Math.Clamp(catchProb, 0.0, 0.9)) then
                    clearOffsideSnapshot state
                    givePossessionTo playerClub player.Id true tick ball state
                    liveResult [ createEvent tick shooterId gkClubId Save
                                 createEvent tick shooterId gkClubId (SaveCaught(shooterId, player.Id)) ]
                               (Some player.Id)
                else
                    clearOffsideSnapshot state
                    state.Ball <- applyParry gkc shootingDir player.Id ball
                    looseResult [ createEvent tick shooterId gkClubId Save
                                  createEvent tick shooterId gkClubId (SaveParried(shooterId, player.Id)) ]
            else
                state.Ball <- ball
                noOpResult

        elif isGoalLineCrossed ballPos.X shootingDir then
            clearOffsideSnapshot state
            let defClub = ClubSide.flip shooterClub

            let lastTouchClub =
                ball.LastTouchBy
                |> Option.bind (fun pid ->
                    if playerOnSide ctx state HomeClub pid then Some HomeClub
                    elif playerOnSide ctx state AwayClub pid then Some AwayClub
                    else None)

            state.Ball <- { ball with Control = Free; Trajectory = None }

            let transition =
                match lastTouchClub with
                | Some club when club = defClub ->
                    MatchFlow.RestartDelay { Kind = SetPieceKind.Corner; Team = shooterClub; Cause = AfterBallOut; RemainingTicks = 120 }
                | _ ->
                    MatchFlow.RestartDelay { Kind = SetPieceKind.GoalKick; Team = defClub; Cause = AfterBallOut; RemainingTicks = 120 }

            setPieceResult transition

        else
            state.Ball <- ball
            noOpResult

    let private resolveCross
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (player: Player)
        (playerClub: ClubSide)
        (crosserId: PlayerId)
        (targetId: PlayerId)
        (traj: BallTrajectory)
        : BallResult =

        let gkc = ctx.Config.GK
        let ballPos = ball.Position
        let crossAttClub = if playerOnSide ctx state HomeClub crosserId then HomeClub else AwayClub
        let crossAttClubId = if crossAttClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let gkClubId = if playerClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let attackerClubId = if playerClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let crossDir = attackDirFor crossAttClub state
        let tick = state.SubTick

        clearOffsideSnapshot state

        if player.Position = GK && player.Id <> crosserId && isInPenaltyArea ballPos.X ballPos.Y crossDir then
            let gkReach =
                normaliseAttr player.Goalkeeping.AerialReach * ctx.Config.Cross.GkAerialReachMult
                + normaliseAttr player.Physical.JumpingReach * ctx.Config.Cross.GkJumpMult

            let claimProb = gkReach * normaliseCondition player.Condition * ctx.Config.Cross.ClaimCrossProbability

            if bernoulli (System.Math.Clamp(claimProb, 0.0, 0.95)) then
                let catchProb = normaliseAttr player.Goalkeeping.Handling * gkc.CatchHandlingMult

                if bernoulli (System.Math.Clamp(catchProb, 0.0, 0.9)) then
                    givePossessionTo playerClub player.Id true tick ball state
                    liveResult [ createEvent tick crosserId crossAttClubId (CrossAttempt false)
                                 createEvent tick player.Id gkClubId Save
                                 createEvent tick crosserId crossAttClubId (SaveCaught(crosserId, player.Id)) ]
                               (Some player.Id)
                else
                    state.Ball <- applyParry gkc crossDir player.Id ball
                    inFlightResult [ createEvent tick crosserId gkClubId (GKPunch player.Id) ]
            else
                state.Ball <- ball
                noOpResult

        elif player.Id = targetId then
            let headerProb =
                normaliseAttr player.Technical.Heading * 0.6
                + normaliseAttr player.Physical.Strength * 0.4

            if bernoulli (System.Math.Clamp(headerProb, 0.0, 0.9)) then
                let shootingDir = attackDirFor playerClub state
                let distToGoal = PhysicsContract.distToGoal ballPos.X shootingDir

                if distToGoal < 15.0<meter> then
                    let shotPower = float player.Technical.Finishing / 20.0 * 10.0
                    let dirSign = forwardX shootingDir
                    let angle = normalSample 0.0 0.3
                    let vx = dirSign * shotPower * System.Math.Cos(angle)
                    let vy = shotPower * System.Math.Sin(angle)
                    let vz = abs (normalSample 2.0 1.0) * 1.0<meter / second>
                    let goalX = if shootingDir = LeftToRight then PitchLength else 0.0<meter>
                    let targetY = ballPos.Y + (vy / (dirSign * shotPower)) * (goalX - ballPos.X)

                    state.Ball <-
                        { ball with
                            Position = { ballPos with Vx = vx * 1.0<meter / second>; Vy = vy * 1.0<meter / second>; Vz = vz }
                            LastTouchBy = Some player.Id
                            Trajectory = Some { traj with Intent = Struck(player.Id, headerProb); TargetY = targetY } }

                    inFlightResult [ createEvent tick player.Id attackerClubId ShotOnTarget ]
                else
                    givePossessionTo playerClub player.Id (player.Position = GK) tick ball state
                    liveResult [ createEvent tick crosserId attackerClubId (CrossAttempt true) ] (Some player.Id)
            else
                state.Ball <- setFreeWithTouch player.Id ball
                emitSemantic BallLoose state
                looseResult [ createEvent tick crosserId attackerClubId (CrossAttempt false) ]

        elif player.Position <> GK then
            let clearProb = 0.5 + normaliseAttr player.Physical.Strength * 0.3

            if bernoulli (System.Math.Clamp(clearProb, 0.0, 0.9)) then
                let dirSign = -forwardX crossDir
                let clearAngle = normalSample 0.0 0.5
                let clearSpeed = 8.0<meter / second>

                state.Ball <-
                    { ball with
                        Position =
                            { ballPos with
                                Vx = dirSign * clearSpeed * System.Math.Cos(clearAngle)
                                Vy = clearSpeed * System.Math.Sin(clearAngle)
                                Vz = 3.0<meter / second> }
                        Control = Airborne
                        LastTouchBy = Some player.Id
                        Trajectory = Some { traj with Intent = Cleared player.Id } }

                inFlightResult [ createEvent tick crosserId crossAttClubId (CrossAttempt false) ]
            else
                state.Ball <- { ball with Control = Contesting playerClub; Trajectory = None }
                contestResult [ createEvent tick crosserId crossAttClubId (CrossAttempt false) ]

        else
            state.Ball <- { ball with Control = Contesting playerClub; Trajectory = None }
            contestResult []

    let private resolveInFlightOutcome
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (player: Player)
        (playerClub: ClubSide)
        : BallResult =
        match ball.Trajectory with
        | None ->
            resolveNoTrajectory ctx state ball player playerClub

        | Some traj ->
            match traj.Intent with
            | Aimed(passerId, targetId, _, RegularPass) ->
                resolvePass ctx state ball player playerClub passerId targetId false

            | Aimed(passerId, targetId, _, AimedKind.LongBall) ->
                resolvePass ctx state ball player playerClub passerId targetId true

            | Struck(shooterId, _) ->
                resolveStruck ctx state ball player playerClub shooterId traj

            | Aimed(crosserId, targetId, _, AimedKind.Cross) ->
                resolveCross ctx state ball player playerClub crosserId targetId traj

            | Cleared _
            | Uncontrolled ->
                givePossessionTo playerClub player.Id (player.Position = GK) state.SubTick ball state
                liveResult [] (Some player.Id)

    // -------------------------------------------------------------------------
    // resolveShot — pelota en vuelo sin arrival trigger, busca GK
    // -------------------------------------------------------------------------

    let private resolveShot
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (traj: BallTrajectory)
        : BallResult =
        let ballPos = ball.Position
        let shooterClub = if playerOnSide ctx state HomeClub traj.KickerId then HomeClub else AwayClub
        let shootingDir = attackDirFor shooterClub state
        let defClub = ClubSide.flip shooterClub
        let homeFrame = getFrame state HomeClub
        let awayFrame = getFrame state AwayClub
        let homeRoster = getRoster ctx HomeClub
        let awayRoster = getRoster ctx AwayClub
        let defFrame = if defClub = HomeClub then homeFrame else awayFrame
        let defRoster = if defClub = HomeClub then homeRoster else awayRoster
        let gkDir = attackDirFor defClub state
        let mutable gkPlayer: Player option = None

        for i = 0 to defFrame.SlotCount - 1 do
            match defFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ when defRoster.Players[i].Position = GK ->
                if isInPenaltyArea ballPos.X ballPos.Y gkDir then
                    gkPlayer <- Some defRoster.Players[i]
            | _ -> ()

        match gkPlayer with
        | Some gk -> resolveInFlightOutcome ctx state ball gk defClub

        | None ->
            if isInGoal ballPos.X ballPos.Y ballPos.Z shootingDir then
                clearOffsideSnapshot state
                state.Ball <- { ball with Control = Free; Trajectory = None }
                { BallResult.empty with GoalScored = Some shooterClub }

            elif isGoalLineCrossed ballPos.X shootingDir then
                clearOffsideSnapshot state

                let lastTouchClub =
                    ball.LastTouchBy
                    |> Option.bind (fun pid ->
                        if playerOnSide ctx state HomeClub pid then Some HomeClub
                        elif playerOnSide ctx state AwayClub pid then Some AwayClub
                        else None)

                state.Ball <- { ball with Control = Free; Trajectory = None }

                let transition =
                    match lastTouchClub with
                    | Some club when club = defClub ->
                        MatchFlow.RestartDelay { Kind = SetPieceKind.Corner; Team = ClubSide.flip defClub; Cause = AfterBallOut; RemainingTicks = 120 }
                    | _ ->
                        MatchFlow.RestartDelay { Kind = SetPieceKind.GoalKick; Team = ClubSide.flip defClub; Cause = AfterBallOut; RemainingTicks = 120 }

                setPieceResult transition
            else
                state.Ball <- ball
                BallResult.empty

    // -------------------------------------------------------------------------
    // resolveInterception
    // -------------------------------------------------------------------------

    let private resolveInterception
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (interceptor: Player)
        (interceptorClub: ClubSide)
        (passerId: PlayerId)
        (passerClub: ClubSide)
        : BallResult =
        let passerClubId = if passerClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let passerFrame = getFrame state passerClub
        let passerRoster = getRoster ctx passerClub

        match findIdxByPid passerId passerFrame passerRoster with
        | ValueSome idx -> MatchMemory.recordPassFailure passerClub idx state.MatchMemory
        | ValueNone -> ()

        clearOffsideSnapshot state
        givePossessionTo interceptorClub interceptor.Id (interceptor.Position = GK) state.SubTick ball state
        liveResult [ createEvent state.SubTick passerId passerClubId (PassIntercepted(passerId, interceptor.Id)) ]
                   (Some interceptor.Id)

    // -------------------------------------------------------------------------
    // resolveArrival — dispatcher una vez que sabemos que alguien puede recibir
    // -------------------------------------------------------------------------

    let private resolveArrival
        (ctx: MatchContext)
        (state: SimState)
        (ball: BallPhysicsState)
        (traj: BallTrajectory)
        : BallResult =
        let homeFrame = getFrame state HomeClub
        let awayFrame = getFrame state AwayClub
        let homeRoster = getRoster ctx HomeClub
        let awayRoster = getRoster ctx AwayClub

        let buildArrivalCtx targetId quality =
            { BallPos = ball.Position
              TargetId = targetId
              Quality = quality
              HomeFrame = homeFrame
              AwayFrame = awayFrame
              HomeRoster = homeRoster
              AwayRoster = awayRoster
              PhysicsCfg = ctx.Config.Physics }

        let resolveContested passerId =
            let attClub = if playerOnSide ctx state HomeClub passerId then HomeClub else AwayClub
            state.Ball <- { zeroVel ball with Control = Contesting attClub; Trajectory = None }
            contestResult []

        match traj.Intent with
        | Struck _ -> resolveShot ctx state ball traj

        | Aimed(passerId, targetId, quality, _) ->
            let arrivalCtx = buildArrivalCtx targetId quality

            match Interception.evaluateArrival arrivalCtx with
            | NoOneInRange ->
                state.Ball <- ball
                noOpResult

            | IntendedTarget(player, club) ->
                resolveInFlightOutcome ctx state ball player club

            | Intercepted(player, club) ->
                let passerClub = if playerOnSide ctx state HomeClub passerId then HomeClub else AwayClub
                resolveInterception ctx state ball player club passerId passerClub

            | Contested ->
                resolveContested passerId

        | Cleared _
        | Uncontrolled ->
            state.Ball <- ball
            noOpResult

    // -------------------------------------------------------------------------
    // resolveFreeBall — física libre: bounds, arrival, chaser
    // -------------------------------------------------------------------------

    let private resolveFreeBall
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (ball: BallPhysicsState)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        (pcfg: PhysicsConfig)
        : BallResult =

        let ballPos = ball.Position
        let bx = ballPos.X
        let by = ballPos.Y
        let bz = ballPos.Z

        let lastTouchClub () =
            ball.LastTouchBy
            |> Option.bind (fun pid ->
                if playerOnSide ctx state HomeClub pid then Some HomeClub
                elif playerOnSide ctx state AwayClub pid then Some AwayClub
                else None)

        let nearestChaser =
            findNearestChaserSoA pcfg ballPos homeFrame awayFrame homeRoster awayRoster

        // 1. Out of bounds — sideline
        if by <= 0.1<meter> || by >= PitchWidth - 0.1<meter> then
            clearOffsideSnapshot state

            match lastTouchClub () with
            | Some side ->
                state.Ball <- { ball with Control = Free }
                setPieceResult (MatchFlow.RestartDelay
                    { Kind = SetPieceKind.ThrowIn
                      Team = ClubSide.flip side
                      Cause = AfterBallOut
                      RemainingTicks = TickDelay.delayFrom clock state.Config.Timing.ThrowInDelay })
            | None ->
                state.Ball <- setFree ball
                emitSemantic BallLoose state
                looseResult []

        // 2. Out of bounds — goal line
        elif bx <= 0.1<meter> || bx >= PitchLength - 0.1<meter> then
            clearOffsideSnapshot state
            let inGoal = by >= PostNearY && by <= PostFarY && bz >= 0.0<meter> && bz <= CrossbarHeight

            if inGoal then
                match lastTouchClub () with
                | Some sc ->
                    state.Ball <- { ball with Control = Free }
                    { BallResult.empty with GoalScored = Some sc }
                | None ->
                    state.Ball <- setFree ball
                    emitSemantic BallLoose state
                    looseResult []
            else
                let behindHome = bx <= 0.5<meter>
                state.Ball <- { ball with Control = Free }

                let attackingSide, defendingSide =
                    if behindHome then HomeClub, AwayClub else AwayClub, HomeClub

                let transition =
                    match lastTouchClub () with
                    | Some club when club = attackingSide ->
                        MatchFlow.RestartDelay
                            { Kind = SetPieceKind.Corner
                              Team = defendingSide
                              Cause = AfterBallOut
                              RemainingTicks = TickDelay.delayFrom clock state.Config.Timing.CornerDelay }
                    | _ ->
                        MatchFlow.RestartDelay
                            { Kind = SetPieceKind.GoalKick
                              Team = attackingSide
                              Cause = AfterBallOut
                              RemainingTicks = TickDelay.delayFrom clock state.Config.Timing.GoalKickDelay }

                setPieceResult transition

        // 3. Arrival — alguien puede recibir
        elif shouldTriggerArrival ball pcfg state homeFrame awayFrame homeRoster awayRoster then
            match ball.Trajectory with
            | Some traj -> resolveArrival ctx state ball traj
            | None ->
                let winner, _, _ =
                    Interception.chooseBestInterceptorSoA pcfg ballPos homeFrame awayFrame homeRoster awayRoster

                match winner with
                | Some p ->
                    let club = clubSideOf ctx state p.Id |> Option.defaultValue state.AttackingSide
                    givePossessionTo club p.Id (p.Position = GK) state.SubTick ball state
                    liveResult [] None
                | None ->
                    state.Ball <- ball
                    noOpResult

        // 4. Pelota parada — nadie la tomó todavía
        elif ballNearlyStopped ball then
            state.Ball <- setFree ball
            emitSemantic BallLoose state
            looseResult []

        // 5. En movimiento — chaser logic
        else
            match state.Ball.Control with
            | Contesting side ->
                let isSameSide pid =
                    match clubSideOf ctx state pid with
                    | Some c -> c = side
                    | _ -> false

                match nearestChaser with
                | Some pid when isSameSide pid ->
                    match findPlayerByPidSoA pid homeFrame awayFrame homeRoster awayRoster with
                    | Some(player, _, _) ->
                        givePossessionTo side player.Id (player.Position = GK) state.SubTick ball state
                        liveResult [] None
                    | None ->
                        state.Ball <- setFree ball
                        emitSemantic BallLoose state
                        looseResult []
                | _ ->
                    state.Ball <- setFree ball
                    emitSemantic BallLoose state
                    looseResult []

            | Free ->
                match nearestChaser with
                | Some pid ->
                    let club = clubSideOf ctx state pid |> Option.defaultValue state.AttackingSide

                    match findPlayerByPidSoA pid homeFrame awayFrame homeRoster awayRoster with
                    | Some(s, sx, sy) ->
                        let pPos =
                            { X = sx; Y = sy; Z = 0.0<meter>
                              Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> }

                        let timeToBall = Interception.estimateTimeToBall pcfg s pPos ball.Position

                        if timeToBall < 1.0 then
                            givePossessionTo club s.Id (s.Position = GK) state.SubTick ball state
                            liveResult [] None
                        else
                            state.Ball <- ball
                            noOpResult
                    | None ->
                        state.Ball <- ball
                        noOpResult
                | None ->
                    state.Ball <- ball
                    noOpResult

            | Airborne
            | Controlled _
            | Receiving _ ->
                state.Ball <- ball
                noOpResult

    // -------------------------------------------------------------------------
    // agent — entry point
    // -------------------------------------------------------------------------

    let agent_DEPRECATED (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : BallResult =
        let pcfg = ctx.Config.Physics
        let dt = SimulationClock.dt clock
        let trajectoryBefore = state.Ball.Trajectory

        let homeFrame = getFrame state HomeClub
        let awayFrame = getFrame state AwayClub
        let homeRoster = getRoster ctx HomeClub
        let awayRoster = getRoster ctx AwayClub

        let result =
            match state.Ball.Control with
            | Controlled(_, pid)
            | Receiving(_, pid, _) ->
                match findPlayerByPidSoA pid homeFrame awayFrame homeRoster awayRoster with
                | Some(_, px, py) ->
                    state.Ball <-
                        { state.Ball with
                            Position =
                                { X = px; Y = py; Z = 0.0<meter>
                                  Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> }
                            Trajectory = None }
                | None -> ()

                noOpResult

            | _ ->
                let withStationary = BallPhysics.update pcfg dt state.Ball

                let wasStationary = state.Ball.StationarySinceSubTick.IsSome
                let isNowStopped = ballNearlyStopped withStationary

                let withStationary =
                    if isNowStopped && not wasStationary then
                        { withStationary with StationarySinceSubTick = Some state.SubTick }
                    elif not isNowStopped then
                        { withStationary with StationarySinceSubTick = None }
                    else
                        withStationary

                match state.Flow with
                | Live ->
                    resolveFreeBall ctx state clock withStationary homeFrame awayFrame homeRoster awayRoster pcfg
                | _ ->
                    state.Ball <- withStationary
                    noOpResult

        let trajectoryAfter = state.Ball.Trajectory

        let launchDetected =
            match trajectoryBefore, trajectoryAfter with
            | None, Some _ -> true
            | Some t1, Some t2 -> t1.LaunchSubTick <> t2.LaunchSubTick
            | _ -> false

        if launchDetected && not result.BallInFlight then
            { result with BallInFlight = true }
        else
            result