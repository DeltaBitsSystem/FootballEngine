namespace FootballEngine.Core.Match

/// Engine-local domain enums. Re-declared here so match code never opens FootballEngine.Domain.
[<Struct>]
type ClubSide =
    | Home
    | Away

[<Struct>]
type PlayerId = PlayerId of int

[<Struct>]
type ActionId = ActionId of uint64

module ActionId =

    let value (ActionId id) : uint64 = id

[<Struct>]
type RestartId = RestartId of uint64

module RestartId =

    let value (RestartId id) : uint64 = id

    let isValid (RestartId id) = id <> 0UL

type SetPieceKind =
    | KickOff
    | ThrowIn
    | Corner
    | GoalKick
    | FreeKick
    | Penalty

type StrikeKind =
    | Pass
    | Cross
    | Clearance
    | Shot
    | Restart of SetPieceKind

type ExecutionContext =
    | OpenPlay
    | SetPiece of SetPieceKind

[<Struct>]
type ContactFoot =
    | Left
    | Right

type FootTouchKind =
    | Carry
    | Receive
    | Kick of StrikeKind

type RestartRole =
    | Executor
    | Receiver
    | Wall
    | Goalkeeper
    | Support

type RestartAction =
    | RestartPass of receiver: PlayerId
    | RestartCross of target: float<meter> * float<meter>
    | RestartShot of target: float<meter> * float<meter>
    | RestartClearance of target: float<meter> * float<meter>

type RestartPlanChangeReason =
    | ExecutorInvalidated
    | ParticipantInvalidated of PlayerId

type FoulSeverity =
    | Minor
    | Tactical
    | YellowWorthy
    | RedWorthy

[<Struct>]
type CardColor =
    | Yellow
    | Red

[<Struct>]
type CardDecision =
    | NoCard
    | Yellow
    | Red

[<Struct>]
type VARIncident =
    | Offside
    | FoulInBox
    | BallOutOfPlay
    | Handball

type RestartCause =
    | MatchStart
    | PeriodStart
    | GoalConceded of scoringSide: ClubSide
    | BallCrossedTouchline of lastTouch: PlayerId option
    | BallCrossedGoalLine of lastTouch: PlayerId option
    | Foul of offender: PlayerId option * victim: PlayerId option
    | Offside of player: PlayerId
    | VAROverturn of VARIncident

type RestartAward =
    { Id: RestartId
      Cause: RestartCause
      Kind: SetPieceKind
      Side: ClubSide
      X: float<meter>
      Y: float<meter> }

[<Struct>]
type VARResult =
    | GoalStands
    | GoalOverturned
