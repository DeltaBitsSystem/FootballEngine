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
            slotX.[i] <- float32 (MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) x)
            slotY.[i] <- float32 (MatchMath.clamp (-(float Pitch.HalfWidth)) (float Pitch.HalfWidth) y)

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
        : FormationModel =
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
    /// always well-defined). The caller owns `result`, allowing the live
    /// positioning system to reuse this buffer on every tick.
    let queryInto
        (formation: FormationModel)
        (ballX: float<meter>)
        (ballY: float<meter>)
        (result: PositionPoint[])
        : unit =
        let mutable firstIndex = -1
        let mutable secondIndex = -1
        let mutable thirdIndex = -1
        let mutable firstDistance = System.Double.MaxValue
        let mutable secondDistance = System.Double.MaxValue
        let mutable thirdDistance = System.Double.MaxValue

        // There are only five fixed anchors. Keeping the three best indices as
        // scalars avoids allocating the sortable tuple/weight arrays per query.
        for i in 0 .. formation.Samples.Length - 1 do
            let sample = formation.Samples.[i]
            let distance = MatchMath.dist ballX ballY sample.BallX sample.BallY

            if distance < firstDistance then
                thirdIndex <- secondIndex
                thirdDistance <- secondDistance
                secondIndex <- firstIndex
                secondDistance <- firstDistance
                firstIndex <- i
                firstDistance <- distance
            elif distance < secondDistance then
                thirdIndex <- secondIndex
                thirdDistance <- secondDistance
                secondIndex <- i
                secondDistance <- distance
            elif distance < thirdDistance then
                thirdIndex <- i
                thirdDistance <- distance

        let inverseDistance distance =
            1.0 / (if distance < 1e-6 then 1e-6 else distance)

        let firstWeight =
            if firstIndex < 0 then
                0.0
            else
                inverseDistance firstDistance

        let secondWeight =
            if secondIndex < 0 then
                0.0
            else
                inverseDistance secondDistance

        let thirdWeight =
            if thirdIndex < 0 then
                0.0
            else
                inverseDistance thirdDistance

        let total = firstWeight + secondWeight + thirdWeight

        if total = 0.0 then
            Array.fill result 0 result.Length (FPoint.ofXY 0.0 0.0)
        else
            let firstWeight = firstWeight / total
            let secondWeight = secondWeight / total
            let thirdWeight = thirdWeight / total

            for k in 0 .. formation.SlotCount - 1 do
                let mutable sx = 0.0
                let mutable sy = 0.0

                if firstIndex >= 0 then
                    let sample = formation.Samples.[firstIndex]
                    sx <- sx + float sample.SlotX.[k] * firstWeight
                    sy <- sy + float sample.SlotY.[k] * firstWeight

                if secondIndex >= 0 then
                    let sample = formation.Samples.[secondIndex]
                    sx <- sx + float sample.SlotX.[k] * secondWeight
                    sy <- sy + float sample.SlotY.[k] * secondWeight

                if thirdIndex >= 0 then
                    let sample = formation.Samples.[thirdIndex]
                    sx <- sx + float sample.SlotX.[k] * thirdWeight
                    sy <- sy + float sample.SlotY.[k] * thirdWeight

                result.[k] <-
                    FPoint.ofXY
                        (MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) sx)
                        (MatchMath.clamp (-(float Pitch.HalfWidth)) (float Pitch.HalfWidth) sy)

    /// Compatibility wrapper for callers that need an owned result array.
    let query (formation: FormationModel) (ballX: float<meter>) (ballY: float<meter>) : PositionPoint[] =
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
                sx <- sx + float s.SlotX.[k] * weights.[j]
                sy <- sy + float s.SlotY.[k] * weights.[j]

            result.[k] <-
                FPoint.ofXY
                    (MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) sx)
                    (MatchMath.clamp (-(float Pitch.HalfWidth)) (float Pitch.HalfWidth) sy)

        result
