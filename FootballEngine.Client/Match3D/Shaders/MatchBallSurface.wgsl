fn shadeMatchBall(uv: vec2<f32>, normal: vec3<f32>, world: vec3<f32>) -> vec3<f32> {
    let phi = uv.x * 6.28318530718;
    let theta = uv.y * 3.14159265359;
    let direction = vec3<f32>(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi));
    let golden = 1.61803398875;
    let inverseGolden = 1.0 / golden;
    var best = -2.0;
    var second = -2.0;
    var darkPanel = false;
    // Spherical Voronoi cells of the icosahedron and its dual produce twelve pentagons and twenty hexagons.
    for (var face = 0u; face < 32u; face += 1u) {
        var centre = vec3<f32>(0.0);
        if face < 12u {
            let axis = face / 4u;
            let a = select(-1.0, 1.0, (face & 1u) != 0u);
            let b = select(-golden, golden, (face & 2u) != 0u);
            if axis == 0u { centre = vec3<f32>(0.0, a, b); }
            else if axis == 1u { centre = vec3<f32>(a, b, 0.0); }
            else { centre = vec3<f32>(b, 0.0, a); }
        } else if face < 20u {
            let bits = face - 12u;
            centre = vec3<f32>(select(-1.0, 1.0, (bits & 1u) != 0u), select(-1.0, 1.0, (bits & 2u) != 0u), select(-1.0, 1.0, (bits & 4u) != 0u));
        } else {
            let index = face - 20u;
            let axis = index / 4u;
            let a = select(-inverseGolden, inverseGolden, (index & 1u) != 0u);
            let b = select(-golden, golden, (index & 2u) != 0u);
            if axis == 0u { centre = vec3<f32>(0.0, b, a); }
            else if axis == 1u { centre = vec3<f32>(b, a, 0.0); }
            else { centre = vec3<f32>(a, 0.0, b); }
        }
        let score = dot(direction, normalize(centre));
        if score > best {
            second = best;
            best = score;
            darkPanel = face < 12u;
        } else { second = max(second, score); }
    }
    let margin = best - second;
    let filterWidth = max(fwidth(margin), 0.0002);
    let seam = 1.0 - smoothstep(0.0015 - filterWidth, 0.0015 + filterWidth, margin);
    let panelColour = select(vec3<f32>(0.68, 0.69, 0.65), vec3<f32>(0.012, 0.015, 0.018), darkPanel);
    let base = mix(panelColour, vec3<f32>(0.055), seam * 0.75);
    let roughness = mix(select(0.46, 0.58, darkPanel), 0.80, seam);
    return shadeMaterial(base, normal, world, roughness, 0.0, 1.0, sunlightVisibility(world, normal));
}
