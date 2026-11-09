namespace FootballEngine.Client.Match3D

open System.Numerics

/// Offsets are float32 element indices into the camera uniform. They mirror `struct Camera` in
/// Shaders/CameraUniform.wgsl; a test parses that struct, so moving a field on either side breaks the build.
module UniformLayout =
    let matrixElements = 16
    let vectorElements = 4
    let viewProjection = 0
    let rootTransforms = viewProjection + matrixElements
    let position = rootTransforms + SceneRoot.count * matrixElements
    let sunAndEnvironment = position + vectorElements
    let sunRadiance = sunAndEnvironment + vectorElements
    let lightViewProjection = sunRadiance + vectorElements
    let inverseViewProjection = lightViewProjection + matrixElements
    let clubPrimary = inverseViewProjection + matrixElements
    let clubSecondary = clubPrimary + vectorElements
    let elementCount = clubSecondary + vectorElements

    let private writeMatrix (target: float32[]) (offset: int) (matrix: Matrix4x4) =
        target[offset] <- matrix.M11
        target[offset + 1] <- matrix.M12
        target[offset + 2] <- matrix.M13
        target[offset + 3] <- matrix.M14
        target[offset + 4] <- matrix.M21
        target[offset + 5] <- matrix.M22
        target[offset + 6] <- matrix.M23
        target[offset + 7] <- matrix.M24
        target[offset + 8] <- matrix.M31
        target[offset + 9] <- matrix.M32
        target[offset + 10] <- matrix.M33
        target[offset + 11] <- matrix.M34
        target[offset + 12] <- matrix.M41
        target[offset + 13] <- matrix.M42
        target[offset + 14] <- matrix.M43
        target[offset + 15] <- matrix.M44

    let private writeVector (target: float32[]) (offset: int) (x: float32) (y: float32) (z: float32) (w: float32) =
        target[offset] <- x
        target[offset + 1] <- y
        target[offset + 2] <- z
        target[offset + 3] <- w

    /// The w of `position` carries the wind clock; no shader reads it as a homogeneous coordinate.
    let packCamera (scene: SceneFrame) : float32[] =
        let data: float32[] = Array.zeroCreate elementCount
        writeMatrix data viewProjection scene.Camera.ViewProjection

        for root = 0 to SceneRoot.count - 1 do
            writeMatrix data (rootTransforms + root * matrixElements) scene.RootTransforms[root]

        let eye = scene.Camera.Position
        writeVector data position eye.X eye.Y eye.Z (float32 scene.TimeSeconds)

        let sun = scene.Sun

        writeVector data sunAndEnvironment sun.Direction.X sun.Direction.Y sun.Direction.Z sun.EnvironmentIntensity

        // The shader never reads the w of sunRadiance; it is kept zero so the uniform bytes stay deterministic.
        writeVector data sunRadiance sun.Radiance.X sun.Radiance.Y sun.Radiance.Z 0.0f
        writeMatrix data lightViewProjection scene.LightViewProjection
        writeMatrix data inverseViewProjection scene.Camera.Inverse
        let primary, secondary = scene.ClubPrimary, scene.ClubSecondary
        writeVector data clubPrimary primary.X primary.Y primary.Z 1.0f
        writeVector data clubSecondary secondary.X secondary.Y secondary.Z 1.0f
        data
