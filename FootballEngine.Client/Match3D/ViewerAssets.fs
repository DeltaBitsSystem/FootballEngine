namespace FootballEngine.Client.Match3D

open System.Diagnostics
open System.IO
open System.Numerics
open System.Threading.Tasks

/// CPU-side packages the viewer needs before touching the GPU, already validated.
type ViewerAssets =
    { Kit: StadiumKitData
      /// Dropped by the renderer once uploaded; the GPU copy is the only one the viewer needs.
      KitTextures: KitTextureData
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

    let load (assetDirectory: string) (exposureStops: float32 option) : Result<ViewerAssets, string> =
        let assetFile name = Path.Combine(assetDirectory, name)

        // Independent CPU packages decode concurrently; all native/GPU ownership stays on the render thread.
        let character =
            Task.Run(fun () -> timed "character" (fun () -> CharacterAsset.load (assetFile "player.glb")))

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

        let shadow = DirectionalShadow.stadium

        match environment, surfaces, characterData, kitData with
        | Error message, _, _, _
        | _, Error message, _, _
        | _, _, Error message, _
        | _, _, _, Error message -> Error message
        | Ok package, Ok surfacePack, Ok characterAsset, Ok(kitMesh, kitTextures) ->
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
                      Environment = package.Environment
                      Surfaces = surfacePack
                      Lighting = lighting
                      Shadow = shadow
                      Sun =
                        { Direction = lighting.SunDirection
                          Radiance = lighting.SunRadiance
                          EnvironmentIntensity = lighting.EnvironmentIntensity }
                      LightViewProjection = lightViewProjection })
