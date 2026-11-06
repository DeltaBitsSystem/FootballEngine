struct SkyVertexOutput {
    @builtin(position) clip: vec4<f32>,
    @location(0) ndc: vec2<f32>,
}

@vertex fn skyVs(@builtin(vertex_index) vertex: u32) -> SkyVertexOutput {
    let coordinate = vec2<f32>(f32((vertex << 1u) & 2u), f32(vertex & 2u)) * 2.0 - vec2<f32>(1.0);
    var output: SkyVertexOutput;
    output.clip = vec4<f32>(coordinate, 1.0, 1.0);
    output.ndc = coordinate;
    return output;
}

@fragment fn skyFs(input: SkyVertexOutput) -> @location(0) vec4<f32> {
    let far = camera.inverseViewProjection * vec4<f32>(input.ndc, 1.0, 1.0);
    let direction = normalize(far.xyz / far.w - camera.position.xyz);
    // Visible sky and material IBL share the same scene-linear environment, exposure and display transform.
    let radiance = textureSampleLevel(skyMap, environmentSampler, environmentUv(direction), 0.0).rgb;
    return vec4<f32>(radiance * camera.sunAndEnvironment.w, 1.0);
}
