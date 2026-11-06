struct GrassVertexOutput {
    @builtin(position) clip: vec4<f32>,
    @location(0) world: vec3<f32>,
    @location(1) height: f32,
    @location(2) coverage: f32,
}

@vertex fn grassVs(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> GrassVertexOutput {
    // World-anchored cells retain blade identities when the close camera or ball moves across the pitch.
    let centre = camera.rootTransforms[22][3].xz;
    let cell = vec2<i32>(i32(instance % 160u), i32(instance / 160u)) + vec2<i32>(floor(centre * 10.0)) - vec2<i32>(80);
    let blade = vertex / 6u;
    let seed = cell + vec2<i32>(i32(blade) * 71, i32(blade) * 113);
    let jitter = vec2<f32>(materialHash(seed), materialHash(seed + vec2<i32>(19, 37)));
    let root = (vec2<f32>(cell) + jitter) * 0.1;
    let angle = materialHash(seed + vec2<i32>(7, 5)) * 6.28318530718;
    let across = vec2<f32>(cos(angle), sin(angle));
    let height = 0.026 + materialHash(seed + vec2<i32>(43, 11)) * 0.016;
    let width = 0.006 + materialHash(seed + vec2<i32>(3, 23)) * 0.004;
    var coordinates = array<vec2<f32>, 6>(vec2<f32>(-0.5, 0.0), vec2<f32>(0.5, 0.0), vec2<f32>(-0.08, 1.0), vec2<f32>(-0.08, 1.0), vec2<f32>(0.5, 0.0), vec2<f32>(0.08, 1.0));
    let coordinate = coordinates[vertex % 6u];
    let mowing = sin((root.x + PITCH_HALF_LENGTH) / PITCH_MOWING_BAND * 3.14159265359);
    let bend = vec2<f32>((jitter.x - 0.5) * 0.008, sign(mowing) * 0.012) * coordinate.y;
    let xz = root + across * coordinate.x * width + bend;
    var output: GrassVertexOutput;
    output.world = vec3<f32>(xz.x, 0.004 + height * coordinate.y, xz.y);
    output.clip = camera.viewProjection * vec4<f32>(output.world, 1.0);
    output.height = coordinate.y;
    output.coverage = 1.0 - smoothstep(5.0, 7.5, distance(root, centre));
    if abs(root.x) > PITCH_HALF_LENGTH || abs(root.y) > PITCH_HALF_WIDTH || output.coverage <= 0.0 {
        output.clip = vec4<f32>(2.0, 2.0, 2.0, 1.0);
    }
    return output;
}

@fragment fn grassFs(input: GrassVertexOutput) -> @location(0) vec4<f32> {
    // The same pigment/paint and lighting as the base turf avoid a different-coloured detail island.
    let radiance = shadePitch(input.world, 1u, dpdx(input.world), dpdy(input.world));
    return vec4<f32>(radiance * mix(0.86, 1.0, input.height), input.coverage);
}
