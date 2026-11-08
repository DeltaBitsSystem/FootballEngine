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
    // Broadcast views resolve patches of growth, not individual blades; keep variation in metre-scale pigment.
    let growth = materialNoise(world.xz * 0.37 + vec2<f32>(13.7, -8.1));
    let fineGrowth = materialNoise(world.xz * 1.25 + vec2<f32>(-4.3, 23.6));
    let growthBlend = smoothstep(0.25, 0.78, growth * 0.72 + fineGrowth * 0.28);
    base *= 0.90 + 0.12 * clumps + 0.08 * broad;
    base *= mix(vec3<f32>(0.93, 0.96, 0.87), vec3<f32>(1.05, 1.02, 0.95), growthBlend);

    // Mowing stripes are not paint: the mower travels across the pitch (along z) and leaves the blades of each band
    // leaning one way. A band looks light when its blades lean away from the viewer (we see their lit, waxy upper
    // sides) and dark when they lean towards us (shadowed tips). The band edges wobble like a real mower line.
    let wobble = (materialNoise(vec2<f32>(world.z * 0.09, 3.1)) - 0.5) * 0.10 + (materialNoise(vec2<f32>(world.z * 0.7, 9.4)) - 0.5) * 0.025;
    let bandPosition = (world.x + PITCH_HALF_LENGTH) / PITCH_MOWING_BAND + wobble;
    let leanSign = smoothstep(-0.06, 0.06, sin(bandPosition * 3.14159265359)) * 2.0 - 1.0;
    let lean = select(leanSign, 0.0, surface == 3u);
    let view = normalize(camera.position.xyz - world);
    let viewFlat = normalize(vec2<f32>(view.x, view.z) + vec2<f32>(1e-4, 0.0));
    // Grazing views see more blade side, so the stripe contrast grows as the camera lowers.
    let grazing = 1.0 - clamp(view.y, 0.0, 1.0);
    // Blades leaning towards the sun catch more light: this keeps stripes readable from high tactical cameras.
    // Broadcast stripes differ by roughly 10-15% in brightness; more reads as painted bands.
    let stripe = -lean * viewFlat.y * (0.045 + 0.09 * grazing) + lean * camera.sunAndEnvironment.z * 0.03;
    base *= 1.0 + stripe;
    // The leaning canopy also faces the sun differently: tilt the shading normal with the blades.
    normal = normalize(normal + vec3<f32>(0.0, 0.0, lean * 0.22));

    let goalDistance = vec2<f32>((abs(world.x) - (PITCH_HALF_LENGTH - 1.5)) / 4.2, world.z / 5.0);
    let goalWear = exp(-dot(goalDistance, goalDistance) * 1.8);
    let centreWear = exp(-dot(world.xz / vec2<f32>(7.0, 5.0), world.xz / vec2<f32>(7.0, 5.0)));
    let wear = max(goalWear * 0.24, centreWear * 0.07) * (0.55 + 0.45 * growth);
    base = mix(base, vec3<f32>(0.11, 0.085, 0.028), wear);

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
    let visibility = sunlightVisibility(world, vec3<f32>(0.0, 1.0, 0.0));
    return shadeCanopy(base, normal, world, roughness, ao, visibility, 1.0 - paint);
}

// Grass is a canopy of thin translucent blades, not an opaque plastic sheet. On top of the shared PBR response:
// - translucency: looking towards the sun, light passes through the blades and they glow yellow-green;
// - hotspot: with the sun behind the viewer the blades hide their own shadows and the canopy brightens;
// - grazing shift: at low angles we see blade sides instead of soil gaps, lighter and a little yellower;
// - cuticle sheen: the waxy blade surface adds a soft, bluish grazing highlight.
// `canopy` fades the effects out under paint, which coats the blades flat.
fn shadeCanopy(baseIn: vec3<f32>, normal: vec3<f32>, world: vec3<f32>, roughness: f32, ao: f32, visibility: f32, canopy: f32) -> vec3<f32> {
    let light = camera.sunAndEnvironment.xyz;
    let view = normalize(camera.position.xyz - world);
    let nv = clamp(dot(normal, view), 0.0, 1.0);
    let sideView = pow(1.0 - nv, 2.0) * canopy;
    let base = mix(baseIn, baseIn * vec3<f32>(1.18, 1.16, 0.92), sideView * 0.55);
    var radiance = shadeMaterial(base, normal, world, roughness, 0.0, ao, visibility);
    let sun = camera.sunRadiance.rgb * visibility;
    let back = pow(clamp(dot(-view, light), 0.0, 1.0), 4.0);
    let transmitted = base * vec3<f32>(0.85, 1.15, 0.45) * sun * back * (0.10 + 0.25 * sideView) * canopy;
    let hotspot = pow(clamp(dot(view, light), 0.0, 1.0), 12.0) * 0.18 * canopy;
    let sheen = pow(1.0 - nv, 5.0) * 0.05 * canopy;
    radiance += transmitted + base * sun * max(dot(normal, light), 0.0) * hotspot / 3.14159265359;
    radiance += vec3<f32>(0.85, 0.92, 1.0) * sheen * (sun * 0.25 + camera.sunAndEnvironment.w * 0.2);
    return radiance;
}
