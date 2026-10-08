namespace FootballEngine.Core.Match

/// Duel execution: the carrier's dribble against the nearest defender's
/// tackle. A lost duel gives the ball away, and a foul is the price of a
/// failed dribble — the two are coupled so beating a man is never free.
module Duel =

    /// Severity follows the same rules everywhere: derived from position,
    /// last-man status and a position-seeded roll, so the same foul in the
    /// same place always gets the same punishment — never the referee's mood.
    let private foulSeverity
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (x: float<meter>)
        (y: float<meter>)
        : FoulSeverity =
        let cfg = ctx.Config.Actions
        let dir = MatchMath.goalDirection side
        let distToOwnGoal = abs (float x - float (MatchMath.ownGoalX side))
        let distToAttGoal = abs (float x - float (MatchMath.goalX side))

        let nearOwnBox = distToOwnGoal < 20.0
        let nearAttGoal = distToAttGoal < 30.0

        // A foul with no defender behind is a denied clear goalscoring
        // chance — that is what makes it red-worthy, not the position itself.
        let mutable lastMan = false

        if nearAttGoal then
            let frame = (MatchWorld.team world side).Frame
            let mutable behind = 0

            for s2 in MatchWorld.activeSlots frame do
                if s2 <> slot && s2 <> 0 then
                    let sx = MatchWorld.posX frame s2

                    if dir > 0.0 && float sx < float x then
                        behind <- behind + 1

                    if dir < 0.0 && float sx > float x then
                        behind <- behind + 1

            lastMan <- behind = 0

        let baseRoll, _ =
            Rng.double (Rng.ofSeed (uint64 (abs (int (float x * 100.0 + float y * 100.0)))))
        // ^ deterministic from position, so same duel position → same severity

        let severity =
            if nearAttGoal && lastMan then
                FoulSeverity.RedWorthy
            elif nearAttGoal && baseRoll < 0.33 then
                FoulSeverity.YellowWorthy
            elif nearOwnBox && baseRoll < 0.25 then
                FoulSeverity.YellowWorthy
            elif baseRoll < cfg.DuelFoulSeverityBias then
                FoulSeverity.Tactical
            else
                FoulSeverity.Minor

        severity

    let adjudicate
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (slot: int)
        : Rng =
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let pid = p.Id
        let frame = (MatchWorld.team world side).Frame

        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot

        match MatchWorld.nearestOpponent ctx.Ctx world side x y 3.0 with
        | None ->
            world.Observation.RejectAction(tick, pid, NoDuelOpponent) |> ignore
            rng
        | Some dSlot ->
            let dPid = (MatchWorld.lineup ctx.Ctx (MatchWorld.oppOf side)).Profiles.[dSlot].Id
            let d = MatchWorld.profileOf ctx.Ctx (MatchWorld.oppOf side) dSlot

            let att =
                (float p.Dribbling + float p.Agility + float p.Composure) / 300.0
                - (float d.Tackling + float d.Anticipation + float d.Strength) / 300.0
                + world.State.Momentum * cfg.Actions.DuelMomentumBonus

            let pWin = MatchMath.clamp01 (MatchMath.sigmoid cfg.Actions.DuelSteepness att)

            let r, rng' = Rng.double rng
            let foulRoll, rng'' = Rng.double rng'
            let foul = foulRoll < cfg.Actions.DuelFoulChance
            let firstEvent = buffer.Events.Count

            let resolution =
                if world.Observation.Enabled then
                    world.Observation.Execution(
                        tick,
                        DuelResolved
                            { Carrier = pid
                              Defender = dPid
                              X = x
                              Y = y
                              WinProbability = pWin
                              WinDraw = r
                              FoulProbability = cfg.Actions.DuelFoulChance
                              FoulDraw = foulRoll
                              Foul = foul
                              Won = not foul && r < pWin }
                    )
                else
                    ValueNone

            let linkConsequences () =
                match resolution with
                | ValueSome id ->
                    for index = firstEvent to buffer.Events.Count - 1 do
                        world.Observation.Associate(buffer.Events[index], ResolutionCause id)
                | ValueNone -> ()

            if foul then
                let severity = foulSeverity ctx.Ctx world side slot x y

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Foul(pid, dPid, severity, CardDecision.NoCard, (x, y)) }

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.BallLoose(x, y) }

                linkConsequences ()
                rng''
            elif r < pWin then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Dribble(pid, true, x, y) }

                linkConsequences ()
                rng''
            else
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Dribble(pid, false, x, y) }

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Tackle(dPid, pid) }

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(MatchWorld.oppOf side, dPid) }

                linkConsequences ()
                rng''
