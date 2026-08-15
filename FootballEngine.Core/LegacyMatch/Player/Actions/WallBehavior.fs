namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

// ============================================================
// WallBehavior
// Reglas FIFA reales:
//   - La barrera debe estar a mínimo 9.15m del balón
//   - La barrera se forma perpendicular a la línea balón→centro del arco
//   - El número de jugadores en la barrera depende de la distancia al arco
//   - Los jugadores se ubican hombro a hombro (≈0.6m de separación)
//   - El GK nunca entra en la barrera
//   - Los defensores seleccionados son los más cercanos al balón
// ============================================================

type WallPosition =
    { PlayerIds: PlayerId list
      X: float<meter> // centro geométrico de la barrera
      Y: float<meter>
      JumpProbability: float }

module WallBehavior =

    let calculateWallSize (config: SetPieceConfig) (distToGoal: float<meter>) : int =
        let d = float distToGoal

        if d <= float config.WallSizeThreshold1 then 5
        elif d <= float config.WallSizeThreshold2 then 4
        elif d <= float config.WallSizeThreshold3 then 3
        elif d <= float config.WallSizeThreshold4 then 2
        else 1

    let wallCenterPosition
        (config: SetPieceConfig)
        (ballX: float<meter>)
        (ballY: float<meter>)
        (goalX: float<meter>)
        : float<meter> * float<meter> =
        let centerGoalY = PitchWidth / 2.0
        let dx = goalX - ballX
        let dy = centerGoalY - ballY
        let dist = sqrt (dx * dx + dy * dy)

        if dist < 0.1<meter> then
            ballX
            + (if goalX > ballX then
                   config.WallMinDistance
               else
                   -config.WallMinDistance),
            ballY
        else
            let ux = dx / dist
            let uy = dy / dist
            ballX + ux * config.WallMinDistance, ballY + uy * config.WallMinDistance

    let private wallPerpendicular
        (ballX: float<meter>)
        (ballY: float<meter>)
        (wallCX: float<meter>)
        (wallCY: float<meter>)
        : float<meter / meter> * float<meter / meter> =
        let dx = wallCX - ballX
        let dy = wallCY - ballY
        let dist = sqrt (dx * dx + dy * dy)

        if dist < 0.01<meter> then
            0.0<meter / meter>, 1.0<meter / meter>
        else
            -dy / dist, dx / dist

    let wallPlayerPositions
        (config: SetPieceConfig)
        (ballX: float<meter>)
        (ballY: float<meter>)
        (wallCX: float<meter>)
        (wallCY: float<meter>)
        (count: int)
        : Spatial[] =
        if count = 0 then
            Array.empty
        else
            let (px, py) = wallPerpendicular ballX ballY wallCX wallCY

            [| for j = 0 to count - 1 do
                   let offset = (float j - float (count - 1) / 2.0) * float config.WallPlayerSpacing

                   { X = wallCX + px * offset * 1.0<meter>
                     Y = wallCY + py * offset * 1.0<meter>
                     Z = 0.0<meter>
                     Vx = 0.0<meter / second>
                     Vy = 0.0<meter / second>
                     Vz = 0.0<meter / second> } |]

    let selectWallPlayers
        (defRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (ballX: float<meter>)
        (ballY: float<meter>)
        (wallSize: int)
        : PlayerId list =
        [| for i = 0 to defFrame.SlotCount - 1 do
               match defFrame.Physics.Occupancy[i] with
               | OccupancyKind.Active _ ->
                   let p = defRoster.Players[i]

                   if p.Position <> GK then
                       let px = float defFrame.Physics.PosX[i] * 1.0<meter>
                       let py = float defFrame.Physics.PosY[i] * 1.0<meter>
                       let dx = px - ballX
                       let dy = py - ballY
                       yield p.Id, sqrt (dx * dx + dy * dy)
               | _ -> () |]
        |> Array.sortBy snd
        |> Array.truncate wallSize
        |> Array.map fst
        |> Array.toList

    let calculateJumpProbability (config: SetPieceConfig) (player: Player) : float =
        let bravery = float player.Mental.Bravery / 20.0
        let jumping = float player.Physical.JumpingReach / 20.0
        config.WallBraveryWeight * bravery + config.WallJumpingWeight * jumping

    let shouldWallJump (config: SetPieceConfig) (player: Player) : bool =
        Stats.bernoulli (calculateJumpProbability config player * config.WallJumpProbabilityBase)

    let buildWall
        (config: SetPieceConfig)
        (defRoster: PlayerRoster)
        (defFrame: TeamFrame)
        (ballX: float<meter>)
        (ballY: float<meter>)
        (goalX: float<meter>)
        : WallPosition * Spatial[] =
        let distToGoal = abs (goalX - ballX)
        let wallSize = calculateWallSize config distToGoal
        let (wCX, wCY) = wallCenterPosition config ballX ballY goalX
        let playerIds = selectWallPlayers defRoster defFrame ballX ballY wallSize
        let spatials = wallPlayerPositions config ballX ballY wCX wCY playerIds.Length

        let avgJump =
            playerIds
            |> List.choose (fun pid -> defRoster.Players |> Array.tryFind (fun p -> p.Id = pid))
            |> List.map (calculateJumpProbability config)
            |> (fun ps -> if ps.IsEmpty then 0.3 else List.average ps)

        { PlayerIds = playerIds
          X = wCX
          Y = wCY
          JumpProbability = avgJump },
        spatials
