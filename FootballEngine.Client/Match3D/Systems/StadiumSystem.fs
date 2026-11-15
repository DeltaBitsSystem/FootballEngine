namespace FootballEngine.Client.Match3D

open System
open System.Numerics

/// The procedural home ground: the kit mesh is uploaded once, the generated stands and instance table are replaced
/// whenever the identity changes, and every frame each cluster picks its level of detail from the camera.
[<Sealed>]
type StadiumSystem
    private (context: GpuContext, kit: StadiumKitData, kitMesh: GpuMesh, instances: GpuBuffer<Vector4>, seatPiece: int)
    =
    let mutable generated: GpuMesh option = None
    let mutable build: StadiumBuild option = None
    let mutable staticDraws: Draw<GpuMesh> list = []
    let mutable draws: Draw<GpuMesh> list = []
    let mutable disposed = false

    let release (mesh: GpuMesh) =
        (mesh.Vertices :> IDisposable).Dispose()
        (mesh.Indices :> IDisposable).Dispose()

    let kitDraw (range: KitRange) (batch: KitBatch) shadow layer =
        { Mesh = kitMesh
          FirstIndex = range.First
          IndexCount = range.Count
          FirstInstance = batch.FirstInstance
          InstanceCount = batch.InstanceCount
          Shadow = shadow
          Layer = layer }

    static member Create(context: GpuContext, kit: StadiumKitData, instances: GpuBuffer<Vector4>) =
        match StadiumKit.pieceIndex kit "seat" with
        | None -> Error(CreationFailed "the stadium kit has no seat piece")
        | Some seatPiece ->
            ViewerSystems.uploadMesh context ("stadium kit", kit.Vertices, kit.Indices)
            |> Result.bind (fun mesh ->
                match mesh with
                | Some mesh -> Ok(new StadiumSystem(context, kit, mesh, instances, seatPiece))
                | None -> Error(CreationFailed "the stadium kit has no triangles"))

    member _.Build = build

    /// Replaces the ground. The caller must re-render the static shadow cache, which still holds the old stands.
    member _.Rebuild(next: StadiumBuild) : Result<unit, GpuError> =
        ViewerSystems.uploadMesh context ("generated stadium", next.Vertices, next.Indices)
        |> Result.map (fun mesh ->
            generated |> Option.iter release
            generated <- mesh
            build <- Some next
            instances.Write(next.Instances)

            let ground =
                mesh
                |> Option.map (fun mesh ->
                    { Mesh = mesh
                      FirstIndex = 0u
                      IndexCount = mesh.IndexCount
                      FirstInstance = 0u
                      InstanceCount = 1u
                      Shadow = StaticCaster
                      Layer = SceneLayer })
                |> Option.toList

            let shadows =
                StadiumGeometry.shadowBatches kit next seatPiece
                |> Array.choose (fun batch ->
                    let range = kit.Pieces[batch.Piece].Lods[batch.Lod].Opaque

                    if range.Count = 0u then
                        None
                    else
                        Some(kitDraw range batch StaticCaster ShadowOnlyLayer))
                |> List.ofArray

            staticDraws <- ground @ shadows
            draws <- staticDraws)

    interface IViewerSystem with
        member _.Update(scene: SceneFrame) =
            match build with
            | None -> ()
            | Some current ->
                let batches =
                    StadiumGeometry.batches kit current scene.Camera.Position scene.Camera.ViewProjection

                let opaque = ResizeArray<Draw<GpuMesh>>()
                let blended = ResizeArray<Draw<GpuMesh>>()

                for batch in batches do
                    let lod = kit.Pieces[batch.Piece].Lods[batch.Lod]

                    if lod.Opaque.Count > 0u then
                        opaque.Add(kitDraw lod.Opaque batch NoShadow SceneLayer)

                    // Blended batches are not depth-sorted: kit glazing rarely overlaps other glazing on screen,
                    // and the premultiplied blend keeps any residual ordering error to a faint tint difference.
                    if lod.Blended.Count > 0u then
                        blended.Add(kitDraw lod.Blended batch NoShadow BlendedLayer)

                draws <- staticDraws @ List.ofSeq opaque @ List.ofSeq blended

        member _.Draws = draws

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                generated |> Option.iter release
                release kitMesh
