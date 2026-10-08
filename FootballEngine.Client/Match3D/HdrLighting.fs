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
      Environment: byte[]
      Brdf: byte[] }

module HdrLighting =
    // Fixed exposure preserves broadcast readability instead of pumping as the camera follows a white kit.
    let broadcastDaylight =
        { ExposureStops = -0.35f
          EnvironmentIntensity = 0.65f
          SunDirection = Vector3.Normalize(Vector3(-0.38f, 0.88f, 0.28f))
          SunRadiance = Vector3(3.2f, 3.05f, 2.8f) }

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
            use document =
                JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))

            let root = document.RootElement
            let number (name: string) = root.GetProperty(name).GetInt32()

            if
                number "schema" <> 1
                || number "width" <> 128
                || number "height" <> 64
                || number "layers" <> 7
                || number "specularLayers" <> 6
                || number "diffuseLayer" <> 6
                || number "lutSize" <> 128
            then
                raise (InvalidDataException "Unsupported HDR environment layout; rebuild the lighting pack.")

            let read name length =
                let entry =
                    root.GetProperty("files").EnumerateArray()
                    |> Seq.tryFind (fun item -> item.GetProperty("path").GetString() = name)

                let entry =
                    entry
                    |> Option.defaultWith (fun () -> raise (InvalidDataException $"Missing HDR manifest entry: {name}"))

                let data = File.ReadAllBytes(Path.Combine(directory, name))
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

            Ok
                { Width = 128
                  Height = 64
                  Layers = 7
                  LutSize = 128
                  Environment = read "environment.bin" (128 * 64 * 7 * 8)
                  Brdf = read "brdf.bin" (128 * 128 * 4) }
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? Collections.Generic.KeyNotFoundException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
