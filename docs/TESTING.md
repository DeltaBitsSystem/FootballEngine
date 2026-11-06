# Repository testing

## One command, local and CI

From the repository root, with the SDK selected by `global.json`:

```sh
dotnet fsi scripts/test.fsx
```

No global tools, Python, Bash, test adapter or extra NuGet package is needed for the runner. Expecto remains the native F# test framework. The F# script uses .NET process/XML APIs and runs the same commands on Windows, Linux and macOS; actual native UI/GPU availability is a separate environment requirement. On Unix, `./scripts/test.sh` forwards arguments to this same implementation and works outside the repository directory.

The runner:

1. Checks its own argument parsing, discovery and report validation.
2. Discovers source `*.Tests.fsproj` projects recursively, excluding generated directories.
3. Builds each selected project in Release (including restore and project dependencies).
4. Executes it with `dotnet run --no-build` and strict Expecto options.
5. Validates its fresh JUnit report and aggregates results across projects.

Projects run sequentially to avoid competing builds and preserve readable logs. Expecto still schedules independent cases according to the suites' configuration. Build/test failures do not prevent attempting the other selected projects. Missing or invalid reports, empty execution, ignored/skipped cases and focused tests cannot become success. Native child exit codes are treated as failures; the top-level gate returns nonzero.

## Focused development

```sh
dotnet fsi scripts/test.fsx --project Core --list
dotnet fsi scripts/test.fsx --project Core --filter "FootballEngine.Core.FootballEngine.Core.Contracts.Rng"
dotnet fsi scripts/test.fsx --project Client
dotnet fsi scripts/test.fsx --help
```

`--project` accepts Core, Client or the full project stem, such as `FootballEngine.Core.Tests`. New projects need no central list: name them `*.Tests.fsproj` and implement an executable Expecto entrypoint supporting the existing reporting options. Non-Expecto projects require an explicit runner design change, not silently treating `dotnet run` as tests.

`--filter` is Expecto's dot-separated full-name prefix, not an arbitrary substring. Discover and copy names first. Prefer pairing it with `--project`: a filter selecting nothing in any selected project fails that project's gate. Test names currently sometimes repeat namespace segments; the runner does not rename them.

`--list` only lists names, without executing game tests. It deliberately cannot combine with `--filter`, because Expecto's list mode does not reliably apply execution filters. `--self-test` checks infrastructure only. Neither mode certifies game acceptance. Arbitrary Expecto passthrough options are intentionally unsupported: they could override strictness, output or selection. Add a documented, tested runner option when a real use case requires one.

## Reports and CI

Each invocation creates a unique `artifacts/tests/<timestamp-guid>/` directory with per-project build logs, test logs and JUnit XML. Unique directories prevent an old passing report from validating a failed or empty new run. Results are ignored by git under the repository's existing allowlist policy. Logs include test failures and Expecto's timing summary; XML contains per-case durations.

`.github/workflows/ci.yml` installs the SDK from `global.json`, checks formatting and builds the solution/tools, then invokes exactly `dotnet fsi scripts/test.fsx`. It does not maintain another list of test projects. Reports/logs are uploaded with `always()` for diagnosis. Do not add a CI-only subset or retry failing tests until green.

This runner does not automatically execute Python/Blender probes, GPU screenshots, benchmarks, packaged-platform smoke tests or multi-season validation beyond registered Expecto cases. Record and run those separately for relevant work; future levels should extend this entrypoint explicitly without silently shrinking the default gate.

## Maintaining the runner

- `scripts/test.fsx`: entrypoint and failure boundary.
- `scripts/testing/TestRunner.fsx`: discovery, selection, build/execute and JUnit validation.
- `scripts/testing/TestRunnerTests.fsx`: dependency-free infrastructure checks, run automatically before game tests.
- `scripts/test.sh`: forwarding wrapper only.

Run `dotnet fsi scripts/test.fsx --self-test` after changing infrastructure, then exercise a real passing suite, a nonexistent filter, an invalid project and failure propagation. Check fresh reports, not only console output. Expecto upgrades must revalidate real emitted JUnit for passing, failing, skipped and empty runs. Never parse colored console summaries as the success oracle.

Tests must be included in the `.fsproj` compilation order and registered in the executable's `Program.fs`. Automatic project discovery does not discover forgotten source files or guarantee test quality; the contract/oracle review rules in `AGENTS.md` still apply. Adding property-based or mutation checks, auditing omitted source files and classifying existing expensive suites are separate improvements, not claims made by this infrastructure change.
