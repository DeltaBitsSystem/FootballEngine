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
dotnet fsi scripts/test.fsx
```

**Formatting (Fantomas):**
```bash
dotnet fantomas .
dotnet fantomas . --check   # check only, do not modify
```

---

## Project Architecture

### Current architecture (authoritative)

The engine has moved out of Client. The historical layout and layer descriptions below are retained as context, not source paths or the current scheduler contract. Use these locations for new work:

- `FootballEngine.Core/Match/Contracts/`: match state, frames, world, configuration, events, observation and session boundary.
- `FootballEngine.Core/Match/Stepper/`: `Stepper.fs`, `EventApply.fs`, `SystemsTable.fs` (the sole scheduler table).
- `FootballEngine.Core/Match/Systems/`: Ball, Players, Referee, Team and Manager systems; `Match/Restart/` owns restart planning.
- `FootballEngine.Core/Match/Simulator/`: factory, Result-based `Simulator.runMatch`, visual replay projection. No `weightsPath` argument or exception-returning facade.
- `FootballEngine.Client/`: Avalonia UI, runtime worker, persistence adapter and WebGPU presentation. Rendering never determines sporting outcomes.
- Canonical test command (local and CI): `dotnet fsi scripts/test.fsx`. Select Core or Client with `--project Core` or `--project Client`; see `docs/TESTING.md`.
- `FootballEngine.slnx` contains Core, Core.Tests, Client and Training. Client.Tests and Benchmarks are built separately.

Decision proposals belong to `TeamWorld.PendingActions`; VAR snapshots belong to `World.Reviews`. Neither belongs in `TeamFrame.RunLookup`. Evidence is captured through `World.Observation`; format text/export outside ticks. Scheduling/visibility and determinism decisions are documented in `docs/evidence/MATCH_ENGINE_FOUNDATION.md`.

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
├── GameData/            # .femod resolution, JSON codecs, and resolved snapshots
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
- Never start a comment with the literal word "why" — the justification is implicit by convention, the prefix is noise.

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

Tests are executable **Expecto** projects, not VSTest projects. `dotnet test` is not the repository test command. Use the same SDK-only F# runner locally and in CI:

```bash
dotnet fsi scripts/test.fsx                         # all discovered test projects, Release
dotnet fsi scripts/test.fsx --project Core --list    # discover current full names; does not execute tests
dotnet fsi scripts/test.fsx --project Core --filter "FootballEngine.Core.FootballEngine.Core.Contracts.Rng"
dotnet fsi scripts/test.fsx --project Client
dotnet fsi scripts/test.fsx --self-test              # runner checks only, not game acceptance
```

The runner discovers source `*.Tests.fsproj` projects, builds before execution, rejects focused tests and validates fresh JUnit reports. Zero executed cases, missing reports, failures, errors and skipped cases are failures. A typo in a filter must not produce a green gate. `scripts/test.sh` is only a forwarding convenience, never a second implementation. Reports and build/test logs live in `artifacts/tests/<run-id>/`; CI uploads them even on failure. Full names may contain repeated namespace segments: copy actual discovery output rather than guessing. Add a test file to its F# compilation order **and** its suite to `Program.fs`; being present on disk does not register a test.

Current ownership:
- `FootballEngine.Core.Tests/Contracts`, `GameData`, `World`, `Stepper`, `Systems`: domain, content, world progression and match simulation. Builders live in `Infrastructure/TestBuilders.fs`.
- `FootballEngine.Client.Tests`: SQLite persistence, application/MVU, runtime workers, presentation, animation and asset contracts. State tests are not proof of actual UI layout or interaction.
- Tool-specific tests and visual/benchmark probes are additional evidence, not automatically covered by the .NET runner. Record their commands explicitly when relevant.

### Testing philosophy and anti-gaming rules

- Define acceptance before implementation: valid starting state, real action, observable result, forbidden effects and the defect the test must detect. A test name must describe what its assertions actually prove. Never compare a value with itself or assert only that a type/case exists as evidence of a working feature.
- Test the whole game: data/save integrity, world scheduling, economy/transfers, competitions, training/scouting, match laws and causal behavior, application/UI journeys, assets and packaged startup. Prioritize irreversible damage (lost careers, duplicated money/results), then blocked player journeys and incorrect simulation. Do not let match-engine volume hide untested world/UI boundaries.
- Protect boundaries with real integration tests: temporary isolated SQLite databases, real codecs, a small valid career traversing create/advance/match/commit/save/reload/continue, and explicit cancellation/failure recovery. Mocks may isolate dependencies but cannot certify the dependency they replace.
- Demonstrate sensitivity: reproduce the regression or show a deliberately incorrect/no-op/duplicate/inverted implementation fails. Use independent simple reference models, negative controls and targeted mutation testing for critical contracts. Review test helpers/oracles too; two implementations sharing the same faulty helper can agree while both are wrong.
- No weakening acceptance to obtain green: deleting/disabling cases, expanding tolerances, changing expected values, reducing seeds/workload, replacing real integration with mocks or updating goldens requires a contract-based justification and review. Never special-case fixture names/seeds or detect test execution in production. Fix the implementation or expose the contradiction with evidence.
- Keep evaluation separate from calibration: use documented representative corpora and held-out scenarios; do not tune only against familiar seeds or require every stochastic match to follow a preferred script. Determinism proves reproducibility, not football validity or fun. Use causal A/B scenarios, invariants, metamorphic properties and corpus-level distributions with justified tolerances/sample sizes.
- Coverage is evidence across risks, states/transitions, branches and defect sensitivity, not a global line percentage or test count target. Record gaps honestly. Never equate successful compilation, discovery, runner self-tests or a filtered subset with all tests passing.
- Keep feedback economical: use the smallest scenario that can expose the defect, reserving full matches/seasons for integration/corpus checks. Measure per-case duration before removing duplication. Separate functional correctness, statistical validation, performance benchmarks and real visual review; do not relax any to meet a speed target.
- Use reproducible inputs and controlled clocks; synchronize workers with explicit signals, not sleeps/retries until green. Keep test state isolated and parallelize only independent work. Do not share mutable fixtures. Preserve a failing seed as a regression case; do not reroll until passing.
- Maintain fast local feedback, a global PR gate, broader integration/corpus/soak checks and package/platform smoke checks as explicit levels. These are design responsibilities, not a claim that all levels already exist. Selection based on changed files never replaces global acceptance at integration.
- Validation reports must identify commands, selected cases, failures/skips, environment, corpus/seeds and relevant timing/artifact evidence. An implementer cannot independently certify its own changed acceptance criteria; critical changes require independent review of code and tests.

Runner maintenance and exact commands: `docs/TESTING.md`.

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

## Collaboration and UI Editing Rules

### Implementation sessions and subagents

- The user defines the product vision and priorities. The primary agent owns analysis, solution design, architecture and independent acceptance review. A smaller implementation model writes and validates a solution already designed by the primary agent; do not delegate an open-ended redesign and expect the implementer to invent the architecture.
- Delegate only when the user or applicable instructions authorize it. Authorized code implementation may use a session or subagent under this workflow; delegation is not restricted to Blender. Do not create additional agents recursively or change the requested model or reasoning effort without authorization.
- Before dispatch, resolve the important design decisions and provide a concise, self-contained specification: objective and rationale; chosen algorithms and contracts; state ownership, update order and invalidation; affected files and consumers; edge cases; acceptance tests; performance budget and measurement method where relevant. Identify existing functionality to reuse and changes that are out of scope. An ambitious goal must become concrete implementation work, not an invitation to add speculative systems.
- Use Goal Mode for dispatched work sessions when supported. Preserve the user's requested model and reasoning effort; configure a goal token budget only when supplied or agreed with the user. Goal Mode sustains execution but does not replace a specification, verification or resource limits. Do not assume subagents support the same session options.
- Give each implementer exclusive write ownership of a bounded set of files, including project files and shared contracts. Treat uncommitted work as the authoritative shared baseline; record status, preserve prior changes and evidence, and do not silently overwrite reports. The primary agent reviews or works in non-overlapping areas while implementation runs.
- The implementer may make ordinary local coding choices, compile, test and fix defects within the specification. It must not independently choose a different architecture, weaken acceptance tests, disable functionality to meet a benchmark, or silently reduce scope. If a design contradiction or genuine blocker appears, return the exact evidence and a concrete question or proposed correction to the primary agent.
- Require a complete reviewable result for the specified unit: actual implementation, relevant tests, reproducible measurements where required, and honest limits. A plan, successful build, convincing report or list of remaining authorized work is not completion. Continue the implement/test/correct cycle until acceptance criteria are met or a genuine blocker is demonstrated.
- The primary agent inspects the actual diff and independently verifies critical behavior and evidence before accepting the result or expanding the next unit. Use focused failure feedback rather than repeatedly resending a broad vision or telling the implementer to try harder. Repeated failed attempts require diagnosis and a revised solution, not an endless continuation loop.
- Keep orchestration efficient: reuse established context, avoid duplicate audits and polling, request automatic result delivery when the next step needs the outcome, and keep handoffs concise without dropping constraints. Do not trade requested completeness or quality for fewer tokens.

### Development commits and review workflow

- Treat the current implementation as the authoritative development baseline, not disposable or legacy work. Build it first; fix compilation failures before starting the replacement, and report warnings separately from errors.
- When the user authorizes committing their current work, preserve all tracked and untracked non-ignored project changes in a checkpoint commit before dispatching a replacement. Do not discard or silently reformat that baseline. An explicitly requested exact-state checkpoint may bypass a formatting hook for that one commit; report the bypass and do not disable hooks globally.
- Build `FootballEngine.slnx`, `FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj`, and `FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj` to cover the solution and separately built projects. Building tests is not running them; state validation results accurately.
- Start an authorized isolated implementation from the checkpoint branch/ref, including the user's latest committed work, not automatically from the default branch. Keep the checkpoint reachable in history; never reset it away or force-push it out of history.
- The user prefers a complete end-to-end implementation delivered for manual review, not repeated approval requests for each internal gate. Validate incrementally, but do not stop at a cosmetic slice or planning document when a functional loop was requested.
- Commits are changes for review, not certificates of correctness or merely backup labels. Describe the actual code changes in commit subjects/bodies, keep diffs coherent and easy to review, and record validation separately. Preserve prior work in history, but do not make checkpoint ceremony or repeated approvals the workflow. Deliver the full requested implementation for manual review; correctness is evaluated through review and validation, not assumed from committing.
- This is an in-development project: backward compatibility is not required for explicitly authorized redesigns. Replace obsolete contracts coherently rather than adding compatibility shims; reject incompatible saves explicitly and never silently delete user data.

- Be token-efficient in the process, never by reducing the requested scope, quality, depth, or completeness of the result. Inspect only the files and ranges needed, avoid duplicate audits and repeated explanations, reuse established context, and keep progress/results concise while still delivering the full objective.
- Do not interpret requests for token efficiency as permission to choose a smaller, merely cosmetic, or partial implementation. If the objective requires substantial architecture or validation, complete that work while minimizing conversational and exploratory overhead.
- Treat the existing worktree as shared. Record `git status --short`, preserve unrelated changes, and never revert work you did not create.
- Before editing F# UI, read the whole touched file when practical and inspect nearby established patterns. Match the local idiomatic style instead of inventing a new layout style.
- Use four spaces per indentation level. In FuncUI attribute lists, align every attribute with the first attribute after `[`. Keep multiline expressions indented one level deeper and preserve the surrounding `match`, `if`, `for`, and `yield!` style.
- A compiler error caused by indentation is fixed by adjusting whitespace only. Do not change logic, names, or structure while correcting formatting unless the error proves the code itself is wrong.
- F# indentation is structural, so never guess or manually approximate whitespace from a rendered diff. Copy the exact indentation column from the adjacent existing expression.
- A whitespace-only correction must use a one-line `apply_patch` anchored to the exact old and new lines. Do not rewrite a surrounding block or move unchanged `testCase` lines.
- The `+` and `-` characters in an `apply_patch` diff are syntax, not file content. Count spaces only after the marker, then verify the resulting columns with `Read` before editing anything else.
- After every indentation edit, run `git diff --check` and build the smallest affected project. Do not combine whitespace corrections with functional changes in the same patch.
- Keep subagent scopes non-overlapping when they edit files. Do not dispatch parallel agents that can modify the same F# file, project file, or shared state contract.
- Build the smallest affected project immediately after an edit. Run the relevant tests before moving to the next gate; do not stack unverified changes.
- For UI work, use the real deterministic screenshot workflow and inspect the generated images. A successful build does not prove layout, state, language, or interaction quality.

```bash
# Build
dotnet build

# Run
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release

# Tests
dotnet fsi scripts/test.fsx
dotnet fsi scripts/test.fsx --project Core --list
dotnet fsi scripts/test.fsx --project Client

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
| Test builders | `FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs` |
| Config validation | `Engine/Match/Core/ConfigValidation.fs` |
| ML weights | `Engine/Match/ML/WeightsLoader.fs` |

## Blender and original asset production

- For any Blender asset work—player models, anatomy, clothing, equipment, stadiums, props, rigs, skinning, animation, materials, textures, skies/clouds or lighting—load the project skill `footballengine-blender` from `.opencode/skills/footballengine-blender/SKILL.md` first, then the relevant installed specialist skill.
- The user uses OpenChamber/OpenCode. Skills are project-local under `.opencode/skills/`; preserve existing MCP configuration and do not install unrelated global/Claude/Codex settings.
- All final visual assets are intended to be original project-owned content, including texture pixels and animations. Reuse established tools, algorithms, procedural generators, rigging/IK, modifiers and baking/export workflows whose licenses permit the intended outputs; do not reinvent capable existing solutions without a reason. Imitate reference quality, form and motion through our own authoring, not copied meshes, pixels, clips, logos or protected distinctive designs.
- Preserve editable Blender sources, parameters/seeds and reproducible runtime exports. Sky/clouds follow this same policy: author and render our own environment rather than using stock HDRIs or cloud photography. Existing third-party/derivative assets must keep honest provenance until explicitly replaced; a CC0 license is not a claim that we originally authored the asset.
- Blender delegation follows the implementation-session workflow above. Preserve the live Blender scene and shared project changes; isolated batch authoring is explicit, never a silent reset of the user's scene.
