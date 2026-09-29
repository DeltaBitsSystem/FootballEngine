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
        let dt = float (SimulationClock.dtPlayer clock)

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
                match frame.Intent.Kind.[slot] with
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

            let mutX = float frame.VelX.[slot]
            let mutY = float frame.VelY.[slot]

            let desiredX = dx / dist * speed
            let desiredY = dy / dist * speed
            let deltaX = desiredX - mutX
            let deltaY = desiredY - mutY
            let deltaLength = sqrt (deltaX * deltaX + deltaY * deltaY)
            let maxDelta = float cfg.Acceleration * dt * max 0.1 (min 1.0 (float cfg.Smoothing))

            let accelerationScale =
                if deltaLength > maxDelta then
                    maxDelta / deltaLength
                else
                    1.0

            let smoothX = mutX + deltaX * accelerationScale
            let smoothY = mutY + deltaY * accelerationScale

            let mutable vx = smoothX
            let mutable vy = smoothY

            let sepRadius = float cfg.SeparationRadius
            let sepStrength = float cfg.SeparationStrength

            let teamFrame = frame

            // Separation: teammates repel within the separation radius, so a
            // compact shape never collapses into players standing on each other.
            for s2 in MatchWorld.activeSlots teamFrame do
                if s2 <> slot then
                    let ox = float teamFrame.PosX.[s2]
                    let oy = float teamFrame.PosY.[s2]
                    let sdx = px - ox
                    let sdy = py - oy
                    let sd = sqrt (sdx * sdx + sdy * sdy)

                    if sd < sepRadius && sd > 0.01 then
                        let push = (1.0 - sd / sepRadius) * sepStrength * 3.0
                        vx <- vx + sdx / sd * push
                        vy <- vy + sdy / sd * push

            frame.PosX.[slot] <-
                float32 (MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) (px + vx * dt))

            frame.PosY.[slot] <-
                float32 (MatchMath.clamp (-(float Pitch.HalfWidth)) (float Pitch.HalfWidth) (py + vy * dt))

            frame.VelX.[slot] <- float32 vx
            frame.VelY.[slot] <- float32 vy

            if
                dist < 1.0
                && activeIntent
                && not (IntentCode.isContinuous frame.Intent.Kind.[slot])
            then
                frame.Intent.ExitTrigger.[slot] <- ExitTrigger.ReachedTarget
                frame.Intent.Phase.[slot] <- IntentPhase.Completed
        else
            let separationRadius = float cfg.SeparationRadius
            let mutable pushX = 0.0
            let mutable pushY = 0.0

            for teammate in MatchWorld.activeSlots frame do
                if teammate <> slot then
                    let dx = px - float frame.PosX.[teammate]
                    let dy = py - float frame.PosY.[teammate]
                    let distanceSquared = dx * dx + dy * dy

                    if distanceSquared < separationRadius * separationRadius then
                        if distanceSquared > 0.0001 then
                            let distance = sqrt distanceSquared
                            let strength = 1.0 - distance / separationRadius
                            pushX <- pushX + dx / distance * strength
                            pushY <- pushY + dy / distance * strength
                        else
                            // Slot identity provides a stable escape direction when two
                            // players occupy exactly the same coordinate.
                            pushY <- pushY + (if slot < teammate then -1.0 else 1.0)

            if pushX <> 0.0 || pushY <> 0.0 then
                let length = sqrt (pushX * pushX + pushY * pushY)
                let speed = float cfg.SeparationStrength

                frame.PosX.[slot] <-
                    float32 (
                        MatchMath.clamp
                            (-(float Pitch.HalfLength))
                            (float Pitch.HalfLength)
                            (px + pushX / length * speed * dt)
                    )

                frame.PosY.[slot] <-
                    float32 (
                        MatchMath.clamp
                            (-(float Pitch.HalfWidth))
                            (float Pitch.HalfWidth)
                            (py + pushY / length * speed * dt)
                    )

                frame.VelX.[slot] <- float32 (pushX / length * speed)
                frame.VelY.[slot] <- float32 (pushY / length * speed)
            else
                frame.VelX.[slot] <- 0.0f
                frame.VelY.[slot] <- 0.0f

        let legalX, legalY = MatchMath.pitchClampF32 frame.PosX.[slot] frame.PosY.[slot]
        frame.PosX.[slot] <- legalX
        frame.PosY.[slot] <- legalY

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
