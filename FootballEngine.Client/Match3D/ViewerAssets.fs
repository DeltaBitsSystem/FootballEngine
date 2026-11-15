namespace FootballEngine.Client.Match3D

open System.Diagnostics
open System.IO
open System.Numerics
open System.Threading.Tasks

/// CPU-side packages the viewer needs before touching the GPU, already validated.
type ViewerAssets =
    {
        Kit: StadiumKitData
        /// Dropped by the renderer once uploaded; the GPU copy is the only one the viewer needs.
        KitTextures: KitTextureData
        Character: CharacterAssetData
        Motion: FootballEngine.Client.Animation.MotionDatabase
        Environment: HdrEnvironmentData
        Surfaces: SurfaceTexturePack
        Lighting: HdrLightingSettings
        Shadow: DirectionalShadowSettings
        Sun: SunState
        LightViewProjection: Matrix4x4
    }

module ViewerAssets =
    let private timed name load =
        let started = Stopwatch.GetTimestamp()
        let value = load ()
        Trace.WriteLine($"Viewer startup {name}: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms")
        value

    let load (assetDirectory: string) (exposureStops: float32 option) : Result<ViewerAssets, string> =
        let assetFile name = Path.Combine(assetDirectory, name)

        // Independent CPU packages decode concurrently; all native/GPU ownership stays on the render thread.
        let character =
            Task.Run(fun () -> timed "character" (fun () -> CharacterAsset.load (assetFile "player.glb")))

        let motion =
            Task.Run(fun () ->
                timed "motion database" (fun () ->
                    FootballEngine.Client.Animation.MotionDatabase.load (assetFile "motion-db.bin")))

        let kit =
            Task.Run(fun () -> timed "stadium kit" (fun () -> StadiumKit.load (assetFile "StadiumKit")))

        let environment =
            timed "environment" (fun () -> StadiumEnvironment.load (assetFile "stadium.environment.json"))
            |> Result.mapError (fun message -> $"Cannot load authored stadium environment: {message}")

        let surfaces =
            timed "surfaces" (fun () -> SurfaceTexturePack.load (assetFile "Surfaces"))
            |> Result.mapError (fun message -> $"Cannot load stadium surface materials: {message}")

        let kitData =
            kit.GetAwaiter().GetResult()
            |> Result.mapError (fun message -> $"Cannot load the stadium kit: {message}")

        let characterData =
            character.GetAwaiter().GetResult()
            |> Result.mapError (fun message -> $"Cannot load the first-party animated character: {message}")

        let motionData =
            motion.GetAwaiter().GetResult()
            |> Result.mapError (fun error -> $"Cannot load the motion database: %A{error}")

        let shadow = DirectionalShadow.stadium

        match environment, surfaces, characterData, kitData, motionData with
        | Error message, _, _, _, _
        | _, Error message, _, _, _
        | _, _, Error message, _, _
        | _, _, _, Error message, _
        | _, _, _, _, Error message -> Error message
        | Ok package, Ok surfacePack, Ok characterAsset, Ok(kitMesh, kitTextures), Ok motionDatabase ->
            let settings =
                { package.Settings with
                    ExposureStops = defaultArg exposureStops package.Settings.ExposureStops }

            match HdrLighting.validate settings with
            | Error message -> Error $"Invalid viewer exposure: {message}"
            | Ok lighting ->
                SceneBuilder.matrixOfElements (DirectionalShadow.matrixData shadow lighting.SunDirection)
                |> Result.map (fun lightViewProjection ->
                    { Kit = kitMesh
                      KitTextures = kitTextures
                      Character = characterAsset
                      Motion = motionDatabase
                      Environment = package.Environment
                      Surfaces = surfacePack
                      Lighting = lighting
                      Shadow = shadow
                      Sun =
                        { Direction = lighting.SunDirection
                          Radiance = lighting.SunRadiance
                          EnvironmentIntensity = lighting.EnvironmentIntensity }
                      LightViewProjection = lightViewProjection })
