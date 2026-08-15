namespace FootballEngine.Core.Match

/// Engine-local domain enums. Re-declared here so match code never opens FootballEngine.Domain.
[<Struct>]
type ClubSide =
    | Home
    | Away

[<Struct>]
type PlayerId = PlayerId of int

type SetPieceKind =
    | KickOff
    | ThrowIn
    | Corner
    | GoalKick
    | FreeKick
    | Penalty

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
