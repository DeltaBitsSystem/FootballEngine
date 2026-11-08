module FootballEngine.Client.Tests.StadiumLayoutTests

open System
open System.IO
open System.Text.Json
open Expecto
open FootballEngine.Client.Match3D

// Golden values were produced by stadium_layout.py; a mismatch means the port drifted from the specification.
// Floats are compared at 1e-3 (values are rounded to 3-4 decimals in Python, so a last-bit difference cannot cross
// a rounding boundary without moving by 1e-3). Sums are rounded to 2 decimals in the golden, so they are compared
// at 1e-2: one unit of that rounding, never more.
let private floatTolerance = 1e-3
let private roundedSumTolerance = 1e-2

let private vectorNames =
    [ "giant"; "big"; "mid_top"; "small_top"; "second_div"; "third_div" ]

let private goldenText =
    lazy (File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layout-golden.json")))

let private assertClose (tolerance: float) (label: string) (expected: float) (actual: float) =
    let delta = abs (expected - actual)
    Expect.isTrue (delta <= tolerance) $"{label}: expected {expected}, actual {actual}, delta {delta}"

let private assertPair (tolerance: float) (label: string) (expected: float * float) (actual: float * float) =
    assertClose tolerance $"{label} x" (fst expected) (fst actual)
    assertClose tolerance $"{label} y" (snd expected) (snd actual)

let private vec2 (e: JsonElement) =
    let values = e.EnumerateArray() |> Seq.map (fun v -> v.GetDouble()) |> Seq.toArray
    Expect.equal values.Length 2 "pair length"
    (values.[0], values.[1])

let private vec3 (e: JsonElement) =
    let values = e.EnumerateArray() |> Seq.map (fun v -> v.GetDouble()) |> Seq.toArray
    Expect.equal values.Length 3 "triple length"
    (values.[0], values.[1], values.[2])

let private loadKitEmitters () : Map<string, (float * float * float) list> =
    use document =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "kit.json")))

    document.RootElement.GetProperty("pieces").EnumerateArray()
    |> Seq.choose (fun piece ->
        let emitters = piece.GetProperty("emitters").EnumerateArray() |> Seq.toList

        if List.isEmpty emitters then
            None
        else
            let positions = emitters |> List.map (fun e -> vec3 (e.GetProperty("position")))
            Some(piece.GetProperty("id").GetString(), positions))
    |> Map.ofSeq

// Each test parses the golden fresh so a failure is local to its own vector and digest group.
let private withVector (name: string) (check: JsonElement -> StadiumLayout.Layout -> unit) () =
    use document = JsonDocument.Parse(goldenText.Force())
    let entry = document.RootElement.GetProperty(name)
    let input = entry.GetProperty("input")

    let layout =
        StadiumLayout.generate
            (input.GetProperty("reputation").GetInt32())
            (input.GetProperty("leagueLevel").GetInt32())
            (input.GetProperty("seed").GetInt32())

    check entry layout

let private checkIdentity (entry: JsonElement) (layout: StadiumLayout.Layout) =
    Expect.equal layout.Schema (entry.GetProperty("schema").GetInt32()) "schema"
    assertClose floatTolerance "prestige" (entry.GetProperty("prestige").GetDouble()) layout.Prestige
    Expect.equal layout.Capacity (entry.GetProperty("capacity").GetInt32()) "capacity"
    Expect.equal (StadiumLayout.shapeName layout.Shape) (entry.GetProperty("shape").GetString()) "shape"
    Expect.equal (StadiumLayout.facadeName layout.Facade) (entry.GetProperty("facade").GetString()) "facade"

    Expect.equal
        (StadiumLayout.seatColoursName layout.SeatColours)
        (entry.GetProperty("seat_colours").GetString())
        "seat colours"

    // The golden's "lighting" key holds the lamp digest and replaces the mode string, so the mode is pinned through
    // the towers it places: towers exist exactly when lighting is towers.
    let hasTowers =
        layout.Pieces |> List.exists (fun piece -> piece.Piece = "floodlight_tower")

    Expect.equal
        hasTowers
        (layout.Lighting = StadiumLayout.TowerLighting)
        "towers are placed exactly when lighting is towers"

let private checkStands (entry: JsonElement) (layout: StadiumLayout.Layout) =
    let expected = entry.GetProperty("stands").EnumerateArray() |> Seq.toArray
    Expect.equal layout.Stands.Length expected.Length "stand count"

    for (i, actual) in List.indexed layout.Stands do
        let exp = expected.[i]
        let label = exp.GetProperty("side").GetString()
        Expect.equal actual.Side label "side"
        Expect.equal (StadiumLayout.kindName actual.Kind) (exp.GetProperty("kind").GetString()) $"{label} kind"
        assertPair floatTolerance $"{label} start" (vec2 (exp.GetProperty("start"))) actual.Start
        assertPair floatTolerance $"{label} end" (vec2 (exp.GetProperty("end"))) actual.End
        assertPair floatTolerance $"{label} centre" (vec2 (exp.GetProperty("centre"))) actual.Centre
        assertClose floatTolerance $"{label} radius" (exp.GetProperty("radius").GetDouble()) actual.Radius
        assertClose floatTolerance $"{label} a0" (exp.GetProperty("a0").GetDouble()) actual.A0
        assertClose floatTolerance $"{label} a1" (exp.GetProperty("a1").GetDouble()) actual.A1
        Expect.equal (StadiumLayout.roofName actual.Roof) (exp.GetProperty("roof").GetString()) $"{label} roof"
        Expect.equal actual.Seated (exp.GetProperty("seated").GetBoolean()) $"{label} seated"
        Expect.equal actual.Main (exp.GetProperty("main").GetBoolean()) $"{label} main"

        let tiers = exp.GetProperty("tiers").EnumerateArray() |> Seq.toArray
        Expect.equal actual.Tiers.Length tiers.Length $"{label} tier count"

        for (j, tier) in List.indexed actual.Tiers do
            let et = tiers.[j]
            let tierLabel = $"{label} tier {j}"
            Expect.equal tier.Rows (et.GetProperty("rows").GetInt32()) $"{tierLabel} rows"
            assertClose floatTolerance $"{tierLabel} row depth" (et.GetProperty("row_depth").GetDouble()) tier.RowDepth
            assertClose floatTolerance $"{tierLabel} riser" (et.GetProperty("riser").GetDouble()) tier.Riser

            assertClose
                floatTolerance
                $"{tierLabel} front offset"
                (et.GetProperty("front_offset").GetDouble())
                tier.FrontOffset

            assertClose
                floatTolerance
                $"{tierLabel} base height"
                (et.GetProperty("base_height").GetDouble())
                tier.BaseHeight

            Expect.equal tier.VipBand (et.GetProperty("vip_band").GetBoolean()) $"{tierLabel} vip band"

let private checkPieces (entry: JsonElement) (layout: StadiumLayout.Layout) =
    let expected = entry.GetProperty("pieces").EnumerateArray() |> Seq.toArray
    Expect.equal layout.Pieces.Length expected.Length "piece count"

    for (i, actual) in List.indexed layout.Pieces do
        let exp = expected.[i]
        let name = exp.GetProperty("piece").GetString()
        let label = $"piece {i} {name}"
        Expect.equal actual.Piece name $"{label} name"
        Expect.equal actual.Tint (exp.GetProperty("tint").GetString()) $"{label} tint"
        assertClose floatTolerance $"{label} x" (exp.GetProperty("x").GetDouble()) actual.X
        assertClose floatTolerance $"{label} y" (exp.GetProperty("y").GetDouble()) actual.Y
        assertClose floatTolerance $"{label} z" (exp.GetProperty("z").GetDouble()) actual.Z
        assertClose floatTolerance $"{label} yaw" (exp.GetProperty("yaw").GetDouble()) actual.Yaw
        let sx, sy, sz = vec3 (exp.GetProperty("scale"))
        let ax, ay, az = actual.Scale
        assertClose floatTolerance $"{label} scale x" sx ax
        assertClose floatTolerance $"{label} scale y" sy ay
        assertClose floatTolerance $"{label} scale z" sz az

let private checkGeometry (entry: JsonElement) (layout: StadiumLayout.Layout) =
    let expected = entry.GetProperty("geometry").EnumerateArray() |> Seq.toArray

    let actual =
        [ for st in layout.Stands do
              for (ti, t) in List.indexed st.Tiers -> (st, ti, t) ]

    Expect.equal actual.Length expected.Length "stand-tier count"

    for ((st, ti, t), exp) in List.zip actual (List.ofArray expected) do
        let label = $"{st.Side} tier {ti}"
        Expect.equal st.Side (exp.GetProperty("stand").GetString()) $"{label} stand"
        Expect.equal ti (exp.GetProperty("tier").GetInt32()) $"{label} tier index"

        let profile = StadiumLayout.tierProfile t (ti > 0)

        let expProfile =
            exp.GetProperty("profile").EnumerateArray() |> Seq.map vec2 |> Seq.toArray

        Expect.equal profile.Length expProfile.Length $"{label} profile point count"

        Array.iteri2 (fun i a b -> assertPair floatTolerance $"{label} profile {i}" b a) profile expProfile

        let seats = StadiumLayout.seatPositions st t
        Expect.equal seats.Length (exp.GetProperty("seatCount").GetInt32()) $"{label} seat count"

        let expFirst = exp.GetProperty("firstSeats").EnumerateArray() |> Seq.toArray

        for i in 0 .. expFirst.Length - 1 do
            let values = expFirst.[i].EnumerateArray() |> Seq.toArray
            let x, y, z, yaw, row = seats.[i]
            let seatLabel = $"{label} seat {i}"
            assertClose floatTolerance $"{seatLabel} x" (values.[0].GetDouble()) x
            assertClose floatTolerance $"{seatLabel} y" (values.[1].GetDouble()) y
            assertClose floatTolerance $"{seatLabel} z" (values.[2].GetDouble()) z
            assertClose floatTolerance $"{seatLabel} yaw" (values.[3].GetDouble()) yaw
            Expect.equal row (values.[4].GetInt32()) $"{seatLabel} row"

        let sumOf (pick: float * float * float * float * int -> float) = seats |> Array.sumBy pick

        let expSum =
            exp.GetProperty("seatSum").EnumerateArray()
            |> Seq.map (fun v -> v.GetDouble())
            |> Seq.toArray

        assertClose roundedSumTolerance $"{label} seat sum x" expSum.[0] (sumOf (fun (x, _, _, _, _) -> x))
        assertClose roundedSumTolerance $"{label} seat sum y" expSum.[1] (sumOf (fun (_, y, _, _, _) -> y))
        assertClose roundedSumTolerance $"{label} seat sum z" expSum.[2] (sumOf (fun (_, _, z, _, _) -> z))
        assertClose roundedSumTolerance $"{label} seat sum yaw" expSum.[3] (sumOf (fun (_, _, _, yaw, _) -> yaw))

let private checkLighting (entry: JsonElement) (layout: StadiumLayout.Layout) =
    let g = entry.GetProperty("lighting")
    let lamps = StadiumLayout.lampPositions layout.Pieces (loadKitEmitters ())
    let targets = StadiumLayout.aimingTargets lamps
    let beams = StadiumLayout.beamHalfAngles lamps targets

    let sheets =
        layout.Stands
        |> List.collect (fun st -> List.ofArray (StadiumLayout.roofSheets st))

    Expect.equal lamps.Length (g.GetProperty("lampCount").GetInt32()) "lamp count"

    let expLamps =
        g.GetProperty("firstLamps").EnumerateArray() |> Seq.map vec3 |> Seq.toArray

    for i in 0 .. expLamps.Length - 1 do
        let ex, ey, ez = expLamps.[i]
        let ax, ay, az = lamps.[i]
        assertClose floatTolerance $"lamp {i} x" ex ax
        assertClose floatTolerance $"lamp {i} y" ey ay
        assertClose floatTolerance $"lamp {i} z" ez az

    let expTargets =
        g.GetProperty("firstTargets").EnumerateArray() |> Seq.map vec3 |> Seq.toArray

    for i in 0 .. expTargets.Length - 1 do
        let ex, ey, ez = expTargets.[i]
        let ax, ay, az = targets.[i]
        assertClose floatTolerance $"target {i} x" ex ax
        assertClose floatTolerance $"target {i} y" ey ay
        assertClose floatTolerance $"target {i} z" ez az

    let expLampSum = vec3 (g.GetProperty("lampSum"))
    let expTargetSum = vec3 (g.GetProperty("targetSum"))
    let sumAll (points: (float * float * float)[]) pick = points |> Array.sumBy pick
    let lx, ly, lz = expLampSum
    assertClose roundedSumTolerance "lamp sum x" lx (sumAll lamps (fun (x, _, _) -> x))
    assertClose roundedSumTolerance "lamp sum y" ly (sumAll lamps (fun (_, y, _) -> y))
    assertClose roundedSumTolerance "lamp sum z" lz (sumAll lamps (fun (_, _, z) -> z))
    let tx, ty, tz = expTargetSum
    assertClose roundedSumTolerance "target sum x" tx (sumAll targets (fun (x, _, _) -> x))
    assertClose roundedSumTolerance "target sum y" ty (sumAll targets (fun (_, y, _) -> y))
    assertClose roundedSumTolerance "target sum z" tz (sumAll targets (fun (_, _, z) -> z))

    assertClose
        roundedSumTolerance
        "beam half-angle sum"
        (g.GetProperty("beamHalfAngleSum").GetDouble())
        (Array.sum beams)

    let expBeams =
        g.GetProperty("firstBeams").EnumerateArray()
        |> Seq.map (fun v -> v.GetDouble())
        |> Seq.toArray

    for i in 0 .. expBeams.Length - 1 do
        assertClose floatTolerance $"beam {i}" expBeams.[i] beams.[i]

    Expect.equal sheets.Length (g.GetProperty("roofSheetCount").GetInt32()) "roof sheet count"

    let sheetCoordinateSum =
        sheets
        |> List.sumBy (fun quad -> quad |> Array.sumBy (fun (x, y, z) -> x + y + z))

    assertClose
        roundedSumTolerance
        "roof sheet coordinate sum"
        (g.GetProperty("roofSheetSum").GetDouble())
        sheetCoordinateSum

let private vectorTests (name: string) =
    testList
        name
        [ testCase "identity fields" <| withVector name checkIdentity
          testCase "stands and tiers" <| withVector name checkStands
          testCase "pieces" <| withVector name checkPieces
          testCase "stand geometry: profiles and seats" <| withVector name checkGeometry
          testCase "lighting: lamps, aiming, beams and roof sheets"
          <| withVector name checkLighting ]

let tests =
    testList
        "Stadium layout"
        [ testCase "pyRound follows Python round on binary-below values and exact ties"
          <| fun _ ->
              // Python rounds the exact binary value, so 2.675 is below the tie; a scaled round-to-even gets 0.005 wrong.
              Expect.equal (StadiumLayout.pyRound 2.675 2) 2.67 "binary value below the tie"
              Expect.equal (StadiumLayout.pyRound 1.005 2) 1.0 "binary value below the tie"
              Expect.equal (StadiumLayout.pyRound 0.1445 3) 0.144 "binary value below the tie at 3 digits"
              Expect.equal (StadiumLayout.pyRound 0.005 2) 0.01 "binary value above the tie rounds up"
              Expect.equal (StadiumLayout.pyRound 0.125 2) 0.12 "exact tie rounds to even"
              Expect.equal (StadiumLayout.pyRound 0.375 2) 0.38 "exact tie rounds to even upward"
              Expect.isTrue (1.0 / StadiumLayout.pyRound (-0.0001) 3 < 0.0) "negative zero is kept as Python does"

          testList "golden vectors" [ for name in vectorNames -> vectorTests name ] ]
