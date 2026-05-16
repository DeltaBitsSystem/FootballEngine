namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.IntentPhaseTypes
open SimStateOps


module CognitiveFrameModule =

    let build (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) : CognitiveFrame =
        let ownFrame = getFrame state clubSide
        let oppFrame = getFrame state (ClubSide.flip clubSide)
        let ownRoster = getRoster ctx clubSide
        let n = ownFrame.SlotCount
        let m = oppFrame.SlotCount

        let buffers =
            let existing =
                if clubSide = HomeClub then
                    state.HomeCFrameBuffers
                else
                    state.AwayCFrameBuffers

            match existing with
            | Some buf when buf.NearestTeammateIdx.Length >= n ->
                Array.fill buf.NearestTeammateIdx 0 buf.NearestTeammateIdx.Length 0s
                Array.fill buf.NearestTeammateDistSq 0 buf.NearestTeammateDistSq.Length System.Single.MaxValue
                Array.fill buf.NearestOpponentIdx 0 buf.NearestOpponentIdx.Length 0s
                Array.fill buf.NearestOpponentDistSq 0 buf.NearestOpponentDistSq.Length System.Single.MaxValue
                Array.fill buf.BestPassTargetIdx 0 buf.BestPassTargetIdx.Length -1s
                Array.fill buf.BestPassTargetPos 0 buf.BestPassTargetPos.Length ValueNone
                Array.fill buf.PressureOnPlayer 0 buf.PressureOnPlayer.Length System.Single.MaxValue
                Array.fill buf.LaneClear 0 buf.LaneClear.Length true
                buf
            | _ ->
                let newBuf = CognitiveFrameBuffers.create n

                if clubSide = HomeClub then
                    state.HomeCFrameBuffers <- Some newBuf
                else
                    state.AwayCFrameBuffers <- Some newBuf

                newBuf

        let nearestTMIdx = buffers.NearestTeammateIdx
        let nearestTMDistSq = buffers.NearestTeammateDistSq
        let nearestOppIdx = buffers.NearestOpponentIdx
        let nearestOppDistSq = buffers.NearestOpponentDistSq
        let bestPassIdx = buffers.BestPassTargetIdx
        let bestPassPos = buffers.BestPassTargetPos
        let pressure = buffers.PressureOnPlayer
        let laneClear = buffers.LaneClear

        let bx = float32 state.Ball.Position.X
        let by = float32 state.Ball.Position.Y
        let dir = attackDirFor clubSide state
        let ballZone = ofBallX state.Ball.Position.X dir
        let phase = phaseFromBallZone dir state.Ball.Position.X

        // Precompute lane-clearance matrix for all (passer, target) pairs
        if laneClear.Length >= n * n then
            for i = 0 to n - 1 do
                match ownFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let ox = ownFrame.Physics.PosX[i]
                    let oy = ownFrame.Physics.PosY[i]
                    for j = 0 to n - 1 do
                        if i <> j then
                            match ownFrame.Physics.Occupancy[j] with
                            | OccupancyKind.Active _ ->
                                let tx = ownFrame.Physics.PosX[j]
                                let ty = ownFrame.Physics.PosY[j]
                                if not (passLaneClear ox oy tx ty oppFrame) then
                                    laneClear[i * n + j] <- false
                            | _ -> ()
                | _ -> ()

        let ballCarrierOppIdx =
            let ballControlledByOpp =
                match state.Ball.Control with
                | Controlled(side, pid) -> if side <> clubSide then Some pid else None
                | Receiving(side, pid, _) -> if side <> clubSide then Some pid else None
                | _ -> None

            match ballControlledByOpp with
            | Some pid ->
                let mutable found = -1s
                let oppFrame = getFrame state (ClubSide.flip clubSide)
                let oppRoster = getRoster ctx (ClubSide.flip clubSide)

                for i = 0 to oppFrame.SlotCount - 1 do
                    match oppFrame.Physics.Occupancy[i] with
                    | OccupancyKind.Active rosterIdx when oppRoster.Players[rosterIdx].Id = pid -> found <- int16 i
                    | _ -> ()

                found
            | _ -> -1s

        for i = 0 to n - 1 do
            match ownFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let ox = ownFrame.Physics.PosX[i]
                let oy = ownFrame.Physics.PosY[i]

                let mutable minTMDistSq = System.Single.MaxValue
                let mutable minTMIdx = int16 -1s

                for j = 0 to n - 1 do
                    if i <> j then
                        match ownFrame.Physics.Occupancy[j] with
                        | OccupancyKind.Active _ ->
                            let dx = ownFrame.Physics.PosX[j] - ox
                            let dy = ownFrame.Physics.PosY[j] - oy
                            let d = dx * dx + dy * dy

                            if d < minTMDistSq then
                                minTMDistSq <- d
                                minTMIdx <- int16 j
                        | _ -> ()

                nearestTMIdx[i] <- minTMIdx
                nearestTMDistSq[i] <- minTMDistSq

                let mutable minOppDistSq = System.Single.MaxValue
                let mutable minOppIdx = int16 -1s

                for j = 0 to m - 1 do
                    match oppFrame.Physics.Occupancy[j] with
                    | OccupancyKind.Active _ ->
                        let dx = oppFrame.Physics.PosX[j] - ox
                        let dy = oppFrame.Physics.PosY[j] - oy
                        let d = dx * dx + dy * dy

                        if d < minOppDistSq then
                            minOppDistSq <- d
                            minOppIdx <- int16 j
                    | _ -> ()

                nearestOppIdx[i] <- minOppIdx
                nearestOppDistSq[i] <- minOppDistSq
                pressure[i] <- minOppDistSq

                // Best pass target using precomputed lane-clearance cache
                let visionWeight = float ownRoster.Players[i].Mental.Vision / 100.0
                let mutable bestPTIdx = -1s
                let mutable bestPTScore = System.Double.MinValue
                let baseIdx = i * n

                for j = 0 to n - 1 do
                    if j <> i then
                        match ownFrame.Physics.Occupancy[j] with
                        | OccupancyKind.Active _ ->
                            let px = ownFrame.Physics.PosX[j]
                            let py = ownFrame.Physics.PosY[j]
                            let dx = px - ox
                            let dy = py - oy
                            let dist = sqrt (float (dx * dx + dy * dy))
                            let laneBonus = if laneClear[baseIdx + j] then 0.2 else -0.3
                            let forwardX = float (px - ox)
                            let forwardBonus = 
                                match dir with
                                | LeftToRight -> if forwardX > 5.0 then 0.35 elif forwardX < -10.0 then -0.25 else 0.0
                                | RightToLeft -> if forwardX < -5.0 then 0.35 elif forwardX > 10.0 then -0.25 else 0.0
                            let score = (1.0 / (1.0 + dist * 0.1)) + forwardBonus + laneBonus + visionWeight * 0.1
                            if score > bestPTScore then
                                bestPTScore <- score
                                bestPTIdx <- int16 j
                        | _ -> ()

                if bestPTIdx >= 0s then
                    bestPassIdx[i] <- bestPTIdx
                    let bi = int bestPTIdx
                    bestPassPos[i] <- ValueSome (defaultSpatial (float ownFrame.Physics.PosX[bi] * 1.0<meter>) (float ownFrame.Physics.PosY[bi] * 1.0<meter>))

            | _ -> ()

        { NearestTeammateIdx = nearestTMIdx
          NearestTeammateDistSq = nearestTMDistSq
          NearestOpponentIdx = nearestOppIdx
          NearestOpponentDistSq = nearestOppDistSq
          BestPassTargetIdx = bestPassIdx
          BestPassTargetPos = bestPassPos
          BallX = bx
          BallY = by
          BallZone = ballZone
          Phase = phase
          PressureOnPlayer = pressure
          SlotCount = n
          BallCarrierOppIdx = ballCarrierOppIdx }
