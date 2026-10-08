namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json

type SurfaceTexturePack =
    { Size: int
      Levels: int
      Layers: int
      Albedo: byte[]
      Normal: byte[]
      Arm: byte[] }

module SurfaceTexturePack =
    let load directory =
        try
            use document =
                JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))

            let root = document.RootElement

            if root.GetProperty("schema").GetInt32() <> 1 then
                raise (InvalidDataException "Unsupported surface pack schema; rebuild the pack.")

            let size = root.GetProperty("size").GetInt32()
            let layers = root.GetProperty("layers").GetInt32()
            let levels = root.GetProperty("levels").GetInt32()

            if size <> 1024 || layers <> 2 || levels <> 11 then
                raise (InvalidDataException "Surface pack must contain two 1024-square layers and a full mip chain.")

            let expected =
                [ 0 .. levels - 1 ]
                |> List.sumBy (fun level -> let width = size >>> level in width * width * layers * 4)

            let read name =
                let entry =
                    root.GetProperty("files").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("path").GetString() = name)

                let data = File.ReadAllBytes(Path.Combine(directory, name))
                let hash = SHA256.HashData data |> Convert.ToHexString

                if
                    data.Length <> expected
                    || not (
                        String.Equals(hash, entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)
                    )
                then
                    raise (InvalidDataException $"Surface texture integrity failure: {name}")

                data

            Ok
                { Size = size
                  Levels = levels
                  Layers = layers
                  Albedo = read "albedo.bin"
                  Normal = read "normal.bin"
                  Arm = read "arm.bin" }
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? Collections.Generic.KeyNotFoundException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
