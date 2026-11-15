namespace FootballEngine.Client.Animation

open System.Numerics
open FootballEngine.Core.Match

[<Struct>]
type MotionTime = { Tick: int; Fraction: float32 }

module MotionTime =
    let value time = float32 time.Tick + time.Fraction

    let ofTicks (ticks: float) =
        { Tick = int (floor ticks)
          Fraction = float32 (ticks - floor ticks) }

[<Struct>]
type MotionResidual =
    | UnreachableContact of actor: PlayerId * effector: MotionEffector * distance: float32
    | SupportConflict of actor: PlayerId * effector: MotionEffector
    | JointLimit of actor: PlayerId * joint: int * radians: float32
    | InsufficientPreparation of actor: PlayerId * taskId: uint64

type SupportPhase =
    | Support = 0
    | ToeOff = 1
    | Swing = 2
    | Landing = 3

[<Struct>]
type MotionSegment =
    { Start: float32
      Finish: float32
      From: Vector3
      Target: Vector3
      FromVelocity: Vector3
      TargetVelocity: Vector3
      Clearance: float32 }

[<Struct>]
type FootPlan =
    { Phase: SupportPhase
      Anchor: Vector3
      Segment: MotionSegment
      ReleaseAt: float32 }

[<Struct>]
type FootOverlay =
    { TaskId: uint64
      Sequence: uint64
      Active: bool
      PreparationSocketStart: float32
      RecoverySocketStart: float32
      PreparationSocketVelocity: float32
      RecoverySocketVelocity: float32
      Preparation: MotionSegment
      Recovery: MotionSegment }

module FootOverlay =
    let empty =
        let segment =
            { Start = 0.0f
              Finish = 1.0f
              From = Vector3.Zero
              Target = Vector3.Zero
              FromVelocity = Vector3.Zero
              TargetVelocity = Vector3.Zero
              Clearance = 0.0f }

        { TaskId = 0UL
          Sequence = 0UL
          Active = false
          PreparationSocketStart = 0.0f
          RecoverySocketStart = 0.0f
          PreparationSocketVelocity = 0.0f
          RecoverySocketVelocity = 0.0f
          Preparation = segment
          Recovery = segment }

[<Struct>]
type ActorPlan =
    { Root: MotionRootSample
      Tick: int
      Left: FootPlan
      Right: FootPlan
      LeftOverlay: FootOverlay
      RightOverlay: FootOverlay
      Acceleration: Vector3
      Lean: Vector3
      Gaze: System.Numerics.Vector2
      Seed: uint64 }

[<Struct>]
type TaskReservation =
    { Evidence: MotionEvidence
      PreparedAt: int
      CancelledAt: int
      Active: bool }

type MotionRuntimeError =
    | NonCanonicalBoundary of expected: int * received: int
    | ActorCapacityExceeded of capacity: int * received: int
    | WindowExpired of oldestTick: int
    | IncompatibleCheckpoint
    | StaleEpoch
    | RuntimeClosed
    | MotionIdentityNotConfigured
    | InvalidMotionBoundary
    | MotionSequenceGap of expectedSequence: uint64 * receivedSequence: uint64

type IMotionPlanSource =
    abstract TryCopyPlan: actor: PlayerId * time: MotionTime * plan: byref<ActorPlan> -> bool
