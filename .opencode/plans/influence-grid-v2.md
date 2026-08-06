# Influence Grid V2 — Complete Rewrite

## Goal

Replace the current influence grid with a system that models how real football teams control space. The current grid treats all players identically, ignores velocity, has no ball influence, and produces flat/unrealistic spatial decisions. The new system must be **better than Football Manager** at modeling off-ball intelligence, pressing, and space creation.

**Design constraints:**
- No backward compatibility — we break and replace
- No redundancy — one grid, one computation, one data flow
- Pragmatic — every field and function earns its place
- Durable — designed to last years, not a quick patch

---

## 1. Architecture: One Grid, Not Two

### Problem

Currently: `HomeInfluenceFrame` and `AwayInfluenceFrame` are computed independently with identical algorithms. Each stores `HomeGrid`, `AwayGrid`, `ContestedGrid`, `ControlGrid`, `AttackerPassSafety`, `DefenderCoverage`. This is 6 arrays × 2 frames = 12 arrays of 70 floats. Most are redundant because `ContestedGrid = HomeGrid - AwayGrid` and `ControlGrid` is derived.

### Solution

A single `InfluenceFrame` stored once in `SimState`. It contains exactly 4 arrays:

```
InfluenceFrame =
    HomeGrid:     float32[70]    // raw home influence per cell
    AwayGrid:     float32[70]    // raw away influence per cell
    NetGrid:      float32[70]    // HomeGrid[i] - AwayGrid[i]
    TotalGrid:    float32[70]    // HomeGrid[i] + AwayGrid[i]
```

`AttackerPassSafety`, `DefenderCoverage`, `ControlGrid` are **deleted**. They are computed on-demand by pure helper functions that read `HomeGrid`, `AwayGrid`, `NetGrid`, `TotalGrid`. This eliminates 3 redundant arrays and removes pre-computed semantics that lock interpretation.

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/InfluenceTypes.fs` | Rewrite `InfluenceFrame` record, keep grid constants, add pure query helpers |
| `Engine/Match/Player/Perception/InfluenceTypes.fs` | **Delete** — duplicate of the above |
| `Engine/Match/Core/SimState.fs` | Replace `HomeInfluenceFrame` + `AwayInfluenceFrame` with single `InfluenceFrame` field |
| `Engine/Match/Core/MatchStepper.fs` | Compute one `InfluenceFrame` instead of two `computeInto` calls |

---

## 2. Dynamic Sigma — Player Quality Shapes Space

### Problem

`PlayerSigma = 15.0f` is hardcoded. A world-class midfielder and a youth prospect produce identical spatial influence. In real football, a player's "control radius" depends on their positioning ability, anticipation, and role.

### Solution

Each player gets a per-cell weight that varies by their attributes. The sigma itself is also dynamic:

```fsharp
let playerSigma (positioning: int) (anticipation: int) (vision: int) : float32 =
    let base = 10.0f
    let attributeBonus =
        0.5f * (float32 positioning / 20.0f)
        + 0.3f * (float32 anticipation / 20.0f)
        + 0.2f * (float32 vision / 20.0f)
    base + attributeBonus * 8.0f   // range: 10m (worst) to 18m (elite)
```

This means:
- Worst player (all stats 4): σ = 10m — controls very little space
- Average player (all stats 10): σ = 14m — decent coverage
- Elite player (all stats 18): σ = 17.6m — dominates a zone
- World-class (all stats 20): σ = 18m — maximum reach

**Positioning** is the primary factor (a player who reads the game well controls more space). **Anticipation** matters for anticipating where play will go. **Vision** is secondary but reflects awareness.

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/BalanceConfig.fs` | Add `InfluenceConfig` record with sigma parameters |
| `Engine/Match/Player/Perception/InfluenceFrame.fs` | Replace hardcoded `PlayerSigma` with `playerSigma` function, pass player attributes |

---

## 3. Velocity-Directed Influence — Movement Creates Space

### Problem

Current influence is isotropic (same in all directions). A player sprinting forward should project more influence in their movement direction. A pressing group moving as a unit should show a "wave" of influence, not individual circles.

### Solution

The Gaussian kernel gets a directional bias based on velocity:

```fsharp
let velocityWeight (vx: float32) (vy: float32) (toCellX: float32) (toCellY: float32) (sigma: float32) : float32 =
    let speed = sqrt (vx * vx + vy * vy)
    if speed < 0.5f then 1.0f  // stationary = isotropic
    else
        let dirX = vx / speed
        let dirY = vy / speed
        let cellLen = sqrt (toCellX * toCellX + toCellY * toCellY)
        if cellLen < 0.01f then 1.0f
        else
            let alignment = (dirX * toCellX + dirY * toCellY) / cellLen
            // alignment ranges from -1 (opposite) to +1 (same direction)
            // Influence expands forward, contracts behind
            1.0f + alignment * 0.4f
```

This creates:
- A player sprinting forward projects ~40% more influence ahead
- A player retreating projects less influence forward
- A pressing group creates a directional "front" of influence

The velocity bias is multiplied into the Gaussian weight before adding to the grid. This is physically correct: a moving player's "zone of control" shifts in their direction of travel.

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Player/Perception/InfluenceFrame.fs` | Add velocity-weighted kernel, read `VelX[]`/`VelY[]` from `PhysicsFrame` |

---

## 4. Ball Gravity — The Pitch Revolves Around the Ball

### Problem

The ball has zero influence on the grid. In real football, the ball is the center of gravity — players converge toward it, space around it is the most contested, and the grid should reflect this.

### Solution

The ball itself adds influence to the grid, creating a "gravity well":

```fsharp
let ballGravityFactor = 0.6f  // ball contributes 60% of an average player's influence
let ballSigma = 20.0f          // ball's influence spreads wider than a single player
```

The ball is treated as a "virtual player" with:
- Position: current ball position
- Sigma: 20m (ball draws attention from further away)
- Weight: 0.6 (significant but not overwhelming)

This means:
- Space near the ball is always "contested" (both teams converge)
- Space far from the ball is "open" (less total influence)
- Pressing near the ball feels natural because both grids peak there

The ball influence is added to **both** `HomeGrid` and `AwayGrid` equally — the ball doesn't belong to either team for influence purposes; it's a shared attractor.

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/BalanceConfig.fs` | Add `BallGravityFactor: float` and `BallGravitySigma: float` to `InfluenceConfig` |
| `Engine/Match/Player/Perception/InfluenceFrame.fs` | Add ball as virtual player in the accumulation loop |

---

## 5. CoverSpace — Read the Neighborhood, Not Just the Cell

### Problem

`coverSpaceScore` (`MovementScorer.fs:175-209`) only reads the `ContestedGrid` value at the player's current cell. A defender standing in a "neutral" cell gets zero bonus even if the adjacent cells are heavily under opponent control.

### Solution

Replace the single-cell read with a neighborhood scan:

```fsharp
let coverSpaceScore (ctx: AgentContext) =
    // ... existing base score logic unchanged ...

    let influenceBonus =
        let myCell = posToCell
            ctx.Team.OwnFrame.Physics.PosX[ctx.MeIdx]
            ctx.Team.OwnFrame.Physics.PosY[ctx.MeIdx]

        let mutable deficitSum = 0.0
        let mutable count = 0

        // Check current cell + all 8 neighbors
        for neighbor in neighbors myCell do
            let net = ctx.Influence.NetGrid[neighbor]
            // Negative net = opponent controls this cell
            if net < 0.0f then
                deficitSum <- deficitSum + float (-net)
            count <- count + 1

        // Also check current cell
        let myNet = ctx.Influence.NetGrid[myCell]
        if myNet < 0.0f then
            deficitSum <- deficitSum + float (-myNet)
        count <- count + 1

        let avgDeficit = if count = 0 then 0.0 else deficitSum / float count
        let gapRead = normStat ctx.Me.Mental.Positioning

        avgDeficit * 0.20 * gapRead

    baseScore + influenceBonus
```

This means defenders:
- React to pressure in their **zone**, not just their exact position
- Higher Positioning stat = better at reading spatial deficits
- Naturally cluster toward areas the opponent controls

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Player/Decision/MovementScorer.fs` | Rewrite `coverSpaceScore` influence section to use neighbor scan |

---

## 6. scoreCellRaw — Invert the Distance Logic

### Problem

`scoreCellRaw` (`InfluenceTypes.fs:106-147`) penalizes cells close to the player (`dist < 5.0 → 0.2`). This is backwards — space close to the player is the most useful for receiving a pass. Space too far is useless because the player can't reach it.

### Solution

Replace the distance scoring with a bell curve that peaks at a useful distance:

```fsharp
let distScore =
    if dist < 3.0 then 0.3       // too close — ball is at feet, not "space"
    elif dist < 15.0 then 0.8    // sweet spot — reachable in a few seconds
    elif dist < 30.0 then 1.0    // ideal — good through ball range
    elif dist < 50.0 then 0.5    // far — possible but risky
    else 0.1                     // very far — unlikely to reach
```

Also adjust the weights to reflect real football priorities:

```fsharp
safetyScore * 0.30        // pass safety — can we reach this cell?
+ opennessScore * 0.25    // how open is it?
+ contestedBonus * 0.15   // do we have advantage here?
+ forwardBonus * 0.20     // how close to goal? (increased from 0.15)
+ distScore * 0.10        // distance from me (reduced from 0.15)
```

Forward bonus gets more weight because in real football, the most valuable space is toward the opponent's goal.

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/InfluenceTypes.fs` | Rewrite `scoreCellRaw` distance logic and weights |

---

## 7. DefenderCoverage — Dual-Sided

### Problem

`DefenderCoverage` is computed as `1.0 - (awayInfluence / total)` — it only considers attacker presence. A zone with 3 defenders and 1 attacker looks "well covered" but a zone with 0 defenders and 0 attackers also returns 0.0, which is correct but the metric doesn't distinguish between "empty and safe" vs "empty and dangerous."

### Solution

Delete the pre-computed `DefenderCoverage` array entirely. Replace with a pure query function that consumers call on-demand:

```fsharp
/// How well defended is this cell from the perspective of `clubSide`?
/// Returns 0.0 (undefended) to 1.0 (heavily defended)
let cellDefensiveStrength (cell: int) (clubSide: ClubSide) (influence: InfluenceFrame) : float =
    let our = if clubSide = HomeClub then influence.HomeGrid[cell] else influence.AwayGrid[cell]
    let opp = if clubSide = HomeClub then influence.AwayGrid[cell] else influence.HomeGrid[cell]
    let total = our + opp
    if total < 0.01f then 0.0
    else float our / float total

/// How dangerous is this cell from the attacker's perspective?
/// Considers both openness AND defensive weakness.
let cellDangerScore (cell: int) (attackingSide: ClubSide) (influence: InfluenceFrame) : float =
    let oppCoverage = cellDefensiveStrength cell (ClubSide.flip attackingSide) influence
    let openness = 1.0 - oppCoverage
    let net = if attackingSide = HomeClub then influence.NetGrid[cell] else -influence.NetGrid[cell]
    let advantage = max 0.0 (float net) * 0.3
    openness + advantage
```

This gives consumers the semantic they actually need without pre-baking interpretation.

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/InfluenceTypes.fs` | Remove `DefenderCoverage` from record, add `cellDefensiveStrength` and `cellDangerScore` |
| `Engine/Match/Player/Decision/PlayerScorer.fs` | Update `influenceSpaceBonus` to use `cellDangerScore` instead of `DefenderCoverage` |
| `Engine/Match/Player/Decision/SpaceScorer.fs` | Update `scoreCell` to use `cellDangerScore` |
| `Engine/Match/TeamOrchestrator/BlackboardBuilder.fs` | Update `computeWeaknessZones` to use `cellDefensiveStrength` |

---

## 8. Threat/Weakness Zones — Density-Aware

### Problem

`computeThreatZones` and `computeWeaknessZones` (`BlackboardBuilder.fs:84-132`) only check if `oppInfluence > 0.3` or `ourInfluence > 0.3`. They don't consider how many players are actually in the zone — a single opponent in a cell triggers "threat" the same as three opponents.

### Solution

Weight zones by total player density (using `TotalGrid`):

```fsharp
let computeThreatZones (influence: InfluenceFrame) (clubSide: ClubSide) : FlankZone[] =
    let mutable threats = ResizeArray<int>()

    for cell = 0 to GridSize - 1 do
        let oppInfluence =
            if clubSide = HomeClub then -influence.NetGrid[cell]
            else influence.NetGrid[cell]

        let totalDensity = influence.TotalGrid[cell]
        // Threat requires both opponent advantage AND significant player density
        // A zone with 1 opponent far away is not a "threat zone"
        if oppInfluence > 0.2f && totalDensity > 0.8f then
            threats.Add(cell)

    threats
    |> Seq.map (fun cell ->
        let _, cy = cellToCenter cell
        PitchZoneOps.toFlank (float cy * 1.0<meter>))
    |> Seq.distinct
    |> Seq.toArray

let computeWeaknessZones (influence: InfluenceFrame) (clubSide: ClubSide) : FlankZone[] =
    let mutable weaknesses = ResizeArray<int>()

    for cell = 0 to GridSize - 1 do
        let ourInfluence =
            if clubSide = HomeClub then influence.NetGrid[cell]
            else -influence.NetGrid[cell]

        let safety = cellDangerScore cell clubSide influence
        let totalDensity = influence.TotalGrid[cell]
        // Weakness: we control this zone but there are open spaces nearby
        if ourInfluence > 0.1f && safety > 0.4f && totalDensity > 0.5f then
            weaknesses.Add(cell)

    weaknesses
    |> Seq.map (fun cell ->
        let _, cy = cellToCenter cell
        PitchZoneOps.toFlank (float cy * 1.0<meter>))
    |> Seq.distinct
    |> Seq.toArray
```

Key changes:
- Require `TotalGrid > threshold` — no density = no zone classification
- Use `NetGrid` instead of `ContestedGrid` (same values, clearer naming)
- Use `cellDangerScore` for weaknesses instead of raw `AttackerPassSafety`

### Files to change

| File | Change |
|---|---|
| `Engine/Match/TeamOrchestrator/BlackboardBuilder.fs` | Rewrite `computeThreatZones` and `computeWeaknessZones` |

---

## 9. Through Ball — Validate the Lane

### Problem

`findThroughBallTarget` (`SpaceScorer.fs:103-167`) finds cells behind the defensive line with good space scores, but doesn't check if there's a clear pass lane between the ball and the target cell. A through ball through 3 defenders scores the same as one through a gap.

### Solution

Add a pass lane validation step after finding candidate cells:

```fsharp
/// Check if there's a clear lane from ball position to target cell.
/// Returns true if the path doesn't cross heavy opponent influence.
let private hasClearLane (ballX: float32) (ballY: float32) (targetCell: int) (influence: InfluenceFrame) : bool =
    let tx, ty = cellToCenter targetCell
    let steps = 8
    let mutable blockedCells = 0

    for s = 1 to steps - 1 do
        let t = float32 s / float32 steps
        let px = ballX + (tx - ballX) * t
        let py = ballY + (ty - ballY) * t
        let cell = posToCell px py
        // If this cell on the path has heavy away influence, the lane is contested
        if influence.AwayGrid[cell] > 0.5f then
            blockedCells <- blockedCells + 1

    blockedCells <= 2  // allow up to 2 contested cells (realistic lane threading)
```

Then in `findThroughBallTarget`, filter candidates:

```fsharp
if isBehindLine && hasClearLane ballX ballY cell influence then
    let score = scoreCell cell influence ctx.MyPos attackDir
    // ... rest of scoring
```

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Player/Decision/SpaceScorer.fs` | Add `hasClearLane`, integrate into `findThroughBallTarget` |

---

## 10. BalanceConfig Integration

### Problem

All influence parameters are hardcoded. Sigma, grid resolution, ball gravity, control thresholds — none are tunable.

### Solution

Add a single `InfluenceConfig` record to `BalanceConfig`:

```fsharp
type InfluenceConfig =
    { GridCols: int
      GridRows: int
      SigmaBase: float
      SigmaAttributeScale: float
      VelocityBiasStrength: float
      BallGravityFactor: float
      BallGravitySigma: float
      ControlThreshold: float
      UpdateFrequencyTicks: int }
```

Default values:

```fsharp
{ GridCols = 10
  GridRows = 7
  SigmaBase = 10.0
  SigmaAttributeScale = 8.0
  VelocityBiasStrength = 0.4
  BallGravityFactor = 0.6
  BallGravitySigma = 20.0
  ControlThreshold = 0.3
  UpdateFrequencyTicks = 10 }
```

**Grid constants (`GridCols`, `GridRows`, `GridSize`) become runtime values derived from config**, not compile-time literals. This means:
- `InfluenceTypes.GridCols` and `GridRows` become `config.GridCols` and `config.GridRows`
- `GridSize` becomes `config.GridCols * config.GridRows`
- `CellWidth` and `CellHeight` become `float32 PitchLength / float32 config.GridCols` etc.
- `posToCell`, `cellToCenter`, `neighbors` take config as parameter

**This is a significant but necessary change.** The current `[<Literal>]` grid constants prevent runtime configurability. Making them config-driven allows:
- Tuning grid resolution without recompilation
- A/B testing different resolutions
- Future per-competition grid settings

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/BalanceConfig.fs` | Add `InfluenceConfig` record, add to `BalanceConfig` root |
| `Engine/Match/Types/InfluenceTypes.fs` | Remove `[<Literal>]` grid constants, make all functions take `InfluenceConfig` |
| `Engine/Match/Player/Perception/InfluenceFrame.fs` | Accept `InfluenceConfig` parameter, use config values |
| `Engine/Match/Core/SimState.fs` | Store `InfluenceConfig` or access via `state.Config.Influence` |

---

## 11. Performance: Pre-allocated Buffers

### Problem

`InfluenceFrame` allocates new arrays via `emptyInfluenceFrame()`. While `computeInto` reuses an existing frame, the grid size is fixed at compile time.

### Solution

With config-driven grid sizes, `InfluenceFrame` buffers must be allocated once and resized only when config changes:

```fsharp
type InfluenceFrame =
    { mutable HomeGrid: float32[]
      mutable AwayGrid: float32[]
      mutable NetGrid: float32[]
      mutable TotalGrid: float32[]
      mutable GridCols: int
      mutable GridRows: int }

let ensureSize (frame: InfluenceFrame) (config: InfluenceConfig) =
    let size = config.GridCols * config.GridRows
    if frame.HomeGrid.Length <> size then
        frame.HomeGrid <- Array.zeroCreate size
        frame.AwayGrid <- Array.zeroCreate size
        frame.NetGrid <- Array.zeroCreate size
        frame.TotalGrid <- Array.zeroCreate size
        frame.GridCols <- config.GridCols
        frame.GridRows <- config.GridRows
```

In `MatchStepper`, before computing:

```fsharp
ensureSize state.InfluenceFrame config
InfluenceFrame.computeInto (getFrame state HomeClub) (getFrame state AwayClub) state.InfluenceFrame config
```

### Files to change

| File | Change |
|---|---|
| `Engine/Match/Types/InfluenceTypes.fs` | Add `ensureSize`, make `InfluenceFrame` mutable for buffer reuse |
| `Engine/Match/Core/MatchStepper.fs` | Call `ensureSize` before `computeInto` |

---

## 12. Files Summary

### Files to rewrite (complete replacement)

| File | Lines (est.) | What changes |
|---|---|---|
| `Engine/Match/Types/InfluenceTypes.fs` | ~200 | New record (4 arrays), config-driven grid, pure query helpers, delete AttackerPassSafety/DefenderCoverage/ControlGrid |
| `Engine/Match/Player/Perception/InfluenceFrame.fs` | ~150 | Dynamic sigma, velocity bias, ball gravity, config parameter |

### Files to delete

| File | Reason |
|---|---|
| `Engine/Match/Player/Perception/InfluenceTypes.fs` | Exact duplicate of `Engine/Match/Types/InfluenceTypes.fs` |

### Files to modify (surgical changes)

| File | Change |
|---|---|
| `Engine/Match/Types/BalanceConfig.fs` | Add `InfluenceConfig` record (~20 lines) |
| `Engine/Match/Core/SimState.fs` | Replace 2 frames with 1, add `ensureSize` call (~5 lines) |
| `Engine/Match/Core/MatchStepper.fs` | Single `computeInto` call, pass config (~3 lines) |
| `Engine/Match/Player/Decision/MovementScorer.fs` | Rewrite `coverSpaceScore` influence section (~15 lines) |
| `Engine/Match/Player/Decision/SpaceScorer.fs` | Add `hasClearLane`, update `scoreCell` (~30 lines) |
| `Engine/Match/Player/Decision/PlayerScorer.fs` | Update `influenceSpaceBonus` to use `cellDangerScore` (~5 lines) |
| `Engine/Match/TeamOrchestrator/BlackboardBuilder.fs` | Rewrite threat/weakness zone computation (~30 lines) |
| `Engine/Match/Systems/Cognition/CognitionSystem.fs` | Pass single influence frame instead of selecting by side (~3 lines) |
| `FootballEngine.Client.fsproj` | Remove deleted file, ensure correct compile order |

### Files that reference influence but need NO changes

| File | Why |
|---|---|
| `Engine/Match/Player/PlayerContext.fs` | `AgentContext.Influence` type stays `InfluenceFrame`, just the record shape changes |
| `Engine/Match/Player/Decision/CollectiveModifiers.fs` | Reads from `TeamBlackboard`, not directly from influence |
| `Engine/Match/TeamOrchestrator/TeamDirector.fs` | Uses `InfluenceTypes.scoreCellRaw` which stays as pure function |

---

## 13. Implementation Order

The changes must be applied in this exact sequence to maintain compilability at each step:

1. **Add `InfluenceConfig` to `BalanceConfig.fs`** — new type, no consumers yet
2. **Rewrite `InfluenceTypes.fs` (Engine/Match/Types/)** — new record, new helpers, config-driven grid
3. **Delete `InfluenceTypes.fs` (Engine/Match/Player/Perception/)** — remove duplicate
4. **Rewrite `InfluenceFrame.fs`** — dynamic sigma, velocity bias, ball gravity, takes config
5. **Update `SimState.fs`** — single `InfluenceFrame` field, `ensureSize` support
6. **Update `MatchStepper.fs`** — single `computeInto`, pass config
7. **Update `CognitionSystem.fs`** — pass single influence frame
8. **Update `MovementScorer.fs`** — rewrite `coverSpaceScore` neighbor scan
9. **Update `SpaceScorer.fs`** — add `hasClearLane`, update scoring
10. **Update `PlayerScorer.fs`** — use `cellDangerScore`
11. **Update `BlackboardBuilder.fs`** — density-aware threat/weakness zones
12. **Update `.fsproj`** — remove deleted file, verify compile order
13. **Build and fix** — `dotnet build`, fix any remaining references

---

## 14. What This Enables (Post-Implementation)

| Behavior | Before | After |
|---|---|---|
| Elite player controls more space | No — sigma 15m for everyone | Yes — sigma 10-18m by attributes |
| Pressing group creates directional pressure | No — isotropic circles | Yes — velocity-directed influence |
| Space near the ball is contested | No — ball has no influence | Yes — ball gravity draws both grids |
| Defender reads zone pressure, not just own cell | No — reads 1 cell | Yes — scans 9-cell neighborhood |
| Through balls check pass lane | No — checks space only | Yes — validates lane isn't blocked |
| Space scoring favors useful distances | No — penalizes close space | Yes — bell curve peaks at 15-30m |
| Grid resolution is tunable | No — compile-time literals | Yes — `InfluenceConfig` |
| Defensive coverage considers both sides | No — one-sided | Yes — `cellDefensiveStrength` |
| Threat zones require player density | No — single opponent triggers threat | Yes — density + opponent advantage |

---

## 15. Testing Strategy

### Unit tests to add

| Test | Validates |
|---|---|
| `playerSigma` returns correct range for min/max attributes | Dynamic sigma bounds |
| `velocityWeight` returns 1.0 for stationary, >1.0 for forward movement | Velocity bias correctness |
| `cellDefensiveStrength` returns 0.5 when equal influence from both sides | Dual-sided coverage |
| `cellDangerScore` returns high value for open cells with advantage | Danger computation |
| `hasClearLane` returns false when path crosses heavy opponent influence | Lane validation |
| `scoreCellRaw` returns higher score for cells at 15-30m than <5m or >50m | Distance inversion |
| `coverSpaceScore` gives bonus when neighbors have deficit | Neighborhood scan |
| `computeThreatZones` requires density threshold | Density-aware zones |
| `ensureSize` reallocates when grid size changes | Buffer management |

### Integration tests to update

| Test | Change needed |
|---|---|
| Any test that constructs `InfluenceFrame` directly | Update to new 4-array record |
| Any test that reads `AttackerPassSafety` or `DefenderCoverage` | Update to use `cellDangerScore`/`cellDefensiveStrength` |
| Any test that reads `ControlGrid` | Update to use `NetGrid` with threshold |

### Verification command

```bash
dotnet test FootballEngine.Tests/FootballEngine.Tests.fsproj
```
