#r "nuget: Silk.NET.WebGPU, 2.23.0"
#r "nuget: Silk.NET.WebGPU.Native.WGPU, 2.23.0"
#nowarn "9"

open System
open System.Runtime.InteropServices
open System.Threading
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

module private Native =
  [<DllImport("libX11.so.6", EntryPoint = "XOpenDisplay", CallingConvention = CallingConvention.Cdecl)>]
  extern nativeint xOpenDisplay(nativeint name)

  [<DllImport("libX11.so.6", EntryPoint = "XDefaultScreen", CallingConvention = CallingConvention.Cdecl)>]
  extern int xDefaultScreen(nativeint display)

  [<DllImport("libX11.so.6", EntryPoint = "XRootWindow", CallingConvention = CallingConvention.Cdecl)>]
  extern unativeint xRootWindow(nativeint display, int screen)

  [<DllImport("libX11.so.6", EntryPoint = "XCreateSimpleWindow", CallingConvention = CallingConvention.Cdecl)>]
  extern unativeint xCreateSimpleWindow(
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

  [<DllImport("libX11.so.6",
              EntryPoint = "XStoreName",
              CallingConvention = CallingConvention.Cdecl,
              CharSet = CharSet.Ansi)>]
  extern int xStoreName(nativeint display, unativeint window, string name)

  [<DllImport("libX11.so.6", EntryPoint = "XMapWindow", CallingConvention = CallingConvention.Cdecl)>]
  extern int xMapWindow(nativeint display, unativeint window)

  [<DllImport("libX11.so.6", EntryPoint = "XFlush", CallingConvention = CallingConvention.Cdecl)>]
  extern int xFlush(nativeint display)

  [<DllImport("libX11.so.6", EntryPoint = "XDestroyWindow", CallingConvention = CallingConvention.Cdecl)>]
  extern int xDestroyWindow(nativeint display, unativeint window)

  [<DllImport("libX11.so.6", EntryPoint = "XCloseDisplay", CallingConvention = CallingConvention.Cdecl)>]
  extern int xCloseDisplay(nativeint display)

  [<DllImport("libwgpu_native.so", EntryPoint = "wgpuDevicePoll", CallingConvention = CallingConvention.Cdecl)>]
  extern byte devicePoll(nativeint device, byte wait, nativeint index)

let private nullPtr<'a when 'a: unmanaged> = NativePtr.nullPtr<'a>
let private voidNull = NativePtr.toVoidPtr (nullPtr<byte>)

type private RenderTarget =
  { Parent: unativeint option
    X: int
    Y: int
    Width: uint32
    Height: uint32 }

type private Renderer(target: RenderTarget) =
  let api = WebGPU.GetApi()
  let mutable display = 0n
  let mutable window = 0un
  let mutable instance = nullPtr<Instance>
  let mutable surface = nullPtr<Surface>
  let mutable adapter = nullPtr<Adapter>
  let mutable device = nullPtr<Device>
  let mutable queue = nullPtr<Queue>
  let mutable shader = nullPtr<ShaderModule>
  let mutable pipeline = nullPtr<RenderPipeline>
  let mutable disposed = false
  let mutable frames = 0
  let mutable targetFormat = TextureFormat.Undefined

  let errorCallback =
    ErrorCallback(fun errorType message _ ->
      eprintfn "WebGPU %A: %s" errorType (Marshal.PtrToStringUTF8(NativePtr.toNativeInt message)))

  let errorCallbackPointer = PfnErrorCallback.From(errorCallback)

  let requestAdapter () =
    let mutable requested = nullPtr<Adapter>

    let callback =
      RequestAdapterCallback(fun status value _ _ ->
        printfn "Adapter callback: %A" status

        if status = RequestAdapterStatus.Success then
          requested <- value)

    use callbackPointer = PfnRequestAdapterCallback.From(callback)
    let mutable options = RequestAdapterOptions()
    options.CompatibleSurface <- surface
    api.InstanceRequestAdapter(instance, &options, callbackPointer, voidNull)

    if NativePtr.toNativeInt requested = 0n then
      invalidOp "No WebGPU adapter for the Xlib surface."

    requested

  let requestDevice () =
    let mutable requested = nullPtr<Device>

    let callback =
      RequestDeviceCallback(fun status value _ _ ->
        if status = RequestDeviceStatus.Success then
          requested <- value)

    use callbackPointer = PfnRequestDeviceCallback.From(callback)
    api.AdapterRequestDevice(adapter, nullPtr<DeviceDescriptor>, callbackPointer, voidNull)

    if NativePtr.toNativeInt requested = 0n then
      invalidOp "WebGPU device creation failed."

    requested

  let configureSurface () =
    let mutable caps = SurfaceCapabilities()
    api.SurfaceGetCapabilities(surface, adapter, &caps)

    let formats =
      Array.init (int caps.FormatCount) (fun index -> NativePtr.read (NativePtr.add caps.Formats index))

    let alphas =
      Array.init (int caps.AlphaModeCount) (fun index -> NativePtr.read (NativePtr.add caps.AlphaModes index))

    let modes =
      Array.init (int caps.PresentModeCount) (fun index -> NativePtr.read (NativePtr.add caps.PresentModes index))

    let preferred = api.SurfaceGetPreferredFormat(surface, adapter)

    targetFormat <-
      [| TextureFormat.Bgra8Unorm; TextureFormat.Rgba8Unorm; preferred |]
      |> Array.tryFind (fun format -> Array.contains format formats)
      |> Option.defaultWith (fun () -> formats[0])

    let alpha =
      if Array.contains CompositeAlphaMode.Opaque alphas then
        CompositeAlphaMode.Opaque
      else
        alphas[0]

    let present =
      if Array.contains PresentMode.Fifo modes then
        PresentMode.Fifo
      else
        modes[0]

    api.SurfaceCapabilitiesFreeMembers(caps)
    let mutable config = SurfaceConfiguration()
    config.Device <- device
    config.Format <- targetFormat
    config.Usage <- TextureUsage.RenderAttachment
    config.AlphaMode <- alpha
    config.Width <- target.Width
    config.Height <- target.Height
    config.PresentMode <- present
    api.SurfaceConfigure(surface, &config)
    printfn "Xlib-owned surface: XID=0x%X format=%A alpha=%A present=%A" (uint64 window) targetFormat alpha present

  let createPipeline () =
    let source =
      """@vertex fn vs(@builtin(vertex_index) id: u32) -> @builtin(position) vec4<f32> {
    var points = array<vec2<f32>, 3>(vec2<f32>(-1.0,-1.0), vec2<f32>(3.0,-1.0), vec2<f32>(-1.0,3.0));
    return vec4<f32>(points[id], 0.0, 1.0);
}
@fragment fn fs() -> @location(0) vec4<f32> { return vec4<f32>(0.04, 0.65, 0.32, 1.0); }
"""

    let shaderBytes = Text.Encoding.UTF8.GetBytes(source + "\u0000")
    use sourcePtr = fixed shaderBytes
    let mutable wgsl = ShaderModuleWGSLDescriptor()
    wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
    wgsl.Code <- sourcePtr
    let wgslPtr = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
    NativePtr.write wgslPtr wgsl
    let mutable shaderDesc = ShaderModuleDescriptor()
    shaderDesc.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPtr)
    shader <- api.DeviceCreateShaderModule(device, &shaderDesc)
    let vertexEntryBytes = Text.Encoding.UTF8.GetBytes("vs\u0000")
    let fragmentEntryBytes = Text.Encoding.UTF8.GetBytes("fs\u0000")
    use vertexEntry = fixed vertexEntryBytes
    use fragmentEntry = fixed fragmentEntryBytes
    let mutable vertex = VertexState()
    vertex.Module <- shader
    vertex.EntryPoint <- vertexEntry
    let mutable colorTarget = ColorTargetState()
    colorTarget.Format <- targetFormat
    colorTarget.WriteMask <- ColorWriteMask.All
    let targetPtr = NativePtr.stackalloc<ColorTargetState> 1
    NativePtr.write targetPtr colorTarget
    let mutable fragment = FragmentState()
    fragment.Module <- shader
    fragment.EntryPoint <- fragmentEntry
    fragment.TargetCount <- unativeint 1
    fragment.Targets <- targetPtr
    let fragmentPtr = NativePtr.stackalloc<FragmentState> 1
    NativePtr.write fragmentPtr fragment
    let mutable desc = RenderPipelineDescriptor()
    desc.Vertex <- vertex
    desc.Fragment <- fragmentPtr
    desc.Primitive.Topology <- PrimitiveTopology.TriangleList
    desc.Multisample.Count <- 1u
    desc.Multisample.Mask <- UInt32.MaxValue
    pipeline <- api.DeviceCreateRenderPipeline(device, &desc)

    if NativePtr.toNativeInt pipeline = 0n then
      invalidOp "WebGPU pipeline creation failed."

  do
    display <- Native.xOpenDisplay (0n)

    if display = 0n then
      invalidOp "XOpenDisplay failed."

    let parent =
      target.Parent
      |> Option.defaultValue (Native.xRootWindow (display, Native.xDefaultScreen (display)))

    window <-
      Native.xCreateSimpleWindow (display, parent, target.X, target.Y, target.Width, target.Height, 0u, 0un, 0un)

    Native.xStoreName (display, window, "FootballEngine Xlib WebGPU window probe")
    |> ignore

    Native.xMapWindow (display, window) |> ignore
    Native.xFlush (display) |> ignore
    instance <- api.CreateInstance(nullPtr<InstanceDescriptor>)
    let mutable xlib = SurfaceDescriptorFromXlibWindow()
    xlib.Chain.SType <- SType.SurfaceDescriptorFromXlibWindow
    xlib.Display <- NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> display)
    xlib.Window <- uint64 window
    let xlibPtr = NativePtr.stackalloc<SurfaceDescriptorFromXlibWindow> 1
    NativePtr.write xlibPtr xlib
    let mutable surfaceDesc = SurfaceDescriptor()
    surfaceDesc.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt xlibPtr)
    surface <- api.InstanceCreateSurface(instance, &surfaceDesc)
    adapter <- requestAdapter ()
    device <- requestDevice ()
    queue <- api.DeviceGetQueue(device)
    api.DeviceSetUncapturedErrorCallback(device, errorCallbackPointer, voidNull)
    configureSurface ()
    createPipeline ()

    printfn
      "Xlib window target: parent=%s position=%d,%d size=%ux%u"
      (target.Parent |> Option.map (sprintf "0x%X") |> Option.defaultValue "root")
      target.X
      target.Y
      target.Width
      target.Height

  member _.Render() =
    if not disposed then
      let mutable frame = SurfaceTexture()
      api.SurfaceGetCurrentTexture(surface, &frame)

      if
        frame.Status <> SurfaceGetCurrentTextureStatus.Success
        || NativePtr.toNativeInt frame.Texture = 0n
      then
        invalidOp $"Surface acquisition failed: {frame.Status}"

      let view = api.TextureCreateView(frame.Texture, nullPtr<TextureViewDescriptor>)

      let encoder =
        api.DeviceCreateCommandEncoder(device, nullPtr<CommandEncoderDescriptor>)

      let mutable attachment = RenderPassColorAttachment()
      attachment.View <- view
      attachment.LoadOp <- LoadOp.Clear
      attachment.StoreOp <- StoreOp.Store
      let mutable clear = Silk.NET.WebGPU.Color()
      clear.R <- 0.02
      clear.G <- 0.12
      clear.B <- 0.20
      clear.A <- 1.0
      attachment.ClearValue <- clear
      let attachmentPtr = NativePtr.stackalloc<RenderPassColorAttachment> 1
      NativePtr.write attachmentPtr attachment
      let mutable passDesc = RenderPassDescriptor()
      passDesc.ColorAttachmentCount <- unativeint 1
      passDesc.ColorAttachments <- attachmentPtr
      let pass = api.CommandEncoderBeginRenderPass(encoder, &passDesc)
      api.RenderPassEncoderSetPipeline(pass, pipeline)
      api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)
      api.RenderPassEncoderEnd(pass)
      let commands = api.CommandEncoderFinish(encoder, nullPtr<CommandBufferDescriptor>)
      let commandsPtr = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
      NativePtr.write commandsPtr commands
      api.QueueSubmit(queue, unativeint 1, commandsPtr)
      Native.devicePoll (NativePtr.toNativeInt device, 0uy, 0n) |> ignore
      api.SurfacePresent(surface)
      frames <- frames + 1

      if frames = 1 then
        printfn "Presented the first WebGPU frame."

      api.CommandBufferRelease(commands)
      api.RenderPassEncoderRelease(pass)
      api.CommandEncoderRelease(encoder)
      api.TextureViewRelease(view)
      api.TextureRelease(frame.Texture)

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

        if window <> 0un then
          Native.xDestroyWindow (display, window) |> ignore

        if display <> 0n then
          Native.xCloseDisplay (display) |> ignore

        (errorCallbackPointer :> IDisposable).Dispose()

let run () =
  let values =
    fsi.CommandLineArgs
    |> Array.skip 1
    |> Array.choose (fun value ->
      match Int32.TryParse value with
      | true, parsed -> Some parsed
      | _ -> None)

  let target =
    match values with
    | [| parent; x; y; width; height |] ->
      { Parent = Some(unativeint parent)
        X = x
        Y = y
        Width = uint32 width
        Height = uint32 height }
    | _ ->
      { Parent = None
        X = 80
        Y = 90
        Width = 900u
        Height = 580u }

  use renderer = new Renderer(target)

  while true do
    renderer.Render()
    Thread.Sleep(16)

run ()
