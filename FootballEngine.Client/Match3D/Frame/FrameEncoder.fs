#nowarn "9"

namespace FootballEngine.Client.Match3D

open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Everything one frame's encoding reads; built by the façade after the pure scene has been uploaded.
type FrameEncoding =
    { Api: WebGPU
      Encoder: nativeptr<CommandEncoder>
      SurfaceView: nativeptr<TextureView>
      Scene: SceneResources
      Targets: RenderTargets
      Output: HdrOutputPass
      Hud: HudOverlay
      Draws: Draw<GpuMesh> list
      StaticShadowsReady: bool
      ShowGrassDetail: bool }

/// Encodes one FramePlan entry. Pass bodies live here so the plan, not the façade, decides the frame's shape.
module FrameEncoder =
    // Shared by the scene and output passes; it shows only where neither geometry nor the sky covers a pixel.
    let private clearColor () =
        let mutable clear = Color()
        clear.R <- 0.015
        clear.G <- 0.045
        clear.B <- 0.065
        clear.A <- 1.0
        clear

    let encode (frame: FrameEncoding) (pass: FramePass) =
        let api = frame.Api
        let encoder = frame.Encoder
        let scene = frame.Scene
        let all = frame.Draws

        let drawAll (renderPass: nativeptr<RenderPassEncoder>) (selected: Draw<GpuMesh> list) =
            for draw in selected do
                ViewerSystems.encode api renderPass draw

        let finish (renderPass: nativeptr<RenderPassEncoder>) =
            api.RenderPassEncoderEnd(renderPass)
            api.RenderPassEncoderRelease(renderPass)

        match pass with
        | ShadowPass ->
            // Static casters are rendered once; later frames copy the cached depth and add only moving geometry.
            if not frame.StaticShadowsReady then
                let renderPass = scene.Shadows.Begin(encoder, scene.CameraGroup, true)
                drawAll renderPass (DrawList.shadowCasters StaticCaster all)
                finish renderPass

            scene.Shadows.CopyStatic(encoder)
            let renderPass = scene.Shadows.Begin(encoder, scene.CameraGroup, false)
            drawAll renderPass (DrawList.shadowCasters DynamicCaster all)
            finish renderPass
        | MainPass ->
            let mutable color = RenderPassColorAttachment()
            color.View <- frame.Targets.Color.View
            color.ResolveTarget <- frame.Output.SceneView
            color.LoadOp <- LoadOp.Clear
            color.StoreOp <- StoreOp.Discard
            color.ClearValue <- clearColor ()
            let mutable depth = RenderPassDepthStencilAttachment()
            depth.View <- frame.Targets.Depth.View
            depth.DepthLoadOp <- LoadOp.Clear
            depth.DepthStoreOp <- StoreOp.Store
            depth.DepthClearValue <- 1.0f
            let mutable descriptor = RenderPassDescriptor()
            descriptor.ColorAttachmentCount <- unativeint 1
            descriptor.ColorAttachments <- &&color
            descriptor.DepthStencilAttachment <- &&depth
            let renderPass = api.CommandEncoderBeginRenderPass(encoder, &descriptor)
            api.RenderPassEncoderSetPipeline(renderPass, scene.Pipeline)

            api.RenderPassEncoderSetBindGroup(
                renderPass,
                1u,
                scene.Shadows.ReceiverGroup,
                unativeint 0,
                NativePtr.nullPtr<uint32>
            )

            api.RenderPassEncoderSetBindGroup(
                renderPass,
                0u,
                scene.CameraGroup,
                unativeint 0,
                NativePtr.nullPtr<uint32>
            )

            drawAll renderPass (DrawList.inLayer SceneLayer all)
            // Procedural passes switch pipelines, so the scene pipeline is restored before the next mesh draws.
            scene.Sky.Encode(renderPass)
            api.RenderPassEncoderSetPipeline(renderPass, scene.Pipeline)

            if frame.ShowGrassDetail then
                scene.Grass.Encode(renderPass)

            // Glazing composites over turf and grass blades, which are opaque and must already be in the target.
            api.RenderPassEncoderSetPipeline(renderPass, scene.BlendedPipeline)
            drawAll renderPass (DrawList.inLayer BlendedLayer all)
            api.RenderPassEncoderSetPipeline(renderPass, scene.Pipeline)
            drawAll renderPass (DrawList.inLayer OverlayLayer all)
            finish renderPass
        | BloomPass -> frame.Output.EncodeBloom(encoder)
        | OutputPass ->
            let mutable color = RenderPassColorAttachment()
            color.View <- frame.SurfaceView
            color.ClearValue <- clearColor ()
            color.LoadOp <- LoadOp.Clear
            color.StoreOp <- StoreOp.Store
            let mutable descriptor = RenderPassDescriptor()
            descriptor.ColorAttachmentCount <- unativeint 1
            descriptor.ColorAttachments <- &&color
            let renderPass = api.CommandEncoderBeginRenderPass(encoder, &descriptor)
            frame.Output.Draw(renderPass)
            frame.Hud.Draw(renderPass)
            finish renderPass
