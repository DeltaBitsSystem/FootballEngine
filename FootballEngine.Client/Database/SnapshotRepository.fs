namespace FootballEngine

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent
open System.Runtime.CompilerServices
open SQLite
open FootballEngine.Database
open FootballEngine.Domain
open FootballEngine.Core.Match
open FootballEngine.Data
open FootballEngine.World
open FootballEngine.Settings

type SaveSlotStatus =
    | CompatibleSlot
    | IncompatibleSlot of expectedSchema: int * actualSchema: int
    | CorruptSlot of message: string

type SaveSlotInfo =
    { SlotId: string
      ClubName: string option
      ManagerName: string option
      GameDate: DateTime option
      LastWrittenUtc: DateTime
      Status: SaveSlotStatus }

[<CLIMutable>]
type CareerSnapshot =
    { [<PrimaryKey>]
      Id: int
      SchemaVersion: int
      SlotId: string
      SaveSequence: int64
      WorldRevision: int64
      GameDate: DateTime
      ClubName: string
      ManagerName: string
      UpdatedAtUtc: DateTime
      Hash: string
      Payload: byte[] }

[<CLIMutable>]
type CareerChunk =
    { [<PrimaryKey>]
      Key: string
      Hash: string
      Payload: byte[] }

[<CLIMutable>]
type CareerSlotHeader =
    { SlotId: string
      SchemaVersion: int
      GameDate: DateTime
      ClubName: string
      ManagerName: string
      UpdatedAtUtc: DateTime }

[<CLIMutable>]
type private CareerPayload =
    { State: GameState
      Clock: WorldClock
      Settings: SaveSettings
      ConfigContent: string
      ConfigHash: uint64 }

module Db =
    [<Literal>]
    let CurrentSchema = 6

    [<Literal>]
    let DefaultSlot = "career-1"

    type LoadedCareer =
        { Envelope: SaveEnvelope
          State: GameState
          WorldClock: WorldClock
          SaveSettings: SaveSettings
          ResolvedData: ResolvedDataSet }

    let private databaseDirectory = new AsyncLocal<string>()
    let private commitFailure = new AsyncLocal<bool>()

    let configureStorageDirectoryForTests directory = databaseDirectory.Value <- directory
    let injectCommitFailureForTests enabled = commitFailure.Value <- enabled

    let private storageDirectory () =
        if String.IsNullOrWhiteSpace databaseDirectory.Value then
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FootballEngine")
        else
            databaseDirectory.Value

    let private validSlot (slot: string) =
        not (String.IsNullOrWhiteSpace slot)
        && slot |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '_')

    let private slotPath slot =
        if not (validSlot slot) then
            invalidArg (nameof slot) "Save slot IDs may contain only ASCII letters, numbers, '-' and '_'."

        Path.Combine(storageDirectory (), $"career-{slot}.db3")

    let private gates = ConcurrentDictionary<string, SemaphoreSlim>()

    let private gateFor slot =
        gates.GetOrAdd(slotPath slot, fun _ -> new SemaphoreSlim(1, 1))

    let private openDb (path: string) =
        let db = new SQLiteConnection(path, true)
        db.CreateTable<CareerSnapshot>() |> ignore
        db.CreateTable<CareerChunk>() |> ignore
        db

    let private openRead path =
        new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly, true)

    let private hash (bytes: byte[]) =
        Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

    let private snapshotHash (payload: byte[]) (chunks: CareerChunk list) =
        let digest = new StringBuilder(hash payload)

        for chunk in chunks |> List.sortBy _.Key do
            digest.Append('|').Append(chunk.Key).Append(':').Append(chunk.Hash) |> ignore

        hash (Encoding.UTF8.GetBytes(digest.ToString()))

    let private encode payload =
        let json = JsonCodec.serialize payload
        use output = new MemoryStream()

        do
            use zip = new GZipStream(output, CompressionLevel.Fastest, true)
            let content = Encoding.UTF8.GetBytes json
            zip.Write(content, 0, content.Length)

        output.ToArray()

    let private decodeValue<'T> (bytes: byte[]) =
        use input = new MemoryStream(bytes)
        use zip = new GZipStream(input, CompressionMode.Decompress)
        use output = new MemoryStream()
        zip.CopyTo output
        JsonCodec.deserialize<'T> (Encoding.UTF8.GetString(output.ToArray()))

    let private chunkCache = ConditionalWeakTable<obj, CareerChunk>()

    let private chunksFor (state: GameState) =
        let chunk prefix id value =
            chunkCache.GetValue(
                box value,
                fun _ ->
                    let bytes = encode value

                    { Key = $"{prefix}:{id}"
                      Hash = hash bytes
                      Payload = bytes }
            )

        [ for KeyValue(id, player) in state.Players do
              if player.Id <> id then
                  invalidOp "Player map key does not match its ID."

              chunk "player" id player
          for KeyValue(id, club) in state.Clubs do
              if club.Id <> id then
                  invalidOp "Club map key does not match its ID."

              chunk "club" id club
          for KeyValue(id, staff) in state.Staff do
              if staff.Id <> id then
                  invalidOp "Staff map key does not match its ID."

              chunk "staff" id staff
          for KeyValue(id, competition) in state.Competitions do
              if competition.Id <> id then
                  invalidOp "Competition map key does not match its ID."

              chunk "competition" id competition
          for KeyValue(code, country) in state.Countries do
              if country.Country.Code <> code then
                  invalidOp "Country map key does not match its code."

              chunk "country" code country ]

    let private restoreChunks (state: GameState) (chunks: CareerChunk list) =
        let restore prefix decodeValue =
            chunks
            |> List.choose (fun entry ->
                if entry.Key.StartsWith(prefix + ":", StringComparison.Ordinal) then
                    let id, value = decodeValue entry.Payload

                    if entry.Key <> $"{prefix}:{id}" then
                        invalidOp $"Snapshot chunk key '{entry.Key}' does not match its payload."

                    Some(id, value)
                else
                    None)
            |> Map.ofList

        { state with
            Players =
                restore "player" (fun payload ->
                    let player = decodeValue<Player> payload
                    player.Id, player)
            Clubs =
                restore "club" (fun payload ->
                    let club = decodeValue<Club> payload
                    club.Id, club)
            Staff =
                restore "staff" (fun payload ->
                    let staff = decodeValue<Staff> payload
                    staff.Id, staff)
            Competitions =
                restore "competition" (fun payload ->
                    let competition = decodeValue<Competition> payload
                    competition.Id, competition)
            Countries =
                restore "country" (fun payload ->
                    let country = decodeValue<CountryData> payload
                    country.Country.Code, country) }

    let private saveCore
        (slot: string)
        (state: GameState)
        (clock: WorldClock)
        (settings: SaveSettings)
        (replace: bool)
        =
        let path = slotPath slot
        Directory.CreateDirectory(storageDirectory ()) |> ignore

        match SaveSettings.validate settings with
        | Error errors -> invalidOp ("Invalid save settings: " + String.concat "; " errors)
        | Ok _ -> ()

        DataLock.validate state.DataLock
        |> Result.mapError (fun error -> InvalidOperationException($"Invalid DataLock: {error}"))
        |> function
            | Error error -> raise error
            | Ok() -> ()

        if clock.Revision > uint64 Int64.MaxValue then
            invalidArg (nameof clock) "World revision exceeds the SQLite integer range."

        if clock.Current.Season <> state.Season then
            invalidArg (nameof clock) "World clock season does not match the confirmed career."

        let config =
            EffectiveConfig.fromBalance (BalanceResolver.effectiveBalance (Some settings))
            |> function
                | Ok value -> value
                | Error error -> invalidOp $"Invalid effective balance: {error}"

        let payload =
            { State =
                { state with
                    Players = Map.empty
                    Clubs = Map.empty
                    Staff = Map.empty
                    Competitions = Map.empty
                    Countries = Map.empty }
              Clock = clock
              Settings = settings
              ConfigContent = config.Content
              ConfigHash = config.Hash }

        let bytes = encode payload
        let chunks = chunksFor state
        let checksum = snapshotHash bytes chunks
        use db = openDb path
        let previous = db.Find<CareerSnapshot>(1)
        let previousChunks = db.Table<CareerChunk>().ToList() |> List.ofSeq

        if
            not (isNull (box previous))
            && (isNull previous.Payload
                || previousChunks
                   |> List.exists (fun chunk -> isNull chunk.Payload || hash chunk.Payload <> chunk.Hash)
                || snapshotHash previous.Payload previousChunks <> previous.Hash)
        then
            invalidOp "Cannot overwrite a corrupt snapshot; load its recovery copy first."

        if
            not replace
            && not (isNull (box previous))
            && previous.WorldRevision > int64 clock.Revision
        then
            invalidOp $"Save revision {clock.Revision} is older than stored revision {previous.WorldRevision}"

        if not (isNull (box previous)) then
            // SQLite's backup API captures a consistent database, including committed WAL pages.
            db.Backup(path + ".bak", "main")

        let sequence =
            if isNull (box previous) then
                1L
            else
                previous.SaveSequence + 1L

        let oldHashes =
            previousChunks |> List.map (fun chunk -> chunk.Key, chunk.Hash) |> Map.ofList

        let nextKeys = chunks |> List.map _.Key |> Set.ofList

        db.RunInTransaction(fun () ->
            for old in previousChunks do
                if not (nextKeys.Contains old.Key) then
                    db.Delete<CareerChunk>(old.Key) |> ignore

            for chunk in chunks do
                if oldHashes |> Map.tryFind chunk.Key <> Some chunk.Hash then
                    db.InsertOrReplace chunk |> ignore

            db.InsertOrReplace(
                { Id = 1
                  SchemaVersion = CurrentSchema
                  SlotId = slot
                  SaveSequence = sequence
                  WorldRevision = int64 clock.Revision
                  GameDate = state.CurrentDate
                  ClubName =
                    state.Clubs
                    |> Map.tryFind state.UserClubId
                    |> Option.map _.Name
                    |> Option.defaultValue ""
                  ManagerName = GameState.userManagerName state
                  UpdatedAtUtc = DateTime.UtcNow
                  Hash = checksum
                  Payload = bytes }
            )
            |> ignore

            if commitFailure.Value then
                invalidOp "Injected commit failure")

        if replace then
            // A replaced slot belongs to a different career; recovery must never revive the old one.
            db.Backup(path + ".bak", "main")

    let private saveAsync (slot: string) (state: GameState) (clock: WorldClock) (settings: SaveSettings) replace =
        task {
            let gate = gateFor slot
            do! gate.WaitAsync()

            try
                do! Task.Run(fun () -> saveCore slot state clock settings replace)
            finally
                gate.Release() |> ignore
        }

    let saveGameInSlotAsync slot state clock settings =
        saveAsync slot state clock settings false

    let replaceGameInSlotAsync slot state clock settings =
        saveAsync slot state clock settings true

    let saveGameAsync state clock settings =
        saveGameInSlotAsync DefaultSlot state clock settings

    let saveGameWithSettings state clock settings = saveGameAsync state clock settings

    type AutosaveResult =
        | Saved
        | Coalesced

    type AutosaveHandle =
        { Completion: Task<AutosaveResult>
          Cancel: unit -> bool }

    type private AutosavePayload =
        { Slot: string
          Revision: uint64
          State: GameState
          Clock: WorldClock
          Settings: SaveSettings
          Completion: TaskCompletionSource<AutosaveResult>
          Cancellation: CancellationToken }

    let private queueLock = obj ()

    let private pending =
        System.Collections.Generic.Dictionary<string, AutosavePayload>()

    let private active = System.Collections.Generic.HashSet<string>()
    let private activeRevision = System.Collections.Generic.Dictionary<string, uint64>()

    let private runWorker slot =
        let rec loop () =
            let next =
                lock queueLock (fun () ->
                    match pending.TryGetValue slot with
                    | true, value ->
                        pending.Remove slot |> ignore
                        activeRevision[slot] <- value.Revision
                        Some value
                    | _ ->
                        active.Remove slot |> ignore
                        activeRevision.Remove slot |> ignore
                        None)

            match next with
            | None -> ()
            | Some item ->
                try
                    if item.Cancellation.IsCancellationRequested then
                        item.Completion.TrySetCanceled item.Cancellation |> ignore
                    else
                        saveGameInSlotAsync item.Slot item.State item.Clock item.Settings
                        |> Async.AwaitTask
                        |> Async.RunSynchronously

                        item.Completion.TrySetResult Saved |> ignore
                with error ->
                    item.Completion.TrySetException error |> ignore

                loop ()

        Task.Run(fun () -> loop ()) |> ignore

    let enqueueAutosave (slot: string) (state: GameState) (clock: WorldClock) (settings: SaveSettings) cancellation =
        let completion =
            TaskCompletionSource<AutosaveResult>(TaskCreationOptions.RunContinuationsAsynchronously)

        let item =
            { Slot = slot
              Revision = clock.Revision
              State = state
              Clock = clock
              Settings = settings
              Completion = completion
              Cancellation = cancellation }

        lock queueLock (fun () ->
            let staleActive =
                match activeRevision.TryGetValue slot with
                | true, revision -> revision > clock.Revision
                | _ -> false

            let stalePending =
                match pending.TryGetValue slot with
                | true, older -> older.Revision > clock.Revision
                | _ -> false

            if staleActive || stalePending then
                completion.TrySetException(InvalidOperationException("Autosave revision is stale"))
                |> ignore
            else
                match pending.TryGetValue slot with
                | true, older -> older.Completion.TrySetResult Coalesced |> ignore
                | _ -> ()

                pending[slot] <- item

                if active.Add slot then
                    runWorker slot)

        { Completion = completion.Task
          Cancel =
            fun () ->
                lock queueLock (fun () ->
                    match pending.TryGetValue slot with
                    | true, current when obj.ReferenceEquals(current.Completion, completion) ->
                        pending.Remove slot |> ignore
                        completion.TrySetCanceled cancellation |> ignore
                        true
                    | _ -> false) }

    let private manifest (dataLock: DataLock) =
        dataLock.Packages
        |> List.map (fun package ->
            { Id = package.ModId
              Version = package.Version
              SchemaVersion = package.SchemaVersion
              Dependencies = package.Dependencies |> List.map fst
              Precedence = package.Precedence
              ContentHash = package.PackageSha256 })

    let private loadFile slot path : Result<LoadedCareer, LoadError> =
        try
            use db = openRead path
            let entry = db.Find<CareerSnapshot>(1)

            if isNull (box entry) then
                Error(Missing slot)
            elif entry.SchemaVersion <> CurrentSchema then
                Error(Incompatible(slot, CurrentSchema, entry.SchemaVersion))
            elif entry.SlotId <> slot || entry.SaveSequence < 1L || entry.WorldRevision < 0L then
                Error(Corrupt(slot, "Invalid snapshot identity or sequence"))
            else
                let chunks = db.Table<CareerChunk>().ToList() |> List.ofSeq

                if
                    isNull entry.Payload
                    || chunks
                       |> List.exists (fun chunk ->
                           isNull chunk.Payload
                           || hash chunk.Payload <> chunk.Hash
                           || not (
                               [ "player:"; "club:"; "staff:"; "competition:"; "country:" ]
                               |> List.exists (fun prefix -> chunk.Key.StartsWith(prefix, StringComparison.Ordinal))
                           ))
                    || snapshotHash entry.Payload chunks <> entry.Hash
                then
                    invalidOp "Snapshot checksum mismatch"

                let payload = decodeValue<CareerPayload> entry.Payload

                if isNull (box payload) || isNull (box payload.State) || isNull (box payload.Clock) then
                    Error(Corrupt(slot, "Snapshot is incomplete"))
                elif
                    payload.Clock.Revision <> uint64 entry.WorldRevision
                    || payload.State.CurrentDate <> entry.GameDate
                    || payload.Clock.Current.Season <> payload.State.Season
                then
                    Error(Corrupt(slot, "Snapshot clock and metadata disagree"))
                else
                    let state = restoreChunks payload.State chunks
                    let dataLock = state.DataLock

                    let envelope: SaveEnvelope =
                        { SchemaVersion = entry.SchemaVersion
                          EngineVersion = "1.0.0"
                          CareerId = slot
                          ConfirmedRevision = payload.Clock.Revision
                          ConfirmedState = state
                          ConfirmedDate = state.CurrentDate
                          Phase = BeforeMatch
                          ClockDay = payload.Clock.Current.Day
                          ClockWeek = payload.Clock.Current.Week
                          ClockMonth = payload.Clock.Current.Month
                          ClockSeason = payload.Clock.Current.Season
                          Rng =
                            { Seed = 0UL
                              S0 = 0UL
                              S1 = 0UL
                              S2 = 0UL
                              S3 = 0UL }
                          NextIds =
                            { NextInboxId = state.NextInboxId
                              NextNegotiationId = state.NextNegotiationId
                              NextScheduledWorkId = 0UL
                              NextCommandId = 0UL
                              NextEventId = 0UL }
                          PendingCommands = []
                          PendingAgendaJson = ""
                          EntitiesJson = ""
                          ObligationsJson = ""
                          RelationsJson = ""
                          LearningJson = ""
                          KnowledgeJson = ""
                          ConfigContent = payload.ConfigContent
                          ConfigHash = payload.ConfigHash
                          DataManifest = manifest dataLock
                          DataLock = dataLock
                          ResolvedDataHash = hash (Encoding.UTF8.GetBytes(JsonCodec.serialize dataLock.Snapshot))
                          ResolvedDataContent = JsonCodec.serialize dataLock.Snapshot }

                    match SaveEnvelope.validate envelope, SaveSettings.validate payload.Settings with
                    | Error error, _ -> Error(Corrupt(slot, error))
                    | _, Error errors -> Error(Corrupt(slot, String.concat "; " errors))
                    | Ok(), Ok settings ->
                        match ModSystem.restoreDataSet dataLock with
                        | Error error ->
                            Error(Corrupt(slot, error.Diagnostics |> List.map _.Message |> String.concat "; "))
                        | Ok resolved ->
                            match ResolvedWorldData.materialize resolved with
                            | Error error ->
                                Error(Corrupt(slot, error.Diagnostics |> List.map _.Message |> String.concat "; "))
                            | Ok _ ->
                                Ok
                                    { Envelope = envelope
                                      State = state
                                      WorldClock = payload.Clock
                                      SaveSettings = settings
                                      ResolvedData = resolved }
        with error ->
            Error(Corrupt(slot, error.Message))

    let loadEnvelope slot : Task<Result<LoadedCareer, LoadError>> =
        task {
            let path = slotPath slot

            if not (File.Exists path || File.Exists(path + ".bak")) then
                return Error(Missing slot)
            else
                let gate = gateFor slot
                do! gate.WaitAsync()

                try
                    return!
                        Task.Run(fun () ->
                            let primary =
                                if File.Exists path then
                                    loadFile slot path
                                else
                                    Error(Missing slot)

                            match primary with
                            | Ok result -> Ok result
                            | Error(Incompatible _ as error) -> Error error
                            | Error error ->
                                if File.Exists(path + ".bak") then
                                    match loadFile slot (path + ".bak") with
                                    | Ok result -> Ok result
                                    | Error _ -> Error error
                                else
                                    Error error)
                finally
                    gate.Release() |> ignore
        }

    let loadEnvelopeWithManifest slot expectedManifest =
        task {
            let! result = loadEnvelope slot

            return
                match result with
                | Ok loaded when loaded.Envelope.DataManifest <> expectedManifest ->
                    Error(Corrupt(slot, "saved data manifest does not match the installed mod manifest"))
                | other -> other
        }

    let loadGameInSlot slot =
        task {
            let! result = loadEnvelope slot

            return
                result
                |> Result.map (fun career -> career.State, career.WorldClock, career.SaveSettings, career.ResolvedData)
        }

    let loadGame () = loadGameInSlot DefaultSlot

    let listSaveSlotsAsync () : Task<Result<SaveSlotInfo list, string>> =
        task {
            try
                if not (Directory.Exists(storageDirectory ())) then
                    return Ok []
                else
                    return!
                        Task.Run(fun () ->
                            Directory.EnumerateFiles(storageDirectory (), "career-*.db3*")
                            |> Seq.choose (fun path ->
                                let name = Path.GetFileName path

                                let suffix =
                                    if name.EndsWith(".db3.bak", StringComparison.Ordinal) then
                                        ".db3.bak"
                                    elif name.EndsWith(".db3", StringComparison.Ordinal) then
                                        ".db3"
                                    else
                                        ""

                                if suffix = "" then
                                    None
                                else
                                    let slot =
                                        name.Substring(
                                            "career-".Length,
                                            name.Length - "career-".Length - suffix.Length
                                        )

                                    if validSlot slot then Some slot else None)
                            |> Seq.distinct
                            |> Seq.map (fun slot ->
                                let path = slotPath slot

                                let readHeader source =
                                    use db = openRead source

                                    db.Query<CareerSlotHeader>(
                                        "SELECT SlotId, SchemaVersion, GameDate, ClubName, ManagerName, UpdatedAtUtc FROM CareerSnapshot WHERE Id = 1"
                                    )
                                    |> Seq.tryHead

                                try
                                    let entry =
                                        try
                                            readHeader path
                                        with _ when File.Exists(path + ".bak") ->
                                            readHeader (path + ".bak")

                                    if entry.IsNone then
                                        { SlotId = slot
                                          ClubName = None
                                          ManagerName = None
                                          GameDate = None
                                          LastWrittenUtc = DateTime.MinValue
                                          Status = CorruptSlot "Snapshot metadata is missing" }
                                    else
                                        let entry = entry.Value

                                        { SlotId = slot
                                          ClubName = Some entry.ClubName
                                          ManagerName = Some entry.ManagerName
                                          GameDate = Some entry.GameDate
                                          LastWrittenUtc = entry.UpdatedAtUtc
                                          Status =
                                            if entry.SchemaVersion = CurrentSchema then
                                                CompatibleSlot
                                            else
                                                IncompatibleSlot(CurrentSchema, entry.SchemaVersion) }
                                with error ->
                                    { SlotId = slot
                                      ClubName = None
                                      ManagerName = None
                                      GameDate = None
                                      LastWrittenUtc = DateTime.MinValue
                                      Status = CorruptSlot error.Message })
                            |> Seq.sortByDescending _.LastWrittenUtc
                            |> Seq.toList
                            |> Ok)
            with error ->
                return Error error.Message
        }

    let initTablesForSlot slot =
        task {
            Directory.CreateDirectory(storageDirectory ()) |> ignore
            use _db = openDb (slotPath slot)
            return ()
        }

    let initTables () = initTablesForSlot DefaultSlot
