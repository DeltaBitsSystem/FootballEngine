<div align="center">

# Football Engine 2026

**A deterministic, offline football management simulation built entirely in F#.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](#)
[![F#](https://img.shields.io/badge/F%23-10.1-378BBA)](#)
[![AvaloniaUI](https://img.shields.io/badge/Avalonia-11.3-purple)](#)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

</div>

---

## Why Football Engine

**Results have causes.** A 2-1 is a chain of linked decisions: a corridor the winger keeps finding, a defensive line that dropped as fatigue built up, a counter-plan your rival prepared after studying your last match. Every event in the engine is resolved from the state of the game -- never from a dice roll.

**Deterministic and fully offline.** One self-contained executable. No installer, no internet connection, no external services. The same seed produces the same match, replayable and reproducible -- which also makes every AI decision auditable.

**The adaptive coach AI learns from you.** The active P0 program builds an adaptive rival: it studies your patterns -- preferred corridors, pass length, formation, instructions -- keeps that memory between matches with temporal decay in the local database, and adjusts its match plan before facing you. You receive the same tool, but biased: your staff's reports carry the noise of real scouting, while the AI reads the truth. Difficulty emerges from the rival's intelligence, not from artificial modifiers. Your rival remembers how you beat them last week; you have to innovate, like in real football. The counter-plan is a pure function of persisted history: same seed plus same history still produces the same match. See [docs/ADAPTIVE_AI_PLAN.md](docs/ADAPTIVE_AI_PLAN.md).

**A market that buys well.** The transfer AI signs by need: role coverage, real minutes played, value discipline. No redundant 29-to-31-year-old purchases piling up on the bench.

## Overview

Football Engine resolves matches through a full 2D spatial simulation running at 40Hz on a 105x68 metre pitch. Players move with steering behaviours, perceive the field through limited vision cones, and make cognitive decisions based on their attributes, tactical instructions, and the current state of play. The ball follows aerodynamic physics including drag, ground friction, restitution, and Magnus effect spin.

The world layer -- transfers, player development, finances, season progression -- runs on the same principles: deterministic, data-driven, no dice rolls.

Everything runs offline. A single self-contained executable. No installer, no internet connection, no external dependencies.

## Quick Start

```bash
git clone https://github.com/DeltaBitsSystem/FootballEngine.git
cd FootballEngine
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release
```

Requires [.NET 10.0 SDK](https://dotnet.microsoft.com/download). League data is bundled as JSON and loads automatically.

## Screenshots

<div align="center">

| | |
|---|---|
| <img src="screenshots/1.png" width="400"> | <img src="screenshots/2.png" width="400"> |
| <img src="screenshots/3.png" width="400"> | <img src="screenshots/4.png" width="400"> |

</div>

## Match Engine

### Ball Physics

The ball is a physical object with position, velocity, and spin in three dimensions.

- Air drag decelerates the ball proportionally to speed
- Ground friction applies when the ball is rolling
- Ground restitution determines bounce height
- Magnus effect curves balls with sidespin or topspin
- Goal posts deflect the ball with configurable restitution
- Stop threshold prevents micro-movement jitter

### Player Movement

Steering behaviours evaluated every tick against a shape grid derived from the active formation.

| Behaviour | Use |
|-----------|-----|
| Seek | Move to target position |
| Arrive | Decelerate approaching target |
| Pursuit | Intercept moving targets (ball, opponent) |
| Separation | Avoid overlapping teammates |
| Shape maintenance | Hold formation position when idle |

Off-ball runs trigger from possession state, space detection, or tactical instruction: deep runs, overlaps, underlaps, diagonal runs, third-man runs, false nine drops.

### Cognitive Processing

Runs on a separate, lower-frequency cycle. Each player computes:

- A visibility mask based on position and facing direction
- A field-of-view cone scaled by Vision and Positioning attributes
- Passing targets, press triggers, and spatial coverage from perceptual state, not omniscient knowledge

The decision pipeline flows through: CollectiveModifiers -> CollectiveIntents -> SlotRole -> DefensiveRole -> EmergentState -> pickIntent. Each stage applies multiplicative weights; the highest-scoring intent wins.

### Spatial Awareness

A 10x7 influence grid (cells ~10.5m x 9.7m) accumulates Gaussian-weighted presence from all players. Sigma varies by position: GK 10m, centre-backs 12m, midfielders 15m, attackers 18m. Derived frames provide pass safety, defender coverage, and territorial control from each team's perspective.

### Tactical Adaptation

Emergent feedback loops track pass success rate, press success rate, and flank success rate during the match. These nudge compactness, pressing intensity, tempo, and wing-play preference in response. A team leading comfortably plays differently from the same team chasing a goal in the 80th minute.

### Fatigue and Condition

Condition drains during the match. A team on a losing run with heavy training load enters matches with degraded compactness, pressing intensity, tempo, and risk appetite -- modelled as cascading emergent state penalties.

### Set Pieces and Officiating

Dedicated agent handles corners, free kicks, throw-ins, goal kicks, and penalties. Offside is checked at pass moment via snapshot. The referee system includes foul analysis, advantage detection, handball detection, and VAR review.

## Player Attributes

Players have 30+ attributes across four groups:

| Group | Attributes |
|-------|-----------|
| Physical | Acceleration, Pace, Agility, Balance, Jumping Reach, Stamina, Strength |
| Technical | Finishing, Long Shots, Dribbling, Ball Control, Passing, Crossing, Tackling, Marking, Heading, Free Kick, Penalty |
| Mental | Aggression, Composure, Vision, Positioning, Bravery, Work Rate, Concentration, Leadership |
| Goalkeeping | Reflexes, Handling, Kicking, One-on-One, Aerial Reach |

Attributes generate a BehavioralProfile -- a continuous representation of how each player naturally moves and decides. The profile feeds directly into steering targets, risk tolerance, pass selection, and pressing triggers.

## Player Development

Development is probabilistic, age-gated, and attribute-sensitive. Players under 20 can gain up to 4 skill points per cycle; this ceiling drops with age, reaching 0 after 30. Weekly development probability scales by training focus, training intensity, the gap between current skill and potential, and the player's behavioral profile. Heavy training increases development rate but raises injury risk and damages morale.

## Transfers and Negotiations

Transfers run as a finite state machine: offer -> club response -> contract offer -> player response. Club response evaluates fee against market value (quadratic in skill) and buyer reputation. Player response evaluates club reputation and offered salary. Both sides can reject independently.

## Staff

Thirteen distinct roles: Head Coach, Assistant Manager, First Team Coach, Goalkeeper Coach, Fitness Coach, Head of Youth Development, Scout, Physio, Sports Scientist, Performance Analyst, Recruitment Analyst, Loan Manager, Technical Director. Each has an attribute set and effective ability formula. Coach quality affects training output, squad development, and tactical execution.

## Board Objectives

The board assigns objectives: Survival, Mid-Table, Top Half, Top Four, Win League, Promotion, Domestic Cup, Continental Cup. Meeting them builds coach reputation. Failing twice in a row costs the job.

## Competitions and Bundled Data

Six data mods ship with the engine:

| Mod | Content |
|-----|---------|
| Argentina | League, cup, name pools |
| Brazil | League, cup, name pools |
| England | League, cup, name pools |
| Spain | League, cup, name pools |
| CONMEBOL | Libertadores, Sudamericana |
| UEFA | Champions League, Europa League |

Competition formats: domestic leagues with configurable points and tiebreakers, straight knockout, group-then-knockout, two-legged ties with configurable resolution (away goals, extra time, penalties, replays).

## Mod System

All database content is decoupled from the executable. Nations, leagues, cup formats, tiebreaker rules, promotion/relegation slots, qualification paths, and name pools are distributed as validated `.femod` packages loaded at startup.

The mod system validates loaded data against a strict schema. Errors surface with precise messages: invalid JSON, schema version mismatch, duplicate content ID, missing dependency, path traversal attempt, oversized file, or invalid package hash.

A built-in mod editor allows creation, modification, and patching of data from within the application.

## Architecture

Football Engine enforces a functional, immutable-by-default architecture. The match simulation hot path uses mutable `TeamFrame` arrays in Structure-of-Arrays layout for cache performance at 40Hz.

```
Match simulation (40 Hz) -- FootballEngine.Core/Match
  |-- Stepper
  |     |-- SystemsTable (Ball, Players, Referee, Team, Manager)
  |     |-- EventApply, Flow, Scheduler, Shootout
  |-- Systems
  |     |-- Ball      # possession, contacts, outcomes, physics
  |     |-- Players   # Perception -> Decision -> Intent -> Planner -> Steering -> Actions
  |     |-- Referee   # fouls, cards, VAR
  |     |-- Team      # Decide, TeamEmergent, Formation, Pressing, InterceptTable
  |     └-- Manager   # substitutions, directives
  |-- Contracts       # MatchContext, BalanceConfig, MatchEvent, Frames, Rng, Units
  └-- Simulator       # Simulator, Replay, MatchDump

World layer (daily / weekly / monthly) -- FootballEngine.Core/World
  |-- Phases         # SeasonPhase, MatchPhase, TransferPhase, DevelopmentPhase, FinancePhase
  |-- Agents         # ClubAgent, ManagerAgent, PlayerAgent
  |-- Supervisors    # Finance, Transfer, Youth
  └-- Core           # WorldClock, WorldTick, agent types
```

| Layer | Technology |
|-------|------------|
| Language | F# (.NET 10), Units of Measure for physics |
| UI | Avalonia FuncUI + Elmish (MVU, unidirectional data flow) |
| Rendering | SkiaSharp via ICustomDrawOperation for live pitch |
| Statistics | Deterministic distributions (Normal, Beta, Poisson) seeded from the match RNG |
| Persistence | SQLite (sqlite-net-pcl), transactional save/load |
| Error Handling | Result types and FsToolkit.ErrorHandling |

### Project Structure

```
FootballEngine/
├── FootballEngine.Core/          # Engine and domain, no UI
│   ├── Domain/                   # Player, Club, Staff, Transfer, MatchMetrics, CoordinationMemory
│   ├── Match/                    # 40 Hz match simulation
│   │   ├── Contracts/            # MatchContext, BalanceConfig, MatchEvent (29 kinds), Frames, Rng
│   │   ├── Config/               # ConfigValidation, weight merging
│   │   ├── Stepper/              # SystemsTable, MatchFlow, MatchClock
│   │   ├── Simulator/            # Simulator, Replay, match dump
│   │   └── Systems/              # Ball, Players, Referee, Team, Manager
│   ├── World/                    # Season, match, transfer, development, finance phases
│   │   ├── Agents/               # ClubAgent, ManagerAgent, PlayerAgent
│   │   ├── Phases/               # SeasonPhase, MatchPhase, TransferPhase, DevelopmentPhase, FinancePhase
│   │   └── Supervisors/          # Finance, Transfer, Youth supervisors
│   ├── WorldGeneration/          # Player, club and fixture generation
│   └── GameData/                 # .femod resolution and resolved snapshots
│
├── FootballEngine.Client/        # Avalonia FuncUI + Elmish desktop application
│   ├── AppState/                 # Elmish model, messages, update functions, slices
│   ├── Database/                 # SQLite entities, mappers, serializers, repository
│   └── Views/                    # Pages, components, shared controls
│
├── FootballEngine.Core.Tests/    # Expecto test suite
├── FootballEngine.Benchmarks/    # Deterministic benchmark + CPU/GC profiling
├── Data/Builtins/                # Six bundled .femod packages
└── Training/                     # Weight calibration pipeline (outside the solution)
```

## Testing

```bash
dotnet fsi scripts/test.fsx
dotnet fsi scripts/test.fsx --project Core --list
dotnet fsi scripts/test.fsx --project Client
```

Local development and CI use the same SDK-only F# runner. It builds and runs all source `*.Tests.fsproj` executables with Expecto, rejects empty/focused/skipped runs and saves JUnit reports and logs under `artifacts/tests/`. See [testing commands and policy](docs/TESTING.md). Benchmarks are separate:

```bash
dotnet run --project FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release
dotnet run --project FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release -- --profile
```

The benchmark runner reports median/p95 time, allocations, GC collections, event/tick counts, replay size and same-seed determinism. `--profile` additionally writes CPU/GC trace files and per-system timings under `artifacts/benchmarks/<run>/`.

### Deterministic match video

```bash
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release -- \
  --match-video artifacts/match-motion.mp4 \
  --video-fps 30 --video-step 4 --video-frames 3600 \
  --shots-size 1600x900
```

`--video-step` controls how many replay snapshots advance per encoded frame. Lower values make movement easier to inspect; higher values cover more match time. Frames come from the fixed screenshot career and replay seed rather than wall-clock recording, so repeated runs remain comparable. The workflow requires `ffmpeg` on `PATH` and removes temporary PNGs after successful encoding.

The test suite covers physics invariants (ball trajectory, restitution), offside detection, match determinism (same seed produces identical scoreline and events), and world state integrity (no orphaned player IDs, no budget underflow, valid standing computations).

## Roadmap

The roadmap is organized around one principle: the AI plays the same game the human plays -- same information, same incentives, same consequences. Difficulty comes from the AI's intelligence, never from artificial modifiers.

- **P0 -- Adaptive coach AI (Phase 1):** cross-match memory. The AI extracts your patterns (corridors, pass length, formation, instructions), persists them with temporal decay, and counter-plans before each match. You get the same tool, biased by your staff. Parallel track: a transfer AI that always buys well -- need by role, real minutes, value discipline -- with a 10-season CI test.
- **P1:** Phase 2 (live in-match adaptation), plus manager AI: distinct philosophies, causal morale, causal market value, load-driven injuries, modern newgen archetypes.
- **P2:** advanced statistics (xG, xA, pass maps, heatmaps -- unlocked by event positions), finances dashboard, match ratings, staff hiring, set piece designer, youth academy.
- **P3:** community: more league mods, exportable match data, documentation for mod authors.

Every item closes with a test in `FootballEngine.Core.Tests` -- balance is measured, not felt. Full detail: [ROADMAP.md](ROADMAP.md).

## Contributing

The mod system is the most accessible entry point. Add a workspace with `manifest.json` and declarative content, validate it with `Tools/ModCli`, and pack it into an immutable `.femod` package.

For code contributions: fork the repository, create a branch, and open a pull request. The test suite must pass. For larger changes, open an issue first to discuss the approach.

## License

Football Engine is free software under the GNU General Public License v3.0. See [LICENSE](LICENSE) for the full text.

The source code is open. The GPL guarantees that any distributed modified version must also be open source.
