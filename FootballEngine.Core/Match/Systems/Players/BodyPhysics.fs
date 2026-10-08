namespace FootballEngine.Core.Match

module PlayerBodyPhysics =
    let step: SystemStep =
        fun ctx rng world buffer ->
            let dt = float (SimulationClock.dt ctx.Clock)
            let gravity = float ctx.Ctx.Config.Ball.Gravity
            let cfg = ctx.Ctx.Config.Movement

            for side in [ ClubSide.Home; ClubSide.Away ] do
                let team = MatchWorld.team world side
                let frame = team.Frame

                for slot in MatchWorld.activeSlots frame do
                    let grounded = frame.PosZ[slot] = 0.0f && frame.VelZ[slot] = 0.0f
                    let requested = frame.JumpRequested[slot]
                    frame.JumpRequested[slot] <- false

                    if requested && grounded && world.State.Flow = MatchFlow.Live then
                        let fatigue = 1.0 - (1.0 - float frame.Condition[slot]) * cfg.FatigueSpeedDecay
                        frame.VelZ[slot] <- float32 (float cfg.JumpSpeed * fatigue)
                        frame.JumpSequence[slot] <- frame.JumpSequence[slot] + 1UL

                        buffer.Events.Add
                            { Tick = world.State.Tick
                              Data = EventData.PlayerBodyFlight(team.RosterIds[slot], frame.JumpSequence[slot], true) }

                    if frame.PosZ[slot] > 0.0f || frame.VelZ[slot] <> 0.0f then
                        let height =
                            float frame.PosZ[slot] + float frame.VelZ[slot] * dt - 0.5 * gravity * dt * dt

                        let velocity = float frame.VelZ[slot] - gravity * dt

                        if height <= 0.0 && velocity <= 0.0 then
                            frame.PosZ[slot] <- 0.0f
                            frame.VelZ[slot] <- 0.0f

                            buffer.Events.Add
                                { Tick = world.State.Tick
                                  Data =
                                    EventData.PlayerBodyFlight(team.RosterIds[slot], frame.JumpSequence[slot], false) }
                        else
                            frame.PosZ[slot] <- float32 (max 0.0 height)
                            frame.VelZ[slot] <- float32 velocity

            rng
