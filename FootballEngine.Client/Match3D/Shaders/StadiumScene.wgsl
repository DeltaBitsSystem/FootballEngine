@group(0) @binding(0) var<uniform> camera: Camera;
@group(0) @binding(1) var<storage, read> joints: array<mat4x4<f32>>;
@group(0) @binding(2) var athleteAlbedo: texture_2d<f32>;
@group(0) @binding(3) var athleteDetail: texture_2d<f32>;
@group(0) @binding(4) var athleteSampler: sampler;
@group(0) @binding(5) var surfaceAlbedo: texture_2d_array<f32>;
@group(0) @binding(6) var surfaceNormalMap: texture_2d_array<f32>;
@group(0) @binding(7) var surfaceArm: texture_2d_array<f32>;
@group(0) @binding(8) var surfaceSampler: sampler;
@group(0) @binding(9) var environmentMap: texture_2d_array<f32>;
@group(0) @binding(10) var environmentBrdf: texture_2d<f32>;
@group(0) @binding(11) var environmentSampler: sampler;
@group(0) @binding(12) var skyMap: texture_2d<f32>;
const JOINT_COUNT: u32 = CHARACTER_JOINT_COUNT;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
    @location(3) uv: vec2<f32>,
    @location(4) rootIndex: u32,
    @location(5) joints: vec4<f32>,
    @location(6) weights: vec4<f32>,
    @location(7) surface: u32,
}

struct VertexOutput {
    @builtin(position) clipPosition: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) uv: vec2<f32>,
    @location(3) world: vec3<f32>,
    @location(4) @interpolate(flat) textured: u32,
    @location(5) @interpolate(flat) surface: u32,
}

fn transformVertex(input: VertexInput, instance: u32) -> VertexOutput {
    var output: VertexOutput;
    let root = input.rootIndex + instance;
    let model = camera.rootTransforms[root];
    var localPosition = vec4<f32>(input.position, 1.0);
    var localNormal = vec4<f32>(input.normal, 0.0);
    if dot(input.weights, vec4<f32>(1.0)) > 0.0 {
        let base = root * JOINT_COUNT;
        let skin = joints[base + u32(input.joints.x)] * input.weights.x
                 + joints[base + u32(input.joints.y)] * input.weights.y
                 + joints[base + u32(input.joints.z)] * input.weights.z
                 + joints[base + u32(input.joints.w)] * input.weights.w;
        localPosition = skin * localPosition;
        localNormal = skin * localNormal;
    }
    if root == 24u && input.position.y > 2.0 {
        // The overhead annotation must not become a giant triangle when the replay lens moves close.
        let markerScale = clamp(distance(camera.position.xyz, model[3].xyz) / 48.0, 0.12, 1.5);
        localPosition.x *= markerScale;
        localPosition.y = 2.08 + (localPosition.y - 2.08) * markerScale;
    }
    let world = model * localPosition;
    output.clipPosition = camera.viewProjection * world;
    output.normal = normalize((model * localNormal).xyz);
    output.color = input.color;
    let awayKit = dot(input.weights, vec4<f32>(1.0)) > 0.0 && root >= 11u;
    output.uv = input.uv + vec2<f32>(0.0, select(0.0, 0.5, awayKit));
    output.world = world.xyz;
    output.textured = select(0u, 1u, dot(input.weights, vec4<f32>(1.0)) > 0.0);
    output.surface = input.surface;
    return output;
}

@vertex fn shadowVs(input: VertexInput, @builtin(instance_index) instance: u32) -> @builtin(position) vec4<f32> {
    let vertex = transformVertex(input, instance);
    return camera.lightViewProjection * vec4<f32>(vertex.world, 1.0);
}

@vertex fn vs(input: VertexInput, @builtin(instance_index) instance: u32) -> VertexOutput {
    return transformVertex(input, instance);
}

fn materialHash(cell: vec2<i32>) -> f32 {
    var h = bitcast<u32>(cell.x) * 1597334677u ^ bitcast<u32>(cell.y) * 3812015801u;
    h = (h ^ (h >> 16u)) * 2246822519u;
    return f32(h & 0x00ffffffu) / 16777215.0;
}

fn materialNoise(p: vec2<f32>) -> f32 {
    let cell = vec2<i32>(floor(p));
    let f = fract(p);
    let blend = f * f * (3.0 - 2.0 * f);
    return mix(mix(materialHash(cell), materialHash(cell + vec2<i32>(1, 0)), blend.x),
               mix(materialHash(cell + vec2<i32>(0, 1)), materialHash(cell + vec2<i32>(1, 1)), blend.x), blend.y);
}

@fragment fn fs(input: VertexOutput) -> @location(0) vec4<f32> {
    let normal = normalize(input.normal);
    let sunlight = camera.sunAndEnvironment.xyz;
    let dpdxWorld = dpdx(input.world);
    let dpdyWorld = dpdy(input.world);
    let duvdx = dpdx(input.uv);
    let duvdy = dpdy(input.uv);
    let footprint = max(length(dpdxWorld), length(dpdyWorld));
    let surfaceUv = select(select(input.world.xy, input.world.zy, abs(normal.x) > abs(normal.z)), input.world.xz * vec2<f32>(1.0, -1.0), abs(normal.y) > 0.7) * 0.5;
    let surfaceLayer = select(0, 1, input.surface == 4u);
    let surfaceUvDx = dpdx(surfaceUv);
    let surfaceUvDy = dpdy(surfaceUv);
    if input.textured != 0u {
        let albedo = textureSampleGrad(athleteAlbedo, athleteSampler, input.uv, duvdx, duvdy);
        let detail = textureSampleGrad(athleteDetail, athleteSampler, input.uv, duvdx, duvdy);
        // The differential tangent frame follows the deformed mesh without importing a second skinning palette.
        let perpendicularX = cross(dpdyWorld, normal);
        let perpendicularY = cross(normal, dpdxWorld);
        let tangent = perpendicularX * duvdx.x + perpendicularY * duvdy.x;
        let bitangent = perpendicularX * duvdx.y + perpendicularY * duvdy.y;
        let inverseScale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 0.00000001));
        let mapped = detail.rgb * 2.0 - 1.0;
        let surfaceNormal = normalize(tangent * inverseScale * mapped.x + bitangent * inverseScale * mapped.y + normal * mapped.z);
        let roughness = clamp(detail.a, 0.18, 1.0);
        let litAthlete = shadeAthlete(albedo.rgb, surfaceNormal, normal, input.world, roughness, input.surface);
        return vec4<f32>(litAthlete, albedo.a);
    }
    if input.surface != 0u {
        if input.surface == 7u {
            return vec4<f32>(shadeMatchBall(input.uv, normal, input.world), 1.0);
        }
        if input.surface <= 3u {
            return vec4<f32>(shadePitch(input.world, input.surface, dpdxWorld, dpdyWorld), input.color.a);
        }
        var base = input.color.rgb;
        var materialNormal = normal;
        var roughness = 0.90;
        var metallic = 0.0;
        var ao = 1.0;
        let broadVariation = materialNoise(input.world.xz * 0.24);
        let medium = materialNoise(input.world.xz * 2.2);
        // Frequencies smaller than a pixel fade to their mean instead of producing broadcast-camera shimmer.
        let detailWeight = 1.0 - smoothstep(0.018, 0.065, footprint);
        let grain = materialNoise(input.world.xz * vec2<f32>(75.0, 22.0));
        if input.surface == 4u {
            let surfaceColour = textureSampleGrad(surfaceAlbedo, surfaceSampler, surfaceUv, surfaceLayer, surfaceUvDx, surfaceUvDy).rgb;
            let surfaceDetail = textureSampleGrad(surfaceNormalMap, surfaceSampler, surfaceUv, surfaceLayer, surfaceUvDx, surfaceUvDy).rgb * 2.0 - 1.0;
            let surfaceProperties = textureSampleGrad(surfaceArm, surfaceSampler, surfaceUv, surfaceLayer, surfaceUvDx, surfaceUvDy).rgb;
            let perpendicularX = cross(dpdyWorld, normal);
            let perpendicularY = cross(normal, dpdxWorld);
            let tangent = perpendicularX * surfaceUvDx.x + perpendicularY * surfaceUvDy.x;
            let bitangent = perpendicularX * surfaceUvDx.y + perpendicularY * surfaceUvDy.y;
            let scale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 0.00000001));
            let mapped = normalize(vec3<f32>(surfaceDetail.xy * 0.45, surfaceDetail.z));
            materialNormal = normalize(tangent * scale * mapped.x + bitangent * scale * mapped.y + normal * mapped.z);
            base *= surfaceColour / vec3<f32>(0.25);
            base *= 0.90 + 0.16 * broadVariation + (grain - 0.5) * 0.09 * detailWeight;
            roughness = clamp(surfaceProperties.g, 0.65, 1.0);
            ao = mix(0.75, 1.0, surfaceProperties.r);
        } else if input.surface == 5u {
            base *= 0.95 + 0.06 * medium;
            roughness = 0.40;
            metallic = 0.72;
        } else {
            roughness = 0.30;
        }
        let visibility = sunlightVisibility(input.world, normal);
        let lit = shadeMaterial(base, materialNormal, input.world, roughness, metallic, ao, visibility);
        return vec4<f32>(lit, input.color.a);
    }
    return vec4<f32>(shadeMaterial(input.color.rgb, normal, input.world, 0.85, 0.0, 1.0, sunlightVisibility(input.world, normal)), input.color.a);
}
