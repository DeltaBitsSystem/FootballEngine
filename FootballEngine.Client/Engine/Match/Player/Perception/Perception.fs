namespace FootballEngine.Player.Perception

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract


module Perception =

    let private normAttr (v: int) = float v / 20.0

    let computeVisionRadius (vision: int) (anticipation: int) (config: PerceptionConfig) : float<meter> =
        let visionNorm = normAttr vision

        let radiusBase =
            config.VisionRadiusBase
            + (config.VisionRadiusMax - config.VisionRadiusBase) * visionNorm

        let anticipationBonus = normAttr anticipation * config.AnticipationBonusRadius
        let radius = radiusBase + anticipationBonus
        max radius config.MinimumAwarenessFloor

    let computeFacingDirection
        (myPos: Spatial)
        (intentKind: IntentKind)
        (intentTargetX: float32)
        (intentTargetY: float32)
        (ballPos: Spatial)
        (vx: float<meter / second>)
        (vy: float<meter / second>)
        : float =

        let hasTarget =
            match intentKind with
            | IntentKind.PressBall
            | IntentKind.RecoverBall
            | IntentKind.MarkMan
            | IntentKind.SupportAttack
            | IntentKind.ExecuteRun -> true
            | _ -> false

        if hasTarget && (intentTargetX <> 0.0f || intentTargetY <> 0.0f) then
            let tx = float intentTargetX * 1.0<meter>
            let ty = float intentTargetY * 1.0<meter>
            atan2 (ty - myPos.Y) (tx - myPos.X)
        else
            let speed = sqrt (vx * vx + vy * vy)

            if speed > 0.5<meter / second> then
                atan2 vy vx
            else
                atan2 (ballPos.Y - myPos.Y) (ballPos.X - myPos.X)

    let isInCone (observerPos: Spatial) (facingAngle: float) (coneAngle: float) (targetPos: Spatial) : bool =
        let dx = targetPos.X - observerPos.X
        let dy = targetPos.Y - observerPos.Y
        let dist = sqrt (dx * dx + dy * dy)

        if dist < 0.01<meter> then
            true
        else
            let targetAngle = atan2 dy dx
            let angleDiff = abs (targetAngle - facingAngle)

            let normalizedDiff =
                if angleDiff > System.Math.PI then
                    2.0 * System.Math.PI - angleDiff
                else
                    angleDiff

            normalizedDiff <= coneAngle / 2.0

    let computeVisibilityMaskInto
        (canSeeTM: bool[])
        (canSeeOpp: bool[])
        (playerIdx: int)
        (playerPos: Spatial)
        (playerVx: float<meter / second>)
        (playerVy: float<meter / second>)
        (intentKind: IntentKind)
        (intentTargetX: float32)
        (intentTargetY: float32)
        (playerVision: int)
        (playerAnticipation: int)
        (isGoalkeeper: bool)
        (ballPos: Spatial)
        (ownFrame: TeamFrame)
        (oppFrame: TeamFrame)
        (config: PerceptionConfig)
        : VisibilityMask =

        let visionRadius = computeVisionRadius playerVision playerAnticipation config

        let coneAngle =
            if isGoalkeeper then
                config.GoalkeeperConeAngle
            else
                config.VisionConeAngle

        let facingAngle =
            computeFacingDirection playerPos intentKind intentTargetX intentTargetY ballPos playerVx playerVy

        let ownCount = ownFrame.SlotCount
        let oppCount = oppFrame.SlotCount
        System.Array.Clear(canSeeTM, 0, canSeeTM.Length)
        System.Array.Clear(canSeeOpp, 0, canSeeOpp.Length)
        let mutable visibleTM = 0
        let mutable visibleOPP = 0

        for i = 0 to ownCount - 1 do
            if i <> playerIdx then
                match ownFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let tx = float ownFrame.Physics.PosX[i] * 1.0<meter>
                    let ty = float ownFrame.Physics.PosY[i] * 1.0<meter>

                    let targetPos =
                        { X = tx
                          Y = ty
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                    let dist = playerPos.DistTo2D targetPos
                    let inFloor = dist <= config.MinimumAwarenessFloor
                    let inRadius = dist <= visionRadius
                    let inCone = isInCone playerPos facingAngle coneAngle targetPos

                    let inPeripheral =
                        if not inCone then
                            let peripheralRadius = visionRadius * config.PeripheralMultiplier
                            dist <= peripheralRadius
                        else
                            false

                    if inFloor || (inRadius && inCone) || inPeripheral then
                        canSeeTM[i] <- true
                        visibleTM <- visibleTM + 1
                | _ -> ()

        for i = 0 to oppCount - 1 do
            match oppFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let tx = float oppFrame.Physics.PosX[i] * 1.0<meter>
                let ty = float oppFrame.Physics.PosY[i] * 1.0<meter>

                let targetPos =
                    { X = tx
                      Y = ty
                      Z = 0.0<meter>
                      Vx = 0.0<meter / second>
                      Vy = 0.0<meter / second>
                      Vz = 0.0<meter / second> }

                let dist = playerPos.DistTo2D targetPos
                let inFloor = dist <= config.MinimumAwarenessFloor
                let inRadius = dist <= visionRadius
                let inCone = isInCone playerPos facingAngle coneAngle targetPos

                let inPeripheral =
                    if not inCone then
                        let peripheralRadius = visionRadius * config.PeripheralMultiplier
                        dist <= peripheralRadius
                    else
                        false

                if inFloor || (inRadius && inCone) || inPeripheral then
                    canSeeOpp[i] <- true
                    visibleOPP <- visibleOPP + 1
            | _ -> ()

        let ballDist = playerPos.DistTo2D ballPos

        let canSeeBall =
            ballDist <= visionRadius || ballDist <= config.MinimumAwarenessFloor

        { CanSeeTeammates = canSeeTM
          CanSeeOpponents = canSeeOpp
          CanSeeBall = canSeeBall
          VisibleTeammateCount = visibleTM
          VisibleOpponentCount = visibleOPP }

    let resolveBestPassTargetThroughMask
        (meIdx: int)
        (attFrame: TeamFrame)
        (attRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (dir: AttackDir)
        (mask: VisibilityMask)
        : (int * Spatial) voption =

        let mutable bestIdx = -1
        let mutable bestScore = -1.0
        let mutable bestPos = defaultSpatial 0.0<meter> 0.0<meter>
        let visionWeight = float attRoster.Players[meIdx].Mental.Vision / 100.0
        let forwardDir = if dir = LeftToRight then 1.0 else -1.0

        for i = 0 to attFrame.SlotCount - 1 do
            if i <> meIdx && mask.CanSeeTeammates[i] then
                match attFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let mx = float attFrame.Physics.PosX[meIdx] * 1.0<meter>
                    let my = float attFrame.Physics.PosY[meIdx] * 1.0<meter>
                    let tx = float attFrame.Physics.PosX[i] * 1.0<meter>
                    let ty = float attFrame.Physics.PosY[i] * 1.0<meter>
                    let dx = tx - mx
                    let dy = ty - my
                    let dist = sqrt (dx * dx + dy * dy)

                    if dist > 1.0<meter> then
                        let proximityScore = 1.0 / (1.0 + float dist * 0.1)
                        let forwardBonus = if dx * forwardDir > 5.0<meter> then 0.35 else 0.0
                        let backwardPenalty = if dx * forwardDir < -10.0<meter> then -0.25 else 0.0
                        let mutable laneClear = true

                        for j = 0 to defFrame.SlotCount - 1 do
                            match defFrame.Physics.Occupancy[j] with
                            | OccupancyKind.Active _ ->
                                let ddx = float defFrame.Physics.PosX[j] * 1.0<meter>
                                let ddy = float defFrame.Physics.PosY[j] * 1.0<meter>

                                let t =
                                    clampFloat (((ddx - mx) * dx + (ddy - my) * dy) / (dx * dx + dy * dy)) 0.0 1.0

                                let closestX = mx + float t * dx
                                let closestY = my + float t * dy

                                let perpDistSq =
                                    (ddx - closestX) * (ddx - closestX) + (ddy - closestY) * (ddy - closestY)

                                if perpDistSq < 9.0<meter^2> then
                                    laneClear <- false
                            | _ -> ()

                        let laneBonus = if laneClear then 0.20 else -0.30

                        let score =
                            proximityScore + forwardBonus + backwardPenalty + laneBonus + visionWeight * 0.1

                        if score > bestScore then
                            bestScore <- score
                            bestIdx <- i
                            bestPos <- defaultSpatial tx ty
                | _ -> ()

        if bestIdx >= 0 then
            ValueSome(bestIdx, bestPos)
        else
            ValueNone

    let resolveNearestOpponentThroughMask
        (myPos: Spatial)
        (oppFrame: TeamFrame)
        (mask: VisibilityMask)
        : int voption * float<meter> =

        let mutable bestIdx: int voption = ValueNone
        let mutable bestDist = 1e18<meter>

        for i = 0 to oppFrame.SlotCount - 1 do
            if mask.CanSeeOpponents[i] then
                match oppFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let ox = float oppFrame.Physics.PosX[i] * 1.0<meter>
                    let oy = float oppFrame.Physics.PosY[i] * 1.0<meter>

                    let oppPos =
                        { X = ox
                          Y = oy
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                    let dist = myPos.DistTo2D oppPos

                    if dist < bestDist then
                        bestDist <- dist
                        bestIdx <- ValueSome i
                | _ -> ()

        bestIdx, bestDist
