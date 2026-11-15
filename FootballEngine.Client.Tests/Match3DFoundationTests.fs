module FootballEngine.Client.Tests.Match3DFoundationTests

open System
open Avalonia
open Expecto
open FootballEngine.Client.Match3D
open Silk.NET.WebGPU

let private area (rectangles: X11ChildWindow.Rectangle array) =
    rectangles |> Array.sumBy (fun rect -> int rect.Width * int rect.Height)

let private intersects (rect: X11ChildWindow.Rectangle) (cutout: PixelRect) =
    let left = int rect.X
    let top = int rect.Y
    let right = left + int rect.Width
    let bottom = top + int rect.Height

    left < cutout.X + cutout.Width
    && cutout.X < right
    && top < cutout.Y + cutout.Height
    && cutout.Y < bottom

let private opaqueFragment =
    { Entry = "fs"
      ColorFormat = Silk.NET.WebGPU.TextureFormat.Rgba16float
      Blend = Replace }

let private additiveFragment = { opaqueFragment with Blend = Additive }

let private depthState =
    { DepthFormat = Silk.NET.WebGPU.TextureFormat.Depth24Plus
      WriteDepth = true
      Compare = Silk.NET.WebGPU.CompareFunction.Less
      Bias = 0
      SlopeScale = 0.0f }

let private opaqueKey: RenderPipelineKey =
    { Shader = "stadium"
      Layout = "stadium scene"
      VertexEntry = "vs"
      Vertex = GpuVertexFull
      Fragment = Some opaqueFragment
      Depth = None
      Cull = Silk.NET.WebGPU.CullMode.None
      Samples = 4u
      AlphaToCoverage = false }

let private sameOpaqueKey: RenderPipelineKey =
    { opaqueKey with
        Fragment =
            Some
                { Entry = "fs"
                  ColorFormat = Silk.NET.WebGPU.TextureFormat.Rgba16float
                  Blend = Replace } }

let tests =
    testList
        "Match3D foundation"
        [ testCase "shader segments are concatenated in the order they are given" (fun _ ->
              Expect.equal
                  (ShaderLibrary.compose [ "constants;"; "lighting;"; "surface;" ])
                  "constants;lighting;surface;"
                  "order"

              Expect.equal (ShaderLibrary.compose [ "b"; "a" ]) "ba" "reversed input stays reversed"
              Expect.equal (ShaderLibrary.compose []) "" "no segments compose to empty source")
          testCase "a missing shader file is reported with its name rather than a generic failure" (fun _ ->
              let name = "NoSuchShaderForTests.wgsl"

              match ShaderLibrary.readFile name with
              | Error(ShaderCompile(file, message)) ->
                  Expect.equal file name "file name is carried in the error"
                  Expect.stringContains message "missing" "message says the file is missing"
              | other -> failtestf "expected ShaderCompile for a missing file, got %A" other)
          testCase "without cutouts the whole window stays visible as one rectangle" (fun _ ->
              let bounds = PixelRect(0, 0, 100, 80)
              let rectangles = X11ChildWindow.cutoutRectangles bounds [||]

              Expect.equal rectangles.Length 1 "one rectangle"
              Expect.equal (area rectangles) 8000 "full area")
          testCase "a cutout removes exactly its area and no visible rectangle overlaps it" (fun _ ->
              let bounds = PixelRect(0, 0, 100, 80)
              let cutout = PixelRect(10, 10, 20, 20)
              let rectangles = X11ChildWindow.cutoutRectangles bounds [| cutout |]

              Expect.equal (area rectangles) (8000 - 400) "visible area excludes the cutout"

              Expect.isFalse
                  (rectangles |> Array.exists (fun rect -> intersects rect cutout))
                  "no overlap with the cutout")
          testCase "a fully covered window falls back to its full bounds instead of an empty shape" (fun _ ->
              let bounds = PixelRect(0, 0, 40, 30)

              let rectangles =
                  X11ChildWindow.cutoutRectangles bounds [| PixelRect(-5, -5, 60, 60) |]

              Expect.equal rectangles.Length 1 "fallback rectangle"
              Expect.equal (area rectangles) 1200 "fallback covers the full window")
          testCase "an equal key returns the first pipeline and never builds a second one" (fun _ ->
              let released = ResizeArray<int>()

              let cache =
                  new PipelineCache<RenderPipelineKey, int>(fun value -> released.Add value)

              let builds = ref 0

              let build () =
                  builds.Value <- builds.Value + 1
                  Ok builds.Value

              let first = cache.GetOrAdd(opaqueKey, build)
              let second = cache.GetOrAdd(sameOpaqueKey, build)

              Expect.equal first (Ok 1) "first build"
              Expect.equal second (Ok 1) "cache hit returns the stored pipeline"
              Expect.equal builds.Value 1 "one build for equal keys"
              Expect.equal cache.Count 1 "one entry")
          testCase "a key that differs in blend or depth state is a separate pipeline" (fun _ ->
              let cache = new PipelineCache<RenderPipelineKey, int>(ignore)
              let builds = ref 0

              let build () =
                  builds.Value <- builds.Value + 1
                  Ok builds.Value

              cache.GetOrAdd(opaqueKey, build) |> ignore

              cache.GetOrAdd(
                  { opaqueKey with
                      Fragment = Some { additiveFragment with Entry = "fs" } },
                  build
              )
              |> ignore

              cache.GetOrAdd(
                  { opaqueKey with
                      Depth = Some depthState },
                  build
              )
              |> ignore

              cache.GetOrAdd({ opaqueKey with Samples = 1u }, build) |> ignore

              Expect.equal builds.Value 4 "each state change builds its own pipeline"
              Expect.equal cache.Count 4 "four entries")
          testCase "a failed build is reported as a value and leaves nothing cached" (fun _ ->
              let cache = new PipelineCache<RenderPipelineKey, int>(ignore)

              let failure =
                  cache.GetOrAdd(opaqueKey, fun () -> Error(CreationFailed "the test pipeline"))

              Expect.equal failure (Error(CreationFailed "the test pipeline")) "error is returned unchanged"
              Expect.equal cache.Count 0 "failed pipelines are not stored"

              let retry = cache.GetOrAdd(opaqueKey, fun () -> Ok 7)
              Expect.equal retry (Ok 7) "a later attempt can succeed")
          testCase "disposing the cache releases every pipeline exactly once" (fun _ ->
              let released = ResizeArray<int>()

              let cache =
                  new PipelineCache<RenderPipelineKey, int>(fun value -> released.Add value)

              cache.GetOrAdd(opaqueKey, fun () -> Ok 1) |> ignore
              cache.GetOrAdd({ opaqueKey with Samples = 1u }, fun () -> Ok 2) |> ignore
              (cache :> IDisposable).Dispose()
              (cache :> IDisposable).Dispose()

              Expect.equal (released |> Seq.sort |> Seq.toList) [ 1; 2 ] "each value released once")
          testCase "every GPU error names its cause in the text the façade reports" (fun _ ->
              Expect.stringContains (GpuError.describe AdapterUnavailable) "adapter" "adapter"
              Expect.stringContains (GpuError.describe (DeviceLost "reset")) "reset" "device loss keeps its message"

              Expect.stringContains
                  (GpuError.describe (CreationFailed "a depth texture"))
                  "a depth texture"
                  "creation label"

              Expect.stringContains
                  (GpuError.describe (ShaderCompile("PitchSurface.wgsl", "bad token")))
                  "PitchSurface.wgsl"
                  "shader file name"

              Expect.isTrue (GpuError.describe SurfaceLost <> "") "surface loss has a message")
          testCase "a sampled texture entry declares the sample type and dimension the shader expects" (fun _ ->
              let entry = BindGroupLayouts.textureArray 5u ShaderStage.Fragment

              Expect.equal entry.Binding 5u "binding index"
              Expect.equal entry.Texture.SampleType TextureSampleType.Float "float samples"
              Expect.equal entry.Texture.ViewDimension TextureViewDimension.Dimension2DArray "array dimension"
              Expect.equal entry.Texture.Multisampled (Silk.NET.Core.Bool32 0u) "single-sampled"

              let depth = BindGroupLayouts.depthMultisampled 2u ShaderStage.Fragment
              Expect.equal depth.Texture.SampleType TextureSampleType.Depth "depth samples"
              Expect.equal depth.Texture.Multisampled (Silk.NET.Core.Bool32 1u) "multisampled depth")
          testCase "a uniform entry carries the byte size its WGSL struct requires" (fun _ ->
              let entry = BindGroupLayouts.uniform 1u ShaderStage.Fragment 48UL

              Expect.equal entry.Buffer.Type BufferBindingType.Uniform "uniform buffer"
              Expect.equal entry.Buffer.MinBindingSize 48UL "minimum binding size") ]
