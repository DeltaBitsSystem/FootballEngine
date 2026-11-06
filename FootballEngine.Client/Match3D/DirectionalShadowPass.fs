#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Keeps caster attachments separate from receiver bindings so a depth texture is never read while being written.
[<Sealed>]
type DirectionalShadowPass
    (
        api: WebGPU,
        device: nativeptr<Device>,
        settings: DirectionalShadowSettings,
        cameraLayout: nativeptr<BindGroupLayout>
    ) =
    let mutable texture = NativePtr.nullPtr<Texture>
    let mutable view = NativePtr.nullPtr<TextureView>
    let mutable staticTexture = NativePtr.nullPtr<Texture>
    let mutable staticView = NativePtr.nullPtr<TextureView>
    let mutable sampler = NativePtr.nullPtr<Sampler>
    let mutable layout = NativePtr.nullPtr<BindGroupLayout>
    let mutable group = NativePtr.nullPtr<BindGroup>
    let mutable pipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable disposed = false

    let check label pointer =
        if NativePtr.toNativeInt pointer = 0n then
            invalidOp $"WebGPU could not create {label}."

    let dispose () =
        if not disposed then
            disposed <- true

            if NativePtr.toNativeInt pipeline <> 0n then
                api.RenderPipelineRelease(pipeline)

            if NativePtr.toNativeInt pipelineLayout <> 0n then
                api.PipelineLayoutRelease(pipelineLayout)

            if NativePtr.toNativeInt group <> 0n then
                api.BindGroupRelease(group)

            if NativePtr.toNativeInt layout <> 0n then
                api.BindGroupLayoutRelease(layout)

            if NativePtr.toNativeInt sampler <> 0n then
                api.SamplerRelease(sampler)

            if NativePtr.toNativeInt view <> 0n then
                api.TextureViewRelease(view)

            if NativePtr.toNativeInt staticView <> 0n then
                api.TextureViewRelease(staticView)

            if NativePtr.toNativeInt staticTexture <> 0n then
                api.TextureDestroy(staticTexture)
                api.TextureRelease(staticTexture)

            if NativePtr.toNativeInt texture <> 0n then
                api.TextureDestroy(texture)
                api.TextureRelease(texture)

    let initialize () =
        let mutable descriptor = TextureDescriptor()
        descriptor.Dimension <- TextureDimension.Dimension2D
        descriptor.Size <- Extent3D(settings.Resolution, settings.Resolution, 1u)
        descriptor.Format <- TextureFormat.Depth32float
        descriptor.MipLevelCount <- 1u
        descriptor.SampleCount <- 1u

        descriptor.Usage <-
            TextureUsage.RenderAttachment
            ||| TextureUsage.TextureBinding
            ||| TextureUsage.CopyDst

        texture <- api.DeviceCreateTexture(device, &descriptor)
        check "the directional shadow texture" texture
        view <- api.TextureCreateView(texture, NativePtr.nullPtr<TextureViewDescriptor>)
        check "the directional shadow view" view
        // The stadium does not move. Copy its cached depth instead of rasterizing a million triangles every frame.
        descriptor.Usage <- TextureUsage.RenderAttachment ||| TextureUsage.CopySrc
        staticTexture <- api.DeviceCreateTexture(device, &descriptor)
        check "the static shadow depth cache" staticTexture
        staticView <- api.TextureCreateView(staticTexture, NativePtr.nullPtr<TextureViewDescriptor>)
        check "the static shadow depth view" staticView

        let mutable samplerDescriptor = SamplerDescriptor()
        samplerDescriptor.AddressModeU <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeV <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeW <- AddressMode.ClampToEdge
        samplerDescriptor.MagFilter <- FilterMode.Linear
        samplerDescriptor.MinFilter <- FilterMode.Linear
        samplerDescriptor.MipmapFilter <- MipmapFilterMode.Nearest
        samplerDescriptor.Compare <- CompareFunction.LessEqual
        samplerDescriptor.MaxAnisotropy <- 1us
        sampler <- api.DeviceCreateSampler(device, &samplerDescriptor)
        check "the shadow comparison sampler" sampler

        let entries = NativePtr.stackalloc<BindGroupLayoutEntry> 2
        let mutable textureEntry = BindGroupLayoutEntry()
        textureEntry.Binding <- 0u
        textureEntry.Visibility <- ShaderStage.Fragment
        textureEntry.Texture.SampleType <- TextureSampleType.Depth
        textureEntry.Texture.ViewDimension <- TextureViewDimension.Dimension2D
        NativePtr.write entries textureEntry
        let mutable samplerEntry = BindGroupLayoutEntry()
        samplerEntry.Binding <- 1u
        samplerEntry.Visibility <- ShaderStage.Fragment
        samplerEntry.Sampler.Type <- SamplerBindingType.Comparison
        NativePtr.write (NativePtr.add entries 1) samplerEntry
        let mutable layoutDescriptor = BindGroupLayoutDescriptor()
        layoutDescriptor.EntryCount <- unativeint 2
        layoutDescriptor.Entries <- entries
        layout <- api.DeviceCreateBindGroupLayout(device, &layoutDescriptor)
        check "the shadow receiver layout" layout

        let bindings = NativePtr.stackalloc<BindGroupEntry> 2
        let mutable binding = BindGroupEntry()
        binding.Binding <- 0u
        binding.TextureView <- view
        NativePtr.write bindings binding
        binding <- BindGroupEntry()
        binding.Binding <- 1u
        binding.Sampler <- sampler
        NativePtr.write (NativePtr.add bindings 1) binding
        let mutable groupDescriptor = BindGroupDescriptor()
        groupDescriptor.Layout <- layout
        groupDescriptor.EntryCount <- unativeint 2
        groupDescriptor.Entries <- bindings
        group <- api.DeviceCreateBindGroup(device, &groupDescriptor)
        check "the shadow receiver bindings" group

        let cameraLayoutPointer = NativePtr.stackalloc<nativeptr<BindGroupLayout>> 1
        NativePtr.write cameraLayoutPointer cameraLayout
        let mutable pipelineLayoutDescriptor = PipelineLayoutDescriptor()
        pipelineLayoutDescriptor.BindGroupLayoutCount <- unativeint 1
        pipelineLayoutDescriptor.BindGroupLayouts <- cameraLayoutPointer
        pipelineLayout <- api.DeviceCreatePipelineLayout(device, &pipelineLayoutDescriptor)
        check "the shadow caster pipeline layout" pipelineLayout

    do
        try
            initialize ()
        with _ ->
            dispose ()
            reraise ()

    member _.ReceiverLayout = layout
    member _.ReceiverGroup = group

    member _.CreatePipeline(vertex: VertexState, depth: DepthStencilState) =
        if disposed then
            raise (ObjectDisposedException(nameof DirectionalShadowPass))

        if NativePtr.toNativeInt pipeline <> 0n then
            invalidOp "The shadow pipeline is already initialized."

        let entryBytes = Text.Encoding.UTF8.GetBytes("shadowVs\u0000")
        use entry = fixed entryBytes
        let mutable shadowVertex = vertex
        shadowVertex.EntryPoint <- entry
        let mutable shadowDepth = depth
        shadowDepth.Format <- TextureFormat.Depth32float
        // Raster bias handles slopes; the shader adds a smaller receiver-normal offset for contact stability.
        shadowDepth.DepthBias <- 1
        shadowDepth.DepthBiasSlopeScale <- 1.5f
        let depthPointer = NativePtr.stackalloc<DepthStencilState> 1
        NativePtr.write depthPointer shadowDepth
        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Layout <- pipelineLayout
        descriptor.Vertex <- shadowVertex
        descriptor.DepthStencil <- depthPointer
        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Primitive.CullMode <- CullMode.None
        descriptor.Multisample.Count <- 1u
        descriptor.Multisample.Mask <- UInt32.MaxValue
        pipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)
        check "the directional shadow pipeline" pipeline

    member _.CopyStatic(encoder: nativeptr<CommandEncoder>) =
        let mutable source = ImageCopyTexture()
        source.Texture <- staticTexture
        source.Aspect <- TextureAspect.DepthOnly
        let mutable destination = ImageCopyTexture()
        destination.Texture <- texture
        destination.Aspect <- TextureAspect.DepthOnly
        let extent = Extent3D(settings.Resolution, settings.Resolution, 1u)
        api.CommandEncoderCopyTextureToTexture(encoder, &source, &destination, &extent)

    member _.Begin(encoder: nativeptr<CommandEncoder>, cameraGroup: nativeptr<BindGroup>, staticCasters: bool) =
        if disposed then
            raise (ObjectDisposedException(nameof DirectionalShadowPass))

        let mutable attachment = RenderPassDepthStencilAttachment()
        attachment.View <- if staticCasters then staticView else view
        attachment.DepthLoadOp <- if staticCasters then LoadOp.Clear else LoadOp.Load
        attachment.DepthStoreOp <- StoreOp.Store
        attachment.DepthClearValue <- 1.0f
        let attachmentPointer = NativePtr.stackalloc<RenderPassDepthStencilAttachment> 1
        NativePtr.write attachmentPointer attachment
        let mutable descriptor = RenderPassDescriptor()
        descriptor.DepthStencilAttachment <- attachmentPointer
        let pass = api.CommandEncoderBeginRenderPass(encoder, &descriptor)
        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderSetBindGroup(pass, 0u, cameraGroup, unativeint 0, NativePtr.nullPtr<uint32>)
        pass

    interface IDisposable with
        member _.Dispose() = dispose ()
