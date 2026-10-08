namespace FootballEngine.Core.Match

/// The declarative bridge between cognition and motion: what each player is
/// trying to do and where they want to go. Intent only describes — steering is
/// the sole consumer that turns it into movement — so how a player thinks is
/// never tangled with how he walks.
module PlayerIntent =

    let private setIntent
        (frame: TeamFrame)
        (slot: int)
        (kind: byte)
        (tx: float32)
        (ty: float32)
        (tick: int<subtick>)
        (duration: int<subtick>)
        : unit =
        let currentKind = frame.Intent.Kind.[slot]

        let currentExpired =
            not (IntentCode.isContinuous currentKind)
            && frame.Intent.Phase.[slot] = IntentPhase.Committed
            && frame.Intent.CommittedUntil.[slot] < tick

        let canReplace =
            currentKind = IntentCode.None
            || frame.Intent.Phase.[slot] = IntentPhase.Completed
            || currentExpired
            || (IntentCode.isContinuous kind && IntentCode.isContinuous currentKind)

        if canReplace then
            frame.Intent.Kind.[slot] <- kind
            frame.Intent.TargetX.[slot] <- max (-float32 Pitch.HalfLength) (min (float32 Pitch.HalfLength) tx)
            frame.Intent.TargetY.[slot] <- max (-float32 Pitch.HalfWidth) (min (float32 Pitch.HalfWidth) ty)

            frame.Intent.Phase.[slot] <-
                if IntentCode.isContinuous kind then
                    IntentPhase.Executing
                else
                    IntentPhase.Committed

            frame.Intent.CommittedAt.[slot] <- tick

            frame.Intent.CommittedUntil.[slot] <-
                if IntentCode.isContinuous kind then
                    tick
                else
                    tick + duration

            frame.Intent.ExitTrigger.[slot] <- ExitTrigger.None

    let private pressSpot (ctx: MatchContext) (world: World) (side: ClubSide) : float32 * float32 =
        let ball = world.State.Ball.Position
        let team = MatchWorld.team world side

        let pressZone =
            14.0 * float team.Blackboard.OpponentPressure / 6.0 |> MatchMath.clamp 8.0 16.0

        let tx =
            MatchMath.clamp
                (-(float Pitch.HalfLength))
                (float Pitch.HalfLength)
                (float ball.X + pressZone * float (MatchMath.goalDirection side))

        float32 tx, float32 ball.Y

    let private insetTarget (ctx: MatchContext) (x: float) (y: float) : float32 * float32 =
        let inset = float ctx.Config.Positioning.TargetInset
        let halfLength = float Pitch.HalfLength - inset
        let halfWidth = float Pitch.HalfWidth - inset
        float32 (MatchMath.clamp (-halfLength) halfLength x), float32 (MatchMath.clamp (-halfWidth) halfWidth y)

    let private carryTarget (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : float32 * float32 =
        let team = MatchWorld.team world side
        let opponents = MatchWorld.team world (MatchWorld.oppOf side)
        let frame = team.Frame
        let dir = MatchMath.goalDirection side
        let px = float (MatchWorld.posX frame slot)
        let py = float (MatchWorld.posY frame slot)
        let forward = float ctx.Config.Positioning.SupportForwardDistance
        let lateral = float ctx.Config.Positioning.CarrySearchRadius
        let roomAhead = (float (MatchMath.goalX side) - px) * dir
        let forwardStep = if roomAhead < forward then -forward * 0.5 else forward
        let preferred = if py >= 0.0 then -1.0 else 1.0
        let safeHalfWidth = float Pitch.HalfWidth - float ctx.Config.Positioning.TargetInset

        let wasMovingInward =
            frame.Intent.Kind.[slot] = IntentCode.BallCarry
            && abs (float frame.Intent.TargetY.[slot]) < abs py

        // Keep the inward escape until there is room for a complete lateral
        // stride. A single threshold made the target flip on every refresh.
        let forceInward =
            abs py >= safeHalfWidth - lateral
            || (wasMovingInward && abs py > safeHalfWidth - lateral * 2.0)

        let mutable bestX = px * dir + forwardStep
        let mutable bestY = py + preferred * lateral
        let mutable bestSpace = -System.Double.MaxValue

        for candidate in 0..1 do
            let sign =
                if forceInward then preferred
                elif candidate = 0 then preferred
                else -preferred

            let tx, ty = insetTarget ctx (px * dir + forwardStep) (py + sign * lateral)
            let mutable nearest = System.Double.MaxValue

            for opponentSlot in MatchWorld.activeSlots opponents.Frame do
                let dx = float tx - float (MatchWorld.posX opponents.Frame opponentSlot) * dir
                let dy = float ty - float (MatchWorld.posY opponents.Frame opponentSlot)
                nearest <- min nearest (sqrt (dx * dx + dy * dy))

            if nearest > bestSpace || (nearest = bestSpace && candidate = 0) then
                bestSpace <- nearest
                bestX <- float tx
                bestY <- float ty

        float32 (bestX * dir), float32 bestY

    let private supportTarget
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (carrierSlot: int)
        (slot: int)
        : (float32 * float32) option =
        if carrierSlot < 0 || slot = carrierSlot then
            None
        else
            let team = MatchWorld.team world side
            let frame = team.Frame
            let carrierX = float (MatchWorld.posX frame carrierSlot)
            let carrierY = float (MatchWorld.posY frame carrierSlot)
            let dir = MatchMath.goalDirection side
            let mutable frontSlot = -1
            let mutable frontDistance = System.Double.MaxValue
            let mutable rearSlot = -1
            let mutable rearDistance = System.Double.MaxValue

            for candidate in MatchWorld.activeSlots frame do
                if candidate <> carrierSlot && frame.SlotRoles.[candidate] <> SlotRole.Goalkeeper then
                    let relativeForward = (float (MatchWorld.posX frame candidate) - carrierX) * dir

                    let distance =
                        MatchMath.distF32
                            frame.PosX.[candidate]
                            frame.PosY.[candidate]
                            frame.PosX.[carrierSlot]
                            frame.PosY.[carrierSlot]

                    if relativeForward >= 0.0 then
                        if distance < frontDistance || (distance = frontDistance && candidate < frontSlot) then
                            frontSlot <- candidate
                            frontDistance <- distance
                    elif distance < rearDistance || (distance = rearDistance && candidate < rearSlot) then
                        rearSlot <- candidate
                        rearDistance <- distance

            let targetFor chosen front =
                if chosen <> slot then
                    None
                else
                    let candidateY = float (MatchWorld.posY frame slot)

                    let naturalSign =
                        if candidateY > carrierY then 1.0
                        elif candidateY < carrierY then -1.0
                        else if slot % 2 = 0 then 1.0
                        else -1.0

                    let sign = if front then naturalSign else -naturalSign

                    let xOffset =
                        if front then
                            float ctx.Config.Positioning.SupportForwardDistance
                        else
                            -float ctx.Config.Positioning.SupportForwardDistance

                    let yOffset = sign * float ctx.Config.Positioning.SupportLateralDistance

                    let targetX, targetY =
                        insetTarget ctx (carrierX * dir + xOffset) (carrierY + yOffset)

                    Some(float32 (float targetX * dir), targetY)

            match targetFor frontSlot true with
            | Some target -> Some target
            | None -> targetFor rearSlot false

    let private roleIntent
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (carrierSlot: int)
        (support: (float32 * float32) option)
        (pressX: float32)
        (pressY: float32)
        (looseBallChaser: int option)
        (looseBall: bool)
        (slot: int)
        (tick: int<subtick>)
        : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let p = MatchWorld.profileOf ctx side slot
        let ball = world.State.Ball.Position
        let bx = float ball.X
        let by = float ball.Y
        let shapeX = float team.Shape.TargetsX.[slot]
        let shapeY = float team.Shape.TargetsY.[slot]
        let phase = team.Blackboard.OurPhase
        let dir = MatchMath.goalDirection side

        let setRoleIntent frame slot kind tx ty tick =
            setIntent frame slot kind tx ty tick ctx.Config.Movement.CommittedRunDuration

        let opponentControlsBall =
            match MatchWorld.ballCarrier ctx world with
            | Some(carrierSide, _, _) -> carrierSide <> side
            | None -> false

        if looseBall then
            match frame.DefensiveRoles.[slot] with
            | _ when p.Role = SlotRole.Goalkeeper ->
                setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
            | _ when looseBallChaser = Some slot ->
                setRoleIntent frame slot IntentCode.Press (float32 bx) (float32 by) tick
            | DefensiveRole.Cover ->
                let sign = if slot % 2 = 0 then 1.0 else -1.0
                let coverX = bx - dir * float ctx.Config.Team.LooseBallCoverDistance
                let coverY = by + sign * float ctx.Config.Team.WeakSideSeparation * 0.5
                setRoleIntent frame slot IntentCode.Cover (float32 coverX) (float32 coverY) tick
            | DefensiveRole.Marker ->
                let ownGoal = float (MatchMath.ownGoalX side)

                let restX =
                    ownGoal
                    + dir
                      * min (float ctx.Config.Team.RestDefenseDistance) (abs (bx - ownGoal) * 0.65)

                let restY =
                    shapeY
                    + (if slot % 2 = 0 then 1.0 else -1.0)
                      * float ctx.Config.Team.WeakSideSeparation
                      * 0.25

                setRoleIntent frame slot IntentCode.Recover (float32 restX) (float32 restY) tick
            | _ -> setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
        elif opponentControlsBall then
            match frame.DefensiveRoles.[slot] with
            | _ when p.Role = SlotRole.Goalkeeper ->
                setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
            | DefensiveRole.FirstDefender -> setRoleIntent frame slot IntentCode.Press (float32 bx) (float32 by) tick
            | DefensiveRole.Cover ->
                let coverX = bx - dir * float ctx.Config.Team.LooseBallCoverDistance
                setRoleIntent frame slot IntentCode.Cover (float32 coverX) (float32 shapeY) tick
            | DefensiveRole.Marker -> setRoleIntent frame slot IntentCode.Recover (float32 shapeX) (float32 shapeY) tick
            | DefensiveRole.Unassigned ->
                setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
        elif slot = carrierSlot then
            let tx, ty = carryTarget ctx world side slot
            setRoleIntent frame slot IntentCode.BallCarry tx ty tick
        elif p.Role = SlotRole.Goalkeeper then
            setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
        elif support.IsSome then
            let tx, ty = support.Value
            setRoleIntent frame slot IntentCode.Support tx ty tick
        elif carrierSlot >= 0 then
            setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
        else
            let ballDist =
                MatchMath.distF32 team.Shape.TargetsX.[slot] team.Shape.TargetsY.[slot] (float32 bx) (float32 by)

            let myStep = team.Intercept.SlotSteps.[slot]

            let press =
                looseBallChaser = Some slot
                || Pressing.shouldPress ctx.Config.Press team.Intercept p.Role myStep

            if looseBallChaser = Some slot then
                setRoleIntent frame slot IntentCode.Press pressX pressY tick
            else
                match p.Role with
                | SlotRole.Goalkeeper ->
                    setRoleIntent frame slot IntentCode.HoldPosition (float32 (MatchMath.ownGoalX side)) 0.0f tick
                | SlotRole.Defender
                | SlotRole.FullBack
                | SlotRole.DefensiveMid ->
                    if phase = PhaseKind.Defending || phase = PhaseKind.TransitionDefend then
                        if press then
                            setRoleIntent frame slot IntentCode.Press pressX pressY tick
                        else
                            let goalX = MatchMath.ownGoalX side
                            let mx = float shapeX + (float shapeX - float goalX) * 0.35
                            setRoleIntent frame slot IntentCode.HoldPosition (float32 mx) (float32 shapeY) tick
                    elif ballDist < 22.0 && phase = PhaseKind.TransitionAttack then
                        setRoleIntent frame slot IntentCode.Press pressX pressY tick
                    else
                        setRoleIntent frame slot IntentCode.Support (float32 shapeX) (float32 shapeY) tick
                | SlotRole.Striker ->
                    if phase = PhaseKind.Attacking || phase = PhaseKind.TransitionAttack then
                        let goalX = float (MatchMath.goalX side)
                        let pushX = shapeX + 6.0 * dir

                        let tx =
                            min pushX goalX
                            |> MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength)

                        setRoleIntent frame slot IntentCode.ExecuteRun (float32 tx) (float32 shapeY) tick
                    elif press then
                        setRoleIntent frame slot IntentCode.Press pressX pressY tick
                    else
                        setRoleIntent frame slot IntentCode.Support (float32 shapeX) (float32 shapeY) tick
                | _ ->
                    if (phase = PhaseKind.Defending || phase = PhaseKind.TransitionDefend) && press then
                        setRoleIntent frame slot IntentCode.Press pressX pressY tick
                    elif phase = PhaseKind.Attacking && ballDist < 25.0 then
                        setRoleIntent frame slot IntentCode.Support (float32 shapeX) (float32 shapeY) tick
                    elif press then
                        let sx, sy = pressSpot ctx world side
                        setRoleIntent frame slot IntentCode.Press sx sy tick
                    else
                        setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick

    let private receiveIntent
        (ctx: MatchContext)
        (world: World)
        (frame: TeamFrame)
        (slot: int)
        (tick: int<subtick>)
        : unit =
        let ball = world.State.Ball

        match ball.Trajectory, ball.ArrivalGate with
        | Some(TrajectoryIntent.Aimed _), Some gate when gate > tick ->
            let remaining = float (gate - tick) / 40.0
            let ax = float ball.Position.X + float ball.Position.Vx * remaining
            let ay = float ball.Position.Y + float ball.Position.Vy * remaining
            frame.Intent.Kind.[slot] <- IntentCode.ExecuteRun

            frame.Intent.TargetX.[slot] <- max (-float32 Pitch.HalfLength) (min (float32 Pitch.HalfLength) (float32 ax))

            frame.Intent.TargetY.[slot] <- max (-float32 Pitch.HalfWidth) (min (float32 Pitch.HalfWidth) (float32 ay))

            frame.Intent.Phase.[slot] <- IntentPhase.Committed
            frame.Intent.CommittedAt.[slot] <- tick
            frame.Intent.CommittedUntil.[slot] <- tick + ctx.Config.Movement.CommittedRunDuration
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            let carrier = MatchWorld.ballCarrier ctx.Ctx world

            let aimedReceiver =
                match world.State.Ball.Trajectory, world.State.Ball.ArrivalGate with
                | Some(TrajectoryIntent.Aimed(_, receiver, _)), Some gate when gate > tick ->
                    match MatchWorld.teamOfPlayer ctx.Ctx receiver with
                    | Some side -> MatchWorld.slotOf ctx.Ctx side receiver |> Option.map (fun slot -> side, slot)
                    | None -> None
                | _ -> None

            let incomingPass =
                match world.State.Ball.Trajectory, world.State.Ball.ArrivalGate with
                | Some(TrajectoryIntent.Aimed _), Some gate when gate > tick -> true
                | _ -> false

            let receiveTarget =
                aimedReceiver
                |> Option.filter (fun (side, slot) -> MatchWorld.isActive (MatchWorld.team world side).Frame slot)

            let processTeam side =
                let team = MatchWorld.team world side
                let frame = team.Frame
                let cancelledByReplacement = Array.zeroCreate<bool> frame.SlotCount

                let carrierSlot =
                    match carrier with
                    | Some(carrierSide, slot, _) when carrierSide = side -> slot
                    | _ -> -1

                if team.LastCarrierSlot <> carrierSlot then
                    for activeSlot in MatchWorld.activeSlots frame do
                        if
                            not (IntentCode.isContinuous frame.Intent.Kind.[activeSlot])
                            && frame.Intent.Phase.[activeSlot] <> IntentPhase.Completed
                        then
                            frame.Intent.Phase.[activeSlot] <- IntentPhase.Completed
                            frame.Intent.ExitTrigger.[activeSlot] <- ExitTrigger.BallNearby

                    if team.LastCarrierSlot <> -1 then
                        team.ActivePlan <- None

                    team.LastCarrierSlot <- carrierSlot

                if incomingPass then
                    for activeSlot in MatchWorld.activeSlots frame do
                        if frame.Intent.Kind.[activeSlot] = IntentCode.ExecuteRun then
                            let keep =
                                match receiveTarget with
                                | Some(receiverSide, receiverSlot) -> receiverSide = side && receiverSlot = activeSlot
                                | None -> false

                            if not keep then
                                frame.Intent.Phase.[activeSlot] <- IntentPhase.Completed
                                frame.Intent.ExitTrigger.[activeSlot] <- ExitTrigger.BallNearby
                                cancelledByReplacement.[activeSlot] <- true

                let pressX, pressY = pressSpot ctx.Ctx world side

                let supportFor slot =
                    supportTarget ctx.Ctx world side carrierSlot slot

                let looseBallChaser =
                    match world.State.Ball.Control with
                    | BallControl.Free
                    | BallControl.Airborne
                    | BallControl.Contesting _ -> MatchWorld.looseBallChaser world side
                    | _ -> None

                let looseBall =
                    match world.State.Ball.Control with
                    | BallControl.Free
                    | BallControl.Airborne
                    | BallControl.Contesting _ -> true
                    | _ -> false

                match world.State.Flow with
                | MatchFlow.Restart _ ->
                    // SetPieceRoutines owns these typed targets. Reapplying the
                    // normal role policy would erase the legal restart shape.
                    ()
                | _ ->
                    // Plan-override: before roleIntent, steer planned receivers
                    // toward their designated positions.
                    match team.ActivePlan with
                    | Some plan when plan.ExpiresAt > tick ->
                        for (slot, px, py) in plan.FutureReceivers do
                            if
                                slot >= 0
                                && slot < frame.Intent.Kind.Length
                                && frame.SlotRoles.[slot] <> SlotRole.Goalkeeper
                                && frame.Intent.Kind.[slot] = IntentCode.None
                            then
                                setIntent
                                    frame
                                    slot
                                    IntentCode.ExecuteRun
                                    (float32 px)
                                    (float32 py)
                                    tick
                                    ctx.Ctx.Config.Movement.CommittedRunDuration
                    | _ -> ()

                    match receiveTarget with
                    | Some(receiverSide, slot) when receiverSide = side -> receiveIntent ctx.Ctx world frame slot tick
                    | _ -> ()

                    for slot in MatchWorld.activeSlots frame do
                        if not cancelledByReplacement.[slot] then
                            roleIntent
                                ctx.Ctx
                                world
                                side
                                carrierSlot
                                (supportFor slot)
                                pressX
                                pressY
                                looseBallChaser
                                looseBall
                                slot
                                tick

            processTeam ClubSide.Home
            processTeam ClubSide.Away

            rng
