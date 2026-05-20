namespace FootballEngine.Player.Actions

open FootballEngine
open FootballEngine.Domain
open FootballEngine.ML

open FootballEngine.Types.PhysicsContract


type xGModel =
    { DistanceToGoal: float<meter>
      AngleToGoal: float
      ShotType: ShotType
      BodyPart: string
      AssistType: string
      PressureLevel: float
      IsOneOnOne: bool
      IsSetPiece: bool }

module xGCalculator =

    let baseXG (dist: float<meter>) (angle: float) (w: XGWeights) : float =
        let d = float dist
        let a = angle
        let distFactor = MathPipelines.xgDistanceComponent w.DistanceFactor d
        let angleFactor = System.Math.Pow(a / 180.0, w.AngleExponent)
        distFactor * angleFactor * w.BaseMultiplier

    let adjustForShotType (xg: float) (shotType: ShotType) (w: XGWeights) : float =
        match shotType with
        | ShotType.Header -> xg * w.HeaderMultiplier
        | ShotType.Volley -> xg * w.VolleyMultiplier
        | ShotType.HalfVolley -> xg * w.HalfVolleyMultiplier
        | ShotType.ChipShot -> xg * w.ChipShotMultiplier
        | ShotType.Curler -> xg * w.CurlerMultiplier
        | ShotType.DrivenShot -> xg * w.DrivenShotMultiplier
        | ShotType.PlacedShot -> xg * w.PlacedShotMultiplier
        | ShotType.FirstTimeShot -> xg * w.FirstTimeShotMultiplier

    let adjustForPressure (xg: float) (pressure: float) (w: XGWeights) : float =
        xg * (1.0 - pressure * w.PressureReduction)

    let calculate (model: xGModel) (w: XGWeights) : float =
        let xg = baseXG model.DistanceToGoal model.AngleToGoal w
        let xg2 = adjustForShotType xg model.ShotType w
        let xg3 = adjustForPressure xg2 model.PressureLevel w
        let xg4 = if model.IsOneOnOne then xg3 * w.OneOnOneMultiplier else xg3
        let xg5 = if model.IsSetPiece then xg4 * w.SetPieceMultiplier else xg4
        min 1.0 (max 0.0 xg5)

    let calculateWithDefaults (model: xGModel) : float =
        let w = EngineWeightDefaults.defaults.Outcomes.XG
        calculate model w

    let baseXGWithDefaults (dist: float<meter>) (angle: float) : float =
        let w = EngineWeightDefaults.defaults.Outcomes.XG
        baseXG dist angle w
