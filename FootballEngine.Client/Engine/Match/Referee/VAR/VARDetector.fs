namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types

module VARDetector =

    let detectGoalCheck
        (scoringClub: ClubSide)
        (scorerId: PlayerId option)
        (isOwnGoal: bool)
        (subTick: int<subtick>)
        : VARReviewableIncident option =
        Some(GoalCheck(scoringClub, scorerId, isOwnGoal, subTick))

    let detectPenaltyCheck (awardedTo: ClubSide) (foulX: float) (foulY: float) : VARReviewableIncident option =
        Some(PenaltyCheck(awardedTo, foulX, foulY))

    let detectRedCardCheck (playerId: PlayerId) (incident: FoulSeverity) : VARReviewableIncident option =
        match incident with
        | DOGSO -> Some(RedCardCheck(playerId, incident))
        | SeriousFoulPlay -> Some(RedCardCheck(playerId, incident))
        | ViolentConduct -> Some(RedCardCheck(playerId, incident))
        | _ -> None

    let shouldReview (incident: VARReviewableIncident) : bool =
        match incident with
        | GoalCheck _ -> true
        | PenaltyCheck _ -> true
        | RedCardCheck _ -> true
        | OffsideCheck _ -> true
