namespace FootballEngine

[<Struct>]
type MovementScores =
    { MaintainShape: float
      MarkMan: float
      PressBall: float
      CoverSpace: float
      SupportAttack: float
      RecoverBall: float
      MoveToSetPiecePos: float }

[<Struct>]
type CollectiveIntent =
    { MaintainShape: float
      MarkMan: float
      PressBall: float
      CoverSpace: float
      SupportAttack: float
      RecoverBall: float }

module CollectiveIntent =
    let neutral =
        { MaintainShape = 1.0
          MarkMan = 1.0
          PressBall = 1.0
          CoverSpace = 1.0
          SupportAttack = 1.0
          RecoverBall = 1.0 }
