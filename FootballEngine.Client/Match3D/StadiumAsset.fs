namespace FootballEngine.Client.Match3D

open System
open System.Collections.Generic
open System.Numerics
open System.Runtime.InteropServices
open SharpGLTF.Schema2

[<RequireQualifiedAccess>]
type StadiumSurface =
    | Plain = 0
    | TurfLight = 1
    | TurfDark = 2
    | TurfRunoff = 3
    | Concrete = 4
    | Steel = 5
    | Enamel = 6

module StadiumSurface =
    let ofMaterialName (name: string) =
        if name.StartsWith("Grass |", StringComparison.Ordinal) then
            if name.Contains("stripe 0", StringComparison.Ordinal) then
                StadiumSurface.TurfLight
            else
                StadiumSurface.TurfDark
        else
            match name with
            | "Runoff grass" -> StadiumSurface.TurfRunoff
            | "Architectural concrete" -> StadiumSurface.Concrete
            | "Midnight steel" -> StadiumSurface.Steel
            | "Ivory enamel" -> StadiumSurface.Enamel
            | _ -> StadiumSurface.Plain

[<Struct; StructLayout(LayoutKind.Sequential)>]
type GpuVertex =
    { Position: Vector3
      Normal: Vector3
      Color: Vector4
      Uv: Vector2
      RootIndex: uint32
      Joints: Vector4
      Weights: Vector4
      Surface: uint32 }

type MeshBatchData =
    { MaterialName: string
      Vertices: GpuVertex array
      Indices: uint32 array }

type DynamicMeshBatchData =
    { MaterialName: string
      Vertices: GpuVertex array
      Indices: uint32 array }

type StadiumCameraData =
    { World: Matrix4x4
      VerticalFov: float32
      NearPlane: float32
      FarPlane: float32 }

type StadiumAssetData =
    { StaticBatches: MeshBatchData array
      DynamicBatches: DynamicMeshBatchData array
      Camera: StadiumCameraData
      BallRadius: float32 }

module StadiumAsset =
    type private BatchBuilder(materialName: string) =
        let vertices = ResizeArray<GpuVertex>()
        let indices = ResizeArray<uint32>()

        member _.MaterialName = materialName
        member _.Vertices = vertices
        member _.Indices = indices

    let private materialDetails (material: Material) =
        let name =
            if isNull material || String.IsNullOrWhiteSpace material.Name then
                "Default"
            else
                material.Name

        let channel =
            if isNull material then
                Nullable()
            else
                material.FindChannel("BaseColor")

        let baseColor =
            if channel.HasValue then
                channel.Value.Color
            else
                Vector4(0.8f, 0.8f, 0.8f, 1.0f)

        name, baseColor

    let private getBuilder (builders: Dictionary<string, BatchBuilder>) materialName =
        match builders.TryGetValue materialName with
        | true, builder -> builder
        | false, _ ->
            let builder = BatchBuilder(materialName)
            builders.Add(materialName, builder)
            builder

    let private readAccessor (primitive: MeshPrimitive) name convert =
        match primitive.VertexAccessors.TryGetValue name with
        | true, accessor -> Some(convert accessor)
        | false, _ -> None

    let private appendPrimitive
        (builders: Dictionary<string, BatchBuilder>)
        (primitive: MeshPrimitive)
        (transform: Matrix4x4)
        (rootIndex: int option)
        (ballRadius: float32 ref option)
        =
        // The pitch shader paints blades directly; retaining the raised chalk meshes would duplicate and float the markings.
        let raisedPitchPaint =
            not (isNull primitive.Material)
            && primitive.Material.Name = "Chalk | warm white"

        if primitive.DrawPrimitiveType = PrimitiveType.TRIANGLES && not raisedPitchPaint then
            let materialName, color = materialDetails primitive.Material
            let builder = getBuilder builders materialName
            let positions = primitive.GetVertexAccessor("POSITION").AsVector3Array()

            let normals =
                readAccessor primitive "NORMAL" (fun accessor -> accessor.AsVector3Array())

            let uvs =
                readAccessor primitive "TEXCOORD_0" (fun accessor -> accessor.AsVector2Array())

            let normalTransform =
                let mutable inverse = Matrix4x4.Identity

                if Matrix4x4.Invert(transform, &inverse) then
                    Matrix4x4.Transpose inverse
                else
                    transform

            let vertexOffset = uint32 builder.Vertices.Count

            for index = 0 to positions.Count - 1 do
                let position = Vector3.Transform(positions[index], transform)

                let normal =
                    match normals with
                    | Some values when index < values.Count ->
                        Vector3.TransformNormal(values[index], normalTransform) |> Vector3.Normalize
                    | _ -> Vector3.UnitY

                let uv =
                    match uvs with
                    | Some values when index < values.Count -> values[index]
                    | _ -> Vector2.Zero

                builder.Vertices.Add
                    { Position = position
                      Normal = normal
                      Color = color
                      Uv = uv
                      RootIndex = rootIndex |> Option.defaultValue 23 |> uint32
                      Joints = Vector4.Zero
                      Weights = Vector4.Zero
                      Surface = uint32 (StadiumSurface.ofMaterialName materialName) }

                ballRadius
                |> Option.iter (fun radius -> radius.Value <- max radius.Value (position.Length()))

            for struct (a, b, c) in primitive.GetTriangleIndices() do
                builder.Indices.Add(vertexOffset + uint32 a)
                builder.Indices.Add(vertexOffset + uint32 b)
                builder.Indices.Add(vertexOffset + uint32 c)

    let private flatten (root: Node) =
        let rec visit (node: Node) =
            seq {
                yield node

                for child in node.VisualChildren do
                    yield! visit child
            }

        visit root

    let private freezeStatic (builders: Dictionary<string, BatchBuilder>) : MeshBatchData array =
        builders.Values
        |> Seq.sortBy _.MaterialName
        |> Seq.map (fun builder ->
            ({ MaterialName = builder.MaterialName
               Vertices = builder.Vertices.ToArray()
               Indices = builder.Indices.ToArray() }
            : MeshBatchData))
        |> Seq.toArray

    let private freezeDynamic (builders: Dictionary<string, BatchBuilder>) : DynamicMeshBatchData array =
        builders.Values
        |> Seq.sortBy _.MaterialName
        |> Seq.map (fun builder ->
            ({ MaterialName = builder.MaterialName
               Vertices = builder.Vertices.ToArray()
               Indices = builder.Indices.ToArray() }
            : DynamicMeshBatchData))
        |> Seq.toArray

    let load (path: string) =
        let model = ModelRoot.Load path

        if isNull model.DefaultScene then
            invalidOp $"The stadium GLB has no default scene: {path}"

        let cameraNode =
            model.LogicalNodes
            |> Seq.tryFind (fun node ->
                not (isNull node.Camera)
                && node.Camera.Name.Contains("Broadcast", StringComparison.OrdinalIgnoreCase))
            |> Option.orElseWith (fun () -> model.LogicalNodes |> Seq.tryFind (fun node -> not (isNull node.Camera)))
            |> Option.defaultWith (fun () -> invalidOp "The stadium GLB has no camera.")

        let cameraSettings: CameraPerspective =
            match cameraNode.Camera.Settings with
            | :? CameraPerspective as perspective when perspective.IsPerspective -> perspective
            | _ -> invalidOp "The stadium camera must use perspective projection."

        let playerRoots =
            model.LogicalNodes
            |> Seq.choose (fun node ->
                if node.Name.StartsWith("Match 1 player ", StringComparison.Ordinal) then
                    match Int32.TryParse(node.Name.Substring("Match 1 player ".Length)) with
                    | true, slot when slot >= 1 && slot <= 11 -> Some(slot - 1, node)
                    | _ -> None
                elif node.Name.StartsWith("Match 2 player ", StringComparison.Ordinal) then
                    match Int32.TryParse(node.Name.Substring("Match 2 player ".Length)) with
                    | true, slot when slot >= 1 && slot <= 11 -> Some(10 + slot, node)
                    | _ -> None
                else
                    None)
            |> Seq.sortBy fst
            |> Seq.toArray

        let ballNode =
            model.LogicalNodes
            |> Seq.tryFind (fun node -> node.Name = "Match ball")
            |> Option.defaultWith (fun () -> invalidOp "The stadium scene has no Match ball mesh.")

        let dynamicRootNodes = Array.append (playerRoots |> Array.map snd) [| ballNode |]
        let dynamicNodes = HashSet<Node>()

        for root in dynamicRootNodes do
            for node in flatten root do
                dynamicNodes.Add node |> ignore

        let staticBuilders = Dictionary<string, BatchBuilder>(StringComparer.Ordinal)
        let dynamicBuilders = Dictionary<string, BatchBuilder>(StringComparer.Ordinal)
        let ballRadius = ref 0.0f

        for node in model.DefaultScene.VisualChildren |> Seq.collect flatten do
            if not (dynamicNodes.Contains node) && not (isNull node.Mesh) then
                for primitive in node.Mesh.Primitives do
                    appendPrimitive staticBuilders primitive node.WorldMatrix None None

        let mutable inverseBall = Matrix4x4.Identity

        if not (Matrix4x4.Invert(ballNode.WorldMatrix, &inverseBall)) then
            invalidOp "Could not invert the match ball transform."

        for node in flatten ballNode do
            if not (isNull node.Mesh) then
                let localToBall = node.WorldMatrix * inverseBall

                for primitive in node.Mesh.Primitives do
                    appendPrimitive dynamicBuilders primitive localToBall (Some 22) (Some ballRadius)

        if
            staticBuilders.Count = 0
            || dynamicBuilders.Count = 0
            || ballRadius.Value <= 0.0f
        then
            invalidOp "The stadium GLB is missing renderable pitch, player, or ball geometry."

        { StaticBatches = freezeStatic staticBuilders
          DynamicBatches = freezeDynamic dynamicBuilders
          Camera =
            { World = cameraNode.WorldMatrix
              VerticalFov = cameraSettings.VerticalFOV
              NearPlane = cameraSettings.ZNear
              FarPlane = cameraSettings.ZFar }
          BallRadius = ballRadius.Value }
