namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text.Json
open SharpGLTF.Schema2
open SkiaSharp

type CharacterTextureData =
    { Width: int
      Height: int
      Pixels: byte[] }

[<Struct>]
type CharacterLocalPose =
    { Translation: Vector3
      Rotation: Quaternion
      Scale: Vector3 }

type CharacterClip =
    { Id: string
      Duration: float
      NominalSpeed: float
      Loop: bool
      ContactTime: float option
      Frames: CharacterLocalPose[][] }

type CharacterAssetData =
    { Batches: DynamicMeshBatchData[]
      Albedo: CharacterTextureData
      NormalRoughness: CharacterTextureData
      Parents: int[]
      LeftLeg: struct (int * int * int * int)
      RightLeg: struct (int * int * int * int)
      JointNodes: int[]
      InverseBind: Matrix4x4[]
      Clips: Map<string, CharacterClip>
      JointCount: int }

module CharacterAsset =
    let private invalidData message = raise (InvalidDataException message)

    let private finiteVector (v: Vector4) =
        Single.IsFinite v.X
        && Single.IsFinite v.Y
        && Single.IsFinite v.Z
        && Single.IsFinite v.W

    let private read (path: string) =
        use metadata =
            JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path, null) + "-motion.json"))

        let manifest = metadata.RootElement

        if
            manifest.GetProperty("schemaVersion").GetInt32() <> 1
            || manifest.GetProperty("rigSchemaVersion").GetInt32() <> 1
        then
            invalidData "Unsupported character or rig schema. Rebuild the first-party pack."

        let expectedHash = manifest.GetProperty("file").GetProperty("sha256").GetString()
        let actualHash = SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString

        if not (String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase)) then
            invalidData "Character GLB does not match its motion manifest."

        let readTexture name =
            let entry =
                manifest.GetProperty("textures").EnumerateArray()
                |> Seq.tryFind (fun entry -> entry.GetProperty("path").GetString() = name)
                |> Option.defaultWith (fun () ->
                    invalidData $"Character texture '{name}' is missing from the manifest.")

            let texturePath = Path.Combine(Path.GetDirectoryName path, name)
            let bytes = File.ReadAllBytes texturePath
            let hash = SHA256.HashData(bytes) |> Convert.ToHexString

            if
                not (String.Equals(hash, entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            then
                invalidData $"Character texture '{name}' does not match the manifest."

            use source = SKBitmap.Decode bytes

            if isNull source || source.Width <> 4096 || source.Height <> 4096 then
                invalidData $"Character texture '{name}' must be a 4096-square atlas."

            use bitmap =
                new SKBitmap(SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul))

            if not (source.CopyTo(bitmap, SKColorType.Rgba8888)) then
                invalidData $"Cannot decode character texture '{name}'."

            let pixels = Array.zeroCreate<byte> (bitmap.Width * bitmap.Height * 4)
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length)

            { Width = bitmap.Width
              Height = bitmap.Height
              Pixels = pixels }

        let albedo = readTexture "player-albedo.png"
        let normalRoughness = readTexture "player-normal-roughness.png"

        let coordinates = manifest.GetProperty("coordinates")

        if
            coordinates.GetProperty("up").GetString() <> "+Y"
            || coordinates.GetProperty("forward").GetString() <> "+Z"
            || coordinates.GetProperty("units").GetString() <> "metres"
        then
            invalidData "Character coordinates must be metres, Y-up and Z-forward."

        let model = ModelRoot.Load path

        if model.LogicalSkins.Count <> 1 then
            invalidData "Character pack must contain exactly one skin."

        let skin = model.LogicalSkins[0]

        if
            skin.JointsCount <> manifest.GetProperty("joints").GetInt32()
            || skin.JointsCount <= 0
        then
            invalidData "Character skin does not match the manifest joint count."

        let ordered = ResizeArray<Node>()

        let rec visit (node: Node) =
            ordered.Add node

            for child in node.VisualChildren do
                visit child

        for node in model.LogicalNodes do
            if isNull node.VisualParent then
                visit node

        let nodes = ordered.ToArray()
        let nodeIndices = nodes |> Array.mapi (fun i n -> n.LogicalIndex, i) |> Map.ofArray

        let parents =
            nodes
            |> Array.map (fun n ->
                if isNull n.VisualParent then
                    -1
                else
                    nodeIndices[n.VisualParent.LogicalIndex])

        let jointNodes =
            skin.Joints |> Seq.map (fun n -> nodeIndices[n.LogicalIndex]) |> Seq.toArray

        let inverseBind = skin.InverseBindMatrices |> Seq.toArray

        let leg suffix =
            let node name =
                nodes
                |> Array.tryFindIndex (fun node -> node.Name = name + "." + suffix)
                |> Option.defaultWith (fun () -> invalidData $"Required contact joint '{name}.{suffix}' is missing.")

            struct (node "thigh", node "shin", node "foot", node "toe")

        let clips =
            manifest.GetProperty("clips").EnumerateArray()
            |> Seq.map (fun item ->
                let id = item.GetProperty("id").GetString()
                let duration = item.GetProperty("durationSeconds").GetDouble()

                let animation =
                    model.LogicalAnimations
                    |> Seq.tryFind (fun animation -> animation.Name = id)
                    |> Option.defaultWith (fun () -> invalidData $"Character motion '{id}' is missing from the GLB.")

                if not (Double.IsFinite duration) || duration <= 0.0 then
                    invalidData $"Character motion '{id}' has invalid duration."

                let samples = max 1 (int (ceil (duration * 60.0)))

                let frames =
                    Array.init (samples + 1) (fun index ->
                        let time = float32 (float index / float samples * duration)

                        nodes
                        |> Array.map (fun node ->
                            let transform = node.GetLocalTransform(animation, time)

                            { Translation = transform.Translation
                              Rotation = transform.Rotation
                              Scale = transform.Scale }))

                id,
                { Id = id
                  Duration = duration
                  NominalSpeed = item.GetProperty("nominalSpeedMetersPerSecond").GetDouble()
                  Loop = item.GetProperty("loop").GetBoolean()
                  ContactTime =
                    let contact = item.GetProperty("contact")

                    if contact.ValueKind = JsonValueKind.Null then
                        None
                    else
                        Some(contact.GetProperty("normalizedTime").GetDouble() * duration)
                  Frames = frames })
            |> Map.ofSeq

        for id in [ "idle"; "walk"; "run"; "sprint" ] do
            if not (clips.ContainsKey id) then
                invalidData $"Required locomotion motion '{id}' is missing."

        let vertices = ResizeArray<GpuVertex>()
        let indices = ResizeArray<uint32>()

        for node in nodes do
            if not (isNull node.Mesh) then
                if node.Skin <> skin then
                    invalidData "Every character mesh must be bound to the canonical skin."

                for primitive in node.Mesh.Primitives do
                    if primitive.DrawPrimitiveType <> PrimitiveType.TRIANGLES then
                        invalidData "Character pack contains a non-triangle primitive."

                    let positions = primitive.GetVertexAccessor("POSITION").AsVector3Array()
                    let normals = primitive.GetVertexAccessor("NORMAL").AsVector3Array()
                    let joints = primitive.GetVertexAccessor("JOINTS_0").AsVector4Array()
                    let weights = primitive.GetVertexAccessor("WEIGHTS_0").AsVector4Array()
                    let uvs = primitive.GetVertexAccessor("TEXCOORD_0").AsVector2Array()
                    let material = primitive.Material
                    let color = material.FindChannel("BaseColor").Value.Color
                    let offset = uint32 vertices.Count

                    for i = 0 to positions.Count - 1 do
                        let j, w = joints[i], weights[i]
                        let uv = uvs[i]

                        if
                            not (
                                finiteVector (Vector4(positions[i], 1.0f))
                                && finiteVector (Vector4(normals[i], 0.0f))
                            )
                        then
                            invalidData "Character contains non-finite geometry."

                        if
                            not (Single.IsFinite uv.X && Single.IsFinite uv.Y)
                            || uv.X < 0.0f
                            || uv.X > 1.0f
                            || uv.Y < 0.0f
                            || uv.Y > 0.5f
                        then
                            invalidData "Character contains UVs outside the home atlas half."

                        let sum = w.X + w.Y + w.Z + w.W

                        if
                            not (finiteVector j && finiteVector w)
                            || abs (sum - 1.0f) > 0.0001f
                            || min (min w.X w.Y) (min w.Z w.W) < 0.0f
                        then
                            invalidData "Character contains invalid skin weights."

                        for joint in [ j.X; j.Y; j.Z; j.W ] do
                            if
                                joint < 0.0f
                                || joint >= float32 skin.JointsCount
                                || MathF.Truncate joint <> joint
                            then
                                invalidData "Character contains an out-of-range joint index."

                        vertices.Add
                            { Position = positions[i]
                              Normal = normals[i]
                              Color = color
                              Uv = uv
                              RootIndex = 0u
                              Joints = j
                              Weights = w
                              Surface = uint32 StadiumSurface.Plain }

                    for struct (a, b, c) in primitive.GetTriangleIndices() do
                        indices.Add(offset + uint32 a)
                        indices.Add(offset + uint32 b)
                        indices.Add(offset + uint32 c)

        if vertices.Count = 0 || indices.Count = 0 then
            invalidData "Character pack contains no renderable geometry."

        // Instance identity selects transforms, palettes and kit UVs on the GPU, not duplicated geometry.
        { Batches =
            [| { MaterialName = "FE first-party skinned players"
                 Vertices = vertices.ToArray()
                 Indices = indices.ToArray() } |]
          Albedo = albedo
          NormalRoughness = normalRoughness
          Parents = parents
          LeftLeg = leg "L"
          RightLeg = leg "R"
          JointNodes = jointNodes
          InverseBind = inverseBind
          Clips = clips
          JointCount = skin.JointsCount }

    let load path =
        try
            Ok(read path)
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? Collections.Generic.KeyNotFoundException as error -> Error error.Message

module CharacterPose =
    let contactSampleTime (clip: CharacterClip) started contact ended time =
        let marker = clip.ContactTime |> Option.defaultValue (clip.Duration * 0.5)

        if time <= contact then
            marker * Math.Clamp((time - started) / max 1e-9 (contact - started), 0.0, 1.0)
        else
            marker
            + (clip.Duration - marker)
              * Math.Clamp((time - contact) / max 1e-9 (ended - contact), 0.0, 1.0)

    let createScratch (asset: CharacterAssetData) =
        Array.create asset.Parents.Length Matrix4x4.Identity

    let sampleInto
        (asset: CharacterAssetData)
        (clip: CharacterClip)
        time
        (world: Matrix4x4[])
        (palette: Matrix4x4[])
        offset
        =
        let time =
            if clip.Loop then
                ((time % clip.Duration) + clip.Duration) % clip.Duration
            else
                Math.Clamp(time, 0.0, clip.Duration)

        let position = time / clip.Duration * float (clip.Frames.Length - 1)
        let first = min (clip.Frames.Length - 1) (int (floor position))
        let second = min (clip.Frames.Length - 1) (first + 1)
        let blend = float32 (position - float first)

        for node = 0 to world.Length - 1 do
            let a, b = clip.Frames[first][node], clip.Frames[second][node]

            let local =
                Matrix4x4.CreateScale(Vector3.Lerp(a.Scale, b.Scale, blend))
                * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(a.Rotation, b.Rotation, blend))
                * Matrix4x4.CreateTranslation(Vector3.Lerp(a.Translation, b.Translation, blend))

            world[node] <-
                if asset.Parents[node] < 0 then
                    local
                else
                    local * world[asset.Parents[node]]

        for joint = 0 to asset.JointCount - 1 do
            palette[offset + joint] <- asset.InverseBind[joint] * world[asset.JointNodes[joint]]

    let private rotationBetween (a: Vector3) (b: Vector3) =
        let a, b = Vector3.Normalize a, Vector3.Normalize b
        let dot = Math.Clamp(Vector3.Dot(a, b), -1.0f, 1.0f)

        if dot > 0.99999f then
            Quaternion.Identity
        elif dot < -0.99999f then
            let basis = if abs a.X < 0.9f then Vector3.UnitX else Vector3.UnitY
            Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(a, basis)), MathF.PI)
        else
            Quaternion.Normalize(Quaternion(Vector3.Cross(a, b), 1.0f + dot))

    let private rotateDescendants (asset: CharacterAssetData) (world: Matrix4x4[]) root rotation =
        let pivot = world[root].Translation

        let delta =
            Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(pivot)

        for node = root to world.Length - 1 do
            let mutable ancestor = node

            while ancestor > root do
                ancestor <- asset.Parents[ancestor]

            if ancestor = root then
                world[node] <- world[node] * delta

    /// Two rigid rotations preserve bone lengths; unreachable targets clamp rather than stretching the boot.
    let alignFootInto
        (asset: CharacterAssetData)
        leftFoot
        (target: Vector3)
        weight
        (world: Matrix4x4[])
        (palette: Matrix4x4[])
        offset
        =
        let struct (hipNode, kneeNode, ankleNode, toeNode) =
            if leftFoot then asset.LeftLeg else asset.RightLeg

        let hip = world[hipNode].Translation
        let knee = world[kneeNode].Translation
        let ankle = world[ankleNode].Translation
        let upper, lower = Vector3.Distance(hip, knee), Vector3.Distance(knee, ankle)

        let desiredToe =
            Vector3.Lerp(world[toeNode].Translation, target, Math.Clamp(weight, 0.0f, 1.0f))

        let desiredAnkle = desiredToe - (world[toeNode].Translation - ankle)
        let delta = desiredAnkle - hip

        if delta.LengthSquared() > 1e-8f && upper > 1e-5f && lower > 1e-5f then
            let distance =
                Math.Clamp(delta.Length(), abs (upper - lower) + 1e-5f, upper + lower - 1e-5f)

            let forward = Vector3.Normalize delta
            let projected = knee - hip - forward * Vector3.Dot(knee - hip, forward)

            let pole =
                if projected.LengthSquared() > 1e-8f then
                    Vector3.Normalize projected
                else
                    let basis =
                        if abs forward.Y < 0.9f then
                            Vector3.UnitY
                        else
                            Vector3.UnitZ

                    Vector3.Normalize(Vector3.Cross(forward, basis))

            let along =
                (upper * upper + distance * distance - lower * lower) / (2.0f * distance)

            let height = MathF.Sqrt(max 0.0f (upper * upper - along * along))
            let desiredKnee = hip + forward * along + pole * height
            rotateDescendants asset world hipNode (rotationBetween (knee - hip) (desiredKnee - hip))
            let newKnee, newAnkle = world[kneeNode].Translation, world[ankleNode].Translation

            rotateDescendants
                asset
                world
                kneeNode
                (rotationBetween (newAnkle - newKnee) (hip + forward * distance - newKnee))

            let solvedAnkle, solvedToe =
                world[ankleNode].Translation, world[toeNode].Translation

            if Vector3.DistanceSquared(desiredToe, solvedAnkle) > 1e-8f then
                rotateDescendants
                    asset
                    world
                    ankleNode
                    (rotationBetween (solvedToe - solvedAnkle) (desiredToe - solvedAnkle))

            for joint = 0 to asset.JointCount - 1 do
                palette[offset + joint] <- asset.InverseBind[joint] * world[asset.JointNodes[joint]]
