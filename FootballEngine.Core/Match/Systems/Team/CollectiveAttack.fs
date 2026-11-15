namespace FootballEngine.Core.Match

module AttackEstimate = FootballEngine.Domain.AttackEstimate

module AttackLearning =
    let estimate key (estimates: Map<int, AttackEstimate>) =
        estimates |> Map.tryFind key |> Option.defaultValue AttackEstimate.empty

    let value (cfg: CollectiveAttackConfig) key estimates =
        let evidence = estimate key estimates
        evidence.RewardSum / (cfg.PriorStrength + evidence.Samples)

    let uncertainty (cfg: CollectiveAttackConfig) key estimates =
        cfg.Exploration / sqrt (cfg.PriorStrength + (estimate key estimates).Samples)

    let add (cfg: CollectiveAttackConfig) key reward estimates =
        let sample: AttackEstimate = { Samples = 1.0; RewardSum = MatchMath.clamp -1.0 1.0 reward }
        estimates |> Map.add key (AttackEstimate.merge cfg.EvidenceCap (estimate key estimates) sample)

    let retain (cfg: CollectiveAttackConfig) (estimates: Map<int, AttackEstimate>) =
        estimates |> Map.map (fun _ estimate ->
            { estimate with Samples = estimate.Samples * cfg.SeasonalRetention
                            RewardSum = estimate.RewardSum * cfg.SeasonalRetention })

    let applyEvents cfg side prior (events: MatchEvent list) =
        events
        |> List.choose (fun event ->
            match event.Data with
            | EventData.CollectiveAttack(owner, episode, Ended(_, reward, Some key)) when owner = side -> Some(episode, key, reward)
            | _ -> None)
        |> List.distinctBy (fun (episode, _, _) -> episode)
        |> List.fold (fun evidence (_, key, reward) -> add cfg key reward evidence) prior

    let fromEvents cfg side events = applyEvents cfg side Map.empty events

module AttackGeometry =
    let position (frame: TeamFrame) slot =
        Vec2.create (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

    let space (cfg: CollectiveAttackConfig) (opponents: TeamFrame) (point: Vec2) =
        let mutable nearest = float cfg.SpaceScale
        for slot in MatchWorld.activeSlots opponents do
            nearest <- min nearest (MatchMath.dist point.X point.Y (MatchWorld.posX opponents slot) (MatchWorld.posY opponents slot))
        MatchMath.clamp01 (nearest / float cfg.SpaceScale)

    let lane (cfg: CollectiveAttackConfig) (opponents: TeamFrame) (origin: Vec2) (target: Vec2) =
        let dx, dy = float (target.X - origin.X), float (target.Y - origin.Y)
        let lengthSq = dx * dx + dy * dy
        let mutable clear = lengthSq > 0.0
        for slot in MatchWorld.activeSlots opponents do
            if opponents.SlotRoles[slot] <> SlotRole.Goalkeeper then
                let ox, oy = float (MatchWorld.posX opponents slot - origin.X), float (MatchWorld.posY opponents slot - origin.Y)
                let fraction = if lengthSq = 0.0 then 0.0 else MatchMath.clamp01 ((ox * dx + oy * dy) / lengthSq)
                let ex, ey = ox - fraction * dx, oy - fraction * dy
                if ex * ex + ey * ey < float cfg.LaneRadius ** 2.0 then clear <- false
        clear

    let legalTarget (ctx: MatchContext) (world: World) side x y =
        let dir = MatchMath.goalDirection side
        let line = MatchWorld.offsideLine ctx world side
        // The ball and halfway line also legalize a runner; the second-last defender alone does not.
        let legalX = max 0.0 (max (float line * dir) (float world.State.Ball.Position.X * dir)) - float ctx.Config.CollectiveAttack.OffsideMargin
        let x, y = MatchMath.pitchClamp (min (x * dir) legalX * dir) y
        Vec2.create (x * 1.0<meter>) (y * 1.0<meter>)

    let context (cfg: CollectiveAttackConfig) side (point: Vec2) =
        let x = float point.X * MatchMath.goalDirection side
        if x < -float cfg.TerritoryBoundary then 0 elif x < float cfg.TerritoryBoundary then 1 else 2

    let situation (cfg: CollectiveAttackConfig) side (point: Vec2) (opponents: TeamFrame) =
        let mutable pressure = 0

        for slot in MatchWorld.activeSlots opponents do
            if opponents.SlotRoles[slot] <> SlotRole.Goalkeeper && MatchMath.dist point.X point.Y (MatchWorld.posX opponents slot) (MatchWorld.posY opponents slot) <= float cfg.PressureRadius then
                pressure <- pressure + 1

        context cfg side point + (if pressure >= cfg.PressureCount then 3 else 0)

    let exposure side (point: Vec2) (frame: TeamFrame) =
        let mutable cover = 0

        for slot in MatchWorld.activeSlots frame do
            if frame.SlotRoles[slot] <> SlotRole.Goalkeeper && float (MatchWorld.posX frame slot - point.X) * MatchMath.goalDirection side < 0.0 then
                cover <- cover + 1

        if cover >= 2 then 0.0 else 1.0

    let complementary cfg (duties: AttackDuty list) =
        duties |> List.forall (fun a -> duties |> List.forall (fun b ->
            a.Player = b.Player || MatchMath.dist a.Target.X a.Target.Y b.Target.X b.Target.Y >= float cfg.MinimumDutySeparation))

    let private separated cfg (a: AttackDuty) (b: AttackDuty) =
        a.Player = b.Player || MatchMath.dist a.Target.X a.Target.Y b.Target.X b.Target.Y >= float cfg.MinimumDutySeparation

    /// `complementary` for exactly three duties without building a list; the distance is symmetric, so the three
    /// unordered pairs decide what the six ordered ones did.
    let complementary3 cfg (a: AttackDuty) (b: AttackDuty) (c: AttackDuty) =
        separated cfg a b && separated cfg a c && separated cfg b c

type AttackCandidate =
    { Pattern: AttackPattern
      Link: AttackDuty
      Runner: AttackDuty
      Security: AttackDuty
      Geometry: float
      Learned: float
      Uncertainty: float
      Score: float }

/// Sustained commitments are team-owned; only observed football facts advance their stages.
module AttackCandidates =
    let private patterns = [| ThirdMan; AttractSwitch; ExploitDepth; SecureProgression |]

    let candidates (ctx: MatchContext) (world: World) side carrierSlot =
        let cfg = ctx.Config.CollectiveAttack
        let team = MatchWorld.team world side
        let frame = team.Frame
        let opponents = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
        let origin = AttackGeometry.position frame carrierSlot
        let dir = MatchMath.goalDirection side
        let context = AttackGeometry.situation cfg side origin opponents
        let sign = if float origin.Y >= 0.0 then 1.0 else -1.0
        let point depth y = AttackGeometry.legalTarget ctx world side (float origin.X + dir * depth) y
        let eligible slot =
            TransitionGeometry.outfielder frame slot && slot <> carrierSlot && float frame.Condition[slot] >= cfg.MinimumCondition
        // The earliest arrival (ties to the lower slot) among the eligible players not already given a duty; -1
        // when nobody gets there within MaxArrival.
        let choose target excluded1 excluded2 =
            let mutable best = -1
            let mutable bestArrival = 0.0<second>

            for slot in MatchWorld.activeSlots frame do
                if eligible slot && slot <> excluded1 && slot <> excluded2 then
                    let arrival = TransitionGeometry.arrivalTime ctx frame slot target

                    if best < 0 || arrival < bestArrival || (arrival = bestArrival && slot < best) then
                        best <- slot
                        bestArrival <- arrival

            if best >= 0 && bestArrival <= cfg.MaxArrival then best else -1
        let dutyOf slot target =
            { Player = (MatchWorld.profileOf ctx side slot).Id; Slot = slot; Target = target }
        let results = Array.zeroCreate<AttackCandidate> patterns.Length
        let mutable count = 0
        for pattern in patterns do
               let linkPoint =
                   match pattern with
                   | ExploitDepth -> point (float cfg.SupportDepth) (float origin.Y + sign * float cfg.SupportWidth)
                   | _ -> point (float cfg.SupportDepth) (float origin.Y - sign * float cfg.SupportWidth)
               let runnerPoint =
                   match pattern with
                   | ThirdMan -> point (float cfg.RunDepth) (float origin.Y + sign * float cfg.SupportWidth)
                   | AttractSwitch -> point (float cfg.SupportDepth) (-sign * float cfg.SwitchWidth)
                   | ExploitDepth -> point (float cfg.RunDepth) (float origin.Y - sign * float cfg.SupportWidth)
                   | SecureProgression -> point (float cfg.SupportDepth) (float origin.Y + sign * float cfg.SupportWidth)
               let securityPoint = point (-float cfg.SecurityDepth) (float origin.Y + sign * float cfg.SupportWidth)
               let linkSlot = choose linkPoint -1 -1
               if linkSlot >= 0 then
                   let runnerSlot = choose runnerPoint linkSlot -1
                   if runnerSlot >= 0 then
                       let securitySlot = choose securityPoint linkSlot runnerSlot
                       if securitySlot >= 0 then
                           let link = dutyOf linkSlot linkPoint
                           let runner = dutyOf runnerSlot runnerPoint
                           let security = dutyOf securitySlot securityPoint
                           let currentLink = AttackGeometry.position frame link.Slot
                           let currentRunner = AttackGeometry.position frame runner.Slot
                           let firstLane = if pattern = ExploitDepth then currentRunner else currentLink
                           if AttackGeometry.complementary3 cfg link runner security && AttackGeometry.lane cfg opponents origin firstLane then
                               let readiness (duty: AttackDuty) =
                                   let p = MatchWorld.profileOf ctx side duty.Slot
                                   MatchMath.clamp01 ((float p.Anticipation + float p.Teamwork) / 200.0)
                                   * float frame.Cognitive[duty.Slot] * float frame.Condition[duty.Slot]
                               let space = AttackGeometry.space cfg opponents runnerPoint
                               let openness = if AttackGeometry.lane cfg opponents linkPoint runnerPoint then 1.0 else 0.0
                               let tactical =
                                   match pattern with
                                   | ThirdMan -> 1.0 - float team.Tactics.Directness
                                   | AttractSwitch -> float team.Tactics.Width
                                   | ExploitDepth -> float team.Tactics.Directness
                                   | SecureProgression -> 1.0 - max 0.0 (float team.Tactics.Mentality)
                               let geometry = (space + openness + readiness link + readiness runner + tactical) / 5.0
                               let key = CollectiveAttackState.key context pattern
                               let learned = if cfg.LearningEnabled then AttackLearning.value cfg key team.Attack.Estimates else 0.0
                               let uncertainty = if cfg.LearningEnabled then AttackLearning.uncertainty cfg key team.Attack.Estimates else 0.0
                               results[count] <-
                                   { Pattern = pattern; Link = link; Runner = runner; Security = security
                                     Geometry = geometry; Learned = learned; Uncertainty = uncertainty
                                     Score = geometry + cfg.LearningWeight * learned + uncertainty }
                               count <- count + 1
        if count = results.Length then results else Array.sub results 0 count

module AttackLifecycle =
    let emit (world: World) (buffer: EventBuffer) side id change cause =
        let ev = { Tick = world.State.Tick; Data = EventData.CollectiveAttack(side, id, change) }
        cause |> Option.iter (fun source -> world.Observation.Associate(ev, EventCause source))
        buffer.Events.Add ev
        buffer.Flags <- EventFlags.union buffer.Flags (EventFlags.ofKind EventKind.CollectiveAttack)
        ev

    let finish (ctx: MatchContext) (world: World) buffer side reason cause =
        let cfg = ctx.Config.CollectiveAttack
        let team = MatchWorld.team world side
        let state = team.Attack
        match state.Episode with
        | None -> ()
        | Some episode ->
            let point = episode.OwnedPoint
            let progress = float (point.X - episode.Origin.X) * MatchMath.goalDirection side
            let space = episode.OwnedSpace - episode.BaselineSpace
            let exposure = episode.OwnedExposure
            // Unconnected carrying does not earn territorial credit for a combination it never executed.
            let realized = if episode.Connections > 0 then cfg.ProgressReward * MatchMath.clamp -1.0 1.0 (progress / float cfg.RunDepth) + cfg.SpaceReward * space else 0.0
            let penalty =
                match reason with
                | LostBall -> cfg.LossPenalty
                | TimedOut | Countered | Deviated -> cfg.IdlePenalty
                | _ -> 0.0
            let reward = MatchMath.clamp -1.0 1.0 (realized + cfg.ConnectionReward * float episode.Connections + cfg.ThreatReward * episode.Threat - penalty - cfg.ExposurePenalty * exposure)
            // Interrupted administrative commitments supply no football sample.
            let learningKey =
                if cfg.LearningEnabled && reason <> ParticipantLeft && reason <> InstructionsChanged && reason <> AttackEnd.Stopped then
                    Some(CollectiveAttackState.key episode.Context episode.Pattern)
                else None
            learningKey |> Option.iter (fun key ->
                state.Estimates <- AttackLearning.add cfg key reward state.Estimates
                state.Evidence <- AttackLearning.add cfg key reward state.Evidence)
            state.Episode <- None
            state.AvailableAt <- world.State.Tick + cfg.Recovery
            let terminal = emit world buffer side episode.Id (Ended(reason, reward, learningKey)) cause
            world.Observation.Record(world.State.Tick, ValueSome(EventCause(MatchEvent.id terminal)), AttackResolved(side, episode.Id, reason, reward, episode.Connections, progress, exposure)) |> ignore

    let frontier ctx world side point (episode: AttackEpisode) =
        episode.OwnedPoint <- point
        episode.OwnedSpace <- AttackGeometry.space ctx.Config.CollectiveAttack (MatchWorld.team world (MatchWorld.oppOf side)).Frame point
        episode.OwnedExposure <- AttackGeometry.exposure side point (MatchWorld.team world side).Frame

    // Reduction consumes each authoritative fact in drain order, so no single-slot mailbox can overwrite a connection or shot.
    let observe (ctx: MatchContext) (world: World) (buffer: EventBuffer) (ev: MatchEvent) =
        for side in MatchWorld.sides do
            let state = (MatchWorld.team world side).Attack
            match state.Episode with
            | Some episode when ev.Tick >= episode.Deadline -> finish ctx world buffer side TimedOut episode.LastFact
            | Some episode when ev.Tick >= episode.StartedAt && ev.Tick >= episode.FactTick && (match episode.LastFact with Some last -> last <> MatchEvent.id ev | None -> true) ->
                let id = MatchEvent.id ev
                match ev.Data with
                | EventData.Possession(owner, _) when owner <> side -> finish ctx world buffer side LostBall (Some id)
                | EventData.Pass(passer, Some receiver, _, _, true, _, _, x, y) when MatchWorld.sideOfPlayer ctx passer = ValueSome side ->
                    let expected =
                        passer = episode.Owner &&
                        match episode.Stage with
                        | Preparing when episode.Pattern = ExploitDepth -> receiver = episode.Runner.Player
                        | Preparing -> receiver = episode.Link.Player
                        | Connected -> receiver = episode.Runner.Player
                        | Exploiting -> false
                    if expected then
                        let stage = if episode.Stage = Preparing && episode.Pattern <> ExploitDepth then Connected else Exploiting
                        frontier ctx world side (Vec2.create x y) episode
                        state.Episode <- Some { episode with Owner = receiver; Stage = stage; Connections = episode.Connections + 1; LastFact = Some id; FactTick = ev.Tick; BlockedSince = None }
                        emit world buffer side episode.Id (Advanced(stage, id)) (Some id) |> ignore
                    else finish ctx world buffer side Deviated (Some id)
                | EventData.Shot(shooter, _, xg, _, _, _) when MatchWorld.sideOfPlayer ctx shooter = ValueSome side ->
                    if shooter = episode.Owner && episode.Connections > 0 && System.Double.IsFinite xg && xg >= 0.0 && xg <= 1.0 then
                        state.Episode <- Some { episode with Threat = xg; LastFact = Some id; FactTick = ev.Tick }
                        finish ctx world buffer side AttackEnd.Completed (Some id)
                    else finish ctx world buffer side Deviated (Some id)
                | _ -> ()
            | _ -> ()

module TeamAttack =
    let candidates = AttackCandidates.candidates
    let private emit = AttackLifecycle.emit
    let private finish = AttackLifecycle.finish

    let private update (ctx: MatchContext) (world: World) buffer side =
        let cfg = ctx.Config.CollectiveAttack
        let team = MatchWorld.team world side
        let state = team.Attack
        let tick = world.State.Tick
        let carrier = MatchWorld.ballCarrier ctx world
        match state.Episode with
        | Some initial ->
            let valid duty = TransitionGeometry.outfielder team.Frame duty.Slot && (MatchWorld.profileOf ctx side duty.Slot).Id = duty.Player
            let cause = initial.LastFact
            let episode = initial
            let ownFlight =
                match world.State.Ball.Trajectory with
                | Some(TrajectoryIntent.Aimed(passer, receiver, _)) ->
                    MatchWorld.sideOfPlayer ctx receiver = ValueSome side && MatchWorld.sideOfPlayer ctx passer = ValueSome side
                | Some(TrajectoryIntent.Struck(shooter, _, _, _)) -> MatchWorld.sideOfPlayer ctx shooter = ValueSome side
                | _ -> false
            // A loose contact is uncertainty, not evidence the rival won possession; flight and contested recovery stay bounded by the deadline.
            let lost = match carrier with ValueSome(struct (owner, _, _)) -> owner <> side | ValueNone -> (match world.State.Possession.LastGainer with Some gainer -> gainer <> side | None -> true)
            let deviated = match carrier with ValueSome(struct (owner, _, player)) -> owner = side && player <> episode.Owner | _ -> false
            match carrier with
            | ValueSome(struct (owner, _, player)) when owner = side && player = episode.Owner ->
                AttackLifecycle.frontier ctx world side (Vec2.create world.State.Ball.Position.X world.State.Ball.Position.Y) episode
            | _ -> ()
            let carrierPresent = TransitionGeometry.outfielder team.Frame (MatchWorld.slotIndex ctx side episode.Carrier)
            if not cfg.Enabled || world.State.Flow <> MatchFlow.Live then finish ctx world buffer side AttackEnd.Stopped cause
            elif not (carrierPresent && valid episode.Link && valid episode.Runner && valid episode.Security) then finish ctx world buffer side ParticipantLeft cause
            elif team.Tactics <> episode.Tactics then finish ctx world buffer side InstructionsChanged cause
            elif lost then finish ctx world buffer side LostBall cause
            elif deviated then finish ctx world buffer side Deviated cause
            elif tick >= episode.Deadline then finish ctx world buffer side TimedOut cause
            else
                let opponents = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
                let origin = Vec2.create world.State.Ball.Position.X world.State.Ball.Position.Y
                let target = if episode.Stage = Preparing && episode.Pattern <> ExploitDepth then episode.Link else episode.Runner
                let clear = episode.Stage = Exploiting || ownFlight || AttackGeometry.lane cfg opponents origin (AttackGeometry.position team.Frame target.Slot)
                let blocked = if clear then None else episode.BlockedSince |> Option.orElse (Some tick)
                // A rival must actually hold the closure before a sustained commitment is abandoned.
                match blocked with
                | Some since when tick - since >= cfg.CounterHold -> finish ctx world buffer side Countered cause
                | _ ->
                    let legal (duty: AttackDuty) = { duty with Target = AttackGeometry.legalTarget ctx world side (float duty.Target.X) (float duty.Target.Y) }
                    let link, runner, security = legal episode.Link, legal episode.Runner, legal episode.Security
                    if AttackGeometry.complementary3 cfg link runner security then
                        episode.BlockedSince <- blocked
                        episode.Link <- link
                        episode.Runner <- runner
                        episode.Security <- security
                    else finish ctx world buffer side Countered cause
        | None when cfg.Enabled && world.State.Flow = MatchFlow.Live && tick >= state.AvailableAt ->
            match carrier with
            | ValueSome(struct (owner, slot, player)) when owner = side && TransitionGeometry.outfielder team.Frame slot ->
                let choices = candidates ctx world side slot
                match choices |> Array.sortByDescending _.Score |> Array.tryHead with
                | Some chosen when chosen.Geometry >= cfg.MinimumScore ->
                    let origin = AttackGeometry.position team.Frame slot
                    let context = AttackGeometry.situation cfg side origin (MatchWorld.team world (MatchWorld.oppOf side)).Frame
                    let episode =
                        { Id = state.NextId; Pattern = chosen.Pattern; Context = context; StartedAt = tick; Deadline = tick + cfg.Duration
                          Stage = Preparing; Carrier = player; Owner = player; Link = chosen.Link; Runner = chosen.Runner; Security = chosen.Security
                          Origin = origin; BaselineSpace = AttackGeometry.space cfg (MatchWorld.team world (MatchWorld.oppOf side)).Frame origin
                          Connections = 0; LastFact = None; Threat = 0.0; OwnedPoint = origin
                          OwnedSpace = AttackGeometry.space cfg (MatchWorld.team world (MatchWorld.oppOf side)).Frame origin
                          OwnedExposure = AttackGeometry.exposure side origin team.Frame; FactTick = tick
                          Tactics = team.Tactics; BlockedSince = None }
                    state.NextId <- state.NextId + 1UL
                    state.Episode <- Some episode
                    let ev = emit world buffer side episode.Id (Started episode.Pattern) None
                    for candidate in choices do
                        world.Observation.Record(tick, ValueSome(EventCause(MatchEvent.id ev)), AttackEvaluated(side, episode.Id, candidate.Pattern, context, candidate.Geometry, candidate.Learned, candidate.Uncertainty)) |> ignore
                | _ -> state.AvailableAt <- tick + cfg.Recovery
            | _ -> ()
        | None -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            update ctx.Ctx world buffer ClubSide.Home
            update ctx.Ctx world buffer ClubSide.Away
            rng
