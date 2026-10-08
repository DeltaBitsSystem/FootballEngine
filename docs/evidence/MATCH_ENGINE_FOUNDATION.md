# Foundation contract — current engine

This records current authority for G0/G1, not completion of the long-horizon plan.

## Coordinates and clocks

- Metres in world space; X along the pitch, Y lateral, Z vertical. Home attacks +X, Away −X. Team-local mirrored coordinates are derived caches, not another sporting world.
- Default fixed clock: 40 subticks/second (25 ms). `State.Tick` is simulation time; `State.Clock.Tick` is play time, frozen during pauses. Rendering uses neither FPS nor wall-clock deadlines to choose football actions.
- `MatchSession.advanceTo` is the single stepping boundary. Fast, live and replay share authoritative systems; presentation policy chooses publication schedules only.
- Boundary contracts live in `Contracts/MatchBoundary.fs`; job identity, the runtime and result projection live in `Simulator/MatchJob.fs`, `Simulator/MatchSession.fs` and `Simulator/MatchAdapter.fs`. Jobs and sessions snapshot caller-owned slot/participant/window arrays before execution; mutating the supplied input cannot change an accepted match.

## Ownership and temporal visibility

| State | Owner |
|---|---|
| Score, structural events, discipline, participation, restarts | `EventApply` reducer |
| Ball integration and impulses | `BallPhysics`; sporting proposals from action/contact/outcome systems |
| Action proposal | `PlayerDecision` → `TeamWorld.PendingActions`; consumed by `Actions` |
| Movement commitment | player/team movement systems → `IntentFrame` and `RunLookup` |
| Review evidence | `RefereeVAR` → `World.Reviews` |
| Match RNG, commands, frontier | `MatchSession` |
| Diagnostic retention | single simulation owner → `ObservationBuffer`; snapshots/deltas cross worker boundary |
| Visual replay, interpolation | projection/Client, never authority for contacts or score |

At tick entry the reducer drains the pending event prefix and builds flags/windows. Flow/administrative events are proposed next. Scheduled systems run in `SystemsTable.fs` order. Direct world writes are visible to later systems in that tick; newly queued events generally await the next entry drain. Certain explicit administrative paths apply immediately. Do not reorder this pipeline casually: it changes visibility, RNG consumption and outcomes.

Current order: body physics → ball physics → contact → outcome → possession → team perception/decision/position/emergence → player perception/intent/steering → restart controller → player decision → actions → fouls/cards → manager. Triggers gate individual execution. Clock/period boundaries follow systems.

## Determinism and safety

- One threaded sporting RNG; capture, UI, formatting and export consume none. Same input/seed/commands/system order must reproduce events, RNG and sporting state. Cross-runtime/architecture bit equality remains unproven, not a promised contract.
- `Simulator.runMatch` returns `Result<MatchOutcome, MatchContractError>`; invalid inputs/live policies and terminal safety failures are errors, never fabricated terminal matches. No legacy adapter or ignored weights-file parameter.
- Configuration is resolved before entering the match. No mutable file reads in the simulator tick.
- Review evidence currently preserves float32 position precision from the original implementation. Typed storage does not turn the current shot-time heuristic into complete IFAB offside evidence.

## Observation and budgets

4096 retained occurrences; loss counters are explicit. Producer state is bounded. Live transport publishes only new entries since its last occurrence cursor; Client merges into the same bounded window. Absent/slow consumer cannot block capture or authoritative stepping. Full snapshots and JSON exports are explicit off-tick diagnostics, not per-tick logs.

Targets remain ≤2% total minimum-capture overhead, ≤5% p99 tick overhead and p99 below 25% of the fixed dt budget. See `MATCH_ENGINE_IMPLEMENTATION.md` for measurements and uncertainty. Full-copy transport was measured around +8% and replaced by deltas. Current short-run measurements vary around the 2% capture target; broader corpus/soak certification is still pending.

## Remaining foundation obligations

Complete control/rules contracts, article-level IFAB matrix, UI/GC/soak measurements and ownership audit. Flags now use explicit assignments and a runtime-exhaustive aggregate-mask test. The reproducibility archive includes input/config/commands/checkpoint/evidence and has been restored from a real UI export. A small paired full-match corpus supports the minimum capture budget; production certification remains separate. Neither typed action storage nor successful tests closes the remaining obligations by itself.
