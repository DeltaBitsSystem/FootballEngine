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
      IndexCount: uint32 }

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
type WebGpuStadiumRenderer(parentWindow: unativeint, initialBounds: PixelRect, assetPath: string) =
    let api = SharedWebGpu.api
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
    let mutable cameraBindGroup = NativePtr.nullPtr<BindGroup>
    let cameraUniformData = Array.zeroCreate<float32> (16 + 24 * 16)
    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
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
    let scene = StadiumAsset.load assetPath

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
                      IndexCount = uint32 indices.Length }
            with _ ->
                api.BufferRelease(vertexBuffer)
                reraise ()

    let mutable staticBatches: GpuDrawBatch array = [||]
    let mutable dynamicBatches: GpuDrawBatch array = [||]

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

        let preferredFormat = api.SurfaceGetPreferredFormat(surface, adapter)

        targetFormat <-
            if Array.contains preferredFormat formats then
                preferredFormat
            else
                formats
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported formats.")

        let alphaMode =
            if Array.contains CompositeAlphaMode.Opaque alphaModes then
                CompositeAlphaMode.Opaque
            else
                alphaModes
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported alpha modes.")

        let presentMode =
            if Array.contains PresentMode.Fifo presentModes then
                PresentMode.Fifo
            else
                presentModes
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported present modes.")

        api.SurfaceCapabilitiesFreeMembers(capabilities)

        let mutable configuration = SurfaceConfiguration()
        configuration.Device <- device
        configuration.Format <- targetFormat
        configuration.Usage <- TextureUsage.RenderAttachment
        configuration.AlphaMode <- alphaMode
        configuration.Width <- width
        configuration.Height <- height
        configuration.PresentMode <- presentMode
        api.SurfaceConfigure(surface, &configuration)
        surfaceConfigured <- true
        targetWidth <- width
        targetHeight <- height

        if NativePtr.toNativeInt depthView <> 0n then
            api.TextureViewRelease(depthView)
            depthView <- NativePtr.nullPtr<TextureView>

        if NativePtr.toNativeInt depthTexture <> 0n then
            api.TextureDestroy(depthTexture)
            api.TextureRelease(depthTexture)
            depthTexture <- NativePtr.nullPtr<Texture>

        let mutable textureDescriptor = TextureDescriptor()
        textureDescriptor.Usage <- TextureUsage.RenderAttachment
        textureDescriptor.Dimension <- TextureDimension.Dimension2D
        textureDescriptor.Size <- Extent3D(width, height, 1u)
        textureDescriptor.Format <- TextureFormat.Depth24Plus
        textureDescriptor.MipLevelCount <- 1u
        textureDescriptor.SampleCount <- 1u
        depthTexture <- api.DeviceCreateTexture(device, &textureDescriptor)
        checkPointer "a depth texture" depthTexture
        depthView <- api.TextureCreateView(depthTexture, NativePtr.nullPtr<TextureViewDescriptor>)
        checkPointer "a depth texture view" depthView

    let createShaderAndPipeline () =
        let source =
            """struct Camera {
    viewProjection: mat4x4<f32>,
    rootTransforms: array<mat4x4<f32>, 24>,
}

@group(0) @binding(0) var<uniform> camera: Camera;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
    @location(3) uv: vec2<f32>,
    @location(4) rootIndex: u32,
}

struct VertexOutput {
    @builtin(position) clipPosition: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) uv: vec2<f32>,
    @location(3) worldPosition: vec3<f32>,
}

@vertex fn vs(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    let model = camera.rootTransforms[input.rootIndex];
    let world = model * vec4<f32>(input.position, 1.0);
    output.clipPosition = camera.viewProjection * world;
    output.normal = normalize((model * vec4<f32>(input.normal, 0.0)).xyz);
    output.color = input.color;
    output.uv = input.uv;
    output.worldPosition = world.xyz;
    return output;
}

@fragment fn fs(input: VertexOutput) -> @location(0) vec4<f32> {
    let normal = normalize(input.normal);
    let sunlight = normalize(vec3<f32>(-0.38, 0.88, 0.28));
    let diffuse = 0.36 + 0.64 * max(dot(normal, sunlight), 0.0);
    let turfVariation = 0.94 + 0.06 * sin(input.worldPosition.x * 2.1 + input.worldPosition.z * 1.7);
    let lit = input.color.rgb * diffuse * turfVariation;
    return vec4<f32>(lit, input.color.a);
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
        shader <- api.DeviceCreateShaderModule(device, &shaderDescriptor)
        checkPointer "the stadium WGSL shader" shader

        let mutable uniformEntry = BindGroupLayoutEntry()
        uniformEntry.Binding <- 0u
        uniformEntry.Visibility <- ShaderStage.Vertex
        uniformEntry.Buffer.Type <- BufferBindingType.Uniform
        let uniformEntryPointer = NativePtr.stackalloc<BindGroupLayoutEntry> 1
        NativePtr.write uniformEntryPointer uniformEntry
        let mutable groupDescriptor = BindGroupLayoutDescriptor()
        groupDescriptor.EntryCount <- unativeint 1
        groupDescriptor.Entries <- uniformEntryPointer
        bindGroupLayout <- api.DeviceCreateBindGroupLayout(device, &groupDescriptor)
        checkPointer "the camera bind-group layout" bindGroupLayout

        let layoutPointer = NativePtr.stackalloc<nativeptr<BindGroupLayout>> 1
        NativePtr.write layoutPointer bindGroupLayout
        let mutable pipelineLayoutDescriptor = PipelineLayoutDescriptor()
        pipelineLayoutDescriptor.BindGroupLayoutCount <- unativeint 1
        pipelineLayoutDescriptor.BindGroupLayouts <- layoutPointer
        pipelineLayout <- api.DeviceCreatePipelineLayout(device, &pipelineLayoutDescriptor)
        checkPointer "the stadium pipeline layout" pipelineLayout

        cameraBuffer <-
            createBuffer
                "broadcast camera and player transforms"
                (BufferUsage.Uniform ||| BufferUsage.CopyDst)
                cameraUniformData

        let mutable bindEntry = BindGroupEntry()
        bindEntry.Binding <- 0u
        bindEntry.Buffer <- cameraBuffer
        bindEntry.Offset <- 0UL
        bindEntry.Size <- uint64 (cameraUniformData.Length * sizeof<float32>)
        let bindEntryPointer = NativePtr.stackalloc<BindGroupEntry> 1
        NativePtr.write bindEntryPointer bindEntry
        let mutable bindDescriptor = BindGroupDescriptor()
        bindDescriptor.Layout <- bindGroupLayout
        bindDescriptor.EntryCount <- unativeint 1
        bindDescriptor.Entries <- bindEntryPointer
        cameraBindGroup <- api.DeviceCreateBindGroup(device, &bindDescriptor)
        checkPointer "the camera uniform bind group" cameraBindGroup

        let attributes = NativePtr.stackalloc<VertexAttribute> 5

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

        let mutable vertexLayout = VertexBufferLayout()
        vertexLayout.ArrayStride <- uint64 (Marshal.SizeOf<GpuVertex>())
        vertexLayout.StepMode <- VertexStepMode.Vertex
        vertexLayout.AttributeCount <- unativeint 5
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
        colorTarget.Format <- targetFormat
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
        descriptor.Multisample.Count <- 1u
        descriptor.Multisample.Mask <- UInt32.MaxValue
        pipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)
        checkPointer "the stadium render pipeline" pipeline

    let updateCamera width height =
        let cameraPosition =
            Vector3(scene.Camera.World.M41, scene.Camera.World.M42, scene.Camera.World.M43)

        let forward =
            Vector3.TransformNormal(-Vector3.UnitZ, scene.Camera.World) |> Vector3.Normalize

        let up =
            Vector3.TransformNormal(Vector3.UnitY, scene.Camera.World) |> Vector3.Normalize

        let view = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition + forward, up)
        let aspect = float32 width / float32 height

        let projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                scene.Camera.VerticalFov,
                aspect,
                scene.Camera.NearPlane,
                scene.Camera.FarPlane
            )

        let matrix = view * projection

        let values =
            [| matrix.M11
               matrix.M12
               matrix.M13
               matrix.M14
               matrix.M21
               matrix.M22
               matrix.M23
               matrix.M24
               matrix.M31
               matrix.M32
               matrix.M33
               matrix.M34
               matrix.M41
               matrix.M42
               matrix.M43
               matrix.M44 |]

        Array.blit values 0 cameraUniformData 0 values.Length

    let updatePlayersAndBall (frame: RenderFrame) =
        let states: RenderPlayer option array = Array.create 22 None

        for player in frame.Players do
            let slot = player.VisualSlot

            if slot < 0 || slot >= 11 then
                invalidArg "frame" $"player {player.Id} has invalid visual slot {slot}"

            let root = (if player.IsHome then 0 else 11) + slot

            if states[root].IsSome then
                invalidArg "frame" $"multiple players occupy visual slot {slot} for the same team"

            states[root] <- Some player

        let hidden = Matrix4x4.CreateTranslation(0.0f, -100000.0f, 0.0f)
        let transforms: Matrix4x4 array = Array.create 24 hidden
        transforms[23] <- Matrix4x4.Identity

        for root = 0 to 21 do
            let defaultYaw = if root < 11 then -MathF.PI / 2.0f else MathF.PI / 2.0f

            let transform =
                match states[root] with
                | Some player ->
                    let velocityX = float32 player.Velocity.X
                    let velocityZ = -float32 player.Velocity.Y
                    let speedSquared = velocityX * velocityX + velocityZ * velocityZ

                    let yaw =
                        if speedSquared > 0.16f then
                            MathF.Atan2(-velocityX, -velocityZ)
                        else
                            defaultYaw

                    let position = Vector3(float32 player.Position.X, 0.0f, -float32 player.Position.Y)
                    Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position)
                | None -> hidden

            transforms[root] <- transform

        let ball = frame.Ball
        let ballRadius = 0.11f
        let ballScale = ballRadius / scene.BallRadius
        let ballRotation = float32 (frame.TimeSeconds * (0.8 + ball.Spin.Y))

        let ballPosition =
            Vector3(float32 ball.Position.X, ballRadius + float32 ball.Height, -float32 ball.Position.Y)

        transforms[22] <-
            Matrix4x4.CreateScale(ballScale)
            * Matrix4x4.CreateRotationY(ballRotation)
            * Matrix4x4.CreateTranslation(ballPosition)

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

            for batch in Array.append staticBatches dynamicBatches do
                releaseBatch batch

            if NativePtr.toNativeInt depthView <> 0n then
                api.TextureViewRelease(depthView)

            if NativePtr.toNativeInt depthTexture <> 0n then
                api.TextureDestroy(depthTexture)
                api.TextureRelease(depthTexture)

            if NativePtr.toNativeInt pipeline <> 0n then
                api.RenderPipelineRelease(pipeline)

            if NativePtr.toNativeInt cameraBindGroup <> 0n then
                api.BindGroupRelease(cameraBindGroup)

            if NativePtr.toNativeInt cameraBuffer <> 0n then
                api.BufferRelease(cameraBuffer)

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

        staticBatches <-
            scene.StaticBatches
            |> Array.map (fun batch -> batch.MaterialName, batch.Vertices, batch.Indices)
            |> createGpuBatches

        dynamicBatches <-
            scene.DynamicBatches
            |> Array.map (fun batch -> batch.MaterialName, batch.Vertices, batch.Indices)
            |> createGpuBatches

        updateCamera targetWidth targetHeight
        updateWindow initialBounds

        Diagnostics.Trace.WriteLine(
            $"WebGPU stadium ready: X11 child 0x{uint64 window:X}, {staticBatches.Length} material batches, {dynamicBatches.Length} live batches."
        )

    do
        try
            initialize ()
        with _ ->
            releaseResources ()
            reraise ()

    member _.NativeWindow = window

    member _.Render(bounds: PixelRect, frame: RenderFrame, cutouts: PixelRect array) =
        if not disposed && bounds.Width > 0 && bounds.Height > 0 then
            updateWindow bounds
            updateWindowShape bounds cutouts
            updatePlayersAndBall frame

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

                    let mutable colorAttachment = RenderPassColorAttachment()
                    colorAttachment.View <- colorView
                    colorAttachment.LoadOp <- LoadOp.Clear
                    colorAttachment.StoreOp <- StoreOp.Store
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
                        0u,
                        cameraBindGroup,
                        unativeint 0,
                        NativePtr.nullPtr<uint32>
                    )

                    let draw (batch: GpuDrawBatch) =
                        api.RenderPassEncoderSetVertexBuffer(pass, 0u, batch.VertexBuffer, 0UL, UInt64.MaxValue)

                        api.RenderPassEncoderSetIndexBuffer(
                            pass,
                            batch.IndexBuffer,
                            IndexFormat.Uint32,
                            0UL,
                            UInt64.MaxValue
                        )

                        api.RenderPassEncoderDrawIndexed(pass, batch.IndexCount, 1u, 0u, 0, 0u)

                    for batch in staticBatches do
                        draw batch

                    for batch in dynamicBatches do
                        draw batch

                    api.RenderPassEncoderEnd(pass)

                    commands <- api.CommandEncoderFinish(encoder, NativePtr.nullPtr<CommandBufferDescriptor>)

                    let commandPointer = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
                    NativePtr.write commandPointer commands
                    api.QueueSubmit(queue, unativeint 1, commandPointer)
                    devicePoll.Invoke(NativePtr.toNativeInt device, 0uy, 0n) |> ignore
                    api.SurfacePresent(surface)
                    frameCount <- frameCount + 1

                    if frameCount = 1 then
                        Diagnostics.Trace.WriteLine("Presented the first live FootballEngine stadium frame.")
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

    let createRenderer (control: Control) =
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

                Some(new WebGpuStadiumRenderer(parent, toPixelBounds control, assetPath))

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
