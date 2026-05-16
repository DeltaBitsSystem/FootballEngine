namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open PhysicsContract
open SimStateOps


module VARApplicator =

    let applyOverturn
        (subTick: int)
        (incident: VARReviewableIncident)
        (ctx: MatchContext)
        (state: SimState)
        : DomainEvent[] =
        match incident with
        | GoalCheck(scoringClub, _, _, _) ->
            let resetBall =
                { state.Ball with
                    Position = kickOffSpatial
                    Spin = Spin.zero
                    Control = Free
                    LastTouchBy = None
                    PendingOffsideSnapshot = None
                    StationarySinceSubTick = None
                    GKHoldSinceSubTick = None
                    PlayerHoldSinceSubTick = None
                    Trajectory = None }
            [| DomainEvent.ScoreGoalAdjust(scoringClub, -1)
               DomainEvent.BallUpdate resetBall |]

        | PenaltyCheck(_, _, _) ->
            let resetBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free
                    PendingOffsideSnapshot = None
                    StationarySinceSubTick = None
                    GKHoldSinceSubTick = None
                    PlayerHoldSinceSubTick = None
                    Trajectory = None }
            [| DomainEvent.BallUpdate resetBall |]

        | RedCardCheck(playerId, _) ->
            let side = clubSideOf ctx state playerId |> Option.defaultValue HomeClub
            let clubId = if side = HomeClub then ctx.Home.Id else ctx.Away.Id
            [| DomainEvent.Emit { SubTick = subTick; PlayerId = playerId; ClubId = clubId; Type = MatchEventType.RedCard; Context = EventContext.empty }
               DomainEvent.SidelinedWrite(side, playerId, SidelinedByRedCard) |]

        | OffsideCheck _ ->
            [| DomainEvent.BallUpdate { state.Ball with Control = Free; PendingOffsideSnapshot = None } |]

    let applyCheckComplete
        (subTick: int)
        (incident: VARReviewableIncident)
        (ctx: MatchContext)
        (state: SimState)
        : DomainEvent[] =
        [||]
