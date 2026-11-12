#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open FsToolkit.ErrorHandling
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
/// transform in the renderer. Its pipelines live in the shared cache.
[<Sealed>]
type HdrOutputPass
    private
    (
        context: GpuContext,
        cache: RenderPipelineCache,
        settings: HdrLightingSettings,
        quality: ViewerQuality,
        grade: BroadcastGrade,
        outputFormat: TextureFormat
    ) =
    let api = context.Api
    // Byte sizes of the WGSL uniform structs: 12 floats for the output pass, 8 for each bloom step.
    let uniformFloats = 12
    let bloomUniformFloats = 8
    let bloomMips = max 1 quality.BloomMips
    let mutable disposed = false
    let mutable shader = NativePtr.nullPtr<ShaderModule>
    let mutable bloomShader = NativePtr.nullPtr<ShaderModule>
    let mutable layout = NativePtr.nullPtr<BindGroupLayout>
    let mutable bloomLayout = NativePtr.nullPtr<BindGroupLayout>
    let mutable pipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable bloomPipelineLayout = NativePtr.nullPtr<PipelineLayout>
    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable prefilterPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable downPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable upPipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable sampler: GpuSampler option = None
    let mutable uniform: GpuBuffer<float32> option = None
    let mutable sceneTarget: GpuTexture option = None
    let bloomTextures = ResizeArray<GpuTexture>()
    let downUniforms = ResizeArray<GpuBuffer<float32>>()
    let upUniforms = ResizeArray<GpuBuffer<float32>>()
    let downGroups = Array.create bloomMips (NativePtr.nullPtr<BindGroup>)
    let upGroups = Array.create bloomMips (NativePtr.nullPtr<BindGroup>)
    let mutable bindGroup = NativePtr.nullPtr<BindGroup>
    let mutable bloomSizes: (uint32 * uint32)[] = [||]
    let mutable cameraProjection = [| 0.1f; 500.0f; 0.5f; 1.6f |]

    let releaseGroup (group: nativeptr<BindGroup>) =
        if not (GpuPointer.isMissing group) then
            api.BindGroupRelease(group)

    let releaseTargets () =
        releaseGroup bindGroup
        bindGroup <- NativePtr.nullPtr<BindGroup>

        for index in 0 .. bloomMips - 1 do
            releaseGroup downGroups[index]
            releaseGroup upGroups[index]
            downGroups[index] <- NativePtr.nullPtr<BindGroup>
            upGroups[index] <- NativePtr.nullPtr<BindGroup>

        for texture in bloomTextures do
            (texture :> IDisposable).Dispose()

        bloomTextures.Clear()

        sceneTarget |> Option.iter (fun texture -> (texture :> IDisposable).Dispose())

        sceneTarget <- None

    let forEachMip (step: int -> Result<unit, GpuError>) : Result<unit, GpuError> =
        Seq.fold (fun state index -> Result.bind (fun () -> step index) state) (Ok()) (seq { 0 .. bloomMips - 1 })

    let target (width: uint32) (height: uint32) (label: string) =
        GpuTexture.Create(
            context,
            { Label = label
              Format = TextureFormat.Rgba16float
              Width = max 1u width
              Height = max 1u height
              Layers = 1u
              MipLevels = 1u
              Samples = 1u
              Usage = TextureUsage.RenderAttachment ||| TextureUsage.TextureBinding }
        )

    let uniformBuffer (label: string) (floats: int) =
        GpuBuffer<float32>.Create(context, label, BufferUsage.Uniform ||| BufferUsage.CopyDst, Array.zeroCreate floats)

    let pipelineFor
        (key: RenderPipelineKey)
        (label: string)
        (shaderModule: nativeptr<ShaderModule>)
        (layoutHandle: nativeptr<PipelineLayout>)
        =
        cache.GetOrAdd(key, fun () -> RenderPipelines.build context label key shaderModule layoutHandle)

    let postKey
        (shaderName: string)
        (layoutName: string)
        (entry: string)
        (format: TextureFormat)
        (blend: FragmentBlend)
        : RenderPipelineKey =
        { Shader = shaderName
          Layout = layoutName
          VertexEntry = "vs"
          Vertex = NoVertexBuffers
          Fragment =
            Some
                { Entry = entry
                  ColorFormat = format
                  Blend = blend }
          Depth = None
          Cull = CullMode.None
          Samples = 1u
          AlphaToCoverage = false }

    let writeSettings () : Result<unit, GpuError> =
        match uniform with
        | None -> Ok()
        | Some buffer ->
            buffer.Write(
                [| settings.ExposureStops
                   quality.BloomIntensity
                   (if quality.AoEnabled then quality.AoIntensity else 0.0f)
                   quality.AoRadiusMetres
                   yield! cameraProjection
                   grade.Saturation
                   grade.Contrast
                   (if quality.AoEnabled then 1.0f else 0.0f)
                   (if quality.BloomEnabled then 1.0f else 0.0f) |]
            )

            Ok()

    let initialize () : Result<unit, GpuError> =
        result {
            let! mainSource = ShaderLibrary.readFile "HdrOutput.wgsl"
            let! mainModule = ShaderModules.compile context "HdrOutput.wgsl" mainSource
            shader <- mainModule
            let! bloomSource = ShaderLibrary.readFile "Bloom.wgsl"
            let! bloomModule = ShaderModules.compile context "Bloom.wgsl" bloomSource
            bloomShader <- bloomModule

            let! postSampler =
                GpuSampler.Create(
                    context,
                    "the post-process sampler",
                    { AddressU = AddressMode.ClampToEdge
                      AddressV = AddressMode.ClampToEdge
                      AddressW = AddressMode.ClampToEdge
                      Magnification = FilterMode.Linear
                      Minification = FilterMode.Linear
                      Mipmap = MipmapFilterMode.Nearest
                      MaxAnisotropy = 1us
                      LodMaxClamp = 0.0f
                      Compare = None }
                )

            sampler <- Some postSampler

            let! outputLayout =
                BindGroupLayouts.create
                    context
                    "the HDR output layout"
                    [ BindGroupLayouts.texture
                          0u
                          ShaderStage.Fragment
                          TextureSampleType.UnfilterableFloat
                          TextureViewDimension.Dimension2D
                          false
                      BindGroupLayouts.uniform 1u ShaderStage.Fragment (uint64 (uniformFloats * 4))
                      BindGroupLayouts.depthMultisampled 2u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 3u ShaderStage.Fragment
                      BindGroupLayouts.sampler 4u ShaderStage.Fragment ]

            layout <- outputLayout

            let! outputPipelineLayout =
                BindGroupLayouts.pipelineLayout context "the HDR output pipeline layout" [ outputLayout ]

            pipelineLayout <- outputPipelineLayout

            let! bloomGroupLayout =
                BindGroupLayouts.create
                    context
                    "the bloom layout"
                    [ BindGroupLayouts.texture2d 0u ShaderStage.Fragment
                      BindGroupLayouts.sampler 1u ShaderStage.Fragment
                      BindGroupLayouts.uniform 2u ShaderStage.Fragment (uint64 (bloomUniformFloats * 4)) ]

            bloomLayout <- bloomGroupLayout

            let! bloomPipelineLayoutHandle =
                BindGroupLayouts.pipelineLayout context "the bloom pipeline layout" [ bloomGroupLayout ]

            bloomPipelineLayout <- bloomPipelineLayoutHandle

            let! outputUniform = uniformBuffer "the HDR output settings" uniformFloats
            uniform <- Some outputUniform

            do!
                forEachMip (fun _ ->
                    result {
                        let! down = uniformBuffer "a bloom downsample uniform" bloomUniformFloats
                        downUniforms.Add down
                        let! up = uniformBuffer "a bloom upsample uniform" bloomUniformFloats
                        upUniforms.Add up
                    })

            let! main =
                pipelineFor
                    (postKey "HdrOutput.wgsl" "HDR output" "fs" outputFormat Replace)
                    "the HDR output pipeline"
                    shader
                    pipelineLayout

            pipeline <- main

            let! prefilter =
                pipelineFor
                    (postKey "Bloom.wgsl" "bloom" "fsPrefilter" TextureFormat.Rgba16float Replace)
                    "the bloom prefilter"
                    bloomShader
                    bloomPipelineLayout

            prefilterPipeline <- prefilter

            let! down =
                pipelineFor
                    (postKey "Bloom.wgsl" "bloom" "fsDown" TextureFormat.Rgba16float Replace)
                    "the bloom downsample"
                    bloomShader
                    bloomPipelineLayout

            downPipeline <- down

            // Bloom steps add into the mip above them, so the upsample blends additively instead of replacing.
            let! up =
                pipelineFor
                    (postKey "Bloom.wgsl" "bloom" "fsUp" TextureFormat.Rgba16float Additive)
                    "the bloom upsample"
                    bloomShader
                    bloomPipelineLayout

            upPipeline <- up
            return! writeSettings ()
        }

    member internal _.Initialize() = initialize ()

    /// Recreates the scene target and bloom chain for a new surface size. Bind groups refer to the views, so they are
    /// rebuilt in the same pass.
    member _.Resize(width: uint32, height: uint32, depth: GpuTexture) : Result<unit, GpuError> =
        if disposed then
            raise (ObjectDisposedException(nameof HdrOutputPass))

        releaseTargets ()

        bloomSizes <-
            Array.init bloomMips (fun index -> max 1u (width >>> (index + 1)), max 1u (height >>> (index + 1)))

        let exposureScale = MathF.Pow(2.0f, settings.ExposureStops)

        target width height "the linear HDR resolve"
        |> Result.bind (fun scene ->
            sceneTarget <- Some scene

            forEachMip (fun index ->
                let bloomWidth, bloomHeight = bloomSizes[index]

                target bloomWidth bloomHeight "a bloom mip"
                |> Result.map (fun texture -> bloomTextures.Add texture)))
        |> Result.bind (fun () ->
            forEachMip (fun index ->
                // Downsample i reads the full-resolution scene (i = 0) or the previous mip; its texel is the source's.
                let sourceWidth, sourceHeight =
                    if index = 0 then width, height else bloomSizes[index - 1]

                let source =
                    if index = 0 then
                        sceneTarget.Value.View
                    else
                        bloomTextures[index - 1].View

                let downUniform = downUniforms[index]

                downUniform.Write(
                    [| quality.BloomThreshold
                       quality.BloomKnee
                       exposureScale
                       1.0f
                       1.0f / float32 sourceWidth
                       1.0f / float32 sourceHeight
                       0.0f
                       0.0f |]
                )

                let upWidth, upHeight = bloomSizes[index]
                let upUniform = upUniforms[index]

                upUniform.Write(
                    [| 0.0f
                       0.0f
                       0.0f
                       quality.BloomSpread
                       1.0f / float32 upWidth
                       1.0f / float32 upHeight
                       0.0f
                       0.0f |]
                )

                result {
                    let! downGroup =
                        BindGroups.create
                            context
                            "a bloom downsample group"
                            bloomLayout
                            [ BindGroups.view 0u source
                              BindGroups.sampler 1u sampler.Value.Handle
                              BindGroups.buffer 2u downUniform.Handle (uint64 (bloomUniformFloats * 4)) ]

                    downGroups[index] <- downGroup

                    let! upGroup =
                        BindGroups.create
                            context
                            "a bloom upsample group"
                            bloomLayout
                            [ BindGroups.view 0u bloomTextures[index].View
                              BindGroups.sampler 1u sampler.Value.Handle
                              BindGroups.buffer 2u upUniform.Handle (uint64 (bloomUniformFloats * 4)) ]

                    upGroups[index] <- upGroup
                }))
        |> Result.bind (fun () ->
            BindGroups.create
                context
                "the HDR output bind group"
                layout
                [ BindGroups.view 0u sceneTarget.Value.View
                  BindGroups.buffer 1u uniform.Value.Handle (uint64 (uniformFloats * 4))
                  BindGroups.view 2u depth.View
                  BindGroups.view 3u bloomTextures[0].View
                  BindGroups.sampler 4u sampler.Value.Handle ]
            |> Result.map (fun group -> bindGroup <- group))

    member _.SceneView =
        match sceneTarget with
        | Some texture -> texture.View
        | None -> NativePtr.nullPtr<TextureView>

    /// Projection parameters for reconstructing view-space positions from depth (ambient occlusion).
    member _.SetCamera(near: float32, far: float32, fieldOfViewY: float32, aspect: float32) =
        let next = [| near; far; MathF.Tan(fieldOfViewY * 0.5f); aspect |]

        if next <> cameraProjection then
            cameraProjection <- next
            writeSettings () |> ignore

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
                run bloomTextures[i].View (if i = 0 then prefilterPipeline else downPipeline) downGroups[i] false

            // Each smaller mip is tent-filtered and added into the next larger one: a wide, soft, energy-bounded glow.
            for i in bloomMips - 1 .. -1 .. 1 do
                run bloomTextures[i - 1].View upPipeline upGroups[i] true

    member _.Draw(pass: nativeptr<RenderPassEncoder>) =
        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderSetBindGroup(pass, 0u, bindGroup, unativeint 0, NativePtr.nullPtr<uint32>)
        api.RenderPassEncoderDraw(pass, 3u, 1u, 0u, 0u)

    static member Create
        (
            context: GpuContext,
            cache: RenderPipelineCache,
            settings: HdrLightingSettings,
            quality: ViewerQuality,
            grade: BroadcastGrade,
            outputFormat: TextureFormat
        ) : Result<HdrOutputPass, GpuError> =
        match HdrLighting.validate settings with
        | Error error -> invalidArg (nameof settings) error
        | Ok _ -> ()

        if
            outputFormat <> TextureFormat.Bgra8UnormSrgb
            && outputFormat <> TextureFormat.Rgba8UnormSrgb
        then
            invalidArg (nameof outputFormat) "HDR presentation requires an sRGB surface for linear-light HUD blending."

        let pass = new HdrOutputPass(context, cache, settings, quality, grade, outputFormat)

        match pass.Initialize() with
        | Ok() -> Ok pass
        | Error error ->
            (pass :> IDisposable).Dispose()
            Error error

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                releaseTargets ()

                if not (GpuPointer.isMissing bloomPipelineLayout) then
                    api.PipelineLayoutRelease(bloomPipelineLayout)

                if not (GpuPointer.isMissing pipelineLayout) then
                    api.PipelineLayoutRelease(pipelineLayout)

                if not (GpuPointer.isMissing bloomLayout) then
                    api.BindGroupLayoutRelease(bloomLayout)

                if not (GpuPointer.isMissing layout) then
                    api.BindGroupLayoutRelease(layout)

                if not (GpuPointer.isMissing bloomShader) then
                    api.ShaderModuleRelease(bloomShader)

                if not (GpuPointer.isMissing shader) then
                    api.ShaderModuleRelease(shader)

                for buffer in downUniforms do
                    (buffer :> IDisposable).Dispose()

                for buffer in upUniforms do
                    (buffer :> IDisposable).Dispose()

                uniform |> Option.iter (fun buffer -> (buffer :> IDisposable).Dispose())
                sampler |> Option.iter (fun value -> (value :> IDisposable).Dispose())
