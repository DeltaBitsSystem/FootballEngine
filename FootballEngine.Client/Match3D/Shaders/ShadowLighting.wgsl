@group(1) @binding(0) var sunShadow: texture_depth_2d;
@group(1) @binding(1) var sunShadowSampler: sampler_comparison;

fn sunlightVisibility(world: vec3<f32>, normal: vec3<f32>) -> f32 {
    // Offset along the receiver normal avoids acne without detaching feet from their shadows.
    let receiver = world + normal * 0.018;
    let clip = camera.lightViewProjection * vec4<f32>(receiver, 1.0);
    let ndc = clip.xyz / clip.w;
    let uv = ndc.xy * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5);
    if any(uv < vec2<f32>(0.0)) || any(uv > vec2<f32>(1.0)) || ndc.z < 0.0 || ndc.z > 1.0 {
        return 1.0;
    }
    let texel = 1.0 / vec2<f32>(textureDimensions(sunShadow));
    var visibility = 0.0;
    for (var y = -1; y <= 1; y += 1) {
        for (var x = -1; x <= 1; x += 1) {
            visibility += textureSampleCompareLevel(sunShadow, sunShadowSampler, uv + vec2<f32>(f32(x), f32(y)) * texel, ndc.z - 0.000035);
        }
    }
    return visibility / 9.0;
}
