namespace FootballEngine.Core.Match

/// The carrier's decision: build a small option set (shot/pass/cross/dribble/
/// clear) with utility weights, gate by thresholds, pick by softmax, and leave
/// a marker (Phase 200) the Actions controller executes. Deterministic: all
/// randomness comes from the threaded Rng.
module PlayerDecision =

    /// xG preview shared with the shot system (same formula as Shot.launch).
    let xGValue (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : float =
        let p = MatchWorld.profileOf ctx side slot

        let x, y =
            MatchWorld.posX (MatchWorld.team world side).Frame slot,
            MatchWorld.posY (MatchWorld.team world side).Frame slot

        let d = MatchMath.distanceToGoal side x y
        let near = MatchWorld.opponentsNear ctx world side x y 6.0
        let openness = 1.0 - 0.3 * MatchMath.clamp01 (float near / 4.0)
        exp (-0.07 * d - 0.9) * (0.6 + float p.Finishing / 150.0) * openness

    let private offsideRisk (ctx: MatchContext) (world: World) (side: ClubSide) (receiverSlot: int) : bool =
        let rx = MatchWorld.posX (MatchWorld.team world side).Frame receiverSlot
        let line = MatchWorld.offsideLine ctx world side
        let dir = MatchMath.goalDirection side

        if dir > 0.0 then
            float rx > float line
        else
            float rx < float line

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
    /// Pass penalty in [0, ~0.4]: grows as the carrier keeps failing passes.
    let passMemoryPenalty (cfg: MatchContext) (mem: PlayerMemory) : float =
        if mem.PassFailMod > 0.25 then
            cfg.Config.Actions.MemoryPassFailWeight * mem.PassFailMod
        else
            0.0

    /// Duel bias: successful duel memory makes dribbling more attractive.
    let duelMemoryBias (cfg: MatchContext) (mem: PlayerMemory) : float =
        cfg.Config.Actions.MemoryDuelWeight * (mem.DuelSuccessMod - mem.DuelFailMod)

    /// Shot bias: a goal-less run (negative ShotSuccessMod) cools shooting.
    let shotMemoryBias (cfg: MatchContext) (mem: PlayerMemory) : float =
        cfg.Config.Actions.MemoryPassFailWeight
        * (if mem.ShotSuccessMod < 0.0 then mem.ShotSuccessMod else 0.0)

    // R5 — effective decision temperature (hotter when cognitively tired).
    let effectiveTemperature (cfg: BalanceConfig) (cognitive: float) : float =
        let baseT = cfg.Weights.Decision.DecisionTemperature
        max 0.05 (baseT * (0.4 + 0.6 * cognitive))

    /// Pass options for the carrier: every active teammate with a utility above
    /// the pass threshold whose pass is feasible under the interceptor model.
    /// Shared by `choose` and the test suite.
    let passOptionsFor (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : (float * int * float) list =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let w = ctx.Config.Weights.Decision
        let p = MatchWorld.profileOf ctx side slot
        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let cognitive = float frame.Cognitive.[slot]
        let mem = team.Memory.[slot]
        let passMemoryMod = passMemoryPenalty ctx mem
        let pressure = MatchWorld.opponentsNear ctx world side x y 4.0

        let attacking =
            team.Blackboard.OurPhase = PhaseKind.Attacking
            || team.Blackboard.OurPhase = PhaseKind.TransitionAttack

        let passing = float p.Passing
        let vision = float p.Vision
        let launchSpeed = ctx.Config.Actions.PassLaunchSpeed

        let mutable options: (float * int * float) list = []

        for s2 in MatchWorld.activeSlots frame do
            if s2 <> slot && s2 <> 0 then
                let rx = MatchWorld.posX frame s2
                let ry = MatchWorld.posY frame s2
                let d = MatchMath.dist x y rx ry
                let space = spaceAt ctx world side rx ry
                let longBall = d > float ctx.Config.Actions.PassLongThreshold

                let offside = offsideRisk ctx world side s2

                // R3 — a receiver that can't see the carrier, or a blocked lane,
                // is not a target.
                let laneOk = frame.LaneClear.[s2] && frame.FaceCone.[s2]

                // F4 — the launched pass must beat the fastest interceptor.
                let pparams =
                    PassIntercept.passParams ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball

                let dM = max d 0.5 * 1.0<meter>

                let launched =
                    { X = world.State.Ball.Position.X
                      Y = world.State.Ball.Position.Y
                      Z = 0.0<meter>
                      Vx = (rx - x) / dM * launchSpeed
                      Vy = (ry - y) / dM * launchSpeed
                      Vz = 0.0<meterPerSecond> }

                if PassIntercept.feasible ctx.Config.PassIntercept ctx pparams world side s2 launched then
                    let uPass =
                        w.PassBaseUtility
                        + w.PassPassingWeight * ((passing - 60.0) / 40.0)
                        + w.PassVisionWeight * ((vision - 60.0) / 40.0)
                        + w.PassTargetBonus * space
                        + w.PassAnticipationBonus * ((float p.Anticipation - 60.0) / 50.0)
                        + w.PassTrajectoryBonus * (if longBall then 0.0 else 0.15)
                        + (if attacking then 0.0 else w.PassAttackPhasePenalty)
                        - w.PassHighPressPenalty * float pressure * w.PassHighPressFactor
                        - (if offside then 0.8 else 0.0)
                        + w.PersonalityTeamworkWeight * ((float p.Teamwork - 60.0) / 100.0)
                        + w.PassConcentrationMod * (cognitive - 0.5)
                        - passMemoryMod
                        - (if laneOk then 0.0 else 0.6)
                        + w.PersonalityConsistencyMod * (float frame.Cognitive.[s2] - 0.5)

                    if uPass >= w.PassMinThreshold then
                        options <- (uPass, 1, float s2) :: options

        options

    let choose (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (tick: int<subtick>) (rng: Rng) : Rng =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let w = ctx.Config.Weights.Decision
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

        let mutable options: (float * int * float) list = []

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
                + w.ShootFinishingWeight * ((fin - 60.0) / 40.0)
                + w.ShootComposureWeight * ((comp - 60.0) / 40.0)
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

            if uShot >= w.ShootMinThreshold - w.ShootDirectnessThresholdMod * 0.5 then
                options <- (uShot, 2, -1.0) :: options

        let passOpts = passOptionsFor ctx world side slot
        options <- passOpts @ options

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
                options <- (uCross, 5, -1.0) :: options

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
            - duelMemoryBias ctx mem * 0.5

        if inOwnThird && pressure > 0 then
            let uClear = 0.55 + 0.45 * MatchMath.clamp01 (float pressure / 3.0)
            options <- (uClear, 4, -1.0) :: options

        if uDribble >= w.DribbleMinThreshold && pressure <= 1 then
            options <- (uDribble, 3, -1.0) :: options

        let chosen, targetSlot =
            match options with
            | [] -> 3, -1.0
            | _ ->
                // R5 — a cognitively tired carrier decides with a hotter (more
                // random, less discerning) softmax.
                let effTemp = effectiveTemperature ctx.Config cognitive
                let exps = options |> List.map (fun (u, _, _) -> exp (u / effTemp))
                let sum = List.sum exps
                let d, rng' = Rng.double rng
                let mutable acc = 0.0
                let mutable picked = 3
                let mutable pickedTarget = -1.0
                let mutable found = false
                let mutable remaining = options

                while not found && not remaining.IsEmpty do
                    let (u, code, tgt) = remaining.Head
                    acc <- acc + exp (u / effTemp) / sum

                    if d <= acc then
                        picked <- code
                        pickedTarget <- tgt
                        found <- true

                    remaining <- remaining.Tail

                if not found then
                    let (_, code, tgt) = options |> List.last
                    picked <- code
                    pickedTarget <- tgt

                picked, pickedTarget

        frame.RunLookup.[slot] <-
            { Slot = slot
              TargetX = float32 targetSlot
              TargetY = float32 chosen
              Phase = 200
              ExpiresAt = tick + 160<subtick> }

        frame.Intent.Kind.[slot] <- IntentCode.BallCarry
        frame.Intent.TargetX.[slot] <- float32 (MatchMath.goalX side)
        frame.Intent.Phase.[slot] <- IntentPhase.Committed
        frame.Intent.CommittedUntil.[slot] <- tick + 480<subtick>

        rng

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            match MatchWorld.ballCarrier ctx.Ctx world with
            | Some(side, slot, _) when slot <> 0 ->
                let mutable r = rng
                r <- choose ctx.Ctx world side slot tick r
                r
            | _ -> rng
