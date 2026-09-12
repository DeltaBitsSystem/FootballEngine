module FootballEngine.Client.Tests.RoutingTests

open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Routing
open FootballEngine.Settings

let private roundTrip route =
    testCase (Route.toPath route)
    <| fun _ ->
        let path = Route.toPath route
        Expect.equal (Route.fromPath path) (Some route) $"route round-trips through {path}"

let routeTests =
    testList
        "Route paths"
        [ roundTrip Route.Home
          roundTrip (Route.Inbox(InboxRoute(Some 4)))
          roundTrip (Route.Squad(SquadRoute.Detail 12))
          roundTrip (Route.Transfers TransferRoute.IncomingOffers)
          roundTrip (Route.Transfers TransferRoute.Negotiations)
          roundTrip (Route.Transfers(TransferRoute.Negotiation 8))
          roundTrip (Route.Finances(FinanceRoute.Operation "winter/fees"))
          roundTrip (Route.Settings(SettingsRoute.Category SettingsCategory.Simulation))
          roundTrip (Route.Match(3, MatchPhase.Replay)) ]

let setupLoadTests =
    testCase "opening load game lists slots without entering a career"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()
        let ready, _ = AppState.update StartupReady initial
        let state, _ = AppState.update (SetupMsg OpenLoadGame) ready

        Expect.equal state.Mode NoSave "load stays outside a career"
        Expect.equal state.Navigation.Current Route.Setup "load stays on the setup route"
        Expect.equal state.Setup.Step LoadGame "load screen opens"

        match state.Setup.LoadState with
        | LoadingSlots requestId -> Expect.isGreaterThan requestId 0UL "slot listing has a request id"
        | other -> failtestf "expected slot loading, got %A" other

let setupDraftTests =
    testCase "setup selections remain a draft until career creation"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()
        let ready, _ = AppState.update StartupReady initial
        let country, _ = AppState.update (SetupMsg(GoToStep CountrySelection)) ready
        let selected, _ = AppState.update (SetupMsg(SelectPrimaryCountry "ARG")) country
        let named, _ = AppState.update (SetupMsg(UpdateManagerName "Ada Manager")) selected
        let styled, _ = AppState.update (SetupMsg(SelectManagementStyle EmPossession)) named
        let club, _ = AppState.update (SetupMsg(SelectClub "River Plate")) styled
        let summary, _ = AppState.update (SetupMsg(GoToStep Summary)) club
        let canceled, _ = AppState.update (SetupMsg CancelSetup) summary

        Expect.equal summary.Mode NoSave "draft stays outside a career"
        Expect.equal summary.Setup.Step Summary "draft navigation is retained"
        Expect.equal summary.Setup.SelectedCountry (Some "ARG") "country is retained"
        Expect.equal summary.Setup.ManagerName "Ada Manager" "manager is retained"
        Expect.equal summary.Setup.ManagementStyle (Some EmPossession) "style is retained"
        Expect.equal summary.Setup.SelectedClubName (Some "River Plate") "club is retained"
        Expect.equal canceled.Setup initSetupState "cancel clears the draft"
        Expect.equal canceled.ActiveSlot None "cancel leaves no active save"

let modEditorBackTests =
    testCase "mod editor can return to the setup menu without a career"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()
        let ready, _ = AppState.update StartupReady initial
        let editor, _ = AppState.update (Navigate Route.ModEditor) ready
        let returned, _ = AppState.update GoBack editor

        Expect.equal editor.Navigation.Current Route.ModEditor "mod editor route opens"
        Expect.equal returned.Navigation.Current Route.Setup "back returns to setup"
        Expect.equal returned.Mode NoSave "back does not create a career"

let employmentGateTests =
    testCase "employment-gated routes remain unavailable without a contract"
    <| fun _ ->
        Expect.isTrue (Route.canNavigate false Route.Home) "home is always available"
        Expect.isFalse (Route.canNavigate false (Route.Training TrainingRoute.Overview)) "training requires employment"

let routingTests =
    testList
        "Routing"
        [ routeTests
          setupLoadTests
          setupDraftTests
          modEditorBackTests
          employmentGateTests ]
