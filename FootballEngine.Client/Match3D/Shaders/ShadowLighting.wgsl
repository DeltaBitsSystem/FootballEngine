@group(1) @binding(0) var sunShadow: texture_depth_2d;
@group(1) @binding(1) var sunShadowSampler: sampler_comparison;

// Poisson disk: evenly spread taps give a round penumbra; the 3x3 box it replaces drew square, stepped edges.
const POISSON = array<vec2<f32>, 16>(
    vec2<f32>(-0.94201624, -0.39906216), vec2<f32>(0.94558609, -0.76890725),
    vec2<f32>(-0.09418410, -0.92938870), vec2<f32>(0.34495938, 0.29387760),
    vec2<f32>(-0.91588581, 0.45771432), vec2<f32>(-0.81544232, -0.87912464),
    vec2<f32>(-0.38277543, 0.27676845), vec2<f32>(0.97484398, 0.75648379),
    vec2<f32>(0.44323325, -0.97511554), vec2<f32>(0.53742981, -0.47373420),
    vec2<f32>(-0.26496911, -0.41893023), vec2<f32>(0.79197514, 0.19090188),
    vec2<f32>(-0.24188840, 0.99706507), vec2<f32>(-0.81409955, 0.91437590),
    vec2<f32>(0.19984126, 0.78641367), vec2<f32>(0.14383161, -0.14100790));

// Penumbra radius in shadow texels: wide enough to soften the stadium's 4096 map, small enough to keep contact.
const PCF_RADIUS_TEXELS = 1.8;

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
    // Per-position rotation turns the fixed tap pattern into fine noise that MSAA and the eye average out.
    let angle = fract(sin(dot(world.xz, vec2<f32>(12.9898, 78.233))) * 43758.5453) * 6.2831853;
    let rotation = mat2x2<f32>(cos(angle), sin(angle), -sin(angle), cos(angle));
    // A function-local copy: WGSL implementations reject runtime indexing into a module-scope const array.
    var taps = POISSON;
    var visibility = 0.0;
    for (var i = 0; i < 16; i += 1) {
        let offset = rotation * taps[i] * PCF_RADIUS_TEXELS * texel;
        visibility += textureSampleCompareLevel(sunShadow, sunShadowSampler, uv + offset, ndc.z - 0.000035);
    }
    return visibility / 16.0;
}
