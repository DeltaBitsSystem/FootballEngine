# Headless match evidence

These commands consume the same typed evidence as the live inspector. They do not start Avalonia, a renderer, a second simulator or an online service.

```bash
dotnet build Tools/MatchDiagnostics/MatchDiagnostics.fsproj

# Inspect a current-version archive without advancing the match.
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --summary
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --actor 2 --action 635655160321 --from 148 --until 173
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --event 123 --limit 100
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --chain 17
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --chain 17 --json --verify
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --audit --json

# Verify the sporting checkpoint through the single session, not through the evidence projection.
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --summary --verify
dotnet run --no-build --project Tools/MatchDiagnostics -- /path/match.json --actor 2 --json --verify

# Generate a reproducible fixture archive entirely from terminal.
dotnet fsi Tools/MatchDiagnostics/CaptureFixture.fsx /tmp/opencode/match-current.json

# Six complete games, explicit text chains and capture-on/off equality.
dotnet build FootballEngine.Core/FootballEngine.Core.fsproj -c Release
dotnet fsi Tools/MatchDiagnostics/CausalCorpus.fsx /tmp/opencode/causal-corpus
```

Selectors combine with AND; tick endpoints are inclusive. `--chain` takes an **observation occurrence ID**, not an action/event ID. It traverses only explicit retained ancestors and reports missing context, cycles and future-directed links. It does not turn a stochastic resolution into a physical contact or certify legal correctness. Query selectors cannot be silently combined with an unrelated chain root.

Text output is bounded to 100 rows by default (`--limit` up to 10000); the count before truncation remains visible. JSON exports all matched/chain entries within the retained snapshot, not the text limit. Chain JSON also includes `Root`, typed `MissingCauses`, numeric `Cycles` and `FutureCauses` edges (`Child`/`Source`). Missing roots are errors; missing ancestors are reported without inventing entries. JSON goes to stdout; verification messages go to stderr. Invalid options are rejected before reading an archive. Bare archive invocation performs checkpoint verification; `--summary` alone reads metadata/evidence without replaying.

Archives from incompatible contracts are rejected, with no migration/default fields. The archive codec remains the existing client boundary adapter; its use here is headless. The full corpus consumer references only Core and the existing test fixture builders, publishes outside ticks and retains at most 524288 occurrences per game. A Release count pass found 221134 occurrences in each of the current contact-dense fixtures; the cap leaves more than 2x headroom and a discontinuity or exhausted budget still fails explicitly instead of dropping evidence.

JSON inspection format 2 exports a `Causes` array with typed `Kind`, numeric uint64 `Id` and `Role`. An advantage recall exposes separate `incident` and `trigger` event links, not a packed/comma-separated identity. This replaces the former inspection shape; no legacy exporter is retained.

`--audit` examines the entire retained window using the same source index as causal queries. It exports child/source identities for missing, ambiguous and future-directed links, duplicate occurrence identities, uncaused occurrence IDs and their type counts. An uncaused decision can be a legitimate root; an event without a cause is not thereby explained. This direct-edge audit does not certify legal/sporting coverage or detect all cycles (rooted chain traversal reports cycles). Chain/query selectors cannot narrow a whole-window audit. The corpus writes `seed-*-audit.json` and a JSON file for each captured representative chain.

Fixtures are sports simulations, not proof of career realism or IFAB completeness. An applied tactic demonstrates command application; without later linked evidence it does not prove the tactic caused an outcome. Recalled advantage retains the original foul and actual trigger, but full legal assessment, deferred sanctions and completeness of all contributing causes remain separate obligations.
