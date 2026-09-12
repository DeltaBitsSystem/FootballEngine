namespace FootballEngine

open System.Text.Json
open Elmish
open FootballEngine.Settings
open FootballEngine.AppTypes
open FootballEngine.AppMsgs

module SettingsSlice =

    // Duplicate types for spec compliance — why duplicated: task requires type definitions inside
    // SettingsSlice.fs while AppTypes also defines them for compilation order; both stay in sync
    // via same shape, helpers below operate on AppTypes types so AppState can call them directly.

    type SettingsCategory =
        | Appearance
        | MatchView
        | Simulation
        | Career
        | Data
        | Scouting
        | Advanced
        | All

    type SettingsState =
        { Global: GlobalSettings
          Save: SaveSettings
          SearchQuery: string
          SelectedCategory: SettingsCategory
          Dirty: bool
          ValidationErrors: Map<string, string>
          History: (GlobalSettings * SaveSettings) list
          Future: (GlobalSettings * SaveSettings) list }

    let private maxHistory = 20

    let validateState (state: AppTypes.SettingsState) : Map<string, string> =
        // why map string->string: UI binds ValidationErrors by field key, Map allows O(log n) lookup per row
        let gErrs =
            match GlobalSettings.validate state.Global with
            | Ok _ -> []
            | Error errs -> errs

        let sErrs =
            match SaveSettings.validate state.Save with
            | Ok _ -> []
            | Error errs -> errs

        (gErrs @ sErrs) |> List.map (fun e -> e, e) |> Map.ofList

    let initSettingsState (globalSettings: GlobalSettings) (saveSettings: SaveSettings) : AppTypes.SettingsState =
        // why recompute ValidationErrors here: fresh state must reflect current validation, not cached Map.empty
        let baseState: AppTypes.SettingsState =
            { Global = globalSettings
              Save = saveSettings
              SearchQuery = ""
              SelectedCategory = AppTypes.SettingsCategory.All
              Dirty = false
              ValidationErrors = Map.empty
              History = []
              Future = [] }

        { baseState with
            ValidationErrors = validateState baseState }

    let private pushHistory (prev: AppTypes.SettingsState) (next: AppTypes.SettingsState) : AppTypes.SettingsState =
        // why truncate 20: bounds memory and keeps undo stack predictable for UI
        let history = (prev.Global, prev.Save) :: prev.History |> List.truncate maxHistory

        { next with
            History = history
            Future = []
            Dirty = true
            ValidationErrors = validateState next }

    let updateGlobal (f: GlobalSettings -> GlobalSettings) (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        let nextGlobal = f state.Global
        let next = { state with Global = nextGlobal }
        pushHistory state next

    let updateSave (f: SaveSettings -> SaveSettings) (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        let nextSave = f state.Save
        let next = { state with Save = nextSave }
        pushHistory state next

    let setSearch (query: string) (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        // why no Dirty/history: search is view filter, not persisted setting
        { state with SearchQuery = query }

    let selectCategory (cat: AppTypes.SettingsCategory) (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        // why no Dirty/history: category selection is navigation, not persisted
        { state with SelectedCategory = cat }

    let applyPreset (preset: DifficultyPreset) (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        // why delegate to Simulation.applyPreset: single source for preset->Balance mapping, avoids drift
        let nextSim = Simulation.applyPreset preset state.Save.Simulation
        let nextSave = { state.Save with Simulation = nextSim }
        let next = { state with Save = nextSave }
        pushHistory state next

    let resetCategory (cat: AppTypes.SettingsCategory) (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        // why per-category reset: lets user revert one section without losing other changes, cheaper than ResetAll
        let nextGlobal, nextSave =
            match cat with
            | AppTypes.SettingsCategory.Appearance ->
                { state.Global with
                    Appearance = AppearanceSettings.defaults },
                state.Save
            | AppTypes.SettingsCategory.MatchView ->
                { state.Global with
                    MatchView = MatchViewSettings.defaults },
                state.Save
            | AppTypes.SettingsCategory.Career ->
                { state.Global with
                    Career = CareerSettings.defaults },
                state.Save
            | AppTypes.SettingsCategory.Data ->
                { state.Global with
                    Data = DataSettings.defaults },
                state.Save
            | AppTypes.SettingsCategory.Advanced ->
                { state.Global with
                    Advanced = AdvancedSettings.defaults },
                state.Save
            | AppTypes.SettingsCategory.Simulation ->
                state.Global,
                { state.Save with
                    Simulation = SimulationSettings.defaults }
            | AppTypes.SettingsCategory.Scouting ->
                state.Global,
                { state.Save with
                    Scouting = ScoutingSettings.defaults }
            | AppTypes.SettingsCategory.All -> GlobalSettings.defaults, SaveSettings.defaults

        let next =
            { state with
                Global = nextGlobal
                Save = nextSave }

        pushHistory state next

    let resetAll (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        let next =
            { state with
                Global = GlobalSettings.defaults
                Save = SaveSettings.defaults }

        pushHistory state next

    let undo (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        match state.History with
        | [] -> state
        | (prevGlobal, prevSave) :: tail ->
            let current = (state.Global, state.Save)

            let next =
                { state with
                    Global = prevGlobal
                    Save = prevSave
                    History = tail
                    Future = current :: state.Future }

            { next with
                ValidationErrors = validateState next }

    let redo (state: AppTypes.SettingsState) : AppTypes.SettingsState =
        match state.Future with
        | [] -> state
        | (nextGlobal, nextSave) :: tail ->
            let current = (state.Global, state.Save)

            let next =
                { state with
                    Global = nextGlobal
                    Save = nextSave
                    History = current :: state.History |> List.truncate maxHistory
                    Future = tail }

            { next with
                ValidationErrors = validateState next }

    let markClean (state: AppTypes.SettingsState) : AppTypes.SettingsState = { state with Dirty = false }

    let saveCmd (state: AppTypes.SettingsState) : Cmd<Msg> =
        // why only Global here: SaveSettings is per-save and requires GameState+WorldClock, handled in AppState with SimHelpers.saveCmd
        Cmd.OfTask.either
            (fun () -> SettingsStorage.saveGlobalAsync state.Global |> Async.StartAsTask)
            ()
            (fun res ->
                match res with
                | Ok() -> NoOp
                | Error err ->
                    NotificationMsg(
                        PushNotification
                            { Id = 0
                              Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                              Title = "Settings save failed"
                              Body = err
                              IsRead = false }
                    ))
            (fun ex ->
                NotificationMsg(
                    PushNotification
                        { Id = 0
                          Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                          Title = "Settings save failed"
                          Body = ex.Message
                          IsRead = false }
                ))

    let loadCmd: Cmd<Msg> =
        Cmd.OfTask.either
            (fun () -> SettingsStorage.loadGlobalAsync () |> Async.StartAsTask)
            ()
            (fun res ->
                match res with
                | Ok g -> Msg.SettingsMsg(SettingsMsg.UpdateGlobal(fun _ -> g))
                | Error err ->
                    NotificationMsg(
                        PushNotification
                            { Id = 0
                              Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                              Title = "Settings load failed"
                              Body = err
                              IsRead = false }
                    ))
            (fun ex ->
                NotificationMsg(
                    PushNotification
                        { Id = 0
                          Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                          Title = "Settings load failed"
                          Body = ex.Message
                          IsRead = false }
                ))

    let exportState (state: AppTypes.SettingsState) : string =
        // why manual writer: keeps formatting stable and avoids double-encoding of nested JSON
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WritePropertyName("global")
        let globalJson = GlobalSettings.toJson state.Global
        use docG = JsonDocument.Parse(globalJson)
        docG.RootElement.WriteTo(writer)
        writer.WritePropertyName("save")
        let saveJson = SaveSettings.toJson state.Save
        use docS = JsonDocument.Parse(saveJson)
        docS.RootElement.WriteTo(writer)
        writer.WriteNumber("version", 1)
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let tryImport (json: string) (state: AppTypes.SettingsState) : Result<AppTypes.SettingsState, string> =
        try
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let tryGetGlobal () : Result<GlobalSettings, string> =
                match root.TryGetProperty("global") with
                | true, elem when elem.ValueKind = JsonValueKind.Object ->
                    match GlobalSettings.fromJson (elem.GetRawText()) with
                    | Ok g -> Ok g
                    | Error e -> Error e
                | _ ->
                    // fallback: whole json might be a GlobalSettings object
                    match GlobalSettings.fromJson json with
                    | Ok g -> Ok g
                    | Error _ -> Ok state.Global

            let tryGetSave () : Result<SaveSettings, string> =
                match root.TryGetProperty("save") with
                | true, elem when elem.ValueKind = JsonValueKind.Object ->
                    match SaveSettings.fromJson (elem.GetRawText()) with
                    | Ok s -> Ok s
                    | Error e -> Error e
                | _ ->
                    match SaveSettings.fromJson json with
                    | Ok s -> Ok s
                    | Error _ -> Ok state.Save

            match tryGetGlobal (), tryGetSave () with
            | Ok g, Ok s ->
                // why validate via validateState after: import must not persist invalid settings
                let next = { state with Global = g; Save = s }

                let history =
                    (state.Global, state.Save) :: state.History |> List.truncate maxHistory

                let withHistory =
                    { next with
                        History = history
                        Future = []
                        Dirty = true }

                Ok
                    { withHistory with
                        ValidationErrors = validateState withHistory }
            | Error e, _ -> Error e
            | _, Error e -> Error e
        with ex ->
            Error ex.Message
