namespace FootballEngine

open FootballEngine.Domain

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

type MemoryWriteKind =
    | PassFailure
    | PassSuccess
    | DuelResult of won: bool * opponentSlot: int

type StatField = | PassAttempts

[<Struct>]
type PossessionHistoryDelta =
    { PossessionChanged: bool
      BallInFlight: bool
      SetPieceAwarded: bool
      ReceivedByPlayer: PlayerId option }


type DomainEvent =
    | BallUpdate of BallPhysicsState
    | BallXSmooth of value: float<meter>
    | FlowChange of MatchFlow
    | ScoreGoal of club: ClubSide * scorerId: PlayerId option * isOwnGoal: bool
    | ScoreGoalAdjust of club: ClubSide * delta: int
    | MomentumDelta of delta: float
    | MemoryWrite of club: ClubSide * slotIdx: int * write: MemoryWriteKind
    | RegisterRun of club: ClubSide * run: RunAssignment
    | ExpireRun of club: ClubSide * playerId: PlayerId
    | EmergentUpdate of club: ClubSide * state: EmergentState
    | AdaptiveUpdate of club: ClubSide * state: AdaptiveState
    | DirectiveUpdate of club: ClubSide * directive: TeamDirectiveState
    | PossessionHistoryUpdate of PossessionHistoryDelta
    | EmitSemantic of SemanticEvent
    | Emit of MatchEvent
    | StoppageTimeAdd of subTick: int * reason: StoppageReason
    | SidelinedWrite of club: ClubSide * playerId: PlayerId * status: PlayerOut
    | YellowsWrite of club: ClubSide * playerId: PlayerId * count: int
    | LastAttackingClubSet of club: ClubSide
    | MatchStatIncrement of club: ClubSide * field: StatField * delta: int
