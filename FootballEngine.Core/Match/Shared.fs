namespace FootballEngine.Core.Match

/// Pure math helpers shared by all systems. No state, no globals.
module MatchMath =

    let inline clamp (lo: float) (hi: float) (v: float) : float = min hi (max lo v)

    let clamp01 (v: float) : float = clamp 0.0 1.0 v

    let inline clampF32 (lo: float32) (hi: float32) (v: float32) : float32 = min hi (max lo v)

    /// Clamp a point to the pitch boundary. Used for any standing target so a
    /// player is never pulled off the pitch by a loose ball or a restart.
    let inline pitchClampF32 (x: float32) (y: float32) : float32 * float32 =
        clampF32 (-(float32 Pitch.HalfLength)) (float32 Pitch.HalfLength) x,
        clampF32 (-(float32 Pitch.HalfWidth)) (float32 Pitch.HalfWidth) y

    let inline pitchClamp (x: float) (y: float) : float * float =
        clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) x,
        clamp (-(float Pitch.HalfWidth)) (float Pitch.HalfWidth) y

    let sigmoid (k: float) (x: float) : float = 1.0 / (1.0 + exp (-k * x))

    let dist (ax: float<meter>) (ay: float<meter>) (bx: float<meter>) (by: float<meter>) : float =
        let dx = float ax - float bx
        let dy = float ay - float by
        sqrt (dx * dx + dy * dy)

    let distF32 (ax: float32) (ay: float32) (bx: float32) (by: float32) : float =
        let dx = float ax - float bx
        let dy = float ay - float by
        sqrt (dx * dx + dy * dy)

    let speedOf (vx: float<meterPerSecond>) (vy: float<meterPerSecond>) : float =
        let vx = float vx
        let vy = float vy
        sqrt (vx * vx + vy * vy)

    let lerp (a: float) (b: float) (t: float) : float = a + (b - a) * t

    let signOf (v: float) : float = if v < 0.0 then -1.0 else 1.0

    /// Home attacks +X (goal at +52.5); away attacks -X.
    let goalX (side: ClubSide) : float<meter> =
        match side with
        | ClubSide.Home -> Pitch.awayGoalX
        | ClubSide.Away -> Pitch.homeGoalX

    let ownGoalX (side: ClubSide) : float<meter> =
        match side with
        | ClubSide.Home -> Pitch.homeGoalX
        | ClubSide.Away -> Pitch.awayGoalX

    let goalDirection (side: ClubSide) : float =
        if side = ClubSide.Home then 1.0 else -1.0

    let distanceToGoal (side: ClubSide) (x: float<meter>) (y: float<meter>) : float = dist x y (goalX side) 0.0<meter>

/// World/team access helpers. Every system reads positions/teams through here.
module MatchWorld =

    let team (world: World) (side: ClubSide) : TeamWorld =
        if side = ClubSide.Home then world.Home else world.Away

    let oppOf (side: ClubSide) : ClubSide =
        if side = ClubSide.Home then
            ClubSide.Away
        else
            ClubSide.Home

    let lineup (ctx: MatchContext) (side: ClubSide) : Lineup =
        if side = ClubSide.Home then
            ctx.HomeLineup
        else
            ctx.AwayLineup

    let teamOfPlayer (ctx: MatchContext) (pid: PlayerId) : ClubSide option =
        if ctx.HomeLineup.Profiles |> Array.exists (fun p -> p.Id = pid) then
            Some ClubSide.Home
        elif ctx.AwayLineup.Profiles |> Array.exists (fun p -> p.Id = pid) then
            Some ClubSide.Away
        else
            None

    let slotOf (ctx: MatchContext) (side: ClubSide) (pid: PlayerId) : int option =
        (lineup ctx side).Profiles |> Array.tryFindIndex (fun p -> p.Id = pid)

    let profileOf (ctx: MatchContext) (side: ClubSide) (slot: int) : PlayerProfile = (lineup ctx side).Profiles.[slot]

    let isActive (frame: TeamFrame) (slot: int) : bool =
        match frame.Occupancy.[slot] with
        | OccupancyKind.Active _ -> true
        | _ -> false

    let activeSlots (frame: TeamFrame) : int[] =
        frame.Occupancy
        |> Array.choose (fun o ->
            match o with
            | OccupancyKind.Active s -> Some s
            | _ -> None)

    let posX (frame: TeamFrame) (slot: int) : float<meter> = float frame.PosX.[slot] * 1.0<meter>

    let posY (frame: TeamFrame) (slot: int) : float<meter> = float frame.PosY.[slot] * 1.0<meter>

    let position (frame: TeamFrame) (slot: int) : Spatial =
        { X = posX frame slot
          Y = posY frame slot
          Z = 0.0<meter>
          Vx = float frame.VelX.[slot] * 1.0<meterPerSecond>
          Vy = float frame.VelY.[slot] * 1.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

    let ballCarrier (ctx: MatchContext) (world: World) : (ClubSide * int * PlayerId) option =
        match world.State.Ball.Control with
        | BallControl.Controlled(side, pid) -> slotOf ctx side pid |> Option.map (fun slot -> side, slot, pid)
        | _ -> None

    let lastTouchSide (ctx: MatchContext) (world: World) : ClubSide option =
        match world.State.Ball.LastTouch with
        | Some pid -> teamOfPlayer ctx pid
        | None -> None

    let nearestActive
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (x: float<meter>)
        (y: float<meter>)
        (maxDist: float)
        : (int * float) option =
        let frame = (team world side).Frame
        let mutable bestSlot = -1
        let mutable bestD = maxDist

        for slot in activeSlots frame do
            let d = MatchMath.dist x y (posX frame slot) (posY frame slot)

            if d < bestD then
                bestD <- d
                bestSlot <- slot

        if bestSlot < 0 then None else Some(bestSlot, bestD)

    /// Nearest opponent within maxDist, if any.
    let nearestOpponent
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (x: float<meter>)
        (y: float<meter>)
        (maxDist: float)
        : int option =
        let frame = (team world (oppOf side)).Frame
        let mutable best = -1
        let mutable bestD = maxDist

        for slot in activeSlots frame do
            let d = MatchMath.dist x y (posX frame slot) (posY frame slot)

            if d < bestD then
                bestD <- d
                best <- slot

        if best < 0 then None else Some best

    /// Count of opponents within maxDist of (x, y).
    let opponentsNear
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (x: float<meter>)
        (y: float<meter>)
        (maxDist: float)
        : int =
        let frame = (team world (oppOf side)).Frame
        let mutable count = 0

        for slot in activeSlots frame do
            if MatchMath.dist x y (posX frame slot) (posY frame slot) <= maxDist then
                count <- count + 1

        count

    /// Last defending line X of a team (max X for Home defending away-goal... uses side of the DEFENDERS).
    /// For attackers of `attackSide`: their offside line is the deepest opponent (excluding GK) X toward their own goal.
    let offsideLine (ctx: MatchContext) (world: World) (attackSide: ClubSide) : float<meter> =
        let frame = (team world (oppOf attackSide)).Frame
        let dir = MatchMath.goalDirection attackSide
        let mutable line = -Pitch.HalfLength

        for slot in activeSlots frame do
            if slot <> 0 then
                let x = posX frame slot

                if dir > 0.0 then
                    if float x > float line then
                        line <- x
                elif float x < float line then
                    line <- x

        line

    let teamMomentum (world: World) (side: ClubSide) : float =
        match side with
        | ClubSide.Home -> max 0.0 world.State.Momentum
        | ClubSide.Away -> max 0.0 (-world.State.Momentum)

    /// BallState is a ref record with immutable fields — every ball write goes through this helper.
    let updateBall (world: World) (f: BallState -> BallState) : unit =
        world.State <-
            { world.State with
                Ball = f world.State.Ball }
