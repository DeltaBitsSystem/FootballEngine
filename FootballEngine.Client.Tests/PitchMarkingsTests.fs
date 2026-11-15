module FootballEngine.Client.Tests.PitchMarkingsTests

open System
open System.IO
open System.Numerics
open Expecto
open FootballEngine.Client.Match3D

let private inside point =
    Expect.isLessThan (PitchMarkings.signedDistance point) 0.0f "paint covers the intended geometry"

let private outside point =
    Expect.isGreaterThan (PitchMarkings.signedDistance point) 0.0f "no unintended white paint"

let tests =
    testList
        "PitchMarkings"
        [ testCase "halfway paint has a real 12 cm width, not a screen-space ribbon" (fun _ ->
              inside (Vector2(0.0f, 5.0f))
              inside (Vector2(0.05f, 5.0f))
              outside (Vector2(0.08f, 5.0f))

              Expect.floatClose
                  Accuracy.medium
                  (float (PitchMarkings.signedDistance (Vector2(0.08f, 5.0f))))
                  0.02
                  "physical stroke width")
          testCase "center circle and solid spot are on the pitch" (fun _ ->
              let diagonal = PitchMarkings.centerRadius / sqrt 2.0f
              inside (Vector2(diagonal, diagonal))
              inside Vector2.Zero

              Expect.floatClose
                  Accuracy.medium
                  (float (PitchMarkings.signedDistance Vector2.Zero))
                  -0.12
                  "solid centre spot, not a ring"

              outside (Vector2(12.0f, 4.0f)))
          testCase "penalty and goal areas contain all three painted sides" (fun _ ->
              for side in [ -1.0f; 1.0f ] do
                  for depth, span in
                      [ PitchMarkings.penaltyDepth, PitchMarkings.penaltyHalfWidth
                        PitchMarkings.goalDepth, PitchMarkings.goalHalfWidth ] do
                      let front = PitchMarkings.length * 0.5f - depth
                      inside (Vector2(side * front, span * 0.6f))
                      inside (Vector2(side * (front + 2.0f), span))
                      inside (Vector2(side * (front + 2.0f), -span)))
          testCase "penalty arcs stop at the area instead of drawing a complete circle" (fun _ ->
              for side in [ -1.0f; 1.0f ] do
                  let spot = PitchMarkings.length * 0.5f - PitchMarkings.penaltySpotDistance
                  inside (Vector2(side * spot, 0.0f))
                  inside (Vector2(side * (spot - PitchMarkings.centerRadius), 0.0f))
                  outside (Vector2(side * (spot + PitchMarkings.centerRadius), 0.0f)))
          testCase "all four corner arcs are present and paint does not leak outside the field" (fun _ ->
              let diagonal = PitchMarkings.cornerRadius / sqrt 2.0f

              for x in [ -1.0f; 1.0f ] do
                  for y in [ -1.0f; 1.0f ] do
                      inside (
                          Vector2(
                              x * (PitchMarkings.length * 0.5f - diagonal),
                              y * (PitchMarkings.width * 0.5f - diagonal)
                          )
                      )

                      outside (Vector2(x * 53.0f, y * 34.5f)))
          testCase "outer boundary stays inside the authored grass footprint" (fun _ ->
              for side in [ -1.0f; 1.0f ] do
                  inside (Vector2(side * 52.44f, 12.0f))
                  inside (Vector2(12.0f, side * 33.94f))
                  outside (Vector2(side * 53.0f, 12.0f)))
          testCase "the paint is symmetric for both teams and all touchlines" (fun _ ->
              for x in 0..23 do
                  for y in 0..16 do
                      let point = Vector2(float32 x * 2.25f, float32 y * 2.1f)
                      let expected = PitchMarkings.signedDistance point

                      for sign in [ Vector2(-1.0f, 1.0f); Vector2(1.0f, -1.0f); Vector2(-1.0f, -1.0f) ] do
                          Expect.floatClose
                              Accuracy.medium
                              (float (PitchMarkings.signedDistance (point * sign)))
                              (float expected)
                              "mirrored field")
          testCase "WGSL geometry constants are culture-independent and share the field contract" (fun _ ->
              Expect.stringContains
                  PitchMarkings.shaderConstants
                  "PITCH_HALF_LENGTH: f32 = 52.5"
                  "authoritative field length"

              Expect.stringContains
                  PitchMarkings.shaderConstants
                  "PITCH_GRASS_TILE: f32 = 1;"
                  "source material physical scale"

              Expect.stringContains
                  PitchMarkings.shaderConstants
                  "PITCH_MOWING_BAND: f32 = 7.5"
                  "fourteen mowing passes"

              Expect.isFalse
                  (PitchMarkings.shaderConstants.Contains("52,5", StringComparison.Ordinal))
                  "locale must not break WGSL")
          testCase "the generated turf spans exactly the painted field" (fun _ ->
              let directory =
                  Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "StadiumKit")

              match
                  StadiumKit.load directory
                  |> Result.bind (fun (kit, _) -> StadiumGeometry.build kit StadiumIdentity.neutral)
              with
              | Error error -> failtest error
              | Ok build ->
                  let turf =
                      build.Vertices
                      |> Array.filter (fun vertex -> vertex.Surface = uint32 StadiumSurface.TurfLight)

                  Expect.equal turf.Length 4 "one field quad; stripes and paint come from the shader, not meshes"
                  let xs = turf |> Array.map _.Position.X
                  let zs = turf |> Array.map _.Position.Z

                  Expect.floatClose
                      Accuracy.medium
                      (float (Array.min xs))
                      (float (-PitchMarkings.length * 0.5f))
                      "west goal line"

                  Expect.floatClose
                      Accuracy.medium
                      (float (Array.max xs))
                      (float (PitchMarkings.length * 0.5f))
                      "east goal line"

                  Expect.floatClose
                      Accuracy.medium
                      (float (Array.min zs))
                      (float (-PitchMarkings.width * 0.5f))
                      "near touchline"

                  Expect.floatClose
                      Accuracy.medium
                      (float (Array.max zs))
                      (float (PitchMarkings.width * 0.5f))
                      "far touchline"

                  Expect.isTrue
                      (turf
                       |> Array.forall (fun vertex ->
                           vertex.Position.Y = 0.0f && vertex.RootIndex = uint32 SceneRoot.stadium))
                      "the field lies on the ground under the identity stadium root") ]
