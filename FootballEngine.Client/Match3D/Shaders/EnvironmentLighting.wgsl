fn environmentUv(direction: vec3<f32>) -> vec2<f32> {
    return vec2<f32>(atan2(direction.z, direction.x) / 6.28318530718 + 0.5, acos(clamp(direction.y, -1.0, 1.0)) / 3.14159265359);
}

fn shadeMaterial(base: vec3<f32>, normal: vec3<f32>, world: vec3<f32>, roughness: f32, metallic: f32, ao: f32, visibility: f32) -> vec3<f32> {
    let light = camera.sunAndEnvironment.xyz;
    let view = normalize(camera.position.xyz - world);
    let halfVector = normalize(light + view);
    let nl = max(dot(normal, light), 0.0);
    let nv = max(dot(normal, view), 0.001);
    let nh = max(dot(normal, halfVector), 0.0);
    let vh = max(dot(view, halfVector), 0.0);
    // Unresolved normal variation must broaden the highlight rather than sparkle as the camera moves.
    let normalDx = dpdx(normal);
    let normalDy = dpdy(normal);
    let variance = 0.5 * (dot(normalDx, normalDx) + dot(normalDy, normalDy));
    let alpha2 = clamp(pow(roughness, 4.0) + min(variance, 0.18), 0.0001, 1.0);
    let filteredRoughness = sqrt(sqrt(alpha2));
    let distribution = alpha2 / max(3.14159265359 * pow(nh * nh * (alpha2 - 1.0) + 1.0, 2.0), 0.000001);
    let k = pow(filteredRoughness + 1.0, 2.0) / 8.0;
    let geometry = (nl / max(nl * (1.0 - k) + k, 0.001)) * (nv / (nv * (1.0 - k) + k));
    let f0 = mix(vec3<f32>(0.04), base, metallic);
    let fresnel = f0 + (vec3<f32>(1.0) - f0) * pow(1.0 - vh, 5.0);
    let specular = distribution * geometry * fresnel / max(4.0 * nl * nv, 0.001);
    let diffuse = base * (vec3<f32>(1.0) - fresnel) * (1.0 - metallic) / 3.14159265359;
    let direct = (diffuse + specular) * camera.sunRadiance.rgb * nl * visibility;

    let reflection = reflect(-view, normal);
    let uv = environmentUv(reflection);
    let level = filteredRoughness * 5.0;
    let lower = i32(floor(level));
    let upper = min(lower + 1, 5);
    let radiance = mix(textureSampleLevel(environmentMap, environmentSampler, uv, lower, 0.0).rgb,
                       textureSampleLevel(environmentMap, environmentSampler, uv, upper, 0.0).rgb, fract(level));
    let irradianceOverPi = textureSampleLevel(environmentMap, environmentSampler, environmentUv(normal), 6, 0.0).rgb;
    let integratedBrdf = textureSampleLevel(environmentBrdf, environmentSampler, vec2<f32>(nv, filteredRoughness), 0.0).rg;
    let grazing = max(vec3<f32>(1.0 - filteredRoughness), f0);
    let environmentFresnel = f0 + (grazing - f0) * pow(1.0 - nv, 5.0);
    let diffuseIbl = irradianceOverPi * base * (vec3<f32>(1.0) - environmentFresnel) * (1.0 - metallic);
    let specularIbl = radiance * (f0 * integratedBrdf.x + integratedBrdf.y);
    // Cavity AO attenuates diffuse light; grazing reflections need view/roughness-aware specular occlusion.
    let specularOcclusion = clamp(pow(nv + ao, exp2(-16.0 * filteredRoughness - 1.0)) - 1.0 + ao, 0.0, 1.0);
    return direct + (diffuseIbl * ao + specularIbl * specularOcclusion) * camera.sunAndEnvironment.w;
}
