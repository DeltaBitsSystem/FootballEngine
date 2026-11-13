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
        let openness = 1.0 - 0.3 * MatchMath.clamp01 (float near / 4.0)
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
            p.Pace + p.Strength + p.Stamina + p.Agility + p.Vision + p.Passing + p.Finishing + p.Crossing
            + p.Dribbling + p.Tackling + p.Marking + p.Positioning + p.Anticipation + p.Composure + p.Teamwork
            + p.DecisionMaking
        )
        / 16.0

    // R5 — effective decision temperature (hotter when cognitively tired).
    let effectiveTemperature (cfg: BalanceConfig) (cognitive: float) : float =
        let baseT = cfg.Weights.Decision.DecisionTemperature
        max 0.05 (baseT * (0.4 + 0.6 * cognitive))

    /// The pass option set, filtered by feasibility so the softmax can never
    /// pick a delivery the interceptor model would reject. Shared by `choose`
    /// and the test suite so pass selection is verifiable in isolation.
    let passOptionsFor (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : (float * int * float) list =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let w = ctx.Config.Weights.Decision

        let tactics = team.Tactics

        let tradeoffs = ctx.Config.TacticalTradeoffs
        let p = MatchWorld.profileOf ctx side slot
        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let cognitive = float frame.Cognitive.[slot]
        let mem = team.Memory.[slot]
        let passMemoryMod = passMemoryPenalty ctx mem
        let pressure = MatchWorld.opponentsNear ctx world side x y 4.0
        let level = ownLevel p

        let attacking =
            team.Blackboard.OurPhase = PhaseKind.Attacking
            || team.Blackboard.OurPhase = PhaseKind.TransitionAttack

        let passing = float p.Passing
        let vision = float p.Vision
        // Hoisted out of the per-candidate loop: the pass model and the offside
        // line do not depend on the receiver, so recomputing them for every
        // candidate would multiply constant work by the team size.
        let pparams =
            PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball

        let offsideLine = MatchWorld.offsideLine ctx world side

        let mutable options: (float * int * float) list = []

        for s2 in MatchWorld.activeSlots frame do
            if s2 <> slot && s2 <> 0 then
                let rx = MatchWorld.posX frame s2
                let ry = MatchWorld.posY frame s2
                let d = MatchMath.dist x y rx ry
                let space = spaceAt ctx world side rx ry
                let longBall = d > float ctx.Config.Actions.PassLongThreshold

                let offside = offsideRiskFromLine offsideLine side rx

                // R3 — a receiver that can't see the carrier, or a blocked lane,
                // is not a target.
                let laneOk = frame.LaneClear.[s2] && frame.FaceCone.[s2]

                let uPass =
                    w.PassBaseUtility
                    + w.PassPassingWeight * ((passing - level) / 40.0)
                    + w.PassVisionWeight * ((vision - level) / 40.0)
                    + w.PassTargetBonus * space
                    + w.PassAnticipationBonus * ((float p.Anticipation - level) / 50.0)
                    + w.PassTrajectoryBonus * (if longBall then 0.0 else 0.15)
                    + (if longBall then
                           float tactics.Directness * tradeoffs.DirectnessLongBallUtility
                       else
                           0.0)
                    + (if attacking then 0.0 else w.PassAttackPhasePenalty)
                    - w.PassHighPressPenalty * float pressure * w.PassHighPressFactor
                    - (if offside then 0.8 else 0.0)
                    + w.PersonalityTeamworkWeight * ((float p.Teamwork - level) / 100.0)
                    + w.PassConcentrationMod * (cognitive - 0.5)
                    - passMemoryMod
                    - (if laneOk then 0.0 else 0.6)
                    + w.PersonalityConsistencyMod * (float frame.Cognitive.[s2] - 0.5)

                // The interceptor check simulates every defender, so it only runs for receivers the cheap utility
                // already rates above the bar; the option set is the same either way.
                if uPass >= w.PassMinThreshold then
                    // F4 — the launched pass must beat the fastest interceptor.
                    let dM = max d 0.5 * 1.0<meter>

                    let launchSpeed =
                        (BallAero.pass ctx.Config.Ball ctx.Config.Actions.PassArrivalSpeed ctx.Config.Actions.PassArrivalSpeedGain d)
                            .LaunchSpeed
                        * 1.0<meterPerSecond>

                    let launched =
                        { X = world.State.Ball.Position.X
                          Y = world.State.Ball.Position.Y
                          Z = 0.0<meter>
                          Vx = (rx - x) / dM * launchSpeed
                          Vy = (ry - y) / dM * launchSpeed
                          Vz = 0.0<meterPerSecond> }

                    if PassIntercept.feasible ctx.Config.PassIntercept pparams world side s2 launched offsideLine then
                        options <- (uPass, 1, float s2) :: options

        options

    let prepare (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (tick: int<subtick>) : PendingPlan =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let w = ctx.Config.Weights.Decision
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

        let options = ResizeArray<float * int * float>()
        let mutable shotOption: (float * int * float) option = None

        let xg = xGValue ctx world side slot

        if distToGoal < 32.0 then
            let distNorm = 1.0 - distToGoal / float w.ShootDistNormDivisor

            let distPenalty =
                if distToGoal > 25.0 then
                    w.ShootDistPenaltyMax * (distToGoal - 25.0) / float w.ShootDistPenaltyDivisor
                else
                    0.0

            let uShot =
                w.ShotBaseUtility
                + w.ShootFinishingWeight * ((fin - level) / 40.0)
                + w.ShootComposureWeight * ((comp - level) / 40.0)
                + w.ShootDistNormWeight * distNorm
                + w.ShootXGWeight * xg * w.ShotXGMultiplier
                + (if attacking then
                       w.ShotDepthBonusAttacking
                   else
                       w.ShotDepthBonusDefending)
                - distPenalty
                + w.ShootBraveryMod * (bravery - 0.5)
                + w.PersonalityFlairWeight * ((float p.Flair - 60.0) / 100.0)
                + shotMemoryBias ctx mem

            if uShot >= w.ShootMinThreshold - w.ShootDirectnessThresholdMod * 0.5 - riskShift then
                shotOption <- Some(uShot, 2, -1.0)

        let passOpts = passOptionsFor ctx world side slot

        for passOpt in passOpts do
            options.Add passOpt

        shotOption |> Option.iter options.Add

        if attacking && abs (float y) > 20.0 then
            // R9 — adaptive channels: wing play bias boosts crosses, center play
            // boosts through passes.
            let channelBias =
                ctx.Config.Team.PatternWingBias
                * (float team.Emergent.ChannelLeft + float team.Emergent.ChannelRight)
                - ctx.Config.Team.PatternCenterBias * float team.Emergent.ChannelCenter

            let uCross =
                w.CrossBaseUtility * (1.0 + channelBias)
                + w.CrossCrossingWeight * (crossing / 100.0) * 0.25
                + w.CrossLateralTendencyBase
                + w.CrossZoneBonus * (if abs (float y) > 25.0 then 1.0 else 0.3)
                + w.CrossWidthBonus * float team.Directive.Params.Width

            if uCross >= 0.25 then
                options.Insert(0, (uCross, 5, -1.0))

        let uDribble =
            w.DribbleBaseUtility
            + float ctx.Config.Actions.DribbleBaseSuccess * 0.3
            + (if attacking then
                   w.DribbleZoneBonusAttacking * w.DribbleZoneAttackingMult
               else if float x * dir > -5.0 then
                   w.DribbleZoneBonusMidfield
               else
                   0.0)
            + (if attacking then
                   w.DribbleAttackPhaseBonus * w.DribblePhaseBonusMult
               else
                   0.0)
            - w.DribblePressurePenalty * float pressure
            - w.DribbleTempoPenalty
              * float (0.5 - float team.Directive.Params.LineHeight / 100.0 |> max 0.0)
            + w.DribbleBraveryMod * (bravery - 0.5)
            + w.DribbleDribblingWeight * ((float p.Dribbling - level) / 40.0)
            - duelMemoryBias ctx mem * 0.5

        if inOwnThird && pressure > 0 then
            let uClear = 0.55 + 0.45 * MatchMath.clamp01 (float pressure / 3.0)
            options.Insert(0, (uClear, 4, -1.0))

        if uDribble >= w.DribbleMinThreshold - riskShift && pressure <= 1 then
            options.Insert(0, (uDribble, 3, -1.0))

        let options = options.ToArray()

        let options =
            match team.Attack.Episode with
            | Some episode when episode.Stage <> Exploiting ->
                let target =
                    match episode.Stage, episode.Pattern with
                    | Preparing, ExploitDepth -> episode.Runner
                    | Preparing, _ -> episode.Link
                    | _ -> episode.Runner
                let actorExpected = episode.Owner
                if actorExpected <> p.Id then options
                else
                    let key = CollectiveAttackState.key episode.Context episode.Pattern
                    let estimate = team.Attack.Estimates |> Map.tryFind key
                    let mean = estimate |> Option.map FootballEngine.Domain.AttackEstimate.mean |> Option.defaultValue 0.0
                    let confidence = MatchMath.clamp01 ((1.0 + mean) / 2.0)
                    let bias = ctx.Config.CollectiveAttack.CooperationWeight * confidence * cognitive * float frame.Condition[slot] * float p.Teamwork / 100.0
                    // Bias only the existing, interceptor-filtered option; collective intent never creates an action.
                    options |> Array.map (fun (utility, code, receiver) ->
                        if code = 1 && int receiver = target.Slot then
                            if world.Observation.WantsDetail(tick, p.Id) then
                                world.Observation.Record(tick, ValueSome(EventCause(MatchEvent.id { Tick = episode.StartedAt; Data = EventData.CollectiveAttack(side, episode.Id, Started episode.Pattern) })), AttackCooperation(p.Id, episode.Id, target.Slot, bias)) |> ignore
                            utility + bias, code, receiver
                        else utility, code, receiver)
            | _ -> options

        let chainState = Planner.project ctx world side slot
        let plannerOpts = options |> Array.map (fun (u, code, tgt) -> (code, int tgt, u))

        let pparams =
            PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball

        let found = ref [||]

        let search () =
            found.Value <- Planner.search ctx.Config.Planner ctx.Config.Weights.Planner pparams chainState plannerOpts

        // Chooses from the search when it is due: the world and tick are those of that moment, everything else
        // was settled when the decision began.
        let finish (world: World) (tick: int<subtick>) (rng: Rng) : Rng =
            let chainEvals = found.Value
            let d, rng' = Rng.double rng

            let chosen, targetSlot, chosenEvaluationIndex =
                match options with
                | [||] -> 3, -1.0, -1
                | _ ->
                    // R5 — a cognitively tired carrier decides with a hotter (more
                    // random, less discerning) softmax.
                    let effTemp = effectiveTemperature ctx.Config cognitive

                    let mutable sum = 0.0
                    let maximum =
                        if chainEvals.Length > 0 then chainEvals |> Array.maxBy _.Value |> _.Value
                        else options |> Array.maxBy (fun (utility, _, _) -> utility) |> fun (utility, _, _) -> utility

                    if chainEvals.Length > 0 then
                        for index = 0 to chainEvals.Length - 1 do
                            sum <- sum + exp ((chainEvals.[index].Value - maximum) / effTemp)
                    else
                        for index = 0 to options.Length - 1 do
                            let utility, _, _ = options.[index]
                            sum <- sum + exp ((utility - maximum) / effTemp)

                    let mutable acc = 0.0
                    let mutable picked = 3
                    let mutable pickedTarget = -1.0
                    let mutable pickedEvaluationIndex = -1
                    let mutable found = false
                    let mutable idx = 0

                    if chainEvals.Length > 0 then
                        while not found && idx < chainEvals.Length do
                            let e = chainEvals[idx]
                            acc <- acc + exp ((e.Value - maximum) / effTemp) / sum

                            if d <= acc then
                                picked <- e.ActionCode
                                pickedTarget <- float e.TargetSlot
                                pickedEvaluationIndex <- idx
                                found <- true

                            idx <- idx + 1
                    else
                        while not found && idx < options.Length do
                            let (u, code, tgt) = options[idx]
                            acc <- acc + exp ((u - maximum) / effTemp) / sum

                            if d <= acc then
                                picked <- code
                                pickedTarget <- tgt
                                found <- true

                            idx <- idx + 1

                    if not found then
                        if chainEvals.Length > 0 then
                            let last = chainEvals[chainEvals.Length - 1]
                            picked <- last.ActionCode
                            pickedTarget <- float last.TargetSlot
                            pickedEvaluationIndex <- chainEvals.Length - 1
                        else
                            let (_, code, tgt) = options[options.Length - 1]
                            picked <- code
                            pickedTarget <- tgt

                    picked, pickedTarget, pickedEvaluationIndex

            let choice =
                match chosen with
                | 1 -> DeliverPass(int targetSlot)
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
                        chainEvals[chosenEvaluationIndex].Value
                    else
                        options
                        |> Array.tryPick (fun (value, code, target) ->
                            if code = chosen && target = targetSlot then
                                Some value
                            else
                                None)
                        |> Option.defaultValue 0.0

                world.Observation.Decision(
                    tick,
                    { Actor = p.Id
                      ActionCode = chosen
                      TargetSlot = int targetSlot
                      Condition = frame.Condition[slot]
                      Cognitive = frame.Cognitive[slot]
                      Pressure = pressure
                      OptionCount = options.Length
                      ChosenValue = chosenValue
                      Temperature = effectiveTemperature ctx.Config cognitive
                      Sample = d }
                )

                if world.Observation.WantsDetail(tick, p.Id) then
                    let alternatives =
                        if chainEvals.Length > 0 then
                            chainEvals
                            |> Array.map (fun evaluation ->
                                { ActionCode = evaluation.ActionCode
                                  TargetSlot = evaluation.TargetSlot
                                  Value = evaluation.Value })
                        else
                            options
                            |> Array.map (fun (value, code, target) ->
                                { ActionCode = code
                                  TargetSlot = int target
                                  Value = value })

                    world.Observation.Alternatives(tick, p.Id, alternatives)

            rng'

        { PlanDue = tick + ctx.Config.Planner.DeliberationTime
          PlanSide = side
          PlanSlot = slot
          PlanActor = p.Id
          PlanSearch = search
          PlanFinish = finish }

    let choose (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (tick: int<subtick>) (rng: Rng) : Rng =
        let plan = prepare ctx world side slot tick
        plan.PlanSearch()
        plan.PlanFinish world tick rng

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            match world.Cognition.Plan, world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
            | ValueNone, None, Some(side, slot, _) when (MatchWorld.profileOf ctx.Ctx side slot).Role <> SlotRole.Goalkeeper ->
                if ctx.Ctx.Config.Planner.DeliberationTime = 0<subtick> then
                    choose ctx.Ctx world side slot tick rng
                else
                    let plan = prepare ctx.Ctx world side slot tick
                    world.Cognition.Plan <- ValueSome plan

                    match world.Cognition.Planner with
                    | ValueSome planner -> planner.Start plan.PlanSearch
                    | ValueNone -> plan.PlanSearch()

                    rng
            | _ -> rng

    /// Ends a carrier's deliberation when it is due. The choice stands only if the same player still carries the
    /// ball in open play; otherwise the moment it was for has passed.
    let deliberate: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            match world.Cognition.Plan with
            | ValueSome plan when tick >= plan.PlanDue ->
                world.Cognition.Plan <- ValueNone
                world.Cognition.Planner |> ValueOption.iter _.Join()

                match world.State.Flow, world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
                | MatchFlow.Live, None, Some(side, slot, carrier) when
                    side = plan.PlanSide && slot = plan.PlanSlot && carrier = plan.PlanActor
                    ->
                    plan.PlanFinish world tick rng
                | _ -> rng
            | _ -> rng

    /// A carrier the body has no action for, closed down within reach, decides at once instead of waiting for
    /// the decision cadence. It reads the body's pending actions as the latch left them, never live.
    let reflex: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            match world.State.Ball.PendingStrike, MatchWorld.ballCarrier ctx.Ctx world with
            | None, Some(side, slot, _) when slot <> 0 && world.Cognition.Plan.IsNone ->
                let team = MatchWorld.team world side
                let frame = team.Frame
                let idle = team.Latched.PendingSlots &&& (1UL <<< slot) = 0UL && not (team.Proposals.ContainsKey slot)

                let pressed =
                    MatchWorld.nearestOpponent
                        ctx.Ctx
                        world
                        side
                        (MatchWorld.posX frame slot)
                        (MatchWorld.posY frame slot)
                        (float ctx.Ctx.Config.Actions.ReflexDecisionRadius)
                    |> Option.isSome

                if idle && pressed
                then
                    choose ctx.Ctx world side slot tick rng
                else
                    rng
            | _ -> rng
