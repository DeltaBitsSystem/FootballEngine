module FootballEngine.Client.Tests.PersistenceTests

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Expecto
open SQLite
open FootballEngine
open FootballEngine.Database
open FootballEngine.Domain
open FootballEngine.Settings
open FootballEngine.World
open FootballEngine.Core.Match
open FootballEngine.Data
open FootballEngine.Generation

let private tempRoot () =
    let path =
        Path.Combine(Path.GetTempPath(), "football-engine-persistence-tests", Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory path |> ignore
    path

let private sha256 (value: string) =
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes value)).ToLowerInvariant()

let private dataLock packageInfo =
    let packages =
        packageInfo
        |> Option.map (fun (id, version) ->
            [ { ModId = id
                Version = version
                SchemaVersion = 1
                Dependencies = []
                Precedence = 0
                Before = []
                After = []
                PackageSha256 = sha256 (id + "@" + version) } ])
        |> Option.defaultValue []

    let emptySports = "sports\n0\n"
    let emptyTranslations = "translations\n0\n"
    let emptyAssets = "assets\n0\n"

    let presentation =
        String.concat "\n" [ emptySports; emptyTranslations; emptyAssets ]

    let lockText (value: string) = string value.Length + ":" + value

    let dependencyLock =
        packages
        |> List.mapi (fun index package ->
            let fields =
                [ "index=" + string index
                  "modId=" + lockText package.ModId
                  "version=" + lockText package.Version
                  "schemaVersion=1"
                  "precedence=0"
                  "packageSha256=" + lockText package.PackageSha256
                  "dependenciesCount=0"
                  "afterCount=0"
                  "beforeCount=0" ]

            String.concat "\n" fields)
        |> String.concat "\n"

    { FormatVersion = 1
      EngineVersion = "2026.1.0"
      ContentApi = 1
      Packages = packages
      DependencyLockHash = sha256 dependencyLock
      ResolvedSportsHash = sha256 emptySports
      ResolvedPresentationHash = sha256 presentation
      RuntimeIdTable = []
      Snapshot =
        { SportsContent = emptySports
          Translations = emptyTranslations
          Assets = emptyAssets }
      RequiredAssets = [] }

let private state date dataLock =
    { CurrentDate = date
      Season = 1
      TrainingWeeksApplied = 0
      Clubs = Map.empty
      Players = Map.empty
      Staff = Map.empty
      Competitions = Map.empty
      Countries = Map.empty
      DataLock = dataLock
      UserClubId = 0
      UserStaffId = 0
      PrimaryCountry = ""
      Inbox = []
      NextInboxId = 1
      PendingNegotiations = Map.empty
      NextNegotiationId = 1
      ProfileCache = Map.empty
      PlayerExperiences = Map.empty
      TeamEmphasis = EmRecovery
      SeasonHistory = []
      CareerHistory = []
      ExecutionSeed = 1UL
      MatchSeeds = Map.empty
      News = []
      NextNewsId = 1
      PulseProcessed = Set.empty
      PulseQueues = PulseQueues.empty
      JobOffers = []
      NextJobOfferId = 1
      Market = MarketState.empty }

let private saveSettings = SaveSettings.defaults

let private clock revision day =
    { WorldClockOps.init 1 with
        Revision = revision
        Current =
            { Day = day
              Week = 1
              Month = 1
              Season = 1 } }

let private save root slot gs c =
    Db.configureStorageDirectoryForTests root
    Db.initTablesForSlot slot |> Async.AwaitTask |> Async.RunSynchronously

    Db.saveGameInSlotAsync slot gs c saveSettings
    |> Async.AwaitTask
    |> Async.RunSynchronously

let private load root slot =
    Db.configureStorageDirectoryForTests root
    Db.loadEnvelope slot |> Async.AwaitTask |> Async.RunSynchronously

type private NonPumpingSynchronizationContext() =
    inherit SynchronizationContext()

    override _.Post(_, _) =
        failtest "a save continuation tried to resume on the closing UI thread"

let isolationTests =
    testCase "slots keep envelopes and entities isolated"
    <| fun _ ->
        let root = tempRoot ()
        let first = state (DateTime(2027, 1, 1)) (dataLock None)
        let second = state (DateTime(2027, 2, 1)) (dataLock (Some("mod-b", "2")))
        save root "slot-a" first (clock 1UL 1)
        save root "slot-b" second (clock 1UL 1)

        match load root "slot-a", load root "slot-b" with
        | Ok a, Ok b ->
            Expect.equal a.Envelope.CareerId "slot-a" "slot A identity"
            Expect.equal b.Envelope.CareerId "slot-b" "slot B identity"
            Expect.equal a.State.CurrentDate first.CurrentDate "slot A state"
            Expect.equal b.State.DataLock second.DataLock "slot B data lock"
            Expect.equal b.Envelope.EngineVersion second.DataLock.EngineVersion "engine version comes from saved lock"
            Expect.equal a.ResolvedData.DataLock first.DataLock "slot A resolved snapshot"
            Expect.equal b.ResolvedData.DataLock second.DataLock "slot B resolved snapshot"
        | left, right -> failtestf "slot load failed: %A / %A" left right

let shutdownWriteTests =
    testCase "shutdown waits for a save without relying on the UI synchronization context"
    <| fun _ ->
        let root = tempRoot ()
        Db.configureStorageDirectoryForTests root
        Db.initTablesForSlot "closing" |> Async.AwaitTask |> Async.RunSynchronously
        let game = state (DateTime(2027, 1, 1)) (dataLock None)
        let original = SynchronizationContext.Current
        SynchronizationContext.SetSynchronizationContext(NonPumpingSynchronizationContext())

        try
            Db.configureStorageDirectoryForTests root
            let writing = Db.saveGameInSlotAsync "closing" game (clock 1UL 1) saveSettings

            Db.waitForSlotWrites "closing"
            |> fun pending -> pending.WaitAsync(TimeSpan.FromSeconds 15.0).GetAwaiter().GetResult()

            writing.GetAwaiter().GetResult()
        finally
            SynchronizationContext.SetSynchronizationContext(original)

        match load root "closing" with
        | Ok loaded -> Expect.equal loaded.State game "the in-flight write finished before shutdown"
        | Error error -> failtestf "shutdown lost the new save: %A" error

let capturedSlotPathTests =
    testCase "a delayed save uses the slot directory from command creation, not its execution context"
    <| fun _ ->
        let originalRoot = tempRoot ()
        let otherRoot = tempRoot ()
        let game = state (DateTime(2027, 1, 1)) (dataLock None)
        Db.configureStorageDirectoryForTests originalRoot
        let save = Db.prepareSaveInSlot "captured" game (clock 1UL 1) saveSettings false
        Db.configureStorageDirectoryForTests otherRoot
        save () |> Async.AwaitTask |> Async.RunSynchronously

        Expect.isTrue
            (File.Exists(Path.Combine(originalRoot, "career-captured.db3")))
            "the confirmed snapshot went to its original directory"

        Expect.isFalse
            (File.Exists(Path.Combine(otherRoot, "career-captured.db3")))
            "the worker did not use an unrelated ambient directory"

        match load originalRoot "captured" with
        | Ok loaded -> Expect.equal loaded.State game "the captured-path save is loadable"
        | Error error -> failtestf "captured path could not be loaded: %A" error

let foreignSlotWriteTests =
    testCase "a copied save from another slot cannot be overwritten under a new filename"
    <| fun _ ->
        let root = tempRoot ()
        let game = state (DateTime(2027, 1, 1)) (dataLock None)
        save root "origin" game (clock 1UL 1)
        let copied = Path.Combine(root, "career-copied.db3")
        File.Copy(Path.Combine(root, "career-origin.db3"), copied)
        let original = File.ReadAllBytes copied

        let rejects replace =
            try
                let action =
                    if replace then
                        Db.replaceGameInSlotAsync "copied" game (clock 1UL 1) saveSettings
                    else
                        Db.saveGameInSlotAsync "copied" game (clock 1UL 1) saveSettings

                action |> Async.AwaitTask |> Async.RunSynchronously
                false
            with _ ->
                true

        Expect.isTrue (rejects false) "ordinary saves reject another slot's identity"
        Expect.isTrue (rejects true) "even explicit replacement does not destroy a foreign save"
        Expect.equal (File.ReadAllBytes copied) original "copied file is unchanged"
        Expect.isFalse (File.Exists(copied + ".bak")) "no backup was created over the foreign file"

let unknownSchemaWriteTests =
    testCase "an unrecognized SQLite file is never initialized or overwritten by an ordinary save"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "foreign-schema"
        let path = Path.Combine(root, "career-foreign-schema.db3")
        Db.configureStorageDirectoryForTests root

        do
            use db = new SQLiteConnection(path, true)
            db.Execute("CREATE TABLE ForeignCareer (Data TEXT)") |> ignore
            db.Execute("INSERT INTO ForeignCareer (Data) VALUES (?)", "important") |> ignore

        Db.initTablesForSlot slot |> Async.AwaitTask |> Async.RunSynchronously

        let game = state (DateTime(2027, 1, 1)) (dataLock None)

        Expect.throws
            (fun () ->
                Db.saveGameInSlotAsync slot game (clock 1UL 1) saveSettings
                |> Async.AwaitTask
                |> Async.RunSynchronously)
            "regular save refuses an unknown format"

        do
            use db = new SQLiteConnection(path, true)
            Expect.equal (db.ExecuteScalar<string>("SELECT Data FROM ForeignCareer")) "important" "foreign data remains"

            Expect.equal
                (db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name='CareerSnapshot'"))
                0
                "foreign file was not modified even by table initialization"

        Db.replaceGameInSlotAsync slot game (clock 0UL 1) saveSettings
        |> Async.AwaitTask
        |> Async.RunSynchronously

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State game "explicit replacement creates a new career"
        | Error error -> failtestf "unrecognized file could not be replaced: %A" error

        let archived =
            Directory.EnumerateFiles(root, "career-foreign-schema.db3.legacy-*")
            |> Seq.exactlyOne

        use previous = new SQLiteConnection(archived, true)

        Expect.equal
            (previous.ExecuteScalar<string>("SELECT Data FROM ForeignCareer"))
            "important"
            "old file is archived"

let incompleteSchemaWriteTests =
    testCase "a damaged database missing its chunk table is not silently repaired by saving"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "incomplete-schema"
        let game = state (DateTime(2027, 1, 1)) (dataLock None)
        save root slot game (clock 1UL 1)
        let path = Path.Combine(root, "career-incomplete-schema.db3")

        do
            use db = new SQLiteConnection(path, true)
            db.Execute("DROP TABLE CareerChunk") |> ignore

        Expect.throws
            (fun () -> save root slot game (clock 2UL 2))
            "a regular save must not re-create a missing aggregate table"

        use db = new SQLiteConnection(path, true)

        Expect.equal
            (db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name='CareerChunk'"))
            0
            "the damaged primary remains untouched for diagnosis"

let backupTests =
    testCase "corrupt primary recovers the previous complete SQLite revision"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "backup"
        save root slot (state (DateTime(2027, 1, 1)) (dataLock None)) (clock 1UL 1)
        save root slot (state (DateTime(2027, 1, 2)) (dataLock None)) (clock 2UL 2)
        let path = Path.Combine(root, "career-backup.db3")
        use db = new SQLiteConnection(path, true)

        db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy; 2uy; 3uy |])
        |> ignore

        match load root slot with
        | Ok recovered ->
            Expect.equal recovered.Envelope.ConfirmedRevision 1UL "backup revision"
            Expect.equal recovered.State.CurrentDate (DateTime(2027, 1, 1)) "backup state"
            Expect.isTrue recovered.RecoveredFromBackup "the UI can report recovery"

            let initial =
                { AppTypes.initialState () with
                    Setup =
                        { AppTypes.initSetupState with
                            LoadState = AppTypes.LoadingSlot(slot, 1UL) } }

            let notified, _ =
                AppState.update (AppMsgs.GameLoaded(1UL, slot, Ok recovered)) initial

            Expect.isTrue
                (notified.Notifications
                 |> List.exists (fun note -> note.Title = "Save recovered"))
                "recovery is visible to the player"

            Expect.equal
                (Directory.EnumerateFiles(root, "career-backup.db3.corrupt-*") |> Seq.length)
                1
                "the damaged primary remains available for inspection"

            let next = state (DateTime(2027, 1, 3)) (dataLock None)
            save root slot next (clock 3UL 3)

            match load root slot with
            | Ok resumed ->
                Expect.equal resumed.State.CurrentDate next.CurrentDate "recovered career can save again"
                Expect.isFalse resumed.RecoveredFromBackup "healthy loads do not repeat the recovery warning"
            | Error error -> failtestf "recovered career could not save again: %A" error
        | Error error -> failtestf "backup was not recovered: %A" error

let unrecoverableFilesTests =
    testCase "two unreadable copies return an error without touching either file"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "double-corrupt"
        let game = state (DateTime(2027, 1, 1)) (dataLock None)
        save root slot game (clock 1UL 1)
        save root slot game (clock 2UL 2)
        let path = Path.Combine(root, "career-double-corrupt.db3")
        let damaged = [| 1uy; 2uy; 3uy; 4uy |]
        File.WriteAllBytes(path, damaged)
        File.WriteAllBytes(path + ".bak", damaged)

        match load root slot with
        | Error(Corrupt _) -> ()
        | other -> failtestf "two damaged files were treated as a saved career: %A" other

        Expect.equal (File.ReadAllBytes path) damaged "the primary is preserved"
        Expect.equal (File.ReadAllBytes(path + ".bak")) damaged "the backup is preserved"

let walRecoveryTests =
    testCase "a damaged primary in WAL mode does not contaminate its restored backup"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "wal-recovery"
        let first = state (DateTime(2027, 1, 1)) (dataLock None)
        save root slot first (clock 1UL 1)

        save
            root
            slot
            { first with
                CurrentDate = first.CurrentDate.AddDays 1.0 }
            (clock 2UL 2)

        let path = Path.Combine(root, "career-wal-recovery.db3")

        do
            use db = new SQLiteConnection(path, true)
            db.ExecuteScalar<string>("PRAGMA journal_mode=WAL") |> ignore
            db.ExecuteScalar<int>("PRAGMA wal_autocheckpoint=0") |> ignore

            db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy |])
            |> ignore

            Expect.isTrue (File.Exists(path + "-wal")) "corrupt revision is stored in a WAL sidecar"

            match load root slot with
            | Ok recovered -> Expect.equal recovered.State first "backup is recovered while WAL exists"
            | Error error -> failtestf "WAL recovery failed: %A" error

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State first "recovered primary stays loadable after the WAL closes"
        | Error error -> failtestf "stale WAL corrupted the recovered primary: %A" error

let commitFailureTests =
    testCase "a failed commit preserves the last revision"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "commit"
        save root slot (state (DateTime(2027, 1, 1)) (dataLock None)) (clock 1UL 1)
        Db.injectCommitFailureForTests true

        let failed =
            try
                save root slot (state (DateTime(2027, 1, 2)) (dataLock None)) (clock 2UL 2)
                false
            with _ ->
                true

        Db.injectCommitFailureForTests false
        Expect.isTrue failed "commit failure must surface"

        match load root slot with
        | Ok loaded -> Expect.equal loaded.Envelope.ConfirmedRevision 1UL "failed commit rolled back"
        | Error error -> failtestf "last revision was lost: %A" error

let replacementBackupFailureTests =
    testCase "a failed post-commit backup cannot revive the career that was replaced"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "post-commit-backup"
        let old = state (DateTime(2028, 1, 1)) (dataLock None)
        let fresh = state (DateTime(2026, 7, 1)) (dataLock None)
        save root slot old (clock 1UL 1)
        save root slot old (clock 2UL 2)
        let path = Path.Combine(root, "career-post-commit-backup.db3")

        let staleOldCareerSave =
            Db.prepareSaveInSlot slot old (clock 3UL 3) saveSettings false

        Db.injectReplacementBackupFailureForTests true

        let failed =
            try
                Db.replaceGameInSlotAsync slot fresh (clock 0UL 1) saveSettings
                |> Async.AwaitTask
                |> Async.RunSynchronously

                false
            with _ ->
                true

        Db.injectReplacementBackupFailureForTests false
        Expect.isTrue failed "backup failure is reported"
        staleOldCareerSave () |> Async.AwaitTask |> Async.RunSynchronously

        match load root slot with
        | Ok loaded ->
            Expect.equal loaded.State fresh "the committed new career remains intact"
            Expect.equal loaded.Envelope.ConfirmedRevision 0UL "an older pending command cannot overwrite it"
        | Error error -> failtestf "the committed career was lost: %A" error

        Expect.isFalse (File.Exists(path + ".bak")) "the old career is not in the active backup"

        Expect.equal
            (Directory.EnumerateFiles(root, "career-post-commit-backup.db3.bak.previous-*")
             |> Seq.length)
            1
            "the displaced backup is preserved for diagnosis"

        do
            use db = new SQLiteConnection(path, true)

            db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy |])
            |> ignore

        match load root slot with
        | Error(Corrupt _) -> ()
        | other -> failtestf "the replaced career was recovered from a stale backup: %A" other

let replacementCommitRollbackTests =
    testCase "a failed replacement transaction restores the prior active backup"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "rollback-backup"
        let old = state (DateTime(2028, 1, 1)) (dataLock None)
        save root slot old (clock 1UL 1)
        save root slot old (clock 2UL 2)
        let path = Path.Combine(root, "career-rollback-backup.db3")
        Db.injectCommitFailureForTests true

        let failed =
            try
                Db.replaceGameInSlotAsync slot (state (DateTime(2026, 7, 1)) (dataLock None)) (clock 0UL 1) saveSettings
                |> Async.AwaitTask
                |> Async.RunSynchronously

                false
            with _ ->
                true

        Db.injectCommitFailureForTests false
        Expect.isTrue failed "transaction failure is reported"
        Expect.isTrue (File.Exists(path + ".bak")) "the previous backup is restored"

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State old "the old career remains active"
        | Error error -> failtestf "failed replacement lost the old career: %A" error

let manifestTests =
    testCase "an edited manifest is rejected by the load boundary"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "manifest"
        save root slot (state (DateTime(2027, 1, 1)) (dataLock (Some("demo", "1")))) (clock 1UL 1)

        let expected =
            [ { Id = "demo"
                Version = "1"
                SchemaVersion = 1
                Dependencies = []
                Precedence = 0
                ContentHash = "edited" } ]

        match
            Db.loadEnvelopeWithManifest slot expected
            |> Async.AwaitTask
            |> Async.RunSynchronously
        with
        | Error(Corrupt(_, message)) -> Expect.stringContains message "manifest" "edited mod is reported"
        | result -> failtestf "edited manifest was accepted: %A" result

let effectiveConfigTests =
    testCase "a checksummed snapshot cannot silently resume with different effective settings"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "balance"
        save root slot (state (DateTime(2027, 1, 1)) (dataLock None)) (clock 1UL 1)
        let path = Path.Combine(root, "career-balance.db3")
        use db = new SQLiteConnection(path, true)
        let entry = db.Find<CareerSnapshot>(1)

        use input = new MemoryStream(entry.Payload)
        use unzip = new GZipStream(input, CompressionMode.Decompress)
        use text = new StreamReader(unzip)
        let json = JsonNode.Parse(text.ReadToEnd())
        json["ConfigHash"] <- JsonValue.Create(1UL)

        use output = new MemoryStream()

        do
            use zip = new GZipStream(output, CompressionLevel.Fastest, true)
            let bytes = Encoding.UTF8.GetBytes(json.ToJsonString())
            zip.Write(bytes, 0, bytes.Length)

        let payload = output.ToArray()

        let digest =
            db.Table<CareerChunk>().ToList()
            |> Seq.sortBy _.Key
            |> Seq.fold
                (fun current chunk -> current + "|" + chunk.Key + ":" + chunk.Hash)
                (Convert.ToHexString(SHA256.HashData payload).ToLowerInvariant())
            |> sha256

        db.Execute("UPDATE CareerSnapshot SET Payload = ?, Hash = ? WHERE Id = 1", payload, digest)
        |> ignore

        use backupDb = new SQLiteConnection(path + ".bak", true)

        backupDb.Execute("UPDATE CareerSnapshot SET Payload = ?, Hash = ? WHERE Id = 1", payload, digest)
        |> ignore

        match load root slot with
        | Error(Corrupt(_, message)) -> Expect.stringContains message "effective configuration" "drift is visible"
        | other -> failtestf "mismatched configuration was accepted: %A" other

let equivalenceTests =
    testCase "direct continuation equals save-load continuation"
    <| fun _ ->
        let root = tempRoot ()
        let initial = state (DateTime(2027, 1, 1)) (dataLock None)
        let initialClock = clock 0UL 1

        let direct =
            WorldRunner.advanceDaysWithBalance BalanceConfig.defaultConfig 1 initialClock initial

        save root "equivalence" initial initialClock

        match load root "equivalence" with
        | Ok loaded ->
            let resumed =
                WorldRunner.advanceDaysWithBalance BalanceConfig.defaultConfig 1 loaded.WorldClock loaded.State

            Expect.equal resumed.GameState.CurrentDate direct.GameState.CurrentDate "date converges"
            Expect.equal resumed.WorldClock direct.WorldClock "clock converges"
            Expect.equal resumed.GameState.DataLock direct.GameState.DataLock "canonical state converges"
        | Error error -> failtestf "roundtrip failed: %A" error

let discriminatedUnionRoundtripTests =
    testCase "save round-trips union values used by finance and world clock"
    <| fun _ ->
        let root = tempRoot ()
        let date = DateTime(2027, 1, 1)

        let finance =
            { Finance.initial 100000m with
                Obligations =
                    [ { Id = "stadium-maintenance"
                        Counterparty = "Stadium Services"
                        Description = "Pitch maintenance"
                        DueDate = date.AddDays 7.0
                        Amount = 250m
                        Currency = EUR
                        Condition = "scheduled"
                        State = ObligationCommitted
                        AccruedAmount = 250m
                        PaidAmount = 0m } ] }

        let club =
            { Id = 1
              Name = "Union Test FC"
              Nationality = "ARG"
              Reputation = 50
              PlayerIds = []
              StaffIds = []
              Finance = finance
              Morale = 60
              BoardObjective = LeagueObjective MidTable
              CoordinationMemory = CoordinationMemory.defaultMemory
              ScoutingMemory = Map.empty
              Kit = None
              BoardConfidence = 60
              FansMood = 60
              RecentResults = []
              ManagerSince = None }

        let game =
            { state date (dataLock None) with
                Clubs = Map.ofList [ club.Id, club ] }

        let savedClock =
            { clock 1UL 1 with
                PhaseHistory =
                    [ { Phase = ResolveFixtures
                        Date = date
                        RevisionBefore = 0UL
                        RevisionAfter = 1UL } ] }

        save root "unions" game savedClock

        match load root "unions" with
        | Ok loaded ->
            Expect.equal loaded.WorldClock.PhaseHistory savedClock.PhaseHistory "phase history round-trips"
            Expect.equal loaded.State.Clubs[club.Id].Finance finance "financial unions round-trip"
        | Error error -> failtestf "union roundtrip failed: %A" error

let newCareerReplacementTests =
    testCase "a new career can replace a slot with a newer stored revision"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "new-career"
        let oldState = state (DateTime(2027, 1, 1)) (dataLock None)
        let newState = state (DateTime(2026, 7, 1)) (dataLock None)
        save root slot oldState (clock 271UL 271)

        Db.replaceGameInSlotAsync slot newState (clock 0UL 1) saveSettings
        |> Async.AwaitTask
        |> Async.RunSynchronously

        use db = new SQLiteConnection(Path.Combine(root, "career-new-career.db3"), true)

        db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy |])
        |> ignore

        match load root slot with
        | Ok loaded ->
            Expect.equal loaded.Envelope.ConfirmedRevision 0UL "new career resets the slot revision"
            Expect.equal loaded.State.CurrentDate newState.CurrentDate "new career replaces the stored state"
        | Error error -> failtestf "new career replacement failed: %A" error

let private handBuiltPlayer =
    { Id = 3
      Name = "Restless Player"
      Birthday = DateTime(2000, 1, 1)
      Nationality = "ARG"
      Position = ST
      PreferredFoot = PreferredFoot.Right
      Height = 180
      Weight = 75
      Physical =
        { Acceleration = 70
          Pace = 70
          Agility = 70
          Balance = 70
          JumpingReach = 70
          Stamina = 70
          Strength = 70 }
      Technical =
        { Finishing = 70
          LongShots = 60
          Dribbling = 60
          BallControl = 60
          Passing = 60
          Crossing = 50
          Tackling = 30
          Marking = 30
          Heading = 60
          FreeKick = 40
          Penalty = 50 }
      Mental =
        { Aggression = 40
          Composure = 70
          Vision = 60
          Positioning = 60
          Bravery = 50
          WorkRate = 60
          Concentration = 60
          Leadership = 30 }
      Goalkeeping =
        { Reflexes = 1
          Handling = 1
          Kicking = 1
          OneOnOne = 1
          AerialReach = 1 }
      Condition = 100
      Fatigue = 0
      MatchFitness = 100
      RecentLoad = 0
      Morale = 70
      Status = Available
      CurrentSkill = 70
      PotentialSkill = 75
      Reputation = 1000
      Affiliation = Contracted(7, { Salary = 1000m; ExpiryYear = 2028 })
      TrainingPlan = Perform
      ExperienceModifiers = ExperienceModifiers.defaultModifiers
      Unrest = None
      BenchWeeks = 0
      UnrestWeeks = 0
      TransferListed = false
      SeasonStartSkill = 0 }


let fullStateTests =
    testCase "saving preserves pending negotiations, experience, history and same-clock changes"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "full-state"
        let date = DateTime(2027, 1, 1)

        let negotiation =
            { Id = 9
              BuyerClubId = 1
              SellerClubId = 2
              PlayerId = 3
              Stage = CounterReceived(100m, 120m)
              Deadline = date.AddDays 3.0 }

        let experience =
            { PlayerExperience.empty with
                TotalMatchesPlayed = 37
                RivalRecord = Map.ofList [ 2, {| Wins = 2; Losses = 1; Draws = 3 |} ] }

        let pulseClub =
            { Id = 7
              Name = "Pulse FC"
              Nationality = "ARG"
              Reputation = 50
              PlayerIds = []
              StaffIds = []
              Finance = Finance.initial 100000m
              Morale = 60
              BoardObjective = LeagueObjective MidTable
              CoordinationMemory = CoordinationMemory.defaultMemory
              ScoutingMemory = Map.empty
              Kit = None
              BoardConfidence = 37
              FansMood = 81
              RecentResults = [ MatchOutcomeTag.Won; MatchOutcomeTag.Drawn; MatchOutcomeTag.Lost ]
              ManagerSince = Some date }

        let news =
            [ { Id = 2
                Date = date
                Kind = NewsKind.TransferDone(3, Some 7, 8, 12500000.5m)
                Importance = NewsImportance.Major
                Scope = NewsScope.UserLeague
                Clubs = [ 7; 8 ]
                Players = [ 3 ] }
              { Id = 1
                Date = date
                Kind = NewsKind.TransferRequest(3, 7, UnrestReason.BiggerClubInterest)
                Importance = NewsImportance.Headline
                Scope = NewsScope.UserClub
                Clubs = [ 7 ]
                Players = [ 3 ] } ]

        // Built by hand: the generator draws from the thread-local RNG, and extra draws here would reshuffle the
        // seeds other tests in this suite rely on.
        let restless =
            { handBuiltPlayer with
                Unrest = Some UnrestReason.LackOfMinutes
                BenchWeeks = 5
                UnrestWeeks = 2
                TransferListed = true
                SeasonStartSkill = 61 }

        let queues =
            { Appearances =
                [ { Player = 3
                    Matches = 100
                    GoalsAfter = 49
                    GoalsInMatch = 2 } ]
              Transfers =
                [ { Player = 3
                    From = Some 7
                    To = 8
                    Fee = 7250000.5m } ]
              NegotiationsSeen = Set.ofList [ 9; 12 ] }

        let first =
            { state date (dataLock None) with
                Clubs = Map.ofList [ pulseClub.Id, pulseClub ]
                Players = Map.ofList [ restless.Id, restless ]
                PulseQueues = queues
                News = news
                NextNewsId = 3
                PulseProcessed = Set.ofList [ 4; 9 ]
                PendingNegotiations = Map.ofList [ negotiation.Id, negotiation ]
                NextNegotiationId = 10
                PlayerExperiences = Map.ofList [ 3, experience ]
                CareerHistory = [ ManagerHired(date, 1) ] }

        save root slot first (clock 1UL 1)

        let second =
            { first with
                TeamEmphasis = EmHighPress }

        save root slot second (clock 1UL 1)

        match load root slot with
        | Ok result ->
            Expect.equal result.State second "the complete canonical state survives the round-trip"
            Expect.equal result.State.Players[3] restless "unrest, bench/unrest weeks, listing and season baseline survive"
            Expect.equal result.State.PulseQueues queues "unconsumed pulse facts survive"
            Expect.equal result.WorldClock (clock 1UL 1) "the world revision is independent of saves"
        | Error error -> failtestf "state round-trip failed: %A" error

let activeMarketTests =
    testCase "active offers, clauses, loans, scouting and registrations round-trip without defaults"
    <| fun _ ->
        let root = tempRoot ()
        let date = DateTime(2027, 1, 1)
        let operationId = TransferOperationId 3
        let contractId = ContractId 4
        let assignmentId = ScoutingAssignmentId 5

        let operation =
            { Id = operationId
              Player = 7
              Buyer = 1
              Seller = Some 2
              Fee = 12500000m
              Salary = 130000m
              ContractYears = 4
              Role = Starter
              MinutesPromised = 2500
              Status = TransferOperationStatus.Rejected "board veto"
              Reason = Some(TransferReason.RegistrationInvalid "quota")
              Created = date
              Updated = date.AddDays 1.0 }

        let contract =
            { Id = contractId
              Employer = 1
              Player = 7
              Starts = date
              Ends = date.AddYears 4
              Salary = 130000m
              Bonuses = 20000m
              Role = Rotation
              MinutesPromised = 1600
              Options = [ ClubOption(date.AddYears 3, 500000m); PlayerOption(date.AddYears 2, 250000m) ]
              Clauses = [ ReleaseClause 30000000m; SellOnPercentage 15m ] }

        let loan =
            { Player = 7
              Origin = 2
              Destination = 1
              Starts = date
              Ends = date.AddYears 1
              WageShareOrigin = 30m
              WageShareDestination = 70m
              BuyOption = Some 8000000m
              RecallDate = Some(date.AddMonths 6) }

        let assignment =
            ScoutingAssignment.assign assignmentId (PlayerTarget 7) 8 500m date (date.AddDays 14.0)

        let knowledge =
            { Player = 7
              CurrentSkillRange = 90, 115
              PotentialRange = 140, 175
              WageRange = 80000m, 130000m
              Confidence = 0.75
              ObservedOn = date
              Sources = [ 8 ]
              ReportKey = "report:5" }

        let market =
            { MarketState.empty with
                Operations = Map.ofList [ operationId, operation ]
                Contracts = Map.ofList [ contractId, contract ]
                Loans = Map.ofList [ 7, loan ]
                Assignments = Map.ofList [ assignmentId, assignment ]
                Knowledge = Map.ofList [ 7, knowledge ]
                Shortlists = Map.ofList [ 1, [ 7; 12 ] ]
                Registrations = Map.ofList [ 7, RegistrationBlocked "work permit" ]
                Events =
                    [ OfferCountered(operationId, "counter")
                      ContractRenewed(contractId, "renewed") ]
                NextOperationId = 6
                NextContractId = 7 }

        let game =
            { state date (dataLock None) with
                Market = market }

        save root "active-market" game (clock 2UL 2)

        match load root "active-market" with
        | Ok loaded -> Expect.equal loaded.State game "the complete market graph survives"
        | Error error -> failtestf "active market could not be loaded: %A" error

let playedMatchHistoryTests =
    testCase "played fixture retains scored goals and typed pass events"
    <| fun _ ->
        let root = tempRoot ()
        let date = DateTime(2027, 1, 1)

        let events: FootballEngine.Core.Match.MatchEvent list =
            [ { Tick = 100<subtick>
                Data =
                  FootballEngine.Core.Match.EventData.Pass(
                      FootballEngine.Core.Match.PlayerId 7,
                      Some(FootballEngine.Core.Match.PlayerId 8),
                      15.0<meter>,
                      0.92,
                      true,
                      1.0<meter>,
                      2.0<meter>,
                      15.0<meter>,
                      4.0<meter>
                  ) }
              { Tick = 200<subtick>
                Data =
                  FootballEngine.Core.Match.EventData.Goal(
                      FootballEngine.Core.Match.ClubSide.Home,
                      Some(FootballEngine.Core.Match.PlayerId 8),
                      Some(FootballEngine.Core.Match.PlayerId 7),
                      0.47
                  ) } ]

        let fixture: MatchFixture =
            { Id = 10
              CompetitionId = 1
              Round = None
              HomeClubId = 1
              AwayClubId = 2
              ScheduledDate = date
              Played = true
              HomeScore = Some 1
              AwayScore = Some 0
              Events = events }

        let competition: Competition =
            { Id = 1
              Name = "Historic League"
              BroadcastIdentity = BroadcastIdentity.generated "competition:1"
              Type =
                NationalLeague(
                    LeagueLevel 0,
                    { PointsForWin = 3
                      PointsForDraw = 1
                      Tiebreakers = [ GoalDifference ]
                      Promotion = []
                      Relegation = [] }
                )
              Country = None
              Season = 1
              ClubIds = [ 1; 2 ]
              Fixtures = Map.ofList [ fixture.Id, fixture ]
              Standings = Map.empty
              KnockoutTies = Map.empty }

        let game =
            { state date (dataLock None) with
                Competitions = Map.ofList [ competition.Id, competition ] }

        save root "played" game (clock 1UL 1)

        match load root "played" with
        | Ok loaded -> Expect.equal loaded.State.Competitions game.Competitions "match history is preserved"
        | Error error -> failtestf "played match could not be loaded: %A" error

let outOfOrderWriteTests =
    testCase "older commands cannot overwrite a later same-revision edit or new career"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "ordered"
        let first = state (DateTime(2027, 1, 1)) (dataLock None)

        let later =
            { first with
                TeamEmphasis = EmHighPress }

        Db.configureStorageDirectoryForTests root
        Db.initTablesForSlot slot |> Async.AwaitTask |> Async.RunSynchronously

        let stale = Db.prepareSaveInSlot slot first (clock 1UL 1) saveSettings false
        let latest = Db.prepareSaveInSlot slot later (clock 1UL 1) saveSettings false
        latest () |> Async.AwaitTask |> Async.RunSynchronously
        stale () |> Async.AwaitTask |> Async.RunSynchronously

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State later "the later command wins even at the same world revision"
        | Error error -> failtestf "latest command was not saved: %A" error

        let oldCareer = Db.prepareSaveInSlot slot first (clock 2UL 2) saveSettings false
        let newCareer = state (DateTime(2026, 7, 1)) (dataLock None)
        let replace = Db.prepareSaveInSlot slot newCareer (clock 0UL 1) saveSettings true
        replace () |> Async.AwaitTask |> Async.RunSynchronously
        oldCareer () |> Async.AwaitTask |> Async.RunSynchronously

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State newCareer "old career writes cannot revive a replaced slot"
        | Error error -> failtestf "replacement was overwritten: %A" error

let replacementWriteOrderTests =
    testCase "an immediate new-career edit can commit before its initial slot replacement"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "replacement-order"
        let previous = state (DateTime(2028, 1, 1)) (dataLock None)
        save root slot previous (clock 365UL 365)

        let fresh = state (DateTime(2026, 7, 1)) (dataLock None)

        let edited =
            { fresh with
                TeamEmphasis = EmHighPress }

        let replace = Db.prepareSaveInSlot slot fresh (clock 0UL 1) saveSettings true
        let edit = Db.prepareSaveInSlot slot edited (clock 0UL 1) saveSettings false

        // Elmish commands are asynchronous; creation order need not equal execution order.
        edit () |> Async.AwaitTask |> Async.RunSynchronously
        replace () |> Async.AwaitTask |> Async.RunSynchronously

        match load root slot with
        | Ok loaded ->
            Expect.equal loaded.State edited "the edit survives despite the old slot's higher revision"
            Expect.equal loaded.Envelope.ConfirmedRevision 0UL "the new career revision is reset"
        | Error error -> failtestf "new-career edit was lost: %A" error

        use db =
            new SQLiteConnection(Path.Combine(root, "career-replacement-order.db3"), true)

        db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy |])
        |> ignore

        match load root slot with
        | Ok recovered -> Expect.equal recovered.State edited "backup also belongs to the edited new career"
        | Error error -> failtestf "the old career reappeared after recovery: %A" error

let failedReplacementEditTests =
    testCase "a failed early new-career edit does not suppress the original replacement"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "replacement-failure"
        save root slot (state (DateTime(2028, 1, 1)) (dataLock None)) (clock 365UL 365)

        let fresh = state (DateTime(2026, 7, 1)) (dataLock None)
        let replace = Db.prepareSaveInSlot slot fresh (clock 0UL 1) saveSettings true

        let edit =
            Db.prepareSaveInSlot
                slot
                { fresh with
                    TeamEmphasis = EmHighPress }
                (clock 0UL 1)
                saveSettings
                false

        Db.injectCommitFailureForTests true

        let failed =
            try
                edit () |> Async.AwaitTask |> Async.RunSynchronously
                false
            with _ ->
                true

        Db.injectCommitFailureForTests false
        Expect.isTrue failed "the injected edit failure is reported"
        replace () |> Async.AwaitTask |> Async.RunSynchronously

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State fresh "the initial replacement remains eligible after a failed edit"
        | Error error -> failtestf "failed edit prevented the new career: %A" error

let longCareerTests =
    testCase "decades of season results and inbox messages survive repeated saves"
    <| fun _ ->
        let root = tempRoot ()
        let date = DateTime(2070, 7, 1)

        let seasons =
            [ 2027..2069 ]
            |> List.map (fun year ->
                { Season = year
                  CompetitionIds = [ 1; 2 ]
                  Winners = [ 1, 3 ]
                  Promoted = [ 4 ]
                  Relegated = [ 5 ]
                  CompletedOn = DateTime(year, 6, 30) })

        let inbox =
            [ 1..4000 ]
            |> List.map (fun id ->
                { Id = id
                  Date = DateTime(2027 + id / 100, 1, 1)
                  From = "Board"
                  Subject = $"Season report {id}"
                  Body = "A long-lived career must retain its messages."
                  Category = InboxMessageCategory.BoardUpdate
                  IsRead = id % 2 = 0
                  RequiresAction = id % 7 = 0
                  ActionTaken = if id % 7 = 0 then Some true else None })

        let career =
            { state date (dataLock None) with
                Season = 2070
                SeasonHistory = seasons
                CareerHistory = seasons |> List.map (fun season -> ManagerHired(season.CompletedOn, 3))
                Inbox = inbox
                NextInboxId = inbox.Length + 1 }

        let worldClock = WorldClockOps.init career.Season
        save root "long-career" career worldClock
        save root "long-career" career worldClock

        match load root "long-career" with
        | Ok loaded ->
            Expect.equal loaded.State career "the complete long-lived world is retained"
            Expect.equal loaded.WorldClock worldClock "the clock is unchanged"
        | Error error -> failtestf "long career could not be loaded: %A" error

let metadataTests =
    testCase "listing slots reads metadata without deserializing the world"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "metadata"
        save root slot (state (DateTime(2027, 1, 1)) (dataLock None)) (clock 1UL 1)
        use db = new SQLiteConnection(Path.Combine(root, "career-metadata.db3"), true)

        db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy |])
        |> ignore

        let listed = Db.listSaveSlotsAsync () |> Async.AwaitTask |> Async.RunSynchronously

        match listed with
        | Ok [ info ] -> Expect.equal info.SlotId slot "slot metadata does not require payload decode"
        | other -> failtestf "slot listing failed: %A" other

        match load root slot with
        | Ok recovered ->
            Expect.isTrue recovered.RecoveredFromBackup "the listing did not need to decode the corrupt primary"
            Expect.equal recovered.State.CurrentDate (DateTime(2027, 1, 1)) "the first backup is complete"
        | other -> failtestf "first backup did not recover a corrupt payload: %A" other

let chunkTests =
    testCase "changed chunks update, deleted chunks disappear and corrupt chunks recover"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "chunks"
        let date = DateTime(2027, 1, 1)

        let firstClub =
            { Id = 1
              Name = "First FC"
              Nationality = "ARG"
              Reputation = 50
              PlayerIds = []
              StaffIds = []
              Finance = Finance.initial 100m
              Morale = 60
              BoardObjective = LeagueObjective MidTable
              CoordinationMemory = CoordinationMemory.defaultMemory
              ScoutingMemory = Map.empty
              Kit = None
              BoardConfidence = 60
              FansMood = 60
              RecentResults = []
              ManagerSince = None }

        let first =
            { state date (dataLock None) with
                Clubs = Map.ofList [ 1, firstClub ] }

        save root slot first (clock 1UL 1)

        let secondClub = { firstClub with Name = "Renamed FC" }

        let second =
            { first with
                Clubs = Map.ofList [ 1, secondClub ] }

        save root slot second (clock 1UL 1)

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State.Clubs second.Clubs "changed aggregate replaces prior row"
        | Error error -> failtestf "updated aggregate could not load: %A" error

        use db = new SQLiteConnection(Path.Combine(root, "career-chunks.db3"), true)

        db.Execute("UPDATE CareerChunk SET Payload = ? WHERE Key = ?", [| 1uy |], "club:1")
        |> ignore

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State.Clubs first.Clubs "corrupt chunk recovers last complete snapshot"
        | Error error -> failtestf "corrupt chunk was not recovered: %A" error

        // A new slot exercises deletion without carrying corrupt primary data forward.
        save root "deleted" first (clock 1UL 1)
        let removed = { first with Clubs = Map.empty }
        save root "deleted" removed (clock 1UL 1)

        match load root "deleted" with
        | Ok loaded -> Expect.equal loaded.State.Clubs Map.empty "removed aggregates do not reappear"
        | Error error -> failtestf "deleted aggregate could not load: %A" error

let dataSnapshotChunkTests =
    testCase "resolved mod snapshot is stored once, verified, and restored with the career"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "resolved-chunk"
        let first = state (DateTime(2027, 1, 1)) (dataLock (Some("mod-a", "1")))
        save root slot first (clock 1UL 1)
        let path = Path.Combine(root, "career-resolved-chunk.db3")

        use db = new SQLiteConnection(path, true)

        let rowId () =
            db.ExecuteScalar<int64>("SELECT rowid FROM CareerChunk WHERE Key = 'datalock:snapshot'")

        let originalRowId = rowId ()
        Expect.isGreaterThan originalRowId 0L "the data snapshot has its own chunk"

        let payload = db.Find<CareerSnapshot>(1).Payload
        use input = new MemoryStream(payload)
        use zip = new GZipStream(input, CompressionMode.Decompress)
        use text = new StreamReader(zip)
        let json = text.ReadToEnd()
        Expect.isFalse (json.Contains("sports\\n0\\n", StringComparison.Ordinal)) "main payload omits snapshot content"

        let second =
            { first with
                CurrentDate = first.CurrentDate.AddDays 1.0 }

        save root slot second (clock 2UL 2)
        Expect.equal (rowId ()) originalRowId "unchanged snapshot is not rewritten"

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State second "load restores the locked resolved data"
        | Error error -> failtestf "resolved snapshot did not round-trip: %A" error

        db.Execute("UPDATE CareerChunk SET Payload = ? WHERE Key = 'datalock:snapshot'", [| 1uy |])
        |> ignore

        match load root slot with
        | Ok recovered ->
            Expect.isTrue recovered.RecoveredFromBackup "corrupted resolved data requires recovery"
            Expect.equal recovered.State first "the previous confirmed save is recovered intact"
        | Error error -> failtestf "corrupted resolved snapshot did not recover: %A" error

let missingPrimaryTests =
    testCase "a missing primary SQLite file can still be listed and loaded from its backup"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "recover-missing"
        let first = state (DateTime(2027, 1, 1)) (dataLock None)
        save root slot first (clock 1UL 1)
        save root slot (state (DateTime(2027, 1, 2)) (dataLock None)) (clock 2UL 2)
        let path = Path.Combine(root, "career-recover-missing.db3")
        File.Move(path, path + ".lost")

        match Db.listSaveSlotsAsync () |> Async.AwaitTask |> Async.RunSynchronously with
        | Ok [ info ] -> Expect.equal info.SlotId slot "orphan backup remains visible"
        | other -> failtestf "backup was not listed: %A" other

        match load root slot with
        | Ok loaded ->
            Expect.equal loaded.State.CurrentDate first.CurrentDate "backup is loadable"
            Expect.isTrue (File.Exists path) "loading the backup restores an active primary"
        | Error error -> failtestf "backup was not loaded: %A" error

let orphanBackupWriteTests =
    testCase "ordinary saves cannot create a fresh primary over an orphaned older backup"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "orphan-write"
        let first = state (DateTime(2027, 1, 1)) (dataLock None)
        let later = state (DateTime(2027, 1, 2)) (dataLock None)
        save root slot first (clock 1UL 1)
        save root slot later (clock 2UL 2)
        let path = Path.Combine(root, "career-orphan-write.db3")
        File.Move(path, path + ".lost")

        let blocked =
            try
                save root slot later (clock 3UL 3)
                false
            with _ ->
                true

        Expect.isTrue blocked "a missing primary requires recovery before a regular save"
        Expect.isFalse (File.Exists path) "failed save does not create an unprotected primary"

        match load root slot with
        | Ok recovered -> Expect.equal recovered.State first "the backup can still be recovered"
        | Error error -> failtestf "orphan backup was lost: %A" error

        save root slot later (clock 3UL 3)

        match load root slot with
        | Ok resumed -> Expect.equal resumed.State later "saving works after recovery"
        | Error error -> failtestf "the restored career could not be saved: %A" error

let invalidSlotTests =
    testCase "slot names cannot escape the career directory or alias another slot"
    <| fun _ ->
        let root = tempRoot ()
        Db.configureStorageDirectoryForTests root

        Expect.throws
            (fun () -> Db.initTablesForSlot "../other" |> Async.AwaitTask |> Async.RunSynchronously)
            "invalid slot names are rejected"

        Expect.throws
            (fun () -> Db.initTablesForSlot "Career-1" |> Async.AwaitTask |> Async.RunSynchronously)
            "case-variant slot IDs cannot alias the same file on a case-insensitive filesystem"

let incompatibleSchemaTests =
    testCase "old formats are identified without changing their SQLite files"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "legacy"
        let path = Path.Combine(root, "career-legacy.db3")
        Db.configureStorageDirectoryForTests root

        use db = new SQLiteConnection(path, true)

        db.Execute("CREATE TABLE SaveEnvelopeEntity (SlotId TEXT PRIMARY KEY, SchemaVersion INTEGER)")
        |> ignore

        db.Execute("INSERT INTO SaveEnvelopeEntity (SlotId, SchemaVersion) VALUES (?, ?)", slot, 4)
        |> ignore

        Db.initTablesForSlot slot |> Async.AwaitTask |> Async.RunSynchronously

        Expect.equal
            (db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name='CareerSnapshot'"))
            0
            "initialization does not modify a legacy save"

        match Db.listSaveSlotsAsync () |> Async.AwaitTask |> Async.RunSynchronously with
        | Ok [ info ] -> Expect.equal info.Status (IncompatibleSlot(Db.CurrentSchema, 4)) "old format is explicit"
        | other -> failtestf "legacy slot was not listed: %A" other

        match load root slot with
        | Error(Incompatible(_, expected, actual)) ->
            Expect.equal (expected, actual) (Db.CurrentSchema, 4) "load rejects unsupported formats"
        | other -> failtestf "old save was not rejected: %A" other

let legacyReplacementTests =
    testCase "explicit replacement archives a legacy save and failed replacement restores it"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "old-default"
        let path = Path.Combine(root, "career-old-default.db3")
        Db.configureStorageDirectoryForTests root

        do
            use db = new SQLiteConnection(path, true)

            db.Execute("CREATE TABLE SaveEnvelopeEntity (SlotId TEXT PRIMARY KEY, SchemaVersion INTEGER)")
            |> ignore

            db.Execute("INSERT INTO SaveEnvelopeEntity (SlotId, SchemaVersion) VALUES (?, ?)", slot, 4)
            |> ignore

        let replacement = state (DateTime(2027, 1, 1)) (dataLock None)
        Db.injectCommitFailureForTests true

        let failed =
            try
                Db.replaceGameInSlotAsync slot replacement (clock 0UL 1) saveSettings
                |> Async.AwaitTask
                |> Async.RunSynchronously

                false
            with _ ->
                true

        Db.injectCommitFailureForTests false
        Expect.isTrue failed "injected failure rejects the replacement"

        match load root slot with
        | Error(Incompatible _) -> ()
        | other -> failtestf "failed replacement removed the previous save: %A" other

        Db.injectReplacementBackupFailureForTests true

        let failedBackup =
            try
                Db.replaceGameInSlotAsync slot replacement (clock 0UL 1) saveSettings
                |> Async.AwaitTask
                |> Async.RunSynchronously

                false
            with _ ->
                true

        Db.injectReplacementBackupFailureForTests false
        Expect.isTrue failedBackup "failed backup is reported after an incompatible replacement"

        match load root slot with
        | Error(Incompatible _) -> ()
        | other -> failtestf "failed backup removed the old incompatible save: %A" other

        Db.replaceGameInSlotAsync slot replacement (clock 0UL 1) saveSettings
        |> Async.AwaitTask
        |> Async.RunSynchronously

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State replacement "new career starts in the old slot"
        | Error error -> failtestf "replacement did not load: %A" error

        Expect.equal
            (Directory.EnumerateFiles(root, "career-old-default.db3.legacy-*")
             |> Seq.filter (fun file -> not (file.EndsWith(".bak", StringComparison.Ordinal)))
             |> Seq.length)
            1
            "the old save is archived rather than deleted"

let unsupportedSnapshotTests =
    testCase "a newer snapshot stays untouched until explicitly replaced"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "future"
        let first = state (DateTime(2027, 1, 1)) (dataLock None)
        save root slot first (clock 1UL 1)

        do
            use db = new SQLiteConnection(Path.Combine(root, "career-future.db3"), true)

            db.Execute("UPDATE CareerSnapshot SET SchemaVersion = ? WHERE Id = 1", Db.CurrentSchema + 1)
            |> ignore

        Db.initTablesForSlot slot |> Async.AwaitTask |> Async.RunSynchronously

        match load root slot with
        | Error(Incompatible(_, _, version)) -> Expect.equal version (Db.CurrentSchema + 1) "future format is rejected"
        | other -> failtestf "unknown snapshot format was loaded: %A" other

        let next = state (DateTime(2028, 1, 1)) (dataLock None)

        Db.replaceGameInSlotAsync slot next (clock 0UL 1) saveSettings
        |> Async.AwaitTask
        |> Async.RunSynchronously

        match load root slot with
        | Ok loaded -> Expect.equal loaded.State next "replacement makes a new career"
        | Error error -> failtestf "replacement did not load: %A" error

let damagedStartupTests =
    testCase "initializing an unreadable default save does not prevent the application from starting"
    <| fun _ ->
        let root = tempRoot ()
        let path = Path.Combine(root, "career-career-1.db3")
        File.WriteAllBytes(path, [| 1uy; 2uy; 3uy |])
        Db.configureStorageDirectoryForTests root
        Db.initTables () |> Async.AwaitTask |> Async.RunSynchronously

        let next = state (DateTime(2027, 1, 1)) (dataLock None)

        Db.replaceGameInSlotAsync Db.DefaultSlot next (clock 0UL 1) saveSettings
        |> Async.AwaitTask
        |> Async.RunSynchronously

        match load root Db.DefaultSlot with
        | Ok loaded -> Expect.equal loaded.State next "corrupt file is archived and replaced"
        | Error error -> failtestf "corrupt save could not be replaced: %A" error

let generatedCareerTests =
    testCase "a generated career with players, fixtures and clubs survives a full round-trip"
    <| fun _ ->
        let country =
            """{"id":"test.save:country/tst","kind":"country","schema":1,"code":"TST","confederation":"UEFA","nameKey":"test.save:country/tst.name","domain":{"Country":{"Code":"TST","Name":"Testland","Confederation":"UEFA"},"Names":{"FirstNames":["Alex","Sam"],"LastNames":["Stone","River"]},"LeagueNames":["Test League"],"LeagueRules":[{"PointsForWin":3,"PointsForDraw":1,"Tiebreakers":["GD","GS"],"Promotion":[],"Relegation":[]}],"Clubs":[{"Name":"North FC","LeagueLevel":0,"Reputation":7000},{"Name":"South FC","LeagueLevel":0,"Reputation":6500},{"Name":"East FC","LeagueLevel":0,"Reputation":6000},{"Name":"West FC","LeagueLevel":0,"Reputation":6000}],"Cups":[]}}"""

        let package: ModPackage =
            { Manifest =
                { Id = ModId "test.save"
                  Version = "1.0.0"
                  Dependencies = []
                  Priority = 0
                  Before = []
                  After = [] }
              PackageSha256 = sha256 "test-save"
              SportsContent = Map.ofList [ ContentId "test.save:country/tst", country ]
              Translations = Map.empty
              Assets = Map.empty
              Patches = [] }

        let resolved =
            match
                ModSystem.resolve
                    [ { RelativePath = "test-save.femod"
                        Package = package } ]
            with
            | Ok value -> value
            | Error error -> failtestf "could not resolve test country: %A" error

        let gs =
            match WorldGen.generateNewGame resolved 0xC02UL (DateTime(2026, 7, 1)) "TST" "Test Manager" [] with
            | Ok value -> value
            | Error error -> failtestf "could not generate test world: %A" error

        Expect.isGreaterThan gs.Players.Count 0 "the fixture is a populated world"
        let prior = Map.ofList [ for key in 0 .. 23 -> key, ({ Samples = 48.0; RewardSum = 36.0 }: AttackEstimate) ]
        let gs =
            { gs with
                Clubs = gs.Clubs |> Map.map (fun _ club ->
                    { club with CoordinationMemory = { club.CoordinationMemory with AttackLearning = prior } })
                // An open job offer must survive the save: it is the only way back into work after a dismissal.
                JobOffers =
                    [ { Id = 7
                        ClubId = gs.Clubs.Keys |> Seq.max
                        Offered = gs.CurrentDate
                        Expires = gs.CurrentDate.AddDays 14.0
                        Salary = 75_000m
                        ContractYears = 2 } ]
                NextJobOfferId = 8 }
        let root = tempRoot ()
        let matchId, fixture =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
            |> Seq.find (fun (_, fixture) -> MatchFixture.involves gs.UserClubId fixture)
        let job =
            WorldRunner.prepareUserMatchJobWithBalance
                BalanceConfig.defaultConfig
                (WorldClockOps.init gs.Season)
                { gs with CurrentDate = fixture.ScheduledDate }
                matchId
            |> function
                | Ok job -> job
                | Error error -> failtest error
        let command =
            { EffectiveTick = 80<subtick>
              Sequence = 0UL
              Revision = job.Input.EffectiveConfig.Revision
              Manager = job.Input.Context.HomeClub.Id
              Action = ChangeTactics { job.Input.Context.HomeClub.BaseTactics with Tempo = 0.8f } }
        let worldClock =
            { WorldClockOps.init gs.Season with
                MatchExecutions = Map.ofList [ matchId, (job.Input, [ command ]) ] }
        save root "generated" gs worldClock

        use db = new SQLiteConnection(Path.Combine(root, "career-generated.db3"), true)
        let executionRowId () =
            db.ExecuteScalar<int64>("SELECT rowid FROM CareerChunk WHERE Key = ?", $"match-execution:{matchId}")
        let originalExecutionRowId = executionRowId ()
        Expect.isGreaterThan originalExecutionRowId 0L "the execution has an independent immutable chunk"
        use rootPayload = new MemoryStream(db.Find<CareerSnapshot>(1).Payload)
        use rootZip = new GZipStream(rootPayload, CompressionMode.Decompress)
        use rootText = new StreamReader(rootZip)
        Expect.isFalse
            ((rootText.ReadToEnd()).Contains(job.Input.EngineVersion, StringComparison.Ordinal))
            "the daily root does not duplicate historical football inputs"

        match load root "generated" with
        | Ok result ->
            Expect.equal result.State gs "all generated world entities survive"
            Expect.equal result.WorldClock worldClock "the full clock survives"
            let input, commands = result.WorldClock.MatchExecutions[matchId]
            Expect.equal (MatchJob.inputHash input) (MatchJob.inputHash job.Input) "recorded football input survives JSON without numerical drift"
            Expect.equal commands [ command ] "managerial journal survives save/load"
            let checkpointSession =
                MatchSession.create Scheduler.systems input MatchRunMode.Fast
                |> function
                    | Ok session -> session
                    | Error error -> failtestf "%A" error
            MatchSession.receive command checkpointSession
            |> function
                | Ok() -> ()
                | Error error -> failtestf "restored command was rejected: %A" error
            save root "generated" { gs with ExecutionSeed = gs.ExecutionSeed + 1UL } worldClock
            Expect.equal (executionRowId ()) originalExecutionRowId "new attempts do not rewrite immutable recordings"
            Expect.equal result.ResolvedData.DataLock gs.DataLock "resolved mod data is restored"

            let direct =
                WorldRunner.advanceDaysWithBalance BalanceConfig.defaultConfig 1 worldClock gs

            let continued =
                WorldRunner.advanceDaysWithBalance BalanceConfig.defaultConfig 1 result.WorldClock result.State

            let differences =
                Array.zip
                    (Microsoft.FSharp.Reflection.FSharpType.GetRecordFields(typeof<GameState>))
                    (Array.zip
                        (Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields(box continued.GameState))
                        (Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields(box direct.GameState)))
                |> Array.choose (fun (field, (actual, expected)) -> if actual = expected then None else Some field.Name)

            let playerDifferences =
                direct.GameState.Players
                |> Map.toSeq
                |> Seq.choose (fun (id, expected) ->
                    match continued.GameState.Players |> Map.tryFind id with
                    | Some actual when actual <> expected ->
                        let fields = Microsoft.FSharp.Reflection.FSharpType.GetRecordFields(typeof<Player>)
                        let left = Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields(box actual)
                        let right = Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields(box expected)

                        Some(
                            id,
                            Array.zip3 fields left right
                            |> Array.choose (fun (field, a, b) -> if a = b then None else Some field.Name)
                            |> Array.toList
                        )
                    | _ -> None)
                |> Seq.truncate 3
                |> Seq.toList

            Expect.equal
                (differences, playerDifferences)
                ([||], [])
                "continuation preserves every generated world field"

            Expect.equal continued.WorldClock direct.WorldClock "continuation preserves scheduler state"

            let advanced =
                WorldRunner.advanceDaysWithBalance BalanceConfig.defaultConfig 8 worldClock gs

            save root "after-days" advanced.GameState advanced.WorldClock

            match load root "after-days" with
            | Ok reloaded ->
                Expect.equal reloaded.State advanced.GameState "career after several days round-trips"
                Expect.equal reloaded.WorldClock advanced.WorldClock "one-based week and month remain loadable"

                let uninterrupted =
                    WorldRunner.advanceDaysWithBalance
                        BalanceConfig.defaultConfig
                        1
                        advanced.WorldClock
                        advanced.GameState

                let resumed =
                    WorldRunner.advanceDaysWithBalance BalanceConfig.defaultConfig 1 reloaded.WorldClock reloaded.State

                Expect.equal resumed.GameState uninterrupted.GameState "continued career is identical after load"
                Expect.equal resumed.WorldClock uninterrupted.WorldClock "continued clock is identical after load"
                // This calendar window may contain no fixture; execute and accept a real pending match before claiming learned persistence.
                let matchId, fixture = resumed.GameState.Competitions |> Map.toSeq |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq) |> Seq.find (fun (_, fixture) -> MatchFixture.isPending fixture && MatchFixture.involves resumed.GameState.UserClubId fixture)
                let ready = {resumed.GameState with CurrentDate = fixture.ScheduledDate.Date}
                let unwrap = function Ok value -> value | Error error -> failtestf "%A" error
                let job = FootballEngine.World.WorldRunner.prepareUserMatchJobWithBalance BalanceConfig.defaultConfig resumed.WorldClock ready matchId |> unwrap
                let session = MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast |> unwrap
                let outcome = MatchSession.finish session |> unwrap
                let accepted = FootballEngine.World.WorldRunner.commitUserMatchJobWithBalance BalanceConfig.defaultConfig resumed.WorldClock ready (FootballEngine.World.WorldRunner.matchJobResult job [] outcome) |> unwrap
                let resumed = accepted.DayResult
                for side, club, final in [ClubSide.Home, job.Input.Context.HomeClub, outcome.Final.Home; ClubSide.Away, job.Input.Context.AwayClub, outcome.Final.Away] do
                    Expect.equal resumed.GameState.Clubs[int club.Id].CoordinationMemory.AttackLearning final.Attack.Estimates "accepted saturated online updates survive into the career"
                    Expect.equal final.Attack.Estimates (AttackLearning.applyEvents BalanceConfig.defaultConfig.CollectiveAttack side club.AttackPrior outcome.Events) "chronological events are sufficient to reconstruct the exact learned policy"
                save root "learned-match" resumed.GameState resumed.WorldClock
                match load root "learned-match" with
                | Ok learned -> Expect.equal learned.State resumed.GameState "real accepted learning survives SQLite roundtrip"
                | Error error -> failtestf "accepted learning failed to reload: %A" error
                Expect.isTrue (resumed.GameState.Clubs |> Map.exists (fun _ club -> club.CoordinationMemory.AttackLearning <> prior)) "real matches update saturated memory before this save/load sequence"
                let season = FootballEngine.World.WorldRunner.advanceToNextSeasonWithBalance BalanceConfig.defaultConfig resumed.WorldClock resumed.GameState
                for KeyValue(id, club) in resumed.GameState.Clubs do
                    Expect.equal season.NextSeasonGs.Clubs[id].CoordinationMemory.AttackLearning (AttackLearning.retain BalanceConfig.defaultConfig.CollectiveAttack club.CoordinationMemory.AttackLearning) "season turnover retains exactly learned samples, without replaying kickoff priors"
                save root "learned-next-season" season.NextSeasonGs season.NextSeasonClock
                match load root "learned-next-season" with
                | Error error -> failtestf "learned seasonal save failed: %A" error
                | Ok loadedSeason ->
                    Expect.equal loadedSeason.State season.NextSeasonGs "seasonal policy and original recordings survive database roundtrip"
                    let fixture = loadedSeason.State.Competitions |> Map.toSeq |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq) |> Seq.map snd |> Seq.find MatchFixture.isPending
                    let next = FootballEngine.World.Phases.MatchContextBuilder.build loadedSeason.State fixture BalanceConfig.defaultConfig |> Option.get
                    Expect.equal next.HomeClub.AttackPrior loadedSeason.State.Clubs[fixture.HomeClubId].CoordinationMemory.AttackLearning "new season's actual kickoff reads restored policy"
            | Error error -> failtestf "career after several days could not be loaded: %A" error
        | Error error -> failtestf "generated world round-trip failed: %A" error

let executionRandomnessTests =
    testCase "reloading the same pre-advance save rerolls at the application boundary"
    <| fun _ ->
        let root = tempRoot ()
        let gs = state (DateTime(2026, 8, 1)) (dataLock None)
        let clock = WorldClockOps.init gs.Season
        save root "reroll" gs clock
        let loaded =
            load root "reroll"
            |> function
                | Ok loaded -> loaded
                | Error error -> failtestf "%A" error
        let state =
            { AppTypes.initialState () with
                Mode = AppTypes.InGame(loaded.State, AppTypes.managerEmployment loaded.State)
                WorldClock = loaded.WorldClock
                IsProcessing = false }
        let first, _ = UpdateSim.handle (AppMsgs.Advance 1) state
        let second, _ = UpdateSim.handle (AppMsgs.Advance 1) state
        let gameState (state: AppTypes.State) =
            match state.Mode with
            | AppTypes.InGame(gs, _) -> gs
            | _ -> failtest "advance lost the career"
        let firstGs, secondGs = gameState first, gameState second
        Expect.notEqual firstGs.ExecutionSeed secondGs.ExecutionSeed "save-scumming creates a new attempt"
        Expect.equal { firstGs with ExecutionSeed = gs.ExecutionSeed } gs "only the boundary seed changes before the worker"
        let busy, _ = UpdateSim.handle (AppMsgs.Advance 1) first
        Expect.equal (gameState busy).ExecutionSeed firstGs.ExecutionSeed "duplicate clicks do not reroll an accepted execution"
        let noOp, _ = UpdateSim.handle (AppMsgs.Advance 0) state
        Expect.equal (gameState noOp).ExecutionSeed gs.ExecutionSeed "a no-op cannot consume a new execution"
        let blocked = { state with Match = { state.Match with RuntimeId = Some(AppTypes.MatchRuntimeId 999UL) } }
        let unchanged, _ = UpdateSim.handle (AppMsgs.Advance 1) blocked
        Expect.equal (gameState unchanged).ExecutionSeed gs.ExecutionSeed "continuing a live match cannot replace its seed"
        let staleAiResult: WorldRunner.DayResult =
            { GameState = gs
              WorldClock = clock
              PlayedMatches = []
              Logs = []
              SeasonComplete = false }
        let guarded, _ = UpdateSim.handle (AppMsgs.AiMatchesDone staleAiResult) first
        Expect.equal guarded first "a same-day result from an older attempt cannot overwrite the accepted seed"

let executionSchemaTests =
    testCase "schema seven cannot silently acquire execution randomness defaults"
    <| fun _ ->
        let root = tempRoot ()
        let gs = state (DateTime(2026, 8, 1)) (dataLock None)
        save root "before-execution" gs (WorldClockOps.init gs.Season)
        use db = new SQLiteConnection(Path.Combine(root, "career-before-execution.db3"), true)
        db.Execute("UPDATE CareerSnapshot SET SchemaVersion = 7 WHERE Id = 1") |> ignore
        match load root "before-execution" with
        | Error(Incompatible(_, expected, actual)) -> Expect.equal (expected, actual) (Db.CurrentSchema, 7) "there is no compatibility facade"
        | other -> failtestf "schema seven was not rejected: %A" other
        Expect.equal (db.ExecuteScalar<int>("SELECT SchemaVersion FROM CareerSnapshot WHERE Id = 1")) 7 "load never migrates the file"

let collectiveSchemaTests =
    testCase "schema eight is rejected instead of inventing collective priors"
    <| fun _ ->
        let root = tempRoot ()
        let gs = state (DateTime(2026, 8, 1)) (dataLock None)
        save root "before-collective" gs (WorldClockOps.init gs.Season)
        use db = new SQLiteConnection(Path.Combine(root, "career-before-collective.db3"), true)
        for schema in [8; 9] do
            db.Execute("UPDATE CareerSnapshot SET SchemaVersion = ? WHERE Id = 1", schema) |> ignore
            match load root "before-collective" with
            | Error(Incompatible(_, expected, actual)) -> Expect.equal (expected, actual) (10, schema) "obsolete contract rejected explicitly"
            | other -> failtestf "obsolete schema was not rejected: %A" other
            Expect.equal (db.ExecuteScalar<int>("SELECT SchemaVersion FROM CareerSnapshot WHERE Id = 1")) schema "read does not migrate or overwrite"

let tests =
    testList
        "Persistence"
        [ executionRandomnessTests
          executionSchemaTests
          collectiveSchemaTests
          isolationTests
          shutdownWriteTests
          capturedSlotPathTests
          foreignSlotWriteTests
          unknownSchemaWriteTests
          incompleteSchemaWriteTests
          backupTests
          unrecoverableFilesTests
          walRecoveryTests
          commitFailureTests
          replacementBackupFailureTests
          replacementCommitRollbackTests
          manifestTests
          effectiveConfigTests
          equivalenceTests
          discriminatedUnionRoundtripTests
          newCareerReplacementTests
          fullStateTests
          activeMarketTests
          playedMatchHistoryTests
          outOfOrderWriteTests
          replacementWriteOrderTests
          failedReplacementEditTests
          longCareerTests
          metadataTests
          chunkTests
          dataSnapshotChunkTests
          missingPrimaryTests
          orphanBackupWriteTests
          invalidSlotTests
          incompatibleSchemaTests
          legacyReplacementTests
          unsupportedSnapshotTests
          damagedStartupTests
          generatedCareerTests ]
