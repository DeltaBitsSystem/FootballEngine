namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Player
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Types.IntentPhaseTypes
open SimStateOps
open SchedulingTypes

type StepResult =
    { State: SimState
      Events: MatchEvent list }

module MatchStepper =

    let private bothSides = [| HomeClub; AwayClub |]

    let private appendEvent (state: SimState) (events: ResizeArray<MatchEvent>) (e: MatchEvent) =
        events.Add e
        state.MatchEvents.Add e

        if state.MatchEvents.Count > 1024 then
            state.MatchEvents.RemoveRange(0, 512)

    let private setFlow (state: SimState) (flow: MatchFlow) = state.Flow <- flow

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




    let private applyFrameWrite (subTick: int) (frame: TeamFrame) (write: FrameWrite) =
        match write with
        | SetPosition(i, x, y) -> FrameMutate.setPos frame.Physics i x y
        | SetVelocity(i, vx, vy) -> FrameMutate.setVel frame.Physics i vx vy
        | SetCondition(i, v) -> FrameMutate.setCondition frame i v
        | SetIntent(i, k, tx, ty, pid) -> FrameMutate.setIntent frame.Intent i k tx ty pid
        | CommitIntent(i, until, trig) ->
            FrameMutate.commitIntent frame.Intent i until (LanguagePrimitives.EnumOfValue<byte, ExitTrigger> trig)
            frame.Intent.CommittedAt[i] <- subTick
        | SetSlotRole(i, r) -> frame.SlotRoles[i] <- r
        | SetCollectiveIntent(i, ci) -> frame.CollectiveIntents[i] <- ci
        | SetSupportPos(i, x, y) ->
            frame.SupportPositionX[i] <- x
            frame.SupportPositionY[i] <- y
        | SetDefensiveRole(i, r) -> frame.DefensiveRole[i] <- byte r
        | SetMentalState(i, comp, conf, agg, foc, risk) -> FrameMutate.setMental frame i comp conf agg foc risk

    let applyOutput (state: SimState) (events: ResizeArray<MatchEvent>) (output: SystemOutput) =
        match output with
        | HomeFrame w -> applyFrameWrite state.SubTick state.Home.Frame w
        | AwayFrame w -> applyFrameWrite state.SubTick state.Away.Frame w
        | BallUpdate b -> state.Ball <- b
        | FlowChange f -> state.Flow <- f
        | ScoreGoal(club, scorerId, isOwn) ->
            if club = HomeClub then
                state.HomeScore <- state.HomeScore + 1
            else
                state.AwayScore <- state.AwayScore + 1
        | EmergentUpdate(club, s) -> SimStateOps.setEmergentState state club s
        | AdaptiveUpdate(club, s) -> SimStateOps.setAdaptiveState state club s
        | DirectiveUpdate(club, d) -> SimStateOps.setDirective state club d
        | MemoryWrite(club, idx, w) ->
            match w with
            | PassFailure -> MatchMemory.recordPassFailure club idx state.MatchMemory
            | PassSuccess -> MatchMemory.recordSuccess club idx state.MatchMemory
            | DuelResult(won, opp) -> MatchMemory.recordDuel club idx opp (if won then Won else Lost) state.MatchMemory
        | RegisterRun(club, run) ->
            let current = SimStateOps.getActiveRuns state club
            SimStateOps.setActiveRuns state club (run :: current)
        | ExpireRun(club, pid) ->
            let current = SimStateOps.getActiveRuns state club
            SimStateOps.setActiveRuns state club (current |> List.filter (fun r -> r.PlayerId <> pid))
        | Emit e ->
            events.Add e
            state.MatchEvents.Add e
        | EmitSemantic s -> SimStateOps.emitSemantic s state
        | PossessionHistoryUpdate delta ->
            let h = state.PossessionHistory

            state.PossessionHistory <-
                { h with
                    LastChangeTick =
                        if delta.PossessionChanged then
                            state.SubTick
                        else
                            h.LastChangeTick
                    LastBallInFlightTick =
                        if delta.BallInFlight then
                            state.SubTick
                        else
                            h.LastBallInFlightTick
                    LastSetPieceTick =
                        if delta.SetPieceAwarded then
                            state.SubTick
                        else
                            h.LastSetPieceTick
                    LastBallReceivedTick =
                        match delta.ReceivedByPlayer with
                        | Some _ -> state.SubTick
                        | None -> h.LastBallReceivedTick
                    ChangedToSide =
                        if delta.PossessionChanged then
                            match state.Ball.Control with
                            | Controlled(side, _)
                            | Receiving(side, _, _) -> Some side
                            | _ -> h.ChangedToSide
                        else
                            h.ChangedToSide }
        | InfluenceFrameUpdate(HomeClub, f) -> state.HomeInfluenceFrame <- f
        | InfluenceFrameUpdate(AwayClub, f) -> state.AwayInfluenceFrame <- f
        | CognitiveFrameUpdate(HomeClub, f) ->
            CognitiveFrameBuffers.copyIntoCFrameBuffers state.HomeCFrameBuffers f
            state.HomeCognitiveFrame <- f
        | CognitiveFrameUpdate(AwayClub, f) ->
            CognitiveFrameBuffers.copyIntoCFrameBuffers state.AwayCFrameBuffers f
            state.AwayCognitiveFrame <- f
        | BallXSmoothUpdate v -> state.BallXSmooth <- v
        | MomentumUpdate delta ->
            let prev = state.Momentum
            state.Momentum <- PhysicsContract.clampFloat (state.Momentum + delta) -10.0 10.0
            let next = state.Momentum

            if prev > 0.0 && next < -3.0 then
                SimStateOps.emitSemantic (SemanticEvent.MomentumShifted AwayClub) state
            elif prev < 0.0 && next > 3.0 then
                SimStateOps.emitSemantic (SemanticEvent.MomentumShifted HomeClub) state
        | StoppageTimeAdd(t, r) -> state.StoppageTime.Add(t, r) |> ignore
        | SidelinedWrite(club, pid, st) ->
            SimStateOps.setSidelined state club (Map.add pid st (SimStateOps.getSidelined state club))
        | YellowsWrite(club, pid, n) ->
            SimStateOps.setYellows state club (Map.add pid n (SimStateOps.getYellows state club))
        | LastAttackingClubSet c -> state.LastAttackingClub <- c
        | ScoreGoalAdjust(club, d) ->
            if club = HomeClub then
                state.HomeScore <- max 0 (state.HomeScore + d)
            else
                state.AwayScore <- max 0 (state.AwayScore + d)
        | MatchStatIncrement(club, field, delta) ->
            let current = SimStateOps.getMatchStats state club

            let updated =
                match field with
                | PassAttempts ->
                    { current with
                        PassAttempts = current.PassAttempts + delta }

            SimStateOps.setMatchStats state club updated

    let applyOutputs (state: SimState) (events: ResizeArray<MatchEvent>) (outputs: SystemOutput[]) =
        for i = 0 to outputs.Length - 1 do
            applyOutput state events outputs[i]

    let private applyVARDecision
        (subTick: int)
        (ctx: MatchContext)
        (clock: SimulationClock)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (review: VARFlowState)
        =
        let decision = VARReview.evaluate state review.Incident

        let varEvents, varOutputs =
            match decision with
            | Overturn -> VARApplicator.applyOverturn subTick review.Incident ctx state
            | CheckComplete -> VARApplicator.applyCheckComplete subTick review.Incident ctx state
            | _ -> [], []

        varEvents |> List.iter (appendEvent state events)
        varOutputs |> List.iter (applyOutput state events)

        match review.Incident with
        | GoalCheck(scoringClub, _, _, _) ->
            let receiving = ClubSide.flip scoringClub
            setFlow state (restartFromSetPiece state clock SetPieceKind.KickOff receiving AfterVAR)
        | PenaltyCheck(team, _, _) ->
            let kind =
                if decision = Overturn then
                    SetPieceKind.FreeKick
                else
                    SetPieceKind.Penalty

            setFlow state (restartFromSetPiece state clock kind team AfterVAR)
        | RedCardCheck _
        | OffsideCheck _ ->
            setFlow state (restartFromSetPiece state clock SetPieceKind.FreeKick state.AttackingSide AfterVAR)

    let private startGoalFlow
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<MatchEvent>)
        =
        let scoringClub = state.AttackingSide
        let scorerId, isOwnGoal = GoalDetector.scorer scoringClub state.Ball ctx state

        let goalEvents, goalOutputs =
            RefereeApplicator.apply subTick (ConfirmGoal(scoringClub, scorerId, isOwnGoal)) ctx state

        goalEvents |> List.iter (appendEvent state events)
        goalOutputs |> List.iter (applyOutput state events)

        match VARDetector.detectGoalCheck scoringClub scorerId isOwnGoal subTick with
        | Some incident ->
            applyOutput state events (StoppageTimeAdd(subTick, StoppageReason.VARReviewDelay))
            let duration = VARReview.reviewDuration subTick

            setFlow
                state
                (VARReview
                    { Incident = incident
                      Phase = CheckingIncident
                      RemainingTicks = duration
                      TotalTicks = duration })
        | None ->
            setFlow
                state
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
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (request: SubstitutionRequest)
        =
        match sideByClubId ctx request.ClubId with
        | None -> []
        | Some side ->
            let team = getTeam state side
            let frame = team.Frame
            let roster = getRoster ctx side

            match findIdxByPid request.OutPlayerId frame roster with
            | ValueNone -> []
            | ValueSome outIdx ->
                let squad = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers

                match squad |> Array.tryFind (fun p -> p.Id = request.InPlayerId) with
                | None -> []
                | Some incoming ->
                    ManagerAgent.resolve subTick (MakeSubstitution(request.ClubId, outIdx, incoming)) ctx state

    let private flushPendingSubstitutions
        (subTick: int)
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
                    let evs = tryApplySubstitution subTick ctx state request

                    if not (List.isEmpty evs) then
                        applied.Add request
                        evs |> List.iter (appendEvent state events)

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
                setFlow
                    state
                    (RestartDelay
                        { Kind = SetPieceKind.KickOff
                          Team = state.AttackingSide
                          Cause = AfterBallOut
                          RemainingTicks = 1 })

        | ResumeSimulation ->
            match state.Flow with
            | RestartDelay r when r.RemainingTicks <= 1 -> setFlow state Live
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
                    let evs = tryApplySubstitution state.SubTick ctx state request

                    if List.isEmpty evs then
                        request :: pendingSubs state side |> setPendingSubs state side
                    else
                        evs |> List.iter (appendEvent state events)

    let private applyCommands
        (ctx: MatchContext)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        (commands: MatchCommandEnvelope[])
        =
        MatchCommands.orderForTick state.SubTick commands
        |> Array.iter (applyCommand ctx state events)

    let private processTransition (state: SimState) (transition: MatchFlow option) =
        match transition with
        | None -> ()
        | Some f -> setFlow state f

    let private runSetPiece
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<MatchEvent>)
        (restart: RestartPlan)
        =
        let result = SetPieceAgent.run restart.Kind restart.Team ctx state clock
        result.Events |> List.iter (appendEvent state events)
        processTransition state result.Transition

    let private updatePossessionHistory (result: BallResult) (subTick: int) (state: SimState) =
        let h = state.PossessionHistory

        state.PossessionHistory <-
            { h with
                LastChangeTick =
                    if result.PossessionChanged then
                        subTick
                    else
                        h.LastChangeTick
                LastBallInFlightTick =
                    if result.BallInFlight then
                        subTick
                    else
                        h.LastBallInFlightTick
                LastSetPieceTick =
                    if result.SetPieceAwarded then
                        subTick
                    else
                        h.LastSetPieceTick
                LastBallReceivedTick =
                    match result.ReceivedByPlayer with
                    | Some _ -> subTick
                    | None -> h.LastBallReceivedTick
                ChangedToSide =
                    if result.PossessionChanged then
                        match state.Ball.Control with
                        | Controlled(side, _)
                        | Receiving(side, _, _) -> Some side
                        | _ -> h.ChangedToSide
                    else
                        h.ChangedToSide }

    let private updateFlow
        (ctx: MatchContext)
        (clock: SimulationClock)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        =
        let wasLive = state.Flow = Live

        match state.Flow with
        | GoalPause goal when goal.RemainingTicks > 0 ->
            setFlow
                state
                (GoalPause
                    { goal with
                        RemainingTicks = goal.RemainingTicks - 1 })

        | GoalPause goal ->
            setFlow
                state
                (restartFromSetPiece state clock SetPieceKind.KickOff (ClubSide.flip goal.ScoringTeam) AfterGoal)

        | VARReview review when review.RemainingTicks > 0 ->
            setFlow
                state
                (VARReview
                    { review with
                        RemainingTicks = review.RemainingTicks - 1 })

        | VARReview review -> applyVARDecision state.SubTick ctx clock state events review

        | InjuryPause injury when injury.RemainingTicks > 0 ->
            setFlow
                state
                (InjuryPause
                    { injury with
                        RemainingTicks = injury.RemainingTicks - 1 })

        | InjuryPause _ ->
            setFlow state (restartFromSetPiece state clock SetPieceKind.FreeKick state.AttackingSide AfterInjury)

        | RestartDelay restart when restart.RemainingTicks > 0 ->
            setFlow
                state
                (RestartDelay
                    { restart with
                        RemainingTicks = restart.RemainingTicks - 1 })

        | RestartDelay restart -> runSetPiece ctx state clock events restart

        | HalfTimePause remaining when remaining > 0 -> setFlow state (HalfTimePause(remaining - 1))

        | HalfTimePause _ ->
            setFlow state (restartFromSetPiece state clock SetPieceKind.KickOff AwayClub InitialKickOff)

        | FullTimeReview -> setFlow state MatchEnded

        | Live
        | MatchEnded -> ()

        // Phase 4: suspend/resume directives on Live<->pause transitions
        let isNowLive = state.Flow = Live

        if wasLive && not isNowLive then
            suspendDirective state HomeClub
            suspendDirective state AwayClub
        elif not wasLive && isNowLive then
            resumeDirective state HomeClub
            resumeDirective state AwayClub

    let private maybeRunManagerWindow
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<MatchEvent>)
        =
        let elapsedSec = int (SimulationClock.subTicksToSeconds clock subTick)

        let shouldRun =
            subTick % clock.SubTicksPerSecond = 0
            && (ctx.Config.Manager.SubWindowMinutes
                |> Array.exists (fun m -> elapsedSec = m * 60))

        if shouldRun then
            let result = ManagerAgent.agent ctx state clock
            result.Events |> List.iter (appendEvent state events)
            processTransition state result.Transition

    let private runLiveSystems
        (ctx: MatchContext)
        (clock: SimulationClock)
        (state: SimState)
        (events: ResizeArray<MatchEvent>)
        =
        match state.Flow with
        | Live ->
            let subTick = state.SubTick

            // Física: siempre corre
            SimStateOps.expireReceiving subTick ctx.Config.Physics.ReceivingGraceSubTicks state
            PhysicsSystem.run subTick ctx state clock |> applyOutputs state events

            FootballEngine.BallSystem.run ctx state clock |> applyOutputs state events

            // Drenar semánticos acumulados por BallAgent y RefereeApplicator
            let semanticEvents = SimStateOps.drainSemanticEvents state

            // Router decide qué agentes corren
            let activations = EventRouter.route semanticEvents subTick clock

            for activation in activations do
                match activation with
                | ActivatePhysics -> ()

                | ActivateCognition -> CognitionSystem.run subTick ctx state clock |> applyOutputs state events

                | ActivateAction _ ->
                    if
                        getFrame state HomeClub |> _.SlotCount > 0
                        && getFrame state AwayClub |> _.SlotCount > 0
                    then
                        ActionSystem.run subTick ctx state clock |> applyOutputs state events

                | ActivateReferee _ ->
                    let refResult = RefereeAgent.agent ctx state clock
                    let refOutputs = ResizeArray<SystemOutput>(8)

                    refResult.Actions
                    |> List.iter (fun a ->
                        let evs, outs = RefereeApplicator.apply subTick a ctx state
                        evs |> List.iter (fun e -> refOutputs.Add(Emit e))
                        outs |> List.iter refOutputs.Add)

                    refOutputs.ToArray() |> applyOutputs state events
                    processTransition state refResult.Transition

                | ActivateTeam _ -> TeamSystem.run subTick ctx state clock |> applyOutputs state events

                | ActivateManager _ -> maybeRunManagerWindow subTick ctx state clock events

            AdaptiveSystem.run subTick clock state |> applyOutputs state events
        | _ -> () // Si es VAR, Pausa, Gol, etc., los sistemas de juego no corren


    let private updateMatchClock (clock: SimulationClock) (state: SimState) =
        if state.EffectiveFullTimeSubTick = 0 then
            state.EffectiveFullTimeSubTick <- SimulationClock.fullTime clock

        let ht = SimulationClock.halfTime clock

        if not state.HalfTimeHandled && state.SubTick >= ht && state.Flow = Live then
            state.HalfTimeHandled <- true
            let halfAdded = state.StoppageTime.DecideHalfTime()

            state.EffectiveFullTimeSubTick <-
                max state.EffectiveFullTimeSubTick (ht + halfAdded * clock.SubTicksPerSecond)

            setFlow state (HalfTimePause(TickDelay.delayFrom clock state.Config.Timing.KickOffDelay))

        if not state.FullTimeHandled && state.SubTick >= state.EffectiveFullTimeSubTick then
            state.FullTimeHandled <- true
            let fullAdded = state.StoppageTime.DecideFullTime()

            let newFull =
                max
                    state.EffectiveFullTimeSubTick
                    (SimulationClock.fullTime clock + fullAdded * clock.SubTicksPerSecond)

            state.EffectiveFullTimeSubTick <- newFull

            if state.SubTick >= newFull then
                setFlow state MatchEnded





    let updateOne (ctx: MatchContext) (clock: SimulationClock) (commands: MatchCommandEnvelope[]) (state: SimState) =
        let events = ResizeArray<MatchEvent>()

        applyCommands ctx state events commands
        updateFlow ctx clock state events

        if state.Flow = Live then
            runLiveSystems ctx clock state events

        if
            state.SubTick - state.LastMemoryDecaySubTick
            >= MatchMemory.DecayIntervalSubTicks
        then
            MatchMemory.decay state.MatchMemory
            state.LastMemoryDecaySubTick <- state.SubTick

        flushPendingSubstitutions state.SubTick ctx state events
        updateMatchClock clock state

        if state.Flow <> MatchEnded then
            state.SubTick <- state.SubTick + 1

        { State = state
          Events = events |> Seq.toList }
