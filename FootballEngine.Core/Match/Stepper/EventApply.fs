namespace FootballEngine.Core.Match

/// The structural reducer. Producers observe physics or rules and append events;
/// this module is the only production writer for score, possession books,
/// discipline, active slots, restarts and match statistics.
module EventApply =

    let private restartSpot (kind: SetPieceKind) (side: ClubSide) (ball: BallState) =
        MatchMath.restartSpot kind side ball.Position.X ball.Position.Y

    let private isStoppage flow =
        match flow with
        | MatchFlow.PreMatch
        | MatchFlow.Live
        | MatchFlow.VARReview _
        | MatchFlow.Shootout
        | MatchFlow.MatchEnded -> false
        | _ -> true

    let private subState phase outgoing incoming tick applied used =
        { Phase = phase
          Outgoing = outgoing
          Incoming = incoming
          RequestedAt = tick
          AppliedAt = applied
          UsedIncoming = used }

    let private emit (buffer: EventBuffer option) event =
        match buffer with
        | Some b -> b.Events.Add event
        | None -> ()

    let private finishTransition (world: World) buffer side tick outcome =
        let team = MatchWorld.team world side
        match team.Defense.Episode with
        | Some episode when episode.EndedAt.IsNone ->
            team.Defense.Episode <- Some { episode with EndedAt = Some tick; Outcome = Some outcome }
            DefensiveTransitionState.clearTasks team.Frame team.Defense
            let cause =
                match world.Observation.CurrentEvent with
                | ValueSome(EventCause trigger) -> ValueSome(PairedEvents(episode.Id, trigger))
                | _ -> ValueSome(EventCause episode.Id)
            let evidence = world.Observation.Record(tick, cause, TransitionEnded(side, episode.Id, outcome, episode.Reason))
            let ev = { Tick = tick; Data = EventData.DefensiveTransition(side, episode.Id, Finished outcome) }
            evidence |> ValueOption.iter (fun id -> world.Observation.Associate(ev, ResolutionCause id))
            emit buffer ev
        | _ -> DefensiveTransitionState.clearTasks team.Frame team.Defense

    let private releaseSlot (team: TeamWorld) slot =
        team.Frame.DefensiveRoles[slot] <- DefensiveRole.Unassigned
        team.Frame.RoleBias[slot] <- 1.0f
        team.Frame.Intent.Kind[slot] <- IntentCode.None
        team.Frame.Intent.Phase[slot] <- IntentPhase.Completed
        team.Frame.ContactActions[slot] <- None
        team.Frame.ContactState[slot] <-
            { BodyContactState.stable with
                Support = BodySupport.NoBodySupport }
        team.Defense.Tasks[slot] <- None
        team.Frame.RunLookup.Remove slot |> ignore
        team.PendingActions.Remove slot |> ignore

    let private teamForPair (ctx: MatchContext) outgoing incoming =
        match MatchWorld.teamOfPlayer ctx outgoing, MatchWorld.teamOfPlayer ctx incoming with
        | Some side, Some incomingSide when side = incomingSide -> Some side
        | _ -> None

    let private availableSubstitute (lineup: Lineup) (team: TeamWorld) (slot: int) (player: PlayerId) =
        // Slots after the starting eleven are the immutable bench boundary in a
        // MatchContext; an outgoing starter must never become selectable again.
        slot >= lineup.StarterCount
        && not (Set.contains player team.Substitution.UsedIncoming)
        && match team.Frame.Occupancy.[slot] with
           | OccupancyKind.Sidelined { Reason = PlayerOutReason.Substituted } -> true
           | _ -> false

    let private validSubstitution (ctx: MatchContext) (world: World) outgoing incoming =
        match teamForPair ctx outgoing incoming with
        | None -> None
        | Some side ->
            let team = MatchWorld.team world side

            match MatchWorld.slotOf ctx side outgoing, MatchWorld.slotOf ctx side incoming with
            | Some outSlot, Some inSlot when outSlot <> inSlot ->
                match team.Frame.Occupancy.[outSlot] with
                | OccupancyKind.Active _ when
                    availableSubstitute (MatchWorld.lineup ctx side) team inSlot incoming
                    && team.SubsUsed < ctx.SubstitutionRules.MaxSubstitutions
                    ->
                    Some(side, outSlot, inSlot)
                | _ -> None
            | _ -> None

    let private applySubstitutionEffect
        (ctx: MatchContext)
        (world: World)
        (tick: int<subtick>)
        outgoing
        incoming
        : bool =
        match validSubstitution ctx world outgoing incoming with
        | None -> false
        | Some(side, outSlot, inSlot) ->
            let team = MatchWorld.team world side
            let outX = team.Frame.PosX.[outSlot]
            let outY = team.Frame.PosY.[outSlot]

            team.Frame.Occupancy.[outSlot] <-
                OccupancyKind.Sidelined
                    { Id = outgoing
                      Reason = PlayerOutReason.Substituted }

            team.Frame.Occupancy.[inSlot] <- OccupancyKind.Active inSlot
            releaseSlot team outSlot
            releaseSlot team inSlot
            team.Frame.PosX.[inSlot] <- outX
            team.Frame.PosY.[inSlot] <- outY
            team.Frame.VelX.[inSlot] <- 0.0f
            team.Frame.VelY.[inSlot] <- 0.0f
            team.Frame.PosZ.[inSlot] <- 0.0f
            team.Frame.VelZ.[inSlot] <- 0.0f
            team.Frame.JumpRequested.[inSlot] <- false
            team.Frame.JumpSequence.[inSlot] <- 0UL
            team.Frame.BodyFacing.[inSlot] <- if side = ClubSide.Home then 0.0f else System.MathF.PI
            team.Frame.BodyAngularVelocity.[inSlot] <- 0.0f
            team.Frame.BodyAngularAcceleration.[inSlot] <- 0.0f
            team.Frame.BodyTravelDistance.[inSlot] <- 0.0f
            team.Frame.GaitPhase.[inSlot] <- 0.0f
            team.Frame.ContactState.[inSlot] <- BodyContactState.stable
            team.Frame.ActiveCache.Revision <- team.Frame.ActiveCache.Revision + 1
            team.SubsUsed <- team.SubsUsed + 1
            ParticipationBook.stop team.Participation outgoing tick
            ParticipationBook.start team.Participation incoming team.Frame.SlotRoles.[inSlot] tick
            team.Frame.ContactGeneration.[inSlot] <- ParticipationBook.generation team.Participation incoming

            let old = team.Substitution

            team.Substitution <-
                subState
                    SubstitutionPhase.Applied
                    (Some outgoing)
                    (Some incoming)
                    (Some tick)
                    (Some tick)
                    (Set.add incoming old.UsedIncoming)

            true

    let private queuePending (world: World) (buffer: EventBuffer option) (tick: int<subtick>) =
        match buffer with
        | None -> ()
        | Some b when isStoppage world.State.Flow ->
            for side in [ ClubSide.Home; ClubSide.Away ] do
                let state = (MatchWorld.team world side).Substitution

                match state.Phase, state.Outgoing, state.Incoming with
                | SubstitutionPhase.Pending, Some outgoing, Some incoming ->
                    let alreadyQueued =
                        b.Events
                        |> Seq.exists (fun ev -> ev.Data = EventData.SubstitutionApplied(outgoing, incoming))

                    if not alreadyQueued then
                        b.Events.Add
                            { Tick = tick
                              Data = EventData.SubstitutionApplied(outgoing, incoming) }
                | _ -> ()
        | Some _ -> ()

    let private restartCause (world: World) kind =
        match kind with
        | SetPieceKind.KickOff ->
            match world.State.Flow with
            | MatchFlow.PreMatch -> RestartCause.MatchStart
            | MatchFlow.GoalPause(_, scoringSide, _) -> RestartCause.GoalConceded scoringSide
            | _ -> RestartCause.PeriodStart
        | SetPieceKind.ThrowIn -> RestartCause.BallCrossedTouchline world.State.Ball.LastTouch
        | SetPieceKind.Corner
        | SetPieceKind.GoalKick -> RestartCause.BallCrossedGoalLine world.State.Ball.LastTouch
        | SetPieceKind.FreeKick
        | SetPieceKind.Penalty -> RestartCause.Foul(None, None)

    let private applyRestart (ctx: MatchContext) (world: World) tick cause kind side x y =
        world.Home.PendingActions.Clear()
        world.Away.PendingActions.Clear()
        let x, y = MatchMath.restartSpot kind side x y

        let id = RestartId world.NextRestartId
        world.NextRestartId <- world.NextRestartId + 1UL

        let award =
            { Id = id
              Cause = cause
              Kind = kind
              Side = side
              X = x
              Y = y }

        if world.Observation.Enabled then
            let evidenceCause =
                match world.State.Advantage, cause, world.Observation.CurrentEvent with
                | AdvantageState.AdvantageRecalled(incident, at), RestartCause.Foul _, ValueSome(EventCause trigger)
                | AdvantageState.AdvantageExpired(incident, at), RestartCause.Foul _, ValueSome(EventCause trigger) when
                    at = tick
                    ->
                    ValueSome(PairedEvents(incident.Source, trigger))
                | _ -> world.Observation.CurrentEvent

            world.Observation.Record(tick, evidenceCause, RestartCreated award) |> ignore

        MatchWorld.placeDeadBall world { X = x; Y = y }

        world.State <-
            { world.State with
                Flow =
                    MatchFlow.Restart
                        { Plan = None
                          Id = id
                          Cause = cause
                          Kind = kind
                          Side = side
                          Spot = { X = x; Y = y }
                          AwardTick = tick
                          Phase = RestartPhase.Awarded
                          CommandIssued = false
                          ContactObserved = false
                          Replanned = false } }

        award

    let private applyPossession (ctx: MatchContext) (world: World) buffer side pid tick =
        let previousOwner =
            match world.State.Ball.Control with
            | BallControl.Controlled(owner, player)
            | BallControl.Receiving(owner, player, _) -> Some(owner, player)
            | _ -> None

        if previousOwner <> Some(side, pid) then
            world.Home.PendingActions.Clear()
            world.Away.PendingActions.Clear()

        let pendingAdvantage =
            match world.State.Advantage with
            | AdvantageState.AdvantagePending(incident, _) -> Some incident
            | _ -> None

        let previous = world.State.Possession.LastGainer
        if world.State.Flow = MatchFlow.Live && previous <> Some side then
            finishTransition world buffer side tick TransitionEnd.Recovered
            match previous with
            | Some losingSide ->
                let losingTeam = MatchWorld.team world losingSide
                let source = world.Observation.CurrentEvent
                let id =
                    match source with
                    | ValueSome(EventCause id) -> id
                    | _ -> MatchEvent.id { Tick = tick; Data = EventData.Possession(side, pid) }
                DefensiveTransitionState.clearTasks losingTeam.Frame losingTeam.Defense
                losingTeam.Defense.ControlChangedAt <- Some tick
                (MatchWorld.team world side).Defense.ControlChangedAt <- Some tick
                losingTeam.Frame.RunLookup.Clear()
                losingTeam.Defense.Episode <-
                    Some
                        { Id = id; LostAt = tick
                          Origin = { X = world.State.Ball.Position.X; Y = world.State.Ball.Position.Y }
                          Mode = Assessing; Reason = AwaitingAssessment; ModeSince = tick; CommitUntil = tick
                          Primary = None; Cover = None; EvaluatedAt = tick
                          Tactics = losingTeam.Tactics; ParticipationRevision = losingTeam.Frame.ActiveCache.Revision
                          StabilizingSince = None; EndedAt = None; Outcome = None }
                let ev = { Tick = tick; Data = EventData.DefensiveTransition(losingSide, id, Lost) }
                world.Observation.Associate(ev, EventCause id)
                emit buffer ev
            | None -> ()

        MatchWorld.updateBall world (fun b ->
            { b with
                Control = BallControl.Controlled(side, pid)
                LastTouch = Some pid
                StationarySince = None })

        let book = world.State.Possession

        world.State <-
            { world.State with
                Possession =
                    { HomeTouches =
                        if side = ClubSide.Home then
                            book.HomeTouches + 1
                        else
                            book.HomeTouches
                      AwayTouches =
                        if side = ClubSide.Away then
                            book.AwayTouches + 1
                        else
                            book.AwayTouches
                      LastGainer = Some side }
                LastAttackingClub = Some side }

        let team = MatchWorld.team world side
        team.Stats.Touches <- team.Stats.Touches + 1

        match pendingAdvantage with
        | Some incident when side = incident.Side ->
            world.State <-
                { world.State with
                    Advantage = AdvantageState.AdvantageRealized(incident, tick) }
        | Some incident ->
            let fouledSide, x, y = incident.Side, incident.X, incident.Y

            world.State <-
                { world.State with
                    Advantage = AdvantageState.AdvantageRecalled(incident, tick) }

            let kind =
                if Pitch.isInPenaltyArea fouledSide x y then
                    SetPieceKind.Penalty
                else
                    SetPieceKind.FreeKick

            applyRestart
                ctx
                world
                tick
                (RestartCause.Foul(Some incident.Fouler, Some incident.Fouled))
                kind
                fouledSide
                x
                y
            |> ignore
        | None -> ()

    let private applyPhysicalContact (ctx: MatchContext) (world: World) (contact: ContactRecord) =
        match MatchWorld.teamOfPlayer ctx contact.Actor with
        | Some side ->
            match MatchWorld.slotOf ctx side contact.Actor with
            | Some slot ->
                let frame = (MatchWorld.team world side).Frame

                if
                    contact.BallTouched
                    && MatchWorld.isActiveSlot frame.Occupancy[slot]
                    && frame.ContactGeneration[slot] = contact.ActorGeneration
                then
                    let acquiredSide, acquiredPlayer =
                        match contact.ControlAfter.Kind, contact.ControlAfter.Side, contact.ControlAfter.Player with
                        | ContactControlKind.ReceivingBall, Some ownerSide, Some player when
                            ownerSide = side
                            && player = contact.Actor
                            && world.State.Flow = MatchFlow.Live
                            ->
                            Some ownerSide, Some player
                        | _ -> None, None

                    MatchWorld.updateBall world (fun ball ->
                        { ball with
                            LastTouch = Some contact.Actor
                            Control =
                                match acquiredSide, acquiredPlayer with
                                | Some ownerSide, Some player -> BallControl.Receiving(ownerSide, player, contact.Tick)
                                | _ -> BallControl.Free
                            GKHoldSince = None
                            StationarySince = None })
            | None -> ()
        | None -> ()

    let private applyGoal (ctx: MatchContext) (world: World) side scorer xG tick buffer =
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

        let reviewable =
            match scorer with
            | Some player when RefereeVAR.isOffsideFor ctx world side player -> true
            | _ -> false

        let nextReview =
            if reviewable then
                Some
                    { Incident = VARIncident.Offside
                      ScoringSide = side
                      Scorer = scorer }
            else
                None

        world.State <-
            { world.State with
                HomeScore = home
                AwayScore = away
                LastAttackingClub = Some side
                VARReview = nextReview }

        if reviewable then
            let duration =
                Flow.delayFor ctx.Config SimulationClock.defaultClock SetPieceKind.FreeKick

            let reviewDuration =
                int (
                    round (
                        float ctx.Config.Match.VarReviewDuration
                        * float SimulationClock.defaultClock.SubTicksPerSecond
                    )
                )
                * 1<tickDelta>

            let _ = duration

            world.State <-
                { world.State with
                    Flow = MatchFlow.VARReview(reviewDuration, VARIncident.Offside) }
        else
            RefereeVAR.clearSnapshots world side

            let pause =
                int (
                    round (
                        float ctx.Config.Match.GoalPauseDuration
                        * float SimulationClock.defaultClock.SubTicksPerSecond
                    )
                )
                * 1<tickDelta>

            world.State <-
                { world.State with
                    Flow = MatchFlow.GoalPause(pause, side, scorer) }

    let private applyVarDecision (ctx: MatchContext) (world: World) decision =
        let review = world.State.VARReview

        let inReview =
            match world.State.Flow, review with
            | MatchFlow.VARReview _, Some _ -> true
            | MatchFlow.VARReview _, None -> true
            | _ -> false

        if inReview then
            let scoring =
                review
                |> Option.map (fun r -> r.ScoringSide)
                |> Option.orElse world.State.LastAttackingClub
                |> Option.defaultValue ClubSide.Home

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

                world.State <-
                    { world.State with
                        HomeScore = home
                        AwayScore = away
                        LastAttackingClub = None
                        VARReview = None }

                applyRestart
                    ctx
                    world
                    world.State.Tick
                    (RestartCause.VAROverturn VARIncident.Offside)
                    SetPieceKind.FreeKick
                    defending
                    world.State.Ball.Position.X
                    world.State.Ball.Position.Y
                |> ignore
            | VARResult.GoalStands ->
                world.State <-
                    { world.State with
                        LastAttackingClub = None
                        VARReview = None }

                applyRestart
                    ctx
                    world
                    world.State.Tick
                    (RestartCause.PeriodStart)
                    SetPieceKind.KickOff
                    defending
                    0.0<meter>
                    0.0<meter>
                |> ignore

            RefereeVAR.clearSnapshots world scoring

    let private applyFullTime (ctx: MatchContext) (world: World) =
        let tied = world.State.HomeScore = world.State.AwayScore

        match ctx.TieResolution, world.State.Clock.Phase, tied with
        | MatchTieResolution.ExtraTimeThenPenalties, ClockPhase.SecondHalf, true ->
            world.State <-
                { world.State with
                    Flow =
                        MatchFlow.ExtraTimeBreak(
                            Flow.delayFor ctx.Config SimulationClock.defaultClock SetPieceKind.KickOff
                        )
                    Clock =
                        { world.State.Clock with
                            Phase = ClockPhase.ExtraTimeFirstHalf } }
        | MatchTieResolution.ExtraTimeThenPenalties, ClockPhase.ExtraTimeSecondHalf, true
        | MatchTieResolution.PenaltiesAfterRegulation, ClockPhase.SecondHalf, true ->
            world.State <-
                { world.State with
                    Flow = MatchFlow.Shootout
                    Shootout =
                        { world.State.Shootout with
                            Scheduled = false } }
        | _ ->
            let duration =
                int (
                    round (
                        float ctx.Config.Match.FullTimeReviewDuration
                        * float SimulationClock.defaultClock.SubTicksPerSecond
                    )
                )
                * 1<tickDelta>

            world.State <-
                { world.State with
                    Flow = MatchFlow.FullTimeReview duration
                    Clock =
                        { world.State.Clock with
                            Phase = ClockPhase.FullTime } }

    let private applyEvent (ctx: MatchContext) (world: World) (buffer: EventBuffer option) (ev: MatchEvent) : bool =
        let occurrence =
            buffer
            |> Option.map (fun b -> b.Applied |> Seq.filter ((=) ev) |> Seq.length)
            |> Option.defaultValue 0

        let eventId =
            if occurrence = 0 then
                MatchEvent.id ev
            else
                let baseId = MatchEvent.id ev |> EventId.value
                EventId((baseId * 1099511628211UL) ^^^ uint64 occurrence)

        if not (world.AppliedEventIds.Add eventId) then
            false
        else
            let tick = ev.Tick
            let attackBuffer = buffer |> Option.defaultWith (fun () -> { Events = ResizeArray(); Flags = { Bits = 0UL }; Applied = ResizeArray() })
            AttackLifecycle.observe ctx world attackBuffer ev

            match ev.Data with
            | EventData.Possession(side, pid) -> applyPossession ctx world buffer side pid tick
            | EventData.DefensiveTransition _ -> ()
            | EventData.CollectiveAttack _ -> ()
            | EventData.TeamInstruction _ -> ()
            | EventData.PhysicalContact contact -> applyPhysicalContact ctx world contact
            | EventData.BallLoose(x, y) ->
                world.Home.PendingActions.Clear()
                world.Away.PendingActions.Clear()
                let side = world.State.Possession.LastGainer |> Option.defaultValue ClubSide.Home

                MatchWorld.updateBall world (fun b ->
                    { b with
                        Control = BallControl.Contesting side
                        Trajectory = None
                        ArrivalGate = None
                        StationarySince = Some tick
                        Impulse = None
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
                        | ShotResult.OnTarget false ->
                            team.Memory.[slot].ShotSuccessMod <- max -2.0 (team.Memory.[slot].ShotSuccessMod - 0.2)
                        | _ -> ()
                    | None -> ()
                | None -> ()
            | EventData.Goal(side, scorer, _, xG) -> applyGoal ctx world side scorer xG tick buffer
            | EventData.Foul(fouler, _, _, _, _) ->
                match MatchWorld.teamOfPlayer ctx fouler with
                | Some side ->
                    let team = MatchWorld.team world side
                    team.Stats.Fouls <- team.Stats.Fouls + 1
                | None -> ()
            | EventData.Card(player, CardColor.Yellow) ->
                match MatchWorld.teamOfPlayer ctx player, MatchWorld.teamOfPlayer ctx player with
                | Some side, Some _ ->
                    let team = MatchWorld.team world side

                    match MatchWorld.slotOf ctx side player with
                    | Some slot ->
                        team.Memory.[slot].Yellows <- team.Memory.[slot].Yellows + 1
                        team.Cards.Yellows <- team.Cards.Yellows + 1
                    | None -> ()
                | _ -> ()
            | EventData.Card(_, CardColor.Red) ->
                // RefereeCards emits RedCardIssued; red totals and dismissal are
                // deliberately committed by that structural effect only.
                ()
            | EventData.Offside player ->
                match MatchWorld.teamOfPlayer ctx player with
                | Some side ->
                    let team = MatchWorld.team world side
                    team.Stats.Offsides <- team.Stats.Offsides + 1
                    let defending = MatchWorld.oppOf side
                    let x, y = restartSpot SetPieceKind.FreeKick defending world.State.Ball

                    applyRestart ctx world tick (RestartCause.Offside player) SetPieceKind.FreeKick defending x y
                    |> ignore
                | None -> ()
            | EventData.SetPiece(kind, side) ->
                let x, y = restartSpot kind side world.State.Ball

                let clock =
                    match kind, world.State.Clock.Phase with
                    | SetPieceKind.KickOff, ClockPhase.HalfTime ->
                        { world.State.Clock with
                            Phase = ClockPhase.SecondHalf }
                    | _ -> world.State.Clock

                world.State <- { world.State with Clock = clock }
                applyRestart ctx world tick (restartCause world kind) kind side x y |> ignore

                if kind = SetPieceKind.Corner then
                    let team = MatchWorld.team world side
                    team.Stats.Corners <- team.Stats.Corners + 1

                queuePending world buffer tick
            | EventData.SetPieceAt(kind, side, x, y) ->
                applyRestart ctx world tick (restartCause world kind) kind side x y |> ignore
                queuePending world buffer tick
            | EventData.RestartPlanned plan ->
                // A plan for a restart that was since replaced or already planned is stale cognition output.
                match world.State.Flow with
                | MatchFlow.Restart restart when restart.Id = plan.Id && restart.Phase = RestartPhase.Awarded ->
                    world.State <-
                        { world.State with
                            Flow =
                                MatchFlow.Restart
                                    { restart with
                                        Plan = Some plan
                                        Phase = RestartPhase.Positioning } }
                | _ -> ()
            | EventData.RestartPlanChanged(id, RestartPlanChangeReason.PlanningFailed) ->
                // No legal plan exists, so play resumes rather than waiting forever on a restart nobody can take.
                match world.State.Flow with
                | MatchFlow.Restart restart when restart.Id = id && restart.Phase = RestartPhase.Awarded ->
                    world.State <-
                        { world.State with
                            Flow =
                                MatchFlow.Restart
                                    { restart with
                                        Phase = RestartPhase.Completed } }
                | _ -> ()
            | EventData.RestartAwarded _
            | EventData.RestartAuthorized _
            | EventData.RestartCommandIssued _
            | EventData.RestartContact _
            | EventData.RestartCompleted _
            | EventData.RestartPlanChanged _ -> ()
            | EventData.Interception _ -> ()
            // Body physics already committed the motion; applying its notification must not integrate it twice.
            | EventData.PlayerBodyFlight _ -> ()
            | EventData.Tackle _ -> ()
            | EventData.Save _ -> ()
            | EventData.CrossAttempt(crosser, _, _) ->
                match MatchWorld.teamOfPlayer ctx crosser with
                | Some side ->
                    (MatchWorld.team world side).Stats.Crosses <- (MatchWorld.team world side).Stats.Crosses + 1
                | None -> ()
            | EventData.Dribble(dribbler, succeeded, _, _) ->
                match MatchWorld.teamOfPlayer ctx dribbler with
                | Some side ->
                    let team = MatchWorld.team world side

                    if succeeded then
                        team.Stats.Dribbles <- team.Stats.Dribbles + 1

                    match MatchWorld.slotOf ctx side dribbler with
                    | Some slot when succeeded ->
                        team.Memory.[slot].SuccessfulDuels <- team.Memory.[slot].SuccessfulDuels + 1
                    | Some slot -> team.Memory.[slot].FailedDuels <- team.Memory.[slot].FailedDuels + 1
                    | None -> ()
                | None -> ()
            | EventData.Injury(player, severity) ->
                match MatchWorld.teamOfPlayer ctx player with
                | Some side ->
                    let team = MatchWorld.team world side

                    match MatchWorld.slotOf ctx side player with
                    | Some slot ->
                        match team.Frame.Occupancy.[slot] with
                        | OccupancyKind.Active _ ->
                            team.Frame.Occupancy.[slot] <-
                                OccupancyKind.Sidelined
                                    { Id = player
                                      Reason = PlayerOutReason.Injury }

                            team.Frame.ActiveCache.Revision <- team.Frame.ActiveCache.Revision + 1
                            releaseSlot team slot
                            let mutable frame = team.Frame
                            frame.ActiveCount <- max 0 (frame.ActiveCount - 1)
                            team.Frame <- frame
                            ParticipationBook.stop team.Participation player tick
                        | _ -> ()
                    | None -> ()
                | None -> ()

                if severity >= 3 then
                    let duration =
                        int (
                            round (
                                float ctx.Config.Match.InjuryPauseDuration
                                * float SimulationClock.defaultClock.SubTicksPerSecond
                            )
                        )
                        * 1<tickDelta>

                    world.State <-
                        { world.State with
                            Flow = MatchFlow.InjuryPause duration }
            | EventData.Substitution(outgoing, incoming) ->
                if not (applySubstitutionEffect ctx world tick outgoing incoming) then
                    let side = teamForPair ctx outgoing incoming

                    match side with
                    | Some s ->
                        let team = MatchWorld.team world s

                        team.Substitution <-
                            subState
                                SubstitutionPhase.Rejected
                                (Some outgoing)
                                (Some incoming)
                                (Some tick)
                                None
                                team.Substitution.UsedIncoming
                    | None -> ()
            | EventData.SubstitutionRequested(outgoing, incoming) ->
                match validSubstitution ctx world outgoing incoming with
                | Some _ ->
                    match teamForPair ctx outgoing incoming with
                    | Some side ->
                        let team = MatchWorld.team world side
                        let current = team.Substitution

                        if
                            current.Outgoing <> Some outgoing
                            || current.Incoming <> Some incoming
                            || (current.Phase <> SubstitutionPhase.Requested
                                && current.Phase <> SubstitutionPhase.Accepted
                                && current.Phase <> SubstitutionPhase.Pending)
                        then
                            team.Substitution <-
                                subState
                                    SubstitutionPhase.Requested
                                    (Some outgoing)
                                    (Some incoming)
                                    (Some tick)
                                    None
                                    current.UsedIncoming

                            emit
                                buffer
                                { Tick = tick
                                  Data = EventData.SubstitutionAccepted(outgoing, incoming) }
                    | None -> ()
                | None ->
                    match teamForPair ctx outgoing incoming with
                    | Some side ->
                        let team = MatchWorld.team world side

                        team.Substitution <-
                            subState
                                SubstitutionPhase.Rejected
                                (Some outgoing)
                                (Some incoming)
                                (Some tick)
                                None
                                team.Substitution.UsedIncoming

                        emit
                            buffer
                            { Tick = tick
                              Data = EventData.SubstitutionRejected(outgoing, incoming) }
                    | None -> ()
            | EventData.SubstitutionAccepted(outgoing, incoming) ->
                match teamForPair ctx outgoing incoming with
                | Some side ->
                    let team = MatchWorld.team world side
                    let current = team.Substitution

                    if
                        current.Outgoing = Some outgoing
                        && current.Incoming = Some incoming
                        && current.Phase = SubstitutionPhase.Requested
                    then
                        team.Substitution <-
                            { current with
                                Phase = SubstitutionPhase.Pending }

                        if isStoppage world.State.Flow then
                            emit
                                buffer
                                { Tick = tick
                                  Data = EventData.SubstitutionApplied(outgoing, incoming) }
                | None -> ()
            | EventData.SubstitutionRejected(outgoing, incoming) ->
                match teamForPair ctx outgoing incoming with
                | Some side ->
                    let team = MatchWorld.team world side
                    let current = team.Substitution

                    if current.Outgoing = Some outgoing && current.Incoming = Some incoming then
                        team.Substitution <-
                            { current with
                                Phase = SubstitutionPhase.Rejected }
                | None -> ()
            | EventData.SubstitutionApplied(outgoing, incoming) ->
                applySubstitutionEffect ctx world tick outgoing incoming |> ignore
            | EventData.VARDecision decision -> applyVarDecision ctx world decision
            | EventData.MomentumShift _ -> ()
            | EventData.KickOff _ -> ()
            | EventData.HalfTime ->
                match world.State.Clock.Phase with
                | ClockPhase.ExtraTimeFirstHalf ->
                    let duration =
                        int (
                            round (
                                float ctx.Config.Match.HalfTimeDuration
                                * float SimulationClock.defaultClock.SubTicksPerSecond
                            )
                        )
                        * 1<tickDelta>

                    world.State <-
                        { world.State with
                            Flow = MatchFlow.ExtraTimeBreak duration
                            Clock =
                                { world.State.Clock with
                                    Phase = ClockPhase.ExtraTimeSecondHalf } }
                | _ ->
                    let duration =
                        int (
                            round (
                                float ctx.Config.Match.HalfTimeDuration
                                * float SimulationClock.defaultClock.SubTicksPerSecond
                            )
                        )
                        * 1<tickDelta>

                    world.State <-
                        { world.State with
                            Flow = MatchFlow.HalfTime duration
                            Clock =
                                { world.State.Clock with
                                    Phase = ClockPhase.HalfTime } }
            | EventData.FullTime -> applyFullTime ctx world
            | EventData.ConditionCritical _ -> ()
            | EventData.RedCardIssued player ->
                match MatchWorld.teamOfPlayer ctx player with
                | Some side ->
                    let team = MatchWorld.team world side

                    match MatchWorld.slotOf ctx side player with
                    | Some slot ->
                        match team.Frame.Occupancy.[slot] with
                        | OccupancyKind.Active _ ->
                            team.Frame.Occupancy.[slot] <-
                                OccupancyKind.Sidelined
                                    { Id = player
                                      Reason = PlayerOutReason.RedCard }

                            team.Frame.ActiveCache.Revision <- team.Frame.ActiveCache.Revision + 1
                            releaseSlot team slot
                            let mutable frame = team.Frame
                            frame.ActiveCount <- max 0 (frame.ActiveCount - 1)
                            team.Frame <- frame
                            ParticipationBook.stop team.Participation player tick
                            team.Cards.Reds <- team.Cards.Reds + 1
                        | _ -> ()
                    | None -> ()
                | None -> ()
            | EventData.ShootoutGoal side ->
                if not world.State.Shootout.Complete then
                    let shootout = world.State.Shootout

                    world.State <-
                        { world.State with
                            Shootout =
                                { shootout with
                                    HomeGoals =
                                        if side = ClubSide.Home then
                                            shootout.HomeGoals + 1
                                        else
                                            shootout.HomeGoals
                                    AwayGoals =
                                        if side = ClubSide.Away then
                                            shootout.AwayGoals + 1
                                        else
                                            shootout.AwayGoals } }
            | EventData.AdvantageStarted(incident, expiresAt) ->
                world.State <-
                    { world.State with
                        Advantage = AdvantageState.AdvantagePending(incident, expiresAt) }
            | EventData.AdvantageRealized side ->
                match world.State.Advantage with
                | AdvantageState.AdvantagePending(incident, _) when incident.Side = side ->
                    world.State <-
                        { world.State with
                            Advantage = AdvantageState.AdvantageRealized(incident, tick) }
                | _ -> ()
            | EventData.AdvantageRecalled side ->
                match world.State.Advantage with
                | AdvantageState.AdvantagePending(incident, _) when incident.Side = side ->
                    let x, y = incident.X, incident.Y

                    world.State <-
                        { world.State with
                            Advantage = AdvantageState.AdvantageRecalled(incident, tick) }

                    let kind =
                        if Pitch.isInPenaltyArea side x y then
                            SetPieceKind.Penalty
                        else
                            SetPieceKind.FreeKick

                    applyRestart
                        ctx
                        world
                        tick
                        (RestartCause.Foul(Some incident.Fouler, Some incident.Fouled))
                        kind
                        side
                        x
                        y
                    |> ignore
                | _ -> ()
            | EventData.AdvantageExpired side ->
                match world.State.Advantage with
                | AdvantageState.AdvantagePending(incident, _) when incident.Side = side ->
                    let x, y = incident.X, incident.Y

                    world.State <-
                        { world.State with
                            Advantage = AdvantageState.AdvantageExpired(incident, tick) }

                    let kind =
                        if Pitch.isInPenaltyArea side x y then
                            SetPieceKind.Penalty
                        else
                            SetPieceKind.FreeKick

                    applyRestart
                        ctx
                        world
                        tick
                        (RestartCause.Foul(Some incident.Fouler, Some incident.Fouled))
                        kind
                        side
                        x
                        y
                    |> ignore
                | _ -> ()
            | EventData.ShootoutStarted ->
                world.State <-
                    { world.State with
                        Shootout =
                            { world.State.Shootout with
                                Scheduled = true } }
            | EventData.ShootoutKick(side, taker, scored) ->
                let valid =
                    match MatchWorld.slotOf ctx side taker with
                    | Some slot -> MatchWorld.isActive (MatchWorld.team world side).Frame slot
                    | None -> false

                if valid then
                    let shootout = world.State.Shootout

                    world.State <-
                        { world.State with
                            Shootout =
                                { shootout with
                                    HomeGoals =
                                        if side = ClubSide.Home && scored then
                                            shootout.HomeGoals + 1
                                        else
                                            shootout.HomeGoals
                                    AwayGoals =
                                        if side = ClubSide.Away && scored then
                                            shootout.AwayGoals + 1
                                        else
                                            shootout.AwayGoals
                                    HomeKicks =
                                        if side = ClubSide.Home then
                                            shootout.HomeKicks + 1
                                        else
                                            shootout.HomeKicks
                                    AwayKicks =
                                        if side = ClubSide.Away then
                                            shootout.AwayKicks + 1
                                        else
                                            shootout.AwayKicks } }
            | EventData.ShootoutFinished ->
                world.State <-
                    { world.State with
                        Flow = MatchFlow.MatchEnded
                        Shootout =
                            { world.State.Shootout with
                                Complete = true } }

            // Contact systems own momentum; the reducer must not apply the same impulse twice.
            | EventData.FootContact _
            | EventData.FootStrikeCancelled _ -> ()
            // Physics already acted (rebound, gloves placed); these events exist for the viewer and the match story.
            | EventData.Woodwork _
            | EventData.KeeperDive _ -> ()

            if world.State.Flow <> MatchFlow.Live then
                // An interrupted advantage cannot later overwrite halftime, a review, or an already-awarded restart.
                match world.State.Advantage with
                | AdvantageState.AdvantagePending _ -> world.State <- {world.State with Advantage = AdvantageState.NoAdvantage}
                | _ -> ()
                for side in [ ClubSide.Home; ClubSide.Away ] do
                    let attack = (MatchWorld.team world side).Attack
                    match attack.Episode with
                    | Some _ -> AttackLifecycle.finish ctx world attackBuffer side AttackEnd.Stopped (Some(MatchEvent.id ev))
                    | _ -> ()
                finishTransition world buffer ClubSide.Home tick TransitionEnd.Interrupted
                finishTransition world buffer ClubSide.Away tick TransitionEnd.Interrupted
                // The first live control after a restart is acquisition, not an inferred loss of dead-ball possession.
                world.State <- { world.State with Possession = { world.State.Possession with LastGainer = None } }
                world.Home.Defense.ControlChangedAt <- None
                world.Away.Defense.ControlChangedAt <- None

            true

    let private applyObserved ctx world buffer ev =
        world.Observation.BeginEvent ev

        try
            applyEvent ctx world buffer ev
        finally
            world.Observation.EndEvent()

    let apply (ctx: MatchContext) (world: World) (ev: MatchEvent) : unit =
        applyObserved ctx world None ev |> ignore

    let applyBuffered (ctx: MatchContext) (world: World) (buffer: EventBuffer) (ev: MatchEvent) : bool =
        applyObserved ctx world (Some buffer) ev
