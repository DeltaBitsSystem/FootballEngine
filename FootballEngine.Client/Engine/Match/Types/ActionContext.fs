namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract

type ActionContext =
    { Att: TeamPerspective
      Def: TeamPerspective
      Zone: PitchZone
      Momentum: float }

[<Struct>]
type AgentContext =
    { MeIdx: int
      Me: Player
      Profile: BehavioralProfile
      MentalState: MentalState
      MyCondition: float32
      MyPos: Spatial
      BallState: BallPhysicsState
      Team: TeamPerspective
      TeamHasBall: bool
      Phase: MatchPhase
      Zone: PitchZone
      NearestTeammateIdx: int voption
      NearestOpponentIdx: int voption
      ImmediatePressure: float
      BestPassTargetIdx: int voption
      BestPassTargetPos: Spatial voption
      BallCarrierOppIdx: int16
      DistToGoal: float<meter>
      GoalDiff: int
      Minute: int
      Urgency: float
      Tactics: TacticsConfig
      Decision: DecisionConfig
      BuildUp: BuildUpConfig
      Dribble: DribbleConfig
      PreviousIntent: MovementIntent voption
      PreviousIntentKind: IntentKind
      PreviousIntentTargetX: float32
      PreviousIntentTargetY: float32
      PreviousIntentTargetPid: int
      VisibilityMask: VisibilityMask voption
      CurrentSubTick: int
      TransitionPressExpiry: int
      Influence: InfluenceTypes.InfluenceFrame }
