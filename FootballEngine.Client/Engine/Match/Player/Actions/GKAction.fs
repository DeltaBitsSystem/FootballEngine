namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player.Decision
open FootballEngine.Referee
open FootballEngine.SimStateOps
open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract



module GKAction =

    type DistributionType =
        | Throw
        | Roll
        | GoalKick
        | Punt

    let private findBestDistributionTarget
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (gkIdx: int)
        (gkFrame: TeamFrame)
        (gkRoster: PlayerRoster)
        (gkc: GKConfig)
        : Player * Spatial * DistributionType =

        let gkX = float gkFrame.Physics.PosX[gkIdx] * 1.0<meter>
        let gkY = float gkFrame.Physics.PosY[gkIdx] * 1.0<meter>

        let gkPos =
            { X = gkX
              Y = gkY
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        let mutable bestTarget: Player option = None

        let mutable bestTargetSp =
            { X = 0.0<meter>
              Y = 0.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        let mutable bestScore = -1.0
        let mutable bestDistType = Roll

        let attClub =
            match state.Ball.Control with
            | Controlled(side, _)
            | Receiving(side, _, _) -> side
            | _ -> ClubSide.flip state.AttackingSide

        let defClub = ClubSide.flip attClub
        let defFrame = getFrame state defClub
        let dir = attackDirFor attClub state

        for i = 0 to gkFrame.SlotCount - 1 do
            match gkFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active rosterIdx when gkRoster.Players[rosterIdx].Position <> GK ->
                let player = gkRoster.Players[rosterIdx]
                let px = float gkFrame.Physics.PosX[i] * 1.0<meter>
                let py = float gkFrame.Physics.PosY[i] * 1.0<meter>

                let dist =
                    gkPos.DistTo2D
                        { X = px
                          Y = py
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                let nearestDefDist =
                    let mutable minDist = 999.0<meter>

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

                            if d < minDist then
                                minDist <- d
                        | _ -> ()

                    minDist

                let forwardBonus =
                    if dir = LeftToRight then
                        float px / float PitchLength
                    else
                        1.0 - float px / float PitchLength

                let safetyBonus = float nearestDefDist / 10.0

                let distType, speed, score =
                    if float dist < 15.0 then
                        Roll, gkc.RollSpeed, safetyBonus * 0.6 + forwardBonus * 0.4
                    elif float dist < 30.0 then
                        Throw, gkc.ThrowSpeed, safetyBonus * 0.5 + forwardBonus * 0.5
                    elif float dist < 50.0 then
                        Punt, gkc.PuntSpeed, safetyBonus * 0.3 + forwardBonus * 0.7
                    else
                        GoalKick, gkc.GoalKickSpeed, forwardBonus

                let noise = normalSample 0.0 gkc.DistributionDecisionNoise
                let totalScore = score + noise

                if totalScore > bestScore then
                    bestScore <- totalScore
                    bestTarget <- Some player

                    bestTargetSp <-
                        { X = px
                          Y = py
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                    bestDistType <- distType
            | _ -> ()

        match bestTarget with
        | Some t -> t, bestTargetSp, bestDistType
        | None ->
            let fallbackX =
                if dir = LeftToRight then
                    gkX + 20.0<meter>
                else
                    gkX - 20.0<meter>

            let fallback =
                gkRoster.Players
                |> Array.tryFind (fun p -> p.Position <> GK)
                |> Option.defaultValue (gkRoster.Players[0])

            fallback,
            { X = fallbackX
              Y = gkY
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> },
            Roll

    let resolve
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : ActionResult =
        let gkc = ctx.Config.GK
        let actx = ActionContext.build ctx state

        match state.Ball.Control with
        | Controlled(side, gkId) ->
            let frame = getFrame state side
            let roster = getRoster ctx side

            let gkIdx =
                let mutable idx = -1

                for i = 0 to frame.SlotCount - 1 do
                    match frame.Physics.Occupancy[i] with
                    | OccupancyKind.Active rosterIdx when roster.Players[rosterIdx].Id = gkId -> idx <- i
                    | _ -> ()

                idx

            if gkIdx < 0 then
                ActionResult.empty
            else
                let shouldDistribute =
                    match state.Ball.GKHoldSinceSubTick with
                    | Some since -> subTick * 1<subtick> - since >= SimulationClock.deltaToSubtick gkc.GKDecisionWindowSubTicks
                    | None -> false

                if not shouldDistribute then
                    ActionResult.empty
                else
                    let target, targetSp, distType =
                        findBestDistributionTarget subTick ctx state gkIdx frame roster gkc

                    let speed =
                        match distType with
                        | Throw -> gkc.ThrowSpeed
                        | Roll -> gkc.RollSpeed
                        | GoalKick -> gkc.GoalKickSpeed
                        | Punt -> gkc.PuntSpeed

                    let gkX = float frame.Physics.PosX[gkIdx] * 1.0<meter>
                    let gkY = float frame.Physics.PosY[gkIdx] * 1.0<meter>

                    let dx = targetSp.X - gkX
                    let dy = targetSp.Y - gkY
                    let dist = sqrt (dx * dx + dy * dy)
                    let vz = speed * 0.15

                    let vx, vy =
                        if dist < 0.01<meter> then
                            0.0<meter / second>, 0.0<meter / second>
                        else
                            dx / dist * speed, dy / dist * speed

                    let flightTime =
                        if speed > 0.0<meter / second> then
                            dist / speed
                        else
                            0.5<second>

                    let arrivalSubTick =
                        subTick + int (float (flightTime / 1.0<second>) * float clock.SubTicksPerSecond)

                    let peakHeight =
                        if vz > 0.0<meter / second> then
                            vz * vz / (2.0 * 9.80665<meter / second^2>)
                        else
                            0.0<meter>

                    let trajectory =
                        { OriginX = gkX
                          OriginY = gkY
                          TargetX = targetSp.X
                          TargetY = targetSp.Y
                          LaunchSubTick = subTick * 1<subtick>
                          EstimatedArrivalSubTick = arrivalSubTick * 1<subtick>
                          KickerId = gkId
                          PeakHeight = peakHeight
                          Intent = Aimed(gkId, target.Id, 0.5, RegularPass) }

                    let gkBall =
                        { state.Ball with
                            Position =
                                { state.Ball.Position with
                                    Vx = vx
                                    Vy = vy
                                    Vz = vz }
                            Control = Airborne
                            LastTouchBy = Some gkId
                            GKHoldSinceSubTick = None
                            PlayerHoldSinceSubTick = None
                            Trajectory = Some trajectory }

                    { Events = [|
                        Emit { SubTick = subTick
                               PlayerId = gkId
                               ClubId = (if side = HomeClub then ctx.Home.Id else ctx.Away.Id)
                               Type = GKDistribution(gkId, target.Id)
                               Context = EventContext.empty }
                        BallUpdate gkBall
                    |] }
        | _ -> ActionResult.empty
