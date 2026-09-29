#r "nuget: Avalonia, 12.1.3"
#r "nuget: Avalonia.Desktop, 12.1.3"
#r "nuget: Silk.NET.WebGPU, 2.23.0"
#r "nuget: Silk.NET.WebGPU.Native.WGPU, 2.23.0"
#nowarn "9"

open System
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform
open Avalonia.Threading
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

module Native =
    [<DllImport("libX11.so.6", EntryPoint = "XOpenDisplay", CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint xOpenDisplay(nativeint name)

    [<DllImport("libX11.so.6", EntryPoint = "XCloseDisplay", CallingConvention = CallingConvention.Cdecl)>]
    extern int xCloseDisplay(nativeint display)

    [<DllImport("libwgpu_native.so", EntryPoint = "wgpuDevicePoll", CallingConvention = CallingConvention.Cdecl)>]
    extern byte devicePoll(nativeint device, byte wait, nativeint index)

let private nullPtr<'a when 'a: unmanaged> = NativePtr.nullPtr<'a>
let private voidNull = NativePtr.toVoidPtr (nullPtr<byte>)

type private SilkWindowRenderer(window: Window) =
    let api = WebGPU.GetApi()
    let mutable display = 0n
    let mutable instance = nullPtr<Instance>
    let mutable adapter = nullPtr<Adapter>
    let mutable device = nullPtr<Device>
    let mutable queue = nullPtr<Queue>
    let mutable surface = nullPtr<Surface>
    let mutable shader = nullPtr<ShaderModule>
    let mutable pipeline = nullPtr<RenderPipeline>
    let mutable surfaceFormat = TextureFormat.Undefined
    let mutable disposed = false

    let requireHandle (handle: IPlatformHandle) =
        if isNull handle || handle.HandleDescriptor <> "XID" then
            let descriptor = if isNull handle then "none" else handle.HandleDescriptor
            invalidOp $"Expected an Avalonia X11 window handle, received '{descriptor}'."

        uint64 handle.Handle

    let requestAdapter () =
        let mutable requested = nullPtr<Adapter>
        let callback = RequestAdapterCallback(fun status value _ _ ->
            if status = RequestAdapterStatus.Success then
                requested <- value)
        use callbackPointer = PfnRequestAdapterCallback.From(callback)
        let mutable options = RequestAdapterOptions()
        options.CompatibleSurface <- surface
        api.InstanceRequestAdapter(instance, &options, callbackPointer, voidNull)

        if NativePtr.toNativeInt requested = 0n then
            invalidOp "Silk.NET.WebGPU did not return an adapter for the Avalonia window."

        requested

    let requestDevice () =
        let mutable requested = nullPtr<Device>
        let callback = RequestDeviceCallback(fun status value _ _ ->
            if status = RequestDeviceStatus.Success then
                requested <- value)
        use callbackPointer = PfnRequestDeviceCallback.From(callback)
        api.AdapterRequestDevice(adapter, nullPtr<DeviceDescriptor>, callbackPointer, voidNull)

        if NativePtr.toNativeInt requested = 0n then
            invalidOp "Silk.NET.WebGPU did not return a device for the Avalonia window."

        requested

    let createPipeline () =
        let source =
            """@vertex fn vs(@builtin(vertex_index) id: u32) -> @builtin(position) vec4<f32> {
    var points = array<vec2<f32>, 3>(vec2<f32>(-1.0,-1.0), vec2<f32>(3.0,-1.0), vec2<f32>(-1.0,3.0));
    return vec4<f32>(points[id], 0.0, 1.0);
}
@fragment fn fs() -> @location(0) vec4<f32> { return vec4<f32>(0.04, 0.18, 0.28, 1.0); }
"""

        let shaderBytes = Text.Encoding.UTF8.GetBytes(source + "\u0000")
        use shaderSourcePointer = fixed shaderBytes
        let mutable wgsl = ShaderModuleWGSLDescriptor()
        wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
        wgsl.Code <- shaderSourcePointer
        let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
        NativePtr.write wgslPointer wgsl

        let mutable shaderDescriptor = ShaderModuleDescriptor()
        shaderDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct>(NativePtr.toNativeInt wgslPointer)
        shader <- api.DeviceCreateShaderModule(device, &shaderDescriptor)

        let vertexBytes = Text.Encoding.UTF8.GetBytes("vs\u0000")
        let fragmentBytes = Text.Encoding.UTF8.GetBytes("fs\u0000")
        use vertexEntry = fixed vertexBytes
        use fragmentEntry = fixed fragmentBytes

        let mutable vertex = VertexState()
        vertex.Module <- shader
        vertex.EntryPoint <- vertexEntry

        let mutable target = ColorTargetState()
        target.Format <- surfaceFormat
        target.WriteMask <- ColorWriteMask.All
        let targetPointer = NativePtr.stackalloc<ColorTargetState> 1
        NativePtr.write targetPointer target

        let mutable fragment = FragmentState()
        fragment.Module <- shader
        fragment.EntryPoint <- fragmentEntry
        fragment.TargetCount <- unativeint 1
        fragment.Targets <- targetPointer
        let fragmentPointer = NativePtr.stackalloc<FragmentState> 1
        NativePtr.write fragmentPointer fragment

        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Vertex <- vertex
        descriptor.Fragment <- fragmentPointer
        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Multisample.Count <- 1u
        descriptor.Multisample.Mask <- UInt32.MaxValue
        pipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)

    do
        let platformHandle = window.TryGetPlatformHandle()
        let nativeWindow = requireHandle platformHandle
        display <- Native.xOpenDisplay(0n)

        if display = 0n then
            invalidOp "XOpenDisplay failed while creating the WebGPU surface."

        instance <- api.CreateInstance(nullPtr<InstanceDescriptor>)

        let mutable surfaceSource = SurfaceDescriptorFromXlibWindow()
        surfaceSource.Chain.SType <- SType.SurfaceDescriptorFromXlibWindow
        surfaceSource.Display <- NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> display)
        surfaceSource.Window <- nativeWindow
        let sourcePointer = NativePtr.stackalloc<SurfaceDescriptorFromXlibWindow> 1
        NativePtr.write sourcePointer surfaceSource

        let mutable surfaceDescriptor = SurfaceDescriptor()
        surfaceDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct>(NativePtr.toNativeInt sourcePointer)
        surface <- api.InstanceCreateSurface(instance, &surfaceDescriptor)
        adapter <- requestAdapter ()
        device <- requestDevice ()
        queue <- api.DeviceGetQueue(device)

        let mutable capabilities = SurfaceCapabilities()
        api.SurfaceGetCapabilities(surface, adapter, &capabilities)

        let readSupported count (values: nativeptr<'T>) =
            Array.init (int count) (fun index -> NativePtr.read (NativePtr.add values index))

        let supportedFormats = readSupported capabilities.FormatCount capabilities.Formats
        let supportedAlphaModes = readSupported capabilities.AlphaModeCount capabilities.AlphaModes
        let supportedPresentModes = readSupported capabilities.PresentModeCount capabilities.PresentModes
        let preferredFormat = api.SurfaceGetPreferredFormat(surface, adapter)

        surfaceFormat <-
            if Array.contains preferredFormat supportedFormats then
                preferredFormat
            else
                supportedFormats
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported formats.")

        let alphaMode =
            if Array.contains CompositeAlphaMode.Premultiplied supportedAlphaModes then
                CompositeAlphaMode.Premultiplied
            else
                supportedAlphaModes
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported alpha modes.")

        let presentMode =
            if Array.contains PresentMode.Fifo supportedPresentModes then
                PresentMode.Fifo
            else
                supportedPresentModes
                |> Array.tryHead
                |> Option.defaultWith (fun () -> invalidOp "The WebGPU surface reports no supported present modes.")

        api.SurfaceCapabilitiesFreeMembers(capabilities)

        if surfaceFormat = TextureFormat.Undefined then
            invalidOp "WebGPU did not report a preferred format for the Avalonia window surface."

        let scale = float32 window.RenderScaling
        let size = window.ClientSize
        let mutable configuration = SurfaceConfiguration()
        configuration.Device <- device
        configuration.Format <- surfaceFormat
        configuration.Usage <- TextureUsage.RenderAttachment
        configuration.AlphaMode <- alphaMode
        configuration.Width <- max 1u (uint32 (Math.Ceiling(size.Width * float scale)))
        configuration.Height <- max 1u (uint32 (Math.Ceiling(size.Height * float scale)))
        configuration.PresentMode <- presentMode
        api.SurfaceConfigure(surface, &configuration)
        createPipeline ()

        printfn
            "Direct Silk WebGPU surface: Avalonia=%s XID=0x%X format=%A alpha=%A present=%A size=%ux%u"
            (if isNull platformHandle then "none" else platformHandle.HandleDescriptor)
            nativeWindow
            surfaceFormat
            alphaMode
            presentMode
            configuration.Width
            configuration.Height

    member _.Render() =
        if not disposed then
            let mutable frame = SurfaceTexture()
            api.SurfaceGetCurrentTexture(surface, &frame)

            if frame.Status = SurfaceGetCurrentTextureStatus.Success && NativePtr.toNativeInt frame.Texture <> 0n then
                let view = api.TextureCreateView(frame.Texture, nullPtr<TextureViewDescriptor>)
                let encoder = api.DeviceCreateCommandEncoder(device, nullPtr<CommandEncoderDescriptor>)
                let mutable attachment = RenderPassColorAttachment()
                attachment.View <- view
                attachment.LoadOp <- LoadOp.Clear
                attachment.StoreOp <- StoreOp.Store
                let mutable clear = Silk.NET.WebGPU.Color()
                clear.R <- 0.035
                clear.G <- 0.12
                clear.B <- 0.19
                clear.A <- 1.0
                attachment.ClearValue <- clear
                let attachmentPointer = NativePtr.stackalloc<RenderPassColorAttachment> 1
                NativePtr.write attachmentPointer attachment
                let mutable passDescriptor = RenderPassDescriptor()
                passDescriptor.ColorAttachmentCount <- unativeint 1
                passDescriptor.ColorAttachments <- attachmentPointer
                let pass = api.CommandEncoderBeginRenderPass(encoder, &passDescriptor)
                api.RenderPassEncoderSetPipeline(pass, pipeline)
                api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)
                api.RenderPassEncoderEnd(pass)
                let commandBuffer = api.CommandEncoderFinish(encoder, nullPtr<CommandBufferDescriptor>)
                let commandPointer = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
                NativePtr.write commandPointer commandBuffer
                api.QueueSubmit(queue, unativeint 1, commandPointer)
                Native.devicePoll(NativePtr.toNativeInt device, 0uy, 0n) |> ignore
                api.SurfacePresent(surface)
                api.CommandBufferRelease(commandBuffer)
                api.RenderPassEncoderRelease(pass)
                api.CommandEncoderRelease(encoder)
                api.TextureViewRelease(view)
                api.TextureRelease(frame.Texture)
            else
                printfn "WebGPU surface acquisition returned %A" frame.Status

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                if NativePtr.toNativeInt pipeline <> 0n then api.RenderPipelineRelease(pipeline)
                if NativePtr.toNativeInt shader <> 0n then api.ShaderModuleRelease(shader)
                if NativePtr.toNativeInt queue <> 0n then api.QueueRelease(queue)
                if NativePtr.toNativeInt surface <> 0n then
                    api.SurfaceUnconfigure(surface)
                    api.SurfaceRelease(surface)
                if NativePtr.toNativeInt device <> 0n then api.DeviceRelease(device)
                if NativePtr.toNativeInt adapter <> 0n then api.AdapterRelease(adapter)
                if NativePtr.toNativeInt instance <> 0n then api.InstanceRelease(instance)
                if display <> 0n then Native.xCloseDisplay(display) |> ignore

type private ProbeApp() =
    inherit Application()

    override _.Initialize() = ()

    override _.OnFrameworkInitializationCompleted() =
        match base.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let window = Window(Title = "FootballEngine direct Silk WebGPU Avalonia probe", Width = 900.0, Height = 580.0)
            let grid = Grid()
            grid.Background <- Brushes.Transparent
            window.Content <- grid
            window.Background <- Brushes.Transparent
            window.TransparencyBackgroundFallback <- Brushes.Transparent
            window.TransparencyLevelHint <- [| WindowTransparencyLevel.Transparent |]
            let mutable renderer: SilkWindowRenderer option = None
            let timer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds(16.0))
            timer.Tick.Add(fun _ -> renderer |> Option.iter _.Render())
            window.Opened.Add(fun _ ->
                printfn "Avalonia probe window opened"
                renderer <- Some(new SilkWindowRenderer(window))
                timer.Start())
            window.Closing.Add(fun _ ->
                printfn "Avalonia probe window closing"
                timer.Stop()
                renderer |> Option.iter (fun current -> (current :> IDisposable).Dispose())
                renderer <- None)
            desktop.MainWindow <- window
            window.Show()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([||])
