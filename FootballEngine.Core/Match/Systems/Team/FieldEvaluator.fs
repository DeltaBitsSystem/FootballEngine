namespace FootballEngine.Core.Match

/// Voronoi-style field evaluator (Gliders2D-inspired, adapted to be pure and
/// deterministic). Refines a base target by scoring a small cloud of candidate
/// points: reward being far from opponents (Voronoi), far enough from
/// teammates (no clumping), and making progress toward the rival goal. No Rng,
/// no World — positions are passed in the team's attacking frame (attack +X).
module FieldEvaluator =

    type EvaluatorParams =
        { TeammateWeight: float
          OpponentWeight: float
          ProgressWeight: float
          OppOurWeight: float
          SearchRadius: float
          CandidateCount: int
          MinTeammateDist: float
          OffsideX: float option }

    /// Deterministic candidate cloud: the base point plus evenly-spaced points
    /// on a circle of `SearchRadius` around it, and (when an offside line is
    /// provided) a point one metre behind that line on the base Y.
    let candidates (p: EvaluatorParams) (cx: float) (cy: float) : (float * float)[] =
        let count = max 1 p.CandidateCount
        let extra = if p.OffsideX.IsSome then 1 else 0
        let pts = Array.zeroCreate<(float * float)> (count + extra)
        pts.[0] <- (cx, cy)

        for k in 1 .. count - 1 do
            let ang = 2.0 * System.Math.PI * float (k - 1) / float (count - 1)
            pts.[k] <- (cx + p.SearchRadius * cos ang, cy + p.SearchRadius * sin ang)

        match p.OffsideX with
        | Some lineX -> pts.[count] <- (lineX - 1.0, cy)
        | None -> ()

        pts

    let private nearestDist (pts: (float * float)[]) (x: float) (y: float) : float =
        let mutable best = System.Double.MaxValue

        for (px, py) in pts do
            let d = MatchMath.distF32 (float32 x) (float32 y) (float32 px) (float32 py)

            if d < best then
                best <- d

        best

    /// Score a candidate in [0,1] (higher is better), in the team's attacking
    /// frame where attack = +X.
    let score
        (p: EvaluatorParams)
        (teammates: (float * float)[])
        (opponents: (float * float)[])
        (x: float)
        (y: float)
        : float =
        // spread: reward being at least MinTeammateDist from the nearest teammate.
        let spread =
            let d = nearestDist teammates x y
            MatchMath.clamp01 ((d - p.MinTeammateDist) / (max 1.0 p.SearchRadius))

        // avoid: reward being far from the nearest opponent (Voronoi behaviour).
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

    /// Refine a base target to the best-scoring candidate.
    let refine
        (p: EvaluatorParams)
        (teammates: (float * float)[])
        (opponents: (float * float)[])
        (baseX: float)
        (baseY: float)
        : (float * float) =
        candidates p baseX baseY
        |> Array.maxBy (fun (x, y) -> score p teammates opponents x y)
