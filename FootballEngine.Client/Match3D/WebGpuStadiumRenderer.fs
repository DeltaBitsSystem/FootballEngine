#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Platform
open FootballEngine.Client.Views.Components
open Silk.NET.Core.Contexts
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

module MatchPitchOverlay =
    let margin = 14.0
    let managerWidth = 250.0
    let managerHeight = 380.0
    let intelligenceWidth = 285.0
    let intelligenceHeight = 390.0

module private GpuFacade =
    /// The one place a GpuError becomes an exception; the viewer reports it through RenderWorker's onError.
    let unwrap (result: Result<'T, GpuError>) : 'T =
        match result with
        | Ok value -> value
        | Error error -> invalidOp (GpuError.describe error)

    let dispose (value: #IDisposable) = value.Dispose()

open GpuFacade

type private GpuDrawBatch =
    { MaterialName: string
      VertexBuffer: GpuBuffer<GpuVertex>
      IndexBuffer: GpuBuffer<uint32>
      IndexCount: uint32
      InstanceCount: uint32 }

[<Sealed>]
type WebGpuStadiumRenderer
    (
        parentWindow: unativeint,
        initialBounds: PixelRect,
        assetPath: string,
        ?camera: ViewerCamera,
        ?exposureStops: float32
    ) =
    let startupClock = Diagnostics.Stopwatch.StartNew()

    let windSeconds () =
        ViewerDiagnostics.windPin ()
        |> Option.defaultWith (fun () -> startupClock.Elapsed.TotalSeconds)

    let timedLoad name load =
        let started = Diagnostics.Stopwatch.GetTimestamp()
        let value = load ()
        let elapsed = Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds
        Diagnostics.Trace.WriteLine($"Viewer startup {name}: {elapsed:F1} ms")
        value

    // Independent CPU packages can decode concurrently; all native/GPU ownership stays on the render thread.
    let characterLoad =
        System.Threading.Tasks.Task.Run(fun () ->
            timedLoad "character" (fun () ->
                CharacterAsset.load (Path.Combine(Path.GetDirectoryName assetPath, "player.glb"))))

    let stadiumLoad =
        System.Threading.Tasks.Task.Run(fun () -> timedLoad "stadium GLB" (fun () -> StadiumAsset.load assetPath))

    let environmentPackage =
        match timedLoad "environment" (fun () -> StadiumEnvironment.load assetPath) with
        | Ok package -> package
        | Error message -> invalidOp $"Cannot load authored stadium environment: {message}"

    let lighting =
        let settings =
            { environmentPackage.Settings with
                ExposureStops = defaultArg exposureStops environmentPackage.Settings.ExposureStops }

        match HdrLighting.validate settings with
        | Ok settings -> settings
        | Error message -> invalidArg (nameof exposureStops) message

    let multisampleCount = 4u
    let mutable cameraMode = defaultArg camera ViewerCamera.Broadcast
    let performanceClock = Diagnostics.Stopwatch.StartNew()
    let mutable lastPresentation: float option = None
    let mutable sampleMilliseconds = 0.0
    let mutable sampleFrames = 0
    let mutable performance: (float * float) option = None

    // Presented-frame intervals include pacing and CPU work, without blocking for GPU timestamps.
    let recordPresentation () =
        let now = performanceClock.Elapsed.TotalMilliseconds

        match lastPresentation with
        | Some previous when now - previous > 0.0 && now - previous < 2000.0 ->
            sampleMilliseconds <- sampleMilliseconds + now - previous
            sampleFrames <- sampleFrames + 1

            // A half-second window keeps the counter readable instead of flickering every frame.
            if sampleMilliseconds >= 500.0 then
                let milliseconds = sampleMilliseconds / float sampleFrames
                performance <- Some(1000.0 / milliseconds, milliseconds)
                sampleMilliseconds <- 0.0
                sampleFrames <- 0
        | _ ->
            sampleMilliseconds <- 0.0
            sampleFrames <- 0
            performance <- None

        lastPresentation <- Some now

    let mutable display = 0n
    let mutable window = 0un
    let mutable gpu: GpuContext option = None
    let mutable pipelines: RenderPipelineCache option = None
    let mutable shader = NativePtr.nullPtr<ShaderModule>
    let mutable sceneLayout = NativePtr.nullPtr<BindGroupLayout>
    let mutable scenePipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable scenePipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable cameraBindGroup = NativePtr.nullPtr<BindGroup>
    let mutable cameraBuffer: GpuBuffer<float32> option = None
    let mutable jointBuffer: GpuBuffer<Matrix4x4> option = None
    let mutable shadows: DirectionalShadowPass option = None
    let mutable grassDetail: ProceduralScenePass option = None
    let mutable skyBackground: ProceduralScenePass option = None
    let mutable showGrassDetail = false
    let mutable staticShadowsReady = false
    let shadowSettings = DirectionalShadow.stadium

    let shadowMatrixData =
        DirectionalShadow.matrixData shadowSettings lighting.SunDirection

    let sun: SunState =
        { Direction = lighting.SunDirection
          Radiance = lighting.SunRadiance
          EnvironmentIntensity = lighting.EnvironmentIntensity }

    let lightViewProjection =
        match SceneBuilder.matrixOfElements shadowMatrixData with
        | Ok matrix -> matrix
        | Error message -> invalidOp message

    let environmentTextures = ResizeArray<GpuTexture>()
    let mutable environmentSampler: GpuSampler option = None
    let surfaceTextures = ResizeArray<GpuTexture>()
    let mutable surfaceSampler: GpuSampler option = None
    let mutable characterAlbedo: GpuTexture option = None
    let mutable characterDetail: GpuTexture option = None
    let mutable characterSampler: GpuSampler option = None
    let mutable hdrOutput: HdrOutputPass option = None

    let quality =
        match
            ViewerQuality.ofTier (ViewerQualitySelection.current ())
            |> ViewerQuality.validated
        with
        | Ok selected -> selected
        // Presets are validated by tests; an invalid one must not cost the player the match view.
        | Error _ -> ViewerQuality.ultra

    let environment = environmentPackage.Environment

    let surfacePack =
        match
            timedLoad "surfaces" (fun () ->
                SurfaceTexturePack.load (Path.Combine(Path.GetDirectoryName assetPath, "Surfaces")))
        with
        | Ok pack -> pack
        | Error message -> invalidOp $"Cannot load stadium surface materials: {message}"

    let mutable hudShader = NativePtr.nullPtr<ShaderModule>
    let mutable hudLayout = NativePtr.nullPtr<BindGroupLayout>
    let mutable hudPipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable hudBindGroup = NativePtr.nullPtr<BindGroup>
    let mutable hudPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable hudTexture: GpuTexture option = None
    let mutable hudSampler: GpuSampler option = None
    let mutable retainedHud: RetainedMatchHud option = None
    let mutable hudUploadBytes = 0L
    let mutable hudQuadVertexBuffer: GpuBuffer<GpuVertex> option = None
    let mutable hudQuadIndexBuffer: GpuBuffer<uint32> option = None
    let mutable multisampleColor: GpuTexture option = None
    let mutable depthTarget: GpuTexture option = None
    let mutable lastBounds = PixelRect(0, 0, 0, 0)
    let mutable lastShapeBounds: PixelRect option = None
    let mutable lastCutouts: PixelRect array = [||]
    let mutable disposed = false
    let mutable frameCount = 0
    let scene = stadiumLoad.GetAwaiter().GetResult()

    let character =
        match characterLoad.GetAwaiter().GetResult() with
        | Ok asset -> asset
        | Error message -> invalidOp $"Cannot load the first-party animated character: {message}"

    let jointMatrices = Array.create (22 * character.JointCount) Matrix4x4.Identity

    let mutable staticBatches: GpuDrawBatch array = [||]
    let mutable dynamicBatches: GpuDrawBatch array = [||]
    let mutable carrierMarkerBatch: GpuDrawBatch option = None

    let context () =
        gpu
        |> Option.defaultWith (fun () -> invalidOp "The WebGPU context was not created.")

    let pipelineCache () =
        pipelines
        |> Option.defaultWith (fun () -> invalidOp "The WebGPU pipeline cache was not created.")

    let releaseBatch (batch: GpuDrawBatch) =
        (batch.VertexBuffer :> IDisposable).Dispose()
        (batch.IndexBuffer :> IDisposable).Dispose()

    let createGpuBatch
        (materialName: string)
        (vertices: GpuVertex array)
        (indices: uint32 array)
        : GpuDrawBatch option =
        if indices.Length = 0 then
            None
        else
            let ctx = context ()

            let vertexBuffer =
                unwrap (
                    GpuBuffer<GpuVertex>
                        .Create(ctx, $"{materialName} vertices", BufferUsage.Vertex ||| BufferUsage.CopyDst, vertices)
                )

            try
                let indexBuffer =
                    unwrap (
                        GpuBuffer<uint32>
                            .Create(ctx, $"{materialName} indices", BufferUsage.Index ||| BufferUsage.CopyDst, indices)
                    )

                Some
                    { MaterialName = materialName
                      VertexBuffer = vertexBuffer
                      IndexBuffer = indexBuffer
                      IndexCount = uint32 indices.Length
                      InstanceCount = 1u }
            with _ ->
                (vertexBuffer :> IDisposable).Dispose()
                reraise ()

    let createGpuBatches (batches: (string * GpuVertex[] * uint32[])[]) =
        let created = ResizeArray<GpuDrawBatch>()

        try
            for materialName, vertices, indices in batches do
                createGpuBatch materialName vertices indices |> Option.iter created.Add

            created.ToArray()
        with _ ->
            created |> Seq.iter releaseBatch
            reraise ()

    let createCarrierMarkerBatch () =
        let vertices = ResizeArray<GpuVertex>()
        let indices = ResizeArray<uint32>()
        let ringColor = Vector4(0.92f, 1.0f, 0.96f, 1.0f)
        let markerColor = Vector4(0.12f, 1.0f, 0.42f, 1.0f)
        let ringNormal = Vector3.UnitY
        let markerNormal = Vector3.Normalize(Vector3(-0.38f, 0.88f, 0.28f))

        let addVertex (position: Vector3) (normal: Vector3) (color: Vector4) =
            let index = uint32 vertices.Count

            vertices.Add
                { Position = position
                  Normal = normal
                  Color = color
                  Uv = Vector2.Zero
                  RootIndex = uint32 SceneRoot.carrierMarker
                  Joints = Vector4.Zero
                  Weights = Vector4.Zero
                  Surface = uint32 StadiumSurface.Plain }

            index

        let ringSegments = 40

        for segment in 0..ringSegments do
            let angle = 2.0f * MathF.PI * float32 segment / float32 ringSegments
            let x = MathF.Cos(angle)
            let z = MathF.Sin(angle)
            addVertex (Vector3(0.88f * x, 0.035f, 0.88f * z)) ringNormal ringColor |> ignore
            addVertex (Vector3(0.74f * x, 0.035f, 0.74f * z)) ringNormal ringColor |> ignore

        for segment in 0 .. ringSegments - 1 do
            let outer = uint32 (segment * 2)
            let inner = outer + 1u
            let nextOuter = outer + 2u
            let nextInner = outer + 3u
            indices.Add outer
            indices.Add inner
            indices.Add nextOuter
            indices.Add nextOuter
            indices.Add inner
            indices.Add nextInner

        let arrowLeft = addVertex (Vector3(-0.38f, 2.65f, 0.04f)) markerNormal markerColor
        let arrowRight = addVertex (Vector3(0.38f, 2.65f, 0.04f)) markerNormal markerColor
        let arrowTip = addVertex (Vector3(0.0f, 2.08f, 0.04f)) markerNormal markerColor
        indices.Add arrowLeft
        indices.Add arrowRight
        indices.Add arrowTip

        createGpuBatch "ball-carrier focus marker" (vertices.ToArray()) (indices.ToArray())

    let configureSurface (width: uint32) (height: uint32) =
        let ctx = context ()
        unwrap (ctx.Configure(width, height))

        // The HDR pass owns the scene target and bloom chain; it is created once and resized on every surface change.
        let output =
            match hdrOutput with
            | Some output -> output
            | None ->
                let output =
                    unwrap (
                        HdrOutputPass.Create(
                            ctx,
                            pipelineCache (),
                            lighting,
                            quality,
                            BroadcastGrade.standard,
                            ctx.Format
                        )
                    )

                hdrOutput <- Some output
                output

        multisampleColor |> Option.iter dispose
        multisampleColor <- None
        depthTarget |> Option.iter dispose
        depthTarget <- None

        let color =
            unwrap (
                GpuTexture.Create(
                    ctx,
                    { Label = $"a {multisampleCount}x multisampled color texture"
                      Format = TextureFormat.Rgba16float
                      Width = width
                      Height = height
                      Layers = 1u
                      MipLevels = 1u
                      Samples = multisampleCount
                      Usage = TextureUsage.RenderAttachment }
                )
            )

        multisampleColor <- Some color

        // The post chain reads scene depth for ambient occlusion, so it must be bindable and kept after the pass.
        let depth =
            unwrap (
                GpuTexture.Create(
                    ctx,
                    { Label = "a depth texture"
                      Format = TextureFormat.Depth24Plus
                      Width = width
                      Height = height
                      Layers = 1u
                      MipLevels = 1u
                      Samples = multisampleCount
                      Usage = TextureUsage.RenderAttachment ||| TextureUsage.TextureBinding }
                )
            )

        depthTarget <- Some depth
        unwrap (output.Resize(width, height, depth))

    let configureHudTexture (width: uint32) (height: uint32) =
        let ctx = context ()
        retainedHud |> Option.iter dispose
        retainedHud <- Some(new RetainedMatchHud(int width, int height))

        if not (GpuPointer.isMissing hudBindGroup) then
            ctx.Api.BindGroupRelease(hudBindGroup)
            hudBindGroup <- NativePtr.nullPtr<BindGroup>

        hudTexture |> Option.iter dispose
        hudTexture <- None

        let texture =
            unwrap (
                GpuTexture.Create(
                    ctx,
                    { Label = "the match HUD texture"
                      Format = TextureFormat.Rgba8Unorm
                      Width = width
                      Height = height
                      Layers = 1u
                      MipLevels = 1u
                      Samples = 1u
                      Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }
                )
            )

        hudTexture <- Some texture

        hudBindGroup <-
            unwrap (
                BindGroups.create
                    ctx
                    "the match HUD texture bind group"
                    hudLayout
                    [ BindGroups.view 0u texture.View
                      BindGroups.sampler 1u hudSampler.Value.Handle ]
            )

    let uploadHudRegion (hud: RetainedMatchHud) (region: SkiaSharp.SKRectI) =
        let pointer = hud.Pixels + nativeint (region.Top * hud.RowBytes + region.Left * 4)
        let length = (region.Height - 1) * hud.RowBytes + region.Width * 4

        hudTexture.Value.UploadRegion(
            uint32 region.Left,
            uint32 region.Top,
            uint32 region.Width,
            uint32 region.Height,
            uint32 hud.RowBytes,
            pointer,
            length
        )

        hudUploadBytes <- hudUploadBytes + int64 (region.Width * region.Height * 4)

    // Composition is pure; this is the one place where a rejected frame becomes an exception for the render worker.
    let buildScene (frame: RenderFrame) (palettes: Matrix4x4[]) : SceneFrame =
        let ctx = context ()

        let input: SceneInput =
            { CameraMode = cameraMode
              Aspect = float32 ctx.Width / float32 ctx.Height
              TimeSeconds = windSeconds ()
              Frame = frame
              JointPalettes = palettes
              JointCount = character.JointCount
              BallMeshRadius = scene.BallRadius
              Sun = sun
              LightViewProjection = lightViewProjection }

        match SceneBuilder.build input with
        | Ok built -> built
        | Error(InvalidPalettes message) -> invalidArg "palettes" message
        | Error(InvalidFrame message) -> invalidArg "frame" message
        | Error SingularCamera -> invalidOp "The viewer camera produced a singular projection."

    let uploadScene (sceneFrame: SceneFrame) =
        // Blade detail is a per-frame decision, so the grass pass follows the same camera that was just packed.
        showGrassDetail <- sceneFrame.ShowGrassDetail

        hdrOutput
        |> Option.iter (fun output ->
            output.SetCamera(
                sceneFrame.Camera.NearPlane,
                sceneFrame.Camera.FarPlane,
                sceneFrame.Camera.FieldOfView,
                sceneFrame.Camera.Aspect
            ))

        jointBuffer.Value.Write(sceneFrame.JointPalettes)
        cameraBuffer.Value.Write(UniformLayout.packCamera sceneFrame)

    let updateWindow (bounds: PixelRect) =
        if bounds.Width > 0 && bounds.Height > 0 && bounds <> lastBounds then
            X11ChildWindow.moveResize display window bounds
            X11ChildWindow.flush display
            lastBounds <- bounds

            let width = uint32 bounds.Width
            let height = uint32 bounds.Height

            let ctx = context ()

            if width <> ctx.Width || height <> ctx.Height then
                configureSurface width height
                configureHudTexture width height

    let updateWindowShape (bounds: PixelRect) (cutouts: PixelRect array) =
        let sameRect (left: PixelRect) (right: PixelRect) =
            left.X = right.X
            && left.Y = right.Y
            && left.Width = right.Width
            && left.Height = right.Height

        let sameCutouts =
            cutouts.Length = lastCutouts.Length
            && Array.forall2 sameRect cutouts lastCutouts

        let sameBounds = lastShapeBounds |> Option.exists (sameRect bounds)

        if not (sameBounds && sameCutouts) then
            // Carving these regions out lets Avalonia draw and receive clicks through its native WebGPU child.
            X11ChildWindow.setShape display window (X11ChildWindow.cutoutRectangles bounds cutouts)
            X11ChildWindow.flush display
            lastShapeBounds <- Some bounds
            lastCutouts <- Array.copy cutouts


    let uploadCharacterLevels (texture: GpuTexture) (data: CharacterTextureData) =
        for level = 0 to data.Levels.Length - 1 do
            let mip = data.Levels[level]

            texture.UploadLevel(
                uint32 level,
                uint32 mip.Width,
                uint32 mip.Height,
                1u,
                uint32 (mip.Width * 4),
                mip.Pixels,
                0,
                mip.Pixels.Length
            )

    let createShaderAndPipeline () =
        let ctx = context ()
        let cache = pipelineCache ()

        let environmentLevels =
            [ environment.Environment,
              environment.Width,
              environment.Height,
              environment.Layers,
              TextureFormat.Rgba16float,
              8
              environment.Brdf, environment.LutSize, environment.LutSize, 1, TextureFormat.RG16float, 4
              environment.Sky, environment.SkyWidth, environment.SkyHeight, 1, TextureFormat.Rgba16float, 8 ]

        for pixels, width, height, layers, format, bytesPerPixel in environmentLevels do
            let texture =
                unwrap (
                    GpuTexture.Create(
                        ctx,
                        { Label = "a floating-point IBL texture"
                          Format = format
                          Width = uint32 width
                          Height = uint32 height
                          Layers = uint32 layers
                          MipLevels = 1u
                          Samples = 1u
                          Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }
                    )
                )

            texture.UploadLevel(
                0u,
                uint32 width,
                uint32 height,
                uint32 layers,
                uint32 (width * bytesPerPixel),
                pixels,
                0,
                pixels.Length
            )

            environmentTextures.Add texture

        environmentSampler <-
            Some(
                unwrap (
                    GpuSampler.Create(
                        ctx,
                        "the HDR environment sampler",
                        { AddressU = AddressMode.Repeat
                          AddressV = AddressMode.ClampToEdge
                          AddressW = AddressMode.ClampToEdge
                          Magnification = FilterMode.Linear
                          Minification = FilterMode.Linear
                          Mipmap = MipmapFilterMode.Linear
                          MaxAnisotropy = 1us
                          LodMaxClamp = 1.0f
                          Compare = None }
                    )
                )
            )

        for index, pixels in [ 0, surfacePack.Albedo; 1, surfacePack.Normal; 2, surfacePack.Arm ] do
            let format =
                if index = 0 then
                    TextureFormat.Rgba8UnormSrgb
                else
                    TextureFormat.Rgba8Unorm

            let texture =
                unwrap (
                    GpuTexture.Create(
                        ctx,
                        { Label = "a surface material array"
                          Format = format
                          Width = uint32 surfacePack.Size
                          Height = uint32 surfacePack.Size
                          Layers = uint32 surfacePack.Layers
                          MipLevels = uint32 surfacePack.Levels
                          Samples = 1u
                          Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }
                    )
                )

            let mutable offset = 0

            for level = 0 to surfacePack.Levels - 1 do
                let width = surfacePack.Size >>> level
                let bytes = width * width * surfacePack.Layers * 4

                texture.UploadLevel(
                    uint32 level,
                    uint32 width,
                    uint32 width,
                    uint32 surfacePack.Layers,
                    uint32 (width * 4),
                    pixels,
                    offset,
                    bytes
                )

                offset <- offset + bytes

            surfaceTextures.Add texture

        surfaceSampler <-
            Some(
                unwrap (
                    GpuSampler.Create(
                        ctx,
                        "the surface material sampler",
                        { AddressU = AddressMode.Repeat
                          AddressV = AddressMode.Repeat
                          AddressW = AddressMode.Repeat
                          Magnification = FilterMode.Linear
                          Minification = FilterMode.Linear
                          Mipmap = MipmapFilterMode.Linear
                          MaxAnisotropy = 8us
                          LodMaxClamp = float32 (surfacePack.Levels - 1)
                          Compare = None }
                    )
                )
            )

        // Stop before atlas tiles collapse into one another; mip filtering suppresses distant fabric shimmer.
        let albedo =
            unwrap (
                GpuTexture.Create(
                    ctx,
                    { Label = "the athlete albedo atlas"
                      Format = TextureFormat.Rgba8UnormSrgb
                      Width = uint32 character.Albedo.Levels[0].Width
                      Height = uint32 character.Albedo.Levels[0].Height
                      Layers = 1u
                      MipLevels = uint32 character.Albedo.Levels.Length
                      Samples = 1u
                      Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }
                )
            )

        uploadCharacterLevels albedo character.Albedo
        characterAlbedo <- Some albedo

        let detail =
            unwrap (
                GpuTexture.Create(
                    ctx,
                    { Label = "the athlete normal/roughness atlas"
                      Format = TextureFormat.Rgba8Unorm
                      Width = uint32 character.Albedo.Levels[0].Width
                      Height = uint32 character.Albedo.Levels[0].Height
                      Layers = 1u
                      MipLevels = uint32 character.Albedo.Levels.Length
                      Samples = 1u
                      Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }
                )
            )

        uploadCharacterLevels detail character.NormalRoughness
        characterDetail <- Some detail

        characterSampler <-
            Some(
                unwrap (
                    GpuSampler.Create(
                        ctx,
                        "the athlete texture sampler",
                        { AddressU = AddressMode.ClampToEdge
                          AddressV = AddressMode.ClampToEdge
                          AddressW = AddressMode.ClampToEdge
                          Magnification = FilterMode.Linear
                          Minification = FilterMode.Linear
                          Mipmap = MipmapFilterMode.Linear
                          MaxAnisotropy = 1us
                          LodMaxClamp = 8.0f
                          Compare = None }
                    )
                )
            )

        let stadiumSource =
            """@group(0) @binding(0) var<uniform> camera: Camera;
@group(0) @binding(1) var<storage, read> joints: array<mat4x4<f32>>;
@group(0) @binding(2) var athleteAlbedo: texture_2d<f32>;
@group(0) @binding(3) var athleteDetail: texture_2d<f32>;
@group(0) @binding(4) var athleteSampler: sampler;
@group(0) @binding(5) var surfaceAlbedo: texture_2d_array<f32>;
@group(0) @binding(6) var surfaceNormalMap: texture_2d_array<f32>;
@group(0) @binding(7) var surfaceArm: texture_2d_array<f32>;
@group(0) @binding(8) var surfaceSampler: sampler;
@group(0) @binding(9) var environmentMap: texture_2d_array<f32>;
@group(0) @binding(10) var environmentBrdf: texture_2d<f32>;
@group(0) @binding(11) var environmentSampler: sampler;
@group(0) @binding(12) var skyMap: texture_2d<f32>;
const JOINT_COUNT: u32 = CHARACTER_JOINT_COUNT;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
    @location(3) uv: vec2<f32>,
    @location(4) rootIndex: u32,
    @location(5) joints: vec4<f32>,
    @location(6) weights: vec4<f32>,
    @location(7) surface: u32,
}

struct VertexOutput {
    @builtin(position) clipPosition: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) uv: vec2<f32>,
    @location(3) world: vec3<f32>,
    @location(4) @interpolate(flat) textured: u32,
    @location(5) @interpolate(flat) surface: u32,
}

fn transformVertex(input: VertexInput, instance: u32) -> VertexOutput {
    var output: VertexOutput;
    let root = input.rootIndex + instance;
    let model = camera.rootTransforms[root];
    var localPosition = vec4<f32>(input.position, 1.0);
    var localNormal = vec4<f32>(input.normal, 0.0);
    if dot(input.weights, vec4<f32>(1.0)) > 0.0 {
        let base = root * JOINT_COUNT;
        let skin = joints[base + u32(input.joints.x)] * input.weights.x
                 + joints[base + u32(input.joints.y)] * input.weights.y
                 + joints[base + u32(input.joints.z)] * input.weights.z
                 + joints[base + u32(input.joints.w)] * input.weights.w;
        localPosition = skin * localPosition;
        localNormal = skin * localNormal;
    }
    if root == 24u && input.position.y > 2.0 {
        // The overhead annotation must not become a giant triangle when the replay lens moves close.
        let markerScale = clamp(distance(camera.position.xyz, model[3].xyz) / 48.0, 0.12, 1.5);
        localPosition.x *= markerScale;
        localPosition.y = 2.08 + (localPosition.y - 2.08) * markerScale;
    }
    let world = model * localPosition;
    output.clipPosition = camera.viewProjection * world;
    output.normal = normalize((model * localNormal).xyz);
    output.color = input.color;
    let awayKit = dot(input.weights, vec4<f32>(1.0)) > 0.0 && root >= 11u;
    output.uv = input.uv + vec2<f32>(0.0, select(0.0, 0.5, awayKit));
    output.world = world.xyz;
    output.textured = select(0u, 1u, dot(input.weights, vec4<f32>(1.0)) > 0.0);
    output.surface = input.surface;
    return output;
}

@vertex fn shadowVs(input: VertexInput, @builtin(instance_index) instance: u32) -> @builtin(position) vec4<f32> {
    let vertex = transformVertex(input, instance);
    return camera.lightViewProjection * vec4<f32>(vertex.world, 1.0);
}

@vertex fn vs(input: VertexInput, @builtin(instance_index) instance: u32) -> VertexOutput {
    return transformVertex(input, instance);
}

fn materialHash(cell: vec2<i32>) -> f32 {
    var h = bitcast<u32>(cell.x) * 1597334677u ^ bitcast<u32>(cell.y) * 3812015801u;
    h = (h ^ (h >> 16u)) * 2246822519u;
    return f32(h & 0x00ffffffu) / 16777215.0;
}

fn materialNoise(p: vec2<f32>) -> f32 {
    let cell = vec2<i32>(floor(p));
    let f = fract(p);
    let blend = f * f * (3.0 - 2.0 * f);
    return mix(mix(materialHash(cell), materialHash(cell + vec2<i32>(1, 0)), blend.x),
               mix(materialHash(cell + vec2<i32>(0, 1)), materialHash(cell + vec2<i32>(1, 1)), blend.x), blend.y);
}

@fragment fn fs(input: VertexOutput) -> @location(0) vec4<f32> {
    let normal = normalize(input.normal);
    let sunlight = camera.sunAndEnvironment.xyz;
    let dpdxWorld = dpdx(input.world);
    let dpdyWorld = dpdy(input.world);
    let duvdx = dpdx(input.uv);
    let duvdy = dpdy(input.uv);
    let footprint = max(length(dpdxWorld), length(dpdyWorld));
    let surfaceUv = select(select(input.world.xy, input.world.zy, abs(normal.x) > abs(normal.z)), input.world.xz * vec2<f32>(1.0, -1.0), abs(normal.y) > 0.7) * 0.5;
    let surfaceLayer = select(0, 1, input.surface == 4u);
    let surfaceUvDx = dpdx(surfaceUv);
    let surfaceUvDy = dpdy(surfaceUv);
    if input.textured != 0u {
        let albedo = textureSampleGrad(athleteAlbedo, athleteSampler, input.uv, duvdx, duvdy);
        let detail = textureSampleGrad(athleteDetail, athleteSampler, input.uv, duvdx, duvdy);
        // The differential tangent frame follows the deformed mesh without importing a second skinning palette.
        let perpendicularX = cross(dpdyWorld, normal);
        let perpendicularY = cross(normal, dpdxWorld);
        let tangent = perpendicularX * duvdx.x + perpendicularY * duvdy.x;
        let bitangent = perpendicularX * duvdx.y + perpendicularY * duvdy.y;
        let inverseScale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 0.00000001));
        let mapped = detail.rgb * 2.0 - 1.0;
        let surfaceNormal = normalize(tangent * inverseScale * mapped.x + bitangent * inverseScale * mapped.y + normal * mapped.z);
        let roughness = clamp(detail.a, 0.18, 1.0);
        let litAthlete = shadeAthlete(albedo.rgb, surfaceNormal, normal, input.world, roughness, input.surface);
        return vec4<f32>(litAthlete, albedo.a);
    }
    if input.surface != 0u {
        if input.surface == 7u {
            return vec4<f32>(shadeMatchBall(input.uv, normal, input.world), 1.0);
        }
        if input.surface <= 3u {
            return vec4<f32>(shadePitch(input.world, input.surface, dpdxWorld, dpdyWorld), input.color.a);
        }
        var base = input.color.rgb;
        var materialNormal = normal;
        var roughness = 0.90;
        var metallic = 0.0;
        var ao = 1.0;
        let broadVariation = materialNoise(input.world.xz * 0.24);
        let medium = materialNoise(input.world.xz * 2.2);
        // Frequencies smaller than a pixel fade to their mean instead of producing broadcast-camera shimmer.
        let detailWeight = 1.0 - smoothstep(0.018, 0.065, footprint);
        let grain = materialNoise(input.world.xz * vec2<f32>(75.0, 22.0));
        if input.surface == 4u {
            let surfaceColour = textureSampleGrad(surfaceAlbedo, surfaceSampler, surfaceUv, surfaceLayer, surfaceUvDx, surfaceUvDy).rgb;
            let surfaceDetail = textureSampleGrad(surfaceNormalMap, surfaceSampler, surfaceUv, surfaceLayer, surfaceUvDx, surfaceUvDy).rgb * 2.0 - 1.0;
            let surfaceProperties = textureSampleGrad(surfaceArm, surfaceSampler, surfaceUv, surfaceLayer, surfaceUvDx, surfaceUvDy).rgb;
            let perpendicularX = cross(dpdyWorld, normal);
            let perpendicularY = cross(normal, dpdxWorld);
            let tangent = perpendicularX * surfaceUvDx.x + perpendicularY * surfaceUvDy.x;
            let bitangent = perpendicularX * surfaceUvDx.y + perpendicularY * surfaceUvDy.y;
            let scale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 0.00000001));
            let mapped = normalize(vec3<f32>(surfaceDetail.xy * 0.45, surfaceDetail.z));
            materialNormal = normalize(tangent * scale * mapped.x + bitangent * scale * mapped.y + normal * mapped.z);
            base *= surfaceColour / vec3<f32>(0.25);
            base *= 0.90 + 0.16 * broadVariation + (grain - 0.5) * 0.09 * detailWeight;
            roughness = clamp(surfaceProperties.g, 0.65, 1.0);
            ao = mix(0.75, 1.0, surfaceProperties.r);
        } else if input.surface == 5u {
            base *= 0.95 + 0.06 * medium;
            roughness = 0.40;
            metallic = 0.72;
        } else {
            roughness = 0.30;
        }
        let visibility = sunlightVisibility(input.world, normal);
        let lit = shadeMaterial(base, materialNormal, input.world, roughness, metallic, ao, visibility);
        return vec4<f32>(lit, input.color.a);
    }
    return vec4<f32>(shadeMaterial(input.color.rgb, normal, input.world, 0.85, 0.0, 1.0, sunlightVisibility(input.world, normal)), input.color.a);
}
"""

        let shaderFile name =
            ShaderLibrary.readFile name
            |> Result.defaultWith (fun error -> invalidOp (GpuError.describe error))

        // Constants precede the files that consume them, so the order below is part of the shader contract.
        let source =
            ShaderLibrary.compose
                [ shaderFile "CameraUniform.wgsl"
                  stadiumSource.Replace("CHARACTER_JOINT_COUNT", $"{character.JointCount}u")
                  shaderFile "EnvironmentLighting.wgsl"
                  shaderFile "ShadowLighting.wgsl"
                  AthleteMaterial.shaderConstants
                  shaderFile "AthleteSurface.wgsl"
                  shaderFile "MatchBallSurface.wgsl"
                  PitchMarkings.shaderConstants
                  shaderFile "PitchSurface.wgsl"
                  shaderFile "GrassDetail.wgsl"
                  shaderFile "SkyBackground.wgsl" ]

        shader <- unwrap (ShaderModules.compile ctx "stadium" source)

        sceneLayout <-
            unwrap (
                BindGroupLayouts.create
                    ctx
                    "the camera bind-group layout"
                    [ BindGroupLayouts.uniform 0u (ShaderStage.Vertex ||| ShaderStage.Fragment) 0UL
                      BindGroupLayouts.storage 1u ShaderStage.Vertex true
                      BindGroupLayouts.texture2d 2u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 3u ShaderStage.Fragment
                      BindGroupLayouts.sampler 4u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 5u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 6u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 7u ShaderStage.Fragment
                      BindGroupLayouts.sampler 8u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 9u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 10u ShaderStage.Fragment
                      BindGroupLayouts.sampler 11u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 12u ShaderStage.Fragment ]
            )

        let shadowPass =
            unwrap (DirectionalShadowPass.Create(ctx, cache, ("stadium", shader), shadowSettings, sceneLayout))

        shadows <- Some shadowPass

        scenePipelineLayout <-
            unwrap (
                BindGroupLayouts.pipelineLayout
                    ctx
                    "the stadium pipeline layout"
                    [ sceneLayout; shadowPass.ReceiverLayout ]
            )

        cameraBuffer <-
            Some(
                unwrap (
                    GpuBuffer<float32>
                        .Create(
                            ctx,
                            "broadcast camera and player transforms",
                            BufferUsage.Uniform ||| BufferUsage.CopyDst,
                            Array.zeroCreate<float32> UniformLayout.elementCount
                        )
                )
            )

        jointBuffer <-
            Some(
                unwrap (
                    GpuBuffer<Matrix4x4>
                        .Create(
                            ctx,
                            "character joint palettes",
                            BufferUsage.Storage ||| BufferUsage.CopyDst,
                            jointMatrices
                        )
                )
            )

        cameraBindGroup <-
            unwrap (
                BindGroups.create
                    ctx
                    "the camera uniform bind group"
                    sceneLayout
                    [ BindGroups.buffer
                          0u
                          cameraBuffer.Value.Handle
                          (uint64 (UniformLayout.elementCount * sizeof<float32>))
                      BindGroups.buffer 1u jointBuffer.Value.Handle (uint64 (jointMatrices.Length * sizeof<Matrix4x4>))
                      BindGroups.view 2u characterAlbedo.Value.View
                      BindGroups.view 3u characterDetail.Value.View
                      BindGroups.sampler 4u characterSampler.Value.Handle
                      BindGroups.view 5u surfaceTextures[0].View
                      BindGroups.view 6u surfaceTextures[1].View
                      BindGroups.view 7u surfaceTextures[2].View
                      BindGroups.sampler 8u surfaceSampler.Value.Handle
                      BindGroups.view 9u environmentTextures[0].View
                      BindGroups.view 10u environmentTextures[1].View
                      BindGroups.sampler 11u environmentSampler.Value.Handle
                      BindGroups.view 12u environmentTextures[2].View ]
            )

        let sceneKey: RenderPipelineKey =
            { Shader = "stadium"
              Layout = "stadium scene"
              VertexEntry = "vs"
              Vertex = GpuVertexFull
              Fragment =
                Some
                    { Entry = "fs"
                      ColorFormat = TextureFormat.Rgba16float
                      Blend = Replace }
              Depth =
                Some
                    { DepthFormat = TextureFormat.Depth24Plus
                      WriteDepth = true
                      Compare = CompareFunction.Less
                      Bias = 0
                      SlopeScale = 0.0f }
              Cull = CullMode.None
              Samples = multisampleCount
              AlphaToCoverage = false }

        scenePipeline <-
            unwrap (
                cache.GetOrAdd(
                    sceneKey,
                    fun () ->
                        RenderPipelines.build ctx "the stadium render pipeline" sceneKey shader scenePipelineLayout
                )
            )

        grassDetail <-
            Some(
                unwrap (
                    ProceduralScenePass.Create(
                        ctx,
                        cache,
                        ("stadium", shader),
                        ("stadium scene", scenePipelineLayout),
                        TextureFormat.Depth24Plus,
                        multisampleCount,
                        ProceduralScenePassSettings.grass
                    )
                )
            )

        skyBackground <-
            Some(
                unwrap (
                    ProceduralScenePass.Create(
                        ctx,
                        cache,
                        ("stadium", shader),
                        ("stadium scene", scenePipelineLayout),
                        TextureFormat.Depth24Plus,
                        multisampleCount,
                        ProceduralScenePassSettings.sky
                    )
                )
            )

    let createHudShaderAndPipeline () =
        let ctx = context ()
        let cache = pipelineCache ()

        let source =
            """struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) uv: vec2<f32>,
}

struct VertexOutput {
    @builtin(position) clipPosition: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@group(0) @binding(0) var hudImage: texture_2d<f32>;
@group(0) @binding(1) var hudSampler: sampler;

@vertex fn vs(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.clipPosition = vec4<f32>(input.position, 1.0);
    output.uv = input.uv;
    return output;
}

@fragment fn fs(input: VertexOutput) -> @location(0) vec4<f32> {
    let pixel = textureSample(hudImage, hudSampler, input.uv);
    // Skia supplies premultiplied sRGB bytes; decode straight RGB before blending in linear light.
    let straight = pixel.rgb / max(pixel.a, 0.00001);
    let linear = select(pow((straight + vec3<f32>(0.055)) / 1.055, vec3<f32>(2.4)), straight / 12.92, straight <= vec3<f32>(0.04045));
    return vec4<f32>(linear * pixel.a, pixel.a);
}
"""

        let hudModule = unwrap (ShaderModules.compile ctx "match HUD" source)
        hudShader <- hudModule

        hudLayout <-
            unwrap (
                BindGroupLayouts.create
                    ctx
                    "the match HUD bind-group layout"
                    [ BindGroupLayouts.texture2d 0u ShaderStage.Fragment
                      BindGroupLayouts.sampler 1u ShaderStage.Fragment ]
            )

        hudPipelineLayout <- unwrap (BindGroupLayouts.pipelineLayout ctx "the match HUD pipeline layout" [ hudLayout ])

        hudSampler <-
            Some(
                unwrap (
                    GpuSampler.Create(
                        ctx,
                        "the match HUD sampler",
                        { AddressU = AddressMode.ClampToEdge
                          AddressV = AddressMode.ClampToEdge
                          AddressW = AddressMode.ClampToEdge
                          Magnification = FilterMode.Linear
                          Minification = FilterMode.Linear
                          Mipmap = MipmapFilterMode.Linear
                          MaxAnisotropy = 1us
                          LodMaxClamp = 0.0f
                          Compare = None }
                    )
                )
            )

        let hudKey: RenderPipelineKey =
            { Shader = "match HUD"
              Layout = "match HUD"
              VertexEntry = "vs"
              Vertex = GpuVertexPositionUv
              Fragment =
                Some
                    { Entry = "fs"
                      ColorFormat = ctx.Format
                      Blend = PremultipliedAlpha }
              Depth = None
              Cull = CullMode.None
              Samples = 1u
              AlphaToCoverage = false }

        hudPipeline <-
            unwrap (
                cache.GetOrAdd(
                    hudKey,
                    fun () ->
                        RenderPipelines.build ctx "the match HUD render pipeline" hudKey hudModule hudPipelineLayout
                )
            )

        let topLeft =
            { Position = Vector3(-1.0f, 1.0f, 0.0f)
              Normal = Vector3.UnitY
              Color = Vector4.One
              Uv = Vector2(0.0f, 0.0f)
              RootIndex = 0u
              Joints = Vector4.Zero
              Weights = Vector4.Zero
              Surface = uint32 StadiumSurface.Plain }

        let topRight =
            { topLeft with
                Position = Vector3(1.0f, 1.0f, 0.0f)
                Uv = Vector2(1.0f, 0.0f) }

        let bottomRight =
            { topLeft with
                Position = Vector3(1.0f, -1.0f, 0.0f)
                Uv = Vector2(1.0f, 1.0f) }

        let bottomLeft =
            { topLeft with
                Position = Vector3(-1.0f, -1.0f, 0.0f)
                Uv = Vector2(0.0f, 1.0f) }

        hudQuadVertexBuffer <-
            Some(
                unwrap (
                    GpuBuffer<GpuVertex>
                        .Create(
                            ctx,
                            "match HUD quad vertices",
                            BufferUsage.Vertex ||| BufferUsage.CopyDst,
                            [| topLeft; topRight; bottomRight; bottomLeft |]
                        )
                )
            )

        hudQuadIndexBuffer <-
            Some(
                unwrap (
                    GpuBuffer<uint32>
                        .Create(
                            ctx,
                            "match HUD quad indices",
                            BufferUsage.Index ||| BufferUsage.CopyDst,
                            [| 0u; 1u; 2u; 0u; 2u; 3u |]
                        )
                )
            )

        configureHudTexture ctx.Width ctx.Height

    let releaseResources () =
        if not disposed then
            disposed <- true
            retainedHud |> Option.iter dispose
            retainedHud <- None
            hdrOutput |> Option.iter dispose
            hdrOutput <- None

            for texture in environmentTextures do
                dispose texture

            environmentTextures.Clear()
            environmentSampler |> Option.iter dispose
            environmentSampler <- None

            for batch in Array.append staticBatches dynamicBatches do
                releaseBatch batch

            carrierMarkerBatch |> Option.iter releaseBatch
            carrierMarkerBatch <- None
            multisampleColor |> Option.iter dispose
            multisampleColor <- None
            depthTarget |> Option.iter dispose
            depthTarget <- None

            // The HUD pipeline is owned by the cache, so it is not released here.
            hudQuadIndexBuffer |> Option.iter dispose
            hudQuadVertexBuffer |> Option.iter dispose

            if not (GpuPointer.isMissing hudBindGroup) then
                gpu |> Option.iter (fun ctx -> ctx.Api.BindGroupRelease(hudBindGroup))

            hudTexture |> Option.iter dispose
            hudSampler |> Option.iter dispose

            gpu
            |> Option.iter (fun ctx ->
                if not (GpuPointer.isMissing hudPipelineLayout) then
                    ctx.Api.PipelineLayoutRelease(hudPipelineLayout)

                if not (GpuPointer.isMissing hudLayout) then
                    ctx.Api.BindGroupLayoutRelease(hudLayout)

                if not (GpuPointer.isMissing hudShader) then
                    ctx.Api.ShaderModuleRelease(hudShader)

                if not (GpuPointer.isMissing cameraBindGroup) then
                    ctx.Api.BindGroupRelease(cameraBindGroup))

            shadows |> Option.iter dispose
            shadows <- None
            grassDetail |> Option.iter dispose
            grassDetail <- None
            skyBackground |> Option.iter dispose
            skyBackground <- None

            characterAlbedo |> Option.iter dispose
            characterDetail |> Option.iter dispose
            characterSampler |> Option.iter dispose

            for texture in surfaceTextures do
                dispose texture

            surfaceTextures.Clear()
            surfaceSampler |> Option.iter dispose
            cameraBuffer |> Option.iter dispose
            jointBuffer |> Option.iter dispose

            gpu
            |> Option.iter (fun ctx ->
                if not (GpuPointer.isMissing scenePipelineLayout) then
                    ctx.Api.PipelineLayoutRelease(scenePipelineLayout)

                if not (GpuPointer.isMissing sceneLayout) then
                    ctx.Api.BindGroupLayoutRelease(sceneLayout)

                if not (GpuPointer.isMissing shader) then
                    ctx.Api.ShaderModuleRelease(shader))

            // Cached pipelines reference the layouts and shaders above, so they go once every owner has released its own handles.
            pipelines |> Option.iter dispose
            pipelines <- None
            gpu |> Option.iter dispose
            gpu <- None

            if window <> 0un then
                X11ChildWindow.destroy display window

            if display <> 0n then
                X11ChildWindow.closeDisplay display

    let initialize () =
        if not (OperatingSystem.IsLinux()) then
            invalidOp "The WebGPU stadium host currently requires the Linux X11/XWayland backend."

        if not (File.Exists assetPath) then
            invalidOp $"The stadium scene asset was not found: {assetPath}"

        display <- X11ChildWindow.openDisplay ()

        if display = 0n then
            invalidOp "XOpenDisplay failed while creating the WebGPU stadium surface."

        window <- X11ChildWindow.createChild display parentWindow initialBounds.Width initialBounds.Height

        if window = 0un then
            invalidOp "XCreateSimpleWindow failed for the WebGPU stadium surface."

        X11ChildWindow.mapRaised display window
        X11ChildWindow.flush display
        ViewerDiagnostics.publishNativeWindow (uint64 window)

        let ctx = unwrap (GpuContext.Create(display, window))
        gpu <- Some ctx

        pipelines <-
            Some(
                new PipelineCache<RenderPipelineKey, nativeptr<RenderPipeline>>(fun pipeline ->
                    ctx.Api.RenderPipelineRelease(pipeline))
            )

        configureSurface (uint32 (max 1 initialBounds.Width)) (uint32 (max 1 initialBounds.Height))
        createShaderAndPipeline ()
        createHudShaderAndPipeline ()

        staticBatches <-
            scene.StaticBatches
            |> Array.map (fun batch -> batch.MaterialName, batch.Vertices, batch.Indices)
            |> createGpuBatches

        dynamicBatches <-
            scene.DynamicBatches
            |> Array.map (fun batch -> batch.MaterialName, batch.Vertices, batch.Indices)
            |> createGpuBatches

        let players =
            character.Batches
            |> Array.map (fun batch -> batch.MaterialName, batch.Vertices, batch.Indices)
            |> createGpuBatches
            |> Array.map (fun batch -> { batch with InstanceCount = 22u })

        dynamicBatches <- Array.append dynamicBatches players

        carrierMarkerBatch <- createCarrierMarkerBatch ()

        updateWindow initialBounds

        Diagnostics.Trace.WriteLine(
            $"WebGPU stadium ready: X11 child 0x{uint64 window:X}, {multisampleCount}x MSAA, {staticBatches.Length} material batches, {dynamicBatches.Length} live batches."
        )

        Diagnostics.Trace.WriteLine($"Viewer startup ready: {startupClock.Elapsed.TotalMilliseconds:F1} ms")

    do
        try
            initialize ()
        with _ ->
            releaseResources ()
            reraise ()

    member _.NativeWindow = window

    member _.CharacterRig = character.Rig
    member _.HudUploadBytes = hudUploadBytes

    // Diagnostics can time completed work without making production rendering wait for the GPU every frame.
    member _.WaitForGpu() =
        if not disposed then
            (context ()).Poll(true)

    member _.Render
        (bounds: PixelRect, frame: RenderFrame, palettes: Matrix4x4[], cutouts: PixelRect array, stats: MatchHudStats)
        =
        let mutable presented = false
        let ctx = context ()
        let api = ctx.Api

        if not disposed && bounds.Width > 0 && bounds.Height > 0 then
            updateWindow bounds
            updateWindowShape bounds cutouts
            uploadScene (buildScene frame palettes)

            let hud = retainedHud.Value

            for region in hud.Update(frame, stats, performance) do
                uploadHudRegion hud region

            let mutable current = SurfaceTexture()
            api.SurfaceGetCurrentTexture(ctx.Surface, &current)

            if
                current.Status = SurfaceGetCurrentTextureStatus.Success
                && NativePtr.toNativeInt current.Texture <> 0n
            then
                let mutable colorView = NativePtr.nullPtr<TextureView>
                let mutable encoder = NativePtr.nullPtr<CommandEncoder>
                let mutable pass = NativePtr.nullPtr<RenderPassEncoder>
                let mutable commands = NativePtr.nullPtr<CommandBuffer>

                try
                    colorView <- api.TextureCreateView(current.Texture, NativePtr.nullPtr<TextureViewDescriptor>)

                    encoder <- api.DeviceCreateCommandEncoder(ctx.Device, NativePtr.nullPtr<CommandEncoderDescriptor>)

                    let draw (batch: GpuDrawBatch) =
                        api.RenderPassEncoderSetVertexBuffer(pass, 0u, batch.VertexBuffer.Handle, 0UL, UInt64.MaxValue)

                        api.RenderPassEncoderSetIndexBuffer(
                            pass,
                            batch.IndexBuffer.Handle,
                            IndexFormat.Uint32,
                            0UL,
                            UInt64.MaxValue
                        )

                        api.RenderPassEncoderDrawIndexed(pass, batch.IndexCount, batch.InstanceCount, 0u, 0, 0u)

                    if not staticShadowsReady then
                        pass <- shadows.Value.Begin(encoder, cameraBindGroup, true)

                        for batch in staticBatches do
                            draw batch

                        api.RenderPassEncoderEnd(pass)
                        api.RenderPassEncoderRelease(pass)
                        pass <- NativePtr.nullPtr<RenderPassEncoder>

                    shadows.Value.CopyStatic(encoder)
                    pass <- shadows.Value.Begin(encoder, cameraBindGroup, false)

                    for batch in dynamicBatches do
                        draw batch

                    // Selection rings and HUD markers are presentation annotations, not physical occluders.
                    api.RenderPassEncoderEnd(pass)
                    api.RenderPassEncoderRelease(pass)
                    pass <- NativePtr.nullPtr<RenderPassEncoder>

                    let mutable colorAttachment = RenderPassColorAttachment()
                    colorAttachment.View <- multisampleColor.Value.View
                    colorAttachment.ResolveTarget <- hdrOutput.Value.SceneView
                    colorAttachment.LoadOp <- LoadOp.Clear
                    colorAttachment.StoreOp <- StoreOp.Discard
                    let mutable clear = Silk.NET.WebGPU.Color()
                    clear.R <- 0.015
                    clear.G <- 0.045
                    clear.B <- 0.065
                    clear.A <- 1.0
                    colorAttachment.ClearValue <- clear
                    let colorPointer = NativePtr.stackalloc<RenderPassColorAttachment> 1
                    NativePtr.write colorPointer colorAttachment

                    let mutable depthAttachment = RenderPassDepthStencilAttachment()
                    depthAttachment.View <- depthTarget.Value.View
                    depthAttachment.DepthLoadOp <- LoadOp.Clear
                    depthAttachment.DepthStoreOp <- StoreOp.Store
                    depthAttachment.DepthClearValue <- 1.0f
                    let depthPointer = NativePtr.stackalloc<RenderPassDepthStencilAttachment> 1
                    NativePtr.write depthPointer depthAttachment

                    let mutable passDescriptor = RenderPassDescriptor()
                    passDescriptor.ColorAttachmentCount <- unativeint 1
                    passDescriptor.ColorAttachments <- colorPointer
                    passDescriptor.DepthStencilAttachment <- depthPointer
                    pass <- api.CommandEncoderBeginRenderPass(encoder, &passDescriptor)
                    api.RenderPassEncoderSetPipeline(pass, scenePipeline)

                    api.RenderPassEncoderSetBindGroup(
                        pass,
                        1u,
                        shadows.Value.ReceiverGroup,
                        unativeint 0,
                        NativePtr.nullPtr<uint32>
                    )

                    api.RenderPassEncoderSetBindGroup(
                        pass,
                        0u,
                        cameraBindGroup,
                        unativeint 0,
                        NativePtr.nullPtr<uint32>
                    )

                    for batch in staticBatches do
                        draw batch

                    for batch in dynamicBatches do
                        draw batch

                    skyBackground.Value.Encode(pass)
                    api.RenderPassEncoderSetPipeline(pass, scenePipeline)

                    if showGrassDetail then
                        grassDetail.Value.Encode(pass)
                        api.RenderPassEncoderSetPipeline(pass, scenePipeline)

                    carrierMarkerBatch |> Option.iter draw

                    api.RenderPassEncoderEnd(pass)
                    api.RenderPassEncoderRelease(pass)
                    pass <- NativePtr.nullPtr<RenderPassEncoder>
                    hdrOutput.Value.EncodeBloom(encoder)
                    colorAttachment.View <- colorView
                    colorAttachment.ResolveTarget <- NativePtr.nullPtr<TextureView>
                    colorAttachment.StoreOp <- StoreOp.Store
                    NativePtr.write colorPointer colorAttachment
                    passDescriptor.DepthStencilAttachment <- NativePtr.nullPtr<RenderPassDepthStencilAttachment>
                    pass <- api.CommandEncoderBeginRenderPass(encoder, &passDescriptor)
                    hdrOutput.Value.Draw(pass)

                    api.RenderPassEncoderSetPipeline(pass, hudPipeline)
                    api.RenderPassEncoderSetBindGroup(pass, 0u, hudBindGroup, unativeint 0, NativePtr.nullPtr<uint32>)

                    api.RenderPassEncoderSetVertexBuffer(
                        pass,
                        0u,
                        hudQuadVertexBuffer.Value.Handle,
                        0UL,
                        UInt64.MaxValue
                    )

                    api.RenderPassEncoderSetIndexBuffer(
                        pass,
                        hudQuadIndexBuffer.Value.Handle,
                        IndexFormat.Uint32,
                        0UL,
                        UInt64.MaxValue
                    )

                    api.RenderPassEncoderDrawIndexed(pass, 6u, 1u, 0u, 0, 0u)

                    api.RenderPassEncoderEnd(pass)

                    commands <- api.CommandEncoderFinish(encoder, NativePtr.nullPtr<CommandBufferDescriptor>)

                    let commandPointer = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
                    NativePtr.write commandPointer commands
                    api.QueueSubmit(ctx.Queue, unativeint 1, commandPointer)
                    staticShadowsReady <- true
                    ctx.Poll(false)
                    api.SurfacePresent(ctx.Surface)
                    presented <- true
                    let before = lastPresentation
                    recordPresentation ()

                    (match before with
                     | Some previous ->
                         let interval = performanceClock.Elapsed.TotalMilliseconds - previous

                         ViewerDiagnostics.record
                             { FrameIndex = int64 frameCount
                               Width = int ctx.Width
                               Height = int ctx.Height
                               Quality = "original"
                               FrameIntervalMs = interval
                               CpuFrameMs = interval
                               GpuTimingAvailable = false
                               GpuTotalMs = None
                               Passes = [||] }
                     | None -> ())

                    frameCount <- frameCount + 1

                    if frameCount = 1 then
                        Diagnostics.Trace.WriteLine(
                            $"Presented the first live FootballEngine stadium frame after {startupClock.Elapsed.TotalMilliseconds:F1} ms."
                        )
                finally
                    if NativePtr.toNativeInt commands <> 0n then
                        api.CommandBufferRelease(commands)

                    if NativePtr.toNativeInt pass <> 0n then
                        api.RenderPassEncoderRelease(pass)

                    if NativePtr.toNativeInt encoder <> 0n then
                        api.CommandEncoderRelease(encoder)

                    if NativePtr.toNativeInt colorView <> 0n then
                        api.TextureViewRelease(colorView)

                    api.TextureRelease(current.Texture)
            elif
                current.Status = SurfaceGetCurrentTextureStatus.Outdated
                || current.Status = SurfaceGetCurrentTextureStatus.Lost
            then
                configureSurface ctx.Width ctx.Height
            else
                Diagnostics.Trace.WriteLine($"WebGPU stadium surface acquisition failed: {current.Status}")

                if NativePtr.toNativeInt current.Texture <> 0n then
                    api.TextureRelease(current.Texture)

        presented

    member _.SetCamera(mode: ViewerCamera) = cameraMode <- mode

    interface IDisposable with
        member _.Dispose() = releaseResources ()

module MatchPitchNative =
    let private toPixelBounds (control: Control) =
        match TopLevel.GetTopLevel(control) with
        | null -> PixelRect(0, 0, 0, 0)
        | topLevel ->
            let scale = topLevel.RenderScaling

            let origin = control.TranslatePoint(Point(0.0, 0.0), topLevel)

            if not origin.HasValue then
                PixelRect(0, 0, 0, 0)
            else
                PixelRect(
                    int (Math.Round(origin.Value.X * scale)),
                    int (Math.Round(origin.Value.Y * scale)),
                    max 1 (int (Math.Round(control.Bounds.Width * scale))),
                    max 1 (int (Math.Round(control.Bounds.Height * scale)))
                )

    let rendererFactory (control: Control) =
        match TopLevel.GetTopLevel(control) with
        | null -> None
        | topLevel ->
            match topLevel.TryGetPlatformHandle() with
            | null -> None
            | handle when handle.HandleDescriptor <> "XID" ->
                invalidOp
                    $"WebGPU stadium rendering requires an X11/XWayland XID; Avalonia returned '{handle.HandleDescriptor}'."
            | handle ->
                let parent = unativeint (uint64 handle.Handle)

                let assetPath =
                    Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "stadium.glb")

                Some(fun bounds -> new WebGpuStadiumRenderer(parent, bounds, assetPath))

    let overlayCutouts (control: Control) =
        match TopLevel.GetTopLevel(control) with
        | null -> [||]
        | topLevel when topLevel.ClientSize.Width < 1300.0 -> [||]
        | topLevel ->
            let scale = topLevel.RenderScaling
            let bounds = toPixelBounds control
            let pixels value = int (Math.Round(value * scale))
            let margin = pixels MatchPitchOverlay.margin
            let managerWidth = pixels MatchPitchOverlay.managerWidth

            let managerHeight =
                max 0 (min (pixels MatchPitchOverlay.managerHeight) (bounds.Height - margin))

            let intelligenceWidth = pixels MatchPitchOverlay.intelligenceWidth

            let intelligenceHeight =
                max 0 (min (pixels MatchPitchOverlay.intelligenceHeight) (bounds.Height - margin))

            if bounds.Width < managerWidth + intelligenceWidth + margin * 2 + pixels 220.0 then
                [||]
            else
                [| PixelRect(margin, margin, managerWidth, managerHeight)
                   PixelRect(bounds.Width - margin - intelligenceWidth, margin, intelligenceWidth, intelligenceHeight) |]

    let bounds control = toPixelBounds control
