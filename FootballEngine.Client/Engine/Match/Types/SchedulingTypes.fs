namespace FootballEngine.Types

open FootballEngine.Domain

[<Struct>]
type SemanticEventKind =
    | BallSecured
    | BallLost
    | BallLoose
    | PassLaunched
    | ShotLaunched
    | FoulOccurred
    | GoalScored
    | SetPieceAwarded
    | PlayerConditionCritical
    | RedCardIssued
    | MomentumShifted

type PlayerAction =
    | Shoot
    | Pass of target: Player
    | Dribble
    | Cross
    | LongBall
    | Tackle of opponent: Player
    | FreeKick
    | Corner
    | ThrowIn of side: ClubSide
    | Penalty of kicker: Player * side: ClubSide * kickNum: int

type FlowMatcher =
    | IsLive
    | IsGoalPause
    | IsHalfTime
    | IsRestartDelay
    | IsAnyPause
    | IsMatchEnded

type SystemFrequency =
    | EverySubtick
    | EveryN of int<tickDelta>
    | EveryMinute of period: int<matchMin> * offset: int<matchMin>
    | OnEvent of SemanticEventKind list
    | OnEventOrEveryN of SemanticEventKind list * int<tickDelta>
    | WhenFlow of FlowMatcher list * SystemFrequency

type MatchTime =
    { Subtick : int<subtick>
      Minute  : int<matchMin>
      Flow    : MatchFlow }

module SchedulingTypes =

    type OnBallIntent =
        | Shoot
        | Pass of target: PlayerId
        | Dribble
        | Cross
        | LongBall of target: PlayerId
        | Tackle of opponent: PlayerId
        | PassIntoSpace of targetCell: int
