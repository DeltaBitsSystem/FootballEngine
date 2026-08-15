namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types
open SimStateOps
open Stats
open PhysicsContract

module RefereeAgent =

    let cardProbability (rw: RefereeWeights) (config: HomeAdvantageConfig) (playerClub: ClubSide) (p: Player) =
        let baseProb = rw.CardBaseProb + float p.Mental.Aggression * rw.CardAggressionMult

        match playerClub with
        | HomeClub -> baseProb * (1.0 - config.CardReduction)
        | AwayClub -> baseProb

    let injuryProbability (rw: RefereeWeights) (p: Player) =
        rw.InjuryBaseProb
        + float (100 - p.Physical.Strength) * rw.InjuryStrengthInverseMult

    type BallOutResult =
        | NoOut
        | ThrowIn of ClubSide
        | Corner of ClubSide
        | GoalKick of ClubSide

    let private ballOutOfBounds (ctx: MatchContext) (state: SimState) : BallOutResult =
        let pos = state.Ball.Position
        let outY = pos.Y < 0.1<meter> || pos.Y > PitchWidth - 0.1<meter>
        let outX = pos.X < 0.1<meter> || pos.X > PitchLength - 0.1<meter>

        let lastTouchClubSide () =
            state.Ball.LastTouchBy
            |> Option.bind (fun pid ->
                if playerOnSide ctx state HomeClub pid then Some HomeClub
                elif playerOnSide ctx state AwayClub pid then Some AwayClub
                else None)

        if outY then
            match lastTouchClubSide () with
            | Some side -> ThrowIn(ClubSide.flip side)
            | None -> NoOut
        elif outX then
            let behindHome = pos.X < 0.5<meter>
            let behindAway = pos.X > PitchLength - 0.5<meter>

            let inGoalY = pos.Y >= PostNearY && pos.Y <= PostFarY

            if inGoalY then
                NoOut
            elif behindHome then
                match lastTouchClubSide () with
                | Some AwayClub -> Corner HomeClub
                | _ -> GoalKick HomeClub
            elif behindAway then
                match lastTouchClubSide () with
                | Some HomeClub -> Corner AwayClub
                | _ -> GoalKick AwayClub
            else
                NoOut
        else
            NoOut

    let decide
        (att: Player option)
        (def: Player option)
        (ctx: MatchContext)
        (state: SimState)
        : RefereeAction ResizeArray =
        let result = ResizeArray<RefereeAction>(6)

        match state.Flow with
        | MatchFlow.Live ->
            match ballOutOfBounds ctx state with
            | ThrowIn team -> result.Add(AwardThrowIn team)
            | Corner team -> result.Add(AwardCorner team)
            | GoalKick team -> result.Add(AwardGoalKick team)
            | NoOut -> ()

            match GoalDetector.detect state.Ball state.HomeAttackDir with
            | Some team ->
                let scorerId, isOwn = GoalDetector.scorer team state.Ball ctx state
                result.Add(ConfirmGoal(team, scorerId, isOwn))
            | None -> ()

            att
            |> Option.filter (fun a -> bernoulli (injuryProbability state.Config.Referee a))
            |> Option.iter (fun a ->
                result.Add(
                    IssueInjury(
                        a,
                        if playerOnSide ctx state HomeClub a.Id then
                            ctx.Home.Id
                        else
                            ctx.Away.Id
                    )
                ))

            match state.Ball.StationarySinceSubTick with
            | Some since when
                state.SubTick - since
                >= SimulationClock.deltaToSubtick state.Config.Timing.StuckBallDelay
                ->
                result.Add(DropBall state.AttackingSide)
            | _ -> ()

            match state.Ball.GKHoldSinceSubTick with
            | Some since when
                state.SubTick - since
                >= SimulationClock.deltaToSubtick state.Config.GK.MaxHoldSubTicks
                ->
                match state.Ball.Control with
                | Controlled(side, _)
                | Receiving(side, _, _) ->
                    let team = if side = HomeClub then state.Home else state.Away
                    let holdX = float state.Ball.Position.X

                    let gkPid =
                        team.Frame.Physics.Occupancy
                        |> Array.tryFindIndex (function
                            | OccupancyKind.Active ri ->
                                let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers
                                ri < roster.Length && roster[ri].Position = GK
                            | _ -> false)
                        |> Option.bind (fun idx ->
                            match team.Frame.Physics.Occupancy[idx] with
                            | OccupancyKind.Active ri ->
                                let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers
                                if ri < roster.Length then Some roster[ri].Id else None
                            | _ -> None)

                    match gkPid with
                    | Some pid ->
                        let violations = Map.tryFind pid team.GKHoldViolations |> Option.defaultValue 0

                        if violations >= 1 then
                            let clubId = if side = HomeClub then ctx.Home.Id else ctx.Away.Id

                            result.Add(AwardIndirectFreeKick(ClubSide.flip side))

                            result.Add(
                                IssueYellow(
                                    (if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers)
                                    |> Array.find (fun p -> p.Id = pid),
                                    clubId
                                )
                            )
                        else
                            result.Add(AwardIndirectFreeKick(ClubSide.flip side))
                    | None -> result.Add(AwardIndirectFreeKick(ClubSide.flip side))
                | _ -> ()
            | _ -> ()
        | _ -> ()

        result


    let processFouls
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<SemanticEvent>)
        : DomainEvent[] * (SetPieceKind * ClubSide) option =
        let fouls = ResizeArray<DomainEvent>(4)
        let ballX = state.Ball.Position.X
        let dir = state.HomeAttackDir
        let minute = float (SimulationClock.toMatchMin clock state.SubTick)

        let homeFoulsMap = ResizeArray<PlayerId>()
        let awayFoulsMap = ResizeArray<PlayerId>()

        for e in state.MatchEvents do
            if e.Type = FoulCommitted then
                if e.ClubId = ctx.Home.Id then
                    homeFoulsMap.Add(e.PlayerId)
                else
                    awayFoulsMap.Add(e.PlayerId)

        let teamFoulCount (side: ClubSide) : int =
            if side = HomeClub then
                homeFoulsMap.Count
            else
                awayFoulsMap.Count

        let playerFoulCount (pid: PlayerId) : int =
            let mutable count = 0
            let mutable i = 0

            while i < homeFoulsMap.Count do
                if homeFoulsMap[i] = pid then
                    count <- count + 1

                i <- i + 1

            i <- 0

            while i < awayFoulsMap.Count do
                if awayFoulsMap[i] = pid then
                    count <- count + 1

                i <- i + 1

            count

        let homeBonus = state.AttackingSide = HomeClub
        let matchImportance = if ctx.IsKnockoutMatch then 0.9 else 0.5

        let disciplineHistory =
            let homeFouls = teamFoulCount HomeClub
            let awayFouls = teamFoulCount AwayClub
            max homeFouls awayFouls

        let mutable foulRestart = None

        for i = 0 to events.Count - 1 do
            match events[i] with
            | SemanticEvent.FoulOccurred(foulerId, fouledId, severity) ->
                let foulerOpt =
                    ctx.HomePlayers
                    |> Array.tryFind (fun p -> p.Id = foulerId)
                    |> Option.orElseWith (fun () -> ctx.AwayPlayers |> Array.tryFind (fun p -> p.Id = foulerId))

                match foulerOpt with
                | Some fouler ->
                    let isHome = playerOnSide ctx state HomeClub foulerId
                    let side = if isHome then HomeClub else AwayClub
                    let clubId = if isHome then ctx.Home.Id else ctx.Away.Id
                    let yellows = getYellows state side |> Map.tryFind foulerId |> Option.defaultValue 0
                    let foulsThisHalf = teamFoulCount side
                    let foulerFouls = playerFoulCount foulerId

                    let fouledSide = if isHome then AwayClub else HomeClub

                    let advDecision =
                        AdvantageEngine.evaluate
                            (float ballX * 1.0<meter>)
                            (float state.Ball.Position.Y * 1.0<meter>)
                            dir
                            fouledSide
                            state.Ball.Control
                            (FoulAuthority.classifyZone ballX dir)

                    let isAdvantagePreserved =
                        match advDecision with
                        | PlayOn _ -> true
                        | StopPlay _ -> false

                    let decision =
                        FoulAuthority.decideFromContext
                            severity
                            yellows
                            foulerFouls
                            ballX
                            dir
                            minute
                            foulsThisHalf
                            isAdvantagePreserved
                            state.RefereeProfile
                            homeBonus
                            matchImportance
                            disciplineHistory
                            state.Config.Referee

                    match decision.Card with
                    | Some FoulAnalysis.CardDecision.Yellow ->
                        if not decision.Advantage then
                            let actions =
                                RefereeApplicator.apply
                                    (int state.SubTick)
                                    (IssueYellow(fouler, clubId))
                                    ctx
                                    state
                                    (Some decision.Reason)

                            for e in actions do
                                fouls.Add(e)
                    | Some FoulAnalysis.CardDecision.Red ->
                        if not decision.Advantage then
                            let actions =
                                RefereeApplicator.apply
                                    (int state.SubTick)
                                    (IssueRed(fouler, clubId))
                                    ctx
                                    state
                                    (Some decision.Reason)

                            for e in actions do
                                fouls.Add(e)
                    | None when decision.Advantage ->
                        state.AdvantageState <- AdvantagePlaying(int state.SubTick, fouledSide, decision.Reason)
                    | None -> ()

                    if not decision.Advantage then
                        let fouledDir =
                            match fouledSide with
                            | HomeClub -> dir
                            | AwayClub ->
                                match dir with
                                | LeftToRight -> RightToLeft
                                | RightToLeft -> LeftToRight

                        let effectiveX =
                            match fouledDir with
                            | LeftToRight -> ballX
                            | RightToLeft -> PitchLength - ballX

                        let inBox = effectiveX > PitchLength - PenaltyAreaDepth

                        let award: RefereeAction =
                            if inBox then
                                AwardPenalty fouledSide
                            else
                                AwardDirectFreeKick fouledSide

                        let kind =
                            if inBox then
                                SetPieceKind.Penalty
                            else
                                SetPieceKind.FreeKick

                        let actions =
                            RefereeApplicator.apply (int state.SubTick) award ctx state (Some decision.Reason)

                        for e in actions do
                            fouls.Add(e)

                        foulRestart <- Some(kind, fouledSide)
                | None -> ()
            | _ -> ()

        fouls.ToArray(), foulRestart

    let agent
        ctx
        (state: SimState)
        (clock: SimulationClock)
        (semanticEvents: ResizeArray<SemanticEvent>)
        : DomainEvent[] =
        let isLive =
            match state.Flow with
            | Live -> true
            | _ -> false

        if not isLive then
            [||]
        else
            let subTick = state.SubTick

            match state.AdvantageState with
            | AdvantagePlaying(foulSubTick, fouledTeam, _) ->
                let window =
                    int (state.Config.Referee.AdvantageCallBackWindow * float clock.SubTicksPerSecond)

                if AdvantageEngine.shouldCallBack foulSubTick (int subTick) state.Ball.Control fouledTeam then
                    state.AdvantageState <- NoAdvantage
                elif int subTick - foulSubTick > window then
                    state.AdvantageState <- NoAdvantage
            | _ -> ()

            let refActions = decide None None ctx state

            let events = ResizeArray<DomainEvent>(16)

            for i = 0 to refActions.Count - 1 do
                let actionEvents =
                    RefereeApplicator.apply (int subTick) refActions[i] ctx state None

                for e in actionEvents do
                    events.Add(e)

            let mutable hasAwardIndirectFreeKick = false
            let mutable hasDropBall = false
            let mutable hasConfirmGoal = false
            let mutable restartOption = ValueNone
            let mutable confirmGoalScorerId: PlayerId option = None
            let mutable confirmGoalIsOwn = false

            for i = 0 to refActions.Count - 1 do
                match refActions[i] with
                | AwardIndirectFreeKick _ -> hasAwardIndirectFreeKick <- true
                | DropBall _ -> hasDropBall <- true
                | ConfirmGoal(_, scorerId, isOwn) ->
                    hasConfirmGoal <- true
                    confirmGoalScorerId <- scorerId
                    confirmGoalIsOwn <- isOwn
                | AwardThrowIn team ->
                    if restartOption = ValueNone then
                        restartOption <- ValueSome(SetPieceKind.ThrowIn, team)
                | AwardCorner team ->
                    if restartOption = ValueNone then
                        restartOption <- ValueSome(SetPieceKind.Corner, team)
                | AwardGoalKick team ->
                    if restartOption = ValueNone then
                        restartOption <- ValueSome(SetPieceKind.GoalKick, team)
                | AwardIndirectFreeKick team
                | AwardDirectFreeKick team ->
                    if restartOption = ValueNone then
                        restartOption <- ValueSome(SetPieceKind.FreeKick, team)
                | AwardPenalty team ->
                    if restartOption = ValueNone then
                        restartOption <- ValueSome(SetPieceKind.Penalty, team)
                | _ -> ()

            let hasGKTimeWasting =
                hasAwardIndirectFreeKick
                && state.Ball.GKHoldSinceSubTick
                   |> Option.exists (fun since ->
                       state.SubTick - since
                       >= SimulationClock.deltaToSubtick state.Config.GK.MaxHoldSubTicks)

            if hasGKTimeWasting then
                match state.Ball.Control with
                | Controlled(side, _)
                | Receiving(side, _, _) ->
                    let team = if side = HomeClub then state.Home else state.Away

                    let gkPidOpt =
                        team.Frame.Physics.Occupancy
                        |> Array.tryFindIndex (function
                            | OccupancyKind.Active ri ->
                                let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers
                                ri < roster.Length && roster[ri].Position = GK
                            | _ -> false)
                        |> Option.bind (fun idx ->
                            match team.Frame.Physics.Occupancy[idx] with
                            | OccupancyKind.Active ri ->
                                let roster = if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers
                                if ri < roster.Length then Some roster[ri].Id else None
                            | _ -> None)

                    match gkPidOpt with
                    | Some gkPid ->
                        let current = Map.tryFind gkPid team.GKHoldViolations |> Option.defaultValue 0
                        let updated = Map.add gkPid (current + 1) team.GKHoldViolations
                        team.GKHoldViolations <- updated
                    | None -> ()
                | _ -> ()

            let restart =
                if hasDropBall then
                    None
                elif hasConfirmGoal then
                    Some(
                        SetPieceKind.KickOff,
                        (match restartOption with
                         | ValueSome(_, team) -> team
                         | _ -> HomeClub)
                    )
                else if restartOption <> ValueNone then
                    match restartOption with
                    | ValueSome v -> Some v
                    | _ -> None
                else
                    None

            let transition =
                if hasDropBall then
                    Some MatchFlow.Live
                elif hasConfirmGoal then
                    match restart with
                    | Some(SetPieceKind.KickOff, team) ->
                        Some(
                            MatchFlow.GoalPause
                                { ScoringTeam = team
                                  ScorerId = confirmGoalScorerId
                                  IsOwnGoal = confirmGoalIsOwn
                                  RemainingTicks = TickDelay.delayFrom clock state.Config.Timing.GoalDelay
                                  VARRequested = false }
                        )
                    | _ -> None
                else
                    match restart with
                    | Some(kind, team) ->
                        let delay =
                            match kind with
                            | SetPieceKind.ThrowIn -> state.Config.Timing.ThrowInDelay
                            | SetPieceKind.Corner -> state.Config.Timing.CornerDelay
                            | SetPieceKind.GoalKick -> state.Config.Timing.GoalKickDelay
                            | SetPieceKind.FreeKick -> state.Config.Timing.FreeKickDelay
                            | _ -> state.Config.Timing.KickOffDelay

                        Some(
                            MatchFlow.RestartDelay
                                { Kind = kind
                                  Team = team
                                  Cause = AfterBallOut
                                  RemainingTicks = TickDelay.delayFrom clock delay }
                        )
                    | _ -> None

            match transition with
            | Some flow -> events.Add(DomainEvent.FlowChange flow)
            | None -> ()

            let foulEvents, foulRestart = processFouls ctx state clock semanticEvents

            match foulRestart with
            | Some(kind, team) ->
                let delay =
                    match kind with
                    | SetPieceKind.Penalty -> state.Config.Timing.FreeKickDelay
                    | SetPieceKind.FreeKick -> state.Config.Timing.FreeKickDelay
                    | _ -> state.Config.Timing.KickOffDelay

                events.Add(
                    DomainEvent.FlowChange(
                        MatchFlow.RestartDelay
                            { Kind = kind
                              Team = team
                              Cause = AfterFoul
                              RemainingTicks = TickDelay.delayFrom clock delay }
                    )
                )
            | None -> ()

            Array.append (events.ToArray()) foulEvents
