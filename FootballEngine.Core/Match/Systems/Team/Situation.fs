namespace FootballEngine.Core.Match

module Situation =

    let resolve (cfg: SituationConfig) (intercept: InterceptState) : PhaseKind option =
        let ourMin = intercept.SelfStep
        let oppMin = intercept.OpponentStep

        if ourMin < oppMin - cfg.LeadSteps then
            Some PhaseKind.Attacking
        elif oppMin < ourMin - cfg.LeadSteps then
            Some PhaseKind.Defending
        else
            None
