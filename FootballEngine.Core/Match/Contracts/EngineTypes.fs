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

[<Struct>]
type VARResult =
    | GoalStands
    | GoalOverturned

[<Struct>]
type RestartPlan =
    { Kind: SetPieceKind
      Side: ClubSide
      X: float<meter>
      Y: float<meter> }
