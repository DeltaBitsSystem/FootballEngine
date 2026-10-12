# Collective and individual football intelligence

## Implementation status

The team-owned contract, physical duty consumers, observed-motion individual
anticipation, bounded online contextual learner, accepted-outcome persistence,
seasonal retention and strict checkpoint/save break are implemented. Rebuilt
Release gates pass: 608 Core and 162 Client tests plus solution build. Measured
paired results and limitations are recorded in
[`docs/evidence/COLLECTIVE_INTELLIGENCE.md`](../evidence/COLLECTIVE_INTELLIGENCE.md).
This is not a separately trained individual policy or proof of multi-season
learning-quality convergence; those broader claims are not established.

## Objective and scope

Implement coordinated, sustained attacking intentions, autonomous individual
execution and evidence-driven adaptation inside a match. Between matches and
seasons retain a small, bounded learning memory, rather than implement a full
development or coaching simulator. Preserve deterministic execution, checkpoint
continuation and sporting replay. No old-contract adapters or save migration.

Realism requires consequences from actual actions; fun requires multiple viable
ways to create an advantage, counterplay, imperfect execution and meaningful
managerial tradeoffs. Neither a desired score nor a hidden opponent policy may
be an input to the intelligence.

## Findings in the existing engine

- `Planner` already evaluates short action chains, but its simulated continuations
  freeze teammates and defenders in place and greedily choose the next action.
- `PlayerDecision` writes `ActivePlan` every decision and discards it on a carrier
  change. This cannot represent a team combination that survives a completed pass.
- `PlayerIntent` consumes proposed receiver runs, but there is no staged collective
  objective, role ownership, progress accounting or learned evaluation of episodes.
- `Planner.value` valued progression in world X, disadvantaging the mirrored away
  attack, and counted the carrier himself as possession support. These defects are
  corrected first, with mirrored and isolated-carrier regression tests.
- Defensive transitions already own coordinated defensive duties. The new attack
  contract must not overwrite them, restart routines or authoritative receptions.
- `CoordinationMemory`, `ClubRef`, world outcome application and strict save chunks
  are the existing seams for persistent memory, kickoff initialization and commit.
- `World.Observation` is optional evidence, not the source of learning. Turning it
  off must not change football decisions or consume different sporting RNG.

## Football grounding

The third-man mechanism is not a compulsory pass sequence. A carrier attracts
pressure, a supporting player supplies a different passing angle, and another
player attacks the newly available space. Opponents can keep their positions,
track that runner or close the second passing lane. Success therefore has to be
measured from actual movement and receptions, not from announcing the plan.

Source read during design: Coaches' Voice, *Third-man runs: football tactics
explained* (13 January 2022):
https://learning.coachesvoice.com/cv/third-man-runs-football-tactics-explained-gasperini-guardiola/

The implementation applies these mechanisms; it does not claim empirical
calibration to professional tracking data.

## Architecture

### One owner for collective attacking plans

Add a Team-owned collective system in the sole `SystemsTable`. It observes facts,
creates one bounded episode, selects a feasible pattern and publishes duties in
`TeamWorld`. Players read those duties; they do not call the team system. The
system must observe possession/receiving/action/participation/tactics facts and
geometry refreshes through the established scheduler contract.

Patterns to compare:

1. Third-man combination: link player plus complementary runner.
2. Attract and switch: near-side support and a far-side outlet.
3. Depth exploitation: legal runner plus a security outlet.
4. Secure progression: complementary passing angles and retained protection.

No pattern forces a pass, guarantees a lane opens, teleports a participant or
increases technical skill. Selection must consider tactical intent, actual
available players, usable geometry, fatigue and uncertainty.

### Sustained episodes, not refreshed animation targets

An episode owns an identity, pattern, original situation, participant identities,
creation frontier, deadline, stage, baseline advantage and terminal reason. It
survives expected teammate carrier changes and its own pass flight. Unexpected
possession loss, opponent interception, restart, departure of a required
participant or incompatible managerial instructions terminate or repair it.
Deadlines bound both stale commitments and storage.

Stages represent preparation, a real intermediate connection and exploitation.
Stage progress requires an actual matching football fact. Geometric opportunities
can change a target without inventing completed passes. Replacement duties cannot
leave retired players with movements or reuse a slot as the same participant.

### Individual anticipation and autonomy

Keep the carrier's existing utility options and deterministic RNG. Collective
intent contributes a bounded value term, scaled by cognition, teamwork and
confidence. It cannot restore an infeasible/offside option or suppress a better
shot or emergency clearance. Non-carriers consume complementary duties through
`PlayerIntent`, below restart/receive/defensive safety priorities; steering remains
the physical consumer.

Improve individual continuation evaluation using the attacking reference frame,
bounded search and physically plausible projected movements. Do not expose
opponent pending choices, exact future outcomes or arbitrary global knowledge to
the individual planner. Skill and fatigue must affect anticipation, execution and
the confidence placed in a continuation.

### Learning that actually runs

Use a bounded contextual learner over collective options, updated when episodes
resolve, not once per tick. Start with an interpretable contextual bandit/value
estimate: sufficient statistics, uncertainty and limited exploration. This is
online adaptive learning, not a pretrained neural policy or a claim of validated
reinforcement learning.

Reward is a bounded combination of actual territorial/space advantage, successful
connections, produced threats, possession loss and exposure cost. A timeout with
no realized advantage is not a success. A goal cannot credit every unrelated
previous plan. Duplicate facts and repeated evaluation cannot update the learner
twice. All learning rates, uncertainty, commitment thresholds and reward weights
belong in named `BalanceConfig` fields and are validated.

Retain a compact club-level prior and pattern familiarity between matches. Merge
only accepted match contributions once. Distinguish prior evidence from new
match evidence to avoid multiplying old observations on each commit. Decay or cap
evidence across seasons so an old successful strategy cannot dominate forever.
No opponent-specific unbounded archive is required for this first implementation.

### Evidence and persistence

Emit semantic events for plan creation, real stage changes and termination, plus
bounded causal observations for evaluations, realized rewards and individual
cooperation. Decisions and learning cannot depend on capture settings.

Extend the explicit match checkpoint with collective episode and learner state;
reject the preceding checkpoint format. Save schema must also advance because
club priors and kickoff inputs change. Update result/input hashing, saved original
inputs, world commits, season rollover and diagnostic/replay consumers. Persist
small sporting memory, not observations or render state.

## Implementation and validation gates

1. Correct the individual value baseline and run existing planner tests.
2. Implement typed collective contracts, validated configuration and the bounded
   learner; test uncertainty, negative evidence, bounded retention and ownership.
3. Implement the episode owner and movement/decision consumers; remove the old
   carrier-change-invalidated `ChainPlan` rather than maintain two competing plans.
4. Connect events, observations, checkpoint format, match result extraction,
   club-memory commits, strict save schema and season decay.
5. Test mirrored opportunities, legal complementary duties, autonomy, persistence
   over real passes, opponent counterplay, fatigue, participation changes,
   interruption, repetition, bounded memory and causal attribution.
6. Compare identical scenarios with intelligence enabled/disabled and competing
   tactical/rival responses. Report actual football effects and costs; do not
   assert that a feature is good merely because it raises win rate or pass count.
7. Verify exact replay, checkpoint continuation, different chunk sizes and disabled
   observation. Run complete rebuilt Core and Client suites and a measured seeded
   corpus/performance gate. Preserve all unrelated shared-worktree changes.

## Current verified progress

The initial individual-value corrections compile. The planner prefix runs
**20/20 tests successfully**, including the new attacking-direction symmetry and
real-support regressions. Collective contracts, integrated learning, persistence
and the final corpus remain to implement and validate; this document is a design,
not a completion report.
