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

    let private insetTarget (ctx: MatchContext) (x: float) (y: float) : struct (float32 * float32) =
        let inset = float ctx.Config.Positioning.TargetInset
        let halfLength = float Pitch.HalfLength - inset
        let halfWidth = float Pitch.HalfWidth - inset
        struct (float32 (MatchMath.clamp (-halfLength) halfLength x), float32 (MatchMath.clamp (-halfWidth) halfWidth y))

    let private carryTarget
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        : struct (float32 * float32) =
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

            let struct (tx, ty) = insetTarget ctx (px * dir + forwardStep) (py + sign * lateral)
            let mutable nearest = System.Double.MaxValue

            for opponentSlot in MatchWorld.activeSlots opponents.Frame do
                let dx = float tx - float (MatchWorld.posX opponents.Frame opponentSlot) * dir
                let dy = float ty - float (MatchWorld.posY opponents.Frame opponentSlot)
                nearest <- min nearest (sqrt (dx * dx + dy * dy))

            if nearest > bestSpace || (nearest = bestSpace && candidate = 0) then
                bestSpace <- nearest
                bestX <- float tx
                bestY <- float ty

        struct (float32 (bestX * dir), float32 bestY)

    /// The two teammates asked to offer themselves to the carrier, nearest ahead and nearest behind; -1 is nobody.
    [<Struct>]
    type private SupportPlan =
        { FrontSlot: int
          FrontX: float32
          FrontY: float32
          RearSlot: int
          RearX: float32
          RearY: float32 }

    let private noSupport =
        { FrontSlot = -1
          FrontX = 0.0f
          FrontY = 0.0f
          RearSlot = -1
          RearX = 0.0f
          RearY = 0.0f }

    let private supportTargets (ctx: MatchContext) (world: World) (side: ClubSide) (carrierSlot: int) : SupportPlan =
        if carrierSlot < 0 then
            noSupport
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

            let targetFor chosen front : struct (float32 * float32) =
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

                let struct (targetX, targetY) =
                    insetTarget ctx (carrierX * dir + xOffset) (carrierY + yOffset)

                struct (float32 (float targetX * dir), targetY)

            let struct (frontX, frontY) =
                if frontSlot < 0 then
                    struct (0.0f, 0.0f)
                else
                    targetFor frontSlot true

            let struct (rearX, rearY) =
                if rearSlot < 0 then
                    struct (0.0f, 0.0f)
                else
                    targetFor rearSlot false

            { FrontSlot = frontSlot
              FrontX = frontX
              FrontY = frontY
              RearSlot = rearSlot
              RearX = rearX
              RearY = rearY }

    /// The attack episode's duty for a player, searched Link, Runner, Security like `CollectiveAttackState.duty`.
    let private dutyOf (state: CollectiveAttackState) (player: PlayerId) : AttackDuty voption =
        match state.Episode with
        | Some episode ->
            if episode.Link.Player = player then
                ValueSome episode.Link
            elif episode.Runner.Player = player then
                ValueSome episode.Runner
            elif episode.Security.Player = player then
                ValueSome episode.Security
            else
                ValueNone
        | None -> ValueNone

    let private roleIntent
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (carrierSlot: int)
        (support: struct (float32 * float32) voption)
        (looseBallChaser: int)
        (looseBall: bool)
        (opponentControlsBall: bool)
        (ownFlight: bool)
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

        let transitionTask =
            match team.Defense.Episode with
            | Some episode when episode.EndedAt.IsNone -> team.Defense.Tasks[slot]
            | _ when opponentControlsBall && world.State.Flow = MatchFlow.Live -> team.Defense.Tasks[slot]
            | _ -> ValueNone

        let attackDuty =
            match dutyOf team.Attack p.Id with
            | ValueSome duty when
                duty.Slot = slot
                && slot <> carrierSlot
                && not opponentControlsBall
                && (carrierSlot >= 0 || ownFlight)
                ->
                ValueSome duty
            | _ -> ValueNone

        let keeperClaims = looseBall && looseBallChaser = slot

        // The chaser runs to where he meets the rolling ball, not where it is now; aiming at the current position
        // trails a moving ball forever and lets slow balls roll out of play.
        // Only the loose-ball chaser needs it, and it integrates the ball step by step, so it is not paid per player.
        let chasePoint () =
            // Meeting the ball means getting a foot to it, not entering the decision table's generous kickable area.
            let p =
                { InterceptTable.parameters ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball with
                    ControlRadius = ctx.Config.ContactAction.FootReach }

            let steps =
                InterceptTable.stepsToReach p ball (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

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
                | DefensiveRole.Contain -> IntentCode.Contain
                | DefensiveRole.Cover -> IntentCode.Cover
                | DefensiveRole.Screen -> IntentCode.Mark
                | DefensiveRole.ProtectDepth
                | DefensiveRole.RecoverShape -> IntentCode.Regroup
                | DefensiveRole.Marker -> IntentCode.Mark
                | DefensiveRole.Unassigned -> IntentCode.HoldPosition
            // An attacking commitment cannot outlive the sporting loss that invalidated its purpose.
            if not (IntentCode.isContinuous frame.Intent.Kind[slot]) then
                frame.Intent.Phase[slot] <- IntentPhase.Completed

            frame.RunLookup.Remove slot |> ignore
            setRoleIntent frame slot kind (float32 task.Target.X) (float32 task.Target.Y) tick
        elif attackDuty.IsSome then
            let duty = attackDuty.Value
            // Receive runs are authoritative; continuous support can otherwise be redirected without locking autonomy.
            setRoleIntent frame slot IntentCode.Support (float32 duty.Target.X) (float32 duty.Target.Y) tick
        elif looseBall then
            match frame.DefensiveRoles.[slot] with
            | _ when looseBallChaser = slot ->
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
            | DefensiveRole.Contain -> setRoleIntent frame slot IntentCode.Contain (float32 bx) (float32 by) tick
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
            let struct (tx, ty) = carryTarget ctx world side slot
            setRoleIntent frame slot IntentCode.BallCarry tx ty tick
        elif p.Role = SlotRole.Goalkeeper then
            setRoleIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick
        elif support.IsSome then
            let struct (tx, ty) = support.Value
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
            let rolling =
                ball.Impulse
                |> Option.exists (fun impulse -> impulse.StrikeKind = StrikeKind.Pass)

            let p =
                { InterceptTable.parameters ctx.Config.Intercept ctx.Config.Movement ctx.Config.Ball with
                    ControlRadius = ctx.Config.ContactAction.FootReach }

            let steps =
                InterceptTable.stepsToReach p ball.Position (MatchWorld.posX frame slot) (MatchWorld.posY frame slot)

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

            let incomingPass =
                match world.State.Ball.Trajectory, world.State.Ball.ArrivalGate with
                | Some(TrajectoryIntent.Aimed _), Some gate when gate > tick -> true
                | _ -> false

            // Slot -1 when no pass is on its way to an active player.
            let struct (receiveSide, receiveSlot) =
                match world.State.Ball.Trajectory, world.State.Ball.ArrivalGate with
                | Some(TrajectoryIntent.Aimed(_, receiver, _)), Some gate when gate > tick ->
                    match MatchWorld.sideOfPlayer ctx.Ctx receiver with
                    | ValueSome side ->
                        let slot = MatchWorld.slotIndex ctx.Ctx side receiver

                        if slot >= 0 && MatchWorld.isActive (MatchWorld.team world side).Frame slot then
                            struct (side, slot)
                        else
                            struct (ClubSide.Home, -1)
                    | ValueNone -> struct (ClubSide.Home, -1)
                | _ -> struct (ClubSide.Home, -1)

            // The opponent holding the ball and our own flight are team-wide facts, so every player's policy
            // reads them from here instead of each rebuilding the lookup.
            let ownFlightOf side =
                match world.State.Ball.Trajectory with
                | Some(TrajectoryIntent.Aimed(passer, _, _)) -> MatchWorld.sideOfPlayer ctx.Ctx passer = ValueSome side
                | Some(TrajectoryIntent.Struck(shooter, _, _, _)) ->
                    MatchWorld.sideOfPlayer ctx.Ctx shooter = ValueSome side
                | _ -> false

            let processTeam side =
                let team = MatchWorld.team world side
                let frame = team.Frame

                let carrierSlot =
                    match carrier with
                    | ValueSome(struct (carrierSide, slot, _)) when carrierSide = side -> slot
                    | _ -> -1

                let opponentControlsBall =
                    match carrier with
                    | ValueSome(struct (carrierSide, _, _)) -> carrierSide <> side
                    | ValueNone -> false

                // Only a pass in flight cancels runs, so the common tick allocates no mask.
                let cancelledByReplacement =
                    if incomingPass then
                        Array.zeroCreate<bool> frame.SlotCount
                    else
                        System.Array.Empty<bool>()

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
                            let keep = receiveSlot >= 0 && receiveSide = side && receiveSlot = activeSlot

                            if not keep then
                                frame.Intent.Phase.[activeSlot] <- IntentPhase.Completed
                                frame.Intent.ExitTrigger.[activeSlot] <- ExitTrigger.BallNearby
                                cancelledByReplacement.[activeSlot] <- true

                let support =
                    if team.Attack.Episode.IsSome then
                        noSupport
                    else
                        supportTargets ctx.Ctx world side carrierSlot

                let looseBallChaser, looseBall =
                    match world.State.Ball.Control with
                    | BallControl.Free
                    | BallControl.Airborne
                    | BallControl.Contesting _ ->
                        (match MatchWorld.looseBallChaser world side with
                         | Some chaser -> chaser
                         | None -> -1),
                        true
                    | _ -> -1, false

                let ownFlight = ownFlightOf side

                match world.State.Flow with
                | MatchFlow.Restart _ ->
                    // SetPieceRoutines owns these typed targets. Reapplying the
                    // normal role policy would erase the legal restart shape.
                    ()
                | _ ->
                    if receiveSlot >= 0 && receiveSide = side then
                        receiveIntent ctx.Ctx world frame receiveSlot tick

                    for slot in MatchWorld.activeSlots frame do
                        let isIncomingReceiver =
                            receiveSlot >= 0 && receiveSide = side && receiveSlot = slot

                        if
                            not isIncomingReceiver
                            && (not (slot < cancelledByReplacement.Length && cancelledByReplacement.[slot])
                                || team.Defense.Tasks[slot].IsSome)
                        then
                            let supportFor =
                                if support.FrontSlot = slot then
                                    ValueSome(struct (support.FrontX, support.FrontY))
                                elif support.RearSlot = slot then
                                    ValueSome(struct (support.RearX, support.RearY))
                                else
                                    ValueNone

                            roleIntent
                                ctx.Ctx
                                world
                                side
                                carrierSlot
                                supportFor
                                looseBallChaser
                                looseBall
                                opponentControlsBall
                                ownFlight
                                slot
                                tick

            MatchWorld.bothTeams world processTeam

            rng
