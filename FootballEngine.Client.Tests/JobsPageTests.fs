module FootballEngine.Client.Tests.JobsPageTests

open System
open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.Pages
open FootballEngine.Routing
open FootballEngine.World.Pulse

let private career () =
    let country =
        """{"id":"test.jobs:country/tst","kind":"country","schema":1,"code":"TST","confederation":"UEFA","nameKey":"test.jobs:country/tst.name","domain":{"Country":{"Code":"TST","Name":"Testland","Confederation":"UEFA"},"Names":{"FirstNames":["Alex","Sam"],"LastNames":["Stone","River"]},"LeagueNames":["Test League"],"LeagueRules":[{"PointsForWin":3,"PointsForDraw":1,"Tiebreakers":["GD","GS"],"Promotion":[],"Relegation":[]}],"Clubs":[{"Name":"North FC","LeagueLevel":0,"Reputation":7000},{"Name":"South FC","LeagueLevel":0,"Reputation":6500},{"Name":"East FC","LeagueLevel":0,"Reputation":6000},{"Name":"West FC","LeagueLevel":0,"Reputation":6000}],"Cups":[]}}"""

    let package: ModPackage =
        { Manifest =
            { Id = ModId "test.jobs"
              Version = "1.0.0"
              Dependencies = []
              Priority = 0
              Before = []
              After = [] }
          PackageSha256 = "test-jobs"
          SportsContent = Map.ofList [ ContentId "test.jobs:country/tst", country ]
          Translations = Map.empty
          Assets = Map.empty
          Patches = [] }

    let resolved =
        match
            ModResolution.resolve
                [ { RelativePath = "test-jobs.femod"
                    Package = package } ]
        with
        | Ok value -> value
        | Error error -> failtestf "Cannot resolve career: %A" error

    match WorldGen.generateNewGame resolved 0xC02UL (DateTime(2026, 7, 1)) "TST" "Jobs Manager" [] with
    | Ok value -> value
    | Error error -> failtestf "Cannot generate career: %A" error

/// A dismissed manager with the week's offers, sitting on a club page that no longer belongs to him.
let private unemployedOnSquadPage () =
    let gs = career ()
    let gs = Career.dismiss gs.CurrentDate "test" gs.UserStaffId gs
    let gs = JobMarket.refresh WorldPulseConfig.defaultWorldPulse true gs.CurrentDate gs

    { AppTypes.initialState () with
        Mode = InGame(gs, managerEmployment gs)
        Navigation =
            { Current = Route.Squad SquadRoute.All
              Back = []
              Forward = [] } },
    gs

let tests =
    testList
        "Jobs page"
        [ testCase "a dismissed career is unemployed, gated from club pages and has offers"
          <| fun _ ->
              let state, gs = unemployedOnSquadPage ()

              match state.Mode with
              | InGame(_, NotEmployed) -> ()
              | other -> failtestf "expected NotEmployed, got %A" other

              Expect.isNonEmpty gs.JobOffers "offers to choose from"

              for route in
                  [ Route.Squad SquadRoute.All
                    Route.Tactics TacticsRoute.Overview
                    Route.Transfers TransferRoute.Market
                    Route.Finances FinanceRoute.Overview
                    Route.Board
                    Route.Calendar ] do
                  Expect.isFalse (Route.canNavigate false route) $"{Route.toPath route} needs a club"

              for route in [ Route.Home; Route.News; Route.Inbox(InboxRoute None); Route.Competitions ] do
                  Expect.isTrue (Route.canNavigate false route) $"{Route.toPath route} stays open"

          testCase "accepting an offer puts the career back in a club and returns home"
          <| fun _ ->
              let state, gs = unemployedOnSquadPage ()
              let offer = gs.JobOffers.Head
              let next, _ = AppState.update (CareerMsg(AcceptJobOffer offer.Id)) state

              match next.Mode with
              | InGame(hired, Employed club) ->
                  Expect.equal club offer.ClubId "employed by the offering club"
                  Expect.equal hired.UserClubId offer.ClubId "user club follows"
                  Expect.equal (Some next.SelectedLeagueId) (SimHelpers.leagueOf hired offer.ClubId) "its league"
              | other -> failtestf "expected Employed, got %A" other

              Expect.equal next.Navigation.Current Route.Home "home"
              Expect.isNonEmpty next.Notifications "the appointment is announced"

          testCase "declining keeps the job hunt going without that club"
          <| fun _ ->
              let state, gs = unemployedOnSquadPage ()
              let offer = gs.JobOffers.Head
              let next, _ = AppState.update (CareerMsg(DeclineJobOffer offer.Id)) state

              match next.Mode with
              | InGame(after, NotEmployed) ->
                  Expect.isFalse (after.JobOffers |> List.exists (fun o -> o.Id = offer.Id)) "offer gone"
                  Expect.equal after.JobOffers.Length (gs.JobOffers.Length - 1) "the others stay"
              | other -> failtestf "expected NotEmployed, got %A" other

          testCase "every career event has a line in both languages"
          <| fun _ ->
              let gs = career ()
              let club = gs.UserClubId
              let day = gs.CurrentDate

              let before = Localization.active ()

              try
                  for locale in [ Locale.Es; Locale.En ] do
                      Localization.setActive locale

                      for event in
                          [ ManagerCareerEvent.ManagerHired(day, club)
                            ManagerCareerEvent.ManagerDismissed(day, club, "x")
                            ManagerCareerEvent.ManagerResigned(day, club)
                            ManagerCareerEvent.ObjectiveReviewed(day, club, true, "x")
                            ManagerCareerEvent.ObjectiveReviewed(day, club, false, "x") ] do
                          let _, text = Jobs.careerLine gs event
                          Expect.stringContains text gs.Clubs[club].Name $"{event} names the club"
                          Expect.isFalse (text.Contains "{") $"{event} has no unfilled placeholder"
              finally
                  Localization.setActive before ]
