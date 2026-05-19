namespace FootballEngine.Player.Decision

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract




module MovementScorer =

    let private normStat (v: int) = float v / 20.0
    let private normCond (v: float32) = float v / 100.0

    let private hasBall (ballState: BallPhysicsState) (pid: PlayerId) =
        match ballState.Control with
        | Controlled(_, p)
        | Receiving(_, p, _) -> p = pid
        | _ -> false

    let maintainShapeScore (ctx: AgentContext) =
        let baseScore = 0.3

        let positionBonus =
            match ctx.Me.Position with
            | DC
            | DM
            | MC -> 0.20
            | DL
            | DR
            | WBL
            | WBR -> 0.12
            | GK -> 0.45
            | _ -> 0.10

        if ctx.TeamHasBall then
            (baseScore + positionBonus) * normCond ctx.MyCondition
        else
            (baseScore + positionBonus - ctx.Profile.PressingIntensity * 0.15)
            * normCond ctx.MyCondition

    let markManScore (ctx: AgentContext) =
        if ctx.TeamHasBall then
            0.0
        else
            let bcIdx = int ctx.BallCarrierOppIdx
            let hasBallCarrierInfo = bcIdx >= 0

            let (oppIdx, oppX, oppY) =
                if hasBallCarrierInfo then
                    let ox = float ctx.Team.OppFrame.Physics.PosX[bcIdx] * 1.0<meter>
                    let oy = float ctx.Team.OppFrame.Physics.PosY[bcIdx] * 1.0<meter>
                    bcIdx, ox, oy
                else
                    match ctx.NearestOpponentIdx with
                    | ValueNone -> -1, 0.0<meter>, 0.0<meter>
                    | ValueSome idx ->
                        let ox = float ctx.Team.OppFrame.Physics.PosX[idx] * 1.0<meter>
                        let oy = float ctx.Team.OppFrame.Physics.PosY[idx] * 1.0<meter>
                        idx, ox, oy

            if oppIdx < 0 then
                0.0
            else
                let oppPos =
                    { X = oppX
                      Y = oppY
                      Z = 0.0<meter>
                      Vx = 0.0<meter / second>
                      Vy = 0.0<meter / second>
                      Vz = 0.0<meter / second> }

                let dist = ctx.MyPos.DistTo2D oppPos
                let markRadius = if hasBallCarrierInfo then 25.0<meter> else 20.0<meter>

                if dist > markRadius then
                    0.0
                else
                    let baseScore = 1.0 - (float dist / float markRadius)
                    let positioningWeight = normStat ctx.Me.Mental.Positioning

                    let defensiveBonus =
                        match ctx.Me.Position with
                        | DC
                        | DM
                        | DL
                        | DR
                        | WBL
                        | WBR -> 0.35
                        | MC -> 0.2
                        | _ -> 0.05

                    let shapeFactor = ctx.Tactics.DefensiveShape
                    let markBonus = shapeFactor * 0.3

                    let ballCarrierBonus = if hasBallCarrierInfo then 0.5 else 0.0

                    (baseScore * positioningWeight + defensiveBonus + markBonus + ballCarrierBonus)
                    * normCond ctx.MyCondition

    let pressBallScore (ctx: AgentContext) =
        if ctx.TeamHasBall then
            0.0
        else
            let dist = ctx.MyPos.DistTo2D ctx.BallState.Position

            if dist > 45.0<meter> then
                0.0
            else
                let zoneScore =
                    if dist < 12.0<meter> then
                        1.5 - float dist / 12.0
                    elif dist < 25.0<meter> then
                        1.2 - (float dist - 12.0) / 13.0
                    elif dist < 45.0<meter> then
                        0.6 - (float dist - 25.0) / 20.0
                    else
                        0.0

                let pressFactor =
                    ctx.Tactics.PressingIntensity * 0.5 + ctx.Profile.PressingIntensity * 0.5

                let pressWeight = 0.4 + pressFactor * 0.6
                let workRate = normStat ctx.Me.Mental.WorkRate

                let positionMod =
                    match ctx.Me.Position with
                    | MC
                    | AML
                    | AMR
                    | AMC -> 0.15
                    | ST -> 0.1
                    | _ -> 0.0

                let staminaPenalty = float (100.0f - ctx.MyCondition) / 100.0 * 0.2

                let transitionBonus =
                    if ctx.CurrentSubTick < ctx.TransitionPressExpiry then
                        1.5
                    else
                        1.0

                let frame = ctx.Team.OwnFrame

                let shapeDist =
                    if ctx.MeIdx < frame.SlotCount then
                        let spX = float frame.SupportPositionX[ctx.MeIdx] * 1.0<meter>
                        let spY = float frame.SupportPositionY[ctx.MeIdx] * 1.0<meter>

                        if spX <> 0.0<meter> || spY <> 0.0<meter> then
                            ctx.MyPos.DistTo2D
                                { X = spX
                                  Y = spY
                                  Z = 0.0<meter>
                                  Vx = 0.0<meter / second>
                                  Vy = 0.0<meter / second>
                                  Vz = 0.0<meter / second> }
                        else
                            0.0<meter>
                    else
                        0.0<meter>

                let positionalPenalty =
                    if shapeDist > 15.0<meter> then
                        1.0 - (float shapeDist - 15.0) / 30.0
                    else
                        1.0

                (zoneScore * pressWeight * workRate * transitionBonus * positionalPenalty
                 + positionMod
                 - staminaPenalty)
                |> max 0.0

    let coverSpaceScore (ctx: AgentContext) =
        let zonalFactor = (1.0 - ctx.Tactics.DefensiveShape) * 0.3

        let baseScore =
            if ctx.TeamHasBall then
                match ctx.Me.Position with
                | DC
                | DM -> (0.15 * normStat ctx.Me.Mental.Positioning + zonalFactor)
                | _ -> 0.05
            else
                match ctx.Me.Position with
                | DC
                | DM -> (0.35 * normStat ctx.Me.Mental.Positioning + zonalFactor)
                | DL
                | DR
                | WBL
                | WBR -> (0.2 * normStat ctx.Me.Mental.Positioning + zonalFactor)
                | _ -> 0.1 + zonalFactor

        let influenceBonus =
            let cell =
                InfluenceTypes.posToCell
                    ctx.Team.OwnFrame.Physics.PosX[ctx.MeIdx]
                    ctx.Team.OwnFrame.Physics.PosY[ctx.MeIdx]

            let contested = ctx.Influence.ContestedGrid[cell]
            let gapRead = normStat ctx.Me.Mental.Positioning

            if contested < 0.0f then
                let deficit = min 1.0f (-contested)
                float deficit * 0.15 * gapRead
            else
                0.0

        baseScore + influenceBonus

    let supportAttackScore (ctx: AgentContext) =
        if not ctx.TeamHasBall then
            0.0
        elif hasBall ctx.BallState ctx.Me.Id then
            0.0
        else
            let baseScore = 0.5
            let offTheBall = normStat ctx.Me.Mental.WorkRate
            let depthBonus = ctx.Profile.AttackingDepth * 0.4

            let widthBonus = ctx.Tactics.Width * ctx.Profile.LateralTendency * 0.25

            let positionBonus =
                match ctx.Me.Position with
                | ST
                | AML
                | AMR
                | AMC -> 0.4
                | ML
                | MR
                | MC -> 0.3
                | DL
                | DR
                | WBL
                | WBR -> 0.2
                | DC
                | DM -> 0.1
                | GK -> 0.0

            let spaceBonus =
                let mutable bestScore = 0.0

                for cell = 0 to InfluenceTypes.GridSize - 1 do
                    let s =
                        InfluenceTypes.scoreCellRaw
                            cell
                            ctx.Influence
                            (float32 ctx.MyPos.X)
                            (float32 ctx.MyPos.Y)
                            ctx.Team.AttackDir

                    if s > bestScore then
                        bestScore <- s

                if bestScore > 0.4 then
                    let positioning = normStat ctx.Me.Mental.Positioning
                    let qualityFactor = min 1.0 ((bestScore - 0.4) / 0.6)
                    qualityFactor * 0.20 * positioning
                else
                    0.0

            (baseScore
             + offTheBall * 0.4
             + depthBonus
             + positionBonus
             + widthBonus
             + spaceBonus)
            * normCond ctx.MyCondition

    let recoverBallScore (ctx: AgentContext) =
        if ctx.TeamHasBall then
            0.0
        else
            match ctx.BallState.Control with
            | Free
            | Contesting _ ->
                let dist = ctx.MyPos.DistTo2D ctx.BallState.Position

                if dist > 20.0<meter> then
                    0.0
                else
                    let baseScore = 1.5 - (float dist / 20.0)

                    let role =
                        if ctx.MeIdx < ctx.Team.OwnFrame.SlotCount then
                            LanguagePrimitives.EnumOfValue<byte, DefensiveRole>
                                ctx.Team.OwnFrame.DefensiveRole[ctx.MeIdx]
                        else
                            DefensiveRole.Marker

                    let roleMod =
                        match role with
                        | DefensiveRole.FirstDefender -> 1.2
                        | _ -> if dist < 5.0<meter> then 0.8 else 0.3

                    if role = DefensiveRole.FirstDefender then
                        // The primary chaser is not constrained by tactical shape
                        baseScore * roleMod
                    else
                        let frame = ctx.Team.OwnFrame

                        let shapeDist =
                            if ctx.MeIdx < frame.SlotCount then
                                let spX = float frame.SupportPositionX[ctx.MeIdx] * 1.0<meter>
                                let spY = float frame.SupportPositionY[ctx.MeIdx] * 1.0<meter>

                                if spX <> 0.0<meter> || spY <> 0.0<meter> then
                                    ctx.MyPos.DistTo2D
                                        { X = spX
                                          Y = spY
                                          Z = 0.0<meter>
                                          Vx = 0.0<meter / second>
                                          Vy = 0.0<meter / second>
                                          Vz = 0.0<meter / second> }
                                else
                                    0.0<meter>
                            else
                                0.0<meter>

                        let positionalPenalty =
                            if shapeDist > 12.0<meter> then
                                1.0 - (float shapeDist - 12.0) / 25.0
                            else
                                1.0

                        baseScore * roleMod * positionalPenalty
            | Controlled(oppositeSide, _)
            | Receiving(oppositeSide, _, _) ->
                if oppositeSide = ctx.Team.ClubSide then
                    0.0
                else
                    let dist = ctx.MyPos.DistTo2D ctx.BallState.Position

                    if dist > 15.0<meter> then
                        0.0
                    else
                        0.8 - (float dist / 15.0) * 0.4
            | _ -> 0.0

    let applyEmergentModifiers (emergent: EmergentState) (_ctx: AgentContext) (scores: MovementScores) : MovementScores =
        { scores with
            PressBall = scores.PressBall * emergent.PressingIntensity
            MarkMan = scores.MarkMan * emergent.CompactnessLevel
            CoverSpace = scores.CoverSpace * emergent.CompactnessLevel
            SupportAttack = scores.SupportAttack * emergent.RiskAppetite }

    let interceptPassScore (ctx: AgentContext) : float =
        if ctx.TeamHasBall then
            0.0
        else
            let frame = ctx.Team.OwnFrame
            if ctx.MeIdx < frame.SlotCount then
                match LanguagePrimitives.EnumOfValue<byte, DefensiveRole> frame.DefensiveRole[ctx.MeIdx] with
                | DefensiveRole.Marker ->
                    let myX = ctx.MyPos.X
                    let myY = ctx.MyPos.Y
                    let bcIdx = int ctx.BallCarrierOppIdx

                    if bcIdx >= 0 then
                        let bcX = float ctx.Team.OppFrame.Physics.PosX[bcIdx] * 1.0<meter>
                        let bcY = float ctx.Team.OppFrame.Physics.PosY[bcIdx] * 1.0<meter>

                        let dist =
                            ctx.MyPos.DistTo2D
                                { X = bcX
                                  Y = bcY
                                  Z = 0.0<meter>
                                  Vx = 0.0<meter / second>
                                  Vy = 0.0<meter / second>
                                  Vz = 0.0<meter / second> }

                        if dist > 25.0<meter> then
                            0.0
                        else
                            let mutable bestInterceptScore = 0.0

                            for j = 0 to ctx.Team.OppFrame.SlotCount - 1 do
                                match ctx.Team.OppFrame.Physics.Occupancy[j] with
                                | OccupancyKind.Active _ when j <> bcIdx ->
                                    let ox = float ctx.Team.OppFrame.Physics.PosX[j] * 1.0<meter>
                                    let oy = float ctx.Team.OppFrame.Physics.PosY[j] * 1.0<meter>
                                    let lx1, ly1 = float32 bcX, float32 bcY
                                    let lx2, ly2 = float32 ox, float32 oy

                                    let laneDistSq =
                                        MatchSpatial.pointToLineDistSq (float32 myX) (float32 myY) lx1 ly1 lx2 ly2

                                    if laneDistSq < 9.0f then
                                        let bonus = 1.0 - float laneDistSq / 9.0
                                        bestInterceptScore <- max bestInterceptScore bonus * 0.5
                                | _ -> ()

                            bestInterceptScore
                    else
                        0.0
                | _ -> 0.0
            else
                0.0

    let applyRoleModifiersForRole (defRole: DefensiveRole) (teamHasBall: bool) (scores: MovementScores) : MovementScores =
        if not teamHasBall then
            match defRole with
            | DefensiveRole.FirstDefender ->
                { scores with
                    PressBall = scores.PressBall * 2.0
                    MarkMan = scores.MarkMan * 0.3
                    CoverSpace = scores.CoverSpace * 0.3 }
            | DefensiveRole.Cover ->
                { scores with
                    CoverSpace = scores.CoverSpace * 2.0
                    MarkMan = scores.MarkMan * 0.5
                    PressBall = scores.PressBall * 0.3 }
            | DefensiveRole.Marker ->
                { scores with
                    MarkMan = scores.MarkMan * 2.0
                    PressBall = scores.PressBall * 0.3
                    CoverSpace = scores.CoverSpace * 0.5 }
            | _ -> scores
        else
            scores

    let private applyRoleModifiers (ctx: AgentContext) (scores: MovementScores) : MovementScores =
        applyRoleModifiersForRole
            (LanguagePrimitives.EnumOfValue<byte, DefensiveRole> ctx.Team.OwnFrame.DefensiveRole[ctx.MeIdx])
            ctx.TeamHasBall
            scores

    let computeAll (ctx: AgentContext) (emergent: EmergentState) : MovementScores =
        let raw =
            { MaintainShape = maintainShapeScore ctx
              MarkMan = markManScore ctx + interceptPassScore ctx
              PressBall = pressBallScore ctx
              CoverSpace = coverSpaceScore ctx
              SupportAttack = supportAttackScore ctx
              RecoverBall = recoverBallScore ctx
              MoveToSetPiecePos = maintainShapeScore ctx }

        let gkAdjusted =
            if ctx.Me.Position = GK then
                { raw with
                    PressBall = 0.0
                    MarkMan = 0.0
                    SupportAttack = 0.0
                    RecoverBall = raw.RecoverBall * 0.2 }
            else
                raw

        gkAdjusted |> applyEmergentModifiers emergent ctx |> applyRoleModifiers ctx

    let private scoreForIntent (scores: MovementScores) (intent: MovementIntent) : float =
        match intent with
        | MaintainShape _ -> scores.MaintainShape
        | MarkMan _ -> scores.MarkMan
        | PressBall _ -> scores.PressBall
        | CoverSpace _ -> scores.CoverSpace
        | SupportAttack _ -> scores.SupportAttack
        | RecoverBall _ -> scores.RecoverBall
        | ExecuteRun _ -> scores.SupportAttack * 0.8
        | MoveToSetPiecePos _ -> scores.MoveToSetPiecePos

    let private scoreForIntentKind (scores: MovementScores) (kind: IntentKind) : float =
        match kind with
        | IntentKind.MaintainShape -> scores.MaintainShape
        | IntentKind.MarkMan -> scores.MarkMan
        | IntentKind.PressBall -> scores.PressBall
        | IntentKind.CoverSpace -> scores.CoverSpace
        | IntentKind.SupportAttack -> scores.SupportAttack
        | IntentKind.RecoverBall -> scores.RecoverBall
        | IntentKind.ExecuteRun -> scores.SupportAttack * 0.8
        | IntentKind.MoveToSetPiecePos -> scores.MoveToSetPiecePos
        | IntentKind.TackleAttempt -> scores.PressBall
        | _ -> System.Double.MinValue

    let private previousIntentFromContext (ctx: AgentContext) : MovementIntent =
        let sp =
            { X = float ctx.PreviousIntentTargetX * 1.0<meter>
              Y = float ctx.PreviousIntentTargetY * 1.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        match ctx.PreviousIntentKind with
        | IntentKind.MaintainShape -> MaintainShape sp
        | IntentKind.MarkMan -> MarkMan(ctx.PreviousIntentTargetPid, sp)
        | IntentKind.PressBall -> PressBall sp
        | IntentKind.ExecuteRun -> MaintainShape sp
        | IntentKind.CoverSpace -> CoverSpace sp
        | IntentKind.SupportAttack -> SupportAttack sp
        | IntentKind.RecoverBall -> RecoverBall sp
        | IntentKind.MoveToSetPiecePos -> MoveToSetPiecePos sp
        | IntentKind.TackleAttempt -> PressBall sp
        | _ -> MaintainShape sp

    let private isInPenaltyArea (x: float<meter>) (y: float<meter>) (dir: AttackDir) : bool =
        let penX = PitchLength - PenaltyAreaDepth

        match dir with
        | LeftToRight -> x > penX
        | RightToLeft -> x < PenaltyAreaDepth

    let private pickIntentNormal (currentSubTick: int) (scores: MovementScores) (ctx: AgentContext) : MovementIntent =
        if ctx.Me.Position = GK then
            let distToBall = ctx.MyPos.DistTo2D ctx.BallState.Position

            let ballIsLoose =
                match ctx.BallState.Control with
                | Free
                | Contesting _ -> true
                | _ -> false

            if ballIsLoose && distToBall < 8.0<meter> then
                RecoverBall ctx.BallState.Position
            else
                let frame = ctx.Team.OwnFrame

                let shapeTarget =
                    if ctx.MeIdx < frame.SlotCount then
                        let spX = float frame.SupportPositionX[ctx.MeIdx] * 1.0<meter>
                        let spY = float frame.SupportPositionY[ctx.MeIdx] * 1.0<meter>

                        if spX <> 0.0<meter> || spY <> 0.0<meter> then
                            { X = spX
                              Y = spY
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                        else
                            ctx.MyPos
                    else
                        ctx.MyPos

                MaintainShape shapeTarget
        else
            let frame = ctx.Team.OwnFrame
            let supportPosition =
                if ctx.MeIdx < frame.SlotCount then
                    let spX = float frame.SupportPositionX[ctx.MeIdx] * 1.0<meter>
                    let spY = float frame.SupportPositionY[ctx.MeIdx] * 1.0<meter>

                    if spX <> 0.0<meter> || spY <> 0.0<meter> then
                        ValueSome
                            { X = spX
                              Y = spY
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                    else
                        ValueNone
                else
                    ValueNone

            let shapeTarget =
                match supportPosition with
                | ValueSome pos -> pos
                | ValueNone -> ctx.MyPos

            let mutable bestScore = -1.0
            let mutable bestKind = IntentKind.MaintainShape

            if scores.MaintainShape > 0.05 && scores.MaintainShape > bestScore then
                bestScore <- scores.MaintainShape
                bestKind <- IntentKind.MaintainShape

            if scores.MarkMan > 0.05 && scores.MarkMan > bestScore then
                bestScore <- scores.MarkMan
                bestKind <- IntentKind.MarkMan

            if scores.PressBall > 0.05 && scores.PressBall > bestScore then
                bestScore <- scores.PressBall
                bestKind <- IntentKind.PressBall

            if scores.CoverSpace > 0.05 && scores.CoverSpace > bestScore then
                bestScore <- scores.CoverSpace
                bestKind <- IntentKind.CoverSpace

            if scores.SupportAttack > 0.05 && scores.SupportAttack > bestScore then
                bestScore <- scores.SupportAttack
                bestKind <- IntentKind.SupportAttack

            if scores.RecoverBall > 0.05 && scores.RecoverBall > bestScore then
                bestScore <- scores.RecoverBall
                bestKind <- IntentKind.RecoverBall

            if hasBall ctx.BallState ctx.Me.Id && scores.SupportAttack > 0.3 then
                let runScore = 1.5 + scores.SupportAttack

                if runScore > bestScore then
                    bestScore <- runScore
                    bestKind <- IntentKind.ExecuteRun

            if bestScore < 0.15 then
                if not ctx.TeamHasBall then
                    let distToBall = ctx.MyPos.DistTo2D ctx.BallState.Position

                    if distToBall < 12.0<meter> then
                        bestKind <- IntentKind.PressBall
                    elif distToBall < 25.0<meter> then
                        bestKind <- IntentKind.CoverSpace
                    elif distToBall < 45.0<meter> then
                        bestKind <- IntentKind.MaintainShape
                    else
                        bestKind <- IntentKind.MaintainShape
                else
                    bestKind <- IntentKind.SupportAttack

            let buildSupportTarget () =
                match supportPosition with
                | ValueSome pos -> pos
                | ValueNone ->
                    match ctx.BestPassTargetPos with
                    | ValueSome pos -> pos
                    | ValueNone ->
                        let forwardGoalX =
                            if ctx.Team.AttackDir = LeftToRight then
                                min (ctx.MyPos.X + 10.0<meter>) 100.0<meter>
                            else
                                max (ctx.MyPos.X - 10.0<meter>) 5.0<meter>

                        { X = forwardGoalX
                          Y = ctx.MyPos.Y
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

            let buildBestIntent () =
                match bestKind with
                | IntentKind.MarkMan ->
                    match ctx.NearestOpponentIdx with
                    | ValueSome oppIdx ->
                        let oppX = float ctx.Team.OppFrame.Physics.PosX[oppIdx] * 1.0<meter>
                        let oppY = float ctx.Team.OppFrame.Physics.PosY[oppIdx] * 1.0<meter>
                        let oppPos =
                            { X = oppX
                              Y = oppY
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }

                        MarkMan(ctx.Team.OppRoster.Players[oppIdx].Id, oppPos)
                    | ValueNone -> MaintainShape shapeTarget
                | IntentKind.PressBall -> PressBall ctx.BallState.Position
                | IntentKind.CoverSpace -> CoverSpace shapeTarget
                | IntentKind.SupportAttack -> SupportAttack(buildSupportTarget ())
                | IntentKind.RecoverBall -> RecoverBall ctx.BallState.Position
                | IntentKind.ExecuteRun ->
                    let forwardGoalX =
                        if ctx.Team.AttackDir = LeftToRight then
                            min (ctx.MyPos.X + 15.0<meter>) 100.0<meter>
                        else
                            max (ctx.MyPos.X - 15.0<meter>) 5.0<meter>

                    ExecuteRun(
                        RunAssignment.create
                            RunType.DeepRun
                            ctx.MyPos.X
                            ctx.MyPos.Y
                            forwardGoalX
                            ctx.MyPos.Y
                            ctx.Me.Id
                            (currentSubTick * 1<subtick>)
                            (120 * 1<tickDelta>)
                    )
                | _ -> MaintainShape shapeTarget

            if ctx.PreviousIntentKind = IntentKind.Idle then
                buildBestIntent ()
            else
                let prevScore = scoreForIntentKind scores ctx.PreviousIntentKind
                // Threshold increased to 0.40 and a small persistence bonus (0.05) to current intent
                if bestScore > prevScore + 0.40 then buildBestIntent () else previousIntentFromContext ctx

    let pickIntent (currentSubTick: int) (scores: MovementScores) (ctx: AgentContext) : MovementIntent =
        match ctx.BallState.Control with
        | Airborne ->
            match ctx.BallState.Trajectory with
            | Some traj ->
                let landingPos =
                    { X = traj.TargetX
                      Y = traj.TargetY
                      Z = 0.0<meter>
                      Vx = 0.0<meter / second>
                      Vy = 0.0<meter / second>
                      Vz = 0.0<meter / second> }

                match traj.Intent with
                | Aimed(_, targetId, _, _) ->
                    if ctx.Me.Id = targetId then
                        SupportAttack landingPos
                    elif
                        ctx.Me.Position = GK
                        && isInPenaltyArea landingPos.X landingPos.Y ctx.Team.AttackDir
                    then
                        MaintainShape ctx.MyPos
                    else
                        let midX = (ctx.MyPos.X + landingPos.X) / 2.0
                        let midY = (ctx.MyPos.Y + landingPos.Y) / 2.0

                        RecoverBall
                            { X = midX
                              Y = midY
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                | Struck _ ->
                    if ctx.Me.Position = GK then
                        MaintainShape ctx.MyPos
                    else
                        let midX = (ctx.MyPos.X + landingPos.X) / 2.0
                        let midY = (ctx.MyPos.Y + landingPos.Y) / 2.0

                        RecoverBall
                            { X = midX
                              Y = midY
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                | Cleared _
                | Uncontrolled -> RecoverBall landingPos
            | None -> pickIntentNormal currentSubTick scores ctx
        | _ -> pickIntentNormal currentSubTick scores ctx
