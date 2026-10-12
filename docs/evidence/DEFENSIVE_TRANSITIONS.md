# Defensive transitions — evidence

## Initial causal path (before correction)

`BallContact/BallPossession/Actions` propose possession; `EventApply.applyPossession` commits control and `Possession.LastGainer`. Free/contested flight retains that last gainer. `TeamPerceive` (4 ticks) interprets it, but initializes `TransitionSince=0` and renews it on **phase** changes after 60 ticks, not on possession facts. The 240-tick window can therefore expire → refresh its own start → invent another transition with unchanged possession. A loss outside the window first becomes Defending and only later TransitionDefend.

`TeamDecide` (one minute) chooses a strategic press directive. `TeamPosition` independently chooses the loose-ball interceptor, nearest cover and two deepest markers on every 4-tick pass, also against controlled possession. `PlayerIntent` then always presses the carrier with that first defender, computes cover from fixed offsets and contains another pressing policy. No local viability/retreat decision. Roles are consumed by Intent → Steering → movement; neither roles nor pending responsibilities are cleared for departed slots. Keeper has a separate shape target and is excluded from normal interceptor selection (currently by slot zero assumption).

Scheduling: queued facts commit on the next entry drain; direct writes are visible in the same tick. Keep this contract. Checkpoints currently replay input + commands and verify event/RNG hashes; they do not contain an explicit transition payload.

## Verification log

The three initial causal fixtures failed before production changes. The completed implementation has 32 transition tests covering factual loss/recovery, contested possession, restarts, fatigue, local support, depth protection, handoffs, availability, mirrored geometry, boundary contact, stabilization, configured cadence, retention and deterministic continuation.

## Implemented contract

- G1: `EventApply` starts an episode only on a committed live change of controlling side. Disputes and same-side touches do not restart it. Recovery and interruptions terminate it. Checkpoint format **4** includes copied episode/task payloads and verifies them after replay; format 3 is rejected rather than guessed.
- G2: `TeamTransition` owns the immediate counterpress/retreat choice. Arrival uses existing acceleration, velocity and fatigue limits; local numbers, cover, depth and effective tactics govern viability. Missing eligible primary/cover overrides commitment. Retreat cannot restart counterpressure without another factual loss.
- G3: `TeamPosition` assigns primary, cover, outlet screens, depth protection and regrouping targets. `PlayerIntent` consumes them; existing Steering integrates movement at its original cadence. `Regroup` does not receive loose-ball recovery intent bonuses. Departures invalidate tasks, runs and proposals. The goalkeeper retains normal goal positioning. Team-local lineup bases are mirrored only once.
- G4: significant loss, mode, primary/cover handoff and termination changes emit semantic events. Typed bounded observations retain loss ancestry and measured rationale; unchanged geometry produces no repeated capture. Sporting RNG and possession outcomes are not awarded by the tactical owner.
- G5: real-transition checkpoint continuation and full-match Fast/Live/Replay comparisons pass with different chunks/publication intervals. Repeated episodes saturate the observation buffer without accumulating task arrays.

## Final validation — 2026-10-11

Both complete suites were rebuilt and rerun after resuming the session:

```bash
dotnet run -c Release --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj
dotnet run -c Release --project FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj
```

**587/587 Core tests; 159/159 Client tests passed.** The previous final Client invocation exited unsuccessfully, but its temporary log was unavailable after resumption. The failure did not reproduce in this run; its cause is not established.

No Client animation, assets or rendering were edited for this feature. Existing procedural-motion work was preserved. No agents, worktrees, issues or PRs were created.

## Paired football corpus

Raw results: [paired-corpus.csv](defensive-transitions/paired-corpus.csv). The same probe source ran against preserved baseline and changed Release binaries: two sides × compact/open geometry × condition 1.0/0.3 × seeds 42/112/941, over 320 subticks. A real loss is queued on an off-cadence entry; response is the first defensive intent publication, not the first later recovery. All repeated runs produced identical corpus rows.

Response changed from **3 ticks to 0** in all 24 cases. The compact fresh block selects one primary and one cover; the open/exhausted cases initially assign no chaser and retain two depth protectors. Dedicated fixtures additionally prove two reachable outlet screens and real movement consumption.

| Geometry / condition | Recoveries before → after (6 cases) | Mean rival progression, m | Mean forward players at end |
|---|---|---|---|
| Compact / fresh | 6 → 6 | 0.081 → 0.081 | 4.333 → 0.500 |
| Compact / exhausted | 6 → 6 | 0.081 → 0.081 | 2.333 → 1.333 |
| Open / fresh | 6 → 3 | 9.462 → 9.880 | 6.000 → 2.833 |
| Open / exhausted | 3 → 3 | 9.462 → 16.485 | 5.667 → 3.500 |

These results establish causal differentiation and a real attacking opportunity cost, **not universal tactical superiority**. The exhausted open block concedes more progression. The recorded threat value is a distance-based xG proxy, not actual shots or goals. The short corpus remains asymmetric in downstream outcomes; the dedicated mirrored fixture checks equal tactics and immediate responsibility/contact geometry, not equal whole-match outcomes.

## Performance and retention

Raw three-run measurements: [paired-performance.json](defensive-transitions/paired-performance.json). Linux x64, Ryzen 5 5600X, SDK 10.0.302/runtime 10.0.10. Each section uses 8,000 warmup calls and 20,000 measured calls; baseline/changed processes run sequentially. Frozen geometry isolates owner/coordination cost; full ticks follow different sporting trajectories and are not a pure overhead comparison.

The persisted JSON records the earlier three-run series: full-tick median p50 7.735 → 9.418 µs, p95 120.241 → 117.767 µs, p99 219.823 → 308.031 µs; allocation 5,551 → 5,523 bytes/tick. A subsequent final-code series, whose temporary files are no longer available, measured medians 8.005 → 10.470 µs, p95 122.234 → 119.028 µs, p99 350.072 → 288.113 µs, and 5,550 → 5,663 bytes/tick. Tail variation prevents a relative-speed improvement claim. Both series remain well below the 6.25 ms p99 budget (25% of the fixed 25 ms tick); they are not production soak certification.

Frozen tactical-owner allocation is 1,488 bytes/call, down from the preliminary 4,096 after avoiding captured frame copies and tuple comparisons. Frozen coordination is 17,328 bytes/call versus approximately 9 KB baseline: additional coordination has a measurable cost. The optimization preserved the corpus exactly. Owner invocation frequency was 0.2696/tick, including calls that have no active assessment.

Observation retention saturates at 4,096; frozen repeated evaluations retain three observations and four buffered events, with no per-call growth. Full managed retained deltas include event identities and other existing world state; negative frozen deltas are GC noise, not evidence of negative storage cost.

## Reproduction and baseline provenance

```bash
dotnet run -c Release --project Tools/DefensiveTransitions/DefensiveTransitions.fsproj
dotnet build Tools/DefensiveTransitions/Probe/PairedProbe.fsproj -c Release -p:ProbeDirectory=/absolute/path/to/baseline -o /absolute/path/to/probe-before
```

The probe's `ProbeDirectory` selects binary references; build the main tool first for the current default references. Baseline was captured from the shared dirty worktree at HEAD `5ca13161b25241011ef62543906c6ee8b4192d8e`, not from clean HEAD. Its Core DLL SHA-256 was `52511e81e90e85031c2228c7ba12a1fa3338d6225a43af708b6f76706c0f74a0`; fixture executable SHA-256 was `b36ca943209a026cb907187269740e8c39d1e6ecbce03af870c67fd4eff9430b`. Temporary baseline binaries are no longer available after resumption; archived CSV/JSON remain, but rerunning the exact historical baseline requires those binaries or reconstruction of that dirty source snapshot.
