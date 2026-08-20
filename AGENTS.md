# Guide for Contributors and AIs — FootballEngine

## What is FootballEngine

Offline deterministic football simulation built in F# + .NET 10 + Avalonia. The goal is a system where collective intelligence emerges: the team thinks as a unit, the manager shapes that unit with every decision, and results are consequences — not inputs. A 2–1 goal has real, chain-linked causes.

The project has two dimensions in permanent tension: **realism** (results with causes, tactics matter, fatigue matters) and **fun** (drama, meaningful surprises).

---

## Environment Setup

**Prerequisites:**
- .NET 10 SDK (version `10.0.300` per `global.json`)

**Clone and build:**
```bash
git clone <repo-url>
cd FootballEngine
dotnet build
```

**Run the application:**
```bash
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release
```

**Run the tests:**
```bash
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj
```

**Formatting (Fantomas):**
```bash
dotnet fantomas .
dotnet fantomas . --check   # check only, do not modify
```

---

## Project Architecture

### Solution Structure

The solution (`FootballEngine.slnx`) contains 3 main projects:

| Project | Type | Description |
|---|---|---|
| `FootballEngine.Client` | WinExe | Desktop application — domain, engine, UI |
| `FootballEngine.Tests` | Exe | Test suite with Expecto |
| `FootballEngine.Benchmarks` | Exe | Benchmarks with BenchmarkDotNet |

Additional directories:
- `Training/` — Weight calibration pipeline (separate project, not in .slnx)
- `Data/` — Game data (countries, clubs, etc.)
- `Tools/` — Auxiliary utilities

### Inside FootballEngine.Client

The client project has ~260 F# files organized in:

```
FootballEngine.Client/
├── Domain/              # Domain types: Player, Club, Match, GameState, etc.
├── GameData/            # Mod loader, JSON serialization, DataRegistry
├── Database/            # SQLite — Entities, Serializers, Repository
├── Engine/
│   ├── Match/           # The match engine (the heart of the project)
│   │   ├── Types/       # All types: BalanceConfig, SemanticEvent, SimState, etc.
│   │   ├── Core/        # SimState, ConfigValidation, EventRouter, MatchStepper
│   │   ├── Ball/        # BallAgent, BallPhysics, Interception
│   │   ├── Referee/     # RefereeAgent, FoulAnalysis, VAR
│   │   ├── Player/      # PlayerAgent (5 layers: Perception → Intent → Decision → Action → Steering)
│   │   ├── Manager/     # ManagerAgent, SubstitutionIntelligence
│   │   ├── TeamOrchestrator/ # Collective intelligence, EmergentLoops, Chemistry
│   │   ├── Systems/     # CognitionSystem, ActionSystem, AdaptiveSystem
│   │   └── Simulation/  # Simulator (runFast/runFull), Momentum, Snapshots
│   └── WorldGeneration/ # World generation: players, clubs, fixtures
├── AppState/            # UI state (MVU/Elmish): Types.fs, Slices, Update
├── Views/               # Avalonia components: Pages/, Components/
└── Program.fs           # Application entry point
```

### The 4 Layers of the Match Engine

```
┌─────────────────────────────────────────────────────┐
│  A — Foundational contract                          │
│  SimState · MatchContext · BalanceConfig            │
│  SemanticEvent · MatchFlow · SimulationClock        │
├─────────────────────────────────────────────────────┤
│  B — Simulator                                      │
│  runFast · runFull                                  │
├─────────────────────────────────────────────────────┤
│  C — Stepper                                        │
│  MatchStepper.updateOne · EventRouter               │
├─────────────────────────────────────────────────────┤
│  D — Agents                                         │
│  BallAgent · RefereeAgent · TeamOrchestrator        │
│  PlayerAgent · ManagerAgent                         │
└─────────────────────────────────────────────────────┘
```

Layers only communicate downward. B knows C, C knows D. No layer knows the one above it.

**Fixed order within each tick (Layer C):**
1. Physics — BallPhysics.update, MovementEngine
2. BallAgent — possession, bounds, trajectories, emits SemanticEvents
3. Drain events — SimStateOps.drainSemanticEvents
4. EventRouter — which agents run this tick
5. Agents — in the order the router determines
6. MatchClock — halftime, fulltime, stoppage

### MVU/Elmish for the UI

The UI uses **Avalonia.FuncUI** with the Elmish pattern:
- `AppState/Types.fs` — Defines global `State` and `Page` DU (`Loading | Setup | HomePage | Squad | Tactics | ...`)
- `AppState/Msg.fs` — System messages
- `AppState/Slices/` — Per-domain sub-states (SquadSlice, TacticsSlice, MatchSlice, etc.)
- `Views/Pages/` — One view per page
- `Views/Components/` — Reusable components (Sidebar, Header, MatchViewer, etc.)

---

## Architectural Principles (summary)

1. **Individual and collective intelligence feed each other.** `TeamOrchestrator` sets the framework, `PlayerAgent` decides within that framework. Accumulated individual decisions are what the orchestrator reads to adjust.

2. **Adding new intelligence must have an obvious home.** If someone wants the team to press better in transition, there must be exactly one place where that lives.

3. **Agents never call each other.** Communication goes through `SimState`. An agent reads, does its work, writes to state or emits events. It never calls another agent directly.

4. **No behavior number lives outside BalanceConfig.** If it affects how the game behaves, it lives in `BalanceConfig` with a name that explains what it does.

5. **The compiler enforces the design.** Change a signature and let the compiler show all the breakages is the correct way to evolve.

6. **Fatigue that only affects speed is not real fatigue.** An exhausted player perceives worse, decides with lower quality, and has more conservative intentions — besides running slower.

7. **If something doesn't emit an event, it doesn't exist for the viewer.** Before implementing new behavior: what event does it emit? If the answer is "none", it's invisible.

8. **Agents live in event time, not clock time.** Agents don't wake up because N ticks passed — they wake up because something relevant to them happened.

9. **Comments justify, they don't narrate.** Every comment must answer "why" — a branch, a constant, an order, a guard. "What" comments are deleted, not kept.

---

## Code Conventions

### F# style guide
- Follow the [official F# style guide](https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/)
- Use `camelCase` for parameters and local functions, `PascalCase` for types and discriminated unions
- Modules organized with `module` inside namespaces

### Naming conventions
- **Domain types:** `PlayerId`, `ClubId`, `CompetitionId` (wrapper types, not naked `int`)
- **Agents:** `{Name}Agent` (PlayerAgent, RefereeAgent, ManagerAgent, BallAgent)
- **Configuration:** `{Name}Config` inside `BalanceConfig` (DuelConfig, ShotConfig, etc.)
- **Tests:** `{Name}Tests` in modules separated by layer

### Module organization
- One file per main module
- Types go in `Engine/Match/Types/`, logic in the folder corresponding to the agent/system
- Compilation order matters: in the `.fsproj`, files are listed in dependency order

### Error handling
- Use `Result<'T, 'E>` for operations that can fail (see `FsToolkit.ErrorHandling`)
- `SimulationError` DU for simulator errors (`MissingLineup`, `IncompleteLineup`, `InvalidConfig`, etc.)
- **Never use `failwith` in runtime code** — only in benchmarks, tools, or temporary setup code
- **Never use `Unchecked.defaultof`** — use `Option` or explicit values

### Logging
- **Do not use `printfn` for logging in production** — only in benchmarks, statistics tests, and calibration tools
- Use `State.LogMessages` and `State.pushNotification` for user-facing information

### Comments
- **Comments explain WHY, never WHAT.** The code says what it does; a comment that restates the code is noise. A comment earns its place when it explains the reason a reader cannot see: *why this branch exists, why this value, why this order, what failure it prevents.*
- When a review flags a suspicious block, the first question is: *does this code look arbitrary because it lacks a why-comment, or is it actually wrong?* Every deliberate tuning constant, guard, or ordering decision gets a one-line why.
- If a comment says "what", rewrite it to "why" or delete it. A `why` comment may accompany a non-obvious `what` (e.g. the physics units), but the default is: no comment unless there is a reason.

---

## How to Add New Things

### New match event

1. Add the case to the `SemanticEvent` DU in `Engine/Match/Types/SemanticEvent.fs`
2. Add the mapping in the `SemanticEvent` module (same file)
3. Update `EventRouter.route` in `Engine/Match/Core/EventRouter.fs` to activate the correct agents
4. If the event needs effects on the referee, add logic in `RefereeAgent`
5. If it needs manager response, activate `RunManager` in the router

### New agent

1. Create file in the corresponding folder (e.g.: `Engine/Match/Manager/NewAgent.fs`)
2. Follow the pattern of `PlayerAgent.tick : AgentContext -> PlayerResult`
3. The agent reads from `SimState` (or a filtered view like `AgentContext`), emits `SemanticEvent`
4. Register activation in `EventRouter`
5. Add file to `.fsproj` in the correct position

### New UI page

1. Add case to the `Page` DU in `AppState/Types.fs`
2. Create view in `Views/Pages/NewPage.fs`
3. Add routing in `Program.fs` (match on `state.CurrentPage`)
4. Add slice if it needs its own state in `AppState/Slices/`
5. Add file to `.fsproj`

### New BalanceConfig field

1. Add field to the corresponding record in `Engine/Match/Types/BalanceConfig.fs`
2. Update `BalanceConfig.defaultConfig` with a reasonable default value
3. Add validation in `Engine/Match/Core/ConfigValidation.fs`
4. If it's an ML weight, add to the schema in `Engine/Match/ML/WeightsLoader.fs`
5. **Never access `BalanceConfig.defaultConfig` directly in agent code** — pass as a parameter or read from `SimState.Config`

---

## Test Structure

Tests use **Expecto** and are organized in 3 layers:

| Layer | Directory | What it tests |
|---|---|---|
| `Layer1_E2E/` | End-to-end tests | Full flows: state transitions, goal cycles, GK distribution, set pieces, match invariants |
| `Layer2_Systems/` | System tests | Output contracts, BallSystem, Chemistry, EmergentLoops, VAR, HandballDetector, statistics |
| `Layer3_EdgeCases/` | Edge cases | Shots, passes, GK, personality, mathematical pipelines, WeightsLoader |

**Test infrastructure:**
- `Infrastructure/Builders.fs` — Test data builders (`makePlayer`, `makeGk`, `eliteAttacker`, `worstAttacker`, etc.)
- `Infrastructure/Runners.fs` — Helpers to run simulations in tests
- `Infrastructure/Assertions.fs` — Custom assertions

**Run specific tests:**

Tests use **Expecto** (not xUnit/NUnit), so `dotnet test --filter` does NOT work. Use `dotnet run` instead:

```bash
# Step 1: List ALL test names (pipe through grep to remove warnings)
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj -- --list-tests 2>&1 | grep -v "warning\|NU1"

# Step 2: Filter by FULL test name prefix (dots as separators)
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "FootballEngine.MatchEngine.GoalCycle"
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "FootballEngine.MatchEngine.Diagnostics"
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "FootballEngine.MatchEngine.FlowTransition"
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "FootballEngine.MatchEngine.MatchClock"
```

**IMPORTANT:** The filter string must match the BEGINNING of the full test name. partial names like `"Layer1"` or `"GoalCycle"` do NOT work. Always run `-- --list-tests` first to get the exact names.

---

## Common Mistakes and How to Avoid Them

| Mistake | Why it's wrong | What to do instead |
|---|---|---|
| `failwith` in runtime code | Unhandled exceptions break the deterministic simulation | Use `Result<'T, 'E>` or `Option` |
| `Unchecked.defaultof` | Garbage values that cause phantom bugs | Use `Option` or initialize explicitly |
| Accessing `BalanceConfig.defaultConfig` directly | Hardcodes configuration, impossible to test with different configs | Pass as a parameter or read from `SimState.Config` |
| `printfn` for logging | Unstructured output, impossible to filter | Use `State.LogMessages` or `pushNotification` |
| Agents calling each other | Direct coupling, breaks layer separation | Communicate via `SimState` and `SemanticEvent` |
| Balance numbers outside `BalanceConfig` | Impossible to tune and calibrate | Always in `BalanceConfig` |
| Behavior without `SemanticEvent` | Invisible to the viewer and the rest of the system | Always emit an event |

---

## Useful Commands

```bash
# Build
dotnet build

# Run
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release

# Tests
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj -- --list-tests 2>&1 | grep -v "warning\|NU1"  # list all tests
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "FootballEngine.MatchEngine.GoalCycle"  # specific suite
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj --filter "FootballEngine.MatchEngine.Diagnostics"  # diagnostics only

# Formatting
dotnet fantomas .
dotnet fantomas . --check

# Benchmarks
dotnet run --project FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release
```

---

## Quick Code Reference

| What you need | Where it is |
|---|---|
| Match state | `Engine/Match/Core/SimState.fs` |
| Balance configuration | `Engine/Match/Types/BalanceConfig.fs` (~1670 lines) |
| Semantic events | `Engine/Match/Types/SemanticEvent.fs` |
| Event router | `Engine/Match/Core/EventRouter.fs` |
| Simulator | `Engine/Match/Simulation/Simulator.fs` |
| Match agents | `Engine/Match/Ball/`, `Referee/`, `Player/`, `Manager/`, `TeamOrchestrator/` |
| UI state | `AppState/Types.fs` |
| Test builders | `FootballEngine.Tests/Infrastructure/Builders.fs` |
| Config validation | `Engine/Match/Core/ConfigValidation.fs` |
| ML weights | `Engine/Match/ML/WeightsLoader.fs` |