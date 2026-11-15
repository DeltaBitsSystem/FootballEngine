namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Numerics
open System.Security.Cryptography
open System.Text.Json

type HdrLightingSettings =
    { ExposureStops: float32
      EnvironmentIntensity: float32
      SunDirection: Vector3
      SunRadiance: Vector3 }

type HdrEnvironmentData =
    { Width: int
      Height: int
      Layers: int
      LutSize: int
      SkyWidth: int
      SkyHeight: int
      ManifestSha256: string
      SunDirection: Vector3
      Environment: byte[]
      Brdf: byte[]
      Sky: byte[] }

type DirectionalShadowSettings =
    { Resolution: uint32
      Width: float32
      Height: float32
      LightDistance: float32
      FarPlane: float32 }

module DirectionalShadow =
    // Coverage includes both goals and the stands that can occlude the pitch, independent of camera cuts.
    let stadium =
        { Resolution = 4096u
          Width = 180.0f
          Height = 160.0f
          LightDistance = 180.0f
          FarPlane = 360.0f }

    let viewProjection settings (direction: Vector3) =
        // A light almost parallel to the usual up vector needs a different basis, including future night lights.
        let up =
            if abs (Vector3.Dot(direction, Vector3.UnitZ)) > 0.95f then
                Vector3.UnitX
            else
                Vector3.UnitZ

        let view =
            Matrix4x4.CreateLookAt(direction * settings.LightDistance, Vector3.Zero, up)

        view
        * Matrix4x4.CreateOrthographic(settings.Width, settings.Height, 1.0f, settings.FarPlane)

    let matrixData settings direction =
        let matrix = viewProjection settings direction

        [| matrix.M11
           matrix.M12
           matrix.M13
           matrix.M14
           matrix.M21
           matrix.M22
           matrix.M23
           matrix.M24
           matrix.M31
           matrix.M32
           matrix.M33
           matrix.M34
           matrix.M41
           matrix.M42
           matrix.M43
           matrix.M44 |]

module HdrLighting =
    let validate settings =
        let finiteVector (value: Vector3) =
            Single.IsFinite value.X && Single.IsFinite value.Y && Single.IsFinite value.Z

        if
            not (Single.IsFinite settings.ExposureStops)
            || abs settings.ExposureStops > 16.0f
        then
            Error "Exposure must be finite and between -16 and 16 stops."
        elif
            not (Single.IsFinite settings.EnvironmentIntensity)
            || settings.EnvironmentIntensity < 0.0f
        then
            Error "Environment intensity must be finite and nonnegative."
        elif
            not (finiteVector settings.SunDirection)
            || abs (settings.SunDirection.LengthSquared() - 1.0f) > 0.001f
            || settings.SunDirection.Y <= 0.0f
        then
            Error "Daylight sun direction must be normalized and above the horizon."
        elif
            not (finiteVector settings.SunRadiance)
            || min settings.SunRadiance.X (min settings.SunRadiance.Y settings.SunRadiance.Z) < 0.0f
        then
            Error "Sun radiance must be finite and nonnegative."
        else
            Ok settings

    let srgbToLinear value =
        if value <= 0.04045f then
            value / 12.92f
        else
            MathF.Pow((value + 0.055f) / 1.055f, 2.4f)

    let linearToSrgb value =
        if value <= 0.0031308f then
            value * 12.92f
        else
            1.055f * MathF.Pow(value, 1.0f / 2.4f) - 0.055f

    // Narkowicz's fitted ACES-style SDR curve is not a full ACES colour-management transform.
    let toneMap exposureStops radiance =
        let value = max 0.0f radiance * MathF.Pow(2.0f, exposureStops)
        Math.Clamp(value * (2.51f * value + 0.03f) / (value * (2.43f * value + 0.59f) + 0.14f), 0.0f, 1.0f)

    let loadEnvironment directory =
        try
            use manifestStream = File.OpenRead(Path.Combine(directory, "manifest.json"))

            if manifestStream.Length > 65536L then
                raise (InvalidDataException "HDR manifest exceeds the package metadata limit.")

            let manifestBytes = Array.zeroCreate<byte> (int manifestStream.Length)
            manifestStream.ReadExactly(manifestBytes.AsSpan())
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(manifestBytes))

            let root = document.RootElement
            let number (name: string) = root.GetProperty(name).GetInt32()

            let files = root.GetProperty("files").EnumerateArray() |> Seq.toArray
            let expectedFiles = Set.ofList [ "environment.bin"; "brdf.bin"; "sky.bin" ]
            let names = files |> Array.map (fun item -> item.GetProperty("path").GetString())

            // Ambiguous manifests must fail before allocating GPU-bound payloads, not silently select the first entry.
            if names.Length <> expectedFiles.Count || Set.ofArray names <> expectedFiles then
                raise (InvalidDataException "HDR manifest must contain exactly one entry for each required texture.")

            if
                number "schema" <> 3
                || number "width" <> 128
                || number "height" <> 64
                || number "layers" <> 7
                || number "specularLayers" <> 6
                || number "diffuseLayer" <> 6
                || number "lutSize" <> 128
                || number "skyWidth" <> 2048
                || number "skyHeight" <> 1024
            then
                raise (InvalidDataException "Unsupported HDR environment layout; rebuild the lighting pack.")

            for name, expected in
                [ "environmentFormat", "rgba16f-le"
                  "skyFormat", "rgba16f-le"
                  "brdfFormat", "rg16f-le"
                  "colorSpace", "linear-rec709"
                  "projection", "latlong-y-up"
                  "diffuseEncoding", "irradiance-over-pi"
                  "solarDisc", "visible-only" ] do
                if root.GetProperty(name).GetString() <> expected then
                    raise (InvalidDataException $"Unsupported HDR contract: {name}")

            let read name length =
                let entry =
                    files |> Seq.tryFind (fun item -> item.GetProperty("path").GetString() = name)

                let entry =
                    entry
                    |> Option.defaultWith (fun () -> raise (InvalidDataException $"Missing HDR manifest entry: {name}"))

                use stream = File.OpenRead(Path.Combine(directory, name))

                // Reject oversized or truncated packs before allocating from untrusted on-disk lengths.
                if stream.Length <> int64 length || entry.GetProperty("bytes").GetInt32() <> length then
                    raise (InvalidDataException $"HDR texture length mismatch: {name}")

                let data = Array.zeroCreate<byte> length
                stream.ReadExactly(data.AsSpan())
                let hash = SHA256.HashData data |> Convert.ToHexString

                if
                    data.Length <> length
                    || entry.GetProperty("bytes").GetInt32() <> length
                    || not (
                        String.Equals(hash, entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)
                    )
                then
                    raise (InvalidDataException $"HDR texture integrity failure: {name}")

                for offset in 0..2 .. data.Length - 2 do
                    let value =
                        BitConverter.UInt16BitsToHalf(uint16 data[offset] ||| (uint16 data[offset + 1] <<< 8))
                        |> float32

                    if not (Single.IsFinite value) || value < 0.0f then
                        raise (InvalidDataException $"Non-finite or negative HDR sample: {name}")

                data

            let sun =
                root.GetProperty("sunDirection").EnumerateArray()
                |> Seq.map (fun value -> value.GetSingle())
                |> Seq.toArray

            if sun.Length <> 3 || sun |> Array.exists (Single.IsFinite >> not) then
                raise (InvalidDataException "HDR sky has an invalid solar direction.")

            let sunDirection = Vector3(sun[0], sun[1], sun[2])

            if abs (sunDirection.LengthSquared() - 1.0f) > 0.001f || sunDirection.Y <= 0.0f then
                raise (InvalidDataException "HDR solar direction must be normalized and above the horizon.")

            Ok
                { Width = 128
                  Height = 64
                  Layers = 7
                  LutSize = 128
                  SkyWidth = 2048
                  SkyHeight = 1024
                  ManifestSha256 = SHA256.HashData manifestBytes |> Convert.ToHexString
                  SunDirection = sunDirection
                  Environment = read "environment.bin" (128 * 64 * 7 * 8)
                  Brdf = read "brdf.bin" (128 * 128 * 4)
                  Sky = read "sky.bin" (2048 * 1024 * 8) }
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? Collections.Generic.KeyNotFoundException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
        | :? FormatException as error -> Error error.Message
        | :? OverflowException as error -> Error error.Message
        | :? UnauthorizedAccessException as error -> Error error.Message
