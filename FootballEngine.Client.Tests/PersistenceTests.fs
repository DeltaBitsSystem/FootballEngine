module FootballEngine.Client.Tests.PersistenceTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Expecto
open SQLite
open FootballEngine
open FootballEngine.Database
open FootballEngine.Domain
open FootballEngine.Settings
open FootballEngine.World
open FootballEngine.Core.Match

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
            Expect.equal a.ResolvedData.DataLock first.DataLock "slot A resolved snapshot"
            Expect.equal b.ResolvedData.DataLock second.DataLock "slot B resolved snapshot"
        | left, right -> failtestf "slot load failed: %A / %A" left right

let backupTests =
    testCase "corrupt primary recovers the previous complete SQLite revision"
    <| fun _ ->
        let root = tempRoot ()
        let slot = "backup"
        save root slot (state (DateTime(2027, 1, 1)) (dataLock None)) (clock 1UL 1)
        save root slot (state (DateTime(2027, 1, 2)) (dataLock None)) (clock 2UL 2)
        let path = Path.Combine(root, "career-backup.db3")
        use db = SQLiteConnection(path)
        db.CreateTable<SaveEnvelopeEntity>() |> ignore
        let current = db.Find<SaveEnvelopeEntity>(slot)
        db.InsertOrReplace({ current with EnvelopeJson = "broken" }) |> ignore

        match load root slot with
        | Ok recovered ->
            Expect.equal recovered.Envelope.ConfirmedRevision 1UL "backup revision"
            Expect.equal recovered.State.CurrentDate (DateTime(2027, 1, 1)) "backup state"
        | Error error -> failtestf "backup was not recovered: %A" error

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

let tests =
    testList
        "Persistence"
        [ isolationTests
          backupTests
          commitFailureTests
          manifestTests
          equivalenceTests ]
