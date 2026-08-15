# FootballEngine Match Engine — Full Rewrite Plan (TDD-First)

> **Scope:** `Engine/Match/**` (~24,600 lines, ~130 files) → rewritten in `FootballEngine.Core` (new engine library).
> **World Engine (`Engine/World/**`, ~20 files):** **NOT rewritten** — moved byte-identical to Core. It is the game world (the player's career); matches happen *inside* it and are handled by the match engine (see §4.1b).
> **Mode:** Full rewrite of the match engine only, no backward compatibility with the old match internals.
> **Method:** TDD-first. Every phase starts with failing tests written against frozen contracts.
> **Executor:** Multi-agent AI teams (4–6 parallel agents) following the protocol in §6.
> **North star:** a deterministic, fast, readable, emergent match engine that is *better than Football Manager* because the result has real chain-linked causes, the team thinks as a unit, and every match is reproducible.

---

## 0. Resumen ejecutivo (ES)

El análisis profundo de las ~24,600 líneas del engine actual encontró: **un `SimState` mutable de 43 campos** atravesado por un loop de 228,000 ticks por partido, **tres vocabularios de eventos** (SemanticEvent / DomainEvent / MatchEvent) que pueden divergir, **~2,000 líneas de código muerto o duplicado** (EventRouter sin compilar, dos implementaciones de pases, cuatro copias de lane-clear), **RNG global no determinista** (ThreadLocal<Random> con seed de contador), **singletons mutables compartidos** (`MatchMemory.empty`) que corrompen partidos paralelos, y **BalanceConfig de 720 campos** con validación que cubre 8/34 secciones y un WeightsLoader de 2,060 líneas de boilerplate manual.

El plan reescribe el engine en un proyecto nuevo `FootballEngine.Core` con: (1) **RNG xoshiro256** explícito y seedeable — determinismo real y paralelizable; (2) **un solo DU de eventos** que sirve tanto al scheduler (via bitmask O(1)) como al viewer; (3) **un scheduler declarativo** como única fuente de verdad (borra el EventRouter fantasma); (4) **una sola implementación** de cada mecánica (un módulo de pases, un lane-clear, un event-window con ventanas O(1)); (5) **config con merge genérico por reflexión** (~150 líneas reemplazan 2,060); (6) **proyección PlayerProfile** que desacopla el engine del Domain; (7) orden de tick "el equipo piensa primero"; (8) invariantes y contratos estadísticos como tests.

**El World Engine no se toca.** El juego tiene dos motores: el **World Engine** (`Engine/World/**` — el mundo donde vive el jugador: clubes, temporadas, fixtures, finanzas, transferencias, juveniles, entrenamiento) y el **Match Engine** (`Engine/Match/**` — el partido que sucede *dentro* de ese mundo). El World Engine se **mueve intacto a `FootballEngine.Core`** (junto con `Domain/`, que necesita), sin reescribir ni refactorizar: solo el *seam* donde llama al simulador de partidos se re-cablea en P9. Todo el plan de rewrite aplica exclusivamente al Match Engine.

**Criterios globales:** determinismo byte-idéntico por seed; runFast < 0.5 s/partido single-thread; < 30 MB alocación por partido fast; 1,000 partidos paralelos < 120 s; replay < 25 MB; cero código muerto; cero `failwith`/`Unchecked.defaultof`; ningún archivo > 400 líneas.

**Timeline:** humano: ~5–8 semanas. AI multi-agente: ~46–61 agent-hours totales → **3–6 días wall-clock** con 4–6 agentes en paralelo (no 1 hora: el cuello de botella es build/test/integración, no la escritura; el plan minimiza esos ciclos con contratos congelados y paquetes independientes).

---

## 1. Diagnosis — why a rewrite, with evidence

All findings below were verified by reading the code. `file:line` references are to the current tree.

### 1.1 Determinism is a lie (the project's core promise is broken)

| # | Problem | Evidence |
|---|---|---|
| D1 | RNG is a global `ThreadLocal<Random>` seeded from `Interlocked.Increment(counter)` — not seedable, not injectable, order-dependent | `Engine/RNG.fs:7–13` |
| D2 | Referee profile picked with bare `System.Random()` (time-seeded) at match setup | `Simulator.fs:246–252` |
| D3 | `Distributions.NormalCache` module-level mutable spare | `Engine/Distributions.fs:9–10` |
| D4 | No test seeds anything; "reproducible match" is untestable and untested | test suite greps: zero `seed` usage |
| D5 | Parallel world sim (`parallelMapLimited`, `WorldRunner.fs:132`) is therefore non-deterministic | `WorldRunner.fs:132` |

### 1.2 Shared mutable singletons corrupt parallel matches

| # | Problem | Evidence |
|---|---|---|
| S1 | `MatchMemory.empty` is a module-level value; its arrays are mutated in place; `SimState.MatchMemory` defaults to it and `initSimState` never replaces it. Two matches in one process share duel/pass memory. | `MatchMemory.fs:34`, `SimState.fs:76`, `Simulator.fs:149–270` |
| S2 | `AdaptiveTactics.initial` same pattern | `SimState.fs:18` |
| S3 | `MovementScorer.neighborBuffer` module-level scratch array | `MovementScorer.fs:13` |

### 1.3 Three event vocabularies, one dead router

| # | Problem | Evidence |
|---|---|---|
| E1 | `EventRouter.fs` is **not even compiled** (absent from `.fsproj`), yet `AGENTS.md` documents it as the activation hub. The live policy is inline `Scheduler.shouldRun` frequency literals in `MatchStepper.runLiveSystems`. Three policies exist (dead router / live scheduler / inline `%` checks). | `EventRouter.fs:16–50`, `MatchStepper.fs:441–546`, `PhysicsSystem.fs:129` |
| E2 | `SemanticEvent` (11 cases) + `SemanticEventKind` (payload-stripped twin, `SchedulingTypes.fs:6–17`) + `MatchEventType` (~40 cases viewer stream) — no mapping function between engine and viewer streams. | `SemanticEvent.fs:6–32`, `Domain/Match.fs:21–59` |
| E3 | `PlayerAgent.agent` (the documented 5-layer entry) is dead — zero callers. The real pipeline is `ActionResolver` + `CognitionSystem` + `PhysicsSystem`, three systems on three cadences. | `PlayerAgent.fs:8` |
| E4 | `PassLauncher.fs` (918 lines) duplicates `PassAction.fs` (753 lines): two long-ball implementations, two into-space implementations, plus dead `launchShort`/`launchGKDistribution` (~390 lines dead). | `PassLauncher.fs:139,322,476,711`, `PassAction.fs:335,522` |
| E5 | Four copies of pass-lane clearance and four best-pass-target scorers with subtly different constants (0.20/−0.30 vs 0.2/0.0). | `MatchSpatial.fs:153`, `PassDecision.fs:16`, `Perception.fs:225–244`, `CognitiveFrameBuilder.fs:65–82` |
| E6 | Two interception implementations (live `ContactResolver.evaluateArrival`; dead `Interception.evaluateArrival`/`chooseBestInterceptorSoA`). | `Interception.fs:47–149` |
| E7 | Dead: `MovementEngine.fs` (222 lines, second steering impl + second reactive override copy), `MathPipelines` 5/9 dead functions, `MovementConstants`, `SubstitutionIntelligence`, manager reactive handlers, `cardProbability`, `FoulAnalysis.decideCard`, `TeamOrchestrator/DirectiveTypes.fs`, `WinProbability.MatchSituation` DU, unreachable `CounterReady`/`ContestBall` directive paths, `AdvantageCalledBack`, `OrchestratorRead.Cohesion` (computed, never consumed), injury path (unreachable — `att` always `None`). | verified by grep: zero callers |

### 1.4 Real bugs in the current design

| # | Bug | Evidence |
|---|---|---|
| B1 | **Double goal award**: referee `decide` emits `FlowChange(GoalPause)`; `updateFlow` then runs `startGoalFlow` which re-applies `ConfirmGoal` via `RefereeApplicator`. | `RefereeAgent.fs:452–465`, `MatchStepper.fs:186–222` |
| B2 | `MatchEvents` ring capped at 1024, evicts 512. Every consumer scanning the whole log (foul counts, momentum streak, possession pressure, event rates) silently loses history. `playerFoulCount` is O(fouls²). | `MatchStepper.fs:122–123`, `RefereeAgent.fs:181–205` |
| B3 | `TeamFrame` uses `Unchecked.defaultof<_>` ×5 and **duplicates its own sub-frame data** in 23 flat arrays (Condition vs Cond.Condition, Mental vs Mental.*, etc.). | `TeamFrame.fs:6–10`, `TeamFrame.fs:29–52` |
| B4 | Advantage is "play on and forget": when the advantage window expires, no free kick is awarded. | `RefereeAgent.fs:347–353`, `AdvantageEngine.fs:54–69` |
| B5 | `failwithf` in runtime paths crashes the parallel world sim instead of returning `SimulationError`. | `Simulator.fs:147,434,453,472` |
| B6 | Command channel (tactics/subs/pause) is unreachable from the shipped loop — `runLoop` hard-codes `[||]`. | `Simulator.fs:32` |
| B7 | Team systems run **after** cognition in the tick, so players always act on 1 s stale shape. | `MatchStepper.fs:512–534` |
| B8 | `validatePerformanceConfig` validates the wrong object (defaults constants, never `cfg.Performance`); `validateShotConfig` is an empty stub. | `ConfigValidation.fs:27,87–89` |
| B9 | Snapshots: ~45,600 per match, each with 4 arrays → ~65–90 MB + JSON dump on every user match. | `Simulator.fs:58,70–78`, `SnapshotBuilder.fs:41–79` |
| B10 | `StoppageEntry.SubTick: int` unmeasured; `CommittedUntil/At: int[]` measure-eroded; `tickDelta`↔`subtick` freely cast. | `StoppageTimeTracker.fs:15`, `IntentTypes.fs:127–137` |

### 1.5 Performance profile (ranked hot paths)

| # | Cost | Evidence |
|---|---|---|
| P1 | O(players × event-ring) scan: `possessionPressure` scans up to 1,024 events **per player per cognition tick** (~22×500 reads every 20 ticks), plus again per action tick. | `MatchSituationBuilder.fs:25–46`, `PlayerContext.fs:221` |
| P2 | ~12 list allocations per tick for frequency specs (`[IsLive]`, `[BallSecured;…]`, …) → ~2.7 M allocations/match. | `MatchStepper.fs:441–492` |
| P3 | `ActiveRuns` as persistent list; `List.tryFind` per run-capable player **per tick** in physics; `ExpireRun` does full `List.filter`. The `ActiveRunLookup` Dictionary exists but physics doesn't use it. | `PhysicsSystem.fs:67–75`, `MatchStepper.fs:80–84` |
| P4 | O(grid) `supportAttackScore` per player per cognition tick, recomputed even when intent is committed. | `MovementScorer.fs:260–283` |
| P5 | `CognitiveFrameBuilder` O(n²·m) lane matrix every 20 ticks (acceptable, keep — but cache). | `CognitiveFrameBuilder.fs:65–82` |
| P6 | `EventWindow.computeRates` full ring scan + `List.ofSeq |> List.rev` per team per team-tick. | `EventWindow.fs:29–89` |
| P7 | O(n²) separation every subtick — inherent to the approach, keep but SIMD-friendly SoA. | `PlayerSteering.fs:286–315` |
| P8 | `tacticsConfig` recomputed per call (per player per tick). | `SimStateOps.fs:16–38` |
| P9 | Snapshots + JSON dump (~65–90 MB) per user match. | see B9 |

### 1.6 Config and weights

| # | Problem | Evidence |
|---|---|---|
| C1 | `BalanceConfig`: 46 record types, ~720 fields, root of 34 subconfigs; `defaultConfig` is an 820-line literal. | `BalanceConfig.fs:930–964` |
| C2 | `WeightsLoader.fs` = 2,060 lines of hand-written partial types + merge functions mirroring every field. 3 subconfigs (OutfieldReception, Influence, Diagnostics) can't be tuned via JSON. | `WeightsLoader.fs` |
| C3 | Validation covers 8/34 sections; one stub; one validates the wrong object; errors are bare `string`. | `ConfigValidation.fs` |
| C4 | Duplicated weight concepts: `DecisionConfig.ShootFinishingWeight` vs `IndividualWeights.Shoot.FinishingWeight`; `DirectiveParamsMap` vs `EmergentWeights`; `PerformanceDefaults` vs `PerformanceWeightsMap`. | `BalanceConfig.fs` |
| C5 | Hardcoded magic numbers outside config: `40` (ticks/s, ≥4 sites), `80`-tick transition window (2 sites + unused config field), `1200` possession window, `0.7` pressing threshold, `maxSubs = 3`, `5*40` advantage window, `0.9/0.5` match importance. | `BlackboardBuilder.fs:19,160,214`, `MatchSituationBuilder.fs:32,63`, `ManagerAgent.fs:27`, `AdvantageEngine.fs:61`, `RefereeAgent.fs:208` |
| C6 | Unused weights that the calibration pipeline tunes to no effect: `CardHomeReduction`, `HomeBonusCardThreshold`, `MatchImportanceWeight`, `DisciplineHistoryMitigation`, `AdvantageWindowSeconds`, `PossessionChangeWindow`, `ScoreDiffPressStep`, `CounterPressStaminaFactor`. | verified by grep: definition/defaults/loader only |

### 1.7 Testability

| # | Problem | Evidence |
|---|---|---|
| T1 | Every system requires the full 43-field `SimState`; no narrow seams. `Infrastructure/Runners.fs` must bootstrap entire matches to test one subsystem. | `Tests/Infrastructure/Runners.fs:16` |
| T2 | Global RNG → no deterministic tests possible, only statistical. | see D1 |
| T3 | Direct-mutating systems (PhysicsSystem, TeamExecutor) return nothing to assert. | `PhysicsSystem.fs:12`, `TeamExecutor.fs:59–137` |
| T4 | `applyDomainEvent` is private; domain effects only testable by full ticks. | `MatchStepper.fs:45` |
| T5 | Replay/UI playback path has zero tests. | `MatchSlice.fs` |

### 1.8 What is GOOD and must survive the rewrite (essential complexity)

1. **SoA frames** (`TeamFrame`, `PhysicsFrame`, `IntentDataFrame`) — cache-friendly, allocation-free steering. Keep, but remove duplication (B3).
2. **Intent commitment + exit triggers** (`IntentPhase`) — the event-driven recalc protocol. Keep as-is conceptually.
3. **Trajectory-as-declaration + arrival race** (`BallTrajectory` + `ContactResolver`) — physics stays continuous, contact is one analyzable event. Keep.
4. **Multi-layer on-ball decision** (5 scorers → eligibility → softmax-with-temperature) with `DecisionTrace`. Keep, unify constants.
5. **The collective-intelligence stack**: blackboard (11 signals) → utility function (15 actions, hysteresis) → directive → per-slot modifier stack → player scorers. **This is the thesis of the project.** Keep the concept, restructure the packaging (god-object `TeamDirector.tick` → separate perceive/decide/apply stages).
6. **DomainEvent write-ahead pattern** ("agents never call each other") — keep, but *enforce* it: no direct mutation (B4, S1, S2, S3).
7. **VAR as pure snapshot evaluation** — keep.
8. **Post-processing philosophy** (StatsPanel computes derived metrics from events, never inside the sim) — keep; it's the correct viewer contract.

---

## 2. What we keep and what we delete (inventory)

### 2.1 Preserved contracts (external, non-negotiable)

| Contract | Notes |
|---|---|
| `weights.json` / `weights_trained.json` format | The `Training/` calibration pipeline depends on it. New generic merge must load it case-insensitively. Renaming fields requires a migration script in the same PR (P1/P10). |
| `CalibrationTargets` concept | Statistical contract tests are derived from it (P9). |
| `MatchReplay` concept (events + snapshots) | The UI boundary. Format is redesigned (compressed) but the *concept* survives. |
| `StatsPanel` post-processing of events | Viewer stats derived from events, never in-sim. |
| Domain types (`Player`, `Club`, `Match`, `ChemistryGraph`) | Move **as-is** to Core with the World Engine (World needs them). The *new* match engine consumes a projection (`PlayerProfile`, §5.4) and never touches them. |
| `MatchFlow` semantics | Live / GoalPause / VARReview / InjuryPause / RestartDelay / HalfTime / FullTimeReview / MatchEnded. Redesigned payloads, same semantics. |
| Sub-tick model | 40 subticks/s, ball dt = 1/40 s, players dt = 2/40 s. |

### 2.2 Deleted outright (dead or duplicated — no porting)

`EventRouter.fs`, `PlayerAgent.fs` (shim), `MovementEngine.fs`, `PassLauncher.fs` (logic merged into one `Pass` module), `Interception.evaluateArrival`/`chooseBestInterceptorSoA`, `MathPipelines` (dead 5) and `ActionMath.calcIntention/calcExecution`, `MovementConstants`, `SubstitutionIntelligence.fs`, manager reactive handlers + `trySub`, `cardProbability`, `FoulAnalysis.decideCard`, `TeamOrchestrator/DirectiveTypes.fs`, `WinProbability.MatchSituation` DU, `AdaptiveTactics.initial` (replaced), `MatchFormulas.fs`, `PlayerPhysics.steer` (non-SoA twin), `flee/alignment/cohesion` behaviors, `Statistics` scaffolding, `DirectiveParamsMap`/`EmergentWeights` duplication, `Probability` wrapper (raw floats win; one `probability` helper instead), `SemanticEventKind` twin, `SystemOutput.DomainEvent` (replaced by `MatchEvent` single stream), `TickDomainOutputs` buffer machinery.

### 2.3 Old engine lifecycle

- **P0:** `Domain/**` + `Engine/World/**` + old `Engine/Match/**` move **byte-identical** into `FootballEngine.Core` (file moves only: same namespaces, same code, project reference fixes). The World Engine is **frozen**: no new work, no refactors; the *only* permitted edit in `World/**` is the match-sim call site in `WorldRunner` (P9, §4.1b seam).
- The old match engine lives in Core as **legacy** (namespace `FootballEngine` kept, `Engine/Match/Legacy/**` layout) so World + Client + Benchmarks keep compiling, **frozen**, until P11 deletes it.
- Client keeps building against Core (old match) until P9 (world sim switch) and P11 (UI switch + legacy deletion).

---

## 3. Global acceptance criteria (the contract of the rewrite)

Measured on every merge from P3 onward. Recorded in `docs/PERF.md` (baseline measured in P9 against the old engine before deletion).

| Criterion | Target | How verified |
|---|---|---|
| **Determinism** | Same (ctx, seed) → byte-identical event stream and replay hash, any thread count | `DeterminismTests`: seed A vs A equal; A vs B differ; sequential vs parallel equal |
| **runFast throughput** | < 0.5 s / match single-thread (95 min, 228,000 ticks) | BenchmarkDotNet `FastMatchBench` |
| **runFast allocation** | < 30 MB GC / match | `dotnet-counters` / BenchmarkDotNet Alloc |
| **runFull** | < 5 s / match; replay < 25 MB | `FullMatchBench` + replay size assert |
| **Parallel world sim** | 1,000 matches < 120 s, deterministic vs sequential | `ParallelSimBench` + determinism test |
| **Statistical contracts** | 200 seeded matches within ranges (§P9): shots 20–36, goals 1.7–4.5 avg 2.7, pass% 72–90, fouls 14–32, yellows 2–8, corners 7–14, offsides 1–6, possession 30–70, home win 45–50% | `StatisticalContractTests` |
| **Invariants** | Zero violations across all tests (NaN, bounds, 11v11, scores ≥ 0, physics speed caps) | `InvariantTests` (checked every 1,000 ticks in all E2E runs) |
| **Readability** | No file > 400 lines (config data excluded); no `Unchecked.defaultof`; no `failwith`; no `printfn`; no module-level mutable/ref except inside `Rng`; no abbreviations in public names | CI greps + `fantomas --check` |
| **No dead code** | Zero top-level values with zero callers | CI dead-code grep script (§6.5) + merge review checklist |
| **Test coverage** | Every system module has a test module; engine line coverage ≥ 60% (Core only) | `dotnet-coverage` (P9 target) |
| **Config discipline** | Every `BalanceConfig` leaf is either read by engine code or listed in `ConfigUnused` (which must stay empty); validation covers 100% of sections | `ConfigCoverageTests` |
| **World Engine untouched** | `Domain/**` + `World/**` moved byte-identical (git diff of the move PR = only `.fsproj`/references, or zero-content moves); World tests green every phase; the only `World/**` edit is the `WorldRunner` seam (P9) | P0 move PR diff review + `Worldtests` green on every merge |

---

## 4. Target architecture

### 4.1 Project layout

```
FootballEngine.slnx
├── FootballEngine.Core/              ALL simulation — no Avalonia, no UI
│   ├── Domain/                       MOVED AS-IS (World needs it; UI reads it from here)
│   ├── World/                        THE WORLD ENGINE — MOVED AS-IS, FROZEN (§4.1b)
│   │   ├── Core/        AgentTypes.fs WorldClock.fs ConflictTypes.fs WorldTick.fs
│   │   ├── Agents/      ManagerAgent.fs PlayerAgent.fs ClubAgent.fs
│   │   ├── Supervisors/ FinanceSupervisor.fs YouthSupervisor.fs TransferSupervisor.fs
│   │   ├── Phases/      FinancePhase.fs SeasonPhase.fs TransferPhase.fs DevelopmentPhase.fs MatchPhase.fs
│   │   └── WorldRunner.fs WorldScheduler.fs      (WorldRunner = the ONE seam, P9)
│   ├── Match/                        THE MATCH ENGINE — REWRITTEN (this plan)
│   │   ├── Contracts/                A — foundational contract (frozen in P1)
│   │   │   ├── Units.fs  Pitch.fs  Clock.fs  Rng.fs  MatchEvent.fs
│   │   │   ├── MatchState.fs  Frames.fs  World.fs  MatchContext.fs
│   │   │   ├── BalanceConfig.fs      (restructured, §4.6)
│   │   │   └── PlayerProfile.fs      (Domain projection — the only Domain touchpoint)
│   │   ├── Stepper/                  C — one tick
│   │   │   ├── Scheduler.fs  Stepper.fs  EventApply.fs  Flow.fs
│   │   ├── Simulator/                B — loops
│   │   │   ├── MatchFactory.fs  Simulator.fs  Replay.fs
│   │   └── Systems/                  D — agents (shared SystemStep signature)
│   │       ├── Ball/      Physics.fs Contact.fs Outcome.fs Possession.fs
│   │       ├── Players/   Perception.fs Intent.fs Decision.fs Actions.fs Steering.fs
│   │       │             Actions/  Pass.fs Shot.fs Duel.fs Cross.fs GK.fs SetPiece.fs
│   │       ├── Team/      Perceive.fs Decide.fs Shape.fs Emergent.fs
│   │       ├── Referee/   Fouls.fs VAR.fs Cards.fs
│   │       └── Manager/   Manager.fs
│   ├── Config/                        Validation.fs  WeightMerge.fs
│   └── Legacy/                        old match engine (moved as-is at P0, DELETED at P11)
├── FootballEngine.Client/            AppState + Views + Program + GameData + Database (UI only)
├── FootballEngine.Tests/             NEW layout per phase (see §6.3)
└── FootballEngine.Benchmarks/        rewired in P9
```

Layers: **A → B → C → D** (B knows C, C knows D; nobody knows upward). New match engine target: **~40 files**, ~15,000 lines total (from ~130 files / 24,600 lines).

### 4.1b The World Engine — moved, not touched

**Definition:** the World Engine is the game world the player lives in: clubs, competitions, seasons, fixtures, finance, transfers, youth development, training, staff, inbox. It is *not* the match. Within that world, fixtures produce matches, and each match is simulated by the Match Engine (`Engine/Match/**`). `WorldRunner` orchestrates seasons; `MatchPhase` invokes the match sim per fixture.

**The rule — "no se toca":**
1. **P0 moves `Domain/**` + `World/**` byte-identical** into Core: same files, same namespaces, same code. Only allowed deltas: `.fsproj` entries, `open` fixes if the move breaks resolution (report them in the PR — they must be zero or trivial), and `AssemblyInfo`/project references.
2. **The World Engine is frozen for the duration of the rewrite.** No refactors, no perf work, no "while we're here" changes. If it has a bug, file it — do not fix it in this rewrite.
3. **One seam, and only one:** `WorldRunner` (and `MatchPhase` where it simulates) call the match sim. That call site adapts in P9 to the new `Simulator.runMatch` + `MatchResult` (§P9 seam contract). Everything else in `World/**` reads and writes its own state and the `MatchResult` surface.
4. Existing World tests (`Tests/Worldtests.fs`, `TrainingSystemTests.fs`) move with it, unchanged, and must stay green in every phase.
5. The World Engine's parallelism (`parallelMapLimited`) now becomes **deterministic** as a free side effect of the match-engine rewrite (per-match seeds, §5.4) — no World code changes needed to get that.

**What the World Engine needs from the Match Engine (the seam contract, defined in P1, verified in P9):**

```fsharp
/// Everything the World Engine reads after a fixture. The seam between World and Match.
type MatchResult =
    { HomeScore : int; AwayScore : int
      Events : MatchEvent list                       // metrics + viewer + CoordinationMemory inputs
      Injuries : PlayerId list * PlayerId list       // out per side after the match
      RedCards : PlayerId list * PlayerId list       // suspension-relevant
      XG : float * float
      Duration : int<matchMin> }
// WorldRunner maps MatchOutcome (new sim) -> MatchResult. That mapping is the ONLY
// match-related edit inside World/** (P9). Old `trySimulateMatch`/`trySimulateMatchFull`
// signatures are replaced by `Simulator.runMatch` + this projection.
```

### 4.2 Core types — frozen in P1 (agents implement these verbatim)

#### Units and clock (`Contracts/Units.fs`, `Contracts/Clock.fs`)

```fsharp
[<Measure>] type meter
[<Measure>] type second
[<Measure>] type subtick              // 1/40 s — the simulation step
[<Measure>] type matchMin             // match minute (1..90+)
[<Measure>] type tickDelta            // relative duration, same base unit as subtick
[<Measure>] type radian
[<Measure>] type meterPerSecond = meter / second

type SimulationClock = { SubTicksPerSecond : int }      // default 40
module SimulationClock =
    val dt : SimulationClock -> float<second>           // 1/40 s (ball)
    val dtPlayer : SimulationClock -> float<second>     // 2/40 s (players)
    val secondsToSubTicks : SimulationClock -> float<second> -> int<subtick>
    val toMatchMinute : int<subtick> -> int<matchMin>   // floors

[<Struct>]
type ClockPhase = FirstHalf | HalfTime | SecondHalf | FullTime

[<Struct>]
type MatchClock = { Tick : int<subtick>; Phase : ClockPhase }
// Invariant: monotonic; SecondHalf end = 90 min + decided stoppage (both from config)
```

#### RNG (`Contracts/Rng.fs`) — xoshiro256**, pure, no global state

```fsharp
[<Struct>]
type Rng = { mutable S0 : uint64; mutable S1 : uint64; mutable S2 : uint64; mutable S3 : uint64 }

module Rng =
    /// SplitMix64 from a 64-bit seed. Same seed -> same stream, always.
    val ofSeed : seed:uint64 -> Rng
    val nextU64 : rng:Rng -> uint64 * Rng
    val double : rng:Rng -> float * Rng                 // [0, 1)
    val uniform : lo:float -> hi:float -> rng:Rng -> float * Rng
    val intIn : lo:int -> hi:int -> rng:Rng -> int * Rng // inclusive both ends
    val normal : mean:float -> std:float -> rng:Rng -> float * Rng   // Box-Muller, one draw at a time (no cache)
    val bernoulli : p:float -> rng:Rng -> bool * Rng
    val beta : alpha:float -> beta:float -> rng:Rng -> float * Rng   // Marsaglia
    val weightedPick : weights:float[] -> rng:Rng -> int * Rng       // 0 if empty

/// Deterministic match seed: hash(matchId, configFingerprint, userSeed)
module MatchSeed =
    val ofMatch : matchId:uint64 -> configFingerprint:uint64 -> userSeed:uint64 -> uint64
```

Rules: every random draw in the engine is `rng -> value * rng`; systems thread `Rng` through `SystemStep` (§4.4). `System.Random`, `DateTime`, `Guid`, `Seq.random` are **banned** in Core (CI grep).

#### Single event stream (`Contracts/MatchEvent.fs`)

```fsharp
[<Struct>]
type EventFlags = { Bits : uint64 }                     // bit i = kind i occurred this tick
module EventFlags =
    val ofKind : EventKind -> EventFlags
    val union : EventFlags -> EventFlags -> EventFlags
    val intersects : EventFlags -> EventFlags -> bool
    val all : EventFlags                                // every bit set (for docs/tests)

type EventKind =                                         // <= 64 cases, tag order fixed in P1
    | PossessionGained | PossessionLost | BallLoose | PassLaunched | PassCompleted
    | ShotLaunched | ShotResult | Goal | Foul | Card | SetPieceAwarded | ThrowIn
    | Corner | GoalKick | Tackle | Save | CrossAttempt | Dribble | Interception
    | Injury | Substitution | VARDecision | KickOff | HalfTime | FullTime
    | MomentumShifted | ConditionCritical | RedCard

type ShotResult = OnTarget of saved:bool | OffTarget | Blocked | Post
type CardColor = Yellow | Red
type CardDecision = None | Yellow | Red

type EventData =
    | Possession of side:ClubSide * player:PlayerId
    | Pass of passer:PlayerId * receiver:PlayerId option * distance:float<meter> * quality:float * completed:bool
    | Shot of shooter:PlayerId * result:ShotResult * xG:float * speed:float<meterPerSecond>
    | Goal of side:ClubSide * scorer:PlayerId option * assist:PlayerId option * xG:float
    | Foul of fouler:PlayerId * fouled:PlayerId * severity:FoulSeverity * card:CardDecision * position:(float<meter> * float<meter>)
    | Card of player:PlayerId * card:CardColor
    | SetPiece of kind:SetPieceKind * side:ClubSide
    | BallLoose of x:float<meter> * y:float<meter>
    | Interception of interceptor:PlayerId * passer:PlayerId
    | Tackle of winner:PlayerId * loser:PlayerId
    | Save of gk:PlayerId * shooter:PlayerId
    | CrossAttempt of crosser:PlayerId
    | Dribble of dribbler:PlayerId * succeeded:bool
    | Injury of player:PlayerId * severity:int
    | Substitution of playerOut:PlayerId * playerIn:PlayerId
    | VARDecision of decision:VARResult
    | MomentumShift of toSide:ClubSide
    | KickOff of side:ClubSide
    | HalfTime | FullTime
    | ConditionCritical of player:PlayerId * condition:int
    | RedCardIssued of player:PlayerId

type MatchEvent = { Tick : int<subtick>; Data : EventData }
module MatchEvent =
    val kind : MatchEvent -> EventKind                    // total function, compiler-checked
    val describe : MatchEvent -> string                   // for tests/diag only (not viewer text)
```

Design notes:
- **One vocabulary.** The scheduler, the engine, and the viewer all consume `MatchEvent`. The scheduler matches `EventKind` via flags; the viewer renders from `EventData` (names/commentary are derived in the UI, never in the engine).
- Flags make activation checks **O(1)** — kills P2/P6 and `Seq.exists` scans.
- `kind` is a total match (like today's `SemanticEvent.kind`) — the only boilerplate, ~30 lines, and it enables the whole flag system.

#### World (`Contracts/MatchState.fs`, `Contracts/Frames.fs`, `Contracts/World.fs`)

```fsharp
[<Struct>]
type Spatial = { X : float<meter>; Y : float<meter>; Z : float<meter>; Vx : float<meterPerSecond>; Vy : float<meterPerSecond>; Vz : float<meterPerSecond> }

[<Struct>]
type Spin = { Top : float<radianPerSecond>; Side : float<radianPerSecond> }

type BallControl =
    | Free | Airborne | Receiving of side:ClubSide * player:PlayerId * since:int<subtick> | Controlled of side:ClubSide * player:PlayerId | Contesting of side:ClubSide

type TrajectoryIntent = Aimed of passer:PlayerId * receiver:PlayerId * quality:float | Struck of shooter:PlayerId * speed:float<meterPerSecond> * angle:float * xG:float | Cleared | Uncontrolled

[<Struct>]
type BallState = { Position : Spatial; Spin : Spin; Control : BallControl; Trajectory : TrajectoryIntent option; LastTouch : PlayerId option; StationarySince : int<subtick> option; GKHoldSince : int<subtick> option; ArrivalGate : int<subtick> option }

[<Struct>]
type MatchState = { Tick : int<subtick>; Clock : MatchClock; Flow : MatchFlow; HomeScore : int; AwayScore : int; Ball : BallState; Momentum : float; LastAttackingClub : ClubSide option; Possession : PossessionBook; Stoppage : StoppageBook }

[<Struct>]
type PossessionBook = { HomeTouches : int; AwayTouches : int; LastGainer : ClubSide option }   // O(1) counters; pressure window lives in TeamWorld.EventWindow

type TeamFrame = {                                   // SoA — ONE source of truth per property (kills B3)
    mutable PosX : float32[];  mutable PosY : float32[]
    mutable VelX : float32[];  mutable VelY : float32[]
    mutable Condition : float32[]                    // 0..1
    mutable Mental : MentalFrame                     // 5 float32[]: composure, confidence, aggression, focus, risk
    mutable Intent : IntentFrame                     // kind byte[], targetX/Y float32[], phase byte[], committedUntil/At int[], exitTrigger byte[]
    mutable Occupancy : OccupancyKind[]              // Active of slot | Sidelined of PlayerOut
    mutable SlotRoles : SlotRole[]                   // per-slot assignment (assigner is pure: Frames -> SlotRole[])
    mutable DefensiveRoles : DefensiveRole[]         // FirstDefender | Cover | Marker
    mutable CollectiveIntents : CollectiveIntent[]   // multiplier per intent kind, set by Team/Shape
    mutable SupportX : float32[];  mutable SupportY : float32[]
    mutable VisibilityTm : bool[];  mutable VisibilityOpp : bool[]
    mutable RunLookup : Dictionary<int, RunAssignment>   // owned per match; cleared each Team tick
    ActiveCount : int; SlotCount : int }
// Invariant: every array is length SlotCount; no duplicated flat arrays; no defaultof — factory allocates everything.

type TeamWorld = { Frame : TeamFrame; mutable Memory : PlayerMemory[]; mutable Cards : CardsBook; mutable SubsUsed : int; mutable Shape : ShapeTargets; mutable Directive : TeamDirective; mutable Blackboard : Blackboard; mutable Emergent : EmergentState; mutable Stats : TeamStats; mutable CFrame : CognitiveFrame; mutable EventWindow : EventWindow }

type World = { mutable State : MatchState; Home : TeamWorld; Away : TeamWorld }
// Invariant: all arrays/books owned by this match. NO module-level mutable state anywhere in Core.
```

- `MatchState` is a `[<Struct>]` record replaced per tick (shallow copies are cheap). `TeamWorld` members are mutable (frames must be mutated in place). `World` is the single object systems see.
- **Allocation discipline:** the only per-tick allocations are `MatchState` copies (~64 bytes) and the occasional event; everything else reuses arrays.

#### Context and projection (`Contracts/MatchContext.fs`, `Contracts/PlayerProfile.fs`)

```fsharp
[<Struct>]
type PlayerProfile = { Id : PlayerId; Role : SlotRole; IsGK : bool; Pace : float32; Strength : float32; Stamina : float32; Agility : float32; Vision : float32; Passing : float32; Finishing : float32; Crossing : float32; Dribbling : float32; Tackling : float32; Marking : float32; Positioning : float32; Anticipation : float32; Composure : float32; Aggression : float32; Flair : float32; Teamwork : float32; DecisionMaking : float32; Personality : BehaviorProfile; InjuryProne : float32 }

type Lineup = { Profiles : PlayerProfile[]; BasePositions : Spatial[]; SlotCount : int }   // slot-indexed

type MatchContext = { HomeClub : ClubRef; AwayClub : ClubRef; HomeLineup : Lineup; AwayLineup : Lineup; Config : BalanceConfig; IsKnockout : bool }
// ClubRef = { Id; Name; BaseTactics : TacticsConfig; Chemistry : float; HomeAdvantage : HomeAdvantageConfig }

module Projection =
    /// Domain Player -> engine PlayerProfile. The ONLY place Domain types appear in Core.
    val toProfile : Player -> PlayerProfile
```

The engine never touches `Domain.Player`/`Club` — tests construct `MatchContext` directly. Kills T1's "bootstrap a whole club to test".

#### Flow (`Contracts/MatchState.fs`) — countdown states

```fsharp
type MatchFlow =
    | Live
    | GoalPause of remaining:int<tickDelta> * side:ClubSide * scorer:PlayerId option
    | VARReview of remaining:int<tickDelta> * incident:VARIncident
    | InjuryPause of remaining:int<tickDelta>
    | RestartDelay of remaining:int<tickDelta> * plan:RestartPlan      // kickoff/throw-in/corner/gk/fk placement
    | HalfTime of remaining:int<tickDelta>
    | FullTimeReview of remaining:int<tickDelta>
    | MatchEnded
// Exactly ONE path can start a goal flow: the Goal event apply (EventApply). Kills B1.
```

### 4.3 The scheduler — single truth (`Stepper/Scheduler.fs`)

```fsharp
type Trigger =
    | EveryTick
    | EveryN of int<tickDelta>
    | EveryMinute of period:int<matchMin> * offset:int<matchMin>
    | OnEvent of EventFlags
    | OnEventOrEveryN of EventFlags * int<tickDelta>
    | WhenLive of Trigger                     // only while Flow = Live

type SystemSpec = { Name : string; Triggers : Trigger list; Step : SystemStep }

/// The ONLY scheduler in the engine. Static table, allocated once, evaluated per tick:
/// flags &&& spec.Mask <> 0 || tick % delta = 0 ...
val systems : SystemSpec list
```

Canonical table (frequencies inherited from the current engine; P7 reorders team-before-players):

| System | Triggers | Step |
|---|---|---|
| BallPhysics | EveryTick | §P2 |
| BallContact | EveryTick (no-op unless airborne/receiving) | §P2 |
| Possession | OnEvent (PossessionGained, BallLoose) | §P2 |
| Steering | EveryTick | §P4 |
| TeamPerceive | OnEventOrEveryN (PossessionGained, PossessionLost, BallLoose, SetPieceAwarded, 20) | §P7 |
| TeamDecide | EveryMinute(1) + OnEvent (Goal, RedCard, ConditionCritical) | §P7 |
| ShapeApply | EveryN 20 (after TeamDecide) | §P7 |
| Cognition | OnEventOrEveryN (PossessionGained, PossessionLost, BallLoose, 20) | §P5 |
| Action | OnEventOrEveryN (PossessionGained, 20) | §P6 |
| Referee | OnEventOrEveryN (BallLoose, Foul, Goal, 80) | §P8 |
| Manager | EveryMinute(1) + OnEvent (Goal, RedCard, ConditionCritical) | §P8 |

### 4.4 The system signature — one shape for all agents

```fsharp
type SystemContext = { Ctx : MatchContext; Clock : SimulationClock }
type EventBuffer = { mutable Events : ResizeArray<MatchEvent>; mutable Flags : EventFlags }
/// All systems share this signature. No system mutates anything outside World/EventBuffer.
/// No system reads global state. No system calls another system.
type SystemStep = SystemContext -> Rng -> World -> EventBuffer -> Rng

/// Fixed tick order (Stepper.step):
/// 1. drain/rotate EventWindow, build flags of this tick's events
/// 2. Flow machine (countdowns; applies flow-exit effects)
/// 3. if Live: BallPhysics -> BallContact -> Possession -> Steering
/// 4. if Live: TeamPerceive -> TeamDecide -> ShapeApply   (team thinks first — kills B7)
/// 5. if Live: Cognition -> Action
/// 6. if Live: Referee
/// 7. Manager (also during pauses for sub windows)
/// 8. Stoppage/flow transitions, clock advance, memory decay
```

### 4.5 Stepper and simulator

```fsharp
module Stepper =
    val step : SystemContext -> Rng -> World -> EventBuffer -> Rng * World

module Simulator =
    type RunMode = Fast | Replay of snapshotEvery:int<tickDelta>       // default 5
    type MatchOutcome = { Events : MatchEvent list; Final : World; Replay : ReplayData option; EventsHash : uint64 }
    val runMatch : MatchContext -> matchSeed:uint64 -> RunMode -> MatchOutcome
    // runMatch = create world -> loop step until Flow = MatchEnded -> hash events -> outcome
    // No JSON dump in the user path (B9). No failwith — result is total, errors are MatchError Result at factory level.

module Replay =
    /// Delta-encoded, SoA, position-quantized to 1 cm. ~45,600 snapshots * ~120 B ≈ 6 MB/match.
    type ReplayData = { Snapshots : byte[]; Count : int; Meta : ReplayMeta }
    val write : World -> ReplayData          // called by runMatch with RunMode.Replay
    val read : ReplayData -> SnapshotSeq     // UI consumes; interpolation in the UI as today
```

### 4.6 Config restructure (`Contracts/BalanceConfig.fs` + `Config/WeightMerge.fs`)

New top-level (34 sections → 10; duplicates merged):

```fsharp
type BalanceConfig =
    { Ball : BallConfig            // physics constants + trajectory + contact + reception  (was PhysicsConfig + Interception + part of Pass)
      Movement : MovementConfig    // speeds, acceleration, separation, fatigue decay, smoothing, scorer thresholds
      Actions : ActionsConfig      // pass/shot/duel/cross/GK/dribble knobs (was PassConfig, ShotConfig, DuelConfig, CrossConfig, DribbleConfig, GKConfig, DecisionConfig + IndividualWeights)
      SetPiece : SetPieceConfig
      Team : TeamConfig            // utility weights, emergent, blackboard windows, shape (was CollectiveWeights + UtilityWeights + MatchSituation + Influence + TeamDirector/Reactive/WinProb weights)
      Referee : RefereeConfig      // foul severity + cards + VAR + advantage + home-card (was FoulAnalysis consts + RefereeWeights + HomeAdvantage card parts)
      Manager : ManagerConfig      // sub windows, thresholds (was ManagerConfig + TimingConfig)
      Environment : EnvironmentConfig  // home advantage, crowd, weather hooks
      Match : MatchConfig          // volume, stoppage, match importance (was MatchVolumeConfig + TimingConfig parts)
      Weights : WeightSections }   // ML-tunable floats ONLY; mirrors weights.json section names
```

Migration rules (executed by the P1 agent, verified by tests):
1. Every old field maps to exactly one new field (table artifact `docs/CONFIG_MIGRATION.md`).
2. Duplicates merge to one (DecisionConfig.ShootFinishingWeight ≡ IndividualWeights.Shoot.FinishingWeight).
3. All hardcoded magic numbers (C5 list) enter `Movement`/`Referee`/`Manager` as named fields.
4. Measured fields get measures (`ForwardDistance : float<meter>`).
5. Abbreviated names expand (AM→AttackingMid, ST→Striker, Other→OtherRole, Cond→Condition…).
6. `weights.json` section names that the Training pipeline uses keep their names; renamed leaves get a migration script `Tools/MigrateWeights.fsx` run in P10.
7. **`defaultConfig` must not exceed 600 lines** — split into per-section `default*` values.

```fsharp
module WeightMerge =
    /// Generic partial merge: JSON with case-insensitive field names, option-typed overrides,
    /// applied over defaults via reflection. Replaces WeightsLoader.fs (2,060 lines).
    val mergeWithDefaults : json:string -> BalanceConfig -> Result<BalanceConfig, string>
    val load : path:string -> BalanceConfig -> Result<BalanceConfig, string>   // missing file -> Ok defaults
// Tests: every leaf tunable; unknown field -> error listing it; weights_trained.json merges and changes >= 50 known values (P10 asserts specific ones).
```

`ConfigValidation` (P10): `val validateAll : BalanceConfig -> ConfigIssue list` with `ConfigIssue = { Section : string; Path : string; Message : string }`; **all 10 sections** covered; range checkers per section (probabilities ∈ [0,1], speeds > 0, etc.).

---

## 5. Execution protocol for multi-agent AI (mandatory)

### 5.1 Team structure

- **P1 Contract Agent** (owns `Contracts/**` + `Config/WeightMerge.fs`). Contracts are **frozen** after P1: any change requires a plan PR, not a silent divergence.
- **Package agents** (P2–P8): one per package, in parallel where the table allows.
- **Integration agents** (P9, P11): own cross-cutting wiring.
- **Verification agents** (P0 CI, P10 config): own the gates.

### 5.2 Branch and merge protocol

1. Each package: branch `rewrite/<pkg>` off `main` (P1 merged first).
2. Packages touch ONLY their directories + their test directory. Shared contracts: read-only.
3. Merge gate (CI, all packages): `dotnet build` (zero errors, warnings ≤ baseline) → `dotnet fantomas . --check` → full test suite → banned-pattern greps → dead-code grep (new code only). P3+ also: determinism hash test + perf smoke.
4. Merge order: P1 → P2 → P3 → (P4 ∥ P5 ∥ P6) → P7 → P8 → P9 → P10 → P11. P4/P5/P6 can start as soon as P3's `World`/`Stepper` skeleton compiles, because their tests run against the skeleton with no-op systems.
5. Conflicts are resolved by the Contract Agent; never by blind merge.

### 5.3 TDD loop per package (non-negotiable)

```
RED    write tests against frozen contracts (they must fail to compile/run)
GREEN  implement in the package dir until tests pass
REFACTOR  simplify; enforce ≤ 400 lines/file; run fantomas
VERIFY full gates (5.2) + package acceptance criteria + update docs/PERF.md if touched
```
Tests are written **before** implementation in the same commit series; a package PR showing only implementation commits is rejected.

### 5.4 Determinism rules (all packages)

- Every random draw goes through the `Rng` parameter. Never `System.Random`, never globals.
- Never read wall-clock, never use `Guid`, `Seq.random`, `Random.Shared`.
- Never mutate anything not reachable from the `World`/`EventBuffer` parameters.
- No module-level `mutable`, `ref`, `Array.zeroCreate`, `Random`, `ResizeArray` outside `Contracts/Rng.fs` (CI grep enforces).
- Parallelism is always "parallelize matches, never ticks": each match owns its World.

### 5.5 Readability rules (all packages)

- Max 400 lines/file (config data file excluded); prefer < 300.
- Zero comments in implementation code; `///` docs only in `Contracts/**` (they are the spec); design notes go to `docs/`.
- Public names fully spelled (no `VisCanSeeTM`, `Cond`, `StdST`).
- One concept per module; module names are nouns (`Ball`, `Referee`, `Pass`), systems are verbs (`Perceive`, `Decide`, `Steer`).
- Pattern: tiny pure functions + one orchestrating `step` per system. If a system's `step` exceeds 60 lines, split into pure helpers.
- **Banned in Core** (CI grep): `Unchecked.defaultof`, `failwith`, `failwithf`, `printfn`, `Unchecked`, `Random(`, `DateTime`, `Guid`, `defaultof`.

### 5.6 Contract freeze list (only the Contract Agent may edit after P1)

`Units.fs, Pitch.fs, Clock.fs, Rng.fs, MatchEvent.fs, MatchState.fs, Frames.fs, World.fs, MatchContext.fs, PlayerProfile.fs, BalanceConfig.fs, Scheduler.fs (SystemSpec type + table), Stepper.fs (step signature)`. Every package test compiles against these — that is what makes parallel agents safe.

**Namespace rule (prevents collisions with the moved World/Domain/Legacy code):**
- New match code lives in namespace `FootballEngine.Core.Match.*` only.
- `Match/Contracts` **re-declares its own tiny domain enums** — `ClubSide`, `PlayerId` (int alias), `SetPieceKind`, `FoulSeverity`, `CardColor` — as engine types. No `open FootballEngine.Domain` and no `open FootballEngine` (which would pull in the legacy `[<AutoOpen>] RNG`/`Stats` globals) anywhere in new match code.
- The **only** file that touches Domain is `Match/Contracts/PlayerProfile.fs` (`Projection.toProfile`), and the only touchpoint to the World is the `MatchResult` seam (§4.1b), consumed by `WorldRunner` in P9.
- CI grep: `grep -rn "open FootballEngine$" Match/` and `grep -rn "open FootballEngine.Domain" Match/` must return zero.

---

## 6. Phases

Conventions: each phase lists **Test-first** (files + what they assert), **Implementation** (files + key notes), **Acceptance** (commands + numbers). Test names use prefix `FootballEngine.Core.<Package>.<Name>` so `--filter` works.

---

### P0 — Repo restructure, CI, test harness, **World move**  (≈ 4–6 agent-hours, 2 agents in parallel)

**Goal:** new projects exist; the World Engine + Domain are in Core **byte-identical** and green; gates exist; nothing behavioral yet.

**Work package A — World move (mechanical, 1 agent):**
- Move `Client/Domain/**` and `Client/Engine/World/**` into `FootballEngine.Core` **with zero content changes** (same files, same namespaces). Move old `Client/Engine/Match/**` into `Core/Legacy/**` the same way (it is what World + Benchmarks + UI still compile against until P11).
- Move old match/world tests (`Tests/Enginetests.fs`, `Tests/Worldtests.fs`, `Tests/TrainingSystemTests.fs`, `Tests/Layer1_3/**`, `Tests/Diagnostics/**`) into `Tests/Legacy/**` unchanged.
- Fix only: `.fsproj` entries, `open` statements that break due to project boundaries (report each in the PR — expected ~0–3), namespace `[<AutoOpen>]` `RNG.fs`/`Distributions.fs`/`Stats.fs` (these are **shared by World** — they move to Core untouched; the NEW match engine will not use them, P1's `Rng` replaces them for match code only).
- **The move PR diff must show only moves + fsproj/reference lines.** Anything else is a violation of "no se toca".
- World tests green on Core; Client still compiles against Core (old match) exactly as before.

**Work package B — harness + CI (1 agent, parallel with A):**
- `Tests/Infrastructure/TestBuilders.fs` — `makeProfile`, `makeLineup`, `minimalWorld`, `worldWithBallAt`, `worldWithScore`, `defaultCtx`. Tests: builder invariants (22 profiles, arrays aligned, no NaN).
- `Tests/Infrastructure/TestRng.fs` — `rngFor testName` (stable seed per test name), `runTicks n ctx seed world` helper.
- `Tests/Infrastructure/Invariants.fs` — `check : World -> string list` (positions in pitch ± margin, speeds ≤ caps, 11v11, scores ≥ 0, possession sums, momentum ∈ [−10,10], flow/clock monotonicity, all arrays length = SlotCount). Tested on a factory world.
- `Tests/Infrastructure/Assertions.fs` — `expectWithin`, `expectApprox`, `expectInRange`, `expectDistribution` (chi-square-lite), `expectDeterministic`.
- `Tests/Core/HarnessTests.fs` — proves the harness: two `runTicks` with same seed → identical `World`; different seeds → different.
- CI workflow `.github/workflows/ci.yml`: build, fantomas check, full tests, banned-pattern grep, dead-code grep (new code).

**Implementation (B):** `FootballEngine.Core.fsproj` (Domain + World + Legacy + empty `Match/Contracts` placeholder), Tests project restructured, CI yaml, `scripts/check_engine_rules.sh` (greps for banned patterns + dead-code heuristics: top-level `val/let` with zero references across new match code, excluding module-exposed API whitelist; the World/Domain/Legacy trees are exempt from the dead-code grep until P11).

**Acceptance:** `dotnet build` green; CI green; **World tests green unchanged**; move PR diff is pure moves; harness tests pass; `check_engine_rules.sh` passes.

---

### P1 — Contracts: types, RNG, clock, config, weight merge  (≈ 4–5 agent-hours, 1 agent)

**Goal:** every type in §4.2–§4.6 exists, compiling, tested. This is the contract freeze.

**Test-first (all in `Tests/Contracts/`):**
- `RngTests.fs` — determinism (same seed → identical 1,000-draw stream; different seeds differ); `double` ∈ [0,1) × 10k; `intIn` inclusive bounds + uniformity (chi-square over 10 buckets); `normal` mean/std over 50k draws ± tolerance; `bernoulli` rate ≈ p; `beta` mean ≈ a/(a+b); `weightedPick` distribution; `MatchSeed.ofMatch` stability.
- `ClockTests.fs` — dt/dtPlayer values; toMatchMinute floors; secondsToSubTicks round-trips.
- `EventFlagTests.fs` — kind↔flag bijection, `intersects`, union idempotence; `MatchEvent.kind` total over all `EventData` constructors (compile-time + runtime exhaustive).
- `ConfigTests.fs` — `defaultConfig` validates structurally (no NaN, no negative probabilities); `WeightMerge` round-trip: JSON of defaults → same config; partial JSON overrides exactly the named leaves; unknown field → Error naming it; case-insensitivity; `weights.json`/`weights_trained.json` load OK and change ≥ 50 known values (list asserts specific leaves, e.g. `Actions.Shoot.FinishingWeight` changed — catches renames breaking Training).
- `ConfigMigrationTests.fs` — mechanical check: the P1 agent's `docs/CONFIG_MIGRATION.md` table is complete (every old section/field listed once, every new section has ≥ 1 source); spot-check 10 mappings by grepping old usage → new field name in Core.

**Implementation:** `Contracts/*.fs` exactly as §4.2–§4.6 (all types, `Rng`, `MatchSeed`, `EventFlags`, `EventKind`/`EventData`/`MatchEvent`, `MatchFlow`, `MatchState`, `TeamFrame`, `TeamWorld`, `World`, `MatchContext`, `PlayerProfile`, `Projection` stub (returns placeholder profile — real mapping in P9), `BalanceConfig` (10 sections, defaults, migration table `docs/CONFIG_MIGRATION.md`), `Config/WeightMerge.fs` (generic reflection merge, ~150 lines), `Stepper/Scheduler.fs` with `Trigger`/`SystemSpec` types only (table empty until systems exist).

**Acceptance:** all `Tests/Contracts` green; `dotnet fantomas . --check`; no banned patterns; `weights_trained.json` merge test green.

---

### P2 — Ball physics, contact, possession  (≈ 3–4 agent-hours, 1 agent)

**Goal:** the pure ball core — no players needed. Ported from `BallPhysics.fs`/`ContactResolver.fs`/`OutcomeResolver.fs` with the duplication removed.

**Test-first (`Tests/Systems/Ball/`):**
- `PhysicsTests.fs` — per-tick invariants for 10,000 random launches (fixed seed): Z ≥ 0, |V| ≤ ShotSpeedMax+ε, bounds clamped, ground bounce restitution & friction within tolerance, post bounce Vx reversal, stop thresholds zero out below cutoff, drag multiplier reduces speed monotonically, Magnus: spin.Side curves trajectory in the expected lateral direction, spin decay.
- `ContactTests.fs` — arrival gate (no contact before `ArrivalGate`); intended-receiver anticipation bonus > neutral interceptor when within contest threshold; contested vs clean arrival by time-gap; GK save evaluated only for `Struck`; shot-save probability monotonic in reflexes, positioning, distance, speed (unit ranges sweep).
- `OutcomeTests.fs` — map contact → outcome incl. out-of-bounds by LastTouch (throw-in/corner/goal kick/goal), reception probability via bernoulli with seeded rng (reception rate rises with BallControl attr — 500 trials), interception event emission, ball loose on failed reception.
- `PossessionTests.fs` — gain/loss events emitted with correct flags; `PossessionBook` counters; Receiving expiry after grace; Contesting resolution.

**Implementation:** `Systems/Ball/Physics.fs` (pure `BallState -> BallState`), `Contact.fs` (pure, returns `Contact option * events`), `Outcome.fs`, `Possession.fs`. All pure of `World` — the three contact implementations collapse to one. Magic constants → `Ball` config section.

**Acceptance:** green; determinism test (same seed, same launches → identical outcomes); no allocation growth beyond baseline (smoke: 228k physics ticks < 20 MB).

---

### P3 — World factory, stepper skeleton, flow, scheduler  (≈ 4–6 agent-hours, 1 agent — the integration backbone)

**Goal:** `runMatch` executes a tick loop with **no-op systems** (steering/cognition/etc. registered but inert), full flow machine, events/flags, clock. Everything later phase plugs into the table.

**Test-first (`Tests/Stepper/`):**
- `SchedulerTests.fs` — trigger math: EveryN fires exactly on cadence; OnEvent fires iff flag present; OnEventOrEveryN both paths; WhenLive off during pauses; static table: every registered system fires within its documented cadence over a 10,000-tick run (probe systems count activations).
- `FlowTests.fs` — initial kickoff → Live; goal flow: GoalPause countdown → kickoff to conceded side; VARReview: pending incident resolved at 0 → goal stands/overturned by `VARResult` (VAR logic itself is P8; flow mechanics here); InjuryPause → free kick; RestartDelay placement then Live; HalfTime → kickoff flip; FullTimeReview → MatchEnded; stopwatch: 2 × 45 min + stoppage ≈ 228k ticks ± tolerance.
- `ClockTests.fs` — FirstHalf ends at 45 min exactly; SecondHalf extends to 90 + decided stoppage; HalfTime pause duration from config; `Tick` monotonic, no skips.
- `StepperTests.fs` — step applies events to MatchState (score changes only via Goal event — regression for B1); flags reflect the tick's events; EventWindow ring is O(1) and correct (windowed kind counts equal brute-force scan over last N ticks); memory decay cadence.
- `DeterminismTests.fs` (the load-bearing test) — two `runMatch` with same ctx+seed → identical `EventsHash` and final `World`; different seed → differ; **parallel**: 8 matches on 4 threads vs sequential → identical hashes.
- `MatchFactoryTests.fs` — world invariants (P0 `Invariants.check` green on factory output); ownership: two factory worlds share no array references (mutate one, other unaffected — regression for S1/S2).

**Implementation:** `Simulator/MatchFactory.fs` (world creation; memory/Cards/EventWindow owned per match), `Stepper/Stepper.fs` (order of §4.4), `Stepper/Flow.fs`, `Stepper/EventApply.fs` (all `EventData` → `MatchState` effects in one place: score, ball, possession, stoppage, cards, subs), `Stepper/Scheduler.fs` (table with stub steps), `Simulator/Simulator.fs` (`runMatch` Fast mode + `EventsHash` via xxHash-lite on the serialized event stream), `Simulator/Replay.fs` (P3: simple uncompressed writer/reader + round-trip test; compression lands in P9).

**Acceptance:** full suite green; determinism tests green (this is the moment the engine becomes reproducible); `runMatch` Fast on empty systems < 100 ms; invariants checked every 1,000 ticks in the E2E determinism test.

---

### P4 — Steering and movement (SoA)  (≈ 3–4 agent-hours, 1 agent — can run in parallel with P5/P6)

**Goal:** players move. Port `steerSoA` (the live one; delete the dead twin), reactive layer, fatigue pipeline — with the O(n²) separation kept but array-of-structs SIMD-friendly and constants in config.

**Test-first (`Tests/Systems/Steering/`):**
- `SteeringTests.fs` — player reaches a static target within expected time (±10%); separation: two players converging stop at separation distance; chasing ball converges; condition 0.1 → speed ≤ 40% of condition 1.0; turning constraint respected (angular change capped); no NaN over 10,000 ticks; momentum/phase modifiers shift targets as specced.
- `ReactiveTests.fs` — near-ball player overrides intent toward ball within reactive radius; radii from config.
- `FatigueTests.fs` — condition decays per config per second; decision-quality pipeline (P5 dependency — keep thin here: speed + condition only).

**Implementation:** `Systems/Players/Steering.fs`. Uses `RunLookup` Dictionary for active runs (fixes P3 — no `List.tryFind`).

**Acceptance:** green; steering 228k ticks × 22 players < 15 MB alloc; determinism green.

---

### P5 — Perception and cognition (intent pipeline)  (≈ 4–6 agent-hours, 1 agent — parallel with P4/P6)

**Goal:** off-ball intent production. Port the modifier stack **exactly** — individual scores → blackboard modifiers → collective intents → slot roles → defensive roles → emergent — this is the thesis. Fix: one scorer module, one lane-clear, one best-pass-target, cached MatchSituation/blackboard reads, committed-intent skip (no recompute for committed players), supportAttack computed once per team-tick.

**Test-first (`Tests/Systems/Cognition/`):**
- `PerceptionTests.fs` — visibility mask: radius/cone/peripheral gates; occlusion via nearest-defender heuristic (ported); mask reused per team-tick, not per player.
- `LaneClearTests.fs` — single implementation, property: a lane with 0 defenders between A and B is clear; N defenders raises blockage monotonically; symmetric.
- `ScorerTests.fs` — each of the 7 movement scores responds monotonically to its dominant input (shape score to distance-from-target, press to proximity, mark to opponent proximity, support to teammate density, recover to ball distance, cover to influence danger); committed-intent skip: a player with unexpired committed intent does not recompute (probe system counts calls).
- `IntentTests.fs` — pickIntent hysteresis (new score must beat old + threshold); fallback ladder when all < floor; ExecuteRun override for active runs; set-piece positioning override.
- `ModifierStackTests.fs` — the collective stack applied in order with documented multipliers; each layer's effect bounded; deterministic given same blackboard.

**Implementation:** `Systems/Players/Perception.fs`, `Systems/Players/Intent.fs`, `Systems/Players/Decision.fs` (scorers + eligibility + softmax, single `Decision` module — note: on-ball decision ships with this package so P6 only does actions), `Systems/Team/Perceive.fs` **stub** (empty blackboard until P7 — tests construct blackboards directly). `MatchSituation` computed once per tick by the stepper and passed via `TeamWorld.Blackboard`-adjacent cache (kills P1).

**Acceptance:** green; cognition tick with 22 players < 10 MB alloc/tick-batch; determinism green.

---

### P6 — Actions: pass, shot, duel, cross, GK, set pieces  (≈ 5–7 agent-hours, 1 agent — parallel with P4/P5; biggest package)

**Goal:** one implementation of every on-ball action. The pass duplication (E4) collapses into one `Pass` module (~350 lines vs 1,671). Interception stays in `Ball/Contact.fs` (P2). No `PassLauncher` file.

**Test-first (`Tests/Systems/Actions/`):**
- `PassTests.fs` — quality mean responds to passing/vision/distance penalties (sweeps); beta quality in [0,1]; lead prediction: receiver moving away → target leads; jitter scaled by (1−quality); heavy-touch probability; trajectory arrival gate = dist/speed; offside snapshot at launch and check at arrival (VAR-ready); events: PassLaunched → PassCompleted/Interception/BallLoose exactly once; memory write on completion (Memory is per-match, P3).
- `ShotTests.fs` — xG from config table (port xGCalculator), result distribution seeded: on-target rate rises with finishing; GK save path (from P2 Contact) feeds Save event; post/off-target paths; goal → Goal event with assist attribution when last-touch teammate ≠ scorer.
- `DuelTests.fs` — logistic duel outcome monotonic in dribbling/tackling gap (500 trials); foul branch: severity classification (port `FoulAnalysis.assess` geometry: last-man, promising attack, zone) — referee *adjudication* is P8, generation is here; contest radius from config.
- `CrossTests.fs` — cross selection needs lateralness + target in box; cross flight = ball physics (no special case); arrival uses same Contact path.
- `GKTests.fs` — distribution choice (roll/throw/punt/goal kick) by pressure + distance; uses the unified pass launcher; catch → Controlled to GK; punch/parry → BallLoose with direction bias.
- `SetPieceTests.fs` — kickoff flip; throw-in/corner/goal-kick placement (RestartPlan from Outcome P2); wall behavior (positions on line between ball and goal); free kick → shot/pass choice; penalty routine (config-based quality vs GK save).
- `ActionFlowTests.fs` — full possession sequence E2E on 2,000 seeded ticks: pass → reception → shot → save → corner → goal distribution sanity (goals only via Goal event; single award path — regression B1).

**Implementation:** `Systems/Players/Actions/{Pass,Shot,Duel,Cross,GK,SetPiece}.fs` + `Systems/Players/Actions.fs` (controller resolution + intent dispatch, replacing `ActionResolver`). Set-piece positioning from old `SetPiecePositioning.fs` ported compactly (~150 lines).

**Acceptance:** green; pass event rate + shot rate within statistical windows on a 5,000-tick seeded run; determinism green; alloc < 20 MB for 5,000 action ticks.

---

### P7 — Team intelligence: perceive → decide → shape → emergent  (≈ 4–6 agent-hours, 1 agent)

**Goal:** the collective brain, restructured as four clean stages (kills the `TeamDirector` god-object). Order in the tick: perceive (P5 stub now real) → decide → shape → then cognition (players act on fresh shape — kills B7).

**Test-first (`Tests/Systems/Team/`):**
- `BlackboardTests.fs` — the 11 signals: OurPhase (attack/defend/transition) with config window; OpponentShape thresholds; OpponentPressure count within radius; ThreatZones/WeaknessZones from influence grid; Urgency clamped [0,1] and monotonic in score deficit + time; MomentumStreak decay half-life from config; transition flag window from config (regression C5).
- `WindowTests.fs` — the ONE EventWindow module: O(1) kind counts in window vs brute force; decay semantics; used by blackboard + situation + emergent (single source).
- `UtilityTests.fs` — all 15 actions evaluated; hysteresis (switch only if best > prev + threshold); monotonic response of each utility to its dominant signal; `toDirectiveKind` total (no unreachable cases — wire all 6 kinds or delete to 4 deliberately).
- `ShapeTests.fs` — ShapeTargets from base positions + phase shift + ball pull + compact shift + width spread; support positions; defensive roles assignment (first defender nearest ball-carrier-side, cover, marker); per-slot CollectiveIntent multipliers applied in cognition (integration test with P5).
- `EmergentTests.fs` — compactness/pressing/wingplay/fatigue loops: clamp increments, feedback into directive params, xG table from the real xG module (kill the duplicated table), half-lives from config.
- `ChemistryTests.fs` — cohesion computed per team-tick (used by Decide now — don't compute-and-drop).

**Implementation:** `Systems/Team/{Perceive,Decide,Shape,Emergent}.fs`; `Decide` = utility + hysteresis + strategic/reactive modes (winProb from `WinProbability` module — port, config weights); directive state machine simplified (delete `Transitioning` and `Suspended` if unused after wiring — must be deliberate, not accidental).

**Acceptance:** green; team-tick < 1 ms; determinism green; a 5,000-tick run shows the team switching directives (probe system logs changes — assert ≥ 1 switch in a scripted deficit scenario).

---

### P8 — Referee, VAR, manager, injuries  (≈ 4–5 agent-hours, 1 agent)

**Goal:** adjudication + management, fully wired, with the dead paths either working or deleted.

**Test-first (`Tests/Systems/Referee/`, `Tests/Systems/Manager/`):**
- `FoulTests.fs` — severity classification table-driven (port geometry cases: DOGSO, SFP, VC, tactical); card decision by profile × zone × phase × team foul accumulation (per-half counters, not ring scans — regression B2); second yellow → red; home-bonus card adjustment via ONE home-advantage source (kill the duplicate); match importance weights actually read (regression C6).
- `AdvantageTests.fs` — play-on conditions; **expiry now awards the free kick** (fix B4); callback within window when possession lost.
- `VARTests.fs` — goal check triggers review; offside snapshot overturn; penalty-area geometry overturn; review duration from config; overturned goal does NOT double-award (regression B1).
- `InjuryTests.fs` — injury generation wired (fix the unreachable path): severe fouls + InjuryProne profile → Injury event → InjuryPause → substitution opportunity.
- `ManagerTests.fs` — sub windows from config (maxSubs from config, regression C5); condition-threshold selection by game state; bench = squad − XI − on-pitch − sidelined (per-match books); event reactions: goal conceded → attacking sub (old dead handler now live), red card → defender on, condition critical → direct sub; user commands (sub request from UI) flow through the same resolver.
- `RefereeFlowTests.fs` — E2E: 10,000 seeded ticks, foul rate 14–32/match window, yellow rate 2–8, red ≤ 1, no double goals, VAR overturn count ≥ 1 in 100 seeded matches (offside traps do occur).

**Implementation:** `Systems/Referee/{Fouls,VAR,Cards}.fs`, `Systems/Manager/Manager.fs`. RefereeAction DU singular (kill the twin). Foul generation stays in P6's Duel (engine of record), adjudication here.

**Acceptance:** green; determinism green; statistical windows green.

---

### P9 — Simulator complete, replay, world integration, statistical contracts  (≈ 5–7 agent-hours, 1–2 agents — hardest package)

**Goal:** the engine is complete; the world sim consumes it; the numbers are football-shaped.

**Test-first (`Tests/E2E/`):**
- `MatchInvariantTests.fs` — 10 seeded full matches; invariants checked every 1,000 ticks (P0 checker); zero violations.
- `MatchDeterminismTests.fs` — 5 matches × same seed twice → byte-identical `EventsHash` + replay; sequential vs 8-thread parallel identical (this test runs the real world-sim path).
- `ReplayTests.fs` — write/read round-trip lossless (positions quantized ≤ 1 cm); snapshot interval honored; replay size < 25 MB (assert); UI consumption smoke: read → interpolate → no NaN.
- `StatisticalContractTests.fs` — **200 seeded matches**, assert ranges: goals 1.7–4.5 (avg ≈ 2.7), shots 20–36, on-target ≥ 40%, pass% 72–90, fouls 14–32, yellows 2–8, reds 0–1, corners 7–14, offsides 1–6, possession 30–70, home win 45–50% (draw 25–28%), xG total 2.0–5.0. Run nightly + on merge (CI: 50-match smoke).
- `WorldSimTests.fs` — the **seam** in action: `WorldRunner` (unchanged World code except the sim call site) plays a fixture week with Core; fixtures simulated in parallel, deterministic (same seed → same table); `MatchResult` → season state (injuries, cards, stats, coordination memory) matches hand-checked ground truth on 5 fixtures.
- `ProjectionTests.fs` — Domain `Player` → `PlayerProfile` mapping: every attribute mapped, no NaN, GK flag, role mapping table.
- `BaselineTests.fs` — the legacy engine's `trySimulateMatch` vs Core `runMatch` on the same fixture set: goal/shots/pass distributions within ±20% relative (same football, new engine — guards against a silent regression of *game feel*).

**Implementation:** `Simulator/Replay.fs` (delta encoding + quantization), `Simulator/Simulator.fs` (runFull), **the seam**: `WorldRunner` (in `Core/World/`) replaces its `trySimulateMatch`/`trySimulateMatchFull` calls with `Simulator.runMatch` + a `MatchOutcome -> MatchResult` projection (§4.1b) — *the only edit allowed inside `World/**` in the entire rewrite*; seeds derived from fixture id (`MatchSeed.ofMatch`); `docs/PERF.md` written with legacy baseline + new numbers; `Projection` real mapping; legacy `Simulator.fs`/`SnapshotBuilder.fs` stay in `Core/Legacy` for UI compatibility until P11.

**Acceptance:** all E2E green; perf targets from §3 met or documented with a gap plan; statistical contracts green; parallel determinism green.

---

### P10 — Config validation, coverage, calibration wiring  (≈ 2–3 agent-hours, 1 agent)

**Goal:** every knob is real, validated, and tuned.

**Test-first (`Tests/Config/`):**
- `ValidationTests.fs` — all 10 sections validated; each validator rejects its out-of-range cases (probability > 1, negative speed, empty windows); `ConfigIssue` carries path; `validateAll defaultConfig` = []. Old bugs as regression: performance section validated against the *actual* config object; shot validator non-empty.
- `CoverageTests.fs` — reflection walk of `BalanceConfig`: every leaf is either (a) reachable from a `SystemStep` test/impl (grep-based whitelist updated by packages) or (b) in `ConfigUnused` — which must be `[]` at P10 end. Regression: the 8 dead weights (C6) are either wired now or deleted from config.
- `WeightsE2ETests.fs` — `weights_trained.json` → merge → validate → run a seeded 500-tick match → no NaN, matches statistical smoke.
- `MigrationScriptTests.fs` — `Tools/MigrateWeights.fsx` renames old→new keys and the merged result matches hand-verified values for 20 named fields.

**Implementation:** `Config/Validation.fs` (full), coverage whitelist mechanism, migration script, `Training/` pipeline adapter (points at Core config; retrain smoke run in CI-nightly).

**Acceptance:** validation 100% sections; coverage list empty; Training smoke green.

---

### P11 — UI rewiring, benchmarks, deletion, calibration  (≈ 4–6 agent-hours, 1–2 agents)

**Goal:** the user-facing product runs on the new engine; the old engine is gone.

**Test-first (`Tests/UI/`):**
- `ReplayUiTests.fs` — playback: snapshot indexing, interpolation at speeds 1×/4×/16×, pause-flow slow-down, no index overflow at match end (the previously-untested path, T5).
- `StatsPanelTests.fs` — post-processed stats from `MatchEvent list` (shots, xG, possession, PPDA) match per-event ground truth on 5 seeded matches.
- `CommentaryTests.fs` — commentary text derived from `EventData` + club/player names; every `EventKind` produces a sentence (exhaustive); no engine-side text (the viewer owns language).
- `ClientE2ETests.fs` — world sim season week with Core: 20 matches parallel, results persist, injuries applied, replay loads in the viewer.

**Implementation:** `MatchSlice`/`MatchViewer` adaptation to `Replay.read`; **delete `Core/Legacy/**`** (old match engine — the full §2.2 list: `EventRouter`, `PassLauncher`, dead twins, legacy tests in `Tests/Legacy/`) + Client references to legacy match types; `Benchmarks/Program.fs` on Core with the §3 bench set; final calibration pass against `CalibrationTargets` (adjust defaults, re-export weights); `AGENTS.md`/`ARCHITECTURE` update: document the World Engine vs Match Engine distinction, the Core project layout, the seam, and describe the real code (kill the EventRouter ghost).

**Acceptance:** app runs a full user match (replay + commentary + stats); all tests green; perf targets green; `grep -r "Legacy" FootballEngine.Core/FootballEngine.Core.fsproj` → empty; **`git diff` of `Core/World/**` across the whole rewrite = only the P9 seam lines**; fantomas clean.

---

## 7. Timeline (realistic, multi-agent AI)

Bottleneck reality: F# full-solution build (~30–90 s) + test runs + integration fixups dominate wall-clock; parallel packages overlap only when contracts are frozen (that's what P1 buys). "1 hour" is not achievable for a 24,600-line engine rewrite with quality gates; the plan minimizes cycles so that ~45–60 **agent-hours** of focused work is the total.

| Phase | Human (solo) | AI agent-hours | Wall-clock, 4–6 agents | Depends on |
|---|---|---|---|---|
| P0 restructure + CI + **World move** | 2–4 days | 4–6 | 4–7 h | — |
| P1 contracts + config + merge | 4–5 days | 4–5 | 4–6 h | P0 |
| P2 ball core | 3–4 days | 3–4 | 3–5 h | P1 |
| P3 stepper/flow/scheduler | 4–6 days | 4–6 | 6–9 h | P1 |
| P4 steering ∥ P5 cognition ∥ P6 actions | 7–10 days (all three) | 12–17 (parallel) | 12–18 h (parallel) | P3 |
| P7 team intelligence | 4–6 days | 4–6 | 5–8 h | P3, P5 |
| P8 referee/manager | 3–5 days | 4–5 | 5–7 h | P6 |
| P9 simulator + **seam** + stats | 5–8 days | 5–7 | 8–12 h | P4–P8 |
| P10 config validation/coverage | 2–3 days | 2–3 | 3–4 h | P1, P9 |
| P11 UI + **legacy deletion** + bench | 4–6 days | 4–6 | 6–9 h | P9, P10 |
| **Total** | **5–8 weeks** | **~46–61** | **3–6 days** | |

Critical path: P0 → P1 → P3 → (P5 → P7 → P9) → P11 ≈ 45–55 agent-hours. All estimates assume the §5 protocol is followed strictly (frozen contracts, no silent divergence, merge gates). The World move adds ~2–3 agent-hours to P0 but sits off the critical path for the match-engine work (package A runs in parallel with package B).

---

## 8. Risk register

| Risk | Mitigation |
|---|---|---|
| Contract drift under parallel agents | P1 freeze + §5.6 list + CI greps + plan-PR rule |
| **World move breaks compile / changes behavior** | Move PR must be pure moves (diff review gate); World tests green unchanged at P0; `World/**` diff across the whole rewrite = only the P9 seam lines (checked at P11 acceptance) |
| **Namespace collision between new match code and moved World/Domain/legacy** | §5.6 namespace rule + CI greps (`open FootballEngine$`, `open FootballEngine.Domain` in `Match/**`) |
| Statistical contracts too tight → endless tuning loops | Ranges are football-realistic and mirror current `CalibrationTargets`; CI runs 50-match smoke, nightly full 200 |
| Perf targets missed | P3 measures from day 1; P9 has a gap-plan clause; SIMD-friendly SoA kept from day 1 |
| Legacy deletion breaks Client/World | Client + World compile against `Core/Legacy` until P9 (World seam) and P11 (deletion); P11 is a single coordinated PR with UI tests first |
| Training pipeline breaks on config renames | Migration script + WeightsE2E test in P1/P10; JSON section names preserved |
| Game feel regresses vs old engine | `BaselineTests` (±20% on distributions) in P9 — the engine must stay "the same football" |
| Multi-agent merge conflicts on shared dirs | Packages own disjoint dirs; contracts read-only; Conflict Agent = P1 agent |
| F# compile order pain | ≤ 40 files (match engine), explicit layered fsproj, contracts-first ordering; CI fails on order errors |

---

## 9. Final Definition of Done

1. `FootballEngine.Core` is the only engine: `Domain/**` + `World/**` moved byte-identical (World untouched except the P9 seam), `Core/Legacy/**` (old match engine) deleted, zero legacy references.
2. §3 acceptance criteria all green on `main` (determinism, perf, alloc, replay size, statistical contracts, invariants, readability greps, coverage, World-untouched).
3. All ~150 planned test modules green with seeded determinism; CI runs everything per merge + nightly full statistical suite.
4. `weights_trained.json` loads, merges, validates, and the Training pipeline retrains against Core.
5. `AGENTS.md` + `ARCHITECTURE` describe the actual code (no ghosts): the World Engine (game world) and the Match Engine (matches inside it) as two distinct layers, with the `MatchResult` seam between them.
6. A user can: simulate a season (parallel, deterministic), watch a match (replay + commentary + stats), change tactics mid-match (command channel works — B6 fixed).
7. `docs/PERF.md` shows legacy vs new numbers with the §3 targets met.