struct OutputSettings {
    // x: exposure stops, y: bloom intensity, z: AO intensity, w: AO radius (metres)
    params: vec4<f32>,
    // x: near, y: far, z: tan(fovY/2), w: aspect
    projection: vec4<f32>,
    // x: saturation, y: contrast, z: AO enabled, w: bloom enabled
    grade: vec4<f32>,
}
@group(0) @binding(0) var scene: texture_2d<f32>;
@group(0) @binding(1) var<uniform> settings: OutputSettings;
@group(0) @binding(2) var sceneDepth: texture_depth_multisampled_2d;
@group(0) @binding(3) var bloomImage: texture_2d<f32>;
@group(0) @binding(4) var linearClamp: sampler;

@vertex fn vs(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
    let x = f32((index << 1u) & 2u);
    let y = f32(index & 2u);
    return vec4<f32>(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
}

fn linearDepth(pixel: vec2<i32>) -> f32 {
    let size = vec2<i32>(textureDimensions(sceneDepth));
    let d = textureLoad(sceneDepth, clamp(pixel, vec2<i32>(0), size - vec2<i32>(1)), 0);
    let n = settings.projection.x;
    let f = settings.projection.y;
    // WebGPU [0,1] depth from a right-handed perspective.
    return n * f / (f - d * (f - n));
}

fn viewPosition(pixel: vec2<i32>, depth: f32) -> vec3<f32> {
    let size = vec2<f32>(textureDimensions(sceneDepth));
    let ndc = (vec2<f32>(pixel) + vec2<f32>(0.5)) / size * 2.0 - vec2<f32>(1.0);
    let t = settings.projection.z;
    return vec3<f32>(ndc.x * t * settings.projection.w * depth, -ndc.y * t * depth, -depth);
}

// Screen-space ambient occlusion from the resolved depth: a 12-tap disk projected from a metric radius, with a
// range check so silhouettes do not cast halos onto the distant background.
fn ambientOcclusion(pixel: vec2<i32>) -> f32 {
    let raw = textureLoad(sceneDepth, pixel, 0);
    if raw >= 0.99999 { return 1.0; }
    let depth = linearDepth(pixel);
    let p = viewPosition(pixel, depth);
    let px = viewPosition(pixel + vec2<i32>(1, 0), linearDepth(pixel + vec2<i32>(1, 0)));
    let py = viewPosition(pixel + vec2<i32>(0, 1), linearDepth(pixel + vec2<i32>(0, 1)));
    let normal = normalize(cross(px - p, py - p));
    let size = vec2<f32>(textureDimensions(sceneDepth));
    let radius = settings.params.w;
    let pixelsPerMetre = size.y / (2.0 * settings.projection.z * depth);
    let screenRadius = clamp(radius * pixelsPerMetre, 2.0, 64.0);
    // Interleaved rotation per pixel hides the fixed tap pattern without a blur pass.
    let rotation = fract(52.9829189 * fract(dot(vec2<f32>(pixel), vec2<f32>(0.06711056, 0.00583715)))) * 6.2831853;
    var occlusion = 0.0;
    for (var i = 0; i < 12; i += 1) {
        let fi = f32(i);
        let angle = fi * 2.3999632 + rotation;
        let r = screenRadius * sqrt((fi + 0.5) / 12.0);
        let offset = vec2<i32>(vec2<f32>(cos(angle), sin(angle)) * r);
        let q = pixel + offset;
        let s = viewPosition(q, linearDepth(q));
        let v = s - p;
        let distance = length(v);
        let range = 1.0 - smoothstep(radius * 0.5, radius * 1.5, distance);
        occlusion += max(dot(normal, v / max(distance, 1e-4)) - 0.08, 0.0) * range;
    }
    return clamp(1.0 - occlusion / 12.0 * 1.6, 0.0, 1.0);
}

@fragment fn fs(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {
    let pixel = vec2<i32>(position.xy);
    // Load exactly one resolved pixel: output transfer must not blur the broadcast image.
    var radiance = max(textureLoad(scene, pixel, 0).rgb, vec3<f32>(0.0));
    if settings.grade.z > 0.5 {
        let ao = ambientOcclusion(pixel);
        radiance *= mix(1.0, ao, settings.params.z);
    }
    var x = radiance * exp2(settings.params.x);
    if settings.grade.w > 0.5 {
        let size = vec2<f32>(textureDimensions(scene));
        let uv = (vec2<f32>(pixel) + vec2<f32>(0.5)) / size;
        x += textureSampleLevel(bloomImage, linearClamp, uv, 0.0).rgb * settings.params.y;
    }
    // Broadcast grade in scene-linear light, before the display curve: saturation around luminance, then a
    // gentle contrast pivot at mid-grey. Neutral values (1, 1) leave the image untouched.
    let luminance = dot(x, vec3<f32>(0.2126, 0.7152, 0.0722));
    x = max(mix(vec3<f32>(luminance), x, settings.grade.x), vec3<f32>(0.0));
    x = 0.18 * pow(x / 0.18, vec3<f32>(settings.grade.y));
    // ACES-style SDR fit; the floating scene remains unexposed and unclipped until this pass.
    let mapped = clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), vec3<f32>(0.0), vec3<f32>(1.0));
    return vec4<f32>(mapped, 1.0);
}
