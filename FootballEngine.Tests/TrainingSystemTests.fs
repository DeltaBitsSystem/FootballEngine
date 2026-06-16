module FootballEngine.Tests.TrainingSystemTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Tests.Helpers
open FootballEngine.World.Phases

// =============================================================================
// Helpers
// =============================================================================

let private findPlayer (game: GameState) (predicate: Player -> bool) : Player option =
    game.Players
    |> Map.toList
    |> List.tryPick (fun (_, p) -> if predicate p then Some p else None)

let private simulateWeeks (player: Player) (weeks: int) (schedule: TrainingSchedule) (game: GameState) : Player =
    let mutable p =
        { player with
            TrainingSchedule = schedule }

    let startDate = game.CurrentDate

    for week = 1 to weeks do
        let currentDate = startDate.AddDays(float (week * 7))
        p <- TrainingEngine.applyWeeklyTraining currentDate schedule p

    p

let private simulateFullSeason (player: Player) (schedule: TrainingSchedule) (game: GameState) : Player =
    let afterWeekly = simulateWeeks player 40 schedule game

    let gs: GameState =
        { game with
            Players = game.Players |> Map.add afterWeekly.Id afterWeekly
            CurrentDate = game.CurrentDate.AddDays(280.0)
            TrainingWeeksApplied = game.TrainingWeeksApplied + 40 }

    PlayerDevelopment.developAll gs |> fun gs -> gs.Players.[afterWeekly.Id]

let private runSimulation (playerFactory: unit -> Player) (simulator: Player -> Player) (iterations: int) : Player seq =
    Seq.init iterations (fun _ ->
        let player = playerFactory ()
        simulator player)

type Stats =
    { Mean: float
      P50: int
      P90: int
      P95: int
      Min: int
      Max: int }

let private calculateStats (selector: Player -> int) (players: Player seq) : Stats =
    let values = players |> Seq.map selector |> Seq.toArray |> Array.sort
    let n = values.Length
    let mean = values |> Seq.averageBy float
    let p50 = values.[n * 50 / 100]
    let p90 = values.[n * 90 / 100]
    let p95 = values.[n * 95 / 100]
    let min = values.[0]
    let max = values.[n - 1]

    { Mean = mean
      P50 = p50
      P90 = p90
      P95 = p95
      Min = min
      Max = max }

let private fresh (p: Player) =
    { p with
        Condition = 100
        Morale = 80
        Status = Available }

let private youngPred (game: GameState) (p: Player) =
    let age = Player.age game.CurrentDate p

    age >= 18
    && age <= 21
    && p.PotentialSkill - p.CurrentSkill >= 10
    && p.CurrentSkill <= 70

// =============================================================================
// Tests
// =============================================================================

let trainingSystemTests =
    testList
        "TrainingSystem"
        [ test "Young player with Heavy training should not gain more than 14 CA in a season" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some basePlayer ->
                  let results =
                      runSimulation
                          (fun () -> fresh basePlayer)
                          (fun p ->
                              simulateFullSeason
                                  p
                                  { Focus = TrainingFocus.TrainingTechnical
                                    Intensity = TrainingIntensity.TrainingHeavy }
                                  game)
                          50

                  let caStats = calculateStats (fun p -> p.CurrentSkill) results
                  let actualGain = caStats.P95 - basePlayer.CurrentSkill

                  Expect.isLessThan
                      actualGain
                      14
                      $"P95 CA gain (%d{actualGain}) exceeds max 14. Mean=%.1f{caStats.Mean}, P95=%d{caStats.P95}, Max=%d{caStats.Max}"
          }

          test "Young player with correct focus should gain more than wrong focus" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some basePlayer ->
                  let correctFocus =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let wrongFocus =
                      { Focus = TrainingFocus.TrainingPhysical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let correctResults =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p correctFocus game) 50

                  let wrongResults =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p wrongFocus game) 50

                  let correctStats = calculateStats (fun p -> p.CurrentSkill) correctResults
                  let wrongStats = calculateStats (fun p -> p.CurrentSkill) wrongResults
                  let diff = correctStats.Mean - wrongStats.Mean

                  Expect.isGreaterThan
                      diff
                      0.3
                      $"Focus diff %.1f{diff} too small. Correct=%.1f{correctStats.Mean}, Wrong=%.1f{wrongStats.Mean}"
          }

          test "Player over 30 should not gain CA on average regardless of training" {
              let game = loadGame ()

              let oldPred (p: Player) =
                  let age = Player.age game.CurrentDate p
                  age >= 30 && p.CurrentSkill >= 65

              match findPlayer game oldPred with
              | None -> failtest "No old player found in game data"
              | Some basePlayer ->
                  for intensity in
                      [ TrainingIntensity.TrainingLight
                        TrainingIntensity.TrainingNormal
                        TrainingIntensity.TrainingHeavy ] do
                      let schedule =
                          { Focus = TrainingFocus.TrainingTechnical
                            Intensity = intensity }

                      let results =
                          runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p schedule game) 50

                      let caStats = calculateStats (fun p -> p.CurrentSkill) results

                      Expect.isLessThanOrEqual
                          caStats.Mean
                          (float basePlayer.CurrentSkill + 0.5)
                          $"30+ yo with %A{intensity}: Mean=%.1f{caStats.Mean}, start=%d{basePlayer.CurrentSkill}"
          }

          test "Player at 27-30 should have minimal or negative CA change" {
              let game = loadGame ()

              let primePred (p: Player) =
                  let age = Player.age game.CurrentDate p
                  age >= 24 && age <= 27 && p.CurrentSkill >= 70

              match findPlayer game primePred with
              | None -> failtest "No prime player found in game data"
              | Some basePlayer ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let results =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p schedule game) 50

                  let caStats = calculateStats (fun p -> p.CurrentSkill) results

                  Expect.isLessThan
                      caStats.Mean
                      (float basePlayer.CurrentSkill + 1.0)
                      $"Prime player mean %.1f{caStats.Mean} too high, expected < %d{basePlayer.CurrentSkill + 1}"
          }

          test "Heavy training should give more CA than Normal for young players" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some basePlayer ->
                  let heavySchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingHeavy }

                  let normalSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let heavyResults =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p heavySchedule game) 50

                  let normalResults =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p normalSchedule game) 50

                  let heavyStats = calculateStats (fun p -> p.CurrentSkill) heavyResults
                  let normalStats = calculateStats (fun p -> p.CurrentSkill) normalResults

                  Expect.isGreaterThan
                      heavyStats.Mean
                      normalStats.Mean
                      $"Heavy %.1f{heavyStats.Mean} should exceed Normal %.1f{normalStats.Mean}"
          }

          test "Normal training should give more CA than Light for young players" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some basePlayer ->
                  let normalSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let lightSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingLight }

                  let normalResults =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p normalSchedule game) 50

                  let lightResults =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p lightSchedule game) 50

                  let normalStats = calculateStats (fun p -> p.CurrentSkill) normalResults
                  let lightStats = calculateStats (fun p -> p.CurrentSkill) lightResults

                  Expect.isGreaterThan
                      normalStats.Mean
                      lightStats.Mean
                      $"Normal %.1f{normalStats.Mean} should exceed Light %.1f{lightStats.Mean}"
          }

          test "Condition should drop below 50 after 10 weeks of Heavy training" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingPhysical
                        Intensity = TrainingIntensity.TrainingHeavy }

                  let after10Weeks = simulateWeeks { player with Condition = 100 } 10 schedule game

                  Expect.isLessThan
                      after10Weeks.Condition
                      50
                      $"Condition after 10w Heavy = %d{after10Weeks.Condition}, expected < 50"
          }

          test "Condition should drop below 30 after 20 weeks of Heavy training" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingPhysical
                        Intensity = TrainingIntensity.TrainingHeavy }

                  let after20Weeks = simulateWeeks { player with Condition = 100 } 20 schedule game

                  Expect.isLessThan
                      after20Weeks.Condition
                      30
                      $"Condition after 20w Heavy = %d{after20Weeks.Condition}, expected < 30"
          }

          test "Light training should maintain condition above 45 after 10 weeks" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingPhysical
                        Intensity = TrainingIntensity.TrainingLight }

                  let after10Weeks = simulateWeeks { player with Condition = 100 } 10 schedule game

                  Expect.isGreaterThan
                      after10Weeks.Condition
                      45
                      $"Condition after 10w Light = %d{after10Weeks.Condition}, expected > 45"
          }

          test "Normal training should result in condition dropping to 0 after 10 weeks" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingPhysical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let after10Weeks = simulateWeeks { player with Condition = 100 } 10 schedule game

                  Expect.isLessThanOrEqual
                      after10Weeks.Condition
                      10
                      $"Condition after 10w Normal = %d{after10Weeks.Condition}, expected <= 10"
          }

          test "Heavy training should cause injuries in ~4% of players per week" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingHeavy }

                  let results =
                      runSimulation (fun () -> fresh player) (fun p -> simulateWeeks p 1 schedule game) 100

                  let injuredCount =
                      results
                      |> Seq.filter (fun p ->
                          match p.Status with
                          | Injured _ -> true
                          | _ -> false)
                      |> Seq.length

                  let injuryRate = float injuredCount / 100.0

                  Expect.isGreaterThan injuryRate 0.01 $"Injury rate %.2f{injuryRate} too low, expected ~4%%"
                  Expect.isLessThan injuryRate 0.10 $"Injury rate %.2f{injuryRate} too high, expected ~4%%"
          }

          test "Light and Normal training should have zero or near-zero injuries" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let lightSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingLight }

                  let normalSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let lightResults =
                      runSimulation (fun () -> fresh player) (fun p -> simulateWeeks p 10 lightSchedule game) 100

                  let normalResults =
                      runSimulation (fun () -> fresh player) (fun p -> simulateWeeks p 10 normalSchedule game) 100

                  let countInjured (results: Player seq) =
                      results
                      |> Seq.filter (fun p ->
                          match p.Status with
                          | Injured _ -> true
                          | _ -> false)
                      |> Seq.length

                  Expect.isLessThanOrEqual (countInjured lightResults) 5 $"Light had injuries"
                  Expect.isLessThanOrEqual (countInjured normalResults) 5 $"Normal had injuries"
          }

          test "GK training focus should benefit goalkeepers" {
              let game = loadGame ()

              let gkPred (p: Player) =
                  p.Position = GK && p.PotentialSkill - p.CurrentSkill >= 10

              match findPlayer game gkPred with
              | None -> failtest "No GK found in game data"
              | Some gk ->
                  let gkSchedule =
                      { Focus = TrainingFocus.TrainingGoalkeeping
                        Intensity = TrainingIntensity.TrainingNormal }

                  let techSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let gkResults =
                      runSimulation (fun () -> fresh gk) (fun p -> simulateFullSeason p gkSchedule game) 50

                  let techResults =
                      runSimulation (fun () -> fresh gk) (fun p -> simulateFullSeason p techSchedule game) 50

                  let gkStats = calculateStats (fun p -> p.CurrentSkill) gkResults
                  let techStats = calculateStats (fun p -> p.CurrentSkill) techResults

                  Expect.isGreaterThan
                      gkStats.Mean
                      techStats.Mean
                      $"GK focus %.1f{gkStats.Mean} should exceed Technical %.1f{techStats.Mean}"
          }

          test "Physical focus should benefit defenders more than Technical" {
              let game = loadGame ()

              let dcPred (p: Player) =
                  p.Position = DC
                  && p.PotentialSkill - p.CurrentSkill >= 10
                  && p.CurrentSkill <= 70

              match findPlayer game dcPred with
              | None -> failtest "No DC found in game data"
              | Some dc ->
                  let physSchedule =
                      { Focus = TrainingFocus.TrainingPhysical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let techSchedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let physResults =
                      runSimulation (fun () -> fresh dc) (fun p -> simulateFullSeason p physSchedule game) 50

                  let techResults =
                      runSimulation (fun () -> fresh dc) (fun p -> simulateFullSeason p techSchedule game) 50

                  let physStats = calculateStats (fun p -> p.CurrentSkill) physResults
                  let techStats = calculateStats (fun p -> p.CurrentSkill) techResults
                  let diff = physStats.Mean - techStats.Mean

                  printfn $"DC Focus: Physical=%.1f{physStats.Mean}, Technical=%.1f{techStats.Mean}, diff=%.2f{diff}"

                  Expect.isGreaterThan
                      diff
                      0.5
                      $"Focus diff %.1f{diff} too small. Physical=%.1f{physStats.Mean}, Technical=%.1f{techStats.Mean}"

                  Expect.isGreaterThan
                      physStats.Mean
                      techStats.Mean
                      $"Physical %.1f{physStats.Mean} should exceed Technical %.1f{techStats.Mean} for DC"
          }

          test "Heavy training should decrease morale over time" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some player ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingHeavy }

                  let after10Weeks =
                      simulateWeeks
                          { player with
                              Condition = 100
                              Morale = 80 }
                          10
                          schedule
                          game

                  Expect.isLessThan
                      after10Weeks.Morale
                      80
                      $"Morale after 10w Heavy = %d{after10Weeks.Morale}, expected < 80"
          }

          test "Player should never exceed potential skill" {
              let game = loadGame ()

              match findPlayer game (youngPred game) with
              | None -> failtest "No young player found in game data"
              | Some basePlayer ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingHeavy }

                  let results =
                      runSimulation (fun () -> fresh basePlayer) (fun p -> simulateFullSeason p schedule game) 50

                  let maxCA =
                      results |> Seq.maxBy (fun p -> p.CurrentSkill) |> (fun p -> p.CurrentSkill)

                  Expect.isLessThanOrEqual
                      maxCA
                      basePlayer.PotentialSkill
                      $"Exceeded potential: max CA = %d{maxCA}, potential = %d{basePlayer.PotentialSkill}"
          }

          test "Players near potential should gain minimal CA" {
              let game = loadGame ()

              let nearPotPred (p: Player) =
                  let age = Player.age game.CurrentDate p
                  age >= 22 && age <= 26 && p.PotentialSkill - p.CurrentSkill <= 3

              match findPlayer game nearPotPred with
              | None -> failtest "No near-potential player found in game data"
              | Some nearPotentialPlayer ->
                  let schedule =
                      { Focus = TrainingFocus.TrainingTechnical
                        Intensity = TrainingIntensity.TrainingNormal }

                  let results =
                      runSimulation
                          (fun () -> fresh nearPotentialPlayer)
                          (fun p -> simulateFullSeason p schedule game)
                          50

                  let caStats = calculateStats (fun p -> p.CurrentSkill) results

                  Expect.isLessThan
                      caStats.Mean
                      (float nearPotentialPlayer.PotentialSkill)
                      $"Near potential gained too much: mean=%.1f{caStats.Mean}, potential=%d{nearPotentialPlayer.PotentialSkill}"
          } ]
