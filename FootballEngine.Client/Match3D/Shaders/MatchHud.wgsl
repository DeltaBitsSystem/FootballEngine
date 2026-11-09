struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) uv: vec2<f32>,
}

struct VertexOutput {
    @builtin(position) clipPosition: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@group(0) @binding(0) var hudImage: texture_2d<f32>;
@group(0) @binding(1) var hudSampler: sampler;

@vertex fn vs(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.clipPosition = vec4<f32>(input.position, 1.0);
    output.uv = input.uv;
    return output;
}

@fragment fn fs(input: VertexOutput) -> @location(0) vec4<f32> {
    let pixel = textureSample(hudImage, hudSampler, input.uv);
    // Skia supplies premultiplied sRGB bytes; decode straight RGB before blending in linear light.
    let straight = pixel.rgb / max(pixel.a, 0.00001);
    let linear = select(pow((straight + vec3<f32>(0.055)) / 1.055, vec3<f32>(2.4)), straight / 12.92, straight <= vec3<f32>(0.04045));
    return vec4<f32>(linear * pixel.a, pixel.a);
}
