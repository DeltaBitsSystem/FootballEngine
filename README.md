<div align="center">

# Football Engine 2026

**A deterministic, offline football management simulation built entirely in F#.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](#)
[![F#](https://img.shields.io/badge/F%23-10.1-378BBA)](#)
[![AvaloniaUI](https://img.shields.io/badge/Avalonia-11.3-purple)](#)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

</div>

---

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

All database content is decoupled from the executable. Nations, leagues, cup formats, tiebreaker rules, promotion/relegation slots, qualification paths, and name pools are defined in JSON files loaded at startup.

The mod system validates loaded data against a strict schema. Errors surface with precise messages: invalid JSON, schema version mismatch, duplicate country code, duplicate competition name, missing dependency, path traversal attempt, oversized file.

A built-in mod editor allows creation, modification, and patching of data from within the application. 

## Architecture

Football Engine enforces a functional, immutable-by-default architecture. The match simulation hot path uses mutable `TeamFrame` arrays in Structure-of-Arrays layout for cache performance at 40Hz.

```
MatchStepper (40Hz)
  |-- BallPhysics (Magnus, drag, restitution, post collision)
  |-- PlayerAgent
  |     |-- Perception (FOV, visibility mask)
  |     |-- Cognition (intent pipeline, influence maps)
  |     |-- Steering (seek, pursue, separate, shape)
  |     |-- Actions (pass, shot, cross, duel, set piece)
  |-- Referee (fouls, advantage, VAR, handball, offside)
  |-- TeamDirector / TeamExecutor (shape, tactics, blackboard)
  |-- ManagerAgent (substitutions)

World Engine (daily / weekly / monthly tick)
  |-- Transfers (FSM)
  |-- Development (probabilistic, age-gated)
  |-- Finance
  |-- Season progression
```

| Layer | Technology |
|-------|------------|
| Language | F# (.NET 10), Units of Measure for physics |
| UI | Avalonia FuncUI + Elmish (MVU, unidirectional data flow) |
| Rendering | SkiaSharp via ICustomDrawOperation for live pitch |
| Statistics | FSharp.Stats -- Normal, Beta, Poisson distributions |
| Persistence | SQLite (sqlite-net-pcl), transactional save/load |
| Error Handling | Result types and FsToolkit.ErrorHandling |

### Project Structure

```
FootballEngine/
├── FootballEngine.Client/
│   ├── AppState/          # Elmish model, messages, update functions
│   ├── Database/          # SQLite entities, mappers, repository
│   ├── Domain/            # Core types: Player, Club, Competition, Transfer, Staff
│   ├── Engine/
│   │   ├── Match/         # Simulator, ball physics, player agents, referee, set pieces
│   │   └── World/         # Season phases, transfer logic, development, finance
│   ├── GameData/          # JSON mod loader, validator, registry, mod editor
│   └── Views/             # Avalonia FuncUI pages and components
│
├── FootballEngine.Tests/
│   ├── Layer1_E2E/        # Flow transitions, GK distribution, goal cycle, set pieces
│   ├── Layer2_Systems/    # Adaptive tactics, ball system, chemistry, VAR, handball
│   └── Layer3_EdgeCases/  # GK, pass, shot, personality edge cases
│
└── FootballEngine.Benchmarks/  # BenchmarkDotNet profiling
```

## Testing

```bash
dotnet run --project FootballEngine.Tests/FootballEngine.Tests.fsproj
dotnet run --project FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release
```

The test suite covers physics invariants (ball trajectory, restitution), offside detection, match determinism (same seed produces identical scoreline and events), and world state integrity (no orphaned player IDs, no budget underflow, valid standing computations).

## Roadmap

**Completed**
- 40Hz physics engine with Magnus effect
- Cognitive processing with limited vision and FOV
- Influence maps with position-aware Gaussian kernel
- Adaptive tactics with emergent feedback loops
- VAR, handball detection, advantage engine
- Mod system with schema validator and in-app editor
- Six bundled data mods (ARG, BRA, ENG, ESP, CONMEBOL, UEFA)

**In Progress**
- Improved goalkeeper AI
- Live commentary engine

**Planned**

Core Features
- Finances dashboard -- budget, wages, matchday income, sponsorship (data exists, UI pending)
- Player match ratings and performance statistics
- Staff hiring, search, and assignment interface
- Contract renewal negotiations

Match Intelligence
- Influence map overlay on the match pitch (territorial control, pass safety, threat zones)
- Advanced match stats: xG, xA, pass maps, player heatmaps
- Causal match analysis -- trace why results happened, not just what happened
- Behavioral profile visualization per player

Gameplay Depth
- Scouting system with knowledge areas and player recommendations
- Set piece designer -- customize corners and free kick routines
- Youth academy page with annual intake and facility ratings
- Player interaction and morale meetings
- Loan system with recall and management

Community
- Expanded league mods contributed by the community
- Exportable match data and statistics API
- Documentation for mod authors

## Contributing

The mod system is the most accessible entry point. Adding a new league requires only JSON files following the schema in `Data/Builtins/`.

For code contributions: fork the repository, create a branch, and open a pull request. The test suite must pass. For larger changes, open an issue first to discuss the approach.

## License

Football Engine is free software under the GNU General Public License v3.0. See [LICENSE](LICENSE) for the full text.

The source code is open. The GPL guarantees that any distributed modified version must also be open source.
