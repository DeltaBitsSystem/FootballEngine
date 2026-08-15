namespace FootballEngine.Types

open FootballEngine.Domain

[<Struct>]
type MatchSituation =
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
