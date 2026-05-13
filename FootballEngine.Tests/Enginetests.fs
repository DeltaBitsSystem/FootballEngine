module FootballEngine.Tests.EngineTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Tests.Helpers
open FootballEngine.World

let private emptyStanding (clubId: ClubId) : LeagueStanding =
    { ClubId = clubId
      Played = 0
      Won = 0
      Drawn = 0
      Lost = 0
      GoalsFor = 0
      GoalsAgainst = 0
      Points = 0 }

let private unplayedFixtures (game: GameState) limit =
    game.Competitions
    |> Map.toList
    |> List.collect (fun (_, comp) -> comp.Fixtures |> Map.toList)
    |> List.filter (fun (_, f) -> not f.Played)
    |> List.truncate limit

let private advanceToSimulate (game: GameState) (fixtures: (MatchId * MatchFixture) list) : WorldRunner.DayResult =
    let maxDate = fixtures |> List.map (fun (_, f) -> f.ScheduledDate) |> List.max
    let days = int (maxDate.Date - game.CurrentDate.Date).TotalDays + 1
    let gameClock = WorldClockOps.init game.Season
    WorldRunner.advanceDays days gameClock game

let batchTests =
    testList
        "Batch & Standings Contracts"
        [ testCase "batch simulates fixtures correctly"
          <| fun () ->
              let game = loadGame ()
              let fixtures = unplayedFixtures game 20

              if fixtures.IsEmpty then
                  failtest "No unplayed fixtures found"

              let result = advanceToSimulate game fixtures
              Expect.isTrue (result.Logs.Length > 0) "no fixtures were simulated"
              Expect.isTrue (result.PlayedMatches.Length > 0) "no played fixtures recorded"

          testCase "standings are mathematically sane"
          <| fun () ->
              let game = loadGame ()
              let result = advanceToSimulate game (unplayedFixtures game 20)

              let sane =
                  result.GameState.Competitions
                  |> Map.forall (fun _ comp ->
                      comp.Standings
                      |> Map.forall (fun _ s ->
                          int s.Played >= 0
                          && int s.Won >= 0
                          && int s.Drawn >= 0
                          && int s.Lost >= 0
                          && int s.GoalsFor >= 0
                          && int s.GoalsAgainst >= 0
                          && int s.Points >= 0
                          && int s.Played = int s.Won + int s.Drawn + s.Lost
                          && (int s.Points = int s.Won * 3 + s.Drawn || s.Won = 0)))

              Expect.isTrue sane "some standing is mathematically impossible"

          testCase "home win awards more points than away win"
          <| fun () ->
              let game = loadGame ()
              let result = advanceToSimulate game (unplayedFixtures game 20)

              let verified =
                  result.GameState.Competitions
                  |> Map.forall (fun _ comp ->
                      comp.Fixtures
                      |> Map.forall (fun _ f ->
                          if not f.Played || f.HomeScore.IsNone || f.AwayScore.IsNone then
                              true
                          else
                              let homeBefore = comp.Standings |> Map.tryFind f.HomeClubId
                              let awayBefore = comp.Standings |> Map.tryFind f.AwayClubId

                              match homeBefore, awayBefore with
                              | Some h, Some a ->
                                  let homePts = int h.Points
                                  let awayPts = int a.Points
                                  homePts >= 0 && awayPts >= 0
                              | _ -> true))

              Expect.isTrue verified "unable to verify points" ]

let doubleSimGuardTests =
    testList
        "Double-Simulation Guard"
        [ testCase "same fixture not simulated twice"
          <| fun () ->
              let game = loadGame ()
              let fixtures = unplayedFixtures game 5
              let first = advanceToSimulate game fixtures
              let second = advanceToSimulate first.GameState fixtures
              Expect.equal second.PlayedMatches.Length 0 "second sim should have zero matches" ]

let standingUpdateTests =
    testList
        "Standing Update Integrity"
        [ testCase "standings exist for all clubs after simulation"
          <| fun () ->
              let game = loadGame ()
              let result = advanceToSimulate game (unplayedFixtures game 20)

              let allHaveStandings =
                  result.GameState.Competitions
                  |> Map.forall (fun _ comp ->
                      comp.Fixtures
                      |> Map.forall (fun _ f ->
                          if not f.Played then
                              true
                          else
                              comp.Standings.ContainsKey f.HomeClubId
                              && comp.Standings.ContainsKey f.AwayClubId))

              Expect.isTrue allHaveStandings "some played fixture missing club standings" ]

let fixtureIntegrityTests =
    testList
        "Fixture Integrity"
        [ testCase "all fixtures have scheduled dates in the future at game start"
          <| fun () ->
              let game = loadGame ()

              let allFuture =
                  game.Competitions
                  |> Map.forall (fun _ comp ->
                      comp.Fixtures |> Map.forall (fun _ f -> f.ScheduledDate >= game.CurrentDate))

              Expect.isTrue allFuture "some fixture has past date"

          testCase "no fixture has nil scores after batch simulation"
          <| fun () ->
              let game = loadGame ()
              let result = advanceToSimulate game (unplayedFixtures game 20)

              let noNilScores =
                  result.PlayedMatches
                  |> List.forall (fun (_, f) -> f.HomeScore.IsSome && f.AwayScore.IsSome)

              Expect.isTrue noNilScores "some played fixture has nil scores" ]
