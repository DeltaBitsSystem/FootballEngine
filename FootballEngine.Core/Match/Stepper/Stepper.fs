namespace FootballEngine.Core.Match

/// The per-tick pipeline. The order is fixed and is itself the determinism
/// contract: drain events -> flow machine -> systems -> clock. Any reordering
/// would change every match produced from the same seed.
module Stepper =

    let rec private evalTrigger (tick: int<subtick>) (flow: MatchFlow) (flags: EventFlags) (t: Trigger) : bool =
        match t with
        | Trigger.EveryTick -> true
        | Trigger.EveryN d ->
            let dT = int d * 1<subtick>
            tick % dT = 0<subtick>
        | Trigger.EveryMinute(period, offset) ->
            tick % SimulationClock.minuteSubticks = 0<subtick>
            && SimulationClock.toMatchMinute tick % period = offset
        | Trigger.OnEvent f -> EventFlags.intersects flags f
        | Trigger.OnEventOrEveryN(f, d) ->
            let dT = int d * 1<subtick>
            EventFlags.intersects flags f || tick % dT = 0<subtick>
        | Trigger.WhenLive inner ->
            match flow with
            | MatchFlow.Live -> evalTrigger tick flow flags inner
            | _ -> false

    let private fire (tick: int<subtick>) (flow: MatchFlow) (flags: EventFlags) (triggers: Trigger list) : bool =
        triggers |> List.exists (evalTrigger tick flow flags)

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

    let private decideStoppage (ctx: SystemContext) (world: World) (tick: int<subtick>) : unit =
        let minute = SimulationClock.toMatchMinute tick

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
        let mutable flags = { EventFlags.Bits = 0UL }
        let applied = ResizeArray<MatchEvent>()
        buffer.Applied.Clear()

        for ev in buffer.Events do
            EventApply.apply ctx.Ctx world ev
            let k = MatchEvent.kind ev
            buffer.Applied.Add ev
            applied.Add ev
            flags <- EventFlags.union flags (EventFlags.ofKind k)
            EventWindow.push world.Home.EventWindow ev.Tick k
            EventWindow.push world.Away.EventWindow ev.Tick k

        buffer.Flags <- flags
        buffer.Events.Clear()

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

                MatchWorld.updateBall world (fun b ->
                    { b with
                        Control = BallControl.Controlled(plan.Side, pid)
                        LastTouch = Some pid })

                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Possession(plan.Side, pid) }
            | None -> ()
        | _ -> ()

        // VAR verdict is decided only at review expiry, from the snapshot taken at
        // goal time — never re-simulated, so the outcome is deterministic.
        match flow' with
        | MatchFlow.VARReview(0<tickDelta>, _) ->
            let scoring = world.State.LastAttackingClub |> Option.defaultValue ClubSide.Home

            let verdict =
                if RefereeVAR.isOffside ctx.Ctx world scoring then
                    VARResult.GoalOverturned
                else
                    VARResult.GoalStands

            RefereeVAR.clearMarkers world scoring

            buffer.Events.Add
                { Tick = tick
                  Data = EventData.VARDecision verdict }
        | _ -> ()

        // Systems run strictly in scheduler order: each may read what earlier systems
        // wrote this same tick — order is part of determinism.
        let mutable rng = rng

        // R10 — knockout draw at full time goes to a deterministic shootout.
        if
            ctx.Ctx.IsKnockout
            && flow' = MatchFlow.MatchEnded
            && world.State.HomeScore = world.State.AwayScore
        then
            let (homeGoals, awayGoals), rng' = Shootout.resolve ctx rng world

            for _ in 1..homeGoals do
                applied.Add
                    { Tick = tick
                      Data = EventData.ShootoutGoal(ClubSide.Home) }

            for _ in 1..awayGoals do
                applied.Add
                    { Tick = tick
                      Data = EventData.ShootoutGoal(ClubSide.Away) }

            world.State <-
                { world.State with
                    HomeScore = world.State.HomeScore + homeGoals
                    AwayScore = world.State.AwayScore + awayGoals }

            rng <- rng'

        for spec in systems do
            if fire tick world.State.Flow flags spec.Triggers then
                rng <- spec.Step ctx rng world buffer

        // Stoppage must be computed before boundary checks: the half ends at
        // 90 + added, so added time has to exist before the whistle fires.
        decideStoppage ctx world tick

        match world.State.Clock.Phase with
        | ClockPhase.FirstHalf ->
            let added = world.State.Stoppage.FirstHalfAdded

            if tick >= 108000<subtick> + added then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.HalfTime }
        | ClockPhase.SecondHalf ->
            let added = world.State.Stoppage.SecondHalfAdded

            if tick >= 216000<subtick> + added then
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
