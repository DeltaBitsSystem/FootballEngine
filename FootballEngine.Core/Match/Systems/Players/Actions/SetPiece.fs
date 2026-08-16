namespace FootballEngine.Core.Match

/// Set-piece routines: a restart must never be improvised. When one is
/// awarded, the nearest active player to the spot is appointed executor and a
/// forced-action marker (key slot+2000, Phase 201) is left for the Actions
/// controller to execute once the restart delay elapses — the routine happens
/// exactly as the rules prescribe, outside the free decision system.
module SetPieceRoutines =

    let private planSpot
        (kind: SetPieceKind)
        (side: ClubSide)
        (ballX: float<meter>)
        (ballY: float<meter>)
        : float<meter> * float<meter> =
        match kind with
        | SetPieceKind.KickOff -> 0.0<meter>, 0.0<meter>
        | SetPieceKind.Corner ->
            let y =
                if ballY >= 0.0<meter> then
                    Pitch.HalfWidth
                else
                    -Pitch.HalfWidth

            MatchMath.goalX side, y
        | SetPieceKind.GoalKick -> MatchMath.ownGoalX side + 6.5<meter> * MatchMath.goalDirection side, 0.0<meter>
        | SetPieceKind.FreeKick
        | SetPieceKind.ThrowIn -> ballX, ballY
        | SetPieceKind.Penalty -> MatchMath.goalX side - 11.0<meter> * MatchMath.goalDirection side, 0.0<meter>

    let private nearestTo
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (x: float<meter>)
        (y: float<meter>)
        : int =
        let frame = (MatchWorld.team world side).Frame
        let mutable best = -1
        let mutable bestD = 1000000.0

        for slot in MatchWorld.activeSlots frame do
            let d = MatchMath.dist (MatchWorld.posX frame slot) (MatchWorld.posY frame slot) x y

            if d < bestD then
                bestD <- d
                best <- slot

        best

    /// The box zones a delivery must threaten to be a real set piece: near
    /// post, far post and the edge of the box give the taker distinct targets
    /// instead of a random crowd.
    let private zoneTargets
        (ctx: MatchContext)
        (world: World)
        (kind: SetPieceKind)
        (side: ClubSide)
        : (float * float) list =
        let ball = world.State.Ball.Position
        let dir = MatchMath.goalDirection side
        let gx = float (MatchMath.goalX side)
        let signY = if float ball.Y >= 0.0 then 1.0 else -1.0
        let cfg = ctx.Config.SetPiece

        match kind with
        | SetPieceKind.Corner
        | SetPieceKind.FreeKick ->
            [ gx - float cfg.CornerNearZoneDist * dir, signY * 5.0
              gx - float cfg.CornerFarZoneDist * dir, -signY * 5.0
              gx - float Pitch.PenaltyAreaDepth * dir, signY * 11.0 ]
        | _ -> []

    /// During the restart delay both teams arrange into the delivery zones so
    /// a set piece is never delivered into empty space. The probable taker is
    /// left in place — moving him would break the routine — and everyone else
    /// is placed directly (Frame) and given the zone as shape target so they
    /// keep it once the ball is live.
    module SetPiecePlacement =

        let place (ctx: MatchContext) (world: World) (kind: SetPieceKind) (side: ClubSide) : unit =
            let zones = zoneTargets ctx world kind side

            if not zones.IsEmpty then
                let ball = world.State.Ball.Position

                // Probable executor — skip him so he stays near the restart spot.
                let taker =
                    let frame = (MatchWorld.team world side).Frame

                    MatchWorld.activeSlots frame
                    |> Array.filter (fun s -> s <> 0)
                    |> Array.sortBy (fun s ->
                        MatchMath.dist ball.X ball.Y (MatchWorld.posX frame s) (MatchWorld.posY frame s))
                    |> Array.tryHead

                let loX = -(float Pitch.HalfLength + 5.0)
                let hiX = float Pitch.HalfLength + 5.0
                let loY = -(float Pitch.HalfWidth + 5.0)
                let hiY = float Pitch.HalfWidth + 5.0
                let gxMouth = MatchMath.goalX side

                for teamSide in [ side; MatchWorld.oppOf side ] do
                    let team = MatchWorld.team world teamSide
                    let frame = team.Frame

                    let slots =
                        MatchWorld.activeSlots frame
                        |> Array.filter (fun s -> s <> 0 && not (teamSide = side && Some s = taker))
                        |> Array.sortBy (fun s ->
                            MatchMath.dist gxMouth 0.0<meter> (MatchWorld.posX frame s) (MatchWorld.posY frame s))

                    if slots.Length > 0 then
                        let mutable queue = zones

                        for slot in slots do
                            if queue.IsEmpty then
                                queue <- zones

                            let zx, zy = queue.Head
                            queue <- queue.Tail

                            let px = MatchMath.clamp loX hiX zx
                            let py = MatchMath.clamp loY hiY zy

                            frame.PosX.[slot] <- float32 px
                            frame.PosY.[slot] <- float32 py
                            frame.VelX.[slot] <- 0.0f
                            frame.VelY.[slot] <- 0.0f
                            team.Shape.TargetsX.[slot] <- float32 px
                            team.Shape.TargetsY.[slot] <- float32 py

    /// The wall exists to shrink the shooting angle: the WallPlayers nearest
    /// outfield defenders stand on a chord WallDistance from the ball,
    /// perpendicular to ball→goal, for direct free kicks inside
    /// DirectFreeKickRange. Positions are written straight into the frame —
    /// during the restart delay there is no time to steer, only to stand.
    module WallPlacer =

        let place (ctx: MatchContext) (world: World) (side: ClubSide) : unit =
            let cfg = ctx.Config.SetPiece
            let ball = world.State.Ball.Position

            if MatchMath.distanceToGoal side ball.X ball.Y < float cfg.DirectFreeKickRange then
                let def = MatchWorld.oppOf side
                let frame = (MatchWorld.team world def).Frame
                let gx = float (MatchMath.goalX side)
                let bx = float ball.X
                let by = float ball.Y
                let dx = gx - bx
                let dy = -by
                let len = sqrt (dx * dx + dy * dy)

                if len > 0.001 then
                    let wallDist = float cfg.WallDistance
                    let anchorX = bx + dx / len * wallDist
                    let anchorY = by + dy / len * wallDist
                    let perpX = -dy / len
                    let perpY = dx / len

                    let count = max 0 (min cfg.WallPlayers frame.SlotCount)

                    let chosen =
                        MatchWorld.activeSlots frame
                        |> Array.filter (fun s -> s <> 0)
                        |> Array.sortBy (fun s ->
                            MatchMath.dist ball.X ball.Y (MatchWorld.posX frame s) (MatchWorld.posY frame s))
                        |> Array.truncate count

                    let n = chosen.Length
                    let span = float cfg.WallArcSpread
                    let loX = -(float Pitch.HalfLength + 5.0)
                    let hiX = float Pitch.HalfLength + 5.0
                    let loY = -(float Pitch.HalfWidth + 5.0)
                    let hiY = float Pitch.HalfWidth + 5.0

                    for i in 0 .. n - 1 do
                        let off = (float i - float (n - 1) / 2.0) * span
                        let wx = MatchMath.clamp loX hiX (anchorX + off * perpX)
                        let wy = MatchMath.clamp loY hiY (anchorY + off * perpY)

                        frame.PosX.[chosen.[i]] <- float32 wx
                        frame.PosY.[chosen.[i]] <- float32 wy
                        frame.VelX.[chosen.[i]] <- 0.0f
                        frame.VelY.[chosen.[i]] <- 0.0f

    let private forcedActionCode
        (kind: SetPieceKind)
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (rng: Rng)
        : (int * int) * Rng =
        match kind with
        | SetPieceKind.Corner -> (5, -1), rng
        | SetPieceKind.Penalty -> (2, -1), rng
        | SetPieceKind.FreeKick ->
            let ball = world.State.Ball.Position
            let d = MatchMath.distanceToGoal side ball.X ball.Y

            if d < float ctx.Config.SetPiece.DirectFreeKickRange then
                let roll, rng' = Rng.double rng

                if roll < ctx.Config.SetPiece.FreeKickShotChance then
                    (2, -1), rng'
                else
                    (1, -1), rng'
            else
                (1, -1), rng
        | SetPieceKind.GoalKick
        | SetPieceKind.ThrowIn
        | SetPieceKind.KickOff -> (1, -1), rng

    let passTarget (ctx: MatchContext) (world: World) (side: ClubSide) (kind: SetPieceKind) : int =
        let frame = (MatchWorld.team world side).Frame
        let dir = MatchMath.goalDirection side
        let ball = world.State.Ball.Position

        match kind with
        | SetPieceKind.KickOff ->
            let mutable best = -1
            let mutable bestScore = 1000000.0

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let score =
                        abs (float (MatchWorld.posX frame slot))
                        + abs (float (MatchWorld.posY frame slot)) * 0.5

                    if score < bestScore then
                        bestScore <- score
                        best <- slot

            best
        | SetPieceKind.GoalKick ->
            let mutable best = -1
            let mutable bestD = 1000000.0

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let d =
                        MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                    let nearOpp =
                        MatchWorld.opponentsNear
                            ctx
                            world
                            side
                            (MatchWorld.posX frame slot)
                            (MatchWorld.posY frame slot)
                            4.0

                    let score = d + float nearOpp * 6.0

                    if score < bestD then
                        bestD <- score
                        best <- slot

            best
        | SetPieceKind.ThrowIn ->
            // A throw-in that lands along the touchline is illegal, so only
            // teammates clearly inside the pitch and reasonably advanced are
            // valid targets.
            let mutable best = -1
            let mutable bestScore = -1000000.0

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let tx = MatchWorld.posX frame slot
                    let ty = MatchWorld.posY frame slot
                    let inside = abs (float ty) < float Pitch.HalfWidth - 6.0
                    let forward = (float tx - float ball.X) * dir

                    if inside && forward > -5.0 then
                        let nearOpp = MatchWorld.opponentsNear ctx world side tx ty 4.0
                        let score = forward * 0.5 - float nearOpp * 4.0 - abs (float ty) * 0.2

                        if score > bestScore then
                            bestScore <- score
                            best <- slot

            if best < 0 then
                // A legal throw must always be possible, so fall back to the most
                // inside teammate near the thrower.
                let mutable fallback = -1
                let mutable fallbackScore = -1000000.0

                for slot in MatchWorld.activeSlots frame do
                    if slot <> 0 then
                        let ty = MatchWorld.posY frame slot
                        let d = MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) ty
                        let score = -d - abs (float ty) * 0.5

                        if score > fallbackScore then
                            fallbackScore <- score
                            fallback <- slot

                fallback
            else
                best
        | _ ->
            let mutable best = -1
            let mutable bestScore = -1000000.0

            for slot in MatchWorld.activeSlots frame do
                if slot <> 0 then
                    let tx = MatchWorld.posX frame slot
                    let forward = (float tx - float ball.X) * dir

                    if forward > -2.0 then
                        let score =
                            forward * 0.5
                            - float (
                                MatchWorld.opponentsNear
                                    ctx
                                    world
                                    side
                                    (MatchWorld.posX frame slot)
                                    (MatchWorld.posY frame slot)
                                    4.0
                              )
                              * 4.0

                        if score > bestScore then
                            bestScore <- score
                            best <- slot

            if best < 0 then
                // Fallback: nearest teammate, so a pass is always available.
                let mutable fallback = -1
                let mutable fallbackD = 1000000.0

                for slot in MatchWorld.activeSlots frame do
                    if slot <> 0 then
                        let d =
                            MatchMath.dist ball.X ball.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

                        if d < fallbackD then
                            fallbackD <- d
                            fallback <- slot

                fallback
            else
                best

    let private mark
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (kind: SetPieceKind)
        (side: ClubSide)
        (tick: int<subtick>)
        : Rng =
        let ball = world.State.Ball.Position
        let x, y = planSpot kind side ball.X ball.Y
        let slot = nearestTo ctx.Ctx world side x y

        // Both teams must be arranged into delivery zones before the ball goes
        // live, or the delivery lands in empty space (no-op for non-scoring kinds).
        SetPiecePlacement.place ctx.Ctx world kind side

        // The wall shortens the shooting angle — the only tool a team has
        // against a direct free kick in range.
        if kind = SetPieceKind.FreeKick then
            WallPlacer.place ctx.Ctx world side

        if slot >= 0 then
            // Put the executor on the ball so the restart handover reaches him.
            // For corners the ball sits on the goal line; place 2 m inside so
            // the carry snap (BallPossession → px + 0.4·dir) stays in bounds
            // even after the player drifts slightly during the restart delay.
            let placeX, placeY =
                if kind = SetPieceKind.Corner then
                    let dir = MatchMath.goalDirection side
                    float x - 2.0 * dir, float y
                else
                    float x, float y

            let team = MatchWorld.team world side
            team.Frame.PosX.[slot] <- float32 placeX
            team.Frame.PosY.[slot] <- float32 placeY
            team.Frame.VelX.[slot] <- 0.0f
            team.Frame.VelY.[slot] <- 0.0f
            team.Shape.TargetsX.[slot] <- float32 placeX
            team.Shape.TargetsY.[slot] <- float32 placeY

            let (code, _), next = forcedActionCode kind ctx.Ctx world side rng
            let target = passTarget ctx.Ctx world side kind
            let delay = Flow.delayFor ctx.Ctx.Config ctx.Clock kind

            team.Frame.RunLookup.[slot + 2000] <-
                { Slot = slot
                  TargetX = float32 target
                  TargetY = float32 code
                  Phase = 201
                  ExpiresAt = tick + int delay * 1<subtick> }

            next
        else
            rng

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let mutable r = rng

            for ev in buffer.Applied do
                match ev.Data with
                | EventData.SetPiece(kind, side) -> r <- mark ctx r world kind side tick
                | EventData.KickOff side -> r <- mark ctx r world SetPieceKind.KickOff side tick
                | _ -> ()

            r
