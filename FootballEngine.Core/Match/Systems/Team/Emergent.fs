namespace FootballEngine.Core.Match

/// Traits derived from what the team actually does on the pitch, so the team's
/// behavior responds to its own play instead of only to the configured tactics.
module TeamEmergent =

    let private emergentTeam (ctx: MatchContext) (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame

        let cx, cy, n =
            let mutable sx = 0.0
            let mutable sy = 0.0
            let mutable n = 0

            for slot in MatchWorld.activeSlots frame do
                sx <- sx + float frame.PosX.[slot]
                sy <- sy + float frame.PosY.[slot]
                n <- n + 1

            if n = 0 then 0.0, 0.0, 0 else sx / float n, sy / float n, n

        let compactness =
            if n = 0 then
                1.0
            else
                let mutable sum = 0.0

                for slot in MatchWorld.activeSlots frame do
                    sum <-
                        sum
                        + MatchMath.dist
                            (MatchWorld.posX frame slot)
                            (MatchWorld.posY frame slot)
                            (cx * 1.0<meter>)
                            (cy * 1.0<meter>)

                let avg = sum / float n
                MatchMath.clamp01 (1.0 - avg / 35.0)

        let foulsWindow =
            EventWindow.countInWindow team.EventWindow tick 6000<subtick> EventKind.Foul

        let tacklesWindow =
            EventWindow.countInWindow team.EventWindow tick 6000<subtick> EventKind.Tackle

        let pressing =
            let pressBase = float (team.Tactics.PressIntensity)

            MatchMath.clamp01 (pressBase + float (foulsWindow + tacklesWindow) * 0.08)

        let crossesWindow =
            EventWindow.countInWindow team.EventWindow tick 6000<subtick> EventKind.CrossAttempt

        let wingplay =
            MatchMath.clamp01 (float crossesWindow * 0.15 + float (team.Tactics.Width) * 0.4)

        // R9 — adaptive channels: an EMA of the ball's corridor lets the team
        // bias its play toward the corridor that is actually producing attacks.
        let by = float world.State.Ball.Position.Y
        let centerNudge = if abs by < 10.0 then 1.0 else 0.0
        let leftNudge = if by <= -10.0 then 1.0 else 0.0
        let rightNudge = if by >= 10.0 then 1.0 else 0.0
        let alpha = 0.05

        team.Emergent.ChannelCenter <-
            float32 (
                float team.Emergent.ChannelCenter
                + alpha * (centerNudge - float team.Emergent.ChannelCenter)
            )

        team.Emergent.ChannelLeft <-
            float32 (
                float team.Emergent.ChannelLeft
                + alpha * (leftNudge - float team.Emergent.ChannelLeft)
            )

        team.Emergent.ChannelRight <-
            float32 (
                float team.Emergent.ChannelRight
                + alpha * (rightNudge - float team.Emergent.ChannelRight)
            )

        // Only the relative distribution of the corridors matters, and the raw EMAs
        // drift with how often the ball is sampled; normalizing keeps the three
        // channels comparable as a fraction regardless of that.
        let cSum =
            float team.Emergent.ChannelCenter
            + float team.Emergent.ChannelLeft
            + float team.Emergent.ChannelRight

        if cSum > 0.0001 then
            team.Emergent.ChannelCenter <- float32 (float team.Emergent.ChannelCenter / cSum)
            team.Emergent.ChannelLeft <- float32 (float team.Emergent.ChannelLeft / cSum)
            team.Emergent.ChannelRight <- float32 (float team.Emergent.ChannelRight / cSum)

        let mutable condSum = 0.0
        let mutable condN = 0

        for slot in MatchWorld.activeSlots frame do
            condSum <- condSum + float frame.Condition.[slot]
            condN <- condN + 1

        let fatigue =
            if condN = 0 then
                1.0
            else
                MatchMath.clamp01 (condSum / float condN)

        let step = float ctx.Config.Team.EmergentCompactnessStep

        team.Emergent.Compactness <-
            float32 (
                float team.Emergent.Compactness
                + step * (compactness - float team.Emergent.Compactness)
            )

        team.Emergent.PressingIntensity <-
            float32 (
                float team.Emergent.PressingIntensity
                + step * (pressing - float team.Emergent.PressingIntensity)
            )

        team.Emergent.WingplayBias <-
            float32 (
                float team.Emergent.WingplayBias
                + step * (wingplay - float team.Emergent.WingplayBias)
            )

        team.Emergent.FatigueFactor <- float32 fatigue

    let step: SystemStep =
        fun ctx rng world _ ->
            let tick = world.State.Tick
            emergentTeam ctx.Ctx world ClubSide.Home tick
            emergentTeam ctx.Ctx world ClubSide.Away tick
            rng
