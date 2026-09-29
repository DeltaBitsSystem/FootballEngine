# FootballEngine - Authoritative Set Pieces Master Plan

## 1. Single Objective

Replace the distributed restart implementation with one deterministic, causal and authoritative set-piece subsystem that cannot remain indefinitely in setup or authorization, preserves the real football reason for every stoppage, positions players physically, executes through the common ball impulse path, and produces faithful replay/UI information.

This is the only objective of this plan. Do not create additional planning documents. Update this file with completion evidence if needed.

## 2. Execution Instructions

Implement this plan completely in one pass.

1. Read `AGENTS.md` fully before editing.
2. Run `git status --short`; the worktree is shared and already contains extensive intentional changes.
3. Preserve all unrelated changes. Never revert, reset, checkout, amend, commit or push.
4. Verify current code before changing it; compiler and tests are authoritative.
5. Use `apply_patch` for manual edits.
6. Build the smallest affected project after each contract change.
7. Run focused tests after each gate and the complete Core and Client suites at the end.
8. Do not stop after analysis, scaffolding or partial migration. Finish implementation and validation.
9. Do not add compatibility adapters, old/new dual paths, feature flags, migrations or deprecated types. There are no released saves requiring compatibility.
10. Delete obsolete restart code once the replacement compiles.
11. Do not solve failures with fabricated impulses, arbitrary phase timeouts, teleports, seed-specific branches or weakened tests.
12. If an old test asserts deleted internals, replace it with a stronger sporting, causal or determinism invariant.

## 3. Verified Problem

The current restart lifecycle is distributed across:

- `EventApply`, which awards restarts and places the ball.
- `Flow`, which decrements setup time.
- `SetPieceRoutines`, which assigns positions, checks readiness and launches.
- `PlayerIntent`, which stores restart movement in generic mutable intent arrays.
- `PlayerSteering`, which moves participants.
- `Stepper`, which infers completion from loose fields on `BallState`.
- `Replay` and `MatchViewer`, which encode and present only part of the cause.

`RestartState` does not own a complete plan. Wall membership, legal targets and initialization are inferred from mutable player intent. Different event routes initialize differently. Re-evaluating moving players can exchange targets. Near goal lines, generic distance checks can demand physically impossible positions. `Ready` does not structurally guarantee exactly one command and one causal contact.

The failure is reproducible in long deterministic scans:

- Restarts remain in `DeadBallSetup 0<tickDelta>` for thousands of ticks.
- Other restarts remain in `Ready` for thousands of ticks.
- Offside-created free kicks do not always follow the same initialization route as foul-created free kicks.
- Goal-line free kicks can require every defender, including the goalkeeper, to stand 9.15 m away even where the Laws permit defenders on the goal line between the posts.
- Generic technical state was previously displayed as `WHISTLE - DEAD BALL`; current worktree changes already begin encoding concrete restart kinds. Preserve and integrate that direction.

## 4. Non-Negotiable Architecture

### 4.1 Single Owner

Create one `RestartController` as the exclusive owner of restart lifecycle transitions.

- `EventApply` may award a restart by creating its canonical initial state.
- `RestartController` alone may move a restart through its phases.
- `PlayerIntent` and `PlayerSteering` execute assigned movement but cannot authorize, execute or complete a restart.
- `Stepper` must not infer restart completion from an ad hoc tuple of flow, impulse and pending flags. It delegates the observed contact to the controller.
- No other system may write `MatchFlow.Restart` phases.

### 4.2 Explicit Identity And Cause

Introduce explicit match-local restart identity and cause. Names may follow local conventions, but the contract must encode equivalent information:

```fsharp
type RestartId = RestartId of uint64

type RestartCause =
    | MatchStart
    | PeriodStart
    | GoalConceded
    | BallCrossedTouchline of lastTouch: PlayerId option
    | BallCrossedGoalLine of lastTouch: PlayerId option
    | Foul of offender: PlayerId option * victim: PlayerId option
    | Offside of player: PlayerId
    | VAROverturn of VARIncident
```

The restart cause is not the same as the restart method. Offside and foul can both produce a free kick but remain distinct causes in events, diagnostics and replay.

`RestartId` must be deterministic and unique by occurrence. Derive it from canonical match-local occurrence sequencing, not structural event hashing.

### 4.3 Explicit Immutable Plan

Planning happens exactly once per `RestartId`. Store the resulting plan in canonical restart state:

```fsharp
type RestartTarget =
    { Side: ClubSide
      Player: PlayerId
      Role: RestartRole
      Position: Vec2 }

type RestartAction =
    | RestartPass of receiver: PlayerId
    | RestartCross of target: Vec2
    | RestartShot of target: Vec2
    | RestartClearance of target: Vec2

type RestartPlan =
    { Id: RestartId
      Cause: RestartCause
      Kind: SetPieceKind
      Side: ClubSide
      Spot: Vec2
      Executor: PlayerId
      Targets: RestartTarget array
      Action: RestartAction
      AwardTick: int<subtick> }
```

Use the repository's existing spatial and command types where appropriate rather than duplicating them. The semantic requirements are mandatory; exact names are not.

The plan must be:

- Deterministic for context, world, seed and restart occurrence.
- Stable after publication.
- Independent of participant movement after publication.
- Fully legal or rejected before becoming active.
- Stored in restart state, not reconstructed from generic `Intent` arrays.

### 4.4 Explicit Lifecycle

Replace the current ambiguous lifecycle with phases equivalent to:

```text
Awarded -> Positioning -> Authorized -> AwaitingContact -> Completed
```

Required semantics:

- `Awarded`: canonical award exists; controller creates and validates one plan.
- `Positioning`: movement targets are active and participants move physically.
- `Authorized`: regulatory constraints are satisfied; controller emits exactly one common player action command.
- `AwaitingContact`: command has been issued and the controller awaits the matching physical impulse/contact.
- `Completed`: matching contact was observed; flow returns to `Live` exactly once.

The existing delay may remain as a minimum preparation duration, but it cannot be the authority for readiness and cannot itself manufacture progress.

Every transition must be total and testable. No phase may depend on a hidden marker such as magic `RunLookup` phases or `Intent.CommittedAt` equality.

### 4.5 Common Command And Impulse Path

At authorization, create exactly one typed player command using the same command/action boundary as open play.

- Pass, cross, shot and clearance use the common action execution and `BallPhysics.createImpulse` path.
- The resulting impulse includes `RestartId`, actor, contact tick, strike kind and execution context.
- `AwaitingContact` completes only from an impulse whose `RestartId`, actor and set-piece kind match the active plan.
- Remove `BallState.PendingRestart` if it becomes redundant. Do not retain duplicate restart identity.
- Never launch directly merely because a phase value changed.

### 4.6 Movement Ownership

- The plan owns semantic restart targets.
- `PlayerIntent` projects those targets into typed restart movement intents.
- `PlayerSteering` remains the only normal physical player mover.
- No direct player position writes or teleports are allowed.
- Target assignment is stable by `PlayerId` for the whole restart.
- Separation may influence the path but must not alter target ownership.
- Steering forces must be bounded so separation cannot permanently overpower arrival.

### 4.7 Regulatory Validation

Create a pure restart-rules validator returning structured results, not one aggregate Boolean.

Validate at least:

- Ball spot legality.
- Executor active and within configured contact readiness radius.
- Minimum opponent distance where applicable.
- Goal-line exception for defending players between the posts.
- Goalkeeper-specific penalty position.
- Penalty-area restrictions for penalties.
- Kickoff half and center-circle restrictions.
- Throw-in spot and in-field receiver target.
- Corner and goal-kick spot rules.
- All planned targets lie in legal reachable space.

Law constants belong to pitch/rules domain constants. Tunable movement/readiness tolerances belong in `BalanceConfig` with validation. Do not scatter new behavior numbers.

The validator must expose why positioning is not ready, suitable for diagnostics and tests.

### 4.8 Progress Without Fake Watchdogs

Infinite phases are forbidden, but do not solve them by launching after an arbitrary timeout.

Guarantee progress structurally:

- Planner only emits reachable, mutually compatible targets.
- Stable target ownership prevents target swapping.
- Steering contributions are bounded and converge within tolerance.
- Authorization emits one command atomically with phase transition.
- Command consumption is idempotent and observable.
- Matching contact completes atomically.

If a participant becomes invalid before authorization, deterministically re-plan once under the same `RestartId` with an explicit `RestartPlanChanged` event and reason. Do not poll or silently replace participants.

If a legal sporting violation occurs, emit a real referee event such as encroachment or delay and apply its rule consequence. Do not use a generic engine timeout.

## 5. Event Contract

Add or refine canonical events so the lifecycle is inspectable:

- `RestartAwarded` with ID, cause, kind, side and spot.
- `RestartPlanned` with ID and executor.
- `RestartAuthorized` with ID.
- `RestartCommandIssued` with ID and actor.
- `RestartContact` or the existing common contact/impulse event carrying the ID.
- `RestartCompleted` with ID.
- `RestartPlanChanged` only for explicit participant invalidation.
- Structured regulatory violation events where supported.

Events must have occurrence identity and causal parentage consistent with the current canonical event work. Applying the same event twice must not duplicate score, stats, commands or phase transitions.

Delete generic or duplicate set-piece events that no longer own a distinct semantic transition.

## 6. Replay And UI Contract

Replay snapshots must preserve enough information to present:

- Concrete set-piece kind.
- Concrete cause where relevant: `FOUL`, `OFFSIDE`, `THROW-IN`, `CORNER`, `GOAL KICK`, `PENALTY`, `KICK-OFF`.
- Restart side.
- Lifecycle state needed for visualization.
- Referee signal appropriate to the cause.

Do not display internal terms such as `DeadBallSetup`, `Ready`, `AwaitingContact`, `WHISTLE - DEAD BALL` or magic signal bytes.

Use a typed replay representation internally. Byte serialization may remain compact, but encoding and decoding must be centralized, exhaustive and tested. Do not duplicate numeric signal mappings in Core and Client.

## 7. Implementation Gates

### Gate 0 - Baseline And Ownership Map

1. Record `git status --short`.
2. Build `FootballEngine.Core` and list relevant tests.
3. Verify current restart writers and document them briefly in the implementation evidence.
4. Add a deterministic diagnostic test that fails if any restart remains non-live beyond a defensible convergence bound, while reporting plan ID, phase and unmet rules. This is a test guard, not runtime behavior.

Exit: reproduced failures are encoded as failing tests before architecture replacement.

### Gate 1 - Canonical Contracts

1. Add restart identity, cause, plan, roles, action and lifecycle types under `Match/Contracts`.
2. Extend common command/impulse contracts with restart identity.
3. Update world/state construction and serialization directly.
4. Remove obsolete phase/marker contracts rather than adapting them.

Exit: Core builds; contract tests cover identity, cause and plan determinism.

### Gate 2 - Planner And Rules

1. Extract a pure deterministic `RestartPlanner` from `SetPieceRoutines`.
2. Build stable participant assignment by `PlayerId`.
3. Implement reachable legal target generation for all restart kinds.
4. Implement pure structured `RestartRules.validate`.
5. Cover central, touchline, corner, penalty-area and near-goal-line geometry for both attacking directions.

Exit: planner/rules tests prove symmetry, determinism, legal targets and goal-line exceptions.

### Gate 3 - Controller

1. Implement `RestartController` as sole lifecycle writer.
2. Register it once in scheduler order after movement and before action/contact completion as required by the final data flow.
3. Award creates `Awarded`; controller plans once; positioning projects stable intents; authorization emits one command; contact completes.
4. Remove lifecycle mutation from `Flow`, `SetPieceRoutines` and `Stepper`.

Exit: lifecycle transition tests pass without direct calls between agents.

### Gate 4 - Common Execution

1. Route planned action through shared command/action execution.
2. Carry `RestartId` into physical impulse/contact.
3. Make command consumption and completion idempotent.
4. Remove magic `RunLookup` restart markers, direct `launch` ownership and redundant pending flags.

Exit: every restart kind produces one matching impulse and returns to `Live` only after contact.

### Gate 5 - Movement And Convergence

1. Project plan targets into restart intents without target reassignment.
2. Bound separation/arrival composition so players converge.
3. Handle invalidated executor explicitly.
4. Run deterministic multi-seed scans across at least 100 seeds and full-match duration.

Exit: zero indefinite `Positioning`, `Authorized` or `AwaitingContact` states; no teleports; identical seeds yield identical traces.

### Gate 6 - Events, Replay And UI

1. Emit lifecycle and cause events.
2. Serialize typed restart presentation information.
3. Replace generic viewer banners with football causes.
4. Preserve referee and assistant visualization semantics.

Exit: Client tests verify every cause/kind label and replay roundtrip.

### Gate 7 - Deletion And Final Validation

Delete obsolete code including, where superseded:

- Distributed restart phase transitions.
- Magic phase-201 markers.
- Duplicate launch path.
- Generic restart initialization branches.
- Structural inference from `BallState.PendingRestart`.
- Old signal-byte mappings.
- Tests of removed internals.

Run:

```bash
git diff --check
dotnet build FootballEngine.slnx -c Release
dotnet run --project FootballEngine.Core.Tests/FootballEngine.Core.Tests.fsproj -c Release
dotnet run --project FootballEngine.Client.Tests/FootballEngine.Client.Tests.fsproj -c Release
dotnet fantomas . --check
```

Also run focused restart, event, replay, determinism and match-session suites using exact prefixes from `-- --list-tests`.

Exit: all builds/tests/format checks pass, or only pre-existing unrelated warnings are reported with exact evidence.

## 8. Required Test Matrix

Cover every restart kind for both sides:

- Kickoff.
- Throw-in near both touchlines and both attacking thirds.
- Corner at all four corners.
- Goal kick for both teams.
- Direct and indirect-style free-kick execution paths supported by current engine.
- Free kick near midfield.
- Free kick within 9.15 m of goal line.
- Free kick near touchline and goal line simultaneously.
- Penalty.
- Offside-derived free kick.
- Foul-derived free kick.
- VAR-overturn-derived free kick.

For each relevant scenario assert:

- One deterministic ID and plan.
- Correct cause and kind.
- Correct side and legal spot.
- Stable executor and participant targets.
- No player teleport.
- Structured readiness result.
- Exactly one issued action command.
- Exactly one matching physical impulse.
- No return to `Live` before contact.
- Event application idempotence.
- Replay roundtrip preserves presentation.
- Same seed reproduces event hash and phase trace.

Add broad invariants:

- No nonterminal restart exceeds the measured legal convergence bound in 100 seeded full matches.
- No target lies outside legal pitch space.
- No restart completes without its causal contact.
- No restart impulse exists without an active matching plan.
- No generic dead-ball banner is emitted.

## 9. Performance Requirements

- Planning occurs once per restart, not every tick.
- Per-tick positioning validation uses owned arrays or bounded loops with no avoidable allocations.
- Do not optimize speculatively; benchmark before and after if restart changes affect full-match throughput materially.
- Determinism and causal correctness take precedence over micro-optimization.

## 10. Definition Of Done

The objective is complete only when:

1. One controller exclusively owns restart lifecycle.
2. Every restart has deterministic occurrence identity, cause and immutable plan.
3. All participants have stable legal physical targets.
4. All execution uses the common command and impulse path.
5. Completion requires a matching causal contact.
6. Offside, foul and ball-out causes survive into events and replay.
7. No runtime watchdog, teleport, fabricated pass or hidden marker remains.
8. Multi-seed full-match scans show no stuck restart phase.
9. Core and Client complete suites pass.
10. The solution builds in Release and formatting checks pass.
11. Obsolete distributed restart code is deleted.
12. Final report lists changed contracts, deleted legacy paths, test counts, scan results and any unrelated pre-existing warnings.
