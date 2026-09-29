namespace FootballEngine

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open System.Threading
open FootballEngine.Domain
open FootballEngine.Core.Match
open FootballEngine.World
open FootballEngine.Data
open TacticalInstructions
open FootballEngine.Database
open FootballEngine.Database.Mappers
open FootballEngine.Settings
open SQLite

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

module Db =

    let private marketToJson market = JsonCodec.serialize market

    let private marketFromJson (json: string) =
        if String.IsNullOrWhiteSpace json then
            Ok MarketState.empty
        else
            try
                let market = JsonCodec.deserialize<MarketState> json

                if box market = null then
                    Error "Market JSON is empty"
                else
                    Ok market
            with ex ->
                Error $"Market JSON is invalid: {ex.Message}"

    [<Literal>]
    let CurrentSchema = 4

    let private databaseDirectory = new AsyncLocal<string>()

    let private defaultDatabaseDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FootballEngine")

    let private commitFailure = new AsyncLocal<bool>()

    let private currentDatabaseDirectory () =
        if String.IsNullOrWhiteSpace databaseDirectory.Value then
            defaultDatabaseDirectory
        else
            databaseDirectory.Value

    let private slotPath (slot: string) =
        let safeSlot =
            slot
            |> Seq.filter (fun c -> Char.IsLetterOrDigit c || c = '-' || c = '_')
            |> Seq.toArray
            |> String

        Path.Combine(currentDatabaseDirectory (), $"career-{safeSlot}.db3")

    let private backupPath slot = slotPath slot + ".bak"

    /// Test configuration keeps persistence tests on isolated real SQLite files.
    let configureStorageDirectoryForTests (directory: string) = databaseDirectory.Value <- directory

    /// Injects an exception after SQLite writes have entered the transaction.
    /// The production path never enables this flag; tests use it to verify rollback.
    let injectCommitFailureForTests (enabled: bool) = commitFailure.Value <- enabled

    let private connections =
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SQLiteAsyncConnection>>()

    let private connection slot =
        let path = slotPath slot
        connections.GetOrAdd(path, fun key -> lazy (SQLiteAsyncConnection(key))).Value

    let private sha256 (content: string) =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes content)).ToLowerInvariant()

    [<Literal>]
    let DefaultSlot = "career-1"

    let private careerId = DefaultSlot

    [<CLIMutable>]
    type StoredEnvelope =
        { SchemaVersion: int
          EngineVersion: string
          CareerId: string
          ConfirmedRevision: uint64
          ConfirmedDate: DateTime
          ClockJson: string
          ConfigContent: string
          ConfigHash: uint64
          Manifest: SaveManifestEntry list
          DataLock: DataLock
          ResolvedDataHash: string
          ResolvedDataContent: string
          PayloadHash: string }

    type LoadedCareer =
        { Envelope: SaveEnvelope
          State: GameState
          WorldClock: WorldClock
          SaveSettings: SaveSettings
          ResolvedData: ResolvedDataSet }

    let private manifestOfDataLock (dataLock: DataLock) : SaveManifestEntry list =
        dataLock.Packages
        |> List.map (fun package ->
            { Id = package.ModId
              Version = package.Version
              SchemaVersion = package.SchemaVersion
              Dependencies = package.Dependencies |> List.map fst
              Precedence = package.Precedence
              ContentHash = package.PackageSha256 })

    let private resolvedDataContentOfDataLock (dataLock: DataLock) =
        JsonCodec.serialize<DataSnapshot> dataLock.Snapshot

    let private modErrorMessage (error: ModError) =
        error.Diagnostics
        |> List.map (fun diagnostic -> $"{diagnostic.Code}: {diagnostic.Message}")
        |> String.concat "; "

    let private makeStoredEnvelope (slot: string) (state: GameState) (clock: WorldClock) (saveSettings: SaveSettings) =
        let effective =
            EffectiveConfig.fromBalance (BalanceResolver.effectiveBalance (Some saveSettings))
            |> Result.defaultValue EffectiveConfig.defaultConfig

        match DataLock.validate state.DataLock with
        | Error message ->
            raise (InvalidOperationException($"Cannot save a career with an invalid DataLock: {message}"))
        | Ok() ->
            let dataLock = state.DataLock
            let manifest = manifestOfDataLock dataLock
            let resolvedDataContent = resolvedDataContentOfDataLock dataLock
            let resolvedDataHash = sha256 resolvedDataContent
            let clockJson = JsonCodec.serialize clock

            let stored =
                { SchemaVersion = CurrentSchema
                  EngineVersion = "1.0.0"
                  CareerId = slot
                  ConfirmedRevision = clock.Revision
                  ConfirmedDate = state.CurrentDate
                  ClockJson = clockJson
                  ConfigContent = effective.Content
                  ConfigHash = effective.Hash
                  Manifest = manifest
                  DataLock = dataLock
                  ResolvedDataHash = resolvedDataHash
                  ResolvedDataContent = resolvedDataContent
                  PayloadHash =
                    sha256 (
                        clockJson
                        + effective.Content
                        + (JsonCodec.serialize manifest)
                        + (JsonCodec.serialize dataLock)
                        + resolvedDataHash
                        + resolvedDataContent
                    ) }

            stored, JsonCodec.serialize stored

    let private saveGameInSlotCoreAsync
        (slot: string)
        (state: GameState)
        (clock: WorldClock)
        (saveSettings: SaveSettings)
        (replaceExisting: bool)
        =
        task {
            let saveSettingsJson = SaveSettings.toJson saveSettings
            let stored, envelopeJson = makeStoredEnvelope slot state clock saveSettings
            let db = connection slot

            if File.Exists(slotPath slot) then
                File.Copy(slotPath slot, backupPath slot, true)

            do!
                db.RunInTransactionAsync(fun (conn: SQLiteConnection) ->
                    let previous = conn.Find<SaveEnvelopeEntity>(slot)

                    if
                        not replaceExisting
                        && box previous <> null
                        && previous.Revision > int64 clock.Revision
                    then
                        raise (
                            InvalidOperationException(
                                $"Save revision {clock.Revision} is older than stored revision {previous.Revision}"
                            )
                        )

                    conn.InsertOrReplace(
                        { SlotId = slot
                          SchemaVersion = stored.SchemaVersion
                          CareerId = stored.CareerId
                          Revision = int64 stored.ConfirmedRevision
                          ConfirmedDate = stored.ConfirmedDate
                          Phase = "BeforeMatch"
                          EnvelopeJson = envelopeJson
                          PayloadHash = stored.PayloadHash
                          BackupJson = if box previous = null then "" else previous.EnvelopeJson
                          UpdatedAtUtc = DateTime.UtcNow }
                    )
                    |> ignore

                    conn.DeleteAll<LoanEntity>() |> ignore

                    for loan in state.Market.Loans.Values do
                        conn.InsertOrReplace(toLoanEntity loan) |> ignore

                    conn.InsertOrReplace(
                        { GameSaveMeta.Id = 1
                          CurrentDate = state.CurrentDate
                          Season = state.Season
                          TrainingWeeksApplied = state.TrainingWeeksApplied
                          UserClubId = state.UserClubId
                          UserStaffId = state.UserStaffId
                          PrimaryCountry = state.PrimaryCountry
                          NextInboxId = state.NextInboxId
                          ClockDay = clock.Current.Day
                          ClockWeek = clock.Current.Week
                          ClockMonth = clock.Current.Month
                          SaveSettingsJson = saveSettingsJson
                          TeamEmphasis = Serializers.emphasisToString state.TeamEmphasis
                          MarketJson = marketToJson state.Market }
                    )
                    |> ignore

                    conn.Execute("DELETE FROM InboxMessageEntity WHERE GameSaveId = ?", 1) |> ignore

                    for msg in state.Inbox do
                        conn.InsertOrReplace(toInboxMessageEntity 1 msg) |> ignore

                    for club in state.Clubs.Values do
                        conn.InsertOrReplace(toClubEntity club) |> ignore

                    for player in state.Players.Values do
                        conn.InsertOrReplace(toPlayerEntity player) |> ignore

                    for staff in state.Staff.Values do
                        conn.InsertOrReplace(toStaffEntity staff) |> ignore

                    for staff in state.Staff.Values do
                        match staff.Role, staff.Contract, staff.Attributes.Coaching.Lineup with
                        | HeadCoach, Some contract, Some lineup ->
                            let instructions = lineup.Instructions |> Option.defaultValue defaultInstructions

                            conn.Execute("DELETE FROM LineupSlotEntity WHERE ClubId = ?", contract.ClubId)
                            |> ignore

                            for slot in lineup.Slots do
                                conn.Insert(
                                    { LineupSlotEntity.Id = 0
                                      ClubId = contract.ClubId
                                      FormationName = Serializers.formationToString lineup.Formation
                                      TacticsName = Serializers.tacticsToString lineup.Tactics
                                      Mentality = instructions.Mentality
                                      DefensiveLine = instructions.DefensiveLine
                                      PressingIntensity = instructions.PressingIntensity
                                      Width = instructions.Width
                                      Tempo = instructions.Tempo
                                      Directness = instructions.Directness
                                      PressTriggerZone = instructions.PressTriggerZone
                                      DefensiveShape = instructions.DefensiveShape
                                      SlotIndex = slot.Index
                                      Role = string slot.Role
                                      X = slot.X
                                      Y = slot.Y
                                      PlayerId = slot.PlayerId |> Option.defaultValue -1 }
                                )
                                |> ignore
                        | _ -> ()

                    conn.DeleteAll<CompetitionClubEntity>() |> ignore
                    conn.DeleteAll<LeagueStandingEntity>() |> ignore
                    conn.DeleteAll<MatchFixtureEntity>() |> ignore

                    for comp in state.Competitions.Values do
                        conn.InsertOrReplace(toCompetitionEntity comp) |> ignore

                        for clubId in comp.ClubIds do
                            conn.Insert(
                                { CompetitionClubEntity.Id = 0
                                  CompetitionId = comp.Id
                                  ClubId = clubId }
                            )
                            |> ignore

                        for fixture in comp.Fixtures.Values do
                            conn.Insert(toFixtureEntity fixture) |> ignore

                        for standing in comp.Standings.Values do
                            conn.Insert(toStandingEntity comp.Id standing) |> ignore

                        for tie in comp.KnockoutTies.Values do
                            conn.InsertOrReplace(toKnockoutTieEntity comp.Id tie) |> ignore

                    for country in state.Countries.Values do
                        conn.InsertOrReplace(toCountryEntity country) |> ignore

                    if commitFailure.Value then
                        raise (InvalidOperationException("Injected commit failure")))
        }

    let private slotWriteGates =
        System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>()

    let private lastCommittedRevision =
        System.Collections.Concurrent.ConcurrentDictionary<string, uint64>()

    let saveGameInSlotAsync (slot: string) (state: GameState) (clock: WorldClock) (saveSettings: SaveSettings) =
        task {
            let storageKey = slotPath slot
            let gate = slotWriteGates.GetOrAdd(storageKey, fun _ -> new SemaphoreSlim(1, 1))
            do! gate.WaitAsync()

            try
                match lastCommittedRevision.TryGetValue storageKey with
                | true, revision when revision > clock.Revision ->
                    return
                        raise (
                            InvalidOperationException(
                                $"Autosave revision {clock.Revision} is older than committed revision {revision}"
                            )
                        )
                | _ ->
                    do! saveGameInSlotCoreAsync slot state clock saveSettings false
                    lastCommittedRevision[storageKey] <- clock.Revision
            finally
                gate.Release() |> ignore
        }

    let replaceGameInSlotAsync (slot: string) (state: GameState) (clock: WorldClock) (saveSettings: SaveSettings) =
        task {
            let storageKey = slotPath slot
            let gate = slotWriteGates.GetOrAdd(storageKey, fun _ -> new SemaphoreSlim(1, 1))
            do! gate.WaitAsync()

            try
                do! saveGameInSlotCoreAsync slot state clock saveSettings true
                lastCommittedRevision[storageKey] <- clock.Revision
            finally
                gate.Release() |> ignore
        }

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
          SaveSettings: SaveSettings
          Completion: TaskCompletionSource<AutosaveResult>
          Cancellation: CancellationToken }

    let private autosaveGate = obj ()

    let private mutableAutosaveState: AutosaveQueue.State<AutosavePayload> ref =
        ref AutosaveQueue.empty

    let private autosaveWorkers = System.Collections.Generic.HashSet<string>()

    let private runAutosaveWorker (slot: string) =
        let rec loop () =
            let next =
                lock autosaveGate (fun () ->
                    let request, nextState = AutosaveQueue.tryStart slot !mutableAutosaveState
                    mutableAutosaveState := nextState
                    request)

            match next with
            | None -> lock autosaveGate (fun () -> autosaveWorkers.Remove slot |> ignore)
            | Some request ->
                let payload = request.Payload

                if payload.Cancellation.IsCancellationRequested then
                    payload.Completion.TrySetCanceled(payload.Cancellation) |> ignore
                else
                    try
                        saveGameInSlotAsync payload.Slot payload.State payload.Clock payload.SaveSettings
                        |> Async.AwaitTask
                        |> Async.RunSynchronously

                        payload.Completion.TrySetResult Saved |> ignore
                    with ex ->
                        payload.Completion.TrySetException ex |> ignore

                lock autosaveGate (fun () ->
                    mutableAutosaveState := AutosaveQueue.complete request !mutableAutosaveState)

                loop ()

        Task.Run(fun () -> loop ()) |> ignore

    let enqueueAutosave
        (slot: string)
        (state: GameState)
        (clock: WorldClock)
        (saveSettings: SaveSettings)
        (cancellation: CancellationToken)
        : AutosaveHandle =
        let completion =
            TaskCompletionSource<AutosaveResult>(TaskCreationOptions.RunContinuationsAsynchronously)

        let payload =
            { Slot = slot
              Revision = clock.Revision
              State = state
              Clock = clock
              SaveSettings = saveSettings
              Completion = completion
              Cancellation = cancellation }

        let request: AutosaveQueue.Request<AutosavePayload> =
            { Slot = slot
              Revision = clock.Revision
              Payload = payload }

        let decision =
            lock autosaveGate (fun () ->
                let oldPending =
                    !mutableAutosaveState |> fun current -> current.Pending |> Map.tryFind slot

                let decision, nextState = AutosaveQueue.enqueue request !mutableAutosaveState

                match decision, oldPending with
                | AutosaveQueue.Coalesced, Some old -> old.Payload.Completion.TrySetResult Coalesced |> ignore
                | _ -> ()

                mutableAutosaveState := nextState

                if decision <> AutosaveQueue.RejectedStale && autosaveWorkers.Add slot then
                    runAutosaveWorker slot

                decision)

        match decision with
        | AutosaveQueue.RejectedStale ->
            completion.TrySetException(
                InvalidOperationException($"Autosave revision {clock.Revision} is stale for slot '{slot}'")
            )
            |> ignore
        | _ -> ()

        { Completion = completion.Task
          Cancel =
            fun () ->
                lock autosaveGate (fun () ->
                    let cancelled, nextState =
                        AutosaveQueue.cancelPending slot clock.Revision !mutableAutosaveState

                    mutableAutosaveState := nextState

                    if cancelled then
                        completion.TrySetCanceled(cancellation) |> ignore

                    cancelled) }

    let saveGameAsync (state: GameState) (clock: WorldClock) (saveSettings: SaveSettings) =
        saveGameInSlotAsync careerId state clock saveSettings

    // Overload for call sites that don't yet have SaveSettings — uses defaults.
    // Kept for convenience; new code should call saveGameAsync with explicit SaveSettings.
    let saveGameWithSettings state clock saveSettings = saveGameAsync state clock saveSettings

    let private loadTables
        (db: SQLiteAsyncConnection)
        (meta: GameSaveMeta)
        (clock: WorldClock)
        (dataLock: DataLock)
        : Task<Result<GameState * WorldClock * SaveSettings, string>> =
        task {
            try
                if box meta = null then
                    return Error "GameSaveMeta is missing"
                elif
                    meta.ClockDay <> clock.Current.Day
                    || meta.ClockWeek <> clock.Current.Week
                    || meta.ClockMonth <> clock.Current.Month
                    || meta.Season <> clock.Current.Season
                then
                    return Error "GameSaveMeta clock does not match the stored WorldClock"
                else
                    let! playerEntities = db.Table<PlayerEntity>().ToListAsync()

                    let players =
                        playerEntities
                        |> Seq.map toPlayerDomain
                        |> Seq.map (fun p -> p.Id, p)
                        |> Map.ofSeq

                    let! staffEntities = db.Table<StaffEntity>().ToListAsync()
                    let staff = staffEntities |> Seq.map toStaffDomain |> Map.ofSeq

                    let! lineupSlots = db.Table<LineupSlotEntity>().ToListAsync()
                    let! compClubs = db.Table<CompetitionClubEntity>().ToListAsync()
                    let! allFixtures = db.Table<MatchFixtureEntity>().ToListAsync()
                    let! allStandings = db.Table<LeagueStandingEntity>().ToListAsync()
                    let! allTies = db.Table<KnockoutTieEntity>().ToListAsync()
                    let! clubEntities = db.Table<ClubEntity>().ToListAsync()

                    let clubs =
                        clubEntities |> Seq.map (fun ce -> toClubDomain players staff ce) |> Map.ofSeq

                    let lineupByClub =
                        lineupSlots
                        |> List.ofSeq
                        |> List.groupBy (fun s -> s.ClubId)
                        |> List.choose (fun (clubId, slots) ->
                            match slots with
                            | [] -> None
                            | first :: _ ->
                                let formation = Serializers.parseFormation first.FormationName
                                let tactics = Serializers.parseTactics first.TacticsName

                                let instructions =
                                    { Mentality = first.Mentality
                                      DefensiveLine = first.DefensiveLine
                                      PressingIntensity = first.PressingIntensity
                                      Width = first.Width
                                      Tempo = first.Tempo
                                      Directness = first.Directness
                                      PressTriggerZone = first.PressTriggerZone
                                      DefensiveShape = first.DefensiveShape }

                                let slotsList =
                                    slots
                                    |> List.sortBy (fun s -> s.SlotIndex)
                                    |> List.map (fun s ->
                                        { Index = s.SlotIndex
                                          Role = Serializers.parsePosition s.Role
                                          X = s.X
                                          Y = s.Y
                                          PlayerId = if s.PlayerId = -1 then None else Some s.PlayerId })

                                Some(
                                    clubId,
                                    { Formation = formation
                                      Tactics = tactics
                                      Instructions = Some instructions
                                      Slots = slotsList }
                                ))

                    let updatedStaff =
                        lineupByClub
                        |> List.fold
                            (fun (staffMap: Map<StaffId, Staff>) (clubId, lineup) ->
                                let headCoach =
                                    staffMap
                                    |> Map.values
                                    |> Seq.tryFind (fun s ->
                                        s.Role = HeadCoach
                                        && s.Contract |> Option.map (fun c -> c.ClubId) = Some clubId)

                                match headCoach with
                                | Some coach ->
                                    let updatedCoach =
                                        { coach with
                                            Attributes.Coaching.Lineup = Some lineup }

                                    staffMap |> Map.add coach.Id updatedCoach
                                | None -> staffMap)
                            staff

                    let! compEntities = db.Table<CompetitionEntity>().ToListAsync()

                    let competitions =
                        compEntities
                        |> Seq.map (
                            toCompetitionDomain
                                (List.ofSeq compClubs)
                                (List.ofSeq allFixtures)
                                (List.ofSeq allStandings)
                                (List.ofSeq allTies)
                        )
                        |> Map.ofSeq

                    let! countryEntities = db.Table<CountryEntity>().ToListAsync()

                    let countryResults = countryEntities |> Seq.map toCountryDomain |> List.ofSeq

                    match
                        countryResults
                        |> List.tryPick (function
                            | Error error -> Some error
                            | Ok _ -> None)
                    with
                    | Some error -> return Error $"Country data is corrupt: {error}"
                    | None ->
                        let countries =
                            countryResults
                            |> List.choose (function
                                | Ok cd -> Some(cd.Country.Code, cd)
                                | Error _ -> None)
                            |> Map.ofList

                        let! inboxEntities =
                            db.Table<InboxMessageEntity>().Where(fun e -> e.GameSaveId = 1).ToListAsync()

                        let inbox = inboxEntities |> Seq.map fromInboxMessageEntity |> List.ofSeq

                        let saveSettingsResult =
                            if String.IsNullOrWhiteSpace meta.SaveSettingsJson then
                                Error "SaveSettings JSON is missing"
                            else
                                match SaveSettings.fromJson meta.SaveSettingsJson with
                                | Error error -> Error error
                                | Ok settings ->
                                    match SaveSettings.validate settings with
                                    | Ok valid -> Ok valid
                                    | Error errors -> Error(String.concat "; " errors)

                        let teamEmphasisResult =
                            match meta.TeamEmphasis with
                            | "High Press"
                            | "Possession"
                            | "Attack"
                            | "Defense"
                            | "Set Pieces"
                            | "Recovery" -> Ok(Serializers.parseTeamEmphasis meta.TeamEmphasis)
                            | _ -> Error "TeamEmphasis is missing or invalid"

                        let marketResult =
                            if String.IsNullOrWhiteSpace meta.MarketJson then
                                Error "MarketState JSON is missing"
                            else
                                marketFromJson meta.MarketJson

                        match saveSettingsResult, teamEmphasisResult, marketResult with
                        | Ok saveSettings, Ok teamEmphasis, Ok market ->
                            return
                                Ok(
                                    { CurrentDate = meta.CurrentDate
                                      Season = meta.Season
                                      TrainingWeeksApplied = meta.TrainingWeeksApplied
                                      Clubs = clubs
                                      Players = players
                                      Staff = updatedStaff
                                      Competitions = competitions
                                      Countries = countries
                                      DataLock = dataLock
                                      UserClubId = meta.UserClubId
                                      UserStaffId = meta.UserStaffId
                                      PrimaryCountry = meta.PrimaryCountry
                                      Inbox = inbox
                                      NextInboxId = meta.NextInboxId
                                      PendingNegotiations = Map.empty
                                      NextNegotiationId = 1
                                      Market = market
                                      ProfileCache =
                                        players |> Map.map (fun _ p -> Player.profile p ProfileWeightDefaults.defaults)
                                      PlayerExperiences = Map.empty
                                      TeamEmphasis = teamEmphasis
                                      SeasonHistory = []
                                      CareerHistory = [] },
                                    clock,
                                    saveSettings
                                )
                        | Error error, _, _
                        | _, Error error, _
                        | _, _, Error error -> return Error error
            with ex ->
                return Error ex.Message
        }

    let private makeEnvelope
        (slot: string)
        (entity: SaveEnvelopeEntity)
        (stored: StoredEnvelope)
        (state: GameState)
        (clock: WorldClock)
        : Result<SaveEnvelope, string> =
        let phaseResult =
            match entity.Phase with
            | "BeforeMatch" -> Ok BeforeMatch
            | "AfterMatch" -> Ok AfterMatch
            | phase -> Error $"Unknown save phase '{phase}'"

        if entity.SlotId <> slot || entity.CareerId <> slot || stored.CareerId <> slot then
            Error "Save slot identity does not match the stored envelope"
        elif entity.Revision < 0L || uint64 entity.Revision <> stored.ConfirmedRevision then
            Error "Save revision does not match the stored envelope"
        elif stored.ConfirmedDate <> state.CurrentDate then
            Error "Save date does not match the stored GameState"
        elif state.DataLock <> stored.DataLock then
            Error "Save DataLock does not match the stored GameState"
        elif stored.Manifest <> manifestOfDataLock stored.DataLock then
            Error "Save manifest does not match the stored DataLock"
        else
            phaseResult
            |> Result.bind (fun phase ->
                let envelope: SaveEnvelope =
                    { SchemaVersion = stored.SchemaVersion
                      EngineVersion = stored.EngineVersion
                      CareerId = stored.CareerId
                      ConfirmedRevision = stored.ConfirmedRevision
                      ConfirmedState = state
                      ConfirmedDate = stored.ConfirmedDate
                      Phase = phase
                      ClockDay = clock.Current.Day
                      ClockWeek = clock.Current.Week
                      ClockMonth = clock.Current.Month
                      ClockSeason = clock.Current.Season
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
                      ConfigContent = stored.ConfigContent
                      ConfigHash = stored.ConfigHash
                      DataManifest = stored.Manifest
                      DataLock = stored.DataLock
                      ResolvedDataHash = stored.ResolvedDataHash
                      ResolvedDataContent = stored.ResolvedDataContent }

                SaveEnvelope.validate envelope
                |> Result.map (fun () -> envelope)
                |> Result.mapError id)

    let loadEnvelope (slot: string) : Task<Result<LoadedCareer, LoadError>> =
        task {
            let path = slotPath slot

            if not (File.Exists path) then
                return Error(Missing slot)
            else
                try
                    let db = connection slot
                    let! entity = db.Table<SaveEnvelopeEntity>().Where(fun e -> e.SlotId = slot).FirstOrDefaultAsync()

                    if box entity = null then
                        return Error(Missing slot)
                    elif entity.SchemaVersion <> CurrentSchema then
                        return Error(Incompatible(slot, CurrentSchema, entity.SchemaVersion))
                    else
                        let parseStoredEnvelope (json: string) =
                            try
                                let stored = JsonCodec.deserialize<StoredEnvelope> json

                                if box stored = null then
                                    Error "Envelope JSON is empty"
                                elif stored.SchemaVersion <> CurrentSchema then
                                    Error $"schema {stored.SchemaVersion} does not match {CurrentSchema}"
                                elif String.IsNullOrWhiteSpace stored.EngineVersion then
                                    Error "Envelope engine version is missing"
                                elif
                                    String.IsNullOrWhiteSpace stored.ClockJson
                                    || String.IsNullOrWhiteSpace stored.ConfigContent
                                    || String.IsNullOrWhiteSpace stored.ResolvedDataHash
                                    || String.IsNullOrWhiteSpace stored.ResolvedDataContent
                                    || obj.ReferenceEquals(stored.DataLock, null)
                                    || obj.ReferenceEquals(stored.Manifest, null)
                                then
                                    Error "Envelope payload is incomplete"
                                else
                                    let dataLockResult = DataLock.validate stored.DataLock
                                    let expectedResolvedDataContent = resolvedDataContentOfDataLock stored.DataLock

                                    let expectedResolvedDataHash = sha256 stored.ResolvedDataContent

                                    let expectedPayloadHash =
                                        sha256 (
                                            stored.ClockJson
                                            + stored.ConfigContent
                                            + (JsonCodec.serialize stored.Manifest)
                                            + (JsonCodec.serialize stored.DataLock)
                                            + stored.ResolvedDataHash
                                            + stored.ResolvedDataContent
                                        )

                                    if dataLockResult |> Result.isError then
                                        Error "Envelope data lock is invalid"
                                    elif stored.ResolvedDataContent <> expectedResolvedDataContent then
                                        Error "Envelope resolved data content does not match the DataLock snapshot"
                                    elif stored.ResolvedDataHash <> expectedResolvedDataHash then
                                        Error "Envelope resolved data hash does not match"
                                    elif stored.PayloadHash <> expectedPayloadHash then
                                        Error "Envelope payload hash does not match"
                                    else
                                        Ok stored
                            with ex ->
                                Error $"Envelope JSON is invalid: {ex.Message}"

                        let primaryResult = parseStoredEnvelope entity.EnvelopeJson

                        let storedResult =
                            match primaryResult with
                            | Ok stored -> Ok(stored, false)
                            | Error primaryError when not (String.IsNullOrWhiteSpace entity.BackupJson) ->
                                match parseStoredEnvelope entity.BackupJson with
                                | Ok backup -> Ok(backup, true)
                                | Error backupError ->
                                    Error(
                                        Corrupt(
                                            slot,
                                            $"Primary envelope is corrupt: {primaryError}; backup recovery failed: {backupError}"
                                        )
                                    )
                            | Error error ->
                                Error(Corrupt(slot, $"Primary envelope is corrupt: {error}; no backup is available"))

                        match storedResult with
                        | Error error -> return Error error
                        | Ok(stored, recoveredFromBackup) ->
                            let! sourceDb, sourceEntity =
                                if recoveredFromBackup && File.Exists(backupPath slot) then
                                    let backupDb = SQLiteAsyncConnection(backupPath slot)

                                    task {
                                        let! backupEntity =
                                            backupDb
                                                .Table<SaveEnvelopeEntity>()
                                                .Where(fun e -> e.SlotId = slot)
                                                .FirstOrDefaultAsync()

                                        if box backupEntity = null then
                                            return db, entity
                                        else
                                            return backupDb, backupEntity
                                    }
                                else
                                    Task.FromResult(db, entity)

                            if not recoveredFromBackup && stored.PayloadHash <> sourceEntity.PayloadHash then
                                return Error(Corrupt(slot, "Envelope payload hash does not match"))
                            else
                                let clockResult =
                                    try
                                        let clock = JsonCodec.deserialize<WorldClock> stored.ClockJson

                                        if box clock = null then
                                            Error "WorldClock JSON is empty"
                                        else
                                            Ok clock
                                    with ex ->
                                        Error $"WorldClock JSON is invalid: {ex.Message}"

                                match clockResult with
                                | Error error -> return Error(Corrupt(slot, error))
                                | Ok clock ->
                                    match ModSystem.restoreDataSet stored.DataLock with
                                    | Error error ->
                                        return
                                            Error(
                                                Corrupt(
                                                    slot,
                                                    "Saved resolved data is invalid: " + modErrorMessage error
                                                )
                                            )
                                    | Ok resolvedData ->
                                        match ResolvedWorldData.materialize resolvedData with
                                        | Error error ->
                                            return
                                                Error(
                                                    Corrupt(
                                                        slot,
                                                        "Saved resolved sports data is invalid: "
                                                        + modErrorMessage error
                                                    )
                                                )
                                        | Ok _ ->
                                            let! meta = sourceDb.Table<GameSaveMeta>().FirstOrDefaultAsync()
                                            let! tablesResult = loadTables sourceDb meta clock stored.DataLock

                                            match tablesResult with
                                            | Error error -> return Error(Corrupt(slot, error))
                                            | Ok(state, restoredClock, saveSettings) ->
                                                match makeEnvelope slot sourceEntity stored state restoredClock with
                                                | Error error -> return Error(Corrupt(slot, error))
                                                | Ok envelope ->
                                                    lastCommittedRevision[slotPath slot] <- envelope.ConfirmedRevision

                                                    return
                                                        Ok
                                                            { Envelope = envelope
                                                              State = state
                                                              WorldClock = restoredClock
                                                              SaveSettings = saveSettings
                                                              ResolvedData = resolvedData }
                with ex ->
                    return Error(Corrupt(slot, ex.Message))
        }

    let loadEnvelopeWithManifest
        (slot: string)
        (expectedManifest: SaveManifestEntry list)
        : Task<Result<LoadedCareer, LoadError>> =
        task {
            let! result = loadEnvelope slot

            return
                match result with
                | Error error -> Error error
                | Ok loaded when loaded.Envelope.DataManifest <> expectedManifest ->
                    Error(Corrupt(slot, "saved data manifest does not match the installed mod manifest"))
                | Ok loaded -> Ok loaded
        }

    let loadGameInSlot
        (slot: string)
        : Task<Result<GameState * WorldClock * SaveSettings * ResolvedDataSet, LoadError>> =
        task {
            let! result = loadEnvelope slot

            match result with
            | Ok loaded -> return Ok(loaded.State, loaded.WorldClock, loaded.SaveSettings, loaded.ResolvedData)
            | Error error -> return Error error
        }

    let loadGame () : Task<Result<GameState * WorldClock * SaveSettings * ResolvedDataSet, LoadError>> =
        loadGameInSlot DefaultSlot

    let private slotIds () =
        if not (Directory.Exists(currentDatabaseDirectory ())) then
            []
        else
            Directory.EnumerateFiles(currentDatabaseDirectory (), "career-*.db3")
            |> Seq.choose (fun path ->
                let fileName = Path.GetFileNameWithoutExtension path

                if fileName.StartsWith("career-", StringComparison.Ordinal) then
                    let slot = fileName.Substring("career-".Length)
                    if String.IsNullOrWhiteSpace slot then None else Some slot
                else
                    None)
            |> Seq.distinct
            |> Seq.toList

    let private slotInfo (slot: string) : Task<SaveSlotInfo option> =
        task {
            try
                let db = connection slot
                let! entity = db.Table<SaveEnvelopeEntity>().Where(fun e -> e.SlotId = slot).FirstOrDefaultAsync()

                if box entity = null then
                    return None
                else
                    let! meta = db.Table<GameSaveMeta>().FirstOrDefaultAsync()
                    let! managers = db.Table<StaffEntity>().ToListAsync()
                    let! clubs = db.Table<ClubEntity>().ToListAsync()

                    let manager =
                        if box meta = null then
                            None
                        else
                            managers |> Seq.tryFind (fun staff -> staff.Id = meta.UserStaffId)

                    let club =
                        if box meta = null then
                            None
                        else
                            clubs |> Seq.tryFind (fun entry -> entry.Id = meta.UserClubId)

                    let basicClubName = club |> Option.map _.Name
                    let basicManagerName = manager |> Option.map _.Name
                    let basicDate = Some entity.ConfirmedDate

                    if entity.SchemaVersion <> CurrentSchema then
                        return
                            Some
                                { SlotId = slot
                                  ClubName = basicClubName
                                  ManagerName = basicManagerName
                                  GameDate = basicDate
                                  LastWrittenUtc = entity.UpdatedAtUtc
                                  Status = IncompatibleSlot(CurrentSchema, entity.SchemaVersion) }
                    else
                        let! loaded = loadEnvelope slot

                        match loaded with
                        | Ok career ->
                            let clubName =
                                career.State.Clubs |> Map.tryFind career.State.UserClubId |> Option.map _.Name

                            return
                                Some
                                    { SlotId = slot
                                      ClubName = clubName
                                      ManagerName = Some(GameState.userManagerName career.State)
                                      GameDate = Some career.State.CurrentDate
                                      LastWrittenUtc = entity.UpdatedAtUtc
                                      Status = CompatibleSlot }
                        | Error error ->
                            let message =
                                match error with
                                | Missing missing -> $"Save '{missing}' does not exist."
                                | Incompatible(_, expected, actual) ->
                                    $"Save schema {actual} is incompatible with schema {expected}."
                                | Corrupt(_, details) -> details

                            return
                                Some
                                    { SlotId = slot
                                      ClubName = basicClubName
                                      ManagerName = basicManagerName
                                      GameDate = basicDate
                                      LastWrittenUtc = entity.UpdatedAtUtc
                                      Status = CorruptSlot message }
            with ex ->
                return
                    Some
                        { SlotId = slot
                          ClubName = None
                          ManagerName = None
                          GameDate = None
                          LastWrittenUtc = DateTime.MinValue
                          Status = CorruptSlot ex.Message }
        }

    let listSaveSlotsAsync () : Task<Result<SaveSlotInfo list, string>> =
        task {
            try
                let! infos = slotIds () |> List.map slotInfo |> Task.WhenAll

                return
                    infos
                    |> Array.choose id
                    |> Array.sortByDescending _.LastWrittenUtc
                    |> Array.toList
                    |> Ok
            with ex ->
                return Error ex.Message
        }

    let initTablesForSlot (slot: string) =
        task {
            Directory.CreateDirectory(currentDatabaseDirectory ()) |> ignore
            let db = connection slot
            let! _ = db.CreateTableAsync<SaveEnvelopeEntity>()
            let! _ = db.CreateTableAsync<PlayerEntity>()
            let! _ = db.CreateTableAsync<ClubEntity>()
            let! _ = db.CreateTableAsync<LineupSlotEntity>()
            let! _ = db.CreateTableAsync<StaffEntity>()
            let! _ = db.CreateTableAsync<CompetitionEntity>()
            let! _ = db.CreateTableAsync<CompetitionClubEntity>()
            let! _ = db.CreateTableAsync<LeagueStandingEntity>()
            let! _ = db.CreateTableAsync<MatchFixtureEntity>()
            let! _ = db.CreateTableAsync<KnockoutTieEntity>()
            let! _ = db.CreateTableAsync<CountryEntity>()
            let! _ = db.CreateTableAsync<GameSaveMeta>()
            let! _ = db.CreateTableAsync<LoanEntity>()
            let! _ = db.CreateTableAsync<InboxMessageEntity>()
            return ()
        }

    let initTables () = initTablesForSlot careerId
