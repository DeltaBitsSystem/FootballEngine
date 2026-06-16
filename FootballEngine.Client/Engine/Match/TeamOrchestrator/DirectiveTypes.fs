namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Domain

type PressParams =
    { Intensity: float
      TriggerZone: PitchZone
      MinPresserCount: int }

type ShapeParams =
    { Width: float
      DefensiveLineHeight: float
      Compactness: float }

type TransitionParams =
    { Tempo: float
      DirectnessThreshold: float
      CounterTrigger: bool
      WingBias: float // -0.3 = forzar izquierda, 0.0 = neutro, 0.3 = forzar derecha
      DirectnessBias: float } // >0 = más directo/longball, <0 = más corto

type DirectiveParams =
    { Press: PressParams
      Shape: ShapeParams
      Transition: TransitionParams }

type DirectiveKind =
    | Structured
    | DirectAttack
    | PressingBlock
    | DefensiveBlock
    | CounterReady
    | ContestBall

type TeamDirective =
    { Kind: DirectiveKind
      Params: DirectiveParams
      TargetRunner: PlayerId option
      RunType: RunType option
      RunTarget: Spatial option
      ActiveSince: int }

type TeamDirectiveState =
    | Active of TeamDirective
    | Transitioning of from: TeamDirective * toDir: TeamDirective * progress: float
    | Suspended of TeamDirective
