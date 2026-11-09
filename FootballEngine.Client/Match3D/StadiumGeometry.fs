namespace FootballEngine.Client.Match3D

open System
open System.Numerics

/// What decides the home ground's look: the layout generator's inputs plus the club colours (linear RGB).
type StadiumIdentity =
    { Reputation: int
      LeagueLevel: int
      Seed: int
      Primary: Vector3
      Secondary: Vector3 }

module StadiumIdentity =
    let private srgbChannel (value: int) =
        let c = float32 value / 255.0f

        if c <= 0.04045f then
            c / 12.92f
        else
            MathF.Pow((c + 0.055f) / 1.055f, 2.4f)

    /// Colours arrive as 0xAARRGGBB; a zero alpha byte means the club has no authored kit colour.
    let linearOfPacked (packed: uint32) (fallback: Vector3) =
        if packed >>> 24 = 0u then
            fallback
        else
            Vector3(
                srgbChannel (int ((packed >>> 16) &&& 0xFFu)),
                srgbChannel (int ((packed >>> 8) &&& 0xFFu)),
                srgbChannel (int (packed &&& 0xFFu))
            )

    /// The look-dev renders use this red and white, so an unbranded ground matches the reference images.
    let defaultPrimary = linearOfPacked 0xFFE2353Bu Vector3.One
    let defaultSecondary = Vector3.One

    let create reputation leagueLevel seed primary secondary =
        { Reputation = reputation
          LeagueLevel = leagueLevel
          Seed = seed
          Primary = linearOfPacked primary defaultPrimary
          Secondary = linearOfPacked secondary defaultSecondary }

    let neutral = create 5000 1 0 0u 0u

/// A run of instances of one kit piece that share a level of detail; seats are grouped spatially so a whole
/// block switches together and the per-frame work is per block, not per seat.
type StadiumCluster =
    { Piece: int
      FirstInstance: int
      InstanceCount: int
      Centre: Vector3
      Radius: float32 }

[<Struct>]
type KitBatch =
    { Piece: int
      Lod: int
      FirstInstance: uint32
      InstanceCount: uint32 }

type StadiumBuild =
    {
        Layout: StadiumLayout.Layout
        /// Stands, roofs, runoff and pitch in renderer space, transformed by the identity stadium root.
        Vertices: GpuVertex[]
        Indices: uint32[]
        /// Three vec4 per instance in layout space: (x, y, z, yaw degrees), (sx, sy, sz, 0), (r, g, b, 1).
        Instances: Vector4[]
        /// Ordered by piece, then by instance, so merged clusters stay contiguous in the instance buffer.
        Clusters: StadiumCluster[]
        /// First instance and count of every kit piece, indexed like `StadiumKitData.Pieces`.
        PieceInstances: struct (int * int)[]
        SeatCount: int
    }

module StadiumGeometry =
    let instanceVectors = 3

    /// The largest generated ground (a 9999-reputation bowl) places about 70k seats and 2k pieces; the buffer is
    /// sized once so a stadium change never reallocates the scene bind group.
    let instanceCapacity = 131072

    let toRenderer (x: float) (y: float) (z: float) = Vector3(float32 x, float32 z, float32 -y)

    let private normalToRenderer (n: Vector3) = Vector3(n.X, n.Z, -n.Y)

    type private MeshOut(kit: StadiumKitData) =
        let vertices = ResizeArray<GpuVertex>()
        let indices = ResizeArray<uint32>()

        member _.Vertices = vertices
        member _.Indices = indices
        member _.Material(id: string) = kit.Materials[id]

        member _.Add(material: KitMaterial, position: Vector3, normal: Vector3, uv: Vector2) =
            vertices.Add(StadiumKit.vertex (uint32 SceneRoot.stadium) material Vector2.One position normal uv)
            uint32 (vertices.Count - 1)

        member this.Quad(material, a: Vector3, b: Vector3, c: Vector3, d: Vector3, normal, uvs: Vector2[]) =
            let ia = this.Add(material, a, normal, uvs[0])
            let ib = this.Add(material, b, normal, uvs[1])
            let ic = this.Add(material, c, normal, uvs[2])
            let id = this.Add(material, d, normal, uvs[3])
            indices.AddRange [| ia; ib; ic; ia; ic; id |]

    let private frameStep = 2.0

    /// Sweeps a stand section along the stand front. Vertices are shared along the run so curved corners shade
    /// smoothly; each profile segment keeps its own vertices so treads and risers keep crisp edges.
    let private extrude (out: MeshOut) (material: KitMaterial) (stand: StadiumLayout.Stand) (profile: (float * float)[]) =
        let frames = StadiumLayout.frontFrames stand frameStep

        for i in 0 .. profile.Length - 2 do
            let o0, z0 = profile[i]
            let o1, z1 = profile[i + 1]
            let dO, dZ = o1 - o0, z1 - z0
            let length = Math.Sqrt(dO * dO + dZ * dZ)

            if length > 1e-6 then
                // The profile runs clockwise in (outward, up), so (-dz, do) points out of the solid section.
                let nO, nZ = -dZ / length, dO / length
                // Mostly horizontal faces take plan UVs; walls run their U along the stand so corners have no seam.
                let horizontal = abs nZ > 0.7
                let first = uint32 out.Vertices.Count
                let mutable along0 = 0.0
                let mutable along1 = 0.0
                let mutable previous: (float * float * float * float) option = None

                for (px, py), (nx, ny) in frames do
                    let x0, y0 = px + nx * o0, py + ny * o0
                    let x1, y1 = px + nx * o1, py + ny * o1

                    match previous with
                    | Some(qx0, qy0, qx1, qy1) ->
                        along0 <- along0 + Math.Sqrt((x0 - qx0) ** 2.0 + (y0 - qy0) ** 2.0)
                        along1 <- along1 + Math.Sqrt((x1 - qx1) ** 2.0 + (y1 - qy1) ** 2.0)
                    | None -> ()

                    previous <- Some(x0, y0, x1, y1)

                    let normal =
                        normalToRenderer (Vector3(float32 (nx * nO), float32 (ny * nO), float32 nZ))

                    let uv (x: float) (y: float) (z: float) (along: float) =
                        if horizontal then
                            Vector2(float32 (x / 2.0), float32 (y / 2.0))
                        else
                            Vector2(float32 (along / 2.0), float32 (z / 2.0))

                    out.Add(material, toRenderer x0 y0 z0, normal, uv x0 y0 z0 along0) |> ignore
                    out.Add(material, toRenderer x1 y1 z1, normal, uv x1 y1 z1 along1) |> ignore

                for k in 0 .. frames.Length - 2 do
                    let a = first + uint32 (2 * k)
                    out.Indices.AddRange [| a; a + 1u; a + 3u; a; a + 3u; a + 2u |]

    /// Straight stands end in open sections; filling under every outward-going segment closes the solid profile.
    let private caps (out: MeshOut) (material: KitMaterial) (stand: StadiumLayout.Stand) (profile: (float * float)[]) (bottom: float) =
        let frames = StadiumLayout.frontFrames stand frameStep
        let (ax, ay), _ = frames[0]
        let (bx, by), _ = frames[frames.Length - 1]
        let length = Math.Sqrt((bx - ax) ** 2.0 + (by - ay) ** 2.0)
        let dx, dy = (bx - ax) / length, (by - ay) / length

        for ((px, py), (nx, ny)), sign in [ frames[0], -1.0; frames[frames.Length - 1], 1.0 ] do
            let normal =
                normalToRenderer (Vector3(float32 (dx * sign), float32 (dy * sign), 0.0f))

            let point (o: float) (z: float) = toRenderer (px + nx * o) (py + ny * o) z
            let uv (o: float) (z: float) = Vector2(float32 (o / 2.0), float32 (z / 2.0))

            for i in 0 .. profile.Length - 2 do
                let o0, z0 = profile[i]
                let o1, z1 = profile[i + 1]

                if o1 - o0 > 1e-6 && min z0 z1 > bottom then
                    out.Quad(
                        material,
                        point o0 bottom,
                        point o1 bottom,
                        point o1 z1,
                        point o0 z0,
                        normal,
                        [| uv o0 bottom; uv o1 bottom; uv o1 z1; uv o0 z0 |]
                    )

    let private stands (out: MeshOut) (layout: StadiumLayout.Layout) =
        let concrete = out.Material "concrete"

        for stand in layout.Stands do
            let tiers = List.toArray stand.Tiers

            for ti in 0 .. tiers.Length - 1 do
                let tier = tiers[ti]
                let upper = ti > 0
                let profile = StadiumLayout.tierProfile tier upper
                extrude out concrete stand profile

                if stand.Kind = StadiumLayout.Straight then
                    caps out concrete stand profile (if upper then tier.BaseHeight - 1.4 else 0.0)

                if ti + 1 < tiers.Length then
                    // Closes the void between tiers; without it the sky shows through the middle of the stand.
                    let next = tiers[ti + 1]
                    let back = tier.BackOffset + 0.6
                    let z0 = tier.TopHeight + 1.1
                    let z1 = next.BaseHeight - 1.4

                    if z1 > z0 then
                        // Hospitality glazing is read as dark reflective curtain wall, not see-through glass: the void
                        // behind it has no interior to reveal.
                        let band =
                            out.Material(if next.VipBand then "curtain_glass" else "painted_steel:charcoal")

                        extrude out band stand [| back, z0; back, z1 |]

    let private roofs (out: MeshOut) (layout: StadiumLayout.Layout) =
        let sheet = out.Material "roof_sheet:white"

        for stand in layout.Stands do
            for quad in StadiumLayout.roofSheets stand do
                if quad.Length = 4 then
                    let corner index =
                        let x, y, z = quad[index]
                        Vector3(float32 x, float32 y, float32 z)

                    let a, b, c = corner 0, corner 1, corner 2
                    let raw = Vector3.Cross(b - a, c - a)
                    // Sheets are lit from above; the shader flips the normal for the underside the broadcast sees.
                    let up = if raw.Z < 0.0f then -raw else raw
                    let normal = normalToRenderer (Vector3.Normalize up)
                    let at index =
                        let x, y, z = quad[index]
                        toRenderer x y z

                    let uv index =
                        let x, y, _ = quad[index]
                        Vector2(float32 x, float32 y)

                    out.Quad(sheet, at 0, at 1, at 2, at 3, normal, [| uv 0; uv 1; uv 2; uv 3 |])

    /// Grass to just inside the stand fronts and a concrete apron beyond, so gaps between stands show ground.
    let private ground (out: MeshOut) (layout: StadiumLayout.Layout) =
        let points (offset: StadiumLayout.Stand -> float) =
            layout.Stands
            |> List.collect (fun stand ->
                let o = offset stand

                StadiumLayout.frontFrames stand frameStep
                |> Array.map (fun ((px, py), (nx, ny)) -> px + nx * o, py + ny * o)
                |> List.ofArray)

        let bounds (pts: (float * float) list) margin =
            (pts |> List.map (fst >> abs) |> List.max) + margin, (pts |> List.map (snd >> abs) |> List.max) + margin

        let fronts = points (fun stand -> (List.head stand.Tiers).FrontOffset)
        let backs = points (fun stand -> (List.last stand.Tiers).BackOffset + 0.6)
        let innerX, innerY = bounds fronts 1.0
        let outerX, outerY = bounds backs 8.0
        let halfLength, halfWidth = StadiumLayout.pitchLength / 2.0, StadiumLayout.pitchWidth / 2.0
        let up = Vector3.UnitY

        let rect material (x0: float) (y0: float) (x1: float) (y1: float) (uvScale: float) (surface: StadiumSurface option) =
            let corners =
                [| x0, y0; x1, y0; x1, y1; x0, y1 |]

            let first = uint32 out.Vertices.Count

            for x, y in corners do
                let index = out.Add(material, toRenderer x y 0.0, up, Vector2(float32 (x / uvScale), float32 (y / uvScale)))

                match surface with
                | Some value ->
                    out.Vertices[int index] <-
                        { out.Vertices[int index] with
                            Surface = uint32 value
                            Color = Vector4.One }
                | None -> ()

            out.Indices.AddRange [| first; first + 1u; first + 2u; first; first + 2u; first + 3u |]

        let concrete = out.Material "concrete"
        // Turf materials ignore the kit material; the pitch shader paints stripes and markings from world position.
        rect concrete -halfLength -halfWidth halfLength halfWidth 1.0 (Some StadiumSurface.TurfLight)
        let runoff = Some StadiumSurface.TurfRunoff
        rect concrete -innerX -innerY innerX -halfWidth 1.0 runoff
        rect concrete -innerX halfWidth innerX innerY 1.0 runoff
        rect concrete -innerX -halfWidth -halfLength halfWidth 1.0 runoff
        rect concrete halfLength -halfWidth innerX halfWidth 1.0 runoff
        rect concrete -outerX -outerY outerX -innerY 2.0 None
        rect concrete -outerX innerY outerX outerY 2.0 None
        rect concrete -outerX -innerY -innerX innerY 2.0 None
        rect concrete innerX -innerY outerX innerY 2.0 None
        // Open grounds and high cameras see past the apron; turf to the far plane hides the sky's lower hemisphere.
        let horizon = 480.0
        rect concrete -horizon -horizon horizon -outerY 1.0 runoff
        rect concrete -horizon outerY horizon horizon 1.0 runoff
        rect concrete -horizon -outerY -outerX outerY 1.0 runoff
        rect concrete outerX -outerY horizon outerY 1.0 runoff

    let private seatColour (layout: StadiumLayout.Layout) (identity: StadiumIdentity) (tierIndex: int) (tier: StadiumLayout.Tier) (row: int) =
        match layout.SeatColours with
        | StadiumLayout.Solid -> identity.Primary
        | StadiumLayout.Tiered -> if tierIndex % 2 = 0 then identity.Primary else identity.Secondary
        | StadiumLayout.Blocks ->
            if row < 3 || row >= tier.Rows - 2 then
                identity.Secondary
            else
                identity.Primary

    /// Seat blocks of about 8 x 8 m and four rows: small enough that LOD follows the camera, large enough that
    /// the per-frame selection touches a thousand clusters rather than sixty thousand seats.
    let private seatBlock = 8.0

    let private worldCentre (piece: KitPiece) (x: float32) (y: float32) (z: float32) (yaw: float32) (scale: Vector3) =
        let c = piece.BoundsCentre * scale
        let radians = yaw * MathF.PI / 180.0f
        let cos, sin = MathF.Cos radians, MathF.Sin radians
        Vector3(x + cos * c.X - sin * c.Y, z + c.Z, -(y + sin * c.X + cos * c.Y))

    let build (kit: StadiumKitData) (identity: StadiumIdentity) : Result<StadiumBuild, string> =
        let layout =
            StadiumLayout.generate identity.Reputation identity.LeagueLevel identity.Seed

        match StadiumKit.pieceIndex kit "seat" with
        | None -> Error "The stadium kit has no seat piece."
        | Some seatPiece ->
            let out = MeshOut(kit)
            stands out layout
            roofs out layout
            ground out layout
            let perPiece = Array.init kit.Pieces.Length (fun _ -> ResizeArray<StadiumCluster * Vector4[]>())

            let seats =
                [| for si, stand in List.indexed layout.Stands do
                       for ti, tier in List.indexed stand.Tiers do
                           for x, y, z, yaw, row in StadiumLayout.seatPositions stand tier do
                               let key = si, ti, int (Math.Floor(x / seatBlock)), int (Math.Floor(y / seatBlock)), row / 4
                               yield key, (x, y, z, yaw, seatColour layout identity ti tier row) |]

            let seatKit = kit.Pieces[seatPiece]

            // Stable grouping keeps the generator's seat order inside a block, so the build is reproducible.
            for _, group in seats |> Seq.groupBy fst do
                let members = group |> Seq.map snd |> Seq.toArray

                let vectors =
                    members
                    |> Array.collect (fun (x, y, z, yaw, colour) ->
                        [| Vector4(float32 x, float32 y, float32 z, float32 yaw)
                           Vector4(1.0f, 1.0f, 1.0f, 0.0f)
                           Vector4(colour, 1.0f) |])

                let centres =
                    members
                    |> Array.map (fun (x, y, z, yaw, _) ->
                        worldCentre seatKit (float32 x) (float32 y) (float32 z) (float32 yaw) Vector3.One)

                let centre = (Array.fold (+) Vector3.Zero centres) / float32 centres.Length

                let radius =
                    (centres |> Array.map (fun c -> Vector3.Distance(c, centre)) |> Array.max)
                    + seatKit.BoundsRadius

                perPiece[seatPiece].Add(
                    { Piece = seatPiece
                      FirstInstance = 0
                      InstanceCount = members.Length
                      Centre = centre
                      Radius = radius },
                    vectors
                )

            for placed in layout.Pieces do
                // Layout pieces the kit does not author (none today) are skipped, as the look-dev assembler does.
                match StadiumKit.pieceIndex kit placed.Piece with
                | None -> ()
                | Some index ->
                    let sx, sy, sz = placed.Scale
                    let scale = Vector3(float32 sx, float32 sy, float32 sz)
                    let piece = kit.Pieces[index]

                    let centre =
                        worldCentre piece (float32 placed.X) (float32 placed.Y) (float32 placed.Z) (float32 placed.Yaw) scale

                    let scaledRadius = piece.BoundsRadius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z))

                    perPiece[index].Add(
                        { Piece = index
                          FirstInstance = 0
                          InstanceCount = 1
                          Centre = centre
                          Radius = scaledRadius },
                        [| Vector4(float32 placed.X, float32 placed.Y, float32 placed.Z, float32 placed.Yaw)
                           Vector4(scale, 0.0f)
                           Vector4(identity.Primary, 1.0f) |]
                    )

            let instances = ResizeArray<Vector4>()
            let clusters = ResizeArray<StadiumCluster>()
            let ranges = Array.zeroCreate<struct (int * int)> kit.Pieces.Length

            for index in 0 .. kit.Pieces.Length - 1 do
                let first = instances.Count / instanceVectors

                for cluster, vectors in perPiece[index] do
                    clusters.Add
                        { cluster with
                            FirstInstance = instances.Count / instanceVectors }

                    instances.AddRange vectors

                ranges[index] <- struct (first, instances.Count / instanceVectors - first)

            if instances.Count / instanceVectors > instanceCapacity then
                Error $"The stadium places {instances.Count / instanceVectors} kit instances; the viewer holds {instanceCapacity}."
            else
                Ok
                    { Layout = layout
                      Vertices = out.Vertices.ToArray()
                      Indices = out.Indices.ToArray()
                      Instances = instances.ToArray()
                      Clusters = clusters.ToArray()
                      PieceInstances = ranges
                      SeatCount = seats.Length }

    /// The kit rule: the highest level whose `fromDistance` does not exceed the distance to the bounds.
    let lodFor (piece: KitPiece) (distance: float32) =
        let mutable lod = 0

        for index in 1 .. piece.Lods.Length - 1 do
            if piece.Lods[index].FromDistance <= distance then
                lod <- index

        lod

    /// Frustum planes (normal, offset) of a row-vector view-projection with WebGPU's [0, 1] clip depth.
    let frustumPlanes (viewProjection: Matrix4x4) : Vector4[] =
        let m = viewProjection
        let c1 = Vector4(m.M11, m.M21, m.M31, m.M41)
        let c2 = Vector4(m.M12, m.M22, m.M32, m.M42)
        let c3 = Vector4(m.M13, m.M23, m.M33, m.M43)
        let c4 = Vector4(m.M14, m.M24, m.M34, m.M44)

        [| c4 + c1; c4 - c1; c4 + c2; c4 - c2; c3; c4 - c3 |]
        |> Array.map (fun plane -> plane / Vector3(plane.X, plane.Y, plane.Z).Length())

    let private visible (planes: Vector4[]) (centre: Vector3) (radius: float32) =
        planes
        |> Array.forall (fun plane -> plane.X * centre.X + plane.Y * centre.Y + plane.Z * centre.Z + plane.W >= -radius)

    /// Clusters outside the view are skipped: the broadcast gantry sits above the main stand, whose nearest seats
    /// would otherwise be drawn at full detail behind the lens. Consecutive visible clusters of one piece at one
    /// level merge into a single instanced draw.
    let batches (kit: StadiumKitData) (build: StadiumBuild) (eye: Vector3) (viewProjection: Matrix4x4) : KitBatch[] =
        let result = ResizeArray<KitBatch>()
        let planes = frustumPlanes viewProjection

        for cluster in build.Clusters |> Seq.filter (fun cluster -> visible planes cluster.Centre cluster.Radius) do
            let distance =
                MathF.Max(0.0f, Vector3.Distance(eye, cluster.Centre) - cluster.Radius)

            let lod = lodFor kit.Pieces[cluster.Piece] distance

            if result.Count > 0 then
                let last = result[result.Count - 1]

                if
                    last.Piece = cluster.Piece
                    && last.Lod = lod
                    && int (last.FirstInstance + last.InstanceCount) = cluster.FirstInstance
                then
                    result[result.Count - 1] <-
                        { last with
                            InstanceCount = last.InstanceCount + uint32 cluster.InstanceCount }
                else
                    result.Add
                        { Piece = cluster.Piece
                          Lod = lod
                          FirstInstance = uint32 cluster.FirstInstance
                          InstanceCount = uint32 cluster.InstanceCount }
            else
                result.Add
                    { Piece = cluster.Piece
                      Lod = lod
                      FirstInstance = uint32 cluster.FirstInstance
                      InstanceCount = uint32 cluster.InstanceCount }

        result.ToArray()

    /// The static shadow cache renders once, so every instance uses one coarse level: shadows of a 0.8 m seat need
    /// no back-rest detail, and a fixed level keeps the cached map independent of where the camera started.
    let shadowBatches (kit: StadiumKitData) (build: StadiumBuild) (seatPiece: int) : KitBatch[] =
        build.PieceInstances
        |> Array.mapi (fun piece (struct (first, count)) ->
            let levels = kit.Pieces[piece].Lods.Length
            let wanted = if piece = seatPiece then 2 else 1

            { Piece = piece
              Lod = min wanted (levels - 1)
              FirstInstance = uint32 first
              InstanceCount = uint32 count })
        |> Array.filter (fun batch -> batch.InstanceCount > 0u)
