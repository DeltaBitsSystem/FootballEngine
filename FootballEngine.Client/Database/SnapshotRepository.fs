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
    let CurrentSchema = 11

    [<Literal>]
    let DefaultSlot = "career-1"

    type LoadedCareer =
        { Envelope: SaveEnvelope
          State: GameState
          WorldClock: WorldClock
          SaveSettings: SaveSettings
          ResolvedData: ResolvedDataSet
          RecoveredFromBackup: bool }

    let private databaseDirectory = new AsyncLocal<string>()
    let private commitFailure = new AsyncLocal<bool>()
    let private replacementBackupFailure = new AsyncLocal<bool>()
    exception private CommittedBackupFailure of exn

    let configureStorageDirectoryForTests directory = databaseDirectory.Value <- directory
    let injectCommitFailureForTests enabled = commitFailure.Value <- enabled

    let injectReplacementBackupFailureForTests enabled =
        replacementBackupFailure.Value <- enabled

    let private storageDirectory () =
        if String.IsNullOrWhiteSpace databaseDirectory.Value then
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FootballEngine")
        else
            databaseDirectory.Value

    let private validSlot (slot: string) =
        not (String.IsNullOrWhiteSpace slot)
        && slot
           |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || Char.IsAsciiDigit c || c = '-' || c = '_')

    let private slotPath slot =
        if not (validSlot slot) then
            invalidArg (nameof slot) "Save slot IDs may contain only lowercase ASCII letters, numbers, '-' and '_'."

        Path.Combine(storageDirectory (), $"career-{slot}.db3")

    type private SlotWriteGate() =
        let semaphore = new SemaphoreSlim(1, 1)
        let sync = obj ()
        let mutable issued = 0L
        let mutable committed = 0L
        let mutable replacementIssued = 0L

        member _.Semaphore = semaphore

        member _.Issue(replace) =
            lock sync (fun () ->
                issued <- issued + 1L

                if replace then
                    replacementIssued <- issued

                issued)

        member _.Superseded sequence =
            lock sync (fun () -> sequence <= committed)

        member _.Commit sequence =
            lock sync (fun () -> committed <- max committed sequence)

        member _.NeedsReplacement sequence =
            lock sync (fun () -> replacementIssued > committed && replacementIssued <= sequence)

    let private gates = ConcurrentDictionary<string, SlotWriteGate>()
    let private inFlightWrites = ConcurrentDictionary<int64, string * Task>()
    let mutable private nextWriteId = 0L

    let waitForInFlightWrites () : Task =
        inFlightWrites.Values |> Seq.map snd |> Seq.toArray |> Task.WhenAll

    let waitForSlotWrites slot : Task =
        let path = slotPath slot

        inFlightWrites.Values
        |> Seq.choose (fun (writePath, pending) -> if writePath = path then Some pending else None)
        |> Seq.toArray
        |> Task.WhenAll

    let private gateFor slot =
        gates.GetOrAdd(slotPath slot, fun _ -> SlotWriteGate())

    let private openDb (path: string) =
        let db = new SQLiteConnection(path, true)
        db.CreateTable<CareerSnapshot>() |> ignore
        db.CreateTable<CareerChunk>() |> ignore
        db

    let private openRead path =
        new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly, true)

    let private legacySchema path =
        use db = openRead path

        if
            db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CareerSnapshot'") = 0
            && db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SaveEnvelopeEntity'"
            ) > 0
        then
            Some(db.ExecuteScalar<int>("SELECT COALESCE(MAX(SchemaVersion), 4) FROM SaveEnvelopeEntity"))
        else
            None

    let private incompatibleSchema path =
        match legacySchema path with
        | Some version -> Some version
        | None ->
            use db = openRead path

            if
                db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CareerSnapshot'") = 0
            then
                Some 0
            elif
                db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CareerChunk'") = 0
            then
                Some 0
            elif db.ExecuteScalar<int>("SELECT COUNT(*) FROM CareerSnapshot WHERE Id = 1") = 0 then
                if
                    db.ExecuteScalar<int>("SELECT COUNT(*) FROM CareerSnapshot") > 0
                    || db.ExecuteScalar<int>("SELECT COUNT(*) FROM CareerChunk") > 0
                then
                    Some 0
                else
                    None
            else
                let version =
                    db.ExecuteScalar<int>("SELECT SchemaVersion FROM CareerSnapshot WHERE Id = 1")

                if version = CurrentSchema then None else Some version

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

    let private chunksFor (state: GameState) (clock: WorldClock) =
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

              chunk "country" code country
          chunk "datalock" "snapshot" state.DataLock.Snapshot
          // Immutable recordings stay in independent chunks so decades do not inflate every daily root write.
          for KeyValue(id, execution) in clock.MatchExecutions do
              let input, _ = execution

              if input.MatchId <> uint64 id then
                  invalidOp "Match execution map key does not match its input ID."

              chunk "match-execution" id execution ]

    let private restoreChunks (state: GameState) (chunks: CareerChunk list) =
        let snapshot =
            chunks
            |> List.filter (fun entry -> entry.Key = "datalock:snapshot")
            |> function
                | [ entry ] -> decodeValue<DataSnapshot> entry.Payload
                | _ -> invalidOp "Expected exactly one resolved data snapshot chunk."

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
            DataLock =
                { state.DataLock with
                    Snapshot = snapshot }
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

    let private restoreMatchExecutions (chunks: CareerChunk list) =
        chunks
        |> List.choose (fun entry ->
            if entry.Key.StartsWith("match-execution:", StringComparison.Ordinal) then
                let input, commands = decodeValue<MatchInput * MatchCommand list> entry.Payload

                if
                    input.MatchId > uint64 Int32.MaxValue
                    || entry.Key <> $"match-execution:{input.MatchId}"
                then
                    invalidOp "Match execution chunk key does not match its input ID."

                Some(int input.MatchId, (input, commands))
            else
                None)
        |> Map.ofList

    let private saveCore
        (slot: string)
        (path: string)
        (state: GameState)
        (clock: WorldClock)
        (settings: SaveSettings)
        (replace: bool)
        =
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

        if not replace && not (File.Exists path) && File.Exists(path + ".bak") then
            invalidOp "Cannot save over an orphaned backup; load the slot to recover it first."

        if File.Exists path then
            match incompatibleSchema path with
            | Some version ->
                invalidOp $"Save schema {version} is incompatible with schema {CurrentSchema} in '{path}'."
            | None -> ()

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
                    Countries = Map.empty
                    DataLock =
                        { state.DataLock with
                            Snapshot =
                                { SportsContent = ""
                                  Translations = ""
                                  Assets = "" } } }
              Clock =
                { clock with
                    MatchExecutions = Map.empty }
              Settings = settings
              ConfigContent = config.Content
              ConfigHash = config.Hash }

        let bytes = encode payload
        let chunks = chunksFor state clock
        let checksum = snapshotHash bytes chunks
        use db = openDb path
        let previous = db.Find<CareerSnapshot>(1)
        let previousChunks = db.Table<CareerChunk>().ToList() |> List.ofSeq

        if not (isNull (box previous)) && previous.SlotId <> slot then
            invalidOp $"Snapshot belongs to slot '{previous.SlotId}', not '{slot}'."

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

        if not replace && not (isNull (box previous)) then
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

        let backup = path + ".bak"

        let archivedBackup =
            if replace && File.Exists backup then
                let archived = backup + ".previous-" + Guid.NewGuid().ToString("N")
                // A post-commit backup failure must never leave an older career at the active recovery path.
                File.Move(backup, archived)
                Some archived
            else
                None

        try
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
        with _ ->
            match archivedBackup with
            | Some archived -> File.Move(archived, backup)
            | None -> ()

            reraise ()

        if replace || isNull (box previous) then
            // An initial save also needs a recovery copy before it can be reported as fully protected.
            try
                if replace && replacementBackupFailure.Value then
                    invalidOp "Injected replacement backup failure"

                db.Backup(backup, "main")
            with error ->
                raise (CommittedBackupFailure error)

    let private replaceLegacy slot path state clock settings =

        let oldFormat =
            if File.Exists path then
                try
                    incompatibleSchema path
                with _ ->
                    // Explicit slot replacement may archive an unreadable database too.
                    Some -1
            else
                None

        match oldFormat with
        | None -> saveCore slot path state clock settings true
        | Some _ ->
            let archived = path + ".legacy-" + Guid.NewGuid().ToString("N")
            let backup = path + ".bak"
            let archivedBackup = archived + ".bak"
            let mutable movedPrimary = false

            try
                File.Move(path, archived)
                movedPrimary <- true

                if File.Exists backup then
                    File.Move(backup, archivedBackup)

                saveCore slot path state clock settings true
            with error ->
                if movedPrimary then
                    let failed = path + ".failed-" + Guid.NewGuid().ToString("N")

                    if File.Exists path then
                        File.Move(path, failed)

                    if File.Exists backup then
                        File.Move(backup, failed + ".bak")

                    File.Move(archived, path)

                    if File.Exists archivedBackup then
                        File.Move(archivedBackup, backup)

                match error with
                | CommittedBackupFailure cause -> raise cause
                | _ -> raise error

    let prepareSaveInSlot (slot: string) (state: GameState) (clock: WorldClock) (settings: SaveSettings) replace =
        let path = slotPath slot
        let writer = gates.GetOrAdd(path, fun _ -> SlotWriteGate())
        let sequence = writer.Issue(replace)

        fun () ->
            let started =
                task {
                    do! writer.Semaphore.WaitAsync().ConfigureAwait(false)

                    try
                        if not (writer.Superseded sequence) then
                            // Later changes to a new career may run before its initial replacement command.
                            let effectiveReplace = replace || writer.NeedsReplacement sequence

                            try
                                do!
                                    Task
                                        .Run(fun () ->
                                            if effectiveReplace then
                                                replaceLegacy slot path state clock settings
                                            else
                                                saveCore slot path state clock settings false)
                                        .ConfigureAwait(false)

                                writer.Commit sequence
                            with CommittedBackupFailure cause ->
                                writer.Commit sequence
                                raise cause
                    finally
                        writer.Semaphore.Release() |> ignore
                }

            let id = Interlocked.Increment(&nextWriteId)
            inFlightWrites[id] <- path, started

            started.ContinueWith(
                Action<Task>(fun _ ->
                    let mutable removed: string * Task = path, Task.CompletedTask
                    inFlightWrites.TryRemove(id, &removed) |> ignore),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            )
            |> ignore

            started

    let saveGameInSlotAsync slot state clock settings =
        (prepareSaveInSlot slot state clock settings false) ()

    let replaceGameInSlotAsync slot state clock settings =
        (prepareSaveInSlot slot state clock settings true) ()

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
        { Revision: uint64
          Completion: TaskCompletionSource<AutosaveResult>
          Cancellation: CancellationToken
          Save: unit -> Task<unit> }

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
                        item.Save() |> Async.AwaitTask |> Async.RunSynchronously

                        item.Completion.TrySetResult Saved |> ignore
                with error ->
                    item.Completion.TrySetException error |> ignore

                loop ()

        Task.Run(fun () -> loop ()) |> ignore

    let enqueueAutosave (slot: string) (state: GameState) (clock: WorldClock) (settings: SaveSettings) cancellation =
        let completion =
            TaskCompletionSource<AutosaveResult>(TaskCreationOptions.RunContinuationsAsynchronously)

        let item =
            { Revision = clock.Revision
              Completion = completion
              Cancellation = cancellation
              Save = prepareSaveInSlot slot state clock settings false }

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

    exception private UnsupportedSave of int

    let private loadFile slot path : Result<LoadedCareer, LoadError> =
        try
            match incompatibleSchema path with
            | Some version -> raise (UnsupportedSave version)
            | None -> ()

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
                               [ "player:"; "club:"; "staff:"; "competition:"; "country:"; "match-execution:" ]
                               |> List.exists (fun prefix -> chunk.Key.StartsWith(prefix, StringComparison.Ordinal))
                               || chunk.Key = "datalock:snapshot"
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
                          EngineVersion = dataLock.EngineVersion
                          CareerId = slot
                          ConfirmedRevision = payload.Clock.Revision
                          ConfirmedState = state
                          ConfirmedDate = state.CurrentDate
                          ClockDay = payload.Clock.Current.Day
                          ClockWeek = payload.Clock.Current.Week
                          ClockMonth = payload.Clock.Current.Month
                          ClockSeason = payload.Clock.Current.Season
                          ConfigContent = payload.ConfigContent
                          ConfigHash = payload.ConfigHash
                          DataManifest = manifest dataLock
                          DataLock = dataLock
                          ResolvedDataHash = hash (Encoding.UTF8.GetBytes(JsonCodec.serialize dataLock.Snapshot))
                          ResolvedDataContent = JsonCodec.serialize dataLock.Snapshot }

                    let configCheck =
                        match
                            EffectiveConfig.fromBalance (BalanceResolver.effectiveBalance (Some payload.Settings))
                        with
                        | Ok config when config.Content = payload.ConfigContent && config.Hash = payload.ConfigHash ->
                            Ok()
                        | _ -> Error "Saved effective configuration does not match the career settings"

                    match SaveEnvelope.validate envelope, SaveSettings.validate payload.Settings, configCheck with
                    | Error error, _, _ -> Error(Corrupt(slot, error))
                    | _, Error errors, _ -> Error(Corrupt(slot, String.concat "; " errors))
                    | _, _, Error error -> Error(Corrupt(slot, error))
                    | Ok(), Ok settings, Ok() ->
                        match ModResolution.restoreDataSet dataLock with
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
                                      WorldClock =
                                        { payload.Clock with
                                            MatchExecutions = restoreMatchExecutions chunks }
                                      SaveSettings = settings
                                      ResolvedData = resolved
                                      RecoveredFromBackup = false }
        with
        | UnsupportedSave version -> Error(Incompatible(slot, CurrentSchema, version))
        | error -> Error(Corrupt(slot, error.Message))

    let private restorePrimary slot path =
        let backup = path + ".bak"
        let recovered = path + ".recovered-" + Guid.NewGuid().ToString("N")
        let mutable quarantined = None

        try
            use db = openRead backup
            db.Backup(recovered, "main")

            if File.Exists path then
                // Preserve the damaged primary for diagnosis; never overwrite user data while recovering.
                let damaged = path + ".corrupt-" + Guid.NewGuid().ToString("N")
                File.Move(path, damaged)
                quarantined <- Some damaged

            File.Move(recovered, path)
            Ok()
        with error ->
            match quarantined with
            | Some damaged when not (File.Exists path) ->
                try
                    File.Move(damaged, path)
                with _ ->
                    ()
            | _ -> ()

            Error(Corrupt(slot, "Backup is valid but cannot restore the primary: " + error.Message))

    let loadEnvelope slot : Task<Result<LoadedCareer, LoadError>> =
        task {
            let path = slotPath slot

            if not (File.Exists path || File.Exists(path + ".bak")) then
                return Error(Missing slot)
            else
                let gate = (gateFor slot).Semaphore
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
                                    | Ok result ->
                                        restorePrimary slot path
                                        |> Result.map (fun () ->
                                            { result with
                                                RecoveredFromBackup = true })
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
                                    match incompatibleSchema source with
                                    | Some version -> raise (UnsupportedSave version)
                                    | None -> ()

                                    use db = openRead source

                                    db.Query<CareerSlotHeader>(
                                        "SELECT SlotId, SchemaVersion, GameDate, ClubName, ManagerName, UpdatedAtUtc FROM CareerSnapshot WHERE Id = 1"
                                    )
                                    |> Seq.tryHead

                                try
                                    let entry =
                                        try
                                            readHeader path
                                        with
                                        | UnsupportedSave version -> raise (UnsupportedSave version)
                                        | _ when File.Exists(path + ".bak") -> readHeader (path + ".bak")

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
                                      Status =
                                        match error with
                                        | UnsupportedSave version -> IncompatibleSlot(CurrentSchema, version)
                                        | _ -> CorruptSlot error.Message })
                            |> Seq.sortByDescending _.LastWrittenUtc
                            |> Seq.toList
                            |> Ok)
            with error ->
                return Error error.Message
        }

    let initTablesForSlot slot =
        task {
            Directory.CreateDirectory(storageDirectory ()) |> ignore

            let path = slotPath slot

            if not (File.Exists path || File.Exists(path + ".bak")) then
                use _db = openDb path
                ()

            return ()
        }

    let initTables () = initTablesForSlot DefaultSlot
