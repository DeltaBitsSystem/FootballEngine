namespace FootballEngine.Client.Match3D

open System
open System.Collections.Generic
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open SharpGLTF.Schema2
open SkiaSharp

/// An index range inside the kit's single shared index buffer.
[<Struct>]
type KitRange = { First: uint32; Count: uint32 }

type KitLod =
    { FromDistance: float32
      /// Depth-writing geometry, drawn by the scene pipeline and the shadow caster.
      Opaque: KitRange
      /// Glass, polycarbonate and nets: composited after opaque geometry and never casting shadows.
      Blended: KitRange }

type KitPiece =
    { Id: string
      Lods: KitLod[]
      /// Blender piece space (z up, +y towards the pitch), the frame the layout places pieces in.
      BoundsCentre: Vector3
      BoundsRadius: float32 }

type KitMaterial =
    | Textured of layer: int * tint: Vector3 * tintRole: bool * alphaMask: bool
    | Flat of baseColor: Vector3 * roughness: float32 * metallic: float32 * opacity: float32 * tintRole: bool
    | Led of baseColor: Vector3 * roughness: float32

type StadiumKitData =
    { Pieces: KitPiece[]
      Vertices: GpuVertex[]
      Indices: uint32[]
      /// Stand, roof and glazing geometry generated at runtime shares the kit's materials.
      Materials: IReadOnlyDictionary<string, KitMaterial> }

/// Kept apart from the mesh data so the viewer can drop these ~110 MB of pixels once they are on the GPU.
type KitTextureData =
    { Size: int
      Layers: int
      Levels: int
      /// Level-major, then layer: the order `GpuTexture.UploadLevel` consumes for a whole array level.
      Albedo: byte[]
      Normal: byte[]
      Orm: byte[] }

module KitTextureData =
    let empty =
        { Size = 0
          Layers = 0
          Levels = 0
          Albedo = [||]
          Normal = [||]
          Orm = [||] }

module StadiumKit =
    let private invalidData message = raise (InvalidDataException message)

    let private vector3 (element: JsonElement) =
        Vector3(element[0].GetSingle(), element[1].GetSingle(), element[2].GetSingle())

    let private optional (element: JsonElement) (name: string) =
        match element.TryGetProperty name with
        | true, value -> Some value
        | false, _ -> None

    // Blender appends ".001" when a material name collides during export; the kit id is the name before it.
    let private materialSuffix = Regex(@"\.\d{3}$", RegexOptions.Compiled)

    let materialId (gltfName: string) =
        let trimmed =
            if gltfName.StartsWith("kit.", StringComparison.Ordinal) then
                gltfName.Substring 4
            else
                gltfName

        materialSuffix.Replace(trimmed, "")

    let private textureBase (path: string) =
        let name = Path.GetFileNameWithoutExtension path

        if name.EndsWith("_albedo", StringComparison.Ordinal) then
            name.Substring(0, name.Length - "_albedo".Length)
        else
            invalidData $"Kit albedo texture '{path}' does not follow the <base>_albedo naming contract."

    let private srgbToLinear =
        Array.init 256 (fun value ->
            let c = float32 value / 255.0f

            if c <= 0.04045f then
                c / 12.92f
            else
                MathF.Pow((c + 0.055f) / 1.055f, 2.4f))

    let private linearToSrgb (linear: float32) =
        let c =
            if linear <= 0.0031308f then
                linear * 12.92f
            else
                1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f

        byte (Math.Clamp(MathF.Round(c * 255.0f), 0.0f, 255.0f))

    type private MipFilter =
        | SrgbColour
        | NormalMap
        | LinearData

    /// One 2x2 box step. Colour averages in linear light and normals are renormalised, so distant kit surfaces keep
    /// their mean brightness and lighting instead of the darkening and flattening of naive byte averaging.
    let private downsample (filter: MipFilter) (source: byte[]) (size: int) =
        let half = size / 2
        let target = Array.zeroCreate<byte> (half * half * 4)

        Parallel.For(
            0,
            half,
            fun y ->
                for x in 0 .. half - 1 do
                    let at dx dy channel =
                        source[((2 * y + dy) * size + 2 * x + dx) * 4 + channel]

                    let output = (y * half + x) * 4

                    match filter with
                    | SrgbColour ->
                        for channel in 0..2 do
                            let sum =
                                srgbToLinear[int (at 0 0 channel)]
                                + srgbToLinear[int (at 1 0 channel)]
                                + srgbToLinear[int (at 0 1 channel)]
                                + srgbToLinear[int (at 1 1 channel)]

                            target[output + channel] <- linearToSrgb (sum * 0.25f)

                        target[output + 3] <-
                            byte ((int (at 0 0 3) + int (at 1 0 3) + int (at 0 1 3) + int (at 1 1 3) + 2) / 4)
                    | NormalMap ->
                        let decode channel =
                            (float32 (int (at 0 0 channel) + int (at 1 0 channel) + int (at 0 1 channel) + int (at 1 1 channel))
                             / 1020.0f)
                            * 2.0f
                            - 1.0f

                        let n = Vector3(decode 0, decode 1, decode 2)

                        let n =
                            if n.LengthSquared() > 0.000001f then
                                Vector3.Normalize n
                            else
                                Vector3.UnitZ

                        target[output] <- byte (MathF.Round((n.X * 0.5f + 0.5f) * 255.0f))
                        target[output + 1] <- byte (MathF.Round((n.Y * 0.5f + 0.5f) * 255.0f))
                        target[output + 2] <- byte (MathF.Round((n.Z * 0.5f + 0.5f) * 255.0f))
                        target[output + 3] <- 255uy
                    | LinearData ->
                        for channel in 0..3 do
                            target[output + channel] <-
                                byte (
                                    (int (at 0 0 channel) + int (at 1 0 channel) + int (at 0 1 channel) + int (at 1 1 channel) + 2)
                                    / 4
                                )
        )
        |> ignore

        target

    let private decodePng (path: string) (expectedSha: string option) (size: int) =
        let bytes = File.ReadAllBytes path

        match expectedSha with
        | Some sha when not (String.Equals(sha, Convert.ToHexString(SHA256.HashData bytes), StringComparison.OrdinalIgnoreCase)) ->
            invalidData $"Kit texture '{Path.GetFileName path}' does not match kit.json."
        | _ -> ()

        use stream = new SKMemoryStream(bytes)
        use codec = SKCodec.Create(stream)

        if isNull codec || codec.Info.Width <> size || codec.Info.Height <> size then
            invalidData $"Kit texture '{Path.GetFileName path}' must be {size}x{size}."

        use bitmap =
            new SKBitmap(SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul))

        // ORM and normal alpha are data; premultiplication would corrupt their colour channels.
        if codec.GetPixels(bitmap.Info, bitmap.GetPixels()) <> SKCodecResult.Success then
            invalidData $"Cannot decode kit texture '{Path.GetFileName path}'."

        let pixels = Array.zeroCreate<byte> (size * size * 4)
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length)
        pixels

    let private mipChain filter (level0: byte[]) (size: int) =
        let levels = ResizeArray<byte[]>([ level0 ])
        let mutable current = level0
        let mutable width = size

        while width > 1 do
            current <- downsample filter current width
            width <- width / 2
            levels.Add current

        levels.ToArray()

    /// Interleaves per-layer chains into the level-major layout of a texture array upload.
    let private packArray (chains: byte[][][]) =
        let levels = chains[0].Length

        [| for level in 0 .. levels - 1 do
               for chain in chains do
                   yield! chain[level] |]

    let private parseMaterial (layers: IReadOnlyDictionary<string, int>) (element: JsonElement) =
        let tintRole = (optional element "tintRole").IsSome

        match optional element "textures" with
        | Some textures ->
            let layer = layers[textureBase (textures.GetProperty("albedo").GetString())]

            let alphaMask =
                optional element "alphaFromAlbedoRed"
                |> Option.exists (fun value -> value.ValueKind = JsonValueKind.True)

            Textured(layer, vector3 (element.GetProperty "tint"), tintRole, alphaMask)
        | None ->
            let baseColor = vector3 (element.GetProperty "baseColor")
            let roughness = element.GetProperty("roughness").GetSingle()

            if (optional element "emissiveContent").IsSome then
                Led(baseColor, roughness)
            else
                // Transmission is approximated as coverage: a broadcast lens sees clear glazing as mostly see-through.
                let opacity = 1.0f - element.GetProperty("transmission").GetSingle()
                Flat(baseColor, roughness, element.GetProperty("metallic").GetSingle(), opacity, tintRole)

    let private isBlended material =
        match material with
        | Textured(_, _, _, alphaMask) -> alphaMask
        | Flat(_, _, _, opacity, _) -> opacity < 1.0f
        | Led _ -> false

    let vertex (root: uint32) (material: KitMaterial) (screen: Vector2) (position: Vector3) (normal: Vector3) (uv: Vector2) =
        let surface, color, joints =
            match material with
            | Textured(layer, tint, tintRole, alphaMask) ->
                uint32 StadiumSurface.KitTextured + uint32 layer,
                Vector4(tint, 1.0f),
                Vector4(0.0f, 0.0f, (if tintRole then 1.0f else 0.0f), (if alphaMask then 1.0f else 0.0f))
            | Flat(baseColor, roughness, metallic, opacity, tintRole) ->
                uint32 StadiumSurface.KitFlat,
                Vector4(baseColor, opacity),
                Vector4(roughness, metallic, (if tintRole then 1.0f else 0.0f), 0.0f)
            | Led(baseColor, roughness) ->
                uint32 StadiumSurface.KitLed, Vector4(baseColor, 1.0f), Vector4(screen.X, screen.Y, roughness, 0.0f)

        { Position = position
          Normal = normal
          Color = color
          Uv = uv
          RootIndex = root
          Joints = joints
          Weights = Vector4.Zero
          Surface = surface }

    let private loadUnchecked (directory: string) : StadiumKitData * KitTextureData =
        let manifestPath = Path.Combine(directory, "kit.json")
        use document = JsonDocument.Parse(File.ReadAllText manifestPath)
        let root = document.RootElement

        if root.GetProperty("schema").GetInt32() <> 1 then
            invalidData "Unsupported stadium kit schema; rebuild the kit."

        let files =
            root.GetProperty("files").EnumerateObject()
            |> Seq.map (fun property -> property.Name, property.Value.GetString())
            |> dict

        let sha name =
            match files.TryGetValue name with
            | true, value -> Some value
            | false, _ -> None

        let materials = root.GetProperty("materials").EnumerateArray() |> Seq.toArray

        let bases =
            materials
            |> Array.choose (fun material ->
                optional material "textures"
                |> Option.map (fun textures -> textureBase (textures.GetProperty("albedo").GetString())))
            |> Array.distinct
            |> Array.sort

        let layers =
            bases |> Array.mapi (fun index name -> name, index) |> readOnlyDict

        let materialTable =
            materials
            |> Array.map (fun element -> element.GetProperty("id").GetString(), parseMaterial layers element)
            |> readOnlyDict

        let size = 1024

        // Twenty-one decodes and mip chains dominate kit startup; they are independent, so they run concurrently.
        let chains (suffix: string) filter =
            bases
            |> Array.map (fun name ->
                Task.Run(fun () ->
                    let relative = $"Textures/{name}_{suffix}.png"
                    mipChain filter (decodePng (Path.Combine(directory, relative)) (sha relative) size) size))

        let albedo = chains "albedo" SrgbColour
        let normal = chains "normal" NormalMap
        let orm = chains "orm" LinearData

        let glbPath = Path.Combine(directory, "stadium-kit.glb")

        match sha "stadium-kit.glb" with
        | Some expected when
            not (
                String.Equals(
                    expected,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes glbPath)),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ->
            invalidData "stadium-kit.glb does not match kit.json."
        | _ -> ()

        let model = ModelRoot.Load glbPath

        let nodes =
            model.LogicalNodes
            |> Seq.filter (fun node -> not (isNull node.Mesh))
            |> Seq.map (fun node -> node.Name, node)
            |> dict

        let vertices = ResizeArray<GpuVertex>()
        let indices = ResizeArray<uint32>()

        let appendLod (node: Node) (screen: Vector2) =
            let opaque = ResizeArray<uint32>()
            let blended = ResizeArray<uint32>()

            for primitive in node.Mesh.Primitives do
                if primitive.DrawPrimitiveType = PrimitiveType.TRIANGLES then
                    let id =
                        if isNull primitive.Material then
                            invalidData $"Kit node '{node.Name}' has a primitive without a kit material."
                        else
                            materialId primitive.Material.Name

                    let material =
                        match materialTable.TryGetValue id with
                        | true, material -> material
                        | false, _ -> invalidData $"Kit node '{node.Name}' uses unknown material '{id}'."

                    let positions = primitive.GetVertexAccessor("POSITION").AsVector3Array()
                    let normals = primitive.GetVertexAccessor("NORMAL").AsVector3Array()

                    let uvs =
                        match primitive.VertexAccessors.TryGetValue "TEXCOORD_0" with
                        | true, accessor -> Some(accessor.AsVector2Array())
                        | false, _ -> None

                    let offset = uint32 vertices.Count

                    for index in 0 .. positions.Count - 1 do
                        let uv =
                            match uvs with
                            | Some values -> values[index]
                            | None -> Vector2.Zero

                        vertices.Add(vertex SceneRoot.instanced material screen positions[index] normals[index] uv)

                    let target = if isBlended material then blended else opaque

                    for struct (a, b, c) in primitive.GetTriangleIndices() do
                        target.Add(offset + uint32 a)
                        target.Add(offset + uint32 b)
                        target.Add(offset + uint32 c)

            let opaqueRange =
                { First = uint32 indices.Count
                  Count = uint32 opaque.Count }

            indices.AddRange opaque

            let blendedRange =
                { First = uint32 indices.Count
                  Count = uint32 blended.Count }

            indices.AddRange blended
            opaqueRange, blendedRange

        let pieces =
            root.GetProperty("pieces").EnumerateArray()
            |> Seq.map (fun piece ->
                let id = piece.GetProperty("id").GetString()

                let screen =
                    piece.GetProperty("screens").EnumerateArray()
                    |> Seq.tryHead
                    |> Option.map (fun screen ->
                        Vector2(screen.GetProperty("widthMetres").GetSingle(), screen.GetProperty("heightMetres").GetSingle()))
                    |> Option.defaultValue Vector2.One

                let lods =
                    piece.GetProperty("lods").EnumerateArray()
                    |> Seq.map (fun lod ->
                        let name = lod.GetProperty("node").GetString()

                        let node =
                            match nodes.TryGetValue name with
                            | true, node -> node
                            | false, _ -> invalidData $"stadium-kit.glb has no node '{name}'."

                        let opaque, blended = appendLod node screen

                        { FromDistance = lod.GetProperty("fromDistance").GetSingle()
                          Opaque = opaque
                          Blended = blended })
                    |> Seq.toArray

                if lods.Length = 0 || lods[0].FromDistance <> 0.0f then
                    invalidData $"Kit piece '{id}' needs a level of detail starting at distance zero."

                let low = vector3 (piece.GetProperty "boundsMin")
                let high = vector3 (piece.GetProperty "boundsMax")

                { Id = id
                  Lods = lods
                  BoundsCentre = (low + high) * 0.5f
                  BoundsRadius = (high - low).Length() * 0.5f })
            |> Seq.toArray

        let await (tasks: Task<byte[][]>[]) =
            tasks |> Array.map (fun task -> task.GetAwaiter().GetResult())

        let albedoChains = await albedo

        { Pieces = pieces
          Vertices = vertices.ToArray()
          Indices = indices.ToArray()
          Materials = materialTable },
        { Size = size
          Layers = bases.Length
          Levels = albedoChains[0].Length
          Albedo = packArray albedoChains
          Normal = packArray (await normal)
          Orm = packArray (await orm) }

    let load (directory: string) : Result<StadiumKitData * KitTextureData, string> =
        try
            Ok(loadUnchecked directory)
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? UnauthorizedAccessException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? KeyNotFoundException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? AggregateException as error -> Error(error.GetBaseException().Message)
        | :? ArgumentException as error -> Error error.Message

    let pieceIndex (kit: StadiumKitData) (id: string) =
        kit.Pieces |> Array.tryFindIndex (fun piece -> piece.Id = id)
