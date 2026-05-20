namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player.Decision
open FootballEngine.ML
open FootballEngine.Referee
open FootballEngine.SimStateOps
open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract


module CrossAction =

    let resolve (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : ActionResult =
        let actx = ActionContext.build ctx state
        let cc = ctx.Config.Cross
        let attFrame = actx.Att.OwnFrame
        let defFrame = actx.Def.OwnFrame
        let attRoster = getRoster ctx actx.Att.ClubSide
        let bX, bY = state.Ball.Position.X, state.Ball.Position.Y

        match nearestActiveSlotInFrame attFrame bX bY with
        | ValueNone -> ActionResult.empty
        | ValueSome crosserIdx ->
            let crosser = attRoster.Players[crosserIdx]
            let crosserCond = attFrame.Condition[crosserIdx]

            let crosserPos =
                { X = float attFrame.Physics.PosX[crosserIdx] * 1.0<meter>
                  Y = float attFrame.Physics.PosY[crosserIdx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            let attClubId = actx.Att.ClubId
            let condNorm = normaliseCondition crosserCond

            let crossMean =
                cc.BaseMean
                + normaliseAttr crosser.Technical.Crossing * cc.CrossingWeight
                + normaliseAttr crosser.Technical.Passing * cc.PassingWeight
                + actx.Att.Bonus.SetPlay

            let quality =
                betaSample crossMean (cc.SuccessShapeAlpha + condNorm * cc.SuccessConditionMultiplier)

            let defRoster = getRoster ctx actx.Def.ClubSide

            let defOutfield =
                [| for i = 0 to defFrame.SlotCount - 1 do
                       match defFrame.Physics.Occupancy[i] with
                       | OccupancyKind.Active _ when defRoster.Players[i].Position <> GK ->
                           let sp =
                               { X = float defFrame.Physics.PosX[i] * 1.0<meter>
                                 Y = float defFrame.Physics.PosY[i] * 1.0<meter>
                                 Z = 0.0<meter>
                                 Vx = 0.0<meter / second>
                                 Vy = 0.0<meter / second>
                                 Vz = 0.0<meter / second> }
                           yield (defRoster.Players[i], sp)
                       | _ -> () |]

            let targets =
                [| for i = 0 to attFrame.SlotCount - 1 do
                       match attFrame.Physics.Occupancy[i] with
                       | OccupancyKind.Active _ ->
                           let profile = attRoster.Profiles[i]
                           if
                               profile.AerialThreat > cc.AerialThreatThreshold
                               || profile.AttackingDepth > cc.AttackingDepthThreshold
                           then
                               let sp =
                                   { X = float attFrame.Physics.PosX[i] * 1.0<meter>
                                     Y = float attFrame.Physics.PosY[i] * 1.0<meter>
                                     Z = 0.0<meter>
                                     Vx = 0.0<meter / second>
                                     Vy = 0.0<meter / second>
                                     Vz = 0.0<meter / second> }

                               let defDist =
                                   if defOutfield.Length = 0 then
                                       999.0<meterSquared>
                                   else
                                       defOutfield
                                       |> Array.map (fun (_, dSp) ->
                                           let dx = dSp.X - sp.X
                                           let dy = dSp.Y - sp.Y
                                           dx * dx + dy * dy)
                                       |> Array.min

                               yield (attRoster.Players[i], sp, -defDist)
                       | _ -> () |]
                |> Array.sortBy (fun (_, _, d) -> d)
                |> Array.map (fun (p, sp, _) -> p, sp)

            if targets.Length = 0 then
                let targetX =
                    if actx.Att.AttackDir = LeftToRight then
                        PitchLength - PenaltyAreaDepth
                    else
                        PenaltyAreaDepth

                let dx = targetX - crosserPos.X
                let dy = (PitchWidth / 2.0) - crosserPos.Y
                let dist = sqrt (dx * dx + dy * dy)
                let vx, vy =
                    if dist < 0.01<meter> then 0.0<meter / second>, 0.0<meter / second>
                    else dx / dist * cc.FallbackSpeed, dy / dist * cc.FallbackSpeed

                let fallbackBall =
                    { state.Ball with
                        Position = { state.Ball.Position with Vx = vx; Vy = vy; Vz = cc.FallbackVz }
                        Control = Airborne
                        PendingOffsideSnapshot = None }

                { Events = [|
                    Emit { SubTick = subTick; PlayerId = crosser.Id; ClubId = attClubId; Type = CrossLaunched(crosser.Id, crosser.Id); Context = EventContext.empty }
                    BallUpdate fallbackBall
                |] }
            else
                let target, targetSp = targets[0]
                let accuracyNoise = EngineWeightDefaults.defaults.Outcomes.Cross.GkSkillDivisor / 1000.0 * (1.0 - quality)

                let targetX =
                    targetSp.X + normalSample 0.0 accuracyNoise * 1.0<meter>
                    |> fun x -> clamp x 0.0<meter> PitchLength

                let targetY =
                    targetSp.Y + normalSample 0.0 accuracyNoise * 1.0<meter>
                    |> fun y -> clamp y 0.0<meter> PitchWidth

                let dist =
                    sqrt (
                        (targetX - crosserPos.X) * (targetX - crosserPos.X)
                        + (targetY - crosserPos.Y) * (targetY - crosserPos.Y)
                    )

                let flightTime =
                    if cc.Speed > 0.0<meter / second> then
                        dist / cc.Speed
                    else
                        1.0<second>

                let arrivalSubTick =
                    subTick + int (float (flightTime / 1.0<second>) * float clock.SubTicksPerSecond)

                let spin =
                    { Top =
                        -(normaliseAttr crosser.Technical.Crossing) * cc.SpinTopMult * 1.0<radianPerSecond>
                      Side =
                        (normaliseAttr crosser.Technical.Crossing) * cc.SpinSideMult * 1.0<radianPerSecond> }

                let trajectory =
                    { OriginX = crosserPos.X
                      OriginY = crosserPos.Y
                      TargetX = targetX
                      TargetY = targetY
                      LaunchSubTick = subTick * 1<subtick>
                      EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
                      KickerId = crosser.Id
                      PeakHeight = cc.Vz * cc.Vz / (2.0 * 9.80665<meter / second^2>)
                      Intent = Aimed(crosser.Id, target.Id, quality, AimedKind.Cross) }

                let dx = targetX - crosserPos.X
                let dy = targetY - crosserPos.Y
                let d = sqrt (dx * dx + dy * dy)
                let vx, vy =
                    if d < 0.01<meter> then 0.0<meter / second>, 0.0<meter / second>
                    else dx / d * cc.Speed, dy / d * cc.Speed

                let crossBall =
                    { state.Ball with
                        Position = { state.Ball.Position with Vx = vx; Vy = vy; Vz = cc.Vz }
                        Control = Airborne
                        Spin = spin
                        LastTouchBy = Some crosser.Id
                        Trajectory = Some trajectory
                        PendingOffsideSnapshot = None }

                { Events = [|
                    Emit { SubTick = subTick; PlayerId = crosser.Id; ClubId = attClubId; Type = CrossLaunched(crosser.Id, target.Id); Context = EventContext.empty }
                    BallUpdate crossBall
                |] }
