namespace FootballEngine.Player.Actions

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Actions
open FootballEngine.Player.Decision
open FootballEngine.Referee
open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps


module ShotAction =

    let private finishingBonusForPosition (cfg: ShotConfig) =
        function
        | ST
        | AMC
        | AML
        | AMR -> cfg.FinishingBonusAM
        | MC -> cfg.FinishingBonusMC
        | _ -> cfg.FinishingBonusOther

    let private calcShotPower
        (cfg: ShotConfig)
        (player: Player)
        (condition: int)
        (composure: float)
        (urgency: float)
        (dist: float<meter>)
        (homeComposure: float)
        =
        let bonus = finishingBonusForPosition cfg player.Position
        let condFactor = sqrt (float condition / cfg.CondFactorDivisor)
        let basePower = float player.Technical.Finishing / cfg.BasePowerDivisor

        basePower
        * condFactor
        * (1.0 + composure * cfg.ComposureMultiplier)
        * (1.0 + urgency * cfg.UrgencyMultiplier)
        * bonus

    let resolve
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : ActionResult =
        let actx = ActionContext.build ctx state
        let sc = ctx.Config.Shot
        let attClubId = actx.Att.ClubId
        let attFrame = actx.Att.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide

        let bX = state.Ball.Position.X
        let bY = state.Ball.Position.Y

        let inChance = actx.Zone = AttackingZone || actx.Zone = MidfieldZone

        match nearestActiveSlotInFrame attFrame bX bY with
        | ValueNone -> ActionResult.empty
        | ValueSome shooterIdx ->
            let shooter = attRoster.Players[shooterIdx]
            let shooterCond = int attFrame.Condition[shooterIdx]
            let shooterProfile = attRoster.Profiles[shooterIdx]

            if not inChance then
                { Events = [| Emit { SubTick = subTick; PlayerId = shooter.Id; ClubId = attClubId; Type = ShotOffTarget; Context = EventContext.empty } |] }
            else
                let quality =
                    let distToGoal = PhysicsContract.distToGoal bX actx.Att.AttackDir
                    let distNorm = clamp (distToGoal / sc.NormalisationDistance) 0.0 1.0

                    let positionBonus =
                        shooterProfile.Directness * sc.PositionDirectnessWeight
                        + shooterProfile.AttackingDepth * sc.PositionDepthWeight
                        + shooterProfile.CreativityWeight * sc.PositionCreativityWeight

                    (1.0 - distNorm) * sc.DistNormWeight + positionBonus * sc.PositionBonusWeight

                if quality < sc.QualityGate then
                    { Events = [| Emit { SubTick = subTick; PlayerId = shooter.Id; ClubId = attClubId; Type = ShotOffTarget; Context = EventContext.empty } |] }
                else
                    let attTactics = getTactics state actx.Att.ClubSide
                    let attInstructions = getInstructions state actx.Att.ClubSide
                    let tacticsCfg = tacticsConfig attTactics attInstructions

                    let composure =
                        pressureMultiplier attClubId ctx state * tacticsCfg.UrgencyMultiplier

                    let u = matchUrgency attClubId ctx state clock * tacticsCfg.UrgencyMultiplier

                    let dist = distToGoal bX actx.Att.AttackDir * sc.DistanceToGoalMultiplier

                    let homeComposure = actx.Att.Bonus.ShotCompos
                    let finishing = calcShotPower sc shooter shooterCond composure u dist homeComposure
                    let dirSign = forwardX actx.Att.AttackDir

                    let finishingNorm =
                        Math.Clamp(normaliseAttr shooter.Technical.Finishing, sc.FinishingMin, sc.FinishingMax)

                    let speed = ActionMath.shotSpeed shooter.Technical.Finishing

                    let goalX =
                        if actx.Att.AttackDir = LeftToRight then
                            PitchLength
                        else
                            0.0<meter>

                    let distToGoal = abs (goalX - bX)

                    let flightTime =
                        if float speed > 0.0 then
                            float distToGoal / float speed
                        else
                            1.0

                    let arrivalSubTick = subTick + int (flightTime * float clock.SubTicksPerSecond)

                    let peakHeight =
                        let vz = abs (normalSample (float sc.VzBase) sc.VzVariance) * 1.0<meter / second>

                        if vz > 0.0<meter / second> then
                            vz * vz / (2.0 * 9.80665<meter / second^2>)
                        else
                            0.0<meter>

                    let speedMag = float speed

                    let angleSpread = sc.AngleSpreadBase * (1.0 - finishingNorm)
                    let angle = normalSample 0.0 angleSpread

                    let vx = dirSign * speedMag * Math.Cos(angle)
                    let vy = speedMag * Math.Sin(angle)
                    let vz = abs (normalSample (float sc.VzBase) sc.VzVariance) * 1.0<meter / second>

                    let targetY = if abs vx > 0.001 then bY + (vy / vx) * (goalX - bX) else bY

                    let spin =
                        { Top =
                            -(normaliseAttr shooter.Technical.Finishing)
                            * sc.SpinTopMultiplier
                            * 1.0<radianPerSecond>
                          Side = 0.0<radianPerSecond> }

                    let trajectory =
                        { OriginX = bX
                          OriginY = bY
                          TargetX = goalX
                          TargetY = targetY
                          LaunchSubTick = subTick * 1<subtick>
                          EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
                          KickerId = shooter.Id
                          PeakHeight = peakHeight
                          Intent = Struck(shooter.Id, quality) }

                    let angleToGoal =
                        let dy = abs (targetY - bY)
                        let dx = abs (goalX - bX)

                        if dx > 0.0<meter> then
                            float (Math.Atan2(float dy, float dx)) * 180.0 / Math.PI
                        else
                            90.0

                    let xgModel =
                        { DistanceToGoal = distToGoal
                          AngleToGoal = angleToGoal
                          ShotType = ShotType.DrivenShot
                          BodyPart = "Foot"
                          AssistType = "OpenPlay"
                          PressureLevel = 0.5
                          IsOneOnOne = false
                          IsSetPiece = false }

                    let xgValue = xGCalculator.calculateWithDefaults xgModel

                    let newBall =
                        { state.Ball with
                            Position =
                                { state.Ball.Position with
                                    Vx = vx * 1.0<meter / second>
                                    Vy = vy * 1.0<meter / second>
                                    Vz = vz
                                    X = bX }
                            Spin = spin
                            LastTouchBy = Some shooter.Id
                            Control = Airborne
                            Trajectory = Some trajectory
                            PendingOffsideSnapshot = None }

                    let momentumDelta = forwardX actx.Att.AttackDir * ctx.Config.Duel.MomentumBonus

                    { Events = [|
                        Emit { SubTick = subTick
                               PlayerId = shooter.Id
                               ClubId = attClubId
                               Type = MatchEventType.ShotLaunched
                               Context =
                                 EventContext.at (float bX) (float bY)
                                 |> fun c -> { c with ExpectedGoal = Some xgValue } }
                        BallUpdate newBall
                        MomentumDelta momentumDelta
                        EmitSemantic(SemanticEvent.ShotLaunched shooter.Id)
                    |] }
