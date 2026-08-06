namespace FootballEngine.Player.Actions

open System
open FootballEngine
open FootballEngine.Domain

open FootballEngine.MatchSpatial
open FootballEngine.Player.Decision

open FootballEngine.SimStateOps
open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module PassLauncher =

    type LaunchResult =
        { Ball: BallPhysicsState
          Trajectory: BallTrajectory
          Events: DomainEvent[] }

    let private predictLeadPosition
        (leadFactor: float)
        (passerX: float<meter>)
        (passerY: float<meter>)
        (targetX: float<meter>)
        (targetY: float<meter>)
        (targetVx: float<meter / second>)
        (targetVy: float<meter / second>)
        (passSpeed: float<meter / second>)
        : float<meter> * float<meter> =
        let dx = targetX - passerX
        let dy = targetY - passerY
        let dist = sqrt (dx * dx + dy * dy)

        let flightTime =
            if passSpeed > 0.0<meter / second> then
                dist / passSpeed
            else
                0.5<second>

        let effectiveTime = flightTime * leadFactor
        let leadX = targetX + targetVx * effectiveTime
        let leadY = targetY + targetVy * effectiveTime
        clamp leadX 0.0<meter> PitchLength, clamp leadY 0.0<meter> PitchWidth

    let private computeFlightTime
        (distance: float<meter>)
        (passSpeed: float<meter / second>)
        (passConfig: PassConfig)
        : int<subtick> =
        if passSpeed <= 0.0<meter / second> then
            passConfig.FlightTimeMin
        else
            let timeSeconds = float distance / float passSpeed
            let subticks = int (timeSeconds * 40.0) * 1<subtick>
            max passConfig.FlightTimeMin (min passConfig.FlightTimeMax subticks)

    let private makeTrajectory
        (subTick: int)
        (clock: SimulationClock)
        (passerId: PlayerId)
        (ox: float<meter>)
        (oy: float<meter>)
        (tx: float<meter>)
        (ty: float<meter>)
        (speed: float<meter / second>)
        (vz: float<meter / second>)
        (intent: InFlightIntent)
        (passConfig: PassConfig)
        : BallTrajectory =
        let dist = sqrt ((tx - ox) * (tx - ox) + (ty - oy) * (ty - oy))
        let flightTime = computeFlightTime dist speed passConfig
        let arrivalSubTick = subTick + int flightTime

        let peakHeight =
            if vz > 0.0<meter / second> then
                vz * vz / (2.0 * 9.80665<meter / second^2>)
            else
                0.0<meter>

        { OriginX = ox
          OriginY = oy
          TargetX = tx
          TargetY = ty
          LaunchSubTick = subTick * 1<subtick>
          EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
          KickerId = passerId
          PeakHeight = peakHeight
          Intent = intent }

    let private ballUpdateTowards
        (originX: float<meter>)
        (originY: float<meter>)
        (targetX: float<meter>)
        (targetY: float<meter>)
        (speed: float<meter / second>)
        (vz: float<meter / second>)
        (baseState: BallPhysicsState)
        : BallPhysicsState =
        let dx = targetX - originX
        let dy = targetY - originY
        let dist = sqrt (dx * dx + dy * dy)

        let vx, vy =
            if dist < 0.01<meter> then
                0.0<meter / second>, 0.0<meter / second>
            else
                dx / dist * speed, dy / dist * speed

        { baseState with
            Position =
                { baseState.Position with
                    Vx = vx
                    Vy = vy
                    Vz = vz } }

    let private captureAndLosePossession (state: SimState) (clubSide: ClubSide) (events: ResizeArray<DomainEvent>) =
        let activeRuns = getActiveRuns state clubSide
        losePossession state

        for run in activeRuns do
            events.Add(DomainEvent.ExpireRun(clubSide, run.PlayerId))

    let private findPasserIdx (state: SimState) (attFrame: TeamFrame) (attRoster: PlayerRoster) : int option =
        match state.Ball.Control with
        | Controlled(_, ctrlId) ->
            match findIdxByPid ctrlId attFrame attRoster with
            | ValueSome i -> Some i
            | ValueNone -> None
        | _ ->
            match state.Ball.LastTouchBy with
            | Some pid ->
                match findIdxByPid pid attFrame attRoster with
                | ValueSome i -> Some i
                | ValueNone -> None
            | None -> None

    let launchShort
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (target: PassTarget)
        : LaunchResult option =
        let events = ResizeArray<DomainEvent>(8)
        let actx = ActionContext.build ctx state
        let pc: PassConfig = ctx.Config.Pass
        let attClubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide

        let targetIdx = PassTarget.idx target
        let targetPid = PassTarget.playerId target
        let targetPos = PassTarget.position target

        match findPasserIdx state attFrame attRoster with
        | None -> None
        | Some pIdx ->
            let passer = attRoster.Players[pIdx]
            let passerCond = attFrame.Condition[pIdx]
            let passerProfile = attRoster.Profiles[pIdx]
            let chemistry = getChemistry ctx actx.Att.ClubSide

            let passerSp =
                { X = float attFrame.Physics.PosX[pIdx] * 1.0<meter>
                  Y = float attFrame.Physics.PosY[pIdx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            let condNorm = normaliseCondition passerCond

            let familiarity =
                if pIdx < chemistry.PlayerCount && targetIdx < chemistry.PlayerCount then
                    chemistry.Familiarity[pIdx, targetIdx]
                else
                    0.5

            let passDist = float (passerSp.DistTo2D targetPos)

            let distPenalty =
                if passDist <= 10.0 then
                    0.0
                elif passDist <= 30.0 then
                    (passDist - 10.0) * pc.DistancePenaltyPerMeter
                else
                    20.0 * pc.DistancePenaltyPerMeter
                    + (passDist - 30.0) * pc.LongPassPenaltyPerMeter

            let zone = PitchZoneOps.ofPosition passerSp.X passerSp.Y

            let passMean =
                pc.BaseMean
                + toScalar (normaliseAttr passer.Technical.Passing) * pc.TechnicalWeight
                + toScalar (normaliseAttr passer.Mental.Vision) * pc.VisionWeight
                + actx.Att.Bonus.PassAcc
                + (if zone = DefensiveZone then
                       passerProfile.CreativityWeight * pc.CreativityWeight
                       + (1.0 - passerProfile.Directness) * pc.DirectnessWeight
                   else
                       0.0)
                - distPenalty
                + (familiarity - 0.5) * state.Config.Collective.Chemistry.FamiliarityPassBonus

            let passMeanCapped = Math.Clamp(passMean, pc.MeanMin, pc.MeanMax)
            let chemBonus = familiarity * state.Config.Collective.Chemistry.FamiliarityPassBonus

            let quality =
                betaSample
                    (passMeanCapped + chemBonus)
                    (pc.SuccessShapeAlpha + condNorm * pc.SuccessConditionMultiplier)

            let offside =
                isOffsideFrame targetIdx attFrame attRoster defFrame state actx.Att.ClubSide

            events.Add(MatchStatIncrement(actx.Att.ClubSide, PassAttempts, 1))

            if offside then
                let mDelta = forwardX actx.Att.AttackDir * (-pc.OffsideMomentum)

                events.Add(
                    Emit
                        { SubTick = subTick
                          PlayerId = targetPid
                          ClubId = attClubId
                          Type = PassIncomplete targetPid
                          Context = EventContext.empty }
                )

                captureAndLosePossession state actx.Att.ClubSide events
                events.Add(MomentumDelta mDelta)
                None
            else
                let snapshot =
                    snapshotAtPassFrame pIdx targetIdx attFrame attRoster defFrame state actx.Att.AttackDir

                let targetPlayer = SlotAccess.playerAtSlot attFrame attRoster targetIdx

                let heavyTouchChance =
                    (1.0 - float targetPlayer.Technical.BallControl / pc.HeavyTouchDivisor)
                    * pc.HeavyTouchMultiplier

                let accuracyNoise = pc.JitterStdDev * (1.0 - quality)

                let targetVx = float attFrame.Physics.VelX[targetIdx] * 1.0<meter / second>
                let targetVy = float attFrame.Physics.VelY[targetIdx] * 1.0<meter / second>

                let leadX, leadY =
                    predictLeadPosition
                        pc.PassLeadFactor
                        passerSp.X
                        passerSp.Y
                        targetPos.X
                        targetPos.Y
                        targetVx
                        targetVy
                        pc.Speed

                let actualTargetX, actualTargetY =
                    if bernoulli heavyTouchChance then
                        clamp (leadX + normalSample 0.0 accuracyNoise * 1.0<meter>) 0.0<meter> PitchLength,
                        clamp (leadY + normalSample 0.0 accuracyNoise * 1.0<meter>) 0.0<meter> PitchWidth
                    else
                        leadX, leadY

                let intent = Aimed(passer.Id, targetPid, quality, RegularPass)

                let traj =
                    makeTrajectory
                        subTick
                        clock
                        passer.Id
                        passerSp.X
                        passerSp.Y
                        actualTargetX
                        actualTargetY
                        pc.Speed
                        pc.Vz
                        intent
                        pc

                let ballWithVel =
                    ballUpdateTowards passerSp.X passerSp.Y actualTargetX actualTargetY pc.Speed pc.Vz state.Ball

                let finalBall =
                    { ballWithVel with
                        Spin =
                            { Top = 0.0<radianPerSecond>
                              Side = 0.0<radianPerSecond> }
                        Control = Airborne
                        PendingOffsideSnapshot = Some snapshot
                        Trajectory = Some traj }

                let mDelta = forwardX actx.Att.AttackDir * pc.SuccessMomentum

                events.Add(
                    Emit
                        { SubTick = subTick
                          PlayerId = passer.Id
                          ClubId = attClubId
                          Type = MatchEventType.PassLaunched(passer.Id, targetPid)
                          Context =
                            { EventContext.empty with
                                Quality = Some quality
                                PassMean = Some passMeanCapped
                                DistancePenalty = Some distPenalty } }
                )

                events.Add(BallUpdate finalBall)
                events.Add(MomentumDelta mDelta)
                events.Add(MemoryWrite(actx.Att.ClubSide, pIdx, MemoryWriteKind.PassSuccess))
                events.Add(EmitSemantic(SemanticEvent.PassLaunched(passer.Id, targetPid)))

                Some
                    { Ball = finalBall
                      Trajectory = traj
                      Events = events.ToArray() }

    let launchLong (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : LaunchResult option =
        let events = ResizeArray<DomainEvent>(8)
        let actx = ActionContext.build ctx state
        let pc: PassConfig = ctx.Config.Pass
        let attClubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide

        let bPos = state.Ball.Position

        match nearestActiveSlotInFrame attFrame bPos.X bPos.Y with
        | ValueNone -> None
        | ValueSome passerIdx ->
            let passer = attRoster.Players[passerIdx]
            let passerCond = attFrame.Condition[passerIdx]

            let passerSp =
                { X = float attFrame.Physics.PosX[passerIdx] * 1.0<meter>
                  Y = float attFrame.Physics.PosY[passerIdx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            let condNorm = normaliseCondition passerCond

            let bestTarget =
                PassDecision.findBestPassTarget attFrame attRoster defFrame passerIdx actx.Att.AttackDir pc None

            match bestTarget with
            | None -> None
            | Some candidate ->
                let target = PassTarget.position candidate.Target
                let targetPid = PassTarget.playerId candidate.Target
                let targetIdx = PassTarget.idx candidate.Target

                let longMean =
                    pc.LongBallBaseMean
                    + toScalar (normaliseAttr passer.Technical.LongShots) * pc.LongBallLongShotsWeight
                    + toScalar (normaliseAttr passer.Technical.Passing) * pc.LongBallPassingWeight
                    + toScalar (normaliseAttr passer.Mental.Vision) * pc.LongBallVisionWeight
                    + actx.Att.Bonus.SetPlay
                    - (if bPos.DistTo2D target > 30.0<meter> then 0.2 else 0.0)

                let quality =
                    betaSample
                        longMean
                        (pc.LongBallSuccessShapeAlpha + condNorm * pc.LongBallSuccessConditionMultiplier)

                let offside =
                    isOffsideFrame targetIdx attFrame attRoster defFrame state actx.Att.ClubSide

                if offside then
                    let mDelta = forwardX actx.Att.AttackDir * (-pc.LongBallOffsideMomentum)

                    events.Add(
                        Emit
                            { SubTick = subTick
                              PlayerId = passer.Id
                              ClubId = attClubId
                              Type = MatchEventType.LongBall false
                              Context = EventContext.empty }
                    )

                    captureAndLosePossession state actx.Att.ClubSide events
                    events.Add(MomentumDelta mDelta)
                    None
                else
                    let snapshot =
                        snapshotAtPassFrame passerIdx targetIdx attFrame attRoster defFrame state actx.Att.AttackDir

                    let targetPlayer = SlotAccess.playerAtSlot attFrame attRoster targetIdx

                    let accuracyNoise =
                        pc.JitterStdDev * pc.LongBallScrambleJitterMult * (1.0 - quality)

                    let heavyTouchChance =
                        (1.0 - float targetPlayer.Technical.BallControl / pc.HeavyTouchDivisor)
                        * pc.HeavyTouchMultiplier

                    let targetVx = float attFrame.Physics.VelX[targetIdx] * 1.0<meter / second>
                    let targetVy = float attFrame.Physics.VelY[targetIdx] * 1.0<meter / second>

                    let leadX, leadY =
                        predictLeadPosition
                            pc.PassLeadFactor
                            passerSp.X
                            passerSp.Y
                            target.X
                            target.Y
                            targetVx
                            targetVy
                            pc.LongBallSpeed

                    let actualTargetX, actualTargetY =
                        if bernoulli heavyTouchChance then
                            clamp (leadX + normalSample 0.0 accuracyNoise * 1.0<meter>) 0.0<meter> PitchLength,
                            clamp (leadY + normalSample 0.0 accuracyNoise * 1.0<meter>) 0.0<meter> PitchWidth
                        else
                            leadX, leadY

                    let intent = Aimed(passer.Id, targetPid, quality, AimedKind.LongBall)

                    let traj =
                        makeTrajectory
                            subTick
                            clock
                            passer.Id
                            passerSp.X
                            passerSp.Y
                            actualTargetX
                            actualTargetY
                            pc.LongBallSpeed
                            pc.LongBallVz
                            intent
                            pc

                    let ballWithVel =
                        ballUpdateTowards
                            passerSp.X
                            passerSp.Y
                            actualTargetX
                            actualTargetY
                            pc.LongBallSpeed
                            pc.LongBallVz
                            state.Ball

                    let finalBall =
                        { ballWithVel with
                            Control = Airborne
                            PendingOffsideSnapshot = Some snapshot
                            Trajectory = Some traj }

                    let mDelta = forwardX actx.Att.AttackDir * pc.LongBallSuccessMomentum

                    events.Add(
                        Emit
                            { SubTick = subTick
                              PlayerId = passer.Id
                              ClubId = attClubId
                              Type = MatchEventType.LongBall true
                              Context = EventContext.empty }
                    )

                    events.Add(BallUpdate finalBall)
                    events.Add(MomentumDelta mDelta)
                    events.Add(EmitSemantic(SemanticEvent.PassLaunched(passer.Id, targetPid)))

                    Some
                        { Ball = finalBall
                          Trajectory = traj
                          Events = events.ToArray() }

    let launchSpace
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (targetCell: int)
        : LaunchResult option =
        let events = ResizeArray<DomainEvent>(8)
        let actx = ActionContext.build ctx state
        let pc: PassConfig = ctx.Config.Pass
        let attClubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide

        let targetX, targetY = InfluenceTypes.cellToCenter ctx.Config.Influence targetCell

        let targetSpatial =
            { X = float targetX * 1.0<meter>
              Y = float targetY * 1.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        match findPasserIdx state attFrame attRoster with
        | None -> None
        | Some pIdx ->
            let passer = attRoster.Players[pIdx]

            let passerSp =
                { X = float attFrame.Physics.PosX[pIdx] * 1.0<meter>
                  Y = float attFrame.Physics.PosY[pIdx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            let mutable bestReceiverIdx = -1
            let mutable bestReceiverScore = -1.0

            for i = 0 to attFrame.SlotCount - 1 do
                if i <> pIdx then
                    match attFrame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ ->
                        let px = float attFrame.Physics.PosX[i] * 1.0<meter>
                        let py = float attFrame.Physics.PosY[i] * 1.0<meter>
                        let vx = float attFrame.Physics.VelX[i] * 1.0<meter / second>
                        let vy = float attFrame.Physics.VelY[i] * 1.0<meter / second>

                        let dist =
                            sqrt (
                                (px - targetSpatial.X) * (px - targetSpatial.X)
                                + (py - targetSpatial.Y) * (py - targetSpatial.Y)
                            )

                        let proximityScore = max 0.0 (1.0 - float (dist / 40.0<meter>))

                        let toTargetX = float (targetSpatial.X - px)
                        let toTargetY = float (targetSpatial.Y - py)
                        let toTargetDist = sqrt (toTargetX * toTargetX + toTargetY * toTargetY)

                        let velAlign =
                            if toTargetDist < 0.01 then
                                0.0
                            else
                                let dot = float vx * toTargetX / toTargetDist + float vy * toTargetY / toTargetDist
                                max 0.0 (dot / 8.0)

                        let convergence = proximityScore * 0.4 + velAlign * 0.6
                        let receiver = attRoster.Players[i]

                        let ballControlBonus =
                            float receiver.Technical.BallControl / 20.0
                            * state.Config.Collective.Chemistry.FamiliarityTimeBonus
                            * 100.0

                        let totalScore = convergence + ballControlBonus

                        if totalScore > bestReceiverScore then
                            bestReceiverScore <- totalScore
                            bestReceiverIdx <- i
                    | _ -> ()

            if bestReceiverScore < 0.15 || bestReceiverIdx < 0 then
                None
            else
                let receiver = attRoster.Players[bestReceiverIdx]
                let receiverCurrX = float attFrame.Physics.PosX[bestReceiverIdx] * 1.0<meter>
                let receiverCurrY = float attFrame.Physics.PosY[bestReceiverIdx] * 1.0<meter>
                events.Add(MatchStatIncrement(actx.Att.ClubSide, PassAttempts, 1))

                let offside =
                    isOffsideFrame bestReceiverIdx attFrame attRoster defFrame state actx.Att.ClubSide

                if offside then
                    let mDelta = forwardX actx.Att.AttackDir * (-pc.OffsideMomentum)

                    events.Add(
                        Emit
                            { SubTick = subTick
                              PlayerId = receiver.Id
                              ClubId = attClubId
                              Type = PassIncomplete receiver.Id
                              Context = EventContext.empty }
                    )

                    captureAndLosePossession state actx.Att.ClubSide events
                    events.Add(MomentumDelta mDelta)
                    None
                else
                    let snapshot =
                        snapshotAtPassFrame pIdx bestReceiverIdx attFrame attRoster defFrame state actx.Att.AttackDir

                    let chemistry = getChemistry ctx actx.Att.ClubSide

                    let familiarity =
                        if pIdx < chemistry.PlayerCount && bestReceiverIdx < chemistry.PlayerCount then
                            chemistry.Familiarity[pIdx, bestReceiverIdx]
                        else
                            0.5

                    let condNorm = normaliseCondition attFrame.Condition[pIdx]
                    let passerProfile = attRoster.Profiles[pIdx]

                    let passDist = float (passerSp.DistTo2D targetSpatial)

                    let distPenalty =
                        if passDist <= 10.0 then
                            0.0
                        elif passDist <= 30.0 then
                            (passDist - 10.0) * pc.DistancePenaltyPerMeter
                        else
                            20.0 * pc.DistancePenaltyPerMeter
                            + (passDist - 30.0) * pc.LongPassPenaltyPerMeter

                    let passMean =
                        pc.BaseMean
                        + toScalar (normaliseAttr passer.Technical.Passing) * pc.TechnicalWeight
                        + toScalar (normaliseAttr passer.Mental.Vision) * pc.VisionWeight
                        + actx.Att.Bonus.PassAcc
                        - distPenalty
                        + (familiarity - 0.5) * state.Config.Collective.Chemistry.FamiliarityPassBonus

                    let passMeanCapped = Math.Clamp(passMean, pc.MeanMin, pc.MeanMax)
                    let chemBonus = familiarity * state.Config.Collective.Chemistry.FamiliarityPassBonus

                    let quality =
                        betaSample
                            (passMeanCapped + chemBonus)
                            (pc.SuccessShapeAlpha + condNorm * pc.SuccessConditionMultiplier)

                    let actualTargetX, actualTargetY =
                        clamp
                            (targetSpatial.X + normalSample 0.0 (pc.JitterStdDev * 0.5) * 1.0<meter>)
                            0.0<meter>
                            PitchLength,
                        clamp
                            (targetSpatial.Y + normalSample 0.0 (pc.JitterStdDev * 0.5) * 1.0<meter>)
                            0.0<meter>
                            PitchWidth

                    let intent = Aimed(passer.Id, receiver.Id, quality, RegularPass)

                    let traj =
                        makeTrajectory
                            subTick
                            clock
                            passer.Id
                            passerSp.X
                            passerSp.Y
                            actualTargetX
                            actualTargetY
                            pc.Speed
                            pc.Vz
                            intent
                            pc

                    let ballWithVel =
                        ballUpdateTowards passerSp.X passerSp.Y actualTargetX actualTargetY pc.Speed pc.Vz state.Ball

                    let finalBall =
                        { ballWithVel with
                            Spin =
                                { Top = 0.0<radianPerSecond>
                                  Side = 0.0<radianPerSecond> }
                            Control = Airborne
                            PendingOffsideSnapshot = Some snapshot
                            Trajectory = Some traj }

                    let runDuration =
                        max 60<subtick> (traj.EstimatedArrivalSubTick - subTick * 1<subtick> + 20<subtick>)

                    let assignment =
                        RunAssignment.create
                            RunType.DeepRun
                            receiverCurrX
                            receiverCurrY
                            actualTargetX
                            actualTargetY
                            receiver.Id
                            (subTick * 1<subtick>)
                            (SimulationClock.subtickToDelta runDuration)

                    let spaceAssignment =
                        { assignment with
                            Trigger = SpaceDetected
                            Priority = 2 }

                    let mDelta = forwardX actx.Att.AttackDir * pc.SuccessMomentum

                    events.Add(
                        Emit
                            { SubTick = subTick
                              PlayerId = passer.Id
                              ClubId = attClubId
                              Type = MatchEventType.PassLaunched(passer.Id, receiver.Id)
                              Context =
                                { EventContext.empty with
                                    Quality = Some quality
                                    PassMean = Some passMeanCapped
                                    DistancePenalty = Some distPenalty } }
                    )

                    events.Add(BallUpdate finalBall)
                    events.Add(MomentumDelta mDelta)
                    events.Add(MemoryWrite(actx.Att.ClubSide, pIdx, MemoryWriteKind.PassSuccess))
                    events.Add(RegisterRun(actx.Att.ClubSide, spaceAssignment))
                    events.Add(EmitSemantic(SemanticEvent.PassLaunched(passer.Id, receiver.Id)))

                    Some
                        { Ball = finalBall
                          Trajectory = traj
                          Events = events.ToArray() }

    let launchGKDistribution
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : LaunchResult option =
        let events = ResizeArray<DomainEvent>(8)
        let actx = ActionContext.build ctx state
        let pc: PassConfig = ctx.Config.Pass
        let gkc: GKConfig = ctx.Config.GK
        let attClubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide

        match findPasserIdx state attFrame attRoster with
        | None -> None
        | Some gkIdx ->
            let gk = attRoster.Players[gkIdx]

            let gkSp =
                { X = float attFrame.Physics.PosX[gkIdx] * 1.0<meter>
                  Y = float attFrame.Physics.PosY[gkIdx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            let dir = attackDirFor actx.Att.ClubSide state

            let mutable bestTargetIdx = -1
            let mutable bestTargetScore = -1.0
            let mutable bestTargetSp = gkSp

            for i = 0 to attFrame.SlotCount - 1 do
                if i <> gkIdx then
                    match attFrame.Physics.Occupancy[i] with
                    | OccupancyKind.Active rosterIdx when attRoster.Players[rosterIdx].Position <> GK ->
                        let player = attRoster.Players[rosterIdx]
                        let px = float attFrame.Physics.PosX[i] * 1.0<meter>
                        let py = float attFrame.Physics.PosY[i] * 1.0<meter>

                        let dist =
                            gkSp.DistTo2D
                                { X = px
                                  Y = py
                                  Z = 0.0<meter>
                                  Vx = 0.0<meter / second>
                                  Vy = 0.0<meter / second>
                                  Vz = 0.0<meter / second> }

                        let isShort = float dist < float gkc.GKShortPassMaxDist

                        let nearestDefDist =
                            let mutable minD = 999.0<meter>

                            for j = 0 to defFrame.SlotCount - 1 do
                                match defFrame.Physics.Occupancy[j] with
                                | OccupancyKind.Active _ ->
                                    let dx = float defFrame.Physics.PosX[j] * 1.0<meter>
                                    let dy = float defFrame.Physics.PosY[j] * 1.0<meter>

                                    let d =
                                        { X = px
                                          Y = py
                                          Z = 0.0<meter>
                                          Vx = 0.0<meter / second>
                                          Vy = 0.0<meter / second>
                                          Vz = 0.0<meter / second> }
                                            .DistTo2D
                                            { X = dx
                                              Y = dy
                                              Z = 0.0<meter>
                                              Vx = 0.0<meter / second>
                                              Vy = 0.0<meter / second>
                                              Vz = 0.0<meter / second> }

                                    if d < minD then
                                        minD <- d
                                | _ -> ()

                            minD

                        let forwardBonus =
                            if dir = LeftToRight then
                                float px / float PitchLength
                            else
                                1.0 - float px / float PitchLength

                        let safetyBonus = float nearestDefDist / 10.0

                        let baseScore =
                            if isShort then
                                gkc.ShortDistributionBias * (safetyBonus * 0.6 + forwardBonus * 0.4)
                            else
                                (1.0 - gkc.ShortDistributionBias) * (safetyBonus * 0.3 + forwardBonus * 0.7)

                        let noise = normalSample 0.0 gkc.DistributionDecisionNoise
                        let totalScore = baseScore + noise

                        if totalScore > bestTargetScore then
                            bestTargetScore <- totalScore
                            bestTargetIdx <- i

                            bestTargetSp <-
                                { X = px
                                  Y = py
                                  Z = 0.0<meter>
                                  Vx = 0.0<meter / second>
                                  Vy = 0.0<meter / second>
                                  Vz = 0.0<meter / second> }
                    | _ -> ()

            if bestTargetIdx < 0 then
                None
            else
                let targetPid = SlotAccess.playerIdAtSlot attFrame attRoster bestTargetIdx
                let targetDist = gkSp.DistTo2D bestTargetSp
                let isShort = float targetDist < float gkc.GKShortPassMaxDist

                let passSpeed, passVz =
                    if isShort then
                        pc.Speed, pc.Vz
                    else
                        pc.LongBallSpeed, pc.LongBallVz

                let quality =
                    if isShort then
                        betaSample 0.8 (pc.SuccessShapeAlpha + 0.5 * pc.SuccessConditionMultiplier)
                    else
                        betaSample 0.6 (pc.LongBallSuccessShapeAlpha + 0.5 * pc.LongBallSuccessConditionMultiplier)

                let offside =
                    isOffsideFrame bestTargetIdx attFrame attRoster defFrame state actx.Att.ClubSide

                if offside then
                    let mDelta = forwardX dir * (-pc.OffsideMomentum)

                    events.Add(
                        Emit
                            { SubTick = subTick
                              PlayerId = targetPid
                              ClubId = attClubId
                              Type = PassIncomplete targetPid
                              Context = EventContext.empty }
                    )

                    captureAndLosePossession state actx.Att.ClubSide events
                    events.Add(MomentumDelta mDelta)
                    None
                else
                    let snapshot =
                        snapshotAtPassFrame gkIdx bestTargetIdx attFrame attRoster defFrame state dir

                    let leadX, leadY =
                        predictLeadPosition
                            pc.PassLeadFactor
                            gkSp.X
                            gkSp.Y
                            bestTargetSp.X
                            bestTargetSp.Y
                            0.0<meter / second>
                            0.0<meter / second>
                            passSpeed

                    let intent =
                        if isShort then
                            Aimed(gk.Id, targetPid, quality, RegularPass)
                        else
                            Aimed(gk.Id, targetPid, quality, AimedKind.LongBall)

                    let traj =
                        makeTrajectory subTick clock gk.Id gkSp.X gkSp.Y leadX leadY passSpeed passVz intent pc

                    let ballWithVel =
                        ballUpdateTowards gkSp.X gkSp.Y leadX leadY passSpeed passVz state.Ball

                    let finalBall =
                        { ballWithVel with
                            Spin =
                                { Top = 0.0<radianPerSecond>
                                  Side = 0.0<radianPerSecond> }
                            Control = Airborne
                            PendingOffsideSnapshot = Some snapshot
                            Trajectory = Some traj }

                    let mDelta = forwardX dir * pc.SuccessMomentum

                    events.Add(
                        Emit
                            { SubTick = subTick
                              PlayerId = gk.Id
                              ClubId = attClubId
                              Type = MatchEventType.PassLaunched(gk.Id, targetPid)
                              Context =
                                { EventContext.empty with
                                    Quality = Some quality } }
                    )

                    events.Add(BallUpdate finalBall)
                    events.Add(MomentumDelta mDelta)
                    events.Add(MemoryWrite(actx.Att.ClubSide, gkIdx, MemoryWriteKind.PassSuccess))
                    events.Add(EmitSemantic(SemanticEvent.PassLaunched(gk.Id, targetPid)))

                    Some
                        { Ball = finalBall
                          Trajectory = traj
                          Events = events.ToArray() }
