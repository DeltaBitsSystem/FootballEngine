#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

type ProceduralScenePassSettings =
    { VertexEntry: string
      FragmentEntry: string
      VertexCount: uint32
      InstanceCount: uint32
      WriteDepth: bool
      DepthCompare: CompareFunction
      AlphaToCoverage: bool }

module ProceduralScenePassSettings =
    let grass =
        { VertexEntry = "grassVs"
          FragmentEntry = "grassFs"
          // Five blades of six vertices per cell; the grid must match GRASS_GRID in GrassDetail.wgsl.
          VertexCount = 30u
          InstanceCount = 240u * 240u
          WriteDepth = true
          DepthCompare = CompareFunction.Less
          AlphaToCoverage = true }

    let sky =
        { VertexEntry = "skyVs"
          FragmentEntry = "skyFs"
          VertexCount = 3u
          InstanceCount = 1u
          WriteDepth = false
          DepthCompare = CompareFunction.LessEqual
          AlphaToCoverage = false }

/// Vertex-ID passes reuse scene bindings without allocating or uploading generated geometry. The pipeline belongs to
/// the shared cache; this pass only encodes it.
[<Sealed>]
type ProceduralScenePass
    private (pipeline: nativeptr<RenderPipeline>, api: Silk.NET.WebGPU.WebGPU, settings: ProceduralScenePassSettings) =
    let mutable disposed = false

    /// The depth state is the scene's own, with only the write and compare behaviour of this pass changed.
    static member Create
        (
            context: GpuContext,
            cache: RenderPipelineCache,
            shader: string * nativeptr<ShaderModule>,
            layout: string * nativeptr<PipelineLayout>,
            depthFormat: Silk.NET.WebGPU.TextureFormat,
            samples: uint32,
            settings: ProceduralScenePassSettings
        ) : Result<ProceduralScenePass, GpuError> =
        let shaderName, shaderModule = shader
        let layoutName, pipelineLayout = layout

        let key: RenderPipelineKey =
            { Shader = shaderName
              Layout = layoutName
              VertexEntry = settings.VertexEntry
              // Generated geometry: vertex IDs address the grid, so the pipeline has no vertex buffers.
              Vertex = NoVertexBuffers
              Fragment =
                Some
                    { Entry = settings.FragmentEntry
                      ColorFormat = Silk.NET.WebGPU.TextureFormat.Rgba16float
                      Blend = Replace }
              Depth =
                Some
                    { DepthFormat = depthFormat
                      WriteDepth = settings.WriteDepth
                      Compare = settings.DepthCompare
                      Bias = 0
                      SlopeScale = 0.0f }
              Cull = CullMode.None
              Samples = samples
              // Coverage fade preserves correct depth without sorting transparent blades or a noisy hard LOD boundary.
              AlphaToCoverage = settings.AlphaToCoverage }

        cache.GetOrAdd(
            key,
            fun () ->
                RenderPipelines.build context $"the {settings.VertexEntry} pipeline" key shaderModule pipelineLayout
        )
        |> Result.map (fun pipeline -> new ProceduralScenePass(pipeline, context.Api, settings))

    member _.Encode(pass: nativeptr<RenderPassEncoder>) =
        if disposed then
            raise (ObjectDisposedException(nameof ProceduralScenePass))

        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderDraw(pass, settings.VertexCount, settings.InstanceCount, 0u, 0u)

    interface IDisposable with
        member _.Dispose() = disposed <- true
