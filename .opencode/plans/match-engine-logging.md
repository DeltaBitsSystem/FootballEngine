# Match Engine E2E Logging Infrastructure

## Goal

Add comprehensive per-event logging to the match engine that captures every important decision and outcome in the simulation. Output: one JSON file per match. Zero overhead in Release builds. Debug-only activation.

## Design Principles

1. **Zero-cost in Release** — `#if DEBUG` guards. In Release, the logger is a no-op struct that the JIT eliminates
2. **No allocation in hot path** — logger pre-allocates all buffers. Events are structs stored in a pre-sized list
3. **JSON serialization only at end** — accumulate in memory, serialize once when match ends
4. **One file per match** — `diag/match_<timestamp>.json`
5. **Easy to extend** — adding a new pipeline = add a new log type + call site. No boilerplate

## Architecture

```
MatchLogger (struct, pre-allocated buffers)
├── OnPassLaunched(...)     → PassLog entry
├── OnPassArrived(...)      → PassLog entry (update)
├── OnPassResult(...)       → PassLog entry (update)
├── OnShotLaunched(...)     → ShotLog entry
├── OnShotArrived(...)      → ShotLog entry (update)
├── OnShotResult(...)       → ShotLog entry (update)
├── OnDecision(...)         → DecisionLog entry
├── OnBallUpdate(...)       → BallLog entry (per tick)
├── OnPossessionChange(...) → PossessionLog entry
├── OnFoul(...)             → FoulLog entry
├── OnSetPiece(...)         → SetPieceLog entry
├── OnMomentum(...)         → MomentumLog entry
└── Dump(filePath)          → serialize all to JSON
```

## File Structure

### New files to create

| File | Purpose |
|---|---|
| `Engine/Match/Logging/MatchLog.fs` | All log types (records) + MatchLogger module |
| `Engine/Match/Logging/JsonLog.fs` | JSON serialization using System.Text.Json |

### Files to modify (surgical injections)

| File | Change | Line cost |
|---|---|---|
| `Engine/Match/Simulation/Simulator.fs` | Pass logger to runLoop, call Dump at end | ~10 lines |
| `Engine/Match/Core/MatchStepper.fs` | Pass logger through, call logger on each system | ~5 lines per system |
| `Engine/Match/Player/Actions/PassAction.fs` | Call `logger.OnPassLaunched(...)` after launch | 1 line |
| `Engine/Match/Player/Actions/ShotAction.fs` | Call `logger.OnShotLaunched(...)` after launch | 1 line |
| `Engine/Match/Player/Decision/PlayerDecision.fs` | Call `logger.OnDecision(...)` after decide | 1 line |
| `Engine/Match/Ball/BallSystem.fs` | Call `logger.OnBallUpdate(...)` per tick | 1 line |
| `Engine/Match/Ball/ContactResolver.fs` | Call `logger.OnPassArrived(...)` / `OnShotArrived(...)` | 2 lines |
| `Engine/Match/Ball/OutcomeResolver.fs` | Call `logger.OnPassResult(...)` / `OnShotResult(...)` | 2 lines |
| `FootballEngine.Client.fsproj` | Add compile entries for new files | 2 lines |

**Total: ~25 lines of call sites across 8 files.**

## Log Types

### MatchLog (root)

```fsharp
type MatchLog =
    { MatchId: string
      HomeClub: string
      AwayClub: string
      FinalScore: int * int
      Duration: float
      Passes: PassLog[]
      Shots: ShotLog[]
      Decisions: DecisionLog[]
      BallUpdates: BallLog[]       // only when ball changes (not every tick)
      PossessionChanges: PossessionLog[]
      Fouls: FoulLog[]
      SetPieces: SetPieceLog[]
      Momentum: MomentumLog[] }
```

### PassLog

```fsharp
type PassLog =
    { Tick: int
      PasserId: int
      ReceiverId: int
      FromX: float; FromY: float
      ToX: float; ToY: float
      Quality: float            // beta sample result
      PassMean: float           // base mean before sampling
      DistancePenalty: float
      Speed: float
      // Outcome phase (filled on arrival/result)
      ArrivedAt: int option     // tick when ball arrived
      ControlProb: float option // control probability applied
      ControlSuccess: bool option
      InterceptorId: int option // who intercepted if failed
      Outcome: string           // "completed" | "intercepted" | "out_of_bounds" | "offside" | "loose" }
```

### ShotLog

```fsharp
type ShotLog =
    { Tick: int
      ShooterId: int
      FromX: float; FromY: float
      Quality: float
      ShotSpeed: float
      AngleDeg: float
      PeakHeight: float
      XG: float
      // Outcome phase
      ArrivedAt: int option
      GKSaveAttempt: bool       // was GK save system invoked?
      GKSaveProb: float option  // save probability used
      GKSaveSuccess: bool option
      Outcome: string           // "goal" | "saved" | "off_target" | "blocked" | "post" }
```

### DecisionLog

```fsharp
type DecisionLog =
    { Tick: int
      PlayerId: int
      Zone: string              // attacking/midfield/defensive
      ShootScore: float
      PassScore: float
      DribbleScore: float
      CrossScore: float
      LongBallScore: float
      Chosen: string            // "Shoot" | "Pass" | "Dribble" | "Cross" | "LongBall" | "None"
      Temperature: float }
```

### BallLog (only on change)

```fsharp
type BallLog =
    { Tick: int
      X: float; Y: float; Z: float
      Vx: float; Vy: float; Vz: float
      Control: string           // "Free" | "Controlled(side,id)" | "Airborne" | ...
      LastTouchBy: int option }
```

### PossessionLog

```fsharp
type PossessionLog =
    { Tick: int
      NewSide: string           // "Home" | "Away"
      Trigger: string }         // "pass" | "interception" | "goal" | "set_piece"
```

### FoulLog, SetPieceLog, MomentumLog

Simple event logs with tick, players involved, type, and context.

## Debug-Only Activation

### MatchLogger type

```fsharp
#if DEBUG
[<Struct>]
type MatchLogger =
    { mutable Passes: ResizeArray<PassLog>
      mutable Shots: ResizeArray<ShotLog>
      mutable Decisions: ResizeArray<DecisionLog>
      // ... all buffers
      mutable Enabled: bool }

    static member Create() =
        { Passes = ResizeArray(200)
          Shots = ResizeArray(50)
          // ...
          Enabled = true }

    member this.Dump(filePath: string) = JsonLog.serialize filePath this
#else
[<Struct>]
type MatchLogger =
    member this.OnPassLaunched _ = ()
    member this.OnPassArrived _ = ()
    // ... all methods are no-ops
    member this.Dump _ = ()
#endif
```

In Release builds, the JIT inlines all methods to `()` and eliminates the struct entirely. **Zero cost.**

### Activation in Simulator

```fsharp
let runLoopFull (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : MatchReplay =
    let logger = MatchLogger.Create()  // no-op in Release
    // ... pass logger through systems ...
    let finalState, events = runLoop ctx state clock takeSnapshotInt logger
    let diagPath = $"diag/match_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
    logger.Dump(diagPath)              // no-op in Release
    // ...
```

## Injection Points (exact locations)

### Pass Pipeline (5 touch points)

| Step | File | Function | Log call |
|---|---|---|---|
| 1. Launch | `PassAction.fs:328` | After `events.Add(BallUpdate finalBall)` | `logger.OnPassLaunched(tick, passer, target, quality, from, to, speed)` |
| 2. Arrival | `ContactResolver.fs:~210` | In `evaluateArrival`, when `IntendedReceiver` | `logger.OnPassArrived(tick, ballPos, targetId, controlProb)` |
| 3. Result OK | `OutcomeResolver.fs:188` | In `IntendedReceiver` branch, after `bernoulli` | `logger.OnPassResult(tick, "completed")` |
| 4. Result Fail | `OutcomeResolver.fs:191` | In `IntendedReceiver` branch, else clause | `logger.OnPassResult(tick, "intercepted")` |
| 5. Out of bounds | `OutcomeResolver.fs:~100` | In `OutOfBounds` branch | `logger.OnPassResult(tick, "out_of_bounds")` |

### Shot Pipeline (5 touch points)

| Step | File | Function | Log call |
|---|---|---|---|
| 1. Launch | `ShotAction.fs:213` | After `events.Add(BallUpdate newBall)` | `logger.OnShotLaunched(tick, shooter, quality, speed, angle, xg)` |
| 2. GK Save check | `ContactResolver.fs:~160` | In `evaluateShotSave` | `logger.OnShotArrived(tick, gkSaveAttempt, gkSaveProb)` |
| 3. Goal | `OutcomeResolver.fs:206` | `GoalScored` branch | `logger.OnShotResult(tick, "goal")` |
| 4. Saved | `OutcomeResolver.fs:188` | GK catches shot | `logger.OnShotResult(tick, "saved")` |
| 5. Off target | `OutcomeResolver.fs:~100` | `OutOfBounds` with `Struck` intent | `logger.OnShotResult(tick, "off_target")` |

### Decision Pipeline (1 touch point)

| Step | File | Function | Log call |
|---|---|---|---|
| 1. Decision | `PlayerDecision.fs:153` | After `softmaxSample` | `logger.OnDecision(tick, playerId, scores, chosen, temp)` |

### Ball Pipeline (1 touch point)

| Step | File | Function | Log call |
|---|---|---|---|
| 1. Ball state | `BallSystem.fs:81` | After `outputs.Add(DomainEvent.BallUpdate updatedBall)` | `logger.OnBallUpdate(tick, ball)` |

### Possession (1 touch point)

| Step | File | Function | Log call |
|---|---|---|---|
| 1. Change | `BallSystem.fs:108` | After `BallSecured` semantic | `logger.OnPossessionChange(tick, club, "pass")` |

## JSON Output Format

```json
{
  "matchId": "match_20260616_143022",
  "home": "Real Madrid",
  "away": "Barcelona",
  "finalScore": [2, 1],
  "duration": 5400.0,
  "passes": [
    {
      "tick": 1200,
      "passerId": 42,
      "receiverId": 7,
      "fromX": 45.2,
      "fromY": 30.1,
      "toX": 62.8,
      "toY": 25.3,
      "quality": 0.82,
      "passMean": 0.71,
      "distancePenalty": 0.05,
      "speed": 12.0,
      "arrivedAt": 1245,
      "controlProb": 0.85,
      "controlSuccess": true,
      "interceptorId": null,
      "outcome": "completed"
    }
  ],
  "shots": [...],
  "decisions": [...],
  "ballUpdates": [...],
  "possessionChanges": [...],
  "fouls": [...],
  "setPieces": [...],
  "momentum": [...]
}
```

## Implementation Order

1. **Create `MatchLog.fs`** — all log types + MatchLogger with `#if DEBUG` guards
2. **Create `JsonLog.fs`** — JSON serialization
3. **Update `.fsproj`** — add compile entries
4. **Inject call sites** — one line per pipeline step (8 files, ~25 lines total)
5. **Update `Simulator.fs`** — pass logger through, call Dump at end
6. **Build and verify** — `dotnet build` in both Debug and Release
7. **Run diagnostics** — verify JSON output is generated and contains data

## Files Summary

| Action | File | Lines |
|---|---|---|
| CREATE | `Engine/Match/Logging/MatchLog.fs` | ~120 |
| CREATE | `Engine/Match/Logging/JsonLog.fs` | ~40 |
| MODIFY | `FootballEngine.Client.fsproj` | +2 |
| MODIFY | `Engine/Match/Simulation/Simulator.fs` | +10 |
| MODIFY | `Engine/Match/Core/MatchStepper.fs` | +5 |
| MODIFY | `Engine/Match/Player/Actions/PassAction.fs` | +1 |
| MODIFY | `Engine/Match/Player/Actions/ShotAction.fs` | +1 |
| MODIFY | `Engine/Match/Player/Decision/PlayerDecision.fs` | +1 |
| MODIFY | `Engine/Match/Ball/BallSystem.fs` | +2 |
| MODIFY | `Engine/Match/Ball/ContactResolver.fs` | +2 |
| MODIFY | `Engine/Match/Ball/OutcomeResolver.fs` | +4 |
| **Total** | | **~190 lines** |

## What This Enables

After implementation, for any match we can answer:

1. **Why did pass X fail?** → See quality, distance penalty, control prob, who intercepted
2. **Why no goals?** → See shot quality, GK save prob, whether shots reach the goal
3. **Why low pass accuracy?** → Funnel: how many launched → arrived → controlled → completed
4. **Why no fouls?** → See tackle attempts vs fouls in the foul log
5. **Is the convergence threshold blocking arrivals?** → Compare `arrivedAt` vs expected arrival tick
6. **Is the GK save system working?** → See `gkSaveAttempt` and `gkSaveProb` for every shot

No more guessing. Data-driven fixes.
