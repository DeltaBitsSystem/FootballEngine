#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.IO
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Owns the scene-linear resolve and the only exposure/display transform in the renderer.
[<Sealed>]
type HdrOutputPass
    (
        api: WebGPU,
        device: nativeptr<Device>,
        queue: nativeptr<Queue>,
        outputFormat: TextureFormat,
        settings: HdrLightingSettings
    ) =
    let mutable texture = NativePtr.nullPtr<Texture>
    let mutable view = NativePtr.nullPtr<TextureView>
    let mutable bindGroup = NativePtr.nullPtr<BindGroup>
    let mutable layout = NativePtr.nullPtr<BindGroupLayout>
    let mutable pipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable shader = NativePtr.nullPtr<ShaderModule>
    let mutable uniform = NativePtr.nullPtr<Buffer>
    let mutable disposed = false

    let check label pointer =
        if NativePtr.toNativeInt pointer = 0n then
            invalidOp $"WebGPU could not create {label}."

    let releaseTargets () =
        if NativePtr.toNativeInt bindGroup <> 0n then
            api.BindGroupRelease(bindGroup)
            bindGroup <- NativePtr.nullPtr<BindGroup>

        if NativePtr.toNativeInt view <> 0n then
            api.TextureViewRelease(view)
            view <- NativePtr.nullPtr<TextureView>

        if NativePtr.toNativeInt texture <> 0n then
            api.TextureDestroy(texture)
            api.TextureRelease(texture)
            texture <- NativePtr.nullPtr<Texture>

    let dispose () =
        if not disposed then
            disposed <- true
            releaseTargets ()

            if NativePtr.toNativeInt pipeline <> 0n then
                api.RenderPipelineRelease(pipeline)

            if NativePtr.toNativeInt pipelineLayout <> 0n then
                api.PipelineLayoutRelease(pipelineLayout)

            if NativePtr.toNativeInt layout <> 0n then
                api.BindGroupLayoutRelease(layout)

            if NativePtr.toNativeInt shader <> 0n then
                api.ShaderModuleRelease(shader)

            if NativePtr.toNativeInt uniform <> 0n then
                api.BufferRelease(uniform)

    let initialize () =
        match HdrLighting.validate settings with
        | Error error -> invalidArg (nameof settings) error
        | Ok _ -> ()

        if
            outputFormat <> TextureFormat.Bgra8UnormSrgb
            && outputFormat <> TextureFormat.Rgba8UnormSrgb
        then
            invalidArg (nameof outputFormat) "HDR presentation requires an sRGB surface for linear-light HUD blending."

        let path =
            Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", "HdrOutput.wgsl")

        let bytes = Text.Encoding.UTF8.GetBytes(File.ReadAllText(path) + "\u0000")
        use pointer = fixed bytes
        let mutable wgsl = ShaderModuleWGSLDescriptor()
        wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
        wgsl.Code <- pointer
        let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
        NativePtr.write wgslPointer wgsl
        let mutable descriptor = ShaderModuleDescriptor()
        descriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPointer)
        shader <- api.DeviceCreateShaderModule(device, &descriptor)
        check "the HDR output shader" shader
        let entries = NativePtr.stackalloc<BindGroupLayoutEntry> 2
        let mutable imageEntry = BindGroupLayoutEntry()
        imageEntry.Binding <- 0u
        imageEntry.Visibility <- ShaderStage.Fragment
        imageEntry.Texture.SampleType <- TextureSampleType.UnfilterableFloat
        imageEntry.Texture.ViewDimension <- TextureViewDimension.Dimension2D
        NativePtr.write entries imageEntry
        let mutable uniformEntry = BindGroupLayoutEntry()
        uniformEntry.Binding <- 1u
        uniformEntry.Visibility <- ShaderStage.Fragment
        uniformEntry.Buffer.Type <- BufferBindingType.Uniform
        uniformEntry.Buffer.MinBindingSize <- 16UL
        NativePtr.write (NativePtr.add entries 1) uniformEntry
        let mutable groupDescriptor = BindGroupLayoutDescriptor()
        groupDescriptor.EntryCount <- unativeint 2
        groupDescriptor.Entries <- entries
        layout <- api.DeviceCreateBindGroupLayout(device, &groupDescriptor)
        check "the HDR output layout" layout
        let layouts = NativePtr.stackalloc<nativeptr<BindGroupLayout>> 1
        NativePtr.write layouts layout
        let mutable pipelineLayoutDescriptor = PipelineLayoutDescriptor()
        pipelineLayoutDescriptor.BindGroupLayoutCount <- unativeint 1
        pipelineLayoutDescriptor.BindGroupLayouts <- layouts
        pipelineLayout <- api.DeviceCreatePipelineLayout(device, &pipelineLayoutDescriptor)
        check "the HDR output pipeline layout" pipelineLayout
        let mutable bufferDescriptor = BufferDescriptor()
        bufferDescriptor.Size <- 16UL
        bufferDescriptor.Usage <- BufferUsage.Uniform ||| BufferUsage.CopyDst
        uniform <- api.DeviceCreateBuffer(device, &bufferDescriptor)
        check "the HDR output settings" uniform
        let data = [| BitConverter.SingleToUInt32Bits(settings.ExposureStops); 0u; 0u; 0u |]
        use dataPointer = fixed data
        api.QueueWriteBuffer(queue, uniform, 0UL, NativePtr.toVoidPtr dataPointer, unativeint 16)
        let vertexName = Text.Encoding.UTF8.GetBytes("vs\u0000")
        let fragmentName = Text.Encoding.UTF8.GetBytes("fs\u0000")
        use vertexPointer = fixed vertexName
        use fragmentPointer = fixed fragmentName
        let mutable target = ColorTargetState()
        target.Format <- outputFormat
        target.WriteMask <- ColorWriteMask.All
        let targets = NativePtr.stackalloc<ColorTargetState> 1
        NativePtr.write targets target
        let mutable fragment = FragmentState()
        fragment.Module <- shader
        fragment.EntryPoint <- fragmentPointer
        fragment.TargetCount <- unativeint 1
        fragment.Targets <- targets
        let fragments = NativePtr.stackalloc<FragmentState> 1
        NativePtr.write fragments fragment
        let mutable pipelineDescriptor = RenderPipelineDescriptor()
        pipelineDescriptor.Layout <- pipelineLayout
        pipelineDescriptor.Vertex.Module <- shader
        pipelineDescriptor.Vertex.EntryPoint <- vertexPointer
        pipelineDescriptor.Fragment <- fragments
        pipelineDescriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        pipelineDescriptor.Multisample.Count <- 1u
        pipelineDescriptor.Multisample.Mask <- UInt32.MaxValue
        pipeline <- api.DeviceCreateRenderPipeline(device, &pipelineDescriptor)
        check "the HDR output pipeline" pipeline

    do
        try
            initialize ()
        with _ ->
            dispose ()
            reraise ()

    member _.Resize(width: uint32, height: uint32) =
        if disposed then
            raise (ObjectDisposedException(nameof HdrOutputPass))

        releaseTargets ()
        let mutable descriptor = TextureDescriptor()
        descriptor.Size <- Extent3D(width, height, 1u)
        descriptor.Dimension <- TextureDimension.Dimension2D
        descriptor.Format <- TextureFormat.Rgba16float
        descriptor.Usage <- TextureUsage.RenderAttachment ||| TextureUsage.TextureBinding
        descriptor.MipLevelCount <- 1u
        descriptor.SampleCount <- 1u
        texture <- api.DeviceCreateTexture(device, &descriptor)
        check "the linear HDR resolve" texture
        view <- api.TextureCreateView(texture, NativePtr.nullPtr<TextureViewDescriptor>)
        check "the linear HDR resolve view" view
        let entries = NativePtr.stackalloc<BindGroupEntry> 2
        let mutable imageEntry = BindGroupEntry()
        imageEntry.Binding <- 0u
        imageEntry.TextureView <- view
        NativePtr.write entries imageEntry
        let mutable uniformEntry = BindGroupEntry()
        uniformEntry.Binding <- 1u
        uniformEntry.Buffer <- uniform
        uniformEntry.Size <- 16UL
        NativePtr.write (NativePtr.add entries 1) uniformEntry
        let mutable groupDescriptor = BindGroupDescriptor()
        groupDescriptor.Layout <- layout
        groupDescriptor.EntryCount <- unativeint 2
        groupDescriptor.Entries <- entries
        bindGroup <- api.DeviceCreateBindGroup(device, &groupDescriptor)
        check "the HDR output bind group" bindGroup

    member _.SceneView = view

    member _.Draw(pass: nativeptr<RenderPassEncoder>) =
        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderSetBindGroup(pass, 0u, bindGroup, unativeint 0, NativePtr.nullPtr<uint32>)
        api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)

    interface IDisposable with
        member _.Dispose() = dispose ()
