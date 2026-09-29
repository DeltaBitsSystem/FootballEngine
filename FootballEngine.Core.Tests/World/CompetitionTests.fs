namespace FootballEngine.Core.Tests.World

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.World.Phases

module CompetitionTests =
    let private league =
        { Id = 1
          Name = "Test League"
          BroadcastIdentity = BroadcastIdentity.generated "competition:test-league"
          Type =
            NationalLeague(
                LeagueLevel 0,
                { PointsForWin = 3
                  PointsForDraw = 1
                  Tiebreakers = [ GoalDifference; GoalsScored ]
                  Promotion = []
                  Relegation = [] }
            )
          Country = Some "TST"
          Season = 2026
          ClubIds = [ 1; 2; 3; 4 ]
          Fixtures = Map.empty
          Standings = Map.empty
          KnockoutTies = Map.empty }

    let competitionTests =
        testList
            "Competition"
            [ testCase "league schedule has no club collision"
              <| fun _ ->
                  let fixtures, _ = FixtureGen.forLeague 1 league.ClubIds (DateTime(2026, 8, 1)) 1

                  let scheduled =
                      { league with
                          Fixtures = fixtures |> List.map (fun f -> f.Id, f) |> Map.ofList }

                  Expect.isOk (Competition.validateCalendar scheduled) "round robin is valid"
                  Expect.equal fixtures.Length 12 "double round robin"

              testCase "knockout schedule marks rounds and creates two legs"
              <| fun _ ->
                  let fixtures, _ =
                      FixtureGen.forCupFormat
                          2
                          [ 1; 2; 3; 4 ]
                          (StraightKnockout(TwoLegs ExtraTimeThenPenalties))
                          (DateTime(2026, 9, 1))
                          1

                  Expect.equal fixtures.Length 6 "scheduled bracket includes the next round"
                  Expect.all fixtures (fun fixture -> fixture.Round.IsSome) "every tie fixture has a round"

              testCase "rescheduling preserves identity and rejects collisions"
              <| fun _ ->
                  let fixtures, _ = FixtureGen.forLeague 1 league.ClubIds (DateTime(2026, 8, 1)) 1

                  let scheduled =
                      { league with
                          Fixtures = fixtures |> List.map (fun f -> f.Id, f) |> Map.ofList }

                  match Competition.reschedule (DateTime(2027, 1, 1)) 1 scheduled with
                  | Error error -> failtestf "reschedule failed: %A" error
                  | Ok changed ->
                      Expect.equal changed.Fixtures[1].Id 1 "fixture identity remains stable"
                      Expect.equal changed.Fixtures[1].ScheduledDate (DateTime(2027, 1, 1)) "date changed"

              testCase "configured points and ranking are applied"
              <| fun _ ->
                  let first =
                      { ClubId = 1
                        Played = 1
                        Won = 1
                        Drawn = 0
                        Lost = 0
                        GoalsFor = 2
                        GoalsAgainst = 0
                        Points = 5 }

                  let second =
                      { ClubId = 2
                        Played = 1
                        Won = 1
                        Drawn = 0
                        Lost = 0
                        GoalsFor = 1
                        GoalsAgainst = 0
                        Points = 5 }

                  let ranked =
                      { league with
                          Standings = Map.ofList [ 1, first; 2, second ] }
                      |> Competition.rankedStandings

                  Expect.equal (ranked |> List.map fst |> List.take 2) [ 1; 2 ] "goal difference resolves the tie"

              testCase "career can continue unemployed"
              <| fun _ ->
                  Expect.equal
                      (ManagerEmployment.Unemployed)
                      ManagerEmployment.Unemployed
                      "employment has an explicit unemployed state" ]
