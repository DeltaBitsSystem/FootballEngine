namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.TacticsConfig
open FootballEngine.Types.TeamDirectiveOps
open SimStateOps


module BatchDecisionSupport =

    let computeSupportPositionsInto
        (team: TeamPerspective)
        (_ballPos: Spatial)
        (ballControl: BallControl)
        (_phase: MatchPhase)
        (_tactics: TacticsConfig)
        (desiredWidth: float)
        (basePositions: Spatial[])
        (targetX: float32[])
        (targetY: float32[])
        : unit =
        let frame = team.OwnFrame
        let n = frame.SlotCount

        let ballCarrierId =
            match ballControl with
            | Controlled(_, pid)
            | Receiving(_, pid, _) -> Some pid
            | _ -> None

        for i = 0 to n - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let px = basePositions[i].X
                let py = basePositions[i].Y

                let isCarrier =
                    match ballCarrierId with
                    | Some pid -> team.OwnRoster.Players[i].Id = pid
                    | None -> false

                if isCarrier then
                    targetX[i] <- float32 px
                    targetY[i] <- float32 py
                else
                    let forwardX = if team.AttackDir = LeftToRight then 1.0 else -1.0
                    let baseX = px + 8.0<meter> * forwardX
                    let centerY = 34.0<meter>
                    let widthOffset = (py - centerY) * desiredWidth
                    targetX[i] <- float32 (clamp baseX 2.0<meter> 98.0<meter>)
                    targetY[i] <- float32 (clamp (centerY + widthOffset) 2.0<meter> 98.0<meter>)
            | _ ->
                targetX[i] <- 52.5f
                targetY[i] <- 34.0f

    let computeSupportPositions
        (team: TeamPerspective)
        (ballPos: Spatial)
        (ballControl: BallControl)
        (phase: MatchPhase)
        (tactics: TacticsConfig)
        (desiredWidth: float)
        (basePositions: Spatial[])
        : Spatial[] =
        let frame = team.OwnFrame
        let resultX = Array.zeroCreate<float32> frame.SlotCount
        let resultY = Array.zeroCreate<float32> frame.SlotCount
        computeSupportPositionsInto team ballPos ballControl phase tactics desiredWidth basePositions resultX resultY
        Array.init frame.SlotCount (fun i ->
            defaultSpatial (float resultX[i] * 1.0<meter>) (float resultY[i] * 1.0<meter>))

    let pickRunner
        (team: TeamPerspective)
        (ballPos: Spatial)
        (ballControl: BallControl)
        (emergent: EmergentState)
        (influence: InfluenceTypes.InfluenceFrame)
        : PlayerId option * RunType option * Spatial option =
        let frame = team.OwnFrame
        let roster = team.OwnRoster

        let ballCarrierId =
            match ballControl with
            | Controlled(_, pid)
            | Receiving(_, pid, _) -> Some pid
            | _ -> None

        let dir = team.AttackDir
        let forwardX = if dir = LeftToRight then 1.0 else -1.0
        let mutable bestCellIdx = 0
        let mutable bestCellScore = -1.0

        for cell = 0 to InfluenceTypes.GridSize - 1 do
            let s =
                InfluenceTypes.scoreCellRaw cell influence (float32 ballPos.X) (float32 ballPos.Y) dir

            if s > bestCellScore then
                bestCellScore <- s
                bestCellIdx <- cell

        let cx, cy = InfluenceTypes.cellToCenter bestCellIdx

        let bestCellTarget =
            defaultSpatial
                (clamp (float cx * 1.0<meter>) 2.0<meter> 98.0<meter>)
                (clamp (float cy * 1.0<meter>) 2.0<meter> 98.0<meter>)

        let mutable bestPlayerId: PlayerId option = None
        let mutable bestRunType: RunType option = None
        let mutable bestScore = -1.0

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let player = roster.Players[i]

                if ballCarrierId <> Some player.Id then
                    let px = float frame.Physics.PosX[i] * 1.0<meter>
                    let isAdvanced = (px - 52.5<meter>) * forwardX > 5.0<meter>

                    let score =
                        float player.Mental.WorkRate / 20.0 * 0.3
                        + float player.Mental.Positioning / 20.0 * 0.4
                        + (if isAdvanced then 0.5 else 0.0)

                    if score > bestScore then
                        bestScore <- score
                        bestPlayerId <- Some player.Id

                        bestRunType <-
                            Some(
                                match player.Position with
                                | ST
                                | AMC -> DeepRun
                                | AML
                                | ML
                                | WBL
                                | AMR
                                | MR
                                | WBR -> DriftWide
                                | MC -> CheckToBall
                                | DL
                                | DR -> OverlapRun
                                | _ -> DeepRun
                            )
            | _ -> ()

        bestPlayerId, bestRunType, Some bestCellTarget

    let computeDefensiveShape
        (team: TeamPerspective)
        (ballPos: Spatial)
        (cFrame: CognitiveFrame)
        (currentSubTick: int)
        (transitionExpiry: int)
        : DefensiveRole[] =
        let frame = team.OwnFrame
        let n = frame.SlotCount
        let assignments = Array.create n DefensiveRole.Marker

        let targetX, targetY =
            if cFrame.BallCarrierOppIdx >= 0s then
                let bcIdx = int cFrame.BallCarrierOppIdx
                team.OppFrame.Physics.PosX[bcIdx], team.OppFrame.Physics.PosY[bcIdx]
            else
                float32 ballPos.X, float32 ballPos.Y

        let mutable nearestIdx = -1
        let mutable nearestDistSq = System.Single.MaxValue

        for i = 0 to n - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let dx = frame.Physics.PosX[i] - targetX
                let dy = frame.Physics.PosY[i] - targetY
                let dSq = dx * dx + dy * dy

                if dSq < nearestDistSq then
                    nearestDistSq <- dSq
                    nearestIdx <- i
            | _ -> ()

        if nearestIdx >= 0 then
            assignments[nearestIdx] <- DefensiveRole.FirstDefender
            let maxCover = if currentSubTick < transitionExpiry then 2 else 3
            let mutable coverCount = 0

            for i = 0 to n - 1 do
                if i <> nearestIdx && coverCount < maxCover then
                    match frame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ when team.OwnRoster.Players[i].Position <> GK ->
                        assignments[i] <- DefensiveRole.Cover
                        coverCount <- coverCount + 1
                    | _ -> ()

        assignments

module TeamDirector =
    let private bothSides = [| HomeClub; AwayClub |]

    let private avgCondition (frame: TeamFrame) : float =
        let mutable total = 0
        let mutable count = 0

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                total <- total + int frame.Condition[i]
                count <- count + 1
            | _ -> ()

        if count > 0 then float total / float count else 50.0

    [<Struct>]
    type private OrchestratorRead =
        { Cohesion: TeamCohesion
          EventRates: EventWindow.EventRates
          AvgCondition: float
          CurrentDirective: TeamDirective
          Blackboard: TeamBlackboard
          CollectiveAction: CollectiveAction }

    let private readTeam (clubSide: ClubSide) (ctx: MatchContext) (state: SimState) : OrchestratorRead =
        let chemistry =
            if clubSide = HomeClub then
                ctx.HomeChemistry
            else
                ctx.AwayChemistry

        let roster = getRoster ctx clubSide

        let cohesion =
            ChemistryTracker.calculateCohesion chemistry.Familiarity roster.Players.Length

        let rates, _ =
            EventWindow.computeRates (state.Config.Timing.EventWindowSubTicks) state.MatchEvents (int state.SubTick)

        let frame = getFrame state clubSide
        let condition = avgCondition frame

        let directive =
            SimStateOps.getDirective state clubSide
            |> TeamDirectiveOps.currentDirective
            |> Option.defaultValue (TeamDirectiveOps.empty state.SubTick)

        let blackboard = BlackboardBuilder.build state clubSide ctx

        let prevAction = (getTeam state clubSide).LastCollectiveAction
        let emergent = getEmergentState state clubSide
        let action = UtilityActions.resolve blackboard prevAction emergent state clubSide ctx

        let teamState = getTeam state clubSide
        teamState.Blackboard <- blackboard
        teamState.LastCollectiveAction <- Some action

        { Cohesion = cohesion
          EventRates = rates
          AvgCondition = condition
          CurrentDirective = directive
          Blackboard = blackboard
          CollectiveAction = action }

    let private resolveKind (read: OrchestratorRead) : DirectiveKind =
        UtilityActions.toDirectiveKind read.CollectiveAction

    let private updateDirective
        (state: SimState)
        (clubSide: ClubSide)
        (kind: DirectiveKind)
        (subTick: int)
        : unit =

        let emergent = getEmergentState state clubSide
        let tacticsCfg =
            tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

        let currentDirective =
            SimStateOps.getDirective state clubSide
            |> TeamDirectiveOps.currentDirective
            |> Option.defaultValue (TeamDirectiveOps.empty (subTick * 1<subtick>))

        let newDirective =
            { currentDirective with
                Kind = kind
                Params = defaultParams tacticsCfg emergent
                ActiveSince =
                    if kind <> currentDirective.Kind then
                        subTick * 1<subtick>
                    else
                        currentDirective.ActiveSince }

        let wrapped =
            match SimStateOps.getDirective state clubSide with
            | TeamDirectiveState.Suspended _ -> TeamDirectiveState.Suspended newDirective
            | TeamDirectiveState.Transitioning(fromDir, _, progress) ->
                TeamDirectiveState.Transitioning(fromDir, newDirective, progress)
            | _ -> TeamDirectiveState.Active newDirective

        setDirective state clubSide wrapped

    let private adaptEmergent (clubSide: ClubSide) (read: OrchestratorRead) (state: SimState) : unit =
        let current = getEmergentState state clubSide
        let r = read.EventRates

        let updated =
            current
            |> EmergentLoops.updateCompactness r.ShortPassRate
            |> EmergentLoops.updatePressing r.PressRate
            |> EmergentLoops.updateWingPlay r.FlankRate
            |> EmergentLoops.updateFatigueSpiral read.AvgCondition 0

        setEmergentState state clubSide updated

    let tick (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : unit =
        let time = Scheduler.buildMatchTime state clock
        let strategicFreq = EveryMinute(30<matchMin>, 0<matchMin>)
        let reactiveFreq  = EveryMinute(3<matchMin>, 0<matchMin>)
        let isStrategicTick = Scheduler.shouldRun strategicFreq time state.PendingSemanticEvents
        let isReactiveTick  = Scheduler.shouldRun reactiveFreq time state.PendingSemanticEvents

        for clubSide in bothSides do
            let read = readTeam clubSide ctx state

            if isStrategicTick then
                let mode = StrategicLoop.run ctx state clock
                adaptEmergent clubSide read state

                let kind =
                    match mode with
                    | ExecutingPlan _ -> resolveKind read
                    | Recovering(_, target) -> target

                updateDirective state clubSide kind subTick

            elif isReactiveTick then
                let deviation = ReactiveLoop.run ctx state clock

                let kind =
                    match deviation with
                    | Critical _ ->
                        let mode = StrategicLoop.run ctx state clock
                        match mode with
                        | Recovering(_, target) -> target
                        | ExecutingPlan _ -> resolveKind read
                    | _ -> resolveKind read

                updateDirective state clubSide kind subTick
