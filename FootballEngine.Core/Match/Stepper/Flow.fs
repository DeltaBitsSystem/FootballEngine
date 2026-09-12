namespace FootballEngine.Core.Match

/// Countdown machine. Pure: reads World, never mutates it. Expired flows freeze at 0
/// and wait for the EventApply of the event they just emitted.
module Flow =

    let delayFor (cfg: BalanceConfig) (clock: SimulationClock) (kind: SetPieceKind) : int<tickDelta> =
        let sub (s: float<second>) : int<tickDelta> =
            int (round (float s * float clock.SubTicksPerSecond)) * 1<tickDelta>

        match kind with
        | SetPieceKind.KickOff -> sub cfg.SetPiece.KickoffDelay
        | SetPieceKind.ThrowIn -> sub cfg.SetPiece.ThrowInDelay
        | SetPieceKind.Corner -> sub cfg.SetPiece.CornerDelay
        | SetPieceKind.GoalKick -> sub cfg.SetPiece.GoalKickDelay
        | SetPieceKind.FreeKick -> sub cfg.SetPiece.FreeKickDelay
        | SetPieceKind.Penalty -> sub cfg.SetPiece.PenaltyDelay

    let tick
        (clock: SimulationClock)
        (cfg: BalanceConfig)
        (world: World)
        (tick: int<subtick>)
        (flow: MatchFlow)
        : MatchFlow * MatchEvent list =
        match flow with
        | MatchFlow.Live -> flow, []
        | MatchFlow.RestartDelay(remaining, plan) ->
            if remaining <= 1<tickDelta> then
                MatchFlow.Live, []
            else
                MatchFlow.RestartDelay(remaining - 1<tickDelta>, plan), []
        | MatchFlow.GoalPause(remaining, side, scorer) ->
            if remaining <= 1<tickDelta> then
                MatchFlow.GoalPause(0<tickDelta>, side, scorer),
                [ { Tick = tick
                    Data = EventData.SetPiece(SetPieceKind.KickOff, MatchWorld.oppOf side) } ]
            else
                MatchFlow.GoalPause(remaining - 1<tickDelta>, side, scorer), []
        | MatchFlow.VARReview(remaining, incident) ->
            if remaining <= 1<tickDelta> then
                MatchFlow.VARReview(0<tickDelta>, incident), []
            else
                MatchFlow.VARReview(remaining - 1<tickDelta>, incident), []
        | MatchFlow.InjuryPause(remaining) ->
            if remaining <= 1<tickDelta> then
                let side = world.State.LastAttackingClub |> Option.defaultValue ClubSide.Home

                MatchFlow.InjuryPause 0<tickDelta>,
                [ { Tick = tick
                    Data = EventData.SetPiece(SetPieceKind.FreeKick, side) } ]
            else
                MatchFlow.InjuryPause(remaining - 1<tickDelta>), []
        | MatchFlow.HalfTime(remaining) ->
            if remaining <= 1<tickDelta> then
                MatchFlow.HalfTime 0<tickDelta>,
                [ { Tick = tick
                    Data = EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Away) } ]
            else
                MatchFlow.HalfTime(remaining - 1<tickDelta>), []
        | MatchFlow.ExtraTimeBreak(remaining) ->
            if remaining <= 1<tickDelta> then
                MatchFlow.ExtraTimeBreak 0<tickDelta>,
                [ { Tick = tick
                    Data = EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Home) } ]
            else
                MatchFlow.ExtraTimeBreak(remaining - 1<tickDelta>), []
        | MatchFlow.FullTimeReview(remaining) ->
            if remaining <= 1<tickDelta> then
                MatchFlow.MatchEnded, []
            else
                MatchFlow.FullTimeReview(remaining - 1<tickDelta>), []
        | MatchFlow.Shootout -> flow, []
        | MatchFlow.MatchEnded -> flow, []
