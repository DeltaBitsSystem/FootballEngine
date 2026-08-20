namespace FootballEngine.Core.Match

/// The ONLY place scores change and the ONLY place restarts/flow states are entered:
/// everything is applied from buffered events. Systems mutate World directly, but any
/// structural change must flow through here as an event.
module EventApply =

    let private planFor (kind: SetPieceKind) (side: ClubSide) (ball: BallState) : RestartPlan =
        match kind with
        | SetPieceKind.KickOff ->
            { Kind = kind
              Side = side
              X = 0.0<meter>
              Y = 0.0<meter> }
        | SetPieceKind.Corner ->
            let x = MatchMath.goalX side

            let y =
                if ball.Position.Y >= 0.0<meter> then
                    Pitch.HalfWidth
                else
                    -Pitch.HalfWidth

            { Kind = kind
              Side = side
              X = x
              Y = y }
        | SetPieceKind.ThrowIn ->
            // The throw-in must be taken from inside the touchline: the referee
            // places the ball a small inset from the line so it can never sit
            // glued to the band. X is clamped to the relevant touchline segment.
            let y =
                if ball.Position.Y >= 0.0<meter> then
                    Pitch.HalfWidth - Pitch.ThrowInInset
                else
                    -(Pitch.HalfWidth - Pitch.ThrowInInset)

            let x =
                MatchMath.clamp (-(float Pitch.HalfLength - 0.5)) (float Pitch.HalfLength - 0.5) (float ball.Position.X)

            { Kind = kind
              Side = side
              X = x * 1.0<meter>
              Y = y }
        | SetPieceKind.GoalKick ->
            let dir = MatchMath.goalDirection side

            { Kind = kind
              Side = side
              X = MatchMath.ownGoalX side + 6.5<meter> * dir
              Y = 0.0<meter> }
        | SetPieceKind.FreeKick ->
            let x =
                MatchMath.clamp (-(float Pitch.HalfLength - 0.5)) (float Pitch.HalfLength - 0.5) (float ball.Position.X)

            { Kind = kind
              Side = side
              X = x * 1.0<meter>
              Y = ball.Position.Y }
        | SetPieceKind.Penalty ->
            let dir = MatchMath.goalDirection side

            { Kind = kind
              Side = side
              X = MatchMath.goalX side - 11.0<meter> * dir
              Y = 0.0<meter> }

    let private placeBall (world: World) (plan: RestartPlan) : unit =
        MatchWorld.updateBall world (fun b ->
            { b with
                Position =
                    { X = plan.X
                      Y = plan.Y
                      Z = 0.0<meter>
                      Vx = 0.0<meterPerSecond>
                      Vy = 0.0<meterPerSecond>
                      Vz = 0.0<meterPerSecond> }
                Control = BallControl.Free
                Trajectory = None
                ArrivalGate = None
                LastTouch = None
                StationarySince = None
                GKHoldSince = None })

    let apply (ctx: MatchContext) (world: World) (ev: MatchEvent) : unit =
        let tick = ev.Tick

        match ev.Data with
        | EventData.Possession(side, pid) ->
            MatchWorld.updateBall world (fun b ->
                { b with
                    Control = BallControl.Controlled(side, pid)
                    LastTouch = Some pid })

            let book = world.State.Possession

            let touches =
                if side = ClubSide.Home then
                    book.HomeTouches + 1
                else
                    book.AwayTouches + 1

            world.State <-
                { world.State with
                    Possession =
                        { book with
                            HomeTouches = (if side = ClubSide.Home then touches else book.HomeTouches)
                            AwayTouches = (if side = ClubSide.Away then touches else book.AwayTouches)
                            LastGainer = Some side } }

            let team = MatchWorld.team world side
            team.Stats.Touches <- team.Stats.Touches + 1
        | EventData.BallLoose(x, y) ->
            let side = world.State.Possession.LastGainer |> Option.defaultValue ClubSide.Home

            MatchWorld.updateBall world (fun b ->
                { b with
                    Control = BallControl.Contesting side
                    Trajectory = None
                    ArrivalGate = None
                    StationarySince = Some tick
                    Position = { b.Position with X = x; Y = y } })
        | EventData.Pass(passer, receiver, _, _, completed, _, _, _, _) ->
            match MatchWorld.teamOfPlayer ctx passer with
            | Some side ->
                let team = MatchWorld.team world side
                team.Stats.Passes <- team.Stats.Passes + 1

                match MatchWorld.slotOf ctx side passer with
                | Some slot ->
                    team.Memory.[slot].PassesMade <- team.Memory.[slot].PassesMade + 1

                    if completed then
                        team.Stats.PassesCompleted <- team.Stats.PassesCompleted + 1
                        team.Memory.[slot].PassesCompleted <- team.Memory.[slot].PassesCompleted + 1
                        team.Memory.[slot].PassSuccessMod <- min 1.5 (team.Memory.[slot].PassSuccessMod + 0.15)
                        team.Memory.[slot].StreakMod <- min 3.0 (team.Memory.[slot].StreakMod + 0.2)
                    else
                        team.Memory.[slot].PassFailMod <- min 1.5 (team.Memory.[slot].PassFailMod + 0.3)
                        team.Memory.[slot].StreakMod <- max -2.0 (team.Memory.[slot].StreakMod - 0.25)
                | None -> ()
            | None -> ()
        | EventData.Shot(shooter, result, xG, _, _, _) ->
            match MatchWorld.teamOfPlayer ctx shooter with
            | Some side ->
                let team = MatchWorld.team world side
                team.Stats.Shots <- team.Stats.Shots + 1
                team.Stats.XgSum <- team.Stats.XgSum + xG

                match result with
                | ShotResult.OnTarget _ -> team.Stats.OnTarget <- team.Stats.OnTarget + 1
                | _ -> ()

                match MatchWorld.slotOf ctx side shooter with
                | Some slot ->
                    team.Memory.[slot].ShotsTaken <- team.Memory.[slot].ShotsTaken + 1

                    match result with
                    | ShotResult.OnTarget true ->
                        team.Memory.[slot].ShotSuccessMod <- min 2.0 (team.Memory.[slot].ShotSuccessMod + 0.2)
                    | ShotResult.OnTarget false
                    | ShotResult.Post ->
                        team.Memory.[slot].ShotSuccessMod <- max -2.0 (team.Memory.[slot].ShotSuccessMod - 0.2)
                    | _ -> ()
                | None -> ()
            | None -> ()
        | EventData.Goal(side, scorer, _, _) ->
            let home =
                if side = ClubSide.Home then
                    world.State.HomeScore + 1
                else
                    world.State.HomeScore

            let away =
                if side = ClubSide.Away then
                    world.State.AwayScore + 1
                else
                    world.State.AwayScore

            world.State <-
                { world.State with
                    HomeScore = home
                    AwayScore = away
                    LastAttackingClub = Some side }

            // A goal can only be reviewed if an offside snapshot was recorded at
            // shot time (Phase 250 marker) — no snapshot, nothing to review.
            let hasMarkers =
                let team = MatchWorld.team world side
                team.Frame.RunLookup.Values |> Seq.exists (fun v -> v.Phase = 250)

            if hasMarkers && RefereeVAR.isOffside ctx world side then
                let varDur =
                    int (
                        round (
                            float ctx.Config.Match.VarReviewDuration
                            * float SimulationClock.defaultClock.SubTicksPerSecond
                        )
                    )
                    * 1<tickDelta>

                world.State <-
                    { world.State with
                        Flow = MatchFlow.VARReview(varDur, VARIncident.Offside) }
            else
                let dur =
                    int (
                        round (
                            float ctx.Config.Match.GoalPauseDuration
                            * float SimulationClock.defaultClock.SubTicksPerSecond
                        )
                    )
                    * 1<tickDelta>

                world.State <-
                    { world.State with
                        Flow = MatchFlow.GoalPause(dur, side, scorer) }
        | EventData.Foul(fouler, _, _, _, _) ->
            match MatchWorld.teamOfPlayer ctx fouler with
            | Some side -> (MatchWorld.team world side).Stats.Fouls <- (MatchWorld.team world side).Stats.Fouls + 1
            | None -> ()
        | EventData.Card(player, color) ->
            match MatchWorld.teamOfPlayer ctx player with
            | Some side ->
                let team = MatchWorld.team world side
                let mutable cards = team.Cards

                match color with
                | CardColor.Yellow -> cards.Yellows <- cards.Yellows + 1
                | CardColor.Red -> cards.Reds <- cards.Reds + 1

                team.Cards <- cards
            | None -> ()
        | EventData.SetPiece(kind, side) ->
            let plan = planFor kind side world.State.Ball
            let delay = Flow.delayFor ctx.Config SimulationClock.defaultClock kind
            placeBall world plan

            let clock =
                match kind with
                | SetPieceKind.KickOff when world.State.Clock.Phase = ClockPhase.HalfTime ->
                    { world.State.Clock with
                        Phase = ClockPhase.SecondHalf }
                | _ -> world.State.Clock

            world.State <-
                { world.State with
                    Flow = MatchFlow.RestartDelay(delay, plan)
                    Clock = clock }

            if kind = SetPieceKind.Corner then
                let team = MatchWorld.team world side
                team.Stats.Corners <- team.Stats.Corners + 1
        | EventData.Interception _ -> ()
        | EventData.Tackle _ -> ()
        | EventData.Save _ -> ()
        | EventData.CrossAttempt(crosser, _, _) ->
            match MatchWorld.teamOfPlayer ctx crosser with
            | Some side ->
                let team = MatchWorld.team world side
                team.Stats.Crosses <- team.Stats.Crosses + 1
            | None -> ()
        | EventData.Dribble(dribbler, succeeded, _, _) ->
            match MatchWorld.teamOfPlayer ctx dribbler with
            | Some side ->
                let team = MatchWorld.team world side

                if succeeded then
                    team.Stats.Dribbles <- team.Stats.Dribbles + 1

                match MatchWorld.slotOf ctx side dribbler with
                | Some slot ->
                    if succeeded then
                        team.Memory.[slot].SuccessfulDuels <- team.Memory.[slot].SuccessfulDuels + 1
                    else
                        team.Memory.[slot].FailedDuels <- team.Memory.[slot].FailedDuels + 1
                | None -> ()
            | None -> ()
        | EventData.Injury(player, severity) ->
            if severity >= 3 then
                let dur =
                    int (
                        round (
                            float ctx.Config.Match.InjuryPauseDuration
                            * float SimulationClock.defaultClock.SubTicksPerSecond
                        )
                    )
                    * 1<tickDelta>

                world.State <-
                    { world.State with
                        Flow = MatchFlow.InjuryPause dur }
        | EventData.Substitution(playerOut, playerIn) ->
            match MatchWorld.teamOfPlayer ctx playerOut with
            | Some side ->
                let team = MatchWorld.team world side

                match MatchWorld.slotOf ctx side playerOut, MatchWorld.slotOf ctx side playerIn with
                | Some outSlot, Some inSlot ->
                    team.Frame.Occupancy.[outSlot] <- OccupancyKind.Active outSlot

                    team.Frame.Occupancy.[inSlot] <-
                        OccupancyKind.Sidelined
                            { Id = playerOut
                              Reason = PlayerOutReason.Substituted }

                    team.Frame.ActiveCache.Revision <- team.Frame.ActiveCache.Revision + 1

                    let mem = team.Memory.[outSlot]

                    team.Memory.[outSlot] <-
                        { team.Memory.[inSlot] with
                            CriticalFlagged = false }

                    team.Memory.[inSlot] <- mem

                    let cond = team.Frame.Condition.[outSlot]
                    team.Frame.Condition.[outSlot] <- 1.0f
                    team.Frame.Condition.[inSlot] <- cond

                    let dir = MatchMath.goalDirection side
                    team.Frame.PosX.[inSlot] <- float32 (MatchMath.ownGoalX side + 4.0<meter> * dir)
                    team.Frame.PosY.[inSlot] <- 20.0f
                    team.SubsUsed <- team.SubsUsed + 1
                | _ -> ()
            | None -> ()
        | EventData.VARDecision(decision) ->
            let scoring = world.State.LastAttackingClub |> Option.defaultValue ClubSide.Home
            let defending = MatchWorld.oppOf scoring

            match decision with
            | VARResult.GoalOverturned ->
                let home =
                    if scoring = ClubSide.Home then
                        max 0 (world.State.HomeScore - 1)
                    else
                        world.State.HomeScore

                let away =
                    if scoring = ClubSide.Away then
                        max 0 (world.State.AwayScore - 1)
                    else
                        world.State.AwayScore

                let delay =
                    Flow.delayFor ctx.Config SimulationClock.defaultClock SetPieceKind.FreeKick

                let dir = MatchMath.goalDirection defending

                let plan =
                    { Kind = SetPieceKind.FreeKick
                      Side = defending
                      X = MatchMath.ownGoalX defending + 12.0<meter> * dir
                      Y = 0.0<meter> }

                placeBall world plan

                world.State <-
                    { world.State with
                        HomeScore = home
                        AwayScore = away
                        Flow = MatchFlow.RestartDelay(delay, plan) }
            | VARResult.GoalStands ->
                let delay =
                    Flow.delayFor ctx.Config SimulationClock.defaultClock SetPieceKind.KickOff

                let plan =
                    { Kind = SetPieceKind.KickOff
                      Side = defending
                      X = 0.0<meter>
                      Y = 0.0<meter> }

                placeBall world plan

                world.State <-
                    { world.State with
                        Flow = MatchFlow.RestartDelay(delay, plan) }
        | EventData.MomentumShift _ -> ()
        | EventData.KickOff _ -> ()
        | EventData.HalfTime ->
            let dur =
                int (
                    round (
                        float ctx.Config.Match.HalfTimeDuration
                        * float SimulationClock.defaultClock.SubTicksPerSecond
                    )
                )
                * 1<tickDelta>

            world.State <-
                { world.State with
                    Flow = MatchFlow.HalfTime dur
                    Clock =
                        { world.State.Clock with
                            Phase = ClockPhase.HalfTime } }
        | EventData.FullTime ->
            let dur =
                int (
                    round (
                        float ctx.Config.Match.FullTimeReviewDuration
                        * float SimulationClock.defaultClock.SubTicksPerSecond
                    )
                )
                * 1<tickDelta>

            world.State <-
                { world.State with
                    Flow = MatchFlow.FullTimeReview dur
                    Clock =
                        { world.State.Clock with
                            Phase = ClockPhase.FullTime } }
        | EventData.ConditionCritical _ -> ()
        | EventData.RedCardIssued(player) ->
            match MatchWorld.teamOfPlayer ctx player with
            | Some side ->
                let team = MatchWorld.team world side

                match MatchWorld.slotOf ctx side player with
                | Some slot ->
                    team.Frame.Occupancy.[slot] <-
                        OccupancyKind.Sidelined
                            { Id = player
                              Reason = PlayerOutReason.RedCard }

                    team.Frame.ActiveCache.Revision <- team.Frame.ActiveCache.Revision + 1

                    let mutable frame = team.Frame
                    frame.ActiveCount <- max 0 (frame.ActiveCount - 1)
                    team.Frame <- frame

                    let mutable cards = team.Cards
                    cards.Reds <- cards.Reds + 1
                    team.Cards <- cards
                | None -> ()
            | None -> ()
        | EventData.ShootoutGoal _ -> ()
