namespace FootballEngine.Player.Decision

open FootballEngine.Types

[<Struct>]
type MatchSituationSnapshot =
    { MinuteNorm: float
      GoalDiff: int
      IsDrawLate: bool
      IsLosingLate: bool
      MomentumNorm: float
      PossessionPressure: float
      TeamInTransition: bool
      TeamPressingHigh: bool
      ShootMod: float
      PassMod: float
      DribbleMod: float
      LongBallMod: float }

[<Struct>]
type ActionScoreTrace =
    { RawScore: float
      SituationalMod: float
      FinalScore: float
      Blocked: bool
      BlockReason: string }

type DecisionTrace =
    { SubTick: int
      PlayerId: int
      Shoot: ActionScoreTrace
      Pass: ActionScoreTrace
      Dribble: ActionScoreTrace
      Cross: ActionScoreTrace
      LongBall: ActionScoreTrace
      ChosenAction: string
      SoftmaxTemperature: float
      MatchSituationSnapshot: MatchSituationSnapshot }
