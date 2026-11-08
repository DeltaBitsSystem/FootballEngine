struct Camera {
    viewProjection: mat4x4<f32>,
    rootTransforms: array<mat4x4<f32>, 25>,
    position: vec4<f32>,
    sunAndEnvironment: vec4<f32>,
    sunRadiance: vec4<f32>,
    lightViewProjection: mat4x4<f32>,
    inverseViewProjection: mat4x4<f32>,
}
