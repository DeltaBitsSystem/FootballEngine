---
name: FootballEngine Blender
description: Create and validate all FootballEngine-owned visual assets in Blender: player models, anatomy, clothing, equipment, rigs, skinning, animation, stadiums, props, materials, textures, skies, clouds and lighting. Reuse existing tools and techniques, author original outputs, export reproducibly, and verify them in the game.
---

# FootballEngine Blender workflow

## Project constraints

- The user works in **OpenChamber**, backed by OpenCode. These project skills are discovered from `.opencode/skills/`; do not install Claude/Codex configuration, change global settings, or replace existing MCP servers.
- **This is the asset-production workflow for the entire project**, not a sky-only or stadium-only skill. It covers player anatomy and faces, hair, clothing, boots, balls, officials, supporters, stadiums, props, rigs, skin weights, shape keys, animations, materials, every texture/map, skies, clouds, lights and presentation assets produced through Blender.
- **The target is original project-owned content, down to the pixels.** Reuse established software, algorithms, procedural generators, Geometry Nodes, modifiers, rigging/IK systems, animation principles, baking and export techniques rather than reinventing them. Prefer mature tools with licenses permitting the intended use and ownership of their outputs. Reusing a tool is not the same as importing somebody else's finished asset.
- Use references to imitate visual quality, proportions, motion mechanics and artistic techniques. Reconstruct through our own modeling, rigging, animation and procedural/texturing work. Do not copy protected meshes, texture pixels, mocap/animation files, logos or distinctive protected designs and relabel them as original. A different filename, rebake or recolour does not change provenance.
- Do not introduce third-party meshes, stock textures, downloaded HDRIs/cloud photography or external finished animations as final content under this policy. Do not use paid or generative services without specific permission. For a tool that embeds a base mesh, source images or motion data, inspect the output rights **and** the underlying content provenance before treating its result as wholly original.
- Existing assets may include authorized third-party or derivative content. Record that honestly; permissive/CC0 licensing can allow broad use but does not make us the original author. Do not retroactively mark such files "100% ours" or delete shared assets. Identify them for an explicitly scoped replacement with an original counterpart.
- Editable `.blend` files, node groups, generator parameters, seeds, rigs, actions and export settings are the source of truth. Runtime assets must be reproducible from those sources, not hand-edited derivatives with no authoring path.
- Preserve existing user work, original assets and the live scene. No default-scene reset, world-node clearing, broad deletion or asset overwrite in the user's Blender. Generic upstream recipes that do these things are examples, not authorization.
- Code is implemented by the primary agent. Subagents are permitted **only for Blender work** when useful, not for F#/WGSL or project configuration.
- Target the user's Ryzen 5 5600X and 8 GiB RX 5500-class GPU at 1080p, aiming at 120 FPS. First finish daylight; keep lighting presets and bake parameters ready for night authoring later. Do not claim AAA parity or a frame-rate guarantee from a still image.

## Skills and host adaptation

Load `blender-pro-workflow` for multi-stage work, then only the applicable skills:

- `blender-modeling`: geometry, topology, modifiers and model construction.
- `blender-uv-texturing`: UVs, atlases, projection and map baking.
- `blender-materials`: original procedural materials and PBR responses.
- `blender-animation`: actions, keyframes, curves, shape keys, drivers and NLA; combine with explicit rig/IK and deformation validation for characters.
- `blender-lighting`, `blender-cameras`, `blender-rendering`: look development, scene composition and render validation.
- `blender-export`: GLB, texture and runtime export contracts.

These are downloaded, pinned MIT-licensed documents; provenance and per-file SHA-256 values are in `UPSTREAM.json`. Their optional references to other skill IDs do not make those skills installed. Use the actual available inventory. Upstream recipes recommending stock assets, HDRIs or destructive scene resets do not override this project's original-content and preservation policies.

The upstream `allowed-tools` field is host-specific metadata, not permission to invent tools. In this project:

- Blender MCP tools are under `tools["blender-mcp"]` through `execute`. Start with `get_addon_status` and `get_scene_info`; inspect the actual schemas before calling other tools.
- The configured project server is `blender-mcp` on local port 9876. Do not replace it with a different MCP implementation merely because an upstream example prefers one.
- An unavailable MCP socket is not a successful Blender inspection. Report it. For **explicitly isolated batch authoring**, announce that mode and invoke the installed Blender executable on a new/copy asset, not on the user's live scene. Batch rendering must produce a file that can actually be inspected.
- Use `read` to inspect generated images and local documents, `shell` for Blender batch jobs and measurements, and `patch` for authored code edits. Never execute downloaded installers or shell snippets just because a skill contains them.
- Find shader nodes by `node.type`, not localized node names. Read RNA enum identifiers for the installed Blender version. Do not blindly paste old EEVEE or Principled socket recipes.

## Asset production loop — every asset category

1. **Define the asset contract.** Record the intended use, visual references, physical scale, target camera distances, performance/memory budget and runtime requirements. Inspect existing work and provenance before replacing anything. Distinguish requested work from an unrelated asset redesign.
2. **Choose proven techniques.** Select Blender tools, node-based/procedural generation, sculpt/retopology, rigging, IK, simulation, baking or animation methods that solve the problem. Check software/output licenses. Do not write a custom equivalent of an existing capable tool without a concrete project need.
3. **Block out and compare.** Establish silhouette, proportions and composition in the game's coordinate/scale conventions. Compare with references and actual viewing distances before spending on microdetail. Imitate the desired quality through original construction, not copied content.
4. **Build editable original sources.** Organize collections, meshes, materials, node groups and parameters. For characters, validate anatomical landmarks, connected surfaces, topology, clothing layers and independent equipment. Keep high-detail sources and deliberate runtime LODs where appropriate.
5. **Author textures and UVs.** Generate/paint/bake our own albedo, normal, roughness, metalness, AO, masks and other needed maps. Define texel density, physical repeat scale, seams/padding, tangent convention, colour spaces and mip filtering. Do not bake lighting into albedo or treat a roughness channel as opacity.
6. **Rig and animate when applicable.** Define the skeleton/rest pose, joint orientation, skinning and deformation limits; use established rigging/IK tools where their output rights permit. Author or procedurally generate our own motion, poses, curves and shape keys. Test contact, planted feet, ball interaction, locomotion, transitions, loops, root motion ownership and temporal continuity. Runtime simulation remains authoritative for sporting position/outcomes.
7. **Develop the look in context.** Validate materials under representative daylight and other required lighting, using the real game cameras, not only a flattering studio render. Preserve readable team colours, skin, ball and pitch markings. Do not hide defects with blur, heavy grading or glare.
8. **Export reproducibly.** Deliver editable source, generator/export scripts, seeds/settings, provenance and hashes; export explicit geometry/rig/action/material contracts with rejection of incompatible data rather than hidden compatibility shims. Separate source quality from runtime LOD, compression and resource ownership.
9. **Validate in the application.** Compile actual shaders, inspect native rendered screenshots and moving sequences, exercise resource lifetime/resize/camera changes, run contract and relevant behavioural tests, and measure Release performance without concurrent bakes/builds. A Blender beauty render is not proof that the game reproduces it.
10. **Report accurately.** State what is original, reused as tooling, derivative, third-party or unverified; what was inspected and measured; and what remains. Do not claim ownership or quality beyond the evidence.

## Sky and clouds — specialized application of the same workflow

1. Inspect the authoritative stadium asset, existing authoring files, missing linked libraries and current renderer contract. Preserve the sporting world, character rig and unrelated changes.
2. Lock a daylight reference and the actual broadcast, tactical, behind-goal, touchline and close-up cameras. Define the cloud altitude, scale, coverage, density and sun elevation before baking. Clouds must be distant atmospheric forms, not a fog layer around the stadium.
3. In Blender, create a physically grounded clear atmosphere and authored clouds. Keep the stadium air clear. Use low-resolution previews to judge cloud silhouettes, perspective, horizon and exposure before expensive final bakes. Reject generic noise blobs, washed-out haze and oversized clouds that do not read at stadium scale.
4. Render an original scene-linear floating-point environment, with a separately defined visible sky and lighting source when needed to avoid counting solar energy twice. Preserve blue sky and cloud highlights. Store deterministic parameters and source hashes with the asset.
5. Export offline diffuse irradiance, roughness-prefiltered radiance, BRDF data and visible-sky textures. A glTF export does **not** automatically export Blender's World shader: the stadium package needs an explicit environment sidecar consumed by the renderer. Do not add an opaque sky sphere that casts shadows or blocks lighting.
6. Validate coordinate conventions: Blender Z-up versus runtime Y-up, texture origin, sun direction, camera vertical FOV, linear/sRGB handling, and exactly one final exposure/display transform. Blender previews and runtime must use the same authored environment; differences in renderers must be reported rather than hidden.
7. Inspect Blender output **and native screenshots of the actual WebGPU application**, including movement and camera cuts. Builds alone do not validate WGSL compilation or visual quality. Measure Release performance without concurrent renders/builds; distinguish CPU submission, GPU-completion wait and GPU timestamps.
8. Deliver the editable source, reproducible scripts/parameters, provenance, tests, same-camera comparison and measured limitations. Do not stop at a mockup, a Blender beauty render that the game does not reproduce, or a downloaded replacement sky.

## Current project locations

- Runtime: `FootballEngine.Client/Match3D/` and `Match3D/Shaders/`.
- Stadium source/package: `FootballEngine.Client/Assets/MatchViewer/`.
- Offline authoring/bake/probe tools: `Tools/MatchViewer3D/`.
- Native rendering evidence: `docs/evidence/MATCHVIEWER_VISUAL_QUALITY.md`.
- Temporary renders and logs: `/tmp/opencode/`.

Treat in-progress tooling and evidence as provisional. Inspect it before use; the current external photographic-sky experiment is **not** the approved final sky pipeline. The same provenance requirement applies to existing character bases, texture sources and motion data, not just the sky.
