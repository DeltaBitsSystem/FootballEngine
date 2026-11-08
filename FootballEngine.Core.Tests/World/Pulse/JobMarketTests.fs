namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Pulse
open FootballEngine.Core.Tests.World

module JobMarketTests =

    let private config = WorldPulseConfig.defaultWorldPulse
    let private firstDay = WorldGenerationTests.startDate.Date
    let private dayAfter n = firstDay.AddDays(float n)

    let private baseWorld () =
        WorldGenerationTests.generate 0xC02UL []

    let private setClub id (f: Club -> Club) (gs: GameState) =
        { gs with
            Clubs = gs.Clubs |> Map.add id (f gs.Clubs[id]) }

    let private aiClubs (gs: GameState) =
        gs.Clubs.Values
        |> Seq.filter (fun c -> c.Id <> gs.UserClubId)
        |> Seq.sortBy _.Id
        |> List.ofSeq

    let private leagueId (gs: GameState) =
        gs.Competitions.Values
        |> Seq.find (fun c ->
            match c.Type with
            | NationalLeague _ -> true
            | _ -> false)
        |> _.Id

    /// Played fixtures for `club` against `rival` on consecutive days starting at `offset + 1`; (own, rival) goals.
    let private addPlayed (club: ClubId) (rival: ClubId) (offset: int) (results: (int * int) list) (gs: GameState) =
        let cid = leagueId gs

        let made =
            results
            |> List.mapi (fun i (own, other) ->
                let id = 950_000 + offset + i
                let isHome = i % 2 = 0
                let home, away = if isHome then club, rival else rival, club
                let hs, aws = if isHome then own, other else other, own

                id,
                { Id = id
                  CompetitionId = cid
                  Round = None
                  HomeClubId = home
                  AwayClubId = away
                  ScheduledDate = firstDay.AddDays(float (1 + offset + i))
                  Played = true
                  HomeScore = Some hs
                  AwayScore = Some aws
                  Events = [] })

        let comp = gs.Competitions[cid]

        { gs with
            Competitions =
                gs.Competitions
                |> Map.add
                    cid
                    { comp with
                        Fixtures = made |> List.fold (fun m (id, f) -> Map.add id f m) comp.Fixtures } },
        made |> List.map fst

    let private processed ids (gs: GameState) =
        { gs with
            PulseProcessed = Set.union gs.PulseProcessed (Set.ofList ids) }

    let private equalReputations (gs: GameState) =
        gs.Clubs
        |> Map.fold (fun acc id _ -> acc |> setClub id (fun c -> { c with Reputation = 5000 })) gs

    let private run date gs = WorldPulse.runWith [] config date gs

    /// The user's club has a board already past the ultimatum and enough matches in charge; one more defeat on
    /// day `config.MinMatchesBeforeSack + 3` should end the job.
    let private onTheBrink () =
        let gs0 = baseWorld () |> equalReputations
        let user = gs0.UserClubId
        let rival = (aiClubs gs0).Head.Id

        let past, ids =
            addPlayed user rival 0 (List.replicate (config.MinMatchesBeforeSack + 1) (0, 2)) gs0

        let past =
            past
            |> processed ids
            |> setClub user (fun c ->
                { c with
                    BoardConfidence = config.SackThreshold })

        let live, _ = addPlayed user rival (config.MinMatchesBeforeSack + 2) [ (0, 2) ] past
        gs0, live, dayAfter (config.MinMatchesBeforeSack + 3)

    let private dismissed () =
        let gs0, live, day = onTheBrink ()
        gs0, run day live, day

    let private dismissalTests =
        testList
            "user dismissal"
            [ testCase "a defeat past the ultimatum ends the job and the club carries on under a coach"
              <| fun _ ->
                  let gs0, after, day = dismissed ()
                  let user = gs0.UserClubId
                  let manager = after.Staff[after.UserStaffId]
                  Expect.isNone (GameState.managedClub after) "no managed club"
                  Expect.equal manager.Status StaffStatus.Unemployed "unemployed"
                  Expect.isNone manager.Contract "no contract"
                  Expect.isNone manager.Attributes.Coaching.Lineup "old lineup dropped"

                  Expect.isLessThan
                      manager.Reputation
                      gs0.Staff[gs0.UserStaffId].Reputation
                      "a sacking costs reputation"

                  Expect.equal after.UserClubId user "the last club is remembered"

                  match after.CareerHistory with
                  | ManagerCareerEvent.ManagerDismissed(on, club, _) :: _ ->
                      Expect.equal (on, club) (day, user) "career history"
                  | other -> failtestf "expected a dismissal, got %A" other

                  let coach = GameState.headCoach user after
                  Expect.isSome coach "the club still has a head coach"
                  Expect.notEqual coach.Value.Id after.UserStaffId "and it is not the user"
                  Expect.isNone (GameState.getUserNextFixture after) "no user fixtures while unemployed"

                  let sacked =
                      after.News
                      |> List.choose (fun n ->
                          match n.Kind with
                          | NewsKind.ManagerSacked(c, m, _) when c = user -> Some(m, n.Importance)
                          | _ -> None)

                  Expect.equal sacked [ after.UserStaffId, NewsImportance.Headline ] "headline news"
                  Expect.isTrue (after.Inbox |> List.exists (fun m -> m.Subject = "Dismissed")) "board letter"
                  Expect.isNonEmpty after.JobOffers "offers arrive the same day"

              testCase "no dismissal on a day the user did not play"
              <| fun _ ->
                  let _, live, day = onTheBrink ()

                  let quiet =
                      live
                      |> processed (live.Competitions.Values |> Seq.collect _.Fixtures.Keys |> List.ofSeq)

                  let quiet =
                      quiet |> setClub quiet.UserClubId (fun c -> { c with BoardConfidence = 0 })

                  let after = run day quiet
                  Expect.isSome (GameState.managedClub after) "still employed"

              testCase "no dismissal before the minimum matches in charge"
              <| fun _ ->
                  let _, live, day = onTheBrink ()

                  let fresh =
                      live
                      |> setClub live.UserClubId (fun c ->
                          { c with
                              ManagerSince = Some(day.AddDays -2.0) })

                  let after = run day fresh
                  Expect.isSome (GameState.managedClub after) "still employed" ]

    let private offerTests =
        testList
            "job offers"
            [ testCase "offers come from reachable boards in crisis, worst first, never from the club that fired him"
              <| fun _ ->
                  let _, after, day = dismissed ()

                  let a, b, c =
                      match aiClubs after with
                      | [ a; b; c ] -> a.Id, b.Id, c.Id
                      | other -> failtestf "expected three AI clubs, got %d" other.Length

                  let gs =
                      { after with
                          JobOffers = []
                          Staff =
                              after.Staff
                              |> Map.add
                                  after.UserStaffId
                                  { after.Staff[after.UserStaffId] with
                                      Reputation = 5000 } }
                      |> setClub a (fun x -> { x with BoardConfidence = 30 })
                      |> setClub b (fun x -> { x with BoardConfidence = 10 })
                      |> setClub c (fun x -> { x with BoardConfidence = 90 })
                      |> setClub after.UserClubId (fun x -> { x with BoardConfidence = 0 })

                  let offered = JobMarket.refresh config false day gs
                  Expect.equal (offered.JobOffers |> List.map _.ClubId) [ b; a ] "crisis clubs, lowest confidence first"
                  let offer = offered.JobOffers.Head
                  Expect.equal offer.Expires (day.AddDays(float config.JobOfferDays)) "expiry"
                  Expect.equal offer.ContractYears config.JobContractYears "contract length"

                  Expect.equal
                      offer.Salary
                      (GameState.headCoach b gs
                       |> Option.bind _.Contract
                       |> Option.map _.Salary
                       |> Option.get)
                      "the salary of the coach he would replace"

                  let manager = gs.Staff[gs.UserStaffId]

                  let outOfReach =
                      gs
                      |> setClub b (fun x ->
                          { x with
                              Reputation = manager.Reputation + config.JobReputationReach + 1 })

                  let offered = JobMarket.refresh config false day outOfReach
                  Expect.equal (offered.JobOffers |> List.map _.ClubId) [ a ] "a club above his standing does not call"

              testCase "an unemployed manager always has somewhere to go"
              <| fun _ ->
                  let _, after, day = dismissed ()

                  let happy =
                      aiClubs after
                      |> List.fold (fun g club -> g |> setClub club.Id (fun x -> { x with BoardConfidence = 90 })) after

                  let happy = { happy with JobOffers = [] }
                  Expect.isNonEmpty (JobMarket.refresh config false day happy).JobOffers "no crisis anywhere"

                  let unknown =
                      { happy with
                          Staff =
                              happy.Staff
                              |> Map.add
                                  happy.UserStaffId
                                  { happy.Staff[happy.UserStaffId] with
                                      Reputation = 0 } }

                  let unknown =
                      aiClubs unknown
                      |> List.fold (fun g club -> g |> setClub club.Id (fun x -> { x with Reputation = 9999 })) unknown

                  Expect.isNonEmpty (JobMarket.refresh config false day unknown).JobOffers "nobody within reach"

              testCase "offers wait on quiet days, expire, and the weekly round respects the cap"
              <| fun _ ->
                  let _, after, day = dismissed ()
                  let first = after.JobOffers
                  let quiet = JobMarket.refresh config false (day.AddDays 1.0) after
                  Expect.equal quiet.JobOffers first "nothing new on a quiet day"

                  let weekly = JobMarket.refresh config true (day.AddDays 1.0) after
                  Expect.isLessThanOrEqual weekly.JobOffers.Length config.MaxJobOffers "cap"

                  Expect.equal
                      (weekly.JobOffers |> List.map _.ClubId |> List.distinct |> List.length)
                      weekly.JobOffers.Length
                      "one offer per club"

                  let late = day.AddDays(float config.JobOfferDays + 1.0)
                  let renewed = JobMarket.refresh config false late after
                  Expect.isNonEmpty renewed.JobOffers "expired offers are replaced"
                  Expect.isTrue (renewed.JobOffers |> List.forall (fun o -> o.Offered = late)) "only fresh offers"

              testCase "declining removes only that offer"
              <| fun _ ->
                  let _, after, day = dismissed ()
                  let after = JobMarket.refresh config true day after
                  let first = after.JobOffers.Head
                  let declined = JobMarket.decline first.Id after
                  Expect.equal declined.JobOffers after.JobOffers.Tail "the rest stay" ]

    let private acceptTests =
        testList
            "accepting a job"
            [ testCase "the career moves to the new club with a fresh squad lineup"
              <| fun _ ->
                  let _, after, day = dismissed ()
                  let offer = after.JobOffers.Head
                  let incumbent = (GameState.headCoach offer.ClubId after).Value

                  match JobMarket.accept config day offer.Id after with
                  | Error e -> failtest e
                  | Ok hired ->
                      Expect.equal (GameState.managedClub hired) (Some offer.ClubId) "managed club"
                      Expect.equal hired.UserClubId offer.ClubId "user club"

                      let coaches =
                          GameState.getStaff offer.ClubId hired
                          |> List.filter (fun s -> s.Role = HeadCoach)

                      Expect.equal (coaches |> List.map _.Id) [ hired.UserStaffId ] "the user is the only head coach"
                      Expect.equal hired.Staff[incumbent.Id].Status StaffStatus.Unemployed "predecessor leaves"
                      Expect.isFalse (List.contains incumbent.Id hired.Clubs[offer.ClubId].StaffIds) "staff list"
                      Expect.isTrue (List.contains hired.UserStaffId hired.Clubs[offer.ClubId].StaffIds) "user listed"
                      let squad = hired.Clubs[offer.ClubId].PlayerIds |> Set.ofList

                      let starters =
                          hired.Staff[hired.UserStaffId].Attributes.Coaching.Lineup.Value.Slots
                          |> List.choose _.PlayerId

                      Expect.equal (starters |> List.distinct |> List.length) 11 "eleven distinct starters"
                      Expect.isTrue (starters |> List.forall squad.Contains) "all from the new squad"
                      Expect.isEmpty hired.JobOffers "offers cleared"
                      Expect.equal hired.Clubs[offer.ClubId].ManagerSince (Some day) "manager since"

                      Expect.equal hired.Clubs[offer.ClubId].BoardConfidence config.HoneymoonConfidence "honeymoon"

                      Expect.equal
                          hired.CareerHistory.Head
                          (ManagerCareerEvent.ManagerHired(day, offer.ClubId))
                          "history"

                      Expect.isTrue
                          (hired.News
                           |> List.exists (fun n -> n.Kind = NewsKind.ManagerAppointed(offer.ClubId, hired.UserStaffId)))
                          "appointment news"

                      Expect.equal (JobMarket.refresh config true day hired).JobOffers [] "no offers once employed"

              testCase "invalid acceptances are refused without changing the career"
              <| fun _ ->
                  let _, after, day = dismissed ()
                  let offer = after.JobOffers.Head
                  Expect.isError (JobMarket.accept config day -1 after) "unknown offer"
                  Expect.isError (JobMarket.accept config (offer.Expires.AddDays 1.0) offer.Id after) "expired"

                  match JobMarket.accept config day offer.Id after with
                  | Ok hired ->
                      let again =
                          { hired with
                              JobOffers = after.JobOffers }

                      Expect.isError (JobMarket.accept config day offer.Id again) "already employed"
                  | Error e -> failtest e ]

    let private schedulerTests =
        testList
            "unemployed world"
            [ testCase "the old club's matches are simulated instead of waiting for the user"
              <| fun _ ->
                  let world = baseWorld ()
                  let balance = FootballEngine.Core.Match.BalanceConfig.defaultConfig
                  let user = world.UserClubId

                  let firstMatch, firstFixture =
                      world.Competitions.Values
                      |> Seq.collect (fun c -> c.Fixtures |> Map.toSeq)
                      |> Seq.filter (fun (_, f) -> MatchFixture.involves user f)
                      |> Seq.minBy (fun (id, f) -> f.ScheduledDate, id)

                  let rec go (clock: WorldClock) (state: GameState) paused =
                      if state.CurrentDate.Date > firstFixture.ScheduledDate.Date then
                          state, paused
                      else
                          match WorldScheduler.executeDayWithBalance (balance, PauseBeforeUserMatch, clock, state) with
                          | Error failure -> failtest failure.Message
                          | Ok day when day.PausedMatch.IsSome -> day.State, day.PausedMatch
                          | Ok day -> go day.Clock day.State paused

                  let _, employedPause = go (WorldClockOps.init world.Season) world None
                  Expect.equal employedPause (Some firstMatch) "an employed manager is waited for"

                  let unemployed = Career.dismiss world.CurrentDate "test" world.UserStaffId world
                  Expect.isNone (GameState.managedClub unemployed) "dismissed"
                  let finished, pause = go (WorldClockOps.init world.Season) unemployed None
                  Expect.isNone pause "nobody waits for an unemployed manager"

                  let played =
                      finished.Competitions.Values
                      |> Seq.pick (fun c -> c.Fixtures |> Map.tryFind firstMatch)

                  Expect.isTrue played.Played "the old club played under its own coach" ]

    let tests =
        testList "JobMarket" [ dismissalTests; offerTests; acceptTests; schedulerTests ]
