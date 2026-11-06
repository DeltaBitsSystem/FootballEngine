fn shadeAthlete(base: vec3<f32>, normal: vec3<f32>, geometricNormal: vec3<f32>, world: vec3<f32>, roughness: f32, material: u32) -> vec3<f32> {
    let visibility = sunlightVisibility(world, geometricNormal);
    let textile = material == ATHLETE_JERSEY || material == ATHLETE_SHORTS || material == ATHLETE_SOCKS;
    let surfaceRoughness = select(roughness, max(roughness, 0.74), textile);
    var radiance = shadeMaterial(base, normal, world, surfaceRoughness, 0.0, 1.0, visibility);
    let view = normalize(camera.position.xyz - world);
    let nv = max(dot(normal, view), 0.0);
    let nl = max(dot(normal, camera.sunAndEnvironment.xyz), 0.0);
    let ambient = textureSampleLevel(environmentMap, environmentSampler, environmentUv(normal), 6, 0.0).rgb * camera.sunAndEnvironment.w;
    if textile {
        // A restrained grazing fibre lobe distinguishes cloth without bleaching the teams' kit colours.
        let sheen = pow(1.0 - nv, 5.0) * 0.065;
        let tint = mix(vec3<f32>(1.0), base / max(max(base.r, base.g), max(base.b, 0.001)), 0.6);
        radiance = radiance * (1.0 - sheen) + tint * sheen * (ambient + camera.sunRadiance.rgb * nl * visibility);
    } else if material == ATHLETE_SKIN {
        // This bounded wrap approximates diffusion; it is not screen-space SSS or light through the whole body.
        let wrapped = clamp((dot(normal, camera.sunAndEnvironment.xyz) + 0.25) / 1.25, 0.0, 1.0);
        radiance = radiance * 0.97 + base * vec3<f32>(1.0, 0.55, 0.36) * camera.sunRadiance.rgb * wrapped * visibility * 0.03;
    }
    return radiance;
}
