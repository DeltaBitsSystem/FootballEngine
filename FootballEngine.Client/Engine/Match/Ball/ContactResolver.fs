namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

module ContactResolver =

    type BoundarySide =
        | Left
        | Right
        | Top
        | Bottom

    type Contact =
        | IntendedReceiver of Player * ClubSide
        | Interceptor of Player * ClubSide
        | Contested of ClubSide
        | OutOfBounds of BoundarySide
        | NoContact

    let private isBallArrivingToPlayer
        (ballPos: Spatial)
        (playerPos: Spatial)
        (competitionRadius: float<meter>)
        (convergenceThreshold: float<meter / second>)
        : bool =
        let dist = ballPos.DistTo2D playerPos

        if dist > competitionRadius then
            false
        else
            let toPlayerX = playerPos.X - ballPos.X
            let toPlayerY = playerPos.Y - ballPos.Y
            let toPlayerLen = sqrt (toPlayerX * toPlayerX + toPlayerY * toPlayerY)

            if toPlayerLen < 0.001<meter> then
                true
            else
                let ballSpeed = sqrt (ballPos.Vx * ballPos.Vx + ballPos.Vy * ballPos.Vy)

                if ballSpeed < 0.5<meter / second> then
                    true
                else
                    let normX = toPlayerX / toPlayerLen
                    let normY = toPlayerY / toPlayerLen
                    let convergence = float (ballPos.Vx * normX + ballPos.Vy * normY)
                    convergence < float convergenceThreshold

    let private checkFrame
        (frame: TeamFrame)
        (roster: PlayerRoster)
        (ballPos: Spatial)
        (compRadius: float<meter>)
        (convThreshold: float<meter / second>)
        (traj: BallTrajectory option)
        (currentSubTick: int)
        =
        let mutable result = false

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                let player = roster.Players[i]

                let isKicker =
                    match traj with
                    | Some t -> t.KickerId = player.Id && (currentSubTick - t.LaunchSubTick < 10)
                    | None -> false

                if not isKicker then
                    let px = float frame.Physics.PosX[i] * 1.0<meter>
                    let py = float frame.Physics.PosY[i] * 1.0<meter>

                    let pPos =
                        { X = px
                          Y = py
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                    if isBallArrivingToPlayer ballPos pPos compRadius convThreshold then
                        result <- true
            | _ -> ()

        result

    let shouldTriggerArrival
        (ball: BallPhysicsState)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        (cfg: PhysicsConfig)
        (traj: BallTrajectory option)
        (currentSubTick: int)
        : bool =
        let ballPos = ball.Position
        let compRadius = cfg.ArrivalCompetitionRadius
        let convThreshold = cfg.ArrivalConvergenceThreshold

        checkFrame homeFrame homeRoster ballPos compRadius convThreshold traj currentSubTick
        || checkFrame awayFrame awayRoster ballPos compRadius convThreshold traj currentSubTick

    let private evaluateArrival (ctx: ArrivalContext) : Contact =
        let anticipationBonus =
            ctx.Quality * ctx.PhysicsCfg.ArrivalAnticipationQuality
            + ctx.PhysicsCfg.ArrivalAnticipationBase

        let ballPos = ctx.BallPos
        let ballSpeed = sqrt (ballPos.Vx * ballPos.Vx + ballPos.Vy * ballPos.Vy)
        let competitionRadius = ctx.PhysicsCfg.ArrivalCompetitionRadius
        let convergenceThreshold = ctx.PhysicsCfg.ArrivalConvergenceThreshold
        let contestThreshold = ctx.PhysicsCfg.ArrivalContestThreshold
        let competitors = ResizeArray<Player * ClubSide * float>()

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
                        let projectedTime =
                            Interception.estimateTimeToBall ctx.PhysicsCfg player pPos ballPos

                        let adjustedTime =
                            if player.Id = ctx.TargetId then
                                projectedTime - anticipationBonus
                            else
                                projectedTime

                        competitors.Add((player, side, adjustedTime))
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
                                let projectedTime =
                                    Interception.estimateTimeToBall ctx.PhysicsCfg player pPos ballPos

                                let adjustedTime =
                                    if player.Id = ctx.TargetId then
                                        projectedTime - anticipationBonus
                                    else
                                        projectedTime

                                competitors.Add((player, side, adjustedTime))
                | _ -> ()

        checkPlayer ctx.HomeFrame ctx.HomeRoster HomeClub
        checkPlayer ctx.AwayFrame ctx.AwayRoster AwayClub

        if competitors.Count = 0 then
            NoContact
        else
            let sorted = Seq.sortBy (fun (_, _, time) -> time) competitors |> Seq.toList
            let (bestPlayer, bestSide, bestTime) = sorted.Head

            if sorted.Length = 1 then
                if bestPlayer.Id = ctx.TargetId then
                    IntendedReceiver(bestPlayer, bestSide)
                else
                    Interceptor(bestPlayer, bestSide)
            else
                let (_, _, secondTime) = sorted.[1]

                if secondTime - bestTime <= contestThreshold then
                    Contested bestSide
                else if bestPlayer.Id = ctx.TargetId then
                    IntendedReceiver(bestPlayer, bestSide)
                else
                    Interceptor(bestPlayer, bestSide)

    let find
        (ball: BallPhysicsState)
        (homeFrame: TeamFrame)
        (awayFrame: TeamFrame)
        (homeRoster: PlayerRoster)
        (awayRoster: PlayerRoster)
        (cfg: PhysicsConfig)
        (traj: BallTrajectory option)
        (currentSubTick: int)
        : Contact =
        let ballPos = ball.Position

        if ballPos.Y <= 0.1<meter> then
            OutOfBounds Bottom
        elif ballPos.Y >= PitchWidth - 0.1<meter> then
            OutOfBounds Top
        elif ballPos.X <= 0.1<meter> then
            OutOfBounds Left
        elif ballPos.X >= PitchLength - 0.1<meter> then
            OutOfBounds Right
        else if shouldTriggerArrival ball homeFrame awayFrame homeRoster awayRoster cfg traj currentSubTick then
            match traj with
            | Some t ->
                let targetId =
                    match t.Intent with
                    | Aimed(_, targetId, _, _) -> targetId
                    | _ -> t.KickerId

                let ctx =
                    { BallPos = ballPos
                      TargetId = targetId
                      Quality = 0.0
                      HomeFrame = homeFrame
                      AwayFrame = awayFrame
                      HomeRoster = homeRoster
                      AwayRoster = awayRoster
                      PhysicsCfg = cfg }

                evaluateArrival ctx
            | None ->

                let ctx =
                    { BallPos = ballPos
                      TargetId = 0
                      Quality = 0.0
                      HomeFrame = homeFrame
                      AwayFrame = awayFrame
                      HomeRoster = homeRoster
                      AwayRoster = awayRoster
                      PhysicsCfg = cfg }

                evaluateArrival ctx
        else
            NoContact
