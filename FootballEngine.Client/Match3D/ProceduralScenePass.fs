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

/// Vertex-ID passes reuse scene bindings without allocating or uploading generated geometry.
[<Sealed>]
type ProceduralScenePass(api: WebGPU, device: nativeptr<Device>, shader: nativeptr<ShaderModule>, layout: nativeptr<PipelineLayout>, depth: DepthStencilState, samples: uint32, settings: ProceduralScenePassSettings) =
    let mutable pipeline = NativePtr.nullPtr<RenderPipeline>
    let mutable disposed = false

    do
        let vertexBytes, fragmentBytes = Text.Encoding.UTF8.GetBytes(settings.VertexEntry + "\u0000"), Text.Encoding.UTF8.GetBytes(settings.FragmentEntry + "\u0000")
        use vertexName = fixed vertexBytes
        use fragmentName = fixed fragmentBytes
        let mutable vertex = VertexState()
        vertex.Module <- shader
        vertex.EntryPoint <- vertexName
        let mutable target = ColorTargetState()
        target.Format <- TextureFormat.Rgba16float
        target.WriteMask <- ColorWriteMask.All
        let targetPointer = NativePtr.stackalloc<ColorTargetState> 1
        NativePtr.write targetPointer target
        let mutable fragment = FragmentState()
        fragment.Module <- shader
        fragment.EntryPoint <- fragmentName
        fragment.TargetCount <- unativeint 1
        fragment.Targets <- targetPointer
        let fragmentPointer = NativePtr.stackalloc<FragmentState> 1
        NativePtr.write fragmentPointer fragment
        let depthPointer = NativePtr.stackalloc<DepthStencilState> 1
        let mutable passDepth = depth
        passDepth.DepthWriteEnabled <- Silk.NET.Core.Bool32(if settings.WriteDepth then 1u else 0u)
        passDepth.DepthCompare <- settings.DepthCompare
        NativePtr.write depthPointer passDepth
        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Layout <- layout
        descriptor.Vertex <- vertex
        descriptor.Fragment <- fragmentPointer
        descriptor.DepthStencil <- depthPointer
        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Primitive.CullMode <- CullMode.None
        descriptor.Multisample.Count <- samples
        descriptor.Multisample.Mask <- UInt32.MaxValue
        // Coverage fade preserves correct depth without sorting transparent blades or a noisy hard LOD boundary.
        descriptor.Multisample.AlphaToCoverageEnabled <- Silk.NET.Core.Bool32(if settings.AlphaToCoverage then 1u else 0u)
        pipeline <- api.DeviceCreateRenderPipeline(device, &descriptor)
        if NativePtr.toNativeInt pipeline = 0n then invalidOp $"WebGPU could not create the {settings.VertexEntry} pipeline."

    member _.Encode(pass: nativeptr<RenderPassEncoder>) =
        if disposed then raise (ObjectDisposedException(nameof ProceduralScenePass))
        api.RenderPassEncoderSetPipeline(pass, pipeline)
        api.RenderPassEncoderDraw(pass, settings.VertexCount, settings.InstanceCount, 0u, 0u)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                if NativePtr.toNativeInt pipeline <> 0n then api.RenderPipelineRelease(pipeline)
