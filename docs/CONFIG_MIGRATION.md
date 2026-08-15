# Config migration — old BalanceConfig (Legacy) → new 10 sections (Core)

The old `BalanceConfig` (~720 fields in 22 section records + 16 ML weight sections) is
replaced by 10 sections in `FootballEngine.Core.Match.BalanceConfig`.
ML weights keep the exact `weights.json` section/field names (`Weights.*`).

## Section-level mapping

Every old section maps to exactly one new section.

| Old section (Legacy) | New section (Core) | Notes |
|---|---|---|
| `PhysicsConfig` | `Ball` | gravity, drag, bounce, max speeds, Magnus |
| `Interception` (fields in `BallConfig`-adjacent old code) | `Ball` | arrival gate, interception bonus |
| `OutfieldReceptionConfig` | `Ball` | reception grace, contest gap |
| `GKConfig` | `Ball` (save side) + `Actions` (distribution side) | save reflex/positioning; punch/parry, distribution pressure |
| `PerformanceConfig` | `Movement` | sprint/jog/walk speeds, acceleration |
| (separation/smoothing, old `BallPhysics` constants) | `Movement` | separation radius/strength, turning, smoothing |
| (fatigue decay, old `ConditionSystem` constants) | `Movement` | condition decay/recovery, fatigue speed decay |
| (scorer thresholds, old scorer modules) | `Movement` | intent floor, hysteresis |
| `PassConfig` | `Actions` | base quality, distance penalty, vision/technical weights, jitter |
| `ShotConfig` | `Actions` | quality gate, angle spread, Vz, distance multiplier |
| `DuelConfig` | `Actions` | steepness, momentum bonus, foul chance |
| `TackleConfig` | `Actions` | tackle base success |
| `CrossConfig` | `Actions` | lateralness, target radius |
| `DribbleConfig` | `Actions` | dribble success, risk weight |
| `DecisionConfig` | `Actions` | softmax temperature |
| `IndividualWeights` | `Weights` | per-attribute weights (ML) |
| `BuildUpConfig` | `Actions` | build-up knobs folded into pass/dribble knobs |
| `SetPieceConfig` | `SetPiece` | delays, wall, penalty bonus, corner radius |
| `CollectiveWeights` | `Team` | utility weights, cohesion |
| `UtilityWeights` | `Team` | utility weights |
| `MatchSituationConfig` | `Team` | blackboard windows (opponent shape, pressure, transition) |
| `InfluenceConfig` | `Team` | threat/weakness zones |
| `TeamDirectorConfig` (in `TeamDirector` module) | `Team` | directive hysteresis, shape params |
| `ReactiveConfig` | `Team` | reactive radii folded into `Movement.ReactiveRadius` |
| `WinProb` weights | `Team` | urgency/deficit |
| `FoulAnalysis` constants | `Referee` | severity chances, last-man/promising/zone factors |
| `RefereeWeights` | `Referee` | card thresholds, advantage window |
| `HomeAdvantageConfig` | `Referee`, `Environment` | card parts → `HomeCardBonus`; crowd, familiarity |
| `ManagerConfig` | `Manager` | sub windows, thresholds |
| `TimingConfig` | `Manager` / `Match` | sub timing → `Manager`; stoppage/volume timing → `Match` |
| `MatchVolumeConfig` | `Match` | stoppage, importance |
| (weather hooks) | `Environment` | weather, pitch quality |
| `PerceptionConfig` | `Movement` / `Team` | visibility radii folded into movement/team windows |
| `DiagnosticsConfig` | — | deleted (engine emits `MatchEvent`; diagnostics are viewers) |
| `BalanceConfig` (top) | `BalanceConfig` | top record |

## Weights sections (weights.json)

Section names are preserved 1:1 under `Weights`:
`Duel, Shot, Pass, Cross, Dribble, Tackle, SetPiece, GK, HomeAdvantage, Physics, Timing,
MatchVolume, Manager, BuildUp, Decision, Perception` — field names identical to `weights.json`.
Renamed leaves (if any) get `Tools/MigrateWeights.fsx` (P10).

## Spot-check mappings (old field → new field)

| Old field | New field |
|---|---|
| `PhysicsConfig.Gravity` | `Ball.Gravity` |
| `PhysicsConfig.MaxShotSpeed` | `Ball.MaxShotSpeed` |
| `PhysicsConfig.BounceRestitution` | `Ball.BounceRestitution` |
| `PassConfig.BaseMean` | `Weights.Pass.BaseMean` |
| `ShotConfig.QualityGate` | `Weights.Shot.QualityGate` |
| `DuelConfig.DuelSteepness` | `Weights.Duel.DuelSteepness` |
| `GKConfig.PunchChance` | `Actions.GkPunchChance` |
| `SetPieceConfig.WallDistance` | `SetPiece.WallDistance` |
| `ManagerConfig.MaxSubs` | `Manager.MaxSubs` |
| `MatchVolumeConfig.StoppageBase` | `Match.StoppageBasePerHalf` |
| `HomeAdvantageConfig.CrowdSupport` | `Environment.CrowdInfluence` |
| `FoulAnalysis.YellowThreshold` | `Referee.FoulYellowWorthyChance` |