namespace FootballEngine.Core.Match

/// Chain state used by the planner and tests. Defined at namespace level
/// so tests can reference it bare after `open FootballEngine.Core.Match`.
type ChainState =
    {
        BallX: float<meter>
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
        CarrySpeed: float<meterPerSecond>
    }

/// Slot sets as one word each: a lineup has far fewer than 64 slots.
module SlotMask =
    let inline contains (slot: int) (mask: uint64) = (mask >>> slot) &&& 1UL = 1UL
    let inline add (slot: int) (mask: uint64) = mask ||| (1UL <<< slot)

    let ofSeq (slots: int seq) =
        Seq.fold (fun mask slot -> add slot mask) 0UL slots

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

    /// A table is valid for exactly the parameters that shaped it, so it is an immutable value found by them:
    /// concurrent matches (a parallel sweep) running different balls or arrival laws each read their own.
    [<Struct>]
    type private PassKey =
        { Ball: BallConfig
          Arrival: float<meterPerSecond>
          Gain: float
          Resolution: float
          Count: int }

    let private passTables =
        System.Collections.Concurrent.ConcurrentDictionary<PassKey, BallAero.GroundPass[]>()

    let private passKeyOf
        (resolution: float)
        (ball: BallConfig)
        (arrival: float<meterPerSecond>)
        (gain: float)
        (maxPass: float<meter>)
        =
        { Ball = ball
          Arrival = arrival
          Gain = gain
          Resolution = resolution
          // Covers every length the planner may propose, plus the upper neighbour interpolation reads.
          Count = int (ceil (float maxPass / resolution)) + 2 }

    let private buildPassTable (key: PassKey) =
        Array.init key.Count (fun i -> BallAero.pass key.Ball key.Arrival key.Gain (float i * key.Resolution))

    let private passTableFor (resolution: float) (s: ChainState) =
        let key =
            passKeyOf resolution s.PassBall s.PassArrivalSpeed s.PassArrivalSpeedGain s.MaxPassDistance

        passTables.GetOrAdd(key, (fun k -> buildPassTable k))

    let private interpolatePass (samples: BallAero.GroundPass[]) (step: float) (distance: float) : BallAero.GroundPass =
        let position = max 0.0 distance / step
        let index = min (int position) (samples.Length - 2)
        let fraction = position - float index
        let low = samples[index]
        let high = samples[index + 1]

        { LaunchSpeed = low.LaunchSpeed + (high.LaunchSpeed - low.LaunchSpeed) * fraction
          Seconds = low.Seconds + (high.Seconds - low.Seconds) * fraction }

    /// The planner weighs hundreds of candidate passes per decision and the exact roll solve was its largest cost;
    /// every length it can propose is covered by one table per ball and arrival law.
    let tabledPass (resolution: float<meter>) (s: ChainState) (distance: float) : BallAero.GroundPass =
        let step = float resolution
        interpolatePass (passTableFor step s) step distance

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

    /// Struct-of-arrays store for the states one search creates. A search builds hundreds of states; keeping them
    /// as indices into reused arrays removes the per-state records, position arrays and chain copies that made the
    /// planner the largest allocator of a decision. It grows by doubling, so no input size can overflow it.
    [<AllowNullLiteral>]
    type private Arena() =
        member val Capacity = 0 with get, set
        member val Players = 0 with get, set
        member val OurX: float[] = [||] with get, set
        member val OurY: float[] = [||] with get, set
        member val OppX: float[] = [||] with get, set
        member val OppY: float[] = [||] with get, set
        member val BallX: float[] = [||] with get, set
        member val BallY: float[] = [||] with get, set
        member val Carrier: int[] = [||] with get, set
        member val Line: float[] = [||] with get, set
        member val Horizon: float[] = [||] with get, set
        member val Visited: uint64[] = [||] with get, set
        member val Parent: int[] = [||] with get, set
        /// 1 pass, 2 shoot, 3 dribble, 4 clear, 5 cross
        member val ActKind: int[] = [||] with get, set
        member val ActSlot: int[] = [||] with get, set
        member val ActX: float[] = [||] with get, set
        member val ActY: float[] = [||] with get, set
        member val Score: float[] = [||] with get, set
        member val Count = 0 with get, set

        /// Starts an empty search sized for `players` per side; storage is kept when it already fits.
        member this.Reset(players: int) =
            this.Count <- 0

            if players > this.Players || this.Capacity = 0 then
                this.Players <- max players this.Players
                this.Capacity <- max 256 this.Capacity
                this.Reallocate(this.Capacity, false)

        member private this.Reallocate(capacity: int, keep: bool) =
            let players = this.Players

            let sized (a: 'T[]) n =
                if keep then
                    (let b = Array.zeroCreate<'T> n in
                     Array.blit a 0 b 0 a.Length
                     b)
                else
                    Array.zeroCreate<'T> n

            this.OurX <- sized this.OurX (capacity * players)
            this.OurY <- sized this.OurY (capacity * players)
            this.OppX <- sized this.OppX (capacity * players)
            this.OppY <- sized this.OppY (capacity * players)
            this.BallX <- sized this.BallX capacity
            this.BallY <- sized this.BallY capacity
            this.Carrier <- sized this.Carrier capacity
            this.Line <- sized this.Line capacity
            this.Horizon <- sized this.Horizon capacity
            this.Visited <- sized this.Visited capacity
            this.Parent <- sized this.Parent capacity
            this.ActKind <- sized this.ActKind capacity
            this.ActSlot <- sized this.ActSlot capacity
            this.ActX <- sized this.ActX capacity
            this.ActY <- sized this.ActY capacity
            this.Score <- sized this.Score capacity
            this.Capacity <- capacity

        member this.New() =
            if this.Count = this.Capacity then
                this.Reallocate(this.Capacity * 2, true)

            let i = this.Count
            this.Count <- i + 1
            i

    /// Cache identity of a reach table: the race parameters it is a function of.
    type private ReachKey = (struct (int * float * float * float))

    let private reachTables =
        System.Collections.Concurrent.ConcurrentDictionary<ReachKey, float[]>()

    let private reachKeyOf (p: InterceptTable.InterceptParams) : ReachKey =
        struct (p.MaxSteps, float p.PlayerSpeedPerSecond, float p.PlayerAcceleration, float p.ControlRadius)

    /// Distance a defender can cover (plus control radius) after n race steps, shared by every search with the
    /// same race parameters.
    let private buildReaches (p: InterceptTable.InterceptParams) =
        let control = float p.ControlRadius

        Array.init (p.MaxSteps + 1) (fun n ->
            control + InterceptTable.reachableDistance p (float n * float InterceptTable.dt))

    /// Reused buffers of one thread's search. Every array is sized up front from the input, so nothing in the
    /// search can run past its end.
    [<Sealed; AllowNullLiteral>]
    type private Scratch() =
        member val Arena = Arena()
        member val CandX: float[] = [||] with get, set
        member val CandY: float[] = [||] with get, set
        member val Branch: int[] = [||] with get, set
        member val Best: int[] = [||] with get, set
        member val BeamStart: int[] = [||] with get, set
        member val BeamLen: int[] = [||] with get, set
        member val BeamStore: int[] = [||] with get, set
        member val RootValues: float[] = [||] with get, set
        member val Work: int[] = [||] with get, set
        member val Scores: float[] = [||] with get, set
        // The last table of each kind this thread used: a match keeps one config, so the shared dictionary is
        // consulted only when it changes.
        member val PassKey: PassKey voption = ValueNone with get, set
        member val PassSamples: BallAero.GroundPass[] = [||] with get, set
        member val ReachKey: ReachKey voption = ValueNone with get, set
        member val Reaches: float[] = [||] with get, set

    let private scratches =
        new System.Threading.ThreadLocal<Scratch>(fun () -> Scratch())

    let private room (a: 'T[]) (n: int) : 'T[] =
        if a.Length >= n then
            a
        else
            Array.zeroCreate<'T> (max n (a.Length * 2))

    /// The search both entry points share: given level-1 options, simulate the first action, then
    /// expand the best continuation recursively up to MaxChainLength.
    /// Each evaluation is anchored on the option's own calibrated utility
    /// (which carries skill, memory, pressure, phase, personality) and adds
    /// the continuation delta (value after - value before), so the chain
    /// ranks options without discarding why the carrier wanted them.
    ///
    /// States live in a per-thread arena and the beam holds indices; a pass race is decided step by step against
    /// only the opponents that can reach the ball's path, which gives the verdict `RolledBall` would. Chains are
    /// rebuilt from parent links only when a root's best improves.
    let private run
        (cfg: PlannerConfig)
        (e: ExpectedValueConfig)
        (pparams: InterceptTable.InterceptParams)
        (ball: BallConfig)
        (arrival: float<meterPerSecond>)
        (gain: float)
        (maxPassDistance: float<meter>)
        (r: ChainRoot)
        (opts: PlanOptions)
        (result: PlanResult)
        (chains: ChainAction[][])
        : unit =
        let sc = scratches.Value
        let arena = sc.Arena
        let n = r.OurCount
        let m = r.OppCount
        let count = opts.Count
        // Only the tests read chains; an empty array means the caller wants none, which keeps the production
        // search from building a path per improvement.
        let wantChains = chains.Length > 0
        let beamWidth = max 1 cfg.BeamWidth
        arena.Reset(max n m)
        let players = arena.Players
        sc.CandX <- room sc.CandX m
        sc.CandY <- room sc.CandY m
        // A beam state yields at most one pass per teammate plus one carry, and `beamWidth` states are expanded.
        sc.Branch <- room sc.Branch (beamWidth * (n + 1))
        sc.Best <- room sc.Best beamWidth
        sc.BeamStart <- room sc.BeamStart count
        sc.BeamLen <- room sc.BeamLen count
        sc.BeamStore <- room sc.BeamStore (count * beamWidth)
        sc.RootValues <- room sc.RootValues count
        sc.Work <- room sc.Work count
        sc.Scores <- room sc.Scores count
        System.Array.Clear(sc.BeamLen, 0, count)
        System.Array.Clear(sc.RootValues, 0, count)

        let reachKey = reachKeyOf pparams

        // `.Equals` on the key type itself: `<>` on a voption boxed both keys on every search.
        let reachSame =
            match sc.ReachKey with
            | ValueSome known -> known.Equals reachKey
            | ValueNone -> false

        if not reachSame then
            sc.Reaches <- reachTables.GetOrAdd(reachKey, (fun _ -> buildReaches pparams))
            sc.ReachKey <- ValueSome reachKey

        let reaches = sc.Reaches
        let resolution = float cfg.PassTableResolution
        let passKey = passKeyOf resolution ball arrival gain maxPassDistance

        let passSame =
            match sc.PassKey with
            | ValueSome known -> known.Equals passKey
            | ValueNone -> false

        if not passSame then
            sc.PassSamples <- passTables.GetOrAdd(passKey, (fun k -> buildPassTable k))
            sc.PassKey <- ValueSome passKey

        let samples = sc.PassSamples
        let maxSteps = pparams.MaxSteps
        // Past this distance from the ball's path no opponent can win a step, so he is left out of the race.
        let longest = if maxSteps > 0 then reaches[maxSteps - 1] + 1e-6 else 0.0
        let stepSeconds = float InterceptTable.dt
        let rollTable = InterceptTable.RollTables.For pparams
        let dir = r.Dir
        let gk = r.GoalkeeperSlots
        let ourGk = r.OurGoalkeeperSlots
        let maxPass = float maxPassDistance
        let minPass = float cfg.MinimumPassDistance
        let ourSlots = r.OurSlot
        let oppSlots = r.OppSlot
        let side = r.Side
        let carrierSlot = r.CarrierSlot
        let confidence = r.Confidence
        let ballX0 = r.BallX
        let ballY0 = r.BallY

        let root = arena.New()

        for i in 0 .. n - 1 do
            arena.OurX[root * players + i] <- r.OurX[i]
            arena.OurY[root * players + i] <- r.OurY[i]

        for i in 0 .. m - 1 do
            arena.OppX[root * players + i] <- r.OppX[i]
            arena.OppY[root * players + i] <- r.OppY[i]

        arena.BallX[root] <- ballX0
        arena.BallY[root] <- ballY0
        arena.Carrier[root] <- carrierSlot
        arena.Line[root] <- r.OffsideLine
        arena.Horizon[root] <- r.Horizon
        arena.Parent[root] <- -1

        let ourMotionX = r.OurVx
        let ourMotionY = r.OurVy
        let ourMoves = r.OurMoves
        let oppMotionX = r.OppVx
        let oppMotionY = r.OppVy
        let oppMoves = r.OppMoves

        let ourIndexOf slot =
            let mutable i = 0

            while i < n && ourSlots[i] <> slot do
                i <- i + 1

            if i < n then i else -1

        let hasOppMotion = r.HasOppMotion
        let halfL, halfW = float Pitch.HalfLength, float Pitch.HalfWidth

        /// `advanceWith`: every player projected `flight` ahead with one pinned; returns the new state's index.
        let advance (from: int) (flight: float) (pinnedSlot: int) (pinnedX: float) (pinnedY: float) =
            let next = arena.New()
            let fb, nb = from * players, next * players

            for i in 0 .. n - 1 do
                if ourSlots[i] = pinnedSlot then
                    arena.OurX[nb + i] <- pinnedX
                    arena.OurY[nb + i] <- pinnedY
                elif ourMoves[i] then
                    let x = arena.OurX[fb + i] + ourMotionX[i] * flight
                    let y = arena.OurY[fb + i] + ourMotionY[i] * flight
                    arena.OurX[nb + i] <- min halfL (max (-halfL) x)
                    arena.OurY[nb + i] <- min halfW (max (-halfW) y)
                else
                    arena.OurX[nb + i] <- arena.OurX[fb + i]
                    arena.OurY[nb + i] <- arena.OurY[fb + i]

            let mutable deepest = 0.0

            for i in 0 .. m - 1 do
                let x =
                    if oppMoves[i] then
                        let x = arena.OppX[fb + i] + oppMotionX[i] * flight
                        let y = arena.OppY[fb + i] + oppMotionY[i] * flight
                        let cx = min halfL (max (-halfL) x)
                        arena.OppX[nb + i] <- cx
                        arena.OppY[nb + i] <- min halfW (max (-halfW) y)
                        cx
                    else
                        arena.OppX[nb + i] <- arena.OppX[fb + i]
                        arena.OppY[nb + i] <- arena.OppY[fb + i]
                        arena.OppX[fb + i]

                if not (SlotMask.contains oppSlots[i] gk) then
                    deepest <- max deepest (x * dir)

            arena.Line[next] <- if hasOppMotion then deepest * dir else arena.Line[from]
            arena.Horizon[next] <- max 0.0 (arena.Horizon[from] - flight)
            arena.Parent[next] <- from
            next

        /// `value` on an arena state.
        let valueOf (st: int) =
            let b = st * players
            let bx, by = arena.BallX[st], arena.BallY[st]
            let mutable nearest = System.Double.PositiveInfinity

            for i in 0 .. m - 1 do
                let dx = bx - arena.OppX[b + i]
                let dy = by - arena.OppY[b + i]
                let d = sqrt (dx * dx + dy * dy)

                if d < nearest then
                    nearest <- d

            let mutable supported = false
            let carrier = arena.Carrier[st]

            for i in 0 .. n - 1 do
                if ourSlots[i] <> carrier && not supported then
                    let dx = bx - arena.OurX[b + i]
                    let dy = by - arena.OurY[b + i]

                    if sqrt (dx * dx + dy * dy) < float e.SupportDistance then
                        supported <- true

            DecisionValue.held e side (bx * 1.0<meter>) (by * 1.0<meter>) nearest supported

        /// Feasible means the ball reaches the receiver before any opponent can reach it. The opponent GK is not an
        /// interceptor (a keeper never leaves his line for a pass), matching PassIntercept.interceptorStep.
        let feasible (st: int) (rx: float) (ry: float) (d: float) (g: BallAero.GroundPass) =
            if d <= 0.0 || d > maxPass then
                false
            else
                let b = st * players
                let speed = g.LaunchSpeed
                let bx, by = arena.BallX[st], arena.BallY[st]
                let ux, uy = (rx - bx) / d, (ry - by) / d
                let rollLength = speed * stepSeconds * float maxSteps + 1e-6
                let mutable candidates = 0

                for i in 0 .. m - 1 do
                    if not (SlotMask.contains oppSlots[i] gk) then
                        let ox, oy = arena.OppX[b + i], arena.OppY[b + i]
                        let px, py = ox - bx, oy - by
                        let along = min rollLength (max 0.0 (px * ux + py * uy))
                        let cx, cy = px - along * ux, py - along * uy

                        if cx * cx + cy * cy <= longest * longest then
                            sc.CandX[candidates] <- ox
                            sc.CandY[candidates] <- oy
                            candidates <- candidates + 1

                // The pass rolls straight: the tabulated distance along the launch direction, or the exact step
                // when the launch speed falls outside the table or in a cell where the ball stops.
                let cell = rollTable.Cell speed
                let mutable rolling = speed
                let mutable rolled = 0.0
                let mutable x = bx
                let mutable y = by
                // 0 undecided, -1 an opponent is there first (or ties), 1 the receiver is.
                let mutable verdict = 0
                let mutable k = 0

                while verdict = 0 && k < maxSteps do
                    if k > 0 then
                        if cell >= 0 then
                            rolled <- rollTable.At(cell, speed, k)
                        else
                            rolling <- InterceptTable.stepSpeed pparams rolling
                            rolled <- rolled + rolling * stepSeconds

                        x <- bx + ux * rolled
                        y <- by + uy * rolled

                    let reach = reaches[k]
                    let r2 = reach * reach
                    let mutable j = 0

                    while verdict = 0 && j < candidates do
                        let dx = sc.CandX[j] - x
                        let dy = sc.CandY[j] - y

                        if dx * dx + dy * dy <= r2 then
                            verdict <- -1

                        j <- j + 1

                    if verdict = 0 then
                        let dx = rx - x
                        let dy = ry - y

                        if dx * dx + dy * dy <= r2 then
                            verdict <- 1

                    k <- k + 1

                verdict = 1

        /// `pass`: -1 when the pass is not playable, else the new state's index.
        let pass (st: int) (slot: int) =
            let i = ourIndexOf slot

            if i < 0 then
                -1
            else
                let b = st * players
                let x, y = arena.OurX[b + i], arena.OurY[b + i]
                let bx, by = arena.BallX[st], arena.BallY[st]
                let offside = x * dir > max 0.0 (max (bx * dir) (arena.Line[st] * dir))

                if slot = arena.Carrier[st] || SlotMask.contains slot ourGk || offside then
                    -1
                else
                    let dx, dy = bx - x, by - y
                    let distance = sqrt (dx * dx + dy * dy)
                    // Length limits are settled before the race: most candidates fail them.
                    if distance < minPass || distance <= 0.0 || distance > maxPass then
                        -1
                    else
                        let roll = interpolatePass samples resolution distance

                        if roll.Seconds <= arena.Horizon[st] && feasible st x y distance roll then
                            let flight = min arena.Horizon[st] roll.Seconds
                            let next = advance st flight -1 0.0 0.0
                            let nb = next * players
                            arena.BallX[next] <- arena.OurX[nb + i]
                            arena.BallY[next] <- arena.OurY[nb + i]
                            arena.Carrier[next] <- slot
                            arena.ActKind[next] <- 1
                            arena.ActSlot[next] <- slot
                            arena.ActX[next] <- arena.BallX[next]
                            arena.ActY[next] <- arena.BallY[next]
                            next
                        else
                            -1

        let carry (st: int) =
            let duration = min (float cfg.DribbleDuration) arena.Horizon[st]

            if duration <= 0.0 then
                -1
            else
                let x = min halfL (max (-halfL) (arena.BallX[st] + dir * r.CarrySpeed * duration))
                let y = min halfW (max (-halfW) arena.BallY[st])
                let carrier = arena.Carrier[st]
                let next = advance st duration carrier x y
                arena.BallX[next] <- x
                arena.BallY[next] <- y
                arena.Carrier[next] <- carrier
                arena.ActKind[next] <- 3
                next

        let actionOf (st: int) =
            match arena.ActKind[st] with
            | 1 -> Pass(arena.ActSlot[st], arena.ActX[st] * 1.0<meter>, arena.ActY[st] * 1.0<meter>)
            | 3 -> Dribble
            | _ -> Shoot

        let pathOf (st: int) (extra: ChainAction voption) =
            let mutable depth = 0
            let mutable cur = st

            while cur >= 0 do
                depth <- depth + 1
                cur <- arena.Parent[cur]

            let len = depth + (if extra.IsSome then 1 else 0)
            let path = Array.zeroCreate<ChainAction> len
            let mutable cur = st
            let mutable k = depth - 1

            while cur >= 0 do
                path[k] <- actionOf cur
                cur <- arena.Parent[cur]
                k <- k - 1

            match extra with
            | ValueSome a -> path[len - 1] <- a
            | ValueNone -> ()

            path

        let mutable used = 0
        let mutable frontierLimit = cfg.MaxEvaluateLimit
        // Spending the budget is a test, not a wrapper taking a thunk: one closure per evaluation dominated the
        // planner's allocations.
        let inline spend () =
            if used < min cfg.MaxEvaluateLimit frontierLimit then
                used <- used + 1
                true
            else
                false

        let scores = sc.Scores
        // Each option already carries its own expected value; a continuation adds only what it is worth beyond
        // where that first action leaves the ball.
        let rootValues = sc.RootValues
        let work = sc.Work
        let beamStart = sc.BeamStart
        let beamLen = sc.BeamLen
        let beamStore = sc.BeamStore

        // First actions are considered for every root before any continuation; later input options cannot be
        // starved by an early tree.
        for index in 0 .. count - 1 do
            let code, target, utility =
                opts.Code[index], opts.Target[index], opts.Utility[index]

            scores[index] <- utility
            beamStart[index] <- index * beamWidth
            let before = used

            if cfg.Enabled then
                if spend () then
                    match code with
                    | 1
                    | 3 ->
                        let st = if code = 1 then pass root target else carry root

                        if st >= 0 then
                            arena.Parent[st] <- -1

                            if wantChains then
                                chains[index] <- [| actionOf st |]

                            rootValues[index] <- valueOf st
                            arena.Visited[st] <- SlotMask.add carrierSlot 0UL |> SlotMask.add arena.Carrier[st]
                            beamStore[beamStart[index]] <- st
                            beamLen[index] <- 1
                    | 2 ->
                        if wantChains then
                            chains[index] <- [| Shoot |]

                        rootValues[index] <- valueOf root
                    | 4 ->
                        if wantChains then
                            chains[index] <- [| Clear |]

                        rootValues[index] <- valueOf root
                    | 5 ->
                        if wantChains then
                            chains[index] <- [| Cross(target, ballX0 * 1.0<meter>, ballY0 * 1.0<meter>) |]

                        rootValues[index] <- valueOf root
                    | _ -> ()

            work[index] <- used - before

        let branch = sc.Branch

        for _ in 2 .. cfg.MaxChainLength do
            for index in 0 .. count - 1 do
                if beamLen[index] > 0 then
                    let before = used
                    let mutable remainingRoots = 0

                    for j in index .. count - 1 do
                        if beamLen[j] > 0 then
                            remainingRoots <- remainingRoots + 1

                    frontierLimit <- used + (cfg.MaxEvaluateLimit - used + remainingRoots - 1) / max 1 remainingRoots
                    let utility = opts.Utility[index]
                    let mutable branches = 0

                    for bi in 0 .. beamLen[index] - 1 do
                        let st = beamStore[beamStart[index] + bi]
                        let bx, by = arena.BallX[st], arena.BallY[st]
                        let goalDistance = MatchMath.distanceToGoal side (bx * 1.0<meter>) (by * 1.0<meter>)

                        // Stop and shoot competes with passing; it is not postponed until every pass fails.
                        if goalDistance < float cfg.ShootingRange then
                            if spend () then
                                let score =
                                    utility
                                    + confidence
                                      * (MatchMath.xGFrom goalDistance e.ChainFinishing 1.0 - rootValues[index])

                                if score > scores[index] then
                                    if wantChains then
                                        chains[index] <- pathOf st (ValueSome Shoot)

                                    scores[index] <- score

                        let visited = arena.Visited[st]

                        for i in 0 .. n - 1 do
                            let slot = ourSlots[i]

                            if not (SlotMask.contains slot visited) && not (SlotMask.contains slot ourGk) then
                                if spend () then
                                    let next = pass st slot

                                    if next >= 0 then
                                        arena.Score[next] <- utility + confidence * (valueOf next - rootValues[index])
                                        arena.Visited[next] <- SlotMask.add slot visited
                                        branch[branches] <- next
                                        branches <- branches + 1

                        if spend () then
                            let next = carry st

                            if next >= 0 then
                                arena.Score[next] <- utility + confidence * (valueOf next - rootValues[index])
                                arena.Visited[next] <- visited
                                branch[branches] <- next
                                branches <- branches + 1

                    // Stable top-k by score, descending: a later state ranks after every kept one it ties with.
                    let k = min beamWidth branches
                    let best = sc.Best
                    let mutable kept = 0

                    for b in 0 .. branches - 1 do
                        let st = branch[b]
                        let score = arena.Score[st]
                        let mutable pos = kept

                        while pos > 0 && arena.Score[best[pos - 1]] < score do
                            pos <- pos - 1

                        if pos < k then
                            let mutable q = min kept (k - 1)

                            while q > pos do
                                best[q] <- best[q - 1]
                                q <- q - 1

                            best[pos] <- st

                            if kept < k then
                                kept <- kept + 1

                    for b in 0 .. kept - 1 do
                        let st = best[b]

                        if arena.Score[st] > scores[index] then
                            scores[index] <- arena.Score[st]

                            if wantChains then
                                chains[index] <- pathOf st ValueNone

                    for b in 0 .. kept - 1 do
                        beamStore[beamStart[index] + b] <- best[b]

                    beamLen[index] <- kept
                    frontierLimit <- cfg.MaxEvaluateLimit
                    work[index] <- work[index] + used - before

        result.Ensure count
        result.Count <- count

        for index in 0 .. count - 1 do
            result.Value[index] <- scores[index]
            result.ActionCode[index] <- opts.Code[index]
            result.TargetSlot[index] <- opts.Target[index]
            result.CandidateEvaluations[index] <- work[index]

    /// Loads a hand-built or projected state into a root. Motion is resolved per position exactly as `moved` does:
    /// the same index first, then a scan, so states with any order agree with it.
    let private loadRoot (s: ChainState) (r: ChainRoot) =
        let n, m = s.OurPos.Length, s.OppPos.Length
        r.Ensure(n, m)
        r.OurCount <- n
        r.OppCount <- m
        r.BallX <- float s.BallX
        r.BallY <- float s.BallY
        r.CarrierSlot <- s.CarrierSlot
        r.Side <- s.Side
        r.OffsideLine <- float s.OffsideLine
        r.Dir <- s.Dir
        r.Horizon <- float s.Horizon
        r.Confidence <- s.Confidence
        r.GoalkeeperSlots <- s.GoalkeeperSlots
        r.OurGoalkeeperSlots <- s.OurGoalkeeperSlots
        r.CarrySpeed <- float s.CarrySpeed
        r.HasOppMotion <- s.OppMotion.Length > 0

        let resolve
            (positions: struct (int * float<meter> * float<meter>)[])
            (motions: struct (int * float<meterPerSecond> * float<meterPerSecond>)[])
            (slots: int[])
            (px: float[])
            (py: float[])
            (mx: float[])
            (my: float[])
            (moves: bool[])
            =
            for i in 0 .. positions.Length - 1 do
                let struct (slot, x, y) = positions[i]
                slots[i] <- slot
                px[i] <- float x
                py[i] <- float y
                mx[i] <- 0.0
                my[i] <- 0.0
                moves[i] <- false

                let mutable index =
                    if i < motions.Length && (let struct (k, _, _) = motions[i] in k = slot) then
                        i
                    else
                        0

                let mutable found = false

                while index < motions.Length && not found do
                    let struct (k, vx, vy) = motions[index]

                    if k = slot then
                        mx[i] <- float vx
                        my[i] <- float vy
                        moves[i] <- true
                        found <- true

                    index <- index + 1

        resolve s.OurPos s.OurMotion r.OurSlot r.OurX r.OurY r.OurVx r.OurVy r.OurMoves
        resolve s.OppPos s.OppMotion r.OppSlot r.OppX r.OppY r.OppVx r.OppVy r.OppMoves

    let search
        (cfg: PlannerConfig)
        (e: ExpectedValueConfig)
        (pparams: InterceptTable.InterceptParams)
        (s: ChainState)
        (options: (int * int * float)[])
        : ChainEvaluation[] =
        let root = ChainRoot()
        loadRoot s root
        let opts = PlanOptions()

        for (code, target, utility) in options do
            opts.Add(utility, code, target)

        let result = PlanResult()
        let chains = Array.create<ChainAction[]> options.Length [||]
        run cfg e pparams s.PassBall s.PassArrivalSpeed s.PassArrivalSpeedGain s.MaxPassDistance root opts result chains

        options
        |> Array.mapi (fun index (code, target, _) ->
            { ActionCode = code
              TargetSlot = target
              Chain = chains[index]
              Value = result.Value[index]
              CandidateEvaluations = result.CandidateEvaluations[index] })

    /// The production search: the same algorithm over a slot's own root and options, writing no chains and
    /// allocating nothing once its buffers have grown.
    let searchInto (config: BalanceConfig) (pparams: InterceptTable.InterceptParams) (plan: PlanSlot) =
        run
            config.Planner
            config.ExpectedValue
            pparams
            config.Ball
            config.Actions.PassArrivalSpeed
            config.Actions.PassArrivalSpeedGain
            config.Actions.MaxPassExecutionDistance
            plan.Root
            plan.Options
            plan.Result
            [||]

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

                   let cap =
                       float ctx.Config.Movement.SprintSpeed
                       * (1.0 - (1.0 - float f.Condition[s]) * ctx.Config.Movement.FatigueSpeedDecay)

                   let scale = if speed > cap && speed > 0.0 then cap / speed else 1.0

                   struct (s,
                           float f.VelX[s] * scale * 1.0<meterPerSecond>,
                           float f.VelY[s] * scale * 1.0<meterPerSecond>) |]

        let profile = MatchWorld.profileOf ctx side slot

        let confidence =
            ctx.Config.CollectiveAttack.AnticipationConfidence * float profile.Anticipation
            / 100.0
            * float frame.Cognitive[slot]
            * float frame.Condition[slot]

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
          GoalkeeperSlots =
            MatchWorld.activeSlots oppFrame
            |> Array.filter (fun slot -> oppFrame.SlotRoles[slot] = SlotRole.Goalkeeper)
            |> SlotMask.ofSeq
          OurGoalkeeperSlots =
            MatchWorld.activeSlots frame
            |> Array.filter (fun slot -> frame.SlotRoles[slot] = SlotRole.Goalkeeper)
            |> SlotMask.ofSeq
          PassBall = ctx.Config.Ball
          PassArrivalSpeed = ctx.Config.Actions.PassArrivalSpeed
          PassArrivalSpeedGain = ctx.Config.Actions.PassArrivalSpeedGain
          MaxPassDistance = ctx.Config.Actions.MaxPassExecutionDistance
          CarrySpeed =
            ctx.Config.Movement.SprintSpeed * float profile.Dribbling / 100.0
            * float frame.Condition[slot] }

    /// `project` into a root's own arrays: the same values and arithmetic, no allocation once they have grown.
    let projectInto (root: ChainRoot) (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let oppFrame = (MatchWorld.team world (MatchWorld.oppOf side)).Frame
        let ours = MatchWorld.activeSlots frame
        let opponents = MatchWorld.activeSlots oppFrame
        root.Ensure(ours.Length, opponents.Length)
        root.OurCount <- ours.Length
        root.OppCount <- opponents.Length
        root.BallX <- float world.State.Ball.Position.X
        root.BallY <- float world.State.Ball.Position.Y
        root.CarrierSlot <- slot
        root.Side <- side
        root.OffsideLine <- float (MatchWorld.offsideLine ctx world side)
        root.Dir <- MatchMath.goalDirection side
        root.Horizon <- float ctx.Config.CollectiveAttack.AnticipationHorizon
        root.HasOppMotion <- opponents.Length > 0

        // A body moves no faster than its sprint, less when tired: the cap `project` applies.
        let load
            (f: TeamFrame)
            (slots: int[])
            (ids: int[])
            (px: float[])
            (py: float[])
            (vx: float[])
            (vy: float[])
            (moves: bool[])
            =
            let mutable goalkeepers = 0UL

            for i in 0 .. slots.Length - 1 do
                let s = slots[i]
                ids[i] <- s
                px[i] <- float (MatchWorld.posX f s)
                py[i] <- float (MatchWorld.posY f s)
                let speed = sqrt (float f.VelX[s] ** 2.0 + float f.VelY[s] ** 2.0)

                let cap =
                    float ctx.Config.Movement.SprintSpeed
                    * (1.0 - (1.0 - float f.Condition[s]) * ctx.Config.Movement.FatigueSpeedDecay)

                let scale = if speed > cap && speed > 0.0 then cap / speed else 1.0
                vx[i] <- float f.VelX[s] * scale
                vy[i] <- float f.VelY[s] * scale
                moves[i] <- true

                if f.SlotRoles[s] = SlotRole.Goalkeeper then
                    goalkeepers <- SlotMask.add s goalkeepers

            goalkeepers

        root.OurGoalkeeperSlots <- load frame ours root.OurSlot root.OurX root.OurY root.OurVx root.OurVy root.OurMoves

        root.GoalkeeperSlots <-
            load oppFrame opponents root.OppSlot root.OppX root.OppY root.OppVx root.OppVy root.OppMoves

        let profile = MatchWorld.profileOf ctx side slot

        root.Confidence <-
            ctx.Config.CollectiveAttack.AnticipationConfidence * float profile.Anticipation
            / 100.0
            * float frame.Cognitive[slot]
            * float frame.Condition[slot]

        root.CarrySpeed <-
            float (
                ctx.Config.Movement.SprintSpeed * float profile.Dribbling / 100.0
                * float frame.Condition[slot]
            )
