namespace FootballEngine.Core.Match

/// Emergent team traits derived from what actually happens on the pitch.
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
            let pressBase =
                float (
                    if side = ClubSide.Home then
                        ctx.HomeClub.BaseTactics.PressIntensity
                    else
                        ctx.AwayClub.BaseTactics.PressIntensity
                )

            MatchMath.clamp01 (pressBase + float (foulsWindow + tacklesWindow) * 0.08)

        let crossesWindow =
            EventWindow.countInWindow team.EventWindow tick 6000<subtick> EventKind.CrossAttempt

        let wingplay =
            MatchMath.clamp01 (
                float crossesWindow * 0.15
                + float (
                    if side = ClubSide.Home then
                        ctx.HomeClub.BaseTactics.Width
                    else
                        ctx.AwayClub.BaseTactics.Width
                  )
                  * 0.4
            )

        // R9 — adaptive channels: EMA of which corridor the attack comes from,
        // based on the ball's Y position when events happen.
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

        // Normalize the three EMA counters so Cross-vs-Pass bias is a clean fraction.
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
