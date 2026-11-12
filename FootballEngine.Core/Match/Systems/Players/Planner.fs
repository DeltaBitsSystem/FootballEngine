namespace FootballEngine.Core.Match

/// Chain state used by the planner and tests. Defined at namespace level
/// so tests can reference it bare after `open FootballEngine.Core.Match`.
type ChainState =
    { BallX: float<meter>
      BallY: float<meter>
      CarrierSlot: int
      Side: ClubSide
      OurPos: struct (int * float<meter> * float<meter>)[]
      OppPos: struct (int * float<meter> * float<meter>)[]
      OffsideLine: float<meter>
      Dir: float
      OurMotion: struct (int * float<meterPerSecond> * float<meterPerSecond>)[]
      OppMotion: struct (int * float<meterPerSecond> * float<meterPerSecond>)[]
      Horizon: float<second>
      Confidence: float
      /// Bit per slot; the search tests membership for every projected player, which a tree set made costly.
      GoalkeeperSlots: uint64
      OurGoalkeeperSlots: uint64
      PassBall: BallConfig
      PassArrivalSpeed: float<meterPerSecond>
      PassArrivalSpeedGain: float
      MaxPassDistance: float<meter>
      CarrySpeed: float<meterPerSecond> }

/// Slot sets as one word each: a lineup has far fewer than 64 slots.
module SlotMask =
    let inline contains (slot: int) (mask: uint64) = (mask >>> slot) &&& 1UL = 1UL
    let inline add (slot: int) (mask: uint64) = mask ||| (1UL <<< slot)
    let ofSeq (slots: int seq) = Seq.fold (fun mask slot -> add slot mask) 0UL slots

/// Action-chain planner: given the level-1 options from Decision, simulate
/// multi-step continuations (pass → pass → shoot, etc.) and score each chain
/// so the carrier picks the option that opens the best future. Pure and
/// deterministic — no mutable state, no RNG — same ChainState always produces
/// the same evaluations.
module Planner =

    type ChainAction =
        | Pass of receiverSlot: int * x: float<meter> * y: float<meter>
        | Shoot
        | Dribble
        | Cross of receiverSlot: int * x: float<meter> * y: float<meter>
        | Clear

    type ChainEvaluation =
        { ActionCode: int
          TargetSlot: int
          Chain: ChainAction[]
          Value: float
          CandidateEvaluations: int }

    /// Value a chain state: progression + shoot bonus + danger penalty +
    /// possession security, all on a 0–1-ish scale matching single-step
    /// utilities. The shooting belief reuses the engine-wide xG formula with a
    /// neutral finishing assumption — the plan evaluates where the ball should
    /// go; the option utility already carries the shooter's actual ability.
    let value (w: WeightSectionsPlanner) (s: ChainState) : float =
        let pitchHalf = float Pitch.HalfLength

        let progression =
            w.ProgressionWeight * (float s.BallX * s.Dir + pitchHalf) / (2.0 * pitchHalf)

        let shootBonus =
            let d = MatchMath.distanceToGoal s.Side s.BallX s.BallY

            if d < 32.0 then
                w.ShootValueWeight * MatchMath.xGFrom d w.ShootFinishingAssumption 1.0
            else
                0.0

        let mutable dangerPenalty = 0.0

        for struct (_, ox, oy) in s.OppPos do
            let distRival = MatchMath.dist s.BallX s.BallY ox oy

            if distRival < 30.0 then
                let idx = min (int (distRival / 5.0)) (w.DangerBase.Length - 1) |> max 0

                let zone =
                    if float s.BallX * s.Dir < -20.0 then 0
                    elif float s.BallX * s.Dir < 20.0 then 1
                    else 2

                let zoneIdx = min zone (w.DangerZoneWeights.Length - 1) |> max 0
                dangerPenalty <- dangerPenalty - w.DangerBase.[idx] * w.DangerZoneWeights.[zoneIdx]

        let mutable hasTeammateNear = false

        for struct (slot, tx, ty) in s.OurPos do
            if slot <> s.CarrierSlot && not hasTeammateNear && MatchMath.dist s.BallX s.BallY tx ty < 10.0 then
                hasTeammateNear <- true

        let possessionSecurity =
            w.PossessionSecurityWeight * (if hasTeammateNear then 1.0 else 0.0)

        progression + shootBonus + dangerPenalty + possessionSecurity

    let private advance (s: ChainState) (flight: float<second>) =
        let advance (motions: struct (int * float<meterPerSecond> * float<meterPerSecond>)[]) positions =
            positions
            |> Array.map (fun struct (slot, x, y) ->
                let mutable index = 0
                let mutable found = false
                let mutable px = x
                let mutable py = y

                // Branch states still own their arrays; scanning avoids one heap Option per projected player.
                while index < motions.Length && not found do
                    let struct (movingSlot, vx, vy) = motions[index]

                    if movingSlot = slot then
                        let x, y = MatchMath.pitchClamp (float (x + vx * flight)) (float (y + vy * flight))
                        px <- x * 1.0<meter>
                        py <- y * 1.0<meter>
                        found <- true

                    index <- index + 1

                struct (slot, px, py))

        let ours, opponents = advance s.OurMotion s.OurPos, advance s.OppMotion s.OppPos

        let line =
            if s.OppMotion.Length = 0 then
                s.OffsideLine
            else
                let mutable count = 0
                let mutable first = 0.0<meter>
                let mutable second = 0.0<meter>

                for struct (slot, x, _) in opponents do
                    if not (SlotMask.contains slot s.GoalkeeperSlots) then
                        let position = x * s.Dir

                        if count = 0 || compare position first > 0 then
                            second <- first
                            first <- position
                        elif count = 1 || compare position second > 0 then
                            second <- position

                        count <- count + 1

                if count >= 2 then
                    second * s.Dir
                else
                    MatchMath.goalX s.Side

        { s with
            OurPos = ours
            OppPos = opponents
            OffsideLine = line
            Horizon = max 0.0<second> (s.Horizon - flight) }

    let private passOf (s: ChainState) distance =
        BallAero.pass s.PassBall s.PassArrivalSpeed s.PassArrivalSpeedGain (float distance)

    let private simulatePassWith (s: ChainState) (roll: BallAero.GroundPass) (receiverSlot: int) : ChainState =
        let flight = min s.Horizon (roll.Seconds * 1.0<second>)
        let next = advance s flight
        let struct(_, x, y) = next.OurPos |> Array.find (fun struct(slot, _, _) -> slot = receiverSlot)
        { next with BallX = x; BallY = y; CarrierSlot = receiverSlot }

    let simulatePass (s: ChainState) (receiverSlot: int) (rx: float<meter>) (ry: float<meter>) : ChainState =
        simulatePassWith s (passOf s (MatchMath.dist s.BallX s.BallY rx ry)) receiverSlot

    /// Check if a receiver is offside given the current state.
    let isOffside (s: ChainState) (rx: float<meter>) : bool =
        rx * s.Dir > max 0.0<meter> (max (s.BallX * s.Dir) (s.OffsideLine * s.Dir))

    /// Find the receiver slot and position from the current state.
    let private findReceiver (s: ChainState) (slot: int) : struct (int * float<meter> * float<meter>) voption =
        s.OurPos
        |> Array.tryFind (fun struct (s', _, _) -> s' = slot)
        |> function
            | Some(struct (_, x, y)) -> ValueSome(struct (slot, x, y))
            | None -> ValueNone

    /// Check if a pass from the current carrier to (rx,ry) is feasible:
    /// the ball must arrive before any opponent. The opponent GK is not an
    /// interceptor (a keeper never leaves his line for a pass) — this must
    /// match PassIntercept.interceptorStep or the plan and the real pass
    /// model would disagree about what is playable.
    let private isPassFeasible
        (pparams: InterceptTable.InterceptParams)
        (s: ChainState)
        (rx: float<meter>)
        (ry: float<meter>)
        (d: float)
        (roll: BallAero.GroundPass)
        : bool =
        if d <= 0.0 || d > float s.MaxPassDistance then
            false
        else
            let speed = roll.LaunchSpeed * 1.0<meterPerSecond>

            let ball =
                { X = s.BallX
                  Y = s.BallY
                  Z = 0.0<meter>
                  Vx = (float (rx - s.BallX) / d) * speed
                  Vy = (float (ry - s.BallY) / d) * speed
                  Vz = 0.0<meterPerSecond> }

            let rolled = InterceptTable.RolledBall(pparams, ball)
            let receiverSteps = rolled.StepsFor(rx, ry)

            let mutable bestOpponent = pparams.MaxSteps

            for struct (oslot, ox, oy) in s.OppPos do
                if not (SlotMask.contains oslot s.GoalkeeperSlots) then
                    let steps = rolled.StepsFor(ox, oy)

                    if steps < bestOpponent then
                        bestOpponent <- steps

            receiverSteps < bestOpponent

    /// Search: given level-1 options, simulate the first action, then
    /// expand the best continuation recursively up to MaxChainLength.
    /// Each evaluation is anchored on the option's own calibrated utility
    /// (which carries skill, memory, pressure, phase, personality) and adds
    /// the continuation delta (value after - value before), so the chain
    /// ranks options without discarding why the carrier wanted them.
    let search
        (cfg: PlannerConfig)
        (w: WeightSectionsPlanner)
        (pparams: InterceptTable.InterceptParams)
        (s: ChainState)
        (options: (int * int * float)[])
        : ChainEvaluation[] =
        let baseline = value w s
        let mutable used = 0
        let mutable frontierLimit = cfg.MaxEvaluateLimit
        // Spending the budget is a test, not a wrapper taking a thunk: one closure per evaluation dominated the
        // planner's allocations.
        let spend () =
            if used < min cfg.MaxEvaluateLimit frontierLimit then used <- used + 1; true else false
        let pass state slot =
            match findReceiver state slot with
            | ValueSome(struct(_, x, y)) when slot <> state.CarrierSlot && not (SlotMask.contains slot state.OurGoalkeeperSlots) && not (isOffside state x) ->
                let distance = MatchMath.dist state.BallX state.BallY x y
                // The roll is solved once: feasibility and the simulated flight fly the same pass.
                let roll = passOf state distance
                let flight = roll.Seconds * 1.0<second>
                if distance >= float cfg.MinimumPassDistance && flight <= state.Horizon && isPassFeasible pparams state x y distance roll then
                    let next = simulatePassWith state roll slot
                    Some(next, Pass(slot, next.BallX, next.BallY))
                else None
            | _ -> None
        let carry state =
            let duration = min cfg.DribbleDuration state.Horizon
            if duration <= 0.0<second> then None else
            let x, y = MatchMath.pitchClamp (float (state.BallX + state.Dir * state.CarrySpeed * duration)) (float state.BallY)
            let point = Vec2.create (x * 1.0<meter>) (y * 1.0<meter>)
            let advanced = advance state duration
            let ours = advanced.OurPos |> Array.map (fun struct(slot, px, py) -> if slot = state.CarrierSlot then struct(slot, point.X, point.Y) else struct(slot, px, py))
            Some({ advanced with BallX = point.X; BallY = point.Y; OurPos = ours; Horizon = state.Horizon - duration }, Dribble)
        let count = options.Length
        let chains = Array.init count (fun _ -> [||])
        let states = Array.create count None
        let scores = options |> Array.map (fun (_, _, utility) -> utility)
        let work = Array.zeroCreate<int> count
        // First actions are considered for every root before any continuation; later input options cannot be starved by an early tree.
        for index in 0 .. count - 1 do
            let code, target, utility = options[index]
            let before = used
            if cfg.Enabled then
                let result =
                    if not (spend ()) then None
                    else
                        match code with
                        | 1 -> pass s target
                        | 3 -> carry s
                        | 2 -> Some(s, Shoot)
                        | 4 -> Some(s, Clear)
                        | 5 -> Some(s, Cross(target, s.BallX, s.BallY))
                        | _ -> None
                result |> Option.iter (fun (state, action) ->
                    chains[index] <- [|action|]
                    scores[index] <- utility + s.Confidence * (value w state - baseline)
                    if code = 1 || code = 3 then states[index] <- Some [|(state, [|action|], SlotMask.add s.CarrierSlot 0UL |> SlotMask.add state.CarrierSlot)|])
            work[index] <- used - before
        for _ in 2 .. cfg.MaxChainLength do
            for index in 0 .. count - 1 do
                match states[index] with
                | Some beam ->
                    let before = used
                    let remainingRoots = states[index..] |> Array.filter Option.isSome |> Array.length
                    frontierLimit <- used + (cfg.MaxEvaluateLimit - used + remainingRoots - 1) / max 1 remainingRoots
                    let branches = ResizeArray<ChainState * ChainAction[] * uint64 * float>()
                    for state, path, visited in beam do
                        // Stop and shoot competes with passing; it is not postponed until every pass fails.
                        if MatchMath.distanceToGoal state.Side state.BallX state.BallY < float cfg.ShootingRange then
                            if spend () then
                                let _, _, utility = options[index]
                                let score = utility + s.Confidence * (value w state - baseline + w.ShootValueWeight * MatchMath.xGFrom (MatchMath.distanceToGoal state.Side state.BallX state.BallY) w.ShootFinishingAssumption 1.0)
                                if score > scores[index] then chains[index] <- Array.append path [|Shoot|]; scores[index] <- score
                        for struct(slot, _, _) in state.OurPos do
                            if not (SlotMask.contains slot visited) && not (SlotMask.contains slot state.OurGoalkeeperSlots) then
                                (if spend () then pass state slot else None) |> Option.iter (fun (next, action) ->
                                    let score = (let _, _, utility = options[index] in utility) + s.Confidence * (value w next - baseline)
                                    branches.Add(next, Array.append path [|action|], SlotMask.add slot visited, score))
                        (if spend () then carry state else None) |> Option.iter (fun (next, action) ->
                            let score = (let _, _, utility = options[index] in utility) + s.Confidence * (value w next - baseline)
                            branches.Add(next, Array.append path [|action|], visited, score))
                    let best = branches |> Seq.sortByDescending (fun (_, _, _, score) -> score) |> Seq.truncate cfg.BeamWidth |> Seq.toArray
                    for _, path, _, score in best do
                        if score > scores[index] then scores[index] <- score; chains[index] <- path
                    states[index] <- if best.Length = 0 then None else Some(best |> Array.map (fun (state, path, visited, _) -> state, path, visited))
                    frontierLimit <- cfg.MaxEvaluateLimit
                    work[index] <- work[index] + used - before
                | None -> ()
        options |> Array.mapi (fun index (code, target, _) ->
            { ActionCode = code; TargetSlot = target; Chain = chains[index]; Value = scores[index]; CandidateEvaluations = work[index] })

    /// Build a ChainState from the live world.
    let project (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : ChainState =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let oppSide = MatchWorld.oppOf side
        let oppTeam = MatchWorld.team world oppSide
        let oppFrame = oppTeam.Frame

        let ballX = world.State.Ball.Position.X
        let ballY = world.State.Ball.Position.Y
        let dir = MatchMath.goalDirection side
        let offsideLine = MatchWorld.offsideLine ctx world side

        let ourPos =
            [| for s in MatchWorld.activeSlots frame do
                   struct (s, MatchWorld.posX frame s, MatchWorld.posY frame s) |]

        let oppPos =
            [| for s in MatchWorld.activeSlots oppFrame do
                    struct (s, MatchWorld.posX oppFrame s, MatchWorld.posY oppFrame s) |]

        let motion (f: TeamFrame) =
            [| for s in MatchWorld.activeSlots f do
                   let speed = sqrt (float f.VelX[s] ** 2.0 + float f.VelY[s] ** 2.0)
                   let cap = float ctx.Config.Movement.SprintSpeed * (1.0 - (1.0 - float f.Condition[s]) * ctx.Config.Movement.FatigueSpeedDecay)
                   let scale = if speed > cap && speed > 0.0 then cap / speed else 1.0
                   struct (s, float f.VelX[s] * scale * 1.0<meterPerSecond>, float f.VelY[s] * scale * 1.0<meterPerSecond>) |]
        let profile = MatchWorld.profileOf ctx side slot
        let confidence = ctx.Config.CollectiveAttack.AnticipationConfidence * float profile.Anticipation / 100.0 * float frame.Cognitive[slot] * float frame.Condition[slot]

        { BallX = ballX
          BallY = ballY
          CarrierSlot = slot
          Side = side
          OurPos = ourPos
          OppPos = oppPos
          OffsideLine = offsideLine
          Dir = dir
          OurMotion = motion frame
          OppMotion = motion oppFrame
          Horizon = ctx.Config.CollectiveAttack.AnticipationHorizon
          Confidence = confidence
          GoalkeeperSlots = MatchWorld.activeSlots oppFrame |> Array.filter (fun slot -> oppFrame.SlotRoles[slot] = SlotRole.Goalkeeper) |> SlotMask.ofSeq
          OurGoalkeeperSlots = MatchWorld.activeSlots frame |> Array.filter (fun slot -> frame.SlotRoles[slot] = SlotRole.Goalkeeper) |> SlotMask.ofSeq
          PassBall = ctx.Config.Ball
          PassArrivalSpeed = ctx.Config.Actions.PassArrivalSpeed
          PassArrivalSpeedGain = ctx.Config.Actions.PassArrivalSpeedGain
          MaxPassDistance = ctx.Config.Actions.MaxPassExecutionDistance
          CarrySpeed = ctx.Config.Movement.SprintSpeed * float profile.Dribbling / 100.0 * float frame.Condition[slot] }
