namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Referee
open FootballEngine.SimStateOps
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.SchedulingTypes



module SetPieceAgent =

    let run
        (kind: SetPieceKind)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : PlayerResult =
        let result =
            match kind with
            | SetPieceKind.FreeKick -> SetPlayAction.resolveFreeKick state.SubTick ctx state clock
            | SetPieceKind.Corner -> SetPlayAction.resolveCorner state.SubTick ctx state clock
            | SetPieceKind.ThrowIn -> SetPlayAction.resolveThrowIn state.SubTick ctx state clubSide clock
            | SetPieceKind.GoalKick ->
                let isHome = clubSide = HomeClub
                let kickingClub = if isHome then HomeClub else AwayClub
                let kickingClubId = if isHome then ctx.Home.Id else ctx.Away.Id
                let frame = getFrame state kickingClub
                let roster = getRoster ctx kickingClub
                let pc = ctx.Config.Pass

                let gkX =
                    if isHome then
                        GoalAreaDepth
                    else
                        PitchLength - GoalAreaDepth

                let centerY = PitchWidth / 2.0

                state.Ball <-
                    { state.Ball with
                        Position =
                            { defaultSpatial gkX centerY with
                                Vx = 0.0<meter / second>
                                Vy = 0.0<meter / second>
                                Vz = 0.0<meter / second> }
                        Control = Free }

                let gkIdx =
                    let mutable idx = -1

                    for i = 0 to frame.SlotCount - 1 do
                        match frame.Physics.Occupancy[i] with
                        | OccupancyKind.Active _ when roster.Players[i].Position = GK -> idx <- i
                        | _ -> ()

                    idx

                if gkIdx < 0 then
                    ActionResult.empty
                else
                    let gk = roster.Players[gkIdx]
                    let gkXf = float frame.Physics.PosX[gkIdx] * 1.0<meter>
                    let gkYf = float frame.Physics.PosY[gkIdx] * 1.0<meter>

                    let targetX, targetY =
                        match nearestActiveSlotInFrameExcluding frame gkIdx gkXf gkYf with
                        | ValueSome tmIdx ->
                            float frame.Physics.PosX[tmIdx] * 1.0<meter>, float frame.Physics.PosY[tmIdx] * 1.0<meter>
                        | ValueNone ->
                            (if isHome then
                                 ctx.Config.SetPiece.GoalKickFallbackDistHome
                             else
                                 ctx.Config.SetPiece.GoalKickFallbackDistAway),
                            centerY

                    let dx = targetX - gkXf
                    let dy = targetY - gkYf
                    let dist = sqrt (dx * dx + dy * dy)
                    let speed = pc.Speed

                    let vx =
                        if dist > 0.1<meter> then
                            dx / dist * speed
                        else
                            0.0<meter / second>

                    let vy =
                        if dist > 0.1<meter> then
                            dy / dist * speed
                        else
                            0.0<meter / second>

                    let flightTime =
                        if speed > 0.0<meter / second> then
                            dist / speed
                        else
                            0.5<second>

                    let arrivalSubTick =
                        state.SubTick
                        + int (float (flightTime / 1.0<second>) * float clock.SubTicksPerSecond)

                    let trajectory =
                        { OriginX = gkXf
                          OriginY = gkYf
                          TargetX = targetX
                          TargetY = targetY
                          LaunchSubTick = state.SubTick
                          EstimatedArrivalSubTick = arrivalSubTick
                          KickerId = gk.Id
                          PeakHeight = 0.0<meter>
                          Intent = Aimed(gk.Id, gk.Id, 0.5, RegularPass) }

                    state.Ball <-
                        { state.Ball with
                            LastTouchBy = Some gk.Id
                            Control = Airborne
                            Position =
                                { state.Ball.Position with
                                    Vx = vx
                                    Vy = vy
                                    Vz = 0.0<meter / second> }
                            Trajectory = Some trajectory }

                    ActionResult.ofEvents [ createEvent state.SubTick gk.Id kickingClubId MatchEventType.GoalKick ]

            | SetPieceKind.Penalty ->
                let roster = getRoster ctx clubSide
                let kickerPlayer = roster.Players[0]

                let penaltyScored =
                    SetPlayAction.resolvePenalty state.SubTick ctx state kickerPlayer clubSide clock

                if penaltyScored then
                    { Events = []
                      PendingRefereeActions = [ConfirmGoal(clubSide, Some kickerPlayer.Id, false)]
                      Outputs = [] }
                else
                    ActionResult.empty

            | SetPieceKind.KickOff ->
                let centerX = HalfwayLineX
                let centerY = PitchWidth / 2.0
                let kickOffSide = clubSide
                let isHomeKickOff = kickOffSide = HomeClub
                let kickingClub = if isHomeKickOff then HomeClub else AwayClub
                let kickingClubId = if isHomeKickOff then ctx.Home.Id else ctx.Away.Id
                let frame = getFrame state kickingClub
                let roster = getRoster ctx kickingClub
                let pc = ctx.Config.Pass

                state.Ball <-
                    { state.Ball with
                        Position =
                            { X = centerX
                              Y = centerY
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                        Control = Free }

                let kickerIdx =
                    let rec findPos pred i =
                        if i >= frame.SlotCount then
                            None
                        else
                            match frame.Physics.Occupancy[i] with
                            | OccupancyKind.Active _ when pred roster.Players[i] -> Some i
                            | _ -> findPos pred (i + 1)

                    findPos (fun p -> p.Position = ST) 0
                    |> Option.orElse (findPos (fun p -> p.Position = AMC) 0)
                    |> Option.orElse (findPos (fun p -> p.Position <> GK) 0)

                match kickerIdx with
                | None -> ActionResult.empty
                | Some kIdx ->
                    let kicker = roster.Players[kIdx]

                    let partnerIdx =
                        let rec findPartner i =
                            if i >= frame.SlotCount then
                                None
                            else
                                match frame.Physics.Occupancy[i] with
                                | OccupancyKind.Active _ when i <> kIdx ->
                                    let p = roster.Players[i]

                                    if p.Position = AMC || p.Position = MC || p.Position = ST then
                                        Some i
                                    else
                                        findPartner (i + 1)
                                | _ -> findPartner (i + 1)

                        findPartner 0

                    let targetX, targetY =
                        match partnerIdx with
                        | Some pIdx ->
                            float frame.Physics.PosX[pIdx] * 1.0<meter>, float frame.Physics.PosY[pIdx] * 1.0<meter>
                        | None ->
                            centerX + ctx.Config.SetPiece.KickOffPartnerOffsetX,
                            centerY + ctx.Config.SetPiece.KickOffPartnerOffsetY

                    let partnerId =
                        partnerIdx
                        |> Option.map (fun i -> roster.Players[i].Id)
                        |> Option.defaultValue kicker.Id

                    state.Ball <-
                        { state.Ball with
                            LastTouchBy = Some kicker.Id }

                    let dx = targetX - centerX
                    let dy = targetY - centerY
                    let dist = sqrt (dx * dx + dy * dy)
                    let speed = pc.Speed

                    if dist > 0.1<meter> then
                        withBallVelocity (dx / dist * speed) (dy / dist * speed) 0.0<meter / second> state

                    SimStateOps.emitSemantic SemanticEvent.BallLoose state

                    ActionResult.ofEvents
                        [ { SubTick = state.SubTick
                            PlayerId = kicker.Id
                            ClubId = kickingClubId
                            Type = MatchEventType.KickOff
                            Context = EventContext.empty } ]

        { Events = result.Events
          Transition = Some MatchFlow.Live
          PendingRefereeActions = [] }
