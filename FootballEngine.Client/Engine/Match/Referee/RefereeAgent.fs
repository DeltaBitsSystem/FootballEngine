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

    let decide (att: Player option) (def: Player option) (ctx: MatchContext) (state: SimState) : RefereeAction list =
        match state.Flow with
        | MatchFlow.Live ->
            let throwInIntent =
                match ballOutOfBounds ctx state with
                | ThrowIn team -> [ AwardThrowIn team ]
                | Corner team -> [ AwardCorner team ]
                | GoalKick team -> [ AwardGoalKick team ]
                | NoOut -> []

            let goalIntent =
                match GoalDetector.detect state.Ball state.HomeAttackDir with
                | Some team ->
                    let scorerId, isOwn = GoalDetector.scorer team state.Ball ctx state
                    [ ConfirmGoal(team, scorerId, isOwn) ]
                | None -> []

            let injuryIntent =
                att
                |> Option.filter (fun a -> bernoulli (injuryProbability state.Config.Referee a))
                |> Option.map (fun a ->
                    IssueInjury(
                        a,
                        if playerOnSide ctx state HomeClub a.Id then
                            ctx.Home.Id
                        else
                            ctx.Away.Id
                    ))
                |> Option.toList

            let stuckBallIntent =
                match state.Ball.StationarySinceSubTick with
                | Some since when
                    state.SubTick - since
                    >= SimulationClock.deltaToSubtick state.Config.Timing.StuckBallDelay
                    ->
                    [ DropBall state.AttackingSide ]
                | _ -> []

            let gkTimeWastingIntent =
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

                                [ AwardIndirectFreeKick(ClubSide.flip side)
                                  IssueYellow(
                                      (if side = HomeClub then ctx.HomePlayers else ctx.AwayPlayers)
                                      |> Array.find (fun p -> p.Id = pid),
                                      clubId
                                  ) ]
                            else
                                [ AwardIndirectFreeKick(ClubSide.flip side) ]
                        | None -> [ AwardIndirectFreeKick(ClubSide.flip side) ]
                    | _ -> []
                | _ -> []

            throwInIntent
            @ goalIntent
            @ injuryIntent
            @ stuckBallIntent
            @ gkTimeWastingIntent
        | _ -> []


    let processFouls
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (events: ResizeArray<SemanticEvent>)
        : DomainEvent[] =
        let fouls = ResizeArray<DomainEvent>(4)
        let ballX = state.Ball.Position.X
        let dir = state.HomeAttackDir
        let minute = float (SimulationClock.toMatchMin clock state.SubTick)

        let teamFoulCount (side: ClubSide) : int =
            let mutable count = 0

            for e in state.MatchEvents do
                if e.Type = FoulCommitted then
                    let isHome = e.ClubId = ctx.Home.Id

                    if (side = HomeClub && isHome) || (side = AwayClub && not isHome) then
                        count <- count + 1

            count

        let playerFoulCount (pid: PlayerId) : int =
            let mutable count = 0

            for e in state.MatchEvents do
                if e.Type = FoulCommitted && e.PlayerId = pid then
                    count <- count + 1

            count

        for i = 0 to events.Count - 1 do
            match events[i] with
            | SemanticEvent.FoulOccurred(foulerId, _, severity) ->
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

                    let decision =
                        FoulAuthority.decideFromContext
                            severity
                            yellows
                            foulerFouls
                            ballX
                            dir
                            minute
                            foulsThisHalf
                            false

                    match decision.Card with
                    | Some FoulAnalysis.CardDecision.Yellow ->
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
                        let actions =
                            RefereeApplicator.apply
                                (int state.SubTick)
                                (IssueRed(fouler, clubId))
                                ctx
                                state
                                (Some decision.Reason)

                        for e in actions do
                            fouls.Add(e)
                    | None -> ()
                | None -> ()
            | _ -> ()

        fouls.ToArray()

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
            let refActions = decide None None ctx state

            let events = ResizeArray<DomainEvent>(16)

            for action in refActions do
                let actionEvents = RefereeApplicator.apply (int subTick) action ctx state None

                for e in actionEvents do
                    events.Add(e)

            let hasGKTimeWasting =
                refActions
                |> List.exists (function
                    | AwardIndirectFreeKick _ -> true
                    | _ -> false)
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

            let hasDropBall =
                refActions
                |> List.exists (function
                    | DropBall _ -> true
                    | _ -> false)

            let hasConfirmGoal =
                refActions
                |> List.exists (function
                    | ConfirmGoal _ -> true
                    | _ -> false)

            let restart =
                refActions
                |> List.tryPick (function
                    | AwardThrowIn team -> Some(SetPieceKind.ThrowIn, team)
                    | AwardCorner team -> Some(SetPieceKind.Corner, team)
                    | AwardGoalKick team -> Some(SetPieceKind.GoalKick, team)
                    | AwardIndirectFreeKick team -> Some(SetPieceKind.FreeKick, team)
                    | ConfirmGoal(team, scorerId, isOwn) -> Some(SetPieceKind.KickOff, team)
                    | _ -> None)

            let restart = if hasDropBall then None else restart

            let transition =
                if hasDropBall then
                    Some MatchFlow.Live
                elif hasConfirmGoal then
                    match restart with
                    | Some(SetPieceKind.KickOff, team) ->
                        let scorerId =
                            refActions
                            |> List.tryPick (function
                                | ConfirmGoal(_, sid, _) -> sid
                                | _ -> None)

                        let isOwn =
                            refActions
                            |> List.exists (function
                                | ConfirmGoal(_, _, true) -> true
                                | _ -> false)

                        Some(
                            MatchFlow.GoalPause
                                { ScoringTeam = team
                                  ScorerId = scorerId
                                  IsOwnGoal = isOwn
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

            let foulEvents = processFouls ctx state clock semanticEvents
            Array.append (events.ToArray()) foulEvents
