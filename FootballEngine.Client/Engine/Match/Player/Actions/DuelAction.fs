namespace FootballEngine.Player.Actions

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Decision

open FootballEngine.Referee
open FootballEngine.SimStateOps
open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract



module DuelAction =

    let private fatigueMultiplier (config: DuelConfig) (condition: int) : float =
        let norm = normaliseCondition condition
        let threshold = normaliseCondition config.FatigueThreshold

        if norm < threshold then
            let deficit = threshold - norm
            exp (-deficit * config.FatigueDecay / normaliseCondition 1)
        else
            1.0

    let private findBestContestant
        (frame: TeamFrame)
        (ballX: float<meter>)
        (ballY: float<meter>)
        (contestRadius: float<meter>)
        : int voption =

        let mutable bestIdx = ValueNone
        let mutable bestScore = -1.0

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let px = float frame.Physics.PosX[i] * 1.0<meter>
                let py = float frame.Physics.PosY[i] * 1.0<meter>
                let dx = px - ballX
                let dy = py - ballY
                let dist = sqrt (dx * dx + dy * dy)

                if dist <= contestRadius then
                    let intentBonus =
                        match frame.Intent.Kind[i] with
                        | IntentKind.PressBall
                        | IntentKind.RecoverBall -> 0.5
                        | IntentKind.TackleAttempt -> 0.3
                        | _ -> 0.0

                    let distPenalty = float dist / float contestRadius
                    let score = 1.0 - distPenalty + intentBonus

                    if score > bestScore then
                        bestScore <- score
                        bestIdx <- ValueSome i
            | _ -> ()

        bestIdx

    let resolve
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : ActionResult * RefereeAction list =
        let actx = ActionContext.build ctx state
        let attClubId = actx.Att.ClubId
        let defClubId = actx.Def.ClubId
        let bPos = state.Ball.Position
        let bX, bY = bPos.X, bPos.Y

        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide
        let defRoster = getRoster ctx actx.Def.ClubSide

        match state.Ball.Control with
        | Receiving _ -> ActionResult.empty, []
        | _ ->

            let contestRadius = 8.0<meter>

            match findBestContestant attFrame bX bY contestRadius,
                  findBestContestant defFrame bX bY contestRadius with
            | ValueSome attIdx, ValueSome defIdx ->
                let attP = attRoster.Players[attIdx]
                let defP = defRoster.Players[defIdx]
                let attCond = int attFrame.Condition[attIdx]
                let defCond = int defFrame.Condition[defIdx]

                let cfg = ctx.Config.Duel

                let attScore =
                    ActionMath.evalPerformance
                        PerformanceDefaults.duelPerformanceConfig
                        attP.Technical.Dribbling
                        attCond
                        0

                let defScore =
                    ActionMath.evalPerformance
                        PerformanceDefaults.duelPerformanceConfig
                        defP.Technical.Tackling
                        defCond
                        0

                let aggressionNorm = float defFrame.AggressionLevel[defIdx]
                let foulChance = 0.06 + aggressionNorm * 0.10

                if bernoulli foulChance then
                    let contestBall =
                        { state.Ball with
                            Control = Contesting(actx.Def.ClubSide) }

                    let mDelta = forwardX actx.Att.AttackDir * (-ctx.Config.Tackle.FoulMomentum)
                    let reckless = bernoulli (aggressionNorm * 0.3)
                    let excessiveForce = bernoulli (aggressionNorm * 0.15)

                    let foulCtx =
                        FoulAnalysis.assess
                            bX
                            bY
                            actx.Att.AttackDir
                            actx.Att.ClubSide
                            defP.Mental.Aggression
                            reckless
                            excessiveForce
                            attFrame
                            attRoster
                            defFrame
                            defRoster

                    let severity = FoulAnalysis.classifySeverity foulCtx

                    let yellows =
                        getYellows state actx.Def.ClubSide
                        |> Map.tryFind defP.Id
                        |> Option.defaultValue 0

                    let side = actx.Def.ClubSide
                    let clubId = if side = HomeClub then ctx.Home.Id else ctx.Away.Id

                    let cardActions, cardDomainEvents =
                        match FoulAnalysis.decideCard severity yellows with
                        | Some FoulAnalysis.CardDecision.Yellow ->
                            [ IssueYellow(defP, clubId) ],
                            [ DomainEvent.EmitSemantic(SemanticEvent.FoulOccurred(defP.Id, attP.Id)) ]
                        | Some FoulAnalysis.CardDecision.Red ->
                            [ IssueRed(defP, clubId) ],
                            [ DomainEvent.EmitSemantic(SemanticEvent.FoulOccurred(defP.Id, attP.Id)) ]
                        | None -> [], []

                    let events = ResizeArray<DomainEvent>(8)
                    events.Add(DomainEvent.BallUpdate contestBall)
                    events.Add(DomainEvent.MomentumDelta mDelta)
                    for de in cardDomainEvents do events.Add(de)
                    events.Add(DomainEvent.Emit { SubTick = subTick; PlayerId = defP.Id; ClubId = defClubId; Type = FoulCommitted; Context = EventContext.empty })
                    { Events = events.ToArray() }, cardActions
                else
                    let diff = float attScore - float defScore
                    let aX = float attFrame.Physics.PosX[attIdx] * 1.0<meter>
                    let aY = float attFrame.Physics.PosY[attIdx] * 1.0<meter>
                    let dX = float defFrame.Physics.PosX[defIdx] * 1.0<meter>
                    let dY = float defFrame.Physics.PosY[defIdx] * 1.0<meter>

                    if logisticBernoulli diff cfg.DuelSteepness then
                        let nx, ny = PitchMath.jitter bX bY aX aY 0.5 cfg.JitterWin cfg.JitterWin

                        let newControl =
                            { state.Ball with
                                Position =
                                    { state.Ball.Position with
                                        X = nx
                                        Y = ny }
                                Control = Controlled(actx.Att.ClubSide, attP.Id)
                                LastTouchBy = Some attP.Id
                                PendingOffsideSnapshot = None
                                GKHoldSinceSubTick = if attP.Position = GK then Some(subTick * 1<subtick>) else None
                                PlayerHoldSinceSubTick = if attP.Position <> GK then Some(subTick * 1<subtick>) else None
                                Trajectory = None }

                        let mDelta = forwardX actx.Att.AttackDir * cfg.MomentumBonus

                        let events = ResizeArray<DomainEvent>(8)
                        events.Add(DomainEvent.Emit { SubTick = subTick; PlayerId = attP.Id; ClubId = attClubId; Type = DribbleSuccess; Context = EventContext.empty })
                        events.Add(DomainEvent.BallUpdate newControl)
                        events.Add(DomainEvent.MomentumDelta mDelta)
                        events.Add(DomainEvent.MemoryWrite(actx.Att.ClubSide, attIdx, MemoryWriteKind.DuelResult(true, defIdx)))
                        events.Add(DomainEvent.MemoryWrite(actx.Att.ClubSide, attIdx, MemoryWriteKind.PassSuccess))
                        events.Add(DomainEvent.EmitSemantic(SemanticEvent.BallSecured(actx.Att.ClubSide, attP.Id)))
                        { Events = events.ToArray() }, []
                    elif logisticBernoulli (-diff) cfg.DuelSteepness then
                        let nx, ny = PitchMath.jitter bX bY dX dY 0.5 cfg.JitterRecover cfg.JitterRecover

                        let looseBallJittered =
                            { state.Ball with
                                Position =
                                    { state.Ball.Position with
                                        X = nx
                                        Y = ny }
                                Control = Free
                                PendingOffsideSnapshot = None
                                GKHoldSinceSubTick = None
                                PlayerHoldSinceSubTick = None
                                Trajectory = None }

                        let events = ResizeArray<DomainEvent>(8)
                        events.Add(DomainEvent.Emit { SubTick = subTick; PlayerId = attP.Id; ClubId = attClubId; Type = DribbleFail; Context = EventContext.empty })
                        events.Add(DomainEvent.BallUpdate looseBallJittered)
                        events.Add(DomainEvent.MomentumDelta -1.0)
                        events.Add(DomainEvent.MemoryWrite(actx.Att.ClubSide, attIdx, MemoryWriteKind.DuelResult(false, defIdx)))

                        match state.Ball.Control with
                        | Controlled(side, pid)
                        | Receiving(side, pid, _) ->
                            events.Add(DomainEvent.EmitSemantic(SemanticEvent.BallLost(side, pid)))
                            for run in SimStateOps.getActiveRuns state side do
                                events.Add(DomainEvent.ExpireRun(side, run.PlayerId))
                        | _ -> ()

                        { Events = events.ToArray() }, []
                    else
                        let nx, ny = PitchMath.jitter bX bY bX bY 0.0 cfg.JitterKeep cfg.JitterKeep
                        let dx = (nx - bX) / 1.0<meter> * cfg.SpeedKeep
                        let dy = (ny - bY) / 1.0<meter> * cfg.SpeedKeep

                        let velBall =
                            { state.Ball with
                                Position =
                                    { state.Ball.Position with
                                        Vx = dx
                                        Vy = dy
                                        Vz = cfg.SpeedKeepVz } }

                        { Events = [|
                            DomainEvent.Emit { SubTick = subTick; PlayerId = attP.Id; ClubId = attClubId; Type = DribbleKeep; Context = EventContext.empty }
                            DomainEvent.BallUpdate velBall
                        |] }, []
            | _ -> ActionResult.empty, []

    let resolveTackle
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (defender: Player)
        : ActionResult * RefereeAction list =
        let actx = ActionContext.build ctx state
        let defFrame = actx.Def.OwnFrame
        let attFrame = actx.Att.OwnFrame
        let defRoster = getRoster ctx actx.Def.ClubSide
        let attRoster = getRoster ctx actx.Att.ClubSide

        let defClubId =
            match findIdxByPid defender.Id defFrame defRoster with
            | ValueSome _ -> actx.Def.ClubId
            | ValueNone ->
                match findIdxByPid defender.Id (getFrame state HomeClub) (getRoster ctx HomeClub) with
                | ValueSome _ -> ctx.Home.Id
                | ValueNone -> ctx.Away.Id

        let defIdx =
            match findIdxByPid defender.Id defFrame defRoster with
            | ValueSome i -> i
            | ValueNone -> -1

        if defIdx < 0 then
            ActionResult.empty, []
        else
            let condNorm = normaliseCondition defFrame.Condition[defIdx]
            let tCfg = ctx.Config.Tackle

            let defScore =
                ActionMath.evalPerformance
                    PerformanceDefaults.duelPerformanceConfig
                    (normaliseAttr defender.Technical.Tackling)
                    (int defFrame.Condition[defIdx])
                    0
                + actx.Def.Bonus.Tackle

            let bPos = state.Ball.Position
            let mutable bestIdx = ValueNone
            let mutable bestDistSq = Single.MaxValue
            let bX32 = float32 bPos.X
            let bY32 = float32 bPos.Y

            for i = 0 to attFrame.SlotCount - 1 do
                match attFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let player = attRoster.Players[i]

                    if player.Position <> GK then
                        let dx = attFrame.Physics.PosX[i] - bX32
                        let dy = attFrame.Physics.PosY[i] - bY32
                        let dSq = dx * dx + dy * dy

                        if dSq < bestDistSq then
                            bestDistSq <- dSq
                            bestIdx <- ValueSome i
                | _ -> ()

            let attacker =
                match bestIdx with
                | ValueSome idx -> attRoster.Players[idx]
                | ValueNone ->
                    attRoster.Players
                    |> Array.tryFind (fun p -> p.Position <> GK)
                    |> Option.defaultValue (failwith "No active players found in attacker team")

            let attIdx =
                match findIdxByPid attacker.Id attFrame attRoster with
                | ValueSome i -> i
                | ValueNone -> 0

            let attCond = int attFrame.Condition[attIdx]

            let attScore =
                ActionMath.evalPerformance
                    PerformanceDefaults.duelPerformanceConfig
                    (normaliseAttr attacker.Technical.Dribbling)
                    attCond
                    0

            let physVar = physicalVariation (int defFrame.Condition[defIdx])
            let duelScore = (defScore - attScore) * condNorm * physVar

            if logisticBernoulli duelScore ctx.Config.Tackle.TackleSteepness then
                let aggressionNorm = normaliseAttr defender.Mental.Aggression
                let positioningNorm = normaliseAttr defender.Mental.Positioning

                let baseFoulRate =
                    ctx.Config.SetPiece.FoulBaseRate + aggressionNorm * tCfg.AggressionWeight
                    - positioningNorm * tCfg.PositioningReduction
                    |> abs

                let adjustedFoulRate = baseFoulRate * (1.0 - actx.Def.Bonus.CardReduc)
                let foulChance = betaSample adjustedFoulRate tCfg.FoulShapeBeta

                if bernoulli foulChance then
                    let freeBall =
                        { state.Ball with
                            Control = Free
                            PendingOffsideSnapshot = None }

                    let mDelta = forwardX actx.Att.AttackDir * (-tCfg.FoulMomentum)
                    let reckless = bernoulli (aggressionNorm * 0.3)
                    let excessiveForce = bernoulli (aggressionNorm * 0.15)
                    let ballPos = state.Ball.Position

                    let foulCtx =
                        FoulAnalysis.assess
                            ballPos.X
                            ballPos.Y
                            actx.Att.AttackDir
                            actx.Att.ClubSide
                            defender.Mental.Aggression
                            reckless
                            excessiveForce
                            attFrame
                            attRoster
                            defFrame
                            defRoster

                    let severity = FoulAnalysis.classifySeverity foulCtx

                    let yellows =
                        getYellows state actx.Def.ClubSide
                        |> Map.tryFind defender.Id
                        |> Option.defaultValue 0

                    let cardActions, cardDomainEvents =
                        match FoulAnalysis.decideCard severity yellows with
                        | Some FoulAnalysis.CardDecision.Yellow ->
                            [ IssueYellow(defender, actx.Def.ClubId) ],
                            [ DomainEvent.EmitSemantic(SemanticEvent.FoulOccurred(defender.Id, attacker.Id)) ]
                        | Some FoulAnalysis.CardDecision.Red ->
                            [ IssueRed(defender, actx.Def.ClubId) ],
                            [ DomainEvent.EmitSemantic(SemanticEvent.FoulOccurred(defender.Id, attacker.Id)) ]
                        | None -> [], []

                    let events = ResizeArray<DomainEvent>(8)
                    events.Add(DomainEvent.Emit { SubTick = subTick; PlayerId = defender.Id; ClubId = defClubId; Type = FoulCommitted; Context = EventContext.empty })
                    events.Add(DomainEvent.BallUpdate freeBall)
                    events.Add(DomainEvent.MomentumDelta mDelta)
                    for de in cardDomainEvents do events.Add(de)
                    { Events = events.ToArray() }, cardActions
                else
                    let mDelta = forwardX actx.Att.AttackDir * tCfg.SuccessMomentum
                    { Events = [|
                        DomainEvent.Emit { SubTick = subTick; PlayerId = defender.Id; ClubId = defClubId; Type = TackleSuccess; Context = EventContext.empty }
                        DomainEvent.MomentumDelta mDelta
                    |] }, []
            else
                let mDelta = forwardX actx.Att.AttackDir * (-tCfg.FailMomentum)

                let clearBall =
                    { state.Ball with
                        PendingOffsideSnapshot = None }

                { Events = [|
                    DomainEvent.Emit { SubTick = subTick; PlayerId = defender.Id; ClubId = defClubId; Type = TackleFail; Context = EventContext.empty }
                    DomainEvent.MomentumDelta mDelta
                    DomainEvent.BallUpdate clearBall
                |] }, []
