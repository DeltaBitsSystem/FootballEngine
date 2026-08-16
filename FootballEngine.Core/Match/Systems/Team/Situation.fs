namespace FootballEngine.Core.Match

/// Who gets to the loose ball first decides the phase: with a clear lead the
/// phase is set, and when the two sides arrive too close to call, the caller
/// keeps its previous phase instead of flip-flopping.
module Situation =

    /// Resolve the phase from intercept steps: a side that clearly reaches
    /// the ball first attacks (or defends, if that side is the opponent);
    /// when the race is too close, keep the caller's existing phase.
    let resolve (cfg: SituationConfig) (intercept: InterceptState) : PhaseKind option =
        let ourMin = intercept.SelfStep
        let oppMin = intercept.OpponentStep

        if ourMin < oppMin - cfg.LeadSteps then
            Some PhaseKind.Attacking
        elif oppMin < ourMin - cfg.LeadSteps then
            Some PhaseKind.Defending
        else
            None
