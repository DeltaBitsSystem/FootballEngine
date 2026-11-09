namespace FootballEngine.Client.Match3D

open System
open Silk.NET.WebGPU

type TargetSpec =
    { Width: uint32
      Height: uint32
      Samples: uint32
      ColorFormat: TextureFormat
      DepthFormat: TextureFormat }

module TargetSpec =
    /// HDR colour keeps highlights for bloom and tonemapping; the depth format is shared with every scene pipeline.
    let ofSize (samples: uint32) (width: uint32) (height: uint32) =
        { Width = max 1u width
          Height = max 1u height
          Samples = samples
          ColorFormat = TextureFormat.Rgba16float
          DepthFormat = TextureFormat.Depth24Plus }

module private TargetTextures =
    let create (context: GpuContext) (spec: TargetSpec) : Result<GpuTexture * GpuTexture, GpuError> =
        match
            GpuTexture.Create(
                context,
                { Label = $"a {spec.Samples}x multisampled color texture"
                  Format = spec.ColorFormat
                  Width = spec.Width
                  Height = spec.Height
                  Layers = 1u
                  MipLevels = 1u
                  Samples = spec.Samples
                  Usage = TextureUsage.RenderAttachment }
            )
        with
        | Error error -> Error error
        | Ok color ->
            // The post chain reads scene depth for ambient occlusion, so it must be bindable and kept after the pass.
            match
                GpuTexture.Create(
                    context,
                    { Label = "a depth texture"
                      Format = spec.DepthFormat
                      Width = spec.Width
                      Height = spec.Height
                      Layers = 1u
                      MipLevels = 1u
                      Samples = spec.Samples
                      Usage = TextureUsage.RenderAttachment ||| TextureUsage.TextureBinding }
                )
            with
            | Error error ->
                (color :> IDisposable).Dispose()
                Error error
            | Ok depth -> Ok(color, depth)

/// Scene attachments are always rebuilt together from one spec, so their sizes and sample counts cannot diverge;
/// a failed resize keeps the previous pair alive.
[<Sealed>]
type RenderTargets private (context: GpuContext, initial: TargetSpec * GpuTexture * GpuTexture) =
    let initialSpec, initialColor, initialDepth = initial
    let mutable spec = initialSpec
    let mutable color = initialColor
    let mutable depth = initialDepth
    let mutable disposed = false

    let release () =
        (color :> IDisposable).Dispose()
        (depth :> IDisposable).Dispose()

    static member Create(context: GpuContext, spec: TargetSpec) : Result<RenderTargets, GpuError> =
        TargetTextures.create context spec
        |> Result.map (fun (color, depth) -> new RenderTargets(context, (spec, color, depth)))

    member _.Spec = spec
    member _.Color = color
    member _.Depth = depth

    member _.Resize(next: TargetSpec) : Result<unit, GpuError> =
        TargetTextures.create context next
        |> Result.map (fun (nextColor, nextDepth) ->
            release ()
            spec <- next
            color <- nextColor
            depth <- nextDepth)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                release ()
