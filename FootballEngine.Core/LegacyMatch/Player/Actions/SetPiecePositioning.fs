namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module SetPiecePositioning =

    let private fromFrame (frame: TeamFrame) (i: int) : Spatial =
        { X = float frame.Physics.PosX[i] * 1.0<meter>
          Y = float frame.Physics.PosY[i] * 1.0<meter>
          Z = 0.0<meter>
          Vx = 0.0<meter / second>
          Vy = 0.0<meter / second>
          Vz = 0.0<meter / second> }

    let private at (x: float<meter>) (y: float<meter>) : Spatial = MatchSpatial.defaultSpatial x y

    let private activeSlots (frame: TeamFrame) : int[] =
        [| for i = 0 to frame.SlotCount - 1 do
               match frame.Physics.Occupancy[i] with
               | OccupancyKind.Active _ -> yield i
               | _ -> () |]

    let private candidates (frame: TeamFrame) (roster: PlayerRoster) (excludeIdx: int) : (int * Player) list =
        [ for i in activeSlots frame do
              if i <> excludeIdx then
                  match SimStateOps.tryGetPlayerFromFrame frame roster i with
                  | Some p -> yield (i, p)
                  | None -> () ]

    let private sortByAerialThreat (cands: (int * Player) list) : (int * Player) list =
        cands
        |> List.sortByDescending (fun (_, p) -> p.Physical.Strength + p.Technical.Heading)

    let private sortByHeight (cands: (int * Player) list) : (int * Player) list =
        cands |> List.sortByDescending (fun (_, p) -> p.Physical.JumpingReach)

    let private assign (positions: Spatial[]) (idx: int) (sp: Spatial) : unit = positions[idx] <- sp

    let private assignMany (positions: Spatial[]) (assignments: (int * Spatial) list) : unit =
        for (idx, sp) in assignments do
            positions[idx] <- sp

    let private goalXFor (dir: AttackDir) : float<meter> =
        match dir with
        | LeftToRight -> GoalLineHome
        | RightToLeft -> GoalLineAway

    let private insideOffset (dir: AttackDir) (d: float<meter>) : float<meter> = if dir = LeftToRight then -d else d

    let private clampToPitch (sp: Spatial) : Spatial =
        { sp with
            X = PhysicsContract.clamp sp.X 0.0<meter> PitchLength
            Y = PhysicsContract.clamp sp.Y 0.0<meter> PitchWidth }

    let private currentPositions (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) : Spatial[] =
        let frame = SimStateOps.getFrame state clubSide
        Array.init frame.SlotCount (fun i -> fromFrame frame i)

    // ============================================================
    // Attack positions (the team taking the set piece)
    // ============================================================

    let computePositions (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) : Spatial[] =
        let frame = SimStateOps.getFrame state clubSide
        let roster = SimStateOps.getRoster ctx clubSide
        let positions = currentPositions ctx state clubSide
        let spc = ctx.Config.SetPiece
        let dir = MatchSpatial.attackDirFor clubSide state
        let gX = goalXFor dir
        let centerY = PitchWidth / 2.0

        match state.Flow with
        | RestartDelay { Kind = kind; Team = side } when side = clubSide ->
            match kind with
            | SetPieceKind.Corner ->
                let ballPos = state.Ball.Position

                match SimStateOps.nearestActiveSlotInFrame frame ballPos.X ballPos.Y with
                | ValueNone -> positions
                | ValueSome takerIdx ->
                    assign positions takerIdx (at ballPos.X ballPos.Y)

                    let cands = candidates frame roster takerIdx |> sortByAerialThreat
                    let mutable i = 0

                    let take () =
                        if i < cands.Length then
                            let idx = i
                            i <- i + 1
                            Some cands[idx]
                        else
                            None

                    let nearPost = take ()
                    let farPost = take ()
                    let box1 = take ()
                    let box2 = take ()
                    let penSpot = take ()
                    let edge1 = take ()
                    let edge2 = take ()

                    let assignments = ResizeArray<int * Spatial>()

                    match nearPost with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir spc.CornerNearPostDist) PostNearY))
                    | None -> ()

                    match farPost with
                    | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir spc.CornerFarPostDist) PostFarY))
                    | None -> ()

                    match box1 with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir spc.CornerBoxDist) (centerY - 1.5<meter>)))
                    | None -> ()

                    match box2 with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir spc.CornerBoxDist) (centerY + 1.5<meter>)))
                    | None -> ()

                    match penSpot with
                    | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir PenaltySpotDistance) centerY))
                    | None -> ()

                    match edge1 with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir spc.CornerEdgeDist) (centerY - 6.0<meter>)))
                    | None -> ()

                    match edge2 with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir spc.CornerEdgeDist) (centerY + 6.0<meter>)))
                    | None -> ()

                    let stayBack =
                        [ for (idx, p) in cands do
                              if idx >= i then
                                  match p.Position with
                                  | DC
                                  | DM
                                  | GK -> yield (idx, p)
                                  | _ -> () ]
                        |> List.truncate spc.CornerStayBackCount

                    for (idx, _) in stayBack do
                        let safeX = gX - PhysicsContract.forwardX dir * (PenaltyAreaDepth + 5.0<meter>)
                        assignments.Add((idx, at safeX (float frame.Physics.PosY[idx] * 1.0<meter>)))

                    assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                    positions
            | SetPieceKind.FreeKick ->
                let ballPos = state.Ball.Position

                match SimStateOps.nearestActiveSlotInFrame frame ballPos.X ballPos.Y with
                | ValueNone -> positions
                | ValueSome takerIdx ->
                    assign positions takerIdx (at ballPos.X ballPos.Y)

                    let cands = candidates frame roster takerIdx |> sortByAerialThreat
                    let mutable i = 0

                    let take () =
                        if i < cands.Length then
                            let idx = i
                            i <- i + 1
                            Some cands[idx]
                        else
                            None

                    let near1 = take ()
                    let near2 = take ()
                    let box1 = take ()
                    let box2 = take ()
                    let edge1 = take ()

                    let assignments = ResizeArray<int * Spatial>()

                    let nearX =
                        if dir = LeftToRight then
                            ballPos.X - spc.FreeKickAttackerNearDist
                        else
                            ballPos.X + spc.FreeKickAttackerNearDist

                    match near1 with
                    | Some(idx, _) -> assignments.Add((idx, at nearX (ballPos.Y - 3.0<meter>)))
                    | None -> ()

                    match near2 with
                    | Some(idx, _) -> assignments.Add((idx, at nearX (ballPos.Y + 3.0<meter>)))
                    | None -> ()

                    match box1 with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir 9.0<meter>) (centerY - 3.0<meter>)))
                    | None -> ()

                    match box2 with
                    | Some(idx, _) ->
                        assignments.Add((idx, at (gX + insideOffset dir 9.0<meter>) (centerY + 3.0<meter>)))
                    | None -> ()

                    match edge1 with
                    | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir spc.CornerEdgeDist) centerY))
                    | None -> ()

                    let defensive =
                        [ for (idx, p) in cands do
                              if idx >= i then
                                  match p.Position with
                                  | DC
                                  | DM
                                  | GK -> yield (idx, p)
                                  | _ -> () ]

                    for (idx, _) in defensive do
                        let safeX =
                            if dir = LeftToRight then
                                ballPos.X - 20.0<meter>
                            else
                                ballPos.X + 20.0<meter>

                        assignments.Add((idx, at safeX (float frame.Physics.PosY[idx] * 1.0<meter>)))

                    assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                    positions
            | SetPieceKind.ThrowIn ->
                let ballPos = state.Ball.Position

                match SimStateOps.nearestActiveSlotInFrame frame ballPos.X ballPos.Y with
                | ValueNone -> positions
                | ValueSome takerIdx ->
                    assign positions takerIdx (at ballPos.X ballPos.Y)

                    let cands = candidates frame roster takerIdx
                    let near = cands |> List.truncate spc.ThrowInSupportCount
                    let assignments = ResizeArray<int * Spatial>()

                    for (idx, _) in near do
                        assignments.Add((idx, at ballPos.X (ballPos.Y + 4.0<meter>)))

                    let longSupport = cands |> List.skip spc.ThrowInSupportCount |> List.tryHead

                    match longSupport with
                    | Some(idx, p) ->
                        let targetX =
                            if dir = LeftToRight then
                                ballPos.X + 25.0<meter>
                            else
                                ballPos.X - 25.0<meter>

                        assignments.Add((idx, at targetX (float frame.Physics.PosY[idx] * 1.0<meter>)))
                    | None -> ()

                    assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                    positions
            | SetPieceKind.GoalKick ->
                let gkX =
                    if dir = LeftToRight then
                        GoalAreaDepth
                    else
                        PitchLength - GoalAreaDepth

                let gkSlot =
                    activeSlots frame
                    |> Array.tryFind (fun i ->
                        match SimStateOps.tryGetPlayerFromFrame frame roster i with
                        | Some p -> p.Position = GK
                        | None -> false)

                match gkSlot with
                | Some gkIdx -> assign positions gkIdx (at gkX (PitchWidth / 2.0))
                | None -> ()

                let cands = candidates frame roster (defaultArg gkSlot -1) |> sortByHeight
                let assignments = ResizeArray<int * Spatial>()

                let cb1 = cands |> List.tryItem 0
                let cb2 = cands |> List.tryItem 1
                let fb1 = cands |> List.tryItem 2
                let fb2 = cands |> List.tryItem 3
                let mid1 = cands |> List.tryItem 4
                let mid2 = cands |> List.tryItem 5

                match cb1 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx,
                         at
                             (if dir = LeftToRight then
                                  gkX + 12.0<meter>
                              else
                                  gkX - 12.0<meter>)
                             (centerY - 8.0<meter>))
                    )
                | None -> ()

                match cb2 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx,
                         at
                             (if dir = LeftToRight then
                                  gkX + 12.0<meter>
                              else
                                  gkX - 12.0<meter>)
                             (centerY + 8.0<meter>))
                    )
                | None -> ()

                match fb1 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx,
                         at
                             (if dir = LeftToRight then
                                  gkX + 28.0<meter>
                              else
                                  gkX - 28.0<meter>)
                             (centerY - 16.0<meter>))
                    )
                | None -> ()

                match fb2 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx,
                         at
                             (if dir = LeftToRight then
                                  gkX + 28.0<meter>
                              else
                                  gkX - 28.0<meter>)
                             (centerY + 16.0<meter>))
                    )
                | None -> ()

                match mid1 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx, at (HalfwayLineX - (PhysicsContract.forwardX dir) * 5.0<meter>) (centerY - 10.0<meter>))
                    )
                | None -> ()

                match mid2 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx, at (HalfwayLineX - (PhysicsContract.forwardX dir) * 5.0<meter>) (centerY + 10.0<meter>))
                    )
                | None -> ()

                let forward = cands |> List.skip 6 |> List.truncate 2

                for (idx, _) in forward do
                    let fX =
                        if dir = LeftToRight then
                            HalfwayLineX + 8.0<meter>
                        else
                            HalfwayLineX - 8.0<meter>

                    assignments.Add((idx, at fX (float frame.Physics.PosY[idx] * 1.0<meter>)))

                assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                positions
            | SetPieceKind.KickOff ->
                let ballPos = state.Ball.Position

                match SimStateOps.nearestActiveSlotInFrame frame ballPos.X ballPos.Y with
                | ValueNone -> positions
                | ValueSome takerIdx ->
                    assign positions takerIdx (at ballPos.X ballPos.Y)

                    let cands = candidates frame roster takerIdx

                    match cands |> List.tryHead with
                    | Some(idx, _) ->
                        assign
                            positions
                            idx
                            (at (ballPos.X + spc.KickOffPartnerOffsetX) (ballPos.Y + spc.KickOffPartnerOffsetY))
                    | None -> ()

                    positions
            | SetPieceKind.Penalty ->
                let ballPos = state.Ball.Position

                match SimStateOps.nearestActiveSlotInFrame frame ballPos.X ballPos.Y with
                | ValueNone -> positions
                | ValueSome takerIdx ->
                    assign positions takerIdx (at ballPos.X ballPos.Y)

                    let cands = candidates frame roster takerIdx
                    let assignments = ResizeArray<int * Spatial>()

                    let edgeX =
                        if dir = LeftToRight then
                            gX - PenaltyAreaDepth
                        else
                            gX + PenaltyAreaDepth

                    let edge = cands |> List.truncate 2

                    for (idx, _) in edge do
                        assignments.Add(
                            (idx,
                             at
                                 edgeX
                                 (centerY
                                  + if List.contains idx (List.map fst edge) then
                                        -2.0<meter>
                                    else
                                        2.0<meter>))
                        )

                    assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                    positions
        | _ -> positions

    // ============================================================
    // Defense positions (the team defending the set piece)
    // ============================================================

    let computeDefensePositions (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) : Spatial[] =
        let frame = SimStateOps.getFrame state clubSide
        let roster = SimStateOps.getRoster ctx clubSide
        let positions = currentPositions ctx state clubSide
        let spc = ctx.Config.SetPiece
        let dir = MatchSpatial.attackDirFor clubSide state
        let gX = goalXFor dir
        let centerY = PitchWidth / 2.0

        let gkSlot =
            activeSlots frame
            |> Array.tryFind (fun i ->
                match SimStateOps.tryGetPlayerFromFrame frame roster i with
                | Some p -> p.Position = GK
                | None -> false)

        match state.Flow with
        | RestartDelay { Kind = kind; Team = _ } ->
            match kind with
            | SetPieceKind.Corner ->
                let assignments = ResizeArray<int * Spatial>()

                match gkSlot with
                | Some gkIdx -> assignments.Add((gkIdx, at (gX + insideOffset dir spc.CornerGKLineDist) centerY))
                | None -> ()

                let cands = candidates frame roster (defaultArg gkSlot -1) |> sortByHeight
                let mutable i = 0

                let take () =
                    if i < cands.Length then
                        let idx = i
                        i <- i + 1
                        Some cands[idx]
                    else
                        None

                let nearPost = take ()
                let farPost = take ()
                let box1 = take ()
                let box2 = take ()
                let box3 = take ()
                let box4 = take ()
                let edge1 = take ()
                let edge2 = take ()
                let counter1 = take ()
                let counter2 = take ()

                match nearPost with
                | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir spc.CornerDefNearPostDist) PostNearY))
                | None -> ()

                match farPost with
                | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir spc.CornerDefFarPostDist) PostFarY))
                | None -> ()

                match box1 with
                | Some(idx, _) ->
                    assignments.Add((idx, at (gX + insideOffset dir spc.CornerDefBoxDist) (centerY - 4.0<meter>)))
                | None -> ()

                match box2 with
                | Some(idx, _) ->
                    assignments.Add((idx, at (gX + insideOffset dir spc.CornerDefBoxDist) (centerY + 4.0<meter>)))
                | None -> ()

                match box3 with
                | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir 8.0<meter>) (centerY - 8.0<meter>)))
                | None -> ()

                match box4 with
                | Some(idx, _) -> assignments.Add((idx, at (gX + insideOffset dir 8.0<meter>) (centerY + 8.0<meter>)))
                | None -> ()

                match edge1 with
                | Some(idx, _) ->
                    assignments.Add((idx, at (gX + insideOffset dir spc.CornerDefEdgeDist) (centerY - 6.0<meter>)))
                | None -> ()

                match edge2 with
                | Some(idx, _) ->
                    assignments.Add((idx, at (gX + insideOffset dir spc.CornerDefEdgeDist) (centerY + 6.0<meter>)))
                | None -> ()

                match counter1 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx, at (gX - (PhysicsContract.forwardX dir) * 30.0<meter>) (centerY - 12.0<meter>))
                    )
                | None -> ()

                match counter2 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx, at (gX - (PhysicsContract.forwardX dir) * 30.0<meter>) (centerY + 12.0<meter>))
                    )
                | None -> ()

                assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                positions
            | SetPieceKind.FreeKick ->
                let ballX = state.Ball.Position.X
                let ballY = state.Ball.Position.Y
                let distToGoal = abs (gX - ballX)

                if distToGoal < 35.0<meter> then
                    let wallPosition, wallSpatials =
                        WallBehavior.buildWall spc roster frame ballX ballY gX

                    let assignments = ResizeArray<int * Spatial>()

                    let wallIdxMap =
                        wallPosition.PlayerIds
                        |> List.choose (fun pid ->
                            activeSlots frame
                            |> Array.tryFind (fun i ->
                                match SimStateOps.tryGetPlayerFromFrame frame roster i with
                                | Some p -> p.Id = pid
                                | None -> false)
                            |> Option.map (fun idx -> idx))
                        |> List.mapi (fun j idx -> idx, wallSpatials[j])

                    for (idx, sp) in wallIdxMap do
                        assignments.Add((idx, sp))

                    match gkSlot with
                    | Some gkIdx -> assignments.Add((gkIdx, at gX centerY))
                    | None -> ()

                    let nonWall =
                        [ for i in activeSlots frame do
                              if i <> (defaultArg gkSlot -1) && not (List.contains i (List.map fst wallIdxMap)) then
                                  match SimStateOps.tryGetPlayerFromFrame frame roster i with
                                  | Some p -> yield (i, p)
                                  | None -> () ]
                        |> sortByHeight

                    let mutable j = 0

                    for (idx, p) in nonWall do
                        match p.Position with
                        | DC
                        | DM when j < 3 ->
                            let boxOffset = float (j % 2) * 6.0<meter> - 3.0<meter>
                            let boxDist = 9.0<meter> + float (j / 2) * 4.0<meter>

                            assignments.Add((idx, at (gX + insideOffset dir boxDist) (centerY + boxOffset)))

                            j <- j + 1
                        | _ -> ()

                    assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                    positions
                else
                    let assignments = ResizeArray<int * Spatial>()

                    match gkSlot with
                    | Some gkIdx -> assignments.Add((gkIdx, at gX centerY))
                    | None -> ()

                    let nonWall = candidates frame roster (defaultArg gkSlot -1) |> sortByHeight

                    let mutable j = 0

                    for (idx, p) in nonWall do
                        match p.Position with
                        | DC
                        | DM when j < 4 ->
                            let row = j / 2
                            let col = j % 2
                            let dist = 12.0<meter> + float row * 8.0<meter>
                            let yOff = if col = 0 then -8.0<meter> else 8.0<meter>

                            assignments.Add((idx, at (gX + insideOffset dir dist) (centerY + yOff)))

                            j <- j + 1
                        | _ -> ()

                    assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                    positions
            | SetPieceKind.ThrowIn ->
                let ballPos = state.Ball.Position
                let assignments = ResizeArray<int * Spatial>()

                match gkSlot with
                | Some gkIdx ->
                    assignments.Add(
                        (gkIdx,
                         at
                             (float frame.Physics.PosX[gkIdx] * 1.0<meter>)
                             (float frame.Physics.PosY[gkIdx] * 1.0<meter>))
                    )
                | None -> ()

                let presser =
                    candidates frame roster (defaultArg gkSlot -1)
                    |> List.sortBy (fun (_, p) -> p.Mental.Aggression)
                    |> List.tryHead

                match presser with
                | Some(idx, _) -> assignments.Add((idx, at ballPos.X (ballPos.Y + 3.0<meter>)))
                | None -> ()

                let rest =
                    candidates frame roster (defaultArg gkSlot -1)
                    |> List.filter (fun (idx, _) -> Some idx <> (presser |> Option.map fst))

                for (idx, _) in rest do
                    let curX = float frame.Physics.PosX[idx] * 1.0<meter>
                    assignments.Add((idx, at curX (float frame.Physics.PosY[idx] * 1.0<meter>)))

                assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                positions
            | SetPieceKind.GoalKick ->
                let gkX =
                    if dir = LeftToRight then
                        GoalAreaDepth
                    else
                        PitchLength - GoalAreaDepth

                let assignments = ResizeArray<int * Spatial>()

                let press1 =
                    candidates frame roster (defaultArg gkSlot -1)
                    |> List.sortByDescending (fun (_, p) -> p.Physical.Pace)
                    |> List.tryHead

                let press2 =
                    candidates frame roster (defaultArg gkSlot -1)
                    |> List.sortByDescending (fun (_, p) -> p.Physical.Pace)
                    |> List.tryItem 1

                match press1 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx,
                         at
                             (if dir = LeftToRight then
                                  gkX + 8.0<meter>
                              else
                                  gkX - 8.0<meter>)
                             (centerY - 5.0<meter>))
                    )
                | None -> ()

                match press2 with
                | Some(idx, _) ->
                    assignments.Add(
                        (idx,
                         at
                             (if dir = LeftToRight then
                                  gkX + 8.0<meter>
                              else
                                  gkX - 8.0<meter>)
                             (centerY + 5.0<meter>))
                    )
                | None -> ()

                assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                positions
            | SetPieceKind.KickOff ->
                let assignments = ResizeArray<int * Spatial>()

                let theirHalfOffset = if dir = LeftToRight then -10.0<meter> else 10.0<meter>

                for i in activeSlots frame do
                    let sp = fromFrame frame i

                    if
                        (dir = LeftToRight && sp.X > HalfwayLineX)
                        || (dir = RightToLeft && sp.X < HalfwayLineX)
                    then
                        assignments.Add(
                            (i, at (PhysicsContract.clamp (sp.X + theirHalfOffset) 0.0<meter> HalfwayLineX) sp.Y)
                        )

                assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                positions
            | SetPieceKind.Penalty ->
                let assignments = ResizeArray<int * Spatial>()

                match gkSlot with
                | Some gkIdx -> assignments.Add((gkIdx, at gX centerY))
                | None -> ()

                let edgeX =
                    if dir = LeftToRight then
                        gX - PenaltyAreaDepth
                    else
                        gX + PenaltyAreaDepth

                let others = candidates frame roster (defaultArg gkSlot -1) |> List.truncate 2

                for (idx, _) in others do
                    assignments.Add((idx, at edgeX (centerY - 3.0<meter>)))

                assignMany positions (List.ofSeq assignments |> List.map (fun (i, sp) -> i, clampToPitch sp))
                positions
        | _ -> positions

    let assignPositions (state: SimState) (positions: Spatial[]) (clubSide: ClubSide) : unit =
        let team = SimStateOps.getTeam state clubSide
        team.SetPiecePositions <- Some positions
