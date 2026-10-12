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

    let restDefenders (world: World) side =
        let frame = (MatchWorld.team world side).Frame
        let dir = MatchMath.goalDirection side
        let positions = frame.PosX
        let mutable first, second = -1, -1

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

    let chooseFor effort (ctx: MatchContext) (frame: TeamFrame) target excluded previous margin =
        let mutable best = None
        let mutable time = System.Double.MaxValue * 1.0<second>

        for slot in MatchWorld.activeSlots frame do
            if outfielder frame slot && not (excluded slot) then
                let candidate = arrivalTimeFor effort ctx frame slot target

                if candidate < time || (candidate = time && Some slot < best) then
                    best <- Some slot
                    time <- candidate

        match previous with
        | Some slot when
            outfielder frame slot
            && not (excluded slot)
            && arrivalTimeFor effort ctx frame slot target <= time + margin
            ->
            Some slot, arrivalTimeFor effort ctx frame slot target
        | _ -> best, time

    let choose ctx frame target excluded previous margin =
        chooseFor 1.0 ctx frame target excluded previous margin

type TransitionAssessment =
    { Primary: int option
      Cover: int option
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
        let rest1, rest2 = TransitionGeometry.restDefenders world side

        let primary, arrival =
            TransitionGeometry.choose
                ctx
                frame
                (TransitionGeometry.pressPoint ctx world side)
                (fun slot ->
                    slot = rest1
                    || slot = rest2
                    || float team.Frame.Condition[slot] < cfg.MinimumPressCondition
                    || float team.Frame.PosX[slot] * dir > float ball.X * dir + float cfg.BeatenMargin)
                episode.Primary
                cfg.HandoffAdvantage

        let coverTarget = TransitionGeometry.coverPoint ctx world side

        let cover, coverArrival =
            TransitionGeometry.chooseFor
                cfg.CoverEffort
                ctx
                frame
                coverTarget
                (fun slot ->
                    Some slot = primary
                    || slot = rest1
                    || slot = rest2
                    || float team.Frame.PosX[slot] * dir > float ball.X * dir
                    || float team.Frame.Condition[slot] < cfg.MinimumPressCondition)
                episode.Cover
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
            elif primary.IsNone || arrival > window then
                ArrivalTooLate
            elif local < cfg.MinimumLocalPlayers || local < opponents + cfg.LocalSurplus then
                Outnumbered
            elif cover.IsNone || coverArrival > cfg.CoverArrivalWindow then
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
                    episode.Primary
                    |> Option.exists (fun slot ->
                        float team.Frame.PosX[slot] * MatchMath.goalDirection side > float world.State.Ball.Position.X
                                                                                     * MatchMath.goalDirection side
                                                                                     + float cfg.BeatenMargin)

                let broken =
                    progression > float cfg.EscapeProgress || (firstBeaten && not assessment.Viable)

                let elapsed = tick - episode.LostAt >= cfg.MaxPressDuration

                let forced =
                    broken
                    || assessment.Primary.IsNone
                    || assessment.Cover.IsNone
                    || assessment.Reason = DepthThreat
                    || assessment.Reason = MissingCover
                    || episode.Tactics <> team.Tactics
                    || episode.ParticipationRevision <> team.Frame.ActiveCache.Revision

                let mode, reason =
                    if episode.Mode = Retreat then
                        Retreat, episode.Reason
                    elif broken then
                        Retreat, PressureBroken
                    elif elapsed then
                        Retreat, PressWindowElapsed
                    elif episode.Mode = Assessing || forced || tick >= episode.CommitUntil then
                        (if assessment.Viable then Counterpress else Retreat), assessment.Reason
                    else
                        episode.Mode, episode.Reason

                let primary, cover =
                    if mode = Counterpress then
                        assessment.Primary, assessment.Cover
                    else
                        None, None

                let changed =
                    mode <> episode.Mode || primary <> episode.Primary || cover <> episode.Cover

                let player slot =
                    slot |> Option.map (fun slot -> team.RosterIds[slot])

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

                if primary <> episode.Primary || cover <> episode.Cover then
                    emit world buffer side episode (Handoff(player primary, player cover)) cause

                let settled =
                    episode.Mode = Retreat
                    && mode = Retreat
                    && assessment.DepthRunners = 0
                    && (MatchWorld.activeSlots team.Frame
                        |> Array.forall (fun slot ->
                            if not (TransitionGeometry.outfielder team.Frame slot) then
                                true
                            else
                                match team.Defense.Tasks[slot] with
                                | Some task ->
                                    float team.Frame.PosX[slot] * MatchMath.goalDirection side
                                    <= float world.State.Ball.Position.X * MatchMath.goalDirection side
                                    && MatchMath.dist
                                        (MatchWorld.posX team.Frame slot)
                                        (MatchWorld.posY team.Frame slot)
                                        task.Target.X
                                        task.Target.Y
                                       <= float cfg.SettledTolerance
                                | None -> false))

                let stabilizing =
                    if settled then
                        episode.StabilizingSince |> Option.orElse (Some tick)
                    else
                        None

                let finished =
                    stabilizing
                    |> Option.exists (fun since -> tick - since >= cfg.StabilizationHold)

                let outcome =
                    if reason = PressureBroken then PressureBeaten
                    elif reason = PressWindowElapsed then Retreated
                    else Stabilized

                let next =
                    { episode with
                        Mode = mode
                        Reason = reason
                        Primary = primary
                        Cover = cover
                        ModeSince = if mode <> episode.Mode then tick else episode.ModeSince
                        CommitUntil =
                            if mode <> episode.Mode then
                                tick + cfg.Commitment
                            else
                                episode.CommitUntil
                        EvaluatedAt = tick
                        Tactics = team.Tactics
                        ParticipationRevision = team.Frame.ActiveCache.Revision
                        StabilizingSince = stabilizing }

                if finished then
                    team.Defense.Episode <-
                        Some
                            { next with
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
                        next
                        (Finished outcome)
                        (evidence |> ValueOption.map ResolutionCause |> ValueOption.defaultValue cause)
                else
                    team.Defense.Episode <- Some next
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            update ctx.Ctx world buffer ClubSide.Home
            update ctx.Ctx world buffer ClubSide.Away
            rng
