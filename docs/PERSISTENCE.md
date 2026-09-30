# Career persistence

## Contract

One SQLite database per slot. `GameState`, `WorldClock`, effective configuration,
and per-career `SaveSettings` are the canonical saved state. `CareerSnapshot` stores
the single confirmed metadata/payload row; `CareerChunk` stores players, clubs,
staff, competitions (including fixtures and match events), countries, and the
resolved mod `DataLock.Snapshot` as `datalock:snapshot`. The
snapshot's SHA-256 covers its compressed payload **and** the sorted keys/hashes
of every chunk. Every chunk's SHA-256 is checked before deserialization. These
checks detect accidental corruption; they are not an authentication scheme.

The other `GameState` fields, including market, negotiations, histories,
experiences, inbox and cached profiles, remain in the snapshot payload. The
`DataLock` identity and package metadata stay there, while its immutable resolved
content is a separate chunk. The complete clock includes scheduled and consumed work,
pending events, phase history and world revision. No field is reconstructed with
the legacy mapper defaults. `SaveEnvelope` exposes only state backed by the
snapshot; it does not claim that a world RNG or pending commands exist. The
canonical continuation state is `LoadedCareer.State` plus `LoadedCareer.WorldClock`.

SQLite updates changed chunks, deletes missing chunks, and replaces the snapshot
inside one transaction. A `SaveSequence` advances even when `WorldClock.Revision`
is unchanged (e.g. tactics or settings). Reference-identity caching avoids
re-encoding immutable entity records shared between successive `GameState`s;
all hashes are still validated against disk before overwriting a save. The
backup is made with SQLite's backup API rather than copying an open `.db3` file.
A failed transaction leaves the previous confirmed snapshot intact. A corrupted
primary can be loaded from its previous full backup; replacing a slot also
replaces its backup, so an old career cannot reappear as a new career. No
old save formats are migrated: schema mismatch is incompatible. Startup does
not modify existing files. Explicitly replacing an incompatible or unreadable
slot archives its previous database as `*.db3.legacy-<id>` before creating the
new career; failed replacements attempt to restore the original path and never
delete the archived file.

After recovery, the damaged primary is kept as `*.db3.corrupt-<id>` and a
consistent SQLite copy of the backup becomes the active primary. Subsequent
saves work normally and the client notifies the player about the recovery.
Save commands receive an in-process per-slot sequence when they are created:
an older command that runs after a newer confirmed command cannot overwrite it,
even if both represent the same world-clock revision or a slot replacement.
If an edit to a newly created career executes before its initial replacement
save, the edit carries the pending replacement intent; the older initial save
then becomes a no-op. A failed edit leaves the original replacement eligible.
Ordinary saves refuse to create a new primary when only an older `.bak` remains;
loading first restores that backup, while an explicit career replacement may
create a fresh primary and matching backup.

## Verification

```bash
dotnet run --project FootballEngine.Client.Tests -- --filter FootballEngine.Client.Persistence
dotnet run --project Tools/PersistenceBench/PersistenceBench.fsproj -c Release -- 10000
dotnet run --project Tools/PersistenceBench/PersistenceBench.fsproj -c Release -- 50000
```

The benchmark builds a real four-club world, then clones players to stress
serialization. It measures initial, unchanged and single-player-change saves,
full load, slot listing, and final database size. It leaves its isolated files
under a printed temporary path so results can be inspected. Treat its numbers
as a local baseline, not as evidence of superiority over any other game. On the
development machine with 10,000 players, a debug run measured ~827 ms first
save, ~98 ms unchanged save, ~102 ms one-player change, ~783 ms load, and a
10.1 MiB database. With 50,000 players: ~2.0 s, ~421 ms, ~455 ms, ~1.5 s,
50.1 MiB. After splitting out the resolved snapshot chunk, a Release run with
10,000 players measured ~814 ms initial, ~93 ms unchanged, ~94 ms one-player
change and ~793 ms load (10.1 MiB). This synthetic world does not contain a
large mod snapshot, so it cannot quantify that optimization. Measure Release
builds across representative worlds and multi-season
careers before setting budgets or changing format/compression.

Integration tests also save a generated world after multiple simulated days,
load it, and compare both the next world state and clock against uninterrupted
simulation. The scheduler's week/month counters are one-based; zero values
would make early-career saves invalid at the load boundary.

## Evolution rules

- Keep persistence codecs and state reconstruction behind `Db`; update the
  round-trip and save-load-continuation tests whenever `GameState` changes.
- `SaveEnvelope` has no invented world RNG, command queue, IDs or phase: all
  active saved state comes from `GameState` and `WorldClock`. A future world
  feature must add real persisted fields before exposing them in this contract.
- Bump `CurrentSchema` when the stored shape or codec changes. Before the first
  public release, replace the current hard incompatibility with explicit,
  version-by-version migration tests if old public saves must remain loadable.
- Incremental writes use stable domain IDs and immutable records. Never mutate
  cached entity instances in place: doing so invalidates the identity cache.
- SQLite backup copies the whole database on each commit. For very large worlds
  this is a measurable cost; keep it until an alternate recovery scheme passes
  corruption, rollback, and replacement tests.
- Listing slots intentionally reads only SQL header columns. A slot can appear
  compatible by version but still fail integrity validation when opened.
