# Changelog

All notable changes to FootballEngine. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

First public snapshot after months of private development.

### Added

* Deterministic match engine: 22 players that perceive, decide and move at 40 Hz, with ball flight, spin and bounce.
* 3D match viewer on WebGPU with broadcast, tactical, behind goal, touchline and close up cameras.
* Career mode: calendar, squad, tactics, training, scouting, transfers, finances, board, news and inbox.
* Rival managers with personalities who study your team before facing you.
* Key-moment pacing that stops the match for chances and manager signals.
* Match replays: watch any played match again and jump to any moment through a timeline of goals, saves, set pieces, cards and substitutions.
* Mod editor with countries, leagues, cups, clubs, kits and squads, plus publishing and a library.
* Calibration CLI (`Training`) that validates results on held-out matches.
* English and Spanish interface.

### Changed

* A match now simulates in 2.8 s on one core, down from 5.0 s.
