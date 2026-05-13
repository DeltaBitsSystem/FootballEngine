namespace FootballEngine.Types

open FootballEngine.Domain

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
