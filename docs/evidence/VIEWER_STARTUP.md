# Viewer startup and capture readiness

The match control no longer decodes `player.glb`, textures and clips on the UI
thread before starting its render worker. Its motion evaluator uses the renderer's
already validated `CharacterRig`, initialized on the render thread. There is one
character decode per renderer, not one for presentation plus another for rendering.

Independent stadium and character CPU loads run concurrently. GPU/native resource
creation and ownership remain on the render worker. This is not a global cache.

`IPitchPresentationSource.FirstPresented` signals the first successful presentation
after GPU completion. Errors fault the signal; detach cancels it; attachment
creates a new signal. Only the first frame waits for GPU completion for readiness.
Normal subsequent frames retain their existing asynchronous presentation behavior.

Live screenshot/video capture waits for control attachment and this signal under
one 15-second deadline. Attachment is observed through layout events, not a sleep
loop. The handler is removed even on timeout. Existing optional UI settling remains
available, but is no longer proof of native pitch readiness.

## Actual application verification

Same new human package, Release, 1600x900 window, deterministic seed 31871:

- Single decode but sequential assets: renderer first present 2560.8 ms.
- Concurrent assets: first present 1935.2 ms; repeat 1941.7 ms.
- These are individual local observations, not cold-start distributions, complete
  navigation timings or performance guarantees. The removed UI decode is outside
  the renderer's stopwatch; no total-navigation speedup is claimed from these numbers.
- Character decode remains about 1.8 seconds and is the principal remaining cost.

Native captures with `--shots-settle 200` and `--shots-settle 0` were opened and
show the actual pitch and both teams. Paths:

- `/tmp/opencode/viewer-ready-gpu/00_Match (1, Live).png`
- `/tmp/opencode/viewer-ready-zero-settle/00_Match (1, Live).png`

A prior attempt signaling immediately after SurfacePresent produced black output;
waiting for first GPU completion fixed the observed race. This evidence does not
guarantee compositor behavior on every backend; native capture currently requires X11.

Release client builds and all 191 client tests pass after these changes.
`git diff --check` passes. Existing warnings remain separate from errors.

## Remaining work

Pitch appearance is not yet instantaneous. Prepare immutable validated CPU data
before match entry or author a bounded, hash-validated precooked runtime format
to avoid repeated glTF/PNG decode. Measure cold/warm startup and validate reentry,
resize, failure and cancellation. Do not weaken integrity checks or hide the
remaining load time by increasing screenshot delays.
