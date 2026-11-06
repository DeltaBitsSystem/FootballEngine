# Player reaction: committed-event boundary

## Contract and method

`SystemsTable` retains its background periods and order. The new event masks
operate on the flags of events reduced at the start of a step, not on events
newly queued by systems. A produced event normally waits for the next drain;
the reaction starts in that drain. Body physics runs before steering, so it
cannot use that step's new steering until a subsequent integration. However,
`PlayerSteering` itself updates `PosX`/`PosY`, so steering can also move a
player in the reaction step. Neither
this boundary nor the measurements below describe biological reaction time,
ball contact windup, acceleration, presentation latency, or wall-clock input.

Reproduce with `dotnet build FootballEngine.Core/FootballEngine.Core.fsproj -c Release`
and `dotnet fsi Tools/MatchDiagnostics/ReactionProbe.fsx`. The probe wraps the
actual systems without drawing RNG. Its baseline schedule replaces only the
three triggers with their previous cadences on the current shared codebase;
both variants retain current contact, defense, reducer and pending-strike
behavior. It is a scheduling comparison, **not** a baseline for measuring
proposal invalidation or a reconstructed historical full engine.

## Scheduler latency

In a reachable, controlled-carrier fixture using the complete schedule, a
possession is applied at phases 0–7. The measured offsets below are subticks
from that application to the first execution or observable choice at Actions.
This is not a promise that a valid choice exists when earlier physics changes
control, or that executing a choice always immediately contacts the ball.

| Measure | Baseline schedule | Reactive schedule |
|---|---:|---:|
| Perception system | 0–7 | 0 |
| Intent system | 0 | 0 |
| Decision system | 0–3 | 0 |
| Valid choice present at Actions | 0–3 | 0 |
| Choice consumed by Actions | 0–3 | 0 |
| Pending physical strike request | 0–3 | 0 |

The 0–3 choice figures are specific to this fixture, not inferred from the
decision trigger alone. In this fixture each choice produces a pending strike;
other choices, such as a duel, need not produce a strike. The request is not
the later physical foot contact. In the no-event
case, background 8/4/2 periods remain unchanged. Tests additionally assert
actual refreshed pressure, cognition, blocked lane and carrier intent, and
the full scheduler's pre-Actions proposal boundary. The next-drain test
separately confirms a system-produced event is not consumed in its own step.

## Release timing sample before restart gate adjustment (historical)

Measured sequentially after warmup, on the same host, with no other benchmark
running. Values are indicative, not guarantees or a statistical benchmark.
Each kernel and fixed-session run has three seeds and 4,000 ticks per seed, injecting possession
once per eight ticks (1,500 total); newly produced events are deliberately
discarded so the event workload is identical, not a simulation of a match.
The fixed-session variant uses `MatchSession.advanceTo` for each tick with
the same injected stream and also discards generated events before the next
drain; this isolates session overhead under the same event frequency.
Each session run has seeds 1, 2, 3 and 10,000 ticks per seed with capture off.
The session event streams are allowed to evolve naturally and therefore their
event frequencies differ; its figures are not an equal-event microbenchmark.
Counts cover Perception, Intent, Steering, Decision and Actions together.

| Run | µs/tick | allocated bytes/tick | Calls | Applied events |
|---|---:|---:|---:|---:|
| Kernel baseline, two runs | 16.24 / 16.03 | 13,296.5 / 13,296.4 | 22,500 | 1,500 injected |
| Kernel reactive, two runs | 20.47 / 20.50 | 19,798.8 | 27,000 | 1,500 injected |
| Fixed session baseline | 17.39 / 17.26 | 17,853.5 | 22,500 | 1,500 injected |
| Fixed session reactive | 22.02 / 21.85 | 24,355.8 | 27,000 | 1,500 injected |
| Natural session baseline | 11.31 / 11.76 | 9,164.4 / 9,164.2 | 45,161 | 3,713 |
| Natural session reactive | 12.88 / 11.63 | 9,609.1 | 46,539 | 3,597 |

The synthetic event-heavy workload demonstrates a real cost; the naturally
evolving sessions also differ in outcomes and number of events. No claim of
equal match trajectories or production performance is made from these samples.

## Verification

`ReactionCoherenceTests` covers the full and isolated scheduler, all eight
phases, event/cadence coincidence, no-event cadence, committed versus queued
events, same-side owner change, duplicate possession, loose recovery,
two-event drain ordering, restart invalidation, red card/injury/substitution,
future versus effective instructions, pending physical strike and RNG, and
Live/Replay/capture/chunk/checkpoint comparisons. Existing contact and match
tests remain part of the full suite. Results of the final full validation are
recorded in the latest validation section below.

### Restart-to-Live ordering conflict and resolution

The earlier diagnostic demonstrated a committed possession drained while a restart is already
`Completed` at tick 4. `PlayerPerception` is skipped by `WhenLive` while the
flow is still `Restart`; later in the same scheduler pass `RestartController`
sets the flow to `Live`, and `PlayerDecision` executes with the prior cognitive
value. This is a reproduced exception to a universal fresh-perception claim.
Sol resolved this by allowing the existing perception trigger during movement-
allowed restarts. Decision remains `WhenLive`; no scheduler reordering or new
state was introduced. `RestartContact` causes the controller to queue
`RestartCompleted`; the next drain maps it to `SetPieceAwarded`, refreshing
perception before the controller enters Live. Tests exercise that real path
in all eight phases, including the queued-event boundary, and ensure that
halftime and goal pauses remain excluded. The existing time-based memory
decay also runs during restart perception, using the same simulation tick
interval as before; it is not a separate decay on each wake.

### Measurements after restart gate adjustment

Same probe, seeds, fixed event stream, warmup and sequential execution as
above. The baseline is still the three old triggers, including
`WhenLive` for perception, applied to the current shared code; this isolates
trigger differences, not all historical invalidation changes.

| Run | µs/tick (two runs) | bytes/tick | player calls | committed events |
|---|---:|---:|---:|---:|
| Fixed kernel baseline | 16.00 / 15.97 | 13,296.5 / 13,296.4 | 22,500 | 1,500 |
| Fixed kernel reactive | 20.34 / 20.41 | 19,798.8 | 27,000 | 1,500 |
| Fixed session baseline | 17.53 / 16.98 | 17,853.5 | 22,500 | 1,500 |
| Fixed session reactive | 21.51 / 21.37 | 24,355.8 | 27,000 | 1,500 |
| Natural session baseline | 12.84 / 11.24 | 9,164.3 / 9,164.2 | 45,161 | 3,713 |
| Natural session reactive | 12.03 / 12.53 | 9,609.1 | 47,453 | 3,597 |

The natural baseline had 5,749 restart ticks and zero perception calls during
restart; the reactive run had 5,449 restart ticks and 914 such calls. The
different trajectories preclude a controlled attribution of session time;
the fixed workloads expose the synthetic cost without hiding it. These samples
do not establish a production performance budget.

### Final validation

- Core: **672/672**; Client: **191/191**.
- Focused reaction: **21/21**; duel/contact: **14/14**; foot motion: **16/16**;
  observation: **24/24**.
- Solution, Client.Tests and Benchmarks builds: zero errors, respectively
  72, 5 and 0 warnings. `git diff --check` passed.
