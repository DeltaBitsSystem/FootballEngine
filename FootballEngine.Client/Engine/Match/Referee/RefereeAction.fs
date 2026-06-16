namespace FootballEngine

open FootballEngine.Domain

type RefereeAction =
    | ConfirmGoal of scoringClub: ClubSide * scorerId: PlayerId option * isOwnGoal: bool
    | AnnulGoal
    | IssueYellow of player: Player * clubId: ClubId
    | IssueRed of player: Player * clubId: ClubId
    | IssueInjury of player: Player * clubId: ClubId
    | AwardThrowIn of team: ClubSide
    | AwardCorner of team: ClubSide
    | AwardGoalKick of team: ClubSide
    | AwardIndirectFreeKick of team: ClubSide
    | DropBall of team: ClubSide
    | RefereeIdle
