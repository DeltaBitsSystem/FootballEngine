namespace FootballEngine.Player.Steering

open FootballEngine.Domain
open FootballEngine.Types

module FatiguePipeline =

    let private sigmoid (x: float) = 1.0 / (1.0 + System.Math.Exp(-x))

    let effectiveVisionFromCondition (baseVision: int) (condition: float32) =
        let normVision = PhysicsContract.normaliseAttr baseVision
        let normCond = PhysicsContract.normaliseCondition condition
        let threshold = 0.7

        let decay =
            if normCond >= threshold then
                1.0
            else
                let deficit = threshold - normCond
                1.0 - sigmoid (deficit * 10.0 - 2.0) * 0.6

        normVision * decay

    let effectivePositioningFromCondition (basePositioning: int) (condition: float32) =
        let normPos = PhysicsContract.normaliseAttr basePositioning
        let normCond = PhysicsContract.normaliseCondition condition
        let threshold = 0.55

        let decay =
            if normCond >= threshold then
                1.0
            else
                let deficit = threshold - normCond
                1.0 - sigmoid (deficit * 12.0 - 2.0) * 0.7

        normPos * decay

    let effectiveComposureFromCondition (baseComposure: int) (condition: float32) =
        let normComp = PhysicsContract.normaliseAttr baseComposure
        let normCond = PhysicsContract.normaliseCondition condition
        let threshold = 0.4

        let decay =
            if normCond >= threshold then
                1.0
            else
                let deficit = threshold - normCond
                1.0 - sigmoid (deficit * 15.0 - 2.0) * 0.8

        normComp * decay

    let decisionQuality (player: Player) (profile: BehavioralProfile) (condition: float32) =
        let v = effectiveVisionFromCondition player.Mental.Vision condition
        let p = effectivePositioningFromCondition player.Mental.Positioning condition
        let c = effectiveComposureFromCondition player.Mental.Composure condition

        let visionWeight = profile.CreativityWeight * 0.4 + 0.15

        let positioningWeight =
            profile.DefensiveHeight * 0.35 + profile.PressingIntensity * 0.20 + 0.10

        let composureWeight =
            profile.RiskAppetite * 0.30 + (1.0 - profile.Directness) * 0.20 + 0.15

        v * visionWeight + p * positioningWeight + c * composureWeight

    // -------------------------------------------------------------------------
    // Condition degradation — called per player per CognitiveTick (every 40 SubTicks = 1s)
    // Degradation depends on pressing intensity, stamina, and work rate
    // -------------------------------------------------------------------------

    let degradeCondition (player: Player) (currentCondition: float32) (isPressing: bool) (matchMinute: float) : float32 =

        let stamina = player.Physical.Stamina
        let workRate = player.Mental.WorkRate
        let naturalCondition = float32 player.Condition

        let staminaFactor = 1.0 - PhysicsContract.normaliseAttr stamina
        let workFactor = PhysicsContract.normaliseAttr workRate
        let baseDecay = 0.0008 + staminaFactor * 0.003 + workFactor * 0.001

        let pressingMultiplier = if isPressing then 1.8 else 1.0

        let lateMultiplier =
            if matchMinute > 60.0 then
                1.0 + (matchMinute - 60.0) / 30.0 * 0.5
            else
                1.0

        let timeFactor = 0.4 + 0.6 * (matchMinute / 90.0)

        let totalDecay = baseDecay * pressingMultiplier * lateMultiplier * timeFactor

        let diff = currentCondition - naturalCondition
        let naturalPull = if diff > 0.0f then diff * 0.001f else 0.0f

        let newCond = currentCondition - float32 totalDecay - naturalPull
        System.Math.Clamp(newCond, 10.0f, 100.0f)
