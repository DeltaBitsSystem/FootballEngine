namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open SimStateOps
open Stats
open PhysicsContract

module RefereeAgent =

    let cardProbability (config: HomeAdvantageConfig) (playerClub: ClubSide) (p: Player) =
        let baseProb = 0.010 + float p.Mental.Aggression * 0.0004

        match playerClub with
        | HomeClub -> baseProb * (1.0 - config.CardReduction)
        | AwayClub -> baseProb

    let injuryProbability (p: Player) =
        0.0008 + float (100 - p.Physical.Strength) * 0.00002

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
                |> Option.filter (fun a -> bernoulli (injuryProbability a))
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
                | Some since when state.SubTick - since >= SimulationClock.deltaToSubtick state.Config.Timing.StuckBallDelay ->
                    [ DropBall state.AttackingSide ]
                | _ -> []

            let gkTimeWastingIntent =
                match state.Ball.GKHoldSinceSubTick with
                | Some since when state.SubTick - since >= SimulationClock.deltaToSubtick state.Config.GK.MaxHoldSubTicks ->
                    match state.Ball.Control with
                    | Controlled(side, _)
                    | Receiving(side, _, _) -> [ AwardIndirectFreeKick(ClubSide.flip side) ]
                    | _ -> []
                | _ -> []

            throwInIntent
            @ goalIntent
            @ injuryIntent
            @ stuckBallIntent
            @ gkTimeWastingIntent
        | _ -> []

    let decideCard (fouler: Player) (ctx: MatchContext) (state: SimState) : RefereeAction list =
        let aggressionNorm = normaliseAttr fouler.Mental.Aggression

        let isHome = playerOnSide ctx state HomeClub fouler.Id

        let clubId = if isHome then ctx.Home.Id else ctx.Away.Id
        let yellows = getYellows state (if isHome then HomeClub else AwayClub)
        let currentYellows = yellows |> Map.tryFind fouler.Id |> Option.defaultValue 0

        let isSidelined =
            getSidelined state (if isHome then HomeClub else AwayClub)
            |> Map.containsKey fouler.Id

        if isSidelined then
            []
        elif currentYellows >= 1 then
            [ IssueRed(fouler, clubId) ]
        elif bernoulli (0.25 + aggressionNorm * 0.35) then
            [ IssueYellow(fouler, clubId) ]
        else
            []


    let agent ctx (state: SimState) (clock: SimulationClock) : DomainEvent[] =
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
                let actionEvents = RefereeApplicator.apply (int subTick) action ctx state
                for e in actionEvents do events.Add(e)

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

            events.ToArray()
