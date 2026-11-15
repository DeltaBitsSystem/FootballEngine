namespace FootballEngine.Core.Match

/// The carrier's mind: turns football judgement into one bounded choice so
/// behaviour is a utility-weighted selection from a small option set
/// (shot/pass/cross/dribble/clear), never a scripted trigger. The choice is
/// stored as a typed proposal that Actions executes later — deciding and
/// doing stay decoupled. Deterministic: all randomness comes from the threaded
/// Rng, so the same situation always picks the same option.
module PlayerDecision =

    /// One formula for xG, shared with the shot system: the carrier's belief
    /// and the strike's value must agree, or decisions would chase a different
    /// number than physics delivers.
    let xGValue (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : float =
        let p = MatchWorld.profileOf ctx side slot

        let x, y =
            MatchWorld.posX (MatchWorld.team world side).Frame slot,
            MatchWorld.posY (MatchWorld.team world side).Frame slot

        let d = MatchMath.distanceToGoal side x y
        let near = MatchWorld.opponentsNear ctx world side x y 6.0
        let blocked = DecisionValue.shotBlockers world side x y

        let openness =
            (1.0 - 0.3 * MatchMath.clamp01 (float near / 4.0))
            * pown (1.0 - ctx.Config.ExpectedValue.ShotBlockShare) blocked

        MatchMath.xGFrom d (float p.Finishing) openness

    let private offsideRiskFromLine (line: float<meter>) (side: ClubSide) (rx: float<meter>) : bool =
        let dir = MatchMath.goalDirection side

        if dir > 0.0 then
            float rx > float line
        else
            float rx < float line

    let private offsideRisk (ctx: MatchContext) (world: World) (side: ClubSide) (receiverSlot: int) : bool =
        let rx = MatchWorld.posX (MatchWorld.team world side).Frame receiverSlot
        let line = MatchWorld.offsideLine ctx world side
        offsideRiskFromLine line side rx

    let private spaceAt
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (rx: float<meter>)
        (ry: float<meter>)
        : float =
        1.0 - 0.25 * float (MatchWorld.opponentsNear ctx world side rx ry 7.0)

    // R6 — pure memory terms, shared by `choose` and the test suite so the
    // behaviour is verifiable without simulating a whole match.
    /// R6 — failing passes cool the next one: the penalty grows with the
    /// memory of failures, so a player in a bad streak chooses safer options.
    let passMemoryPenalty (cfg: MatchContext) (mem: PlayerMemory) : float =
        if mem.PassFailMod > 0.25 then
            cfg.Config.Actions.MemoryPassFailWeight * mem.PassFailMod
        else
            0.0

    /// R6 — duel form feeds confidence: past success makes dribbling more
    /// attractive and past failure less, so risk-taking follows the streak.
    let duelMemoryBias (cfg: MatchContext) (mem: PlayerMemory) : float =
        cfg.Config.Actions.MemoryDuelWeight * (mem.DuelSuccessMod - mem.DuelFailMod)

    /// R6 — a goal-less run cools shooting: only negative shot memory ever
    /// damps the shot, so form can hold a player back but never inflate him.
    let shotMemoryBias (cfg: MatchContext) (mem: PlayerMemory) : float =
        cfg.Config.Actions.MemoryPassFailWeight
        * (if mem.ShotSuccessMod < 0.0 then mem.ShotSuccessMod else 0.0)

    /// The player's own mean skill. Option preferences read each skill against it: a player better at everything
    /// is better at executing every option, not keener on the options with more skill terms, which made stronger
    /// sides pass instead of shooting and weaker sides shoot more.
    let ownLevel (p: PlayerProfile) : float =
        float (
            p.Pace
            + p.Strength
            + p.Stamina
            + p.Agility
            + p.Vision
            + p.Passing
            + p.Finishing
            + p.Crossing
            + p.Dribbling
            + p.Tackling
            + p.Marking
            + p.Positioning
            + p.Anticipation
            + p.Composure
            + p.Teamwork
            + p.DecisionMaking
        )
        / 16.0

    /// A cognitively tired carrier settles for the obvious choice: a colder softmax.
    let effectiveTemperature (cfg: BalanceConfig) (cognitive: float) : float =
        cfg.ExpectedValue.Temperature * (0.4 + 0.6 * cognitive)

    /// Tackling of the nearest opponent: he is the one who contests a carry.
    let private nearestTackler (ctx: MatchContext) (world: World) (side: ClubSide) (x: float<meter>) (y: float<meter>) =
        let opp = MatchWorld.team world (MatchWorld.oppOf side)
        let mutable best = System.Double.PositiveInfinity
        let mutable tackling = 50.0

        for s in MatchWorld.activeSlots opp.Frame do
            let d =
                MatchMath.dist x y (MatchWorld.posX opp.Frame s) (MatchWorld.posY opp.Frame s)

            if d < best then
                best <- d
                tackling <- float (MatchWorld.profileOf ctx (MatchWorld.oppOf side) s).Tackling

        tackling

    /// The pass option set: every receiver the lane and the offside line allow, worth the expected value of the
    /// pass (DecisionValue.pass) at the completion the intercept race and the passer's Passing give, plus a small
    /// style term. Shared by `prepare` and the test suite so pass selection is verifiable in isolation.
    let private passOptionsInto (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (options: PlanOptions) =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let e = ctx.Config.ExpectedValue
        let tactics = team.Tactics
        let tradeoffs = ctx.Config.TacticalTradeoffs
        let p = MatchWorld.profileOf ctx side slot
        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let cognitive = float frame.Cognitive.[slot]
        let mem = team.Memory.[slot]
        let passMemoryMod = passMemoryPenalty ctx mem
        let level = ownLevel p

        let attacking =
            team.Blackboard.OurPhase = PhaseKind.Attacking
            || team.Blackboard.OurPhase = PhaseKind.TransitionAttack

        // Hoisted out of the per-candidate loop: the pass model and the offside line do not depend on the receiver.
        let pparams =
            PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball

        let offsideLine = MatchWorld.offsideLine ctx world side

        for s2 in MatchWorld.activeSlots frame do
            // A receiver who cannot see the carrier, behind a lane a body already cuts or offside is not a target:
            // the race below prices interceptors but not a deflection off a body in the line or a ball played
            // behind a man facing away.
            if
                s2 <> slot
                && s2 <> 0
                && frame.LaneClear.[s2]
                && frame.FaceCone.[s2]
                && not (
                    offsideRiskFromLine
                        (offsideLine + e.OffsideReadMargin * MatchMath.goalDirection side)
                        side
                        (MatchWorld.posX frame s2)
                )
            then
                let rx = MatchWorld.posX frame s2
                let ry = MatchWorld.posY frame s2
                let d = MatchMath.dist x y rx ry
                let longBall = d > float ctx.Config.Actions.PassLongThreshold
                let dM = max d 0.5 * 1.0<meter>

                let launchSpeed =
                    (BallAero.pass
                        ctx.Config.Ball
                        ctx.Config.Actions.PassArrivalSpeed
                        ctx.Config.Actions.PassArrivalSpeedGain
                        d)
                        .LaunchSpeed
                    * 1.0<meterPerSecond>

                let launched =
                    { X = world.State.Ball.Position.X
                      Y = world.State.Ball.Position.Y
                      Z = 0.0<meter>
                      Vx = (rx - x) / dM * launchSpeed
                      Vy = (ry - y) / dM * launchSpeed
                      Vz = 0.0<meterPerSecond> }

                let margin =
                    PassIntercept.margin ctx.Config.PassIntercept pparams world side s2 launched offsideLine

                if margin >= e.PassMinimumMargin then
                    let completion = DecisionValue.passCompletion e (float p.Passing) margin

                    let style =
                        (if longBall then
                             float tactics.Directness * tradeoffs.DirectnessLongBallUtility
                         else
                             0.0)
                        + (if attacking then 0.0 else e.StylePhasePenalty)
                        + e.StyleTeamwork * ((float p.Teamwork - level) / 100.0)
                        + e.StyleConcentration * (cognitive - 0.5)
                        - passMemoryMod
                        + e.StyleConsistency * (float frame.Cognitive.[s2] - 0.5)

                    let value = DecisionValue.pass e side completion x y rx ry + e.StyleValue * style

                    options.Add(value, 1, s2)

    /// The same options as a list, last candidate first: the order the plan lists passes in before it reverses them.
    let passOptionsFor (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : (float * int * float) list =
        let found = PlanOptions()
        passOptionsInto ctx world side slot found
        let mutable options: (float * int * float) list = []

        for index in 0 .. found.Count - 1 do
            options <- (found.Utility[index], found.Code[index], float found.Target[index]) :: options

        options

    /// What the planner is handed, in the reference form the tests compare against the slot's own arrays.
    type internal DecisionInputs =
        { Plan: PlanSlot
          Chain: ChainState
          PlannerOptions: (int * int * float)[]
          Intercept: InterceptTable.InterceptParams }

    /// Writes a decision's inputs into the slot: the options, the snapshot the planner searches and the values the
    /// choice still needs from this moment. Separate from `choose` so the planner can be exercised on exactly the
    /// inputs a real carrier produces.
    let internal fillPlan
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (tick: int<subtick>)
        (plan: PlanSlot)
        : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        // Attacking mentality lowers the bar for shots and take-ons; defensive
        // mentality only takes the sure thing.
        let riskShift =
            float (MatchWorld.team world side).Tactics.Mentality
            * ctx.Config.TacticalTradeoffs.MentalityRiskThresholdShift

        let p = MatchWorld.profileOf ctx side slot
        let phase = team.Blackboard.OurPhase
        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let dir = MatchMath.goalDirection side

        let cognitive = float frame.Cognitive.[slot]
        let mem = team.Memory.[slot]

        // R6 — failure streaks push the player toward safer choices.
        let passMemoryMod = passMemoryPenalty ctx mem

        let pressure = MatchWorld.opponentsNear ctx world side x y 4.0
        let distToGoal = MatchMath.distanceToGoal side x y
        let attacking = phase = PhaseKind.Attacking || phase = PhaseKind.TransitionAttack
        let inOwnThird = float x * dir < -20.0

        let fin = float p.Finishing
        let comp = float p.Composure
        let passing = float p.Passing
        let vision = float p.Vision
        let crossing = float p.Crossing
        let bravery = (float p.Aggression * 0.5 + float p.Composure * 0.5) / 100.0
        let level = ownLevel p

        let options = plan.Options
        options.Clear()
        let mutable hasShot = false
        let mutable shotUtility = 0.0

        let e = ctx.Config.ExpectedValue
        let xg = xGValue ctx world side slot

        // Style moves a choice only between options of near-equal value; the value itself decides who shoots.
        if distToGoal < 32.0 then
            let style =
                e.StyleShotBravery * (bravery - 0.5)
                + e.StyleFlair * ((float p.Flair - 60.0) / 100.0)
                + shotMemoryBias ctx mem
                + riskShift

            hasShot <- true
            shotUtility <- xg + e.StyleValue * style

        passOptionsInto ctx world side slot options
        options.ReverseFrom 0

        if hasShot then
            options.Add(shotUtility, 2, -1)

        if
            attacking
            && abs (float y) > 20.0
            && DecisionValue.crossTarget ctx world side slot >= 0
        then
            // R9 — adaptive channels: wing play favours crosses, center play favours through passes.
            let channelBias =
                ctx.Config.Team.PatternWingBias
                * (float team.Emergent.ChannelLeft + float team.Emergent.ChannelRight)
                - ctx.Config.Team.PatternCenterBias * float team.Emergent.ChannelCenter

            let style = channelBias + e.StyleCrossWidth * float team.Directive.Params.Width
            options.InsertFirst(DecisionValue.cross e side crossing + e.StyleValue * style, 5, -1)

        let keep =
            DecisionValue.carryKeep
                e
                (float p.Dribbling)
                (nearestTackler ctx world side x y)
                (MatchWorld.opponentsNear ctx world side x y (float e.PressureRadius))
                (DecisionValue.blockers e world side x y)

        let carryStyle =
            e.StyleCarryBravery * (bravery - 0.5)
            - duelMemoryBias ctx mem * 0.5
            - e.StyleCarryTempo
              * float (0.5 - float team.Directive.Params.LineHeight / 100.0 |> max 0.0)
            + riskShift

        if inOwnThird && pressure > 0 then
            options.InsertFirst(DecisionValue.clear e side x y, 4, -1)

        options.InsertFirst(DecisionValue.carry e side keep x y + e.StyleValue * carryStyle, 3, -1)

        match team.Attack.Episode with
        | Some episode when episode.Stage <> Exploiting ->
            let target =
                match episode.Stage, episode.Pattern with
                | Preparing, ExploitDepth -> episode.Runner
                | Preparing, _ -> episode.Link
                | _ -> episode.Runner

            if episode.Owner = p.Id then
                let key = CollectiveAttackState.key episode.Context episode.Pattern
                let estimate = team.Attack.Estimates |> Map.tryFind key

                let mean =
                    estimate
                    |> Option.map FootballEngine.Domain.AttackEstimate.mean
                    |> Option.defaultValue 0.0

                let confidence = MatchMath.clamp01 ((1.0 + mean) / 2.0)

                let bias =
                    ctx.Config.CollectiveAttack.CooperationWeight
                    * confidence
                    * cognitive
                    * float frame.Condition[slot]
                    * float p.Teamwork
                    / 100.0

                // Bias only the existing, interceptor-filtered option; collective intent never creates an action.
                for index in 0 .. options.Count - 1 do
                    if options.Code[index] = 1 && options.Target[index] = target.Slot then
                        if world.Observation.WantsDetail(tick, p.Id) then
                            world.Observation.Record(
                                tick,
                                ValueSome(
                                    EventCause(
                                        MatchEvent.id
                                            { Tick = episode.StartedAt
                                              Data =
                                                EventData.CollectiveAttack(side, episode.Id, Started episode.Pattern) }
                                    )
                                ),
                                AttackCooperation(p.Id, episode.Id, target.Slot, bias)
                            )
                            |> ignore

                        options.Utility[index] <- options.Utility[index] + ctx.Config.ExpectedValue.StyleValue * bias
        | _ -> ()

        plan.Cognitive <- cognitive
        plan.Pressure <- pressure
        Planner.projectInto plan.Root ctx world side slot

    let internal decisionInputs
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (tick: int<subtick>)
        : DecisionInputs =
        let plan = PlanSlot()
        fillPlan ctx world side slot tick plan
        let options = plan.Options

        { Plan = plan
          Chain = Planner.project ctx world side slot
          PlannerOptions = Array.init options.Count (fun i -> options.Code[i], options.Target[i], options.Utility[i])
          Intercept = PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball }

    /// Chooses from the search when it is due: the world and tick are those of that moment; the options, result,
    /// cognition and pressure were settled when the decision began, and only the observation reads the frame live.
    let private finish (ctx: MatchContext) (plan: PlanSlot) (world: World) (tick: int<subtick>) (rng: Rng) : Rng =
        let side = plan.Side
        let slot = plan.Slot
        let frame = (MatchWorld.team world side).Frame
        let cognitive = plan.Cognitive
        let options = plan.Options
        let result = plan.Result
        let evaluations = result.Count
        let d, rng' = Rng.double rng

        let mutable chosen = 3
        let mutable targetSlot = -1
        let mutable chosenEvaluationIndex = -1

        if options.Count > 0 then
            // R5 — a cognitively tired carrier decides with a hotter (more
            // random, less discerning) softmax.
            let effTemp = effectiveTemperature ctx.Config cognitive
            let mutable maximum = result.Value[0]

            for index in 1 .. evaluations - 1 do
                if result.Value[index] > maximum then
                    maximum <- result.Value[index]

            let mutable sum = 0.0

            for index = 0 to evaluations - 1 do
                sum <- sum + exp ((result.Value[index] - maximum) / effTemp)

            let mutable acc = 0.0
            let mutable found = false
            let mutable idx = 0

            while not found && idx < evaluations do
                acc <- acc + exp ((result.Value[idx] - maximum) / effTemp) / sum

                if d <= acc then
                    chosen <- result.ActionCode[idx]
                    targetSlot <- result.TargetSlot[idx]
                    chosenEvaluationIndex <- idx
                    found <- true

                idx <- idx + 1

            // Rounding can leave the cumulative sum a hair under 1: the last option takes the remainder.
            if not found then
                chosen <- result.ActionCode[evaluations - 1]
                targetSlot <- result.TargetSlot[evaluations - 1]
                chosenEvaluationIndex <- evaluations - 1

        let choice =
            match chosen with
            | 1 -> DeliverPass targetSlot
            | 2 -> TakeShot
            | 3 -> ContestDuel
            | 4 -> ClearBall
            | _ -> DeliverCross

        (MatchWorld.team world side).Proposals[ slot ] <-
            { Choice = choice
              DecidedAt = tick
              ExpiresAt = tick + ctx.Config.Movement.CommittedRunDuration }

        if world.Observation.Enabled then
            let chosenValue =
                if chosenEvaluationIndex >= 0 then
                    result.Value[chosenEvaluationIndex]
                else
                    0.0

            world.Observation.Decision(
                tick,
                { Actor = plan.Actor
                  ActionCode = chosen
                  TargetSlot = targetSlot
                  Condition = frame.Condition[slot]
                  Cognitive = frame.Cognitive[slot]
                  Pressure = plan.Pressure
                  OptionCount = options.Count
                  ChosenValue = chosenValue
                  Temperature = effectiveTemperature ctx.Config cognitive
                  Sample = d }
            )

            if world.Observation.WantsDetail(tick, plan.Actor) then
                let alternatives =
                    Array.init evaluations (fun index ->
                        { ActionCode = result.ActionCode[index]
                          TargetSlot = result.TargetSlot[index]
                          Value = result.Value[index] })

                world.Observation.Alternatives(tick, plan.Actor, alternatives)

        rng'

    /// The search over a slot, built once per match and handed to the planner's thread each decision.
    let private searchFor (ctx: MatchContext) (plan: PlanSlot) : unit -> unit =
        let pparams =
            PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball

        fun () -> Planner.searchInto ctx.Config pparams plan

    let choose (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (tick: int<subtick>) (rng: Rng) : Rng =
        let plan = world.Cognition.Plan
        fillPlan ctx world side slot tick plan
        plan.Side <- side
        plan.Slot <- slot
        plan.Actor <- (MatchWorld.profileOf ctx side slot).Id

        Planner.searchInto
            ctx.Config
            (PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball)
            plan

        finish ctx plan world tick rng

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick
            let plan = world.Cognition.Plan

            match plan.Active, world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
            | false, None, ValueSome(struct (side, slot, _)) when
                (MatchWorld.profileOf ctx.Ctx side slot).Role <> SlotRole.Goalkeeper
                ->
                if ctx.Ctx.Config.Planner.DeliberationTime = 0<subtick> then
                    choose ctx.Ctx world side slot tick rng
                else
                    fillPlan ctx.Ctx world side slot tick plan
                    plan.Due <- tick + ctx.Ctx.Config.Planner.DeliberationTime
                    plan.Side <- side
                    plan.Slot <- slot
                    plan.Actor <- (MatchWorld.profileOf ctx.Ctx side slot).Id
                    plan.Active <- true

                    let search =
                        match plan.Search with
                        | ValueSome search -> search
                        | ValueNone ->
                            let search = searchFor ctx.Ctx plan
                            plan.Search <- ValueSome search
                            search

                    match world.Cognition.Planner with
                    | ValueSome planner -> planner.Start search
                    | ValueNone -> search ()

                    rng
            | _ -> rng

    /// Ends a carrier's deliberation when it is due. The choice stands only if the same player still carries the
    /// ball in open play; otherwise the moment it was for has passed.
    let deliberate: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick
            let plan = world.Cognition.Plan

            if plan.Active && tick >= plan.Due then
                plan.Active <- false
                world.Cognition.Planner |> ValueOption.iter _.Join()

                match world.State.Flow, world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
                | MatchFlow.Live, None, ValueSome(struct (side, slot, carrier)) when
                    side = plan.Side && slot = plan.Slot && carrier = plan.Actor
                    ->
                    finish ctx.Ctx plan world tick rng
                | _ -> rng
            else
                rng

    /// A carrier the body has no action for, closed down within reach, decides at once instead of waiting for
    /// the decision cadence. It reads the body's pending actions as the latch left them, never live.
    let reflex: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            match world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
            | None, ValueSome(struct (side, slot, _)) when slot <> 0 && not world.Cognition.Plan.Active ->
                let team = MatchWorld.team world side
                let frame = team.Frame

                let idle =
                    team.Latched.PendingSlots &&& (1UL <<< slot) = 0UL
                    && not (team.Proposals.ContainsKey slot)

                let pressed =
                    MatchWorld.nearestOpponent
                        ctx.Ctx
                        world
                        side
                        (MatchWorld.posX frame slot)
                        (MatchWorld.posY frame slot)
                        (float ctx.Ctx.Config.Actions.ReflexDecisionRadius)
                    |> Option.isSome

                if idle && pressed then
                    choose ctx.Ctx world side slot tick rng
                else
                    rng
            | _ -> rng
