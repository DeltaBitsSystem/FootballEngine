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
        | GoalScored
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
                    | Some t -> t.KickerId = player.Id
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

    let private defIdx (frame: TeamFrame) (idx: int) : int =
        match frame.Physics.Occupancy[idx] with
        | OccupancyKind.Active rosterIdx -> rosterIdx
        | _ -> failwith "GK slot is not active"

    let private findGK (frame: TeamFrame) (roster: PlayerRoster) : int option =
        let mutable gkIdx = None

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active rosterIdx when roster.Players[rosterIdx].Position = GK -> gkIdx <- Some i
            | _ -> ()

        gkIdx

    let private evaluateShotSave
        (gkCfg: GKConfig)
        (defFrame: TeamFrame)
        (defRoster: PlayerRoster)
        (defSide: ClubSide)
        (ballPos: Spatial)
        (shotSpeed: float)
        (shotAngleDeg: float)
        : Contact =
        match findGK defFrame defRoster with
        | None -> NoContact
        | Some gkIdx ->
            let gk = defRoster.Players[defIdx defFrame gkIdx]
            let gkX = float defFrame.Physics.PosX[gkIdx] * 1.0<meter>
            let gkY = float defFrame.Physics.PosY[gkIdx] * 1.0<meter>

            let gkDistToBall =
                let dx = float (ballPos.X - gkX)
                let dy = float (ballPos.Y - gkY)
                sqrt (dx * dx + dy * dy)

            let saveRadius = gkCfg.DiveReach + gkCfg.CollectionRadius

            if gkDistToBall > float saveRadius then
                NoContact
            else
                let reflexNorm = float gk.Mental.Positioning / 20.0
                let positioningNorm = float gk.Mental.Vision / 20.0
                let distFactor = 1.0 - min 1.0 (gkDistToBall / float saveRadius)
                let speedPenalty = gkCfg.SaveSpeedFactor * shotSpeed
                let anglePenalty = gkCfg.SaveAngleFactor * abs shotAngleDeg

                let saveProb =
                    gkCfg.SaveBaseProbability
                    + gkCfg.SaveReflexesWeight * reflexNorm
                    + gkCfg.SavePositioningWeight * positioningNorm
                    + distFactor * 0.2
                    - speedPenalty
                    - anglePenalty

                let clampedProb = max 0.0 (min 1.0 saveProb)

                if Stats.bernoulli clampedProb then
                    IntendedReceiver(gk, defSide)
                else
                    NoContact

    let private evaluateArrival (iw: InterceptionWeights) (ctx: ArrivalContext) : Contact =
        let anticipationBonus =
            ctx.Quality * ctx.PhysicsCfg.ArrivalAnticipationQuality
            + ctx.PhysicsCfg.ArrivalAnticipationBase

        let ballPos = ctx.BallPos
        let ballSpeed = sqrt (ballPos.Vx * ballPos.Vx + ballPos.Vy * ballPos.Vy)
        let competitionRadius = ctx.PhysicsCfg.ArrivalCompetitionRadius
        let convergenceThreshold = ctx.PhysicsCfg.ArrivalConvergenceThreshold
        let contestThreshold = ctx.PhysicsCfg.ArrivalContestThreshold
        let competitors = ResizeArray<Player * ClubSide * float>(8)

        let kickerId =
            match ctx.Intent with
            | Aimed(kickerId, _, _, _) -> Some kickerId
            | Struck(kickerId, _, _, _) -> Some kickerId
            | Cleared(kickerId) -> Some kickerId
            | Uncontrolled -> None

        let checkPlayer (frame: TeamFrame) (roster: PlayerRoster) (side: ClubSide) =
            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active _ ->
                    let player = roster.Players[i]

                    if kickerId |> Option.exists (fun kid -> kid = player.Id) |> not then
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
                                Interception.estimateTimeToBall
                                    ctx.PhysicsCfg
                                    iw
                                    player
                                    pPos
                                    ballPos
                                    frame.Intent.Kind[i]

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
                                        Interception.estimateTimeToBall
                                            ctx.PhysicsCfg
                                            iw
                                            player
                                            pPos
                                            ballPos
                                            frame.Intent.Kind[i]

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
            let mutable bestIdx = 0
            let mutable bestTime = System.Double.MaxValue
            let mutable secondTime = System.Double.MaxValue

            for i = 0 to competitors.Count - 1 do
                let (_, _, t) = competitors[i]

                if t < bestTime then
                    secondTime <- bestTime
                    bestTime <- t
                    bestIdx <- i
                elif t < secondTime then
                    secondTime <- t

            let (bestPlayer, bestSide, _) = competitors[bestIdx]

            if competitors.Count = 1 then
                if bestPlayer.Id = ctx.TargetId then
                    IntendedReceiver(bestPlayer, bestSide)
                else
                    Interceptor(bestPlayer, bestSide)
            else if secondTime - bestTime <= contestThreshold then
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
        (gkCfg: GKConfig)
        (iw: InterceptionWeights)
        (traj: BallTrajectory option)
        (currentSubTick: int)
        (attDir: AttackDir)
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
        else
            match traj with
            | Some t ->
                match t.Intent with
                | Struck(shooterId, quality, shotSpeed, shotAngleDeg) ->
                    let shooterClub =
                        if homeRoster.Players |> Array.exists (fun p -> p.Id = shooterId) then
                            HomeClub
                        else
                            AwayClub

                    let defSide = ClubSide.flip shooterClub
                    let defFrame = if defSide = HomeClub then homeFrame else awayFrame
                    let defRoster = if defSide = HomeClub then homeRoster else awayRoster

                    let gkResult =
                        evaluateShotSave gkCfg defFrame defRoster defSide ballPos shotSpeed shotAngleDeg

                    match gkResult with
                    | IntendedReceiver _ -> gkResult
                    | _ ->
                        if
                            shouldTriggerArrival ball homeFrame awayFrame homeRoster awayRoster cfg traj currentSubTick
                        then
                            let ctx =
                                { BallPos = ballPos
                                  TargetId = t.KickerId
                                  Quality = quality
                                  Intent = t.Intent
                                  HomeFrame = homeFrame
                                  AwayFrame = awayFrame
                                  HomeRoster = homeRoster
                                  AwayRoster = awayRoster
                                  PhysicsCfg = cfg }

                            evaluateArrival iw ctx
                        else
                            NoContact

                | Aimed(passerId, targetId, quality, kind) ->
                    if shouldTriggerArrival ball homeFrame awayFrame homeRoster awayRoster cfg traj currentSubTick then
                        let ctx =
                            { BallPos = ballPos
                              TargetId = targetId
                              Quality = quality
                              Intent = t.Intent
                              HomeFrame = homeFrame
                              AwayFrame = awayFrame
                              HomeRoster = homeRoster
                              AwayRoster = awayRoster
                              PhysicsCfg = cfg }

                        evaluateArrival iw ctx
                    else
                        NoContact

                | Cleared _
                | Uncontrolled ->
                    if shouldTriggerArrival ball homeFrame awayFrame homeRoster awayRoster cfg traj currentSubTick then
                        let ctx =
                            { BallPos = ballPos
                              TargetId = t.KickerId
                              Quality = 0.0
                              Intent = t.Intent
                              HomeFrame = homeFrame
                              AwayFrame = awayFrame
                              HomeRoster = homeRoster
                              AwayRoster = awayRoster
                              PhysicsCfg = cfg }

                        evaluateArrival iw ctx
                    else
                        NoContact

            | None ->
                if shouldTriggerArrival ball homeFrame awayFrame homeRoster awayRoster cfg traj currentSubTick then
                    let ctx =
                        { BallPos = ballPos
                          TargetId = 0
                          Quality = 0.0
                          Intent = Uncontrolled
                          HomeFrame = homeFrame
                          AwayFrame = awayFrame
                          HomeRoster = homeRoster
                          AwayRoster = awayRoster
                          PhysicsCfg = cfg }

                    evaluateArrival iw ctx
                else
                    NoContact
