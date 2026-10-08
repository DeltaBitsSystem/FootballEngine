struct GrassSample {
    colour: vec3<f32>,
    normal: vec3<f32>,
    properties: vec3<f32>,
}

fn grassSample(uv: vec2<f32>, dx: vec2<f32>, dy: vec2<f32>) -> GrassSample {
    // Continuous triangular blending hides repeated tufts without UV seams or changing mip footprints.
    let skew = vec2<f32>(uv.x - uv.y * 0.577350269, uv.y * 1.154700538) * 0.22;
    let cell = vec2<i32>(floor(skew));
    let f = fract(skew);
    var vertices = array<vec2<i32>, 3>(cell, cell + vec2<i32>(1, 0), cell + vec2<i32>(0, 1));
    var weights = vec3<f32>(1.0 - f.x - f.y, f.x, f.y);
    if f.x + f.y > 1.0 {
        vertices = array<vec2<i32>, 3>(cell + vec2<i32>(1, 1), cell + vec2<i32>(0, 1), cell + vec2<i32>(1, 0));
        weights = vec3<f32>(f.x + f.y - 1.0, 1.0 - f.x, 1.0 - f.y);
    }
    var result: GrassSample;
    result.colour = vec3<f32>(0.0);
    result.normal = vec3<f32>(0.0);
    result.properties = vec3<f32>(0.0);
    for (var i = 0u; i < 3u; i += 1u) {
        let offset = vec2<f32>(materialHash(vertices[i]), materialHash(vertices[i] + vec2<i32>(17, 53))) * 31.0;
        let coordinate = uv + offset;
        result.colour += textureSampleGrad(surfaceAlbedo, surfaceSampler, coordinate, 0, dx, dy).rgb * weights[i];
        result.normal += (textureSampleGrad(surfaceNormalMap, surfaceSampler, coordinate, 0, dx, dy).rgb * 2.0 - 1.0) * weights[i];
        result.properties += textureSampleGrad(surfaceArm, surfaceSampler, coordinate, 0, dx, dy).rgb * weights[i];
    }
    result.normal = normalize(result.normal);
    return result;
}

fn pitchSegmentDistance(point: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {
    let axis = b - a;
    let t = clamp(dot(point - a, axis) / dot(axis, axis), 0.0, 1.0);
    return distance(point, a + axis * t);
}

fn pitchBoxDistance(point: vec2<f32>, halfSize: vec2<f32>) -> f32 {
    let q = abs(point) - halfSize;
    return length(max(q, vec2<f32>(0.0))) + min(max(q.x, q.y), 0.0);
}

fn pitchPaintDistance(pointInput: vec2<f32>) -> f32 {
    let point = abs(pointInput);
    let boundary = vec2<f32>(PITCH_HALF_LENGTH, PITCH_HALF_WIDTH) - PITCH_HALF_STROKE;
    var distanceToPaint = abs(pitchBoxDistance(point, boundary)) - PITCH_HALF_STROKE;
    distanceToPaint = min(distanceToPaint, pitchSegmentDistance(point, vec2<f32>(0.0, -boundary.y), vec2<f32>(0.0, boundary.y)) - PITCH_HALF_STROKE);
    distanceToPaint = min(distanceToPaint, abs(length(point) - PITCH_CENTER_RADIUS) - PITCH_HALF_STROKE);
    distanceToPaint = min(distanceToPaint, length(point) - PITCH_SPOT_RADIUS);
    for (var side = 0u; side < 2u; side += 1u) {
        let p = vec2<f32>(point.x * select(-1.0, 1.0, side == 1u), point.y);
        for (var area = 0u; area < 2u; area += 1u) {
            let depth = select(PITCH_PENALTY_DEPTH, PITCH_GOAL_DEPTH, area == 1u);
            let span = select(PITCH_PENALTY_HALF_WIDTH, PITCH_GOAL_HALF_WIDTH, area == 1u);
            let front = PITCH_HALF_LENGTH - depth;
            distanceToPaint = min(distanceToPaint, pitchSegmentDistance(p, vec2<f32>(front, -span), vec2<f32>(front, span)) - PITCH_HALF_STROKE);
            distanceToPaint = min(distanceToPaint, pitchSegmentDistance(p, vec2<f32>(front, span), vec2<f32>(boundary.x, span)) - PITCH_HALF_STROKE);
            distanceToPaint = min(distanceToPaint, pitchSegmentDistance(p, vec2<f32>(front, -span), vec2<f32>(boundary.x, -span)) - PITCH_HALF_STROKE);
        }
        let spot = vec2<f32>(PITCH_HALF_LENGTH - PITCH_PENALTY_SPOT_DISTANCE, 0.0);
        distanceToPaint = min(distanceToPaint, distance(p, spot) - PITCH_SPOT_RADIUS);
        let arc = max(abs(distance(p, spot) - PITCH_CENTER_RADIUS) - PITCH_HALF_STROKE, p.x - (PITCH_HALF_LENGTH - PITCH_PENALTY_DEPTH));
        distanceToPaint = min(distanceToPaint, arc);
    }
    let nearestCorner = select(vec2<f32>(-PITCH_HALF_LENGTH, -PITCH_HALF_WIDTH), vec2<f32>(PITCH_HALF_LENGTH, PITCH_HALF_WIDTH), point >= vec2<f32>(0.0));
    distanceToPaint = min(distanceToPaint, abs(distance(point, nearestCorner) - PITCH_CORNER_RADIUS) - PITCH_HALF_STROKE);
    return max(distanceToPaint, pitchBoxDistance(point, vec2<f32>(PITCH_HALF_LENGTH, PITCH_HALF_WIDTH)));
}

fn shadePitch(world: vec3<f32>, surface: u32, dx: vec3<f32>, dy: vec3<f32>) -> vec3<f32> {
    let uv = world.xz * vec2<f32>(1.0, -1.0) / PITCH_GRASS_TILE;
    let grass = grassSample(uv, dx.xz * vec2<f32>(1.0, -1.0) / PITCH_GRASS_TILE, dy.xz * vec2<f32>(1.0, -1.0) / PITCH_GRASS_TILE);
    let footprint = max(length(dx.xz), length(dy.xz));
    let bladeDetail = 1.0 - smoothstep(0.025, 0.14, footprint);
    var normal = normalize(vec3<f32>(grass.normal.x * 0.50, grass.normal.z, -grass.normal.y * 0.50));
    var base = grass.colour;
    let clumps = materialNoise(world.xz * 0.65);
    let broad = materialNoise(world.xz * 0.11);
    base *= 0.91 + 0.13 * clumps + 0.07 * broad;

    let bandPosition = (world.x + PITCH_HALF_LENGTH) / PITCH_MOWING_BAND;
    let bend = smoothstep(-0.045, 0.045, sin(bandPosition * 3.14159265359)) * 2.0 - 1.0;
    let view = normalize(camera.position.xyz - world);
    // Bent blades change reflectance with viewing direction, not just with an alternating green vertex colour.
    let mowing = select(bend, 0.0, surface == 3u);
    base *= 1.0 + mowing * (0.055 + 0.07 * view.z);
    normal = normalize(normal + vec3<f32>(0.0, 0.0, mowing * 0.032));

    let goalDistance = vec2<f32>((abs(world.x) - (PITCH_HALF_LENGTH - 1.5)) / 4.2, world.z / 5.0);
    let goalWear = exp(-dot(goalDistance, goalDistance) * 1.8) * 0.12;
    base = mix(base, base * vec3<f32>(1.17, 0.93, 1.05), goalWear);

    let paintDistance = pitchPaintDistance(world.xz);
    let filterWidth = max(footprint * 0.65, 0.0005);
    let edgeVariation = (materialNoise(world.xz * 95.0) - 0.5) * 0.003 * bladeDetail;
    // Integrating the narrow stroke preserves subpixel paint instead of fattening far-away lines.
    let inner = smoothstep(-filterWidth, filterWidth, paintDistance + PITCH_HALF_STROKE + PITCH_HALF_STROKE);
    let outer = smoothstep(-filterWidth, filterWidth, paintDistance + edgeVariation);
    let nearestSpot = vec2<f32>(PITCH_HALF_LENGTH - PITCH_PENALTY_SPOT_DISTANCE, 0.0);
    let spotDistance = min(length(world.xz), distance(vec2<f32>(abs(world.x), world.z), nearestSpot)) - PITCH_SPOT_RADIUS;
    let spotCoverage = 1.0 - smoothstep(-filterWidth, filterWidth, spotDistance);
    let paint = max(clamp(inner - outer, 0.0, 1.0), spotCoverage);
    let pigment = vec3<f32>(0.72, 0.73, 0.68) * (0.92 + 0.08 * grass.properties.r);
    base = mix(base, pigment, paint * (0.96 + 0.04 * grass.properties.r));
    // Paint coats blades: it retains the grass normal and local occlusion rather than becoming a floating white ribbon.
    let roughness = mix(clamp(grass.properties.g, 0.78, 1.0), 0.92, paint);
    let ao = mix(0.85, 1.0, grass.properties.r);
    let visibility = pitchShadow(world, camera.sunAndEnvironment.xyz);
    return shadeMaterial(base, normal, world, roughness, 0.0, ao * mix(0.82, 1.0, visibility), visibility);
}
