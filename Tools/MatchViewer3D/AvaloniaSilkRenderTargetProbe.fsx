#r "nuget: Avalonia, 12.1.3"
#r "nuget: Avalonia.Desktop, 12.1.3"
#r "nuget: Silk.NET.WebGPU, 2.23.0"
#r "nuget: Silk.NET.WebGPU.Native.WGPU, 2.23.0"
#nowarn "9"

open System
open System.Collections.Generic
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Platform.Surfaces
open Avalonia.Rendering.Composition
open Avalonia.Threading
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

module private Native =
  [<DllImport("libX11.so.6", EntryPoint = "XOpenDisplay", CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint xOpenDisplay(nativeint name)

  [<DllImport("libX11.so.6", EntryPoint = "XCloseDisplay", CallingConvention = CallingConvention.Cdecl)>]
  extern int xCloseDisplay(nativeint display)

let private nullPtr<'a when 'a: unmanaged> = NativePtr.nullPtr<'a>
let private voidNull = NativePtr.toVoidPtr (nullPtr<byte>)
let mutable private probeWindow: Window option = None

type private SilkFrame(window: Window) =
  let api = WebGPU.GetApi()
  let mutable display = 0n
  let mutable instance = nullPtr<Instance>
  let mutable adapter = nullPtr<Adapter>
  let mutable device = nullPtr<Device>
  let mutable queue = nullPtr<Queue>
  let mutable surface = nullPtr<Surface>
  let mutable pipeline = nullPtr<RenderPipeline>
  let mutable shader = nullPtr<ShaderModule>
  let mutable format = TextureFormat.Undefined
  let mutable alphaMode = CompositeAlphaMode.Premultiplied
  let mutable presentMode = PresentMode.Fifo
  let mutable width = 0u
  let mutable height = 0u
  let mutable frameCount = 0
  let mutable disposed = false

  let errorCallback =
    ErrorCallback(fun errorType message _ ->
      let text = Marshal.PtrToStringUTF8(NativePtr.toNativeInt message)
      eprintfn "WebGPU uncaptured error (%A): %s" errorType text)

  let errorCallbackPointer = PfnErrorCallback.From(errorCallback)

  let createSurface () =
    let handle = window.TryGetPlatformHandle()

    if isNull handle || handle.HandleDescriptor <> "XID" then
      let descriptor = if isNull handle then "none" else handle.HandleDescriptor
      invalidOp $"The probe requires Avalonia's X11/XWayland XID, received '{descriptor}'."

    display <- Native.xOpenDisplay (0n)

    if display = 0n then
      invalidOp "XOpenDisplay failed while creating a WebGPU surface."

    let mutable source = SurfaceDescriptorFromXlibWindow()
    source.Chain.SType <- SType.SurfaceDescriptorFromXlibWindow
    source.Display <- NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> display)
    source.Window <- uint64 handle.Handle
    let sourcePointer = NativePtr.stackalloc<SurfaceDescriptorFromXlibWindow> 1
    NativePtr.write sourcePointer source
    let mutable descriptor = SurfaceDescriptor()
    descriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt sourcePointer)
    surface <- api.InstanceCreateSurface(instance, &descriptor)

  let requestAdapter () =
    let mutable result = nullPtr<Adapter>

    let callback =
      RequestAdapterCallback(fun status value _ _ ->
        if status = RequestAdapterStatus.Success then
          result <- value)

    use callbackPointer = PfnRequestAdapterCallback.From(callback)
    let mutable options = RequestAdapterOptions()
    options.CompatibleSurface <- surface
    api.InstanceRequestAdapter(instance, &options, callbackPointer, voidNull)

    if NativePtr.toNativeInt result = 0n then
      invalidOp "WebGPU did not return a surface-compatible adapter."

    result

  let requestDevice () =
    let mutable result = nullPtr<Device>

    let callback =
      RequestDeviceCallback(fun status value _ _ ->
        if status = RequestDeviceStatus.Success then
          result <- value)

    use callbackPointer = PfnRequestDeviceCallback.From(callback)
    api.AdapterRequestDevice(adapter, nullPtr<DeviceDescriptor>, callbackPointer, voidNull)

    if NativePtr.toNativeInt result = 0n then
      invalidOp "WebGPU did not return a device."

    result

  let createPipeline () =
    let source =
      """@vertex fn vs(@builtin(vertex_index) id: u32) -> @builtin(position) vec4<f32> {
    var points = array<vec2<f32>, 3>(vec2<f32>(-0.78,-0.72), vec2<f32>(0.78,-0.72), vec2<f32>(0.0,0.82));
    return vec4<f32>(points[id], 0.2, 1.0);
}
@fragment fn fs() -> @location(0) vec4<f32> { return vec4<f32>(0.10, 0.84, 0.58, 1.0); }
"""

    let bytes = Text.Encoding.UTF8.GetBytes(source + "\u0000")
    use code = fixed bytes
    let mutable wgsl = ShaderModuleWGSLDescriptor()
    wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
    wgsl.Code <- code
    let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
    NativePtr.write wgslPointer wgsl
    let mutable shaderDescriptor = ShaderModuleDescriptor()
    shaderDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPointer)
    shader <- api.DeviceCreateShaderModule(device, &shaderDescriptor)

    let vertexName = Text.Encoding.UTF8.GetBytes("vs\u0000")
    let fragmentName = Text.Encoding.UTF8.GetBytes("fs\u0000")
    use vertexEntry = fixed vertexName
    use fragmentEntry = fixed fragmentName
    let mutable vertex = VertexState()
    vertex.Module <- shader
    vertex.EntryPoint <- vertexEntry
    let mutable colorTarget = ColorTargetState()
    colorTarget.Format <- format
    colorTarget.WriteMask <- ColorWriteMask.All
    let colorPointer = NativePtr.stackalloc<ColorTargetState> 1
    NativePtr.write colorPointer colorTarget
    let mutable fragment = FragmentState()
    fragment.Module <- shader
    fragment.EntryPoint <- fragmentEntry
    fragment.TargetCount <- unativeint 1
    fragment.Targets <- colorPointer
    let fragmentPointer = NativePtr.stackalloc<FragmentState> 1
    NativePtr.write fragmentPointer fragment
    let mutable descriptor = RenderPipelineDescriptor()
    descriptor.Vertex <- vertex
    descriptor.Fragment <- fragmentPointer
    descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
    descriptor.Multisample.Count <- 1u
    descriptor.Multisample.Mask <- UInt32.MaxValue
    pipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)

  let configure () =
    let mutable capabilities = SurfaceCapabilities()
    api.SurfaceGetCapabilities(surface, adapter, &capabilities)

    let formats =
      Array.init (int capabilities.FormatCount) (fun index -> NativePtr.read (NativePtr.add capabilities.Formats index))

    let alphaModes =
      Array.init (int capabilities.AlphaModeCount) (fun index ->
        NativePtr.read (NativePtr.add capabilities.AlphaModes index))

    let presentModes =
      Array.init (int capabilities.PresentModeCount) (fun index ->
        NativePtr.read (NativePtr.add capabilities.PresentModes index))

    let preferred = api.SurfaceGetPreferredFormat(surface, adapter)

    format <-
      if Array.contains preferred formats then
        preferred
      else
        formats[0]

    alphaMode <-
      if Array.contains CompositeAlphaMode.Opaque alphaModes then
        CompositeAlphaMode.Opaque
      elif Array.contains CompositeAlphaMode.Premultiplied alphaModes then
        CompositeAlphaMode.Premultiplied
      else
        alphaModes[0]

    presentMode <-
      if Array.contains PresentMode.Fifo presentModes then
        PresentMode.Fifo
      else
        presentModes[0]

    api.SurfaceCapabilitiesFreeMembers(capabilities)
    let size = window.ClientSize
    let scaling = window.RenderScaling
    width <- max 1u (uint32 (Math.Ceiling(size.Width * scaling)))
    height <- max 1u (uint32 (Math.Ceiling(size.Height * scaling)))
    let mutable configuration = SurfaceConfiguration()
    configuration.Device <- device
    configuration.Format <- format
    configuration.Usage <- TextureUsage.RenderAttachment
    configuration.AlphaMode <- alphaMode
    configuration.Width <- width
    configuration.Height <- height
    configuration.PresentMode <- presentMode
    api.SurfaceConfigure(surface, &configuration)
    createPipeline ()

  do
    instance <- api.CreateInstance(nullPtr<InstanceDescriptor>)
    createSurface ()
    adapter <- requestAdapter ()
    device <- requestDevice ()
    queue <- api.DeviceGetQueue(device)
    api.DeviceSetUncapturedErrorCallback(device, errorCallbackPointer, voidNull)
    configure ()

    if NativePtr.toNativeInt pipeline = 0n then
      invalidOp "WebGPU failed to create the scene pipeline."

    printfn
      "Avalonia-owned render cycle: target=XID 0x%X format=%A size=%ux%u"
      (uint64 (window.TryGetPlatformHandle().Handle))
      format
      width
      height

  member _.Render() =
    if not disposed then
      let currentSize = window.ClientSize

      let nextWidth =
        max 1u (uint32 (Math.Ceiling(currentSize.Width * window.RenderScaling)))

      let nextHeight =
        max 1u (uint32 (Math.Ceiling(currentSize.Height * window.RenderScaling)))

      if nextWidth <> width || nextHeight <> height then
        width <- nextWidth
        height <- nextHeight
        let mutable configuration = SurfaceConfiguration()
        configuration.Device <- device
        configuration.Format <- format
        configuration.Usage <- TextureUsage.RenderAttachment
        configuration.AlphaMode <- alphaMode
        configuration.Width <- width
        configuration.Height <- height
        configuration.PresentMode <- presentMode
        api.SurfaceConfigure(surface, &configuration)

      let mutable current = SurfaceTexture()
      api.SurfaceGetCurrentTexture(surface, &current)

      if
        current.Status <> SurfaceGetCurrentTextureStatus.Success
        || NativePtr.toNativeInt current.Texture = 0n
      then
        invalidOp $"WebGPU surface acquisition failed inside Avalonia's render target: {current.Status}."

      if frameCount = 0 then
        printfn "First Avalonia-owned WebGPU texture status: %A" current.Status

      let view = api.TextureCreateView(current.Texture, nullPtr<TextureViewDescriptor>)

      let encoder =
        api.DeviceCreateCommandEncoder(device, nullPtr<CommandEncoderDescriptor>)

      let mutable color = RenderPassColorAttachment()
      color.View <- view
      color.LoadOp <- LoadOp.Clear
      color.StoreOp <- StoreOp.Store
      let mutable clear = Silk.NET.WebGPU.Color()
      clear.R <- 0.025
      clear.G <- 0.075
      clear.B <- 0.13
      clear.A <- 1.0
      color.ClearValue <- clear
      let colorPointer = NativePtr.stackalloc<RenderPassColorAttachment> 1
      NativePtr.write colorPointer color
      let mutable passDescriptor = RenderPassDescriptor()
      passDescriptor.ColorAttachmentCount <- unativeint 1
      passDescriptor.ColorAttachments <- colorPointer
      let pass = api.CommandEncoderBeginRenderPass(encoder, &passDescriptor)
      api.RenderPassEncoderSetPipeline(pass, pipeline)
      api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)
      api.RenderPassEncoderEnd(pass)
      let commands = api.CommandEncoderFinish(encoder, nullPtr<CommandBufferDescriptor>)
      let commandPointer = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
      NativePtr.write commandPointer commands
      api.QueueSubmit(queue, unativeint 1, commandPointer)
      api.SurfacePresent(surface)
      api.CommandBufferRelease(commands)
      api.RenderPassEncoderRelease(pass)
      api.CommandEncoderRelease(encoder)
      api.TextureViewRelease(view)
      api.TextureRelease(current.Texture)
      frameCount <- frameCount + 1

      if frameCount = 1 || frameCount % 60 = 0 then
        printfn "Avalonia render-target presents=%d" frameCount

  interface IDisposable with
    member _.Dispose() =
      if not disposed then
        disposed <- true

        if NativePtr.toNativeInt pipeline <> 0n then
          api.RenderPipelineRelease(pipeline)

        if NativePtr.toNativeInt shader <> 0n then
          api.ShaderModuleRelease(shader)

        if NativePtr.toNativeInt queue <> 0n then
          api.QueueRelease(queue)

        if NativePtr.toNativeInt surface <> 0n then
          api.SurfaceUnconfigure(surface)
          api.SurfaceRelease(surface)

        if NativePtr.toNativeInt device <> 0n then
          api.DeviceRelease(device)

        if NativePtr.toNativeInt adapter <> 0n then
          api.AdapterRelease(adapter)

        if NativePtr.toNativeInt instance <> 0n then
          api.InstanceRelease(instance)

        if display <> 0n then
          Native.xCloseDisplay (display) |> ignore

        (errorCallbackPointer :> IDisposable).Dispose()

type private ProbeDrawingContext(render: unit -> unit) =
  let mutable transform = Matrix.Identity

  interface IDrawingContextImpl with
    member _.Transform
      with get () = transform
      and set value = transform <- value

    member _.Clear _ = ()
    member _.DrawBitmap(_: IBitmapImpl, _: float, _: Rect, _: Rect) = ()
    member _.DrawBitmap(_: IBitmapImpl, _: IBrush, _: Rect, _: Rect) = ()
    member _.DrawLine(_, _, _) = ()
    member _.DrawGeometry(_, _, _) = ()
    member _.DrawRectangle(_, _, _, _) = ()
    member _.DrawRegion(_, _, _) = ()
    member _.DrawEllipse(_, _, _) = ()
    member _.DrawGlyphRun(_, _) = ()

    member _.CreateLayer _ =
      raise (NotSupportedException("The probe does not render Avalonia layers."))

    member _.PushClip(_: Rect) = ()
    member _.PushClip(_: RoundedRect) = ()
    member _.PushClip(_: IPlatformRenderInterfaceRegion) = ()
    member _.PopClip() = ()
    member _.PushLayer _ = ()
    member _.PopLayer() = ()
    member _.PushOpacity(_, _) = ()
    member _.PopOpacity() = ()
    member _.PushOpacityMask(_, _) = ()
    member _.PopOpacityMask() = ()
    member _.PushGeometryClip _ = ()
    member _.PopGeometryClip() = ()
    member _.PushRenderOptions _ = ()
    member _.PopRenderOptions() = ()
    member _.PushTextOptions _ = ()
    member _.PopTextOptions() = ()
    member _.GetFeature _ = null

  interface IDisposable with
    member _.Dispose() = render ()

type private ProbeRenderTarget(window: Window) =
  let mutable renderer: SilkFrame option = None

  interface IRenderTarget with
    member _.Properties =
      RenderTargetProperties(RetainsPreviousFrameContents = false, IsSuitableForDirectRendering = true)

    member _.PlatformRenderTargetState = PlatformRenderTargetState.Ready

    member _.CreateDrawingContext(_, properties: byref<RenderTargetDrawingContextProperties>) =
      properties <- RenderTargetDrawingContextProperties(PreviousFrameIsRetained = false)

      let current =
        match renderer with
        | Some existing -> existing
        | None ->
          let created = new SilkFrame(window)
          renderer <- Some created
          created

      new ProbeDrawingContext(current.Render) :> IDrawingContextImpl

  interface IDisposable with
    member _.Dispose() =
      renderer |> Option.iter (fun current -> (current :> IDisposable).Dispose())
      renderer <- None

type private ProbeBackendContext(window: Window) =
  interface IPlatformRenderInterfaceContext with
    member _.IsLost = false
    member _.PublicFeatures = Dictionary<Type, obj>()
    member _.MaxOffscreenRenderTargetPixelSize = Nullable<PixelSize>()

    member _.CreateRenderTarget _ =
      printfn "Custom IPlatformRenderInterfaceContext.CreateRenderTarget"
      new ProbeRenderTarget(window) :> IRenderTarget

    member _.CreateOffscreenRenderTarget(_, _, _) =
      raise (NotSupportedException("Offscreen Avalonia targets are not part of this probe."))

    member _.IsReadyToCreateRenderTarget _ = true
    member _.TryGetFeature _ = null

  interface IDisposable with
    member _.Dispose() = ()

type private ProbeRenderInterface(window: unit -> Window) =
  let unsupported name =
    raise (NotSupportedException($"Probe render interface does not implement {name}."))

  interface IPlatformRenderInterface with
    member _.SupportsIndividualRoundRects = true
    member _.DefaultAlphaFormat = AlphaFormat.Premul
    member _.DefaultPixelFormat = PixelFormats.Bgra8888
    member _.SupportsRegions = false
    member _.CreateEllipseGeometry(_: Rect) : IGeometryImpl = unsupported "ellipse geometry"
    member _.CreateLineGeometry(_: Point, _: Point) : IGeometryImpl = unsupported "line geometry"
    member _.CreateRectangleGeometry(_: Rect) : IGeometryImpl = unsupported "rectangle geometry"
    member _.CreateStreamGeometry() : IStreamGeometryImpl = unsupported "stream geometry"

    member _.CreateGeometryGroup(_: FillRule, _: IReadOnlyList<IGeometryImpl>) : IGeometryImpl =
      unsupported "geometry groups"

    member _.CreateCombinedGeometry(_: GeometryCombineMode, _: IGeometryImpl, _: IGeometryImpl) : IGeometryImpl =
      unsupported "combined geometry"

    member _.BuildGlyphRunGeometry(_: GlyphRun) : IGeometryImpl = unsupported "glyph geometry"

    member _.CreateRenderTargetBitmap(_: PixelSize, _: Vector) : IRenderTargetBitmapImpl =
      unsupported "render-target bitmaps"

    member _.CreateWriteableBitmap(_: PixelSize, _: Vector, _: PixelFormat, _: AlphaFormat) : IWriteableBitmapImpl =
      unsupported "writeable bitmaps"

    member _.LoadBitmap(_: string) : IBitmapImpl = unsupported "bitmap loading"
    member _.LoadBitmap(_: IO.Stream) : IBitmapImpl = unsupported "bitmap loading"

    member _.LoadWriteableBitmapToWidth(_: IO.Stream, _: int, _: BitmapInterpolationMode) : IWriteableBitmapImpl =
      unsupported "bitmap scaling"

    member _.LoadWriteableBitmapToHeight(_: IO.Stream, _: int, _: BitmapInterpolationMode) : IWriteableBitmapImpl =
      unsupported "bitmap scaling"

    member _.LoadWriteableBitmap(_: string) : IWriteableBitmapImpl = unsupported "writeable bitmap loading"
    member _.LoadWriteableBitmap(_: IO.Stream) : IWriteableBitmapImpl = unsupported "writeable bitmap loading"

    member _.LoadBitmapToWidth(_: IO.Stream, _: int, _: BitmapInterpolationMode) : IBitmapImpl =
      unsupported "bitmap scaling"

    member _.LoadBitmapToHeight(_: IO.Stream, _: int, _: BitmapInterpolationMode) : IBitmapImpl =
      unsupported "bitmap scaling"

    member _.ResizeBitmap(_: IBitmapImpl, _: PixelSize, _: BitmapInterpolationMode) : IBitmapImpl =
      unsupported "bitmap resizing"

    member _.LoadBitmap(_: PixelFormat, _: AlphaFormat, _: nativeint, _: PixelSize, _: Vector, _: int) : IBitmapImpl =
      unsupported "bitmap loading"

    member _.CreateGlyphRun
      (_: GlyphTypeface, _: float, _: IReadOnlyList<Avalonia.Media.TextFormatting.GlyphInfo>, _: Point)
      : IGlyphRunImpl =
      unsupported "glyph runs"

    member _.CreateBackendContext(_: IPlatformGraphicsContext) : IPlatformRenderInterfaceContext =
      printfn "Custom IPlatformRenderInterface.CreateBackendContext"
      new ProbeBackendContext(window ()) :> IPlatformRenderInterfaceContext

    member _.IsSupportedBitmapPixelFormat _ = false
    member _.CreateRegion() = unsupported "regions"

type private ProbeApp() =
  inherit Application()

  override _.OnFrameworkInitializationCompleted() =
    let window =
      Window(Title = "Avalonia-owned Silk WebGPU RenderTarget probe", Width = 900.0, Height = 580.0)

    let timer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds(16.0))
    timer.Tick.Add(fun _ -> window.InvalidateVisual())
    window.Content <- Grid()
    window.Opened.Add(fun _ -> timer.Start())
    window.Closing.Add(fun _ -> timer.Stop())
    probeWindow <- Some window

    match base.ApplicationLifetime with
    | :? IClassicDesktopStyleApplicationLifetime as desktop -> desktop.MainWindow <- window
    | _ -> ()

    base.OnFrameworkInitializationCompleted()

AppBuilder
  .Configure<ProbeApp>()
  .UsePlatformDetect()
  .AfterPlatformServicesSetup(fun _ ->
    AvaloniaLocator.CurrentMutable
      .Bind<IPlatformRenderInterface>()
      .ToConstant(ProbeRenderInterface(fun () -> probeWindow.Value))
    |> ignore)
  .StartWithClassicDesktopLifetime([||])
