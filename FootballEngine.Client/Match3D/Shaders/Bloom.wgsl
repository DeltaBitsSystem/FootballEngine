struct BloomSettings {
    // x: threshold (exposed radiance), y: soft knee, z: exposure scale, w: upsample weight
    params: vec4<f32>,
    // xy: texel size of the source mip
    texel: vec4<f32>,
}
@group(0) @binding(0) var source: texture_2d<f32>;
@group(0) @binding(1) var linearClamp: sampler;
@group(0) @binding(2) var<uniform> bloom: BloomSettings;

struct FullscreenOut {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@vertex fn vs(@builtin(vertex_index) index: u32) -> FullscreenOut {
    let x = f32((index << 1u) & 2u);
    let y = f32(index & 2u);
    var out: FullscreenOut;
    out.position = vec4<f32>(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
    out.uv = vec2<f32>(x, 1.0 - y);
    return out;
}

// 13-tap box downsample (Jimenez 2014): four overlapping 2x2 boxes plus a centre box keep the result stable when
// bright pixels move by a texel, which a plain 2x2 average turns into flicker.
fn downsample13(uv: vec2<f32>) -> vec3<f32> {
    let t = bloom.texel.xy;
    let a = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(-2.0, -2.0), 0.0).rgb;
    let b = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(0.0, -2.0), 0.0).rgb;
    let c = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(2.0, -2.0), 0.0).rgb;
    let d = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(-2.0, 0.0), 0.0).rgb;
    let e = textureSampleLevel(source, linearClamp, uv, 0.0).rgb;
    let f = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(2.0, 0.0), 0.0).rgb;
    let g = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(-2.0, 2.0), 0.0).rgb;
    let h = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(0.0, 2.0), 0.0).rgb;
    let i = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(2.0, 2.0), 0.0).rgb;
    let j = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(-1.0, -1.0), 0.0).rgb;
    let k = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(1.0, -1.0), 0.0).rgb;
    let l = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(-1.0, 1.0), 0.0).rgb;
    let m = textureSampleLevel(source, linearClamp, uv + t * vec2<f32>(1.0, 1.0), 0.0).rgb;
    return e * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + (j + k + l + m) * 0.125;
}

@fragment fn fsPrefilter(in: FullscreenOut) -> @location(0) vec4<f32> {
    let colour = downsample13(in.uv) * bloom.params.z;
    // Quadratic soft knee: only highlights above the threshold glow, without a hard edge at the threshold.
    let brightness = max(colour.r, max(colour.g, colour.b));
    let knee = max(bloom.params.y, 1e-4);
    let soft = clamp(brightness - bloom.params.x + knee, 0.0, 2.0 * knee);
    let contribution = max(soft * soft / (4.0 * knee), brightness - bloom.params.x) / max(brightness, 1e-4);
    // Clamp fireflies (specular sparkles on wet or metallic surfaces) before they become blobs.
    return vec4<f32>(min(colour * max(contribution, 0.0), vec3<f32>(64.0)), 1.0);
}

@fragment fn fsDown(in: FullscreenOut) -> @location(0) vec4<f32> {
    return vec4<f32>(downsample13(in.uv), 1.0);
}

// 3x3 tent upsample, additively blended into the next larger mip with weight `params.w`.
@fragment fn fsUp(in: FullscreenOut) -> @location(0) vec4<f32> {
    let t = bloom.texel.xy;
    var sum = textureSampleLevel(source, linearClamp, in.uv, 0.0).rgb * 4.0;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(-t.x, 0.0), 0.0).rgb * 2.0;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(t.x, 0.0), 0.0).rgb * 2.0;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(0.0, -t.y), 0.0).rgb * 2.0;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(0.0, t.y), 0.0).rgb * 2.0;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(-t.x, -t.y), 0.0).rgb;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(t.x, -t.y), 0.0).rgb;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(-t.x, t.y), 0.0).rgb;
    sum += textureSampleLevel(source, linearClamp, in.uv + vec2<f32>(t.x, t.y), 0.0).rgb;
    return vec4<f32>(sum / 16.0 * bloom.params.w, 1.0);
}
