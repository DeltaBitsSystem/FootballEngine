namespace FootballEngine.Database

open SQLite

// Save File Migration Strategy:
// - Each schema change increments SCHEMA_VERSION in Repository.Db
// - migrateIfNeeded handles forward migrations only
// - v0: Original format (no SaveMetaEntity table)
// - v1: Added SaveMetaEntity with schema versioning
// - Future: Add migration steps for each version bump
// - Rule: Never delete data, only add or transform

type MigrationResult =
    | Migrated of newVersion: int
    | AlreadyCurrent
    | MigrationFailed of reason: string

module Migrations =

    let migrateIfNeeded (conn: SQLiteConnection) (currentVersion: int) (targetVersion: int) =
        if currentVersion >= targetVersion then
            AlreadyCurrent
        elif currentVersion < 0 then
            MigrationFailed $"Invalid current version {currentVersion}"
        else
            match currentVersion, targetVersion with
            | 0, 1 ->
                // v0 -> v1: SaveMetaEntity table is created by initTables via CreateTableIfNotExists.
                // No data transformation needed — the table is new and empty.
                // Insert a v1 SaveMetaEntity row so future loads recognize the schema.
                conn.Execute(
                    "INSERT OR REPLACE INTO SaveMetaEntity (Id, SchemaVersion, GameSaveId, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?, ?)",
                    1,
                    1,
                    1,
                    System.DateTime.UtcNow.ToString("o"),
                    System.DateTime.UtcNow.ToString("o")
                )
                |> ignore

                Migrated 1
            | _ -> MigrationFailed $"Cannot migrate from v{currentVersion} to v{targetVersion}"
