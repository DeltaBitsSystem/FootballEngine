namespace FootballEngine.Core.Match

/// Pass execution: launches an aimed delivery at a teammate, judging offside
/// at the moment of the pass — the law applies when the ball is played, not
/// when it lands. Resolution lives in BallContact (which emits the Pass
/// event), so launching and resolving stay one step each.
module Pass =

    let private flagOffside
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (receiverSlot: int)
        (tick: int<subtick>)
        : unit =
        let team = MatchWorld.team world side
        let receiver = (MatchWorld.lineup ctx.Ctx side).Profiles.[receiverSlot].Id
        let line = MatchWorld.offsideLine ctx.Ctx world side
        let receiverX = MatchWorld.posX team.Frame receiverSlot

        RefereeVAR.markOffsideSnapshot world side receiverSlot receiverX line tick

        buffer.Events.Add
            { Tick = tick
              Data = EventData.Offside receiver }

    /// Launch a pass to a teammate slot. `qualityBonus` lets the GK system
    /// make distribution easier (positive) or hurried (negative); passes
    /// beyond 65 m are refused as a sanity cap on delivery.
    let launch
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (slot: int)
        (targetSlot: int)
        (qualityBonus: float)
        : Rng =
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let frame = (MatchWorld.team world side).Frame
        let pid = p.Id

        let x = MatchWorld.posX frame slot
        let y = MatchWorld.posY frame slot
        let rx = MatchWorld.posX frame targetSlot
        let ry = MatchWorld.posY frame targetSlot

        let d = MatchMath.dist x y rx ry
        let speed = float ctx.Ctx.Config.Actions.PassLaunchSpeed + float d * 0.1
        let originX = world.State.Ball.Position.X
        let originY = world.State.Ball.Position.Y
        let flightDistance = MatchMath.dist originX originY rx ry
        let flightSeconds = float flightDistance / speed

        let targetRx =
            MatchMath.clamp
                (-(float Pitch.HalfLength + 5.0))
                (float Pitch.HalfLength + 5.0)
                (float rx + float frame.VelX.[targetSlot] * flightSeconds)
            * 1.0<meter>

        let targetRy =
            MatchMath.clamp
                (-(float Pitch.HalfWidth + 5.0))
                (float Pitch.HalfWidth + 5.0)
                (float ry + float frame.VelY.[targetSlot] * flightSeconds)
            * 1.0<meter>

        let launchDelivery (quality: float) (rng: Rng) : Rng =
            let direction = max (float (MatchMath.dist originX originY targetRx targetRy)) 0.001

            let linearVelocity =
                { X = float (targetRx - originX) / direction * speed * 1.0<meterPerSecond>
                  Y = float (targetRy - originY) / direction * speed * 1.0<meterPerSecond>
                  Z = 0.0<meterPerSecond> }

            let angularVelocity =
                { Top = 0.0<radianPerSecond>
                  Side = 0.0<radianPerSecond>
                  Roll = 0.0<radianPerSecond> }

            let target: Vec2 option = Some { X = targetRx; Y = targetRy }

            let impulse =
                BallPhysics.createImpulse
                    pid
                    { X = world.State.Ball.Position.X
                      Y = world.State.Ball.Position.Y
                      Z = world.State.Ball.Position.Z }
                    linearVelocity
                    angularVelocity
                    target
                    StrikeKind.Pass
                    (SimTick.ofSubTicks tick)
                    ExecutionContext.OpenPlay

            let arrival =
                tick
                + int (round (flightSeconds * float ctx.Clock.SubTicksPerSecond)) * 1<subtick>
                + 6<subtick>

            BallPhysics.applyImpulse
                world
                impulse
                (TrajectoryIntent.Aimed(pid, (MatchWorld.lineup ctx.Ctx side).Profiles.[targetSlot].Id, quality))
                (Some arrival)

            rng

        // R5 — a cognitively tired receiver is harder to feed.
        let receiverCognition = float frame.Cognitive.[targetSlot]
        let cognitiveQuality = 0.6 + 0.4 * receiverCognition

        if d > 65.0 then
            rng
        else
            let line = MatchWorld.offsideLine ctx.Ctx world side
            let dir = MatchMath.goalDirection side
            let forward = (float rx - float x) * dir > 0.0

            let beyond =
                if dir > 0.0 then
                    float rx > float line
                else
                    float rx < float line

            let beyondBall =
                if dir > 0.0 then
                    float rx > float world.State.Ball.Position.X
                else
                    float rx < float world.State.Ball.Position.X

            if forward && beyond && beyondBall then
                flagOffside ctx world buffer side targetSlot tick
                rng
            else
                let jitter, rng' = Rng.uniform (-1.0) 1.0 rng

                let quality =
                    MatchMath.clamp01 (
                        cfg.Actions.PassBaseQuality * cognitiveQuality
                        + (float p.Passing - 60.0) * cfg.Actions.PassTechnicalWeight
                        + (float p.Vision - 60.0) * cfg.Actions.PassVisionWeight
                        - d * cfg.Actions.PassDistancePenaltyPerMeter
                        + jitter * cfg.Actions.PassJitterBase
                        + qualityBonus
                    )

                launchDelivery quality rng'
