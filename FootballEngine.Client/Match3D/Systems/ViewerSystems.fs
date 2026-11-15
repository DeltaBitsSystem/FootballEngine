#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.Numerics
open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

type GpuMesh =
    { Name: string
      Vertices: GpuBuffer<GpuVertex>
      Indices: GpuBuffer<uint32>
      IndexCount: uint32 }

/// A game-specific producer of scene content: it owns its GPU meshes, refreshes per-frame data from the pure
/// scene and states what it draws. Stadium, crowd and weather each become one of these.
type IViewerSystem =
    inherit IDisposable
    abstract Update: SceneFrame -> unit
    abstract Draws: Draw<GpuMesh> list

/// Meshes whose motion comes entirely from the shared root/joint tables need no per-system update.
[<Sealed>]
type MeshSystem(meshes: GpuMesh list, instances: uint32, shadow: ShadowCasting, layer: DrawLayer) =
    let draws =
        meshes
        |> List.map (fun mesh ->
            { Mesh = mesh
              FirstIndex = 0u
              IndexCount = mesh.IndexCount
              FirstInstance = 0u
              InstanceCount = instances
              Shadow = shadow
              Layer = layer })

    let mutable disposed = false

    interface IViewerSystem with
        member _.Update _ = ()
        member _.Draws = draws

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                for mesh in meshes do
                    (mesh.Vertices :> IDisposable).Dispose()
                    (mesh.Indices :> IDisposable).Dispose()

module ViewerSystems =
    let uploadMesh (context: GpuContext) (name: string, vertices: GpuVertex[], indices: uint32[]) =
        if indices.Length = 0 then
            Ok None
        else
            match
                GpuBuffer<GpuVertex>
                    .Create(context, $"{name} vertices", BufferUsage.Vertex ||| BufferUsage.CopyDst, vertices)
            with
            | Error error -> Error error
            | Ok vertexBuffer ->
                match
                    GpuBuffer<uint32>
                        .Create(context, $"{name} indices", BufferUsage.Index ||| BufferUsage.CopyDst, indices)
                with
                | Error error ->
                    (vertexBuffer :> IDisposable).Dispose()
                    Error error
                | Ok indexBuffer ->
                    Ok(
                        Some
                            { Name = name
                              Vertices = vertexBuffer
                              Indices = indexBuffer
                              IndexCount = uint32 indices.Length }
                    )

    let private meshSystem context batches instances shadow layer : Result<IViewerSystem, GpuError> =
        let created = ResizeArray<GpuMesh>()

        let release () =
            for mesh in created do
                (mesh.Vertices :> IDisposable).Dispose()
                (mesh.Indices :> IDisposable).Dispose()

        let rec upload remaining =
            match remaining with
            | [] -> Ok(new MeshSystem(List.ofSeq created, instances, shadow, layer) :> IViewerSystem)
            | batch :: rest ->
                match uploadMesh context batch with
                | Ok(Some mesh) ->
                    created.Add mesh
                    upload rest
                | Ok None -> upload rest
                | Error error ->
                    release ()
                    Error error

        upload batches

    let ball context =
        let mesh = MatchBallMesh.create MatchBallMesh.radius
        meshSystem context [ mesh.MaterialName, mesh.Vertices, mesh.Indices ] 1u DynamicCaster SceneLayer

    /// One instanced draw per material covers both squads; the shader picks each player's root and kit by instance.
    let players context (character: CharacterAssetData) =
        let batches =
            character.Batches
            |> Array.map (fun batch -> batch.MaterialName, batch.Vertices, batch.Indices)
            |> List.ofArray

        meshSystem context batches (uint32 SceneRoot.playerCount) DynamicCaster SceneLayer

    let private carrierMarkerMesh () =
        let vertices = ResizeArray<GpuVertex>()
        let indices = ResizeArray<uint32>()
        let ringColor = Vector4(0.92f, 1.0f, 0.96f, 1.0f)
        let markerColor = Vector4(0.12f, 1.0f, 0.42f, 1.0f)
        let ringNormal = Vector3.UnitY
        let markerNormal = Vector3.Normalize(Vector3(-0.38f, 0.88f, 0.28f))

        let addVertex (position: Vector3) (normal: Vector3) (color: Vector4) =
            let index = uint32 vertices.Count

            vertices.Add
                { Position = position
                  Normal = normal
                  Color = color
                  Uv = Vector2.Zero
                  RootIndex = uint32 SceneRoot.carrierMarker
                  Joints = Vector4.Zero
                  Weights = Vector4.Zero
                  Surface = uint32 StadiumSurface.Plain }

            index

        let ringSegments = 40

        for segment in 0..ringSegments do
            let angle = 2.0f * MathF.PI * float32 segment / float32 ringSegments
            let x = MathF.Cos(angle)
            let z = MathF.Sin(angle)
            addVertex (Vector3(0.88f * x, 0.035f, 0.88f * z)) ringNormal ringColor |> ignore
            addVertex (Vector3(0.74f * x, 0.035f, 0.74f * z)) ringNormal ringColor |> ignore

        for segment in 0 .. ringSegments - 1 do
            let outer = uint32 (segment * 2)
            let inner = outer + 1u
            let nextOuter = outer + 2u
            let nextInner = outer + 3u
            indices.Add outer
            indices.Add inner
            indices.Add nextOuter
            indices.Add nextOuter
            indices.Add inner
            indices.Add nextInner

        let arrowLeft = addVertex (Vector3(-0.38f, 2.65f, 0.04f)) markerNormal markerColor
        let arrowRight = addVertex (Vector3(0.38f, 2.65f, 0.04f)) markerNormal markerColor
        let arrowTip = addVertex (Vector3(0.0f, 2.08f, 0.04f)) markerNormal markerColor
        indices.Add arrowLeft
        indices.Add arrowRight
        indices.Add arrowTip
        "ball-carrier focus marker", vertices.ToArray(), indices.ToArray()

    // Selection rings are presentation annotations, not physical occluders, so they cast no shadow.
    let carrierMarker context =
        meshSystem context [ carrierMarkerMesh () ] 1u NoShadow OverlayLayer

    let encode (api: WebGPU) (pass: nativeptr<RenderPassEncoder>) (draw: Draw<GpuMesh>) =
        api.RenderPassEncoderSetVertexBuffer(pass, 0u, draw.Mesh.Vertices.Handle, 0UL, UInt64.MaxValue)
        api.RenderPassEncoderSetIndexBuffer(pass, draw.Mesh.Indices.Handle, IndexFormat.Uint32, 0UL, UInt64.MaxValue)

        api.RenderPassEncoderDrawIndexed(
            pass,
            draw.IndexCount,
            draw.InstanceCount,
            draw.FirstIndex,
            0,
            draw.FirstInstance
        )
