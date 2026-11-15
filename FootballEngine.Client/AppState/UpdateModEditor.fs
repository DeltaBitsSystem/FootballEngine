namespace FootballEngine

open FootballEngine.Data
open ModEditorTypes
open AppMsgs

module UpdateModEditor =

    let createEmptyCountry code name =
        { CountryDataDto.Country =
            { Code = code
              Name = name
              Confederation = "UEFA" }
          Names = { FirstNames = []; LastNames = [] }
          LeagueNames = []
          LeagueRules = []
          Clubs = []
          Cups = [] }

    let createEmptyLeague _name =
        { LeagueRulesDto.PointsForWin = 3
          PointsForDraw = 1
          Tiebreakers = [ "GD"; "GS" ]
          Promotion = []
          Relegation = [] }

    let createEmptyClub name level =
        { ClubEntryDto.Name = name
          LeagueLevel = level
          Reputation = Some 5000
          Kit = None
          Players = [] }

    let createEmptyPlayer name position =
        { PlayerEntryDto.Name = name
          Position = position
          Age = 24
          Rating = 60
          Potential = 70
          PreferredFoot = None
          Nationality = None }

    let private positionCodes =
        set [ "GK"; "DC"; "DL"; "DR"; "WBL"; "WBR"; "DM"; "MC"; "ML"; "MR"; "AML"; "AMR"; "AMC"; "ST" ]

    /// One player per line: name, position, age, rating[, potential[, foot[, nationality]]], separated by
    /// tab, semicolon or comma, so a squad copied from any spreadsheet pastes as is. A header row and
    /// malformed lines are skipped and counted rather than guessed.
    let parseSquad (text: string) : PlayerEntryDto list * int =
        let parseLine (line: string) =
            let cells =
                line.Split([| '\t'; ';'; ',' |])
                |> Array.map (fun cell -> cell.Trim())
                |> Array.filter (fun cell -> cell <> "")

            let number index =
                cells
                |> Array.tryItem index
                |> Option.bind (fun cell ->
                    match System.Int32.TryParse cell with
                    | true, value -> Some value
                    | _ -> None)

            match cells |> Array.tryItem 0, cells |> Array.tryItem 1, number 2, number 3 with
            | Some name, Some position, Some age, Some rating when positionCodes.Contains(position.ToUpperInvariant()) ->
                let rating = System.Math.Clamp(rating, 1, 100)

                let foot =
                    cells
                    |> Array.tryItem 5
                    |> Option.bind (fun cell ->
                        match cell.ToLowerInvariant() with
                        | "left" | "izquierdo" | "zurdo" | "l" -> Some "Left"
                        | "right" | "derecho" | "diestro" | "r" -> Some "Right"
                        | "both" | "ambos" | "ambidiestro" -> Some "Both"
                        | _ -> None)

                Some
                    { PlayerEntryDto.Name = name
                      Position = position.ToUpperInvariant()
                      Age = System.Math.Clamp(age, 15, 45)
                      Rating = rating
                      Potential = System.Math.Clamp(number 4 |> Option.defaultValue rating, rating, 100)
                      PreferredFoot = foot
                      Nationality =
                        cells
                        |> Array.tryItem 6
                        |> Option.filter (fun cell -> cell.Length = 3)
                        |> Option.map _.ToUpperInvariant() }
            | _ -> None

        let lines =
            text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.filter (fun line -> line.Trim() <> "")

        let players = lines |> Array.choose parseLine |> List.ofArray
        players, lines.Length - players.Length

    let private snapshot (state: ModEditorState) =
        { Document = state.Document
          SelectedCountryCode = state.SelectedCountryCode }

    /// A new history entry is skipped only while the same field keeps changing; any other edit,
    /// or an undo, starts a fresh step.
    let private record (editKey: string option) (state: ModEditorState) =
        let continuesEdit = editKey.IsSome && editKey = state.LastEditKey

        { state with
            Past =
                if continuesEdit then
                    state.Past
                else
                    snapshot state :: (state.Past |> List.truncate 100)
            Future = []
            LastEditKey = editKey
            IsDirty = true
            Validated = false
            PublishStatus = NotPublished }

    let private restore (item: HistoryItem) (state: ModEditorState) =
        { state with
            Document = item.Document
            SelectedCountryCode = item.SelectedCountryCode
            SelectedClubIndex = None
            LastEditKey = None
            IsDirty = true
            Validated = false
            PublishStatus = NotPublished }

    let undo (state: ModEditorState) : ModEditorState =
        match state.Past with
        | [] -> state
        | head :: tail ->
            { restore head state with
                Past = tail
                Future = snapshot state :: state.Future }

    let redo (state: ModEditorState) : ModEditorState =
        match state.Future with
        | [] -> state
        | head :: tail ->
            { restore head state with
                Past = snapshot state :: state.Past
                Future = tail }

    let private setDocument (document: EditorDocument) (state: ModEditorState) = { state with Document = document }

    let private withCountries countries (state: ModEditorState) =
        setDocument
            { state.Document with
                Countries = countries }
            state

    let private withComps comps (state: ModEditorState) =
        setDocument
            { state.Document with
                InternationalComps = comps }
            state

    /// Drafts are written on every committed change so closing the app never loses work;
    /// continuous typing in one field is written when the field or the tab changes.
    let private saveDraft (state: ModEditorState) =
        if state.HasDocument then
            match ModEditorPackage.saveDraft state.Document with
            | Ok() -> { state with HasDraft = true }
            | Error _ -> state
        else
            state

    let private committed (editKey: string option) (previous: ModEditorState) (next: ModEditorState) =
        if editKey.IsSome && editKey = previous.LastEditKey then
            next
        else
            saveDraft next

    let private openDocument (document: EditorDocument) (state: ModEditorState) =
        let firstCountry = document.Countries |> Map.tryPick (fun code _ -> Some code)

        { state with
            Document = document
            HasDocument = true
            ActiveTab = (if document.Countries.IsEmpty then Details else World)
            ActiveSubTab = General
            SelectedCountryCode = firstCountry
            SelectedClubIndex = None
            Past = []
            Future = []
            LastEditKey = None
            Errors = []
            Validated = false
            IsDirty = false
            PublishStatus = NotPublished }
        |> saveDraft

    let refreshLibrary (state: ModEditorState) =
        let entries, errors = ModEditorPackage.scanLibrary ()

        { state with
            Library = entries
            LibraryError =
                (if errors.IsEmpty then
                     None
                 else
                     Some(String.concat "\n" errors))
            HasDraft = ModEditorPackage.hasDraft () }

    let private editCountry editKey code updater (state: ModEditorState) =
        match state.Document.Countries |> Map.tryFind code with
        | None -> state
        | Some data when updater data = data ->
            // Controls report their value again when the view re-binds them to another entity;
            // an unchanged value must not become an undo step or mark the mod as edited.
            state
        | Some data ->
            let updatedData = updater data
            let nextCode = updatedData.Country.Code

            // Country codes are map keys as well as editable data. A rename that would replace
            // another country is refused, and an overridden country keeps the code its target owns.
            if
                nextCode <> code
                && (state.Document.Countries |> Map.containsKey nextCode
                    || state.Document.Sources |> Map.containsKey code)
            then
                state
            else
                let next = record editKey state

                let nextSelection =
                    match state.SelectedCountryCode with
                    | Some selected when selected = code -> Some nextCode
                    | selected -> selected

                { next with
                    Document =
                        { next.Document with
                            Countries = state.Document.Countries |> Map.remove code |> Map.add nextCode updatedData }
                    SelectedCountryCode = nextSelection }
                |> committed editKey state

    let rec update (msg: ModEditorMsg) (state: ModEditorState) : ModEditorState * Elmish.Cmd<ModEditorMsg> =
        match msg with
        | SetTab tab ->
            let next =
                { state with
                    ActiveTab = tab
                    LastEditKey = None }
                |> saveDraft

            match tab with
            | Start -> refreshLibrary next, Elmish.Cmd.none
            | Publish when not next.Validated -> next, Elmish.Cmd.ofMsg Validate
            | _ -> next, Elmish.Cmd.none

        | SetSubTab subTab ->
            { state with
                ActiveSubTab = subTab
                LastEditKey = None },
            Elmish.Cmd.none

        | UpdateManifest(_, updater) when updater state.Document.Manifest = state.Document.Manifest ->
            state, Elmish.Cmd.none

        | UpdateManifest(field, updater) ->
            let editKey = Some("manifest:" + field)
            let next = record editKey state

            setDocument
                { next.Document with
                    Manifest = updater state.Document.Manifest }
                next
            |> committed editKey state,
            Elmish.Cmd.none

        | SelectCountry code ->
            { state with
                SelectedCountryCode = code
                SelectedClubIndex = None
                PlayerQuery = ""
                LastEditKey = None },
            Elmish.Cmd.none

        | SelectEditorClub index ->
            { state with
                SelectedClubIndex = index
                PlayerQuery = ""
                SquadPasteReport = None
                LastEditKey = None },
            Elmish.Cmd.none

        | AddCountry data ->
            if state.Document.Countries |> Map.containsKey data.Country.Code then
                state, Elmish.Cmd.none
            else
                let next = record None state

                { withCountries (state.Document.Countries |> Map.add data.Country.Code data) next with
                    SelectedCountryCode = Some data.Country.Code
                    SelectedClubIndex = None
                    ActiveSubTab = General }
                |> saveDraft,
                Elmish.Cmd.none

        | UpdateCountry(code, updater) -> editCountry None code updater state, Elmish.Cmd.none

        | EditCountry(code, field, updater) ->
            editCountry (Some $"country:{code}:{field}") code updater state, Elmish.Cmd.none

        | AddCup(code, cup) ->
            editCountry None code (fun data -> { data with Cups = data.Cups @ [ cup ] }) state, Elmish.Cmd.none

        | UpdateCup(code, index, updater) ->
            editCountry
                (Some $"country:{code}:cup:{index}")
                code
                (fun data ->
                    { data with
                        Cups = data.Cups |> List.mapi (fun i c -> if i = index then updater c else c) })
                state,
            Elmish.Cmd.none

        | RemoveCup(code, index) ->
            editCountry
                None
                code
                (fun data ->
                    { data with
                        Cups =
                            data.Cups
                            |> List.indexed
                            |> List.filter (fun (i, _) -> i <> index)
                            |> List.map snd })
                state,
            Elmish.Cmd.none

        | RemoveCountry code ->
            if not (state.Document.Countries |> Map.containsKey code) then
                state, Elmish.Cmd.none
            else
                let next = record None state
                let nextCountries = state.Document.Countries |> Map.remove code

                { next with
                    Document =
                        { next.Document with
                            Countries = nextCountries
                            Sources = next.Document.Sources |> Map.remove code }
                    SelectedCountryCode =
                        if state.SelectedCountryCode = Some code then
                            nextCountries |> Map.tryPick (fun key _ -> Some key)
                        else
                            state.SelectedCountryCode
                    SelectedClubIndex = None }
                |> saveDraft,
                Elmish.Cmd.none

        | AddInternationalComp comp ->
            let next = record None state
            withComps (state.Document.InternationalComps @ [ comp ]) next |> saveDraft, Elmish.Cmd.none

        | UpdateInternationalComp(index, updater) when
            index >= 0
            && index < state.Document.InternationalComps.Length
            && updater state.Document.InternationalComps.[index] = state.Document.InternationalComps.[index]
            ->
            state, Elmish.Cmd.none

        | UpdateInternationalComp(index, updater) ->
            if index >= 0 && index < state.Document.InternationalComps.Length then
                let editKey = Some $"competition:{index}"
                let next = record editKey state
                let current = state.Document.InternationalComps.[index]
                let updated = updater current

                // An overridden competition is found again by name, so a rename moves its source with it.
                let competitionSources =
                    match state.Document.CompetitionSources |> Map.tryFind current.Name with
                    | Some source when updated.Name <> current.Name ->
                        state.Document.CompetitionSources
                        |> Map.remove current.Name
                        |> Map.add updated.Name source
                    | _ -> state.Document.CompetitionSources

                { next with
                    Document =
                        { next.Document with
                            InternationalComps =
                                state.Document.InternationalComps
                                |> List.mapi (fun i c -> if i = index then updated else c)
                            CompetitionSources = competitionSources } }
                |> committed editKey state,
                Elmish.Cmd.none
            else
                state, Elmish.Cmd.none

        | RemoveInternationalComp index ->
            if index >= 0 && index < state.Document.InternationalComps.Length then
                let next = record None state
                let removed = state.Document.InternationalComps.[index]

                { next with
                    Document =
                        { next.Document with
                            InternationalComps =
                                state.Document.InternationalComps
                                |> List.indexed
                                |> List.filter (fun (i, _) -> i <> index)
                                |> List.map snd
                            CompetitionSources = next.Document.CompetitionSources |> Map.remove removed.Name } }
                |> saveDraft,
                Elmish.Cmd.none
            else
                state, Elmish.Cmd.none

        | UndoMod -> undo state |> saveDraft, Elmish.Cmd.none

        | RedoMod -> redo state |> saveDraft, Elmish.Cmd.none

        | Validate ->
            let library =
                if state.Library.IsEmpty then
                    (refreshLibrary state).Library
                else
                    state.Library

            { state with
                Errors = ModEditorPackage.validate state.Document library
                Validated = true
                Library = library },
            Elmish.Cmd.none

        | PublishMod enable ->
            let validated =
                if state.Validated then
                    state
                else
                    fst (update Validate state)

            if not validated.Errors.IsEmpty then
                validated, Elmish.Cmd.none
            else
                match ModEditorPackage.publish validated.Document with
                | Ok path ->
                    refreshLibrary
                        { validated with
                            IsDirty = false
                            PublishStatus = Published(path, enable) },
                    Elmish.Cmd.none
                | Error diagnostics ->
                    { validated with
                        PublishStatus = PublishFailed(diagnostics |> List.map _.Message |> String.concat "; ") },
                    Elmish.Cmd.none

        | SearchMod query -> { state with SearchQuery = query }, Elmish.Cmd.none

        | SearchPlayers query -> { state with PlayerQuery = query }, Elmish.Cmd.none

        | SetSquadPaste text ->
            { state with
                SquadPaste = text
                SquadPasteReport = None },
            Elmish.Cmd.none

        | ImportSquad(code, clubIndex) ->
            let players, skipped = parseSquad state.SquadPaste

            if players.IsEmpty then
                { state with
                    SquadPasteReport = Some $"0/{skipped}" },
                Elmish.Cmd.none
            else
                let next =
                    editCountry
                        None
                        code
                        (fun country ->
                            { country with
                                Clubs =
                                    country.Clubs
                                    |> List.mapi (fun index club ->
                                        if index = clubIndex then
                                            { club with Players = club.Players @ players }
                                        else
                                            club) })
                        state

                { next with
                    SquadPaste = ""
                    SquadPasteReport = Some $"{players.Length}/{players.Length + skipped}" },
                Elmish.Cmd.none

        | RefreshLibrary -> refreshLibrary state, Elmish.Cmd.none

        | NewMod -> openDocument emptyDocument state, Elmish.Cmd.none

        | ResumeDraft ->
            match ModEditorPackage.loadDraft () with
            | Ok document ->
                { openDocument document state with
                    IsDirty = true },
                Elmish.Cmd.none
            | Error error ->
                { state with
                    LibraryError = Some $"The draft could not be read: {error}"
                    HasDraft = false },
                Elmish.Cmd.none

        | OpenPackage(path, asOverride) ->
            match ModEditorPackage.openPackage path asOverride with
            | Ok document -> openDocument document state, Elmish.Cmd.none
            | Error error -> { state with LibraryError = Some error }, Elmish.Cmd.none

        | ImportCountry(path, code) ->
            match ModEditorPackage.importCountry path code with
            | Error error -> { state with LibraryError = Some error }, Elmish.Cmd.none
            | Ok(country, source) ->
                if state.Document.Countries |> Map.containsKey country.Country.Code then
                    { state with
                        SelectedCountryCode = Some country.Country.Code
                        ActiveTab = World },
                    Elmish.Cmd.none
                else
                    let next = record None state

                    { next with
                        Document =
                            { next.Document with
                                Countries = next.Document.Countries |> Map.add country.Country.Code country
                                Sources = next.Document.Sources |> Map.add country.Country.Code source }
                        SelectedCountryCode = Some country.Country.Code
                        SelectedClubIndex = None
                        ActiveTab = World
                        ActiveSubTab = Clubs }
                    |> saveDraft,
                    Elmish.Cmd.none

        | CloseDocument ->
            refreshLibrary
                { saveDraft state with
                    HasDocument = false
                    ActiveTab = Start },
            Elmish.Cmd.none
