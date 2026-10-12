# Execution randomness

## Contract

Determinism belongs to a **match execution**, not to a career save or calendar date.
Reloading the same pre-advance save and advancing again intentionally starts a new
attempt. This supports save scumming; it does not promise a different scoreline.

- New career generation samples OS entropy in the client. `WorldGen` still accepts
  an explicit seed and remains reproducible for tools and tests.
- `ExecutionRandomness.beginAttempt` is called when accepting a new advance,
  season simulation, or user-match execution. The accepted seed is put in the
  application state before its worker starts.
- Core derives each fixture's stream from the accepted execution seed and fixture
  identity. Enumeration order, thread count and worker slices do not sample entropy.
- Duplicate clicks, no-op advances and advances during an active match do not
  replace the current seed. Pause/resume and checkpoint restoration keep the
  accepted `MatchInput` and RNG frontier.
- AI workers block overlapping advances while active; same-day results from a
  different execution seed or revision cannot overwrite the accepted attempt.
- Completed fixtures retain their actual seeds in `GameState.MatchSeeds`.
- User-match recordings retain the original resolved `MatchInput` and accepted
  `MatchCommand` journal in `WorldClock.MatchExecutions`. These are also recorded
  for scheduler-driven matches and season simulations, and retained at season
  rollover.
- Replay cannot rebuild football inputs from current players, fitness, tactics,
  scouting memories or save settings. It uses the archived input and journal;
  missing recordings are explicit errors, not invented matches. Commands are
  validated at their recorded effective frontier so a later substitution can
  remove someone introduced by an earlier one. Runtime completion checks the
  reproduced event hash against the committed events.
- Commit validates the recorded input against both the worker's input hash and
  the currently accepted fixture input. A result from another seed cannot commit
  merely because its date and world revision happen to match.

## Persistence

Career schema is **8**. Earlier schemas are rejected through the existing strict
save boundary. There is no seed default for older saves and no migration path.
The existing snapshot checksum covers the execution seed, completed seed ledger,
resolved match inputs and managerial journals. Immutable user-match recordings use
individual `match-execution` chunks, rather than being repeatedly serialized in the
daily root payload; the chunk cache and hash-based writes reuse historical data.

Seeds are 64-bit values sampled from the OS cryptographic generator, not timestamps
or a process-global counter. Different attempts are statistically independent;
neither entropy uniqueness nor different sporting results is a mathematical
guarantee. The engine itself never calls that generator.

## Regression coverage

- Identical explicit generation inputs produce identical worlds.
- Different execution seeds change a pending match's input identity without
  changing its resolved football context.
- Fixture stream derivation is independent of scheduling order and separates
  10,000 selected execution seeds.
- A completed recording replays identical sporting events and score after the
  career date, seed, players and staff have changed, including a managerial action.
- Reloading one pre-advance SQLite save produces fresh application attempts;
  duplicate clicks, no-op advances and an active runtime retain their seed.
- SQLite round-trip preserves the full resolved match input hash and journal,
  and the restored command is accepted by a new deterministic session.
- Existing checkpoint, chunk-size, save/load continuation and long-career suites
  remain the gates for session determinism and career persistence.

## Validation

Final Release suites: **591/591 Core**, **161/161 Client**. Both were rebuilt and
run through their executable Expecto runners. `git diff --check` passed, and the
new entropy-boundary module passes the Fantomas formatting check. Existing
compiler warnings remain outside this change.
