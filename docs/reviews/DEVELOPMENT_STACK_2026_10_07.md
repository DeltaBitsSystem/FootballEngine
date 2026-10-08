# Development stack review — 2026-10-07

## Scope and status

This stack groups the entire previously uncommitted worktree without resetting, discarding or squashing existing history. It is **development work**, not a release declaration. Full procedural AAA animation remains a future design/implementation task; the current viewer uses authored clips plus contact IK. No issue or pull request was supplied or opened for this cleanup.

Review base: `5ab03fd`. Branch: `rewrite-match-engine`. Nothing has been pushed by this cleanup.

## Commit order

| Commit | Responsibility | Review focus |
|---|---|---|
| `94e6fad` | Save lifecycle | Captured slot paths, UI-independent continuations, graceful-shutdown wait and failure handling; persistence regression tests. |
| `6013b15` | Formatting only | Fantomas normalization of previously unchanged files. Review separately from behavior. |
| `f25c319` | Core contract and simulation | Match session/job/adapter extraction, causal evidence, typed action/VAR storage, effective tactics, tie resolution, body motion, delayed ball contacts and replay v10. Includes Core tests and training/benchmark migration. |
| `3c6af29` | Player asset corpus | Pinned CC0 anatomy, legal text, generated GLB/atlases/motion manifest and offline authoring sources. Large OBJ/target/weights diffs are vendored data, not handwritten runtime logic. |
| `260dbb1` | Surface and lighting assets | Raw PBR sources, mip-chain binaries, manifests and offline generators. Inspect provenance/hashes rather than binary diffs. |
| `9505ae3` | Diagnostic consumers | Bounded worker requests, immutable archives, causal query state, inspector and headless CLI with regression tests. |
| `45dca6f` | Viewer integration | Replay migration, contact/spin interpolation, clip/IK sampling, instancing, owning render thread, retained HUD, PBR/HDR/MSAA and match-day layout. Includes client tests and native probe. |
| `018370f` | Diagnostic probes | Opt-in observation overhead and restart readiness scripts. |
| Final documentation commit | Review and evidence | Historical corpus, screenshots, architecture guidance, plans and this review guide. |

The Core and viewer commits are intentionally larger: their shared state/project/replay contracts make arbitrary file-sized splits misleading. This is an **ordered stack**: the Core breaking contract requires the later Client migration, and the diagnostic view is wired into the later viewer layout. Do not cherry-pick intermediate commits as standalone full-solution upgrades. Existing source files that contain functional changes are formatted within their owning commit; baseline-only formatting is isolated.

### Core review slices

1. Boundary: `MatchBoundary.fs` → `Simulator/MatchJob.fs`, `MatchSession.fs`, `MatchAdapter.fs`; command/input ownership, checkpoint bounds and Result errors.
2. Evidence: `Contracts/Observation.fs`, `World.fs`, typed action/review storage and event/reducer associations; observation must not consume RNG or determine results.
3. Motion: `Systems/Ball/Dynamics.fs`, `FootContact.fs`, possession/reception, scheduler order, body motion and replay v10; tests accompany each changed contract.

### Viewer review slices

1. Authoritative projection/interpolation and rigid-leg contact sampling.
2. GPU/resource ownership and attach/detach publication: `RenderWorker`, `PresentationLoop`, `MatchPitchControl`.
3. Character instancing, texture integrity, surface/HDR shaders and output colour handling.
4. Retained HUD invalidation, camera/layout controls and diagnostic inspector wiring.

## Packaging correction

The deny-all `.gitignore` previously omitted `player.glb`, `player-motion.json` and several anatomical source/licence files. A local build could therefore pass while a pushed checkout lacked essential assets. These exact inputs are now allowlisted and committed. Blender `.blend1` backups, caches and build outputs remain ignored. No new individual asset exceeds GitHub's 100-MB file limit; the largest is the approximately 30-MB player GLB. Asset binaries are isolated from runtime logic for review.

Pinned anatomical/model/texture SHA-256s were verified, and new JSON/Python files were parsed. A tracked-files-only export is used to check that the final build does not depend on ignored local inputs.

## Validation

Post-format validation: **545/545 Core tests and 139/139 Client tests passed**, with no ignored or errored tests. The solution, Benchmarks and native renderer probe build successfully. Fantomas and `git diff --check` pass; `git fsck --no-reflogs` reports no integrity failure.

A `git archive` export of the committed implementation (no ignored assets or existing `bin/obj`) also restored and built the solution, then passed **139/139 Client tests**, including asset-loading and integrity checks. This is a tracked-input reproducibility check, not an offline NuGet restore or a deployment test. Existing build warnings remain.

Earlier evidence documents retain historical measurements; they are not current release certification. No new performance benchmark or native visual-quality claim was made by this Git cleanup.

```bash
dotnet build
dotnet run --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj
dotnet run --project FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj
dotnet build FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj
dotnet fantomas . --check
git diff --check
```

## Known development boundaries

- Stadium distribution rights remain **unattested/development-only**, as stated in the asset licence file. Preparing a Git push does not certify distribution rights or a release.
- The repository still needs project-level legal confirmation for distribution; no root licence was invented by this cleanup.
- IFAB coverage and the long-horizon engine plan are incomplete. Evidence links and passing tests do not certify legal completeness or football realism.
- Animation uses reachable-effector proxies and bounded IK, not mesh collision, full anatomical limits or a completed procedural locomotion system.
- Existing warnings are not silently promoted to “warning-free”; historic performance figures are local observations, not new measurements from this cleanup.
- Save shutdown protection covers already-started graceful writes, not forced termination or power loss.

## Push

After review, push the branch explicitly (no force push):

```bash
git push -u origin rewrite-match-engine
```

No remote branches, tracking configuration or existing commits were rewritten during cleanup.
