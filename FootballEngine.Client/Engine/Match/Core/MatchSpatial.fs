namespace FootballEngine

open FootballEngine.Domain

open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module PitchMath =

    let inline distance (x1, y1) (x2, y2) =
        let dx = x1 - x2
        let dy = y1 - y2
        sqrt (dx * dx + dy * dy)

    let inline distanceSq (x1, y1) (x2, y2) =
        let dx = x1 - x2
        let dy = y1 - y2
        dx * dx + dy * dy

    let inline nearestIdx (positions: (float * float)[]) (point: float * float) =
        positions
        |> Array.mapi (fun i pos -> i, distance point pos)
        |> Array.minBy snd
        |> fst

    let jitter (oX: float<meter>) (oY: float<meter>) (tX: float<meter>) (tY: float<meter>) scale nx ny =
        clamp (oX + (tX - oX) * scale + (normalSample 0.0 nx) * 1.0<meter>) 0.0<meter> PitchLength,
        clamp (oY + (tY - oY) * scale + (normalSample 0.0 ny) * 1.0<meter>) 0.0<meter> PitchWidth

    type PlayerRole =
        | Defender
        | Midfielder
        | Attacker
        | Goalkeeper

    let playerRole (profile: BehavioralProfile) (position: Position) =
        if profile.DefensiveHeight > 0.6 && profile.Directness < 0.3 then
            Defender
        elif profile.AttackingDepth > 0.7 && profile.CreativityWeight < 0.3 then
            Attacker
        elif position = GK then
            Goalkeeper
        else
            Midfielder

module MatchSpatial =
    let attackDirFor (clubSide: ClubSide) (state: SimState) =
        match clubSide with
        | HomeClub -> state.HomeAttackDir
        | AwayClub ->
            match state.HomeAttackDir with
            | LeftToRight -> RightToLeft
            | RightToLeft -> LeftToRight

    let defaultSpatial (x: float<meter>) (y: float<meter>) : Spatial =
        { X = x
          Y = y
          Z = 0.0<meter>
          Vx = 0.0<meter / second>
          Vy = 0.0<meter / second>
          Vz = 0.0<meter / second> }

    let ofBallX (x: float<meter>) (dir: AttackDir) : PitchZone =
        let effectiveX =
            match dir with
            | LeftToRight -> x
            | RightToLeft -> PitchLength - x

        if effectiveX < 30.0<meter> then DefensiveZone
        elif effectiveX <= 70.0<meter> then MidfieldZone
        else AttackingZone

    let spatialXY (sp: Spatial) = sp.X, sp.Y
    let kickOffSpatial = defaultSpatial HalfwayLineX (PitchWidth / 2.0)

    let withBallVelocity
        (vx: float<meter / second>)
        (vy: float<meter / second>)
        (vz: float<meter / second>)
        (state: SimState)
        =
        state.Ball <-
            { state.Ball with
                Position =
                    { state.Ball.Position with
                        Vx = vx
                        Vy = vy
                        Vz = vz } }

    let ballTowards
        (originX: float<meter>)
        (originY: float<meter>)
        (targetX: float<meter>)
        (targetY: float<meter>)
        (speed: float<meter / second>)
        (vz: float<meter / second>)
        (state: SimState)
        =
        let origin =
            { X = originX
              Y = originY
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        let target =
            { X = targetX
              Y = targetY
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        let dist = origin.DistTo2D target

        if dist < 0.01<meter> then
            withBallVelocity 0.0<meter / second> 0.0<meter / second> vz state
        else
            let dx = targetX - originX
            let dy = targetY - originY
            withBallVelocity (dx / dist * speed) (dy / dist * speed) vz state

    let nearestActiveSlotInFrameExcluding
        (frame: TeamFrame)
        (excludeIdx: int)
        (x: float<meter>)
        (y: float<meter>)
        : int voption =
        let mutable bestIdx = ValueNone
        let mutable bestDistSq = System.Single.MaxValue
        let x32 = float32 x
        let y32 = float32 y

        for i = 0 to frame.SlotCount - 1 do
            if i <> excludeIdx then
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let dx = frame.Physics.PosX[i] - x32
                    let dy = frame.Physics.PosY[i] - y32
                    let d = dx * dx + dy * dy

                    if d < bestDistSq then
                        bestDistSq <- d
                        bestIdx <- ValueSome i
                | _ -> ()

        bestIdx

    /// Check if a pass lane between two positions is clear of defenders.
    /// Returns true if no defender is blocking the lane segment.
    let passLaneClear (bX: float32) (bY: float32) (tX: float32) (tY: float32) (defFrame: TeamFrame) : bool =
        let tdx = tX - bX
        let tdy = tY - bY
        let lenSq = tdx * tdx + tdy * tdy
        if lenSq < 0.01f then true
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
                        if crossSq < radiusSq * lenSq then blocked <- true
                | _ -> ()
                i <- i + 1
            not blocked

    let findBestPassTargetFrame
        (meIdx: int)
        (attFrame: TeamFrame)
        (attRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (dir: AttackDir)
        : (int * Spatial) voption =
        let bX = attFrame.Physics.PosX[meIdx]
        let bY = attFrame.Physics.PosY[meIdx]

        let isForward (px: float32) =
            match dir with
            | LeftToRight -> float px > float bX + 5.0
            | RightToLeft -> float px < float bX - 5.0

        let isBackward (px: float32) =
            match dir with
            | LeftToRight -> float px < float bX - 10.0
            | RightToLeft -> float px > float bX + 10.0

        let localPassLaneClear (targetIdx: int) =
            let tx = attFrame.Physics.PosX[targetIdx]
            let ty = attFrame.Physics.PosY[targetIdx]
            passLaneClear bX bY tx ty defFrame

        let visionWeight = float attRoster.Players[meIdx].Mental.Vision / 100.0
        let mutable bestIdx = ValueNone
        let mutable bestScore = System.Double.MinValue

        for i = 0 to attFrame.SlotCount - 1 do
            if i <> meIdx then
                match attFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let px = attFrame.Physics.PosX[i]
                    let py = attFrame.Physics.PosY[i]
                    let dx = px - bX
                    let dy = py - bY
                    let dist = sqrt (dx * dx + dy * dy)
                    let forwardBonus = if isForward px then 0.35 else 0.0
                    let backwardPenalty = if isBackward px then -0.25 else 0.0
                    let laneBonus = if localPassLaneClear i then 0.2 else 0.0

                    let score =
                        (1.0 / (1.0 + float dist * 0.1))
                        + forwardBonus
                        + backwardPenalty
                        + laneBonus
                        + visionWeight * 0.1

                    if score > bestScore then
                        bestScore <- score
                        bestIdx <- ValueSome i
                | _ -> ()

        match bestIdx with
        | ValueSome idx ->
            let sp =
                { X = float attFrame.Physics.PosX[idx] * 1.0<meter>
                  Y = float attFrame.Physics.PosY[idx] * 1.0<meter>
                  Z = 0.0<meter>
                  Vx = 0.0<meter / second>
                  Vy = 0.0<meter / second>
                  Vz = 0.0<meter / second> }

            ValueSome(idx, sp)
        | ValueNone -> ValueNone

    let secondLastDefenderXFrame (frame: TeamFrame) (dir: AttackDir) : float<meter> =
        let mutable firstX = if dir = LeftToRight then -1.0f else 106.0f
        let mutable secondX = if dir = LeftToRight then -1.0f else 106.0f
        let mutable found = 0

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let x = frame.Physics.PosX[i]
                found <- found + 1

                if dir = LeftToRight then
                    if x > firstX then
                        secondX <- firstX
                        firstX <- x
                    elif x > secondX then
                        secondX <- x
                else if x < firstX then
                    secondX <- firstX
                    firstX <- x
                elif x < secondX then
                    secondX <- x
            | _ -> ()

        if found < 2 then 0.0<meter> else float secondX * 1.0<meter>

    let isOffsideFrame
        (idx: int)
        (attFrame: TeamFrame)
        (attRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (state: SimState)
        (clubSide: ClubSide)
        : bool =
        let player = attRoster.Players[idx]

        if player.Position = GK then
            false
        else
            let dir = attackDirFor clubSide state
            let playerX = float attFrame.Physics.PosX[idx] * 1.0<meter>
            let secondX = secondLastDefenderXFrame defFrame dir
            let ballX = state.Ball.Position.X

            let offsideLine =
                if defFrame.Physics.ActiveCount < 2 then
                    match dir with
                    | LeftToRight -> 106.0<meter>
                    | RightToLeft -> -1.0<meter>
                else
                    match dir with
                    | LeftToRight -> max secondX ballX
                    | RightToLeft -> min secondX ballX

            let inOwnHalf =
                match dir with
                | LeftToRight -> playerX <= 50.0<meter>
                | RightToLeft -> playerX >= 50.0<meter>

            not inOwnHalf
            && (match dir with
                | LeftToRight -> playerX > offsideLine + 0.1<meter>
                | RightToLeft -> playerX < offsideLine - 0.1<meter>)

    let snapshotAtPassFrame
        (passerIdx: int)
        (receiverIdx: int)
        (attFrame: TeamFrame)
        (attRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (state: SimState)
        (dir: AttackDir)
        : OffsideSnapshot =
        { PasserId = attRoster.Players[passerIdx].Id
          ReceiverId = attRoster.Players[receiverIdx].Id
          ReceiverXAtPass = float attFrame.Physics.PosX[receiverIdx] * 1.0<meter>
          SecondLastDefenderX = secondLastDefenderXFrame defFrame dir
          BallXAtPass = state.Ball.Position.X
          Dir = dir }

    let isOffsideFromSnapshot (snapshot: OffsideSnapshot) : bool =
        match snapshot.Dir with
        | LeftToRight ->
            snapshot.ReceiverXAtPass > snapshot.SecondLastDefenderX
            && snapshot.ReceiverXAtPass > snapshot.BallXAtPass
        | RightToLeft ->
            snapshot.ReceiverXAtPass < snapshot.SecondLastDefenderX
            && snapshot.ReceiverXAtPass < snapshot.BallXAtPass

    let mirrorSpatial (s: Spatial) =
        { s with
            X = PitchLength - s.X
            Vx = -s.Vx }

    let pointToLineDistSq
        (px: float32)
        (py: float32)
        (lx1: float32)
        (ly1: float32)
        (lx2: float32)
        (ly2: float32)
        : float32 =
        let dx = lx2 - lx1
        let dy = ly2 - ly1
        let lenSq = dx * dx + dy * dy

        if lenSq < 0.0001f then
            (px - lx1) * (px - lx1) + (py - ly1) * (py - ly1)
        else
            let t = max 0.0f (min 1.0f (((px - lx1) * dx + (py - ly1) * dy) / lenSq))
            let cx = lx1 + t * dx
            let cy = ly1 + t * dy
            (px - cx) * (px - cx) + (py - cy) * (py - cy)
