namespace FootballEngine.Player.Perception

open System
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.InfluenceTypes


module InfluenceFrame =

    let private accumulateTeamInfluence
        (frame: TeamFrame)
        (roster: PlayerRoster)
        (grid: InfluenceGrid)
        (config: InfluenceConfig)
        (ballX: float32)
        (ballY: float32)
        (vxStrengh: float32)
        : unit =
        let cw = cellWidth config
        let ch = cellHeight config
        let gridCols = config.GridCols
        let gridRows = config.GridRows

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active rosterIdx ->
                let px = frame.Physics.PosX[i]
                let py = frame.Physics.PosY[i]
                let pvx = frame.Physics.VelX[i]
                let pvy = frame.Physics.VelY[i]
                let conditionFactor = float32 frame.Condition[i] / 100.0f
                let player = roster.Players[rosterIdx]

                let sigma =
                    playerSigma config player.Mental.Positioning player.Mental.Concentration player.Mental.Vision

                let sigmaSq = sigma * sigma

                let minCol = max 0 (int ((px - 3.0f * sigma) / cw))
                let maxCol = min (gridCols - 1) (int ((px + 3.0f * sigma) / cw))
                let minRow = max 0 (int ((py - 3.0f * sigma) / ch))
                let maxRow = min (gridRows - 1) (int ((py + 3.0f * sigma) / ch))

                for row = minRow to maxRow do
                    for col = minCol to maxCol do
                        let cell = row * gridCols + col
                        let cx = (float32 col + 0.5f) * cw
                        let cy = (float32 row + 0.5f) * ch
                        let dx = cx - px
                        let dy = cy - py
                        let distSq = dx * dx + dy * dy
                        let toBallX = ballX - cx
                        let toBallY = ballY - cy
                        let vWeight = velocityWeight pvx pvy toBallX toBallY vxStrengh
                        let weight = MathF.Exp(-distSq / (2.0f * sigmaSq)) * conditionFactor * vWeight
                        grid[cell] <- grid[cell] + weight
            | _ -> ()

    let private addBallGravity
        (ballX: float32)
        (ballY: float32)
        (homeGrid: InfluenceGrid)
        (awayGrid: InfluenceGrid)
        (config: InfluenceConfig)
        : unit =
        let sigma = float32 config.BallGravitySigma
        let sigmaSq = sigma * sigma
        let factor = float32 config.BallGravityFactor
        let cw = cellWidth config
        let ch = cellHeight config
        let gridCols = config.GridCols
        let gridRows = config.GridRows

        let minCol = max 0 (int ((ballX - 3.0f * sigma) / cw))
        let maxCol = min (gridCols - 1) (int ((ballX + 3.0f * sigma) / cw))
        let minRow = max 0 (int ((ballY - 3.0f * sigma) / ch))
        let maxRow = min (gridRows - 1) (int ((ballY + 3.0f * sigma) / ch))

        for row = minRow to maxRow do
            for col = minCol to maxCol do
                let cell = row * gridCols + col
                let cx = (float32 col + 0.5f) * cw
                let cy = (float32 row + 0.5f) * ch
                let dx = cx - ballX
                let dy = cy - ballY
                let distSq = dx * dx + dy * dy
                let weight = MathF.Exp(-distSq / (2.0f * sigmaSq)) * factor
                homeGrid[cell] <- homeGrid[cell] + weight
                awayGrid[cell] <- awayGrid[cell] + weight

    let computeInto
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        (target: InfluenceFrame)
        (config: InfluenceConfig)
        (ballX: float32)
        (ballY: float32)
        : unit =
        let homeGrid = target.HomeGrid
        let awayGrid = target.AwayGrid
        let netGrid = target.NetGrid
        let totalGrid = target.TotalGrid
        let size = gridSize config

        Array.Clear(homeGrid, 0, size)
        Array.Clear(awayGrid, 0, size)

        let velStrength = float32 config.VelocityBiasStrength
        accumulateTeamInfluence homeFrame homeRoster homeGrid config ballX ballY velStrength
        accumulateTeamInfluence awayFrame awayRoster awayGrid config ballX ballY velStrength
        addBallGravity ballX ballY homeGrid awayGrid config

        for i = 0 to size - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            netGrid[i] <- h - a
            totalGrid[i] <- h + a
