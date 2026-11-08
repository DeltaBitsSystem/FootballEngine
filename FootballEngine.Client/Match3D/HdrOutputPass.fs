#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.IO
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Broadcast look applied in scene-linear light before the display curve. Neutral is (1, 1).
type BroadcastGrade =
    { Saturation: float32
      Contrast: float32 }

module BroadcastGrade =
    // A touch of saturation and contrast is what a TV camera pipeline adds; more would make kits look painted.
    let standard = { Saturation = 1.06f; Contrast = 1.04f }

/// Owns the scene-linear resolve, the post chain (ambient occlusion, bloom, grade) and the only exposure/display
/// transform in the renderer.
[<Sealed>]
type HdrOutputPass
    (
        api: WebGPU,
        device: nativeptr<Device>,
        queue: nativeptr<Queue>,
        outputFormat: TextureFormat,
        settings: HdrLightingSettings,
        quality: ViewerQuality,
        grade: BroadcastGrade
    ) =
    let uniformBytes = 48UL
    let bloomUniformBytes = 32UL
    let bloomMips = max 1 quality.BloomMips
    let mutable texture = NativePtr.nullPtr<Texture>
    let mutable view = NativePtr.nullPtr<TextureView>
    let mutable bindGroup = NativePtr.nullPtr<BindGroup>
    let mutable layout = NativePtr.nullPtr<BindGroupLayout>
    let mutable pipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable shader = NativePtr.nullPtr<ShaderModule>
    let mutable uniform = NativePtr.nullPtr<Buffer>
    let mutable sampler = NativePtr.nullPtr<Sampler>
    let mutable bloomShader = NativePtr.nullPtr<ShaderModule>
    let mutable bloomLayout = NativePtr.nullPtr<BindGroupLayout>
    let mutable bloomPipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable prefilterPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable downPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable upPipeline = NativePtr.nullPtr<RenderPipeline>
    let bloomTextures = Array.create bloomMips (NativePtr.nullPtr<Texture>)
    let bloomViews = Array.create bloomMips (NativePtr.nullPtr<TextureView>)
    let downGroups = Array.create bloomMips (NativePtr.nullPtr<BindGroup>)
    let upGroups = Array.create bloomMips (NativePtr.nullPtr<BindGroup>)
    let downUniforms = Array.create bloomMips (NativePtr.nullPtr<Buffer>)
    let upUniforms = Array.create bloomMips (NativePtr.nullPtr<Buffer>)
    let mutable bloomSizes: (uint32 * uint32)[] = [||]
    let mutable disposed = false

    let check label pointer =
        if NativePtr.toNativeInt pointer = 0n then
            invalidOp $"WebGPU could not create {label}."

    let releaseTargets () =
        let releaseGroup (g: nativeptr<BindGroup>) =
            if NativePtr.toNativeInt g <> 0n then
                api.BindGroupRelease(g)

        releaseGroup bindGroup
        bindGroup <- NativePtr.nullPtr<BindGroup>

        for i in 0 .. bloomMips - 1 do
            releaseGroup downGroups[i]
            releaseGroup upGroups[i]
            downGroups[i] <- NativePtr.nullPtr<BindGroup>
            upGroups[i] <- NativePtr.nullPtr<BindGroup>

            if NativePtr.toNativeInt bloomViews[i] <> 0n then
                api.TextureViewRelease(bloomViews[i])
                bloomViews[i] <- NativePtr.nullPtr<TextureView>

            if NativePtr.toNativeInt bloomTextures[i] <> 0n then
                api.TextureDestroy(bloomTextures[i])
                api.TextureRelease(bloomTextures[i])
                bloomTextures[i] <- NativePtr.nullPtr<Texture>

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

            for p in [ pipeline; prefilterPipeline; downPipeline; upPipeline ] do
                if NativePtr.toNativeInt p <> 0n then
                    api.RenderPipelineRelease(p)

            for l in [ pipelineLayout; bloomPipelineLayout ] do
                if NativePtr.toNativeInt l <> 0n then
                    api.PipelineLayoutRelease(l)

            for l in [ layout; bloomLayout ] do
                if NativePtr.toNativeInt l <> 0n then
                    api.BindGroupLayoutRelease(l)

            for s in [ shader; bloomShader ] do
                if NativePtr.toNativeInt s <> 0n then
                    api.ShaderModuleRelease(s)

            for b in Array.append [| uniform |] (Array.append downUniforms upUniforms) do
                if NativePtr.toNativeInt b <> 0n then
                    api.BufferRelease(b)

            if NativePtr.toNativeInt sampler <> 0n then
                api.SamplerRelease(sampler)

    let loadShader (file: string) =
        let path = Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders", file)
        let bytes = Text.Encoding.UTF8.GetBytes(File.ReadAllText(path) + "\u0000")
        use pointer = fixed bytes
        let mutable wgsl = ShaderModuleWGSLDescriptor()
        wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
        wgsl.Code <- pointer
        let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
        NativePtr.write wgslPointer wgsl
        let mutable descriptor = ShaderModuleDescriptor()
        descriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPointer)
        let m = api.DeviceCreateShaderModule(device, &descriptor)
        check $"the {file} shader" m
        m

    let createBuffer size label =
        let mutable d = BufferDescriptor()
        d.Size <- size
        d.Usage <- BufferUsage.Uniform ||| BufferUsage.CopyDst
        let b = api.DeviceCreateBuffer(device, &d)
        check label b
        b

    let writeFloats (buffer: nativeptr<Buffer>) (values: float32[]) =
        use p = fixed values
        api.QueueWriteBuffer(queue, buffer, 0UL, NativePtr.toVoidPtr p, unativeint (values.Length * 4))

    let createPipeline
        (pipelineLayout: nativeptr<PipelineLayout>)
        (m: nativeptr<ShaderModule>)
        (fragmentEntry: string)
        (format: TextureFormat)
        (additive: bool)
        label
        =
        let vertexName = Text.Encoding.UTF8.GetBytes("vs\u0000")
        let fragmentName = Text.Encoding.UTF8.GetBytes(fragmentEntry + "\u0000")
        use vertexPointer = fixed vertexName
        use fragmentPointer = fixed fragmentName
        let blend = NativePtr.stackalloc<BlendState> 1
        let mutable target = ColorTargetState()
        target.Format <- format
        target.WriteMask <- ColorWriteMask.All

        if additive then
            let mutable component = BlendComponent()
            component.Operation <- BlendOperation.Add
            component.SrcFactor <- BlendFactor.One
            component.DstFactor <- BlendFactor.One
            let mutable state = BlendState()
            state.Color <- component
            state.Alpha <- component
            NativePtr.write blend state
            target.Blend <- blend

        let targets = NativePtr.stackalloc<ColorTargetState> 1
        NativePtr.write targets target
        let mutable fragment = FragmentState()
        fragment.Module <- m
        fragment.EntryPoint <- fragmentPointer
        fragment.TargetCount <- unativeint 1
        fragment.Targets <- targets
        let fragments = NativePtr.stackalloc<FragmentState> 1
        NativePtr.write fragments fragment
        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Layout <- pipelineLayout
        descriptor.Vertex.Module <- m
        descriptor.Vertex.EntryPoint <- vertexPointer
        descriptor.Fragment <- fragments
        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Multisample.Count <- 1u
        descriptor.Multisample.Mask <- UInt32.MaxValue
        let p = api.DeviceCreateRenderPipeline(device, &descriptor)
        check label p
        p

    let createLayout (entries: BindGroupLayoutEntry[]) label =
        let pointer = NativePtr.stackalloc<BindGroupLayoutEntry> entries.Length

        entries |> Array.iteri (fun i e -> NativePtr.write (NativePtr.add pointer i) e)

        let mutable d = BindGroupLayoutDescriptor()
        d.EntryCount <- unativeint entries.Length
        d.Entries <- pointer
        let l = api.DeviceCreateBindGroupLayout(device, &d)
        check label l
        let layouts = NativePtr.stackalloc<nativeptr<BindGroupLayout>> 1
        NativePtr.write layouts l
        let mutable pd = PipelineLayoutDescriptor()
        pd.BindGroupLayoutCount <- unativeint 1
        pd.BindGroupLayouts <- layouts
        let pl = api.DeviceCreatePipelineLayout(device, &pd)
        check (label + " pipeline layout") pl
        l, pl

    let textureEntry binding sampleType dimension multisampled =
        let mutable e = BindGroupLayoutEntry()
        e.Binding <- binding
        e.Visibility <- ShaderStage.Fragment
        e.Texture.SampleType <- sampleType
        e.Texture.ViewDimension <- dimension
        e.Texture.Multisampled <- Silk.NET.Core.Bool32(if multisampled then 1u else 0u)
        e

    let uniformEntry binding size =
        let mutable e = BindGroupLayoutEntry()
        e.Binding <- binding
        e.Visibility <- ShaderStage.Fragment
        e.Buffer.Type <- BufferBindingType.Uniform
        e.Buffer.MinBindingSize <- size
        e

    let samplerEntry binding =
        let mutable e = BindGroupLayoutEntry()
        e.Binding <- binding
        e.Visibility <- ShaderStage.Fragment
        e.Sampler.Type <- SamplerBindingType.Filtering
        e

    let createGroup (l: nativeptr<BindGroupLayout>) (entries: BindGroupEntry[]) label =
        let pointer = NativePtr.stackalloc<BindGroupEntry> entries.Length

        entries |> Array.iteri (fun i e -> NativePtr.write (NativePtr.add pointer i) e)

        let mutable d = BindGroupDescriptor()
        d.Layout <- l
        d.EntryCount <- unativeint entries.Length
        d.Entries <- pointer
        let g = api.DeviceCreateBindGroup(device, &d)
        check label g
        g

    let viewEntry binding (v: nativeptr<TextureView>) =
        let mutable e = BindGroupEntry()
        e.Binding <- binding
        e.TextureView <- v
        e

    let bufferEntry binding (b: nativeptr<Buffer>) size =
        let mutable e = BindGroupEntry()
        e.Binding <- binding
        e.Buffer <- b
        e.Size <- size
        e

    let samplerBinding binding =
        let mutable e = BindGroupEntry()
        e.Binding <- binding
        e.Sampler <- sampler
        e

    let initialize () =
        match HdrLighting.validate settings with
        | Error error -> invalidArg (nameof settings) error
        | Ok _ -> ()

        if
            outputFormat <> TextureFormat.Bgra8UnormSrgb
            && outputFormat <> TextureFormat.Rgba8UnormSrgb
        then
            invalidArg (nameof outputFormat) "HDR presentation requires an sRGB surface for linear-light HUD blending."

        shader <- loadShader "HdrOutput.wgsl"
        bloomShader <- loadShader "Bloom.wgsl"

        let mutable samplerDescriptor = SamplerDescriptor()
        samplerDescriptor.AddressModeU <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeV <- AddressMode.ClampToEdge
        samplerDescriptor.AddressModeW <- AddressMode.ClampToEdge
        samplerDescriptor.MagFilter <- FilterMode.Linear
        samplerDescriptor.MinFilter <- FilterMode.Linear
        samplerDescriptor.MipmapFilter <- MipmapFilterMode.Nearest
        samplerDescriptor.MaxAnisotropy <- 1us
        sampler <- api.DeviceCreateSampler(device, &samplerDescriptor)
        check "the post-process sampler" sampler

        let l, pl =
            createLayout
                [| textureEntry 0u TextureSampleType.UnfilterableFloat TextureViewDimension.Dimension2D false
                   uniformEntry 1u uniformBytes
                   textureEntry 2u TextureSampleType.Depth TextureViewDimension.Dimension2D true
                   textureEntry 3u TextureSampleType.Float TextureViewDimension.Dimension2D false
                   samplerEntry 4u |]
                "the HDR output layout"

        layout <- l
        pipelineLayout <- pl

        let bl, bpl =
            createLayout
                [| textureEntry 0u TextureSampleType.Float TextureViewDimension.Dimension2D false
                   samplerEntry 1u
                   uniformEntry 2u bloomUniformBytes |]
                "the bloom layout"

        bloomLayout <- bl
        bloomPipelineLayout <- bpl
        uniform <- createBuffer uniformBytes "the HDR output settings"

        for i in 0 .. bloomMips - 1 do
            downUniforms[i] <- createBuffer bloomUniformBytes "a bloom downsample uniform"
            upUniforms[i] <- createBuffer bloomUniformBytes "a bloom upsample uniform"

        pipeline <- createPipeline pipelineLayout shader "fs" outputFormat false "the HDR output pipeline"

        prefilterPipeline <-
            createPipeline
                bloomPipelineLayout
                bloomShader
                "fsPrefilter"
                TextureFormat.Rgba16float
                false
                "the bloom prefilter"

        downPipeline <-
            createPipeline
                bloomPipelineLayout
                bloomShader
                "fsDown"
                TextureFormat.Rgba16float
                false
                "the bloom downsample"

        upPipeline <-
            createPipeline bloomPipelineLayout bloomShader "fsUp" TextureFormat.Rgba16float true "the bloom upsample"

    let mutable cameraProjection = [| 0.1f; 500.0f; 0.5f; 1.6f |]

    let writeSettings () =
        writeFloats
            uniform
            [| settings.ExposureStops
               quality.BloomIntensity
               (if quality.AoEnabled then quality.AoIntensity else 0.0f)
               quality.AoRadiusMetres
               yield! cameraProjection
               grade.Saturation
               grade.Contrast
               (if quality.AoEnabled then 1.0f else 0.0f)
               (if quality.BloomEnabled then 1.0f else 0.0f) |]

    do
        try
            initialize ()
            writeSettings ()
        with _ ->
            dispose ()
            reraise ()

    member _.Resize(width: uint32, height: uint32, depthView: nativeptr<TextureView>) =
        if disposed then
            raise (ObjectDisposedException(nameof HdrOutputPass))

        releaseTargets ()

        let createTarget (w: uint32) (h: uint32) label =
            let mutable descriptor = TextureDescriptor()
            descriptor.Size <- Extent3D(max 1u w, max 1u h, 1u)
            descriptor.Dimension <- TextureDimension.Dimension2D
            descriptor.Format <- TextureFormat.Rgba16float
            descriptor.Usage <- TextureUsage.RenderAttachment ||| TextureUsage.TextureBinding
            descriptor.MipLevelCount <- 1u
            descriptor.SampleCount <- 1u
            let t = api.DeviceCreateTexture(device, &descriptor)
            check label t
            let v = api.TextureCreateView(t, NativePtr.nullPtr<TextureViewDescriptor>)
            check (label + " view") v
            t, v

        let t, v = createTarget width height "the linear HDR resolve"
        texture <- t
        view <- v
        bloomSizes <- Array.init bloomMips (fun i -> max 1u (width >>> (i + 1)), max 1u (height >>> (i + 1)))

        for i in 0 .. bloomMips - 1 do
            let w, h = bloomSizes[i]
            let bt, bv = createTarget w h "a bloom mip"
            bloomTextures[i] <- bt
            bloomViews[i] <- bv

        let exposureScale = MathF.Pow(2.0f, settings.ExposureStops)

        for i in 0 .. bloomMips - 1 do
            // Downsample i reads the full-resolution scene (i = 0) or the previous mip; its texel is the source's.
            let sw, sh = if i = 0 then width, height else bloomSizes[i - 1]
            let source = if i = 0 then view else bloomViews[i - 1]

            writeFloats
                downUniforms[i]
                [| quality.BloomThreshold
                   quality.BloomKnee
                   exposureScale
                   1.0f
                   1.0f / float32 sw
                   1.0f / float32 sh
                   0.0f
                   0.0f |]

            downGroups[i] <-
                createGroup
                    bloomLayout
                    [| viewEntry 0u source
                       samplerBinding 1u
                       bufferEntry 2u downUniforms[i] bloomUniformBytes |]
                    "a bloom downsample group"

            let uw, uh = bloomSizes[i]

            writeFloats
                upUniforms[i]
                [| 0.0f
                   0.0f
                   0.0f
                   quality.BloomSpread
                   1.0f / float32 uw
                   1.0f / float32 uh
                   0.0f
                   0.0f |]

            upGroups[i] <-
                createGroup
                    bloomLayout
                    [| viewEntry 0u bloomViews[i]
                       samplerBinding 1u
                       bufferEntry 2u upUniforms[i] bloomUniformBytes |]
                    "a bloom upsample group"

        bindGroup <-
            createGroup
                layout
                [| viewEntry 0u view
                   bufferEntry 1u uniform uniformBytes
                   viewEntry 2u depthView
                   viewEntry 3u bloomViews[0]
                   samplerBinding 4u |]
                "the HDR output bind group"

    member _.SceneView = view

    /// Projection parameters for reconstructing view-space positions from depth (ambient occlusion).
    member _.SetCamera(near: float32, far: float32, fieldOfViewY: float32, aspect: float32) =
        let next = [| near; far; MathF.Tan(fieldOfViewY * 0.5f); aspect |]

        if next <> cameraProjection then
            cameraProjection <- next
            writeSettings ()

    member _.EncodeBloom(encoder: nativeptr<CommandEncoder>) =
        if quality.BloomEnabled && bloomSizes.Length > 0 then
            let run (target: nativeptr<TextureView>) (p: nativeptr<RenderPipeline>) (g: nativeptr<BindGroup>) load =
                let mutable attachment = RenderPassColorAttachment()
                attachment.View <- target
                attachment.LoadOp <- (if load then LoadOp.Load else LoadOp.Clear)
                attachment.StoreOp <- StoreOp.Store
                attachment.ClearValue <- Silk.NET.WebGPU.Color()
                let pointer = NativePtr.stackalloc<RenderPassColorAttachment> 1
                NativePtr.write pointer attachment
                let mutable d = RenderPassDescriptor()
                d.ColorAttachmentCount <- unativeint 1
                d.ColorAttachments <- pointer
                let pass = api.CommandEncoderBeginRenderPass(encoder, &d)
                api.RenderPassEncoderSetPipeline(pass, p)
                api.RenderPassEncoderSetBindGroup(pass, 0u, g, unativeint 0, NativePtr.nullPtr<uint32>)
                api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)
                api.RenderPassEncoderEnd(pass)
                api.RenderPassEncoderRelease(pass)

            for i in 0 .. bloomMips - 1 do
                run bloomViews[i] (if i = 0 then prefilterPipeline else downPipeline) downGroups[i] false

            // Each smaller mip is tent-filtered and added into the next larger one: a wide, soft, energy-bounded glow.
            for i in bloomMips - 1 .. -1 .. 1 do
                run bloomViews[i - 1] upPipeline upGroups[i] true

    member _.Draw(pass: nativeptr<RenderPassEncoder>) =
        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderSetBindGroup(pass, 0u, bindGroup, unativeint 0, NativePtr.nullPtr<uint32>)
        api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)

    interface IDisposable with
        member _.Dispose() = dispose ()
