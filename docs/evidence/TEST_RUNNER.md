# Unified Expecto runner — validation

## Implementation

Local and CI now invoke `dotnet fsi scripts/test.fsx`. Source test projects are discovered rather than duplicated in CI. The runner builds Release, executes Expecto, preserves logs/JUnit per invocation and checks both child exit and actual nonempty results. Focused/ignored cases are not accepted. Bash forwards only; the implementation needs only the .NET SDK. `global.json` selects CI's SDK. No game test assertion or workload was reduced for this infrastructure change.

`AGENTS.md` records whole-game risk priorities, independent oracles, negative controls/mutations, review of tolerance/golden changes, separation of calibration/evaluation, honest coverage and measured execution levels. `docs/TESTING.md` documents commands, extension and limits. `.editorconfig` uses four-space indentation for the new F# runner scripts without reformatting unrelated scripts.

## Checks performed

- Runner self-tests: 22 passed, covering options, source discovery, project selection/collisions and missing/malformed/empty/failing/skipped JUnit.
- Real Core RNG selection through the entrypoint: 13 passed; Release build succeeded.
- Nonexistent filter: Expecto itself returned exit 0 for zero tests; repository runner correctly returned exit 1.
- Unknown project: runner returned exit 1.
- Bash wrapper from `/tmp/opencode`: runner self-tests passed, confirming paths do not depend on caller cwd.
- Real Expecto 11.1 fixtures (pass, assertion failure, pending, focused, empty) were run outside the repo through `runProcess` and report validation. Only pass was accepted. Pending/empty had child exit 0 but were rejected by JUnit validation; focused failed before producing an accepted report.
- `git diff --check` and Fantomas check for the three new F# scripts passed.

## Initial full-suite execution exposes contact work in progress

`dotnet fsi scripts/test.fsx` wrote logs/XML to:
`artifacts/tests/20261105T022656128-1bf9e3535478412e9f474e1f6f5b740d/`.

- Client.Tests: build failed at `LiveMatchTests.fs:736`, ambiguous `ContactFoot.Right` after a contact-contract change; no Client test execution claimed.
- Core.Tests: 680 executed, 676 passed, four failed, no ignored/errors; runtime approximately 110.44 seconds under local shared load.
- Failures: FootMotion fixed-subtick displacement, goal-line physical deflection provenance, complete-pass statistical floor (40.8 vs >100), and collective-learning chronology scenario not reaching its saturation condition (35 vs >48).
- The runner attempted Core despite Client's failed build and returned exit 1, retaining both diagnostics. These are failures in the paused contact implementation, not a green game baseline. They must be diagnosed without weakening acceptance.

This is evidence for execution infrastructure, not certification of contact completeness, source/branch coverage, all platforms, packaged startup, screenshots or benchmark budgets. Subsequent contact corrections require a new full run and fresh artifacts.
