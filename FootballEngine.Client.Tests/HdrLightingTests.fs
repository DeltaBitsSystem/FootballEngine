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

let private withPack action =
    let target =
        Path.Combine(Path.GetTempPath(), "football-hdr-test-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory target |> ignore

    try
        for name in [ "manifest.json"; "environment.bin"; "brdf.bin" ] do
            File.Copy(Path.Combine(directory, name), Path.Combine(target, name))

        action target
    finally
        Directory.Delete(target, true)

let tests =
    testList
        "HdrLighting"
        [ testCase "daylight settings are explicit and physically admissible" (fun _ ->
              Expect.isOk (HdrLighting.validate HdrLighting.broadcastDaylight) "canonical daylight"

              for settings in
                  [ { HdrLighting.broadcastDaylight with
                        ExposureStops = Single.NaN }
                    { HdrLighting.broadcastDaylight with
                        ExposureStops = 17.0f }
                    { HdrLighting.broadcastDaylight with
                        EnvironmentIntensity = -1.0f }
                    { HdrLighting.broadcastDaylight with
                        SunRadiance = Vector3(Single.PositiveInfinity) }
                    { HdrLighting.broadcastDaylight with
                        SunDirection = Vector3.Zero }
                    { HdrLighting.broadcastDaylight with
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
                  manifest["files"][0]["sha256"] <- JsonValue.Create(Convert.ToHexString(SHA256.HashData bytes))
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
          testCase "missing lighting reports a structured error" (fun _ ->
              Expect.isError
                  (HdrLighting.loadEnvironment (Path.Combine(directory, "missing")))
                  "no silent flat lighting") ]
