struct GrassVertexOutput {
    @builtin(position) clip: vec4<f32>,
    @location(0) world: vec3<f32>,
    @location(1) height: f32,
    @location(2) coverage: f32,
    @location(3) bladeNormal: vec3<f32>,
    @location(4) tint: f32,
}

// 240 x 240 cells of 8 cm, five blades each: ~780 blades per square metre inside a 9 m radius around the play.
const GRASS_GRID: u32 = 240u;
const GRASS_CELL: f32 = 0.08;
const GRASS_RADIUS: f32 = 9.0;

@vertex fn grassVs(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> GrassVertexOutput {
    // World-anchored cells retain blade identities when the close camera or ball moves across the pitch.
    let centre = camera.rootTransforms[22][3].xz;
    let half = i32(GRASS_GRID / 2u);
    let cell = vec2<i32>(i32(instance % GRASS_GRID), i32(instance / GRASS_GRID)) + vec2<i32>(floor(centre / GRASS_CELL)) - vec2<i32>(half);
    let blade = vertex / 6u;
    let seed = cell + vec2<i32>(i32(blade) * 71, i32(blade) * 113);
    let jitter = vec2<f32>(materialHash(seed), materialHash(seed + vec2<i32>(19, 37)));
    let root = (vec2<f32>(cell) + jitter) * GRASS_CELL;
    let angle = materialHash(seed + vec2<i32>(7, 5)) * 6.28318530718;
    let across = vec2<f32>(cos(angle), sin(angle));
    let height = 0.024 + materialHash(seed + vec2<i32>(43, 11)) * 0.018;
    let width = 0.004 + materialHash(seed + vec2<i32>(3, 23)) * 0.003;
    var coordinates = array<vec2<f32>, 6>(vec2<f32>(-0.5, 0.0), vec2<f32>(0.5, 0.0), vec2<f32>(-0.06, 1.0), vec2<f32>(-0.06, 1.0), vec2<f32>(0.5, 0.0), vec2<f32>(0.06, 1.0));
    let coordinate = coordinates[vertex % 6u];
    // Blades lean with their mowing band (same rule as the turf shader), so close detail and stripes agree.
    let band = sin((root.x + PITCH_HALF_LENGTH) / PITCH_MOWING_BAND * 3.14159265359);
    let lean = select(-1.0, 1.0, band > 0.0);
    // Wind: a slow travelling gust plus a per-blade flutter. Short match turf barely moves; tips more than roots.
    let time = camera.position.w;
    let gust = sin(dot(root, vec2<f32>(0.21, 0.13)) - time * 1.3) * 0.5 + 0.5;
    let flutter = sin(time * (5.0 + jitter.x * 3.0) + jitter.y * 6.283) * 0.25;
    let t = coordinate.y;
    let bend = vec2<f32>((jitter.x - 0.5) * 0.010 + (gust * 0.9 + flutter) * 0.006, lean * 0.010) * t * t;
    let xz = root + across * coordinate.x * width + bend;
    var output: GrassVertexOutput;
    output.world = vec3<f32>(xz.x, 0.003 + height * t * (1.0 - 0.15 * t * t), xz.y);
    output.clip = camera.viewProjection * vec4<f32>(output.world, 1.0);
    output.height = t;
    output.coverage = 1.0 - smoothstep(GRASS_RADIUS * 0.7, GRASS_RADIUS, distance(root, centre));
    output.bladeNormal = normalize(vec3<f32>(-across.y, 0.35, across.x));
    output.tint = materialHash(seed + vec2<i32>(29, 31));
    if abs(root.x) > PITCH_HALF_LENGTH || abs(root.y) > PITCH_HALF_WIDTH || output.coverage <= 0.0 {
        output.clip = vec4<f32>(2.0, 2.0, 2.0, 1.0);
    }
    return output;
}

@fragment fn grassFs(input: GrassVertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    // Pigment, paint and stripes come from the base turf so the blade field never reads as a different island.
    let footprint = max(length(dpdx(input.world)), length(dpdy(input.world)));
    let uv = input.world.xz * vec2<f32>(1.0, -1.0) / PITCH_GRASS_TILE;
    let grass = grassSample(uv, dpdx(uv), dpdy(uv));
    var base = grass.colour * mix(0.88, 1.10, input.tint);
    // Older blades yellow at the tip; young ones stay saturated. Roots sit in the canopy's shade.
    base = mix(base, base * vec3<f32>(1.15, 1.08, 0.70), input.height * input.height * input.tint * 0.6);
    let normal = select(-input.bladeNormal, input.bladeNormal, front);
    let ao = mix(0.45, 1.0, input.height);
    let visibility = sunlightVisibility(input.world, vec3<f32>(0.0, 1.0, 0.0));
    let radiance = shadeCanopy(base, normalize(normal + vec3<f32>(0.0, 0.6, 0.0)), input.world, 0.75, ao, visibility, 1.0);
    // Fade by coverage and by sub-pixel size so the field dissolves into the turf texture instead of shimmering.
    let fade = input.coverage * (1.0 - smoothstep(0.012, 0.03, footprint));
    return vec4<f32>(radiance, fade);
}
