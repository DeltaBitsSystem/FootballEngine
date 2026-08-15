namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract


// ============================================================
// Influence Map types — spatial discretization for AI awareness
// ============================================================

module InfluenceTypes =

    type InfluenceValue = float32

    type InfluenceGrid = InfluenceValue[]

    type InfluenceFrame =
        { mutable HomeGrid: InfluenceGrid
          mutable AwayGrid: InfluenceGrid
          mutable NetGrid: InfluenceGrid
          mutable TotalGrid: InfluenceGrid }

    let emptyInfluenceFrame (config: InfluenceConfig) : InfluenceFrame =
        let size = config.GridCols * config.GridRows

        { HomeGrid = Array.zeroCreate size
          AwayGrid = Array.zeroCreate size
          NetGrid = Array.zeroCreate size
          TotalGrid = Array.zeroCreate size }

    let gridSize (config: InfluenceConfig) = config.GridCols * config.GridRows

    let cellWidth (config: InfluenceConfig) =
        float32 PitchLength / float32 config.GridCols

    let cellHeight (config: InfluenceConfig) =
        float32 PitchWidth / float32 config.GridRows

    let posToCell (config: InfluenceConfig) (x: float32) (y: float32) : int =
        let cw = cellWidth config
        let ch = cellHeight config
        let col = max 0 (min (config.GridCols - 1) (int (x / cw)))
        let row = max 0 (min (config.GridRows - 1) (int (y / ch)))
        row * config.GridCols + col

    let cellToCenter (config: InfluenceConfig) (cell: int) : float32 * float32 =
        let cw = cellWidth config
        let ch = cellHeight config
        let row = cell / config.GridCols
        let col = cell % config.GridCols
        let cx = (float32 col + 0.5f) * cw
        let cy = (float32 row + 0.5f) * ch
        cx, cy

    let neighborsInto (config: InfluenceConfig) (cell: int) (buffer: int[]) : int =
        let row = cell / config.GridCols
        let col = cell % config.GridCols
        let mutable count = 0

        for dr = -1 to 1 do
            for dc = -1 to 1 do
                if dr <> 0 || dc <> 0 then
                    let nr = row + dr
                    let nc = col + dc

                    if nr >= 0 && nr < config.GridRows && nc >= 0 && nc < config.GridCols then
                        buffer[count] <- nr * config.GridCols + nc
                        count <- count + 1

        count

    let neighbors (config: InfluenceConfig) (cell: int) : int[] =
        let buffer = Array.zeroCreate<int> 8
        let count = neighborsInto config cell buffer
        if count = 8 then buffer else buffer.[.. count - 1]

    let gaussianWeight
        (cellCenterX: float32)
        (cellCenterY: float32)
        (playerX: float32)
        (playerY: float32)
        (sigma: float32)
        : float32 =
        let dx = cellCenterX - playerX
        let dy = cellCenterY - playerY
        let distSq = dx * dx + dy * dy
        let sigmaSq = sigma * sigma
        System.MathF.Exp(-distSq / (2.0f * sigmaSq))

    let velocityWeight (vx: float32) (vy: float32) (toCellX: float32) (toCellY: float32) (strength: float32) : float32 =
        let speed = sqrt (vx * vx + vy * vy)

        if speed < 0.5f then
            1.0f
        else
            let dirX = vx / speed
            let dirY = vy / speed
            let cellLen = sqrt (toCellX * toCellX + toCellY * toCellY)

            if cellLen < 0.01f then
                1.0f
            else
                let alignment = (dirX * toCellX + dirY * toCellY) / cellLen
                1.0f + alignment * strength

    let playerSigma (config: InfluenceConfig) (positioning: int) (anticipation: int) (vision: int) : float32 =
        let sigmaBase = float32 config.SigmaBase

        let attributeBonus =
            0.5f * (float32 positioning / 20.0f)
            + 0.3f * (float32 anticipation / 20.0f)
            + 0.2f * (float32 vision / 20.0f)

        sigmaBase + attributeBonus * float32 config.SigmaAttributeScale

    let cellDefensiveStrength (cell: int) (clubSide: ClubSide) (influence: InfluenceFrame) : float =
        let our =
            if clubSide = HomeClub then
                influence.HomeGrid[cell]
            else
                influence.AwayGrid[cell]

        let opp =
            if clubSide = HomeClub then
                influence.AwayGrid[cell]
            else
                influence.HomeGrid[cell]

        let total = our + opp
        if total < 0.01f then 0.0 else float our / float total

    let cellDangerScore (cell: int) (attackingDir: AttackDir) (influence: InfluenceFrame) : float =
        let attackingSide = if attackingDir = LeftToRight then HomeClub else AwayClub
        let oppCoverage = cellDefensiveStrength cell (ClubSide.flip attackingSide) influence
        let openness = 1.0 - oppCoverage

        let net =
            if attackingDir = LeftToRight then
                influence.NetGrid[cell]
            else
                -influence.NetGrid[cell]

        let advantage = max 0.0 (float net) * 0.3
        openness + advantage

    let scoreCellRaw
        (config: InfluenceConfig)
        (cell: int)
        (influence: InfluenceFrame)
        (myX: float32)
        (myY: float32)
        (attackDir: AttackDir)
        : float =
        let net = influence.NetGrid[cell]
        let total = influence.TotalGrid[cell]

        let safetyScore =
            if total < 0.01f then
                0.5
            else
                float net / float total * 0.5 + 0.5

        let opennessScore = cellDangerScore cell attackDir influence

        let cx, cy = cellToCenter config cell

        let forwardness =
            match attackDir with
            | LeftToRight -> float cx / float PitchLength
            | RightToLeft -> 1.0 - float cx / float PitchLength

        let forwardBonus = forwardness * 0.20

        let dx = float (cx - myX)
        let dy = float (cy - myY)
        let dist = sqrt (dx * dx + dy * dy)

        let distScore =
            if dist < 3.0 then 0.3
            elif dist < 15.0 then 0.8
            elif dist < 30.0 then 1.0
            elif dist < 50.0 then 0.5
            else 0.1

        safetyScore * 0.30
        + opennessScore * 0.25
        + (max 0.0 (float net) * 0.3) * 0.15
        + forwardBonus
        + distScore * 0.10

    let ensureSize (frame: InfluenceFrame) (config: InfluenceConfig) =
        let size = gridSize config

        if frame.HomeGrid.Length <> size then
            frame.HomeGrid <- Array.zeroCreate size
            frame.AwayGrid <- Array.zeroCreate size
            frame.NetGrid <- Array.zeroCreate size
            frame.TotalGrid <- Array.zeroCreate size
