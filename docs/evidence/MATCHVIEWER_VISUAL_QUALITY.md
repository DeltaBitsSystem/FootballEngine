# Match viewer visual quality — implementation in progress

Target: the user's Ryzen 5 5600X, AMD Navi 14 / RX 5500-class GPU with 8 GiB VRAM, 16 GiB RAM. Desktop reported by XWayland: 1920×1080 at approximately 60 Hz. Engineering target is 120 rendered frames/s at 1080p, not a claim that this display shows 120 distinct frames/s.

## Implemented so far

- Directional geometric depth shadows, 4096² Depth32float, fixed world-space coverage of the pitch and nearby stadium. Caster and receiver bindings are separate: a shadow texture cannot be bound for sampling during its depth pass. The same vertex deformation function and live skeletal palettes drive both passes. Geometry shadows replace the pitch's old 22-player proxy loop; no doubled proxy darkening remains.
- Players, pitch and stadium receive direct-light shadows. Selection annotations do not cast physical shadows. PCF comparison filtering, slope bias and receiver-normal offset. Resource ownership and partial-initialization cleanup live in `DirectionalShadowPass.fs`.
- Stable camera-independent directional projection with alternate basis for near-parallel light directions; no per-frame shadow-matrix allocation. Daylight direction adjusted for more readable modelling.
- Normal-variance specular antialiasing affects both direct and image-based highlights; environment specular occlusion is separate from diffuse cavity AO.
- Restrained metre-scale turf pigment variation and localized goalmouth/centre wear, preserving derivative-filtered paint and existing stochastic turf textures.
- Offline surface mip generation now filters GGX roughness in alpha-squared space and accumulates normal variance rather than discarding it during normal renormalization. Existing pinned CC0 sources are reused; no new network or Blender dependency.
- Athlete atlas schema 2 declares material tile identities and eight verified offline mips per map. Runtime no longer averages sRGB bytes or normals at startup. Albedo reduction is linear-light; normal vectors are renormalized; roughness retains normal variance. The Blender authoring script invokes the same builder after exporting a fresh pack.
- Fixed a pre-existing detail-image decode error: roughness in alpha was being treated as transparency, premultiplying normal RGB. Explicit unpremultiplied codec decode preserves data channels. New tests exposed this issue and now check mip colour-space and normal-length behaviour.
- Explicit athlete triangle material identities drive restrained cloth sheen and bounded skin diffusion-wrap responses. This is not a claim of full skin SSS. Turf fragments no longer sample the athlete/concrete textures unnecessarily.
- Static stadium shadow depth is cached once and copied before drawing moving occluders, instead of rasterizing the stadium again each frame. Cache ownership is renderer-local; its immutable light direction/projection match the renderer lifetime. A resized output does not invalidate world-space shadow depth.
- Broadcast, tactical, behind-goal, touchline and close-up camera projection presets are now independent of the stadium's authored camera. Renderer API supports changing the preset; the diagnostic probe selects it with `MATCHVIEWER_PROBE_VIEW`. User-facing camera selection remains to be wired.

## Verified

The initial solution, Client.Tests and Benchmarks builds succeeded before implementation. Existing warnings were not treated as errors or silently fixed as part of this task.

Client suite after shadow/material changes: **174/174 passed**. New tests cover pitch shadow-volume coverage, WebGPU depth ordering and nondegenerate directional-light bases. Formatting limited to the touched render/test files; `git diff --check` passes.

Real WebGPU moving probe after shadow module extraction initialized and presented without logged validation errors. Same-size intermediate observations at 1160×698 (Debug, Mailbox, 165 samples after warmup): before geometric shadows median submission/backpressure 4.746 ms, p95 5.318 ms, 222.2 presents/s; first geometric-shadow version median 4.820 ms, p95 5.397 ms, 211.3 presents/s. These are **not GPU timestamps, 1080p benchmarks or final performance results**; the later sun/material refinements require fresh measurements.

Native probe captures inspected: `/tmp/opencode/visual-material-initial.png`, `/tmp/opencode/visual-shadow-first.png`, `/tmp/opencode/visual-grass-first.png`, `/tmp/opencode/visual-shadow-goal.png`. The grass-first capture had overly broad pigment patches; contrast was reduced afterward. Goal capture shows current geometric player shadows, turf wear and preserved line readability. These are actual viewer images, not Blender renders.

## Remaining before completion

- Finish camera-selection integration and close-view detail review.
- Validate different viewing distances/angles, shadows near goals and stands, motion and resize/attach/disposal.
- Capture controlled before/after evidence and measure the final renderer at 1080p. Inspect the actual deterministic application, not only the synthetic probe.
- Full closing builds/tests, asset reproducibility verification and final documentation. No claim of AAA parity or that simpler crowd/geometry have become photorealistic through shaders.

Useful commands/logs: `/tmp/opencode/visual-*-build.log`, `/tmp/opencode/visual-client-tests.log`, `/tmp/opencode/visual-shadow-module-probe.log`. Probe: `dotnet run --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj`; `MATCHVIEWER_PROBE_FRAMES`, `MATCHVIEWER_PROBE_FIXED_POSE`, `MATCHVIEWER_PROBE_CAMERA_SCENE`, `MATCHVIEWER_PROBE_CAPTURE`, `MATCHVIEWER_PROBE_MEASURE` and `MATCHVIEWER_PROBE_WAIT_GPU` bound/capture the run. Performance tests must run without concurrent builds/tests.

## 1080p measurement checkpoint

Release, actual 1920×1080 GPU surface confirmed in logs, default 4× MSAA, 600-frame moving probe, 15 warmup frames excluded. Before snapshot is `git archive HEAD` in `/tmp/opencode/visual-baseline`; only the diagnostic fullscreen switch was added there. It does not contain unrelated uncommitted application changes, but its rendering/assets match the inspected pre-task baseline. No original worktree files were reset or replaced. Same fixed-pose midfield captures are `/tmp/opencode/visual-before-1080.png` and `/tmp/opencode/visual-after-1080.png`.

| Version | Method | Median | p95 | Presents/s |
| --- | --- | ---: | ---: | ---: |
| Before | no GPU wait, submission/backpressure | 4.634 ms | 5.077 ms | 292.3 |
| Before | diagnostic CPU + GPU completion wait | 2.650 ms | 7.758 ms | 209.8 |
| New shadows/materials, before static cache | no GPU wait, submission/backpressure | 4.737 ms | 7.747 ms | 234.2 |
| New shadows/materials, before static cache | diagnostic CPU + GPU completion wait | 4.225 ms | 9.291 ms | 159.0 |
| New shadows/materials, static cache | diagnostic CPU + GPU completion wait | 3.319 ms | 8.376 ms | 185.2 |

These are different diagnostic methods, not interchangeable GPU timestamps. Static caching reduced the new completed-work median by about 21%. Average throughput exceeds the 120 FPS goal in this synthetic trace; the completed-work p95 is still slightly over 8.33 ms. This is not a guarantee of 120 FPS minimum in a real match or every camera. Measurements must be refreshed after remaining changes.
