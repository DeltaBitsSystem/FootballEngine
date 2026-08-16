namespace FootballEngine.Core.Match

/// What each player is allowed to know before deciding: who they can see, how
/// pressed they are, which lanes are open and which receivers can see the
/// carrier. Everything downstream is gated by this — a player cannot react to
/// what perception says he does not see. Also refreshes cognitive vitality and
/// decays R6 memory in batch so streaks fade on a schedule.
module PlayerPerception =

    let private pressRadius = 3.5

    /// R6: exponentially decay each player's memory mods every interval.
    let private decayMemoryBatch
        (ctx: MatchContext)
        (world: World)
        (tick: int<subtick>)
        (interval: int<subtick>)
        : unit =
        let decay = float ctx.Config.Actions.MemoryMentalDecay

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world side

            for slot in MatchWorld.activeSlots team.Frame do
                let mem = team.Memory.[slot]

                if tick - mem.LastMemoryDecayTick >= interval then
                    let elapsed = float (tick - mem.LastMemoryDecayTick) / float interval
                    let factor = decay ** elapsed

                    team.Memory.[slot] <-
                        { mem with
                            PassFailMod = mem.PassFailMod * factor
                            PassSuccessMod = mem.PassSuccessMod * factor
                            DuelFailMod = mem.DuelFailMod * factor
                            DuelSuccessMod = mem.DuelSuccessMod * factor
                            ShotSuccessMod = mem.ShotSuccessMod * factor
                            StreakMod = mem.StreakMod * factor
                            LastMemoryDecayTick = tick }

    /// Geometry behind lane-clear: distance from a point to the segment a→b
    /// and whether the projection lies inside it. An opponent only blocks a
    /// pass when he is near the segment itself — someone beyond its ends is
    /// irrelevant.
    let private offsetFromSegment
        (ax: float)
        (ay: float)
        (bx: float)
        (by: float)
        (px: float)
        (py: float)
        : float * bool =
        let dx = bx - ax
        let dy = by - ay
        let len2 = dx * dx + dy * dy

        if len2 < 1.0e-9 then
            sqrt ((px - ax) * (px - ax) + (py - ay) * (py - ay)), true
        else
            let t = ((px - ax) * dx + (py - ay) * dy) / len2
            let clamped = MatchMath.clamp 0.0 1.0 t
            let cx = ax + clamped * dx
            let cy = ay + clamped * dy
            sqrt ((px - cx) * (px - cx) + (py - cy) * (py - cy)), t > 0.1 && t < 0.9

    /// R3 — build the physical and cognitive perception frame for one team.
    let private perceiveTeam (ctx: MatchContext) (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let cfg = ctx.Config.Weights.Perception
        let aCfg = ctx.Config.Actions
        let radius = float (cfg.VisionRadiusBase + cfg.AnticipationBonusRadius)

        let carrier =
            match MatchWorld.ballCarrier ctx world with
            | Some(s, _, _) when s = side -> MatchWorld.ballCarrier ctx world |> Option.map (fun (_, slot, _) -> slot)
            | _ -> None

        for slot in MatchWorld.activeSlots frame do
            let p = MatchWorld.profileOf ctx side slot
            let r = radius + float (p.Anticipation - 60.0f) * 0.2

            frame.VisibilityOpp.[slot] <-
                MatchWorld.nearestOpponent ctx world side (MatchWorld.posX frame slot) (MatchWorld.posY frame slot) r
                |> Option.isSome

            frame.VisibilityTm.[slot] <- true

            frame.OppsNear.[slot] <-
                MatchWorld.opponentsNear ctx world side (MatchWorld.posX frame slot) (MatchWorld.posY frame slot) 7.0

            frame.PressCount.[slot] <-
                MatchWorld.opponentsNear
                    ctx
                    world
                    side
                    (MatchWorld.posX frame slot)
                    (MatchWorld.posY frame slot)
                    pressRadius

            let cond = float frame.Condition.[slot]

            frame.Cognitive.[slot] <-
                float32 (
                    aCfg.CognitiveFatigueFloor
                    + (1.0 - aCfg.CognitiveFatigueFloor)
                      * MatchMath.sigmoid aCfg.CognitiveFatigueSteepness (cond - 0.5)
                )

        // Lane and cone geometry are relative to the ball carrier.
        match carrier with
        | Some cSlot when MatchWorld.isActive frame cSlot ->
            let cx = float frame.PosX.[cSlot]
            let cy = float frame.PosY.[cSlot]

            for slot in MatchWorld.activeSlots frame do
                if slot <> cSlot && slot <> 0 then
                    let tx = float frame.PosX.[slot]
                    let ty = float frame.PosY.[slot]

                    // A single opponent on the segment does not kill a lane —
                    // the pass can still beat him — but two bodies make a wall.
                    let mutable blockers = 0

                    for opp in MatchWorld.activeSlots (MatchWorld.team world (MatchWorld.oppOf side)).Frame do
                        let oframe = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
                        let ox = float oframe.PosX.[opp]
                        let oy = float oframe.PosY.[opp]
                        let off, inside = offsetFromSegment cx cy tx ty ox oy

                        if inside && off < float aCfg.LaneClearBlockRadius then
                            blockers <- blockers + 1

                    frame.LaneClear.[slot] <- blockers < 2

                    // R3 — a receiver who cannot see the carrier cannot react
                    // to the ball; the cone is that visibility check.
                    if slot = 0 then
                        frame.FaceCone.[slot] <- true
                    else
                        let dirX =
                            let vx = float frame.VelX.[slot]
                            let vy = float frame.VelY.[slot]
                            let sp = sqrt (vx * vx + vy * vy)

                            if sp > 0.5 then
                                vx / sp
                            else
                                float (MatchMath.goalDirection side)

                        let dirY =
                            let vx = float frame.VelX.[slot]
                            let vy = float frame.VelY.[slot]
                            let sp = sqrt (vx * vx + vy * vy)

                            if sp > 0.5 then vy / sp else 0.0

                        let dx = cx - tx
                        let dy = cy - ty
                        let d = sqrt (dx * dx + dy * dy)

                        if d < 0.001 then
                            frame.FaceCone.[slot] <- true
                        else
                            let dot = (dirX * dx + dirY * dy) / d
                            frame.FaceCone.[slot] <- dot > cos (float aCfg.PerceptionConeAngle * 0.5)
                else
                    frame.LaneClear.[slot] <- true
                    frame.FaceCone.[slot] <- true
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick
            perceiveTeam ctx.Ctx world ClubSide.Home tick
            perceiveTeam ctx.Ctx world ClubSide.Away tick
            decayMemoryBatch ctx.Ctx world tick 12000<subtick>
            rng
