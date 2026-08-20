namespace FootballEngine.Core.Match

/// The declarative bridge between cognition and motion: what each player is
/// trying to do and where they want to go. Intent only describes — steering is
/// the sole consumer that turns it into movement — so how a player thinks is
/// never tangled with how he walks.
module PlayerIntent =

    let private setIntent
        (frame: TeamFrame)
        (slot: int)
        (kind: byte)
        (tx: float32)
        (ty: float32)
        (tick: int<subtick>)
        : unit =
        if
            frame.Intent.Kind.[slot] = IntentCode.None
            || frame.Intent.Phase.[slot] = IntentPhase.Completed
        then
            let tx, ty = MatchMath.pitchClampF32 tx ty
            frame.Intent.Kind.[slot] <- kind
            frame.Intent.TargetX.[slot] <- tx
            frame.Intent.TargetY.[slot] <- ty
            frame.Intent.Phase.[slot] <- IntentPhase.Committed
            frame.Intent.CommittedAt.[slot] <- tick
            frame.Intent.CommittedUntil.[slot] <- tick + 480<subtick>
            frame.Intent.ExitTrigger.[slot] <- ExitTrigger.None

    let private pressSpot (ctx: MatchContext) (world: World) (side: ClubSide) : float32 * float32 =
        let ball = world.State.Ball.Position
        let team = MatchWorld.team world side

        let pressZone =
            14.0 * float team.Blackboard.OpponentPressure / 6.0 |> MatchMath.clamp 8.0 16.0

        let tx =
            MatchMath.clamp
                (-(float Pitch.HalfLength))
                (float Pitch.HalfLength)
                (float ball.X + pressZone * float (MatchMath.goalDirection side))

        float32 tx, float32 ball.Y

    let private roleIntent (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let p = MatchWorld.profileOf ctx side slot
        let ball = world.State.Ball.Position
        let bx = float ball.X
        let by = float ball.Y
        let shapeX = float team.Shape.TargetsX.[slot]
        let shapeY = float team.Shape.TargetsY.[slot]
        let phase = team.Blackboard.OurPhase
        let dir = MatchMath.goalDirection side

        let carrier =
            match MatchWorld.ballCarrier ctx world with
            | Some(s, cSlot, _) -> s = side && cSlot = slot
            | None -> false

        if carrier then
            setIntent frame slot IntentCode.BallCarry (float32 (MatchMath.goalX side)) 0.0f tick
        else
            let ballDist =
                MatchMath.distF32 team.Shape.TargetsX.[slot] team.Shape.TargetsY.[slot] (float32 bx) (float32 by)

            let myStep = team.Intercept.SlotSteps.[slot]
            let press = Pressing.shouldPress ctx.Config.Press team.Intercept p.Role myStep

            match p.Role with
            | SlotRole.Goalkeeper ->
                setIntent frame slot IntentCode.HoldPosition (float32 (MatchMath.ownGoalX side)) 0.0f tick
            | SlotRole.Defender
            | SlotRole.FullBack
            | SlotRole.DefensiveMid ->
                if phase = PhaseKind.Defending || phase = PhaseKind.TransitionDefend then
                    if press then
                        let sx, sy = pressSpot ctx world side
                        setIntent frame slot IntentCode.Press sx sy tick
                    else
                        let goalX = MatchMath.ownGoalX side
                        let mx = float shapeX + (float shapeX - float goalX) * 0.35
                        setIntent frame slot IntentCode.HoldPosition (float32 mx) (float32 shapeY) tick
                elif ballDist < 22.0 && phase = PhaseKind.TransitionAttack then
                    let sx, sy = pressSpot ctx world side
                    setIntent frame slot IntentCode.Press sx sy tick
                else
                    setIntent frame slot IntentCode.Support (float32 shapeX) (float32 shapeY) tick
            | SlotRole.Striker ->
                if phase = PhaseKind.Attacking || phase = PhaseKind.TransitionAttack then
                    let goalX = float (MatchMath.goalX side)
                    let pushX = shapeX + 6.0 * dir

                    let tx =
                        min pushX goalX
                        |> MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength)

                    setIntent frame slot IntentCode.ExecuteRun (float32 tx) (float32 shapeY) tick
                elif press then
                    let sx, sy = pressSpot ctx world side
                    setIntent frame slot IntentCode.Press sx sy tick
                else
                    setIntent frame slot IntentCode.Support (float32 shapeX) (float32 shapeY) tick
            | _ ->
                if (phase = PhaseKind.Defending || phase = PhaseKind.TransitionDefend) && press then
                    let sx, sy = pressSpot ctx world side
                    setIntent frame slot IntentCode.Press sx sy tick
                elif phase = PhaseKind.Attacking && ballDist < 25.0 then
                    setIntent frame slot IntentCode.Support (float32 shapeX) (float32 shapeY) tick
                elif press then
                    let sx, sy = pressSpot ctx world side
                    setIntent frame slot IntentCode.Press sx sy tick
                else
                    setIntent frame slot IntentCode.HoldPosition (float32 shapeX) (float32 shapeY) tick

    let private receiveIntent
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (tick: int<subtick>)
        : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let ball = world.State.Ball

        match ball.Trajectory, ball.ArrivalGate with
        | Some(TrajectoryIntent.Aimed(_, receiver, _)), Some gate when gate > tick ->
            match MatchWorld.teamOfPlayer ctx receiver with
            | Some rs when rs = side ->
                match MatchWorld.slotOf ctx side receiver with
                | Some rslot when rslot = slot ->
                    let remaining = float (gate - tick) / 40.0
                    let ax = float ball.Position.X + float ball.Position.Vx * remaining
                    let ay = float ball.Position.Y + float ball.Position.Vy * remaining
                    let ax, ay = MatchMath.pitchClampF32 (float32 ax) (float32 ay)

                    frame.Intent.Kind.[slot] <- IntentCode.ExecuteRun
                    frame.Intent.TargetX.[slot] <- float32 ax
                    frame.Intent.TargetY.[slot] <- float32 ay
                    frame.Intent.Phase.[slot] <- IntentPhase.Committed
                    frame.Intent.CommittedAt.[slot] <- tick
                    frame.Intent.CommittedUntil.[slot] <- tick + 480<subtick>
                | _ -> ()
            | _ -> ()
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            for side in [ ClubSide.Home; ClubSide.Away ] do
                let team = MatchWorld.team world side
                let frame = team.Frame

                // Plan-override: before roleIntent, steer planned receivers
                // toward their designated positions.
                match team.ActivePlan with
                | Some plan when plan.ExpiresAt > tick ->
                    for (slot, px, py) in plan.FutureReceivers do
                        if
                            slot > 0
                            && slot < frame.Intent.Kind.Length
                            && frame.Intent.Kind.[slot] = IntentCode.None
                        then
                            setIntent frame slot IntentCode.ExecuteRun (float32 px) (float32 py) tick
                | _ -> ()

                for slot in MatchWorld.activeSlots frame do
                    receiveIntent ctx.Ctx world side slot tick
                    roleIntent ctx.Ctx world side slot tick

            rng
