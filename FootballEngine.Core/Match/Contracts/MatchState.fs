namespace FootballEngine.Core.Match

[<Struct>]
type Vec2 = { X: float<meter>; Y: float<meter> }

module Vec2 =

    let create (x: float<meter>) (y: float<meter>) : Vec2 = { X = x; Y = y }

[<Struct>]
type Vec3 =
    { X: float<meter>
      Y: float<meter>
      Z: float<meter> }

[<Struct>]
type LinearVelocity =
    { X: float<meterPerSecond>
      Y: float<meterPerSecond>
      Z: float<meterPerSecond> }

[<Struct>]
type AngularVelocity =
    { Top: float<radianPerSecond>
      Side: float<radianPerSecond>
      Roll: float<radianPerSecond> }

[<Struct>]
type GoalCrossing =
    { Side: ClubSide
      Position: Vec3
      Fraction: float }

[<Struct>]
type Spatial =
    { X: float<meter>
      Y: float<meter>
      Z: float<meter>
      Vx: float<meterPerSecond>
      Vy: float<meterPerSecond>
      Vz: float<meterPerSecond> }

[<Struct>]
type BallImpulse =
    { ActionId: ActionId
      RestartId: RestartId option
      ActorId: PlayerId
      Origin: Vec3
      LinearVelocity: LinearVelocity
      AngularVelocity: AngularVelocity
      IntendedTarget: Vec2 option
      StrikeKind: StrikeKind
      ContactTick: SimTick
      ExecutionContext: ExecutionContext }

[<Struct>]
type Spin =
    { X: float<radianPerSecond>
      Y: float<radianPerSecond>
      Z: float<radianPerSecond> }

type BallControl =
    | Free
    | Airborne
    | Receiving of side: ClubSide * player: PlayerId * since: int<subtick>
    | Controlled of side: ClubSide * player: PlayerId
    | Contesting of side: ClubSide

type TrajectoryIntent =
    | Aimed of passer: PlayerId * receiver: PlayerId * quality: float
    | Struck of shooter: PlayerId * speed: float<meterPerSecond> * angle: float * xG: float
    | Cleared
    | Uncontrolled

[<Struct>]
type FootMotion =
    // Replay poses describe the end-of-step boundary, while event ticks identify the producing step.
    { Actor: PlayerId
      Foot: ContactFoot
      Kind: FootTouchKind
      StartedAt: int<subtick>
      ContactAt: int<subtick>
      EndsAt: int<subtick>
      Point: Vec3
      BallCenter: Vec3 }

type PendingFootStrike =
    { MotionTaskId: uint64
      Impulse: BallImpulse
      Trajectory: TrajectoryIntent
      ArrivalGate: int<subtick> option
      Side: ClubSide
      Slot: int }

/// The most recent contact with a goal frame; Goal is the side that defends that goal.
[<Struct>]
type GoalFrameContact =
    { Tick: int<subtick>
      Part: GoalFramePart
      Goal: ClubSide
      Position: Vec3 }

[<Struct>]
type BallState =
    { Position: Spatial
      Radius: float<meter>
      Spin: Spin
      Orientation: System.Numerics.Quaternion
      FootMotion: FootMotion option
      PendingStrike: PendingFootStrike option
      Control: BallControl
      Trajectory: TrajectoryIntent option
      LastTouch: PlayerId option
      StationarySince: int<subtick> option
      GKHoldSince: int<subtick> option
      ArrivalGate: int<subtick> option
      PreviousPosition: Spatial voption
      FrameContact: GoalFrameContact voption
      Impulse: BallImpulse option }

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

type RestartPhase =
    | Awarded
    | Positioning
    | Authorized
    | AwaitingContact
    | Completed

type RestartState =
    { Id: RestartId
      Cause: RestartCause
      Kind: SetPieceKind
      Side: ClubSide
      Spot: Vec2
      AwardTick: int<subtick>
      Plan: RestartPlan option
      Phase: RestartPhase
      CommandIssued: bool
      ContactObserved: bool
      Replanned: bool }

/// O(1) counters only — the pressure window lives in TeamWorld.EventWindow, so
/// one fact is never stored in two places.
[<Struct>]
type PossessionBook =
    { HomeTouches: int
      AwayTouches: int
      LastGainer: ClubSide option }

[<Struct>]
type StoppageBook =
    {
        FirstHalfAdded: int<subtick>
        SecondHalfAdded: int<subtick>
        Decided: bool
        /// goals*10000 + fouls*100 + cards*10 + subs at the 44th minute — lets the 89th-minute decision subtract half-1 events.
        Half1Fingerprint: int
    }

type AdvantageState =
    | NoAdvantage
    | AdvantagePending of incident: AdvantageIncident * expiresAt: int<subtick>
    | AdvantageRealized of incident: AdvantageIncident * at: int<subtick>
    | AdvantageRecalled of incident: AdvantageIncident * at: int<subtick>
    | AdvantageExpired of incident: AdvantageIncident * at: int<subtick>

[<Struct>]
type VARReviewState =
    { Incident: VARIncident
      ScoringSide: ClubSide
      Scorer: PlayerId option }

[<Struct>]
type ShootoutBook =
    { HomeGoals: int
      AwayGoals: int
      HomeKicks: int
      AwayKicks: int
      Scheduled: bool
      Complete: bool }

/// Countdown states. Exactly ONE path can start a goal flow: the Goal event apply.
type MatchFlow =
    | PreMatch
    | Live
    | GoalPause of remaining: int<tickDelta> * side: ClubSide * scorer: PlayerId option
    | VARReview of remaining: int<tickDelta> * incident: VARIncident
    | InjuryPause of remaining: int<tickDelta>
    | Restart of RestartState
    | HalfTime of remaining: int<tickDelta>
    | ExtraTimeBreak of remaining: int<tickDelta>
    | FullTimeReview of remaining: int<tickDelta>
    | Shootout
    | MatchEnded

/// The canonical match truth: tick, clock, flow, score, ball and momentum in
/// one place, read and written by every system.
[<Struct>]
type MatchState =
    { Tick: int<subtick>
      Clock: MatchClock
      Flow: MatchFlow
      HomeScore: int
      AwayScore: int
      Ball: BallState
      Momentum: float
      LastAttackingClub: ClubSide option
      Possession: PossessionBook
      Stoppage: StoppageBook
      Advantage: AdvantageState
      VARReview: VARReviewState option
      Shootout: ShootoutBook }
