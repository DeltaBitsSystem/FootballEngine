namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Decision
open FootballEngine.Referee
open FootballEngine.SimStateOps
open FootballEngine.Stats
open FootballEngine.Types.PhysicsContract
open SimStateOps
open MatchSpatial
open FootballEngine.Types
open WallBehavior

module SetPlayAction =

    let resolveFreeKick (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : ActionResult =
        let actx = ActionContext.build ctx state
        let spc = ctx.Config.SetPiece
        let clubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide
        let defRoster = getRoster ctx actx.Def.ClubSide
        let bX, bY = state.Ball.Position.X, state.Ball.Position.Y

        match nearestActiveSlotInFrame attFrame bX bY with
        | ValueNone -> ActionResult.empty
        | ValueSome kickerIdx ->
            let kicker =
                let takerResult =
                    SetPieceTakerSelection.selectTaker attRoster.Players SetPieceTakerSelection.scoreFreeKickTaker

                attRoster.Players
                |> Array.tryFind (fun p -> p.Id = takerResult.PlayerId)
                |> Option.defaultValue attRoster.Players[kickerIdx]

            let kickerCond = int attFrame.Condition[kickerIdx]

            let quality =
                ActionMath.evalPerformance
                    PerformanceDefaults.technicalPerformanceConfig
                    kicker.Technical.Finishing
                    kickerCond
                    kicker.Morale
                + ActionMath.evalPerformance
                    PerformanceDefaults.technicalPerformanceConfig
                    kicker.Technical.LongShots
                    kickerCond
                    kicker.Morale
                  * 0.5
                + actx.Att.Bonus.FreeKick

            let goalX =
                if actx.Att.AttackDir = LeftToRight then
                    PitchLength
                else
                    0.0<meter>

            let distToGoal = abs (goalX - bX)
            let routine = FreeKickRoutineSelection.select kicker distToGoal clock

            let flightTime =
                if spc.FreeKickSpeed > 0.0<meter / second> then
                    distToGoal / spc.FreeKickSpeed
                else
                    1.0<second>

            let arrivalSubTick =
                subTick + int (float (flightTime / 1.0<second>) * float clock.SubTicksPerSecond)

            let dirSign = forwardX actx.Att.AttackDir
            let angleSpread = max 0.01 (spc.FreeKickSpinSideMult * 0.1 * (1.0 - quality / 2.0))
            let angle = normalSample 0.0 angleSpread

            let vx = dirSign * float spc.FreeKickSpeed * System.Math.Cos(angle)
            let vy = float spc.FreeKickSpeed * System.Math.Sin(angle)
            let vz = spc.FreeKickVz

            let targetY = bY + (vy / (dirSign * float spc.FreeKickSpeed)) * (goalX - bX)

            let trajectory =
                { OriginX = bX
                  OriginY = bY
                  TargetX = goalX
                  TargetY = targetY
                  LaunchSubTick = subTick * 1<subtick>
                  EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
                  KickerId = kicker.Id
                  PeakHeight = vz * vz / (2.0 * 9.80665<meter / second^2>)
                  Intent = Struck(kicker.Id, quality / 2.0) }

            let wallSize = calculateWallSize distToGoal
            let wallPlayers = selectWallPlayers defRoster attFrame goalX bY wallSize

            state.Ball <-
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            Vx = vx * 1.0<meter / second>
                            Vy = vy * 1.0<meter / second>
                            Vz = vz }
                    Control = Airborne
                    LastTouchBy = Some kicker.Id
                    Trajectory = Some trajectory }

            { Events = [| Emit { SubTick = subTick; PlayerId = kicker.Id; ClubId = clubId; Type = MatchEventType.FreeKick false; Context = EventContext.empty } |] }

    let resolveCorner (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : ActionResult =
        let actx = ActionContext.build ctx state
        let spc = ctx.Config.SetPiece
        let cc = ctx.Config.Cross
        let attClubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide
        let defRoster = getRoster ctx actx.Def.ClubSide

        let activeAtts =
            [| for i = 0 to attFrame.SlotCount - 1 do
                   match attFrame.Physics.Occupancy[i] with
                   | OccupancyKind.Active _ -> yield attRoster.Players[i]
                   | _ -> () |]

        if activeAtts.Length = 0 then
            ActionResult.empty
        else
            let taker =
                let takerResult =
                    SetPieceTakerSelection.selectTaker activeAtts SetPieceTakerSelection.scoreCrossingTaker

                activeAtts
                |> Array.tryFind (fun p -> p.Id = takerResult.PlayerId)
                |> Option.defaultValue activeAtts[0]

            let routine = CornerRoutineSelection.select taker defRoster.Players clock

            let boxThreshold = spc.CornerBoxXThreshold

            let attackersInBox =
                [| for i = 0 to attFrame.SlotCount - 1 do
                       match attFrame.Physics.Occupancy[i] with
                       | OccupancyKind.Active _ ->
                           let p = attRoster.Players[i]

                           let sp =
                               { X = float attFrame.Physics.PosX[i] * 1.0<meter>
                                 Y = float attFrame.Physics.PosY[i] * 1.0<meter>
                                 Z = 0.0<meter>
                                 Vx = 0.0<meter / second>
                                 Vy = 0.0<meter / second>
                                 Vz = 0.0<meter / second> }

                           let cond = int attFrame.Condition[i]

                           if
                               (p.Position = ST
                                || p.Position = AML
                                || p.Position = AMR
                                || p.Position = AMC
                                || p.Position = MC
                                || p.Position = DC)
                               && (if actx.Att.AttackDir = LeftToRight then
                                       sp.X > boxThreshold
                                   else
                                       sp.X < (PitchLength - boxThreshold))
                           then
                               yield (p, sp, cond)
                       | _ -> () |]

            if attackersInBox.Length = 0 then
                let targetX =
                    if actx.Att.AttackDir = LeftToRight then
                        PitchLength - PenaltyAreaDepth
                    else
                        PenaltyAreaDepth

                ballTowards
                    state.Ball.Position.X
                    state.Ball.Position.Y
                    targetX
                    (PitchWidth / 2.0)
                    spc.CornerSpeed
                    spc.CornerVz
                    state

                state.Ball <- { state.Ball with Control = Airborne }
                { Events = [| Emit { SubTick = subTick; PlayerId = taker.Id; ClubId = attClubId; Type = MatchEventType.Corner; Context = EventContext.empty } |] }
            else
                let bestAttacker, bestAttackerSp, _ =
                    attackersInBox
                    |> Array.maxBy (fun (p, _, _) -> p.Physical.Strength + p.Technical.Heading)

                let crossQuality =
                    activeAtts
                    |> Array.tryPick (fun p ->
                        if
                            p.Position = ML
                            || p.Position = MR
                            || p.Position = AML
                            || p.Position = AMR
                            || p.Position = MC
                        then
                            Some(
                                normaliseAttr p.Technical.Crossing * cc.CrossingWeight
                                + normaliseAttr p.Technical.Passing * cc.PassingWeight
                            )
                        else
                            None)
                    |> Option.defaultValue cc.BaseMean

                let targetX =
                    bestAttackerSp.X + normalSample 0.0 (0.15 * (1.0 - crossQuality)) * 1.0<meter>
                    |> fun x -> clamp x 0.0<meter> PitchLength

                let targetY =
                    bestAttackerSp.Y + normalSample 0.0 (0.15 * (1.0 - crossQuality)) * 1.0<meter>
                    |> fun y -> clamp y 0.0<meter> PitchWidth

                let dist =
                    sqrt (
                        (targetX - state.Ball.Position.X) * (targetX - state.Ball.Position.X)
                        + (targetY - state.Ball.Position.Y) * (targetY - state.Ball.Position.Y)
                    )

                let flightTime =
                    if spc.CornerSpeed > 0.0<meter / second> then
                        dist / spc.CornerSpeed
                    else
                        1.0<second>

                let arrivalSubTick =
                    subTick + int (float (flightTime / 1.0<second>) * float clock.SubTicksPerSecond)

                let trajectory =
                    { OriginX = state.Ball.Position.X
                      OriginY = state.Ball.Position.Y
                      TargetX = targetX
                      TargetY = targetY
                      LaunchSubTick = subTick * 1<subtick>
                      EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
                      KickerId = taker.Id
                      PeakHeight = spc.CornerVz * spc.CornerVz / (2.0 * 9.80665<meter / second^2>)
                      Intent = Aimed(taker.Id, bestAttacker.Id, crossQuality, AimedKind.Cross) }

                ballTowards
                    state.Ball.Position.X
                    state.Ball.Position.Y
                    targetX
                    targetY
                    spc.CornerSpeed
                    spc.CornerVz
                    state

                state.Ball <-
                    { state.Ball with
                        Control = Airborne
                        LastTouchBy = Some taker.Id
                        Trajectory = Some trajectory }

                { Events = [| Emit { SubTick = subTick; PlayerId = taker.Id; ClubId = attClubId; Type = MatchEventType.Corner; Context = EventContext.empty } |] }

    let resolveThrowIn
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (throwClub: ClubSide)
        (clock: SimulationClock)
        : ActionResult =
        let actx = ActionContext.build ctx state
        let spc = ctx.Config.SetPiece
        let clubId = if throwClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let throwFrame = getFrame state throwClub
        let throwRoster = getRoster ctx throwClub

        let activeCount =
            throwFrame.Physics.Occupancy
            |> Array.sumBy (function
                | OccupancyKind.Active _ -> 1
                | _ -> 0)

        if activeCount = 0 then
            ActionResult.empty
        else
            let throwerIdx =
                let mutable bestIdx = 0
                let mutable bestScore = 999

                for i = 0 to throwFrame.SlotCount - 1 do
                    match throwFrame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ ->
                        let profile = throwRoster.Profiles[i]

                        let score =
                            if profile.LateralTendency > 0.3 || profile.LateralTendency < -0.3 then
                                0
                            else
                                1

                        if score < bestScore then
                            bestScore <- score
                            bestIdx <- i
                    | _ -> ()

                bestIdx

            let thrower = throwRoster.Players[throwerIdx]
            let hasLongThrow = thrower.Physical.Strength > 15 && thrower.Technical.Crossing > 12
            let throwRoutine = ThrowInSelection.select thrower hasLongThrow clock
            let bX = state.Ball.Position.X
            let bY = state.Ball.Position.Y

            match nearestActiveSlotInFrameExcluding throwFrame throwerIdx bX bY with
            | ValueNone -> ActionResult.empty
            | ValueSome tmIdx ->
                let teammate = throwRoster.Players[tmIdx]
                let tX = float throwFrame.Physics.PosX[tmIdx] * 1.0<meter>
                let tY = float throwFrame.Physics.PosY[tmIdx] * 1.0<meter>
                ballTowards state.Ball.Position.X state.Ball.Position.Y tX tY spc.ThrowInSpeed spc.ThrowInVz state
                adjustMomentum actx.Att.AttackDir spc.ThrowInMomentum state
                { Events = [| Emit { SubTick = subTick; PlayerId = teammate.Id; ClubId = clubId; Type = MatchEventType.PassLaunched(thrower.Id, teammate.Id); Context = EventContext.empty } |] }

    let resolvePenalty
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (kicker: Player)
        (kickerClub: ClubSide)
        (clock: SimulationClock)
        : bool =
        let spc = ctx.Config.SetPiece
        let clubId = if kickerClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let defFrame = getFrame state (ClubSide.flip kickerClub)
        let defRoster = getRoster ctx (ClubSide.flip kickerClub)

        let gk =
            let mutable gkOpt: Player option = None

            for i = 0 to defFrame.SlotCount - 1 do
                match defFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ when defRoster.Players[i].Position = GK -> gkOpt <- Some defRoster.Players[i]
                | _ -> ()

            gkOpt

        let gkIdx =
            let mutable idx = -1

            for i = 0 to defFrame.SlotCount - 1 do
                match defFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ when defRoster.Players[i].Position = GK -> idx <- i
                | _ -> ()

            idx

        let dirSign = forwardX (attackDirFor kickerClub state)
        let finishingNorm = normaliseAttr kicker.Technical.Finishing
        let angleSpread = spc.PenaltyAngleSpread * (1.0 - finishingNorm)
        let angle = normalSample 0.0 angleSpread
        let speed = spc.PenaltySpeed

        let vz =
            abs (normalSample (float spc.PenaltyVzBase) spc.PenaltyVzVariance)
            * 1.0<meter / second>

        let vx = dirSign * float speed * System.Math.Cos(angle)
        let vy = float speed * System.Math.Sin(angle)

        let kickerSkill = float kicker.CurrentSkill
        let kickerCond = float kicker.Condition
        let kickerMorale = float kicker.Morale

        let pressNoise =
            if state.AttackingSide = kickerClub then
                0.0
            else
                pressureNoise kicker.Mental.Composure spc.PenaltyComposureNoise

        let kickerPower =
            kickerSkill / spc.PenaltySkillDivisor
            * (0.5 + kickerCond / spc.PenaltyCondDivisor)
            * (spc.PenaltyMoraleBase + kickerMorale / spc.PenaltyMoraleDivisor)
            + (if kickerClub = HomeClub then
                   ctx.Config.HomeAdvantage.PenaltyBonus
               else
                   0.0)
            - pressNoise * spc.PenaltyPressureMultiplier

        let gkSaves =
            match gk with
            | Some g ->
                let gkCond = if gkIdx >= 0 then int defFrame.Condition[gkIdx] else 50

                let savePower =
                    ActionMath.evalPerformance
                        PerformanceDefaults.technicalPerformanceConfig
                        (normaliseAttr g.Goalkeeping.Reflexes)
                        gkCond
                        g.Morale
                    * spc.PenaltyGkReflexesMult
                    + ActionMath.evalPerformance
                        PerformanceDefaults.technicalPerformanceConfig
                        (normaliseAttr g.Goalkeeping.Handling)
                        gkCond
                        g.Morale
                      * spc.PenaltyGkHandlingMult

                ActionMath.engineBernoulli (Probability.from (savePower / (savePower + kickerPower + 1.0)))
            | None -> false

        let goalX =
            if attackDirFor kickerClub state = LeftToRight then
                PitchLength
            else
                0.0<meter>

        let bX = state.Ball.Position.X
        let distToGoal = abs (goalX - bX)

        let flightTime =
            if float speed > 0.0 then
                float distToGoal / float speed
            else
                0.5

        let arrivalSubTick = subTick + int (flightTime * float clock.SubTicksPerSecond)

        let trajectory =
            { OriginX = bX
              OriginY = state.Ball.Position.Y
              TargetX = goalX
              TargetY = state.Ball.Position.Y + (vy / (dirSign * float speed)) * (goalX - bX)
              LaunchSubTick = subTick * 1<subtick>
              EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
              KickerId = kicker.Id
              PeakHeight = vz * vz / (2.0 * 9.80665<meter / second^2>)
              Intent = Struck(kicker.Id, kickerPower) }

        state.Ball <-
            { state.Ball with
                Position =
                    { state.Ball.Position with
                        Vx = vx * 1.0<meter / second>
                        Vy = vy * 1.0<meter / second>
                        Vz = vz }
                Control = Airborne
                LastTouchBy = Some kicker.Id
                Trajectory = Some trajectory }

        if gkSaves then
            let gkClubId =
                ClubSide.flip kickerClub
                |> fun side -> if side = HomeClub then ctx.Home.Id else ctx.Away.Id

            false
        else
            true
