namespace FootballEngine.Core.Match

/// The per-tick pipeline. The order is fixed and is itself the determinism
/// contract: drain events -> flow machine -> systems -> clock. Any reordering
/// would change every match produced from the same seed.
module Stepper =

    let private updateOfficials (world: World) =
        let ball = world.State.Ball.Position

        let refereeX, refereeY, signal =
            match world.State.Flow with
            | MatchFlow.Restart restart ->
                match restart.Plan with
                | Some plan ->
                    let lateral = if plan.Spot.Y >= 0.0<meter> then -4.0 else 4.0

                    let x, y =
                        MatchMath.pitchClamp (float plan.Spot.X - 5.0) (float plan.Spot.Y + lateral)

                    x, y, RefereeSignal.RestartDirection(plan.Kind, plan.Side)
                | None -> float ball.X, float ball.Y, RefereeSignal.Whistle
            | _ ->
                let x, y = MatchMath.pitchClamp (float ball.X - 7.0) (float ball.Y + 5.0)
                x, y, RefereeSignal.NoSignal

        let assistantX =
            MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) (float ball.X)

        world.Officials <-
            { Referee =
                { X = refereeX * 1.0<meter>
                  Y = refereeY * 1.0<meter> }
              NearAssistant =
                { X = assistantX * 1.0<meter>
                  Y = -Pitch.HalfWidth }
              FarAssistant =
                { X = assistantX * 1.0<meter>
                  Y = Pitch.HalfWidth }
              Signal = signal }

    let rec private evalTrigger
        (transitionCadence: int<tickDelta>)
        (simTick: SimTick)
        (playClock: PlayClock)
        (flow: MatchFlow)
        (flags: EventFlags)
        (t: CompiledTrigger)
        : bool =
        let tick = SimTick.value simTick

        match t with
        | CompiledTrigger.EveryTick -> true
        | CompiledTrigger.EveryN d ->
            let dT = int d * 1<subtick>
            tick % dT = 0<subtick>
        | CompiledTrigger.EveryMinute(period, offset) ->
            let playTick = PlayClock.value playClock

            playTick % SimulationClock.minuteSubticks = 0<subtick>
            && PlayClock.toMatchMinute playClock % period = offset
        | CompiledTrigger.OnEvent f -> EventFlags.intersects flags f
        | CompiledTrigger.OnEventOrEveryN(f, d) ->
            let dT = int d * 1<subtick>
            EventFlags.intersects flags f || tick % dT = 0<subtick>
        | CompiledTrigger.OnTransitionOrGeometry f ->
            EventFlags.intersects flags f || tick % (int transitionCadence * 1<subtick>) = 0<subtick>
        | CompiledTrigger.WhenLive inner ->
            match flow with
            | MatchFlow.Live -> evalTrigger transitionCadence simTick playClock flow flags inner
            | _ -> false
        | CompiledTrigger.WhenMovementAllowed inner ->
            match flow with
            | MatchFlow.Live
            | MatchFlow.Restart _ -> evalTrigger transitionCadence simTick playClock flow flags inner
            | _ -> false
        | CompiledTrigger.WhenBallDead inner ->
            match flow with
            | MatchFlow.GoalPause _
            | MatchFlow.VARReview _ -> evalTrigger transitionCadence simTick playClock flow flags inner
            | _ -> false

    let private fire
        (transitionCadence: int<tickDelta>)
        (simTick: SimTick)
        (playClock: PlayClock)
        (flow: MatchFlow)
        (flags: EventFlags)
        (triggers: CompiledTrigger[])
        : bool =
        let mutable index = 0
        let mutable matched = false

        while index < triggers.Length && not matched do
            matched <- evalTrigger transitionCadence simTick playClock flow flags triggers[index]
            index <- index + 1

        matched

    let private stoppageFingerprint (world: World) : int =
        let g = world.State.HomeScore + world.State.AwayScore
        let f = world.Home.Stats.Fouls + world.Away.Stats.Fouls

        let c =
            world.Home.Cards.Yellows
            + world.Away.Cards.Yellows
            + world.Home.Cards.Reds
            + world.Away.Cards.Reds

        let s = world.Home.SubsUsed + world.Away.SubsUsed
        g * 10000 + f * 100 + c * 10 + s

    let private addedTimeFor (ctx: SystemContext) (world: World) (g: int) (f: int) (c: int) (s: int) : int<subtick> =
        let cfg = ctx.Ctx.Config.Match

        let sec =
            (float cfg.StoppageBasePerHalf)
            + float g * float cfg.StoppagePerGoal
            + float f * float cfg.StoppagePerFoul
            + float c * float cfg.StoppagePerCard
            + float s * float cfg.StoppagePerSub

        let clamped = min sec (float cfg.StoppageMax)
        int (round (clamped * float ctx.Clock.SubTicksPerSecond)) * 1<subtick>

    let private decideStoppage (ctx: SystemContext) (world: World) (playClock: PlayClock) : unit =
        let minute = PlayClock.toMatchMinute playClock

        if minute = 44<matchMin> && world.State.Stoppage.FirstHalfAdded = 0<subtick> then
            let g = world.State.HomeScore + world.State.AwayScore
            let f = world.Home.Stats.Fouls + world.Away.Stats.Fouls

            let c =
                world.Home.Cards.Yellows
                + world.Away.Cards.Yellows
                + world.Home.Cards.Reds
                + world.Away.Cards.Reds

            let s = world.Home.SubsUsed + world.Away.SubsUsed

            world.State <-
                { world.State with
                    Stoppage =
                        { world.State.Stoppage with
                            FirstHalfAdded = addedTimeFor ctx world g f c s
                            Half1Fingerprint = stoppageFingerprint world } }
        elif minute = 89<matchMin> && not world.State.Stoppage.Decided then
            let fp1 = world.State.Stoppage.Half1Fingerprint
            let fp2 = stoppageFingerprint world
            // Only second-half incidents count for added time: the first-half
            // fingerprint is subtracted, so stoppage is never double-counted.
            let h2 = max 0 (fp2 - fp1)
            let g2 = h2 / 10000
            let rem = h2 % 10000
            let f2 = rem / 100
            let rem2 = rem % 100
            let c2 = rem2 / 10
            let s2 = rem2 % 10

            world.State <-
                { world.State with
                    Stoppage =
                        { world.State.Stoppage with
                            SecondHalfAdded = addedTimeFor ctx world g2 f2 c2 s2
                            Decided = true } }

    let stepInto
        (schedule: CompiledSchedule)
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (output: MatchEvent[])
        : Rng * World * int =
        let tick = world.State.Tick
        let simTick = SimTick.ofSubTicks tick
        let playClock = MatchClock.playClock world.State.Clock
        let mutable flags = { EventFlags.Bits = 0UL }
        let applied = buffer.Applied
        buffer.Applied.Clear()
        let mutable appliedCount = 0

        let recordApplied (ev: MatchEvent) =
            if appliedCount >= output.Length then
                invalidOp "the per-tick applied event buffer is too small"

            applied.Add ev
            output[appliedCount] <- ev
            appliedCount <- appliedCount + 1

            if world.Observation.Enabled then
                let cause =
                    match ev.Data with
                    | EventData.RestartPlanned(id, _)
                    | EventData.RestartAuthorized id
                    | EventData.RestartCommandIssued(id, _)
                    | EventData.RestartContact(id, _, _)
                    | EventData.RestartCompleted id
                    | EventData.RestartPlanChanged(id, _) -> ValueSome(RestartReference id)
                    | _ -> world.Observation.ConsumeCause ev

                world.Observation.Record(ev.Tick, cause, EventCommitted ev) |> ignore

        let pendingCount = buffer.Events.Count

        // Event reducers only write Applied/world state; removing the processed
        // prefix afterward preserves newly queued events without copying it.
        for index = 0 to pendingCount - 1 do
            let ev = buffer.Events[index]

            if EventApply.applyBuffered ctx.Ctx world buffer ev then
                let k = MatchEvent.kind ev
                recordApplied ev
                flags <- EventFlags.union flags (EventFlags.ofKind k)
                EventWindow.push world.Home.EventWindow ev.Tick k
                EventWindow.push world.Away.EventWindow ev.Tick k

        if pendingCount > 0 then
            buffer.Events.RemoveRange(0, pendingCount)

        buffer.Flags <- flags

        let applyImmediately (ev: MatchEvent) : unit =
            if EventApply.applyBuffered ctx.Ctx world buffer ev then
                let k = MatchEvent.kind ev
                recordApplied ev
                flags <- EventFlags.union flags (EventFlags.ofKind k)
                buffer.Flags <- flags
                EventWindow.push world.Home.EventWindow ev.Tick k
                EventWindow.push world.Away.EventWindow ev.Tick k

        let flow', flowEvents =
            Flow.tick ctx.Clock ctx.Ctx.Config world tick world.State.Flow

        for ev in flowEvents do
            buffer.Events.Add ev

        world.State <- { world.State with Flow = flow' }

        // VAR verdict is decided only at review expiry, from the snapshot taken at
        // goal time — never re-simulated, so the outcome is deterministic.
        match flow' with
        | MatchFlow.VARReview(0<tickDelta>, _) ->
            let scoring =
                world.State.VARReview
                |> Option.map (fun review -> review.ScoringSide)
                |> Option.orElse world.State.LastAttackingClub
                |> Option.defaultValue ClubSide.Home

            let verdict =
                if RefereeVAR.isOffside ctx.Ctx world scoring then
                    VARResult.GoalOverturned
                else
                    VARResult.GoalStands

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.VARDecision verdict }
        | _ -> ()

        let mutable rng = rng

        match world.State.Advantage with
        | AdvantageState.AdvantagePending(incident, expiresAt) when tick >= expiresAt ->
            buffer.Events.Add
                { Tick = tick
                  Data = EventData.AdvantageExpired incident.Side }
        | _ -> ()

        // Knockout shootouts are events too: the resolver proposes the complete
        // deterministic sequence, and the reducer commits its separate score.
        if world.State.Flow = MatchFlow.Shootout && not world.State.Shootout.Scheduled then
            let kicks, rng' = Shootout.resolveDetailed ctx rng world

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.ShootoutStarted }

            for kick in kicks do
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.ShootoutKick(kick.Side, kick.Taker, kick.Scored) }

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.ShootoutFinished }

            rng <- rng'

        // Systems run strictly in scheduler order: each may read what earlier systems
        // wrote this same tick — order is part of determinism.
        // The match stream advances by exactly one draw per tick whatever the systems consume; each system gets
        // its own stream from that draw (see Rng.stream).
        let stepKey, advanced = Rng.nextU64 rng
        rng <- advanced

        for spec in schedule.Systems do
            if fire ctx.Ctx.Config.DefensiveTransition.ReevaluateTicks simTick playClock world.State.Flow flags spec.Triggers then
                spec.Step ctx (Rng.stream stepKey spec.StreamKey) world buffer |> ignore

        // Stoppage must be computed before boundary checks: the half ends at
        // 90 + added, so added time has to exist before the whistle fires.
        decideStoppage ctx world playClock

        match world.State.Flow, world.State.Clock.Phase with
        | (MatchFlow.Live | MatchFlow.Restart _), ClockPhase.FirstHalf ->
            let added = world.State.Stoppage.FirstHalfAdded

            if PlayClock.value playClock >= 108000<subtick> + added then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.HalfTime }
        | (MatchFlow.Live | MatchFlow.Restart _), ClockPhase.SecondHalf ->
            let added = world.State.Stoppage.SecondHalfAdded

            if PlayClock.value playClock >= 216000<subtick> + added then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.FullTime }
        | (MatchFlow.Live | MatchFlow.Restart _), ClockPhase.ExtraTimeFirstHalf ->
            let period =
                SimulationClock.secondsToSubTicks ctx.Clock ctx.Ctx.Config.Match.ExtraTimePeriodDuration

            if PlayClock.value playClock >= 216000<subtick> + period then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.HalfTime }
        | (MatchFlow.Live | MatchFlow.Restart _), ClockPhase.ExtraTimeSecondHalf ->
            let period =
                SimulationClock.secondsToSubTicks ctx.Clock ctx.Ctx.Config.Match.ExtraTimePeriodDuration

            if PlayClock.value playClock >= 216000<subtick> + period * 2 then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.FullTime }
        | _ -> ()

        // Clock advances only during play and restarts.
        match world.State.Flow with
        | MatchFlow.Live
        | MatchFlow.Restart _ ->
            let c = world.State.Clock

            world.State <-
                { world.State with
                    Clock = { c with Tick = c.Tick + 1<subtick> } }
        | _ -> ()

        // Momentum is transient by design: it must decay, or one lucky run would
        // color the rest of the match.
        if abs world.State.Momentum > 0.0001 then
            world.State <-
                { world.State with
                    Momentum = world.State.Momentum * 0.9995 }

        updateOfficials world

        world.State <-
            { world.State with
                Tick = tick + 1<subtick> }

        rng, world, appliedCount

    /// Test and tooling adapter. Runtime sessions use stepInto so no mutable
    /// collection escapes the tick owner.
    let step
        (systems: SystemSpec list)
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        : Rng * World * ResizeArray<MatchEvent> =
        let output = Array.zeroCreate<MatchEvent> 256

        let rng', world', count =
            stepInto (ScheduleCompiler.compile systems) ctx rng world buffer output

        let applied = ResizeArray<MatchEvent>(count)

        for i = 0 to count - 1 do
            applied.Add output[i]

        rng', world', applied
