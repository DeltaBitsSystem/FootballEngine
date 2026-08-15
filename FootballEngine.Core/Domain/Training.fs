namespace FootballEngine.Domain

type TrainingFocus =
    | TrainingPhysical
    | TrainingTechnical
    | TrainingMental
    | TrainingGoalkeeping
    | TrainingAllRound

type TrainingIntensity =
    | TrainingLight
    | TrainingNormal
    | TrainingHeavy

type TrainingSchedule =
    { Focus: TrainingFocus
      Intensity: TrainingIntensity }

type TrainingIntensityData =
    { DeltaMultiplier: float
      ConditionCost: int
      InjuryRisk: float
      MoraleChange: int }

module TrainingIntensityData =
    let get (intensity: TrainingIntensity) : TrainingIntensityData =
        match intensity with
        | TrainingLight ->
            { DeltaMultiplier = 0.6
              ConditionCost = -5
              InjuryRisk = 0.0
              MoraleChange = -2 }
        | TrainingNormal ->
            { DeltaMultiplier = 1.0
              ConditionCost = -10
              InjuryRisk = 0.0
              MoraleChange = 0 }
        | TrainingHeavy ->
            { DeltaMultiplier = 1.4
              ConditionCost = -18
              InjuryRisk = 0.04
              MoraleChange = -5 }

    let formatMult (mult: float) : string =
        if mult >= 1.0 then
            sprintf "+%d%%" (int ((mult - 1.0) * 100.0))
        else
            sprintf "%d%%" (int ((mult - 1.0) * 100.0))
