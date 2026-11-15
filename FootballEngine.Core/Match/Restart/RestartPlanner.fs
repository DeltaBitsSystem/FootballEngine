namespace FootballEngine.Core.Match

/// Pure planning and law validation for restarts. The controller publishes one
/// result and every later system reads that result; no plan is rebuilt from
/// moving intent or player positions.
module RestartPlanner =

    type PlanError =
        | NoExecutor
        | InvalidSpot of string
        | InvalidTargets of string list

    let private clampPoint (x: float) (y: float) : Vec2 =
        let px, py = MatchMath.pitchClamp x y
        Vec2.create (px * 1.0<meter>) (py * 1.0<meter>)

    let private distance (a: Vec2) (b: Vec2) = MatchMath.dist a.X a.Y b.X b.Y

    let private executorPoint (ctx: MatchContext) (kind: SetPieceKind) (side: ClubSide) (spot: Vec2) : Vec2 =
        match kind with
        | SetPieceKind.Corner ->
            clampPoint
                (float spot.X - float ctx.Config.SetPiece.CornerTakerOffset * MatchMath.goalDirection side)
                (float spot.Y)
        | _ -> spot

    let private awayFrom (spot: Vec2) (point: Vec2) (required: float) : Vec2 =
        let dx = float point.X - float spot.X
        let dy = float point.Y - float spot.Y
        let d = sqrt (dx * dx + dy * dy)

        let projected =
            if d >= required then
                point
            elif d < 0.001 then
                clampPoint (float spot.X + required) (float spot.Y)
            else
                clampPoint (float spot.X + dx / d * required) (float spot.Y + dy / d * required)

        // The radial projection lands exactly on the circle; rounding can leave it a hair inside, which used to
        // send a penalty defender to a fallback point inside the area and freeze the kick.
        if distance spot projected >= required - 1e-6 then
            projected
        else
            // Clipping a radial projection at a touchline can put the wall back inside the exclusion circle.
            [| clampPoint (float spot.X + required) (float spot.Y)
               clampPoint (float spot.X - required) (float spot.Y)
               clampPoint (float spot.X) (float spot.Y + required)
               clampPoint (float spot.X) (float spot.Y - required) |]
            |> Array.minBy (fun candidate -> -(min required (distance spot candidate)), distance point candidate)

    let private actionFor
        (ctx: MatchContext)
        (world: World)
        (kind: SetPieceKind)
        (side: ClubSide)
        (executorSlot: int)
        (spot: Vec2)
        =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let dir = MatchMath.goalDirection side
        let mutable receiver = -1

        for slot in MatchWorld.activeSlots frame do
            if slot <> executorSlot && slot <> 0 then
                if receiver < 0 then
                    receiver <- slot

        let receiverId slot =
            (MatchWorld.lineup ctx side).Profiles.[slot].Id

        match kind with
        | SetPieceKind.Penalty -> RestartAction.RestartShot(MatchMath.goalX side, 0.0<meter>)
        | SetPieceKind.Corner -> RestartAction.RestartCross(MatchMath.goalX side - 8.0<meter> * dir, 0.0<meter>)
        | SetPieceKind.FreeKick when
            MatchMath.distanceToGoal side spot.X spot.Y
            <= float ctx.Config.SetPiece.DirectFreeKickRange
            ->
            RestartAction.RestartShot(MatchMath.goalX side, 0.0<meter>)
        | SetPieceKind.GoalKick ->
            if receiver >= 0 then
                RestartAction.RestartPass(receiverId receiver)
            else
                RestartAction.RestartClearance(spot.X + 25.0<meter> * dir, spot.Y)
        | SetPieceKind.ThrowIn
        | SetPieceKind.KickOff
        | SetPieceKind.FreeKick ->
            if receiver >= 0 then
                RestartAction.RestartPass(receiverId receiver)
            else
                RestartAction.RestartClearance(spot.X + 15.0<meter> * dir, spot.Y)

    let private executorSlot (world: World) (side: ClubSide) (kind: SetPieceKind) (spot: Vec2) =
        let frame = (MatchWorld.team world side).Frame

        let slots =
            MatchWorld.activeSlots frame
            |> Array.filter (fun slot -> kind = SetPieceKind.GoalKick || slot <> 0)
            |> Array.sortBy (fun slot ->
                MatchMath.dist spot.X spot.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot), slot)

        slots |> Array.tryHead

    let plan
        (ctx: MatchContext)
        (world: World)
        (id: RestartId)
        (cause: RestartCause)
        (kind: SetPieceKind)
        (side: ClubSide)
        (x: float<meter>)
        (y: float<meter>)
        (awardTick: int<subtick>)
        : Result<RestartPlan, PlanError> =
        let spot =
            let sx, sy = MatchMath.restartSpot kind side x y
            Vec2.create sx sy

        match executorSlot world side kind spot with
        | None -> Error PlanError.NoExecutor
        | Some executorSlot ->
            let executor = (MatchWorld.lineup ctx side).Profiles.[executorSlot].Id
            let executorTarget = executorPoint ctx kind side spot
            let opponent = MatchWorld.oppOf side
            let mutable targets = ResizeArray<RestartTarget>()

            for currentSide in [ side; opponent ] do
                let currentTeam = MatchWorld.team world currentSide
                let currentFrame = currentTeam.Frame

                for slot in MatchWorld.activeSlots currentFrame do
                    let player = (MatchWorld.lineup ctx currentSide).Profiles.[slot].Id

                    let current =
                        clampPoint
                            (float (MatchWorld.posX currentFrame slot))
                            (float (MatchWorld.posY currentFrame slot))

                    let role, position =
                        if currentSide = side && player = executor then
                            RestartRole.Executor, executorTarget
                        elif currentSide = opponent && kind = SetPieceKind.KickOff then
                            // Aim beyond the legal boundary so normalized targets remain legal after rounding.
                            RestartRole.Support,
                            awayFrom spot current (9.15 + float ctx.Config.SetPiece.TargetTolerance)
                        elif currentSide = opponent && kind = SetPieceKind.Corner then
                            RestartRole.Support, awayFrom spot current (float ctx.Config.SetPiece.WallDistance)
                        elif currentSide = opponent && kind = SetPieceKind.FreeKick then
                            let target =
                                if slot = 0 then
                                    clampPoint (float (MatchMath.ownGoalX opponent)) 0.0
                                else
                                    awayFrom spot current (float ctx.Config.SetPiece.WallDistance)

                            if slot = 0 then
                                RestartRole.Goalkeeper, target
                            else
                                RestartRole.Wall, target
                        elif kind = SetPieceKind.Penalty then
                            if currentSide = opponent && currentFrame.SlotRoles[slot] = SlotRole.Goalkeeper then
                                RestartRole.Goalkeeper, clampPoint (float (MatchMath.goalX side)) 0.0
                            else
                                let dir = MatchMath.goalDirection side
                                // A radial exclusion cannot guarantee leaving the rectangular area or staying behind the spot.
                                let boundary = float (MatchMath.goalX side) * dir - float Pitch.PenaltyAreaDepth - float ctx.Config.SetPiece.TargetTolerance
                                let behindArea = clampPoint (min (float current.X * dir) boundary * dir) (float current.Y)
                                RestartRole.Support, awayFrom spot behindArea (float ctx.Config.SetPiece.WallDistance + float ctx.Config.SetPiece.TargetTolerance)
                        elif
                            currentSide = side
                            && (match actionFor ctx world kind side executorSlot spot with
                                | RestartAction.RestartPass receiverId' when receiverId' = player -> true
                                | _ -> false)
                        then
                            let receiverTarget =
                                if kind = SetPieceKind.ThrowIn then
                                    // The receiver must be on the field; planning his current spot froze the
                                    // restart whenever he stood on the touchline the ball had just crossed.
                                    let limit = float Pitch.HalfWidth - float ctx.Config.SetPiece.ThrowInReceiverInset
                                    clampPoint (float current.X) (MatchMath.clamp -limit limit (float current.Y))
                                else
                                    current

                            RestartRole.Receiver, awayFrom spot receiverTarget (float ctx.Config.SetPiece.BallClearance)
                        else
                            RestartRole.Support, awayFrom spot current (float ctx.Config.SetPiece.BallClearance)

                    targets.Add
                        { Side = currentSide
                          Player = player
                          Role = role
                          Position = position }

            let plan =
                { Id = id
                  Cause = cause
                  Kind = kind
                  Side = side
                  Spot = spot
                  Executor = executor
                  Targets = targets.ToArray()
                  Action = actionFor ctx world kind side executorSlot spot
                  AwardTick = awardTick }

            Ok plan

module RestartRules =

    type Rule =
        | BallSpot
        | ExecutorActive
        | ExecutorReady
        | OpponentDistance
        | GoalLineException
        | GoalkeeperPosition
        | PenaltyArea
        | KickoffGeometry
        | ThrowInGeometry
        | TargetReachability
        | BallClearance

    type Violation =
        { Rule: Rule
          Player: PlayerId option
          Detail: string }

    type Validation =
        { Valid: bool
          Violations: Violation list }

    let private violation rule player detail =
        { Rule = rule
          Player = player
          Detail = detail }

    let private clampPoint (x: float) (y: float) : Vec2 =
        let px, py = MatchMath.pitchClamp x y
        Vec2.create (px * 1.0<meter>) (py * 1.0<meter>)

    let private distance (a: Vec2) (b: Vec2) = MatchMath.dist a.X a.Y b.X b.Y

    let private insidePitch (point: Vec2) =
        abs (float point.X) <= float Pitch.HalfLength
        && abs (float point.Y) <= float Pitch.HalfWidth

    let validate (ctx: MatchContext) (world: World) (plan: RestartPlan) : Validation =
        let errors = ResizeArray<Violation>()
        let spot = plan.Spot
        let side = plan.Side
        let opponents = MatchWorld.oppOf side

        let add rule player detail =
            errors.Add(violation rule player detail)

        let spotLegal =
            match plan.Kind with
            | SetPieceKind.KickOff -> spot.X = 0.0<meter> && spot.Y = 0.0<meter>
            | SetPieceKind.Corner -> abs (abs (float spot.Y) - float Pitch.HalfWidth) < 0.01
            | SetPieceKind.GoalKick ->
                abs (float spot.Y) <= float Pitch.GoalAreaHalfWidth
                && abs (float spot.X - float (MatchMath.ownGoalX side))
                   <= float Pitch.GoalAreaDepth + 0.01
            | SetPieceKind.Penalty ->
                abs (float spot.Y) < 0.01
                && abs (float spot.X - float (MatchMath.goalX side)) = float Pitch.PenaltySpotDistance
            | SetPieceKind.ThrowIn
            | SetPieceKind.FreeKick -> insidePitch spot

        if not spotLegal then
            add Rule.BallSpot None "restart spot is outside the law-defined area"

        match MatchWorld.slotOf ctx side plan.Executor with
        | ValueNone -> add Rule.ExecutorActive (Some plan.Executor) "executor is not in the lineup"
        | ValueSome slot ->
            let frame = (MatchWorld.team world side).Frame

            if not (MatchWorld.isActive frame slot) then
                add Rule.ExecutorActive (Some plan.Executor) "executor is not active"

            let actual =
                clampPoint (float (MatchWorld.posX frame slot)) (float (MatchWorld.posY frame slot))

            // Ready means the boot reaches the ball: authorizing from further away only had the strike cancelled.
            if distance actual spot > float ctx.Config.Ball.FootReach then
                add Rule.ExecutorReady (Some plan.Executor) "executor cannot reach the ball"

        // A player sent off or injured after planning has left the pitch; he cannot hold up the restart.
        let onPitch (target: RestartTarget) =
            target.Player = plan.Executor
            || (match MatchWorld.slotIndex ctx target.Side target.Player with
                | -1 -> false
                | slot -> MatchWorld.isActive (MatchWorld.team world target.Side).Frame slot)

        for target in plan.Targets |> Array.filter onPitch do
            if not (insidePitch target.Position) then
                add Rule.TargetReachability (Some target.Player) "target is outside the pitch"

            let currentPosition =
                match MatchWorld.slotIndex ctx target.Side target.Player with
                | -1 -> ValueNone
                | slot ->
                    let frame = (MatchWorld.team world target.Side).Frame
                    ValueSome(clampPoint (float (MatchWorld.posX frame slot)) (float (MatchWorld.posY frame slot)))

            match currentPosition with
            | ValueSome current ->
                if distance current target.Position > float ctx.Config.SetPiece.MaxTargetDistance then
                    add Rule.TargetReachability (Some target.Player) "target is unreachable"

                // A throw-in leaves the hands, so no body near the line can touch it before release.
                if
                    target.Player <> plan.Executor
                    && plan.Kind <> SetPieceKind.ThrowIn
                    && distance current spot < float ctx.Config.SetPiece.BallClearance - float ctx.Config.SetPiece.TargetTolerance
                then
                    add Rule.BallClearance (Some target.Player) "player is still at the ball"
            | ValueNone -> add Rule.TargetReachability (Some target.Player) "target player is not in the lineup"

            if target.Side = opponents then
                match plan.Kind, target.Role with
                | SetPieceKind.Corner, _ ->
                    let required = float ctx.Config.SetPiece.WallDistance
                    let tolerance = float ctx.Config.SetPiece.TargetTolerance
                    let targetDistance = distance target.Position spot
                    let currentDistance =
                        match currentPosition with
                        | ValueSome current -> distance current spot
                        | ValueNone -> required

                    if targetDistance + tolerance < required then
                        add Rule.OpponentDistance (Some target.Player) "corner target is inside the minimum distance"

                    if currentDistance + tolerance < required then
                        add Rule.OpponentDistance (Some target.Player) "opponent has not yet vacated the corner distance"
                | SetPieceKind.FreeKick, RestartRole.Wall ->
                    let d = distance target.Position spot

                    if d + float ctx.Config.SetPiece.TargetTolerance < float ctx.Config.SetPiece.WallDistance then
                        add Rule.OpponentDistance (Some target.Player) "wall is inside the minimum distance"
                | SetPieceKind.FreeKick, RestartRole.Goalkeeper ->
                    let onGoalLine = abs (float target.Position.X - float (MatchMath.goalX side)) < 0.01
                    let betweenPosts = abs (float target.Position.Y) <= float Pitch.GoalHalfWidth

                    if not (onGoalLine && betweenPosts) then
                        add
                            Rule.GoalLineException
                            (Some target.Player)
                            "goalkeeper is not in a legal goal-line position"
                | SetPieceKind.Penalty, RestartRole.Goalkeeper ->
                    if
                        abs (float target.Position.X - float (MatchMath.goalX side)) > 0.01
                        || abs (float target.Position.Y) > 0.01
                    then
                        add
                            Rule.GoalkeeperPosition
                            (Some target.Player)
                            "penalty goalkeeper must remain on the goal line"
                | SetPieceKind.Penalty, _ when Pitch.isInPenaltyArea side target.Position.X target.Position.Y ->
                    add Rule.PenaltyArea (Some target.Player) "defender is inside the penalty area"
                | _ -> ()

        if plan.Kind = SetPieceKind.KickOff then
            for target in plan.Targets |> Array.filter onPitch do
                if
                    target.Side = opponents
                    && MatchMath.dist target.Position.X target.Position.Y 0.0<meter> 0.0<meter> < 9.15
                then
                    add Rule.KickoffGeometry (Some target.Player) "opponent is inside the centre circle"

        if plan.Kind = SetPieceKind.ThrowIn then
            match plan.Action with
            | RestartAction.RestartPass receiver ->
                match plan.Targets |> Array.tryFind (fun t -> t.Player = receiver) with
                | Some target when abs (float target.Position.Y) >= float Pitch.HalfWidth ->
                    add Rule.ThrowInGeometry (Some receiver) "throw-in receiver target is on the touchline"
                | None -> add Rule.ThrowInGeometry (Some receiver) "throw-in receiver is not planned"
                | _ -> ()
            | _ -> add Rule.ThrowInGeometry None "throw-in has no legal receiver action"

        { Valid = errors.Count = 0
          Violations = if errors.Count = 0 then [] else List.ofSeq errors }
