# Original human runtime base

The live `Assets/MatchViewer/player*` package now comes from
`PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base/`. Its README describes
the editable source, original authoring, reconstruction commands and limitations.
The selected surface is the evaluated Landmark-Cage revision, not the old player.

The source rig was conformed to the existing physical proxy, without changing Core
or relaxing `RigDefinition.conform`: thigh 0.440255612 m and shin 0.435258448 m
at the reference stature, on both sides. Rest geometry was preserved; inverse
binds and the 18 original procedural clips were regenerated. The pack has 60 joints.

The animation regression now checks segment rigidity against the authored bind
lengths rather than the previous model's hardcoded arm dimensions. A 0.1 mm
absolute tolerance accommodates exported translation-curve precision.

Validation after promotion:

- Client test project builds; CharacterAnimation and ProceduralMotion suites pass.
- After promotion, solution and separately built Benchmarks builds pass; existing
  compiler warnings are not treated as errors. `git diff --check` is clean.
- Full client suite passes (log `/tmp/opencode/human-integrated-all-client-tests.log`).
- Two 150-frame moving Release WebGPU diagnostic runs completed; native captures
  at frames 55 and 100 were opened and inspected, using the close-up camera.
- Captures: `/tmp/opencode/human-integrated-native-phase55.png` and
  `/tmp/opencode/human-integrated-native.png`.
- The actual application's deterministic live-match capture was also opened:
  `/tmp/opencode/human-integrated-app/00_Match (1, Live).png`; the field and
  both teams are visible at kickoff. This short sample does not certify a full match.

These are actual renderer diagnostics, not Blender images or a complete live-match
acceptance test. Clothes, anatomy and motion remain visibly provisional. All
contacts, collisions, transitions and production performance are not certified.
The prior live package was preserved in `/tmp/opencode/player-before-runtime-base/`;
this temporary copy is not a version-control checkpoint.

## Next priorities

1. Finish startup preparation. Duplicate loading and first-present readiness were
   subsequently addressed (see `VIEWER_STARTUP.md`), but immediate pitch appearance
   is still not achieved or certified.
2. Validate moving live-match sequences and foot/ball contact before more facial
   refinement. The close-up diagnostic reveals provisional posture and clothing.
3. Improve one complete player in context, then evaluate LOD and frame times at
   1080p on the target hardware. A diagnostic FPS overlay is not a benchmark.

No commit or push was made. Unrelated shared changes remain untouched.
