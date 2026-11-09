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
@group(0) @binding(13) var kitAlbedo: texture_2d_array<f32>;
@group(0) @binding(14) var kitNormalMap: texture_2d_array<f32>;
@group(0) @binding(15) var kitOrm: texture_2d_array<f32>;
// Three vec4 per kit instance in layout space: (x, y, z, yaw degrees), (scale, 0), (tint, 1).
@group(0) @binding(16) var<storage, read> kitInstances: array<vec4<f32>>;
const KIT_INSTANCED: u32 = 0xffffffffu;
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
    // Kit material parameters ride in the joint attribute, which unskinned geometry does not use.
    @location(6) @interpolate(flat) params: vec4<f32>,
    @location(7) @interpolate(flat) tint: vec3<f32>,
}

// Kit pieces are authored glTF y-up; the layout places them in Blender's z-up frame exactly like the look-dev
// assembler (translate * rotate z * scale), and the renderer is y-up with layout +y mapped to -z.
fn kitRotate(b: vec3<f32>, yawDegrees: f32) -> vec3<f32> {
    let angle = radians(yawDegrees);
    let c = cos(angle);
    let s = sin(angle);
    return vec3<f32>(c * b.x - s * b.y, s * b.x + c * b.y, b.z);
}

fn kitVertex(input: VertexInput, instance: u32) -> VertexOutput {
    var output: VertexOutput;
    let base = instance * 3u;
    let place = kitInstances[base];
    let scale = kitInstances[base + 1u].xyz;
    let placed = kitRotate(vec3<f32>(input.position.x * scale.x, -input.position.z * scale.y, input.position.y * scale.z), place.w) + place.xyz;
    // Normals take the inverse scale so stretched towers and tunnels keep perpendicular shading.
    let normal = kitRotate(vec3<f32>(input.normal.x / scale.x, -input.normal.z / scale.y, input.normal.y / scale.z), place.w);
    let world = vec3<f32>(placed.x, placed.z, -placed.y);
    output.clipPosition = camera.viewProjection * vec4<f32>(world, 1.0);
    output.normal = normalize(vec3<f32>(normal.x, normal.z, -normal.y));
    output.color = input.color;
    output.uv = input.uv;
    output.world = world;
    output.textured = 0u;
    output.surface = input.surface;
    output.params = input.joints;
    output.tint = kitInstances[base + 2u].rgb;
    return output;
}

fn transformVertex(input: VertexInput, instance: u32) -> VertexOutput {
    if input.rootIndex == KIT_INSTANCED {
        return kitVertex(input, instance);
    }
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
    output.params = input.joints;
    output.tint = input.color.rgb;
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

// Daylight LED radiance relative to the sun's 3.2: bright enough to read as lit panels, not as lamps.
const LED_RADIANCE: f32 = 1.6;

// Scrolling club-coloured banners: alternating 6 m panels with a bold centre stripe of dashes. Coarse shapes
// keep distant boards from aliasing into noise until sponsor content exists.
fn ledContent(uv: vec2<f32>, size: vec2<f32>, world: vec3<f32>) -> vec3<f32> {
    let metres = uv * size;
    let scrolled = metres.x - camera.position.w * 1.2 + world.x * 0.05;
    let panel = floor(scrolled / 6.0);
    let local = vec2<f32>(scrolled - panel * 6.0, metres.y / max(size.y, 0.01));
    let alternate = (i32(panel) & 1) == 0;
    let ground = select(camera.clubSecondary.rgb, camera.clubPrimary.rgb, alternate);
    let ink = select(camera.clubPrimary.rgb, camera.clubSecondary.rgb, alternate);
    let dash = fract(local.x / 1.6) < 0.7;
    let stripe = local.x > 0.5 && local.x < 5.5 && abs(local.y - 0.5) < 0.18 && dash;
    return select(ground, ink, stripe) * LED_RADIANCE;
}

struct KitShading {
    colour: vec3<f32>,
    alpha: f32,
}

fn shadeKit(input: VertexOutput, geometricNormal: vec3<f32>, dpdxWorld: vec3<f32>, dpdyWorld: vec3<f32>, duvdx: vec2<f32>, duvdy: vec2<f32>) -> KitShading {
    // Sheets, nets and flags are seen from both sides; lighting the back face with the front normal turns roof
    // undersides into glowing ceilings.
    let toEye = camera.position.xyz - input.world;
    let normal = select(geometricNormal, -geometricNormal, dot(geometricNormal, toEye) < 0.0);
    let tint = select(input.color.rgb, input.tint, input.params.z > 0.5);
    var base = tint;
    var shadingNormal = normal;
    var roughness = 0.85;
    var metallic = 0.0;
    var ao = 1.0;
    var alpha = input.color.a;
    var emitted = vec3<f32>(0.0);
    if input.surface < 32u {
        let layer = i32(input.surface - 16u);
        let albedo = textureSampleGrad(kitAlbedo, surfaceSampler, input.uv, layer, duvdx, duvdy);
        let detail = textureSampleGrad(kitNormalMap, surfaceSampler, input.uv, layer, duvdx, duvdy).rgb * 2.0 - 1.0;
        let orm = textureSampleGrad(kitOrm, surfaceSampler, input.uv, layer, duvdx, duvdy).rgb;
        let perpendicularX = cross(dpdyWorld, normal);
        let perpendicularY = cross(normal, dpdxWorld);
        let tangent = perpendicularX * duvdx.x + perpendicularY * duvdy.x;
        let bitangent = perpendicularX * duvdx.y + perpendicularY * duvdy.y;
        let scale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 0.00000001));
        shadingNormal = normalize(tangent * scale * detail.x + bitangent * scale * detail.y + normal * detail.z);
        if input.params.w > 0.5 {
            // The net texture is a coverage mask in red; mips average it, so distant mesh fades instead of aliasing.
            alpha = albedo.r;
            base = tint * 0.9;
        } else {
            base = albedo.rgb * tint;
        }
        ao = orm.r;
        roughness = clamp(orm.g, 0.05, 1.0);
        metallic = orm.b;
    } else if input.surface == 32u {
        roughness = input.params.x;
        metallic = input.params.y;
    } else {
        roughness = input.params.z;
        emitted = ledContent(input.uv, input.params.xy, input.world);
    }
    let lit = shadeMaterial(base, shadingNormal, input.world, roughness, metallic, ao, sunlightVisibility(input.world, normal)) + emitted;
    return KitShading(lit, alpha);
}

@fragment fn fs(input: VertexOutput) -> @location(0) vec4<f32> {
    let normal = normalize(input.normal);
    let dpdxWorld = dpdx(input.world);
    let dpdyWorld = dpdy(input.world);
    let duvdx = dpdx(input.uv);
    let duvdy = dpdy(input.uv);
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
    if input.surface >= 16u {
        let kit = shadeKit(input, normal, dpdxWorld, dpdyWorld, duvdx, duvdy);
        return vec4<f32>(kit.colour, 1.0);
    }
    if input.surface == 7u {
        return vec4<f32>(shadeMatchBall(input.uv, normal, input.world), 1.0);
    }
    if input.surface != 0u {
        return vec4<f32>(shadePitch(input.world, input.surface, dpdxWorld, dpdyWorld), input.color.a);
    }
    return vec4<f32>(shadeMaterial(input.color.rgb, normal, input.world, 0.85, 0.0, 1.0, sunlightVisibility(input.world, normal)), input.color.a);
}

// Blended kit geometry outputs premultiplied colour so glass keeps its tint while letting the scene through.
@fragment fn fsBlended(input: VertexOutput) -> @location(0) vec4<f32> {
    let normal = normalize(input.normal);
    let kit = shadeKit(input, normal, dpdx(input.world), dpdy(input.world), dpdx(input.uv), dpdy(input.uv));
    return vec4<f32>(kit.colour * kit.alpha, kit.alpha);
}
