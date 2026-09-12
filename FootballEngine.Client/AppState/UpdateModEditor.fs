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
          Tiebreakers = [ "GoalDifference"; "GoalsScored" ]
          Promotion = []
          Relegation = [] }

    let createEmptyClub name level =
        { ClubEntryDto.Name = name
          LeagueLevel = level
          Reputation = Some 5000 }

    let pushHistory (state: ModEditorState) : ModEditorState =
        let item =
            { ModManifest = state.Manifest
              Countries = state.Countries
              InternationalComps = state.InternationalComps
              SelectedCountryCode = state.SelectedCountryCode }

        { state with
            Past = item :: (state.Past |> List.truncate 50)
            Future = []
            IsDirty = true
            ExportStatus = NotExported }

    let undo (state: ModEditorState) : ModEditorState =
        match state.Past with
        | [] -> state
        | head :: tail ->
            let current =
                { ModManifest = state.Manifest
                  Countries = state.Countries
                  InternationalComps = state.InternationalComps
                  SelectedCountryCode = state.SelectedCountryCode }

            { state with
                Manifest = head.ModManifest
                Countries = head.Countries
                InternationalComps = head.InternationalComps
                SelectedCountryCode = head.SelectedCountryCode
                Past = tail
                Future = current :: state.Future
                IsDirty = true
                ExportStatus = NotExported }

    let redo (state: ModEditorState) : ModEditorState =
        match state.Future with
        | [] -> state
        | head :: tail ->
            let current =
                { ModManifest = state.Manifest
                  Countries = state.Countries
                  InternationalComps = state.InternationalComps
                  SelectedCountryCode = state.SelectedCountryCode }

            { state with
                Manifest = head.ModManifest
                Countries = head.Countries
                InternationalComps = head.InternationalComps
                SelectedCountryCode = head.SelectedCountryCode
                Past = current :: state.Past
                Future = tail
                IsDirty = true
                ExportStatus = NotExported }

    let update (msg: ModEditorMsg) (state: ModEditorState) : ModEditorState * Elmish.Cmd<ModEditorMsg> =
        let runValidation nextState = nextState, Elmish.Cmd.ofMsg Validate

        match msg with
        | SetTab tab -> { state with ActiveTab = tab }, Elmish.Cmd.none

        | SetSubTab subTab -> { state with ActiveSubTab = subTab }, Elmish.Cmd.none

        | UpdateManifest updater ->
            let newState = pushHistory state

            { newState with
                Manifest = updater state.Manifest }
            |> runValidation

        | SelectCountry code ->
            { state with
                SelectedCountryCode = code },
            Elmish.Cmd.none

        | AddCountry data ->
            if state.Countries |> Map.containsKey data.Country.Code then
                state, Elmish.Cmd.none
            else
                let newState = pushHistory state

                { newState with
                    Countries = state.Countries |> Map.add data.Country.Code data
                    SelectedCountryCode = Some data.Country.Code }
                |> runValidation

        | UpdateCountry(code, updater) ->
            match state.Countries |> Map.tryFind code with
            | Some data ->
                let newState = pushHistory state
                let updatedData = updater data
                let nextCode = updatedData.Country.Code

                // Country codes are map keys as well as editable data. Reject a rename
                // that would silently replace another country and keep the current edit
                // session usable while validation reports other data issues.
                if nextCode <> code && state.Countries |> Map.containsKey nextCode then
                    state, Elmish.Cmd.none
                else
                    let nextCountries =
                        state.Countries |> Map.remove code |> Map.add nextCode updatedData

                    let nextSelection =
                        match state.SelectedCountryCode with
                        | Some selected when selected = code -> Some nextCode
                        | selected -> selected

                    { newState with
                        Countries = nextCountries
                        SelectedCountryCode = nextSelection }
                    |> runValidation
            | None -> state, Elmish.Cmd.none

        | AddCup(code, cup) ->
            match state.Countries |> Map.tryFind code with
            | Some data ->
                let newState = pushHistory state
                let nextData = { data with Cups = data.Cups @ [ cup ] }

                { newState with
                    Countries = state.Countries |> Map.add code nextData }
                |> runValidation
            | None -> state, Elmish.Cmd.none

        | UpdateCup(code, index, updater) ->
            match state.Countries |> Map.tryFind code with
            | Some data ->
                if index >= 0 && index < data.Cups.Length then
                    let newState = pushHistory state

                    let nextData =
                        { data with
                            Cups = data.Cups |> List.mapi (fun i c -> if i = index then updater c else c) }

                    { newState with
                        Countries = state.Countries |> Map.add code nextData }
                    |> runValidation
                else
                    state, Elmish.Cmd.none
            | None -> state, Elmish.Cmd.none

        | RemoveCup(code, index) ->
            match state.Countries |> Map.tryFind code with
            | Some data ->
                if index >= 0 && index < data.Cups.Length then
                    let newState = pushHistory state

                    let nextData =
                        { data with
                            Cups =
                                data.Cups
                                |> List.indexed
                                |> List.filter (fun (i, _) -> i <> index)
                                |> List.map snd }

                    { newState with
                        Countries = state.Countries |> Map.add code nextData }
                    |> runValidation
                else
                    state, Elmish.Cmd.none
            | None -> state, Elmish.Cmd.none


        | RemoveCountry code ->
            if not (state.Countries |> Map.containsKey code) then
                state, Elmish.Cmd.none
            else
                let newState = pushHistory state
                let nextCountries = state.Countries |> Map.remove code

                let nextSelection =
                    if state.SelectedCountryCode = Some code then
                        nextCountries |> Map.tryPick (fun key _ -> Some key)
                    else
                        state.SelectedCountryCode

                { newState with
                    Countries = nextCountries
                    SelectedCountryCode = nextSelection }
                |> runValidation

        | AddInternationalComp comp ->
            let newState = pushHistory state

            { newState with
                InternationalComps = state.InternationalComps @ [ comp ] }
            |> runValidation

        | UpdateInternationalComp(index, updater) ->
            if index >= 0 && index < state.InternationalComps.Length then
                let newState = pushHistory state

                let newComps =
                    state.InternationalComps
                    |> List.mapi (fun i c -> if i = index then updater c else c)

                { newState with
                    InternationalComps = newComps }
                |> runValidation
            else
                state, Elmish.Cmd.none

        | RemoveInternationalComp index ->
            if index >= 0 && index < state.InternationalComps.Length then
                let newState = pushHistory state

                let newComps =
                    state.InternationalComps
                    |> List.indexed
                    |> List.filter (fun (i, _) -> i <> index)
                    |> List.map snd

                { newState with
                    InternationalComps = newComps }
                |> runValidation
            else
                state, Elmish.Cmd.none

        | UndoMod -> undo state |> runValidation

        | RedoMod -> redo state |> runValidation

        | ModEditorMsg.Export ->
            match ModEditorPackage.saveMod ModPaths.modsDir state.Manifest state.Countries state.InternationalComps with
            | Ok() ->
                { state with
                    IsDirty = false
                    ExportStatus = Exported },
                Elmish.Cmd.none
            | Error err ->
                System.Diagnostics.Debug.WriteLine($"Export failed: {err}")

                { state with
                    ExportStatus = ExportFailed err },
                Elmish.Cmd.none

        | Validate ->
            match ModEditorPackage.validateMod state.Manifest state.Countries state.InternationalComps with
            | Ok() -> { state with Errors = [] }, Elmish.Cmd.none
            | Error error ->
                { state with
                    Errors = error.Diagnostics },
                Elmish.Cmd.none

        | SearchMod query -> { state with SearchQuery = query }, Elmish.Cmd.none
