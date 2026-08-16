namespace FootballEngine.Core.Match

/// HELIOS "position pairs" (HELIOS base, `setPositionPair`): symmetric pairs
/// such as LB<->RB or LW<->RW may swap their assigned targets to balance
/// coverage without breaking the formation. Pure and deterministic: for each
/// pair we compare the total travel of keeping vs. swapping, and swap only when
/// the gain exceeds a hysteresis threshold (so the team never flips every tick).
module LateralPairs =

    type PairParams = { SwapThreshold: float }

    let private optionCost
        (current: (float * float)[])
        (l: int)
        (r: int)
        (tl: float * float)
        (tr: float * float)
        : float =
        MatchMath.distF32 (float32 (fst current.[l])) (float32 (snd current.[l])) (float32 (fst tl)) (float32 (snd tl))
        + MatchMath.distF32
            (float32 (fst current.[r]))
            (float32 (snd current.[r]))
            (float32 (fst tr))
            (float32 (snd tr))

    /// Apply swap decisions. `pairs` is a list of (slotL, slotR) where slotL
    /// sits on the left (negative Y) and slotR on the right (positive Y) in the
    /// team's resting shape. Returns the targets with swaps applied.
    let solve
        (params: PairParams)
        (current: (float * float)[])
        (targets: (float * float)[])
        (pairs: (int * int)[])
        : (float * float)[] =
        let result = Array.copy targets

        for (l, r) in pairs do
            let noSwap = optionCost current l r targets.[l] targets.[r]
            let swap = optionCost current l r targets.[r] targets.[l]

            if swap < noSwap - params.SwapThreshold then
                result.[l] <- targets.[r]
                result.[r] <- targets.[l]

        result
