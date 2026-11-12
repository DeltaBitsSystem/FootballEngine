module FootballEngine.Client.Tests.HdrLightingTests

open System
open System.IO
open System.Numerics
open System.Security.Cryptography
open System.Text.Json.Nodes
open Expecto
open FootballEngine.Client.Match3D

let private directory =
    Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "Lighting")

let private daylight =
    { ExposureStops = -0.35f
      EnvironmentIntensity = 0.65f
      SunDirection = Vector3.Normalize(Vector3(-0.48f, 0.78f, 0.40f))
      SunRadiance = Vector3(3.2f, 3.0912f, 2.88f) }

let private withPack action =
    let target =
        Path.Combine(Path.GetTempPath(), "football-hdr-test-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory target |> ignore

    try
        for name in [ "manifest.json"; "environment.bin"; "brdf.bin"; "sky.bin" ] do
            File.Copy(Path.Combine(directory, name), Path.Combine(target, name))

        action target
    finally
        Directory.Delete(target, true)

let private withStadium action =
    withPack (fun target ->
        let lighting = Path.Combine(target, "Lighting")
        Directory.CreateDirectory lighting |> ignore

        for name in [ "manifest.json"; "environment.bin"; "brdf.bin"; "sky.bin" ] do
            File.Move(Path.Combine(target, name), Path.Combine(lighting, name))

        let sidecar =
            JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName directory, "stadium.environment.json")))

        sidecar["environmentDirectory"] <- JsonValue.Create("Lighting")
        let path = Path.Combine(target, "stadium.environment.json")
        File.WriteAllText(path, sidecar.ToJsonString())
        action path path)

let tests =
    testList
        "HdrLighting"
        [ testCase "daylight settings are explicit and physically admissible" (fun _ ->
              Expect.isOk (HdrLighting.validate daylight) "canonical daylight"

              for settings in
                  [ { daylight with
                        ExposureStops = Single.NaN }
                    { daylight with
                        ExposureStops = 17.0f }
                    { daylight with
                        EnvironmentIntensity = -1.0f }
                    { daylight with
                        SunRadiance = Vector3(Single.PositiveInfinity) }
                    { daylight with
                        SunDirection = Vector3.Zero }
                    { daylight with
                        SunDirection = -Vector3.UnitY } ] do
                  Expect.isError (HdrLighting.validate settings) "invalid lighting cannot upload")
          testCase "tone mapping preserves black and compresses HDR highlights instead of clipping at one" (fun _ ->
              Expect.equal (HdrLighting.toneMap 0.0f 0.0f) 0.0f "black stays black"

              let values =
                  [ 0.01f; 0.18f; 1.0f; 2.0f; 4.0f; 8.0f ] |> List.map (HdrLighting.toneMap 0.0f)

              for a, b in List.pairwise values do
                  Expect.isLessThan a b "HDR highlights remain ordered after display mapping"

              Expect.isLessThan (HdrLighting.toneMap 0.0f 1.0f) 1.0f "headroom above diffuse white"

              Expect.floatClose
                  Accuracy.medium
                  (float (HdrLighting.toneMap 0.0f 1.0f))
                  0.8037974684
                  "reference fitted curve")
          testCase "one exposure stop doubles scene radiance only before tone mapping" (fun _ ->
              for radiance in [ 0.02f; 0.18f; 1.0f; 3.0f ] do
                  Expect.floatClose
                      Accuracy.medium
                      (float (HdrLighting.toneMap 1.0f radiance))
                      (float (HdrLighting.toneMap 0.0f (radiance * 2.0f)))
                      "exposure definition")
          testCase "display transfer is an sRGB round trip not an approximate gamma" (fun _ ->
              for value in [ 0.0f; 0.003f; 0.04f; 0.18f; 0.5f; 1.0f ] do
                  Expect.floatClose
                      Accuracy.medium
                      (float (value |> HdrLighting.srgbToLinear |> HdrLighting.linearToSrgb))
                      (float value)
                      "single decode then encode")
          testCase "offline IBL contains diffuse convolution, six roughness layers and HDR samples" (fun _ ->
              match HdrLighting.loadEnvironment directory with
              | Error error -> failtest error
              | Ok pack ->
                  Expect.equal
                      (pack.Width, pack.Height, pack.Layers, pack.LutSize)
                      (128, 64, 7, 128)
                   "authoritative pack dimensions"
                  Expect.equal (pack.SkyWidth, pack.SkyHeight, pack.Sky.Length) (2048, 1024, 2048 * 1024 * 8) "original visible sky is separate from low-resolution IBL"
                  Expect.isLessThan (Vector3.Distance(pack.SunDirection, daylight.SunDirection)) 0.0001f "authored sun and geometry shadows agree"

                  let samples =
                      [| for offset in 0..2 .. pack.Environment.Length - 2 ->
                             BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pack.Environment, offset))
                             |> float32 |]

                  Expect.isTrue
                      (samples |> Array.forall (fun value -> Single.IsFinite value && value >= 0.0f))
                      "finite positive radiance"

                  Expect.isGreaterThan (Array.max samples) 1.0f "pack is floating HDR, not an LDR image renamed HDR"
                  let layerBytes = pack.Width * pack.Height * 8

                  Expect.notEqual
                      pack.Environment[.. layerBytes - 1]
                      pack.Environment[layerBytes * 5 .. layerBytes * 6 - 1]
                      "roughness changes reflected radiance")
          testCase "tampered HDR radiance cannot enter the renderer" (fun _ ->
              withPack (fun target ->
                  let path = Path.Combine(target, "environment.bin")
                  let bytes = File.ReadAllBytes path
                  bytes[0] <- bytes[0] ^^^ 1uy
                  File.WriteAllBytes(path, bytes)
                  Expect.isError (HdrLighting.loadEnvironment target) "SHA-256 protects pack"))
          testCase "nonfinite half floats are rejected even with matching integrity hash" (fun _ ->
              withPack (fun target ->
                  let path = Path.Combine(target, "environment.bin")
                  let bytes = File.ReadAllBytes path
                  bytes[0] <- 0uy
                  bytes[1] <- 0x7Cuy
                  File.WriteAllBytes(path, bytes)
                  let manifestPath = Path.Combine(target, "manifest.json")
                  let manifest = JsonNode.Parse(File.ReadAllText manifestPath)
                  manifest.["files"].[0].["sha256"] <- JsonValue.Create(Convert.ToHexString(SHA256.HashData bytes))
                  File.WriteAllText(manifestPath, manifest.ToJsonString())
                  Expect.isError (HdrLighting.loadEnvironment target) "infinite scene radiance cannot propagate"))
          testCase "truncated BRDF and incompatible layout fail without a legacy fallback" (fun _ ->
              withPack (fun target ->
                  File.WriteAllBytes(Path.Combine(target, "brdf.bin"), [| 0uy |])
                  Expect.isError (HdrLighting.loadEnvironment target) "truncated BRDF")

              withPack (fun target ->
                  let path = Path.Combine(target, "manifest.json")
                  let manifest = JsonNode.Parse(File.ReadAllText path)
                  manifest["schema"] <- JsonValue.Create(0)
                  File.WriteAllText(path, manifest.ToJsonString())
                  Expect.isError (HdrLighting.loadEnvironment target) "old layout is not retained"))
          testCase "malformed numeric metadata returns an error instead of escaping the asset boundary" (fun _ ->
              for field, value in [ "width", "2147483648"; "width", "1.5"; "sunDirection", "[1e1000, 0, 0]" ] do
                  withPack (fun target ->
                      let path = Path.Combine(target, "manifest.json")
                      let manifest = JsonNode.Parse(File.ReadAllText path)
                      manifest[field] <- JsonNode.Parse value
                      File.WriteAllText(path, manifest.ToJsonString())
                      Expect.isError (HdrLighting.loadEnvironment target) "invalid metadata is a structured failure"))
          testCase "duplicate and unknown texture entries are rejected without choosing a fallback" (fun _ ->
              for extra in [ "environment.bin"; "unknown.bin" ] do
                  withPack (fun target ->
                      let path = Path.Combine(target, "manifest.json")
                      let manifest = JsonNode.Parse(File.ReadAllText path)
                      let entry = (manifest["files"][0]).DeepClone()
                      entry["path"] <- JsonValue.Create extra
                      (manifest["files"].AsArray()).Add entry
                      File.WriteAllText(path, manifest.ToJsonString())
                      Expect.isError (HdrLighting.loadEnvironment target) "one authoritative entry per texture"))
          testCase "directional shadow volume covers both goals and all pitch corners" (fun _ ->
              let matrix =
                  DirectionalShadow.viewProjection DirectionalShadow.stadium daylight.SunDirection

              for x in [ -55.0f; 0.0f; 55.0f ] do
                  for z in [ -36.0f; 0.0f; 36.0f ] do
                      let clip = Vector4.Transform(Vector4(x, 0.0f, z, 1.0f), matrix)
                      Expect.isLessThan (abs clip.X) clip.W "horizontal shadow coverage"
                      Expect.isLessThan (abs clip.Y) clip.W "vertical shadow coverage"
                      Expect.isGreaterThan clip.Z 0.0f "WebGPU near depth is zero"
                      Expect.isLessThan clip.Z clip.W "WebGPU far depth is one")
          testCase "shadow depth orders occluders toward the light before their receivers" (fun _ ->
              let direction = daylight.SunDirection
              let matrix = DirectionalShadow.viewProjection DirectionalShadow.stadium direction
              let receiver = Vector4.Transform(Vector4(0.0f, 0.0f, 0.0f, 1.0f), matrix)
              let occluder = Vector4.Transform(Vector4(direction * 2.0f, 1.0f), matrix)
              Expect.isLessThan occluder.Z receiver.Z "comparison shadow depth convention")
          testCase "shadow matrix stays finite for lights parallel to either basis axis" (fun _ ->
              for direction in [ Vector3.UnitZ; -Vector3.UnitZ; Vector3.UnitY; Vector3.UnitX ] do
                  let values = DirectionalShadow.matrixData DirectionalShadow.stadium direction
                  Expect.equal values.Length 16 "WGSL mat4 ABI"
                  Expect.isTrue (values |> Array.forall Single.IsFinite) "no degenerate light basis")
          testCase "all viewer lenses produce finite projections across pitch and viewport shapes" (fun _ ->
              for mode in ViewerCamera.all do
                  for aspect in [ 0.7f; 1.5f; 16.0f / 9.0f; 2.4f ] do
                      for focus in [ Vector3.Zero; Vector3(-50.0f, 1.0f, -32.0f); Vector3(50.0f, 1.0f, 32.0f) ] do
                          let frame = ViewerCamera.frame mode aspect focus
                          let matrix = ViewerCamera.viewProjection aspect frame
                          let clip = Vector4.Transform(Vector4(frame.Target, 1.0f), matrix)
                          Expect.isGreaterThan frame.Position.Y 0.0f "camera remains above the playing surface"
                          Expect.isTrue (Single.IsFinite clip.W && clip.W > 0.0f) "finite forward projection"
                          Expect.isLessThan (abs (clip.X / clip.W)) 0.001f "target stays on optical axis"
                          Expect.isLessThan (abs (clip.Y / clip.W)) 0.001f "target stays on optical axis")
          testCase "tactical camera retains the complete pitch rather than following the carrier" (fun _ ->
              let aspect = 16.0f / 9.0f
              let first = ViewerCamera.frame ViewerCamera.Tactical aspect Vector3.Zero

              let second =
                  ViewerCamera.frame ViewerCamera.Tactical aspect (Vector3(45.0f, 1.0f, 30.0f))

              Expect.equal first second "fixed tactical reference"
              let matrix = ViewerCamera.viewProjection aspect first

              for x in [ -52.5f; 52.5f ] do
                  for z in [ -34.0f; 34.0f ] do
                      let clip = Vector4.Transform(Vector4(x, 0.0f, z, 1.0f), matrix)
                      Expect.isLessThan (abs clip.X) clip.W "entire pitch width is visible"
                      Expect.isLessThan (abs clip.Y) clip.W "entire pitch length is visible")
          testCase "stadium package supplies the authored lighting rather than a runtime duplicate" (fun _ ->
              let sidecar = Path.Combine(Path.GetDirectoryName directory, "stadium.environment.json")

              match StadiumEnvironment.load sidecar with
              | Error error -> failtest error
              | Ok package ->
                  Expect.isOk (HdrLighting.validate package.Settings) "authored lighting is admissible"
                  Expect.equal package.Settings.SunDirection package.Environment.SunDirection "one solar direction"
                  Expect.floatClose Accuracy.medium (float package.Settings.ExposureStops) -0.35 "authored exposure"
                  Expect.floatClose Accuracy.medium (float package.Settings.SunRadiance.Y) 3.0912 "cloud sun and runtime sun agree")
          testCase "missing sidecar does not silently load default lighting" (fun _ ->
              withStadium (fun asset sidecar ->
                  File.Delete sidecar
                  Expect.isError (StadiumEnvironment.load asset) "no default or old-format fallback"))
          testCase "sidecar cannot redirect assets outside its package" (fun _ ->
              for directoryName in [ ".."; "."; "../outside"; "/tmp/outside"; "Lighting\\escape"; "C:\\outside" ] do
                  withStadium (fun asset path ->
                      let sidecar = JsonNode.Parse(File.ReadAllText path)
                      sidecar["environmentDirectory"] <- JsonValue.Create directoryName
                      File.WriteAllText(path, sidecar.ToJsonString())
                      Expect.isError (StadiumEnvironment.load asset) "portable relative folder only"))
          testCase "sidecar validates schema vectors numbers and manifest binding" (fun _ ->
              for field, value in
                  [ "schema", "1"
                    "exposureStops", "17"
                    "environmentIntensity", "-1"
                    "sunDirection", "[0, 1, 0]"
                    "sunDirection", "[0, 1, 0, 0]"
                    "sunRadiance", "[1e1000, 1, 1]"
                    "sunRadiance", "[-1, 1, 1]"
                    "environmentManifestSha256", "\"invalid\"" ] do
                  withStadium (fun asset path ->
                      let sidecar = JsonNode.Parse(File.ReadAllText path)
                      sidecar[field] <- JsonNode.Parse value
                      File.WriteAllText(path, sidecar.ToJsonString())
                      Expect.isError (StadiumEnvironment.load asset) $"invalid authored metadata: {field}"))
          testCase "IBL rejects wrong coordinate color and solar energy contracts" (fun _ ->
              for field, value in
                  [ "schema", "2"
                    "projection", "\"z-up\""
                    "colorSpace", "\"acescg\""
                    "diffuseEncoding", "\"irradiance\""
                    "solarDisc", "\"included-in-ibl\"" ] do
                  withPack (fun target ->
                      let path = Path.Combine(target, "manifest.json")
                      let manifest = JsonNode.Parse(File.ReadAllText path)
                      manifest[field] <- JsonNode.Parse value
                      File.WriteAllText(path, manifest.ToJsonString())
                      Expect.isError (HdrLighting.loadEnvironment target) $"unsupported contract: {field}"))
          testCase "missing lighting reports a structured error" (fun _ ->
              Expect.isError
                  (HdrLighting.loadEnvironment (Path.Combine(directory, "missing")))
                  "no silent flat lighting") ]
