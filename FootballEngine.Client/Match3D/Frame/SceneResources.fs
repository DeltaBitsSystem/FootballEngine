#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.Numerics
open FsToolkit.ErrorHandling
open Silk.NET.WebGPU

type SceneResourceInputs =
    { Environment: HdrEnvironmentData
      Surfaces: SurfaceTexturePack
      Character: CharacterAssetData
      KitTextures: KitTextureData
      Shadow: DirectionalShadowSettings
      Samples: uint32
      DepthFormat: TextureFormat }

/// Everything the opaque scene shader binds: one camera/joint uniform set, the material textures, the shared
/// stadium pipeline and the passes compiled from the same module. Built once; per frame only `Upload` runs.
[<Sealed>]
type SceneResources
    private
    (
        owner: GpuOwner,
        cameraBuffer: GpuBuffer<float32>,
        jointBuffer: GpuBuffer<Matrix4x4>,
        cameraGroup: nativeptr<BindGroup>,
        pipeline: nativeptr<RenderPipeline>,
        blendedPipeline: nativeptr<RenderPipeline>,
        kitInstances: GpuBuffer<Vector4>,
        shadows: DirectionalShadowPass,
        grass: ProceduralScenePass,
        sky: ProceduralScenePass
    ) =
    static let sampler addressU addressVW anisotropy lodMax : SamplerSpec =
        { AddressU = addressU
          AddressV = addressVW
          AddressW = addressVW
          Magnification = FilterMode.Linear
          Minification = FilterMode.Linear
          Mipmap = MipmapFilterMode.Linear
          MaxAnisotropy = anisotropy
          LodMaxClamp = lodMax
          Compare = None }

    static let sampled label format width height layers mips : TextureSpec =
        { Label = label
          Format = format
          Width = uint32 width
          Height = uint32 height
          Layers = uint32 layers
          MipLevels = uint32 mips
          Samples = 1u
          Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }

    static let uploadCharacterLevels (texture: GpuTexture) (data: CharacterTextureData) =
        for level = 0 to data.Levels.Length - 1 do
            let mip = data.Levels[level]

            texture.UploadLevel(
                uint32 level,
                uint32 mip.Width,
                uint32 mip.Height,
                1u,
                uint32 (mip.Width * 4),
                mip.Pixels,
                0,
                mip.Pixels.Length
            )

    static let uploadEnvironment
        (context: GpuContext)
        (owner: GpuOwner)
        (pixels: byte[], width: int, height: int, layers: int, format: TextureFormat, bytesPerPixel: int)
        =
        GpuTexture.Create(context, sampled "a floating-point IBL texture" format width height layers 1)
        |> Result.map (fun texture ->
            owner.Own texture |> ignore

            texture.UploadLevel(
                0u,
                uint32 width,
                uint32 height,
                uint32 layers,
                uint32 (width * bytesPerPixel),
                pixels,
                0,
                pixels.Length
            )

            texture)

    static let uploadSurface
        (context: GpuContext)
        (owner: GpuOwner)
        (surfaces: SurfaceTexturePack)
        (format: TextureFormat, pixels: byte[])
        =
        GpuTexture.Create(
            context,
            sampled "a surface material array" format surfaces.Size surfaces.Size surfaces.Layers surfaces.Levels
        )
        |> Result.map (fun texture ->
            owner.Own texture |> ignore
            let mutable offset = 0

            for level = 0 to surfaces.Levels - 1 do
                let width = surfaces.Size >>> level
                let bytes = width * width * surfaces.Layers * 4

                texture.UploadLevel(
                    uint32 level,
                    uint32 width,
                    uint32 width,
                    uint32 surfaces.Layers,
                    uint32 (width * 4),
                    pixels,
                    offset,
                    bytes
                )

                offset <- offset + bytes

            texture)

    static let uploadKit
        (context: GpuContext)
        (owner: GpuOwner)
        (kit: KitTextureData)
        (format: TextureFormat, pixels: byte[])
        =
        GpuTexture.Create(
            context,
            sampled "a stadium kit material array" format kit.Size kit.Size kit.Layers kit.Levels
        )
        |> Result.map (fun texture ->
            owner.Own texture |> ignore
            let mutable offset = 0

            for level = 0 to kit.Levels - 1 do
                let width = kit.Size >>> level
                let bytes = width * width * kit.Layers * 4

                texture.UploadLevel(
                    uint32 level,
                    uint32 width,
                    uint32 width,
                    uint32 kit.Layers,
                    uint32 (width * 4),
                    pixels,
                    offset,
                    bytes
                )

                offset <- offset + bytes

            texture)

    static let build
        (context: GpuContext)
        (cache: RenderPipelineCache)
        (inputs: SceneResourceInputs)
        (owner: GpuOwner)
        =
        result {
            let api = context.Api
            let environment = inputs.Environment
            let surfaces = inputs.Surfaces
            let character = inputs.Character

            let! environmentTextures =
                [ environment.Environment,
                  environment.Width,
                  environment.Height,
                  environment.Layers,
                  TextureFormat.Rgba16float,
                  8
                  environment.Brdf, environment.LutSize, environment.LutSize, 1, TextureFormat.RG16float, 4
                  environment.Sky, environment.SkyWidth, environment.SkyHeight, 1, TextureFormat.Rgba16float, 8 ]
                |> List.traverseResultM (uploadEnvironment context owner)
                |> Result.map Array.ofList

            let! environmentSampler =
                GpuSampler.Create(
                    context,
                    "the HDR environment sampler",
                    sampler AddressMode.Repeat AddressMode.ClampToEdge 1us 1.0f
                )

            owner.Own environmentSampler |> ignore

            let! surfaceTextures =
                [ TextureFormat.Rgba8UnormSrgb, surfaces.Albedo
                  TextureFormat.Rgba8Unorm, surfaces.Normal
                  TextureFormat.Rgba8Unorm, surfaces.Arm ]
                |> List.traverseResultM (uploadSurface context owner surfaces)
                |> Result.map Array.ofList

            let! surfaceSampler =
                GpuSampler.Create(
                    context,
                    "the surface material sampler",
                    sampler AddressMode.Repeat AddressMode.Repeat 8us (float32 (surfaces.Levels - 1))
                )

            owner.Own surfaceSampler |> ignore

            let! kitTextures =
                [ TextureFormat.Rgba8UnormSrgb, inputs.KitTextures.Albedo
                  TextureFormat.Rgba8Unorm, inputs.KitTextures.Normal
                  TextureFormat.Rgba8Unorm, inputs.KitTextures.Orm ]
                |> List.traverseResultM (uploadKit context owner inputs.KitTextures)
                |> Result.map Array.ofList

            let! kitInstances =
                GpuBuffer<Vector4>
                    .Create(
                        context,
                        "stadium kit instances",
                        BufferUsage.Storage ||| BufferUsage.CopyDst,
                        Array.zeroCreate<Vector4> (StadiumGeometry.instanceCapacity * StadiumGeometry.instanceVectors)
                    )

            owner.Own kitInstances |> ignore
            let atlas = character.Albedo.Levels[0]

            let! albedo =
                GpuTexture.Create(
                    context,
                    sampled
                        "the athlete albedo atlas"
                        TextureFormat.Rgba8UnormSrgb
                        atlas.Width
                        atlas.Height
                        1
                        character.Albedo.Levels.Length
                )

            owner.Own albedo |> ignore
            uploadCharacterLevels albedo character.Albedo

            let! detail =
                GpuTexture.Create(
                    context,
                    sampled
                        "the athlete normal/roughness atlas"
                        TextureFormat.Rgba8Unorm
                        atlas.Width
                        atlas.Height
                        1
                        character.Albedo.Levels.Length
                )

            owner.Own detail |> ignore
            uploadCharacterLevels detail character.NormalRoughness

            // Stop before atlas tiles collapse into one another; mip filtering suppresses distant fabric shimmer.
            let! characterSampler =
                GpuSampler.Create(
                    context,
                    "the athlete texture sampler",
                    sampler AddressMode.ClampToEdge AddressMode.ClampToEdge 1us 8.0f
                )

            owner.Own characterSampler |> ignore
            let! stadiumSource = ShaderLibrary.readFile "StadiumScene.wgsl"
            let! cameraUniform = ShaderLibrary.readFile "CameraUniform.wgsl"
            let! environmentLighting = ShaderLibrary.readFile "EnvironmentLighting.wgsl"
            let! shadowLighting = ShaderLibrary.readFile "ShadowLighting.wgsl"
            let! athleteSurface = ShaderLibrary.readFile "AthleteSurface.wgsl"
            let! ballSurface = ShaderLibrary.readFile "MatchBallSurface.wgsl"
            let! pitchSurface = ShaderLibrary.readFile "PitchSurface.wgsl"
            let! grassDetail = ShaderLibrary.readFile "GrassDetail.wgsl"
            let! skyBackground = ShaderLibrary.readFile "SkyBackground.wgsl"

            // Constants precede the files that consume them, so the order below is part of the shader contract.
            let source =
                ShaderLibrary.compose
                    [ cameraUniform
                      stadiumSource.Replace("CHARACTER_JOINT_COUNT", $"{character.JointCount}u")
                      environmentLighting
                      shadowLighting
                      AthleteMaterial.shaderConstants
                      athleteSurface
                      ballSurface
                      PitchMarkings.shaderConstants
                      pitchSurface
                      grassDetail
                      skyBackground ]

            let! shader = ShaderModules.compile context "stadium" source
            owner.Release(fun () -> api.ShaderModuleRelease(shader))

            let! sceneLayout =
                BindGroupLayouts.create
                    context
                    "the camera bind-group layout"
                    [ BindGroupLayouts.uniform 0u (ShaderStage.Vertex ||| ShaderStage.Fragment) 0UL
                      BindGroupLayouts.storage 1u ShaderStage.Vertex true
                      BindGroupLayouts.texture2d 2u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 3u ShaderStage.Fragment
                      BindGroupLayouts.sampler 4u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 5u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 6u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 7u ShaderStage.Fragment
                      BindGroupLayouts.sampler 8u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 9u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 10u ShaderStage.Fragment
                      BindGroupLayouts.sampler 11u ShaderStage.Fragment
                      BindGroupLayouts.texture2d 12u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 13u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 14u ShaderStage.Fragment
                      BindGroupLayouts.textureArray 15u ShaderStage.Fragment
                      BindGroupLayouts.storage 16u ShaderStage.Vertex true ]

            owner.Release(fun () -> api.BindGroupLayoutRelease(sceneLayout))

            let! shadows = DirectionalShadowPass.Create(context, cache, ("stadium", shader), inputs.Shadow, sceneLayout)

            owner.Own shadows |> ignore

            let! pipelineLayout =
                BindGroupLayouts.pipelineLayout
                    context
                    "the stadium pipeline layout"
                    [ sceneLayout; shadows.ReceiverLayout ]

            owner.Release(fun () -> api.PipelineLayoutRelease(pipelineLayout))

            let! cameraBuffer =
                GpuBuffer<float32>
                    .Create(
                        context,
                        "broadcast camera and player transforms",
                        BufferUsage.Uniform ||| BufferUsage.CopyDst,
                        Array.zeroCreate<float32> UniformLayout.elementCount
                    )

            owner.Own cameraBuffer |> ignore

            let joints =
                Array.create (SceneRoot.playerCount * character.JointCount) Matrix4x4.Identity

            let! jointBuffer =
                GpuBuffer<Matrix4x4>
                    .Create(context, "character joint palettes", BufferUsage.Storage ||| BufferUsage.CopyDst, joints)

            owner.Own jointBuffer |> ignore

            let! cameraGroup =
                BindGroups.create
                    context
                    "the camera uniform bind group"
                    sceneLayout
                    [ BindGroups.buffer 0u cameraBuffer.Handle (uint64 (UniformLayout.elementCount * sizeof<float32>))
                      BindGroups.buffer 1u jointBuffer.Handle (uint64 (joints.Length * sizeof<Matrix4x4>))
                      BindGroups.view 2u albedo.View
                      BindGroups.view 3u detail.View
                      BindGroups.sampler 4u characterSampler.Handle
                      BindGroups.view 5u surfaceTextures[0].View
                      BindGroups.view 6u surfaceTextures[1].View
                      BindGroups.view 7u surfaceTextures[2].View
                      BindGroups.sampler 8u surfaceSampler.Handle
                      BindGroups.view 9u environmentTextures[0].View
                      BindGroups.view 10u environmentTextures[1].View
                      BindGroups.sampler 11u environmentSampler.Handle
                      BindGroups.view 12u environmentTextures[2].View
                      BindGroups.view 13u kitTextures[0].View
                      BindGroups.view 14u kitTextures[1].View
                      BindGroups.view 15u kitTextures[2].View
                      BindGroups.buffer 16u kitInstances.Handle (uint64 (kitInstances.Length * sizeof<Vector4>)) ]

            owner.Release(fun () -> api.BindGroupRelease(cameraGroup))

            let sceneKey: RenderPipelineKey =
                { Shader = "stadium"
                  Layout = "stadium scene"
                  VertexEntry = "vs"
                  Vertex = GpuVertexFull
                  Fragment =
                    Some
                        { Entry = "fs"
                          ColorFormat = TextureFormat.Rgba16float
                          Blend = Replace }
                  Depth =
                    Some
                        { DepthFormat = inputs.DepthFormat
                          WriteDepth = true
                          Compare = CompareFunction.Less
                          Bias = 0
                          SlopeScale = 0.0f }
                  Cull = CullMode.None
                  Samples = inputs.Samples
                  AlphaToCoverage = false }

            let! pipeline =
                cache.GetOrAdd(
                    sceneKey,
                    fun () -> RenderPipelines.build context "the stadium render pipeline" sceneKey shader pipelineLayout
                )

            // Glazing and nets blend over the finished opaque scene; writing depth would hide players behind glass.
            let blendedKey: RenderPipelineKey =
                { sceneKey with
                    Fragment =
                        Some
                            { Entry = "fsBlended"
                              ColorFormat = TextureFormat.Rgba16float
                              Blend = PremultipliedAlpha }
                    Depth =
                        Some
                            { DepthFormat = inputs.DepthFormat
                              WriteDepth = false
                              Compare = CompareFunction.Less
                              Bias = 0
                              SlopeScale = 0.0f } }

            let! blendedPipeline =
                cache.GetOrAdd(
                    blendedKey,
                    fun () ->
                        RenderPipelines.build context "the stadium glazing pipeline" blendedKey shader pipelineLayout
                )

            let procedural settings =
                ProceduralScenePass.Create(
                    context,
                    cache,
                    ("stadium", shader),
                    ("stadium scene", pipelineLayout),
                    inputs.DepthFormat,
                    inputs.Samples,
                    settings
                )

            let! grass = procedural ProceduralScenePassSettings.grass
            owner.Own grass |> ignore
            let! sky = procedural ProceduralScenePassSettings.sky
            owner.Own sky |> ignore

            return
                new SceneResources(
                    owner,
                    cameraBuffer,
                    jointBuffer,
                    cameraGroup,
                    pipeline,
                    blendedPipeline,
                    kitInstances,
                    shadows,
                    grass,
                    sky
                )
        }

    let mutable disposed = false

    static member Create
        (context: GpuContext, cache: RenderPipelineCache, inputs: SceneResourceInputs)
        : Result<SceneResources, GpuError> =
        let owner = new GpuOwner()

        match build context cache inputs owner with
        | Ok resources -> Ok resources
        | Error error ->
            (owner :> IDisposable).Dispose()
            Error error

    member _.CameraGroup = cameraGroup
    /// Owned by the pipeline cache, which outlives these resources.
    member _.Pipeline = pipeline
    member _.BlendedPipeline = blendedPipeline
    member _.KitInstances = kitInstances
    member _.Shadows = shadows
    member _.Grass = grass
    member _.Sky = sky

    member _.Upload(scene: SceneFrame) =
        jointBuffer.Write(scene.JointPalettes)
        cameraBuffer.Write(UniformLayout.packCamera scene)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                (owner :> IDisposable).Dispose()
