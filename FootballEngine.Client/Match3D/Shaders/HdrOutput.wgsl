struct OutputSettings {
    exposureStops: f32,
    padding0: f32,
    padding: vec2<f32>,
}
@group(0) @binding(0) var scene: texture_2d<f32>;
@group(0) @binding(1) var<uniform> settings: OutputSettings;

@vertex fn vs(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
    let x = f32((index << 1u) & 2u);
    let y = f32(index & 2u);
    return vec4<f32>(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
}

@fragment fn fs(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {
    // Load exactly one resolved pixel: output transfer must not blur the broadcast image.
    let radiance = max(textureLoad(scene, vec2<i32>(position.xy), 0).rgb, vec3<f32>(0.0));
    let x = radiance * exp2(settings.exposureStops);
    // ACES-style SDR fit; the floating scene remains unexposed and unclipped until this pass.
    let mapped = clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), vec3<f32>(0.0), vec3<f32>(1.0));
    return vec4<f32>(mapped, 1.0);
}
