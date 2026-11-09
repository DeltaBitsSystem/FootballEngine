namespace FootballEngine.Client.Match3D

open System
open System.Numerics
open System.Runtime.InteropServices

[<RequireQualifiedAccess>]
type StadiumSurface =
    | Plain = 0
    | TurfLight = 1
    | TurfDark = 2
    | TurfRunoff = 3
    | MatchBall = 7
    /// Kit materials sampled from the kit texture arrays: the layer is `surface - KitTextured`.
    | KitTextured = 16
    /// Untextured kit materials: roughness and metallic travel in Joints.xy because kit meshes are never skinned.
    | KitFlat = 32
    /// Broadcast LED panels; Joints.xy carries the panel size in metres so the pixel grid stays physical.
    | KitLed = 33

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

type DynamicMeshBatchData =
    { MaterialName: string
      Vertices: GpuVertex array
      Indices: uint32 array }

module MatchBallMesh =
    /// A size-5 ball: the simulation reports the centre height, so the mesh radius lifts it onto the turf.
    let radius = 0.11f

    let create (radius: float32) : DynamicMeshBatchData =
        let latitudeCount, longitudeCount = 24, 48
        let vertices =
            [| for latitude in 0 .. latitudeCount do
                   let v = float32 latitude / float32 latitudeCount
                   let theta = v * MathF.PI
                   for longitude in 0 .. longitudeCount do
                       let u = float32 longitude / float32 longitudeCount
                       let phi = u * MathF.PI * 2.0f
                       let normal = Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi))
                       yield
                           { Position = normal * radius
                             Normal = normal
                             Color = Vector4.One
                             Uv = Vector2(u, v)
                             RootIndex = 22u
                             Joints = Vector4.Zero
                             Weights = Vector4.Zero
                             Surface = uint32 StadiumSurface.MatchBall } |]
        let indices =
            [| for latitude in 0 .. latitudeCount - 1 do
                   for longitude in 0 .. longitudeCount - 1 do
                       let a = uint32 (latitude * (longitudeCount + 1) + longitude)
                       let b = a + uint32 (longitudeCount + 1)
                       if latitude > 0 then yield! [| a; b; a + 1u |]
                       if latitude < latitudeCount - 1 then yield! [| a + 1u; b; b + 1u |] |]
        { MaterialName = "FE match ball | procedural panel leather"
          Vertices = vertices
          Indices = indices }
