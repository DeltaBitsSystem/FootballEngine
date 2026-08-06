# Influence Maps Implementation Plan

## 1. Architecture Overview

### Current System (Pre-Influence Maps)

```
SimState
├── Home/Away TeamSimState
│   ├── TeamFrame (SoA: PosX, PosY, VelX, VelY, IntentKind, ...)
│   ├── CognitiveFrame (nearest TM/Opp, best pass target, pressure)
│   └── TeamIntent (build-up side, runner, support positions)
│
└── Ball (BallPhysicsState)

Decision Pipeline (per cognitive tick):
  CognitiveFrameModule.build → TeamIntentModule.build → BatchDecision.processTeam
    └── For each player:
        AgentContext.build → MovementScorer.computeAll → MovementScorer.pickIntent
        (if on-ball) PlayerScorer.computeAll → PlayerDecision.decide → ActionResolver.run
```

### Where Influence Maps Fit

```
SimState
├── Home/Away TeamSimState
│   ├── TeamFrame
│   ├── CognitiveFrame
│   ├── TeamIntent
│   └── InfluenceFrame      ← NEW: spatial control grid
│
└── Ball

Updated Decision Pipeline:
  CognitiveFrameModule.build
  → InfluenceFrameModule.build    ← NEW: computes control grid
  → TeamIntentModule.build        ← MODIFIED: uses influence for runner/space
  → BatchDecision.processTeam     ← MODIFIED: influence-aware scoring
    └── For each player:
        AgentContext.build        ← MODIFIED: includes influence data
        MovementScorer.computeAll ← MODIFIED: CoverSpace/SupportAttack use influence
        PlayerScorer.computeAll   ← MODIFIED: passScore considers space, not just player
```

### Design Principles

1. **InfluenceFrame lives alongside CognitiveFrame** — same lifecycle (built per cognitive tick, stored in SimState)
2. **Grid resolution is configurable** — coarse grid (10x7) for performance, can be tuned
3. **Pure functions** — `InfluenceFrameModule.build` takes frames and returns a struct, no mutation
4. **SoA-friendly** — influence values stored as `float32[]` arrays matching grid layout
5. **No changes to physics loop** — influence is computed at cognitive tick frequency (~2Hz), not 40Hz

---

## 2. New Types to Add

### 2.1 Grid Resolution Config

```fsharp
// File: Engine/Match/Core/InfluenceTypes.fs
[<Struct>]
type InfluenceGridConfig = {
    CellsX: int        // default 10
    CellsY: int        // default 7
    CellWidth: float<meter>   // PitchLength / CellsX
    CellHeight: float<meter>  // PitchWidth / CellsY
}
```

### 2.2 InfluenceFrame (struct, stored in SimState)

```fsharp
// File: Engine/Match/Core/InfluenceTypes.fs
[<Struct>]
type InfluenceFrame = {
    /// Home team control per cell. 1.0 = full control, 0.0 = none, negative = contested
    HomeControl: float32[]
    /// Away team control per cell
    AwayControl: float32[]
    /// Net control: positive = home advantage, negative = away advantage
    NetControl: float32[]
    /// Space score: how "open" each cell is (low influence from both teams)
    OpenSpace: float32[]
    /// Pass lane safety: for each cell, how safe a pass through it would be
    PassSafety: float32[]
    GridConfig: InfluenceGridConfig
    SlotCount: int
}
```

### 2.3 Spatial Influence Query Helpers

```fsharp
// File: Engine/Match/Core/InfluenceTypes.fs
[<Struct>]
type InfluenceQuery = {
    CellX: int
    CellY: int
    HomeControl: float32
    AwayControl: float32
    NetControl: float32
    OpenSpace: float32
}
```

### 2.4 Extended CognitiveFrame (add influence-derived fields)

```fsharp
// ADD to existing CognitiveFrame in SimTypes.fs:
// (these are computed by InfluenceFrameModule, not CognitiveFrameModule)

[<Struct>]
type InfluenceCognitiveData = {
    /// For each player slot: the cell index they currently occupy
    PlayerCellIndex: int16[]
    /// For each player slot: how much space they have around them (meters)
    SpaceAroundPlayer: float32[]
    /// Best "space pass" target for each player (cell coordinates, not player)
    BestSpaceTarget: (int16 * int16)[]  // (cellX, cellY) per player
    SlotCount: int
}
```

### 2.5 New Movement Intent (for pass-into-space)

```fsharp
// ADD to MovementIntent in SimTypes.fs:
// | MoveToSpace of targetCellX: int * targetCellY: int * urgency: float
```

---

## 3. New Modules to Create

### 3.1 `Engine/Match/Core/InfluenceTypes.fs`

**Purpose:** Type definitions for the influence map system.

**Contents:**
- `InfluenceGridConfig`
- `InfluenceFrame`
- `InfluenceQuery`
- `InfluenceCognitiveData`
- Grid helper functions (`cellCenter`, `posToCell`, `cellIndex`)

**Dependencies:** `PhysicsContract`, `SimTypes`

**Compilation position:** After `SimTypes.fs`, before `CognitiveFrame.fs`

### 3.2 `Engine/Match/Core/InfluenceFrame.fs`

**Purpose:** Core influence map computation.

**Key functions:**

```fsharp
module InfluenceFrameModule =
    /// Build influence frame from current team positions
    let build (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) : InfluenceFrame

    /// Compute single player's influence contribution to a cell
    /// Uses: position, velocity (direction of movement), attributes (Positioning, Anticipation)
    let private playerInfluence
        (player: Player)
        (profile: BehavioralProfile)
        (posX: float<meter>)
        (posY: float<meter>)
        (velX: float<meter/second>)
        (velY: float<meter/second>)
        (config: InfluenceGridConfig)
        : int * int * float32  // (cellX, cellY, influenceValue)

    /// Gaussian kernel for influence spread
    let private influenceKernel (dist: float) (radius: float) : float32

    /// Query influence at a specific position
    let queryAt (frame: InfluenceFrame) (x: float<meter>) (y: float<meter>) : InfluenceQuery

    /// Query influence at a future position (for pass-into-space prediction)
    let queryAtPredicted
        (frame: InfluenceFrame)
        (x: float<meter>)
        (y: float<meter>)
        (timeAhead: float<second>)
        : InfluenceQuery

    /// Find the cell with highest open space in a region
    let findBestSpaceCell
        (frame: InfluenceFrame)
        (centerX: float<meter>)
        (centerY: float<meter>)
        (radius: float<meter>)
        (forwardBias: float)  // bias towards opponent's goal
        : (int * int) voption

    /// Build influence-derived cognitive data
    let buildCognitiveData
        (influenceFrame: InfluenceFrame)
        (ownFrame: TeamFrame)
        (oppFrame: TeamFrame)
        : InfluenceCognitiveData
```

**Influence computation model:**

```
For each cell (cx, cy):
    homeControl[cx, cy] = Σ playerInfluence(homePlayer, cell)
    awayControl[cx, cy] = Σ playerInfluence(awayPlayer, cell)

    playerInfluence = baseInfluence * velocityBias * attributeMod

    baseInfluence = exp(-dist² / (2 * σ²))   // Gaussian kernel
    σ = controlRadius(player.Positioning, player.Anticipation)

    velocityBias = 1.0 + dot(velocity, toCell) * 0.3  // influence extends in movement direction

    attributeMod = 0.5 + 0.5 * (Positioning/20 * 0.6 + Anticipation/20 * 0.4)

    netControl[cx, cy] = homeControl[cx, cy] - awayControl[cx, cy]
    openSpace[cx, cy] = 1.0 - min(1.0, homeControl[cx, cy] + awayControl[cx, cy])
    passSafety[cx, cy] = 1.0 - awayControl[cx, cy]  // from attacker's perspective
```

**Dependencies:** `PhysicsContract`, `SimTypes`, `InfluenceTypes`, `MatchSpatial`

**Compilation position:** After `InfluenceTypes.fs`, before `CognitiveFrame.fs`

### 3.3 `Engine/Match/Player/SpaceScorer.fs`

**Purpose:** Score off-ball actions using influence maps (pass into space, runs into space).

**Key functions:**

```fsharp
module SpaceScorer =
    /// Score passing into a space cell (not to a player)
    /// This is what enables "pass into space" — the ball goes to where
    /// a teammate is running TO, not where they currently are
    let spacePassScore
        (ctx: AgentContext)
        (influenceFrame: InfluenceFrame)
        (targetCellX: int)
        (targetCellY: int)
        : float<decisionScore>

    /// Score a run into space for the current player
    let runIntoSpaceScore
        (ctx: AgentContext)
        (influenceFrame: InfluenceFrame)
        (targetCellX: int)
        (targetCellY: int)
        : float

    /// Find the best space cell for a through-ball
    /// Combines: open space, teammate velocity, defensive pressure gradient
    let findThroughBallTarget
        (ctx: AgentContext)
        (influenceFrame: InfluenceFrame)
        : (int * int * float<decisionScore>) voption

    /// Compute "space creation" bonus: how much does this player's movement
    /// open up space for teammates? (dragging defenders)
    let spaceCreationBonus
        (ctx: AgentContext)
        (influenceFrame: InfluenceFrame)
        (currentCellX: int)
        (currentCellY: int)
        (targetCellX: int)
        (targetCellY: int)
        : float
```

**Dependencies:** `SimTypes`, `InfluenceTypes`, `InfluenceFrame`, `PlayerContext`, `PhysicsContract`

**Compilation position:** After `MovementScorer.fs`, before `PlayerScorer.fs`

---

## 4. Existing Files to Modify

### 4.1 `Engine/Match/Core/SimTypes.fs`

**Changes:**
- Add `InfluenceFrame` field to `SimState`:
  ```fsharp
  member val HomeInfluenceFrame: InfluenceFrame voption = ValueNone with get, set
  member val AwayInfluenceFrame: InfluenceFrame voption = ValueNone with get, set
  member val HomeInfluenceCognitive: InfluenceCognitiveData voption = ValueNone with get, set
  member val AwayInfluenceCognitive: InfluenceCognitiveData voption = ValueNone with get, set
  ```
- Add `MoveToSpace` case to `MovementIntent` union
- Add `SpaceAroundPlayer` and `BestSpaceTarget` fields to `InfluenceCognitiveData` (new type, not modifying CognitiveFrame)

### 4.2 `Engine/Match/Core/MatchSpatial.fs`

**Changes:**
- Add `findBestSpacePassTarget` function that uses influence frame instead of player positions
- This is a NEW function, existing `findBestPassTargetFrame` remains unchanged (backward compatible)

```fsharp
let findBestSpacePassTarget
    (meIdx: int)
    (attFrame: TeamFrame)
    (attRoster: PlayerRoster)
    (defFrame: TeamFrame)
    (influenceFrame: InfluenceFrame)
    (dir: AttackDir)
    : (float<meter> * float<meter>) voption
```

### 4.3 `Engine/Match/Player/PlayerContext.fs`

**Changes:**
- Add `InfluenceFrame` and `InfluenceCognitiveData` fields to `AgentContext`:
  ```fsharp
  InfluenceFrame: InfluenceFrame voption
  InfluenceData: InfluenceCognitiveData voption
  ```
- Modify `AgentContext.build` to accept and populate these fields (optional parameters with `ValueNone` default)

### 4.4 `Engine/Match/Player/MovementScorer.fs`

**Changes:**
- Modify `coverSpaceScore` to use influence map when available:
  ```fsharp
  // Current: static position-based scoring
  // New: if influenceFrame is available, score based on net control gradient
  // Players move to cells where their team can establish control
  ```
- Modify `supportAttackScore` to reward moving into open space cells
- Modify `pickIntentNormal` to consider `MoveToSpace` intent when influence data shows high-value space

### 4.5 `Engine/Match/Player/PlayerScorer.fs`

**Changes:**
- Modify `passScore` to include space-awareness bonus:
  ```fsharp
  let spaceBonus =
      match ctx.InfluenceFrame with
      | ValueSome inf ->
          // Check if pass lane goes through open space
          // Check if target area has favorable net control
          computeSpaceBonus ctx inf targetIdx
      | ValueNone -> 0.0
  ```
- Add `spacePassScore` integration: when the best option is passing into space (not to a static player), boost the pass score

### 4.6 `Engine/Match/Player/BatchDecision.fs`

**Changes:**
- Add influence frame computation before the player loop:
  ```fsharp
  let influenceFrame = InfluenceFrameModule.build ctx state clubSide
  let influenceData = InfluenceFrameModule.buildCognitiveData influenceFrame frame oppFrame
  SimStateOps.setInfluenceFrame state clubSide influenceFrame
  SimStateOps.setInfluenceCognitive state clubSide influenceData
  ```
- Pass influence frame to `AgentContext.build`
- Pass influence frame to `MovementScorer.computeAll`

### 4.7 `Engine/Match/Movement/TeamIntent.fs`

**Changes:**
- Modify `pickRunner` to use influence map for selecting run targets:
  ```fsharp
  // Current: picks runner and target based on position/attributes
  // New: also considers where the open space cells are
  // Runner target = cell with high openSpace + forward bias
  ```
- Modify `computeSupportPositions` to shift support positions toward favorable influence zones

### 4.8 `Engine/Match/Actions/PassAction.fs`

**Changes:**
- Modify `resolve` to support "pass into space" targets:
  - Add optional `spaceTarget: (float<meter> * float<meter>) option` parameter
  - When present, the ball trajectory targets the space cell, not the player's current position
  - The receiver's `RunAssignment` is updated to move toward the ball landing position
- This is the KEY change that enables pass-into-space execution

### 4.9 `Engine/Match/Core/Simulator.fs` (or wherever the main loop lives)

**Changes:**
- Ensure influence frame is built at the right point in the tick cycle (after positions update, before decision making)

### 4.10 `Engine/Match/Core/IntentFrame.fs`

**Changes:**
- Add handling for `MoveToSpace` intent:
  ```fsharp
  | MoveToSpace(cx, cy, urgency) ->
      let center = InfluenceFrameModule.cellCenter config cx cy
      (IntentKind.SupportAttack, float32 center.X, float32 center.Y, 0)
  ```

### 4.11 Project file `FootballEngine.Client.fsproj`

**Changes:**
- Add new compile entries in correct order:
  ```xml
  <Compile Include="Engine\Match\Core\InfluenceTypes.fs"/>
  <Compile Include="Engine\Match\Core\InfluenceFrame.fs"/>
  ...
  <Compile Include="Engine\Match\Player\SpaceScorer.fs"/>
  ```

---

## 5. Step-by-Step Implementation Phases

Each phase compiles independently. No phase breaks existing functionality.

### Phase 1: Types and Infrastructure

**Goal:** Define all new types, prove they compile, zero behavioral change.

**Files created:**
- `Engine/Match/Core/InfluenceTypes.fs` — all type definitions

**Files modified:**
- `Engine/Match/Core/SimTypes.fs` — add `InfluenceFrame voption` fields to `SimState`
- `FootballEngine.Client.fsproj` — add `InfluenceTypes.fs` compile entry

**What this enables:**
- Types exist and compile
- SimState can hold influence data (always `ValueNone` initially)
- No runtime behavior changes

**Tests:**
- Unit test: `InfluenceGridConfig` creates valid config
- Unit test: `InfluenceFrame` struct initializes correctly

**Estimated effort:** 1-2 hours

---

### Phase 2: Core Influence Computation

**Goal:** Compute influence maps from player positions. Still not used by decision making.

**Files created:**
- `Engine/Match/Core/InfluenceFrame.fs` — core computation logic

**Files modified:**
- `FootballEngine.Client.fsproj` — add `InfluenceFrame.fs` compile entry

**Key implementation:**
```fsharp
let build (ctx: MatchContext) (state: SimState) (clubSide: ClubSide) : InfluenceFrame
```

- Iterates over both team frames
- For each active player, computes Gaussian influence spread
- Produces `HomeControl`, `AwayControl`, `NetControl`, `OpenSpace`, `PassSafety` arrays
- All pure functions, no side effects

**Grid resolution:** Start with 10x7 (10.5m x ~9.7m cells). This is coarse enough for performance but fine enough for tactical decisions.

**What this enables:**
- Influence maps can be computed and inspected
- Can be visualized in debug output
- Still NOT used by any decision logic

**Tests:**
- Unit test: single player at center creates expected influence pattern
- Unit test: two players create overlapping influence
- Unit test: net control is zero when both teams have equal presence
- Unit test: open space is high in empty corners

**Estimated effort:** 3-4 hours

---

### Phase 3: Influence-Aware Context

**Goal:** Thread influence data through `AgentContext` so scorers can access it.

**Files modified:**
- `Engine/Match/Core/SimTypes.fs` — add `InfluenceCognitiveData` type
- `Engine/Match/Player/PlayerContext.fs` — add influence fields to `AgentContext`, update `build`
- `Engine/Match/Core/InfluenceFrame.fs` — add `buildCognitiveData` function

**Files created:**
- (none new)

**Key changes:**
- `AgentContext` gets optional `InfluenceFrame` and `InfluenceCognitiveData` fields
- `AgentContext.build` accepts these as optional parameters (default `ValueNone`)
- `buildCognitiveData` computes per-player space metrics from the influence grid

**What this enables:**
- All scorers CAN access influence data
- Existing scorers still work (they get `ValueNone` and skip influence logic)
- Backward compatible: no existing code path changes

**Tests:**
- Unit test: `buildCognitiveData` produces correct cell indices
- Integration test: `AgentContext.build` with influence data populates fields correctly

**Estimated effort:** 2 hours

---

### Phase 4: Movement Scorer Integration

**Goal:** Off-ball movement decisions use influence maps.

**Files modified:**
- `Engine/Match/Player/MovementScorer.fs`
- `Engine/Match/Player/BatchDecision.fs`

**Key changes in `MovementScorer.fs`:**

1. **`coverSpaceScore`** — when influence frame is available:
   ```fsharp
   // Score cells near the player by net control
   // Higher score for cells where away team has low control
   // (defenders should cover spaces their team doesn't control)
   let influenceBonus =
       match ctx.InfluenceFrame with
       | ValueSome inf ->
           let nearbyCells = getNearbyCells inf ctx.MyPos
           let avgNetControl = averageNetControl nearbyCells
           // Defenders want to move to cells their team doesn't control yet
           (1.0 - float avgNetControl) * 0.3
       | ValueNone -> 0.0
   ```

2. **`supportAttackScore`** — when influence frame is available:
   ```fsharp
   // Attackers want to move to open space cells in attacking direction
   let spaceBonus =
       match ctx.InfluenceFrame with
       | ValueSome inf ->
           let forwardOpenSpace = findForwardOpenSpace inf ctx.MyPos ctx.Team.AttackDir
           forwardOpenSpace * 0.4
       | ValueNone -> 0.0
   ```

3. **`pickIntentNormal`** — add `MoveToSpace` as a candidate intent:
   ```fsharp
   // If there's a high-value space cell nearby, consider moving there
   match ctx.InfluenceFrame with
   | ValueSome inf ->
       match findBestSpaceCell inf ctx.MyPos ctx.Team.AttackDir with
       | ValueSome (cx, cy, score) when score > 0.5 ->
           // MoveToSpace becomes a candidate
   ```

**Key changes in `BatchDecision.fs`:**
- Compute influence frame before player loop
- Pass it to `AgentContext.build`
- Pass it to `MovementScorer.computeAll`

**What this enables:**
- Defenders proactively cover uncontrolled space
- Attackers make runs into open areas
- Players show spatial awareness (like real footballers scanning for space)

**Tests:**
- Unit test: defender in low-control area gets higher coverSpace score
- Unit test: attacker near open space gets higher supportAttack score
- Integration test: influence-aware movement produces different (better) positioning

**Estimated effort:** 4-5 hours

---

### Phase 5: Pass-Into-Space Scoring

**Goal:** On-ball players can score and choose "pass into space" actions.

**Files created:**
- `Engine/Match/Player/SpaceScorer.fs`

**Files modified:**
- `Engine/Match/Player/PlayerScorer.fs`
- `FootballEngine.Client.fsproj`

**Key implementation in `SpaceScorer.fs`:**

```fsharp
let spacePassScore ctx influenceFrame targetCellX targetCellY =
    let cellQuery = InfluenceFrameModule.queryAt influenceFrame (cellCenter targetCellX targetCellY)

    // Factor 1: How open is the target cell?
    let openness = float cellQuery.OpenSpace

    // Factor 2: Is a teammate moving toward this cell?
    let teammateConvergence =
        computeTeammateConvergence ctx influenceFrame targetCellX targetCellY

    // Factor 3: Is the pass lane safe? (low defender influence along path)
    let laneSafety =
        computePassLaneSafety ctx influenceFrame ctx.MyPos (cellCenter targetCellX targetCellY)

    // Factor 4: How forward is this pass?
    let forwardBonus =
        computeForwardBonus ctx targetCellX targetCellY

    (openness * 0.3 + teammateConvergence * 0.3 + laneSafety * 0.25 + forwardBonus * 0.15)
    |> LanguagePrimitives.FloatWithMeasure<decisionScore>
```

**Key changes in `PlayerScorer.fs`:**
- Modify `passScore` to add space-awareness bonus:
  ```fsharp
  let spaceAwareBonus =
      match ctx.InfluenceFrame with
      | ValueSome inf ->
          // Check if the best pass target is in a good space zone
          // If target is in high-openSpace area, boost the score
          match ctx.BestPassTargetIdx with
          | ValueSome targetIdx ->
              let targetPos = getTargetPosition ctx targetIdx
              let query = InfluenceFrameModule.queryAt inf targetPos.X targetPos.Y
              float query.OpenSpace * 0.2
          | ValueNone -> 0.0
      | ValueNone -> 0.0
  ```

**What this enables:**
- Pass scoring considers spatial context, not just receiver quality
- Passes to players in open space are rated higher
- Passes through contested zones are penalized

**Tests:**
- Unit test: pass to player in open space scores higher than pass to player in crowded area
- Unit test: pass through high-defender-influence lane scores lower
- Unit test: forward passes into open space get bonus

**Estimated effort:** 4-5 hours

---

### Phase 6: Pass-Into-Space Execution

**Goal:** The engine can actually execute passes into space (ball goes to a location, receiver runs to it).

**Files modified:**
- `Engine/Match/Actions/PassAction.fs`
- `Engine/Match/Player/PlayerDecision.fs`
- `Engine/Match/Core/SimTypes.fs` (add `MoveToSpace` to `MovementIntent`)
- `Engine/Match/Core/IntentFrame.fs`

**Key changes in `PassAction.fs`:**

1. Add space-target variant of `resolve`:
   ```fsharp
   let resolveIntoSpace
       (subTick: int)
       (ctx: MatchContext)
       (state: SimState)
       (clock: SimulationClock)
       (targetPlayer: Player)
       (targetSpaceX: float<meter>)
       (targetSpaceY: float<meter>)
       : MatchEvent list
   ```

2. The ball trajectory targets `targetSpaceX/Y`, not the player's current position
3. The target player receives a `RunAssignment` to move toward the ball landing position
4. Offside check uses the receiver's position at pass time (existing logic works)

**Key changes in `PlayerDecision.fs`:**
- When influence frame is available and `findThroughBallTarget` returns a high-score result:
  - Yield a "pass into space" candidate alongside regular pass candidates
  - The candidate targets the space cell, not a specific player

**What this enables:**
- THROUGH BALLS: ball played into space for a runner to chase
- Passes to where a teammate WILL BE, not where they ARE
- This is the "better than FM" moment

**Tests:**
- Integration test: pass into space creates correct ball trajectory
- Integration test: receiver gets run assignment toward ball landing position
- Integration test: offside detection still works for space passes

**Estimated effort:** 5-6 hours

---

### Phase 7: Team Intent Integration

**Goal:** Team-level decisions (runner selection, support positions) use influence maps.

**Files modified:**
- `Engine/Match/Movement/TeamIntent.fs`

**Key changes:**

1. **`pickRunner`** — use influence map to find best run target:
   ```fsharp
   // Current: pick runner, then target is fixed offset from current position
   // New: pick runner, then find best OPEN SPACE cell as run target

   let findRunTarget influenceFrame runnerPos dir =
       // Search for cells with:
       // 1. High openSpace
       // 2. Forward of runner
       // 3. Low defender concentration
       findBestSpaceCell influenceFrame runnerPos dir 15.0<meter>
   ```

2. **`computeSupportPositions`** — shift support positions toward favorable influence zones:
   ```fsharp
   // Current: positions are fixed offsets from base positions
   // New: positions are nudged toward cells with favorable net control

   let influenceNudge influenceFrame basePos =
       let nearbyCells = getNearbyCells influenceFrame basePos
       let avgNetControl = averageNetControl nearbyCells
       // If our team controls this area, push forward
       // If contested, push wider
       computeNudge avgNetControl basePos
   ```

**What this enables:**
- Team-level spatial awareness
- Runners make intelligent runs into space, not predetermined patterns
- Support positions adapt to the spatial context of the moment

**Tests:**
- Unit test: runner target is in open space, not crowded area
- Unit test: support positions shift toward favorable zones

**Estimated effort:** 3-4 hours

---

### Phase 8: Performance Optimization

**Goal:** Ensure influence maps don't degrade match simulation performance.

**Optimizations:**

1. **Pre-allocated arrays** — `InfluenceFrame` arrays are allocated once and reused (same pattern as `CognitiveFrameBuffers`)
2. **SIMD-friendly layout** — arrays are flat `float32[]`, not `float32[,]`, for cache locality
3. **Selective computation** — only recompute influence at cognitive tick frequency, not every physics tick
4. **Early exit for empty cells** — cells far from all players skip computation
5. **Configurable grid resolution** — can reduce to 8x5 if needed for performance

**Files modified:**
- `Engine/Match/Core/InfluenceFrame.fs` — add buffer pooling
- `Engine/Match/Core/SimTypes.fs` — add `InfluenceFrameBuffers` type

**What to measure:**
- Cognitive tick duration before/after influence maps
- Total match simulation time for 90 minutes
- Memory allocation rate

**Estimated effort:** 2-3 hours

---

### Phase 9: Visualization and Debugging

**Goal:** Make influence maps visible for tuning and debugging.

**Files modified:**
- `FootballEngine.Client/Views/Components/MatchProjection.fs` (or `MatchViewer.fs`)
- `FootballEngine.Client/Views/Components/FootballPitch.fs`

**Implementation:**
- Render influence grid as heatmap overlay on the pitch
- Toggle between HomeControl, AwayControl, NetControl, OpenSpace views
- Show pass lanes colored by passSafety

**What this enables:**
- Visual tuning of influence parameters
- Debugging why players make certain decisions
- Demonstrating the "better than FM" spatial awareness

**Estimated effort:** 3-4 hours

---

## 6. Performance Considerations

### Computational Cost

| Operation | Cost | Frequency |
|---|---|---|
| Build influence frame (10x7 grid, 22 players) | ~5000 cell-player distance calculations | ~2Hz (cognitive tick) |
| Query influence at position | O(1) array lookup | Per player per cognitive tick |
| findBestSpaceCell | O(cells in radius) ≈ 20-30 lookups | Per on-ball player per cognitive tick |
| Pass lane safety | O(cells along path) ≈ 10-15 lookups | Per pass candidate |

### Total Overhead Estimate

- **Current cognitive tick:** ~2-3ms for 22 players
- **With influence maps:** ~4-5ms for 22 players (roughly 2x)
- **Impact on total match sim:** < 5% (cognitive tick is a small fraction of total work)

### Mitigations

1. **Grid resolution tuning:** If 10x7 is too slow, drop to 8x5 (40% fewer cells)
2. **Player influence radius cap:** Only compute influence for cells within N meters of each player
3. **Dirty flag:** Only recompute influence if player positions have changed significantly
4. **Parallel computation:** Home and away influence can be computed in parallel

---

## 7. How It Enables "Pass Into Space"

### The Problem (Current System)

Currently, `passScore` in `PlayerScorer.fs` evaluates passes to **where teammates currently are**:

```fsharp
let targetBonus =
    match ctx.BestPassTargetIdx with
    | ValueSome targetIdx ->
        let target = ctx.Team.OwnRoster.Players[targetIdx]
        // Scores based on target's BallControl and marking pressure
        // at their CURRENT position
```

And `PassAction.resolve` sends the ball to the target's current position (with a small lead prediction based on velocity).

### The Solution (With Influence Maps)

1. **SpaceScorer.findThroughBallTarget** identifies cells where:
   - Open space is high (few defenders nearby)
   - A teammate is moving toward that cell (velocity convergence)
   - The pass lane is safe (low defender influence along the path)
   - The cell is in an attacking direction

2. **PlayerDecision.decide** considers "pass into space" as a candidate alongside regular passes:
   ```fsharp
   // If space pass score > regular pass score, choose space pass
   match SpaceScorer.findThroughBallTarget ctx influenceFrame with
   | ValueSome (cx, cy, score) when score > regularPassScore ->
       yield score, OnBallIntent.PassIntoSpace(cx, cy)
   ```

3. **PassAction.resolveIntoSpace** executes the pass:
   - Ball trajectory targets the space cell center
   - The best teammate converging on that cell is identified as the intended receiver
   - The receiver gets a `RunAssignment` to sprint toward the ball landing position
   - Offside check uses receiver position at pass time (existing logic)

4. **MovementScorer.pickIntent** for the receiver:
   - When a space pass is incoming, `SupportAttack` intent targets the ball landing position
   - The receiver's run is a `DeepRun` or `DiagonalRun` toward the space

### Why This Is Better Than FM

| Feature | FM24 | This System |
|---|---|---|
| Pass target selection | Heuristic: "is there a teammate nearby?" | Influence-based: "where is the best space?" |
| Spatial awareness | Player vision cone only | Full pitch influence map |
| Off-ball runs | Predefined patterns (overlap, underlap) | Dynamic: runs toward open space cells |
| Pass lane evaluation | Simple distance check | Influence-weighted path safety |
| Space creation | Not modeled | Players score for dragging defenders into space |
| Through balls | Basic lead prediction | Convergence-based: ball to where runner will arrive |

---

## 8. Risk Mitigation

| Risk | Mitigation |
|---|---|
| Influence computation too slow | Configurable grid resolution, buffer pooling, early exit |
| Breaking existing behavior | All influence features are opt-in via `ValueNone` defaults |
| Parameter tuning complexity | Start with sensible defaults, add Phase 9 visualization for tuning |
| Offside exploitation | Existing offside logic in `MatchSpatial` applies to space passes unchanged |
| Players clustering in "best" cell | Add separation penalty in influence computation (players repel each other) |
| Memory pressure | Arrays are pre-allocated and reused, no per-tick allocation |

---

## 9. Implementation Order Summary

```
Phase 1: Types (1-2h)      → Compiles, no behavior change
Phase 2: Core compute (3-4h) → Compiles, influence maps exist but unused
Phase 3: Context threading (2h) → Compiles, data flows to scorers
Phase 4: Movement scoring (4-5h) → Off-ball movement uses influence
Phase 5: Pass scoring (4-5h) → On-ball pass scoring uses influence
Phase 6: Pass execution (5-6h) → Pass-into-space actually works
Phase 7: Team intent (3-4h) → Team-level spatial awareness
Phase 8: Performance (2-3h) → Optimize and benchmark
Phase 9: Visualization (3-4h) → Debug overlay for tuning

Total: 27-36 hours
```

Each phase compiles independently. After Phase 1, the system works exactly as before. After Phase 6, pass-into-space is fully functional. Phases 7-9 are polish and optimization.
