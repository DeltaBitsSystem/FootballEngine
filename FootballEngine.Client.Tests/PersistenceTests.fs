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
        use db = new SQLiteConnection(path, true)

        db.Execute("UPDATE CareerSnapshot SET Payload = ? WHERE Id = 1", [| 1uy; 2uy; 3uy |])
        |> ignore

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
              ScoutingMemory = Map.empty }

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

        let first =
            { state date (dataLock None) with
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
            Expect.equal result.WorldClock (clock 1UL 1) "the world revision is independent of saves"
        | Error error -> failtestf "state round-trip failed: %A" error

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
        | Error(Corrupt _) -> ()
        | other -> failtestf "corrupt payload was accepted: %A" other

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
              ScoutingMemory = Map.empty }

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
        | Ok loaded -> Expect.equal loaded.State.CurrentDate first.CurrentDate "backup is loadable"
        | Error error -> failtestf "backup was not loaded: %A" error

let invalidSlotTests =
    testCase "slot names cannot escape the career directory or alias another slot"
    <| fun _ ->
        let root = tempRoot ()
        Db.configureStorageDirectoryForTests root

        Expect.throws
            (fun () -> Db.initTablesForSlot "../other" |> Async.AwaitTask |> Async.RunSynchronously)
            "invalid slot names are rejected"

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
        let root = tempRoot ()
        let worldClock = WorldClockOps.init gs.Season
        save root "generated" gs worldClock

        match load root "generated" with
        | Ok result ->
            Expect.equal result.State gs "all generated world entities survive"
            Expect.equal result.WorldClock worldClock "the full clock survives"
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
        | Error error -> failtestf "generated world round-trip failed: %A" error

let tests =
    testList
        "Persistence"
        [ isolationTests
          backupTests
          commitFailureTests
          manifestTests
          equivalenceTests
          discriminatedUnionRoundtripTests
          newCareerReplacementTests
          fullStateTests
          metadataTests
          chunkTests
          missingPrimaryTests
          invalidSlotTests
          generatedCareerTests ]
