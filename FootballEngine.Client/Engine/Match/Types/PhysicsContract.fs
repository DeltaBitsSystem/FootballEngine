namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Domain

module PhysicsContract =

    // ============================================================
    // UNIT OF MEASURE DEFINITIONS — Must precede all literals
    // ============================================================
    [<Measure>]
    type meter

    [<Measure>]
    type second

    [<Measure>]
    type meterPerSecond = meter / second

    [<Measure>]
    type meterPerSecondSquared = meter / second^2

    [<Measure>]
    type meterSquared = meter * meter

    [<Measure>]
    type radian

    [<Measure>]
    type radianPerSecond = radian / second

    [<Measure>]
    type kilogram

    [<Measure>]
    type newton = kilogram * meter / second^2

    // ============================================================
    // WORLD CONSTANTS — Never calibrate these
    // ============================================================
    [<Literal>]
    let PitchLength = 105.0<meter>

    [<Literal>]
    let PitchWidth = 68.0<meter>

    [<Literal>]
    let HalfwayLineX = 52.5<meter>

    [<Literal>]
    let GoalLineHome = 105.0<meter>

    [<Literal>]
    let GoalLineAway = 0.0<meter>

    [<Literal>]
    let PostNearY = 30.34<meter>

    [<Literal>]
    let PostFarY = 37.66<meter>

    [<Literal>]
    let CrossbarHeight = 2.44<meter>

    [<Literal>]
    let PenaltySpotDistance = 11.0<meter>

    [<Literal>]
    let PenaltyAreaDepth = 16.5<meter>

    [<Literal>]
    let PenaltyAreaHalfWidth = 20.16<meter>

    [<Literal>]
    let GoalAreaDepth = 5.5<meter>

    [<Literal>]
    let GoalAreaHalfWidth = 9.16<meter>

    [<Literal>]
    let CenterCircleRadius = 9.15<meter>

    [<Literal>]
    let PenaltyArcRadius = 9.15<meter>

    [<Literal>]
    let CornerArcRadius = 1.0<meter>

    // ============================================================
    // PLAYER PHYSICAL CONSTANTS — Calibratable via MovementPhysics
    // ============================================================
    [<Literal>]
    let AttrMax = 20.0

    let inline normaliseAttr (v: int) : float =
        float v / AttrMax

    let inline toScalar (v: float<'u>) : float = float v

    [<Literal>]
    let ConditionMax = 100.0

    let inline normaliseCondition v : float = float v / ConditionMax

    [<Literal>]
    let PlayerSpeedMax = 9.5<meter / second>

    [<Literal>]
    let PlayerSpeedMin = 1.2<meter / second>

    [<Literal>]
    let PlayerAccelMax = 6.5<meter / second^2>

    [<Literal>]
    let PlayerAccelMin = 2.0<meter / second^2>

    [<Literal>]
    let PlayerSeparationRadius = 2.5<meter>

    [<Literal>]
    let BallContactRadius = 0.35<meter>

    [<Literal>]
    let BallCaptureRadius = 1.0<meter>

    [<Literal>]
    let ActionContactRadius = 1.5<meter>

    [<Literal>]
    let BallContestSeparationRadius = 0.8<meter>

    [<Literal>]
    let SteeringSlowRadius = 2.5<meter>

    [<Literal>]
    let TurnConstraintBase = 0.65

    [<Literal>]
    let TurnConstraintAgilityCoeff = 0.018

    [<Literal>]
    let PlayerMassBase = 70.0<kilogram>

    // ============================================================
    // PHYSICS CONSTANTS — Ball and world simulation
    // ============================================================
    [<Literal>]
    let Gravity = -9.80665<meter / second^2>

    [<Literal>]
    let BallAirDrag = 0.985

    [<Literal>]
    let BallGroundRestitution = 0.55

    [<Literal>]
    let BallGroundFriction = 0.92

    [<Literal>]
    let BallPostRestitution = 0.65

    [<Literal>]
    let BallSpinDecay = 0.98

    [<Literal>]
    let BallMagnusCoeff = 0.0035

    [<Literal>]
    let PassSpeed = 16.0<meter / second>

    [<Literal>]
    let PassVz = 0.5<meter / second>

    [<Literal>]
    let LongBallSpeed = 26.0<meter / second>

    [<Literal>]
    let LongBallVz = 5.0<meter / second>

    [<Literal>]
    let CrossSpeed = 20.0<meter / second>

    [<Literal>]
    let CrossVz = 4.0<meter / second>

    [<Literal>]
    let ShotSpeedMax = 34.0<meter / second>

    [<Literal>]
    let ShotSpeedMin = 14.0<meter / second>

    // ============================================================
    // MATH HELPERS — Pure functions, no side effects
    // ============================================================
    let inline clamp (value: float<'u>) (min: float<'u>) (max: float<'u>) : float<'u> =
        if value < min then min
        elif value > max then max
        else value

    let inline clampFloat (value: float) (min: float) (max: float) : float =
        if value < min then min
        elif value > max then max
        else value

    // ============================================================
    // SPATIAL HELPERS — Pitch geometry
    // ============================================================
    [<Literal>]
    let AttackingThirdThreshold = 35.0<meter>

    [<Literal>]
    let DefensiveThirdThreshold = 35.0<meter>

    [<Literal>]
    let DuelIntervalMeanSeconds = 24.0<second>

    [<Literal>]
    let MaxDistanceSq = 1000000.0<meterSquared>

    let distToGoal (x: float<meter>) (dir: AttackDir) : float<meter> =
        match dir with
        | LeftToRight -> PitchLength - x
        | RightToLeft -> x

    let isInAttackingThird (x: float<meter>) (dir: AttackDir) : bool =
        match dir with
        | LeftToRight -> x >= PitchLength - AttackingThirdThreshold
        | RightToLeft -> x <= AttackingThirdThreshold

    let isInDefensiveThird (x: float<meter>) (dir: AttackDir) : bool =
        match dir with
        | LeftToRight -> x <= DefensiveThirdThreshold
        | RightToLeft -> x >= PitchLength - DefensiveThirdThreshold

    let forwardX (dir: AttackDir) : float =
        match dir with
        | LeftToRight -> 1.0
        | RightToLeft -> -1.0

    let momentumSign (dir: AttackDir) : float = forwardX dir

    let momentumDelta (dir: AttackDir) (delta: float) : float = momentumSign dir * delta

    // ============================================================
    // STEERING CONSTANTS — Time constants for steering behaviors
    // ============================================================
    [<Literal>]
    let SteeringSeekTimeConstant = 0.5<second>

    [<Literal>]
    let SteeringArriveTimeConstant = 0.3<second>

    [<Literal>]
    let SteeringFleeTimeConstant = 0.5<second>

    [<Literal>]
    let SteeringSeparationTimeConstant = 0.2<second>

    [<Literal>]
    let SteeringAlignmentTimeConstant = 1.0<second>

    [<Literal>]
    let SteeringCohesionTimeConstant = 0.5<second>

    // Grass friction coefficients (dimensionless)
    [<Literal>]
    let PlayerGrassFrictionRunning = 0.85

    [<Literal>]
    let PlayerGrassFrictionDribbling = 0.75
