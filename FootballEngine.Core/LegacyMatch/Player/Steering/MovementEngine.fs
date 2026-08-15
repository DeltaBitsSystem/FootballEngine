namespace FootballEngine.Player.Steering

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player
open FootballEngine.Player.Decision
open FootballEngine.Player.Steering.PlayerSteering
open FootballEngine.Player.Steering.FatiguePipeline
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimulationClock
open SimStateOps


module MovementEngine =

    let private applyReactiveOverrides
        (ctx: MatchContext)
        (state: SimState)
        (clubSide: ClubSide)
        (currentSubTick: int)
        =
        let oppSide = ClubSide.flip clubSide
        let ownFrame = getFrame state clubSide
        let oppFrame = getFrame state oppSide
        let roster = getRoster ctx clubSide

        let ballCarrierOppIdx =
            let cFrame =
                if clubSide = HomeClub then
                    state.HomeCognitiveFrame
                else
                    state.AwayCognitiveFrame

            cFrame.BallCarrierOppIdx

        if ballCarrierOppIdx < 0s then
            ()
        else
            let bcx = oppFrame.Physics.PosX[int ballCarrierOppIdx]
            let bcy = oppFrame.Physics.PosY[int ballCarrierOppIdx]

            for i = 0 to ownFrame.SlotCount - 1 do
                match ownFrame.Physics.Occupancy[i] with
                | OccupancyKind.Sidelined _ -> ()
                | OccupancyKind.Active rosterIdx ->
                    if ownFrame.Intent.Kind[i] <> IntentKind.ExecuteRun then
                        let aggression =
                            if rosterIdx < roster.Players.Length then
                                float roster.Players[rosterIdx].Mental.Aggression / 20.0
                            else
                                0.5

                        match
                            ReactiveLayer.evaluateReactiveIntent
                                i
                                ownFrame
                                oppFrame
                                (int ballCarrierOppIdx)
                                bcx
                                bcy
                                aggression
                        with
                        | TackleAttempt oppSlot ->
                            FrameMutate.setIntent
                                ownFrame.Intent
                                i
                                IntentKind.TackleAttempt
                                oppFrame.Physics.PosX[oppSlot]
                                oppFrame.Physics.PosY[oppSlot]
                                oppSlot
                        | ReactiveIntent.PressBall(tx, ty) ->
                            FrameMutate.setIntent ownFrame.Intent i IntentKind.PressBall tx ty 0
                        | InterceptLane(tx, ty) -> FrameMutate.setIntent ownFrame.Intent i IntentKind.CoverSpace tx ty 0
                        | NoReaction -> ()


    let updatePhysics
        (ctx: MatchContext)
        (state: SimState)
        (clubSide: ClubSide)
        (currentSubTick: int)
        (dt: float<second>)
        =
        let frame = getFrame state clubSide
        let roster = getRoster ctx clubSide
        let pcfg = state.Config.Physics

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Sidelined _ -> ()
            | OccupancyKind.Active rosterIdx ->
                let player = roster.Players[rosterIdx]
                let condition = int frame.Condition[i]

                // INVARIANT: ActiveRuns es la única fuente de verdad para runs en ejecución.
                // El frame lleva solo el IntentKind como señal. La posición viene de aquí.
                let targetX, targetY =
                    match frame.Intent.Kind[i] with
                    | IntentKind.ExecuteRun ->
                        getActiveRuns state clubSide
                        |> List.tryFind (fun r -> r.PlayerId = player.Id && RunAssignment.isActive currentSubTick r)
                        |> Option.map (fun run ->
                            let t = RunAssignment.progress currentSubTick run
                            let tx, ty = RunAssignment.evaluateTrajectory t run.Trajectory
                            float32 tx, float32 ty)
                        |> Option.defaultWith (fun () -> frame.Intent.TargetX[i], frame.Intent.TargetY[i])
                    | _ -> frame.Intent.TargetX[i], frame.Intent.TargetY[i]

                let hasBall =
                    match state.Ball.Control with
                    | Controlled(_, pid)
                    | Receiving(_, pid, _) -> pid = player.Id
                    | _ -> false

                let chasingBall =
                    match frame.Intent.Kind[i] with
                    | IntentKind.PressBall
                    | IntentKind.TackleAttempt
                    | IntentKind.RecoverBall -> true
                    | _ -> false

                let myX = float frame.Physics.PosX[i] * 1.0<meter>
                let myY = float frame.Physics.PosY[i] * 1.0<meter>
                let myVx = float frame.Physics.VelX[i] * 1.0<meter / second>
                let myVy = float frame.Physics.VelY[i] * 1.0<meter / second>

                let current =
                    { X = myX
                      Y = myY
                      Z = 0.0<meter>
                      Vx = myVx
                      Vy = myVy
                      Vz = 0.0<meter / second> }

                let newPos =
                    PlayerPhysics.steerSoA
                        pcfg
                        player
                        condition
                        current
                        i
                        frame.Physics.PosX
                        frame.Physics.PosY
                        frame.SlotCount
                        (targetX, targetY)
                        state.Ball.Position
                        hasBall
                        chasingBall
                        dt

                FrameMutate.setPos frame.Physics i newPos.X newPos.Y
                FrameMutate.setVel frame.Physics i newPos.Vx newPos.Vy

    let refreshCache (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) =
        let frame = getFrame state clubSide
        let roster = getRoster ctx clubSide

        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Sidelined _ -> ()
            | OccupancyKind.Active _ ->
                let player = roster.Players[i]
                let condition = int frame.Condition[i]

                let exec =
                    ActionMath.calcMovementExecution
                        player.Physical.Agility
                        player.Physical.Balance
                        player.Physical.Acceleration
                        condition

                let targetX = frame.Intent.TargetX[i]
                let targetY = frame.Intent.TargetY[i]

                FrameMutate.setCachedTarget frame i (float targetX * 1.0<meter>) (float targetY * 1.0<meter>)
                FrameMutate.setCachedExecution frame i (float exec)

    let updateTeamSide
        (currentSubTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clubSide: ClubSide)
        (dt: float<second>)
        (clock: SimulationClock)
        =
        let smoothing = 0.92

        let clampedX =
            PhysicsContract.clamp state.Ball.Position.X 0.0<meter> PhysicsContract.PitchLength

        state.BallXSmooth <- smoothing * state.BallXSmooth + (1.0 - smoothing) * clampedX

        match state.Flow with
        | RestartDelay _ ->
            // Do not apply reactive overrides during set-piece positioning
            updatePhysics ctx state clubSide currentSubTick dt
            refreshCache ctx state clubSide
        | _ ->
            applyReactiveOverrides ctx state clubSide currentSubTick
            updatePhysics ctx state clubSide currentSubTick dt
            refreshCache ctx state clubSide

// Degradar condición solo en ticks cognitivos (1 vez por segundo)
//if currentSubTick % clock.SubTicksPerSecond = 0 then
//    let frame = getFrame state clubSide
//    let roster = getRoster ctx clubSide
//    let matchMinute = subTicksToSeconds clock currentSubTick / 60.0
//
//    for i = 0 to frame.SlotCount - 1 do
//        match frame.Physics.Occupancy[i] with
//        | OccupancyKind.Active rosterIdx ->
//            let player = roster.Players[rosterIdx]
//            let currentCondition = int frame.Condition[i]
//            let isPressing = false // TODO: check pressing role if available
//            let newCondition = degradeCondition player currentCondition isPressing matchMinute
//            FrameMutate.setCondition frame i newCondition
//
//            if newCondition < ctx.Config.Manager.ConditionThresholdLosing then
//                emitSemantic (PlayerConditionCritical(roster.Players[rosterIdx].Id, newCondition)) state
//        | _ -> ()
//
