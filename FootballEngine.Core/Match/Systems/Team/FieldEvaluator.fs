namespace FootballEngine.Core.Match

/// Voronoi-style field evaluator (Gliders2D-inspired, adapted to be pure and
/// deterministic). The formation cannot know the local spacing of a given
/// moment, so the base target is refined against the actual positions: a
/// candidate scores well when it is far from opponents (Voronoi), far enough
/// from teammates (no clumping), and making progress toward the rival goal.
/// No Rng, no World — positions are passed in the team's attacking frame
/// (attack +X).
module FieldEvaluator =

    [<Struct>]
    type EvaluatorParams =
        { TeammateWeight: float
          OpponentWeight: float
          ProgressWeight: float
          OppOurWeight: float
          SearchRadius: float
          CandidateCount: int
          MinTeammateDist: float
          OffsideX: float option }

    /// The search space for refinement: a fixed, evenly-spaced cloud around
    /// the base point (reproducible every tick, unlike random sampling) plus,
    /// when an offside line is known, a legal option one metre behind it on
    /// the base Y — otherwise the refinement could pick an offside spot.
    let candidates (p: EvaluatorParams) (cx: float) (cy: float) : PositionPoint[] =
        let count = max 1 p.CandidateCount
        let extra = if p.OffsideX.IsSome then 1 else 0
        let pts = Array.zeroCreate (count + extra)
        pts.[0] <- FPoint.ofXY cx cy

        for k in 1 .. count - 1 do
            let ang = 2.0 * System.Math.PI * float (k - 1) / float (count - 1)
            pts.[k] <- FPoint.ofXY (cx + p.SearchRadius * cos ang) (cy + p.SearchRadius * sin ang)

        match p.OffsideX with
        | Some lineX -> pts.[count] <- FPoint.ofXY (lineX - 1.0) cy
        | None -> ()

        pts

    let private nearestDist (pts: PositionPoint[]) (x: float) (y: float) : float =
        let mutable best = System.Double.MaxValue

        for pt in pts do
            let d = MatchMath.distF32 (float32 x) (float32 y) (float32 pt.X) (float32 pt.Y)

            if d < best then
                best <- d

        best

    /// Squared distances are compared and only the winner is rooted: sqrt is monotonic and correctly rounded,
    /// so the result is the same as taking the minimum of the roots.
    let inline private nearestDistAtSlots
        (pts: PositionPoint[])
        (slots: int[])
        (excludedSlot: int)
        (x: float)
        (y: float)
        : float =
        let qx = float (float32 x)
        let qy = float (float32 y)
        let mutable best = System.Double.MaxValue

        for slot in slots do
            if slot <> excludedSlot then
                let pt = pts.[slot]
                let dx = qx - float (float32 pt.X)
                let dy = qy - float (float32 pt.Y)
                let d2 = dx * dx + dy * dy

                if d2 < best then
                    best <- d2

        if best = System.Double.MaxValue then best else sqrt best

    let inline private scoreAtSlots
        (p: EvaluatorParams)
        (teammates: PositionPoint[])
        (teammateSlots: int[])
        (excludedTeammate: int)
        (opponents: PositionPoint[])
        (opponentSlots: int[])
        (pt: PositionPoint)
        : float =
        let dOur = nearestDistAtSlots teammates teammateSlots excludedTeammate pt.X pt.Y

        let spread =
            MatchMath.clamp01 ((dOur - p.MinTeammateDist) / (max 1.0 p.SearchRadius))

        let dOpp = nearestDistAtSlots opponents opponentSlots -1 pt.X pt.Y

        let avoid =
            if opponentSlots.Length = 0 then
                1.0
            else
                MatchMath.clamp01 (dOpp / (2.0 * p.SearchRadius))

        let progress = MatchMath.clamp01 (0.5 + pt.X / (2.0 * float Pitch.HalfLength))

        let openness =
            MatchMath.clamp -1.0 1.0 ((dOpp - dOur) / (2.0 * max 1.0 p.SearchRadius))

        p.TeammateWeight * spread
        + p.OpponentWeight * avoid
        + p.ProgressWeight * progress
        + p.OppOurWeight * openness

    /// Quantify how good a candidate spot is (higher is better), so `refine`
    /// can compare every point on one scale. In the team's attacking frame
    /// where attack = +X.
    let score
        (p: EvaluatorParams)
        (teammates: PositionPoint[])
        (opponents: PositionPoint[])
        (x: float)
        (y: float)
        : float =
        // spread: a candidate too close to a teammate is wasteful, so reward
        // standing at least MinTeammateDist away from the nearest one.
        let spread =
            let d = nearestDist teammates x y
            MatchMath.clamp01 ((d - p.MinTeammateDist) / (max 1.0 p.SearchRadius))

        // avoid: staying far from the nearest opponent (Voronoi behaviour) keeps
        // the receiver in free space rather than contested ground.
        let avoid =
            if opponents.Length = 0 then
                1.0
            else
                let d = nearestDist opponents x y
                MatchMath.clamp01 (d / (2.0 * p.SearchRadius))

        let progress = MatchMath.clamp01 (0.5 + x / (2.0 * float Pitch.HalfLength))

        let openness =
            let dOpp = nearestDist opponents x y
            let dOur = nearestDist teammates x y
            MatchMath.clamp -1.0 1.0 ((dOpp - dOur) / (2.0 * max 1.0 p.SearchRadius))

        p.TeammateWeight * spread
        + p.OpponentWeight * avoid
        + p.ProgressWeight * progress
        + p.OppOurWeight * openness

    /// Adjust a coarse formation target using the current local spacing — the
    /// formation cannot see the opponents, so the best-scoring candidate of
    /// the surrounding cloud replaces it.
    let refine
        (p: EvaluatorParams)
        (teammates: PositionPoint[])
        (opponents: PositionPoint[])
        (baseX: float)
        (baseY: float)
        : PositionPoint =
        candidates p baseX baseY
        |> Array.maxBy (fun pt -> score p teammates opponents pt.X pt.Y)

    /// The candidate cloud repeats every call, so its directions are computed once per candidate count.
    /// The table is built whole before it is published, which keeps concurrent matches safe.
    let mutable private unitCircle: struct (float * float)[] = [||]

    let private unitCircleFor (count: int) =
        let table = unitCircle

        if table.Length = count then
            table
        else
            let built =
                Array.init count (fun k ->
                    if k = 0 then
                        struct (0.0, 0.0)
                    else
                        let ang = 2.0 * System.Math.PI * float (k - 1) / float (count - 1)
                        struct (cos ang, sin ang))

            unitCircle <- built
            built

    let refineInto
        (p: EvaluatorParams)
        (teammates: PositionPoint[])
        (teammateSlots: int[])
        (excludedTeammate: int)
        (opponents: PositionPoint[])
        (opponentSlots: int[])
        (scratch: PositionPoint[])
        (baseX: float)
        (baseY: float)
        : PositionPoint =
        let count = max 1 p.CandidateCount
        let extra = if p.OffsideX.IsSome then 1 else 0
        scratch.[0] <- FPoint.ofXY baseX baseY
        let directions = unitCircleFor count

        for k in 1 .. count - 1 do
            let struct (c, s) = directions.[k]
            scratch.[k] <- FPoint.ofXY (baseX + p.SearchRadius * c) (baseY + p.SearchRadius * s)

        match p.OffsideX with
        | Some lineX -> scratch.[count] <- FPoint.ofXY (lineX - 1.0) baseY
        | None -> ()

        let mutable best = scratch.[0]

        let mutable bestScore =
            scoreAtSlots p teammates teammateSlots excludedTeammate opponents opponentSlots best

        for k in 1 .. count + extra - 1 do
            let candidate = scratch.[k]

            let candidateScore =
                scoreAtSlots p teammates teammateSlots excludedTeammate opponents opponentSlots candidate

            if candidateScore > bestScore then
                best <- candidate
                bestScore <- candidateScore

        best
