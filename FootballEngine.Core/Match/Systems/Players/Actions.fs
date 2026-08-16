namespace FootballEngine.Core.Match

/// The single "do" point for player actions. Other systems only propose —
/// PlayerDecision stores the carrier's choice as a marker (Phase 200) and
/// SetPieceRoutines forces a routine (Phase 201) — and every proposal is
/// executed here, so each action has exactly one execution path and deciding
/// stays decoupled from doing.
module Actions =

    let private executeDecision
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (slot: int)
        (code: float32)
        (targetSlot: int)
        : Rng =
        match int code with
        | 1 -> Pass.launch ctx rng world buffer side slot targetSlot 0.0
        | 2 -> Shot.launch ctx rng world buffer side slot
        | 3 -> Duel.adjudicate ctx rng world buffer side slot
        | 4 ->
            let tick = world.State.Tick
            let p = MatchWorld.profileOf ctx.Ctx side slot
            let pid = p.Id
            let frame = (MatchWorld.team world side).Frame
            let x = MatchWorld.posX frame slot
            let y = MatchWorld.posY frame slot
            let dir = MatchMath.goalDirection side

            let targetX = MatchMath.ownGoalX side + 30.0<meter> * dir

            MatchWorld.updateBall world (fun b ->
                { b with
                    Control = BallControl.Airborne
                    Trajectory = Some(TrajectoryIntent.Cleared)
                    LastTouch = Some pid
                    ArrivalGate = Some(tick + int (MatchMath.dist x y targetX 0.0<meter> / 20.0 * 40.0) * 1<subtick>)
                    StationarySince = None })

            rng
        | 5 -> Cross.launch ctx rng world buffer side slot
        | _ -> rng

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let mutable r = rng

            match MatchWorld.ballCarrier ctx.Ctx world with
            | None -> ()
            | Some(side, slot, _) ->
                let team = MatchWorld.team world side
                let frame = team.Frame

                let setPieceMarker =
                    match frame.RunLookup.TryGetValue(slot + 2000) with
                    | true, a when a.Phase = 201 && a.ExpiresAt <= tick -> Some a
                    | _ -> None

                match setPieceMarker with
                | Some a ->
                    let code = int a.TargetY

                    if code = 1 && a.TargetX >= 0.0f then
                        r <- Pass.launch ctx r world buffer side slot (int a.TargetX) 0.25
                    elif code = 1 then
                        // No safe target: hold the ball.
                        ()

                    frame.RunLookup.Remove(slot + 2000) |> ignore
                | None ->
                    if slot = 0 then
                        r <- GK.distribute ctx r world buffer side
                    else
                        match frame.RunLookup.TryGetValue slot with
                        | true, a when a.Phase = 200 && a.ExpiresAt >= tick ->
                            r <- executeDecision ctx r world buffer side slot a.TargetY (int a.TargetX)
                            frame.RunLookup.Remove slot |> ignore
                        | _ ->
                            match
                                MatchWorld.nearestOpponent
                                    ctx.Ctx
                                    world
                                    side
                                    (MatchWorld.posX frame slot)
                                    (MatchWorld.posY frame slot)
                                    2.5
                            with
                            | Some _ -> r <- PlayerDecision.choose ctx.Ctx world side slot tick r
                            | None -> ()

            r
