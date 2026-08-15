namespace FootballEngine.Player.Decision

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.InfluenceTypes
open FootballEngine.Types.PhysicsContract


module SpaceScorer =

    [<Struct>]
    type SpaceTarget =
        { Cell: int
          Score: float
          Position: Spatial }

    [<Literal>]
    let private MinTeammateConvergence = 0.15

    let private normStat (v: int) = float v / 20.0
    let private pitchLengthM = float PitchLength

    let private cellCenterPos (config: InfluenceConfig) (cell: int) : Spatial =
        let cx, cy = cellToCenter config cell

        { X = float cx * 1.0<meter>
          Y = float cy * 1.0<meter>
          Z = 0.0<meter>
          Vx = 0.0<meter / second>
          Vy = 0.0<meter / second>
          Vz = 0.0<meter / second> }

    let scoreCell
        (config: InfluenceConfig)
        (cell: int)
        (influence: InfluenceFrame)
        (myPos: Spatial)
        (attackDir: AttackDir)
        : float =
        scoreCellRaw config cell influence (float32 myPos.X) (float32 myPos.Y) attackDir

    let private computeTeammateConvergence (ctx: AgentContext) (targetCell: int) : float =
        let config = ctx.Weights.Influence
        let targetPos = cellCenterPos config targetCell
        let mutable totalConvergence = 0.0
        let mutable count = 0

        for i = 0 to ctx.Team.OwnFrame.SlotCount - 1 do
            if i <> ctx.MeIdx then
                match ctx.Team.OwnFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let px = float ctx.Team.OwnFrame.Physics.PosX[i] * 1.0<meter>
                    let py = float ctx.Team.OwnFrame.Physics.PosY[i] * 1.0<meter>
                    let vx = float ctx.Team.OwnFrame.Physics.VelX[i] * 1.0<meter / second>
                    let vy = float ctx.Team.OwnFrame.Physics.VelY[i] * 1.0<meter / second>

                    let playerPos =
                        { X = px
                          Y = py
                          Z = 0.0<meter>
                          Vx = vx
                          Vy = vy
                          Vz = 0.0<meter / second> }

                    let dist = playerPos.DistTo2D targetPos
                    let proximityScore = max 0.0 (1.0 - float dist / 40.0)

                    let velAlign =
                        let toTargetX = float (targetPos.X - playerPos.X)
                        let toTargetY = float (targetPos.Y - playerPos.Y)
                        let toTargetDist = sqrt (toTargetX * toTargetX + toTargetY * toTargetY)

                        if toTargetDist < 0.01 then
                            0.0
                        else
                            let dot = vx * toTargetX / toTargetDist + vy * toTargetY / toTargetDist
                            max 0.0 (float dot / 8.0)

                    let convergence = proximityScore * 0.4 + velAlign * 0.6
                    totalConvergence <- totalConvergence + convergence
                    count <- count + 1
                | _ -> ()

        if count = 0 then 0.0 else totalConvergence / float count

    let private hasClearLane
        (ballX: float32)
        (ballY: float32)
        (targetCell: int)
        (influence: InfluenceFrame)
        (config: InfluenceConfig)
        : bool =
        let tx, ty = cellToCenter config targetCell
        let steps = 8
        let mutable blockedCells = 0

        for s = 1 to steps - 1 do
            let t = float32 s / float32 steps
            let px = ballX + (tx - ballX) * t
            let py = ballY + (ty - ballY) * t
            let cell = posToCell config px py

            if influence.AwayGrid[cell] > 0.5f then
                blockedCells <- blockedCells + 1

        blockedCells <= 2

    let findThroughBallTarget (ctx: AgentContext) : SpaceTarget voption =
        let config = ctx.Weights.Influence
        let influence = ctx.Influence
        let attackDir = ctx.Team.AttackDir
        let size = gridSize config
        let ballX = float32 ctx.BallState.Position.X
        let ballY = float32 ctx.BallState.Position.Y

        let mutable deepestDefX =
            match attackDir with
            | LeftToRight -> 0.0f
            | RightToLeft -> float32 PitchLength

        for i = 0 to ctx.Team.OppFrame.SlotCount - 1 do
            match ctx.Team.OppFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let dx = ctx.Team.OppFrame.Physics.PosX[i]

                let isDeeper =
                    match attackDir with
                    | LeftToRight -> dx > deepestDefX
                    | RightToLeft -> dx < deepestDefX

                if isDeeper then
                    deepestDefX <- dx
            | _ -> ()

        let throughBallLine =
            match attackDir with
            | LeftToRight -> deepestDefX + 5.0f
            | RightToLeft -> deepestDefX - 5.0f

        let mutable bestTarget: SpaceTarget voption = ValueNone
        let mutable bestScore = 0.0

        for cell = 0 to size - 1 do
            let cx, _ = cellToCenter config cell

            let isBehindLine =
                match attackDir with
                | LeftToRight -> cx > throughBallLine
                | RightToLeft -> cx < throughBallLine

            if isBehindLine && hasClearLane ballX ballY cell influence config then
                let score = scoreCell config cell influence ctx.MyPos attackDir

                let depthBonus =
                    let depth =
                        match attackDir with
                        | LeftToRight -> float cx / pitchLengthM
                        | RightToLeft -> 1.0 - float cx / pitchLengthM

                    if depth > 0.7 then 0.2 else 0.0

                let adjustedScore = score + depthBonus

                if adjustedScore > bestScore then
                    bestScore <- adjustedScore

                    bestTarget <-
                        ValueSome
                            { Cell = cell
                              Score = adjustedScore
                              Position = cellCenterPos config cell }

        bestTarget

    let findBestSpaceTarget (ctx: AgentContext) : SpaceTarget voption =
        let config = ctx.Weights.Influence
        let influence = ctx.Influence
        let attackDir = ctx.Team.AttackDir
        let size = gridSize config

        let mutable bestTarget: SpaceTarget voption = ValueNone
        let mutable bestScore = 0.0

        for cell = 0 to size - 1 do
            let score = scoreCell config cell influence ctx.MyPos attackDir

            if score > bestScore then
                bestScore <- score

                bestTarget <-
                    ValueSome
                        { Cell = cell
                          Score = score
                          Position = cellCenterPos config cell }

        bestTarget

    let spacePassScore (ctx: AgentContext) (targetCell: int) : float<decisionScore> =
        let config = ctx.Weights.Influence
        let influence = ctx.Influence
        let attackDir = ctx.Team.AttackDir

        let spaceScore = scoreCell config targetCell influence ctx.MyPos attackDir
        let convergence = computeTeammateConvergence ctx targetCell

        let convergenceFactor =
            if convergence < MinTeammateConvergence then 0.3
            elif convergence < 0.4 then 0.6
            else 0.9

        let combined = spaceScore * convergenceFactor

        combined |> LanguagePrimitives.FloatWithMeasure<decisionScore>
