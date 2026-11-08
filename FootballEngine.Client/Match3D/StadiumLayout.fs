namespace FootballEngine.Client.Match3D

open System
open System.Globalization
open System.Numerics

// Port of Tools/MatchViewer3D/StadiumKit/stadium_layout.py. Every float operation keeps the Python order of
// evaluation because the golden digests compare against Python output at sub-millimetre precision.
module StadiumLayout =

    let pitchLength = 105.0
    let pitchWidth = 68.0
    let seatPitch = 0.5
    let aisleEvery = 14
    let benchInner = 4.0
    let benchLength = 7.0
    let techZoneHalf = benchInner + benchLength + 2.0
    let roofClearance = 6.5
    let towerHeight = 42.0
    let trussSpacing = 10.0
    let cornerRadius = 14.0

    // Python's round(x, digits) rounds the exact binary value half-to-even. Math.Round scales by a power of ten
    // in floating point first, which can move a value across a tie, so the rounding is done on the exact fraction.
    let pyRound (x: float) (digits: int) : float =
        if Double.IsNaN x || Double.IsInfinity x then
            x
        else
            let bits = BitConverter.DoubleToInt64Bits x
            let negative = bits < 0L
            let biased = int ((bits >>> 52) &&& 0x7FFL)
            let fraction = bits &&& 0xFFFFFFFFFFFFFL

            let mantissa, exponent =
                if biased = 0 then
                    fraction, -1074
                else
                    (fraction ||| (1L <<< 52)), biased - 1075

            let scale = BigInteger.Pow(BigInteger 10, digits)

            let numerator, denominator =
                if exponent >= 0 then
                    BigInteger mantissa * BigInteger.Pow(BigInteger 2, exponent) * scale, BigInteger.One
                else
                    BigInteger mantissa * scale, BigInteger.Pow(BigInteger 2, -exponent)

            let quotient = BigInteger.Divide(numerator, denominator)
            let remainder = BigInteger.Remainder(numerator, denominator)
            let twice = remainder * BigInteger 2

            let rounded =
                if twice > denominator || (twice = denominator && not quotient.IsEven) then
                    quotient + BigInteger.One
                else
                    quotient

            let digitsText =
                rounded.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0')

            let text =
                if digits = 0 then
                    digitsText
                else
                    digitsText.Insert(digitsText.Length - digits, ".")

            let value = Double.Parse(text, CultureInfo.InvariantCulture)
            if negative then -value else value

    // Python's math.radians/degrees multiply by these exact constants; using the same form keeps the last bit.
    let private toRadians (degrees: float) = degrees * (Math.PI / 180.0)
    let private toDegrees (radians: float) = radians * (180.0 / Math.PI)

    // SplitMix64 with explicit wrapping: Python masks every step to 64 bits, which uint64 arithmetic reproduces.
    type Rng(seed: uint64) =
        let mutable state = seed

        member _.NextU64() =
            state <- state + 0x9E3779B97F4A7C15UL
            let z1 = (state ^^^ (state >>> 30)) * 0xBF58476D1CE4E5B9UL
            let z2 = (z1 ^^^ (z1 >>> 27)) * 0x94D049BB133111EBUL
            z2 ^^^ (z2 >>> 31)

        member this.Unit() =
            float (this.NextU64() >>> 11) / float (1UL <<< 53)

        member this.Chance(p: float) = this.Unit() < p

    type StandKind =
        | Straight
        | Corner

    type Roof =
        | NoRoof
        | Truss20
        | Truss30
        | Truss40

    type Shape =
        | Bowl
        | Closed
        | Open

    type Lighting =
        | TowerLighting
        | RoofLighting

    type Facade =
        | Fins
        | Panels

    type SeatColours =
        | Solid
        | Tiered
        | Blocks

    type Tier =
        { Rows: int
          RowDepth: float
          Riser: float
          FrontOffset: float
          BaseHeight: float
          VipBand: bool }

        member t.BackOffset = t.FrontOffset + float t.Rows * t.RowDepth
        member t.TopHeight = t.BaseHeight + float t.Rows * t.Riser

    type Stand =
        { Side: string
          Kind: StandKind
          Start: float * float
          End: float * float
          Centre: float * float
          Radius: float
          A0: float
          A1: float
          Tiers: Tier list
          Roof: Roof
          Seated: bool
          Main: bool }

    type Piece =
        { Piece: string
          X: float
          Y: float
          Z: float
          Yaw: float
          Tint: string
          Scale: float * float * float }

    type Layout =
        { Schema: int
          Prestige: float
          Capacity: int
          Shape: Shape
          Lighting: Lighting
          Facade: Facade
          SeatColours: SeatColours
          Stands: Stand list
          Pieces: Piece list }

    // Golden digests and the renderer address these by their Python spelling, so the names are part of the contract.
    let kindName kind =
        match kind with
        | Straight -> "straight"
        | Corner -> "corner"

    let roofName roof =
        match roof with
        | NoRoof -> "none"
        | Truss20 -> "truss_20"
        | Truss30 -> "truss_30"
        | Truss40 -> "truss_40"

    // NoRoof has no span; callers check for a roof before asking, so 0.0 is never used as a real span.
    let roofSpan roof =
        match roof with
        | NoRoof -> 0.0
        | Truss20 -> 20.0
        | Truss30 -> 30.0
        | Truss40 -> 40.0

    let shapeName shape =
        match shape with
        | Bowl -> "bowl"
        | Closed -> "closed"
        | Open -> "open"

    let lightingName lighting =
        match lighting with
        | TowerLighting -> "towers"
        | RoofLighting -> "roof"

    let facadeName facade =
        match facade with
        | Fins -> "fins"
        | Panels -> "panels"

    let seatColoursName seatColours =
        match seatColours with
        | Solid -> "solid"
        | Tiered -> "tiered"
        | Blocks -> "blocks"

    let prestigeOf (reputation: int) (leagueLevel: int) =
        // The division adds a floor so a top-flight minnow still looks top-flight.
        let division =
            match leagueLevel with
            | 0 -> 0.18
            | 1 -> 0.07
            | _ -> 0.0

        max 0.0 (min 1.0 (float reputation / 9999.0 * 0.82 + division))

    let private tiersFor (p: float) (rng: Rng) (main: bool) (sideScale: float) : Tier list =
        let baseCount =
            if p < 0.3 then 1
            elif p < 0.68 then 2
            else 3

        let count =
            if main && p >= 0.22 then
                min 3 (baseCount + (if p < 0.68 then 1 else 0))
            else
                baseCount

        let tiers = ResizeArray<Tier>()
        let mutable front = 0.0
        let mutable height = 0.6

        for t in 0 .. count - 1 do
            // Lower bowls rake shallower for sightlines near the pitch; upper tiers steepen so the back still sees.
            let rake = toRadians (20.0 + 7.0 * float t + 3.0 * rng.Unit())

            let rows0 =
                Math.Round((8.0 + 30.0 * p) * sideScale * (if t = 0 then 1.0 else 0.85) + rng.Unit() * 3.0)
                |> int

            let rows = max 6 rows0
            let depth = 0.8

            // The stored (rounded) tier drives the next tier's position, so the stack matches the emitted geometry.
            let tier =
                { Rows = rows
                  RowDepth = depth
                  Riser = pyRound (depth * Math.Tan(rake)) 3
                  FrontOffset = pyRound front 3
                  BaseHeight = pyRound height 3
                  VipBand = t > 0 && p > 0.55 }

            tiers.Add tier
            // Upper tiers overhang the one below: it shelters it and puts fans closer to the pitch.
            let overhang =
                if t = count - 1 then
                    0.0
                else
                    min (float tier.Rows * depth * 0.45) (3.0 + 6.0 * p)

            front <- tier.BackOffset - overhang
            height <- tier.TopHeight + (if p > 0.55 then 4.2 else 3.2)

        List.ofSeq tiers

    let length (st: Stand) =
        match st.Kind with
        | Straight -> Double.Hypot(fst st.End - fst st.Start, snd st.End - snd st.Start)
        | Corner -> toRadians (st.A1 - st.A0) * st.Radius

    // Straight runs go counter-clockwise around the pitch so the pitch is on their left; corners are arcs.
    let frontFrames (st: Stand) (step: float) : ((float * float) * (float * float))[] =
        match st.Kind with
        | Straight ->
            let (ax, ay), (bx, by) = st.Start, st.End
            let len = Double.Hypot(bx - ax, by - ay)
            let dx, dy = (bx - ax) / len, (by - ay) / len
            let n = max 1 (int (len / step))

            Array.init (n + 1) (fun i ->
                let t = float i / float n
                ((ax + (bx - ax) * t, ay + (by - ay) * t), (dy, -dx)))
        | Corner ->
            let cx, cy = st.Centre
            let sweep = toRadians (st.A1 - st.A0)
            let n = max 2 (int (sweep * st.Radius / step) + 4)

            Array.init (n + 1) (fun i ->
                let a = toRadians st.A0 + sweep * float i / float n

                ((cx + Math.Cos(a) * st.Radius, cy + Math.Sin(a) * st.Radius), (Math.Cos(a), Math.Sin(a))))

    let tierProfile (t: Tier) (upper: bool) : (float * float)[] =
        let fo = t.FrontOffset
        let h = t.BaseHeight
        let d = t.RowDepth
        let r = t.Riser
        let rows = t.Rows
        let pts = ResizeArray<float * float>()

        if upper then
            pts.Add((fo + float rows * d, h - 1.4))
            pts.Add((fo, h - 1.4))
        else
            pts.Add((fo, 0.0))

        pts.Add((fo, h + 0.25))

        for i in 0 .. rows - 1 do
            let z = h + float i * r
            pts.Add((fo + float i * d, z))
            pts.Add((fo + float (i + 1) * d, z))
            pts.Add((fo + float (i + 1) * d, z + r))

        pts.Add((fo + float rows * d + 0.6, h + float rows * r + 1.1))
        pts.Add((fo + float rows * d + 0.6, (if upper then h - 1.4 else 0.0)))

        pts |> Seq.map (fun (o, z) -> (pyRound o 4, pyRound z 4)) |> Array.ofSeq

    // Every fourth aisle block hosts a vomitory in the middle rows of the tier.
    let vomitoryBlock (block: int) = block % 4 = 2

    let seatPositions (st: Stand) (t: Tier) : (float * float * float * float * int)[] =
        if not st.Seated then
            [||]
        else
            let fr = frontFrames st seatPitch
            let mid = t.Rows / 2

            [| for row in 0 .. t.Rows - 1 do
                   let o = t.FrontOffset + float row * t.RowDepth + 0.45
                   let z = t.BaseHeight + float row * t.Riser

                   for i in 0 .. fr.Length - 2 do
                       let k = i % (aisleEvery + 2)
                       let block = i / (aisleEvery + 2)

                       // Aisles fall between the seat blocks; a vomitory replaces the middle rows of its block.
                       let aisle = k >= aisleEvery

                       let vomitory =
                           t.Rows >= 12 && vomitoryBlock block && row >= mid - 2 && row <= mid + 1

                       if not aisle && not vomitory then
                           let (px, py), (nx, ny) = fr.[i]
                           let (qx, qy), _ = fr.[i + 1]
                           let cx, cy = (px + qx) / 2.0, (py + qy) / 2.0
                           let yaw = toDegrees (Math.Atan2(-ny, -nx)) - 90.0

                           yield (pyRound (cx + nx * o) 4, pyRound (cy + ny * o) 4, pyRound z 4, pyRound yaw 3, row) |]

    let roofEdge (st: Stand) : ((float * float * float) * (float * float))[] =
        let top = List.last st.Tiers
        let back = top.FrontOffset + float top.Rows * top.RowDepth + 0.6
        let z = top.BaseHeight + float top.Rows * top.Riser + roofClearance

        frontFrames st trussSpacing
        |> Array.map (fun ((x, y), (nx, ny)) -> ((x + nx * back, y + ny * back, z), (-nx, -ny)))

    let roofSheets (st: Stand) : (float * float * float)[][] =
        match st.Roof with
        | NoRoof -> [||]
        | roof ->
            let span = roofSpan roof

            let ring ((rx, ry, rz), (ix, iy)) =
                [| (rx, ry, rz + 0.25)
                   (rx + ix * span, ry + iy * span, rz + 0.02 * span + 0.25) |]

            roofEdge st
            |> Array.pairwise
            |> Array.map (fun (r0, r1) ->
                let a = ring r0
                let b = ring r1

                [| for (x, y, z) in [| a.[0]; b.[0]; b.[1]; a.[1] |] -> (pyRound x 4, pyRound y 4, pyRound z 4) |])

    // Emitter positions come from kit.json and are passed in, so the port has no hidden file dependency.
    let lampPositions
        (pieces: Piece list)
        (kitEmitters: Map<string, (float * float * float) list>)
        : (float * float * float)[] =
        [| for pc in pieces do
               let sx, sy, sz = pc.Scale

               for (ex0, ey0, ez0) in Map.tryFind pc.Piece kitEmitters |> Option.defaultValue [] do
                   let ex = ex0 * sx
                   let ey = ey0 * sy
                   let ez = ez0 * sz
                   let a = toRadians pc.Yaw

                   yield
                       (pyRound (pc.X + ex * Math.Cos(a) - ey * Math.Sin(a)) 4,
                        pyRound (pc.Y + ex * Math.Sin(a) + ey * Math.Cos(a)) 4,
                        pyRound (pc.Z + ez) 4) |]

    let aimingTargets (lamps: (float * float * float)[]) : (float * float * float)[] =
        let cols = 13
        let rows = 7

        let grid =
            [| for j in 0 .. rows - 1 do
                   for i in 0 .. cols - 1 ->
                       (-pitchLength / 2.0 + 4.0 + (pitchLength - 8.0) * float i / float (cols - 1),
                        -pitchWidth / 2.0 + 4.0 + (pitchWidth - 8.0) * float j / float (rows - 1)) |]

        // Bearing order plus a stride co-prime with the grid size spreads neighbouring lamps over different spots.
        let order =
            Array.init lamps.Length id
            |> Array.sortBy (fun k ->
                let lx, ly, _ = lamps.[k]
                (Math.Atan2(ly, lx), k))

        let stride = 11
        let targets = Array.create lamps.Length (0.0, 0.0, 0.0)

        order
        |> Array.iteri (fun rank k ->
            let lx, ly, _ = lamps.[k]

            let pointAt step =
                grid.[(rank * stride + step) % grid.Length]

            // Far-side preference: the last step is taken unconditionally, so the search always terminates.
            let step =
                Seq.init grid.Length id
                |> Seq.find (fun s ->
                    let gx, gy = pointAt s
                    gx * lx + gy * ly <= 0.0 || s = grid.Length - 1)

            let gx, gy = pointAt step
            targets.[k] <- (pyRound gx 3, pyRound gy 3, 0.0))

        targets

    // A ~24 m spot at the target, clamped to real luminaire optics: far targets need narrow beams so a wide beam
    // from a corner mast does not spill its lower edge onto the stands.
    let beamHalfAngles (lamps: (float * float * float)[]) (targets: (float * float * float)[]) : float[] =
        Array.map2
            (fun (lx, ly, lz) (tx, ty, tz) ->
                let dx = lx - tx
                let dy = ly - ty
                let dz = lz - tz
                let d = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                pyRound (min 20.0 (max 6.0 (toDegrees (Math.Atan(12.0 / max d 1.0))))) 3)
            lamps
            targets

    let private yawTowardsPitch (st: Stand) =
        match st.Kind with
        | Corner ->
            let mid = toRadians ((st.A0 + st.A1) / 2.0)
            toDegrees (Math.Atan2(-Math.Sin(mid), -Math.Cos(mid))) - 90.0
        | Straight ->
            let dx = fst st.End - fst st.Start
            let dy = snd st.End - snd st.Start
            toDegrees (Math.Atan2(dy, dx))

    // Main TV position on the halfway line, in front of the main stand at 14-22 m; the camera rides this gantry so
    // framing follows each stadium's real geometry.
    let broadcastGantry (main: Stand) (p: float) : float * float * float =
        let target = 14.0 + 8.0 * p
        let my = snd main.Start

        match
            main.Tiers
            |> List.tryFind (fun t -> t.BaseHeight <= target && target <= t.TopHeight)
        with
        | Some t -> (0.0, my - (t.FrontOffset - 1.5), target)
        | None ->
            let top = List.last main.Tiers

            if main.Roof <> NoRoof then
                // A low roofed stand hangs the gantry under the roof edge so the trusses stay behind the lens.
                let span = roofSpan main.Roof

                let o =
                    max ((List.head main.Tiers).FrontOffset + 1.0) (top.BackOffset + 0.6 - span + 2.0)

                (0.0, my - o, top.TopHeight + roofClearance - 4.5)
            else
                (0.0, my - top.BackOffset, top.TopHeight + 2.5)

    let private corner (name: string) (centre: float * float) (radius: float) (a0: float) (tiers: Tier list) : Stand =
        { Side = name
          Kind = Corner
          Start = (0.0, 0.0)
          End = (0.0, 0.0)
          Centre = centre
          Radius = radius
          A0 = a0
          A1 = a0 + 90.0
          Tiers = tiers
          Roof = NoRoof
          Seated = true
          Main = false }

    let private pieces
        (p: float)
        (stands: Stand list)
        (hx: float)
        (hy: float)
        (lighting: Lighting)
        (facade: Facade)
        : Piece list =
        let out = ResizeArray<Piece>()
        let unitScale = (1.0, 1.0, 1.0)

        let add piece x y z yaw tint (sx, sy, sz) =
            out.Add(
                { Piece = piece
                  X = pyRound x 3
                  Y = pyRound y 3
                  Z = pyRound z 3
                  Yaw = pyRound yaw 2
                  Tint = tint
                  Scale = (pyRound sx 4, pyRound sy 4, pyRound sz 4) }
            )

        let put piece x y z yaw = add piece x y z yaw "none" unitScale

        // The goal's net hangs behind its local -Y; yaw +90 sends it to +X, so each net points away from the pitch.
        for sx in [ -1.0; 1.0 ] do
            put "goal" (sx * pitchLength / 2.0) 0.0 0.0 (90.0 * sx)
            put "goal_net" (sx * pitchLength / 2.0) 0.0 0.0 (90.0 * sx)

            for sy in [ -1.0; 1.0 ] do
                add "corner_flag" (sx * pitchLength / 2.0) (sy * pitchWidth / 2.0) 0.0 0.0 "clubPrimary" unitScale

        // Boards sit 4 m off the touchlines and 5 m behind the goal lines; the main side leaves the technical zone
        // clear, where the benches and tunnel meet the pitch.
        let boardY = pitchWidth / 2.0 + 4.0

        for side in [ -1.0; 1.0 ] do
            let mutable x = -pitchLength / 2.0

            while x + 6.0 <= pitchLength / 2.0 + 0.01 do
                if not (side < 0.0 && x + 6.0 > -techZoneHalf && x < techZoneHalf) then
                    let yaw = if side < 0.0 then 0.0 else 180.0
                    let px = if side < 0.0 then x else x + 6.0
                    put "adboard" px (side * boardY) 0.0 yaw

                x <- x + 6.0

        let boardX = pitchLength / 2.0 + 5.0

        for side in [ -1.0; 1.0 ] do
            let mutable y = -pitchWidth / 2.0 + 4.0

            while y + 6.0 <= pitchWidth / 2.0 - 4.0 + 0.01 do
                let yaw = if side < 0.0 then -90.0 else 90.0
                let py = if side < 0.0 then y + 6.0 else y
                put "adboard" (side * boardX) py 0.0 yaw
                y <- y + 6.0

        for st in stands do
            for (ti, t) in List.indexed st.Tiers do
                let fo = t.FrontOffset
                // Glass balustrades only in premium upper tiers; the lower tiers keep tubular rails.
                let rail = if ti > 0 && p > 0.55 then "railing_glass" else "railing"
                let railFrames = frontFrames st 2.0

                for ((x, y), (nx, ny)) in Array.take (railFrames.Length - 1) railFrames do
                    put
                        rail
                        (x + nx * (fo - 0.05))
                        (y + ny * (fo - 0.05))
                        (t.BaseHeight + 0.25)
                        (toDegrees (Math.Atan2(-ny, -nx)) - 90.0)

                if ti > 0 && p > 0.5 then
                    let ribbonFrames = frontFrames st 10.0

                    for ((x, y), (nx, ny)) in Array.take (ribbonFrames.Length - 1) ribbonFrames do
                        put
                            "led_ribbon"
                            (x + nx * (fo - 0.02))
                            (y + ny * (fo - 0.02))
                            (t.BaseHeight - 1.35)
                            (toDegrees (Math.Atan2(-ny, -nx)) - 90.0)

                if st.Seated && t.Rows >= 12 then
                    let fr = frontFrames st seatPitch
                    let gapRow = t.Rows / 2 - 2
                    let mutable block = 0
                    let mutable running = true

                    while running do
                        let i0 = block * (aisleEvery + 2) + aisleEvery / 2

                        if i0 + 1 >= fr.Length then
                            running <- false
                        else
                            if vomitoryBlock block then
                                let (x, y), (nx, ny) = fr.[i0]
                                let o = fo + float gapRow * t.RowDepth

                                put
                                    "vomitory"
                                    (x + nx * o)
                                    (y + ny * o)
                                    (t.BaseHeight + float gapRow * t.Riser)
                                    (toDegrees (Math.Atan2(-ny, -nx)) - 90.0)

                            block <- block + 1

            // Cantilever trusses every TRUSS spacing from the back of the top tier; lit roofs carry a lamp gantry.
            if st.Roof <> NoRoof then
                let span = roofSpan st.Roof
                let edge = roofEdge st

                for ((rx, ry, rz), (ix, iy)) in edge do
                    add
                        ("roof_" + roofName st.Roof)
                        rx
                        ry
                        rz
                        (toDegrees (Math.Atan2(iy, ix)) - 90.0)
                        "clubPrimary"
                        unitScale

                if lighting = RoofLighting then
                    for (((rx, ry, rz), (ix, iy)), ((qx, qy, _), _)) in Array.pairwise edge do
                        let len = Double.Hypot(qx - rx, qy - ry)

                        add
                            "roof_light_bar"
                            (rx + ix * (span - 0.5))
                            (ry + iy * (span - 0.5))
                            (rz + 0.02 * span - 0.6)
                            (toDegrees (Math.Atan2(iy, ix)) - 90.0)
                            "none"
                            (len / 10.0, 1.0, 1.0)

            // Exterior skin: 10 m facade modules stacked in 5 m levels behind the top tier, facing outwards.
            let top = List.last st.Tiers
            let back = top.FrontOffset + float top.Rows * top.RowDepth + 1.4
            let height = top.BaseHeight + float top.Rows * top.Riser + 1.0
            let fr10 = frontFrames st 10.0

            // Modules are measured on the facade line, not the front: around a bowl corner the facade runs a
            // longer chord than the front, and modules sized on the front would leave gaps.
            for (((px, py), (pnx, pny)), ((qx, qy), (qnx, qny))) in Array.pairwise fr10 do
                let ax, ay = px + pnx * back, py + pny * back
                let bx, by = qx + qnx * back, qy + qny * back
                let len = Double.Hypot(bx - ax, by - ay)
                let ox, oy = (by - ay) / len, -(bx - ax) / len

                for level in 0 .. max 1 (int (height / 5.0)) - 1 do
                    add
                        ("facade_" + facadeName facade)
                        bx
                        by
                        (float level * 5.0)
                        (toDegrees (Math.Atan2(oy, ox)) - 90.0)
                        "none"
                        (len / 10.0, 1.0, 1.0)

        // The south stand is always the first side built, so a main stand is always present.
        let main = stands |> List.find (fun s -> s.Main)
        let my = snd main.Start

        // One bench per club, symmetric about the halfway line, fronts 3 m off the touchline.
        let benchFront = -pitchWidth / 2.0 - 3.0
        put "dugout" (-benchInner - benchLength) benchFront 0.0 0.0
        put "dugout" benchInner benchFront 0.0 0.0

        // The tunnel runs from the main stand's front to just behind the bench line, never onto the pitch.
        let tunnelEnd = benchFront - 2.0
        add "tunnel" 0.0 my 0.0 0.0 "clubPrimary" (1.0, max 0.3 ((tunnelEnd - my) / 8.0), 1.0)

        let gx, gy, gz = broadcastGantry main p
        put "camera_gantry" gx gy gz 0.0

        if lighting = TowerLighting then
            // Masts must clear the highest roof with margin, or their beams land on the cladding instead of the pitch.
            let highest =
                stands
                |> List.map (fun st ->
                    (List.last st.Tiers).TopHeight
                    + (if st.Roof <> NoRoof then roofClearance + 6.0 else 2.0))
                |> List.max

            let zScale = max 1.0 ((highest + 10.0) / towerHeight)

            for sx in [ -1.0; 1.0 ] do
                for sy in [ -1.0; 1.0 ] do
                    let yaw = toDegrees (Math.Atan2(-sy, -sx)) - 90.0
                    add "floodlight_tower" (sx * (hx + 8.0)) (sy * (hy + 8.0)) 0.0 yaw "none" (1.0, 1.0, zScale)

        if p >= 0.4 then
            let ends = stands |> List.filter (fun s -> s.Side = "west" || s.Side = "east")
            let count = if p < 0.7 then 1 else 2

            for st in List.truncate count ends do
                let yaw = yawTowardsPitch st
                let top = List.last st.Tiers

                let x =
                    fst st.Start + (if st.Side = "west" then -1.0 else 1.0) * (top.BackOffset + 2.0)

                put "scoreboard" x 0.0 (top.TopHeight + 4.0) yaw

        List.ofSeq out

    let generate (reputation: int) (leagueLevel: int) (seed: int) : Layout =
        // The seed mix is a fixed contract: changing it reshuffles every stadium the game has ever generated.
        let rng = Rng(uint64 seed * 0x9E37UL + 0x51A7UL)
        let p = prestigeOf reputation leagueLevel
        let sideRunoff = 6.5 + 2.5 * rng.Unit()
        let endRunoff = 8.0 + 3.0 * rng.Unit()
        let hx = pitchLength / 2.0 + endRunoff
        let hy = pitchWidth / 2.0 + sideRunoff

        let shape =
            if p >= 0.7 then Bowl
            elif p >= 0.4 then Closed
            else Open

        let mutable lighting = if p >= 0.6 then RoofLighting else TowerLighting
        let facade = if p >= 0.6 then Fins else Panels

        // The draw happens only above the low-league floor: the rng sequence after it is part of the golden contract.
        let seatColours =
            if p < 0.3 then
                Solid
            else
                match int (rng.Unit() * 3.0) with
                | 0 -> Solid
                | 1 -> Tiered
                | _ -> Blocks

        // South is the main stand (cameras, dugouts, tunnel); the TV camera looks at the north stand.
        let sides =
            [ ("south", (-hx + 4.0, -hy), (hx - 4.0, -hy), true, 1.0)
              ("north", (hx - 4.0, hy), (-hx + 4.0, hy), false, 1.0)
              ("west", (-hx, hy - 6.0), (-hx, -hy + 6.0), false, 0.8)
              ("east", (hx, -hy + 6.0), (hx, hy - 6.0), false, 0.8) ]

        // A bowl is one continuous structure: every side shares the tier section so the corners close cleanly.
        let bowlTiers = if shape = Bowl then tiersFor p rng false 1.0 else []
        let stands = ResizeArray<Stand>()

        for (name, a0, b0, main, scale) in sides do
            let a, b =
                if shape = Bowl then
                    if name = "south" || name = "north" then
                        let sx = hx - cornerRadius
                        let ax = if name = "south" then -sx else sx
                        let bx = if name = "south" then sx else -sx
                        (ax, snd a0), (bx, snd b0)
                    else
                        let sy = hy - cornerRadius
                        let ay = if name = "west" then sy else -sy
                        let by = if name = "west" then -sy else sy
                        (fst a0, ay), (fst b0, by)
                elif shape = Open && (name = "west" || name = "east") then
                    // Open grounds: shorter end stands with gaps at the corners.
                    (fst a0, snd a0 * 0.75), (fst b0, snd b0 * 0.75)
                else
                    a0, b0

            let tiers =
                if shape = Bowl then
                    bowlTiers
                else
                    tiersFor p rng main scale
            // Standing terraces are only a low-league feature, and never on the main side.
            let seated = not (p < 0.3 && not main && rng.Chance(0.45))

            stands.Add(
                { Side = name
                  Kind = Straight
                  Start = a
                  End = b
                  Centre = (0.0, 0.0)
                  Radius = 0.0
                  A0 = 0.0
                  A1 = 0.0
                  Tiers = tiers
                  Roof = NoRoof
                  Seated = seated
                  Main = main }
            )

        let bowlCorners =
            [ ("corner_se", 1.0, -1.0, -90.0)
              ("corner_ne", 1.0, 1.0, 0.0)
              ("corner_nw", -1.0, 1.0, 90.0)
              ("corner_sw", -1.0, -1.0, 180.0) ]

        if shape = Bowl then
            for (name, cx, cy, a0) in bowlCorners do
                stands.Add(corner name (cx * (hx - cornerRadius), cy * (hy - cornerRadius)) cornerRadius a0 bowlTiers)
        elif shape = Closed then
            // Closed grounds infill two diagonal corners with a single low tier; the seed picks the pair.
            let pair =
                if rng.Chance(0.5) then
                    [ ("corner_ne", 1.0, 1.0, 0.0); ("corner_sw", -1.0, -1.0, 180.0) ]
                else
                    [ ("corner_se", 1.0, -1.0, -90.0); ("corner_nw", -1.0, 1.0, 90.0) ]

            for (name, cx, cy, a0) in pair do
                let tiers = tiersFor (min p 0.29) rng false 0.7
                stands.Add(corner name (cx * (hx - 6.0), cy * (hy - 6.0)) 6.0 a0 tiers)

        for i in 0 .. stands.Count - 1 do
            let st = stands.[i]

            let roof =
                if p < 0.2 then
                    if st.Main then Truss20 else NoRoof
                elif p < 0.45 then
                    if st.Main || st.Side = "north" then Truss20
                    elif rng.Chance(0.5) then Truss20
                    else NoRoof
                else
                    let depth = (List.last st.Tiers).BackOffset - (List.head st.Tiers).FrontOffset

                    if depth > 32.0 then Truss40
                    elif depth > 22.0 then Truss30
                    else Truss20

            stands.[i] <- { st with Roof = roof }

        // Deep roofs reach almost to the touchline: no corner mast can light past them, so such grounds light from
        // the roof edge. Towers remain for open grounds with short roofs.
        if stands |> Seq.exists (fun st -> st.Roof = Truss30 || st.Roof = Truss40) then
            lighting <- RoofLighting

        let mutable capacity = 0

        for st in stands do
            let runLength = length st

            for t in st.Tiers do
                // Two-metre aisles per AISLE_EVERY seats; a standing terrace holds ~1.8 people per seat footprint.
                let perRow = int (runLength / seatPitch) * aisleEvery / (aisleEvery + 2)
                capacity <- capacity + int (float (perRow * t.Rows) * (if st.Seated then 1.0 else 1.8))

        let stands = List.ofSeq stands

        { Schema = 1
          Prestige = pyRound p 4
          Capacity = capacity
          Shape = shape
          Lighting = lighting
          Facade = facade
          SeatColours = seatColours
          Stands = stands
          Pieces = pieces p stands hx hy lighting facade }
