#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.Numerics
open FsToolkit.ErrorHandling
open FootballEngine.Client.Views.Components
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// The HUD is a 2D overlay composited after tonemapping, not scene content: Skia redraws only dirty regions on
/// the CPU and only those rectangles are uploaded.
[<Sealed>]
type HudOverlay
    private
    (
        context: GpuContext,
        owner: GpuOwner,
        layout: nativeptr<BindGroupLayout>,
        pipeline: nativeptr<RenderPipeline>,
        sampler: GpuSampler,
        quadVertices: GpuBuffer<GpuVertex>,
        quadIndices: GpuBuffer<uint32>
    ) =
    let api = context.Api
    let mutable hud: RetainedMatchHud option = None
    let mutable texture: GpuTexture option = None
    let mutable group = NativePtr.nullPtr<BindGroup>
    let mutable uploadBytes = 0L
    let mutable disposed = false

    let releaseTarget () =
        hud |> Option.iter (fun value -> (value :> IDisposable).Dispose())
        hud <- None

        if not (GpuPointer.isMissing group) then
            api.BindGroupRelease(group)
            group <- NativePtr.nullPtr<BindGroup>

        texture |> Option.iter (fun value -> (value :> IDisposable).Dispose())
        texture <- None

    static let quad () =
        let topLeft =
            { Position = Vector3(-1.0f, 1.0f, 0.0f)
              Normal = Vector3.UnitY
              Color = Vector4.One
              Uv = Vector2(0.0f, 0.0f)
              RootIndex = 0u
              Joints = Vector4.Zero
              Weights = Vector4.Zero
              Surface = uint32 StadiumSurface.Plain }

        [| topLeft
           { topLeft with
               Position = Vector3(1.0f, 1.0f, 0.0f)
               Uv = Vector2(1.0f, 0.0f) }
           { topLeft with
               Position = Vector3(1.0f, -1.0f, 0.0f)
               Uv = Vector2(1.0f, 1.0f) }
           { topLeft with
               Position = Vector3(-1.0f, -1.0f, 0.0f)
               Uv = Vector2(0.0f, 1.0f) } |]

    static let build (context: GpuContext) (cache: RenderPipelineCache) (owner: GpuOwner) =
        result {
            let api = context.Api
            let! source = ShaderLibrary.readFile "MatchHud.wgsl"
            let! shader = ShaderModules.compile context "match HUD" source
            owner.Release(fun () -> api.ShaderModuleRelease(shader))

            let! layout =
                BindGroupLayouts.create
                    context
                    "the match HUD bind-group layout"
                    [ BindGroupLayouts.texture2d 0u ShaderStage.Fragment
                      BindGroupLayouts.sampler 1u ShaderStage.Fragment ]

            owner.Release(fun () -> api.BindGroupLayoutRelease(layout))
            let! pipelineLayout = BindGroupLayouts.pipelineLayout context "the match HUD pipeline layout" [ layout ]
            owner.Release(fun () -> api.PipelineLayoutRelease(pipelineLayout))

            let! sampler =
                GpuSampler.Create(
                    context,
                    "the match HUD sampler",
                    { AddressU = AddressMode.ClampToEdge
                      AddressV = AddressMode.ClampToEdge
                      AddressW = AddressMode.ClampToEdge
                      Magnification = FilterMode.Linear
                      Minification = FilterMode.Linear
                      Mipmap = MipmapFilterMode.Linear
                      MaxAnisotropy = 1us
                      LodMaxClamp = 0.0f
                      Compare = None }
                )

            owner.Own sampler |> ignore

            let key: RenderPipelineKey =
                { Shader = "match HUD"
                  Layout = "match HUD"
                  VertexEntry = "vs"
                  Vertex = GpuVertexPositionUv
                  Fragment =
                    Some
                        { Entry = "fs"
                          ColorFormat = context.Format
                          Blend = PremultipliedAlpha }
                  Depth = None
                  Cull = CullMode.None
                  Samples = 1u
                  AlphaToCoverage = false }

            let! pipeline =
                cache.GetOrAdd(
                    key,
                    fun () -> RenderPipelines.build context "the match HUD render pipeline" key shader pipelineLayout
                )

            let! vertices =
                GpuBuffer<GpuVertex>
                    .Create(context, "match HUD quad vertices", BufferUsage.Vertex ||| BufferUsage.CopyDst, quad ())

            owner.Own vertices |> ignore

            let! indices =
                GpuBuffer<uint32>
                    .Create(
                        context,
                        "match HUD quad indices",
                        BufferUsage.Index ||| BufferUsage.CopyDst,
                        [| 0u; 1u; 2u; 0u; 2u; 3u |]
                    )

            owner.Own indices |> ignore
            return new HudOverlay(context, owner, layout, pipeline, sampler, vertices, indices)
        }

    static member Create(context: GpuContext, cache: RenderPipelineCache) : Result<HudOverlay, GpuError> =
        let owner = new GpuOwner()

        match build context cache owner with
        | Ok overlay -> Ok overlay
        | Error error ->
            (owner :> IDisposable).Dispose()
            Error error

    member _.UploadBytes = uploadBytes

    member _.Resize(width: uint32, height: uint32) : Result<unit, GpuError> =
        releaseTarget ()
        hud <- Some(new RetainedMatchHud(int width, int height))

        result {
            let! created =
                GpuTexture.Create(
                    context,
                    { Label = "the match HUD texture"
                      Format = TextureFormat.Rgba8Unorm
                      Width = width
                      Height = height
                      Layers = 1u
                      MipLevels = 1u
                      Samples = 1u
                      Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst }
                )

            texture <- Some created

            let! bindGroup =
                BindGroups.create
                    context
                    "the match HUD texture bind group"
                    layout
                    [ BindGroups.view 0u created.View; BindGroups.sampler 1u sampler.Handle ]

            group <- bindGroup
        }

    member _.Update(frame: RenderFrame, stats: MatchHudStats, performance: (float * float) option) =
        match hud, texture with
        | Some image, Some target ->
            for region in image.Update(frame, stats, performance) do
                let pointer =
                    image.Pixels + nativeint (region.Top * image.RowBytes + region.Left * 4)

                let length = (region.Height - 1) * image.RowBytes + region.Width * 4

                target.UploadRegion(
                    uint32 region.Left,
                    uint32 region.Top,
                    uint32 region.Width,
                    uint32 region.Height,
                    uint32 image.RowBytes,
                    pointer,
                    length
                )

                uploadBytes <- uploadBytes + int64 (region.Width * region.Height * 4)
        | _ -> ()

    member _.Draw(pass: nativeptr<RenderPassEncoder>) =
        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderSetBindGroup(pass, 0u, group, unativeint 0, NativePtr.nullPtr<uint32>)
        api.RenderPassEncoderSetVertexBuffer(pass, 0u, quadVertices.Handle, 0UL, UInt64.MaxValue)
        api.RenderPassEncoderSetIndexBuffer(pass, quadIndices.Handle, IndexFormat.Uint32, 0UL, UInt64.MaxValue)
        api.RenderPassEncoderDrawIndexed(pass, 6u, 1u, 0u, 0, 0u)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                releaseTarget ()
                (owner :> IDisposable).Dispose()
