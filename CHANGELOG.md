# Changelog

All notable changes in FootballEngine.

The format is based on [Keep a Changelog](https://keepachangelog.com/es/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/lang/es/).

## [Unreleased]

### Added
- AGENTS.md — contributor and AI guide
- CONTRIBUTING.md — contribution guide
- .editorconfig — formatting rules
- .github/workflows/ci.yml — CI pipeline with build, test, and dependency audit
- global.json — SDK version pinning
- Save file schema versioning and automatic backup
- Migrations.fs — save file migration system
- Build scripts (build.sh, build.ps1, publish.sh)
- Pre-commit hook for Fantomas

### Fixed
- Crash on Scouting, Finances, and Settings pages (failwith "todo")
- Crash in Simulator.fs due to failwithf inside result{}
- Crash in DuelAction.fs due to failwith on empty roster
- Silent exception swallowing in UpdateSim.fs
- Debug printfn removed from production

### Changed
- Full formatting with Fantomas v7.0.5 (153 files)
- Debug printfn replaced with eprintfn

## [0.1.0] - 2026-XX-XX

### Added
- Deterministic 2D match simulation engine
- World generation with players, clubs, and competitions
- Transfer system with FSM
- VAR and foul analysis
- Training and ML weight calibration
- Graphical interface with Avalonia (FuncUI + Elmish)
- Cross-platform support (Windows, Linux, macOS)
- JSON mod system
