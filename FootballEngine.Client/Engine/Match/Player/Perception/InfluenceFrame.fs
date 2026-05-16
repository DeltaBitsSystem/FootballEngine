namespace FootballEngine.Player.Perception

open System
open FootballEngine.Types
open FootballEngine.Types.InfluenceTypes


// ============================================================
// Influence Frame computation — builds spatial influence grids
// from TeamFrame data. Runs at cognitive tick frequency (~2Hz).
// ============================================================

module InfluenceFrame =

    // Sigma for Gaussian kernel: ~15m radius of influence per player
    [<Literal>]
    let private PlayerSigma = 15.0f

    // Pre-compute cell centers to avoid recomputing every frame
    let private cellCenters: (float32 * float32)[] =
        Array.init GridSize (fun i -> cellToCenter i)

    /// Build influence grids from team frames.
    /// homeFrame/awayFrame = current positions, homeRoster/awayRoster = player attributes.
    let compute (homeFrame: TeamFrame) (awayFrame: TeamFrame) : InfluenceFrame =
        let result = emptyInfluenceFrame ()
        let homeGrid = result.HomeGrid
        let awayGrid = result.AwayGrid
        let sigma = PlayerSigma
        let sigmaSq = sigma * sigma

        // Accumulate home influence
        for i = 0 to homeFrame.SlotCount - 1 do
            match homeFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let px = homeFrame.Physics.PosX[i]
                let py = homeFrame.Physics.PosY[i]
                let conditionFactor = float32 homeFrame.Condition[i] / 100.0f
                // Only iterate cells within ~3 sigma (45m) for performance
                let minCol = max 0 (int ((px - 3.0f * sigma) / CellWidth))
                let maxCol = min (GridCols - 1) (int ((px + 3.0f * sigma) / CellWidth))
                let minRow = max 0 (int ((py - 3.0f * sigma) / CellHeight))
                let maxRow = min (GridRows - 1) (int ((py + 3.0f * sigma) / CellHeight))

                for row = minRow to maxRow do
                    for col = minCol to maxCol do
                        let cell = row * GridCols + col
                        let cx, cy = cellCenters[cell]
                        let dx = cx - px
                        let dy = cy - py
                        let distSq = dx * dx + dy * dy
                        let weight = System.MathF.Exp(-distSq / (2.0f * sigmaSq)) * conditionFactor
                        homeGrid[cell] <- homeGrid[cell] + weight
            | _ -> ()

        // Accumulate away influence (same logic)
        for i = 0 to awayFrame.SlotCount - 1 do
            match awayFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let px = awayFrame.Physics.PosX[i]
                let py = awayFrame.Physics.PosY[i]
                let conditionFactor = float32 awayFrame.Condition[i] / 100.0f
                let minCol = max 0 (int ((px - 3.0f * sigma) / CellWidth))
                let maxCol = min (GridCols - 1) (int ((px + 3.0f * sigma) / CellWidth))
                let minRow = max 0 (int ((py - 3.0f * sigma) / CellHeight))
                let maxRow = min (GridRows - 1) (int ((py + 3.0f * sigma) / CellHeight))

                for row = minRow to maxRow do
                    for col = minCol to maxCol do
                        let cell = row * GridCols + col
                        let cx, cy = cellCenters[cell]
                        let dx = cx - px
                        let dy = cy - py
                        let distSq = dx * dx + dy * dy
                        let weight = System.MathF.Exp(-distSq / (2.0f * sigmaSq)) * conditionFactor
                        awayGrid[cell] <- awayGrid[cell] + weight
            | _ -> ()

        // Compute contested grid and control
        let contested = result.ContestedGrid
        let control = result.ControlGrid

        for i = 0 to GridSize - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            contested[i] <- h - a

            if h > a + 0.3f then control[i] <- 1uy
            elif a > h + 0.3f then control[i] <- 2uy
            else control[i] <- 0uy

        // Attacker pass safety: for each cell, how much home influence minus away influence
        // Normalized to 0.0-1.0 range
        let passSafety = result.AttackerPassSafety

        for i = 0 to GridSize - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            let total = h + a

            if total < 0.01f then
                passSafety[i] <- 0.5f
            else
                // Positive = home advantage (safe), negative = away advantage (dangerous)
                passSafety[i] <- 0.5f + 0.5f * (h - a) / total

        // Defender coverage: for each cell, how well is it covered by defenders?
        // From defender's perspective: high value = well covered
        let coverage = result.DefenderCoverage

        for i = 0 to GridSize - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            let total = h + a

            if total < 0.01f then
                coverage[i] <- 0.0f
            else
                // For home team defending: how much away influence (attackers) is in this cell?
                // High away influence = poorly covered from home perspective
                coverage[i] <- 1.0f - (a / total)

        result

    /// Same algorithm as compute but writes into a pre-existing target InfluenceFrame
    /// to avoid allocating new arrays every tick.
    let computeInto (homeFrame: TeamFrame) (awayFrame: TeamFrame) (target: InfluenceFrame) : unit =
        let homeGrid = target.HomeGrid
        let awayGrid = target.AwayGrid
        let sigma = PlayerSigma
        let sigmaSq = sigma * sigma

        Array.Clear(homeGrid, 0, homeGrid.Length)
        Array.Clear(awayGrid, 0, awayGrid.Length)

        // Accumulate home influence
        for i = 0 to homeFrame.SlotCount - 1 do
            match homeFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let px = homeFrame.Physics.PosX[i]
                let py = homeFrame.Physics.PosY[i]
                let conditionFactor = float32 homeFrame.Condition[i] / 100.0f
                let minCol = max 0 (int ((px - 3.0f * sigma) / CellWidth))
                let maxCol = min (GridCols - 1) (int ((px + 3.0f * sigma) / CellWidth))
                let minRow = max 0 (int ((py - 3.0f * sigma) / CellHeight))
                let maxRow = min (GridRows - 1) (int ((py + 3.0f * sigma) / CellHeight))

                for row = minRow to maxRow do
                    for col = minCol to maxCol do
                        let cell = row * GridCols + col
                        let cx, cy = cellCenters[cell]
                        let dx = cx - px
                        let dy = cy - py
                        let distSq = dx * dx + dy * dy
                        let weight = System.MathF.Exp(-distSq / (2.0f * sigmaSq)) * conditionFactor
                        homeGrid[cell] <- homeGrid[cell] + weight
            | _ -> ()

        // Accumulate away influence (same logic)
        for i = 0 to awayFrame.SlotCount - 1 do
            match awayFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let px = awayFrame.Physics.PosX[i]
                let py = awayFrame.Physics.PosY[i]
                let conditionFactor = float32 awayFrame.Condition[i] / 100.0f
                let minCol = max 0 (int ((px - 3.0f * sigma) / CellWidth))
                let maxCol = min (GridCols - 1) (int ((px + 3.0f * sigma) / CellWidth))
                let minRow = max 0 (int ((py - 3.0f * sigma) / CellHeight))
                let maxRow = min (GridRows - 1) (int ((py + 3.0f * sigma) / CellHeight))

                for row = minRow to maxRow do
                    for col = minCol to maxCol do
                        let cell = row * GridCols + col
                        let cx, cy = cellCenters[cell]
                        let dx = cx - px
                        let dy = cy - py
                        let distSq = dx * dx + dy * dy
                        let weight = System.MathF.Exp(-distSq / (2.0f * sigmaSq)) * conditionFactor
                        awayGrid[cell] <- awayGrid[cell] + weight
            | _ -> ()

        // Compute contested grid and control
        let contested = target.ContestedGrid
        let control = target.ControlGrid

        for i = 0 to GridSize - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            contested[i] <- h - a

            if h > a + 0.3f then control[i] <- 1uy
            elif a > h + 0.3f then control[i] <- 2uy
            else control[i] <- 0uy

        // Attacker pass safety
        let passSafety = target.AttackerPassSafety

        for i = 0 to GridSize - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            let total = h + a

            if total < 0.01f then
                passSafety[i] <- 0.5f
            else
                passSafety[i] <- 0.5f + 0.5f * (h - a) / total

        // Defender coverage
        let coverage = target.DefenderCoverage

        for i = 0 to GridSize - 1 do
            let h = homeGrid[i]
            let a = awayGrid[i]
            let total = h + a

            if total < 0.01f then
                coverage[i] <- 0.0f
            else
                coverage[i] <- 1.0f - (a / total)
