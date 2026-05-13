namespace FootballEngine

open FootballEngine.Domain


open FootballEngine.Player.Perception
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps
open MatchSpatial



module AgentContext =

    let build
        (me: Player)
        (profile: BehavioralProfile)
        (meIdx: int)
        (team: TeamPerspective)
        (previousIntent: MovementIntent voption)
        (state: SimState)
        (clock: SimulationClock)
        (ctx: MatchContext)
        (decision: DecisionConfig)
        (buildUp: BuildUpConfig)
        (cFrame: CognitiveFrame option)
        (visibilityMask: VisibilityMask voption)
        (influence: InfluenceTypes.InfluenceFrame)
        : AgentContext =
        let ballState = state.Ball
        let bX = ballState.Position.X
        let dir = team.AttackDir
        let phase = phaseFromBallZone dir bX
        let zone = ofBallX bX dir

        let gd =
            if team.ClubSide = HomeClub then
                state.HomeScore - state.AwayScore
            else
                state.AwayScore - state.HomeScore

        let teamHasBall =
            match ballState.Control with
            | Controlled(side, _)
            | Receiving(side, _, _) -> side = team.ClubSide
            | Contesting(side) -> side = team.ClubSide
            | Airborne
            | Free -> false

        let tacticsCfg =
            tacticsConfig (getTactics state team.ClubSide) (getInstructions state team.ClubSide)

        let clubId =
            if team.ClubSide = HomeClub then
                ctx.Home.Id
            else
                ctx.Away.Id

        let urgency = matchUrgency clubId ctx state clock

        let mentalState =
            { ComposureLevel = float team.OwnFrame.ComposureLevel[meIdx]
              ConfidenceLevel = float team.OwnFrame.ConfidenceLevel[meIdx]
              FocusLevel = float team.OwnFrame.FocusLevel[meIdx]
              AggressionLevel = float team.OwnFrame.AggressionLevel[meIdx]
              RiskTolerance = float team.OwnFrame.RiskTolerance[meIdx] }

        let myX32 = team.OwnFrame.Physics.PosX[meIdx]
        let myY32 = team.OwnFrame.Physics.PosY[meIdx]

        let myPos =
            { X = float myX32 * 1.0<meter>
              Y = float myY32 * 1.0<meter>
              Z = 0.0<meter>
              Vx = float team.OwnFrame.Physics.VelX[meIdx] * 1.0<meter / second>
              Vy = float team.OwnFrame.Physics.VelY[meIdx] * 1.0<meter / second>
              Vz = 0.0<meter / second> }

        let nearestTMIdx, nearestOppIdx, bestPassTargetIdx, bestPassTargetPos =
            match visibilityMask with
            | ValueSome mask ->
                let oppIdx, _ =
                    Perception.resolveNearestOpponentThroughMask myPos team.OppFrame mask

                let passTarget =
                    Perception.resolveBestPassTargetThroughMask
                        meIdx
                        team.OwnFrame
                        team.OwnRoster
                        team.OppFrame
                        dir
                        mask

                let bpIdx, bpPos =
                    match passTarget with
                    | ValueSome(idx, sp) -> ValueSome idx, ValueSome sp
                    | ValueNone -> ValueNone, ValueNone

                ValueNone, oppIdx, bpIdx, bpPos
            | ValueNone ->
                match cFrame with
                | Some cf ->
                    let tmIdx =
                        if cf.NearestTeammateIdx[meIdx] >= 0s then
                            ValueSome(int cf.NearestTeammateIdx[meIdx])
                        else
                            ValueNone

                    let oppIdx =
                        if cf.NearestOpponentIdx[meIdx] >= 0s then
                            ValueSome(int cf.NearestOpponentIdx[meIdx])
                        else
                            ValueNone

                    let bpIdx =
                        if cf.BestPassTargetIdx[meIdx] >= 0s then
                            ValueSome(int cf.BestPassTargetIdx[meIdx])
                        else
                            ValueNone

                    tmIdx, oppIdx, bpIdx, cf.BestPassTargetPos[meIdx]
                | None ->
                    let mutable nTM: int voption = ValueNone
                    let mutable nOpp: int voption = ValueNone
                    let mutable minDistTM = System.Single.MaxValue
                    let mutable minDistOpp = System.Single.MaxValue

                    for i = 0 to team.OwnFrame.SlotCount - 1 do
                        if i <> meIdx then
                            match team.OwnFrame.Physics.Occupancy[i] with
                            | OccupancyKind.Active _ ->
                                let dx = team.OwnFrame.Physics.PosX[i] - myX32
                                let dy = team.OwnFrame.Physics.PosY[i] - myY32
                                let d = dx * dx + dy * dy

                                if d < minDistTM then
                                    minDistTM <- d
                                    nTM <- ValueSome i
                            | _ -> ()

                    for i = 0 to team.OppFrame.SlotCount - 1 do
                        match team.OppFrame.Physics.Occupancy[i] with
                        | OccupancyKind.Active _ ->
                            let dx = team.OppFrame.Physics.PosX[i] - myX32
                            let dy = team.OppFrame.Physics.PosY[i] - myY32
                            let d = dx * dx + dy * dy

                            if d < minDistOpp then
                                minDistOpp <- d
                                nOpp <- ValueSome i
                        | _ -> ()

                    let bestPassTarget =
                        findBestPassTargetFrame meIdx team.OwnFrame team.OwnRoster team.OppFrame dir

                    let bpIdx, bpPos =
                        match bestPassTarget with
                        | ValueSome(idx, sp) -> ValueSome idx, ValueSome sp
                        | ValueNone -> ValueNone, ValueNone

                    nTM, nOpp, bpIdx, bpPos

        let goalX = if dir = LeftToRight then PitchLength else 0.0<meter>

        let dist = abs (myPos.X - goalX)

        { MeIdx = meIdx
          Me = me
          Profile = profile
          MentalState = mentalState
          MyCondition = team.OwnFrame.Condition[meIdx]
          MyPos = myPos
          BallState = ballState
          Team = team
          TeamHasBall = teamHasBall
          Phase = phase
          Zone = zone
          NearestTeammateIdx = nearestTMIdx
          NearestOpponentIdx = nearestOppIdx
          BestPassTargetIdx = bestPassTargetIdx
          BestPassTargetPos = bestPassTargetPos
          BallCarrierOppIdx =
            match cFrame with
            | Some cf -> cf.BallCarrierOppIdx
            | None -> -1s
          DistToGoal = dist
          GoalDiff = gd
          Minute = int (SimulationClock.subTicksToSeconds clock state.SubTick / 60.0)
          Urgency = urgency
          Tactics = tacticsCfg
          Decision = decision
          BuildUp = buildUp
          Dribble = state.Config.Dribble
          PreviousIntent = previousIntent
          VisibilityMask = visibilityMask
          CurrentSubTick = state.SubTick
          TransitionPressExpiry = (getTeam state team.ClubSide).TransitionPressExpiry
          Influence = influence }
