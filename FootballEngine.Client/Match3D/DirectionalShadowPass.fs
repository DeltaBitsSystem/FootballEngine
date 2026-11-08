#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open FsToolkit.ErrorHandling
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Keeps caster attachments separate from receiver bindings so a depth texture is never read while being written.
[<Sealed>]
type DirectionalShadowPass
    private
    (
        context: GpuContext,
        settings: DirectionalShadowSettings,
        depth: GpuTexture,
        staticDepth: GpuTexture,
        sampler: GpuSampler,
        receiverLayout: nativeptr<BindGroupLayout>,
        receiverGroup: nativeptr<BindGroup>,
        casterLayout: nativeptr<PipelineLayout>,
        pipeline: nativeptr<RenderPipeline>
    ) =
    let api = context.Api
    let mutable disposed = false

    static member Create
        (
            context: GpuContext,
            cache: RenderPipelineCache,
            shader: string * nativeptr<ShaderModule>,
            settings: DirectionalShadowSettings,
            cameraLayout: nativeptr<BindGroupLayout>
        ) : Result<DirectionalShadowPass, GpuError> =
        let api = context.Api

        let depthSpec: TextureSpec =
            { Label = "the directional shadow texture"
              Format = TextureFormat.Depth32float
              Width = settings.Resolution
              Height = settings.Resolution
              Layers = 1u
              MipLevels = 1u
              Samples = 1u
              Usage =
                TextureUsage.RenderAttachment
                ||| TextureUsage.TextureBinding
                ||| TextureUsage.CopyDst }

        // The stadium does not move. Copy its cached depth instead of rasterizing a million triangles every frame.
        let staticSpec: TextureSpec =
            { depthSpec with
                Label = "the static shadow depth cache"
                Usage = TextureUsage.RenderAttachment ||| TextureUsage.CopySrc }

        result {
            let! depth = GpuTexture.Create(context, depthSpec)
            let! staticDepth = GpuTexture.Create(context, staticSpec)

            let! sampler =
                GpuSampler.Create(
                    context,
                    "the shadow comparison sampler",
                    { AddressU = AddressMode.ClampToEdge
                      AddressV = AddressMode.ClampToEdge
                      AddressW = AddressMode.ClampToEdge
                      Magnification = FilterMode.Linear
                      Minification = FilterMode.Linear
                      Mipmap = MipmapFilterMode.Nearest
                      MaxAnisotropy = 1us
                      LodMaxClamp = 32.0f
                      Compare = Some CompareFunction.LessEqual }
                )

            let! receiverLayout =
                BindGroupLayouts.create
                    context
                    "the shadow receiver layout"
                    [ BindGroupLayouts.texture
                          0u
                          ShaderStage.Fragment
                          TextureSampleType.Depth
                          TextureViewDimension.Dimension2D
                          false
                      BindGroupLayouts.comparisonSampler 1u ShaderStage.Fragment ]

            let! receiverGroup =
                let bindings = NativePtr.stackalloc<BindGroupEntry> 2
                let mutable textureBinding = BindGroupEntry()
                textureBinding.Binding <- 0u
                textureBinding.TextureView <- depth.View
                NativePtr.write bindings textureBinding
                let mutable samplerBinding = BindGroupEntry()
                samplerBinding.Binding <- 1u
                samplerBinding.Sampler <- sampler.Handle
                NativePtr.write (NativePtr.add bindings 1) samplerBinding
                let mutable descriptor = BindGroupDescriptor()
                descriptor.Layout <- receiverLayout
                descriptor.EntryCount <- unativeint 2
                descriptor.Entries <- bindings
                let group = api.DeviceCreateBindGroup(context.Device, &descriptor)

                if GpuPointer.isMissing group then
                    Error(CreationFailed "the shadow receiver bindings")
                else
                    Ok group

            let! casterLayout =
                BindGroupLayouts.pipelineLayout context "the shadow caster pipeline layout" [ cameraLayout ]

            let shaderName, shaderModule = shader

            // The caster reuses the stadium module through its shadow entry point and has no fragment stage.
            let key: RenderPipelineKey =
                { Shader = shaderName
                  Layout = "shadow caster"
                  VertexEntry = "shadowVs"
                  Vertex = GpuVertexFull
                  Fragment = None
                  Depth =
                    Some
                        { DepthFormat = TextureFormat.Depth32float
                          WriteDepth = true
                          Compare = CompareFunction.Less
                          // Raster bias handles slopes; the shader adds a smaller receiver-normal offset for contact stability.
                          Bias = 1
                          SlopeScale = 1.5f }
                  Cull = CullMode.None
                  Samples = 1u
                  AlphaToCoverage = false }

            let! pipeline =
                cache.GetOrAdd(
                    key,
                    fun () ->
                        RenderPipelines.build context "the directional shadow pipeline" key shaderModule casterLayout
                )

            return
                new DirectionalShadowPass(
                    context,
                    settings,
                    depth,
                    staticDepth,
                    sampler,
                    receiverLayout,
                    receiverGroup,
                    casterLayout,
                    pipeline
                )
        }

    member _.ReceiverLayout = receiverLayout
    member _.ReceiverGroup = receiverGroup

    member _.CopyStatic(encoder: nativeptr<CommandEncoder>) =
        if disposed then
            raise (ObjectDisposedException(nameof DirectionalShadowPass))

        let mutable source = ImageCopyTexture()
        source.Texture <- staticDepth.Handle
        source.Aspect <- TextureAspect.DepthOnly
        let mutable destination = ImageCopyTexture()
        destination.Texture <- depth.Handle
        destination.Aspect <- TextureAspect.DepthOnly
        let mutable extent = Extent3D(settings.Resolution, settings.Resolution, 1u)
        api.CommandEncoderCopyTextureToTexture(encoder, &source, &destination, &extent)

    member _.Begin(encoder: nativeptr<CommandEncoder>, cameraGroup: nativeptr<BindGroup>, staticCasters: bool) =
        if disposed then
            raise (ObjectDisposedException(nameof DirectionalShadowPass))

        let mutable attachment = RenderPassDepthStencilAttachment()
        attachment.View <- if staticCasters then staticDepth.View else depth.View
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
        member _.Dispose() =
            if not disposed then
                disposed <- true

                api.PipelineLayoutRelease(casterLayout)

                api.BindGroupRelease(receiverGroup)
                api.BindGroupLayoutRelease(receiverLayout)
                (sampler :> IDisposable).Dispose()
                (staticDepth :> IDisposable).Dispose()
                (depth :> IDisposable).Dispose()
