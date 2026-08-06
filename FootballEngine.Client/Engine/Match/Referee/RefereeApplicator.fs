namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open SimStateOps
open PhysicsContract


module RefereeApplicator =

    let private goalKickOff (scoringClub: ClubSide) : BallPhysicsState =
        { Position = kickOffSpatial
          Spin = Spin.zero
          Control = Free
          LastTouchBy = None
          PendingOffsideSnapshot = None
          StationarySinceSubTick = None
          GKHoldSinceSubTick = None
          PlayerHoldSinceSubTick = None
          Trajectory = None
          GKJudged = false }

    let private awardGoal
        (scoringClub: ClubSide)
        (scorerId: PlayerId option)
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        : DomainEvent[] =
        let clubId = if scoringClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let momentumDelta = if scoringClub = HomeClub then 3.0 else -3.0
        let kickOffBall = goalKickOff (ClubSide.flip scoringClub)

        let events = ResizeArray<DomainEvent>(8)
        events.Add(DomainEvent.ScoreGoal(scoringClub, scorerId, false))
        events.Add(DomainEvent.MomentumDelta momentumDelta)
        events.Add(DomainEvent.BallUpdate kickOffBall)
        events.Add(DomainEvent.LastAttackingClubSet(ClubSide.flip scoringClub))
        events.Add(DomainEvent.StoppageTimeAdd(subTick, StoppageReason.GoalDelay))

        match scorerId with
        | Some pid ->
            events.Add(
                DomainEvent.Emit
                    { SubTick = subTick
                      PlayerId = pid
                      ClubId = clubId
                      Type = Goal
                      Context = EventContext.empty }
            )
        | None -> ()

        events.ToArray()

    let apply
        (subTick: int)
        (action: RefereeAction)
        (ctx: MatchContext)
        (state: SimState)
        (description: string option)
        : DomainEvent[] =
        match action with
        | RefereeIdle -> [||]

        | ConfirmGoal(scoringClub, scorerId, isOwnGoal) ->
            let goalEvents = awardGoal scoringClub scorerId subTick ctx state
            let events = ResizeArray<DomainEvent>(goalEvents.Length + 1)
            events.Add(DomainEvent.EmitSemantic(GoalScored(scoringClub, scorerId)))

            for e in goalEvents do
                events.Add(e)

            if isOwnGoal then
                // Replace the Goal event with OwnGoal
                for i = 0 to events.Count - 1 do
                    match events[i] with
                    | DomainEvent.Emit e when e.Type = Goal -> events[i] <- DomainEvent.Emit { e with Type = OwnGoal }
                    | _ -> ()

            events.ToArray()

        | AnnulGoal ->
            let resetX =
                match state.PendingOffsideSnapshot with
                | Some snap -> snap.BallXAtPass
                | None -> HalfwayLineX

            let resetBall =
                { state.Ball with
                    Position = defaultSpatial resetX (PitchWidth / 2.0)
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free
                    PendingOffsideSnapshot = None }

            [| DomainEvent.BallUpdate resetBall |]

        | AwardThrowIn team ->
            let throwX =
                match team with
                | HomeClub -> PenaltyAreaDepth
                | AwayClub -> PitchLength - PenaltyAreaDepth

            let throwBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            X = throwX
                            Y = PitchWidth / 2.0
                            Z = 0.0<meter>
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }

            [| DomainEvent.EmitSemantic(SetPieceAwarded(SetPieceKind.ThrowIn, team))
               DomainEvent.BallUpdate throwBall |]

        | AwardCorner team ->
            let cornerX =
                match team with
                | HomeClub -> PitchLength - 0.5<meter>
                | AwayClub -> 0.5<meter>

            let cornerBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            X = cornerX
                            Y = PitchWidth / 2.0
                            Z = 0.0<meter>
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }

            let clubId = if team = HomeClub then ctx.Home.Id else ctx.Away.Id

            [| DomainEvent.Emit
                   { SubTick = subTick
                     PlayerId = 0
                     ClubId = clubId
                     Type = MatchEventType.Corner
                     Context = EventContext.empty }
               DomainEvent.EmitSemantic(SetPieceAwarded(SetPieceKind.Corner, team))
               DomainEvent.BallUpdate cornerBall |]

        | AwardGoalKick team ->
            let gkX =
                match team with
                | HomeClub -> GoalAreaDepth
                | AwayClub -> PitchLength - GoalAreaDepth

            let gkBall =
                { state.Ball with
                    Position = defaultSpatial gkX (PitchWidth / 2.0)
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }

            [| DomainEvent.EmitSemantic(SetPieceAwarded(SetPieceKind.GoalKick, team))
               DomainEvent.BallUpdate gkBall |]

        | AwardIndirectFreeKick team ->
            let fkBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }

            let clubId = if team = HomeClub then ctx.Home.Id else ctx.Away.Id

            [| DomainEvent.Emit
                   { SubTick = subTick
                     PlayerId = 0
                     ClubId = clubId
                     Type = MatchEventType.IndirectFreeKickAwarded team
                     Context = EventContext.empty }
               DomainEvent.EmitSemantic(SetPieceAwarded(SetPieceKind.FreeKick, team))
               DomainEvent.BallUpdate fkBall |]

        | DropBall _ ->
            let bx = float state.Ball.Position.X
            let by = float state.Ball.Position.Y

            let findNearest (frame: TeamFrame) (roster: PlayerRoster) =
                let mutable bestDist = System.Double.MaxValue
                let mutable bestPid = ValueNone

                for i = 0 to frame.SlotCount - 1 do
                    match frame.Physics.Occupancy[i] with
                    | OccupancyKind.Active rosterIdx ->
                        let px = float frame.Physics.PosX[i]
                        let py = float frame.Physics.PosY[i]
                        let dx = px - bx
                        let dy = py - by
                        let dist = sqrt (dx * dx + dy * dy)

                        if dist < bestDist then
                            bestDist <- dist
                            bestPid <- ValueSome roster.Players[rosterIdx].Id
                    | OccupancyKind.Sidelined _ -> ()

                bestPid, bestDist

            let homeFrame = getFrame state HomeClub
            let awayFrame = getFrame state AwayClub
            let homePidOpt, homeDist = findNearest homeFrame ctx.HomeRoster
            let awayPidOpt, awayDist = findNearest awayFrame ctx.AwayRoster

            let winner =
                match homePidOpt, awayPidOpt with
                | ValueSome pid, _ when homeDist <= 3.0 -> Some(HomeClub, pid)
                | _, ValueSome pid when awayDist <= 3.0 -> Some(AwayClub, pid)
                | ValueSome hPid, ValueSome aPid when homeDist <= 8.0 && awayDist <= 8.0 ->
                    if homeDist <= awayDist then
                        Some(HomeClub, hPid)
                    else
                        Some(AwayClub, aPid)
                | ValueSome hPid, _ when homeDist <= 8.0 -> Some(HomeClub, hPid)
                | _, ValueSome aPid when awayDist <= 8.0 -> Some(AwayClub, aPid)
                | _ -> None

            let control, lastTouch, gkHold =
                match winner with
                | Some(side, pid) -> Controlled(side, pid), Some pid, None
                | None -> Free, None, None

            let dropBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            Z = 0.0<meter>
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    Control = control
                    Trajectory = None
                    LastTouchBy = lastTouch
                    GKHoldSinceSubTick = gkHold }

            [| DomainEvent.BallUpdate dropBall |]

        | IssueYellow(player, clubId) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub

            let currentYellows =
                getYellows state side |> Map.tryFind player.Id |> Option.defaultValue 0

            let newCount = currentYellows + 1
            let events = ResizeArray<DomainEvent>(4)
            let ctx = EventContext.withDescription (defaultArg description "")

            if currentYellows >= 1 then
                events.Add(
                    DomainEvent.Emit
                        { SubTick = subTick
                          PlayerId = player.Id
                          ClubId = clubId
                          Type = YellowCard
                          Context = ctx }
                )

                events.Add(
                    DomainEvent.Emit
                        { SubTick = subTick
                          PlayerId = player.Id
                          ClubId = clubId
                          Type = RedCard
                          Context = ctx }
                )

                events.Add(DomainEvent.YellowsWrite(side, player.Id, newCount))
                events.Add(DomainEvent.SidelinedWrite(side, player.Id, SidelinedByRedCard))
                events.Add(DomainEvent.EmitSemantic(RedCardIssued player.Id))
                events.Add(DomainEvent.StoppageTimeAdd(subTick, StoppageReason.CardDelay))
            else
                events.Add(
                    DomainEvent.Emit
                        { SubTick = subTick
                          PlayerId = player.Id
                          ClubId = clubId
                          Type = YellowCard
                          Context = ctx }
                )

                events.Add(DomainEvent.YellowsWrite(side, player.Id, newCount))
                events.Add(DomainEvent.StoppageTimeAdd(subTick, StoppageReason.CardDelay))

            events.ToArray()

        | IssueRed(player, clubId) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub
            let ctx = EventContext.withDescription (defaultArg description "")

            [| DomainEvent.Emit
                   { SubTick = subTick
                     PlayerId = player.Id
                     ClubId = clubId
                     Type = RedCard
                     Context = ctx }
               DomainEvent.EmitSemantic(RedCardIssued player.Id)
               DomainEvent.SidelinedWrite(side, player.Id, SidelinedByRedCard)
               DomainEvent.StoppageTimeAdd(subTick, StoppageReason.CardDelay) |]

        | IssueInjury(player, clubId) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub

            [| DomainEvent.Emit
                   { SubTick = subTick
                     PlayerId = player.Id
                     ClubId = clubId
                     Type = MatchEventType.Injury "match"
                     Context = EventContext.empty }
               DomainEvent.SidelinedWrite(side, player.Id, SidelinedByInjury)
               DomainEvent.StoppageTimeAdd(subTick, StoppageReason.InjuryDelay 1) |]
