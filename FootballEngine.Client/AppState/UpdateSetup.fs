namespace FootballEngine

open System
open Elmish
open FootballEngine.Domain
open FootballEngine.Data
open AppTypes
open AppMsgs
open FootballEngine.Routing
open FootballEngine.Settings
open FootballEngine.Generation.WorldGen
open FootballEngine.World

module UpdateSetup =

    let private materializedCountries (state: State) : FootballEngine.Domain.CountryData list =
        match state.ResolvedData with
        | None -> []
        | Some resolved ->
            match ResolvedWorldData.materialize resolved with
            | Ok world -> world.Countries
            | Error _ -> []

    let private selectedCountryData (state: State) : FootballEngine.Domain.CountryData option =
        state.Setup.SelectedCountry
        |> Option.bind (fun code ->
            materializedCountries state
            |> List.tryFind (fun country -> country.Country.Code = code))

    let private setupError (message: string) (state: State) =
        { state with
            ModLoadErrors = [ message ]
            IsProcessing = false },
        Cmd.none

    let private generatedClubId
        (gameState: GameState)
        (countryCode: CountryCode)
        (leagueLevel: int)
        (clubName: string)
        : ClubId option =
        gameState.Competitions
        |> Map.toSeq
        |> Seq.tryPick (fun (_, competition) ->
            match competition.Type, competition.Country with
            | NationalLeague(LeagueLevel level, _), Some code when code = countryCode && level = leagueLevel ->
                competition.ClubIds
                |> List.tryFind (fun clubId ->
                    gameState.Clubs
                    |> Map.tryFind clubId
                    |> Option.exists (fun club -> club.Name = clubName))
            | _ -> None)

    let private assignUserClub (clubId: ClubId) (gameState: GameState) : Result<GameState, string> =
        match gameState.Staff |> Map.tryFind gameState.UserStaffId with
        | None -> Error "The generated manager could not be found."
        | Some manager ->
            let contract =
                manager.Contract
                |> Option.map (fun current -> { current with ClubId = clubId })
                |> Option.defaultValue
                    { ClubId = clubId
                      Salary = 50_000m
                      ExpiryYear = gameState.Season + 3 }

            Ok
                { gameState with
                    UserClubId = clubId
                    Staff =
                        gameState.Staff
                        |> Map.add
                            manager.Id
                            { manager with
                                Contract = Some contract } }

    let private saveSettingsForSetup (state: State) =
        match state.Setup.Difficulty with
        | None -> state.Settings.Save
        | Some preset ->
            { state.Settings.Save with
                Simulation = SimulationSettings.applyPreset preset state.Settings.Save.Simulation }

    let private startCareer (state: State) : State * Cmd<Msg> =
        match state.Setup.SelectedCountry, state.Setup.SelectedLeagueLevel, state.Setup.SelectedClubName with
        | None, _, _ -> setupError "Select a primary country before continuing." state
        | _, None, _ -> setupError "Select a league before continuing." state
        | _, _, None -> setupError "Select a club before creating the career." state
        | Some countryCode, Some leagueLevel, Some clubName ->
            match state.ResolvedData with
            | None -> setupError "World data is unavailable. Reload the installed data and try again." state
            | Some resolved ->
                let managerName = state.Setup.ManagerName.Trim()

                if managerName.Length < 2 then
                    setupError "Enter a manager name before creating the career." state
                else
                    match
                        generateNewGame
                            resolved
                            state.Setup.Seed
                            (DateTime(2026, 7, 1))
                            countryCode
                            managerName
                            state.Setup.SecondaryCountries
                    with
                    | Error error ->
                        let message =
                            error.Diagnostics
                            |> List.map (fun diagnostic -> $"{diagnostic.Code}: {diagnostic.Message}")
                            |> String.concat "\n"

                        setupError message state
                    | Ok generated ->
                        match generatedClubId generated countryCode leagueLevel clubName with
                        | None ->
                            setupError
                                "The selected club is not available in the generated league. Choose another club."
                                state
                        | Some clubId ->
                            match assignUserClub clubId generated with
                            | Error message -> setupError message state
                            | Ok assigned ->
                                let gameState =
                                    { assigned with
                                        TeamEmphasis =
                                            state.Setup.ManagementStyle |> Option.defaultValue assigned.TeamEmphasis }

                                let saveSettings = saveSettingsForSetup state
                                let clock = WorldClockOps.init gameState.Season
                                let requestId = state.PersistenceRequestId + 1UL

                                let nextState =
                                    { state with
                                        Mode = InGame(gameState, managerEmployment gameState)
                                        Navigation =
                                            { Current = Route.Home
                                              Back = []
                                              Forward = [] }
                                        IsProcessing = false
                                        WorldClock = clock
                                        ResolvedData = Some resolved
                                        SaveSettings = saveSettings
                                        Settings = initSettingsState state.Settings.Global saveSettings
                                        ActiveSlot = Some Db.DefaultSlot
                                        PersistenceRequestId = requestId
                                        SaveStatus = Saving requestId
                                        Setup =
                                            { state.Setup with
                                                Step = MainMenu
                                                LoadState = LoadClosed }
                                        ModLoadErrors = []
                                        LogMessages = [ $"Career started by {GameState.userManagerName gameState}" ] }

                                nextState,
                                SimHelpers.replaceSaveCmdWithRequest nextState requestId gameState clock saveSettings

    let handle (msg: SetupMsg) (state: State) : State * Cmd<Msg> =
        match msg with
        | GoToStep step ->
            { state with
                Setup = { state.Setup with Step = step }
                ModLoadErrors = [] },
            Cmd.none

        | SelectPrimaryCountry code ->
            let defaultLeague =
                materializedCountries state
                |> List.tryFind (fun country -> country.Country.Code = code)
                |> Option.bind (fun country ->
                    country.Clubs
                    |> List.filter (fun club -> club.LeagueLevel = 0)
                    |> List.tryHead
                    |> Option.map (fun _ -> 0))

            { state with
                Setup =
                    { state.Setup with
                        SelectedCountry = Some code
                        SelectedLeagueLevel = defaultLeague
                        SelectedClubName = None
                        SecondaryCountries = state.Setup.SecondaryCountries |> List.filter ((<>) code) } },
            Cmd.none

        | SelectLeague level ->
            match selectedCountryData state with
            | Some country ->
                if country.Clubs |> List.exists (fun club -> club.LeagueLevel = level) then
                    { state with
                        Setup =
                            { state.Setup with
                                SelectedLeagueLevel = Some level
                                SelectedClubName = None } },
                    Cmd.none
                else
                    state, Cmd.none
            | None -> state, Cmd.none

        | ToggleSecondaryCountry code ->
            if state.Setup.SelectedCountry = Some code then
                state, Cmd.none
            else
                let updated =
                    if List.contains code state.Setup.SecondaryCountries then
                        state.Setup.SecondaryCountries |> List.filter ((<>) code)
                    else
                        code :: state.Setup.SecondaryCountries

                { state with
                    Setup =
                        { state.Setup with
                            SecondaryCountries = updated } },
                Cmd.none

        | UpdateManagerName name ->
            { state with
                Setup = { state.Setup with ManagerName = name } },
            Cmd.none

        | SelectManagementStyle style ->
            { state with
                Setup =
                    { state.Setup with
                        ManagementStyle = Some style } },
            Cmd.none

        | SelectClub clubName ->
            { state with
                Setup =
                    { state.Setup with
                        SelectedClubName = Some clubName } },
            Cmd.none

        | SelectDifficulty difficulty ->
            { state with
                Setup =
                    { state.Setup with
                        Difficulty = difficulty } },
            Cmd.none

        | OpenLoadGame -> state, Cmd.none

        | CloseLoadGame ->
            { state with
                Setup =
                    { state.Setup with
                        Step = MainMenu
                        LoadState = LoadClosed } },
            Cmd.none

        | CancelSetup ->
            { state with
                Mode = NoSave
                Navigation =
                    { Current = Route.Setup
                      Back = []
                      Forward = [] }
                IsProcessing = false
                Setup = initSetupState
                ActiveSlot = None
                SaveStatus = SaveIdle
                ModLoadErrors = [] },
            Cmd.none

        | StartNewGame -> startCareer state
