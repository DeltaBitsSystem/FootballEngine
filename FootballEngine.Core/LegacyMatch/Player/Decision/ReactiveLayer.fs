namespace FootballEngine.Player.Decision

open FootballEngine
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract


type ReactiveIntent =
    | PressBall of tx: float32 * ty: float32
    | TackleAttempt of oppSlotIdx: int
    | InterceptLane of tx: float32 * ty: float32
    | NoReaction

module ReactiveLayer =

    let private reactiveRadius = 45.0<meter>
    let private tackleRadius = 3.0<meter>
    let private pressRadius = 12.0<meter>
    let private coverRadius = 20.0<meter>
    let private laneCutRadiusSq = 9.0f
    let private distantReactionThreshold = 0.55

    let evaluateReactiveIntent
        (meIdx: int)
        (ownFrame: TeamFrame)
        (oppFrame: TeamFrame)
        (ballCarrierOppIdx: int)
        (bcx: float32)
        (bcy: float32)
        (aggression: float)
        : ReactiveIntent =

        if ballCarrierOppIdx < 0 then
            NoReaction
        else
            let myX = ownFrame.Physics.PosX[meIdx]
            let myY = ownFrame.Physics.PosY[meIdx]
            let dx = float myX - float bcx
            let dy = float myY - float bcy
            let dist = sqrt (dx * dx + dy * dy) * 1.0<meter>

            if dist > reactiveRadius then
                NoReaction
            elif dist > 25.0<meter> && aggression < distantReactionThreshold then
                NoReaction
            elif dist < tackleRadius then
                TackleAttempt ballCarrierOppIdx
            elif dist < pressRadius then
                PressBall(bcx, bcy)
            else
                let mutable bestTargetX = bcx
                let mutable bestTargetY = bcy
                let mutable bestScore = System.Single.MaxValue

                for j = 0 to oppFrame.SlotCount - 1 do
                    if j <> ballCarrierOppIdx then
                        let active = oppFrame.Physics.Occupancy[j]

                        match active with
                        | OccupancyKind.Active _ ->
                            let odx = oppFrame.Physics.PosX[j] - bcx
                            let ody = oppFrame.Physics.PosY[j] - bcy
                            let oDist = sqrt (odx * odx + ody * ody)

                            if oDist > 3.0f && oDist < 30.0f then
                                let laneDistSq =
                                    MatchSpatial.pointToLineDistSq
                                        myX
                                        myY
                                        bcx
                                        bcy
                                        oppFrame.Physics.PosX[j]
                                        oppFrame.Physics.PosY[j]

                                if laneDistSq < laneCutRadiusSq && laneDistSq < bestScore then
                                    bestScore <- laneDistSq
                                    bestTargetX <- oppFrame.Physics.PosX[j]
                                    bestTargetY <- oppFrame.Physics.PosY[j]
                        | _ -> ()

                if bestScore < laneCutRadiusSq then
                    InterceptLane(bestTargetX, bestTargetY)
                else
                    NoReaction
