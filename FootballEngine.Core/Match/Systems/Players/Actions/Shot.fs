namespace FootballEngine.Core.Match

/// Shot execution: strike the ball and leave behind everything a goal may be
/// judged on — a VAR offside snapshot for close-range attempts (the ones that
/// get disputed) and a memory record so goal-less runs cool shooting (R6).
/// The Shot event is emitted by BallOutcome at resolution, one per attempt,
/// so the viewer sees the shot when it resolves.
module Shot =

    let launch (ctx: SystemContext) (rng: Rng) (world: World) (buffer: EventBuffer) (side: ClubSide) (slot: int) : Rng =
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let frame = (MatchWorld.team world side).Frame
        let pid = p.Id

        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot

        let d = MatchMath.distanceToGoal side x y
        let goalY = 0.0<meter>

        let angle, rng' =
            let spread = cfg.Actions.ShotAngleSpread * (1.0 + d / 40.0)
            Rng.uniform (-spread) spread rng

        let near = MatchWorld.opponentsNear ctx.Ctx world side x y 6.0
        let openness = 1.0 - 0.3 * MatchMath.clamp01 (float near / 4.0)
        let xg = MatchMath.xGFrom d (float p.Finishing) openness

        let pOnTarget =
            MatchMath.clamp01 (0.5 + (float p.Finishing - 60.0) / 100.0 + 0.2 * openness)

        let angle, rng' =
            let onTarget, rng' = Rng.bernoulli pOnTarget rng

            if onTarget then
                let spread = cfg.Actions.ShotAngleSpread * (1.0 + d / 40.0)
                Rng.uniform (-spread) spread rng'
            else
                let wide, rng'' = Rng.uniform 1.0 4.0 rng'
                let flip, rng''' = Rng.bernoulli 0.5 rng''
                let targetY = (float Pitch.GoalHalfWidth + wide) * (if flip then 1.0 else -1.0)
                atan ((targetY - float y) / float d), rng'''

        let speed = 22.0 + float p.Pace * 0.06

        MatchWorld.updateBall world (fun b ->
            { b with
                Control = BallControl.Airborne
                Trajectory = Some(TrajectoryIntent.Struck(pid, speed * 1.0<meterPerSecond>, angle, xg))
                LastTouch = Some pid
                ArrivalGate = Some(tick + int (d / speed * 40.0) * 1<subtick> + 4<subtick>)
                StationarySince = None })

        let gkSlot = 0

        if d < 30.0 then
            let defLine = MatchWorld.offsideLine ctx.Ctx world side
            let defTeam = MatchWorld.team world (MatchWorld.oppOf side)
            let defFrame = defTeam.Frame

            RefereeVAR.markOffsideSnapshot world side slot x defLine tick

        let mem = (MatchWorld.team world side).Memory

        mem.[slot].ShotsTaken <- mem.[slot].ShotsTaken + 1

        rng'
