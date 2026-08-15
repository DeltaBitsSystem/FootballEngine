namespace FootballEngine.Player.Intent

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

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
