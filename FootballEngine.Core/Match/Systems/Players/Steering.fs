namespace FootballEngine.Core.Match

/// Steering turns intents into motion with the behaviours a player needs to
/// look alive: arrive (decelerate into the target instead of overshooting),
/// separation (never stack on a teammate), fatigue scaling (tired legs run
/// slower) and smoothing (no teleporting direction changes). Velocity is
/// written back to the frame because other systems — carries, perception —
/// read it.
module PlayerSteering =

    /// Set-piece positioning is uncontested, so a run turns aside from the nearest body ahead of it instead of
    /// pressing into it; contact resolution alone left two players walking into each other frozen chest to chest.
    /// Turning away from the side the body stands on, and to the right when it is dead ahead, means two players
    /// meeting head-on pass each other rather than mirroring each other's step.
    let private sidestep (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) (ux: float) (uy: float) =
        let frame = (MatchWorld.team world side).Frame
        let px = float frame.PosX.[slot]
        let py = float frame.PosY.[slot]
        let radius = float ctx.Config.Movement.SetPieceAvoidRadius
        let mutable nearest = radius * radius
        let mutable lateral = 0.0
        let mutable found = false

        for otherSide in MatchWorld.sides do
            let other = (MatchWorld.team world otherSide).Frame

            for o in MatchWorld.activeSlots other do
                if otherSide <> side || o <> slot then
                    let ox = float other.PosX.[o] - px
                    let oy = float other.PosY.[o] - py
                    let d2 = ox * ox + oy * oy
                    let ahead = ox * ux + oy * uy

                    if ahead > 0.0 && d2 < nearest then
                        nearest <- d2
                        lateral <- ox * -uy + oy * ux
                        found <- true

        if not found then
            ux, uy
        else
            // Positive lateral means the body is on the left, so the run bends right, and vice versa.
            let turn =
                if lateral > 0.0 then -1.0
                else if lateral < 0.0 then 1.0
                else -1.0

            let px', py' = -uy * turn, ux * turn
            let sx, sy = ux + px', uy + py'
            let n = sqrt (sx * sx + sy * sy)
            sx / n, sy / n

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
            match frame.SteerIntent.Kind.[slot] with
            | IntentCode.None -> float frame.SteerShapeX.[slot], float frame.SteerShapeY.[slot], false
            | kind when IntentCode.isContinuous kind ->
                float frame.SteerIntent.TargetX.[slot], float frame.SteerIntent.TargetY.[slot], true
            | _ ->
                if tick <= frame.SteerIntent.CommittedUntil.[slot] then
                    float frame.SteerIntent.TargetX.[slot], float frame.SteerIntent.TargetY.[slot], true
                else
                    frame.SteerExit.[slot] <- ExitTrigger.Expired
                    float frame.SteerShapeX.[slot], float frame.SteerShapeY.[slot], false

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

        frame.TargetFacing.[slot] <-
            let targetDx = tx - px
            let targetDy = ty - py

            if targetDx * targetDx + targetDy * targetDy > float cfg.FacingIntentDistance ** 2.0 then
                float32 (atan2 targetDy targetDx)
            else
                // Small shape corrections must not turn an otherwise settled player away from play.
                let ballDx, ballDy =
                    float world.State.Ball.Position.X - px, float world.State.Ball.Position.Y - py

                if ballDx * ballDx + ballDy * ballDy > float cfg.FacingIntentDistance ** 2.0 then
                    float32 (atan2 ballDy ballDx)
                else
                    frame.BodyFacing.[slot]

        let dx = tx - px
        let dy = ty - py
        let dist = sqrt (dx * dx + dy * dy)

        if dist > 0.05 then
            let cond = float frame.Condition.[slot]

            // Fatigue scales the whole desired speed: an exhausted player is
            // not just slower — his movement range shrinks with his condition.
            let fatigue = 1.0 - (1.0 - cond) * float cfg.FatigueSpeedDecay

            let phaseBoost =
                match team.Latched.OurPhase with
                | PhaseKind.Attacking -> 0.25
                | PhaseKind.TransitionAttack -> 0.35
                | PhaseKind.Defending -> 0.1
                | PhaseKind.TransitionDefend -> 0.2

            let speedMult =
                let transition = team.Latched.InTransition

                match frame.SteerIntent.Kind.[slot] with
                // The man on the ball must keep pace with the carrier or the carrier simply runs past his path.
                | IntentCode.Contain -> 1.0
                | IntentCode.Cover when transition -> ctx.Config.DefensiveTransition.CoverEffort
                | IntentCode.Mark when transition -> ctx.Config.DefensiveTransition.ScreenEffort
                | IntentCode.Regroup when transition -> ctx.Config.DefensiveTransition.RecoveryEffort
                | IntentCode.BallCarry -> cfg.CarryEffort
                | IntentCode.ExecuteRun
                | IntentCode.Press
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

            let ux, uy =
                if frame.SteerIntent.Kind.[slot] = IntentCode.SetPieceRun then
                    sidestep ctx world side slot (dx / dist) (dy / dist)
                else
                    dx / dist, dy / dist

            frame.TargetVelX.[slot] <- float32 (ux * speed)
            frame.TargetVelY.[slot] <- float32 (uy * speed)
        else
            frame.TargetVelX.[slot] <- 0.0f
            frame.TargetVelY.[slot] <- 0.0f

        if
            dist < 1.0
            && activeIntent
            && not (IntentCode.isContinuous frame.SteerIntent.Kind.[slot])
        then
            frame.SteerExit.[slot] <- ExitTrigger.ReachedTarget

    let private decayCondition (ctx: MatchContext) (clock: SimulationClock) (world: World) : unit =
        let dt = float (SimulationClock.dt clock)
        let cfg = ctx.Config.Movement
        let decay = float cfg.ConditionDecayPerSecond * dt
        let recover = float cfg.RecoveryPerSecond * dt

        let ticking =
            world.State.Clock.Tick > 0<subtick>
            && world.State.Clock.Phase <> ClockPhase.HalfTime

        for side in MatchWorld.sides do
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

            for side in MatchWorld.sides do
                let frame = (MatchWorld.team world side).Frame

                for slot in MatchWorld.activeSlots frame do
                    stepPlayer ctx.Ctx ctx.Clock world side slot tick

            decayCondition ctx.Ctx ctx.Clock world
            rng
