namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Player.Decision.ActionMath


module Interception =
    let estimateTimeToBall (config: PhysicsConfig) (player: Player) (pPos: Spatial) (ballPos: Spatial) (intentKind: IntentKind) : float =
        let iw = EngineWeightDefaults.defaults.Outcomes.Interception
        let captureRadius =
            config.ContactRadius + (float player.Technical.BallControl / 20.0) * iw.BallControlRadiusMult * 1.0<meter>

        let dist = float (ballPos.DistTo2D pPos)

        if dist <= float captureRadius then
            0.0
        else
            let maxSpeed = float (playerMaxSpeed player.Physical.Pace player.Condition)

            if maxSpeed <= 1e-6 then
                System.Double.PositiveInfinity
            else
                let baseTime = (dist - float captureRadius) / maxSpeed

                let iw = EngineWeightDefaults.defaults.Outcomes.Interception
                let intentFactor =
                    match intentKind with
                    | IntentKind.PressBall
                    | IntentKind.RecoverBall -> iw.PressIntentFactor
                    | IntentKind.MaintainShape -> iw.MaintainShapeIntentFactor
                    | IntentKind.CoverSpace -> iw.CoverSpaceIntentFactor
                    | _ -> 1.0

                baseTime * intentFactor

    let evaluateArrival (ctx: ArrivalContext) : ArrivalWinner =
        let anticipationBonus =
            ctx.Quality * ctx.PhysicsCfg.ArrivalAnticipationQuality
            + ctx.PhysicsCfg.ArrivalAnticipationBase

        let ballPos = ctx.BallPos
        let ballSpeed = sqrt (ballPos.Vx * ballPos.Vx + ballPos.Vy * ballPos.Vy)

        let competitionRadius = ctx.PhysicsCfg.ArrivalCompetitionRadius
        let convergenceThreshold = ctx.PhysicsCfg.ArrivalConvergenceThreshold
        let contestThreshold = ctx.PhysicsCfg.ArrivalContestThreshold

        let competitors = ResizeArray()

        let checkPlayer (frame: TeamFrame) (roster: PlayerRoster) (side: ClubSide) =
            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let player = roster.Players[i]
                    let px = float frame.Physics.PosX[i] * 1.0<meter>
                    let py = float frame.Physics.PosY[i] * 1.0<meter>

                    let pPos =
                        { X = px
                          Y = py
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                    let distToBall = ballPos.DistTo2D pPos

                    if distToBall > competitionRadius then
                        ()
                    elif ballSpeed < 0.5<meter / second> then
                        let intentKind = frame.Intent.Kind[i]
                        let projectedTime = estimateTimeToBall ctx.PhysicsCfg player pPos ballPos intentKind

                        let adjustedTime =
                            if player.Id = ctx.TargetId then
                                projectedTime - anticipationBonus
                            else
                                projectedTime

                        competitors.Add(player, side, adjustedTime)
                    else
                        let toPlayerX = px - ballPos.X
                        let toPlayerY = py - ballPos.Y
                        let toPlayerLen = sqrt (toPlayerX * toPlayerX + toPlayerY * toPlayerY)

                        if toPlayerLen < 0.001<meter> then
                            ()
                        else
                            let toPlayerNormX = toPlayerX / toPlayerLen
                            let toPlayerNormY = toPlayerY / toPlayerLen

                            let convergenceSpeed =
                                float (ballPos.Vx * toPlayerNormX + ballPos.Vy * toPlayerNormY)

                            if convergenceSpeed > float convergenceThreshold then
                                ()
                            else
                                let intentKind = frame.Intent.Kind[i]
                                let projectedTime = estimateTimeToBall ctx.PhysicsCfg player pPos ballPos intentKind

                                let adjustedTime =
                                    if player.Id = ctx.TargetId then
                                        projectedTime - anticipationBonus
                                    else
                                        projectedTime

                                competitors.Add(player, side, adjustedTime)
                | _ -> ()

        checkPlayer ctx.HomeFrame ctx.HomeRoster HomeClub
        checkPlayer ctx.AwayFrame ctx.AwayRoster AwayClub

        if competitors.Count = 0 then
            NoOneInRange
        else
            let sorted = Seq.sortBy (fun (_, _, time) -> time) competitors |> Seq.toList
            let bestPlayer, bestSide, bestTime = sorted.Head

            if sorted.Length = 1 then
                if bestPlayer.Id = ctx.TargetId then
                    IntendedTarget(bestPlayer, bestSide)
                else
                    Intercepted(bestPlayer, bestSide)
            else
                let secondTime = (sorted.Item 1 |> fun (_, _, t) -> t)

                if secondTime - bestTime <= contestThreshold then
                    Contested
                else if bestPlayer.Id = ctx.TargetId then
                    IntendedTarget(bestPlayer, bestSide)
                else
                    Intercepted(bestPlayer, bestSide)

    let chooseBestInterceptorSoA
        (config: PhysicsConfig)
        (ballPos: Spatial)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        : Player option * Spatial option * bool =
        let mutable touchingCount = 0
        let mutable touchingPlayer1 = Unchecked.defaultof<Player>

        let mutable touchingPos1 =
            { X = 0.0<meter>
              Y = 0.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        let mutable touchingPlayer2 = Unchecked.defaultof<Player>

        let mutable touchingPos2 =
            { X = 0.0<meter>
              Y = 0.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        for i = 0 to homeFrame.SlotCount - 1 do
            match homeFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let player = homeRoster.Players[i]

                let captureRadius =
                    config.ContactRadius + (float player.Technical.BallControl / 20.0) * EngineWeightDefaults.defaults.Outcomes.Interception.BallControlRadiusMult * 1.0<meter>

                let px = float homeFrame.Physics.PosX[i] * 1.0<meter>
                let py = float homeFrame.Physics.PosY[i] * 1.0<meter>
                let dx = ballPos.X - px
                let dy = ballPos.Y - py
                let dist = sqrt (dx * dx + dy * dy)

                if dist <= captureRadius then
                    if touchingCount = 0 then
                        touchingPlayer1 <- player

                        touchingPos1 <-
                            { X = px
                              Y = py
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                    elif touchingCount = 1 then
                        touchingPlayer2 <- player

                        touchingPos2 <-
                            { X = px
                              Y = py
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }

                    touchingCount <- touchingCount + 1
            | _ -> ()

        for i = 0 to awayFrame.SlotCount - 1 do
            match awayFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let player = awayRoster.Players[i]

                let captureRadius =
                    config.ContactRadius + (float player.Technical.BallControl / 20.0) * EngineWeightDefaults.defaults.Outcomes.Interception.BallControlRadiusMult * 1.0<meter>

                let px = float awayFrame.Physics.PosX[i] * 1.0<meter>
                let py = float awayFrame.Physics.PosY[i] * 1.0<meter>
                let dx = ballPos.X - px
                let dy = ballPos.Y - py
                let dist = sqrt (dx * dx + dy * dy)

                if dist <= captureRadius then
                    if touchingCount = 0 then
                        touchingPlayer1 <- player

                        touchingPos1 <-
                            { X = px
                              Y = py
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }
                    elif touchingCount = 1 then
                        touchingPlayer2 <- player

                        touchingPos2 <-
                            { X = px
                              Y = py
                              Z = 0.0<meter>
                              Vx = 0.0<meter / second>
                              Vy = 0.0<meter / second>
                              Vz = 0.0<meter / second> }

                    touchingCount <- touchingCount + 1
            | _ -> ()

        match touchingCount with
        | 0 -> None, None, false
        | 1 -> Some touchingPlayer1, Some touchingPos1, false
        | _ -> None, None, true
