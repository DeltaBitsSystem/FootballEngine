namespace FootballEngine.Types

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
      ActiveSince: int<subtick> }

type TeamDirectiveState =
    | Active of TeamDirective
    | Transitioning of from: TeamDirective * toDir: TeamDirective * progress: float
    | Suspended of TeamDirective

module TeamDirectiveOps =

    let kindFromTactics (teamTactics: TeamTactics) : DirectiveKind =
        match teamTactics with
        | TeamTactics.Attacking -> DirectAttack
        | TeamTactics.Pressing -> PressingBlock
        | TeamTactics.Defensive -> DefensiveBlock
        | TeamTactics.Counter -> CounterReady
        | _ -> Structured

    let suspend (state: TeamDirectiveState) : TeamDirectiveState =
        match state with
        | TeamDirectiveState.Active d -> TeamDirectiveState.Suspended d
        | TeamDirectiveState.Transitioning(_, d, _) -> TeamDirectiveState.Suspended d
        | TeamDirectiveState.Suspended _ as s -> s

    let resume (state: TeamDirectiveState) : TeamDirectiveState =
        match state with
        | TeamDirectiveState.Suspended d -> TeamDirectiveState.Active d
        | other -> other

    let currentDirective (state: TeamDirectiveState) : TeamDirective option =
        match state with
        | TeamDirectiveState.Active d -> Some d
        | TeamDirectiveState.Transitioning(_, d, _) -> Some d
        | TeamDirectiveState.Suspended d -> Some d

    let empty (subTick: int<subtick>) : TeamDirective =
        { Kind = Structured
          Params =
            { Press =
                { Intensity = 0.5
                  TriggerZone = MidfieldZone
                  MinPresserCount = 3 }
              Shape =
                { Width = 0.5
                  DefensiveLineHeight = 0.0
                  Compactness = 0.5 }
              Transition =
                { Tempo = 0.5
                  DirectnessThreshold = 0.5
                  CounterTrigger = false
                  WingBias = 0.0
                  DirectnessBias = 0.0 } }
          TargetRunner = None
          RunType = None
          RunTarget = None
          ActiveSince = subTick }
