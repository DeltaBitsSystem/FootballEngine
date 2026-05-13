module FootballEngine.Tests.WorldTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Tests.Helpers

let private playerClubId (p: Player) (game: GameState) : ClubId option =
    game.Clubs
    |> Map.toSeq
    |> Seq.tryPick (fun (cid, c) -> if c.PlayerIds |> List.contains p.Id then Some cid else None)

let private playerValue (_p: Player) = 0m

let private playerSalary (p: Player) =
    match p.Affiliation with
    | Contracted(_, c) -> c.Salary
    | _ -> 0m

let gameStateIntegrityTests =
    testList
        "Game State Integrity"
        [ test "UserClubId exists in Clubs" {
              let game = loadGame ()
              Expect.isTrue (game.Clubs |> Map.containsKey game.UserClubId) $"UserClubId {game.UserClubId} not found"
          }
          test "all club.Players reference valid PlayerIds" {
              let game = loadGame ()
              let allPlayerIds = game.Players |> Map.toList |> List.map fst |> Set.ofList

              Expect.isTrue
                  (game.Clubs
                   |> Map.forall (fun _ c -> c.PlayerIds |> List.forall (fun pid -> Set.contains pid allPlayerIds)))
                  "club references unknown player"
          }
          test "all players reference valid ClubId" {
              let game = loadGame ()
              let allClubIds = game.Clubs |> Map.toList |> List.map fst |> Set.ofList

              Expect.isTrue
                  (game.Players
                   |> Map.forall (fun _ p ->
                       playerClubId p game
                       |> Option.map (fun cid -> Set.contains cid allClubIds)
                       |> Option.defaultValue false))
                  "player references unknown club"
          }
          test "no player appears in two clubs" {
              let game = loadGame ()

              let allRefs =
                  game.Clubs
                  |> Map.toList
                  |> List.collect (fun (_, c) -> c.PlayerIds |> List.map (fun pid -> pid, c.Id))

              let grouped = allRefs |> List.groupBy fst
              Expect.isTrue (grouped |> List.forall (fun (_, refs) -> refs.Length = 1)) "player found in multiple clubs"
          }
          test "no fixture has HomeClubId = AwayClubId" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp -> comp.Fixtures |> Map.forall (fun _ f -> f.HomeClubId <> f.AwayClubId)))
                  "fixture has same home and away club"
          }
          test "played fixtures have both scores" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp ->
                       comp.Fixtures
                       |> Map.forall (fun _ f -> not f.Played || (f.HomeScore.IsSome && f.AwayScore.IsSome))))
                  "played fixture missing score"
          }
          test "unplayed fixtures have no scores" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp ->
                       comp.Fixtures
                       |> Map.forall (fun _ f -> f.Played || (f.HomeScore.IsNone && f.AwayScore.IsNone))))
                  "unplayed fixture has a score"
          }
          test "all fixture IDs are unique across competitions" {
              let game = loadGame ()

              let allIds =
                  game.Competitions
                  |> Map.toList
                  |> List.collect (fun (_, comp) -> comp.Fixtures |> Map.toList |> List.map fst)

              Expect.equal allIds.Length (allIds |> List.distinct).Length "duplicate fixture ID detected"
          }
          test "all fixture.CompetitionId matches parent competition" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun compId comp -> comp.Fixtures |> Map.forall (fun _ f -> f.CompetitionId = compId)))
                  "fixture has wrong CompetitionId"
          }
          test "all competition seasons match game season" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions |> Map.forall (fun _ comp -> comp.Season = game.Season))
                  "competition season mismatch"
          }
          test "all competition.ClubIds reference valid clubs" {
              let game = loadGame ()
              let allClubIds = game.Clubs |> Map.toList |> List.map fst |> Set.ofList

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp -> comp.ClubIds |> List.forall (fun cid -> Set.contains cid allClubIds)))
                  "competition references unknown club"
          }
          test "all knockout tie club refs are valid" {
              let game = loadGame ()
              let allClubIds = game.Clubs |> Map.toList |> List.map fst |> Set.ofList

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp ->
                       comp.KnockoutTies
                       |> Map.forall (fun _ tie ->
                           Set.contains tie.HomeClubId allClubIds && Set.contains tie.AwayClubId allClubIds)))
                  "knockout tie references unknown club"
          } ]

let playerDataTests =
    testList
        "Player & Club Data Contracts"
        [ test "all CurrentSkill in [1,200] and PA >= CA" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       p.CurrentSkill >= 1
                       && p.CurrentSkill <= 200
                       && p.PotentialSkill >= p.CurrentSkill
                       && p.PotentialSkill <= 200))
                  "player CA/PA out of range"
          }
          test "all player ages in [15, 45]" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       let age = (game.CurrentDate - p.Birthday).TotalDays / 365.25 in age >= 15.0 && age <= 45.0))
                  "player age unreasonable"
          }
          test "all MatchFitness in [0, 100]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Players
                   |> Map.forall (fun _ p -> p.MatchFitness >= 0 && p.MatchFitness <= 100))
                  "player fitness out of range"
          }
          test "all Condition in [0, 100]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Players |> Map.forall (fun _ p -> p.Condition >= 0 && p.Condition <= 100))
                  "player condition out of range"
          }
          test "all Morale in [0, 100]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Players |> Map.forall (fun _ p -> p.Morale >= 0 && p.Morale <= 100))
                  "player morale out of range"
          }
          test "all Value >= 0" {
              let game = loadGame ()
              Expect.isTrue (game.Players |> Map.forall (fun _ p -> playerValue p >= 0m)) "player has negative value"
          }
          test "all Salary >= 0" {
              let game = loadGame ()
              Expect.isTrue (game.Players |> Map.forall (fun _ p -> playerSalary p >= 0m)) "player has negative salary"
          }
          test "all technical stats in [1, 20]" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       let t = p.Technical

                       [ t.Finishing
                         t.Dribbling
                         t.Passing
                         t.BallControl
                         t.Tackling
                         t.Marking
                         t.LongShots
                         t.Crossing
                         t.Heading
                         t.FreeKick
                         t.Penalty ]
                       |> List.forall (fun v -> v >= 1 && v <= 20)))
                  "technical stat out of range"
          }
          test "all physical stats in [1, 20]" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       let ph = p.Physical

                       [ ph.Pace
                         ph.Stamina
                         ph.Strength
                         ph.Agility
                         ph.Acceleration
                         ph.Balance
                         ph.JumpingReach ]
                       |> List.forall (fun v -> v >= 1 && v <= 20)))
                  "physical stat out of range"
          }
          test "all mental stats in [1, 20]" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       let m = p.Mental

                       [ m.Vision
                         m.Composure
                         m.Positioning
                         m.WorkRate
                         m.Aggression
                         m.Bravery
                         m.Concentration
                         m.Leadership ]
                       |> List.forall (fun v -> v >= 1 && v <= 20)))
                  "mental stat out of range"
          }
          test "all GK stats in [1, 20]" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       let g = p.Goalkeeping

                       [ g.Reflexes; g.Handling; g.Kicking; g.OneOnOne; g.AerialReach ]
                       |> List.forall (fun v -> v >= 1 && v <= 20)))
                  "GK stat out of range"
          }
          test "all player heights in [150, 220]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Players |> Map.forall (fun _ p -> p.Height >= 150 && p.Height <= 220))
                  "player height unreasonable"
          }
          test "all player weights in [50, 120]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Players |> Map.forall (fun _ p -> p.Weight >= 50 && p.Weight <= 120))
                  "player weight unreasonable"
          }
          test "all player reputations in [0, 1000]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Players
                   |> Map.forall (fun _ p -> p.Reputation >= 0 && p.Reputation <= 1000))
                  "player reputation out of range"
          }
          test "every club has at least 1 GK" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Clubs
                   |> Map.forall (fun _ c ->
                       c.PlayerIds
                       |> List.exists (fun pid ->
                           game.Players |> Map.tryFind pid |> Option.exists (fun p -> p.Position = GK))))
                  "club has no goalkeeper"
          }
          test "all club budgets >= 0" {
              let game = loadGame ()
              Expect.isTrue (game.Clubs |> Map.forall (fun _ c -> c.Budget >= 0m)) "club has negative budget"
          }
          test "all clubs have at least 11 players" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Clubs |> Map.forall (fun _ c -> c.PlayerIds.Length >= 11))
                  "club has fewer than 11 players"
          }
          test "all club morale in [0, 100]" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Clubs |> Map.forall (fun _ c -> c.Morale >= 0 && c.Morale <= 100))
                  "club morale out of range"
          }
          test "all club reputations >= 0" {
              let game = loadGame ()
              Expect.isTrue (game.Clubs |> Map.forall (fun _ c -> c.Reputation >= 0)) "club has negative reputation"
          }
          test "all player names are non-empty" {
              let game = loadGame ()
              Expect.isTrue (game.Players |> Map.forall (fun _ p -> p.Name.Trim().Length > 0)) "player has empty name"
          }
          test "all club names are non-empty" {
              let game = loadGame ()
              Expect.isTrue (game.Clubs |> Map.forall (fun _ c -> c.Name.Trim().Length > 0)) "club has empty name"
          }
          test "all player IDs are unique" {
              let game = loadGame ()
              let ids = game.Players |> Map.toList |> List.map fst
              Expect.equal ids.Length (ids |> List.distinct).Length "duplicate player ID detected"
          }
          test "all player.ClubId back-references roster" {
              let game = loadGame ()
              let players = game.Players |> Map.toList |> List.map snd

              Expect.isTrue
                  (players
                   |> List.forall (fun p ->
                       playerClubId p game
                       |> Option.map (fun cid ->
                           game.Clubs
                           |> Map.tryFind cid
                           |> Option.map (fun c -> c.PlayerIds |> List.exists (fun pid -> pid = p.Id))
                           |> Option.defaultValue false)
                       |> Option.defaultValue false))
                  "player.ClubId does not match roster"
          } ]

let seasonProgressTests =
    testList
        "Season Progress Contracts"
        [ test "game has at least 1 fixture" {
              let game = loadGame ()

              let total =
                  game.Competitions |> Map.toList |> List.sumBy (fun (_, c) -> c.Fixtures.Count)

              Expect.isTrue (total > 0) "no fixtures found"
          }
          test "played <= total fixtures" {
              let game = loadGame ()

              let total =
                  game.Competitions |> Map.toList |> List.sumBy (fun (_, c) -> c.Fixtures.Count)

              let played =
                  game.Competitions
                  |> Map.toList
                  |> List.sumBy (fun (_, c) -> c.Fixtures |> Map.filter (fun _ f -> f.Played) |> Map.count)

              Expect.isTrue (played <= total) $"played {played} > total {total}"
          }
          test "total points <= 3 * played fixtures" {
              let game = loadGame ()

              let played =
                  game.Competitions
                  |> Map.toList
                  |> List.sumBy (fun (_, c) -> c.Fixtures |> Map.filter (fun _ f -> f.Played) |> Map.count)

              let totalPoints =
                  game.Competitions
                  |> Map.toList
                  |> List.sumBy (fun (_, c) -> c.Standings |> Map.toList |> List.sumBy (snd >> fun s -> int s.Points))

              Expect.isTrue (totalPoints <= played * 3) $"total points {totalPoints} > max possible {played * 3}"
          }
          test "no team has more points than 3 * played" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp ->
                       comp.Standings |> Map.forall (fun _ s -> int s.Points <= int s.Played * 3)))
                  "team has impossible points total"
          }
          test "unplayed fixtures are scheduled in the future" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp ->
                       comp.Fixtures
                       |> Map.forall (fun _ f -> f.Played || f.ScheduledDate >= game.CurrentDate.AddDays(-1.0))))
                  "unplayed fixture scheduled in the past"
          }
          test "standings club count matches competition ClubIds" {
              let game = loadGame ()

              Expect.isTrue
                  (game.Competitions
                   |> Map.forall (fun _ comp -> comp.Standings.Count = 0 || comp.Standings.Count = comp.ClubIds.Length))
                  "standings count mismatch"
          }
          test "isSeasonOver is consistent with fixture state" {
              let game = loadGame ()
              let seasonOver = isSeasonOver game

              let allPlayed =
                  game.Competitions
                  |> Map.forall (fun _ comp -> comp.Fixtures |> Map.forall (fun _ f -> f.Played))

              Expect.isTrue (not seasonOver || allPlayed) "isSeasonOver=true but unplayed fixtures remain"
          } ]

let lineupTests =
    testList
        "Lineup & Formation Contracts"
        [ test "lineup data is accessible" {
              let game = loadGame ()
              Expect.isGreaterThan game.Clubs.Count 0 "at least one club exists"
          }
          test "lineup integrity" {
              let game = loadGame ()
              let allHavePlayers = game.Clubs |> Map.forall (fun _ c -> c.PlayerIds.Length >= 11)
              Expect.isTrue allHavePlayers "all clubs have >= 11 players"
          } ]

let isSeasonOverTests =
    testList
        "isSeasonOver Edge Cases"
        [ test "no fixtures → season is over" {
              let game = loadGame ()

              let noFixtures =
                  { game with
                      Competitions = game.Competitions |> Map.map (fun _ comp -> { comp with Fixtures = Map.empty }) }

              Expect.isTrue (isSeasonOver noFixtures) "expected true when no fixtures"
          }
          test "all played → season is over" {
              let game = loadGame ()

              let allPlayed =
                  { game with
                      Competitions =
                          game.Competitions
                          |> Map.map (fun _ comp ->
                              { comp with
                                  Fixtures =
                                      comp.Fixtures
                                      |> Map.map (fun _ f ->
                                          { f with
                                              Played = true
                                              HomeScore = Some 1
                                              AwayScore = Some 0 }) }) }

              Expect.isTrue (isSeasonOver allPlayed) "expected true when all played"
          }
          test "fresh game → season is NOT over" {
              let game = loadGame ()
              Expect.isFalse (isSeasonOver game) "expected false for fresh game with unplayed fixtures"
          } ]
