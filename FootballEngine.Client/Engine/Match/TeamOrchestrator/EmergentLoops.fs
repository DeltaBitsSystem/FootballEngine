namespace FootballEngine.TeamOrchestrator


open FootballEngine.Types
open FootballEngine.ML

module EmergentLoops =

    let updateCompactness (shortPassSuccess: float) (current: EmergentState) : EmergentState =
        let w = EngineWeightDefaults.defaults.Collective.Emergent
        let adjustment =
            if shortPassSuccess > w.CompactnessSuccessThreshold then w.CompactnessSuccessDelta
            elif shortPassSuccess < w.CompactnessFailThreshold then w.CompactnessFailDelta
            else 0.0

        { current with
            CompactnessLevel = System.Math.Clamp(current.CompactnessLevel + adjustment, 0.1, 1.0) }

    let updatePressing (pressSuccessRate: float) (current: EmergentState) : EmergentState =
        let w = EngineWeightDefaults.defaults.Collective.Emergent
        let adjustment =
            if pressSuccessRate > w.PressingSuccessThreshold then w.PressingSuccessDelta
            elif pressSuccessRate < w.PressingFailThreshold then w.PressingFailDelta
            else 0.0

        { current with
            PressingIntensity = System.Math.Clamp(current.PressingIntensity + adjustment, 0.1, 1.0) }

    let updateWingPlay (flankSuccessRate: float) (current: EmergentState) : EmergentState =
        let w = EngineWeightDefaults.defaults.Collective.Emergent
        let adjustment =
            if flankSuccessRate > w.WingPlaySuccessThreshold then w.WingPlaySuccessDelta
            elif flankSuccessRate < w.WingPlayFailThreshold then w.WingPlayFailDelta
            else 0.0

        { current with
            WingPlayPreference = System.Math.Clamp(current.WingPlayPreference + adjustment, 0.1, 1.0) }

    let updateFatigueSpiral (avgCondition: float) (consecutiveLosses: int) (current: EmergentState) : EmergentState =
        let w = EngineWeightDefaults.defaults.Collective.Emergent
        let conditionFactor = avgCondition / 100.0
        let lossPenalty = float consecutiveLosses * w.ConsecutiveLossPenalty
        let fatigueEffect = max 0.0 (1.0 - conditionFactor) * w.FatigueSpiralThreshold + lossPenalty

        { current with
            CompactnessLevel = max 0.1 (current.CompactnessLevel - fatigueEffect * w.FatigueSpiralCompactnessFactor)
            PressingIntensity = max 0.1 (current.PressingIntensity - fatigueEffect * w.FatigueSpiralPressingFactor)
            TempoLevel = max 0.1 (current.TempoLevel - fatigueEffect * w.FatigueSpiralTempoFactor)
            RiskAppetite = max 0.1 (current.RiskAppetite - fatigueEffect * w.FatigueSpiralRiskFactor) }
