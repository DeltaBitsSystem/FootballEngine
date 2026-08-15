namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.MatchSpatial

module PassDecision =

    type PassCandidate =
        { Target: PassTarget
          LaneClear: bool
          Score: float }

    let private passLaneClear (bX: float32) (bY: float32) (tX: float32) (tY: float32) (defFrame: TeamFrame) : bool =
        let tdx = tX - bX
        let tdy = tY - bY
        let lenSq = tdx * tdx + tdy * tdy

        if lenSq < 0.01f then
            true
        else
            let invLenSq = 1.0f / lenSq
            let radiusSq = 9.0f
            let mutable blocked = false
            let mutable i = 0

            while not blocked && i < defFrame.SlotCount do
                match defFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let dpx = defFrame.Physics.PosX[i] - bX
                    let dpy = defFrame.Physics.PosY[i] - bY
                    let t = (dpx * tdx + dpy * tdy) * invLenSq

                    if t >= 0.0f && t <= 1.0f then
                        let crossSq = (dpx * tdy - dpy * tdx) * (dpx * tdy - dpy * tdx)

                        if crossSq < radiusSq * lenSq then
                            blocked <- true
                | _ -> ()

                i <- i + 1

            not blocked

    let private computeScore
        (dist: float<meter>)
        (forwardDir: float)
        (dx: float<meter>)
        (laneClear: bool)
        (visionWeight: float)
        : float =
        let proximityScore = 1.0 / (1.0 + float dist * 0.1)
        let forwardBonus = if float dx * forwardDir > 5.0 then 0.35 else 0.0
        let backwardPenalty = if float dx * forwardDir < -10.0 then -0.25 else 0.0
        let laneBonus = if laneClear then 0.20 else -0.30
        proximityScore + forwardBonus + backwardPenalty + laneBonus + visionWeight * 0.1

    let findBestPassTarget
        (attFrame: TeamFrame)
        (attRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (meIdx: int)
        (attackDir: AttackDir)
        (passConfig: PassConfig)
        (mask: VisibilityMask option)
        : PassCandidate option =
        let bX = attFrame.Physics.PosX[meIdx]
        let bY = attFrame.Physics.PosY[meIdx]
        let forwardDir = if attackDir = LeftToRight then 1.0 else -1.0

        let visionWeight =
            let ri =
                match attFrame.Physics.Occupancy[meIdx] with
                | OccupancyKind.Active ri -> ri
                | _ -> meIdx

            float attRoster.Players[ri].Mental.Vision / 100.0

        let mutable bestTarget: PassCandidate option = None
        let mutable bestScore = System.Double.MinValue

        for i = 0 to attFrame.SlotCount - 1 do
            if i <> meIdx then
                let visible =
                    match mask with
                    | Some m -> m.CanSeeTeammates[i]
                    | None -> true

                if visible then
                    match attFrame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ ->
                        let px = attFrame.Physics.PosX[i]
                        let py = attFrame.Physics.PosY[i]
                        let dx = px - bX
                        let dy = py - bY
                        let distSq = float (dx * dx + dy * dy)
                        let dist = sqrt distSq

                        if dist > 1.0 then
                            let laneClear = passLaneClear bX bY px py defFrame
                            let distM = dist * 1.0<meter>
                            let dxM = float dx * 1.0<meter>
                            let score = computeScore distM forwardDir dxM laneClear visionWeight

                            if score > bestScore then
                                let candidateIdx = i
                                let pid = SlotAccess.playerIdAtSlot attFrame attRoster i
                                let pos = defaultSpatial (float px * 1.0<meter>) (float py * 1.0<meter>)

                                match PassTarget.create meIdx candidateIdx pid pos distM with
                                | Some target ->
                                    bestScore <- score

                                    bestTarget <-
                                        Some
                                            { Target = target
                                              LaneClear = laneClear
                                              Score = score }
                                | None -> ()
                    | _ -> ()

        bestTarget
