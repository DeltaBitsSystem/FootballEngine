#nowarn "9" "51"

namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Numerics
open Avalonia
open FootballEngine.Client.Views.Components
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

module private GpuFacade =
    /// The one place a GpuError becomes an exception; the viewer reports it through RenderWorker's onError.
    let unwrap (result: Result<'T, GpuError>) : 'T =
        match result with
        | Ok value -> value
        | Error error -> invalidOp (GpuError.describe error)

    let dispose (value: #IDisposable) = value.Dispose()

open GpuFacade

/// Composes the platform window, GPU core, pure scene, systems and frame plan. Everything below this type is
/// testable or GPU-isolated; this façade only wires lifetimes and runs the plan once per presented frame.
[<Sealed>]
type WebGpuStadiumRenderer
    (
        parentWindow: unativeint,
        initialBounds: PixelRect,
        assetDirectory: string,
        ?camera: ViewerCamera,
        ?exposureStops: float32
    ) =
    let startupClock = Diagnostics.Stopwatch.StartNew()
    let multisampleCount = 4u
    let mutable cameraMode = defaultArg camera ViewerCamera.Broadcast

    let mutable assets =
        match ViewerAssets.load assetDirectory exposureStops with
        | Ok loaded -> loaded
        | Error message -> invalidOp message

    let character = assets.Character

    let quality =
        match
            ViewerQuality.ofTier (ViewerQualitySelection.current ())
            |> ViewerQuality.validated
        with
        | Ok selected -> selected
        // Presets are validated by tests; an invalid one must not cost the player the match view.
        | Error _ -> ViewerQuality.ultra

    let plan =
        match FramePlan.validate FramePlan.stadium with
        | Ok plan -> plan
        | Error error -> invalidOp (FramePlan.describe error)


    let owner = new GpuOwner()
    let mutable display = 0n
    let mutable window = 0un
    let mutable gpu: GpuContext option = None
    let mutable resources: SceneResources option = None
    let mutable targets: RenderTargets option = None
    let mutable hdrOutput: HdrOutputPass option = None
    let mutable hud: HudOverlay option = None
    let mutable systems: IViewerSystem list = []
    let mutable stadium: StadiumSystem option = None
    let mutable requestedStadium = StadiumIdentity.neutral
    let mutable builtStadium: StadiumIdentity option = None
    let mutable staticShadowsReady = false
    let mutable showGrassDetail = false
    let mutable lastBounds = PixelRect(0, 0, 0, 0)
    let mutable lastShape: (PixelRect * PixelRect array) option = None
    let mutable disposed = false
    let pacing = FramePacing(startupClock)

    let context () =
        gpu
        |> Option.defaultWith (fun () -> invalidOp "The WebGPU context was not created.")

    let configureSurface (width: uint32) (height: uint32) =
        let ctx = context ()
        unwrap (ctx.Configure(width, height))
        unwrap (targets.Value.Resize(TargetSpec.ofSize multisampleCount width height))
        unwrap (hdrOutput.Value.Resize(width, height, targets.Value.Depth))
        unwrap (hud.Value.Resize(width, height))

    let updateWindow (bounds: PixelRect) =
        if bounds.Width > 0 && bounds.Height > 0 && bounds <> lastBounds then
            X11ChildWindow.moveResize display window bounds
            X11ChildWindow.flush display
            lastBounds <- bounds
            let ctx = context ()

            if uint32 bounds.Width <> ctx.Width || uint32 bounds.Height <> ctx.Height then
                configureSurface (uint32 bounds.Width) (uint32 bounds.Height)

    let updateWindowShape (bounds: PixelRect) (cutouts: PixelRect array) =
        let unchanged =
            lastShape
            |> Option.exists (fun (previous, previousCutouts) -> previous = bounds && previousCutouts = cutouts)

        if not unchanged then
            // Carving these regions out lets Avalonia draw and receive clicks through its native WebGPU child.
            X11ChildWindow.setShape display window (X11ChildWindow.cutoutRectangles bounds cutouts)
            X11ChildWindow.flush display
            lastShape <- Some(bounds, Array.copy cutouts)

    // Composition is pure; this is the one place where a rejected frame becomes an exception for the render worker.
    let buildScene (frame: RenderFrame) (palettes: Matrix4x4[]) : SceneFrame =
        let ctx = context ()

        let input: SceneInput =
            { CameraMode = cameraMode
              Aspect = float32 ctx.Width / float32 ctx.Height
              TimeSeconds =
                ViewerDiagnostics.windPin ()
                |> Option.defaultWith (fun () -> startupClock.Elapsed.TotalSeconds)
              Frame = frame
              JointPalettes = palettes
              JointCount = character.JointCount
              BallMeshRadius = MatchBallMesh.radius
              Sun = assets.Sun
              LightViewProjection = assets.LightViewProjection
              ClubPrimary = requestedStadium.Primary
              ClubSecondary = requestedStadium.Secondary }

        match SceneBuilder.build input with
        | Ok built -> built
        | Error(InvalidPalettes message) -> invalidArg "palettes" message
        | Error(InvalidFrame message) -> invalidArg "frame" message
        | Error SingularCamera -> invalidOp "The viewer camera produced a singular projection."

    // Generation is a few tens of milliseconds of CPU work, paid once per ground rather than per frame.
    let ensureStadium () =
        let wanted =
            match ViewerDiagnostics.stadiumPin () with
            | Some(reputation, level, seed) ->
                { requestedStadium with
                    Reputation = reputation
                    LeagueLevel = level
                    Seed = seed }
            | None -> requestedStadium

        if builtStadium <> Some wanted then
            match stadium with
            | None -> ()
            | Some system ->
                let started = Diagnostics.Stopwatch.GetTimestamp()

                match StadiumGeometry.build assets.Kit wanted with
                | Error message -> invalidOp message
                | Ok build ->
                    unwrap (system.Rebuild build)
                    builtStadium <- Some wanted
                    // The cached sun shadow still holds the previous ground.
                    staticShadowsReady <- false

                    Diagnostics.Trace.WriteLine(
                        $"Stadium generated: {build.SeatCount} seats, {build.Clusters.Length} clusters, "
                        + $"{build.Indices.Length / 3} stand triangles in "
                        + $"{Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms"
                    )

    let uploadScene (scene: SceneFrame) =
        // Blade detail is a per-frame decision, so the grass pass follows the same camera that was just packed.
        showGrassDetail <- scene.ShowGrassDetail
        let camera = scene.Camera
        hdrOutput.Value.SetCamera(camera.NearPlane, camera.FarPlane, camera.FieldOfView, camera.Aspect)
        resources.Value.Upload(scene)

        for system in systems do
            system.Update scene

    let draws () =
        systems |> List.collect (fun system -> system.Draws)


    let releaseResources () =
        if not disposed then
            disposed <- true
            // Owned in creation order and released in reverse, so the pipeline cache outlives every user and the
            // context outlives the cache.
            (owner :> IDisposable).Dispose()

            if window <> 0un then
                X11ChildWindow.destroy display window

            if display <> 0n then
                X11ChildWindow.closeDisplay display

    let initialize () =
        if not (OperatingSystem.IsLinux()) then
            invalidOp "The WebGPU stadium host currently requires the Linux X11/XWayland backend."

        if not (Directory.Exists assetDirectory) then
            invalidOp $"The match viewer asset folder was not found: {assetDirectory}"

        display <- X11ChildWindow.openDisplay ()

        if display = 0n then
            invalidOp "XOpenDisplay failed while creating the WebGPU stadium surface."

        window <- X11ChildWindow.createChild display parentWindow initialBounds.Width initialBounds.Height

        if window = 0un then
            invalidOp "XCreateSimpleWindow failed for the WebGPU stadium surface."

        X11ChildWindow.mapRaised display window
        X11ChildWindow.flush display
        ViewerDiagnostics.publishNativeWindow (uint64 window)
        let ctx = owner.Own(unwrap (GpuContext.Create(display, window)))
        gpu <- Some ctx

        let cache =
            owner.Own(new RenderPipelineCache(fun pipeline -> ctx.Api.RenderPipelineRelease(pipeline)))

        let width = uint32 (max 1 initialBounds.Width)
        let height = uint32 (max 1 initialBounds.Height)
        unwrap (ctx.Configure(width, height))
        let spec = TargetSpec.ofSize multisampleCount width height
        targets <- Some(owner.Own(unwrap (RenderTargets.Create(ctx, spec))))

        hdrOutput <-
            Some(
                owner.Own(
                    unwrap (
                        HdrOutputPass.Create(ctx, cache, assets.Lighting, quality, BroadcastGrade.standard, ctx.Format)
                    )
                )
            )

        unwrap (hdrOutput.Value.Resize(width, height, targets.Value.Depth))

        let inputs: SceneResourceInputs =
            { Environment = assets.Environment
              Surfaces = assets.Surfaces
              Character = character
              KitTextures = assets.KitTextures
              Shadow = assets.Shadow
              Samples = multisampleCount
              DepthFormat = spec.DepthFormat }

        resources <- Some(owner.Own(unwrap (SceneResources.Create(ctx, cache, inputs))))
        // The pixels now live on the GPU; holding the CPU copy would keep ~110 MB alive for the whole match.
        assets <-
            { assets with
                KitTextures = KitTextureData.empty }

        hud <- Some(owner.Own(unwrap (HudOverlay.Create(ctx, cache))))
        unwrap (hud.Value.Resize(width, height))

        let stadiumSystem =
            owner.Own(unwrap (StadiumSystem.Create(ctx, assets.Kit, resources.Value.KitInstances)))

        // The ground is generated on the first Render, after the control has named the home club; building the
        // neutral one here would cost a discarded generation at every match start.
        stadium <- Some stadiumSystem

        // Order is submission order: stadium, ball, players, then overlays.
        systems <-
            (stadiumSystem :> IViewerSystem)
            :: ([ ViewerSystems.ball ctx
                  ViewerSystems.players ctx character
                  ViewerSystems.carrierMarker ctx ]
                |> List.map (fun system -> owner.Own(unwrap system)))

        updateWindow initialBounds

        Diagnostics.Trace.WriteLine(
            $"WebGPU stadium ready: X11 child 0x{uint64 window:X}, {multisampleCount}x MSAA, {(draws ()).Length} draws."
        )

        Diagnostics.Trace.WriteLine($"Viewer startup ready: {startupClock.Elapsed.TotalMilliseconds:F1} ms")

    do
        try
            initialize ()
        with _ ->
            releaseResources ()
            reraise ()

    member _.NativeWindow = window
    member _.CharacterRig = character.Rig
    member _.MotionDatabase = assets.Motion
    member _.HudUploadBytes = hud |> Option.map _.UploadBytes |> Option.defaultValue 0L

    // Diagnostics can time completed work without making production rendering wait for the GPU every frame.
    member _.WaitForGpu() =
        if not disposed then
            (context ()).Poll(true)

    member _.Render
        (bounds: PixelRect, frame: RenderFrame, palettes: Matrix4x4[], cutouts: PixelRect array, stats: MatchHudStats)
        =
        let mutable presented = false

        if not disposed && bounds.Width > 0 && bounds.Height > 0 then
            let ctx = context ()
            let api = ctx.Api
            updateWindow bounds
            updateWindowShape bounds cutouts
            ensureStadium ()
            uploadScene (buildScene frame palettes)

            hud.Value.Update(
                frame,
                stats,
                (if ViewerDiagnostics.profilingHud () then
                     pacing.Performance
                 else
                     None)
            )

            let mutable current = SurfaceTexture()
            api.SurfaceGetCurrentTexture(ctx.Surface, &current)

            if
                current.Status = SurfaceGetCurrentTextureStatus.Success
                && not (GpuPointer.isMissing current.Texture)
            then
                let surfaceView =
                    api.TextureCreateView(current.Texture, NativePtr.nullPtr<TextureViewDescriptor>)

                let encoder =
                    api.DeviceCreateCommandEncoder(ctx.Device, NativePtr.nullPtr<CommandEncoderDescriptor>)

                let mutable commands = NativePtr.nullPtr<CommandBuffer>

                try
                    let encoding: FrameEncoding =
                        { Api = api
                          Encoder = encoder
                          SurfaceView = surfaceView
                          Scene = resources.Value
                          Targets = targets.Value
                          Output = hdrOutput.Value
                          Hud = hud.Value
                          Draws = draws ()
                          StaticShadowsReady = staticShadowsReady
                          ShowGrassDetail = showGrassDetail }

                    for step in plan do
                        FrameEncoder.encode encoding step.Pass

                    commands <- api.CommandEncoderFinish(encoder, NativePtr.nullPtr<CommandBufferDescriptor>)
                    api.QueueSubmit(ctx.Queue, unativeint 1, &&commands)
                    staticShadowsReady <- true
                    ctx.Poll(false)
                    api.SurfacePresent(ctx.Surface)
                    presented <- true
                    pacing.Presented(ctx.Width, ctx.Height)
                finally
                    if not (GpuPointer.isMissing commands) then
                        api.CommandBufferRelease(commands)

                    api.CommandEncoderRelease(encoder)
                    api.TextureViewRelease(surfaceView)
                    api.TextureRelease(current.Texture)
            elif
                current.Status = SurfaceGetCurrentTextureStatus.Outdated
                || current.Status = SurfaceGetCurrentTextureStatus.Lost
            then
                configureSurface ctx.Width ctx.Height
            else
                Diagnostics.Trace.WriteLine($"WebGPU stadium surface acquisition failed: {current.Status}")

                if not (GpuPointer.isMissing current.Texture) then
                    api.TextureRelease(current.Texture)

        presented

    member _.SetCamera(mode: ViewerCamera) = cameraMode <- mode

    /// Takes effect on the next rendered frame; an unchanged identity costs nothing.
    member _.SetStadium(identity: StadiumIdentity) = requestedStadium <- identity

    interface IDisposable with
        member _.Dispose() = releaseResources ()
