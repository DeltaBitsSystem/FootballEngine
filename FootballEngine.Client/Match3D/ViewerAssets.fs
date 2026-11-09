namespace FootballEngine.Client.Match3D

open System.Diagnostics
open System.IO
open System.Numerics
open System.Threading.Tasks

/// CPU-side packages the viewer needs before touching the GPU, already validated.
type ViewerAssets =
    { Stadium: StadiumAssetData
      Character: CharacterAssetData
      Environment: HdrEnvironmentData
      Surfaces: SurfaceTexturePack
      Lighting: HdrLightingSettings
      Shadow: DirectionalShadowSettings
      Sun: SunState
      LightViewProjection: Matrix4x4 }

module ViewerAssets =
    let private timed name load =
        let started = Stopwatch.GetTimestamp()
        let value = load ()
        Trace.WriteLine($"Viewer startup {name}: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms")
        value

    let load (assetPath: string) (exposureStops: float32 option) : Result<ViewerAssets, string> =
        let assetFile name =
            Path.Combine(Path.GetDirectoryName assetPath, name)

        // Independent CPU packages decode concurrently; all native/GPU ownership stays on the render thread.
        let character =
            Task.Run(fun () -> timed "character" (fun () -> CharacterAsset.load (assetFile "player.glb")))

        let stadium =
            Task.Run(fun () -> timed "stadium GLB" (fun () -> StadiumAsset.load assetPath))

        let environment =
            timed "environment" (fun () -> StadiumEnvironment.load assetPath)
            |> Result.mapError (fun message -> $"Cannot load authored stadium environment: {message}")

        let surfaces =
            timed "surfaces" (fun () -> SurfaceTexturePack.load (assetFile "Surfaces"))
            |> Result.mapError (fun message -> $"Cannot load stadium surface materials: {message}")

        let stadiumData = stadium.GetAwaiter().GetResult()

        let characterData =
            character.GetAwaiter().GetResult()
            |> Result.mapError (fun message -> $"Cannot load the first-party animated character: {message}")

        let shadow = DirectionalShadow.stadium

        match environment, surfaces, characterData with
        | Error message, _, _
        | _, Error message, _
        | _, _, Error message -> Error message
        | Ok package, Ok surfacePack, Ok characterAsset ->
            let settings =
                { package.Settings with
                    ExposureStops = defaultArg exposureStops package.Settings.ExposureStops }

            match HdrLighting.validate settings with
            | Error message -> Error $"Invalid viewer exposure: {message}"
            | Ok lighting ->
                SceneBuilder.matrixOfElements (DirectionalShadow.matrixData shadow lighting.SunDirection)
                |> Result.map (fun lightViewProjection ->
                    { Stadium = stadiumData
                      Character = characterAsset
                      Environment = package.Environment
                      Surfaces = surfacePack
                      Lighting = lighting
                      Shadow = shadow
                      Sun =
                        { Direction = lighting.SunDirection
                          Radiance = lighting.SunRadiance
                          EnvironmentIntensity = lighting.EnvironmentIntensity }
                      LightViewProjection = lightViewProjection })
