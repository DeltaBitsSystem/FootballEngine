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
        { MaintainShape = 0.5
          MarkMan = 0.5
          PressBall = 0.5
          CoverSpace = 0.5
          SupportAttack = 0.5
          RecoverBall = 0.5 }

    // merge: lo que el jugador quiere (MovementScores) vs lo que el equipo necesita (CollectiveIntent)
    // compliance determina cuánto pesa el equipo — WorkRate * condición * urgencia
    let merge (individual: MovementScores) (collective: CollectiveIntent) (w: float) : MovementScores =
        let i = 1.0 - w

        { MaintainShape = individual.MaintainShape * i + collective.MaintainShape * w
          MarkMan = individual.MarkMan * i + collective.MarkMan * w
          PressBall = individual.PressBall * i + collective.PressBall * w
          CoverSpace = individual.CoverSpace * i + collective.CoverSpace * w
          SupportAttack = individual.SupportAttack * i + collective.SupportAttack * w
          RecoverBall = individual.RecoverBall * i + collective.RecoverBall * w
          MoveToSetPiecePos = individual.MoveToSetPiecePos }
