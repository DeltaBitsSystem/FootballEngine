namespace FootballEngine.Core.Match

[<Struct>]
type Spatial =
    { X: float<meter>
      Y: float<meter>
      Z: float<meter>
      Vx: float<meterPerSecond>
      Vy: float<meterPerSecond>
      Vz: float<meterPerSecond> }

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
      ArrivalGate: int<subtick> option }

/// O(1) counters; the pressure window lives in TeamWorld.EventWindow.
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

/// Countdown states. Exactly ONE path can start a goal flow: the Goal event apply.
type MatchFlow =
    | Live
    | GoalPause of remaining: int<tickDelta> * side: ClubSide * scorer: PlayerId option
    | VARReview of remaining: int<tickDelta> * incident: VARIncident
    | InjuryPause of remaining: int<tickDelta>
    | RestartDelay of remaining: int<tickDelta> * plan: RestartPlan
    | HalfTime of remaining: int<tickDelta>
    | FullTimeReview of remaining: int<tickDelta>
    | MatchEnded

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
      Stoppage: StoppageBook }
