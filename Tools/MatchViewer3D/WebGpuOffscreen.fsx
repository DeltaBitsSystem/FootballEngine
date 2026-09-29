#r "nuget: Silk.NET.WebGPU, 2.23.0"
#r "nuget: Silk.NET.WebGPU.Native.WGPU, 2.23.0"
#r "nuget: SixLabors.ImageSharp, 3.1.12"
#nowarn "9"

open System
open System.IO
open System.Threading
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU
open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats

// This wgpu-native release aborts on InstanceProcessEvents; its native device poll
// is needed to deliver the buffer-map callback after the GPU finishes drawing.
module Native =
    [<DllImport("libwgpu_native.so", EntryPoint = "wgpuDevicePoll", CallingConvention = CallingConvention.Cdecl)>]
    extern byte devicePoll(nativeint device, byte wait, nativeint index)

let private nullPtr<'a when 'a: unmanaged> = NativePtr.nullPtr<'a>

let run (output: string) =
    let api = WebGPU.GetApi()
    let voidNull = NativePtr.toVoidPtr (nullPtr<byte>)
    let instance = api.CreateInstance(nullPtr<InstanceDescriptor>)

    if NativePtr.toNativeInt instance = 0n then
        failwith "WebGPU instance unavailable"

    let mutable adapter = nullPtr<Adapter>
    let adapterCallback = RequestAdapterCallback(fun status value _ _ ->
        printfn "adapter: %A" status
        adapter <- value)
    use adapterFn = PfnRequestAdapterCallback.From(adapterCallback)
    api.InstanceRequestAdapter(instance, nullPtr<RequestAdapterOptions>, adapterFn, voidNull)

    if NativePtr.toNativeInt adapter = 0n then
        failwith "WebGPU adapter unavailable"

    let mutable device = nullPtr<Device>
    let deviceCallback = RequestDeviceCallback(fun status value _ _ ->
        printfn "device: %A" status
        device <- value)
    use deviceFn = PfnRequestDeviceCallback.From(deviceCallback)
    api.AdapterRequestDevice(adapter, nullPtr<DeviceDescriptor>, deviceFn, voidNull)

    if NativePtr.toNativeInt device = 0n then
        failwith "WebGPU device unavailable"

    let queue = api.DeviceGetQueue(device)
    let width, height = 512u, 320u
    let mutable textureDesc = TextureDescriptor()
    textureDesc.Usage <- TextureUsage.RenderAttachment ||| TextureUsage.CopySrc
    textureDesc.Dimension <- TextureDimension.Dimension2D
    textureDesc.Size <- Extent3D(Width = width, Height = height, DepthOrArrayLayers = 1u)
    textureDesc.Format <- TextureFormat.Rgba8Unorm
    textureDesc.MipLevelCount <- 1u
    textureDesc.SampleCount <- 1u
    let texture = api.DeviceCreateTexture(device, &textureDesc)
    let view = api.TextureCreateView(texture, nullPtr<TextureViewDescriptor>)

    let shaderText =
        """@vertex fn vs(@builtin(vertex_index) id: u32) -> @builtin(position) vec4<f32> {
    var points = array<vec3<f32>, 3>(vec3<f32>(-0.78,-0.7,-2.0), vec3<f32>(0.78,-0.7,-2.0), vec3<f32>(0.0,0.85,-3.0));
    let p = points[id];
    return vec4<f32>(p.xy * 1.6, -p.z * 0.5, -p.z);
}
@fragment fn fs() -> @location(0) vec4<f32> { return vec4<f32>(0.11,0.86,0.62,1.0); }
"""

    let shaderBytes = Text.Encoding.UTF8.GetBytes(shaderText + "\u0000")
    use shaderPtr = fixed shaderBytes
    let mutable wgsl = ShaderModuleWGSLDescriptor()
    wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
    wgsl.Code <- shaderPtr
    let wgslPtr = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
    NativePtr.write wgslPtr wgsl
    let mutable shaderDesc = ShaderModuleDescriptor()
    shaderDesc.NextInChain <- NativePtr.ofNativeInt<ChainedStruct>(NativePtr.toNativeInt wgslPtr)
    let shader = api.DeviceCreateShaderModule(device, &shaderDesc)
    let vertexBytes = Text.Encoding.UTF8.GetBytes("vs\u0000")
    let fragmentBytes = Text.Encoding.UTF8.GetBytes("fs\u0000")
    use vertexPtr = fixed vertexBytes
    use fragmentPtr = fixed fragmentBytes
    let mutable vertex = VertexState()
    vertex.Module <- shader
    vertex.EntryPoint <- vertexPtr
    let mutable target = ColorTargetState()
    target.Format <- TextureFormat.Rgba8Unorm
    target.WriteMask <- ColorWriteMask.All
    let targetPtr = NativePtr.stackalloc<ColorTargetState> 1
    NativePtr.write targetPtr target
    let mutable fragment = FragmentState()
    fragment.Module <- shader
    fragment.EntryPoint <- fragmentPtr
    fragment.TargetCount <- unativeint 1
    fragment.Targets <- targetPtr
    let fragmentStatePtr = NativePtr.stackalloc<FragmentState> 1
    NativePtr.write fragmentStatePtr fragment
    let mutable pipelineDesc = RenderPipelineDescriptor()
    pipelineDesc.Vertex <- vertex
    pipelineDesc.Fragment <- fragmentStatePtr
    pipelineDesc.Primitive.Topology <- PrimitiveTopology.TriangleList
    pipelineDesc.Multisample.Count <- 1u
    pipelineDesc.Multisample.Mask <- UInt32.MaxValue
    let pipeline = api.DeviceCreateRenderPipeline(device, &pipelineDesc)

    let bytesPerRow = width * 4u
    let mutable bufferDesc = BufferDescriptor()
    bufferDesc.Size <- uint64 (bytesPerRow * height)
    bufferDesc.Usage <- BufferUsage.CopyDst ||| BufferUsage.MapRead
    let buffer = api.DeviceCreateBuffer(device, &bufferDesc)
    let encoder = api.DeviceCreateCommandEncoder(device, nullPtr<CommandEncoderDescriptor>)
    let mutable attachment = RenderPassColorAttachment()
    attachment.View <- view
    attachment.LoadOp <- LoadOp.Clear
    attachment.StoreOp <- StoreOp.Store
    let mutable clear = Silk.NET.WebGPU.Color()
    clear.R <- 0.05
    clear.G <- 0.25
    clear.B <- 0.17
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

    let mutable src = ImageCopyTexture()
    src.Texture <- texture
    src.Aspect <- TextureAspect.All
    let mutable dst = ImageCopyBuffer()
    dst.Buffer <- buffer
    dst.Layout.BytesPerRow <- bytesPerRow
    dst.Layout.RowsPerImage <- height
    let mutable extent = Extent3D(Width = width, Height = height, DepthOrArrayLayers = 1u)
    api.CommandEncoderCopyTextureToBuffer(encoder, &src, &dst, &extent)
    let commands = api.CommandEncoderFinish(encoder, nullPtr<CommandBufferDescriptor>)
    let commandPtr = NativePtr.stackalloc<nativeptr<CommandBuffer>> 1
    NativePtr.write commandPtr commands
    api.QueueSubmit(queue, unativeint 1, commandPtr)

    let mutable mapped, succeeded = false, false
    let mappedCallback = BufferMapCallback(fun status _ ->
        succeeded <- status = BufferMapAsyncStatus.Success
        mapped <- true)
    use mapFn = PfnBufferMapCallback.From(mappedCallback)
    api.BufferMapAsync(buffer, MapMode.Read, unativeint 0, unativeint (bytesPerRow * height), mapFn, voidNull)
    let deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10.0)

    while not mapped && DateTime.UtcNow < deadline do
        Native.devicePoll(NativePtr.toNativeInt device, 0uy, 0n) |> ignore
        Thread.Sleep 10

    if not succeeded then
        failwith "WebGPU readback failed"

    let rgba = Array.zeroCreate<byte> (int (bytesPerRow * height))
    let data = api.BufferGetMappedRange(buffer, unativeint 0, unativeint rgba.Length)
    Marshal.Copy(NativePtr.toNativeInt (NativePtr.ofVoidPtr<byte> data), rgba, 0, rgba.Length)
    api.BufferUnmap(buffer)
    let pixels = rgba |> Array.chunkBySize 4
    let isTriangle (p: byte[]) = p[1] > 150uy && p[2] > 100uy
    let count = pixels |> Array.filter isTriangle |> Array.length

    if count < 1000 then
        failwithf "WebGPU geometry missing: %d triangle pixels" count

    use image = Image.LoadPixelData<Rgba32>(rgba, int width, int height)
    image.SaveAsPng(output)
    printfn "WebGPU triangle pixels=%d, image=%s" count output

    api.CommandBufferRelease(commands)
    api.CommandEncoderRelease(encoder)
    api.BufferRelease(buffer)
    api.RenderPipelineRelease(pipeline)
    api.ShaderModuleRelease(shader)
    api.TextureViewRelease(view)
    api.TextureRelease(texture)
    api.QueueRelease(queue)
    api.DeviceRelease(device)
    api.AdapterRelease(adapter)
    api.InstanceRelease(instance)

let output =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [| path |] -> Path.GetFullPath(path)
    | _ -> Path.Combine(Path.GetTempPath(), "fe-webgpu-offscreen.png")

run output
