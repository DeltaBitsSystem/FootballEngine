namespace FootballEngine

open FootballEngine.Domain

open FootballEngine.Types
open FootballEngine.Types.InfluenceTypes
open FootballEngine.Types.PhysicsContract

// ── SystemOutput ──────────────────────────────────────────────────────────────
//
// Protocolo universal de todos los sistemas del engine.
//
// INVARIANTE: ningún sistema muta SimState directamente.
// Cada sistema produce un SystemOutput.
// MatchStepper.apply es el único punto de escritura en todo el engine.
//
// Para agregar un sistema nuevo:
//   1. Agregar un case a SystemOutput si produce un tipo de cambio nuevo
//   2. Implementar el sistema como función pura que retorna SystemOutput list
//   3. Agregar el apply en MatchStepper.applyOutput
//   4. Activarlo desde EventRouter si corresponde
//
// Nunca hay paso 5. El compilador verifica el contrato.
// ─────────────────────────────────────────────────────────────────────────────

type FrameWrite =
    // Movimiento
    | SetPosition of slotIdx: int * x: float<meter> * y: float<meter>
    | SetVelocity of slotIdx: int * vx: float<meter / second> * vy: float<meter / second>
    | SetCondition of slotIdx: int * value: float32
    // Intent
    | SetIntent of slotIdx: int * kind: IntentKind * tx: float32 * ty: float32 * pid: int
    | CommitIntent of slotIdx: int * until: int * trigger: byte
    // Roles colectivos
    | SetSlotRole of slotIdx: int * role: SlotRole
    | SetCollectiveIntent of slotIdx: int * intent: CollectiveIntent
    | SetSupportPos of slotIdx: int * x: float32 * y: float32
    | SetDefensiveRole of slotIdx: int * role: DefensiveRole
    // Mental
    | SetMentalState of
        slotIdx: int *
        composure: float *
        confidence: float *
        aggression: float *
        focus: float *
        riskTolerance: float

and SystemOutput =
    // Escrituras en TeamFrame — el sistema dice qué cambiar, no cómo
    | HomeFrame of FrameWrite
    | AwayFrame of FrameWrite
    // Estado global del partido
    | BallUpdate of BallPhysicsState
    | FlowChange of MatchFlow
    | ScoreGoal of club: ClubSide * scorerId: PlayerId option * isOwnGoal: bool
    // Memoria y aprendizaje
    | EmergentUpdate of club: ClubSide * state: EmergentState
    | AdaptiveUpdate of club: ClubSide * state: AdaptiveState
    | DirectiveUpdate of club: ClubSide * directive: TeamDirectiveState
    | MemoryWrite of club: ClubSide * slotIdx: int * write: MemoryWrite
    // Runs
    | RegisterRun of club: ClubSide * run: RunAssignment
    | ExpireRun of club: ClubSide * playerId: PlayerId
    // Eventos de partido
    | Emit of MatchEvent
    | EmitSemantic of SemanticEvent
    // Tracking interno
    | PossessionHistoryUpdate of PossessionHistoryDelta
    // Caches derivados — computed, no domain state, pero deben pasar por applyOutput
    | InfluenceFrameUpdate of club: ClubSide * frame: InfluenceFrame
    | CognitiveFrameUpdate of club: ClubSide * frame: CognitiveFrame
    // BallXSmooth — exponential moving average de posición X del balón
    | BallXSmoothUpdate of value: float<meter>
    // Momentum
    | MomentumUpdate of delta: float
    // Club state mutations (RefereeApplicator / VAR action domain)
    | StoppageTimeAdd of subTick: int * reason: StoppageReason
    | SidelinedWrite of club: ClubSide * playerId: PlayerId * status: PlayerOut
    | YellowsWrite of club: ClubSide * playerId: PlayerId * count: int
    | LastAttackingClubSet of club: ClubSide
    | ScoreGoalAdjust of club: ClubSide * delta: int
    | MatchStatIncrement of club: ClubSide * field: StatField * delta: int

and StatField = | PassAttempts

and MemoryWrite =
    | PassFailure
    | PassSuccess
    | DuelResult of won: bool * opponentSlot: int

and [<Struct>] PossessionHistoryDelta =
    { PossessionChanged: bool
      BallInFlight: bool
      SetPieceAwarded: bool
      ReceivedByPlayer: PlayerId option }
