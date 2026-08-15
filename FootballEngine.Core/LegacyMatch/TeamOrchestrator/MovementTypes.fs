namespace FootballEngine.TeamOrchestrator

open FootballEngine.PhysicsContract

type RunType =
    | DeepRun
    | OverlapRun
    | UnderlapRun
    | DiagonalRun
    | CheckToBall
    | DriftWide
    | ThirdManRun
    | FalseNineDrop
    | WingBackSurge

type RunTrigger =
    | TeammateHasBall
    | TeammateStartedDribble
    | SpaceDetected
    | TacticalInstruction
    | SetPieceRoutine
    | CounterAttack

type RunTrajectory =
    | Linear of float<meter> * float<meter> * float<meter> * float<meter>
    | Waypoints of (float<meter> * float<meter>)[]


type MovementMode =
    | OpenPlay
    | SetPiece of SetPieceRole

and SetPieceRole =
    | CornerAttack
    | CornerDefend
    | FreeKickAttack
    | FreeKickDefend
    | ThrowInNear
    | ThrowInFar

type MovementRole =
    | BallCarrier
    | SupportPlayer
    | ShadowPlayer
    | PressingPlayer
    | CoveringPlayer
    | MarkerPlayer
    | RunPlayer
    | SetPiecePlayer
