namespace FootballEngine.Core.Match

module TransitionGeometry =
    let outfielder (frame: TeamFrame) slot =
        slot >= 0
        && slot < frame.SlotCount
        && MatchWorld.isActive frame slot
        && frame.SlotRoles[slot] <> SlotRole.Goalkeeper

    let arrivalTimeFor effort (ctx: MatchContext) (frame: TeamFrame) slot (target: Vec2) =
        let cfg = ctx.Config.Movement

        let dx, dy =
            float target.X - float frame.PosX[slot], float target.Y - float frame.PosY[slot]

        let distance = max 0.0 (sqrt (dx * dx + dy * dy) - float ctx.Config.Ball.FootReach)

        let speed =
            (float cfg.JogSpeed + (float cfg.SprintSpeed - float cfg.JogSpeed) * effort)
            * (1.0 - (1.0 - float frame.Condition[slot]) * cfg.FatigueSpeedDecay)
        // Mirror Steering's acceleration cap so the estimate cannot assume an instant sprint.
        let acceleration = float cfg.Acceleration * max 0.1 (min 1.0 cfg.Smoothing)

        let initial =
            if distance = 0.0 then
                0.0
            else
                MatchMath.clamp
                    0.0
                    speed
                    ((float frame.VelX[slot] * dx + float frame.VelY[slot] * dy)
                     / sqrt (dx * dx + dy * dy))

        let accelerateDistance = (speed * speed - initial * initial) / (2.0 * acceleration)

        let seconds =
            if distance = 0.0 then
                0.0
            elif speed <= 0.0 then
                System.Double.PositiveInfinity
            elif distance <= accelerateDistance then
                (sqrt (initial * initial + 2.0 * acceleration * distance) - initial)
                / acceleration
            else
                (speed - initial) / acceleration + (distance - accelerateDistance) / speed

        seconds * 1.0<second>

    let arrivalTime ctx frame slot target =
        arrivalTimeFor 1.0 ctx frame slot target

    let restDefendersV (world: World) side : struct (int * int) =
        let frame = (MatchWorld.team world side).Frame
        let dir = MatchMath.goalDirection side
        let positions = frame.PosX
        let mutable first = -1
        let mutable second = -1

        let before slot current =
            current < 0
            || float positions[slot] * dir < float positions[current] * dir
            || (positions[slot] = positions[current] && slot < current)

        for slot in MatchWorld.activeSlots frame do
            if outfielder frame slot then
                if before slot first then
                    second <- first
                    first <- slot
                elif before slot second then
                    second <- slot

        struct (first, second)

    let restDefenders (world: World) side =
        let struct (first, second) = restDefendersV world side
        first, second

    let pressPoint (ctx: MatchContext) (world: World) side =
        let cfg = ctx.Config.DefensiveTransition
        let ball = world.State.Ball.Position
        let dx = ball.Vx * cfg.CarrierPrediction
        let dy = ball.Vy * cfg.CarrierPrediction

        let x, y =
            MatchMath.pitchClamp
                (float (ball.X + dx) - MatchMath.goalDirection side * float cfg.ApproachOffset)
                (float (ball.Y + dy))

        Vec2.create (x * 1.0<meter>) (y * 1.0<meter>)

    let coverPoint (ctx: MatchContext) (world: World) side =
        let ball = world.State.Ball.Position

        Vec2.create
            (ball.X
             - MatchMath.goalDirection side * ctx.Config.DefensiveTransition.CoverDepth)
            ball.Y

    /// `chooseFor` without options or tuples: -1 stands for no slot. Inline so the exclusion test is not a closure
    /// allocated on every evaluation.
    let inline chooseSlotFor
        effort
        (ctx: MatchContext)
        (frame: TeamFrame)
        target
        ([<InlineIfLambda>] excluded: int -> bool)
        (previous: int)
        margin
        : struct (int * float<second>) =
        let mutable best = -1
        let mutable time = System.Double.MaxValue * 1.0<second>

        for slot in MatchWorld.activeSlots frame do
            if outfielder frame slot && not (excluded slot) then
                let candidate = arrivalTimeFor effort ctx frame slot target

                if candidate < time || (candidate = time && best >= 0 && slot < best) then
                    best <- slot
                    time <- candidate

        if previous >= 0 && outfielder frame previous && not (excluded previous) then
            let held = arrivalTimeFor effort ctx frame previous target

            if held <= time + margin then
                struct (previous, held)
            else
                struct (best, time)
        else
            struct (best, time)

    let chooseFor effort (ctx: MatchContext) (frame: TeamFrame) target excluded previous margin =
        let struct (slot, time) =
            chooseSlotFor effort ctx frame target excluded (Option.defaultValue -1 previous) margin

        (if slot >= 0 then Some slot else None), time

    let choose ctx frame target excluded previous margin =
        chooseFor 1.0 ctx frame target excluded previous margin

/// A struct so the per-cadence verdict allocates nothing; a slot of -1 is no player.
[<Struct>]
type TransitionAssessment =
    { Primary: int
      Cover: int
      Arrival: float<second>
      CoverArrival: float<second>
      ArrivalWindow: float<second>
      LocalPlayers: int
      Opponents: int
      DepthRunners: int
      Reason: TransitionReason
      Viable: bool }

/// One immediate tactical owner; the minute-scale directive remains a strategic preference, not a loss detector.
module TeamTransition =
    let assess (ctx: MatchContext) (world: World) side (episode: DefensiveEpisode) =
        let cfg = ctx.Config.DefensiveTransition
        let team = MatchWorld.team world side
        let frame = team.Frame
        let opponent = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
        let dir = MatchMath.goalDirection side
        let ball = world.State.Ball.Position
        let struct (rest1, rest2) = TransitionGeometry.restDefendersV world side

        let struct (primary, arrival) =
            TransitionGeometry.chooseSlotFor
                1.0
                ctx
                frame
                (TransitionGeometry.pressPoint ctx world side)
                (fun slot ->
                    slot = rest1
                    || slot = rest2
                    || float team.Frame.Condition[slot] < cfg.MinimumPressCondition
                    || float team.Frame.PosX[slot] * dir > float ball.X * dir + float cfg.BeatenMargin)
                (Option.defaultValue -1 episode.Primary)
                cfg.HandoffAdvantage

        let coverTarget = TransitionGeometry.coverPoint ctx world side

        let struct (cover, coverArrival) =
            TransitionGeometry.chooseSlotFor
                cfg.CoverEffort
                ctx
                frame
                coverTarget
                (fun slot ->
                    slot = primary
                    || slot = rest1
                    || slot = rest2
                    || float team.Frame.PosX[slot] * dir > float ball.X * dir
                    || float team.Frame.Condition[slot] < cfg.MinimumPressCondition)
                (Option.defaultValue -1 episode.Cover)
                cfg.HandoffAdvantage

        let mutable local, opponents, depth = 0, 0, 0
        let target = Vec2.create ball.X ball.Y

        for slot in MatchWorld.activeSlots frame do
            if
                TransitionGeometry.outfielder frame slot
                && float frame.Condition[slot] >= cfg.MinimumPressCondition
                && TransitionGeometry.arrivalTime ctx frame slot target <= cfg.LocalArrivalWindow
            then
                local <- local + 1

        let depthLine =
            if rest2 < 0 then
                float (MatchMath.ownGoalX side) * dir
            else
                float frame.PosX[rest2] * dir

        for slot in MatchWorld.activeSlots opponent do
            if TransitionGeometry.outfielder opponent slot then
                if
                    TransitionGeometry.arrivalTime ctx opponent slot target
                    <= cfg.LocalArrivalWindow
                then
                    opponents <- opponents + 1

                if
                    float opponent.PosX[slot] * dir < depthLine - float cfg.DepthSafety
                    && abs (float opponent.PosY[slot]) <= float cfg.CentralHalfWidth
                then
                    depth <- depth + 1

        let window =
            cfg.BaseArrivalWindow
            + float team.Tactics.PressIntensity * cfg.PressArrivalExtension

        let nearGoal =
            MatchMath.dist ball.X ball.Y (MatchMath.ownGoalX side) 0.0<meter> < float cfg.ThreatGoalDistance

        let reason =
            if float team.Tactics.PressIntensity < cfg.MinimumPressIntensity then
                TacticalRestraint
            elif depth > 0 || rest2 < 0 || (nearGoal && coverArrival > arrival) then
                DepthThreat
            elif primary < 0 || arrival > window then
                ArrivalTooLate
            elif local < cfg.MinimumLocalPlayers || local < opponents + cfg.LocalSurplus then
                Outnumbered
            elif cover < 0 || coverArrival > cfg.CoverArrivalWindow then
                MissingCover
            else
                LocalAdvantage

        { Primary = primary
          Cover = cover
          Arrival = arrival
          CoverArrival = coverArrival
          ArrivalWindow = window
          LocalPlayers = local
          Opponents = opponents
          DepthRunners = depth
          Reason = reason
          Viable = reason = LocalAdvantage }

    let private emit (world: World) (buffer: EventBuffer) side (episode: DefensiveEpisode) change cause =
        let ev =
            { Tick = world.State.Tick
              Data = EventData.DefensiveTransition(side, episode.Id, change) }

        world.Observation.Associate(ev, cause)
        buffer.Events.Add ev

    /// Every outfielder is behind the ball and standing on his retreat task. A loop keeps the tick's allocation
    /// flat; the short-circuit order is the one `Array.forall` had.
    let private allSettled (team: TeamWorld) side (ball: Spatial) tolerance =
        let frame = team.Frame
        let dir = MatchMath.goalDirection side
        let slots = MatchWorld.activeSlots frame
        let mutable settled = true
        let mutable index = 0

        while settled && index < slots.Length do
            let slot = slots[index]

            if TransitionGeometry.outfielder frame slot then
                match team.Defense.Tasks[slot] with
                | ValueSome task ->
                    settled <-
                        float frame.PosX[slot] * dir <= float ball.X * dir
                        && MatchMath.dist
                            (MatchWorld.posX frame slot)
                            (MatchWorld.posY frame slot)
                            task.Target.X
                            task.Target.Y
                           <= float tolerance
                | ValueNone -> settled <- false

            index <- index + 1

        settled

    let private update (ctx: MatchContext) (world: World) buffer side =
        let team = MatchWorld.team world side
        let tick = world.State.Tick

        match team.Defense.Episode with
        | Some episode when episode.EndedAt.IsNone ->
            if world.State.Flow <> MatchFlow.Live then
                team.Defense.Episode <-
                    Some
                        { episode with
                            EndedAt = Some tick
                            Outcome = Some Interrupted }

                DefensiveTransitionState.clearTasks team.Frame team.Defense

                let evidence =
                    world.Observation.Record(
                        tick,
                        ValueSome(EventCause episode.Id),
                        TransitionEnded(side, episode.Id, Interrupted, episode.Reason)
                    )

                emit
                    world
                    buffer
                    side
                    episode
                    (Finished Interrupted)
                    (evidence
                     |> ValueOption.map ResolutionCause
                     |> ValueOption.defaultValue (EventCause episode.Id))
            else
                let cfg = ctx.Config.DefensiveTransition
                let assessment = assess ctx world side episode

                let progression =
                    (float episode.Origin.X - float world.State.Ball.Position.X)
                    * MatchMath.goalDirection side

                let firstBeaten =
                    match episode.Primary with
                    | Some slot ->
                        float team.Frame.PosX[slot] * MatchMath.goalDirection side > float world.State.Ball.Position.X
                                                                                     * MatchMath.goalDirection side
                                                                                     + float cfg.BeatenMargin
                    | None -> false

                let broken =
                    progression > float cfg.EscapeProgress || (firstBeaten && not assessment.Viable)

                let elapsed = tick - episode.LostAt >= cfg.MaxPressDuration

                let forced =
                    broken
                    || assessment.Primary < 0
                    || assessment.Cover < 0
                    || assessment.Reason = DepthThreat
                    || assessment.Reason = MissingCover
                    || episode.Tactics <> team.Tactics
                    || episode.ParticipationRevision <> team.Frame.ActiveCache.Revision

                let struct (mode, reason) =
                    if episode.Mode = Retreat then
                        struct (Retreat, episode.Reason)
                    elif broken then
                        struct (Retreat, PressureBroken)
                    elif elapsed then
                        struct (Retreat, PressWindowElapsed)
                    elif episode.Mode = Assessing || forced || tick >= episode.CommitUntil then
                        struct ((if assessment.Viable then Counterpress else Retreat), assessment.Reason)
                    else
                        struct (episode.Mode, episode.Reason)

                let primary = if mode = Counterpress then assessment.Primary else -1
                let cover = if mode = Counterpress then assessment.Cover else -1

                let slotOfOption (slot: int option) =
                    match slot with
                    | Some slot -> slot
                    | None -> -1

                let primaryChanged = primary <> slotOfOption episode.Primary
                let coverChanged = cover <> slotOfOption episode.Cover

                let changed = mode <> episode.Mode || primaryChanged || coverChanged

                let player slot =
                    if slot >= 0 then Some team.RosterIds[slot] else None

                let cause =
                    if changed && world.Observation.Enabled then
                        world.Observation.Record(
                            tick,
                            ValueSome(EventCause episode.Id),
                            TransitionEvaluated
                                { Side = side
                                  Episode = episode.Id
                                  Mode = mode
                                  Reason = reason
                                  Primary = player primary
                                  Cover = player cover
                                  Arrival = assessment.Arrival
                                  ArrivalWindow = assessment.ArrivalWindow
                                  CoverArrival = assessment.CoverArrival
                                  LocalPlayers = assessment.LocalPlayers
                                  Opponents = assessment.Opponents
                                  DepthRunners = assessment.DepthRunners }
                        )
                        |> ValueOption.map DecisionCause
                        |> ValueOption.defaultValue (EventCause episode.Id)
                    else
                        EventCause episode.Id

                if mode <> episode.Mode then
                    emit world buffer side episode (ModeSelected(mode, reason)) cause

                if primaryChanged || coverChanged then
                    emit world buffer side episode (Handoff(player primary, player cover)) cause

                let settled =
                    episode.Mode = Retreat
                    && mode = Retreat
                    && assessment.DepthRunners = 0
                    && allSettled team side world.State.Ball.Position cfg.SettledTolerance

                let stabilizing =
                    if settled then
                        match episode.StabilizingSince with
                        | Some _ as since -> since
                        | None -> Some tick
                    else
                        None

                let finished =
                    stabilizing
                    |> Option.exists (fun since -> tick - since >= cfg.StabilizationHold)

                let outcome =
                    if reason = PressureBroken then PressureBeaten
                    elif reason = PressWindowElapsed then Retreated
                    else Stabilized

                // The episode is owned by this system, so each reassessment updates it in place; copying the record
                // every cadence was the largest steady allocation of the team layer.
                if mode <> episode.Mode then
                    episode.ModeSince <- tick
                    episode.CommitUntil <- tick + cfg.Commitment

                episode.Mode <- mode
                episode.Reason <- reason

                if primaryChanged then
                    episode.Primary <- (if primary >= 0 then Some primary else None)

                if coverChanged then
                    episode.Cover <- (if cover >= 0 then Some cover else None)

                episode.EvaluatedAt <- tick
                episode.Tactics <- team.Tactics
                episode.ParticipationRevision <- team.Frame.ActiveCache.Revision
                episode.StabilizingSince <- stabilizing

                if finished then
                    team.Defense.Episode <-
                        Some
                            { episode with
                                EndedAt = Some tick
                                Outcome = Some outcome }

                    DefensiveTransitionState.clearTasks team.Frame team.Defense

                    let evidence =
                        world.Observation.Record(
                            tick,
                            ValueSome cause,
                            TransitionEnded(side, episode.Id, outcome, reason)
                        )

                    emit
                        world
                        buffer
                        side
                        episode
                        (Finished outcome)
                        (evidence |> ValueOption.map ResolutionCause |> ValueOption.defaultValue cause)
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            update ctx.Ctx world buffer ClubSide.Home
            update ctx.Ctx world buffer ClubSide.Away
            rng
