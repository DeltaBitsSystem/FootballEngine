# Match Viewer engine — pure scene, thin GPU core

Goal: turn `Match3D/WebGpuStadiumRenderer.fs` (2215 lines that mix X11, device setup, buffers, pipelines, camera
packing, match→pose logic, passes and HUD) into a small engine shaped for this game. What is drawn is a pure,
testable value; everything that touches WebGPU or X11 lives in a thin, isolated core. New visual features (C2b
stadium, C3 night, C4 crowd, weather) must become "add a system and/or a pass" without editing unrelated code.

Not goals: a generic engine, ECS, editor, scripting, hot reload, multiple backends. Abstractions exist only where
two or more current or planned (C2–C7) users need them.

## Layers (compile order inside `Match3D/`)

1. `Platform/X11ChildWindow.fs` — the `Xlib` module, child-window creation, `updateWindow`, window shape/cutouts.
   Owns the native window lifetime. No WebGPU types.
2. `Gpu/` — the only place with `nativeptr`, Silk.NET calls and native lifetimes.
   - `GpuError.fs`: `GpuError` DU (`AdapterUnavailable | DeviceLost of string | CreationFailed of label: string |
     ShaderCompile of file: string * message: string | SurfaceLost`). Creation functions return
     `Result<_, GpuError>`; nothing below the renderer façade throws.
   - `GpuContext.fs`: instance, surface, adapter, device, queue, uncaptured-error callback, surface configure
     (format, present mode, size). `IDisposable`.
   - `GpuResources.fs`: `GpuBuffer<'T>` (typed; `Create`, `Write(data)`, `Write(data, offsetElements)`,
     `Length`), `GpuTexture` (texture + default view + format + size + mips/layers; `Upload2D`, `UploadLayer`),
     `GpuSampler`. All `IDisposable`, idempotent dispose. The current `BufferWriter` and the texture upload helpers
     move here.
   - `ShaderLibrary.fs`: builds a module from an ordered list of WGSL files plus a constant preamble
     (`PitchMarkings.shaderConstants`, quality defines). Pure part `compose: preamble -> files -> string` is tested;
     the impure part compiles and returns `ShaderCompile` with the file name on failure.
   - `PipelineCache.fs`: render/compute pipelines keyed by an F# record describing them (shader entry points,
     vertex layout id, blend, depth, cull, sample count, target format). Same key → same pipeline. Bind-group
     layouts are created through small typed helpers (`uniform`, `storage`, `texture2d`, `textureArray`,
     `depthMultisampled`, `sampler`, `comparisonSampler`) instead of hand-filled descriptors.
3. `Scene/` — pure. No WebGPU, no mutation visible to callers.
   - `SceneTypes.fs`: `CameraState` (view-projection, inverse, position, near/far, fovY, aspect),
     `SunState`, `SceneFrame` = { Camera; Sun; LightViewProjection; TimeSeconds; RootTransforms: Matrix4x4[25];
     JointPalettes; ShowGrassDetail; Hud inputs }.
   - `SceneBuilder.fs`: `build : ViewerCamera -> aspect -> timeSeconds -> RenderFrame -> palettes -> SceneFrame`.
     Moves here: `updateCamera` math, `cameraFocus` smoothing (as explicit state in/out:
     `build ... previousFocus -> SceneFrame * focus`), `updatePlayersAndBall` transform logic, grass-detail rule.
   - `UniformLayout.fs`: `packCamera : SceneFrame -> float32[]` with named offsets that mirror the WGSL `Camera`
     struct (today's magic `16 + rootTransformCount * 16 + 12`). One module owns the layout; tests check sizes
     and offsets against the WGSL struct text.
   - `DrawList.fs`: `Draw` = { Pipeline: PipelineKey; Mesh: MeshHandle; IndexCount; InstanceCount;
     FirstInstance; Group: DrawGroup } where `DrawGroup = ShadowStatic | ShadowDynamic | Opaque | Blended`.
     Sorting/grouping is a pure function (opaque by pipeline then mesh; blended back-to-front by a sort key).
4. `Systems/` — game-specific producers. Each system uploads its assets once, writes its per-frame buffers from
   the `SceneFrame`, and returns its `Draw`s. Interface (the one interface in the engine):
   ```fsharp
   type IViewerSystem =
       inherit IDisposable
       abstract Update: SceneFrame * GpuFrame -> unit      // queue writes only
       abstract Draws: Draw list                            // stable unless the system changes
   ```
   Initial systems, extracted from the current renderer with identical output:
   `StadiumSystem` (today's GLB static batches; replaced by the procedural stadium in C2b),
   `PlayersSystem` (character batches ×22, joint buffer, textures), `BallSystem`, `CarrierMarkerSystem`,
   `GrassDetailSystem` (wraps `ProceduralScenePass` grass), `SkySystem`.
   HUD stays its own thing (`MatchHudRenderer` + upload) because it is a 2D overlay, not scene content.
5. `Frame/` — the frame plan.
   - `RenderTargets.fs`: MSAA colour, depth (with `TextureBinding`), resolve/HDR targets; recreated on resize or
     quality change from a pure `TargetSpec` (`TargetSpec.ofSize quality w h`), never ad hoc.
   - `FramePlan.fs`: a fixed, explicit list of passes. Each pass declares a pure `PassIo` = { Reads: Target list;
     Writes: Target list }. `FramePlan.validate` (pure, tested) rejects a pass reading a target nobody wrote earlier
     in the frame and duplicate writers without load=Load. Passes today: `Shadow` (static cache copy + dynamic),
     `Main` (sky, opaque, grass, blended), `Bloom`, `Output` (+HUD). C3 adds lights/shadow atlas, C4 crowd, C6
     weather by inserting entries — no other file changes.
   - Existing `DirectionalShadowPass`, `HdrOutputPass`, `ProceduralScenePass` are adapted to the `Gpu/` types
     and `RenderTargets`; their shading and parameters do not change.
6. `WebGpuStadiumRenderer.fs` — façade, target ≤ 300 lines: composes platform + context + systems + plan; keeps
   the current public API (`Render`, `SetCamera`, `NativeWindow`, `CharacterRig`, `HudUploadBytes`,
   `WaitForGpu`, `IDisposable`) so `MatchPitchControl`, `Shots` and `Tools/MatchViewer3D/StadiumRendererProbe`
   compile unchanged. Construction converts `GpuError` to the single exception the render worker already reports
   (`RenderWorker` `onError`), with the GpuError text — the only throw site.

## Determinism for verification

`TimeSeconds` becomes an input of `SceneBuilder.build`. The live viewer passes the wall clock; the
`--viewer-scenes` harness passes the match time of each scene. This removes the only non-deterministic input
(grass wind) so captures are comparable byte-for-byte across runs.

## Migration order (each step builds, tests green, captures compared)

0. Make time an input (harness: fixed per-scene time). Capture the 6 scenes → baseline
   (`artifacts/viewer-engine/baseline/`). Run twice to prove the harness is reproducible (identical PNGs, or
   document the measured max diff if MSAA/driver noise exists).
1. Extract `Platform/X11ChildWindow.fs`.
2. Add `Gpu/` and move device/context, buffers, textures, samplers, shader composition; adapt the three pass
   files.
3. Add `Scene/` (types, builder, uniform layout); renderer consumes `SceneFrame`.
4. Add `DrawList` + systems; the main/shadow passes iterate draws instead of hard-coded batch arrays.
5. Add `RenderTargets` + `FramePlan`; slim the façade.
After every step: captures vs baseline. Acceptance per scene: max abs channel diff ≤ 2/255 and mean ≤ 0.1/255
(expect exact equality; any non-zero diff must be explained), frame time from `stats.json` within ±5 %.

## Tests (Client.Tests, Expecto)

- `UniformLayout`: total size and every offset match the WGSL `Camera` struct parsed from the shader file;
  a mutation (shift one offset) fails.
- `SceneBuilder`: 22 root transforms placed from a known `RenderFrame`; ball root; grass rule threshold (11.9 vs
  12.1 m); focus smoothing is a function of inputs only (same inputs → same output).
- `FramePlan.validate`: current plan valid; a plan with Output before Main rejected; missing writer rejected.
- `ShaderLibrary.compose`: preamble first, files in order, every referenced file exists.
- `DrawList` grouping/sorting: opaque grouped by pipeline, blended back-to-front.
- Existing 220+ tests stay green.

## Rules

AGENTS.md conventions; comments explain why. No `Unchecked.defaultof`/`failwith`; `invalidOp` only at the façade
boundary described above. No new NuGet packages. Keep shading output identical: this unit is a refactor; visual
changes belong to C2b+.

## As built (steps 0–5 complete)

Files: `Platform/X11ChildWindow.fs`; `Gpu/` (GpuError, GpuContext, GpuResources with `GpuBuffer<'T>`,
`GpuTexture`, `GpuSampler`, `GpuOwner`, ShaderLibrary, PipelineCache); `Scene/` (SceneTypes, SceneBuilder,
UniformLayout, DrawList); `Frame/` (FramePlan, RenderTargets, SceneResources, HudOverlay, FramePacing,
FrameEncoder); `ViewerAssets.fs`; `Systems/ViewerSystems.fs`; `MatchPitchNative.fs`. The embedded WGSL moved to
`Shaders/StadiumScene.wgsl` and `Shaders/MatchHud.wgsl`. The façade is 320 lines (was 2215).

Every step was verified against `artifacts/viewer-engine/baseline2`: all six scenes masked-identical (diff 0), frame
time within ±2.5 %. Harness determinism needed three fixes, not one: `--match-seed 0`, a pinned presentation clock,
and an initial hold at tick 0 (`MatchRuntime.armInitialHold`); the HUD FPS readout is masked when comparing.

Deviations from the design above, deliberately:
- `Draw` carries no pipeline key and there is no blended layer yet: one opaque pipeline exists today. C2b adds
  `Pipeline` to `Draw` and a back-to-front blended layer when kit glass/nets arrive (the compiler will list every
  producer).
- Draw grouping keeps submission order instead of sorting by pipeline/mesh: reordering equal-depth opaque
  fragments changes pixels, and with one pipeline sorting buys nothing.
- No camera-focus smoothing exists in the old renderer, so `SceneBuilder` is stateless.
- Grass and sky remain `ProceduralScenePass` objects owned by `SceneResources`, encoded inside the main pass, rather
  than `IViewerSystem`s: they draw with no mesh and share the scene pipeline layout.

How to add things now:
- New scene content (procedural stadium, crowd): an `IViewerSystem` from `ViewerSystems`, listed in the façade's
  `systems` in submission order.
- New pass (night lights, weather): a `FramePass` case + `PassIo` entry in `FramePlan.stadium` (validated by tests)
  and its body in `FrameEncoder.encode`.
- New per-frame data: a `SceneFrame` field from `SceneBuilder`, and a named offset in `UniformLayout` (checked against
  `CameraUniform.wgsl`).

## As built (C2b — procedural stadium)

- `StadiumKit.fs` loads `StadiumKit/kit.json` (sha-checked), the kit mesh with separate opaque/blended index
  ranges per LOD, and the albedo/normal/ORM texture arrays with CPU mips. `StadiumGeometry.fs` is pure: it turns
  `StadiumIdentity` (reputation, league level, seed = home ClubId, club colours) into the generated stands, roof,
  ground, the instance table (3 `vec4` per instance: position+yaw, scale, tint) and seat clusters.
- `Systems/StadiumSystem.fs` uploads the kit once, rebuilds when the identity changes (the renderer resets the
  static shadow cache) and each frame emits per-cluster LOD batches.
- `Draw` gained `FirstIndex`/`FirstInstance`; `DrawLayer` gained `BlendedLayer` (premultiplied alpha, no depth
  write, `fsBlended`, drawn after the sky) and `ShadowOnlyLayer` (static shadow casters at low LOD).
- Scene bindings 13–15 are the kit texture arrays, 16 the instance storage buffer; kit vertices carry
  `SceneRoot.instanced`. The camera uniform carries `clubPrimary`/`clubSecondary` (468 floats).
- Identity flows from `ClubMatchIdentity` (now with `Reputation`, `LeagueLevel`) through
  `MatchPitchControl` → `SetStadium`; the harness pins it with `--viewer-stadium rep,level,seed`.

