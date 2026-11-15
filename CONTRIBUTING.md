# Contributing

Thanks for your interest. This guide explains how the game is built and why, so you can find where a change belongs before you write it. FootballEngine is in active development and the design moves fast, so open an issue before starting anything large.

[AGENTS.md](AGENTS.md) holds the complete rules. Design notes in `docs/` are mostly in Spanish.

## Setup

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json` pins the version).

```bash
git clone https://github.com/<your-username>/FootballEngine.git
cd FootballEngine
dotnet build
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release
```

## The idea behind the code

The goal is a football world where results are consequences, not inputs. A 2 to 1 must have real causes you can trace: a tired full back, a press that arrived late, a rival manager who read your shape. Every architectural rule below exists to protect that.

* **Determinism.** Same input, same seed, same commands, same match, bit for bit. That makes any result replayable, any bug reproducible and any tuning change measurable.
* **Causality you can see.** If something happens and emits no event, it does not exist for the viewer, the replay or the tests.
* **One obvious home for every behaviour.** If someone wants the team to press better in transition, there is exactly one place where that lives.
* **The compiler enforces the design.** Change a contract and let the compiler list everything that must follow.

## Projects

| Project | Role |
|---|---|
| `FootballEngine.Core` | Domain, game data, the world engine and the match engine. No UI and no I/O beyond explicit adapters. |
| `FootballEngine.Client` | Avalonia and FuncUI desktop app: MVU state, SQLite persistence, runtime workers and the WebGPU match viewer. |
| `FootballEngine.Core.Tests` | Expecto suite for domain, world, match laws and causal behaviour. |
| `FootballEngine.Client.Tests` | Persistence, MVU, runtime workers, presentation, animation and asset contracts. |
| `Training` | Calibration CLI: `validate`, `sweep`, `profile`, `hash`, `diagnose`. |
| `FootballEngine.Benchmarks` | BenchmarkDotNet micro benchmarks. |
| `Data/Builtins` | The built in world, shipped as mods in the same format players use. |
| `Tools` | Probes and authoring tools: mod CLI, diagnostics, Blender pipelines. |

Core never references Client. The match engine knows nothing about careers, and the career knows nothing about rendering. Each layer talks to the next through plain data.

## The match engine

`FootballEngine.Core/Match/`

A match is a simulation of 22 bodies and 22 minds at 40 ticks per second, in metres, with the home side attacking +X.

```
Contracts/   types only: World, frames, MatchState, MatchEvent, BalanceConfig, Rng, Observation
Config/      validation and the effective config resolved before kickoff
Stepper/     Scheduler, SystemsTable, Stepper, EventApply, cognition thread
Systems/     Ball/, Players/, Team/, Referee/, Manager/: one module per system
Restart/     restart planning and the laws a restart must satisfy
Simulator/   MatchFactory, MatchSession, Simulator.runMatch, replay projection
```

### One tick

1. `EventApply` reduces the events buffered last tick into `MatchState` (score, discipline, restarts, participation) and turns their kinds into trigger flags.
2. The stepper walks `SystemsTable.fs` in order. Each system runs only if one of its triggers fires: an event kind from the last tick, a change in the shape of play, or a cadence as a fallback.
3. A system is a `SystemStep`: it reads `World`, writes only the state it owns and appends `MatchEvent`s. Each system draws from its own deterministic RNG stream.

Systems never call each other. They communicate through `World` and events. A player decision becomes a proposal in `TeamWorld.PendingActions` that the `Actions` system consumes; VAR evidence goes to `World.Reviews`. This keeps update order explicit: reordering `SystemsTable.fs` changes what each system can see, how the RNG is consumed and therefore the outcome.

### Who decides what

* **Team systems** (`Systems/Team`) perceive the game as a unit, choose the collective plan and set positions: shape, pressing, transitions, the defensive line, attacking patterns. They frame the choices players make.
* **Player systems** (`Systems/Players`) decide within that frame: perception, intent, steering, the decision itself and the reflexes. The ball carrier runs a planner that searches chains of up to four actions before passing, carrying or shooting.
* **Ball systems** (`Systems/Ball`) integrate flight, spin, bounce, drag and foot contact. Outcomes and possession come from physics, not from a roll.
* **Referee and manager systems** apply the laws and let the rival manager react to goals, cards and the break.

Fatigue reaches every layer: an exhausted player perceives worse, decides worse and chooses safer actions, not only runs slower.

### Determinism and threads

`MatchSession.advanceTo` is the only way to move a match forward. Fast simulation, live play and replay share the same systems; only what gets published differs. A watched match can run its cognition on a second thread beside the body physics, and the result is identical either way. Capture, UI and formatting never draw from the sporting RNG. Configuration is resolved before kickoff, so nothing reads files inside a tick.

`docs/evidence/MATCH_ENGINE_FOUNDATION.md` records state ownership, the tick order and the determinism contract in detail.

## The world engine

`FootballEngine.Core/World/`

The world advances one day at a time through fixed phases in `WorldScheduler`:

1. Extract the work due today and apply records that take effect.
2. Prepare the day. Training runs once a week.
3. Resolve fixtures. Independent matches run in disjoint batches. The scheduler stops before your match so you can play it.
4. Confirm results and settle financial obligations.
5. Resolve decisions: club, manager and player **agents** act (signings, sackings, contracts), then the **pulse** writes stories, pressure and the job market from what really happened.
6. Close the day.

One fixed order means one source of truth: a story can only describe a transfer that already happened, and a result is never applied twice. `WorldGeneration/` builds new worlds; `Domain/` holds the career types (clubs, players, finance, market, scouting, training).

Execution randomness belongs to a match attempt, not to a save. Reloading and advancing again starts a new attempt with a new seed; tools and tests pass explicit seeds. See `docs/evidence/EXECUTION_RANDOMNESS.md`.

## Game data and mods

`FootballEngine.Core/GameData/`

Countries, leagues, cups, clubs, kits and squads load from mod packages. The built in world in `Data/Builtins` uses the same format, so the mod editor is not a side feature: it edits exactly what the game plays. Mods are resolved and validated into a locked snapshot when a career starts, and that snapshot is saved with the career.

## The client

`FootballEngine.Client/`

* **MVU.** `AppState/Types.fs` holds the state, `Msg.fs` the messages, `AppState/Slices` the per page state and the `Update*` modules the transitions. Views in `Views/Pages` and `Views/Components` are pure functions of state.
* **Workers.** Career advances and matches run off the UI thread. `AppState/MatchRuntime.fs` owns the live match: it steps the session, applies your commands (substitutions, instructions, pause, speed) and publishes poses and events. The UI is woken once per batch so fast playback never floods it.
* **Viewer.** `Match3D/` renders with WebGPU on its own render thread; `Animation/` turns simulated motion into character poses. Rendering never decides a sporting outcome: it only draws what the engine published.
* **Persistence.** One SQLite database per save slot (`Database/`). Snapshots and chunks are hashed and checked before loading. A save whose balance no longer matches the running engine is rejected explicitly, never silently changed. See `docs/PERSISTENCE.md`.
* **Localisation.** English and Spanish. User facing text goes through `Localization`, never as a hardcoded string.

## Tests

Tests are Expecto executables, not VSTest, so `dotnet test` is not the command.

```bash
dotnet fsi scripts/test.fsx                 # the full gate
dotnet fsi scripts/test.fsx --fast          # quick loop, skips full matches and seasons
dotnet fsi scripts/test.fsx --project Core --list
dotnet fsi scripts/test.fsx --project Core --filter "<full name from --list>"
dotnet fsi scripts/test.fsx --project Client
```

The runner fails on zero executed cases, skipped cases or missing reports, so a typo in a filter cannot pass. Reports land in `artifacts/tests/<run-id>/`. A new test file must be added to the project's compile order and its suite to `Program.fs`.

What a good test looks like here:

* It proves a behaviour, not that a type exists. Say the valid start, the action, the observable result and the effect that must not happen.
* It would fail on a wrong implementation. Show it once with a deliberately broken version.
* It uses the smallest scenario that exposes the defect. Full matches and seasons are for integration and corpus checks.
* It never weakens acceptance to go green: no widened tolerances, rerolled seeds or deleted cases without a contract based reason.
* Football validity is measured on corpora of many matches, never by forcing one seeded match to follow a script.

`docs/TESTING.md` covers the runner in detail.

## Measuring the engine

```bash
dotnet fsi scripts/dev.fsx check     # build everything and run the fast tier
dotnet fsi scripts/dev.fsx profile   # time and allocation per system for one match
dotnet fsi scripts/dev.fsx perf      # the same profile held to perf/budget.txt
dotnet fsi scripts/dev.fsx hash [n]  # event hash over n tuning seeds
```

A pure refactor must not change the hash. A behaviour change will, and should be explained in the PR along with a calibration run (`Training validate`) when it moves match statistics.

## Rules that matter

* **Every behaviour number lives in `BalanceConfig`,** with a name that says what it does and a one line reason. Systems read it from `ctx.Config`, never from the defaults.
* **No `failwith` or `Unchecked.defaultof` in runtime code.** Use `Result` and `Option`; a crash breaks a deterministic simulation.
* **No `printfn` for logging in the game.** Use the app's notifications and log messages.
* **Comments explain why, never what.** A constant, a guard or an order earns a comment when its reason is not visible.
* **Format with Fantomas.** `dotnet fantomas .` The pre-commit hook checks it.

## Adding things

**A match event.** Add the case to `EventData` and its `EventKind` in `Match/Contracts/MatchEvent.fs` and let the compiler list every match to handle. Reduce it in `Stepper/EventApply.fs` if it changes state. Add its kind to the triggers of every system that must react.

**A match system.** Create the module in the right `Systems/` folder with `let step: SystemStep`. Register it in `SystemsTable.fs` at the position its inputs require, with its triggers. Keep the state it owns in `World`; nobody else writes it. Add the file to the `.fsproj` in compile order.

**A balance value.** Add the field and a default with its reason in `Contracts/BalanceConfig.fs`, then validate it in `Config/ConfigValidation.fs`.

**A UI page.** Add the route in `Routing/Route.fs`, the view in `Views/Pages`, a slice if it needs state, and check it with the screenshot tool (`--shots`) in both languages.

## Pull requests

* One focused change per PR, with [Conventional Commits](https://www.conventionalcommits.org/) messages.
* Say what changed, why, and how you validated it: commands, seeds, and measurements when relevant.
* New behaviour comes with a test that fails without it.
* Backward compatibility is not required while the game is in development. Replace obsolete contracts cleanly instead of adding shims, and never delete user data silently.

## Bugs

Open an issue with steps to reproduce, what you expected, what happened, your OS and a screenshot if it helps. Include the match seed when the bug is in a match: it makes the match reproducible.

## License

By contributing you agree your work is released under [GPL 3.0](LICENSE).
