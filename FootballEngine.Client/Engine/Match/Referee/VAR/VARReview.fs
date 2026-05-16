namespace FootballEngine

open FootballEngine.Domain

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open Stats

module VARReview =

    let reviewDuration (subTick: int) : int<tickDelta> = normalInt 45.0 10.0 30 60 |> fun x -> x * 1<tickDelta>

    let evaluateGoal
        (state: SimState)
        (scoringClub: ClubSide)
        (scorerId: PlayerId option)
        (isOwnGoal: bool)
        : VARDecision =
        let offsideActive =
            state.PendingOffsideSnapshot
            |> Option.map MatchSpatial.isOffsideFromSnapshot
            |> Option.defaultValue false

        if offsideActive then Overturn else CheckComplete

    let evaluatePenalty (state: SimState) (awardedTo: ClubSide) (foulX: float) (foulY: float) : VARDecision =
        let x = float32 foulX
        let y = float32 foulY

        let inArea =
            match awardedTo with
            | HomeClub ->
                x <= float32 PenaltyAreaDepth
                && y >= (float32 (PitchWidth / 2.0) - float32 PenaltyAreaHalfWidth)
                && y <= (float32 (PitchWidth / 2.0) + float32 PenaltyAreaHalfWidth)
            | AwayClub ->
                x >= float32 (PitchLength - PenaltyAreaDepth)
                && y >= (float32 (PitchWidth / 2.0) - float32 PenaltyAreaHalfWidth)
                && y <= (float32 (PitchWidth / 2.0) + float32 PenaltyAreaHalfWidth)

        if not inArea then Overturn else CheckComplete

    let evaluateRedCard (playerId: PlayerId) (incident: FoulSeverity) : VARDecision =
        match incident with
        | DOGSO
        | SeriousFoulPlay
        | ViolentConduct -> CheckComplete
        | _ -> NoIncident

    let evaluateOffside (state: SimState) (attackingPlayer: PlayerId) : VARDecision =
        match state.PendingOffsideSnapshot with
        | Some snap when snap.ReceiverId = attackingPlayer ->
            if MatchSpatial.isOffsideFromSnapshot snap then
                Overturn
            else
                CheckComplete
        | _ -> NoIncident

    let evaluate (state: SimState) (incident: VARReviewableIncident) : VARDecision =
        match incident with
        | GoalCheck(scoringClub, scorerId, isOwnGoal, _) -> evaluateGoal state scoringClub scorerId isOwnGoal
        | PenaltyCheck(awardedTo, fx, fy) -> evaluatePenalty state awardedTo fx fy
        | RedCardCheck(playerId, sev) -> evaluateRedCard playerId sev
        | OffsideCheck(attackingPlayer, _) -> evaluateOffside state attackingPlayer
