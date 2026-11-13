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

    let private supportTargets
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (carrierSlot: int)
        : (int * float32 * float32) option * (int * float32 * float32) option =
        if carrierSlot < 0 then
            None, None
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
                if chosen < 0 then
                    None
                else
                    let candidateY = float (MatchWorld.posY frame chosen)

                    let naturalSign =
                        if candidateY > carrierY then 1.0
                        elif candidateY < carrierY then -1.0
                        else if chosen % 2 = 0 then 1.0
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

                    Some(chosen, float32 (float targetX * dir), targetY)

            targetFor frontSlot true, targetFor rearSlot false

    let private roleIntent
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (carrierSlot: int)
        (support: (float32 * float32) option)
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
        let dir = MatchMath.goalDirection side

        let setRoleIntent frame slot kind tx ty tick =
            setIntent frame slot kind tx ty tick ctx.Config.Movement.CommittedRunDuration

        let opponentControlsBall =
            match MatchWorld.ballCarrier ctx world with
            | Some(carrierSide, _, _) -> carrierSide <> side
            | None -> false

        let transitionTask =
            match team.Defense.Episode with
            | Some episode when episode.EndedAt.IsNone -> team.Defense.Tasks[slot]
            | _ when opponentControlsBall && world.State.Flow = MatchFlow.Live -> team.Defense.Tasks[slot]
            | _ -> None

        let attackDuty =
            let ownFlight =
                match world.State.Ball.Trajectory with
                | Some(TrajectoryIntent.Aimed(passer, _, _)) -> MatchWorld.teamOfPlayer ctx passer = Some side
                | Some(TrajectoryIntent.Struck(shooter, _, _, _)) -> MatchWorld.teamOfPlayer ctx shooter = Some side
                | _ -> false
            CollectiveAttackState.duty team.Attack p.Id
            |> Option.filter (fun duty -> duty.Slot = slot && slot <> carrierSlot && not opponentControlsBall && (carrierSlot >= 0 || ownFlight))

        let keeperClaims = looseBall && looseBallChaser = Some slot

        // The chaser runs to where he meets the rolling ball, not where it is now; aiming at the current position
        // trails a moving ball forever and lets slow balls roll out of play.
        // Only the loose-ball chaser needs it, and it integrates the ball step by step, so it is not paid per player.
        let chasePoint () =
            // Meeting the ball means getting a foot to it, not entering the decision table's generous kickable area.
            let p =
                { InterceptTable.parameters ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball with
                    ControlRadius = ctx.Config.ContactAction.FootReach }

            let steps = InterceptTable.stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

            if steps < p.MaxSteps then
                let meet = InterceptTable.ballInertiaPoint p ball steps
                float meet.X, float meet.Y
            else
                bx, by

        if p.IsGK && carrierSlot <> slot && not keeperClaims then
            setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
        elif transitionTask.IsSome then
            let task = transitionTask.Value
            let kind =
                match task.Role with
                | DefensiveRole.FirstDefender -> IntentCode.Press
                | DefensiveRole.Cover -> IntentCode.Cover
                | DefensiveRole.Screen -> IntentCode.Mark
                | DefensiveRole.ProtectDepth
                | DefensiveRole.RecoverShape -> IntentCode.Regroup
                | DefensiveRole.Marker -> IntentCode.Mark
                | DefensiveRole.Unassigned -> IntentCode.HoldPosition
            // An attacking commitment cannot outlive the sporting loss that invalidated its purpose.
            if not (IntentCode.isContinuous frame.Intent.Kind[slot]) then frame.Intent.Phase[slot] <- IntentPhase.Completed
            frame.RunLookup.Remove slot |> ignore
            setRoleIntent frame slot kind (float32 task.Target.X) (float32 task.Target.Y) tick
        elif attackDuty.IsSome then
            let duty = attackDuty.Value
            // Receive runs are authoritative; continuous support can otherwise be redirected without locking autonomy.
            setRoleIntent frame slot IntentCode.Support (float32 duty.Target.X) (float32 duty.Target.Y) tick
        elif looseBall then
            match frame.DefensiveRoles.[slot] with
            | _ when looseBallChaser = Some slot ->
                let chaseX, chaseY = chasePoint ()
                setRoleIntent frame slot IntentCode.Press (float32 chaseX) (float32 chaseY) tick
            | _ when p.Role = SlotRole.Goalkeeper ->
                setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
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
            | DefensiveRole.Screen
            | DefensiveRole.ProtectDepth
            | DefensiveRole.RecoverShape
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
            // A ground pass is met where the receiver first reaches the ball's actual rolling path, so he adjusts
            // to a mis-hit instead of running to where it should have gone; an aerial ball is met at its solved
            // target. A straight-line extrapolation ignores deceleration and overruns the ball's line.
            let rolling = ball.Impulse |> Option.exists (fun impulse -> impulse.StrikeKind = StrikeKind.Pass)
            let p =
                { InterceptTable.parameters ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball with
                    ControlRadius = ctx.Config.ContactAction.FootReach }

            let steps = InterceptTable.stepsToReach p ball.Position (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

            let arrival = InterceptTable.ballInertiaPoint p ball.Position (int (gate - tick))

            let onLine (target: Vec2) =
                let dx, dy = float arrival.X - float target.X, float arrival.Y - float target.Y
                let tolerance = float ctx.Config.ContactAction.ReceptionTargetTolerance
                dx * dx + dy * dy <= tolerance * tolerance

            let ax, ay =
                match ball.Impulse |> Option.bind _.IntendedTarget with
                // The run the passer led is kept, but he stands where the ball actually arrives: the tolerance is wider
                // than his foot reach, and waiting on the aimed point let a slightly mis-hit pass roll past him and out.
                | Some target when rolling && onLine target -> float arrival.X, float arrival.Y
                | _ when rolling && steps < p.MaxSteps ->
                    let meet = InterceptTable.ballInertiaPoint p ball.Position steps
                    float meet.X, float meet.Y
                | Some target -> float target.X, float target.Y
                | None ->
                    let remaining = float (gate - tick) / 40.0
                    float ball.Position.X + float ball.Position.Vx * remaining,
                    float ball.Position.Y + float ball.Position.Vy * remaining

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

                let frontSupport, rearSupport =
                    if team.Attack.Episode.IsSome then None, None
                    else supportTargets ctx.Ctx world side carrierSlot

                let supportFor slot =
                    match frontSupport, rearSupport with
                    | Some (owner, x, y), _ when owner = slot -> Some(x, y)
                    | _, Some (owner, x, y) when owner = slot -> Some(x, y)
                    | _ -> None

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
                    match receiveTarget with
                    | Some(receiverSide, slot) when receiverSide = side -> receiveIntent ctx.Ctx world frame slot tick
                    | _ -> ()

                    for slot in MatchWorld.activeSlots frame do
                        let isIncomingReceiver =
                            receiveTarget
                            |> Option.exists (fun (receiverSide, receiverSlot) ->
                                receiverSide = side && receiverSlot = slot)

                        if
                            not isIncomingReceiver
                            && (not cancelledByReplacement.[slot] || team.Defense.Tasks[slot].IsSome)
                        then
                            roleIntent
                                ctx.Ctx
                                world
                                side
                                carrierSlot
                                (supportFor slot)
                                looseBallChaser
                                looseBall
                                slot
                                tick

            MatchWorld.bothTeams world processTeam

            rng
