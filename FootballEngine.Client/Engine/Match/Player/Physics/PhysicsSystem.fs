namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Player.Steering
open FootballEngine.Player.Decision
open FootballEngine.Player

module PhysicsSystem =

    let run (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : unit =

        // BallXSmooth — exponential moving average de posición X del balón
        let smoothing = 0.92
        let clampedX = clamp state.Ball.Position.X 0.0<meter> PitchLength
        state.BallXSmooth <- smoothing * state.BallXSmooth + (1.0 - smoothing) * clampedX

        for clubSide in [| HomeClub; AwayClub |] do
            let roster = SimStateOps.getRoster ctx clubSide
            let pcfg = state.Config.Physics
            let dt = SimulationClock.dtPlayer clock
            let frame = SimStateOps.getFrame state clubSide
            let oppFrame = SimStateOps.getFrame state (ClubSide.flip clubSide)

            // CognitiveFrame para reactive overrides
            let cFrame =
                if clubSide = HomeClub then
                    state.HomeCognitiveFrame
                else
                    state.AwayCognitiveFrame

            let bcIdx = int cFrame.BallCarrierOppIdx

            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Sidelined _ -> ()
                | OccupancyKind.Active rosterIdx ->
                    let player = roster.Players[rosterIdx]
                    let condition = frame.Condition[i]

                    // Reactive override si corresponde
                    let reactiveWrite =
                        if bcIdx >= 0 && frame.Intent.Kind[i] <> IntentKind.ExecuteRun then
                            let aggression = float player.Mental.Aggression / 20.0
                            let bcx = oppFrame.Physics.PosX[bcIdx]
                            let bcy = oppFrame.Physics.PosY[bcIdx]

                            match ReactiveLayer.evaluateReactiveIntent i frame oppFrame bcIdx bcx bcy aggression with
                            | TackleAttempt oppSlotIdx ->
                                let ttx = float32 oppFrame.Physics.PosX[oppSlotIdx]
                                let tty = float32 oppFrame.Physics.PosY[oppSlotIdx]
                                Some(IntentKind.TackleAttempt, ttx, tty, oppSlotIdx)
                            | ReactiveIntent.PressBall(tx, ty) -> Some(IntentKind.PressBall, tx, ty, 0)
                            | InterceptLane(tx, ty) -> Some(IntentKind.CoverSpace, tx, ty, 0)
                            | NoReaction -> None
                        else
                            None

                    match reactiveWrite with
                    | Some(kind, tx, ty, pid) ->
                        FrameMutate.setIntent frame.Intent i kind tx ty pid
                    | None -> ()

                    // Steering — física pura
                    let targetX, targetY =
                        match frame.Intent.Kind[i] with
                        | IntentKind.ExecuteRun ->
                            SimStateOps.getActiveRuns state clubSide
                            |> List.tryFind (fun r -> r.PlayerId = player.Id && RunAssignment.isActive (subTick * 1<subtick>) r)
                            |> Option.map (fun run ->
                                let t = RunAssignment.progress (subTick * 1<subtick>) run
                                let tx, ty = RunAssignment.evaluateTrajectory t run.Trajectory
                                float32 tx, float32 ty)
                            |> Option.defaultWith (fun () -> frame.Intent.TargetX[i], frame.Intent.TargetY[i])
                        | _ -> frame.Intent.TargetX[i], frame.Intent.TargetY[i]

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

                    let quality =
                        FatiguePipeline.decisionQuality player roster.Profiles[rosterIdx] condition

                    let effectiveCondition = int (float condition * quality)

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

                    let newPos =
                        PlayerSteering.PlayerPhysics.steerSoA
                            pcfg
                            player
                            effectiveCondition
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

                    // Degradación de condición — 1 vez por segundo
                    if subTick % clock.SubTicksPerSecond = 0 then
                        let matchMinute = SimulationClock.subTicksToSeconds clock (subTick * 1<subtick>) / 60.0

                        let isPressing =
                            frame.Intent.Kind[i] = IntentKind.PressBall
                            || frame.Intent.Kind[i] = IntentKind.TackleAttempt

                        let newCond =
                            FatiguePipeline.degradeCondition player condition isPressing matchMinute

                        FrameMutate.setCondition frame i newCond
