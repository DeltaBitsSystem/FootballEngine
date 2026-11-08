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

module private Xlib =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type Rectangle =
        { X: int16
          Y: int16
          Width: uint16
          Height: uint16 }

    [<DllImport("libX11.so.6", EntryPoint = "XOpenDisplay", CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint openDisplay(nativeint name)

    [<DllImport("libX11.so.6", EntryPoint = "XCloseDisplay", CallingConvention = CallingConvention.Cdecl)>]
    extern int closeDisplay(nativeint display)

    [<DllImport("libX11.so.6", EntryPoint = "XCreateSimpleWindow", CallingConvention = CallingConvention.Cdecl)>]
    extern unativeint createSimpleWindow(
        nativeint display,
        unativeint parent,
        int x,
        int y,
        uint32 width,
        uint32 height,
        uint32 borderWidth,
        unativeint border,
        unativeint background
    )

    [<DllImport("libX11.so.6", EntryPoint = "XMapRaised", CallingConvention = CallingConvention.Cdecl)>]
    extern int mapRaised(nativeint display, unativeint window)

    [<DllImport("libX11.so.6", EntryPoint = "XMoveResizeWindow", CallingConvention = CallingConvention.Cdecl)>]
    extern int moveResizeWindow(nativeint display, unativeint window, int x, int y, uint32 width, uint32 height)

    [<DllImport("libX11.so.6", EntryPoint = "XDestroyWindow", CallingConvention = CallingConvention.Cdecl)>]
    extern int destroyWindow(nativeint display, unativeint window)

    [<DllImport("libX11.so.6", EntryPoint = "XFlush", CallingConvention = CallingConvention.Cdecl)>]
    extern int flush(nativeint display)

    [<DllImport("libXext.so.6", EntryPoint = "XShapeCombineRectangles", CallingConvention = CallingConvention.Cdecl)>]
    extern void shapeCombineRectangles(
        nativeint display,
        unativeint window,
        int destinationKind,
        int xOffset,
        int yOffset,
        nativeint rectangles,
        int rectangleCount,
        int operation,
        int ordering
    )

module MatchPitchOverlay =
    let margin = 14.0
    let managerWidth = 250.0
    let managerHeight = 380.0
    let intelligenceWidth = 285.0
    let intelligenceHeight = 390.0

module private WgpuNative =
    [<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
    type DevicePoll = delegate of nativeint * byte * nativeint -> byte

    let nativeLibraryPath () =
        let fileName =
            if OperatingSystem.IsWindows() then "wgpu_native.dll"
            elif OperatingSystem.IsMacOS() then "libwgpu_native.dylib"
            else "libwgpu_native.so"

        let path =
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName)

        if not (File.Exists path) then
            invalidOp $"The WebGPU native runtime library was not packaged at {path}."

        path

module private SharedWebGpu =
    let context = new DefaultNativeContext(WgpuNative.nativeLibraryPath ())
    let api = new WebGPU(context)

    let devicePoll =
        context.Library.LoadFunction("wgpuDevicePoll")
        |> Marshal.GetDelegateForFunctionPointer<WgpuNative.DevicePoll>

type private GpuDrawBatch =
    { MaterialName: string
      VertexBuffer: nativeptr<Buffer>
      IndexBuffer: nativeptr<Buffer>
      IndexCount: uint32
      InstanceCount: uint32 }

module private BufferWriter =
    let write<'T when 'T: unmanaged>
        (api: WebGPU)
        (queue: nativeptr<Queue>)
        (buffer: nativeptr<Buffer>)
        (data: 'T array)
        =
        use dataPointer = fixed data

        api.QueueWriteBuffer(queue, buffer, 0UL, NativePtr.toVoidPtr dataPointer, unativeint (data.Length * sizeof<'T>))

    let create<'T when 'T: unmanaged>
        (api: WebGPU)
        (device: nativeptr<Device>)
        (queue: nativeptr<Queue>)
        (label: string)
        usage
        (data: 'T array)
        =
        let size = uint64 (max (sizeof<'T>) (data.Length * sizeof<'T>))
        let mutable descriptor = BufferDescriptor()
        descriptor.Usage <- usage
        descriptor.Size <- size
        let buffer = api.DeviceCreateBuffer(device, &descriptor)

        if NativePtr.toNativeInt buffer = 0n then
            invalidOp $"WebGPU failed to create {label}."

        try
            if data.Length > 0 then
                write api queue buffer data

            buffer
        with _ ->
            api.BufferRelease(buffer)
            reraise ()

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
    let rootTransformCount = 25
    let carrierMarkerRoot = 24u
    let mutable cameraMode = defaultArg camera ViewerCamera.Broadcast
    let api = SharedWebGpu.api
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

    let devicePoll = SharedWebGpu.devicePoll
    let mutable display = 0n
    let mutable window = 0un
    let mutable instance = NativePtr.nullPtr<Instance>
    let mutable surface = NativePtr.nullPtr<Surface>
    let mutable adapter = NativePtr.nullPtr<Adapter>
    let mutable device = NativePtr.nullPtr<Device>
    let mutable queue = NativePtr.nullPtr<Queue>
    let mutable shader = NativePtr.nullPtr<ShaderModule>
    let mutable bindGroupLayout = NativePtr.nullPtr<BindGroupLayout>
    let mutable pipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable cameraBuffer = NativePtr.nullPtr<Buffer>
    let mutable jointBuffer = NativePtr.nullPtr<Buffer>
    let mutable cameraBindGroup = NativePtr.nullPtr<BindGroup>
    let mutable shadows: DirectionalShadowPass option = None
    let mutable grassDetail: ProceduralScenePass option = None
    let mutable skyBackground: ProceduralScenePass option = None
    let mutable showGrassDetail = false
    let mutable staticShadowsReady = false
    let shadowSettings = DirectionalShadow.stadium

    let shadowMatrixData =
        DirectionalShadow.matrixData shadowSettings lighting.SunDirection

    let cameraUniformData =
        Array.zeroCreate<float32> (16 + rootTransformCount * 16 + 12 + 16 + 16)

    let mutable characterAlbedo = NativePtr.nullPtr<Texture>
    let mutable characterAlbedoView = NativePtr.nullPtr<TextureView>
    let mutable characterDetail = NativePtr.nullPtr<Texture>
    let mutable characterDetailView = NativePtr.nullPtr<TextureView>
    let mutable characterSampler = NativePtr.nullPtr<Sampler>
    let surfaceTextures = Array.create 3 (NativePtr.nullPtr<Texture>)
    let surfaceViews = Array.create 3 (NativePtr.nullPtr<TextureView>)
    let mutable surfaceSampler = NativePtr.nullPtr<Sampler>
    let environmentTextures = Array.create 3 (NativePtr.nullPtr<Texture>)
    let environmentViews = Array.create 3 (NativePtr.nullPtr<TextureView>)
    let mutable environmentSampler = NativePtr.nullPtr<Sampler>
    let mutable hdrOutput: HdrOutputPass option = None
    let quality =
        match ViewerQuality.ofTier (ViewerQualitySelection.current ()) |> ViewerQuality.validated with
        | Ok selected -> selected
        // Presets are validated by tests; an invalid one must not cost the player the match view.
        | Error _ -> ViewerQuality.ultra

    let environment = environmentPackage.Environment

    let surfacePack =
        match timedLoad "surfaces" (fun () -> SurfaceTexturePack.load (Path.Combine(Path.GetDirectoryName assetPath, "Surfaces"))) with
        | Ok pack -> pack
        | Error message -> invalidOp $"Cannot load stadium surface materials: {message}"

    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable hudShader = NativePtr.nullPtr<ShaderModule>
    let mutable hudBindGroupLayout = NativePtr.nullPtr<BindGroupLayout>
    let mutable hudPipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable hudBindGroup = NativePtr.nullPtr<BindGroup>
    let mutable hudPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable hudTexture = NativePtr.nullPtr<Texture>
    let mutable retainedHud: RetainedMatchHud option = None
    let mutable hudUploadBytes = 0L
    let mutable hudTextureView = NativePtr.nullPtr<TextureView>
    let mutable hudSampler = NativePtr.nullPtr<Sampler>
    let mutable hudQuadVertexBuffer = NativePtr.nullPtr<Buffer>
    let mutable hudQuadIndexBuffer = NativePtr.nullPtr<Buffer>
    let mutable multisampleColorTexture = NativePtr.nullPtr<Texture>
    let mutable multisampleColorView = NativePtr.nullPtr<TextureView>
    let mutable depthTexture = NativePtr.nullPtr<Texture>
    let mutable depthView = NativePtr.nullPtr<TextureView>
    let mutable targetFormat = TextureFormat.Undefined
    let mutable targetWidth = 0u
    let mutable targetHeight = 0u
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

    let nullVoid = NativePtr.toVoidPtr (NativePtr.nullPtr<byte>)

    let errorCallback =
        ErrorCallback(fun errorType message _ ->
            let text = Marshal.PtrToStringUTF8(NativePtr.toNativeInt message)
            Diagnostics.Trace.WriteLine($"WebGPU uncaptured error ({errorType}): {text}"))

    let errorCallbackPointer = PfnErrorCallback.From(errorCallback)

    let checkPointer label pointer =
        if NativePtr.toNativeInt pointer = 0n then
            invalidOp $"WebGPU failed to create {label}."

    let requestAdapter () =
        let mutable requested = NativePtr.nullPtr<Adapter>

        let callback =
            RequestAdapterCallback(fun status value _ _ ->
                if status = RequestAdapterStatus.Success then
                    requested <- value)

        use callbackPointer = PfnRequestAdapterCallback.From(callback)
        let mutable options = RequestAdapterOptions()
        options.CompatibleSurface <- surface
        options.PowerPreference <- PowerPreference.HighPerformance
        api.InstanceRequestAdapter(instance, &options, callbackPointer, nullVoid)
        checkPointer "a surface-compatible adapter" requested
        requested

    let requestDevice () =
        let mutable requested = NativePtr.nullPtr<Device>

        let callback =
            RequestDeviceCallback(fun status value _ _ ->
                if status = RequestDeviceStatus.Success then
                    requested <- value)

        use callbackPointer = PfnRequestDeviceCallback.From(callback)
        api.AdapterRequestDevice(adapter, NativePtr.nullPtr<DeviceDescriptor>, callbackPointer, nullVoid)
        checkPointer "a device" requested
        requested

    let pinAndWrite buffer data =
        BufferWriter.write api queue buffer data

    let createBuffer label usage data =
        BufferWriter.create api device queue label usage data

    let createGpuBatch
        (materialName: string)
        (vertices: GpuVertex array)
        (indices: uint32 array)
        : GpuDrawBatch option =
        if indices.Length = 0 then
            None
        else
            let vertexBuffer =
                createBuffer $"{materialName} vertices" (BufferUsage.Vertex ||| BufferUsage.CopyDst) vertices

            try
                let indexBuffer =
                    createBuffer $"{materialName} indices" (BufferUsage.Index ||| BufferUsage.CopyDst) indices

                Some
                    { MaterialName = materialName
                      VertexBuffer = vertexBuffer
                      IndexBuffer = indexBuffer
                      IndexCount = uint32 indices.Length
                      InstanceCount = 1u }
            with _ ->
                api.BufferRelease(vertexBuffer)
                reraise ()

    let mutable staticBatches: GpuDrawBatch array = [||]
    let mutable dynamicBatches: GpuDrawBatch array = [||]
    let mutable carrierMarkerBatch: GpuDrawBatch option = None

    let releaseBatch batch =
        api.BufferRelease(batch.VertexBuffer)
        api.BufferRelease(batch.IndexBuffer)

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
                  RootIndex = carrierMarkerRoot
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

    let mutable surfaceConfigured = false

    let configureSurface (width: uint32) (height: uint32) =
        let mutable capabilities = SurfaceCapabilities()
        api.SurfaceGetCapabilities(surface, adapter, &capabilities)

        let formats =
            Array.init (int capabilities.FormatCount) (fun index ->
                NativePtr.read (NativePtr.add capabilities.Formats index))

        let alphaModes =
            Array.init (int capabilities.AlphaModeCount) (fun index ->
                NativePtr.read (NativePtr.add capabilities.AlphaModes index))

        let presentModes =
            Array.init (int capabilities.PresentModeCount) (fun index ->
                NativePtr.read (NativePtr.add capabilities.PresentModes index))

        // Hardware sRGB encoding keeps both scene output and premultiplied HUD blending in linear light.
        let outputFormat =
            [ TextureFormat.Bgra8UnormSrgb; TextureFormat.Rgba8UnormSrgb ]
            |> List.tryFind (fun format -> Array.contains format formats)

        let previousFormat = targetFormat

        let alphaMode =
            if Array.contains CompositeAlphaMode.Opaque alphaModes then
                CompositeAlphaMode.Opaque
            else
                alphaModes
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported alpha modes.")

        let presentMode =
            if Array.contains PresentMode.Mailbox presentModes then
                PresentMode.Mailbox
            // FIFO remains a supported presentation mode, not a legacy renderer or quality fallback.
            else if Array.contains PresentMode.Fifo presentModes then
                PresentMode.Fifo
            else
                invalidOp "The WebGPU renderer requires tear-free mailbox or FIFO presentation."

        api.SurfaceCapabilitiesFreeMembers(capabilities)

        targetFormat <-
            outputFormat
            |> Option.defaultWith (fun () -> invalidOp "The HDR renderer requires an sRGB presentation surface.")

        if previousFormat <> TextureFormat.Undefined && previousFormat <> targetFormat then
            invalidOp
                "The presentation format changed; recreate the HDR renderer instead of reusing incompatible pipelines."

        let mutable configuration = SurfaceConfiguration()
        configuration.Device <- device
        configuration.Format <- targetFormat
        configuration.Usage <- TextureUsage.RenderAttachment
        configuration.AlphaMode <- alphaMode
        configuration.Width <- width
        configuration.Height <- height
        configuration.PresentMode <- presentMode
        Diagnostics.Trace.WriteLine($"WebGPU presentation: {presentMode}, {width}x{height}.")
        api.SurfaceConfigure(surface, &configuration)
        surfaceConfigured <- true
        targetWidth <- width
        targetHeight <- height

        let output =
            match hdrOutput with
            | Some output -> output
            | None ->
                let output =
                    new HdrOutputPass(api, device, queue, targetFormat, lighting, quality, BroadcastGrade.standard)

                hdrOutput <- Some output
                output

        if NativePtr.toNativeInt multisampleColorView <> 0n then
            api.TextureViewRelease(multisampleColorView)
            multisampleColorView <- NativePtr.nullPtr<TextureView>

        if NativePtr.toNativeInt multisampleColorTexture <> 0n then
            api.TextureDestroy(multisampleColorTexture)
            api.TextureRelease(multisampleColorTexture)
            multisampleColorTexture <- NativePtr.nullPtr<Texture>

        if NativePtr.toNativeInt depthView <> 0n then
            api.TextureViewRelease(depthView)
            depthView <- NativePtr.nullPtr<TextureView>

        if NativePtr.toNativeInt depthTexture <> 0n then
            api.TextureDestroy(depthTexture)
            api.TextureRelease(depthTexture)
            depthTexture <- NativePtr.nullPtr<Texture>

        let mutable colorDescriptor = TextureDescriptor()
        colorDescriptor.Usage <- TextureUsage.RenderAttachment
        colorDescriptor.Dimension <- TextureDimension.Dimension2D
        colorDescriptor.Size <- Extent3D(width, height, 1u)
        colorDescriptor.Format <- TextureFormat.Rgba16float
        colorDescriptor.MipLevelCount <- 1u
        colorDescriptor.SampleCount <- multisampleCount
        multisampleColorTexture <- api.DeviceCreateTexture(device, &colorDescriptor)
        checkPointer $"a {multisampleCount}x multisampled color texture" multisampleColorTexture
        multisampleColorView <- api.TextureCreateView(multisampleColorTexture, NativePtr.nullPtr<TextureViewDescriptor>)
        checkPointer $"a {multisampleCount}x multisampled color texture view" multisampleColorView

        let mutable textureDescriptor = TextureDescriptor()
        // The post chain reads scene depth for ambient occlusion, so it must be bindable and kept after the pass.
        textureDescriptor.Usage <- TextureUsage.RenderAttachment ||| TextureUsage.TextureBinding
        textureDescriptor.Dimension <- TextureDimension.Dimension2D
        textureDescriptor.Size <- Extent3D(width, height, 1u)
        textureDescriptor.Format <- TextureFormat.Depth24Plus
        textureDescriptor.MipLevelCount <- 1u
        textureDescriptor.SampleCount <- multisampleCount
        depthTexture <- api.DeviceCreateTexture(device, &textureDescriptor)
        checkPointer "a depth texture" depthTexture
        depthView <- api.TextureCreateView(depthTexture, NativePtr.nullPtr<TextureViewDescriptor>)
        checkPointer "a depth texture view" depthView
        output.Resize(width, height, depthView)

    let uploadCharacterTexture (texture: nativeptr<Texture>) (data: CharacterTextureData) =
        for level = 0 to data.Levels.Length - 1 do
            let mip = data.Levels[level]
            let mutable destination = ImageCopyTexture()
            destination.Texture <- texture
            destination.MipLevel <- uint32 level
            destination.Aspect <- TextureAspect.All
            let mutable layout = TextureDataLayout()
            layout.BytesPerRow <- uint32 (mip.Width * 4)
            layout.RowsPerImage <- uint32 mip.Height
            let extent = Extent3D(uint32 mip.Width, uint32 mip.Height, 1u)
            use pointer = fixed mip.Pixels

            api.QueueWriteTexture(
                queue,
                &destination,
                NativePtr.toVoidPtr pointer,
                unativeint mip.Pixels.Length,
                &layout,
                &extent
            )

    let createShaderAndPipeline () =
        for index, pixels, width, height, layers, format, bytesPerPixel in
            [ 0,
              environment.Environment,
              environment.Width,
              environment.Height,
              environment.Layers,
              TextureFormat.Rgba16float,
              8
              1, environment.Brdf, environment.LutSize, environment.LutSize, 1, TextureFormat.RG16float, 4
              2, environment.Sky, environment.SkyWidth, environment.SkyHeight, 1, TextureFormat.Rgba16float, 8 ] do
            let mutable descriptor = TextureDescriptor()
            descriptor.Dimension <- TextureDimension.Dimension2D
            descriptor.Size <- Extent3D(uint32 width, uint32 height, uint32 layers)
            descriptor.Format <- format
            descriptor.MipLevelCount <- 1u
            descriptor.SampleCount <- 1u
            descriptor.Usage <- TextureUsage.TextureBinding ||| TextureUsage.CopyDst
            environmentTextures[index] <- api.DeviceCreateTexture(device, &descriptor)
            checkPointer "a floating-point IBL texture" environmentTextures[index]

            environmentViews[index] <-
                api.TextureCreateView(environmentTextures[index], NativePtr.nullPtr<TextureViewDescriptor>)

            checkPointer "a floating-point IBL view" environmentViews[index]
            let mutable destination = ImageCopyTexture()
            destination.Texture <- environmentTextures[index]
            destination.Aspect <- TextureAspect.All
            let mutable layout = TextureDataLayout()
            layout.BytesPerRow <- uint32 (width * bytesPerPixel)
            layout.RowsPerImage <- uint32 height
            let extent = Extent3D(uint32 width, uint32 height, uint32 layers)
            use pointer = fixed pixels

            api.QueueWriteTexture(
                queue,
                &destination,
                NativePtr.toVoidPtr pointer,
                unativeint pixels.Length,
                &layout,
                &extent
            )

        let mutable environmentSamplerDescriptor = SamplerDescriptor()
        environmentSamplerDescriptor.AddressModeU <- AddressMode.Repeat
        environmentSamplerDescriptor.AddressModeV <- AddressMode.ClampToEdge
        environmentSamplerDescriptor.AddressModeW <- AddressMode.ClampToEdge
        environmentSamplerDescriptor.MinFilter <- FilterMode.Linear
        environmentSamplerDescriptor.MagFilter <- FilterMode.Linear
        environmentSamplerDescriptor.MipmapFilter <- MipmapFilterMode.Linear
        environmentSamplerDescriptor.MaxAnisotropy <- 1us
        environmentSamplerDescriptor.LodMaxClamp <- 1.0f
        environmentSampler <- api.DeviceCreateSampler(device, &environmentSamplerDescriptor)
        checkPointer "the HDR environment sampler" environmentSampler

        for index, pixels in [ 0, surfacePack.Albedo; 1, surfacePack.Normal; 2, surfacePack.Arm ] do
            let mutable descriptor = TextureDescriptor()
            descriptor.Usage <- TextureUsage.TextureBinding ||| TextureUsage.CopyDst
            descriptor.Dimension <- TextureDimension.Dimension2D
            descriptor.Size <- Extent3D(uint32 surfacePack.Size, uint32 surfacePack.Size, uint32 surfacePack.Layers)
            descriptor.MipLevelCount <- uint32 surfacePack.Levels
            descriptor.SampleCount <- 1u

            descriptor.Format <-
                if index = 0 then
                    TextureFormat.Rgba8UnormSrgb
                else
                    TextureFormat.Rgba8Unorm

            surfaceTextures[index] <- api.DeviceCreateTexture(device, &descriptor)
            checkPointer "a surface material array" surfaceTextures[index]

            surfaceViews[index] <-
                api.TextureCreateView(surfaceTextures[index], NativePtr.nullPtr<TextureViewDescriptor>)

            checkPointer "a surface material array view" surfaceViews[index]
            let mutable offset = 0

            for level = 0 to surfacePack.Levels - 1 do
                let width = surfacePack.Size >>> level
                let bytes = width * width * surfacePack.Layers * 4
                let mutable destination = ImageCopyTexture()
                destination.Texture <- surfaceTextures[index]
                destination.MipLevel <- uint32 level
                destination.Aspect <- TextureAspect.All
                let mutable layout = TextureDataLayout()
                layout.BytesPerRow <- uint32 (width * 4)
                layout.RowsPerImage <- uint32 width
                let extent = Extent3D(uint32 width, uint32 width, uint32 surfacePack.Layers)
                use pointer = fixed pixels

                api.QueueWriteTexture(
                    queue,
                    &destination,
                    NativePtr.toVoidPtr (NativePtr.add pointer offset),
                    unativeint bytes,
                    &layout,
                    &extent
                )

                offset <- offset + bytes

        let mutable surfaceSamplerDescriptor = SamplerDescriptor()
        surfaceSamplerDescriptor.AddressModeU <- AddressMode.Repeat
        surfaceSamplerDescriptor.AddressModeV <- AddressMode.Repeat
        surfaceSamplerDescriptor.AddressModeW <- AddressMode.Repeat
        surfaceSamplerDescriptor.MinFilter <- FilterMode.Linear
        surfaceSamplerDescriptor.MagFilter <- FilterMode.Linear
        surfaceSamplerDescriptor.MipmapFilter <- MipmapFilterMode.Linear
        surfaceSamplerDescriptor.LodMaxClamp <- float32 (surfacePack.Levels - 1)
        surfaceSamplerDescriptor.MaxAnisotropy <- 8us
        surfaceSampler <- api.DeviceCreateSampler(device, &surfaceSamplerDescriptor)
        checkPointer "the surface material sampler" surfaceSampler
        let mutable textureDescriptor = TextureDescriptor()
        textureDescriptor.Usage <- TextureUsage.TextureBinding ||| TextureUsage.CopyDst
        textureDescriptor.Dimension <- TextureDimension.Dimension2D

        textureDescriptor.Size <-
            Extent3D(uint32 character.Albedo.Levels[0].Width, uint32 character.Albedo.Levels[0].Height, 1u)
        // Stop before atlas tiles collapse into one another; mip filtering suppresses distant fabric shimmer.
        textureDescriptor.MipLevelCount <- uint32 character.Albedo.Levels.Length
        textureDescriptor.SampleCount <- 1u
        textureDescriptor.Format <- TextureFormat.Rgba8UnormSrgb
        characterAlbedo <- api.DeviceCreateTexture(device, &textureDescriptor)
        checkPointer "the athlete albedo atlas" characterAlbedo
        characterAlbedoView <- api.TextureCreateView(characterAlbedo, NativePtr.nullPtr<TextureViewDescriptor>)
        checkPointer "the athlete albedo view" characterAlbedoView
        uploadCharacterTexture characterAlbedo character.Albedo
        textureDescriptor.Format <- TextureFormat.Rgba8Unorm
        characterDetail <- api.DeviceCreateTexture(device, &textureDescriptor)
        checkPointer "the athlete normal/roughness atlas" characterDetail
        characterDetailView <- api.TextureCreateView(characterDetail, NativePtr.nullPtr<TextureViewDescriptor>)
        checkPointer "the athlete normal/roughness view" characterDetailView
        uploadCharacterTexture characterDetail character.NormalRoughness
        let mutable samplerDescriptor = SamplerDescriptor()
        samplerDescriptor.AddressModeU <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeV <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeW <- AddressMode.ClampToEdge
        samplerDescriptor.MagFilter <- FilterMode.Linear
        samplerDescriptor.MinFilter <- FilterMode.Linear
        samplerDescriptor.MipmapFilter <- MipmapFilterMode.Linear
        samplerDescriptor.LodMaxClamp <- 8.0f
        samplerDescriptor.MaxAnisotropy <- 1us
        characterSampler <- api.DeviceCreateSampler(device, &samplerDescriptor)
        checkPointer "the athlete texture sampler" characterSampler

        let source =
            """struct Camera {
    viewProjection: mat4x4<f32>,
    rootTransforms: array<mat4x4<f32>, 25>,
    position: vec4<f32>,
    sunAndEnvironment: vec4<f32>,
    sunRadiance: vec4<f32>,
    lightViewProjection: mat4x4<f32>,
    inverseViewProjection: mat4x4<f32>,
}

@group(0) @binding(0) var<uniform> camera: Camera;
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
            |> fun source -> source.Replace("CHARACTER_JOINT_COUNT", $"{character.JointCount}u")
            |> fun source ->
                source
                + File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "EnvironmentLighting.wgsl")
                )
                + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "ShadowLighting.wgsl"))
                + AthleteMaterial.shaderConstants
                + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "AthleteSurface.wgsl"))
                + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "MatchBallSurface.wgsl"))
            |> fun source ->
                source
                + PitchMarkings.shaderConstants
                + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "PitchSurface.wgsl"))
                + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "GrassDetail.wgsl"))
                + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "SkyBackground.wgsl"))

        let sourceBytes = Text.Encoding.UTF8.GetBytes(source + "\u0000")
        use sourcePointer = fixed sourceBytes
        let mutable wgsl = ShaderModuleWGSLDescriptor()
        wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
        wgsl.Code <- sourcePointer
        let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
        NativePtr.write wgslPointer wgsl
        let mutable shaderDescriptor = ShaderModuleDescriptor()
        shaderDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPointer)
        shader <- api.DeviceCreateShaderModule(device, &shaderDescriptor)
        checkPointer "the stadium WGSL shader" shader

        let mutable uniformEntry = BindGroupLayoutEntry()
        uniformEntry.Binding <- 0u
        uniformEntry.Visibility <- ShaderStage.Vertex ||| ShaderStage.Fragment
        uniformEntry.Buffer.Type <- BufferBindingType.Uniform
        let uniformEntryPointer = NativePtr.stackalloc<BindGroupLayoutEntry> 13
        NativePtr.write uniformEntryPointer uniformEntry
        let mutable jointEntry = BindGroupLayoutEntry()
        jointEntry.Binding <- 1u
        jointEntry.Visibility <- ShaderStage.Vertex
        jointEntry.Buffer.Type <- BufferBindingType.ReadOnlyStorage
        NativePtr.write (NativePtr.add uniformEntryPointer 1) jointEntry

        for binding in [ 2u; 3u ] do
            let mutable entry = BindGroupLayoutEntry()
            entry.Binding <- binding
            entry.Visibility <- ShaderStage.Fragment
            entry.Texture.SampleType <- TextureSampleType.Float
            entry.Texture.ViewDimension <- TextureViewDimension.Dimension2D
            entry.Texture.Multisampled <- Silk.NET.Core.Bool32(0u)
            NativePtr.write (NativePtr.add uniformEntryPointer (int binding)) entry

        let mutable samplerEntry = BindGroupLayoutEntry()
        samplerEntry.Binding <- 4u
        samplerEntry.Visibility <- ShaderStage.Fragment
        samplerEntry.Sampler.Type <- SamplerBindingType.Filtering
        NativePtr.write (NativePtr.add uniformEntryPointer 4) samplerEntry

        for binding in [ 5u; 6u; 7u ] do
            let mutable entry = BindGroupLayoutEntry()
            entry.Binding <- binding
            entry.Visibility <- ShaderStage.Fragment
            entry.Texture.SampleType <- TextureSampleType.Float
            entry.Texture.ViewDimension <- TextureViewDimension.Dimension2DArray
            NativePtr.write (NativePtr.add uniformEntryPointer (int binding)) entry

        samplerEntry.Binding <- 8u
        NativePtr.write (NativePtr.add uniformEntryPointer 8) samplerEntry

        for binding, dimension in
            [ 9u, TextureViewDimension.Dimension2DArray
              10u, TextureViewDimension.Dimension2D
              12u, TextureViewDimension.Dimension2D ] do
            let mutable entry = BindGroupLayoutEntry()
            entry.Binding <- binding
            entry.Visibility <- ShaderStage.Fragment
            entry.Texture.SampleType <- TextureSampleType.Float
            entry.Texture.ViewDimension <- dimension
            NativePtr.write (NativePtr.add uniformEntryPointer (int binding)) entry

        samplerEntry.Binding <- 11u
        NativePtr.write (NativePtr.add uniformEntryPointer 11) samplerEntry
        let mutable groupDescriptor = BindGroupLayoutDescriptor()
        groupDescriptor.EntryCount <- unativeint 13
        groupDescriptor.Entries <- uniformEntryPointer
        bindGroupLayout <- api.DeviceCreateBindGroupLayout(device, &groupDescriptor)
        checkPointer "the camera bind-group layout" bindGroupLayout

        let shadowPass =
            new DirectionalShadowPass(api, device, shadowSettings, bindGroupLayout)

        shadows <- Some shadowPass

        let layoutPointer = NativePtr.stackalloc<nativeptr<BindGroupLayout>> 2
        NativePtr.write layoutPointer bindGroupLayout
        let mutable pipelineLayoutDescriptor = PipelineLayoutDescriptor()
        pipelineLayoutDescriptor.BindGroupLayouts <- layoutPointer
        NativePtr.write (NativePtr.add layoutPointer 1) shadowPass.ReceiverLayout
        pipelineLayoutDescriptor.BindGroupLayoutCount <- unativeint 2
        pipelineLayout <- api.DeviceCreatePipelineLayout(device, &pipelineLayoutDescriptor)
        checkPointer "the stadium pipeline layout" pipelineLayout

        cameraBuffer <-
            createBuffer
                "broadcast camera and player transforms"
                (BufferUsage.Uniform ||| BufferUsage.CopyDst)
                cameraUniformData

        jointBuffer <-
            createBuffer "character joint palettes" (BufferUsage.Storage ||| BufferUsage.CopyDst) jointMatrices

        let mutable bindEntry = BindGroupEntry()
        bindEntry.Binding <- 0u
        bindEntry.Buffer <- cameraBuffer
        bindEntry.Offset <- 0UL
        bindEntry.Size <- uint64 (cameraUniformData.Length * sizeof<float32>)
        let bindEntryPointer = NativePtr.stackalloc<BindGroupEntry> 13
        NativePtr.write bindEntryPointer bindEntry
        let mutable jointBindEntry = BindGroupEntry()
        jointBindEntry.Binding <- 1u
        jointBindEntry.Buffer <- jointBuffer
        jointBindEntry.Size <- uint64 (jointMatrices.Length * sizeof<Matrix4x4>)
        NativePtr.write (NativePtr.add bindEntryPointer 1) jointBindEntry

        for binding, view in [ 2u, characterAlbedoView; 3u, characterDetailView ] do
            let mutable entry = BindGroupEntry()
            entry.Binding <- binding
            entry.TextureView <- view
            NativePtr.write (NativePtr.add bindEntryPointer (int binding)) entry

        let mutable samplerBindEntry = BindGroupEntry()
        samplerBindEntry.Binding <- 4u
        samplerBindEntry.Sampler <- characterSampler
        NativePtr.write (NativePtr.add bindEntryPointer 4) samplerBindEntry

        for index = 0 to 2 do
            let mutable entry = BindGroupEntry()
            entry.Binding <- uint32 (5 + index)
            entry.TextureView <- surfaceViews[index]
            NativePtr.write (NativePtr.add bindEntryPointer (5 + index)) entry

        samplerBindEntry.Binding <- 8u
        samplerBindEntry.Sampler <- surfaceSampler
        NativePtr.write (NativePtr.add bindEntryPointer 8) samplerBindEntry

        for index = 0 to 1 do
            let mutable entry = BindGroupEntry()
            entry.Binding <- uint32 (9 + index)
            entry.TextureView <- environmentViews[index]
            NativePtr.write (NativePtr.add bindEntryPointer (9 + index)) entry

        samplerBindEntry.Binding <- 11u
        samplerBindEntry.Sampler <- environmentSampler
        NativePtr.write (NativePtr.add bindEntryPointer 11) samplerBindEntry
        let mutable skyEntry = BindGroupEntry()
        skyEntry.Binding <- 12u
        skyEntry.TextureView <- environmentViews[2]
        NativePtr.write (NativePtr.add bindEntryPointer 12) skyEntry
        let mutable bindDescriptor = BindGroupDescriptor()
        bindDescriptor.Layout <- bindGroupLayout
        bindDescriptor.EntryCount <- unativeint 13
        bindDescriptor.Entries <- bindEntryPointer
        cameraBindGroup <- api.DeviceCreateBindGroup(device, &bindDescriptor)
        checkPointer "the camera uniform bind group" cameraBindGroup

        let attributes = NativePtr.stackalloc<VertexAttribute> 8

        let writeAttribute index format offset location =
            let mutable attribute = VertexAttribute()
            attribute.Format <- format
            attribute.Offset <- offset
            attribute.ShaderLocation <- location
            NativePtr.write (NativePtr.add attributes index) attribute

        writeAttribute 0 VertexFormat.Float32x3 0UL 0u
        writeAttribute 1 VertexFormat.Float32x3 12UL 1u
        writeAttribute 2 VertexFormat.Float32x4 24UL 2u
        writeAttribute 3 VertexFormat.Float32x2 40UL 3u
        writeAttribute 4 VertexFormat.Uint32 48UL 4u
        writeAttribute 5 VertexFormat.Float32x4 52UL 5u
        writeAttribute 6 VertexFormat.Float32x4 68UL 6u
        writeAttribute 7 VertexFormat.Uint32 84UL 7u

        let mutable vertexLayout = VertexBufferLayout()
        vertexLayout.ArrayStride <- uint64 (Marshal.SizeOf<GpuVertex>())
        vertexLayout.StepMode <- VertexStepMode.Vertex
        vertexLayout.AttributeCount <- unativeint 8
        vertexLayout.Attributes <- attributes
        let vertexLayoutPointer = NativePtr.stackalloc<VertexBufferLayout> 1
        NativePtr.write vertexLayoutPointer vertexLayout

        let vertexName = Text.Encoding.UTF8.GetBytes("vs\u0000")
        let fragmentName = Text.Encoding.UTF8.GetBytes("fs\u0000")
        use vertexEntry = fixed vertexName
        use fragmentEntry = fixed fragmentName
        let mutable vertexState = VertexState()
        vertexState.Module <- shader
        vertexState.EntryPoint <- vertexEntry
        vertexState.BufferCount <- unativeint 1
        vertexState.Buffers <- vertexLayoutPointer

        let mutable colorTarget = ColorTargetState()
        colorTarget.Format <- TextureFormat.Rgba16float
        colorTarget.WriteMask <- ColorWriteMask.All
        let colorTargetPointer = NativePtr.stackalloc<ColorTargetState> 1
        NativePtr.write colorTargetPointer colorTarget
        let mutable fragmentState = FragmentState()
        fragmentState.Module <- shader
        fragmentState.EntryPoint <- fragmentEntry
        fragmentState.TargetCount <- unativeint 1
        fragmentState.Targets <- colorTargetPointer
        let fragmentStatePointer = NativePtr.stackalloc<FragmentState> 1
        NativePtr.write fragmentStatePointer fragmentState

        let mutable depthState = DepthStencilState()
        depthState.Format <- TextureFormat.Depth24Plus
        depthState.DepthWriteEnabled <- Silk.NET.Core.Bool32(1u)
        depthState.DepthCompare <- CompareFunction.Less
        let mutable stencilFace = StencilFaceState()
        stencilFace.Compare <- CompareFunction.Always
        stencilFace.FailOp <- StencilOperation.Keep
        stencilFace.DepthFailOp <- StencilOperation.Keep
        stencilFace.PassOp <- StencilOperation.Keep
        depthState.StencilFront <- stencilFace
        depthState.StencilBack <- stencilFace
        depthState.StencilReadMask <- 0u
        depthState.StencilWriteMask <- 0u
        let depthStatePointer = NativePtr.stackalloc<DepthStencilState> 1
        NativePtr.write depthStatePointer depthState

        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Layout <- pipelineLayout
        descriptor.Vertex <- vertexState
        descriptor.Fragment <- fragmentStatePointer
        descriptor.DepthStencil <- depthStatePointer
        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Primitive.CullMode <- CullMode.None
        descriptor.Multisample.Count <- multisampleCount
        descriptor.Multisample.Mask <- UInt32.MaxValue
        pipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)
        checkPointer "the stadium render pipeline" pipeline

        shadowPass.CreatePipeline(vertexState, depthState)
        grassDetail <- Some(new ProceduralScenePass(api, device, shader, pipelineLayout, depthState, multisampleCount, ProceduralScenePassSettings.grass))
        skyBackground <- Some(new ProceduralScenePass(api, device, shader, pipelineLayout, depthState, multisampleCount, ProceduralScenePassSettings.sky))

    let configureHudTexture (width: uint32) (height: uint32) =
        retainedHud |> Option.iter (fun hud -> (hud :> IDisposable).Dispose())
        retainedHud <- Some(new RetainedMatchHud(int width, int height))

        if NativePtr.toNativeInt hudBindGroup <> 0n then
            api.BindGroupRelease(hudBindGroup)
            hudBindGroup <- NativePtr.nullPtr<BindGroup>

        if NativePtr.toNativeInt hudTextureView <> 0n then
            api.TextureViewRelease(hudTextureView)
            hudTextureView <- NativePtr.nullPtr<TextureView>

        if NativePtr.toNativeInt hudTexture <> 0n then
            api.TextureDestroy(hudTexture)
            api.TextureRelease(hudTexture)
            hudTexture <- NativePtr.nullPtr<Texture>

        let mutable descriptor = TextureDescriptor()
        descriptor.Usage <- TextureUsage.TextureBinding ||| TextureUsage.CopyDst
        descriptor.Dimension <- TextureDimension.Dimension2D
        descriptor.Size <- Extent3D(width, height, 1u)
        descriptor.Format <- TextureFormat.Rgba8Unorm
        descriptor.MipLevelCount <- 1u
        descriptor.SampleCount <- 1u
        hudTexture <- api.DeviceCreateTexture(device, &descriptor)
        checkPointer "the match HUD texture" hudTexture
        hudTextureView <- api.TextureCreateView(hudTexture, NativePtr.nullPtr<TextureViewDescriptor>)
        checkPointer "the match HUD texture view" hudTextureView

        let entries = NativePtr.stackalloc<BindGroupEntry> 2
        let mutable textureEntry = BindGroupEntry()
        textureEntry.Binding <- 0u
        textureEntry.TextureView <- hudTextureView
        NativePtr.write entries textureEntry
        let mutable samplerEntry = BindGroupEntry()
        samplerEntry.Binding <- 1u
        samplerEntry.Sampler <- hudSampler
        NativePtr.write (NativePtr.add entries 1) samplerEntry

        let mutable bindDescriptor = BindGroupDescriptor()
        bindDescriptor.Layout <- hudBindGroupLayout
        bindDescriptor.EntryCount <- unativeint 2
        bindDescriptor.Entries <- entries
        hudBindGroup <- api.DeviceCreateBindGroup(device, &bindDescriptor)
        checkPointer "the match HUD texture bind group" hudBindGroup

    let createHudShaderAndPipeline () =
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

        let sourceBytes = Text.Encoding.UTF8.GetBytes(source + "\u0000")
        use sourcePointer = fixed sourceBytes
        let mutable wgsl = ShaderModuleWGSLDescriptor()
        wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
        wgsl.Code <- sourcePointer
        let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
        NativePtr.write wgslPointer wgsl
        let mutable shaderDescriptor = ShaderModuleDescriptor()
        shaderDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPointer)
        hudShader <- api.DeviceCreateShaderModule(device, &shaderDescriptor)
        checkPointer "the match HUD shader" hudShader

        let entries = NativePtr.stackalloc<BindGroupLayoutEntry> 2
        let mutable textureEntry = BindGroupLayoutEntry()
        textureEntry.Binding <- 0u
        textureEntry.Visibility <- ShaderStage.Fragment
        textureEntry.Texture.SampleType <- TextureSampleType.Float
        textureEntry.Texture.ViewDimension <- TextureViewDimension.Dimension2D
        textureEntry.Texture.Multisampled <- Silk.NET.Core.Bool32(0u)
        NativePtr.write entries textureEntry
        let mutable samplerEntry = BindGroupLayoutEntry()
        samplerEntry.Binding <- 1u
        samplerEntry.Visibility <- ShaderStage.Fragment
        samplerEntry.Sampler.Type <- SamplerBindingType.Filtering
        NativePtr.write (NativePtr.add entries 1) samplerEntry

        let mutable groupDescriptor = BindGroupLayoutDescriptor()
        groupDescriptor.EntryCount <- unativeint 2
        groupDescriptor.Entries <- entries
        hudBindGroupLayout <- api.DeviceCreateBindGroupLayout(device, &groupDescriptor)
        checkPointer "the match HUD bind-group layout" hudBindGroupLayout

        let layoutPointer = NativePtr.stackalloc<nativeptr<BindGroupLayout>> 1
        NativePtr.write layoutPointer hudBindGroupLayout
        let mutable pipelineLayoutDescriptor = PipelineLayoutDescriptor()
        pipelineLayoutDescriptor.BindGroupLayoutCount <- unativeint 1
        pipelineLayoutDescriptor.BindGroupLayouts <- layoutPointer
        hudPipelineLayout <- api.DeviceCreatePipelineLayout(device, &pipelineLayoutDescriptor)
        checkPointer "the match HUD pipeline layout" hudPipelineLayout

        let mutable samplerDescriptor = SamplerDescriptor()
        samplerDescriptor.AddressModeU <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeV <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeW <- AddressMode.ClampToEdge
        samplerDescriptor.MagFilter <- FilterMode.Linear
        samplerDescriptor.MinFilter <- FilterMode.Linear
        samplerDescriptor.MipmapFilter <- MipmapFilterMode.Linear
        samplerDescriptor.MaxAnisotropy <- 1us
        hudSampler <- api.DeviceCreateSampler(device, &samplerDescriptor)
        checkPointer "the match HUD sampler" hudSampler

        let attributes = NativePtr.stackalloc<VertexAttribute> 2
        let mutable positionAttribute = VertexAttribute()
        positionAttribute.Format <- VertexFormat.Float32x3
        positionAttribute.Offset <- 0UL
        positionAttribute.ShaderLocation <- 0u
        NativePtr.write attributes positionAttribute
        let mutable uvAttribute = VertexAttribute()
        uvAttribute.Format <- VertexFormat.Float32x2
        uvAttribute.Offset <- 40UL
        uvAttribute.ShaderLocation <- 1u
        NativePtr.write (NativePtr.add attributes 1) uvAttribute

        let mutable vertexLayout = VertexBufferLayout()
        vertexLayout.ArrayStride <- uint64 (Marshal.SizeOf<GpuVertex>())
        vertexLayout.StepMode <- VertexStepMode.Vertex
        vertexLayout.AttributeCount <- unativeint 2
        vertexLayout.Attributes <- attributes
        let vertexLayoutPointer = NativePtr.stackalloc<VertexBufferLayout> 1
        NativePtr.write vertexLayoutPointer vertexLayout

        let vertexName = Text.Encoding.UTF8.GetBytes("vs\u0000")
        let fragmentName = Text.Encoding.UTF8.GetBytes("fs\u0000")
        use vertexEntry = fixed vertexName
        use fragmentEntry = fixed fragmentName
        let mutable vertexState = VertexState()
        vertexState.Module <- hudShader
        vertexState.EntryPoint <- vertexEntry
        vertexState.BufferCount <- unativeint 1
        vertexState.Buffers <- vertexLayoutPointer

        let mutable blend = BlendState()
        blend.Color.SrcFactor <- BlendFactor.One
        blend.Color.DstFactor <- BlendFactor.OneMinusSrcAlpha
        blend.Color.Operation <- BlendOperation.Add
        blend.Alpha.SrcFactor <- BlendFactor.One
        blend.Alpha.DstFactor <- BlendFactor.OneMinusSrcAlpha
        blend.Alpha.Operation <- BlendOperation.Add
        let blendPointer = NativePtr.stackalloc<BlendState> 1
        NativePtr.write blendPointer blend
        let mutable colorTarget = ColorTargetState()
        colorTarget.Format <- targetFormat
        colorTarget.Blend <- blendPointer
        colorTarget.WriteMask <- ColorWriteMask.All
        let colorTargetPointer = NativePtr.stackalloc<ColorTargetState> 1
        NativePtr.write colorTargetPointer colorTarget
        let mutable fragmentState = FragmentState()
        fragmentState.Module <- hudShader
        fragmentState.EntryPoint <- fragmentEntry
        fragmentState.TargetCount <- unativeint 1
        fragmentState.Targets <- colorTargetPointer
        let fragmentStatePointer = NativePtr.stackalloc<FragmentState> 1
        NativePtr.write fragmentStatePointer fragmentState

        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Layout <- hudPipelineLayout
        descriptor.Vertex <- vertexState
        descriptor.Fragment <- fragmentStatePointer
        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Primitive.CullMode <- CullMode.None
        descriptor.Multisample.Count <- 1u
        descriptor.Multisample.Mask <- UInt32.MaxValue
        hudPipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)
        checkPointer "the match HUD render pipeline" hudPipeline

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
            createBuffer
                "match HUD quad vertices"
                (BufferUsage.Vertex ||| BufferUsage.CopyDst)
                [| topLeft; topRight; bottomRight; bottomLeft |]

        hudQuadIndexBuffer <-
            createBuffer
                "match HUD quad indices"
                (BufferUsage.Index ||| BufferUsage.CopyDst)
                [| 0u; 1u; 2u; 0u; 2u; 3u |]

        configureHudTexture targetWidth targetHeight

    let uploadHudRegion (hud: RetainedMatchHud) (region: SkiaSharp.SKRectI) =
        let mutable destination = ImageCopyTexture()
        destination.Texture <- hudTexture
        destination.MipLevel <- 0u
        destination.Origin <- Origin3D(uint32 region.Left, uint32 region.Top, 0u)
        destination.Aspect <- TextureAspect.All
        let mutable layout = TextureDataLayout()
        layout.Offset <- 0UL
        layout.BytesPerRow <- uint32 hud.RowBytes
        layout.RowsPerImage <- uint32 region.Height
        let extent = Extent3D(uint32 region.Width, uint32 region.Height, 1u)
        let pointer = hud.Pixels + nativeint (region.Top * hud.RowBytes + region.Left * 4)
        let length = (region.Height - 1) * hud.RowBytes + region.Width * 4

        api.QueueWriteTexture(queue, &destination, pointer.ToPointer(), unativeint length, &layout, &extent)
        hudUploadBytes <- hudUploadBytes + int64 (region.Width * region.Height * 4)

    let mutable cameraFocus = Vector3(0.0f, 1.0f, 0.0f)

    let updateCamera width height =
        let aspect = float32 width / float32 height
        let cameraFrame = ViewerCamera.frame cameraMode aspect cameraFocus
        // At broadcast height these blades are subpixel; drawing them would add shimmer instead of detail.
        showGrassDetail <- cameraFrame.Position.Y < 12.0f
        let cameraPosition = cameraFrame.Position
        let cameraOffset = 16 + rootTransformCount * 16
        cameraUniformData[cameraOffset] <- cameraPosition.X
        cameraUniformData[cameraOffset + 1] <- cameraPosition.Y
        cameraUniformData[cameraOffset + 2] <- cameraPosition.Z
        // position.w carries a wrapped clock for grass wind; no shader uses it as a homogeneous coordinate.
        cameraUniformData[cameraOffset + 3] <- float32 (startupClock.Elapsed.TotalSeconds % 3600.0)
        let matrix = ViewerCamera.viewProjection aspect cameraFrame

        hdrOutput
        |> Option.iter (fun output ->
            output.SetCamera(cameraFrame.NearPlane, cameraFrame.FarPlane, cameraFrame.FieldOfView, aspect))

        cameraUniformData[0] <- matrix.M11
        cameraUniformData[1] <- matrix.M12
        cameraUniformData[2] <- matrix.M13
        cameraUniformData[3] <- matrix.M14
        cameraUniformData[4] <- matrix.M21
        cameraUniformData[5] <- matrix.M22
        cameraUniformData[6] <- matrix.M23
        cameraUniformData[7] <- matrix.M24
        cameraUniformData[8] <- matrix.M31
        cameraUniformData[9] <- matrix.M32
        cameraUniformData[10] <- matrix.M33
        cameraUniformData[11] <- matrix.M34
        cameraUniformData[12] <- matrix.M41
        cameraUniformData[13] <- matrix.M42
        cameraUniformData[14] <- matrix.M43
        cameraUniformData[15] <- matrix.M44

        let lightOffset = 16 + rootTransformCount * 16 + 12
        Array.Copy(shadowMatrixData, 0, cameraUniformData, lightOffset, 16)
        let mutable inverse = Matrix4x4.Identity
        if not (Matrix4x4.Invert(matrix, &inverse)) then
            invalidOp "The viewer camera produced a singular projection."
        let inverseOffset = lightOffset + 16
        cameraUniformData[inverseOffset] <- inverse.M11
        cameraUniformData[inverseOffset + 1] <- inverse.M12
        cameraUniformData[inverseOffset + 2] <- inverse.M13
        cameraUniformData[inverseOffset + 3] <- inverse.M14
        cameraUniformData[inverseOffset + 4] <- inverse.M21
        cameraUniformData[inverseOffset + 5] <- inverse.M22
        cameraUniformData[inverseOffset + 6] <- inverse.M23
        cameraUniformData[inverseOffset + 7] <- inverse.M24
        cameraUniformData[inverseOffset + 8] <- inverse.M31
        cameraUniformData[inverseOffset + 9] <- inverse.M32
        cameraUniformData[inverseOffset + 10] <- inverse.M33
        cameraUniformData[inverseOffset + 11] <- inverse.M34
        cameraUniformData[inverseOffset + 12] <- inverse.M41
        cameraUniformData[inverseOffset + 13] <- inverse.M42
        cameraUniformData[inverseOffset + 14] <- inverse.M43
        cameraUniformData[inverseOffset + 15] <- inverse.M44

    let updatePlayersAndBall (frame: RenderFrame) (palettes: Matrix4x4[]) =
        if palettes.Length <> jointMatrices.Length then
            invalidArg "palettes" "procedural palette cardinality does not match the canonical rig"

        Array.Copy(palettes, jointMatrices, palettes.Length)
        let states: RenderPlayer option array = Array.create 22 None
        let mutable carrier = None

        for player in frame.Players do
            if player.HasBall then
                match carrier with
                | None -> carrier <- Some player
                | Some _ -> invalidArg "frame" "a render frame cannot have multiple ball carriers"

            let slot = player.VisualSlot

            if slot < 0 || slot >= 11 then
                invalidArg "frame" $"player {player.Id} has invalid visual slot {slot}"

            let root = (if player.IsHome then 0 else 11) + slot

            if states[root].IsSome then
                invalidArg "frame" $"multiple players occupy visual slot {slot} for the same team"

            states[root] <- Some player

        let hidden = Matrix4x4.CreateTranslation(0.0f, -100000.0f, 0.0f)
        let transforms: Matrix4x4 array = Array.create rootTransformCount hidden
        transforms[23] <- Matrix4x4.Identity

        for root = 0 to 21 do
            let transform =
                match states[root] with
                | Some player ->
                    let yaw = float32 player.BodyFacing + MathF.PI / 2.0f

                    let position =
                        Vector3(float32 player.Position.X, float32 player.Height, -float32 player.Position.Y)

                    let transform =
                        Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position)

                    transform
                | None -> hidden

            transforms[root] <- transform

        pinAndWrite jointBuffer jointMatrices

        let ball = frame.Ball
        let ballRadius = float32 ball.Radius
        let ballScale = ballRadius / scene.BallRadius
        // Core is Z-up; the viewer is Y-up with pitch Y mapped to negative Z.
        let q = ball.Orientation
        let ballRotation = Quaternion(q.X, q.Z, -q.Y, q.W)

        let ballPosition =
            Vector3(float32 ball.Position.X, ballRadius + float32 ball.Height, -float32 ball.Position.Y)

        transforms[22] <-
            Matrix4x4.CreateScale(ballScale)
            * Matrix4x4.CreateFromQuaternion(ballRotation)
            * Matrix4x4.CreateTranslation(ballPosition)

        match carrier with
        | Some player ->
            let position = Vector3(float32 player.Position.X, 0.0f, -float32 player.Position.Y)
            cameraFocus <- Vector3(position.X, 1.0f, position.Z)
            transforms[24] <- Matrix4x4.CreateTranslation(position)
        | None ->
            // A lob changes ball height, not the sideline camera's framing of the pitch.
            cameraFocus <- Vector3(ballPosition.X, 1.0f, ballPosition.Z)
            transforms[24] <- hidden

        for root = 0 to transforms.Length - 1 do
            let matrix = transforms[root]
            let offset = 16 + root * 16
            cameraUniformData[offset] <- matrix.M11
            cameraUniformData[offset + 1] <- matrix.M12
            cameraUniformData[offset + 2] <- matrix.M13
            cameraUniformData[offset + 3] <- matrix.M14
            cameraUniformData[offset + 4] <- matrix.M21
            cameraUniformData[offset + 5] <- matrix.M22
            cameraUniformData[offset + 6] <- matrix.M23
            cameraUniformData[offset + 7] <- matrix.M24
            cameraUniformData[offset + 8] <- matrix.M31
            cameraUniformData[offset + 9] <- matrix.M32
            cameraUniformData[offset + 10] <- matrix.M33
            cameraUniformData[offset + 11] <- matrix.M34
            cameraUniformData[offset + 12] <- matrix.M41
            cameraUniformData[offset + 13] <- matrix.M42
            cameraUniformData[offset + 14] <- matrix.M43
            cameraUniformData[offset + 15] <- matrix.M44

        updateCamera targetWidth targetHeight
        let lightingOffset = 16 + rootTransformCount * 16 + 4
        cameraUniformData[lightingOffset] <- lighting.SunDirection.X
        cameraUniformData[lightingOffset + 1] <- lighting.SunDirection.Y
        cameraUniformData[lightingOffset + 2] <- lighting.SunDirection.Z
        cameraUniformData[lightingOffset + 3] <- lighting.EnvironmentIntensity
        cameraUniformData[lightingOffset + 4] <- lighting.SunRadiance.X
        cameraUniformData[lightingOffset + 5] <- lighting.SunRadiance.Y
        cameraUniformData[lightingOffset + 6] <- lighting.SunRadiance.Z
        pinAndWrite cameraBuffer cameraUniformData

    let updateWindow (bounds: PixelRect) =
        if bounds.Width > 0 && bounds.Height > 0 && bounds <> lastBounds then
            Xlib.moveResizeWindow (display, window, bounds.X, bounds.Y, uint32 bounds.Width, uint32 bounds.Height)
            |> ignore

            Xlib.flush (display) |> ignore
            lastBounds <- bounds

            let width = uint32 bounds.Width
            let height = uint32 bounds.Height

            if width <> targetWidth || height <> targetHeight then
                configureSurface width height
                updateCamera width height
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
            let clamp low high value = max low (min high value)

            let clipped =
                cutouts
                |> Array.choose (fun rect ->
                    let x = clamp 0 bounds.Width rect.X
                    let y = clamp 0 bounds.Height rect.Y
                    let right = clamp x bounds.Width (rect.X + rect.Width)
                    let bottom = clamp y bounds.Height (rect.Y + rect.Height)

                    if right <= x || bottom <= y then
                        None
                    else
                        Some(x, y, right, bottom))

            let boundaries (start: int) (finish: int) selector =
                seq {
                    yield start
                    yield finish

                    for rect in clipped do
                        let first, last = selector rect
                        yield first
                        yield last
                }
                |> Seq.distinct
                |> Seq.sort
                |> Seq.toArray

            let horizontal = boundaries 0 bounds.Width (fun (x, _, right, _) -> x, right)
            let vertical = boundaries 0 bounds.Height (fun (_, y, _, bottom) -> y, bottom)
            let visible = ResizeArray<Xlib.Rectangle>()

            for yIndex = 0 to vertical.Length - 2 do
                let y = vertical[yIndex]
                let bottom = vertical[yIndex + 1]

                for xIndex = 0 to horizontal.Length - 2 do
                    let x = horizontal[xIndex]
                    let right = horizontal[xIndex + 1]
                    let centerX = x + (right - x) / 2
                    let centerY = y + (bottom - y) / 2

                    let covered =
                        clipped
                        |> Array.exists (fun (left, top, edge, lower) ->
                            centerX >= left && centerX < edge && centerY >= top && centerY < lower)

                    if right > x && bottom > y && not covered then
                        visible.Add
                            { X = int16 x
                              Y = int16 y
                              Width = uint16 (right - x)
                              Height = uint16 (bottom - y) }

            if visible.Count = 0 then
                visible.Add
                    { X = 0s
                      Y = 0s
                      Width = uint16 bounds.Width
                      Height = uint16 bounds.Height }

            let rectangles = visible.ToArray()
            use rectanglePointer = fixed rectangles

            // Carving these regions out lets Avalonia draw and receive clicks through its native WebGPU child.
            Xlib.shapeCombineRectangles (
                display,
                window,
                0,
                0,
                0,
                NativePtr.toNativeInt rectanglePointer,
                rectangles.Length,
                0,
                0
            )

            Xlib.flush display |> ignore
            lastShapeBounds <- Some bounds
            lastCutouts <- Array.copy cutouts

    let releaseResources () =
        if not disposed then
            disposed <- true
            retainedHud |> Option.iter (fun hud -> (hud :> IDisposable).Dispose())
            retainedHud <- None
            hdrOutput |> Option.iter (fun output -> (output :> IDisposable).Dispose())
            hdrOutput <- None

            for view in environmentViews do
                if NativePtr.toNativeInt view <> 0n then
                    api.TextureViewRelease(view)

            for texture in environmentTextures do
                if NativePtr.toNativeInt texture <> 0n then
                    api.TextureDestroy(texture)
                    api.TextureRelease(texture)

            if NativePtr.toNativeInt environmentSampler <> 0n then
                api.SamplerRelease(environmentSampler)

            for batch in Array.append staticBatches dynamicBatches do
                releaseBatch batch

            carrierMarkerBatch |> Option.iter releaseBatch
            carrierMarkerBatch <- None

            if NativePtr.toNativeInt depthView <> 0n then
                api.TextureViewRelease(depthView)

            if NativePtr.toNativeInt multisampleColorView <> 0n then
                api.TextureViewRelease(multisampleColorView)

            if NativePtr.toNativeInt multisampleColorTexture <> 0n then
                api.TextureDestroy(multisampleColorTexture)
                api.TextureRelease(multisampleColorTexture)

            if NativePtr.toNativeInt depthTexture <> 0n then
                api.TextureDestroy(depthTexture)
                api.TextureRelease(depthTexture)

            if NativePtr.toNativeInt pipeline <> 0n then
                api.RenderPipelineRelease(pipeline)

            if NativePtr.toNativeInt hudPipeline <> 0n then
                api.RenderPipelineRelease(hudPipeline)

            if NativePtr.toNativeInt hudQuadIndexBuffer <> 0n then
                api.BufferRelease(hudQuadIndexBuffer)

            if NativePtr.toNativeInt hudQuadVertexBuffer <> 0n then
                api.BufferRelease(hudQuadVertexBuffer)

            if NativePtr.toNativeInt hudBindGroup <> 0n then
                api.BindGroupRelease(hudBindGroup)

            if NativePtr.toNativeInt hudTextureView <> 0n then
                api.TextureViewRelease(hudTextureView)

            if NativePtr.toNativeInt hudTexture <> 0n then
                api.TextureDestroy(hudTexture)
                api.TextureRelease(hudTexture)

            if NativePtr.toNativeInt hudSampler <> 0n then
                api.SamplerRelease(hudSampler)

            if NativePtr.toNativeInt hudPipelineLayout <> 0n then
                api.PipelineLayoutRelease(hudPipelineLayout)

            if NativePtr.toNativeInt hudBindGroupLayout <> 0n then
                api.BindGroupLayoutRelease(hudBindGroupLayout)

            if NativePtr.toNativeInt hudShader <> 0n then
                api.ShaderModuleRelease(hudShader)

            if NativePtr.toNativeInt cameraBindGroup <> 0n then
                api.BindGroupRelease(cameraBindGroup)

            shadows |> Option.iter (fun shadowPass -> (shadowPass :> IDisposable).Dispose())
            shadows <- None
            grassDetail |> Option.iter (fun detail -> (detail :> IDisposable).Dispose())
            grassDetail <- None
            skyBackground |> Option.iter (fun sky -> (sky :> IDisposable).Dispose())
            skyBackground <- None

            if NativePtr.toNativeInt characterAlbedoView <> 0n then
                api.TextureViewRelease(characterAlbedoView)

            if NativePtr.toNativeInt characterDetailView <> 0n then
                api.TextureViewRelease(characterDetailView)

            for texture in [ characterAlbedo; characterDetail ] do
                if NativePtr.toNativeInt texture <> 0n then
                    api.TextureDestroy(texture)
                    api.TextureRelease(texture)

            if NativePtr.toNativeInt characterSampler <> 0n then
                api.SamplerRelease(characterSampler)

            for view in surfaceViews do
                if NativePtr.toNativeInt view <> 0n then
                    api.TextureViewRelease(view)

            for texture in surfaceTextures do
                if NativePtr.toNativeInt texture <> 0n then
                    api.TextureDestroy(texture)
                    api.TextureRelease(texture)

            if NativePtr.toNativeInt surfaceSampler <> 0n then
                api.SamplerRelease(surfaceSampler)

            if NativePtr.toNativeInt cameraBuffer <> 0n then
                api.BufferRelease(cameraBuffer)

            if NativePtr.toNativeInt jointBuffer <> 0n then
                api.BufferRelease(jointBuffer)

            if NativePtr.toNativeInt pipelineLayout <> 0n then
                api.PipelineLayoutRelease(pipelineLayout)

            if NativePtr.toNativeInt bindGroupLayout <> 0n then
                api.BindGroupLayoutRelease(bindGroupLayout)

            if NativePtr.toNativeInt shader <> 0n then
                api.ShaderModuleRelease(shader)

            if NativePtr.toNativeInt queue <> 0n then
                api.QueueRelease(queue)

            if NativePtr.toNativeInt surface <> 0n then
                if surfaceConfigured then
                    api.SurfaceUnconfigure(surface)

                api.SurfaceRelease(surface)

            if NativePtr.toNativeInt device <> 0n then
                api.DeviceRelease(device)

            if NativePtr.toNativeInt adapter <> 0n then
                api.AdapterRelease(adapter)

            if NativePtr.toNativeInt instance <> 0n then
                api.InstanceRelease(instance)

            if window <> 0un then
                Xlib.destroyWindow (display, window) |> ignore

            if display <> 0n then
                Xlib.closeDisplay (display) |> ignore

            (errorCallbackPointer :> IDisposable).Dispose()

    let initialize () =
        if not (OperatingSystem.IsLinux()) then
            invalidOp "The WebGPU stadium host currently requires the Linux X11/XWayland backend."

        if not (File.Exists assetPath) then
            invalidOp $"The stadium scene asset was not found: {assetPath}"

        display <- Xlib.openDisplay (0n)

        if display = 0n then
            invalidOp "XOpenDisplay failed while creating the WebGPU stadium surface."

        window <-
            Xlib.createSimpleWindow (
                display,
                parentWindow,
                0,
                0,
                uint32 (max 1 initialBounds.Width),
                uint32 (max 1 initialBounds.Height),
                0u,
                0un,
                0un
            )

        if window = 0un then
            invalidOp "XCreateSimpleWindow failed for the WebGPU stadium surface."

        Xlib.mapRaised (display, window) |> ignore
        Xlib.flush (display) |> ignore
        ViewerDiagnostics.publishNativeWindow (uint64 window)

        instance <- api.CreateInstance(NativePtr.nullPtr<InstanceDescriptor>)
        checkPointer "a WebGPU instance" instance

        let mutable xlibSource = SurfaceDescriptorFromXlibWindow()
        xlibSource.Chain.SType <- SType.SurfaceDescriptorFromXlibWindow
        xlibSource.Display <- NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> display)
        xlibSource.Window <- uint64 window
        let xlibSourcePointer = NativePtr.stackalloc<SurfaceDescriptorFromXlibWindow> 1
        NativePtr.write xlibSourcePointer xlibSource
        let mutable surfaceDescriptor = SurfaceDescriptor()
        surfaceDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt xlibSourcePointer)
        surface <- api.InstanceCreateSurface(instance, &surfaceDescriptor)
        checkPointer "the stadium Xlib surface" surface

        adapter <- requestAdapter ()
        device <- requestDevice ()
        queue <- api.DeviceGetQueue(device)
        checkPointer "the WebGPU queue" queue
        api.DeviceSetUncapturedErrorCallback(device, errorCallbackPointer, nullVoid)
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

        updateCamera targetWidth targetHeight
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
            devicePoll.Invoke(NativePtr.toNativeInt device, 1uy, 0n) |> ignore

    member _.Render
        (bounds: PixelRect, frame: RenderFrame, palettes: Matrix4x4[], cutouts: PixelRect array, stats: MatchHudStats)
        =
        let mutable presented = false

        if not disposed && bounds.Width > 0 && bounds.Height > 0 then
            updateWindow bounds
            updateWindowShape bounds cutouts
            updatePlayersAndBall frame palettes

            let hud = retainedHud.Value

            for region in hud.Update(frame, stats, performance) do
                uploadHudRegion hud region

            let mutable current = SurfaceTexture()
            api.SurfaceGetCurrentTexture(surface, &current)

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

                    encoder <- api.DeviceCreateCommandEncoder(device, NativePtr.nullPtr<CommandEncoderDescriptor>)

                    let draw (batch: GpuDrawBatch) =
                        api.RenderPassEncoderSetVertexBuffer(pass, 0u, batch.VertexBuffer, 0UL, UInt64.MaxValue)

                        api.RenderPassEncoderSetIndexBuffer(
                            pass,
                            batch.IndexBuffer,
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
                    colorAttachment.View <- multisampleColorView
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
                    depthAttachment.View <- depthView
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
                    api.RenderPassEncoderSetPipeline(pass, pipeline)

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
                    api.RenderPassEncoderSetPipeline(pass, pipeline)

                    if showGrassDetail then
                        grassDetail.Value.Encode(pass)
                        api.RenderPassEncoderSetPipeline(pass, pipeline)

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
                    api.RenderPassEncoderSetVertexBuffer(pass, 0u, hudQuadVertexBuffer, 0UL, UInt64.MaxValue)

                    api.RenderPassEncoderSetIndexBuffer(
                        pass,
                        hudQuadIndexBuffer,
                        IndexFormat.Uint32,
                        0UL,
                        UInt64.MaxValue
                    )

                    api.RenderPassEncoderDrawIndexed(pass, 6u, 1u, 0u, 0, 0u)

                    api.RenderPassEncoderEnd(pass)

                    commands <- api.CommandEncoderFinish(encoder, NativePtr.nullPtr<CommandBufferDescriptor>)

                    let commandPointer = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
                    NativePtr.write commandPointer commands
                    api.QueueSubmit(queue, unativeint 1, commandPointer)
                    staticShadowsReady <- true
                    devicePoll.Invoke(NativePtr.toNativeInt device, 0uy, 0n) |> ignore
                    api.SurfacePresent(surface)
                    presented <- true
                    let before = lastPresentation
                    recordPresentation ()

                    (match before with
                     | Some previous ->
                         let interval = performanceClock.Elapsed.TotalMilliseconds - previous

                         ViewerDiagnostics.record
                             { FrameIndex = int64 frameCount
                               Width = int targetWidth
                               Height = int targetHeight
                               Quality = "original"
                               FrameIntervalMs = interval
                               CpuFrameMs = interval
                               GpuTimingAvailable = false
                               GpuTotalMs = None
                               Passes = [||] }
                     | None -> ())

                    frameCount <- frameCount + 1

                    if frameCount = 1 then
                        Diagnostics.Trace.WriteLine($"Presented the first live FootballEngine stadium frame after {startupClock.Elapsed.TotalMilliseconds:F1} ms.")
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
                configureSurface targetWidth targetHeight
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
