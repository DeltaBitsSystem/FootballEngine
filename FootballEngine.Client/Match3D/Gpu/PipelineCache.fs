#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.Collections.Generic
open System.Runtime.InteropServices
open FsToolkit.ErrorHandling
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Equal keys share one value. The cache owns every value it returns and releases each once, on disposal.
[<Sealed>]
type PipelineCache<'Key, 'Value when 'Key: equality>(release: 'Value -> unit) =
    let entries = Dictionary<'Key, 'Value>()
    let mutable disposed = false

    member _.Count = entries.Count

    member _.GetOrAdd(key: 'Key, create: unit -> Result<'Value, GpuError>) : Result<'Value, GpuError> =
        if disposed then
            raise (ObjectDisposedException(nameof PipelineCache))

        match entries.TryGetValue key with
        | true, existing -> Ok existing
        | _ ->
            create ()
            |> Result.map (fun value ->
                entries.Add(key, value)
                value)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                for value in entries.Values do
                    release value

                entries.Clear()

type VertexInputKey =
    | NoVertexBuffers
    /// The full GpuVertex layout used by stadium, athlete, ball and marker batches.
    | GpuVertexFull
    /// Position and UV only, reading the HUD quad out of a GpuVertex buffer.
    | GpuVertexPositionUv

type FragmentBlend =
    | Replace
    | Additive
    | PremultipliedAlpha

type FragmentKey =
    { Entry: string
      ColorFormat: TextureFormat
      Blend: FragmentBlend }

type DepthKey =
    {
        DepthFormat: TextureFormat
        WriteDepth: bool
        Compare: CompareFunction
        /// Shadow casters need a raster bias; zero for every camera-facing pass.
        Bias: int
        SlopeScale: float32
    }

/// Everything that changes pipeline state. Shader and layout are named, not compared by handle, because the caller
/// creates each module once and names it for the lifetime of the cache.
type RenderPipelineKey =
    { Shader: string
      Layout: string
      VertexEntry: string
      Vertex: VertexInputKey
      Fragment: FragmentKey option
      Depth: DepthKey option
      Cull: CullMode
      Samples: uint32
      AlphaToCoverage: bool }

type RenderPipelineCache = PipelineCache<RenderPipelineKey, nativeptr<RenderPipeline>>

module RenderPipelines =
    let private fullAttributes =
        [| VertexFormat.Float32x3, 0UL, 0u
           VertexFormat.Float32x3, 12UL, 1u
           VertexFormat.Float32x4, 24UL, 2u
           VertexFormat.Float32x2, 40UL, 3u
           VertexFormat.Uint32, 48UL, 4u
           VertexFormat.Float32x4, 52UL, 5u
           VertexFormat.Float32x4, 68UL, 6u
           VertexFormat.Uint32, 84UL, 7u |]

    let private screenAttributes =
        [| VertexFormat.Float32x3, 0UL, 0u; VertexFormat.Float32x2, 40UL, 1u |]

    let private writeAttributes (target: nativeptr<VertexAttribute>) (attributes: (VertexFormat * uint64 * uint32)[]) =
        attributes
        |> Array.iteri (fun index (format, offset, location) ->
            let mutable attribute = VertexAttribute()
            attribute.Format <- format
            attribute.Offset <- offset
            attribute.ShaderLocation <- location
            NativePtr.write (NativePtr.add target index) attribute)

    let private blendState (blend: FragmentBlend) : BlendState option =
        match blend with
        | Replace -> None
        | Additive ->
            let mutable blend = BlendComponent()
            blend.Operation <- BlendOperation.Add
            blend.SrcFactor <- BlendFactor.One
            blend.DstFactor <- BlendFactor.One
            let mutable state = BlendState()
            state.Color <- blend
            state.Alpha <- blend
            Some state
        | PremultipliedAlpha ->
            let mutable blend = BlendComponent()
            blend.Operation <- BlendOperation.Add
            blend.SrcFactor <- BlendFactor.One
            blend.DstFactor <- BlendFactor.OneMinusSrcAlpha
            let mutable state = BlendState()
            state.Color <- blend
            state.Alpha <- blend
            Some state

    /// Builds the pipeline described by a key. Stack-allocated descriptor arrays must stay alive until
    /// DeviceCreateRenderPipeline returns, so the whole descriptor is assembled inside this one function.
    let build
        (context: GpuContext)
        (label: string)
        (key: RenderPipelineKey)
        (shader: nativeptr<ShaderModule>)
        (layout: nativeptr<PipelineLayout>)
        : Result<nativeptr<RenderPipeline>, GpuError> =
        let api = context.Api
        let vertexName = Text.Encoding.UTF8.GetBytes(key.VertexEntry + "\u0000")
        // Always a pinnable array: a shadow pipeline has no fragment stage and simply never reads this one.
        let fragmentName =
            Text.Encoding.UTF8.GetBytes((key.Fragment |> Option.map _.Entry |> Option.defaultValue "") + "\u0000")

        use vertexEntry = fixed vertexName
        use fragmentEntry = fixed fragmentName

        let attributes, attributeCount =
            match key.Vertex with
            | NoVertexBuffers -> [||], 0
            | GpuVertexFull -> fullAttributes, fullAttributes.Length
            | GpuVertexPositionUv -> screenAttributes, screenAttributes.Length

        let attributePointer = NativePtr.stackalloc<VertexAttribute> (max 1 attributeCount)
        writeAttributes attributePointer attributes
        let mutable vertexLayout = VertexBufferLayout()
        vertexLayout.ArrayStride <- uint64 (Marshal.SizeOf<GpuVertex>())
        vertexLayout.StepMode <- VertexStepMode.Vertex
        vertexLayout.AttributeCount <- unativeint attributeCount
        vertexLayout.Attributes <- attributePointer
        let vertexLayoutPointer = NativePtr.stackalloc<VertexBufferLayout> 1
        NativePtr.write vertexLayoutPointer vertexLayout

        let mutable vertexState = VertexState()
        vertexState.Module <- shader
        vertexState.EntryPoint <- vertexEntry

        if attributeCount > 0 then
            vertexState.BufferCount <- unativeint 1
            vertexState.Buffers <- vertexLayoutPointer

        let blendPointer = NativePtr.stackalloc<BlendState> 1
        let colorTargetPointer = NativePtr.stackalloc<ColorTargetState> 1
        let fragmentStatePointer = NativePtr.stackalloc<FragmentState> 1

        match key.Fragment with
        | Some fragment ->
            let mutable colorTarget = ColorTargetState()
            colorTarget.Format <- fragment.ColorFormat
            colorTarget.WriteMask <- ColorWriteMask.All

            match blendState fragment.Blend with
            | Some blend ->
                NativePtr.write blendPointer blend
                colorTarget.Blend <- blendPointer
            | None -> ()

            NativePtr.write colorTargetPointer colorTarget
            let mutable fragmentState = FragmentState()
            fragmentState.Module <- shader
            fragmentState.EntryPoint <- fragmentEntry
            fragmentState.TargetCount <- unativeint 1
            fragmentState.Targets <- colorTargetPointer
            NativePtr.write fragmentStatePointer fragmentState
        | None -> ()

        let depthStatePointer = NativePtr.stackalloc<DepthStencilState> 1

        key.Depth
        |> Option.iter (fun depth ->
            let mutable depthState = DepthStencilState()
            depthState.Format <- depth.DepthFormat
            depthState.DepthWriteEnabled <- Silk.NET.Core.Bool32(if depth.WriteDepth then 1u else 0u)
            depthState.DepthCompare <- depth.Compare
            depthState.DepthBias <- depth.Bias
            depthState.DepthBiasSlopeScale <- depth.SlopeScale
            let mutable stencilFace = StencilFaceState()
            stencilFace.Compare <- CompareFunction.Always
            stencilFace.FailOp <- StencilOperation.Keep
            stencilFace.DepthFailOp <- StencilOperation.Keep
            stencilFace.PassOp <- StencilOperation.Keep
            depthState.StencilFront <- stencilFace
            depthState.StencilBack <- stencilFace
            depthState.StencilReadMask <- 0u
            depthState.StencilWriteMask <- 0u
            NativePtr.write depthStatePointer depthState)

        let mutable descriptor = RenderPipelineDescriptor()
        descriptor.Layout <- layout
        descriptor.Vertex <- vertexState

        if key.Fragment.IsSome then
            descriptor.Fragment <- fragmentStatePointer

        if key.Depth.IsSome then
            descriptor.DepthStencil <- depthStatePointer

        descriptor.Primitive.Topology <- PrimitiveTopology.TriangleList
        descriptor.Primitive.CullMode <- key.Cull
        descriptor.Multisample.Count <- key.Samples
        descriptor.Multisample.Mask <- UInt32.MaxValue
        descriptor.Multisample.AlphaToCoverageEnabled <- Silk.NET.Core.Bool32(if key.AlphaToCoverage then 1u else 0u)
        let pipeline = api.DeviceCreateRenderPipeline(context.Device, &descriptor)

        if GpuPointer.isMissing pipeline then
            Error(CreationFailed label)
        else
            Ok pipeline

/// Typed constructors for bind-group layout entries. Each names the visibility and resource kind once, so a layout
/// reads like the WGSL bindings it describes.
module BindGroupLayouts =
    let uniform (binding: uint32) (visibility: ShaderStage) (minBindingSize: uint64) =
        let mutable entry = BindGroupLayoutEntry()
        entry.Binding <- binding
        entry.Visibility <- visibility
        entry.Buffer.Type <- BufferBindingType.Uniform
        entry.Buffer.MinBindingSize <- minBindingSize
        entry

    let storage (binding: uint32) (visibility: ShaderStage) (readOnly: bool) =
        let mutable entry = BindGroupLayoutEntry()
        entry.Binding <- binding
        entry.Visibility <- visibility

        entry.Buffer.Type <-
            if readOnly then
                BufferBindingType.ReadOnlyStorage
            else
                BufferBindingType.Storage

        entry

    let texture
        (binding: uint32)
        (visibility: ShaderStage)
        (sampleType: TextureSampleType)
        (dimension: TextureViewDimension)
        (multisampled: bool)
        =
        let mutable entry = BindGroupLayoutEntry()
        entry.Binding <- binding
        entry.Visibility <- visibility
        entry.Texture.SampleType <- sampleType
        entry.Texture.ViewDimension <- dimension
        entry.Texture.Multisampled <- Silk.NET.Core.Bool32(if multisampled then 1u else 0u)
        entry

    let texture2d (binding: uint32) (visibility: ShaderStage) =
        texture binding visibility TextureSampleType.Float TextureViewDimension.Dimension2D false

    let textureArray (binding: uint32) (visibility: ShaderStage) =
        texture binding visibility TextureSampleType.Float TextureViewDimension.Dimension2DArray false

    let depthMultisampled (binding: uint32) (visibility: ShaderStage) =
        texture binding visibility TextureSampleType.Depth TextureViewDimension.Dimension2D true

    let sampler (binding: uint32) (visibility: ShaderStage) =
        let mutable entry = BindGroupLayoutEntry()
        entry.Binding <- binding
        entry.Visibility <- visibility
        entry.Sampler.Type <- SamplerBindingType.Filtering
        entry

    let comparisonSampler (binding: uint32) (visibility: ShaderStage) =
        let mutable entry = BindGroupLayoutEntry()
        entry.Binding <- binding
        entry.Visibility <- visibility
        entry.Sampler.Type <- SamplerBindingType.Comparison
        entry

    let create
        (context: GpuContext)
        (label: string)
        (entries: BindGroupLayoutEntry list)
        : Result<nativeptr<BindGroupLayout>, GpuError> =
        let array = List.toArray entries
        let pointer = NativePtr.stackalloc<BindGroupLayoutEntry> array.Length

        array
        |> Array.iteri (fun index entry -> NativePtr.write (NativePtr.add pointer index) entry)

        let mutable descriptor = BindGroupLayoutDescriptor()
        descriptor.EntryCount <- unativeint array.Length
        descriptor.Entries <- pointer
        let handle = context.Api.DeviceCreateBindGroupLayout(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(CreationFailed label)
        else
            Ok handle

    let pipelineLayout
        (context: GpuContext)
        (label: string)
        (layouts: nativeptr<BindGroupLayout> list)
        : Result<nativeptr<PipelineLayout>, GpuError> =
        let array = List.toArray layouts
        let pointer = NativePtr.stackalloc<nativeptr<BindGroupLayout>> array.Length

        array
        |> Array.iteri (fun index layout -> NativePtr.write (NativePtr.add pointer index) layout)

        let mutable descriptor = PipelineLayoutDescriptor()
        descriptor.BindGroupLayoutCount <- unativeint array.Length
        descriptor.BindGroupLayouts <- pointer
        let handle = context.Api.DeviceCreatePipelineLayout(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(CreationFailed label)
        else
            Ok handle

/// Bind groups bind concrete resources to a layout; each helper names one binding kind.
module BindGroups =
    let view (binding: uint32) (textureView: nativeptr<TextureView>) =
        let mutable entry = BindGroupEntry()
        entry.Binding <- binding
        entry.TextureView <- textureView
        entry

    let buffer (binding: uint32) (handle: nativeptr<Buffer>) (size: uint64) =
        let mutable entry = BindGroupEntry()
        entry.Binding <- binding
        entry.Buffer <- handle
        entry.Size <- size
        entry

    let sampler (binding: uint32) (handle: nativeptr<Sampler>) =
        let mutable entry = BindGroupEntry()
        entry.Binding <- binding
        entry.Sampler <- handle
        entry

    let create
        (context: GpuContext)
        (label: string)
        (layout: nativeptr<BindGroupLayout>)
        (entries: BindGroupEntry list)
        : Result<nativeptr<BindGroup>, GpuError> =
        let array = List.toArray entries
        let pointer = NativePtr.stackalloc<BindGroupEntry> array.Length

        array
        |> Array.iteri (fun index entry -> NativePtr.write (NativePtr.add pointer index) entry)

        let mutable descriptor = BindGroupDescriptor()
        descriptor.Layout <- layout
        descriptor.EntryCount <- unativeint array.Length
        descriptor.Entries <- pointer
        let handle = context.Api.DeviceCreateBindGroup(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(CreationFailed label)
        else
            Ok handle

/// Compiles one WGSL module. The name is only used to report which file failed.
module ShaderModules =
    let compile (context: GpuContext) (name: string) (source: string) : Result<nativeptr<ShaderModule>, GpuError> =
        let bytes = Text.Encoding.UTF8.GetBytes(source + "\u0000")
        use code = fixed bytes
        let mutable wgsl = ShaderModuleWGSLDescriptor()
        wgsl.Chain.SType <- SType.ShaderModuleWgslDescriptor
        wgsl.Code <- code
        let wgslPointer = NativePtr.stackalloc<ShaderModuleWGSLDescriptor> 1
        NativePtr.write wgslPointer wgsl
        let mutable descriptor = ShaderModuleDescriptor()
        descriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt wgslPointer)
        let handle = context.Api.DeviceCreateShaderModule(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(ShaderCompile(name, "WebGPU rejected the module"))
        else
            Ok handle
