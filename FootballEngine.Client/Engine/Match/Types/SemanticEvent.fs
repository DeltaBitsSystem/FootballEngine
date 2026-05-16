namespace FootballEngine.Types

open FootballEngine.Domain
open SchedulingTypes

type SemanticEvent =
    | BallSecured of side: ClubSide * playerId: PlayerId
    | BallLost of side: ClubSide * playerId: PlayerId
    | BallLoose
    | PassLaunched of from: PlayerId * target: PlayerId
    | ShotLaunched of shooter: PlayerId
    | FoulOccurred of fouler: PlayerId * fouled: PlayerId
    | GoalScored of side: ClubSide * scorer: PlayerId option
    | SetPieceAwarded of kind: SetPieceKind * team: ClubSide
    | PlayerConditionCritical of playerId: PlayerId * condition: int
    | RedCardIssued of playerId: PlayerId
    | MomentumShifted of toSide: ClubSide

module SemanticEvent =
    let kind (e: SemanticEvent) : SemanticEventKind =
        match e with
        | BallSecured(_, _) -> SemanticEventKind.BallSecured
        | BallLost(_, _) -> SemanticEventKind.BallLost
        | BallLoose -> SemanticEventKind.BallLoose
        | PassLaunched(_, _) -> SemanticEventKind.PassLaunched
        | ShotLaunched(_) -> SemanticEventKind.ShotLaunched
        | FoulOccurred(_, _) -> SemanticEventKind.FoulOccurred
        | GoalScored(_, _) -> SemanticEventKind.GoalScored
        | SetPieceAwarded(_, _) -> SemanticEventKind.SetPieceAwarded
        | PlayerConditionCritical(_, _) -> SemanticEventKind.PlayerConditionCritical
        | RedCardIssued(_) -> SemanticEventKind.RedCardIssued
        | MomentumShifted(_) -> SemanticEventKind.MomentumShifted
