namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract


[<Struct>]
type SetPieceKind =
    | KickOff
    | ThrowIn
    | Corner
    | GoalKick
    | FreeKick
    | Penalty

type RestartCause =
    | AfterGoal
    | AfterFoul
    | AfterBallOut
    | AfterInjury
    | AfterVAR
    | InitialKickOff

type RestartPlan =
    { Kind: SetPieceKind
      Team: ClubSide
      Cause: RestartCause
      RemainingTicks: int<tickDelta> }

type GoalPauseState =
    { ScoringTeam: ClubSide
      ScorerId: PlayerId option
      IsOwnGoal: bool
      RemainingTicks: int<tickDelta>
      VARRequested: bool }

type InjuryPauseState =
    { PlayerId: PlayerId
      Team: ClubSide
      Severity: int
      RemainingTicks: int<tickDelta>
      CanContinue: bool option }

type VARPhase =
    | CheckingIncident
    | ReviewingAngles
    | RefereeToMonitor
    | DecisionReady

type VARFlowState =
    { Incident: VARReviewableIncident
      Phase: VARPhase
      RemainingTicks: int<tickDelta>
      TotalTicks: int<tickDelta> }

type MatchFlow =
    | Live
    | GoalPause of GoalPauseState
    | VARReview of VARFlowState
    | InjuryPause of InjuryPauseState
    | RestartDelay of RestartPlan
    | HalfTimePause of remainingTicks: int<tickDelta>
    | FullTimeReview
    | MatchEnded


type ArrivalWinner =
    | IntendedTarget of player: Player * club: ClubSide
    | Intercepted of player: Player * club: ClubSide
    | Contested
    | NoOneInRange

type OffsideSnapshot =
    { PasserId: PlayerId
      ReceiverId: PlayerId
      ReceiverXAtPass: float<meter>
      SecondLastDefenderX: float<meter>
      BallXAtPass: float<meter>
      Dir: AttackDir }
