# Match viewer: retained GPU rendering

## Goal and boundaries

Keep the current broadcast picture (geometry, anatomical animation, turf/PBR, HDR and 4x MSAA) while increasing presentation throughput and reducing CPU/GC and upload overhead. Simulation and sporting decisions stay on the deterministic Core worker. GPU utilization is not a success metric by itself: the useful metrics are presented FPS, frame-work median/p95, allocations and uploaded bytes at a declared resolution.

This is the sole new rendering path, not a compatibility mode or a quality downgrade. No timer/duplicated-character/full-frame-HUD fallback is retained.

## Baseline, before implementation

2026-10-07, existing XWayland probe, 1160x790 window (1160x698 render surface), moving 22 players, 180 frames, 15 warmup frames, release of GPU work awaited for diagnostics only:

- Completed-frame work: median 8.236 ms, p95 9.127 ms, 165 samples.
- Probe timer: 30 Hz; production timer: 60 Hz. Neither counter demonstrates maximum GPU throughput.
- HUD: new full-surface SKBitmap + managed byte array + Marshal.Copy + QueueWriteTexture every frame (~3.24 MB/frame at this surface size).
- Character geometry: 22 identical mesh copies with root/atlas offsets baked into vertices.
- Skinning already executes on the GPU; animation interpolation produces a small palette on CPU. Moving that interpolation to compute is not justified by this baseline alone.

## Design

### 1. Retained HUD, explicit invalidation

`MatchHudRenderer` owns one bitmap/canvas for the surface lifetime. A pure invalidation planner tracks only visible HUD values, theme/language, integer match clock, stats, carrier and minimap positions. It computes bounded dirty rectangles from the same layout used to draw.

Unchanged widgets retain their pixels. Dirty regions are cleared and redrawn, including old carrier pixels when possession disappears. Minimap positions keep updating at render frequency; text is not throttled. A resize/theme/language change invalidates the whole image. QueueWriteTexture reads the persistent native bitmap directly with the bitmap row stride, uploads only changed regions and makes no managed image copy. Clipping unions all dirty regions so overlaps cannot double-blend. Bitmap and GPU texture are recreated together on resize and explicitly disposed.

### 2. GPU character instancing

The asset loader emits one canonical weighted mesh. One indexed draw has 22 instances; WGSL instance_index selects the root transform, joint palette and home/away atlas half. All existing poses, UVs, topology and visibility semantics remain. Non-character meshes use one instance. The runtime contract and tests are migrated, not duplicated behind an old interface.

### 3. Dedicated presentation owner (revised after measurement)

The initial RequestAnimationFrame-only experiment still reached only 44.3 presentations/s despite 6.6-ms work samples: doing rendering on the UI thread couples throughput to Avalonia animation scheduling. This evidence changes the design before the worker implementation.

`RenderWorker` owns renderer construction, all WebGPU calls, its private Xlib connection and disposal on one background thread. It samples the existing thread-safe presentation stream at render time, caching the two projected endpoints. UI `PresentationLoop` uses TopLevel.RequestAnimationFrame only to publish layout/visibility/configuration, not to render. A latest-value mailbox replaces stale layouts, never queues old frames. The worker sleeps on an event when no renderable input exists and joins before the parent window tears down. Failure unwinds native ownership before reporting.

Request the high-performance adapter. Prefer supported tear-free Mailbox presentation; FIFO on devices without Mailbox is the same renderer, not a legacy/quality fallback. Surface acquisition supplies swapchain backpressure. There is no fixed 30/60/26-FPS timer, no queue of outdated visual inputs, no GPU completion wait in production and no software frame limiter. Mailbox may submit more frames than the display shows; report render/present throughput honestly rather than claiming the monitor displays every frame.

`PresentationLoop` generation guards isolate attachments and pending callbacks. Worker lifetime and single native-thread ownership have dedicated tests. Diagnostic pacing and GPU waits exist only as explicitly selected probe controls, not runtime alternatives.

### 4. Repeatable diagnostics

The probe reports completed-frame CPU+GPU-wait work separately from submission CPU, actual presentation cadence, managed allocations and HUD upload bytes. Timed samples exclude warmup and screenshot pauses. Optional diagnostic GPU waits must not enter the production path. Before/after comparisons use identical workload/resolution/pacing; reported FPS is not inferred from a CPU timer or reciprocal GPU-wait sample.

## Validation gates

Build Client after each implementation gate; run relevant Expecto tests. Add tests for dirty-region invalidation (stable HUD, clock, carrier loss, stats, minimap, theme/resize) and the single-mesh/instance/atlas contract. Inspect real native screenshots, including both teams, thin painted markings, text and minimap. Run moving benchmark, resize/reconfigure and attach/detach probes with WebGPU validation enabled. Run full Client tests and git diff --check. Keep shared worktree changes intact.

Success: materially lower HUD upload/allocation volume, exactly 1/22 character geometry storage, stable appearance and measured lower frame-work median/p95. Do not promise a particular FPS on other GPUs or claim GPU timestamp measurements without implementing them. Further compute animation, visibility/LOD, indirect drawing and native platform expansion require their own measured bottlenecks; they are not speculative dependencies of this design.

## Implemented measurements

See `docs/evidence/MATCHVIEWER_RENDER_PERFORMANCE.md` for commands, measurements and native evidence. The old full-frame HUD, replicated character vertices and render timers were removed; there is no compatibility branch retaining them.
