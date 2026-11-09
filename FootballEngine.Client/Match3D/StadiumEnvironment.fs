namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Numerics
open System.Text.Json

type StadiumEnvironmentData =
    { Settings: HdrLightingSettings
      Environment: HdrEnvironmentData }

module StadiumEnvironment =
    let private vector (root: JsonElement) (name: string) =
        let value = root.GetProperty(name)

        if value.GetArrayLength() <> 3 then
            raise (InvalidDataException $"Environment vector '{name}' requires three components.")

        Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle())

    let private matches expected actual =
        String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)

    /// The sidecar names the lighting folder beside it; the stadium itself is procedural and has no file to match.
    let load sidecarPath =
        try
            let sidecarPath = Path.GetFullPath sidecarPath
            use stream = File.OpenRead sidecarPath

            if stream.Length > 65536L then
                raise (InvalidDataException "Stadium environment metadata exceeds the package limit.")

            use document = JsonDocument.Parse stream
            let root = document.RootElement

            if root.GetProperty("schema").GetInt32() <> 2 then
                raise (InvalidDataException "Unsupported stadium environment schema; rebuild the stadium package.")

            let directory = root.GetProperty("environmentDirectory").GetString()

            // A portable single-folder reference cannot redirect a package to an arbitrary absolute path.
            if
                String.IsNullOrWhiteSpace directory
                || directory = "."
                || directory = ".."
                || directory.IndexOfAny([| '/'; '\\'; ':' |]) >= 0
            then
                raise (InvalidDataException "Environment directory must be a single relative folder name.")

            let settings =
                { ExposureStops = root.GetProperty("exposureStops").GetSingle()
                  EnvironmentIntensity = root.GetProperty("environmentIntensity").GetSingle()
                  SunDirection = vector root "sunDirection"
                  SunRadiance = vector root "sunRadiance" }

            match HdrLighting.validate settings with
            | Error error -> Error error
            | Ok settings ->
                let environmentPath = Path.Combine(Path.GetDirectoryName sidecarPath, directory)

                match HdrLighting.loadEnvironment environmentPath with
                | Error error -> Error error
                | Ok environment ->
                    if
                        not (
                            matches
                                (root.GetProperty("environmentManifestSha256").GetString())
                                environment.ManifestSha256
                        )
                    then
                        Error "HDR manifest does not match the stadium environment sidecar."
                    elif Vector3.Distance(settings.SunDirection, environment.SunDirection) > 0.0001f then
                        Error "Visible sky, image-based lighting and geometric shadows have different sun directions."
                    else
                        Ok
                            { Settings = settings
                              Environment = environment }
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? UnauthorizedAccessException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? Collections.Generic.KeyNotFoundException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
        | :? FormatException as error -> Error error.Message
        | :? OverflowException as error -> Error error.Message
