namespace FootballEngine

open Elmish
open FootballEngine.Domain
open AppTypes
open AppMsgs
open FootballEngine.Generation.WorldGen

module UpdateSetup =

    let private saveCmd = SimHelpers.saveCmd

    let handle (msg: SetupMsg) (state: State) : State * Cmd<Msg> =
        match msg with
        | GoToStep step -> { state with Setup = { state.Setup with Step = step } }, Cmd.none

        | SelectPrimaryCountry code ->
            { state with
                Setup =
                    { state.Setup with
                        SelectedCountry = Some code
                        SecondaryCountries = state.Setup.SecondaryCountries |> List.filter ((<>) code) } },
            Cmd.none

        | ToggleSecondaryCountry code ->
            if state.Setup.SelectedCountry = Some code then
                state, Cmd.none
            else
                let updated =
                    if List.contains code state.Setup.SecondaryCountries then
                        state.Setup.SecondaryCountries |> List.filter ((<>) code)
                    else
                        code :: state.Setup.SecondaryCountries

                { state with Setup = { state.Setup with SecondaryCountries = updated } },
                Cmd.none

        | UpdateManagerName name ->
            { state with Setup = { state.Setup with ManagerName = name } },
            Cmd.none

        | StartNewGame ->
            match state.Setup.SelectedCountry with
            | None -> state, Cmd.none
            | Some primaryCountry ->
                let newGameState =
                    generateNewGame primaryCountry state.Setup.ManagerName state.Setup.SecondaryCountries

                let managerName = GameState.userManagerName newGameState

                { state with
                    Mode = InGame(newGameState, managerEmployment newGameState)
                    CurrentPage = HomePage
                    LogMessages = [ $"Career started by {managerName}" ] },
                saveCmd newGameState state.WorldClock

        | ConfirmClub clubId ->
            match state.Mode with
            | InGame(gs, _) ->
                let updatedStaff =
                    gs.Staff
                    |> Map.tryFind gs.UserStaffId
                    |> Option.map (fun manager ->
                        { manager with
                            Contract =
                                Some
                                    { ClubId = clubId
                                      Salary = 50_000m
                                      ExpiryYear = gs.Season + 3 } })
                    |> Option.map (fun manager -> gs.Staff |> Map.add manager.Id manager)
                    |> Option.defaultValue gs.Staff

                let newGameState =
                    { gs with
                        UserClubId = clubId
                        Staff = updatedStaff }

                let managerName = GameState.userManagerName newGameState

                { state with
                    Mode = InGame(newGameState, managerEmployment newGameState)
                    CurrentPage = HomePage
                    LogMessages = [ $"Career started by {managerName}" ] },
                saveCmd newGameState state.WorldClock
            | _ -> state, Cmd.none
