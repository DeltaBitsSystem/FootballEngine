namespace FootballEngine.Core.Tests.Systems.Players

open FootballEngine.Core.Match
open FootballEngine.Core.Match.Planner

/// The planner as it was before its search was rewritten to run on a state arena (a literal copy of that
/// `search` and the helpers it used). It is the oracle for PlannerEquivalenceTests: two implementations agree
/// bit for bit on real inputs, so the rewrite can be trusted to change cost and nothing else.
module PlannerReference =

    /// A chain state in goal probability: the ball held where it ends, at risk from the nearest opponent and
    /// safer with a teammate in support (DecisionValue.held), the same scale the carrier's options are valued on.
    let value (e: ExpectedValueConfig) (s: ChainState) : float =
        let mutable nearest = System.Double.PositiveInfinity

        for struct (_, ox, oy) in s.OppPos do
            let d = MatchMath.dist s.BallX s.BallY ox oy

            if d < nearest then
                nearest <- d

        let mutable supported = false

        for struct (slot, tx, ty) in s.OurPos do
            if
                slot <> s.CarrierSlot
                && not supported
                && MatchMath.dist s.BallX s.BallY tx ty < float e.SupportDistance
            then
                supported <- true

        DecisionValue.held e s.Side s.BallX s.BallY nearest supported

    let private moved
        (motions: struct (int * float<meterPerSecond> * float<meterPerSecond>)[])
        (positions: struct (int * float<meter> * float<meter>)[])
        (flight: float<second>)
        (pinnedSlot: int)
        (pinnedX: float<meter>)
        (pinnedY: float<meter>)
        =
        let result = Array.zeroCreate positions.Length

        for i in 0 .. positions.Length - 1 do
            let struct (slot, x, y) = positions[i]

            if slot = pinnedSlot then
                result[i] <- struct (slot, pinnedX, pinnedY)
            else
                // A projected state lists motions in position order, so the same index is tried first; the scan
                // keeps hand-built states with any order correct.
                let mutable index =
                    if i < motions.Length && (let struct (m, _, _) = motions[i] in m = slot) then
                        i
                    else
                        0

                let mutable found = false
                let mutable px = x
                let mutable py = y

                while index < motions.Length && not found do
                    let struct (movingSlot, vx, vy) = motions[index]

                    if movingSlot = slot then
                        let x, y = MatchMath.pitchClamp (float (x + vx * flight)) (float (y + vy * flight))
                        px <- x * 1.0<meter>
                        py <- y * 1.0<meter>
                        found <- true

                    index <- index + 1

                result[i] <- struct (slot, px, py)

        result

    /// Every branch builds exactly one state: the carrier's override and the new ball are applied while the
    /// positions are projected, instead of copying a projected state again.
    let private advanceWith (s: ChainState) (flight: float<second>) (pinnedSlot: int) pinnedX pinnedY ballOf =
        let ours = moved s.OurMotion s.OurPos flight pinnedSlot pinnedX pinnedY
        let opponents = moved s.OppMotion s.OppPos flight -1 0.0<meter> 0.0<meter>

        let line =
            if s.OppMotion.Length = 0 then
                s.OffsideLine
            else
                // The referee's line (MatchWorld.offsideLine) projected forward: the deepest outfield defender, never
                // inside the attacker's own half, so the plan and the flag agree on who is onside.
                let mutable deepest = 0.0<meter>

                for struct (slot, x, _) in opponents do
                    if not (SlotMask.contains slot s.GoalkeeperSlots) then
                        deepest <- max deepest (x * s.Dir)

                deepest * s.Dir

        let struct (ballX, ballY, carrier) = ballOf ours

        { s with
            BallX = ballX
            BallY = ballY
            CarrierSlot = carrier
            OurPos = ours
            OppPos = opponents
            OffsideLine = line
            Horizon = max 0.0<second> (s.Horizon - flight) }

    let private passOf (s: ChainState) distance =
        BallAero.pass s.PassBall s.PassArrivalSpeed s.PassArrivalSpeedGain (float distance)

    type private PassTable =
        { Ball: BallConfig
          Arrival: float<meterPerSecond>
          Gain: float
          Resolution: float
          Samples: BallAero.GroundPass[] }

    /// Published whole as one reference, so concurrent matches never read a table another thread is building.
    let mutable private passTable: PassTable option = None

    /// The planner weighs hundreds of candidate passes per decision and the exact roll solve was its largest cost;
    /// every length it can propose is covered by one table per ball and arrival law.
    let tabledPass (resolution: float<meter>) (s: ChainState) (distance: float) : BallAero.GroundPass =
        let step = float resolution

        let table =
            match passTable with
            | Some table when
                table.Arrival = s.PassArrivalSpeed
                && table.Gain = s.PassArrivalSpeedGain
                && table.Resolution = step
                && float (table.Samples.Length - 1) * step >= float s.MaxPassDistance
                && table.Ball = s.PassBall
                ->
                table
            | _ ->
                let count = int (ceil (float s.MaxPassDistance / step)) + 2

                let built =
                    { Ball = s.PassBall
                      Arrival = s.PassArrivalSpeed
                      Gain = s.PassArrivalSpeedGain
                      Resolution = step
                      Samples = Array.init count (fun i -> passOf s (float i * step)) }

                passTable <- Some built
                built

        let position = max 0.0 distance / step
        let index = min (int position) (table.Samples.Length - 2)
        let fraction = position - float index
        let low = table.Samples[index]
        let high = table.Samples[index + 1]

        { LaunchSpeed = low.LaunchSpeed + (high.LaunchSpeed - low.LaunchSpeed) * fraction
          Seconds = low.Seconds + (high.Seconds - low.Seconds) * fraction }

    let private simulatePassWith (s: ChainState) (roll: BallAero.GroundPass) (receiverSlot: int) : ChainState =
        let flight = min s.Horizon (roll.Seconds * 1.0<second>)

        advanceWith s flight -1 0.0<meter> 0.0<meter> (fun ours ->
            let mutable i = 0

            while (let struct (slot, _, _) = ours[i] in slot <> receiverSlot) do
                i <- i + 1

            let struct (_, x, y) = ours[i]
            struct (x, y, receiverSlot))

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
        (rolled: InterceptTable.RolledBall)
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

            rolled.Reset(pparams, ball)
            let receiverSteps = rolled.StepsFor(rx, ry)
            // Feasible means no interceptor is there by the receiver's step: an opponent's exact step past it
            // cannot change the verdict, so the roll stops there.
            let mutable open' = receiverSteps < pparams.MaxSteps
            let mutable index = 0

            while open' && index < s.OppPos.Length do
                let struct (oslot, ox, oy) = s.OppPos[index]

                if
                    not (SlotMask.contains oslot s.GoalkeeperSlots)
                    && rolled.ReachesWithin(ox, oy, receiverSteps)
                then
                    open' <- false

                index <- index + 1

            open'

    /// Search: given level-1 options, simulate the first action, then
    /// expand the best continuation recursively up to MaxChainLength.
    /// Each evaluation is anchored on the option's own calibrated utility
    /// (which carries skill, memory, pressure, phase, personality) and adds
    /// the continuation delta (value after - value before), so the chain
    /// ranks options without discarding why the carrier wanted them.
    let search
        (cfg: PlannerConfig)
        (e: ExpectedValueConfig)
        (pparams: InterceptTable.InterceptParams)
        (s: ChainState)
        (options: (int * int * float)[])
        : ChainEvaluation[] =
        let rolled =
            InterceptTable.RolledBalls.Rent(
                pparams,
                { X = s.BallX
                  Y = s.BallY
                  Z = 0.0<meter>
                  Vx = 0.0<meterPerSecond>
                  Vy = 0.0<meterPerSecond>
                  Vz = 0.0<meterPerSecond> }
            )

        let mutable used = 0
        let mutable frontierLimit = cfg.MaxEvaluateLimit
        // Spending the budget is a test, not a wrapper taking a thunk: one closure per evaluation dominated the
        // planner's allocations.
        let spend () =
            if used < min cfg.MaxEvaluateLimit frontierLimit then
                used <- used + 1
                true
            else
                false

        let pass state slot =
            match findReceiver state slot with
            | ValueSome(struct (_, x, y)) when
                slot <> state.CarrierSlot
                && not (SlotMask.contains slot state.OurGoalkeeperSlots)
                && not (isOffside state x)
                ->
                let distance = MatchMath.dist state.BallX state.BallY x y
                // Length limits are settled before the roll is solved: most candidates fail them, and the solve
                // was the planner's largest cost.
                if
                    distance < float cfg.MinimumPassDistance
                    || distance <= 0.0
                    || distance > float state.MaxPassDistance
                then
                    None
                else
                    // The roll is solved once: feasibility and the simulated flight fly the same pass.
                    let roll = tabledPass cfg.PassTableResolution state distance
                    let flight = roll.Seconds * 1.0<second>

                    if
                        distance >= float cfg.MinimumPassDistance
                        && flight <= state.Horizon
                        && isPassFeasible pparams rolled state x y distance roll
                    then
                        let next = simulatePassWith state roll slot
                        Some(next, Pass(slot, next.BallX, next.BallY))
                    else
                        None
            | _ -> None

        let carry state =
            let duration = min cfg.DribbleDuration state.Horizon

            if duration <= 0.0<second> then
                None
            else
                let x, y =
                    MatchMath.pitchClamp
                        (float (state.BallX + state.Dir * state.CarrySpeed * duration))
                        (float state.BallY)

                let bx, by = x * 1.0<meter>, y * 1.0<meter>
                let carrier = state.CarrierSlot

                let next =
                    advanceWith state duration carrier bx by (fun _ -> struct (bx, by, carrier))

                Some(next, Dribble)

        let count = options.Length
        let chains = Array.init count (fun _ -> [||])
        let states = Array.create count None
        let scores = options |> Array.map (fun (_, _, utility) -> utility)
        // Each option already carries its own expected value; a continuation adds only what it is worth beyond
        // where that first action leaves the ball.
        let rootValues = Array.zeroCreate<float> options.Length
        let work = Array.zeroCreate<int> count
        // First actions are considered for every root before any continuation; later input options cannot be starved by an early tree.
        for index in 0 .. count - 1 do
            let code, target, _ = options[index]
            let before = used

            if cfg.Enabled then
                let result =
                    if not (spend ()) then
                        None
                    else
                        match code with
                        | 1 -> pass s target
                        | 3 -> carry s
                        | 2 -> Some(s, Shoot)
                        | 4 -> Some(s, Clear)
                        | 5 -> Some(s, Cross(target, s.BallX, s.BallY))
                        | _ -> None

                result
                |> Option.iter (fun (state, action) ->
                    chains[index] <- [| action |]
                    rootValues[index] <- value e state

                    if code = 1 || code = 3 then
                        states[index] <-
                            Some
                                [| (state,
                                    [| action |],
                                    SlotMask.add s.CarrierSlot 0UL |> SlotMask.add state.CarrierSlot) |])

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

                                let score =
                                    utility
                                    + s.Confidence
                                      * (MatchMath.xGFrom
                                          (MatchMath.distanceToGoal state.Side state.BallX state.BallY)
                                          e.ChainFinishing
                                          1.0
                                         - rootValues[index])

                                if score > scores[index] then
                                    chains[index] <- Array.append path [| Shoot |]
                                    scores[index] <- score

                        for struct (slot, _, _) in state.OurPos do
                            if
                                not (SlotMask.contains slot visited)
                                && not (SlotMask.contains slot state.OurGoalkeeperSlots)
                            then
                                (if spend () then pass state slot else None)
                                |> Option.iter (fun (next, action) ->
                                    let score =
                                        (let _, _, utility = options[index] in utility)
                                        + s.Confidence * (value e next - rootValues[index])

                                    branches.Add(
                                        next,
                                        Array.append path [| action |],
                                        SlotMask.add slot visited,
                                        score
                                    ))

                        (if spend () then carry state else None)
                        |> Option.iter (fun (next, action) ->
                            let score =
                                (let _, _, utility = options[index] in utility)
                                + s.Confidence * (value e next - rootValues[index])

                            branches.Add(next, Array.append path [| action |], visited, score))

                    let best =
                        branches
                        |> Seq.sortByDescending (fun (_, _, _, score) -> score)
                        |> Seq.truncate cfg.BeamWidth
                        |> Seq.toArray

                    for _, path, _, score in best do
                        if score > scores[index] then
                            scores[index] <- score
                            chains[index] <- path

                    states[index] <-
                        if best.Length = 0 then
                            None
                        else
                            Some(best |> Array.map (fun (state, path, visited, _) -> state, path, visited))

                    frontierLimit <- cfg.MaxEvaluateLimit
                    work[index] <- work[index] + used - before
                | None -> ()

        InterceptTable.RolledBalls.Return rolled

        options
        |> Array.mapi (fun index (code, target, _) ->
            { ActionCode = code
              TargetSlot = target
              Chain = chains[index]
              Value = scores[index]
              CandidateEvaluations = work[index] })
