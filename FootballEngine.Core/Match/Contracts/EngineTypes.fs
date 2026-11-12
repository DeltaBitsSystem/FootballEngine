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

[<Struct>]
type ContactId = ContactId of uint64

module ContactId =

    let value (ContactId id) : uint64 = id

[<Struct>]
type ContactFoot =
    | Left
    | Right

[<Struct>]
type BodySupport =
    | NoBodySupport
    | LeftFootSupport
    | RightFootSupport
    | DoubleFootSupport

[<Struct>]
type BodyContactStatus =
    | BodyStable
    | BodyStaggering
    | BodyFalling
    | BodyRecovering

[<Struct; RequireQualifiedAccess>]
type ContactSurface =
    | ContactTorso
    | ContactHead
    | ContactLeg
    | ContactFoot
    | ContactHand
    | ContactBall

/// The part of the boot that meets a kicked ball. It decides the spin and the precision of the strike, and the
/// viewer chooses the swing that matches it.
[<Struct; RequireQualifiedAccess>]
type StrikeSurface =
    | Inside
    | Instep
    | Outside

type ContactActionKind =
    | ContactContain
    | ContactShield
    | ContactShoulderChallenge
    | ContactStandingTackle
    | ContactSlidingTackle
    | ContactTrap
    | ContactStrike
    | ContactHeader

[<Struct>]
type ContactActionPhase =
    | ContactActionPreparing
    | ContactActionCommitted
    | ContactActionRecovering
    | ContactActionCompleted
    | ContactActionCancelled

[<Struct>]
type ContactControlKind =
    | UncontrolledBall
    | ControlledBall
    | ReceivingBall
    | ContestedBall
    | HeldBall

[<Struct>]
type ContactControlSnapshot =
    { Kind: ContactControlKind
      Side: ClubSide option
      Player: PlayerId option }

[<Struct>]
type BodyContactState =
    { Support: BodySupport
      Stability: float32
      PerturbationX: float32
      PerturbationY: float32
      AngularPerturbation: float32
      Status: BodyContactStatus
      EnteredAt: int<subtick>
      ExitsAt: int<subtick>
      LastContactAt: int<subtick> }

module BodyContactState =

    let stable =
        { Support = BodySupport.DoubleFootSupport
          Stability = 1.0f
          PerturbationX = 0.0f
          PerturbationY = 0.0f
          AngularPerturbation = 0.0f
          Status = BodyContactStatus.BodyStable
          EnteredAt = 0<subtick>
          ExitsAt = 0<subtick>
          LastContactAt = 0<subtick> }

type ContactAction =
    { Id: ActionId
      Actor: PlayerId
      Generation: int
      Kind: ContactActionKind
      Target: PlayerId option
      Phase: ContactActionPhase
      StartedAt: int<subtick>
      CommitAt: int<subtick>
      EndsAt: int<subtick>
      Foot: ContactFoot option
      PredictedX: float<meter>
      PredictedY: float<meter>
      PredictedZ: float<meter>
      ErrorX: float32
      ErrorY: float32
      ErrorZ: float32 }

[<Struct>]
type ContactRecord =
    { Id: ContactId
      Actor: PlayerId
      ActorGeneration: int
      Other: PlayerId option
      OtherGeneration: int option
      Action: ActionId option
      ActionKind: ContactActionKind option
      Tick: int<subtick>
      Fraction: float
      PointX: float<meter>
      PointY: float<meter>
      PointZ: float<meter>
      NormalX: float
      NormalY: float
      NormalZ: float
      SurfaceA: ContactSurface
      SurfaceB: ContactSurface
      RelativeVelocityX: float<meterPerSecond>
      RelativeVelocityY: float<meterPerSecond>
      RelativeVelocityZ: float<meterPerSecond>
      NormalImpulse: float
      TangentialImpulse: float
      SupportBefore: BodySupport
      SupportAfter: BodySupport
      BallTouched: bool
      BallTouchOrder: int option
      ControlBefore: ContactControlSnapshot
      ControlAfter: ContactControlSnapshot }

[<Struct>]
type RestartId = RestartId of uint64

module RestartId =

    let value (RestartId id) : uint64 = id

    let isValid (RestartId id) = id <> 0UL

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

type FootTouchKind =
    | Carry
    | Receive
    | Kick of StrikeKind

type RestartRole =
    | Executor
    | Receiver
    | Wall
    | Goalkeeper
    | Support

type RestartAction =
    | RestartPass of receiver: PlayerId
    | RestartCross of target: float<meter> * float<meter>
    | RestartShot of target: float<meter> * float<meter>
    | RestartClearance of target: float<meter> * float<meter>

type RestartPlanChangeReason =
    | ExecutorInvalidated
    | ParticipantInvalidated of PlayerId
    | PlanningFailed

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

type RestartCause =
    | MatchStart
    | PeriodStart
    | GoalConceded of scoringSide: ClubSide
    | BallCrossedTouchline of lastTouch: PlayerId option
    | BallCrossedGoalLine of lastTouch: PlayerId option
    | Foul of offender: PlayerId option * victim: PlayerId option
    | Offside of player: PlayerId
    | VAROverturn of VARIncident

[<Struct>]
type Vec2 = { X: float<meter>; Y: float<meter> }

module Vec2 =

    let create (x: float<meter>) (y: float<meter>) : Vec2 = { X = x; Y = y }

[<Struct>]
type RestartTarget =
    { Side: ClubSide
      Player: PlayerId
      Role: RestartRole
      Position: Vec2 }

type RestartPlan =
    { Id: RestartId
      Cause: RestartCause
      Kind: SetPieceKind
      Side: ClubSide
      Spot: Vec2
      Executor: PlayerId
      Targets: RestartTarget array
      Action: RestartAction
      AwardTick: int<subtick> }

type RestartAward =
    { Id: RestartId
      Cause: RestartCause
      Kind: SetPieceKind
      Side: ClubSide
      X: float<meter>
      Y: float<meter> }

[<Struct>]
type VARResult =
    | GoalStands
    | GoalOverturned
