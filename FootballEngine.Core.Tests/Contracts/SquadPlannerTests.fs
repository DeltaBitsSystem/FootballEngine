namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Domain

/// Spec for the SquadPlanner — the transfer AI that "always buys well".
/// Pins the public API of `Domain/SquadPlanner.fs`.
module SquadPlannerTests =

    let private now = DateTime(2026, 8, 19)

    let private defaultTransfer =
        { BenchWarmerAgeMin = 25
          MinMatchesForSquadPlayer = 10
          MaxOver25BenchWarmers = 2
          OverpayTolerance = 0.25m }

    let private phys =
        { Acceleration = 60
          Pace = 60
          Agility = 60
          Balance = 60
          JumpingReach = 60
          Stamina = 60
          Strength = 60 }

    let private tech =
        { Finishing = 60
          LongShots = 60
          Dribbling = 60
          BallControl = 60
          Passing = 60
          Crossing = 60
          Tackling = 60
          Marking = 60
          Heading = 60
          FreeKick = 60
          Penalty = 60 }

    let private mental =
        { Aggression = 60
          Composure = 60
          Vision = 60
          Positioning = 60
          Bravery = 60
          WorkRate = 60
          Concentration = 60
          Leadership = 60 }

    let private gk =
        { Reflexes = 60
          Handling = 60
          Kicking = 60
          OneOnOne = 60
          AerialReach = 60 }

    /// id, position, age, skill
    let private makePlayer (id: PlayerId) (pos: Position) (age: int) (skill: int) : Player =
        { Id = id
          Name = sprintf "Player %d" id
          Birthday = now.AddYears(-age)
          Nationality = "ES"
          Position = pos
          PreferredFoot = Right
          Height = 180
          Weight = 75
          Physical = phys
          Technical = tech
          Mental = mental
          Goalkeeping = gk
          Condition = 90
          MatchFitness = 90
          Morale = 70
          Status = Available
          CurrentSkill = skill
          PotentialSkill = skill
          Reputation = 40
          Affiliation = FreeAgent
          TrainingSchedule =
            { Focus = TrainingAllRound
              Intensity = TrainingNormal }
          ExperienceModifiers = ExperienceModifiers.defaultModifiers }

    let squadPlannerTests =
        testList
            "FootballEngine.Core.Contracts.SquadPlanner"
            [ test "a young player with few matches is not a bench warmer" {
                  let squad = [ makePlayer 1 ST 20 70; makePlayer 2 ST 20 70 ]

                  let experiences =
                      Map.ofList [ 1, PlayerExperience.empty; 2, PlayerExperience.empty ]

                  Expect.isEmpty
                      (SquadPlanner.benchWarmers defaultTransfer now squad experiences)
                      "young players are projects, not warmers"
              }

              test "a 25+ player who never plays is a bench warmer" {
                  let squad = [ makePlayer 1 ST 29 70; makePlayer 2 ST 28 70; makePlayer 3 ST 27 70 ]

                  let experiences =
                      Map.ofList
                          [ 1, PlayerExperience.empty
                            2, PlayerExperience.empty
                            3, PlayerExperience.empty ]

                  let warmers = SquadPlanner.benchWarmers defaultTransfer now squad experiences
                  Expect.hasLength warmers 3 "all three rot"
              }

              test "a 25+ player with real matches is not a warmer" {
                  let squad = [ makePlayer 1 ST 30 70 ]

                  let experiences =
                      Map.ofList
                          [ 1,
                            { PlayerExperience.empty with
                                TotalMatchesPlayed = 25 } ]

                  Expect.isEmpty (SquadPlanner.benchWarmers defaultTransfer now squad experiences) "plays enough"
              }

              test "players without an experience record count as warmers when old" {
                  let squad = [ makePlayer 1 ST 31 70 ]

                  Expect.hasLength
                      (SquadPlanner.benchWarmers defaultTransfer now squad Map.empty)
                      1
                      "missing record treated as idle"
              }

              test "a position with too many warmers is closed to new signings" {
                  let squad =
                      [ makePlayer 1 ST 29 70
                        makePlayer 2 ST 28 70
                        makePlayer 3 ST 27 70
                        makePlayer 4 MC 22 70 ]

                  let experiences = Map.empty
                  let openPos = SquadPlanner.openPositions defaultTransfer now squad experiences
                  Expect.isFalse (openPos.Contains ST) "ST closed: three warmers"
                  Expect.isTrue (openPos.Contains MC) "MC open: no warmers"
              }

              test "two warmers are tolerated, three are not" {
                  let squad = [ makePlayer 1 ST 29 70; makePlayer 2 ST 28 70 ]

                  let openPos = SquadPlanner.openPositions defaultTransfer now squad Map.empty
                  Expect.isTrue (openPos.Contains ST) "two warmers are within the limit"

                  let squad3 = makePlayer 3 ST 27 70 :: squad
                  let openPos3 = SquadPlanner.openPositions defaultTransfer now squad3 Map.empty
                  Expect.isFalse (openPos3.Contains ST) "three warmers exceed the limit"
              }

              test "refineTargets drops signings for closed positions" {
                  let squad = [ makePlayer 1 ST 29 70; makePlayer 2 ST 28 70; makePlayer 3 ST 27 70 ]
                  let pool = Map.ofList [ 10, makePlayer 10 ST 22 80; 11, makePlayer 11 MC 23 80 ]

                  let targets =
                      [ { PlayerId = 10
                          Priority = 1.0
                          MaxFee = 10_000_000m }
                        { PlayerId = 11
                          Priority = 1.0
                          MaxFee = 10_000_000m } ]

                  let kept =
                      SquadPlanner.refineTargets defaultTransfer now squad Map.empty pool targets

                  Expect.hasLength kept 1 "only the open-position target survives"
                  Expect.equal kept[0].PlayerId 11 "the MC target stays"
              }

              test "urgent positional gaps allow a capped overpay" {
                  let fee = SquadPlanner.cappedFee defaultTransfer (NeedsPosition ST) 10_000_000m
                  Expect.equal fee (10_000_000m * (1.0m + defaultTransfer.OverpayTolerance)) "gap signing may overpay"

                  let upgrade =
                      SquadPlanner.cappedFee defaultTransfer (NeedsQualityUpgrade 65) 10_000_000m

                  Expect.equal upgrade 10_000_000m "upgrade must respect market value"
              } ]
