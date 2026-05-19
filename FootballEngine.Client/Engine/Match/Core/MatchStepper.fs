namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Player
open FootballEngine.Player.Actions
open FootballEngine.Player.Perception
open FootballEngine.TeamOrchestrator
open FootballEngine.Types
open FootballEngine.Types.IntentPhaseTypes
open SimStateOps

[<Struct>]
type StepResult =
    { State: SimState
      Events: ResizeArray<MatchEvent> }

module MatchStepper =

    let private bothSides = [| HomeClub; AwayClub |]

    let private append_event (events: ResizeArray<MatchEvent>) (e: MatchEvent) =
        events.Add e

    let private setPieceDelay (clock: SimulationClock) (config: BalanceConfig) (kind: SetPieceKind) =
        match kind with
        | SetPieceKind.KickOff -> TickDelay.delayFrom clock config.Timing.KickOffDelay
        | SetPieceKind.ThrowIn -> TickDelay.delayFrom clock config.Timing.ThrowInDelay
        | SetPieceKind.Corner -> TickDelay.delayFrom clock config.Timing.CornerDelay
        | SetPieceKind.GoalKick -> TickDelay.delayFrom clock config.Timing.GoalKickDelay
        | SetPieceKind.FreeKick -> TickDelay.delayFrom clock config.Timing.FreeKickDelay
        | SetPieceKind.Penalty -> TickDelay.delayFrom clock config.Timing.FreeKickDelay

    let private restartFromSetPiece
        (state: SimState)
        (clock: SimulationClock)
        (kind: SetPieceKind)
        (team: ClubSide)
        (cause: RestartCause)
        =
        RestartDelay
            { Kind = kind
              Team = team
              Cause = cause
              RemainingTicks = setPieceDelay clock state.Config kind }

    let applyDomainEvent (state: SimState) (events: ResizeArray<MatchEvent>) (e: DomainEvent) =
        match e with
        | DomainEvent.BallUpdate b -> state.Ball <- b
        | DomainEvent.BallXSmooth v -> state.BallXSmooth <- v
        | DomainEvent.FlowChange f -> state.Flow <- f
        | DomainEvent.ScoreGoal(club, sid, own) ->
            if club = HomeClub then state.HomeScore <- state.HomeScore + 1
            else state.AwayScore <- state.AwayScore + 1
        | DomainEvent.ScoreGoalAdjust(club, d) ->
            if club = HomeClub then state.HomeScore <- max 0 (state.HomeScore + d)
            else state.AwayScore <- max 0 (state.AwayScore + d)
        | DomainEvent.MomentumDelta d ->
            let prev = state.Momentum
            state.Momentum <- PhysicsContract.clampFloat (state.Momentum + d) -10.0 10.0
            let next = state.Momentum
            if prev > 0.0 && next < -3.0 then
                SimStateOps.emitSemantic (SemanticEvent.MomentumShifted AwayClub) state
            elif prev < 0.0 && next > 3.0 then
                SimStateOps.emitSemantic (SemanticEvent.MomentumShifted HomeClub) state
        | DomainEvent.EmergentUpdate(club, s) -> SimStateOps.setEmergentState state club s
        | DomainEvent.AdaptiveUpdate(club, s) -> SimStateOps.setAdaptiveState state club s
        | DomainEvent.DirectiveUpdate(club, d) -> SimStateOps.setDirective state club d
        | DomainEvent.MemoryWrite(club, idx, w) ->
            match w with
            | MemoryWriteKind.PassFailure -> MatchMemory.recordPassFailure club idx state.MatchMemory
            | MemoryWriteKind.PassSuccess -> MatchMemory.recordSuccess club idx state.MatchMemory
            | MemoryWriteKind.DuelResult(won, opp) ->
                MatchMemory.recordDuel club idx opp (if won then Won else Lost) state.MatchMemory
        | DomainEvent.RegisterRun(club, run) ->
            SimStateOps.setActiveRuns state club (run :: SimStateOps.getActiveRuns state club)
        | DomainEvent.ExpireRun(club, pid) ->
            SimStateOps.setActiveRuns state club
                (SimStateOps.getActiveRuns state club |> List.filter (fun r -> r.PlayerId <> pid))
        | DomainEvent.PossessionHistoryUpdate delta ->
            let h = state.PossessionHistory
            state.PossessionHistory <-
                { h with
                    LastChangeTick =
                        if delta.PossessionChanged then int state.SubTick
                        else h.LastChangeTick
                    LastBallInFlightTick =
                        if delta.BallInFlight then int state.SubTick
                        else h.LastBallInFlightTick
                    LastSetPieceTick =
                        if delta.SetPieceAwarded then int state.SubTick
                        else h.LastSetPieceTick
                    LastBallReceivedTick =
                        match delta.ReceivedByPlayer with
                        | Some _ -> int state.SubTick
                        | None -> h.LastBallReceivedTick
                    ChangedToSide =
                        if delta.PossessionChanged then
                            match state.Ball.Control with
                            | Controlled(side, _)
                            | Receiving(side, _, _) -> Some side
                            | _ -> h.ChangedToSide
                        else h.ChangedToSide }
        | DomainEvent.EmitSemantic s -> SimStateOps.emitSemantic s state
        | DomainEvent.Emit e ->
            events.Add e
            state.MatchEvents.Add e
            if state.MatchEvents.Count > 1024 then
                state.MatchEvents.RemoveRange(0, 512)
        | DomainEvent.StoppageTimeAdd(t, r) -> state.StoppageTime.Add(t, r) |> ignore
        | DomainEvent.SidelinedWrite(c, p, s) ->
            SimStateOps.setSidelined state c (Map.add p s (SimStateOps.getSidelined state c))
        | DomainEvent.YellowsWrite(c, p, n) ->
            SimStateOps.setYellows state c (Map.add p n (SimStateOps.getYellows state c))
        | DomainEvent.LastAttackingClubSet c -> state.LastAttackingClub <- c
        | DomainEvent.MatchStatIncrement(c, f, d) ->
            let current = SimStateOps.getMatchStats state c
            let updated = match f with | PassAttempts -> { current with PassAttempts = current.PassAttempts + d }
            SimStateOps.setMatchStats state c updated

    let private applyDomainEvents (state: SimState) (events: ResizeArray<MatchEvent>) (outputs: DomainEvent[]) =
        for i = 0 to outputs.Length - 1 do
            applyDomainEvent state events outputs[i]

    let private applyVARDecision
        (ctx: MatchContext)
        (clock: SimulationClock)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (review: VARFlowState)
        =
        let decision = VARReview.evaluate state review.Incident

        let varEvents =
            match decision with
            | Overturn -> VARApplicator.applyOverturn (int state.SubTick) review.Incident ctx state
            | CheckComplete -> VARApplicator.applyCheckComplete (int state.SubTick) review.Incident ctx state
            | _ -> [||]

        applyDomainEvents state events varEvents

        match review.Incident with
        | GoalCheck(scoringClub, _, _, _) ->
            let receiving = ClubSide.flip scoringClub
            state.Flow <- restartFromSetPiece state clock SetPieceKind.KickOff receiving AfterVAR
        | PenaltyCheck(team, _, _) ->
            let kind =
                if decision = Overturn then
                    SetPieceKind.FreeKick
                else
                    SetPieceKind.Penalty
            state.Flow <- restartFromSetPiece state clock kind team AfterVAR
        | RedCardCheck _
        | OffsideCheck _ ->
            state.Flow <- restartFromSetPiece state clock SetPieceKind.FreeKick state.AttackingSide AfterVAR

    let private startGoalFlow
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<MatchEvent>)
        =
        let scoringClub = state.AttackingSide
        let scorerId, isOwnGoal = GoalDetector.scorer scoringClub state.Ball ctx state

        let goalEvents = RefereeApplicator.apply (int state.SubTick) (ConfirmGoal(scoringClub, scorerId, isOwnGoal)) ctx state
        applyDomainEvents state events goalEvents

        match VARDetector.detectGoalCheck scoringClub scorerId isOwnGoal state.SubTick with
        | Some incident ->
            applyDomainEvent state events (DomainEvent.StoppageTimeAdd(int state.SubTick, StoppageReason.VARReviewDelay))
            let duration = VARReview.reviewDuration (int state.SubTick)

            state.Flow <-
                (VARReview
                    { Incident = incident
                      Phase = CheckingIncident
                      RemainingTicks = duration
                      TotalTicks = duration })
        | None ->
            state.Flow <-
                (GoalPause
                    { ScoringTeam = scoringClub
                      ScorerId = scorerId
                      IsOwnGoal = isOwnGoal
                      RemainingTicks = TickDelay.delayFrom clock state.Config.Timing.KickOffDelay
                      VARRequested = false })

    let private sideByClubId (ctx: MatchContext) (clubId: ClubId) =
        if clubId = ctx.Home.Id then Some HomeClub
        elif clubId = ctx.Away.Id then Some AwayClub
        else None

    let private pendingSubs (state: SimState) (side: ClubSide) : SubstitutionRequest list =
        if side = HomeClub then
            state.HomePendingSubstitutions
        else
            state.AwayPendingSubstitutions

    let private setPendingSubs (state: SimState) (side: ClubSide) (requests: SubstitutionRequest list) =
        if side = HomeClub then
            state.HomePendingSubstitutions <- requests
        else
            state.AwayPendingSubstitutions <- requests

    let private tryApplySubstitution
        (ctx: MatchContext)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (request: SubstitutionRequest)
        =
        match sideByClubId ctx request.ClubId with
        | None -> ()
        | Some side ->
            let team = getTeam state side
            let frame = team.Frame
            let roster = getRoster ctx side

            match findIdxByPid request.OutPlayerId frame roster with
            | ValueNone -> ()
            | ValueSome outIdx ->
                let squad = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers

                match squad |> Array.tryFind (fun p -> p.Id = request.InPlayerId) with
                | None -> ()
                | Some incoming ->
                    let evs = ManagerAgent.resolve (int state.SubTick) (MakeSubstitution(request.ClubId, outIdx, incoming)) ctx state
                    applyDomainEvents state events evs

    let private flushPendingSubstitutions
        (ctx: MatchContext)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        =
        match state.Flow with
        | Live
        | MatchEnded -> ()
        | _ ->
            for side in bothSides do
                let requests = pendingSubs state side
                let applied = ResizeArray<SubstitutionRequest>()

                for request in requests do
                    let beforeCount = events.Count
                    tryApplySubstitution ctx state events request

                    if events.Count > beforeCount then
                        applied.Add request

                if applied.Count > 0 then
                    let appliedIds =
                        applied
                        |> Seq.map (fun r -> r.CommandId, r.OutPlayerId, r.InPlayerId)
                        |> Set.ofSeq

                    requests
                    |> List.filter (fun r -> not (Set.contains (r.CommandId, r.OutPlayerId, r.InPlayerId) appliedIds))
                    |> setPendingSubs state side

    let private applyCommand
        (ctx: MatchContext)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (command: MatchCommandEnvelope)
        =
        match command.Command with
        | PauseSimulation ->
            if state.Flow = Live then
                state.Flow <-
                    (RestartDelay
                        { Kind = SetPieceKind.KickOff
                          Team = state.AttackingSide
                          Cause = AfterBallOut
                          RemainingTicks = 1<tickDelta> })

        | ResumeSimulation ->
            match state.Flow with
            | RestartDelay r when r.RemainingTicks <= 1<tickDelta> -> state.Flow <- Live
            | _ -> ()

        | ChangeTactics(clubId, tactics) -> setTacticsByClubId clubId ctx state tactics

        | ChangeInstructions(clubId, instructions) ->
            match sideByClubId ctx clubId with
            | Some HomeClub -> state.Home.Instructions <- Some instructions
            | Some AwayClub -> state.Away.Instructions <- Some instructions
            | None -> ()

        | RequestSubstitution(clubId, outPlayerId, inPlayerId) ->
            match sideByClubId ctx clubId with
            | None -> ()
            | Some side ->
                let request =
                    { ClubId = clubId
                      OutPlayerId = outPlayerId
                      InPlayerId = inPlayerId
                      RequestedSubTick = state.SubTick
                      CommandId = Some command.CommandId }

                match state.Flow with
                | Live -> request :: pendingSubs state side |> setPendingSubs state side
                | _ ->
                    let beforeCount = events.Count
                    tryApplySubstitution ctx state events request

                    if events.Count = beforeCount then
                        request :: pendingSubs state side |> setPendingSubs state side

    let private applyCommands
        (ctx: MatchContext)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (commands: MatchCommandEnvelope[])
        =
        MatchCommands.orderForTick (int state.SubTick) commands
        |> Array.iter (applyCommand ctx state events)

    let private runSetPiece
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<MatchEvent>)
        (restart: RestartPlan)
        =
        let result = SetPieceAgent.run restart.Kind restart.Team ctx state clock
        applyDomainEvents state events result

    let private updateFlow
        (ctx: MatchContext)
        (clock: SimulationClock)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        =
        let wasLive = state.Flow = Live

        match state.Flow with
        | GoalPause goal when goal.RemainingTicks > 0<tickDelta> ->
            state.Flow <-
                (GoalPause
                    { goal with
                        RemainingTicks = goal.RemainingTicks - 1<tickDelta> })

        | GoalPause goal ->
            state.Flow <-
                (restartFromSetPiece state clock SetPieceKind.KickOff (ClubSide.flip goal.ScoringTeam) AfterGoal)

        | VARReview review when review.RemainingTicks > 0<tickDelta> ->
            state.Flow <-
                (VARReview
                    { review with
                        RemainingTicks = review.RemainingTicks - 1<tickDelta> })

        | VARReview review -> applyVARDecision ctx clock state events review

        | InjuryPause injury when injury.RemainingTicks > 0<tickDelta> ->
            state.Flow <-
                (InjuryPause
                    { injury with
                        RemainingTicks = injury.RemainingTicks - 1<tickDelta> })

        | InjuryPause _ ->
            state.Flow <- (restartFromSetPiece state clock SetPieceKind.FreeKick state.AttackingSide AfterInjury)

        | RestartDelay restart when restart.RemainingTicks > 0<tickDelta> ->
            let team = getTeam state restart.Team
            if team.SetPiecePositions.IsNone then
                team.SetPiecePositions <- Some(SetPiecePositioning.computePositions ctx state restart.Team)
            state.Flow <-
                (RestartDelay
                    { restart with
                        RemainingTicks = restart.RemainingTicks - 1<tickDelta> })

        | RestartDelay restart ->
            let team = getTeam state restart.Team
            team.SetPiecePositions <- None
            runSetPiece ctx state clock events restart

        | HalfTimePause remaining when remaining > 0<tickDelta> -> state.Flow <- (HalfTimePause(remaining - 1<tickDelta>))

        | HalfTimePause _ ->
            state.Flow <- (restartFromSetPiece state clock SetPieceKind.KickOff AwayClub InitialKickOff)

        | FullTimeReview -> state.Flow <- MatchEnded

        | Live
        | MatchEnded -> ()

        let isNowLive = state.Flow = Live

        if wasLive && not isNowLive then
            suspendDirective state HomeClub
            suspendDirective state AwayClub
        elif not wasLive && isNowLive then
            resumeDirective state HomeClub
            resumeDirective state AwayClub

    let private runLiveSystems
        (ctx: MatchContext)
        (clock: SimulationClock)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (time: MatchTime)
        (semanticEvents: ResizeArray<SemanticEvent>)
        =
        let influenceFreq = WhenFlow([IsLive], EveryN 10<tickDelta>)
        let cognitiveFrameFreq = WhenFlow([IsLive], EveryN 20<tickDelta>)
        let cognitionFreq = WhenFlow([IsLive], OnEventOrEveryN([SemanticEventKind.BallSecured; SemanticEventKind.BallLost; SemanticEventKind.BallLoose], 20<tickDelta>))
        let actionFreq = WhenFlow([IsLive], OnEventOrEveryN([SemanticEventKind.BallSecured], 20<tickDelta>))
        let refereeFreq = OnEvent([SemanticEventKind.BallLoose; SemanticEventKind.FoulOccurred; SemanticEventKind.GoalScored])
        let teamFreq = WhenFlow([IsLive], OnEventOrEveryN([SemanticEventKind.BallSecured; SemanticEventKind.BallLost; SemanticEventKind.BallLoose; SemanticEventKind.SetPieceAwarded], 40<tickDelta>))
        let adaptiveFreq = WhenFlow([IsLive], EveryN 200<tickDelta>)
        let managerFreq = EveryMinute(period = 1<matchMin>, offset = 0<matchMin>)

        if Scheduler.shouldRun influenceFreq time semanticEvents then
            InfluenceFrame.computeInto (getFrame state HomeClub) (getFrame state AwayClub) state.HomeInfluenceFrame
            InfluenceFrame.computeInto (getFrame state AwayClub) (getFrame state HomeClub) state.AwayInfluenceFrame

        if Scheduler.shouldRun cognitiveFrameFreq time semanticEvents then
            state.HomeCognitiveFrame <- CognitiveFrameModule.build ctx state HomeClub
            state.AwayCognitiveFrame <- CognitiveFrameModule.build ctx state AwayClub

        if Scheduler.shouldRun cognitionFreq time semanticEvents then
            CognitionSystem.run ctx state clock time
            |> applyDomainEvents state events

        if Scheduler.shouldRun actionFreq time semanticEvents then
            if
                getFrame state HomeClub |> _.SlotCount > 0
                && getFrame state AwayClub |> _.SlotCount > 0
            then
                ActionSystem.run (int time.Subtick) ctx state clock
                |> applyDomainEvents state events

        if Scheduler.shouldRun refereeFreq time semanticEvents then
            RefereeAgent.agent ctx state clock
            |> applyDomainEvents state events

        if Scheduler.shouldRun teamFreq time semanticEvents then
            TeamDirector.tick (int time.Subtick) ctx state clock
            TeamExecutor.run ctx state clock time |> ignore

        if Scheduler.shouldRun managerFreq time semanticEvents then
            let result = ManagerAgent.agent ctx state clock
            applyDomainEvents state events result

        if Scheduler.shouldRun adaptiveFreq time semanticEvents then
            AdaptiveSystem.run clock state time
            |> applyDomainEvents state events

    let private updateMatchClock (clock: SimulationClock) (state: SimState) =
        if state.EffectiveFullTimeSubTick = 0<subtick> then
            state.EffectiveFullTimeSubTick <- SimulationClock.fullTime clock

        let ht = SimulationClock.halfTime clock

        if not state.HalfTimeHandled && state.SubTick >= ht && state.Flow = Live then
            state.HalfTimeHandled <- true
            let halfAdded = state.StoppageTime.DecideHalfTime()

            state.EffectiveFullTimeSubTick <-
                max state.EffectiveFullTimeSubTick (ht + halfAdded * int clock.SubTicksPerSecond * 1<subtick>)

            state.Flow <- (HalfTimePause(TickDelay.delayFrom clock state.Config.Timing.KickOffDelay))

        if not state.FullTimeHandled && state.SubTick >= state.EffectiveFullTimeSubTick then
            state.FullTimeHandled <- true
            let fullAdded = state.StoppageTime.DecideFullTime()

            let newFull =
                max
                    state.EffectiveFullTimeSubTick
                    (SimulationClock.fullTime clock + fullAdded * int clock.SubTicksPerSecond * 1<subtick>)

            state.EffectiveFullTimeSubTick <- newFull

            if state.SubTick >= newFull then
                state.Flow <- MatchEnded



    let updateOne (ctx: MatchContext) (clock: SimulationClock) (commands: MatchCommandEnvelope[]) (state: SimState) =
        let events = state.TickEvents
        events.Clear()

        applyCommands ctx state events commands
        updateFlow ctx clock state events

        if state.Flow = Live then
            let time = Scheduler.buildMatchTime state clock
            let semanticEvents = SimStateOps.drainSemanticEvents state

            SimStateOps.expireReceiving state.SubTick (state.Config.Physics.ReceivingGraceSubTicks * 1<tickDelta>) state
            PhysicsSystem.run (int state.SubTick) ctx state clock

            BallSystem.run ctx state clock |> applyDomainEvents state events

            state.PendingSemanticEvents.Clear()

            runLiveSystems ctx clock state events time semanticEvents

        if
            int state.SubTick - int state.LastMemoryDecaySubTick
            >= MatchMemory.DecayIntervalSubTicks
        then
            MatchMemory.decay state.MatchMemory
            state.LastMemoryDecaySubTick <- state.SubTick

        flushPendingSubstitutions ctx state events
        updateMatchClock clock state

        if state.Flow <> MatchEnded then
            state.SubTick <- state.SubTick + 1<subtick>

        { State = state
          Events = events }
