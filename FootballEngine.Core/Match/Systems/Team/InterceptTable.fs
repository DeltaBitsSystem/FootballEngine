namespace FootballEngine.Core.Match

/// RoboCup InterceptTable, adapted. Who reaches the ball first decides
/// whether the side attacks or defends, so every player is reduced to one
/// number: how many ticks until they can control the ball (stepped rolling-ball
/// model sharing BallAero's drag), plus the team aggregates (self/teammate/
/// opponent/ball step) HELIOS uses for pressing, situation, passing and
/// positioning. Pure and deterministic.
module InterceptTable =

    [<Struct>]
    type InterceptParams =
        {
            Ball: BallConfig
            PlayerSpeedPerSecond: float<meterPerSecond>
            /// The body's acceleration limit: a race from a standstill is not run at top speed from the first step.
            PlayerAcceleration: float<meterPerSecondSquared>
            ControlRadius: float<meter> // kickable area
            MaxSteps: int // horizon cap: beyond this many ticks the ball is treated as unreachable
            /// Grid step of the tabulated roll in launch speed; see `RollTable`.
            RollTableResolution: float<meterPerSecond>
            /// Fastest launch speed the table covers; zero disables the table and rolls every ball step by step.
            RollTableMaxSpeed: float<meterPerSecond>
        }

    /// The race step, shared with the planner so both integrate the same roll.
    let dt = 1.0<second> / 40.0

    /// The same limit ContactDynamics applies when it integrates a player toward his target velocity.
    let bodyAcceleration (movement: MovementConfig) =
        movement.Acceleration * max 0.1 (min 1.0 (float movement.Smoothing))

    /// Distance covered from a standstill after the given time, accelerating to top speed and then holding it.
    let reachableDistance (p: InterceptParams) (seconds: float) =
        let top = float p.PlayerSpeedPerSecond
        let acceleration = float p.PlayerAcceleration

        // Validation keeps real configs positive; a hand-built zero means the instant-top-speed idealisation.
        if acceleration <= 0.0 then
            top * seconds
        else
            let rampTime = top / acceleration

            if seconds <= rampTime then
                0.5 * acceleration * seconds * seconds
            else
                top * seconds - top * rampTime * 0.5

    let parameters (intercept: InterceptConfig) (movement: MovementConfig) (ball: BallConfig) : InterceptParams =
        // This closed-form race estimate is conservative; authoritative motion remains BallDynamics.
        { Ball = ball
          PlayerSpeedPerSecond = movement.SprintSpeed
          PlayerAcceleration = bodyAcceleration movement
          ControlRadius = intercept.ControlRadius
          MaxSteps = intercept.MaxSteps
          RollTableResolution = intercept.RollTableResolution
          RollTableMaxSpeed = intercept.RollTableMaxSpeed }

    /// Ground speed after one prediction step: turf resistance plus air drag, as BallDynamics rolls the ball.
    let private nextGroundSpeed (p: InterceptParams) speed =
        let loss =
            (p.Ball.RollingResistance * float p.Ball.Gravity
             + BallAero.dragDeceleration p.Ball speed)
            * float dt

        max 0.0 (speed - loss)

    /// Predicted rolling-ball state after n ticks; the per-step loop is bounded by MaxSteps.
    let ballInertiaPoint (p: InterceptParams) (ball: Spatial) (n: int) : Spatial =
        let mutable x, y = float ball.X, float ball.Y
        let mutable vx, vy = float ball.Vx, float ball.Vy

        // Decelerate, then move: the same order BallDynamics uses when it rolls the ball.
        for _ in 1..n do
            let speed = sqrt (vx * vx + vy * vy)

            if speed > 1e-9 then
                let scale = nextGroundSpeed p speed / speed
                vx <- vx * scale
                vy <- vy * scale

            x <- x + vx * float dt
            y <- y + vy * float dt

        { X = x * 1.0<meter>
          Y = y * 1.0<meter>
          Z = 0.0<meter>
          Vx = vx * 1.0<meterPerSecond>
          Vy = vy * 1.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

    /// Speed after one prediction step; a ball already at rest stays at rest.
    let stepSpeed (p: InterceptParams) (speed: float) =
        if speed > 1e-9 then nextGroundSpeed p speed else speed

    /// Cache identity of a roll table: every ball field `BallAero.dragDeceleration` and the turf resistance read,
    /// plus the table's own shape.
    [<Struct>]
    type private RollKey =
        { Gravity: float
          Rolling: float
          AirDensity: float
          Mass: float
          Radius: float
          DragSubcritical: float
          DragSupercritical: float
          CrisisSpeed: float
          CrisisWidth: float
          Steps: int
          Resolution: float
          MaxSpeed: float }

    /// Distance rolled after n steps as a function of launch speed, s(n; v0): the stepped 1D roll (decelerate, then
    /// move) tabulated on a uniform v0 grid so a straight roll costs one interpolation per step instead of a drag
    /// evaluation. Immutable once built, so matches on any thread share it.
    ///
    /// A node whose ball stops inside the horizon makes s(n; v0) kinked in v0 (the speed is clipped at zero), where
    /// linear interpolation is two orders of magnitude worse than in smooth cells; such cells are not served and
    /// the caller steps the roll exactly instead.
    [<Sealed; AllowNullLiteral>]
    type RollTable internal (p: InterceptParams) =
        let resolution = float p.RollTableResolution
        let steps = p.MaxSteps
        let width = steps + 1

        let nodes =
            if resolution > 0.0 && float p.RollTableMaxSpeed > 0.0 then
                int (float p.RollTableMaxSpeed / resolution) + 1
            else
                0

        let distance = Array.zeroCreate<float> (nodes * width)
        // First step at which the node's ball is at rest, or -1 when it still rolls at the horizon.
        let stop = Array.create nodes -1

        do
            for j in 0 .. nodes - 1 do
                let mutable v = float j * resolution
                let mutable s = 0.0
                let row = j * width

                if v <= 1e-9 then
                    stop[j] <- 0

                for n in 1..steps do
                    v <- stepSpeed p v
                    s <- s + v * float dt
                    distance[row + n] <- s

                    if v <= 1e-9 && stop[j] < 0 then
                        stop[j] <- n

        /// The cell holding this launch speed, or -1 when it must be rolled step by step: at rest, past the table, or
        /// in a cell where either node stops inside the horizon.
        member _.Cell(v0: float) : int =
            if nodes < 2 || not (v0 > 0.0) then
                -1
            else
                let j = int (v0 / resolution)

                if j + 1 >= nodes || stop[j] >= 0 || stop[j + 1] >= 0 then
                    -1
                else
                    j

        member _.At(cell: int, v0: float, n: int) : float =
            let f = (v0 - float cell * resolution) / resolution
            let lower = cell * width + n
            distance[lower] * (1.0 - f) + distance[lower + width] * f

    [<AbstractClass; Sealed>]
    type RollTables =
        static let tables =
            System.Collections.Concurrent.ConcurrentDictionary<RollKey, RollTable>()

        // The last table a thread used: a match keeps one ball config, so the dictionary is rarely consulted.
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private lastKey: RollKey

        [<System.ThreadStatic; DefaultValue>]
        static val mutable private lastTable: RollTable

        static member For(p: InterceptParams) : RollTable =
            let b = p.Ball

            let key =
                { Gravity = float b.Gravity
                  Rolling = b.RollingResistance
                  AirDensity = b.AirDensity
                  Mass = b.Mass
                  Radius = float b.Radius
                  DragSubcritical = b.DragSubcritical
                  DragSupercritical = b.DragSupercritical
                  CrisisSpeed = float b.DragCrisisSpeed
                  CrisisWidth = float b.DragCrisisWidth
                  Steps = p.MaxSteps
                  Resolution = float p.RollTableResolution
                  MaxSpeed = float p.RollTableMaxSpeed }

            if not (isNull RollTables.lastTable) && RollTables.lastKey = key then
                RollTables.lastTable
            else
                // A table is ~1.8 MB and a sweep over ball parameters makes one per variant; dropping them all past
                // a bound keeps memory flat, and threads still holding an evicted table keep using it safely.
                if tables.Count >= 32 then
                    tables.Clear()

                let table = tables.GetOrAdd(key, fun _ -> RollTable p)
                RollTables.lastKey <- key
                RollTables.lastTable <- table
                table

    /// Smallest n in [0..MaxSteps] such that the player, running straight at
    /// the ball at PlayerSpeed, is within ControlRadius of the ball's inertia
    /// point at tick n. Equivalently: dist(player, ballAt(n)) - ControlRadius
    /// <= speed·dt·n. The reachable predicate is monotonic in n, so scanning
    /// upward and taking the first hit is exact; unreachable balls return
    /// MaxSteps, keeping the result deterministic. The roll is the exact stepped
    /// 1D roll along the launch direction, the reference the tabulated one is held to.
    let stepsToReach (p: InterceptParams) (ball: Spatial) (px: float<meter>) (py: float<meter>) : int =
        let control = float p.ControlRadius
        let stepSeconds = float dt
        let startX = float ball.X
        let startY = float ball.Y
        let launch = sqrt (float ball.Vx * float ball.Vx + float ball.Vy * float ball.Vy)
        let dirX = if launch > 0.0 then float ball.Vx / launch else 0.0
        let dirY = if launch > 0.0 then float ball.Vy / launch else 0.0
        let mutable speed = launch
        let mutable rolled = 0.0
        let playerX = float px
        let playerY = float py
        let mutable n = 0
        let mutable reached = false

        while not reached && n <= p.MaxSteps do
            let dx = playerX - (startX + dirX * rolled)
            let dy = playerY - (startY + dirY * rolled)
            let reach = control + reachableDistance p (float n * stepSeconds)

            if dx * dx + dy * dy <= reach * reach then
                reached <- true
            else
                speed <- stepSpeed p speed
                rolled <- rolled + speed * stepSeconds
                n <- n + 1

        if reached then n else p.MaxSteps

    /// The roll `stepsToReach` integrates, shared by every player raced against the same ball: the per-step drag
    /// dominated pass and pressing evaluation, and was being recomputed once per player. Steps are integrated
    /// lazily up to the furthest any query needs, with the same operations in the same order, so `StepsFor` equals
    /// `stepsToReach` for every player.
    [<Struct>]
    type private ReachKey =
        { Speed: float<meterPerSecond>
          Acceleration: float<meterPerSecondSquared>
          Control: float<meter>
          Steps: int }

    /// A player's reach after n steps depends only on the race parameters, not on the ball, so every rolled ball
    /// of the same parameters shares one table. Tables are immutable and keyed by their parameters: `sweep` runs
    /// matches with different configs in parallel, and a single mutable slot would make them evict each other.
    [<AbstractClass; Sealed>]
    type private ReachTables =
        static let tables = System.Collections.Concurrent.ConcurrentDictionary<ReachKey, float[]>()

        // The last table a thread used: a match keeps one parameter set, so the dictionary is rarely consulted.
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private lastKey: ReachKey

        [<System.ThreadStatic; DefaultValue>]
        static val mutable private lastTable: float[]

        static member For(p: InterceptParams) : float[] =
            let key =
                { Speed = p.PlayerSpeedPerSecond
                  Acceleration = p.PlayerAcceleration
                  Control = p.ControlRadius
                  Steps = p.MaxSteps }

            if not (isNull ReachTables.lastTable) && ReachTables.lastKey = key then
                ReachTables.lastTable
            else
                let table =
                    tables.GetOrAdd(
                        key,
                        fun _ ->
                            let control = float p.ControlRadius
                            Array.init (p.MaxSteps + 1) (fun n -> control + reachableDistance p (float n * float dt))
                    )

                ReachTables.lastKey <- key
                ReachTables.lastTable <- table
                table

    /// Slack on the exclusion distance: the rolled positions drift from the ideal straight line by rounding
    /// (below 1e-12 m over a 50-step roll), so a body this close to the limit is still scanned.
    [<Literal>]
    let private ExclusionSlack = 1e-6

    [<Sealed>]
    type RolledBall(initial: InterceptParams, ball: Spatial) =
        let mutable p = initial
        let mutable xs = Array.zeroCreate<float> (p.MaxSteps + 1)
        let mutable ys = Array.zeroCreate<float> (p.MaxSteps + 1)
        let mutable reaches = ReachTables.For p
        let mutable filled = -1
        let mutable ballX = float ball.X
        let mutable ballY = float ball.Y
        let mutable velocityX = float ball.Vx
        let mutable velocityY = float ball.Vy
        let mutable table = RollTables.For p
        // The launch speed and, for a ball rolled step by step, its current speed and distance rolled.
        let mutable launch = 0.0
        let mutable speed = 0.0
        let mutable rolled = 0.0
        // The table cell serving this launch speed, or -1 when the roll is stepped exactly.
        let mutable cell = -1
        // The roll is a straight segment from the start: both velocity components are scaled by the same factor
        // each step and the speed never grows, so the path is no longer than speed * dt * MaxSteps.
        let mutable startX = 0.0
        let mutable startY = 0.0
        let mutable dirX = 0.0
        let mutable dirY = 0.0
        let mutable pathLength = 0.0
        // No scanned step can bring a body within this distance of the segment, or it is out of reach at every step.
        let mutable cutoff = 0.0
        let mutable bodyX: float[] = Array.empty
        let mutable bodyY: float[] = Array.empty
        let mutable alive: int[] = Array.empty

        let aim () =
            startX <- ballX
            startY <- ballY
            launch <- sqrt (velocityX * velocityX + velocityY * velocityY)

            if launch > 0.0 then
                dirX <- velocityX / launch
                dirY <- velocityY / launch
            else
                dirX <- 0.0
                dirY <- 0.0

            speed <- launch
            rolled <- 0.0
            cell <- table.Cell launch
            pathLength <- launch * float dt * float p.MaxSteps
            cutoff <- reaches[p.MaxSteps] + ExclusionSlack

        do aim ()

        /// Whether the body is too far from the whole roll to reach it at any scanned step.
        let outOfRange (playerX: float) (playerY: float) =
            let along = (playerX - startX) * dirX + (playerY - startY) * dirY

            let t =
                if along < 0.0 then 0.0
                elif along > pathLength then pathLength
                else along

            let dx = playerX - (startX + dirX * t)
            let dy = playerY - (startY + dirY * t)
            sqrt (dx * dx + dy * dy) > cutoff

        let fillTo n =
            while filled < n do
                filled <- filled + 1

                if filled > 0 then
                    if cell >= 0 then
                        rolled <- table.At(cell, launch, filled)
                    else
                        speed <- stepSpeed p speed
                        rolled <- rolled + speed * float dt

                xs[filled] <- startX + dirX * rolled
                ys[filled] <- startY + dirY * rolled

        /// Re-aims the roll at a new ball, keeping the buffers: passes and intercepts are raced thousands of times
        /// a match, and two fresh arrays per race were a sixth of a match's allocations.
        member _.Reset(parameters: InterceptParams, next: Spatial) =
            if parameters.MaxSteps + 1 > xs.Length then
                xs <- Array.zeroCreate (parameters.MaxSteps + 1)
                ys <- Array.zeroCreate (parameters.MaxSteps + 1)

            p <- parameters
            reaches <- ReachTables.For parameters
            table <- RollTables.For parameters
            filled <- -1
            ballX <- float next.X
            ballY <- float next.Y
            velocityX <- float next.Vx
            velocityY <- float next.Vy
            aim ()

        /// Scratch coordinates for `FirstArrival` callers, owned by the roll so a race gathers its bodies without
        /// allocating.
        member _.Bodies(capacity: int) : struct (float[] * float[]) =
            if bodyX.Length < capacity then
                bodyX <- Array.zeroCreate capacity
                bodyY <- Array.zeroCreate capacity
                alive <- Array.zeroCreate capacity

            struct (bodyX, bodyY)

        /// Whether the player reaches the ball within `limit` steps: the same test as `StepsFor(px, py) <= limit`,
        /// without rolling the ball past a step that can no longer change the verdict.
        member _.ReachesWithin(px: float<meter>, py: float<meter>, limit: int) : bool =
            let playerX = float px
            let playerY = float py

            if outOfRange playerX playerY then
                limit >= p.MaxSteps
            else
                let last = min limit p.MaxSteps
                let mutable n = 0
                let mutable reached = false

                while not reached && n <= last do
                    if n > filled then
                        fillTo n

                    let dx = playerX - xs[n]
                    let dy = playerY - ys[n]
                    let reach = reaches[n]

                    if dx * dx + dy * dy <= reach * reach then
                        reached <- true

                    n <- n + 1

                reached || limit >= p.MaxSteps

        member _.StepsFor(px: float<meter>, py: float<meter>) : int =
            let playerX = float px
            let playerY = float py

            if outOfRange playerX playerY then
                p.MaxSteps
            else
                let mutable n = 0
                let mutable result = p.MaxSteps
                let mutable reached = false

                while not reached && n <= p.MaxSteps do
                    if n > filled then
                        fillTo n

                    let dx = playerX - xs[n]
                    let dy = playerY - ys[n]
                    let reach = reaches[n]

                    if dx * dx + dy * dy <= reach * reach then
                        reached <- true
                        result <- n

                    n <- n + 1

                result

        /// min(limit, the smallest `StepsFor` over the first `count` bodies): every body that can still reach is
        /// checked at each step, so the scan stops at the first arrival instead of running each body to its end.
        member _.FirstArrival(px: float[], py: float[], count: int, limit: int) : int =
            if count <= 0 then
                limit
            else
                if alive.Length < count then
                    alive <- Array.zeroCreate count

                let horizon = min limit p.MaxSteps
                let mutable remaining = 0

                for i in 0 .. count - 1 do
                    if not (outOfRange px[i] py[i]) then
                        alive[remaining] <- i
                        remaining <- remaining + 1

                let mutable n = 0
                let mutable found = false

                while not found && remaining > 0 && n <= horizon do
                    if n > filled then
                        fillTo n

                    let rollX = xs[n]
                    let rollY = ys[n]
                    let reach = reaches[n]
                    let mutable k = 0

                    while not found && k < remaining do
                        let i = alive[k]
                        let dx = px[i] - rollX
                        let dy = py[i] - rollY

                        if dx * dx + dy * dy <= reach * reach then
                            found <- true

                        k <- k + 1

                    if not found then
                        n <- n + 1

                if found then n else horizon

    /// Rolled balls lent per thread: matches run in parallel, and a race never outlives the call that rents it.
    [<AbstractClass; Sealed>]
    type RolledBalls =
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private free: System.Collections.Generic.Stack<RolledBall>

        static member Rent(p: InterceptParams, ball: Spatial) =
            if isNull RolledBalls.free then
                RolledBalls.free <- System.Collections.Generic.Stack()

            if RolledBalls.free.Count > 0 then
                let rolled = RolledBalls.free.Pop()
                rolled.Reset(p, ball)
                rolled
            else
                RolledBall(p, ball)

        static member Return(rolled: RolledBall) = RolledBalls.free.Push rolled

    /// Per-team aggregate: the minimum steps define who is first to the ball,
    /// which is what the pressing and situation logic compares. SelfStep and
    /// TeammateStep are the min over the side's active outfielders (GK
    /// excluded — a keeper's reach is not comparable to an outfielder's),
    /// OpponentStep the min over the opponent's, and BallStep = min(self,
    /// teammate, opponent). SlotSteps holds every active slot's own
    /// stepsToReach, the keeper's included so he can be sent to a loose ball in
    /// his area; sidelined or inactive slots get MaxSteps.
    let compute (p: InterceptParams) (world: World) (side: ClubSide) : InterceptState =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps
        let rolled = RolledBall(p, ball)

        let sideInfo (side: ClubSide) : int[] * int * int * int =
            // Single pass: fill SlotSteps and track the team aggregate minima
            // (self, best slot, next-best) without allocating filter/map arrays.
            let frame = (MatchWorld.team world side).Frame
            let steps = Array.create frame.SlotCount maxSteps
            let mutable bestSlot = -1
            let mutable bestStep = maxSteps
            let mutable secondStep = maxSteps
            let mutable found = false

            for slot in MatchWorld.activeSlots frame do
                let s = rolled.StepsFor(MatchWorld.posX frame slot, MatchWorld.posY frame slot)
                steps.[slot] <- s

                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper then
                    found <- true

                    if s < bestStep then
                        secondStep <- bestStep
                        bestStep <- s
                        bestSlot <- slot
                    elif s < secondStep then
                        secondStep <- s

            steps, (if found then bestStep else maxSteps), bestSlot, (if found then secondStep else maxSteps)

        let ourSteps, selfStep, bestSlot, teammateStep = sideInfo side
        let _, opponentStep, _, _ = sideInfo (MatchWorld.oppOf side)

        let ballStep = min selfStep (min teammateStep opponentStep)

        { ComputedAt = world.State.Tick
          SelfStep = selfStep
          TeammateStep = teammateStep
          OpponentStep = opponentStep
          BallStep = ballStep
          SlotSteps = ourSteps }

    let computeBoth (p: InterceptParams) (world: World) : InterceptState * InterceptState =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps
        let rolled = RolledBall(p, ball)

        let sideInfo side =
            let frame = (MatchWorld.team world side).Frame
            let steps = Array.create frame.SlotCount maxSteps
            let mutable bestSlot = -1
            let mutable bestStep = maxSteps
            let mutable secondStep = maxSteps

            for slot in MatchWorld.activeSlots frame do
                let step = rolled.StepsFor(MatchWorld.posX frame slot, MatchWorld.posY frame slot)
                steps.[slot] <- step

                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper then
                    if step < bestStep then
                        secondStep <- bestStep
                        bestStep <- step
                        bestSlot <- slot
                    elif step < secondStep then
                        secondStep <- step

            steps, bestStep, bestSlot, secondStep

        let homeSteps, homeBest, homeSlot, homeSecond = sideInfo ClubSide.Home
        let awaySteps, awayBest, awaySlot, awaySecond = sideInfo ClubSide.Away

        let state steps ownBest ownSlot ownSecond opponentBest =
            { ComputedAt = world.State.Tick
              SelfStep = ownBest
              TeammateStep = ownSecond
              OpponentStep = opponentBest
              BallStep = min ownBest (min ownSecond opponentBest)
              SlotSteps = steps }

        state homeSteps homeBest homeSlot homeSecond awayBest, state awaySteps awayBest awaySlot awaySecond homeBest

    let computeBothInto (p: InterceptParams) (world: World) : unit =
        let ball = world.State.Ball.Position
        let maxSteps = p.MaxSteps
        let rolled = RolledBalls.Rent(p, ball)

        let sideInfo side (steps: int[]) =
            let frame = (MatchWorld.team world side).Frame
            Array.fill steps 0 steps.Length maxSteps
            let mutable bestStep = maxSteps
            let mutable secondStep = maxSteps

            for slot in MatchWorld.activeSlots frame do
                let step = rolled.StepsFor(MatchWorld.posX frame slot, MatchWorld.posY frame slot)
                steps.[slot] <- step

                if frame.SlotRoles[slot] <> SlotRole.Goalkeeper then
                    if step < bestStep then
                        secondStep <- bestStep
                        bestStep <- step
                    elif step < secondStep then
                        secondStep <- step

            struct (bestStep, secondStep)

        let homeSteps = world.Home.Intercept.SlotSteps
        let awaySteps = world.Away.Intercept.SlotSteps
        let struct (homeBest, homeSecond) = sideInfo ClubSide.Home homeSteps
        let struct (awayBest, awaySecond) = sideInfo ClubSide.Away awaySteps
        RolledBalls.Return rolled

        world.Home.Intercept <-
            { ComputedAt = world.State.Tick
              SelfStep = homeBest
              TeammateStep = homeSecond
              OpponentStep = awayBest
              BallStep = min homeBest (min homeSecond awayBest)
              SlotSteps = homeSteps }

        world.Away.Intercept <-
            { ComputedAt = world.State.Tick
              SelfStep = awayBest
              TeammateStep = awaySecond
              OpponentStep = homeBest
              BallStep = min awayBest (min awaySecond homeBest)
              SlotSteps = awaySteps }
