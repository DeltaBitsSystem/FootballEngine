namespace FootballEngine.Client.Match3D

open System.Numerics

/// Root slots index the transform table that the shader reads through each vertex's root index. The numbers are a
/// contract with StadiumAsset (static stadium geometry uses slot 23) and with the carrier marker batch.
module SceneRoot =
    let playerCount = 22
    let teamSize = 11
    let awayFirst = 11
    let ball = 22
    let stadium = 23
    let carrierMarker = 24
    let count = 25
    /// Kit vertices carry this instead of a slot: their transform comes from the instance storage buffer.
    let instanced = System.UInt32.MaxValue

type CameraState =
    { ViewProjection: Matrix4x4
      Inverse: Matrix4x4
      Position: Vector3
      NearPlane: float32
      FarPlane: float32
      FieldOfView: float32
      Aspect: float32 }

type SunState =
    { Direction: Vector3
      Radiance: Vector3
      EnvironmentIntensity: float32 }

/// Everything the GPU needs for one frame's scene. Built once per frame; the renderer only copies it to buffers.
type SceneFrame =
    {
        Camera: CameraState
        Sun: SunState
        LightViewProjection: Matrix4x4
        /// Wrapped to one hour so the float32 grass-wind phase keeps its precision over a long session.
        TimeSeconds: float
        RootTransforms: Matrix4x4[]
        /// Borrowed from the caller's palette buffer, which the evaluator rewrites every frame. The renderer uploads it
        /// within the same call, so a copy here would only add a per-frame allocation of every joint matrix.
        JointPalettes: Matrix4x4[]
        ShowGrassDetail: bool
        /// Linear home-club colours for LED content; static stadium tints are baked into instance data.
        ClubPrimary: Vector3
        ClubSecondary: Vector3
    }
