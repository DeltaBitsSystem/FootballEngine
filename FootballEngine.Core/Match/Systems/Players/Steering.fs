namespace FootballEngine.Core.Match

/// Steering turns intents into motion with the behaviours a player needs to
/// look alive: arrive (decelerate into the target instead of overshooting),
/// separation (never stack on a teammate), fatigue scaling (tired legs run
/// slower) and smoothing (no teleporting direction changes). Velocity is
/// written back to the frame because other systems — carries, perception —
/// read it.
module PlayerSteering =

    let private stepPlayer
        (ctx: MatchContext)
        (clock: SimulationClock)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (tick: int<subtick>)
        : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let cfg = ctx.Config.Movement

        let tx, ty, activeIntent =
            match frame.Intent.Kind.[slot] with
            | IntentCode.None -> float team.Shape.TargetsX.[slot], float team.Shape.TargetsY.[slot], false
            | kind when IntentCode.isContinuous kind ->
                float frame.Intent.TargetX.[slot], float frame.Intent.TargetY.[slot], true
            | _ ->
                if tick <= frame.Intent.CommittedUntil.[slot] then
                    float frame.Intent.TargetX.[slot], float frame.Intent.TargetY.[slot], true
                else
                    frame.Intent.Phase.[slot] <- IntentPhase.Completed
                    frame.Intent.ExitTrigger.[slot] <- ExitTrigger.Expired
                    float team.Shape.TargetsX.[slot], float team.Shape.TargetsY.[slot], false

        let px = float frame.PosX.[slot]
        let py = float frame.PosY.[slot]

        let restartExecutor =
            match world.State.Flow with
            | MatchFlow.Restart { Plan = Some plan; Phase = phase } when
                plan.Side = side
                && (phase = RestartPhase.Positioning || phase = RestartPhase.Authorized)
                ->
                plan.Executor = team.RosterIds.[slot]
            | _ -> false

        let desiredFacing =
            let targetDx = tx - px
            let targetDy = ty - py

            if targetDx * targetDx + targetDy * targetDy > float cfg.FacingIntentDistance ** 2.0 then
                Some(atan2 targetDy targetDx)
            else
                // Small shape corrections must not turn an otherwise settled player away from play.
                let ballDx, ballDy = float world.State.Ball.Position.X - px, float world.State.Ball.Position.Y - py
                if ballDx * ballDx + ballDy * ballDy > float cfg.FacingIntentDistance ** 2.0 then
                    Some(atan2 ballDy ballDx)
                else None

        frame.TargetFacing.[slot] <-
            desiredFacing
            |> Option.map float32
            |> Option.defaultValue frame.BodyFacing.[slot]

        let dx = tx - px
        let dy = ty - py
        let dist = sqrt (dx * dx + dy * dy)

        if dist > 0.05 then
            let cond = float frame.Condition.[slot]

            // Fatigue scales the whole desired speed: an exhausted player is
            // not just slower — his movement range shrinks with his condition.
            let fatigue = 1.0 - (1.0 - cond) * float cfg.FatigueSpeedDecay

            let phaseBoost =
                match team.Blackboard.OurPhase with
                | PhaseKind.Attacking -> 0.25
                | PhaseKind.TransitionAttack -> 0.35
                | PhaseKind.Defending -> 0.1
                | PhaseKind.TransitionDefend -> 0.2

            let speedMult =
                let transition = team.Defense.Episode |> Option.exists (fun episode -> episode.EndedAt.IsNone)
                match frame.Intent.Kind.[slot] with
                | IntentCode.Cover when transition -> ctx.Config.DefensiveTransition.CoverEffort
                | IntentCode.Mark when transition -> ctx.Config.DefensiveTransition.ScreenEffort
                | IntentCode.Regroup when transition -> ctx.Config.DefensiveTransition.RecoveryEffort
                | IntentCode.ExecuteRun
                | IntentCode.Press
                | IntentCode.BallCarry
                | IntentCode.SetPieceRun -> 1.0
                | _ -> phaseBoost

            let desiredSpeed =
                (float cfg.JogSpeed + (float cfg.SprintSpeed - float cfg.JogSpeed) * speedMult)
                * fatigue

            // Arrive behaviour: inside the reactive radius the player eases
            // into the target, so he stops instead of oscillating around it.
            let speed =
                if dist < float cfg.ReactiveRadius then
                    desiredSpeed * (dist / float cfg.ReactiveRadius)
                else
                    desiredSpeed

            frame.TargetVelX.[slot] <- float32 (dx / dist * speed)
            frame.TargetVelY.[slot] <- float32 (dy / dist * speed)
        else
            frame.TargetVelX.[slot] <- 0.0f
            frame.TargetVelY.[slot] <- 0.0f

        if
            dist < 1.0
            && activeIntent
            && not (IntentCode.isContinuous frame.Intent.Kind.[slot])
        then
            frame.Intent.ExitTrigger.[slot] <- ExitTrigger.ReachedTarget
            frame.Intent.Phase.[slot] <- IntentPhase.Completed

    let private decayCondition (ctx: MatchContext) (clock: SimulationClock) (world: World) : unit =
        let dt = float (SimulationClock.dt clock)
        let cfg = ctx.Config.Movement
        let decay = float cfg.ConditionDecayPerSecond * dt
        let recover = float cfg.RecoveryPerSecond * dt

        let ticking =
            world.State.Clock.Tick > 0<subtick>
            && world.State.Clock.Phase <> ClockPhase.HalfTime

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let frame = (MatchWorld.team world side).Frame

            for slot in MatchWorld.activeSlots frame do
                let c = float frame.Condition.[slot]

                if ticking then
                    // Tiredness follows effort: condition decays by actual
                    // running speed, and stops (halftime) recover it.
                    let running =
                        sqrt (
                            float frame.VelX.[slot] * float frame.VelX.[slot]
                            + float frame.VelY.[slot] * float frame.VelY.[slot]
                        )
                        / 8.0
                        |> MatchMath.clamp 0.2 1.0

                    frame.Condition.[slot] <- float32 (MatchMath.clamp01 (c - decay * running))
                elif c < 1.0 then
                    frame.Condition.[slot] <- float32 (min 1.0 (c + recover))

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick

            for side in [ ClubSide.Home; ClubSide.Away ] do
                let frame = (MatchWorld.team world side).Frame

                for slot in MatchWorld.activeSlots frame do
                    stepPlayer ctx.Ctx ctx.Clock world side slot tick

            decayCondition ctx.Ctx ctx.Clock world
            rng
