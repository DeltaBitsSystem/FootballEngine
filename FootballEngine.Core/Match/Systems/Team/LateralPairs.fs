namespace FootballEngine.Core.Match

/// HELIOS "position pairs" (HELIOS base, `setPositionPair`): symmetric pairs
/// such as LB<->RB or LW<->RW may swap their assigned targets, so the pair
/// balances coverage of its corridor without breaking the formation. Pure and
/// deterministic: for each pair we compare the total travel of keeping vs.
/// swapping, and swap only when the gain exceeds a hysteresis threshold (so
/// the team never flips every tick).
module LateralPairs =

    type PairParams = { SwapThreshold: float }

    let private optionCost (current: FPoint[]) (l: int) (r: int) (tl: FPoint) (tr: FPoint) : float =
        MatchMath.distF32 (float32 current.[l].X) (float32 current.[l].Y) (float32 tl.X) (float32 tl.Y)
        + MatchMath.distF32 (float32 current.[r].X) (float32 current.[r].Y) (float32 tr.X) (float32 tr.Y)

    /// Apply swap decisions, comparing only the two alternatives (keep vs. swap)
    /// because both slots cover the same corridor — a swap is worthwhile only
    /// when it clearly shortens total travel. `pairs` is a list of (slotL,
    /// slotR) where slotL sits on the left (negative Y) and slotR on the
    /// right (positive Y) in the team's resting shape. Returns a fresh array;
    /// the caller's `targets` is left untouched so the function stays pure.
    let solve (params: PairParams) (current: FPoint[]) (targets: FPoint[]) (pairs: (int * int)[]) : FPoint[] =
        let result = Array.copy targets

        for (l, r) in pairs do
            let noSwap = optionCost current l r targets.[l] targets.[r]
            let swap = optionCost current l r targets.[r] targets.[l]

            if swap < noSwap - params.SwapThreshold then
                result.[l] <- targets.[r]
                result.[r] <- targets.[l]

        result
