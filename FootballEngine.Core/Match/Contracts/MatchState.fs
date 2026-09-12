namespace FootballEngine.Core.Match

[<Struct>]
type Vec2 = { X: float<meter>; Y: float<meter> }

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
    { Top: float<radianPerSecond>
      Side: float<radianPerSecond> }

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
type BallState =
    { Position: Spatial
      Spin: Spin
      Control: BallControl
      Trajectory: TrajectoryIntent option
      LastTouch: PlayerId option
      StationarySince: int<subtick> option
      GKHoldSince: int<subtick> option
      ArrivalGate: int<subtick> option
      PreviousPosition: Spatial option
      Impulse: BallImpulse option
      PendingRestart: SetPieceKind option }

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
    | AdvantagePending of
        side: ClubSide *
        fouler: PlayerId *
        fouled: PlayerId *
        x: float<meter> *
        y: float<meter> *
        expiresAt: int<subtick>
    | AdvantageRealized of side: ClubSide * at: int<subtick>
    | AdvantageRecalled of side: ClubSide * at: int<subtick>
    | AdvantageExpired of side: ClubSide * at: int<subtick>

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
    | Live
    | GoalPause of remaining: int<tickDelta> * side: ClubSide * scorer: PlayerId option
    | VARReview of remaining: int<tickDelta> * incident: VARIncident
    | InjuryPause of remaining: int<tickDelta>
    | RestartDelay of remaining: int<tickDelta> * plan: RestartPlan
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
