namespace FootballEngine.Core.Match

/// Gliders-style pressing: two players chasing the same loose ball is worse
/// than one, so a slot chases only when it is the side's best interceptor
/// (myStep <= SelfStep), it is within the pressing horizon, and it can arrive
/// before the rival's best interceptor within a role-dependent allowance.
/// Pure and deterministic.
module Pressing =

    /// The step allowance before a slot gives up on a loose ball, per role.
    /// The allowance encodes how much risk each role should take: full backs
    /// are encouraged to charge; midfielders need a tight margin.
    let allowance (cfg: PressConfig) (role: SlotRole) : int =
        match role with
        | SlotRole.FullBack -> cfg.FullBackAllowance
        | SlotRole.DefensiveMid
        | SlotRole.CenterMid
        | SlotRole.AttackingMid
        | SlotRole.Winger -> cfg.MidfieldAllowance
        | _ -> cfg.BaseAllowance

    /// True when this slot should chase the loose ball. SelfStep is the side's
    /// min step, so the first condition is met only by the best interceptor —
    /// the "one chaser" rule comes from that min comparison, not from any
    /// coordination between players.
    let shouldPress (cfg: PressConfig) (intercept: InterceptState) (role: SlotRole) (myStep: int) : bool =
        myStep <= intercept.SelfStep
        && myStep <= cfg.MaxPressStep
        && myStep < intercept.OpponentStep + allowance cfg role
