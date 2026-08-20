namespace FootballEngine.Core.Match

/// RoboCup 2D (HELIOS) inspired formation: the team's shape must track the
/// ball as a unit, so instead of static positions the formation is a map from
/// ball position to the 11 ideal rest positions, queried by inverse-distance
/// interpolation over the nearest anchors. Pure and deterministic — no Rng,
/// no World.
///
/// Frame of reference: everything works in the TEAM'S OWN attacking frame,
/// where the team always attacks +X (own goal at -HalfLength, rival goal at
/// +HalfLength). Callers mirror world coordinates in/out via `dir`.
module Formation =

    /// A snapshot of the formation for one ball position: what each slot's ideal
    /// target is when the ball sits at (BallX, BallY). Interpolation blends
    /// between these snapshots as the ball moves.
    type FormationSample =
        { BallX: float<meter>
          BallY: float<meter>
          SlotX: float32[]
          SlotY: float32[] }

    type Formation =
        { Samples: FormationSample[]
          SlotCount: int }

    /// Per-role follow weights, kept in the module (like `phaseShift` in the
    /// old Shape) rather than in the samples: how far a role chases the ball
    /// is a stable property of the role, not of the ball zone. Larger = more
    /// mobile toward the ball. GK is nearly static so the back line stays
    /// anchored; the striker and wingers are the most mobile.
    let private followX (role: SlotRole) : float =
        match role with
        | SlotRole.Goalkeeper -> 0.10
        | SlotRole.Defender -> 0.30
        | SlotRole.FullBack -> 0.45
        | SlotRole.DefensiveMid -> 0.50
        | SlotRole.CenterMid -> 0.60
        | SlotRole.AttackingMid -> 0.70
        | SlotRole.Winger -> 0.65
        | SlotRole.Striker -> 0.80

    let private followY (role: SlotRole) : float =
        match role with
        | SlotRole.Goalkeeper -> 0.15
        | SlotRole.Defender -> 0.20
        | SlotRole.FullBack -> 0.40
        | SlotRole.DefensiveMid -> 0.50
        | SlotRole.CenterMid -> 0.60
        | SlotRole.AttackingMid -> 0.60
        | SlotRole.Winger -> 0.50
        | SlotRole.Striker -> 0.40

    /// The shape must stay recognizable but lean toward the ball: each slot
    /// sits at its base position pulled toward the ball anchor by its role's
    /// follow weights, then clamped to the pitch.
    let private sampleFor
        (roles: SlotRole[])
        (baseX: float<meter>[])
        (baseY: float<meter>[])
        (ballX: float<meter>)
        (ballY: float<meter>)
        : FormationSample =
        let n = roles.Length
        let slotX = Array.zeroCreate<float32> n
        let slotY = Array.zeroCreate<float32> n

        for i in 0 .. n - 1 do
            let x = float baseX.[i] + float ballX * followX roles.[i]
            let y = float baseY.[i] + float ballY * followY roles.[i]
            let cx, cy = MatchMath.pitchClamp x y
            slotX.[i] <- float32 cx
            slotY.[i] <- float32 cy

        { BallX = ballX
          BallY = ballY
          SlotX = slotX
          SlotY = slotY }

    /// Generate the formation from a lineup's roles and base positions, in the
    /// team's attacking frame. Roles and base positions are fixed per lineup,
    /// so this runs once per match; the anchors are the ball zones for which
    /// role-specific samples are produced; `dir` mirrors base X so both teams
    /// attack +X.
    let fromLineup
        (roles: SlotRole[])
        (basePositions: Spatial[])
        (dir: float)
        (anchors: (float<meter> * float<meter>)[])
        : Formation =
        let n = roles.Length
        let baseX = basePositions |> Array.map (fun p -> p.X * dir)
        let baseY = basePositions |> Array.map (fun p -> p.Y)

        let samples =
            anchors |> Array.map (fun (bx, by) -> sampleFor roles baseX baseY bx by)

        { Samples = samples; SlotCount = n }

    /// Interpolate the ideal targets for a ball position by inverse-distance
    /// weighting over the 3 nearest anchors, so the targets change
    /// continuously as the ball moves instead of snapping between zones (a
    /// practical equivalent of HELIOS's Delaunay/Gouraud interpolation,
    /// always well-defined). Returns targets in the team's attacking frame.
    let query (formation: Formation) (ballX: float<meter>) (ballY: float<meter>) : FPoint[] =
        let nearest: (FormationSample * float)[] =
            formation.Samples
            |> Array.map (fun s -> s, MatchMath.dist ballX ballY s.BallX s.BallY)
            |> Array.sortBy (fun (_, d) -> d)
            |> Array.truncate 3

        let eps = 1e-6

        let invDists =
            nearest |> Array.map (fun (_, d) -> 1.0 / (if d < eps then eps else d))

        let total = invDists |> Array.sum
        let weights = invDists |> Array.map (fun w -> w / total)

        let result = Array.zeroCreate formation.SlotCount

        for k in 0 .. formation.SlotCount - 1 do
            let mutable sx = 0.0
            let mutable sy = 0.0

            for j in 0 .. nearest.Length - 1 do
                let s, _ = nearest.[j]
                sx <- sx + float (s.SlotX.[k]) * weights.[j]
                sy <- sy + float (s.SlotY.[k]) * weights.[j]

            let cx, cy = MatchMath.pitchClamp sx sy
            result.[k] <- FPoint.ofXY (float cx) (float cy)

        result
