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

    let private restartSpot kind side x y : Vec2 =
        match kind with
        | SetPieceKind.KickOff -> Vec2.create 0.0<meter> 0.0<meter>
        | SetPieceKind.Corner ->
            Vec2.create (MatchMath.goalX side) (if y >= 0.0<meter> then Pitch.HalfWidth else -Pitch.HalfWidth)
        | SetPieceKind.GoalKick ->
            Vec2.create (MatchMath.ownGoalX side + Pitch.GoalAreaDepth * MatchMath.goalDirection side) 0.0<meter>
        | SetPieceKind.Penalty ->
            Vec2.create (MatchMath.goalX side - Pitch.PenaltySpotDistance * MatchMath.goalDirection side) 0.0<meter>
        | SetPieceKind.ThrowIn
        | SetPieceKind.FreeKick -> clampPoint (float x) (float y)

    let private executorPoint (kind: SetPieceKind) (side: ClubSide) (spot: Vec2) : Vec2 =
        match kind with
        | SetPieceKind.Corner -> clampPoint (float spot.X - 1.0 * MatchMath.goalDirection side) (float spot.Y)
        | _ -> spot

    let private awayFrom (spot: Vec2) (point: Vec2) (required: float) : Vec2 =
        let dx = float point.X - float spot.X
        let dy = float point.Y - float spot.Y
        let d = sqrt (dx * dx + dy * dy)

        if d >= required then
            point
        elif d < 0.001 then
            clampPoint (float spot.X + required) (float spot.Y)
        else
            clampPoint (float spot.X + dx / d * required) (float spot.Y + dy / d * required)

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
                if receiver < 0 then receiver <- slot

        let receiverId slot = (MatchWorld.lineup ctx side).Profiles.[slot].Id

        match kind with
        | SetPieceKind.Penalty ->
            RestartAction.RestartShot(MatchMath.goalX side, 0.0<meter>)
        | SetPieceKind.Corner ->
            RestartAction.RestartCross(MatchMath.goalX side - 8.0<meter> * dir, 0.0<meter>)
        | SetPieceKind.FreeKick when MatchMath.distanceToGoal side spot.X spot.Y <= float ctx.Config.SetPiece.DirectFreeKickRange ->
            RestartAction.RestartShot(MatchMath.goalX side, 0.0<meter>)
        | SetPieceKind.GoalKick ->
            if receiver >= 0 then RestartAction.RestartPass(receiverId receiver)
            else RestartAction.RestartClearance(spot.X + 25.0<meter> * dir, spot.Y)
        | SetPieceKind.ThrowIn
        | SetPieceKind.KickOff
        | SetPieceKind.FreeKick ->
             if receiver >= 0 then RestartAction.RestartPass(receiverId receiver)
             else RestartAction.RestartClearance(spot.X + 15.0<meter> * dir, spot.Y)

    let private executorSlot (world: World) (side: ClubSide) (kind: SetPieceKind) (spot: Vec2) =
        let frame = (MatchWorld.team world side).Frame
        let slots =
            MatchWorld.activeSlots frame
            |> Array.filter (fun slot -> kind = SetPieceKind.GoalKick || slot <> 0)
            |> Array.sortBy (fun slot -> MatchMath.dist spot.X spot.Y (MatchWorld.posX frame slot) (MatchWorld.posY frame slot), slot)

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
        let spot = restartSpot kind side x y
        match executorSlot world side kind spot with
        | None -> Error PlanError.NoExecutor
        | Some executorSlot ->
            let executor = (MatchWorld.lineup ctx side).Profiles.[executorSlot].Id
            let executorTarget = executorPoint kind side spot
            let opponent = MatchWorld.oppOf side
            let mutable targets = ResizeArray<RestartTarget>()

            for currentSide in [ side; opponent ] do
                let currentTeam = MatchWorld.team world currentSide
                let currentFrame = currentTeam.Frame

                for slot in MatchWorld.activeSlots currentFrame do
                    let player = (MatchWorld.lineup ctx currentSide).Profiles.[slot].Id
                    let current = clampPoint (float (MatchWorld.posX currentFrame slot)) (float (MatchWorld.posY currentFrame slot))
                    let role, position =
                        if currentSide = side && player = executor then
                            RestartRole.Executor, executorTarget
                        elif currentSide = opponent && kind = SetPieceKind.KickOff then
                            RestartRole.Support, awayFrom spot current 9.15
                        elif currentSide = opponent && kind = SetPieceKind.FreeKick then
                            let target =
                                if slot = 0 then
                                    clampPoint (float (MatchMath.ownGoalX opponent)) 0.0
                                else
                                    awayFrom spot current (float ctx.Config.SetPiece.WallDistance)

                            if slot = 0 then RestartRole.Goalkeeper, target else RestartRole.Wall, target
                        elif currentSide = opponent && kind = SetPieceKind.Penalty then
                            if slot = 0 then
                                RestartRole.Goalkeeper, clampPoint (float (MatchMath.goalX side)) 0.0
                            else
                                RestartRole.Support, awayFrom spot current (float Pitch.PenaltyAreaDepth)
                        elif currentSide = side &&
                             (match actionFor ctx world kind side executorSlot spot with
                              | RestartAction.RestartPass receiverId' when receiverId' = player -> true
                              | _ -> false) then
                            RestartRole.Receiver, current
                        else
                            RestartRole.Support, current

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

    type Violation =
        { Rule: Rule
          Player: PlayerId option
          Detail: string }

    type Validation =
        { Valid: bool
          Violations: Violation list }

    let private violation rule player detail =
        { Rule = rule; Player = player; Detail = detail }

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

        let add rule player detail = errors.Add(violation rule player detail)

        let spotLegal =
            match plan.Kind with
            | SetPieceKind.KickOff -> spot.X = 0.0<meter> && spot.Y = 0.0<meter>
            | SetPieceKind.Corner -> abs (abs (float spot.Y) - float Pitch.HalfWidth) < 0.01
            | SetPieceKind.GoalKick ->
                abs (float spot.Y) <= float Pitch.GoalAreaHalfWidth
                && abs (float spot.X - float (MatchMath.ownGoalX side)) <= float Pitch.GoalAreaDepth + 0.01
            | SetPieceKind.Penalty -> abs (float spot.Y) < 0.01 && abs (float spot.X - float (MatchMath.goalX side)) = float Pitch.PenaltySpotDistance
            | SetPieceKind.ThrowIn
            | SetPieceKind.FreeKick -> insidePitch spot

        if not spotLegal then add Rule.BallSpot None "restart spot is outside the law-defined area"

        match MatchWorld.slotOf ctx side plan.Executor with
        | None -> add Rule.ExecutorActive (Some plan.Executor) "executor is not in the lineup"
        | Some slot ->
            let frame = (MatchWorld.team world side).Frame
            if not (MatchWorld.isActive frame slot) then add Rule.ExecutorActive (Some plan.Executor) "executor is not active"
            let actual = clampPoint (float (MatchWorld.posX frame slot)) (float (MatchWorld.posY frame slot))
            let executorTarget =
                plan.Targets
                |> Array.tryFind (fun target -> target.Player = plan.Executor)
                |> Option.map (fun target -> target.Position)
                |> Option.defaultValue spot

            if distance actual executorTarget > float ctx.Config.SetPiece.ContactReadyRadius then
                add Rule.ExecutorReady (Some plan.Executor) "executor is not within contact readiness radius"

        for target in plan.Targets do
            if not (insidePitch target.Position) then
                add Rule.TargetReachability (Some target.Player) "target is outside the pitch"

            match MatchWorld.slotOf ctx target.Side target.Player with
            | Some slot ->
                let frame = (MatchWorld.team world target.Side).Frame
                let current = clampPoint (float (MatchWorld.posX frame slot)) (float (MatchWorld.posY frame slot))
                if distance current target.Position > float ctx.Config.SetPiece.MaxTargetDistance then
                    add Rule.TargetReachability (Some target.Player) "target is unreachable"
            | None -> add Rule.TargetReachability (Some target.Player) "target player is not in the lineup"

            if target.Side = opponents then
                match plan.Kind, target.Role with
                | SetPieceKind.FreeKick, RestartRole.Wall ->
                    let d = distance target.Position spot
                    if d + float ctx.Config.SetPiece.TargetTolerance < float ctx.Config.SetPiece.WallDistance then
                        add Rule.OpponentDistance (Some target.Player) "wall is inside the minimum distance"
                | SetPieceKind.FreeKick, RestartRole.Goalkeeper ->
                    let onGoalLine = abs (float target.Position.X - float (MatchMath.goalX side)) < 0.01
                    let betweenPosts = abs (float target.Position.Y) <= float Pitch.GoalHalfWidth
                    if not (onGoalLine && betweenPosts) then
                        add Rule.GoalLineException (Some target.Player) "goalkeeper is not in a legal goal-line position"
                | SetPieceKind.Penalty, RestartRole.Goalkeeper ->
                    if abs (float target.Position.X - float (MatchMath.goalX side)) > 0.01 || abs (float target.Position.Y) > 0.01 then
                        add Rule.GoalkeeperPosition (Some target.Player) "penalty goalkeeper must remain on the goal line"
                | SetPieceKind.Penalty, _ when Pitch.isInPenaltyArea side target.Position.X target.Position.Y ->
                    add Rule.PenaltyArea (Some target.Player) "defender is inside the penalty area"
                | _ -> ()

        if plan.Kind = SetPieceKind.KickOff then
            for target in plan.Targets do
                if target.Side = opponents && MatchMath.dist target.Position.X target.Position.Y 0.0<meter> 0.0<meter> < 9.15 then
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
          Violations = List.ofSeq errors }
