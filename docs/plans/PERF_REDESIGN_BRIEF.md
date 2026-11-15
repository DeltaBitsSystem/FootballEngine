You are the architect for a substantial PERFORMANCE redesign of the FootballEngine match engine (F#, .NET 10, this repo). Your job is ONLY to think, measure and design. Do not edit code. Write your design to `docs/plans/PERF_REDESIGN.md`; that is the only file you create. We will implement it afterwards, so it must be concrete enough to implement without you.

## Read first (all real, measured history)
- `AGENTS.md`: architecture, rules, tick model, systems never call each other, every behaviour number lives in BalanceConfig, determinism.
- `docs/plans/MAINTAINABILITY_PERFORMANCE_PLAN.md`: the F1 section and "Cierre" are the full log of what we tried and measured.
- `docs/plans/F1_ENGINE_REDESIGN.md`: an earlier redesign proposal, never executed.
- `docs/evidence/MATCH_ENGINE_FOUNDATION.md`: scheduling and determinism decisions.
- Code: `FootballEngine.Core/Match/` (Stepper/Scheduler.fs, Stepper/SystemsTable.fs, Stepper/Stepper.fs, Stepper/EventApply.fs, Systems/ContactDynamics.fs, Systems/Players/Decision.fs and the planner, Systems/Team/Position.fs, Contracts/World.fs, Frames.fs, BalanceConfig.fs).

## What we went through (summary; details in the plan doc)
- Target F1: ≤1.5 s per full match, ≤100 MB allocated per match, same events for the same seed unless a change is justified and recalibrated held-out.
- 12.8 s → ~5.0 s through allocation removal, pure refactors with identical hash, and contact segments 4→2.
- Then sporting fixes (restarts froze ~1/3 of the match) raised live play from 129k to 202k ticks, and the cost went back up.
- After the F3.2 expected-value decision rework: ~7.2 s per match. Breakdown: PlayerDecision 2.1 s / 1.45 GB, ContactDynamics 1.8 s, TeamPosition 0.9 s.
- 54,932 ball-carrier decisions per match (re-decide every ~8 ticks), 66 planner evaluations per decision, about 29 µs per decision.
- Tried and rejected:
  - Planner states only for beam survivors: less memory, no time gain; the cost is the feasibility computation.
  - Re-decide every 0.5 s: faster, but quality stops paying off (+10 in all attributes wins by 0.35 instead of 0.65), so it fails acceptance.
  - Single contact segment for balls without spin: almost every ball has spin, so no change.
  - RedecisionInterval with a latch: the gate never bound; a fake gain came from comparing different-length matches.
- Measured levers:
  - 20 Hz physics: 7.2 → 5.5 s, but cognition is event-driven and doesn't drop, and it would need ~25% recalibration of everything.
  - Cognition on its own thread: → 5.8 s (with 20 Hz, ~4.1 s).
  - 2-step planner chains instead of 4: about 9% of the match.
- Measurement trap: ticks per match vary from 263k to 363k after any sporting change. Compare µs per tick or the total of `validate 48`, never a single match.
- Context: background matches use `QuickMatch`. The full engine plays only the user's own fixtures, plus tests and calibration (Core suite ~2 min, target <1 min). Speed matters for iteration (tests, calibration sweeps) and for a live viewer.
- Sporting acceptance that must survive:
  - held-out 48 matches in band;
  - "+10 in all attributes wins by ≥0.6 goals in 144 matches";
  - determinism per seed.
- Commit `66d0eee` added rival managers and key-moment pacing, so the current numbers may differ. MEASURE the current baseline yourself:
  - `dotnet build FootballEngine.slnx -c Release`
  - `dotnet fsi scripts/dev.fsx profile`: per-system time and allocation.
  - `dotnet fsi scripts/dev.fsx hash`
  - See `scripts/dev.fsx` and the `Training` CLI (`profile`, `validate`) for more.

## What to write (in Spanish)
1. Current measured baseline: per system, µs/tick, ticks per match, allocations, with the commands you ran.
2. Root cause: why the engine costs what it costs structurally (work per tick × ticks × agents). Say where micro-tuning hit a ceiling, and why.
3. ONE coherent, substantial redesign, compared against 2–3 alternatives you considered and rejected, with reasons. Think boldly, for example:
   - multi-rate simulation (physics vs cognition vs team);
   - analytic or event-time ball flight instead of per-tick contact search;
   - level of detail for players far from the ball;
   - incremental or cached decision values;
   - SoA data layout and SIMD;
   - batch evaluation;
   - parallel matches for tests and calibration;
   - anything else.
   Respect the architecture rules, or say explicitly which rule must change and why.
4. For the chosen design: contracts and types, state ownership, update order, invalidation rules, files that change, and how determinism and the event hash are preserved or why they intentionally change.
5. Expected gain per step with your estimation method, plus a measurement plan.
6. Staged implementation plan. Each stage independently verifiable (identical hash where pure; held-out plus quality contrast where sporting), ordered by gain/risk.
7. Risks, and what would make you abandon a stage.

Be honest. Ground every claim in code you read or numbers you measured. Don't invent. If 1.5 s is unrealistic under the sporting constraints, say what is realistic.
