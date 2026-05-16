namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.TacticsConfig
open FootballEngine.Types.TeamDirectiveOps
open SimStateOps


// ── Pipeline colectivo por equipo ────────────────────────────────────────────
//
// TeamOrchestrator.tick corre a dos frecuencias distintas:
//   strategic (~30s) — WinProbability + StrategicLoop + EmergentLoops
//   reactive  (~3s)  — ReactiveLoop + DirectiveResolver + SlotRoles + Shape
//
// Output: escribe en TeamFrame. BatchDecision solo lee — no decide nada colectivo.
//
// Para agregar inteligencia colectiva nueva: identificar en qué paso del
// pipeline vive y agregar ahí. El compilador verifica que el contrato no rompa.
//
// ─────────────────────────────────────────────────────────────────────────────


module private DirectiveResolver =

    [<Literal>]
    let private MinCommitmentTicks = 90

    type private Situation =
        | JustWonBall
        | JustLostBall
        | OpponentBuilding
        | Dominating
        | Chasing
        | Balanced
        | Scramble

    let private classifySituation (state: SimState) (clubSide: ClubSide) (pressingCoordination: float) =
        let weHaveBall =
            match state.Ball.Control with
            | Controlled(side, _)
            | Receiving(side, _, _) -> side = clubSide
            | _ -> false

        let ballIsLoose =
            match state.Ball.Control with
            | Free
            | Contesting _ -> true
            | _ -> false

        if ballIsLoose then
            Scramble
        else
            let scoreDiff =
                if clubSide = HomeClub then
                    state.HomeScore - state.AwayScore
                else
                    state.AwayScore - state.HomeScore

            let justChanged = state.PossessionHistory.LastChangeTick > int state.SubTick - 30

            if justChanged && weHaveBall then JustWonBall
            elif justChanged && not weHaveBall then JustLostBall
            elif not weHaveBall && scoreDiff < 0 then Chasing
            elif weHaveBall && scoreDiff > 0 then Dominating
            elif not weHaveBall then OpponentBuilding
            else Balanced

    let resolve
        (state: SimState)
        (clubSide: ClubSide)
        (pressingCoordination: float)
        (currentDirective: TeamDirective)
        : DirectiveKind =
        let situation = classifySituation state clubSide pressingCoordination
        let isCommitted = state.SubTick - currentDirective.ActiveSince < MinCommitmentTicks * 1<subtick>

        if situation = Scramble then
            ContestBall
        elif isCommitted then
            currentDirective.Kind
        else
            let managerIntent = kindFromTactics (getTactics state clubSide)

            match situation with
            | JustWonBall -> CounterReady
            | JustLostBall -> PressingBlock
            | Chasing -> DirectAttack
            | Dominating ->
                match managerIntent with
                | DefensiveBlock -> DefensiveBlock
                | _ -> Structured
            | OpponentBuilding ->
                if pressingCoordination > 0.65 then
                    PressingBlock
                else
                    DefensiveBlock
            | Balanced -> managerIntent
            | Scramble -> ContestBall

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

module TeamOrchestrator =
    let private bothSides = [| HomeClub; AwayClub |]
    // ── Helpers internos ─────────────────────────────────────────────────────

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

    // ── Paso 1 — Read ────────────────────────────────────────────────────────
    // Lee todo lo que el orchestrator necesita saber antes de decidir.
    // Puro — no muta nada.

    [<Struct>]
    type private OrchestratorRead =
        { Cohesion: TeamCohesion
          EventRates: EventWindow.EventRates
          AvgCondition: float
          CurrentDirective: TeamDirective }

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
            EventWindow.computeRates (state.Config.Timing.EventWindowSubTicks) state.MatchEvents

        let frame = getFrame state clubSide
        let condition = avgCondition frame

        let directive =
            SimStateOps.getDirective state clubSide
            |> TeamDirectiveOps.currentDirective
            |> Option.defaultValue (TeamDirectiveOps.empty state.SubTick)

        { Cohesion = cohesion
          EventRates = rates
          AvgCondition = condition
          CurrentDirective = directive }

    // ── Paso 2 — Resolve DirectiveKind ───────────────────────────────────────
    // Inteligencia colectiva sobre qué está haciendo el equipo AHORA.
    // Para cambiar cuándo el equipo presiona, defiende, contraataca: acá.

    let private resolveKind (clubSide: ClubSide) (read: OrchestratorRead) (state: SimState) : DirectiveKind =
        DirectiveResolver.resolve state clubSide read.Cohesion.PressingCoordination read.CurrentDirective

    // ── Paso 3 — Plan por slot ───────────────────────────────────────────────
    // Dado el DirectiveKind, calcula qué hace cada jugador DENTRO del plan colectivo.
    // SlotRole, posiciones de forma, posiciones de soporte, runs, shape defensivo.
    //
    // Para agregar inteligencia en cómo el equipo se posiciona: acá.
    // Para cambiar cómo se asignan roles (pressing zonal, offside trap): SlotRoleAssigner.

    [<Struct>]
    type private TeamPlan =
        { Kind: DirectiveKind
          SlotRoles: SlotRole[]
          SupportPosX: float32[]
          SupportPosY: float32[]
          DefRoles: DefensiveRole[]
          RunnerTarget: PlayerId option
          RunType: RunType option
          RunTarget: Spatial option }

    let private buildPlan
        (subTick: int)
        (clubSide: ClubSide)
        (read: OrchestratorRead)
        (kind: DirectiveKind)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : TeamPlan =

        let team = SimStateOps.buildTeamPerspective clubSide ctx state
        let frame = team.OwnFrame
        let roster = team.OwnRoster
        let ballPos = state.Ball.Position
        let bx = float32 ballPos.X
        let by = float32 ballPos.Y
        let dir = team.AttackDir

        let tacticsCfg =
            tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

        // 3a. Roles por slot — inteligencia colectiva de pressing/defensa/ataque
        let slotRoles = SlotRoleAssigner.assign frame roster kind tacticsCfg bx by

        // 3b. Support positions — dónde quiere el equipo que estés cuando no tenés la pelota
        let basePositions = getBasePositions state clubSide
        let phase = phaseFromBallZone dir state.BallXSmooth

        let supportPosX = Array.zeroCreate<float32> frame.SlotCount
        let supportPosY = Array.zeroCreate<float32> frame.SlotCount

        BatchDecisionSupport.computeSupportPositionsInto
            team
            ballPos
            state.Ball.Control
            phase
            tacticsCfg
            tacticsCfg.Width
            basePositions
            supportPosX
            supportPosY

        // 3c. Run assignment — quién hace el movimiento de ruptura
        let influence =
            if clubSide = HomeClub then
                state.HomeInfluenceFrame
            else
                state.AwayInfluenceFrame

        let runnerId, runType, runTarget =
            BatchDecisionSupport.pickRunner team ballPos state.Ball.Control (getEmergentState state clubSide) influence

        // 3d. Defensive shape — FirstDefender, Cover, Marker por slot
        let cFrame =
            if clubSide = HomeClub then
                state.HomeCognitiveFrame
            else
                state.AwayCognitiveFrame

        let defRolesRaw =
            BatchDecisionSupport.computeDefensiveShape
                team
                ballPos
                cFrame
                subTick
                (int (getTeam state clubSide).TransitionPressExpiry)

        let defRoles = Array.map (fun (r: DefensiveRole) -> r) defRolesRaw

        { Kind = kind
          SlotRoles = slotRoles
          SupportPosX = supportPosX
          SupportPosY = supportPosY
          DefRoles = defRoles
          RunnerTarget = runnerId
          RunType = runType
          RunTarget = runTarget }

    // ── Paso 4 — Adapt (EmergentLoops) ──────────────────────────────────────
    // Lee qué está pasando en el partido y actualiza el estado emergente del equipo.
    // El EmergentState afecta los scores de MovementScorer en el próximo ciclo.
    //
    // Para que el equipo aprenda y ajuste más patrones: agregar funciones en EmergentLoops.

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

    // ── Paso 5 — Write ───────────────────────────────────────────────────────
    // Única función que muta SimState. Escribe el TeamPlan en el TeamFrame
    // y actualiza la TeamDirective.
    // BatchDecision lee estos arrays — no los calcula.

    let private writePlan
        (subTick: int)
        (clubSide: ClubSide)
        (read: OrchestratorRead)
        (plan: TeamPlan)
        (state: SimState)
        : unit =

        let frame = getFrame state clubSide
        let emergent = getEmergentState state clubSide

        let tacticsCfg =
            tacticsConfig (getTactics state clubSide) (getInstructions state clubSide)

        // 5a. SlotRoles al frame — BatchDecision los lee para sesgar scores individuales
        for i = 0 to frame.SlotCount - 1 do
            frame.SlotRoles[i] <- plan.SlotRoles[i]

        // 5b. Support positions al frame
        for i = 0 to frame.SlotCount - 1 do
            frame.SupportPositionX[i] <- plan.SupportPosX[i]
            frame.SupportPositionY[i] <- plan.SupportPosY[i]

        // 5c. Defensive roles al frame
        for i = 0 to frame.SlotCount - 1 do
            frame.DefensiveRole[i] <- byte plan.DefRoles[i]

        // 5d. TeamDirective actualizada — Kind + Params frescos desde EmergentState
        let newDirective =
            { read.CurrentDirective with
                Kind = plan.Kind
                Params = defaultParams tacticsCfg emergent
                TargetRunner = plan.RunnerTarget
                RunType = plan.RunType
                RunTarget = plan.RunTarget
                ActiveSince =
                    if plan.Kind <> read.CurrentDirective.Kind then
                        subTick * 1<subtick>
                    else
                        read.CurrentDirective.ActiveSince }

        setDirective state clubSide (TeamDirectiveState.Active newDirective)

    // ── Tick principal ───────────────────────────────────────────────────────
    //
    // Frecuencias:
    //   strategic (~30s): WinProbability + StrategicLoop + EmergentLoops + Plan completo
    //   reactive  (~3s):  ReactiveLoop + Plan completo (sin recalcular WinProbability)
    //
    // El Stepper llama esto — no sabe qué loop corrió adentro.

    let tick (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : unit =
        let isStrategicTick = subTick % (clock.SubTicksPerSecond * 30) = 0
        let isReactiveTick  = subTick % (clock.SubTicksPerSecond * 3) = 0
        for clubSide in bothSides do
            let read = readTeam clubSide ctx state

            if isStrategicTick then
                // Ciclo estratégico: evalúa situación global, adapta estado emergente
                let mode = StrategicLoop.run ctx state clock

                adaptEmergent clubSide read state

                let kind =
                    match mode with
                    | ExecutingPlan _ -> resolveKind clubSide read state
                    | Recovering(_, target) -> target

                let plan = buildPlan subTick clubSide read kind ctx state clock
                writePlan subTick clubSide read plan state

            elif isReactiveTick then
                // Ciclo reactivo: detecta desviación, ajusta si es crítico
                let deviation = ReactiveLoop.run ctx state clock

                let kind =
                    match deviation with
                    | Critical _ ->
                        let mode = StrategicLoop.run ctx state clock

                        match mode with
                        | Recovering(_, target) -> target
                        | ExecutingPlan _ -> resolveKind clubSide read state
                    | _ -> resolveKind clubSide read state

                let plan = buildPlan subTick clubSide read kind ctx state clock
                writePlan subTick clubSide read plan state
