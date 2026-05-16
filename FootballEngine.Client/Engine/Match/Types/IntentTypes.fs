namespace FootballEngine.Types

open FootballEngine.Domain


module IntentPhaseTypes =
    [<Literal>]
    let Deciding = 0uy

    [<Literal>]
    let Executing = 1uy

    type ExitTrigger =
        | None = 0uy
        | PossessionChanged = 1uy
        | BallInFlight = 2uy
        | BallReceived = 3uy
        | SetPieceAwarded = 4uy

[<Struct>]
type MovementIntent =
    | MaintainShape of target: Spatial
    | MarkMan of targetPlayerId: PlayerId * targetPos: Spatial
    | PressBall of ballPredPos: Spatial
    | ExecuteRun of assignment: RunAssignment
    | CoverSpace of target: Spatial
    | SupportAttack of target: Spatial
    | RecoverBall of ballPredPos: Spatial
    | MoveToSetPiecePos of target: Spatial

[<Struct>]
type VisibilityMask =
    { CanSeeTeammates: bool[]
      CanSeeOpponents: bool[]
      CanSeeBall: bool
      VisibleTeammateCount: int
      VisibleOpponentCount: int }

module VisibilityMask =
    let empty ownCount oppCount =
        { CanSeeTeammates = Array.create ownCount true
          CanSeeOpponents = Array.create oppCount true
          CanSeeBall = true
          VisibleTeammateCount = ownCount
          VisibleOpponentCount = oppCount }

[<Struct>]
type CognitiveFrame =
    { NearestTeammateIdx: int16[]
      NearestTeammateDistSq: float32[]
      NearestOpponentIdx: int16[]
      NearestOpponentDistSq: float32[]
      BestPassTargetIdx: int16[]
      BestPassTargetPos: Spatial voption[]
      BallX: float32
      BallY: float32
      BallZone: PitchZone
      Phase: MatchPhase
      PressureOnPlayer: float32[]
      SlotCount: int
      BallCarrierOppIdx: int16 }

module CognitiveFrameDefaults =
    let empty =
        { NearestTeammateIdx = Array.empty
          NearestTeammateDistSq = Array.empty
          NearestOpponentIdx = Array.empty
          NearestOpponentDistSq = Array.empty
          BestPassTargetIdx = Array.empty
          BestPassTargetPos = Array.empty
          BallX = 0.0f
          BallY = 0.0f
          BallZone = MidfieldZone
          Phase = BuildUp
          PressureOnPlayer = Array.empty
          SlotCount = 0
          BallCarrierOppIdx = -1s }

[<Struct>]
type CognitiveFrameBuffers =
    { NearestTeammateIdx: int16[]
      NearestTeammateDistSq: float32[]
      NearestOpponentIdx: int16[]
      NearestOpponentDistSq: float32[]
      BestPassTargetIdx: int16[]
      BestPassTargetPos: Spatial voption[]
      PressureOnPlayer: float32[]
      LaneClear: bool[] }

module CognitiveFrameBuffers =
    let create n =
        { NearestTeammateIdx = Array.zeroCreate<int16> n
          NearestTeammateDistSq = Array.create<float32> n System.Single.MaxValue
          NearestOpponentIdx = Array.zeroCreate<int16> n
          NearestOpponentDistSq = Array.create<float32> n System.Single.MaxValue
          BestPassTargetIdx = Array.create<int16> n -1s
          BestPassTargetPos = Array.create<Spatial voption> n ValueNone
          PressureOnPlayer = Array.create<float32> n System.Single.MaxValue
          LaneClear = Array.create<bool> (n * n) true }


    let copyIntoCFrameBuffers (buffers: CognitiveFrameBuffers option) (frame: CognitiveFrame) =
        match buffers with
        | Some buf ->
            let n = min frame.SlotCount buf.NearestTeammateIdx.Length
            Array.blit frame.NearestTeammateIdx 0 buf.NearestTeammateIdx 0 n
            Array.blit frame.NearestTeammateDistSq 0 buf.NearestTeammateDistSq 0 n
            Array.blit frame.NearestOpponentIdx 0 buf.NearestOpponentIdx 0 n
            Array.blit frame.NearestOpponentDistSq 0 buf.NearestOpponentDistSq 0 n
            Array.blit frame.BestPassTargetIdx 0 buf.BestPassTargetIdx 0 n
            Array.blit frame.BestPassTargetPos 0 buf.BestPassTargetPos 0 n
            Array.blit frame.PressureOnPlayer 0 buf.PressureOnPlayer 0 n
        | None -> ()

type IntentKind =
    | Idle = 0uy
    | MaintainShape = 1uy
    | MarkMan = 2uy
    | PressBall = 3uy
    | ExecuteRun = 4uy
    | CoverSpace = 5uy
    | SupportAttack = 6uy
    | RecoverBall = 7uy
    | MoveToSetPiecePos = 8uy
    | TackleAttempt = 9uy

// INVARIANT: CommittedUntil and CommittedAt store int<subtick> without measure.
// The contract is enforced by the signatures of commitIntent and shouldRecalculate,
// not by the array type itself.
type IntentDataFrame =
    { Kind: IntentKind[]
      TargetX: float32[]
      TargetY: float32[]
      TargetPid: int[]
      Phase: byte[]
      CommittedUntil: int[]
      CommittedAt: int[]
      ExitTrigger: byte[] }

module IntentFrameOps =
    let init (n: int) : IntentDataFrame =
        { Kind = Array.create n IntentKind.Idle
          TargetX = Array.zeroCreate n
          TargetY = Array.zeroCreate n
          TargetPid = Array.zeroCreate n
          Phase = Array.create n IntentPhaseTypes.Deciding
          CommittedUntil = Array.zeroCreate n
          CommittedAt = Array.zeroCreate n
          ExitTrigger = Array.create n (byte IntentPhaseTypes.ExitTrigger.None) }
