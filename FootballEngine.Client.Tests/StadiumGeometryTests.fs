module FootballEngine.Client.Tests.StadiumGeometryTests

open System
open System.IO
open System.Numerics
open Expecto
open FootballEngine.Client.Match3D

let private kitDirectory =
    Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "StadiumKit")

// Decoding the shipped kit takes about a second; every case reads the same immutable result.
let private shipped =
    lazy
        (match StadiumKit.load kitDirectory with
         | Ok loaded -> loaded
         | Error error -> failtest error)

let private kit () = fst shipped.Value

let private scenarios =
    [ "giant", 9600, 0, 7; "second_div", 2800, 1, 31; "third_div", 1200, 2, 41 ]

let private identity reputation level seed =
    StadiumIdentity.create reputation level seed 0xFF1E5AA8u 0xFFF2C230u

let private built reputation level seed =
    match StadiumGeometry.build (kit ()) (identity reputation level seed) with
    | Ok build -> build
    | Error error -> failtest error

/// An orthographic box around the whole site: nothing is culled, so batches can be checked for completeness.
let private everything =
    Matrix4x4.CreateOrthographic(4000.0f, 4000.0f, -4000.0f, 4000.0f)

let private pieceCount (kit: StadiumKitData) (build: StadiumBuild) (id: string) =
    match StadiumKit.pieceIndex kit id with
    | Some index ->
        let struct (_, count) = build.PieceInstances[index]
        count
    | None -> failtest $"kit has no {id}"

/// Independent reference for the WGSL kit transform: glTF y-up piece space -> layout z-up -> renderer y-up.
let private referenceTransform (instance: Vector4) (scale: Vector3) (local: Vector3) =
    let b = Vector3(local.X * scale.X, -local.Z * scale.Y, local.Y * scale.Z)
    let rotation = Matrix4x4.CreateRotationZ(instance.W * MathF.PI / 180.0f)

    let layout =
        Vector3.Transform(b, rotation) + Vector3(instance.X, instance.Y, instance.Z)

    Vector3(layout.X, layout.Z, -layout.Y)

let tests =
    testList
        "FootballEngine.Client.Match3D procedural stadium"
        [ testCase "the kit loads every piece, level and material named by kit.json" (fun _ ->
              let kit, textures = shipped.Value
              Expect.equal kit.Pieces.Length 20 "twenty authored pieces"
              Expect.isTrue (kit.Pieces |> Array.forall (fun piece -> piece.Lods.Length = 3)) "three LODs each"
              Expect.equal (textures.Size, textures.Layers, textures.Levels) (1024, 7, 11) "full mip chains"

              Expect.equal
                  textures.Albedo.Length
                  (Array.sum [| for l in 0..10 -> (1024 >>> l) * (1024 >>> l) * 4 * 7 |])
                  "packed array"

              let lod (id: string) level =
                  kit.Pieces[(StadiumKit.pieceIndex kit id).Value].Lods[level]

              Expect.equal (lod "seat" 0).Blended.Count 0u "seats are opaque"
              Expect.isGreaterThan (lod "railing_glass" 0).Blended.Count 0u "glazed railings blend"
              Expect.isGreaterThan (lod "goal_net" 0).Blended.Count 0u "nets blend through their coverage mask"
              Expect.equal (lod "goal_net" 0).Opaque.Count 0u "a net has no opaque triangles"

              Expect.isLessThan
                  (lod "seat" 2).Opaque.Count
                  ((lod "seat" 0).Opaque.Count / 100u)
                  "distant seats are cheap"

              Expect.isTrue
                  (kit.Vertices
                   |> Array.forall (fun vertex ->
                       vertex.RootIndex = SceneRoot.instanced && vertex.Weights = Vector4.Zero))
                  "kit vertices address the instance table and are never skinned")
          testCase "material ids drop the export prefix and Blender collision suffixes" (fun _ ->
              Expect.equal (StadiumKit.materialId "kit.seat_plastic:club.001") "seat_plastic:club" "suffix"
              Expect.equal (StadiumKit.materialId "kit.painted_steel:charcoal") "painted_steel:charcoal" "prefix"
              Expect.equal (StadiumKit.materialId "glass") "glass" "plain name")
          testCase "every layout seat and placed piece becomes exactly one instance" (fun _ ->
              let kit = kit ()

              for name, reputation, level, seed in scenarios do
                  let build = built reputation level seed
                  let layout = build.Layout

                  let seats =
                      layout.Stands
                      |> List.sumBy (fun stand ->
                          stand.Tiers
                          |> List.sumBy (fun tier -> (StadiumLayout.seatPositions stand tier).Length))

                  Expect.isGreaterThan seats 0 $"{name} has seats"
                  Expect.equal build.SeatCount seats $"{name} seat count"
                  Expect.equal (pieceCount kit build "seat") seats $"{name} seats on the GPU"

                  for id, placed in layout.Pieces |> List.countBy _.Piece do
                      Expect.equal (pieceCount kit build id) placed $"{name} {id} instances"

                  let total = build.Instances.Length / StadiumGeometry.instanceVectors
                  Expect.equal (build.Instances.Length % StadiumGeometry.instanceVectors) 0 "whole instances"
                  Expect.equal total (seats + layout.Pieces.Length) $"{name} nothing extra")
          testCase "clusters tile the instance table contiguously without mixing pieces" (fun _ ->
              let build = built 9600 0 7
              let mutable next = 0

              for cluster in build.Clusters do
                  Expect.equal cluster.FirstInstance next "no gap or overlap"
                  let struct (first, count) = build.PieceInstances[cluster.Piece]

                  Expect.isTrue
                      (cluster.FirstInstance >= first
                       && cluster.FirstInstance + cluster.InstanceCount <= first + count)
                      "inside its piece"

                  next <- cluster.FirstInstance + cluster.InstanceCount

              Expect.equal next (build.Instances.Length / StadiumGeometry.instanceVectors) "all instances covered"

              let seatClusters =
                  build.Clusters
                  |> Array.filter (fun cluster -> cluster.Piece = (StadiumKit.pieceIndex (kit ()) "seat").Value)

              Expect.isLessThan seatClusters.Length (build.SeatCount / 20) "seats are grouped into blocks"
              Expect.isLessThan (seatClusters |> Array.map _.Radius |> Array.max) 12.0f "blocks stay local")
          testCase "level of detail follows the kit fromDistance rule" (fun _ ->
              let seat = (kit ()).Pieces[(StadiumKit.pieceIndex (kit ()) "seat").Value]

              for distance, expected in [ 0.0f, 0; 39.9f, 0; 40.0f, 1; 109.0f, 1; 110.0f, 2; 5000.0f, 2 ] do
                  Expect.equal (StadiumGeometry.lodFor seat distance) expected $"{distance} m")
          testCase "per-frame batches draw every instance once and coarsen with distance" (fun _ ->
              let kit = kit ()
              let build = built 9600 0 7
              let total = build.Instances.Length / StadiumGeometry.instanceVectors

              for eye in
                  [ Vector3(0.0f, 20.0f, 60.0f)
                    Vector3(0.0f, 1.7f, 0.0f)
                    Vector3(4000.0f, 300.0f, 0.0f) ] do
                  let batches = StadiumGeometry.batches kit build eye everything

                  Expect.equal
                      (batches |> Array.sumBy (fun batch -> int batch.InstanceCount))
                      total
                      "each instance once"

              let far =
                  StadiumGeometry.batches kit build (Vector3(4000.0f, 300.0f, 0.0f)) everything

              Expect.isTrue
                  (far |> Array.forall (fun batch -> batch.Lod = 2))
                  "a distant camera sees only the coarsest level"

              Expect.equal
                  far.Length
                  (build.PieceInstances
                   |> Array.filter (fun (struct (_, count)) -> count > 0)
                   |> Array.length)
                  "one merged draw per piece"

              let near = StadiumGeometry.batches kit build (Vector3(0.0f, 1.7f, 0.0f)) everything

              Expect.isTrue
                  (near |> Array.exists (fun batch -> batch.Lod < 2))
                  "the pitch-level camera refines nearby kit")
          testCase "clusters behind the broadcast lens are culled, the stand in front is kept" (fun _ ->
              let kit = kit ()
              let build = built 9600 0 7

              let frame =
                  ViewerCamera.frame ViewerCamera.Broadcast 1.62f (Vector3(0.0f, 1.0f, 0.0f))

              let batches =
                  StadiumGeometry.batches kit build frame.Position (ViewerCamera.viewProjection 1.62f frame)

              let seat = (StadiumKit.pieceIndex kit "seat").Value
              let drawnSeats = batches |> Array.filter (fun batch -> batch.Piece = seat)
              let total = build.SeatCount

              let renderedZ index = -build.Instances[index * 3].Y

              let drawn =
                  [ for batch in drawnSeats do
                        for index in int batch.FirstInstance .. int (batch.FirstInstance + batch.InstanceCount) - 1 ->
                            index ]

              Expect.isLessThan drawn.Length (total * 4 / 5) "the main stand behind the gantry is skipped"

              Expect.isTrue
                  (drawn |> List.forall (fun index -> renderedZ index < frame.Position.Z + 15.0f))
                  "nothing far behind the lens"

              Expect.isTrue (drawn |> List.exists (fun index -> renderedZ index < -40.0f)) "the far stand is drawn")
          testCase "seats face the pitch through the shader's instance transform" (fun _ ->
              let build = built 5200 0 19

              let struct (first, count) =
                  build.PieceInstances[(StadiumKit.pieceIndex (kit ()) "seat").Value]

              for index in first..97 .. first + count - 1 do
                  let place = build.Instances[index * 3]
                  let scale = build.Instances[index * 3 + 1]

                  let origin =
                      referenceTransform place (Vector3(scale.X, scale.Y, scale.Z)) Vector3.Zero
                  // Seat fronts face glTF -Z (Blender +Y), the axis the kit contract says points at the pitch.
                  let front =
                      referenceTransform place (Vector3(scale.X, scale.Y, scale.Z)) (Vector3(0.0f, 0.0f, -1.0f))
                      - origin

                  let toPitch = Vector3(-origin.X, 0.0f, -origin.Z)

                  Expect.isGreaterThan
                      (Vector3.Dot(Vector3.Normalize front, Vector3.Normalize toPitch))
                      0.3f
                      "seat looks inwards"

                  Expect.isGreaterThan origin.Y -0.01f "seats are above ground")
          testCase "seat colours follow the layout scheme and the club colours" (fun _ ->
              let kit = kit ()
              let palette = identity 0 0 0

              for reputation, level, seed in [ 9600, 0, 7; 7800, 0, 11; 1200, 2, 41 ] do
                  let build = built reputation level seed

                  let struct (first, count) =
                      build.PieceInstances[(StadiumKit.pieceIndex kit "seat").Value]

                  let colours =
                      [ for index in first .. first + count - 1 -> build.Instances[index * 3 + 2] ]
                      |> List.map (fun colour -> Vector3(colour.X, colour.Y, colour.Z))
                      |> List.distinct

                  match build.Layout.SeatColours with
                  | StadiumLayout.Solid -> Expect.equal colours [ palette.Primary ] "solid uses the primary"
                  | _ ->
                      Expect.equal colours.Length 2 "two-colour schemes"
                      Expect.containsAll colours [ palette.Primary; palette.Secondary ] "both club colours")
          testCase "generation is reproducible and depends on the identity" (fun _ ->
              let a = built 2800 1 31
              let b = built 2800 1 31
              Expect.equal a.Instances b.Instances "same instances"
              Expect.equal a.Indices b.Indices "same stands"
              Expect.notEqual (built 2800 1 32).Instances a.Instances "another club gets another ground")
          testCase "generated stands have unit normals and sane extents" (fun _ ->
              let build = built 9600 0 7
              Expect.equal (build.Indices.Length % 3) 0 "triangles"

              Expect.isTrue
                  (build.Indices |> Array.forall (fun index -> int index < build.Vertices.Length))
                  "indices in range"

              for vertex in build.Vertices do
                  Expect.isLessThan (abs (vertex.Normal.Length() - 1.0f)) 0.001f "unit normal"
                  // Only the turf skirt reaches the far plane; every built surface stays on the site.
                  if vertex.Surface >= 16u then
                      Expect.isTrue
                          (abs vertex.Position.X < 250.0f && abs vertex.Position.Z < 250.0f)
                          "inside the site"

                  Expect.isTrue (vertex.Position.Y >= -0.001f && vertex.Position.Y < 80.0f) "between ground and roof"

              // A tread faces up; the tier front faces the pitch.
              let concrete =
                  build.Vertices
                  |> Array.filter (fun vertex -> vertex.Surface >= 16u && vertex.Surface < 32u)

              Expect.isTrue
                  (concrete
                   |> Array.exists (fun vertex -> vertex.Normal.Y > 0.99f && vertex.Position.Y > 1.0f))
                  "upward treads")
          testCase "packed club colours decode to linear light with an explicit fallback" (fun _ ->
              let fallback = Vector3(0.5f, 0.25f, 0.125f)
              Expect.equal (StadiumIdentity.linearOfPacked 0u fallback) fallback "no kit colour"
              Expect.equal (StadiumIdentity.linearOfPacked 0xFFFFFFFFu fallback) Vector3.One "white"
              Expect.equal (StadiumIdentity.linearOfPacked 0xFF000000u fallback) Vector3.Zero "black is a real colour"
              let mid = StadiumIdentity.linearOfPacked 0xFF808080u fallback
              Expect.floatClose Accuracy.medium (float mid.X) 0.2158605 "sRGB 128 is 21.6 % linear") ]
