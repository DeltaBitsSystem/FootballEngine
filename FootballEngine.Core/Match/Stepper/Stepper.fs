namespace FootballEngine.Core.Match

/// The per-tick pipeline. The order is fixed and is itself the determinism
/// contract: drain events -> flow machine -> systems -> clock. Any reordering
/// would change every match produced from the same seed.
module Stepper =

    let rec private evalTrigger
        (simTick: SimTick)
        (playClock: PlayClock)
        (flow: MatchFlow)
        (flags: EventFlags)
        (t: Trigger)
        : bool =
        let tick = SimTick.value simTick

        match t with
        | Trigger.EveryTick -> true
        | Trigger.EveryN d ->
            let dT = int d * 1<subtick>
            tick % dT = 0<subtick>
        | Trigger.EveryMinute(period, offset) ->
            let playTick = PlayClock.value playClock

            playTick % SimulationClock.minuteSubticks = 0<subtick>
            && PlayClock.toMatchMinute playClock % period = offset
        | Trigger.OnEvent f -> EventFlags.intersects flags f
        | Trigger.OnEventOrEveryN(f, d) ->
            let dT = int d * 1<subtick>
            EventFlags.intersects flags f || tick % dT = 0<subtick>
        | Trigger.WhenLive inner ->
            match flow with
            | MatchFlow.Live -> evalTrigger simTick playClock flow flags inner
            | _ -> false

    let private fire
        (simTick: SimTick)
        (playClock: PlayClock)
        (flow: MatchFlow)
        (flags: EventFlags)
        (triggers: Trigger list)
        : bool =
        triggers |> List.exists (evalTrigger simTick playClock flow flags)

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

    let step
        (systems: SystemSpec list)
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        : Rng * World * MatchEvent list =
        let tick = world.State.Tick
        let simTick = SimTick.ofSubTicks tick
        let playClock = MatchClock.playClock world.State.Clock
        let mutable flags = { EventFlags.Bits = 0UL }
        let applied = ResizeArray<MatchEvent>()
        buffer.Applied.Clear()

        let toApply = List.ofSeq buffer.Events
        buffer.Events.Clear()

        for ev in toApply do
            if EventApply.applyBuffered ctx.Ctx world buffer ev then
                let k = MatchEvent.kind ev
                buffer.Applied.Add ev
                applied.Add ev
                flags <- EventFlags.union flags (EventFlags.ofKind k)
                EventWindow.push world.Home.EventWindow ev.Tick k
                EventWindow.push world.Away.EventWindow ev.Tick k

        buffer.Flags <- flags

        let applyImmediately (ev: MatchEvent) : unit =
            if EventApply.applyBuffered ctx.Ctx world buffer ev then
                let k = MatchEvent.kind ev
                buffer.Applied.Add ev
                applied.Add ev
                flags <- EventFlags.union flags (EventFlags.ofKind k)
                buffer.Flags <- flags
                EventWindow.push world.Home.EventWindow ev.Tick k
                EventWindow.push world.Away.EventWindow ev.Tick k

        let prevFlow = world.State.Flow
        let flow', flowEvents = Flow.tick ctx.Clock ctx.Ctx.Config world tick prevFlow

        for ev in flowEvents do
            buffer.Events.Add ev

        world.State <- { world.State with Flow = flow' }

        // Restart expiry: the ball must have a controller when play resumes. Hand
        // over to the side's set-piece executor (Phase 201 routine marker) so
        // the planned routine is actually taken; nearest active is the fallback.
        match prevFlow, flow' with
        | MatchFlow.RestartDelay(_, plan), MatchFlow.Live ->
            let executor =
                let team = MatchWorld.team world plan.Side

                team.Frame.RunLookup.Values
                |> Seq.tryFind (fun a -> a.Phase = 201)
                |> Option.map (fun a -> a.Slot)

            let slot =
                match executor with
                | Some slot when MatchWorld.isActive (MatchWorld.team world plan.Side).Frame slot -> Some slot
                | _ ->
                    MatchWorld.nearestActive
                        ctx.Ctx
                        world
                        plan.Side
                        world.State.Ball.Position.X
                        world.State.Ball.Position.Y
                        9999.0
                    |> Option.map fst

            match slot with
            | Some slot ->
                let pid = (MatchWorld.lineup ctx.Ctx plan.Side).Profiles.[slot].Id

                applyImmediately
                    { Tick = tick
                      Data = EventData.Possession(plan.Side, pid) }
            | None -> ()
        | _ -> ()

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
        | AdvantageState.AdvantagePending(_, _, _, _, _, expiresAt) when tick >= expiresAt ->
            buffer.Events.Add
                { Tick = tick
                  Data =
                    EventData.AdvantageExpired(
                        match world.State.Advantage with
                        | AdvantageState.AdvantagePending(side, _, _, _, _, _) -> side
                        | _ -> ClubSide.Home
                    ) }
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
        for spec in systems do
            if fire simTick playClock world.State.Flow flags spec.Triggers then
                rng <- spec.Step ctx rng world buffer

        // Stoppage must be computed before boundary checks: the half ends at
        // 90 + added, so added time has to exist before the whistle fires.
        decideStoppage ctx world playClock

        match world.State.Flow, world.State.Clock.Phase with
        | (MatchFlow.Live | MatchFlow.RestartDelay _), ClockPhase.FirstHalf ->
            let added = world.State.Stoppage.FirstHalfAdded

            if PlayClock.value playClock >= 108000<subtick> + added then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.HalfTime }
        | (MatchFlow.Live | MatchFlow.RestartDelay _), ClockPhase.SecondHalf ->
            let added = world.State.Stoppage.SecondHalfAdded

            if PlayClock.value playClock >= 216000<subtick> + added then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.FullTime }
        | (MatchFlow.Live | MatchFlow.RestartDelay _), ClockPhase.ExtraTimeFirstHalf ->
            let period =
                SimulationClock.secondsToSubTicks ctx.Clock ctx.Ctx.Config.Match.ExtraTimePeriodDuration

            if PlayClock.value playClock >= 216000<subtick> + period then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.HalfTime }
        | (MatchFlow.Live | MatchFlow.RestartDelay _), ClockPhase.ExtraTimeSecondHalf ->
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
        | MatchFlow.RestartDelay _ ->
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

        world.State <-
            { world.State with
                Tick = tick + 1<subtick> }

        rng, world, List.ofSeq applied
